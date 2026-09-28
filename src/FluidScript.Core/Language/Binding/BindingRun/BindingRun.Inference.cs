using System.Globalization;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
    // ---- step 8: I2 and I3 -------------------------------------------------------------------------

    private void ApplyInference()
    {
        InsertIntermediateNodes();
        TerminateOpenPorts();
        SeparateJunctionPorts();
    }

    private void SeparateJunctionPorts()
    {
        // I9 (D-183) — a junction holds one state, the mix of everything arriving. A component port
        // wired straight to one would have no state of its own: its stated `out.t` would land on the
        // mix (C-123), and its hold-up would be mixed into the other streams (D-146). So every
        // component port on a junction gets its own point, joined to the junction by a zero-length
        // link, and the junction is left to mix. Inlets as well as outlets, because a flow may reverse
        // in the solve and an inlet then discharges into the junction.
        bool IsJunction(string component) => IsNode(component) && _degrees.GetValueOrDefault(component) >= 3;

        // A pipe is left on the junction: nothing is stated on its ends and it holds no volume there, and its
        // outlet stream is reported on its own port already (C-103), so a point would cost the wire and the
        // layout and settle nothing.
        bool OwnsAPoint(string component) =>
            !IsNode(component)
            && !(_componentsByName.TryGetValue(component, out var at) && _components[at.Index].Kind?.Keyword == "pipe");

        var rewritten = new List<ConnectionSymbol>(_connections.Count);

        foreach (var connection in _connections)
        {
            var (port, junction, portIsFrom) =
                IsJunction(connection.To.Component) && OwnsAPoint(connection.From.Component) ? (connection.From, connection.To, true)
                : IsJunction(connection.From.Component) && OwnsAPoint(connection.To.Component) ? (connection.To, connection.From, false)
                : (default(EndpointSymbol), default(EndpointSymbol), false);

            if (port.Component is null)
            {
                rewritten.Add(connection);
                continue;
            }

            var stem = $"{port.Component}__{port.Port}";
            var name = stem;

            for (var ordinal = 2; _componentsByName.ContainsKey(name); ordinal++)
            {
                name = $"{stem}_{ordinal.ToString(CultureInfo.InvariantCulture)}";
            }

            var slot = Infer(name, "I9", connection.Circuit ?? CircuitOf(port.Component), connection.SourceSpan);
            var point = new EndpointSymbol(_components[slot.Index].Name, string.Empty);

            if (portIsFrom)
            {
                rewritten.Add(connection with { To = point });
                rewritten.Add(new ConnectionSymbol(point, junction, connection.SourceSpan) { Circuit = connection.Circuit });
            }
            else
            {
                rewritten.Add(new ConnectionSymbol(junction, point, connection.SourceSpan) { Circuit = connection.Circuit });
                rewritten.Add(connection with { From = point });
            }

            Count(point.Component);
            Count(point.Component);
        }

        _connections.Clear();
        _connections.AddRange(rewritten);
    }

    private void InsertIntermediateNodes()
    {
        // I2 — two non-node components joined directly have no state between them to write an equation
        // about, so a node goes in the middle and the connection becomes two.
        var rewritten = new List<ConnectionSymbol>(_connections.Count);

        foreach (var connection in _connections)
        {
            if (IsNode(connection.From.Component) || IsNode(connection.To.Component))
            {
                rewritten.Add(connection);
                continue;
            }

            // A node beside an implicit pipe (I7) is named after the pipe's port it joins, as I3 names a
            // boundary: `N1__HE1__out`, not `N1__HE1__HE1`.
            bool IsImplicitPipe(string component) =>
                _componentsByName.TryGetValue(component, out var slot) && _components[slot.Index].Origin is Origin.Inferred { Rule: "I7" };

            var stem = IsImplicitPipe(connection.From.Component) ? $"{connection.From.Component}__{connection.From.Port}"
                : IsImplicitPipe(connection.To.Component) ? $"{connection.To.Component}__{connection.To.Port}"
                : $"{connection.From.Component}__{connection.To.Component}";
            var name = stem;

            // The same pair connected twice appends an ordinal rather than colliding.
            for (var ordinal = 2; _componentsByName.ContainsKey(name); ordinal++)
            {
                name = $"{stem}_{ordinal.ToString(CultureInfo.InvariantCulture)}";
            }

            var slot = Infer(name, "I2", connection.Circuit ?? CircuitOf(connection.From.Component), connection.SourceSpan);
            var middle = new EndpointSymbol(_components[slot.Index].Name, string.Empty);

            rewritten.Add(connection with { To = middle });
            rewritten.Add(new ConnectionSymbol(middle, connection.To, connection.SourceSpan) { Circuit = connection.Circuit });

            Count(middle.Component);
            Count(middle.Component);
        }

        _connections.Clear();
        _connections.AddRange(rewritten);
    }

    private void TerminateOpenPorts()
    {
        // I3 — every non-optional port nothing connected gets a boundary node carrying zero flow. That
        // is the conservative termination: it changes no other result and it keeps the graph solvable,
        // so the user sees a diagram with a visibly dangling stub rather than an error message. It is
        // still worth saying, because it is almost always an unfinished script — which is FS2202, and
        // why a three-way valve's optional bypass is exempt rather than warned about.
        //
        // So is a component nothing connected at all: FS1507 already names that, and one mistake gets
        // one message (16, rule 4). FS2202 is for the *partly* wired component, where the script means
        // to reach this port and has not yet.
        foreach (var component in _components.ToArray())
        {
            if (component.Kind is not { } kind || kind.HasUnlimitedPorts)
            {
                continue;
            }

            var span = component.DeclarationSpan ?? default;
            var wired = _claimed.TryGetValue(component.Name, out var already) && already.Count > 0;

            foreach (var port in kind.Ports)
            {
                var claimed = _claimed.TryGetValue(component.Name, out var used) ? used : [];

                if (port.IsOptional || claimed.ContainsKey(port.Key))
                {
                    continue;
                }

                var slot = Infer($"{component.Name}__{port.Key}", "I3", component.CircuitName, span);
                var boundary = new EndpointSymbol(_components[slot.Index].Name, string.Empty);

                _connections.Add(new ConnectionSymbol(
                    new EndpointSymbol(component.Name, port.Key), boundary, span));

                Claim(component.Name, port.Key, span, stated: false);
                Count(component.Name);
                Count(boundary.Component);

                if (wired)
                {
                    Report(
                        TopologyDiagnostics.OpenPortTerminated,
                        span,
                        ("component", component.Name),
                        ("port", port.Spelling));
                }
            }
        }
    }

    private bool IsNode(string component) =>
        _componentsByName.TryGetValue(component, out var slot)
        && _components[slot.Index].Kind?.HasUnlimitedPorts == true;

    private string CircuitOf(string component) =>
        _componentsByName.TryGetValue(component, out var slot)
            ? _components[slot.Index].CircuitName
            : _circuits[0].Name;
}
