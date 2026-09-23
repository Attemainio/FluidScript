using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

/// <content>
/// Binding steps 6 through 11, plus the schedule step <c>15</c>'s order never had: materialize
/// indexed ports, bind connections, apply the inference rules, bind attachments, control bindings and
/// disturbances, validate, and assign tags last.
/// </content>
/// <remarks>
/// This half has no notion of expressions, exactly as steps 0–5 have none of topology. The two
/// exceptions are deliberate and narrow: a <c>setpoint=</c> and a schedule's times and values are
/// quantities, and evaluating them here is cheaper than a third pass existing only for them.
/// </remarks>
internal sealed partial class BindingRun
{
    private static readonly ImmutableArray<string> ControlArguments =
        ["actuate", "measure", "by", "setpoint"];

    private readonly List<ConnectionSymbol> _connections = [];
    private readonly List<ControlBindingSymbol> _controlBindings = [];
    private readonly List<DisturbanceSymbol> _disturbances = [];
    private readonly Dictionary<string, Dictionary<string, TextSpan>> _claimed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _degrees = new(StringComparer.Ordinal);
    private readonly List<ConnectionSymbol> _sourceConnections = [];

    private ISymbolMap _symbolMap = SymbolMap.Empty;

    private void BindTopology(List<CircuitBlock> blocks)
    {
        MaterializePorts();
        BindConnections(blocks);

        // Before inference, because an `inlet` or `outlet` line IS a connection the user wrote
        // another syntax (23). Left until after I3, it would find every port it wants to claim already
        // terminated by a dead-leg node, and the subcircuit would hang off the parent by nothing.
        BindAttachments(blocks);

        // Snapshotted before inference, because FS1507 and FS1511 are statements about the topology
        // the user wrote. After I3 every component is connected to something, and the two codes could
        // never fire again.
        _sourceConnections.AddRange(_connections);

        ApplyInference();

        // After inference, so the node I2 puts between two components at different heights is the
        // one FS2219 describes; before observers, which sit on nodes and carry no height (D-70).
        AssignHeights();

        // After inference for the same reason: the node a port pressure is written onto may be the
        // one I2 put there.
        PropagatePortPressures();
        BindObservers();
        BindControlBindings(blocks);
        BindSchedule(blocks);
        Validate();
        ResolveOwnership();
        AssignTags();

        _symbolMap = BuildSymbolMap(blocks);
    }

    // ---- step 6: materialize indexed ports --------------------------------------------------------

    private void MaterializePorts()
    {
        var evidenced = EvidencedPorts();

        for (var i = 0; i < _components.Count; i++)
        {
            var component = _components[i];

            if (component.Kind is not { } kind)
            {
                continue;
            }

            var ports = new List<string>(kind.Ports.Select(static port => port.Key));

            if (evidenced.TryGetValue(component.Name, out var named))
            {
                foreach (var port in named)
                {
                    if (!ports.Contains(port, StringComparer.Ordinal) && Fit(kind, port) == PortFit.Member)
                    {
                        ports.Add(port);
                    }
                }
            }

            _components[i] = component with { Ports = [.. Order(kind, ports)] };
        }
    }

    /// <summary>Collects the ports the source actually named, per component.</summary>
    /// <remarks>
    /// Two things evidence a port: a qualified endpoint naming it, and a level parameter that
    /// belongs to it. Nothing else creates one — a tank has sixteen possible inlets and exactly as
    /// many as the script used.
    /// </remarks>
    private Dictionary<string, SortedSet<string>> EvidencedPorts()
    {
        var evidenced = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        void Evidence(string component, string port)
        {
            if (!evidenced.TryGetValue(component, out var ports))
            {
                evidenced[component] = ports = new SortedSet<string>(StringComparer.Ordinal);
            }

            ports.Add(port);
        }

        foreach (var connection in parse.Root.Statements.OfType<ConnectionSyntax>())
        {
            foreach (var endpoint in connection.Endpoints)
            {
                if (endpoint.Port is not { } port)
                {
                    continue;
                }

                // Evidence is in keys: `T1.in[3]` and the old `T1.in3` both materialize `in3`. An
                // unresolvable spelling is kept as written and falls out at `Fit`.
                var name = endpoint.Component.Token.Text;
                var key = _componentsByName.TryGetValue(name, out var slot) && _components[slot.Index].Kind is { } kind
                    ? kind.ResolvePort(port.Text, out _, out _) ?? port.Text
                    : port.Text;

                Evidence(name, key);
            }
        }

        foreach (var component in _components)
        {
            foreach (var family in component.Kind?.PortFamilies ?? [])
            {
                if (family.LevelParameterSuffix is not { } suffix)
                {
                    continue;
                }

                foreach (var parameter in component.Parameters.Keys)
                {
                    if (parameter.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        Evidence(component.Name, parameter[..^suffix.Length]);
                    }
                }
            }
        }

        return evidenced;
    }

