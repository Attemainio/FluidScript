using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class BindingRun
{
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

    private void BindObservers()
    {
        foreach (var component in _components)
        {
            if (component.AttachedTo is not { } node)
            {
                continue;
            }

            if (_componentsByName.ContainsKey(node))
            {
                CheckMeasuredNode(component.Name, node, component.DeclarationSpan ?? default);
                continue;
            }

            Report(
                BinderDiagnostics.UnknownName,
                component.DeclarationSpan ?? default,
                ("name", node));
        }
    }

    /// <summary>Refuses a measurement at a node where more than two pipes meet (<c>FS1548</c>, <c>D-150</c>).</summary>
    /// <param name="reader">The sensor or controller doing the reading, for the message.</param>
    /// <param name="node">The node read.</param>
    /// <param name="span">Where to report it.</param>
    /// <remarks>
    /// Two connections is an inline point on one pipe and one is a terminal, and either has one
    /// stream. Counted after inference, so a node rule I1 created is judged by the connections that
    /// made it.
    /// </remarks>
    private void CheckMeasuredNode(string reader, string node, TextSpan span)
    {
        var count = _degrees.GetValueOrDefault(node);

        if (count > 2)
        {
            Report(BinderDiagnostics.MeasuredJunction, span, ("name", reader), ("node", node), ("count", count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Checks a <c>measure=</c> that reads a node directly (<c>D-150</c>).</summary>
    /// <param name="controller">The controller, for the message.</param>
    /// <param name="component">The measured component.</param>
    /// <param name="span">The control line.</param>
    /// <remarks>A sensor was checked where it was placed, so only a node read directly is checked here.</remarks>
    private void CheckMeasurement(string controller, string component, TextSpan span)
    {
        if (_componentsByName.TryGetValue(component, out var measured)
            && _components[measured.Index].Kind?.Keyword is "node")
        {
            CheckMeasuredNode(controller, component, span);
        }
    }

    private void BindControl(ControlBindingSyntax statement)
    {
        var arguments = new Dictionary<string, ParameterSyntax>(StringComparer.Ordinal);

        foreach (var argument in statement.Arguments)
        {
            arguments[argument.Name.Text] = argument;
        }

        if (statement.IsShortForm)
        {
            BindShortControl(statement, arguments);
            return;
        }

        foreach (var argument in statement.Arguments)
        {
            arguments[argument.Name.Text] = argument;
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

        CheckMeasurement(controller.Name, measurement.Component, statement.Span);

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

        CheckMeasurement(controller.Name, measurement.Component, statement.Span);

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
            ? kind?.Parameters.Values.Select(static info => info.Name).Order(StringComparer.Ordinal)
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
        && kind.ResolveProperty(reference.Property) is { } property
            ? property.Dimension
            : null;

    private static PropertyReference? Reference(ParameterSyntax argument) =>
        argument.Value is ReferenceSyntax { Parts.Length: > 0 } reference
            ? new PropertyReference(reference.Head.Token.Text, reference.PropertyPath())
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

        var parameter = written.Text;

        if (!_componentsByName.TryGetValue(component, out var slot))
        {
            Report(
                BinderDiagnostics.UnknownName,
                target.Span,
                ("name", component));
            return;
        }

        ParameterInfo? info = null;

        if (_components[slot.Index].Kind is { } kind)
        {
            // The same spellings a declaration accepts (`D-120`), the old ones with their suggestion;
            // the target is stored by key, which is how the transient finds the parameter.
            info = kind.ResolveParameter(parameter, out var suggestion, out _);

            if (info is null)
            {
                Report(
                    BinderDiagnostics.UnknownParameter,
                    target.Span,
                    ("kind", kind.Keyword),
                    ("parameter", parameter),
                    ("available", string.Join(", ", kind.Parameters.Values.Select(static info => info.Name).Order(StringComparer.Ordinal))));
                return;
            }

            if (suggestion is not null)
            {
                ReportLegacySpelling(written.Span, parameter, suggestion);
            }
        }

        var (from, to) = Bounds(statement.When, Dimension.Time);
        var (fromValue, toValue) = Bounds(statement.Value, info?.Dimension);

        // A single value is where the change ends: at the instant for `at`, at the end of the span for
        // `over` (`12` §Schedule: a step at the end of the ramp). Only a range has a start value.
        _disturbances.Add(new DisturbanceSymbol(
            circuit,
            new PropertyReference(component, info?.Key ?? parameter),
            from,
            to ?? from,
            toValue is null ? null : fromValue,
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
}
