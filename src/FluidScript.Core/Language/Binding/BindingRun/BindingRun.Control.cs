using System.Collections.Immutable;
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

    /// <summary>Checks a <c>measure=</c> that reads a node directly (<c>D-150</c>), and puts the sensor the reading implies on it (I8, <c>D-151</c>).</summary>
    /// <param name="controller">The controller, for the message.</param>
    /// <param name="measurement">What the line measures.</param>
    /// <param name="span">The control line.</param>
    /// <remarks>A sensor was checked where it was placed, so only a node read directly is checked here.</remarks>
    private void CheckMeasurement(string controller, PropertyReference measurement, TextSpan span)
    {
        if (_componentsByName.TryGetValue(measurement.Component, out var measured)
            && _components[measured.Index].Kind?.Keyword is "node")
        {
            if (_degrees.GetValueOrDefault(measurement.Component) > 2)
            {
                CheckMeasuredNode(controller, measurement.Component, span);
                return;
            }

            InferSensor(measurement.Component, measurement.Property, span);
        }
    }

    /// <summary>
    /// I8 (<c>D-151</c>): a controller that reads a node's temperature, pressure or flow directly reads it through a sensor
    /// the binder puts there -- <c>NS__TE</c> for <c>measure=NS.t</c> -- unless one of that kind already stands on the node,
    /// which it then reads through.
    /// </summary>
    /// <remarks>
    /// The user's rule: a sensor is a physical component and is always drawn, and a controller is joined to the sensor, never
    /// to a node. Inferred like I1's nodes, so the script stays as short as it was and the sensor is a real component -- one
    /// the layout stands on its node, the canvas can hover, and the user can promote by writing its name down. Untagged, as
    /// every inferred component is (<c>D-34</c>).
    /// </remarks>
    private void InferSensor(string node, string property, TextSpan span)
    {
        var keyword = property switch
        {
            "t" => "t_sensor",
            "p" => "p_sensor",
            "flow" => "flow_sensor",
            _ => null,
        };

        if (keyword is null || registry.Resolve(keyword) is not KindResolution.Exact { Kind: var kind })
        {
            return;
        }

        if (_components.Any(c => string.Equals(c.AttachedTo, node, StringComparison.Ordinal) && string.Equals(c.Kind?.Keyword, kind.Keyword, StringComparison.Ordinal)))
        {
            return;
        }

        var name = $"{node}__{kind.TagCode}";

        if (_componentsByName.ContainsKey(name))
        {
            return;
        }

        Register(
            new ComponentSymbol
            {
                Name = name,
                Origin = new Origin.Inferred("I8", name),
                Kind = kind,
                WrittenKind = keyword,
                Parameters = ImmutableDictionary.Create<string, ParameterValue>(StringComparer.Ordinal),
                DeclarationSpan = null,
                CircuitName = CircuitOf(node),
                Ports = [],
                AttachedTo = node,
            },
            null);

        Report(BinderDiagnostics.ComponentInferred, span, ("kind", keyword), ("name", name), ("rule", "I8"));
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

        CheckMeasurement(controller.Name, measurement, statement.Span);

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

        CheckMeasurement(controller.Name, measurement, statement.Span);

        var target = Value(setpoint.Value, DimensionOf(measurement));
        var binding = new ControlBindingSymbol
        {
            Controller = controller,
            Actuator = actuator,
            Measurement = measurement,
            Setpoint = target,
            Setpoints = PerCase(setpoint.Value, DimensionOf(measurement), target),
            Span = statement.Span,
        };

        _controlBindings.Add(parse.Language == 2 ? WithTuning(binding, arguments) : binding);
    }

    /// <summary>Binds what language 2 writes on a controller beyond its setpoint (<c>D-168</c>, <c>19</c> §Controllers).</summary>
    /// <param name="binding">The line as language 1's short form binds it.</param>
    /// <param name="arguments">Its named arguments, by name as written.</param>
    /// <returns>The binding with its band, differential, output limits and curve.</returns>
    /// <remarks>
    /// A band and a differential are differences in what is measured (<c>20 K</c> on a temperature), and output
    /// limits are values of what is moved (<c>10..100 %</c> of a valve's position), which is why they sit on the
    /// line and not on the controller: only the line knows either. The setpoint's dimension is checked here too,
    /// which language 1 does not do.
    /// </remarks>
    private ControlBindingSymbol WithTuning(ControlBindingSymbol binding, Dictionary<string, ParameterSyntax> arguments)
    {
        var named = arguments.Values.ToDictionary(static argument => NameResolution.Normalize(argument.Name.Text), StringComparer.Ordinal);
        var measured = DimensionOf(binding.Measurement);

        if (binding.Setpoint is { } setpoint && measured is { } expected && setpoint.Dimension != expected
            && named.TryGetValue("setpoint", out var written))
        {
            Mismatch(written, "setpoint", expected, setpoint.Dimension);
            binding = binding with { Setpoint = null, Setpoints = [] };
        }

        (Quantity? Low, Quantity? High) output = named.TryGetValue("output", out var limits) ? Output(binding.Actuator, limits) : (null, null);

        return binding with
        {
            Band = named.TryGetValue("band", out var band) ? Difference(band, measured) : null,
            Differential = named.TryGetValue("differential", out var differential) ? Difference(differential, measured) : null,
            OutputLow = output.Low,
            OutputHigh = output.High,
            Curve = named.TryGetValue("curve", out var curve) ? CurveNamed(curve) : null,
        };
    }

    /// <summary>A positive difference in what is measured: a band or a differential.</summary>
    private Quantity? Difference(ParameterSyntax argument, Dimension? measured)
    {
        var dimension = measured?.Delta ?? measured;

        if (Value(argument.Value, dimension) is not { } value)
        {
            return null;
        }

        if (dimension is { } expected && value.Dimension != expected)
        {
            Mismatch(argument, argument.Name.Text, expected, value.Dimension);
            return null;
        }

        if (value.SiValue <= 0)
        {
            Report(BinderDiagnostics.NegativeValue, argument.Span, ("parameter", argument.Name.Text));
            return null;
        }

        return value;
    }

    /// <summary>The output limits, <c>low..high</c> in the actuated parameter's dimension and inside its valid range.</summary>
    private (Quantity? Low, Quantity? High) Output(PropertyReference actuator, ParameterSyntax argument)
    {
        if (argument.Value is not RangeExpressionSyntax range)
        {
            Report(
                BinderDiagnostics.UnacceptedSymbol,
                argument.Value.Span,
                ("parameter", argument.Name.Text),
                ("available", "a range, such as 10..100 %"),
                ("written", parse.Source.ToString(argument.Value.Span).Trim()));
            return (null, null);
        }

        var target = _componentsByName.TryGetValue(actuator.Component, out var slot)
            && _components[slot.Index].Kind is { } kind
            && kind.Parameters.TryGetValue(actuator.Property, out var info)
                ? new ParameterTarget(actuator.Component, kind, info)
                : null;

        var low = Value(range.From, target?.Info.Dimension);
        var high = Value(range.To, target?.Info.Dimension);

        // Both ends are checked, so a range wrong at both ends says so twice rather than once and then again.
        var lowInvalid = target is not null && low is { } l && CheckValidity(target, l, range.From.Span);
        var highInvalid = target is not null && high is { } h && CheckValidity(target, h, range.To.Span);

        if (lowInvalid || highInvalid)
        {
            return (null, null);
        }

        return (low, high);
    }

    /// <summary>The curve a <c>curve</c> controller follows, by name.</summary>
    private string? CurveNamed(ParameterSyntax argument)
    {
        if (argument.Value is ReferenceSyntax { Parts.IsDefaultOrEmpty: true } reference && _curvesByName.ContainsKey(reference.Head.Token.Text))
        {
            return reference.Head.Token.Text;
        }

        Report(BinderDiagnostics.UnknownName, argument.Value.Span, ("name", parse.Source.ToString(argument.Value.Span).Trim()));
        return null;
    }

    private void Mismatch(ParameterSyntax argument, string parameter, Dimension expected, Dimension actual) =>
        Report(
            BinderDiagnostics.ParameterDimensionMismatch,
            argument.Span,
            ("parameter", parameter),
            ("expected", BinderDiagnostics.Expected(expected)),
            ("value", parse.Source.ToString(argument.Value.Span).Trim()),
            ("actual", actual.Name.ToLowerInvariant()));

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
        if (Disturbance(statement, circuit, range => Bounds(range, Dimension.Time)) is { } disturbance)
        {
            _disturbances.Add(disturbance);
        }
    }

    /// <summary>Binds one step or ramp: its target, its times and its values.</summary>
    /// <param name="statement">The line.</param>
    /// <param name="circuit">The circuit it belongs to.</param>
    /// <param name="times">Reads the line's times; a language 2 run reads a clock time as well as a duration.</param>
    /// <returns>The change, or <see langword="null"/> when its target has been reported.</returns>
    private DisturbanceSymbol? Disturbance(
        DisturbanceSyntax statement, string circuit, Func<RangeOrPointSyntax, (Quantity? From, Quantity? To)> times)
    {
        var target = statement.Target;
        var component = target.Component.Token.Text;

        if (target.Port is not { } written)
        {
            // `at 60 s HE4 = 45` names no parameter, and the reference message says exactly what is
            // missing: a schedule changes a property, not a component.
            Report(BinderDiagnostics.ExpectedReference, target.Span, ("parameter", "the schedule target"));
            return null;
        }

        var parameter = written.Text;

        if (!_componentsByName.TryGetValue(component, out var slot))
        {
            Report(
                BinderDiagnostics.UnknownName,
                target.Span,
                ("name", component));
            return null;
        }

        ParameterInfo? info = null;
        Dimension? dimension = null;

        // A language 2 run may move a controller's setpoint (`19` §Runs), which is its line's and read in what
        // it measures, not a parameter of the controller.
        if (parse.Language == 2
            && string.Equals(NameResolution.Normalize(parameter), "setpoint", StringComparison.Ordinal)
            && _controlBindings.FirstOrDefault(binding => string.Equals(binding.Controller.Name, component, StringComparison.Ordinal)) is { } loop)
        {
            parameter = "setpoint";
            dimension = DimensionOf(loop.Measurement);
        }
        else if (_components[slot.Index].Kind is { } kind)
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
                return null;
            }

            if (suggestion is not null)
            {
                ReportLegacySpelling(written.Span, parameter, suggestion);
            }

            dimension = info.Dimension;
        }

        var (from, to) = times(statement.When);
        var (fromValue, toValue) = Bounds(statement.Value, dimension);

        // A single value is where the change ends: at the instant for `at`, at the end of the span for
        // `over` (`12` §Schedule: a step at the end of the ramp). Only a range has a start value.
        return new DisturbanceSymbol(
            circuit,
            new PropertyReference(component, info?.Key ?? parameter),
            from,
            to ?? from,
            toValue is null ? null : fromValue,
            toValue ?? fromValue,
            statement.Span);
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
        // In a case other than the design case the same expression has been evaluated once already, and
        // says the same mistakes again.
        var diagnostics = _case is null ? _diagnostics : ImmutableArray.CreateBuilder<Diagnostic>();
        var evaluator = new ExpressionEvaluator(this, parse.Source, diagnostics);
        var result = evaluator.Evaluate(expression);

        if (_case is not null)
        {
            _diagnostics.AddRange(diagnostics.Where(diagnostic => !Reported(diagnostic)));
        }

        if (result is not EvaluationResult.Value value)
        {
            return null;
        }

        return value.IsBare && dimension is { } target
            ? Quantity.FromBareNumber(value.Quantity.SiValue, target)
            : value.Quantity;
    }
}