    /// <summary>Orders ports as the kind declares them, then by family index.</summary>
    /// <remarks>
    /// So a diagnostic lists <c>in1, in2, out1</c> and an unqualified endpoint walks them in that
    /// order — never the order the file happened to mention them in, which would make the meaning of
    /// an unqualified endpoint depend on where an unrelated line sits.
    /// </remarks>
    private static IEnumerable<string> Order(ComponentKindInfo kind, List<string> ports)
    {
        var declared = kind.Ports.Select(static port => port.Key).ToList();

        return ports
            .OrderBy(port => declared.IndexOf(port) is var at && at >= 0 ? at : int.MaxValue)
            .ThenBy(port => FamilyIndex(kind, port))
            .ThenBy(static port => port, StringComparer.Ordinal);
    }

    private static int FamilyIndex(ComponentKindInfo kind, string port)
    {
        foreach (var family in kind.PortFamilies)
        {
            if (Indexed.Matches($"{family.Prefix}{{index}}", port, out var index))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>Says whether a port name belongs to a family of the kind, and whether it is in range.</summary>
    private static PortFit Fit(ComponentKindInfo kind, string port)
    {
        foreach (var family in kind.PortFamilies)
        {
            if (!Indexed.Matches($"{family.Prefix}{{index}}", port, out var index))
            {
                continue;
            }

            return index >= family.MinIndex && index <= family.MaxIndex
                ? PortFit.Member
                : PortFit.OutsideRange;
        }

        return PortFit.NoSuchPort;
    }

    // ---- step 7: connections, and rule I1 along the way -------------------------------------------

    private void BindConnections(List<CircuitBlock> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var connection in block.Statements.OfType<ConnectionSyntax>())
            {
                var endpoints = connection.Endpoints;

                // `A - B - C` is one line and two connections (rule I6). It stays one line in the
                // file, which is why both connections carry the whole statement's span.
                for (var i = 0; i + 1 < endpoints.Length; i++)
                {
                    var from = Endpoint(endpoints[i], block.Circuit!.Name, connection.Span, outgoing: true);
                    var to = Endpoint(endpoints[i + 1], block.Circuit!.Name, connection.Span, outgoing: false);

                    if (from is null || to is null)
                    {
                        continue;
                    }

                    // I7 (D-110): a connection carrying pipe properties runs through the pipe CollectDeclarations
                    // declared for it, keyed by the line and the pair.
                    var key = $"{connection.Span.Start.ToString(CultureInfo.InvariantCulture)}:{i.ToString(CultureInfo.InvariantCulture)}";

                    if (_components.FirstOrDefault(c => c.Origin is Origin.Inferred { Rule: "I7" } inferred && inferred.StableKey == key) is { } implicitPipe)
                    {
                        var pipe = implicitPipe.Name;
                        _connections.Add(new ConnectionSymbol(from.Value, new EndpointSymbol(pipe, "in"), connection.Span));
                        _connections.Add(new ConnectionSymbol(new EndpointSymbol(pipe, "out"), to.Value, connection.Span));
                        Claim(pipe, "in", connection.Span, stated: false);
                        Claim(pipe, "out", connection.Span, stated: false);
                        Count(from.Value.Component);
                        Count(pipe);
                        Count(pipe);
                        Count(to.Value.Component);
                        continue;
                    }

                    _connections.Add(new ConnectionSymbol(from.Value, to.Value, connection.Span));
                    Count(from.Value.Component);
                    Count(to.Value.Component);
                }
            }
        }
    }

    private EndpointSymbol? Endpoint(EndpointSyntax endpoint, string circuit, TextSpan span, bool outgoing)
    {
        var name = endpoint.Component.Token.Text;

        // I1 — an endpoint naming nothing declared becomes a node, keeping the user's identifier so
        // `N1` in the script is `N1` in the model and in hover.
        if (!_componentsByName.TryGetValue(name, out var slot))
        {
            if (_bindingsByName.ContainsKey(name))
            {
                // The one case I1 cannot absorb: the name is taken. A model holding a value and a
                // component under one identifier has no way to answer what `N1.t` meant.
                Report(BinderDiagnostics.ValueUsedAsComponent, endpoint.Span, ("name", name));
                return null;
            }

            slot = Infer(name, "I1", circuit, endpoint.Span);
        }

        var component = _components[slot.Index];

        // No kind resolved: the declaration already carries an error, and inventing a port here would
        // add a second one saying the same thing.
        if (component.Kind is not { } kind)
        {
            return new EndpointSymbol(component.Name, endpoint.Port?.Text ?? string.Empty);
        }

        return endpoint.Port is { } written
            ? Qualified(component, kind, written, span)
            : Unqualified(component, kind, span, outgoing);
    }

