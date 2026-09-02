using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

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

        // Snapshotted before inference, because FS1507 and FS1511 are statements about the topology
        // the user wrote. After I3 every component is connected to something, and the two codes could
        // never fire again.
        _sourceConnections.AddRange(_connections);

        ApplyInference();
        BindObservers();
        BindAttachments(blocks);
        BindControlBindings(blocks);
        BindSchedule(blocks);
        Validate();
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

            var ports = new List<string>(kind.Ports.Select(static port => port.Name));

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
    /// Two things evidence a port: a qualified endpoint naming it, and an elevation parameter that
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
                if (endpoint.Port is { } port)
                {
                    Evidence(endpoint.Component.Token.Text, port.Token.Text);
                }
            }
        }

        foreach (var component in _components)
        {
            foreach (var family in component.Kind?.PortFamilies ?? [])
            {
                if (family.ElevationParameterSuffix is not { } suffix)
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
        var declared = kind.Ports.Select(static port => port.Name).ToList();

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
            return new EndpointSymbol(component.Name, endpoint.Port?.Token.Text ?? string.Empty);
        }

        return endpoint.Port is { } written
            ? Qualified(component, kind, written, span)
            : Unqualified(component, kind, span, outgoing);
    }

    private EndpointSymbol? Qualified(
        ComponentSymbol component, ComponentKindInfo kind, IdentifierSyntax written, TextSpan span)
    {
        var port = written.Token.Text;

        if (component.Ports.Contains(port, StringComparer.Ordinal))
        {
            return Claim(component.Name, port, span);
        }

        if (kind.HasUnlimitedPorts)
        {
            return new EndpointSymbol(component.Name, port);
        }

        if (Fit(kind, port) == PortFit.OutsideRange)
        {
            var family = kind.PortFamilies.First(
                candidate => Indexed.Matches($"{candidate.Prefix}{{index}}", port, out _));

            Report(
                BinderDiagnostics.IndexOutsideFamily,
                written.Span,
                ("written", port),
                ("kind", kind.Keyword),
                ("min", family.MinIndex.ToString(CultureInfo.InvariantCulture)),
                ("max", family.MaxIndex.ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            Report(
                BinderDiagnostics.UnknownPort,
                written.Span,
                ("kind", kind.Keyword),
                ("port", port),
                ("available", string.Join(", ", component.Ports)));
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

        return free is null ? null : Claim(component.Name, free, span);
    }

    private static PortRole RoleOf(ComponentKindInfo kind, string port)
    {
        foreach (var declared in kind.Ports)
        {
            if (string.Equals(declared.Name, port, StringComparison.Ordinal))
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

    private EndpointSymbol? Claim(string component, string port, TextSpan span)
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

        return new EndpointSymbol(component, port);
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

        _components.Add(node);
        var slot = new ComponentSlot(_components.Count - 1, null);
        _componentsByName[name] = slot;

        // Info, and off by default in the log: on a large script these would drown everything else.
        // They must exist all the same, or the inference is invisible magic.
        Report(BinderDiagnostics.ComponentInferred, span, ("kind", "node"), ("name", name), ("rule", rule));

        return slot;
    }

    private void Count(string component) =>
        _degrees[component] = _degrees.GetValueOrDefault(component) + 1;

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

            var stem = $"{connection.From.Component}__{connection.To.Component}";
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
        // I3 — every non-optional port nothing connected gets a boundary node. What condition it
        // carries is topology's decision; the binder records only that the port is terminated.
        foreach (var component in _components.ToArray())
        {
            if (component.Kind is not { } kind || kind.HasUnlimitedPorts)
            {
                continue;
            }

            var span = component.DeclarationSpan ?? default;

            foreach (var port in kind.Ports)
            {
                var claimed = _claimed.TryGetValue(component.Name, out var used) ? used : [];

                if (port.IsOptional || claimed.ContainsKey(port.Name))
                {
                    continue;
                }

                var slot = Infer($"{component.Name}__{port.Name}", "I3", component.CircuitName, span);
                var boundary = new EndpointSymbol(_components[slot.Index].Name, string.Empty);

                _connections.Add(new ConnectionSymbol(
                    new EndpointSymbol(component.Name, port.Name), boundary, span));

                Claim(component.Name, port.Name, span);
                Count(component.Name);
                Count(boundary.Component);
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

            var supply = Attachment(block, AttachmentDirection.Supply);
            var returned = Attachment(block, AttachmentDirection.Return);

            if (supply is null != returned is null)
            {
                var present = (supply ?? returned)!;

                Report(
                    BinderDiagnostics.LoneAttachment,
                    present.Span,
                    ("circuit", circuit.Name),
                    ("present", supply is null ? "return" : "supply"),
                    ("node", present.ParentComponentName),
                    ("other", supply is null ? "supply" : "return"));
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

            _circuits[index] = circuit with
            {
                Supply = supply,
                Return = returned,
                ParentCircuit = parents.Length == 1 ? parents[0] : null,
            };
        }
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

    private void BindControlBindings(List<CircuitBlock> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var statement in block.Statements.OfType<ControlBindingSyntax>())
            {
                BindControl(statement);
            }
        }
    }

    /// <summary>Places every instrument that stated an <c>at</c> clause (<c>D-61</c>).</summary>
    /// <remarks>
    /// After inference rather than with the declaration, because the node an instrument observes is
    /// very often one rule I1 created: <c>N2</c> exists because a connection named it, not because
    /// anybody declared it. Whether the clause belongs on the kind at all was settled at declaration
    /// time; all that is left here is whether the node exists.
    /// </remarks>
    private void BindObservers()
    {
        foreach (var component in _components)
        {
            if (component.AttachedTo is not { } node || _componentsByName.ContainsKey(node))
            {
                continue;
            }

            Report(
                BinderDiagnostics.UnknownName,
                component.DeclarationSpan ?? default,
                ("name", node));
        }
    }

    private void BindControl(ControlBindingSyntax statement)
    {
        var arguments = new Dictionary<string, ParameterSyntax>(StringComparer.Ordinal);

        foreach (var argument in statement.Arguments)
        {
            arguments[argument.Name.Token.Text] = argument;
        }

        if (statement.IsShortForm)
        {
            BindShortControl(statement, arguments);
            return;
        }

        foreach (var argument in statement.Arguments)
        {
            arguments[argument.Name.Token.Text] = argument;
        }

        var missing = ControlArguments.Where(name => !arguments.ContainsKey(name)).ToArray();

        if (missing.Length > 0)
        {
            Report(
                BinderDiagnostics.ControlMissingArgument,
                statement.Span,
                ("list", string.Join(", ", ControlArguments)),
                ("missing", string.Join(", ", missing)));
            return;
        }

        // Every field comes from a named argument, so transposing two of them is an error here rather
        // than a silent reversal that drives the valve the wrong way (`D-40`).
        if (Reference(arguments["actuate"]) is not { } actuator)
        {
            // `D-43`: a bare component name is not an actuator. There is deliberately no per-kind
            // default, because a valve has more than one thing that could move.
            Report(BinderDiagnostics.ExpectedReference, arguments["actuate"].Span, ("parameter", "actuate"));
            return;
        }

        if (Reference(arguments["measure"]) is not { } measurement)
        {
            Report(BinderDiagnostics.ExpectedReference, arguments["measure"].Span, ("parameter", "measure"));
            return;
        }

        var controllerName = (arguments["by"].Value as ReferenceSyntax)?.Head.Token.Text ?? string.Empty;
        var controller = _componentsByName.TryGetValue(controllerName, out var slot)
            ? _components[slot.Index]
            : null;

        if (controller?.Kind?.Keyword is not "controller")
        {
            Report(
                BinderDiagnostics.NotAController,
                arguments["by"].Span,
                ("name", controllerName),
                ("kind", controller?.Kind?.Keyword ?? controller?.WrittenKind ?? "value"));
            return;
        }

        if (_componentsByName.TryGetValue(actuator.Component, out var actuated)
            && _components[actuated.Index].Kind is { } kind
            && !kind.Parameters.ContainsKey(actuator.Property))
        {
            Report(
                BinderDiagnostics.ParameterNotControllable,
                arguments["actuate"].Span,
                ("param", actuator.Property),
                ("component", actuator.Component));
            return;
        }

        _controlBindings.Add(new ControlBindingSymbol
        {
            Controller = controller,
            Actuator = actuator,
            Measurement = measurement,
            Setpoint = Value(arguments["setpoint"].Value, DimensionOf(measurement)),
            Span = statement.Span,
        });
    }

    /// <summary>Binds <c>control TV1 with TE1 by PID1 setpoint=21</c> (<c>D-61</c>).</summary>
    /// <remarks>
    /// The same three resolutions as the long form, reached from positions instead of names. Only
    /// <c>setpoint=</c> survives as a named argument, because it is the one value no position can
    /// carry: the other three are components, and a setpoint is a quantity.
    /// </remarks>
    private void BindShortControl(
        ControlBindingSyntax statement, Dictionary<string, ParameterSyntax> arguments)
    {
        if (!arguments.TryGetValue("setpoint", out var setpoint))
        {
            Report(
                BinderDiagnostics.ControlMissingArgument,
                statement.Span,
                ("list", "setpoint"),
                ("missing", "setpoint"));
            return;
        }

        if (Endpoint(statement.Actuator!, actuated: true) is not { } actuator
            || Endpoint(statement.Sensor!, actuated: false) is not { } measurement)
        {
            return;
        }

        var controllerName = statement.Controller!.Text;
        var controller = _componentsByName.TryGetValue(controllerName, out var slot)
            ? _components[slot.Index]
            : null;

        if (controller?.Kind?.Keyword is not "controller")
        {
            Report(
                BinderDiagnostics.NotAController,
                statement.Controller.Span,
                ("name", controllerName),
                ("kind", controller?.Kind?.Keyword ?? controller?.WrittenKind ?? "value"));
            return;
        }

        _controlBindings.Add(new ControlBindingSymbol
        {
            Controller = controller,
            Actuator = actuator,
            Measurement = measurement,
            Setpoint = Value(setpoint.Value, DimensionOf(measurement)),
            Span = statement.Span,
        });
    }

    /// <summary>Resolves one bare or qualified endpoint of a short <c>control</c> line.</summary>
    /// <param name="endpoint">The endpoint as written, with or without its <c>.</c> half.</param>
    /// <param name="actuated">
    /// <see langword="true"/> for the thing the loop drives, <see langword="false"/> for what it reads.
    /// </param>
    /// <returns>The property reference, or <see langword="null"/> when it has been reported.</returns>
    /// <remarks>
    /// <c>D-61</c> amends <c>D-43</c>, which was right about parameters and wrong about actuators: of a
    /// valve's <c>position</c>, <c>kv</c> and <c>authority</c>, only <c>position</c> moves during a
    /// solve, so where the registry names exactly one the bare form is unambiguous <em>by
    /// construction</em>. Where it names none, this is <c>FS1531</c> and the qualified form is
    /// required — which stays legal everywhere.
    /// </remarks>
    private PropertyReference? Endpoint(EndpointSyntax endpoint, bool actuated)
    {
        var name = endpoint.Component.Text;

        if (endpoint.Port is { } port)
        {
            return new PropertyReference(name, port.Text);
        }

        if (!_componentsByName.TryGetValue(name, out var slot))
        {
            Report(BinderDiagnostics.UnknownName, endpoint.Span, ("name", name));
            return null;
        }

        var kind = _components[slot.Index].Kind;
        var single = actuated ? kind?.ActuatedParameter : kind?.MeasuredProperty;

        if (single is not null)
        {
            return new PropertyReference(name, single);
        }

        var candidates = actuated
            ? kind?.Parameters.Keys.Order(StringComparer.Ordinal)
            : kind?.Properties.Keys.Order(StringComparer.Ordinal);

        Report(
            BinderDiagnostics.NoSingleEndpoint,
            endpoint.Span,
            ("kind", kind?.Keyword ?? _components[slot.Index].WrittenKind),
            ("role", actuated ? "parameter to move" : "property to read"),
            ("example", $"{name}.{candidates?.FirstOrDefault() ?? (actuated ? "position" : "t")}"));

        return null;
    }

    /// <summary>Reads the dimension of the property a controller measures, for its setpoint.</summary>
    /// <returns>
    /// The measured property's dimension, or <see langword="null"/> when nothing can say — in which
    /// case a bare setpoint stays bare rather than taking a unit that was guessed.
    /// </returns>
    private Dimension? DimensionOf(PropertyReference reference) =>
        _componentsByName.TryGetValue(reference.Component, out var slot)
        && _components[slot.Index].Kind is { } kind
        && kind.Properties.TryGetValue(reference.Property, out var property)
            ? property.Dimension
            : null;

    private static PropertyReference? Reference(ParameterSyntax argument) =>
        argument.Value is ReferenceSyntax { Parts.Length: > 0 } reference
            ? new PropertyReference(reference.Head.Token.Text, reference.Parts[^1].Name.Token.Text)
            : null;

    /// <summary>Binds the <c>schedule</c> section — the step <c>15</c>'s binding order never had.</summary>
    /// <remarks>
    /// Steps 0–11 cover directives, declarations, kinds, parameters, expressions, ports, connections,
    /// inference, attachments, control bindings, validation and tags, and never mention a disturbance:
    /// the parser produced <see cref="DisturbanceSyntax"/> and nothing consumed it. It runs here,
    /// after control bindings and before validation, because a scheduled target resolves exactly the
    /// way an actuated one does.
    /// </remarks>
    private void BindSchedule(List<CircuitBlock> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var statement in block.Statements.OfType<DisturbanceSyntax>())
            {
                BindDisturbance(statement, block.Circuit!.Name);
            }
        }
    }

    private void BindDisturbance(DisturbanceSyntax statement, string circuit)
    {
        var target = statement.Target;
        var component = target.Component.Token.Text;

        if (target.Port is not { } written)
        {
            // `at 60 s HE4 = 45` names no parameter, and the reference message says exactly what is
            // missing: a schedule changes a property, not a component.
            Report(BinderDiagnostics.ExpectedReference, target.Span, ("parameter", "the schedule target"));
            return;
        }

        var parameter = written.Token.Text;

        if (!_componentsByName.TryGetValue(component, out var slot))
        {
            Report(
                BinderDiagnostics.UnknownName,
                target.Span,
                ("name", component));
            return;
        }

        ParameterInfo? info = null;

        if (_components[slot.Index].Kind is { } kind && !kind.Parameters.TryGetValue(parameter, out info))
        {
            Report(
                BinderDiagnostics.UnknownParameter,
                target.Span,
                ("kind", kind.Keyword),
                ("parameter", parameter),
                ("available", string.Join(", ", kind.Parameters.Keys.Order(StringComparer.Ordinal))));
            return;
        }

        var (from, to) = Bounds(statement.When, Dimension.Time);
        var (fromValue, toValue) = Bounds(statement.Value, info?.Dimension);

        _disturbances.Add(new DisturbanceSymbol(
            circuit,
            new PropertyReference(component, parameter),
            from,
            to ?? from,
            fromValue,
            toValue ?? fromValue,
            statement.Span));
    }

    private (Quantity? From, Quantity? To) Bounds(RangeOrPointSyntax range, Dimension? dimension) =>
        range switch
        {
            PointSyntax point => (Value(point.Value, dimension), null),
            RangeSyntax span => (Value(span.From, dimension), Value(span.To, dimension)),
            _ => (null, null),
        };

    /// <summary>Evaluates one expression, applying <c>D-14</c>'s bare-number rule against a target.</summary>
    private Quantity? Value(ExpressionSyntax expression, Dimension? dimension)
    {
        var evaluator = new ExpressionEvaluator(this, parse.Source, _diagnostics);

        if (evaluator.Evaluate(expression) is not EvaluationResult.Value value)
        {
            return null;
        }

        return value.IsBare && dimension is { } target
            ? Quantity.FromBareNumber(value.Quantity.SiValue, target)
            : value.Quantity;
    }

    // ---- step 10: validate --------------------------------------------------------------------------

    private void Validate()
    {
        var mentioned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in _sourceConnections)
        {
            mentioned.Add(connection.From.Component);
            mentioned.Add(connection.To.Component);
        }

        foreach (var component in _components)
        {
            // A warning, not an error: a partially written script is the normal editing state, and
            // erroring here would blank the diagram on every keystroke.
            if (component.Origin is Origin.Declared
                && component.Kind is { HasUnlimitedPorts: false, Ports.IsEmpty: false }
                && !mentioned.Contains(component.Name))
            {
                Report(
                    BinderDiagnostics.NotConnected,
                    component.DeclarationSpan ?? default,
                    ("name", component.Name));
            }
        }

        ReportIslands(mentioned);
        ReportDeadEnds();
    }

    private void ReportIslands(HashSet<string> mentioned)
    {
        // FS1511 is about a cluster and FS1507 about a component on its own, and the two never both
        // fire for one component — which is why anything in no connection at all is excluded here.
        foreach (var circuit in _circuits)
        {
            var members = _components
                .Where(component =>
                    string.Equals(component.CircuitName, circuit.Name, StringComparison.Ordinal)
                    && mentioned.Contains(component.Name))
                .Select(static component => component.Name)
                .ToHashSet(StringComparer.Ordinal);

            var islands = Islands(members);

            if (islands.Count < 2)
            {
                continue;
            }

            // The largest island is "the rest of the circuit"; every other one is adrift from it. A
            // tie breaks on the first name, so the report does not move when an edit elsewhere
            // changes a count.
            var main = islands
                .OrderByDescending(static island => island.Count)
                .ThenBy(static island => island[0], StringComparer.Ordinal)
                .First();

            foreach (var island in islands.Where(island => island != main))
            {
                Report(
                    BinderDiagnostics.DisconnectedGraph,
                    SpanOfFirstMention(island[0]),
                    ("name", island[0]),
                    ("count", (island.Count - 1).ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    private List<List<string>> Islands(HashSet<string> members)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var name in members)
        {
            adjacency[name] = [];
        }

        foreach (var connection in _sourceConnections)
        {
            if (members.Contains(connection.From.Component) && members.Contains(connection.To.Component))
            {
                adjacency[connection.From.Component].Add(connection.To.Component);
                adjacency[connection.To.Component].Add(connection.From.Component);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var islands = new List<List<string>>();

        // Walked in declaration order, so the island a diagnostic names is the one whose first
        // component the user wrote first rather than whichever the hash set happened to yield.
        foreach (var component in _components)
        {
            if (!members.Contains(component.Name) || !seen.Add(component.Name))
            {
                continue;
            }

            var island = new List<string> { component.Name };
            var stack = new Stack<string>();
            stack.Push(component.Name);

            while (stack.Count > 0)
            {
                foreach (var neighbour in adjacency[stack.Pop()])
                {
                    if (seen.Add(neighbour))
                    {
                        island.Add(neighbour);
                        stack.Push(neighbour);
                    }
                }
            }

            islands.Add(island);
        }

        return islands;
    }

    private void ReportDeadEnds()
    {
        // A node a subcircuit attaches to is not a dead end, however few connections were written on
        // it: `23` lowers `supply N3` to a connection from `N3` to the subcircuit's first unconnected
        // inlet, so the second edge exists — one stage later than this one runs. The distribution
        // header is where it shows, and both ends of both its headers were warned about (`F-12`).
        var attached = _circuits
            .SelectMany(static circuit => new[] { circuit.Supply, circuit.Return })
            .Where(static attachment => attachment is not null)
            .Select(static attachment => attachment!.ParentComponentName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var node in _components)
        {
            // A node with one connection and no boundary parameter is a dead end: nothing sets its
            // state and nothing else can. A node inferred by I3 is exempt — it *is* the boundary that
            // rule created, so it terminates a port rather than dead-ending on one.
            if (node.Kind?.HasUnlimitedPorts != true
                || node.Origin is Origin.Inferred { Rule: "I3" }
                || _degrees.GetValueOrDefault(node.Name) != 1
                || attached.Contains(node.Name)
                || IsBoundary(node))
            {
                continue;
            }

            Report(
                BinderDiagnostics.DeadEndNode,
                node.DeclarationSpan ?? SpanOfFirstMention(node.Name),
                ("name", node.Name));
        }
    }

    private static bool IsBoundary(ComponentSymbol node) =>
        node.Parameters.ContainsKey("t")
        || node.Parameters.ContainsKey("p")
        || node.Parameters.ContainsKey("flow");

    private TextSpan SpanOfFirstMention(string component) =>
        _connections.FirstOrDefault(connection =>
            string.Equals(connection.From.Component, component, StringComparison.Ordinal)
            || string.Equals(connection.To.Component, component, StringComparison.Ordinal))
            ?.SourceSpan ?? default;

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

        foreach (var component in _components)
        {
            if (component.DeclarationSpan is { } span)
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
