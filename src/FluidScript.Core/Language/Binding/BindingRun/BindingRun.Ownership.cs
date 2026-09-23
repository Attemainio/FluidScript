using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    // ---- step 10b: which circuit owns a two-sided component (D-36) -----------------------------------

    /// <summary>Moves a two-sided component to the circuit on its enthalpy-losing side (<c>D-36</c>).</summary>
    /// <remarks>
    /// <para>
    /// <strong>Ownership is a tagging and grouping question, never a solver one.</strong> The circuit a
    /// component is declared in is a text-editing accident: moving <c>HX1</c>'s line from the heating
    /// block to the district block would renumber equipment on a drawing, which is what <c>D-34</c> is
    /// built to avoid. The side losing nominal enthalpy is the rule a designer's "the leftmost circuit
    /// owns it" states in Core's own terms (<c>D-31</c>), and it is computable without a canvas.
    /// </para>
    /// <para>
    /// The direction is read from the duty's sign -- a role word carries it (<c>D-91</c>) -- or, with no
    /// stated duty, from whichever side's stated terminals drop. Both sides in one circuit is that
    /// circuit; a side wired to nothing is a boundary profile, not a circuit, and the declaration
    /// stands; two circuits with no readable direction fall back to the lower circuit number with
    /// <c>FS2216</c>. Before tags, because the tag's circuit number and ordinal are read from
    /// <see cref="ComponentSymbol.CircuitName"/>.
    /// </para>
    /// </remarks>
    private void ResolveOwnership()
    {
        for (var i = 0; i < _components.Count; i++)
        {
            var component = _components[i];

            if (component.Origin is not Origin.Declared || component.Kind is not { } kind || !IsTwoSided(kind))
            {
                continue;
            }

            var one = SideCircuit(component.Name, "in", "out");
            var two = SideCircuit(component.Name, "in2", "out2");

            if (one is null || two is null)
            {
                continue;
            }

            var owner = string.Equals(one, two, StringComparison.Ordinal)
                ? one
                : LosingSide(component) switch
                {
                    1 => one,
                    2 => two,
                    _ => null,
                };

            if (owner is null)
            {
                owner = CircuitNumber(one) <= CircuitNumber(two) ? one : two;

                Report(
                    TopologyDiagnostics.AmbiguousOwnership,
                    component.DeclarationSpan ?? SpanOfFirstMention(component.Name),
                    ("component", component.Name),
                    ("a", one),
                    ("b", two),
                    ("chosen", owner));
            }

            if (!string.Equals(owner, component.CircuitName, StringComparison.Ordinal))
            {
                _components[i] = component with { CircuitName = owner };
            }
        }
    }

    /// <summary>Whether a kind has a second flow group: <c>in2</c> and <c>out2</c> beside <c>in</c> and <c>out</c>.</summary>
    private static bool IsTwoSided(ComponentKindInfo kind) =>
        kind.Ports.Any(static port => string.Equals(port.Key, "in2", StringComparison.Ordinal))
        && kind.Ports.Any(static port => string.Equals(port.Key, "out2", StringComparison.Ordinal));

    /// <summary>The circuit one side of a component is wired into, read from the first declared component beyond its ports.</summary>
    /// <returns>The circuit name, or <see langword="null"/> when neither port reaches a declared component.</returns>
    /// <remarks>
    /// Inferred nodes are walked through rather than read: an I2 node between <c>PP</c> and <c>HX1.in2</c>
    /// inherits a circuit from whichever end named it, which is exactly the accident this pass exists
    /// to ignore. A dead-end I3 node reaches nothing, and the side reads as a boundary.
    /// </remarks>
    private string? SideCircuit(string component, string inlet, string outlet)
    {
        foreach (var port in (string[])[inlet, outlet])
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { component };
            var frontier = new Queue<string>();

            foreach (var peer in Peers(component, port))
            {
                frontier.Enqueue(peer);
            }

            while (frontier.TryDequeue(out var name))
            {
                if (!visited.Add(name) || !_componentsByName.TryGetValue(name, out var slot))
                {
                    continue;
                }

                var reached = _components[slot.Index];

                if (reached.Origin is Origin.Declared)
                {
                    return reached.CircuitName;
                }

                foreach (var next in Peers(name, port: null))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        return null;
    }

    /// <summary>The components on the far end of a component's connections, at one port or at all of them.</summary>
    private IEnumerable<string> Peers(string component, string? port)
    {
        foreach (var connection in _connections)
        {
            if (string.Equals(connection.From.Component, component, StringComparison.Ordinal)
                && (port is null || string.Equals(connection.From.Port, port, StringComparison.Ordinal)))
            {
                yield return connection.To.Component;
            }
            else if (string.Equals(connection.To.Component, component, StringComparison.Ordinal)
                && (port is null || string.Equals(connection.To.Port, port, StringComparison.Ordinal)))
            {
                yield return connection.From.Component;
            }
        }
    }

    /// <summary>Which side of a two-sided component loses nominal enthalpy: 1, 2, or 0 when nothing stated says.</summary>
    /// <remarks>
    /// A positive duty enters side 1, so side 2 loses; a role word fixes the sign the way lowering
    /// applies it (<c>D-91</c>). With no duty, a side whose stated terminals drop is the losing one;
    /// two sides that both drop, or both rise, contradict each other and decide nothing.
    /// </remarks>
    private static int LosingSide(ComponentSymbol component)
    {
        if (Stated(component, "power") is { } power && power != 0)
        {
            var signed = NameResolution.Normalize(component.WrittenKind) switch
            {
                "load" or "cooler" or "radiator" or "chiller" => -Math.Abs(power),
                "heater" or "boiler" => Math.Abs(power),
                _ => power,
            };

            return signed > 0 ? 2 : 1;
        }

        var one = Stated(component, "in") is { } a && Stated(component, "out") is { } b ? Math.Sign(b - a) : 0;
        var two = Stated(component, "in2") is { } c && Stated(component, "out2") is { } d ? Math.Sign(d - c) : 0;

        return (one, two) switch
        {
            (< 0, < 0) or (> 0, > 0) => 0,
            (< 0, _) => 1,
            (> 0, _) => 2,
            (0, < 0) => 2,
            (0, > 0) => 1,
            _ => 0,
        };
    }

    private static double? Stated(ComponentSymbol component, string parameter) =>
        component.Parameters.TryGetValue(parameter, out var value) && value.Value is { } quantity
            ? quantity.SiValue
            : null;

    private int CircuitNumber(string circuit) =>
        _circuits.FirstOrDefault(candidate => string.Equals(candidate.Name, circuit, StringComparison.Ordinal))?.Number
        ?? int.MaxValue;

    // ---- step 11: tags, last and for a reason ------------------------------------------------------

    private void AssignTags()
    {
        // Last, because an ordinal depends on the complete declaration set of its circuit, and because
        // nothing in binding may read a tag — a stage that did would make identity circular (`D-34`).
        var ordinals = new Dictionary<(int Circuit, string Code), int>();

        for (var i = 0; i < _components.Count; i++)
        {
            var component = _components[i];

            // Inferred components are never tagged: they have no declaration to order by, their count
            // changes with unrelated edits, and tagging scaffolding the user did not write would put
            // `HE1__3WV` on an equipment schedule.
            if (component.Origin is not Origin.Declared || component.Kind?.TagCode is not { } code)
            {
                continue;
            }

            var number = _circuits
                .FirstOrDefault(circuit =>
                    string.Equals(circuit.Name, component.CircuitName, StringComparison.Ordinal))
                ?.Number ?? _circuits[0].Number;

            var ordinal = ordinals.GetValueOrDefault((number, code)) + 1;
            ordinals[(number, code)] = ordinal;

            // Two digits from 01, widening past 99 rather than wrapping.
            _components[i] = component with
            {
                Tag = string.Create(CultureInfo.InvariantCulture, $"{number}{code}{ordinal:00}"),
            };
        }
    }

    // ---- the map from a position back to what it names --------------------------------------------

    private SymbolMap BuildSymbolMap(List<CircuitBlock> blocks)
    {
        var builder = new SymbolMap.Builder();

        foreach (var circuit in _circuits)
        {
            builder.Add(new SymbolReference.Circuit(circuit), circuit.DeclarationSpan);
        }

        foreach (var binding in _bindings)
        {
            builder.Add(new SymbolReference.Binding(binding), binding.DeclarationSpan);
        }

        // An implicit pipe (I7) carries its connection line as its span (C-97) but is not mapped at it:
        // the line is the connection's, whose card already shows the pipe behind it (54), and mapping
        // both would make the narrowest-span lookup pick by insertion order.
        foreach (var component in _components)
        {
            if (component is { Origin: Origin.Declared, DeclarationSpan: { } span })
            {
                builder.Add(new SymbolReference.Component(component), span);
            }
        }

        foreach (var connection in _connections)
        {
            builder.Add(new SymbolReference.Connection(connection), connection.SourceSpan);
        }

        // Every place a component's name is written, so go-to-definition works from a use and not only
        // from a declaration. Expression bodies are not walked: `14` owns references inside an
        // expression, and the evaluator records them as dependencies rather than as spans.
        foreach (var block in blocks)
        {
            foreach (var statement in block.Statements)
            {
                foreach (var endpoint in Endpoints(statement))
                {
                    var name = endpoint.Component.Token.Text;

                    if (_componentsByName.TryGetValue(name, out var slot))
                    {
                        builder.Add(
                            new SymbolReference.Component(_components[slot.Index]),
                            endpoint.Component.Span);
                    }
                }
            }
        }

        return builder.Build();
    }

    private static ImmutableArray<EndpointSyntax> Endpoints(StatementSyntax statement) => statement switch
    {
        ConnectionSyntax connection => connection.Endpoints,
        AttachmentSyntax attachment => [attachment.Endpoint],
        DisturbanceSyntax disturbance => [disturbance.Target],
        _ => [],
    };
}
