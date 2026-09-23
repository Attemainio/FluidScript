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

            var slot = Infer(name, "I2", CircuitOf(connection.From.Component), connection.SourceSpan);
            var middle = new EndpointSymbol(_components[slot.Index].Name, string.Empty);

            rewritten.Add(connection with { To = middle });
            rewritten.Add(new ConnectionSymbol(middle, connection.To, connection.SourceSpan));

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
                        ("port", port.Name));
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

    // ---- step 9: attachments, control bindings, and the schedule ----------------------------------

    private void BindAttachments(List<CircuitBlock> blocks)
    {
        foreach (var block in blocks)
        {
            var index = _circuits.IndexOf(block.Circuit!);
            var circuit = _circuits[index];

            var supply = Attachment(block, AttachmentDirection.Inlet);
            var returned = Attachment(block, AttachmentDirection.Outlet);

            if (supply is null != returned is null)
            {
                var present = (supply ?? returned)!;

                Report(
                    BinderDiagnostics.LoneAttachment,
                    present.Span,
                    ("circuit", circuit.Name),
                    ("present", supply is null ? "outlet" : "inlet"),
                    ("node", present.ParentComponentName),
                    ("other", supply is null ? "inlet" : "outlet"));
            }

            var parents = new[] { supply?.ParentComponent, returned?.ParentComponent }
                .Where(static component => component is not null)
                .Select(static component => component!.CircuitName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (parents.Length > 1)
            {
                // One parent, or the model cannot carry it. A subcircuit fed by one circuit and
                // draining into another is a topology to write as connections.
                Report(
                    BinderDiagnostics.AttachmentsDisagree,
                    supply!.Span,
                    ("circuit", circuit.Name),
                    ("a", parents[0]),
                    ("b", parents[1]));
            }

            Attach(circuit.Name, supply, asSupply: true);
            Attach(circuit.Name, returned, asSupply: false);

            _circuits[index] = circuit with
            {
                Supply = supply,
                Return = returned,
                ParentCircuit = parents.Length == 1 ? parents[0] : null,
            };
        }
    }

    /// <summary>Turns one attachment line into an ordinary connection.</summary>
    /// <param name="circuit">The attaching subcircuit's name.</param>
    /// <param name="attachment">The resolved attachment, or <see langword="null"/> when the line is absent.</param>
    /// <param name="asSupply">Whether this is the <c>supply</c> end rather than the <c>return</c> end.</param>
    /// <remarks>
    /// <para>
    /// <c>supply N3</c> becomes a connection from the parent's <c>N3</c> to the subcircuit's first
    /// unconnected inlet, and <c>return N5</c> one from its last unconnected outlet back to <c>N5</c>.
    /// <strong>After this there is nothing structurally special about a subcircuit</strong>: it is a set
    /// of components connected to the rest, and every well-posedness rule applies to it unmodified.
    /// </para>
    /// <para>
    /// That is the whole point of making attachment explicit. An inferred one would have to guess which
    /// port of which component the header meets, and a wrong guess yields a graph that is well-posed,
    /// solvable, and describes a different plant.
    /// </para>
    /// </remarks>
    private void Attach(string circuit, AttachmentSymbol? attachment, bool asSupply)
    {
        if (attachment?.ParentComponent is not { } parent || parent.Kind is not { } parentKind)
        {
            return;
        }

        // FS2217 and FS1518 partition one mistake and never both fire: FS1518 is the name resolving to
        // nothing, this is the name resolving to a component of the attaching circuit itself.
        if (string.Equals(parent.CircuitName, circuit, StringComparison.Ordinal))
        {
            Report(
                TopologyDiagnostics.SelfAttachment,
                attachment.Span,
                ("circuit", circuit),
                ("node", parent.Name));
            return;
        }

        if (OpenEnd(circuit, asSupply ? PortRole.Inlet : PortRole.Outlet, last: !asSupply)
                is not { Kind: { } endKind } end
            || Unqualified(parent, parentKind, attachment.Span, asSupply) is not { } outer
            || Unqualified(end, endKind, attachment.Span, !asSupply) is not { } inner)
        {
            return;
        }

        var connection = asSupply
            ? new ConnectionSymbol(outer, inner, attachment.Span)
            : new ConnectionSymbol(inner, outer, attachment.Span);

        _connections.Add(connection);
        Count(connection.From.Component);
        Count(connection.To.Component);
    }

    /// <summary>The end of a subcircuit an attachment claims.</summary>
    /// <param name="circuit">The subcircuit's name.</param>
    /// <param name="role">The direction wanted: an inlet for a supply, an outlet for a return.</param>
    /// <param name="last">Whether to take the last such component rather than the first.</param>
    /// <returns>The component, or <see langword="null"/> when the subcircuit has no free end.</returns>
    /// <remarks>
    /// <para>
    /// Nodes are skipped. A node never runs out of ports, so an unrelated declared one would always
    /// look free and would hijack every attachment in the circuit; the end an attachment means is a
    /// real component's port.
    /// </para>
    /// <para>
    /// <strong>So is any port I3 is allowed to leave open.</strong> A duty-mode exchanger's <c>in2</c>
    /// and <c>out2</c> are materialized and unconnected by design, and they sit earlier in the AHU
    /// branch than its pump does — so an attachment taking the first free inlet without this test wired
    /// the district header into the exchanger's <em>second side</em>, made it look coupled, and left the
    /// branch it was meant to feed in a hydraulic component of its own.
    /// </para>
    /// </remarks>
    private ComponentSymbol? OpenEnd(string circuit, PortRole role, bool last) =>
        OpenEnd(circuit, role, last, bidirectional: false)
        ?? OpenEnd(circuit, role, last, bidirectional: true);

    /// <summary>One pass of <see cref="OpenEnd(string, PortRole, bool)"/>, over one class of port.</summary>
    /// <param name="circuit">The subcircuit's name.</param>
    /// <param name="role">The direction wanted.</param>
    /// <param name="last">Whether to take the last such component rather than the first.</param>
    /// <param name="bidirectional">Whether to accept a port whose direction the solve decides.</param>
    /// <returns>The component, or <see langword="null"/> when this pass found none.</returns>
    /// <remarks>
    /// <strong>A directed port wins over an undirected one wherever both are free.</strong> A three-way
    /// valve declares <c>a</c>, <c>b</c> and <c>c</c> bidirectional, because which way each carries flow
    /// is a solved outcome — so accepting bidirectional ports in one pass would attach the AHU branch's
    /// <em>supply</em> to its mixing valve, which sits before the pump in declaration order, rather than
    /// to the pump inlet the branch actually opens at. Taking them only when nothing directed is free
    /// still lets the <em>return</em> land on that valve, where no directed outlet exists at all.
    /// </remarks>
    private ComponentSymbol? OpenEnd(string circuit, PortRole role, bool last, bool bidirectional)
    {
        ComponentSymbol? found = null;

        foreach (var component in _components)
        {
            if (!string.Equals(component.CircuitName, circuit, StringComparison.Ordinal)
                || component.Kind is not { IsObserver: false, HasUnlimitedPorts: false } kind)
            {
                continue;
            }

            var claimed = _claimed.TryGetValue(component.Name, out var used) ? used : [];
            var wanted = bidirectional ? PortRole.Bidirectional : role;

            if (!component.Ports.Any(port =>
                    !claimed.ContainsKey(port) && RoleOf(kind, port) == wanted && IsRequired(kind, port)))
            {
                continue;
            }

            found = component;

            if (!last)
            {
                return found;
            }
        }

        return found;
    }

    /// <summary>Whether a port is one the design means to connect.</summary>
    /// <param name="kind">The component's registry entry.</param>
    /// <param name="port">The materialized port's name.</param>
    /// <returns><see langword="false"/> when inference rule I3 may leave it open.</returns>
    /// <remarks>
    /// An indexed port is never claimed by an attachment either: it exists only because a connection
    /// evidenced it, so one that is free is one nothing asked for.
    /// </remarks>
    private static bool IsRequired(ComponentKindInfo kind, string port)
    {
        foreach (var declared in kind.Ports)
        {
            if (string.Equals(declared.Key, port, StringComparison.Ordinal))
            {
                return !declared.IsOptional;
            }
        }

        return false;
    }

    private AttachmentSymbol? Attachment(CircuitBlock block, AttachmentDirection direction)
    {
        var statement = block.Statements
            .OfType<AttachmentSyntax>()
            .FirstOrDefault(attachment => attachment.Direction == direction);

        if (statement is null)
        {
            return null;
        }

        var name = statement.Endpoint.Component.Token.Text;

        // An ordinary name lookup rather than a qualified one: identifiers are unique across the model
        // (`D-41`), which is exactly what an attachment relies on.
        if (!_componentsByName.TryGetValue(name, out var slot))
        {
            Report(BinderDiagnostics.AttachmentNotDeclared, statement.Span, ("name", name));
            return new AttachmentSymbol(name, null, statement.Span);
        }

        return new AttachmentSymbol(name, _components[slot.Index], statement.Span);
    }
}