    private EndpointSymbol? Qualified(
        ComponentSymbol component, ComponentKindInfo kind, QualifiedNameSyntax written, TextSpan span)
    {
        var port = written.Text;

        if (kind.HasUnlimitedPorts)
        {
            return new EndpointSymbol(component.Name, port, PortStated: true);
        }

        // The written spelling to the model's key (`D-120`): `in[2]` and the old `in2` are the port
        // `in2`, and the old form says so once, here, where the endpoint is.
        var key = kind.ResolvePort(port, out var suggestion, out var outside);

        if (key is not null && suggestion is not null)
        {
            ReportLegacySpelling(written.Span, port, suggestion);
        }

        if (key is not null && component.Ports.Contains(key, StringComparer.Ordinal))
        {
            return Claim(component.Name, key, span, stated: true);
        }

        if (outside is { } family)
        {
            // The family proper starts at 2, its first member being the fixed row `in`; to the user
            // the range is 1…16.
            Report(
                BinderDiagnostics.IndexOutsideFamily,
                written.Span,
                ("written", port),
                ("kind", kind.Keyword),
                ("min", Math.Min(1, family.MinIndex).ToString(CultureInfo.InvariantCulture)),
                ("max", family.MaxIndex.ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            Report(
                BinderDiagnostics.UnknownPort,
                written.Span,
                ("kind", kind.Keyword),
                ("port", port),
                ("available", string.Join(", ", component.Ports.Select(kind.PortName))));
        }

        return null;
    }

    private EndpointSymbol? Unqualified(
        ComponentSymbol component, ComponentKindInfo kind, TextSpan span, bool outgoing)
    {
        // A node has unlimited unnamed ports, so it never runs out and never claims a name.
        if (kind.HasUnlimitedPorts)
        {
            return new EndpointSymbol(component.Name, string.Empty);
        }

        // Outlets first on the left of the dash and inlets first on the right, so `T1 - N2` means what
        // it looks like. Direction is read from the port's *name* before its role, because a tank's
        // `in1` and `out1` are both bidirectional and the role cannot tell them apart.
        var prefix = outgoing ? "out" : "in";
        var role = outgoing ? PortRole.Outlet : PortRole.Inlet;

        var ordered = component.Ports
            .OrderBy(port => port.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(port => RoleOf(kind, port) == role ? 0 : 1)
            .ToArray();

        var claimed = _claimed.TryGetValue(component.Name, out var used) ? used : [];

        // Every port taken: the first in preference order is the one the user meant, and naming it
        // sends them to the line that already has it.
        var free = ordered.FirstOrDefault(port => !claimed.ContainsKey(port))
            ?? ordered.FirstOrDefault();

        // Not stated: the letters this hands out are connection order, not the user's word for the leg.
        return free is null ? null : Claim(component.Name, free, span, stated: false);
    }

    private static PortRole RoleOf(ComponentKindInfo kind, string port)
    {
        foreach (var declared in kind.Ports)
        {
            if (string.Equals(declared.Key, port, StringComparison.Ordinal))
            {
                return declared.Role;
            }
        }

        foreach (var family in kind.PortFamilies)
        {
            if (Indexed.Matches($"{family.Prefix}{{index}}", port, out _))
            {
                return family.Role;
            }
        }

        return PortRole.Bidirectional;
    }

    /// <summary>Takes a port for a connection, or reports that something already has it.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="port">The port being claimed.</param>
    /// <param name="span">The line the connection was written on.</param>
    /// <param name="stated">
    /// Whether the script wrote <paramref name="port"/>. False when this rule chose it for an
    /// unqualified endpoint, which is what <see cref="EndpointSymbol.PortStated"/> exists to say.
    /// </param>
    /// <returns>The endpoint, or <see langword="null"/> when the port was already claimed.</returns>
    private EndpointSymbol? Claim(string component, string port, TextSpan span, bool stated)
    {
        if (!_claimed.TryGetValue(component, out var claimed))
        {
            _claimed[component] = claimed = new Dictionary<string, TextSpan>(StringComparer.Ordinal);
        }

        if (claimed.TryGetValue(port, out var first))
        {
            Report(
                BinderDiagnostics.PortAlreadyConnected,
                span,
                ("port", port),
                ("name", component),
                ("line", LineOf(first)));
            return null;
        }

        claimed[port] = span;

        return new EndpointSymbol(component, port, stated);
    }

    private ComponentSlot Infer(string name, string rule, string circuit, TextSpan span)
    {
        var node = new ComponentSymbol
        {
            Name = name,
            Origin = new Origin.Inferred(rule, name),
            Kind = registry.Resolve("node") is KindResolution.Exact exact ? exact.Kind : null,
            WrittenKind = "node",
            Parameters = ImmutableDictionary.Create<string, ParameterValue>(StringComparer.Ordinal),
            DeclarationSpan = null,
            CircuitName = circuit,
            Ports = [],
        };

        var slot = Register(node, null);

        // Info, and off by default in the log: on a large script these would drown everything else.
        // They must exist all the same, or the inference is invisible magic.
        Report(BinderDiagnostics.ComponentInferred, span, ("kind", "node"), ("name", name), ("rule", rule));

        return slot;
    }

    private void Count(string component) =>
        _degrees[component] = _degrees.GetValueOrDefault(component) + 1;

    /// <summary>Places every instrument that stated an <c>at</c> clause (<c>D-61</c>).</summary>
    /// <remarks>
    /// After inference rather than with the declaration, because the node an instrument observes is
    /// very often one rule I1 created: <c>N2</c> exists because a connection named it, not because
    /// anybody declared it. Whether the clause belongs on the kind at all was settled at declaration
    /// time; all that is left here is whether the node exists.
    /// </remarks>
    /// <summary>Copies every stated port pressure onto the node the port touches (<c>D-124</c>).</summary>
    /// <remarks>
    /// <para>
    /// <c>V1 valve out.p=100</c> is <c>N1 node p=100</c> on the node <c>V1.out</c> is wired to. The
    /// node is the only thing that has a pressure -- one per node is what makes the balances rows --
    /// so the statement is moved there and the counting, the datum pick and <c>FS2210</c> see a
    /// stated node pressure like any other. The copied value keeps the component's span, so a
    /// diagnostic about the node's pressure, and a write-back to it, land on the line that stated it.
    /// </para>
    /// <para>
    /// A pressure the node already carries -- its own <c>p=</c>, or another component's port on the
    /// same node -- is <c>FS1539</c> on the later statement, agreeing or not. A port wired to nothing
    /// has no node to state, and the dangling port is already <c>FS1507</c>'s.
    /// </para>
    /// </remarks>
    private void PropagatePortPressures()
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < _components.Count; i++)
        {
            index[_components[i].Name] = i;
        }

        // Declaration order, so a second statement of one node is the one reported.
        for (var i = 0; i < _components.Count; i++)
        {
            var component = _components[i];

            if (component.Kind is not { } kind || kind.HasUnlimitedPorts || kind.IsObserver)
            {
                continue;
            }

            foreach (var (key, value) in component.Parameters.OrderBy(static pair => pair.Value.Span.Start))
            {
                if (!ComponentRegistry.IsPortPressure(key, out var port) || value.Value is null)
                {
                    continue;
                }

                var peer = Peers(component.Name, port).FirstOrDefault(IsNode);

                if (peer is null || !index.TryGetValue(peer, out var slot))
                {
                    continue;
                }

                var node = _components[slot];
                var written = $"{component.Name} {value.WrittenName}";

                if (node.Parameters.TryGetValue("p", out var existing))
                {
                    Report(
                        BinderDiagnostics.PortPressureStatedTwice,
                        value.Span,
                        ("written", written),
                        ("node", node.Name),
                        ("other", existing.WrittenName.Contains(' ', StringComparison.Ordinal) ? existing.WrittenName : $"{node.Name} {existing.WrittenName}"));
                    continue;
                }

                _components[slot] = node with
                {
                    Parameters = node.Parameters.Add("p", value with { WrittenName = written }),
                };
            }
        }
    }

    /// <summary>The parameters that describe how heat crosses an exchanger, and so promote nothing (<c>D-19</c>).</summary>
    private static readonly ImmutableArray<string> RatingParameters =
        ["ua", "area", "u", "approach", "arrangement", "plates", "lamella", "plate_area", "fouling"];

    /// <summary>How a written port name relates to a kind's declared port families.</summary>
    private enum PortFit
    {
        /// <summary>No family of this kind owns that prefix.</summary>
        NoSuchPort = 0,

        /// <summary>A family owns the prefix, and the index is inside its range.</summary>
        Member,

        /// <summary>A family owns the prefix, and the index is not.</summary>
        OutsideRange,
    }
}
