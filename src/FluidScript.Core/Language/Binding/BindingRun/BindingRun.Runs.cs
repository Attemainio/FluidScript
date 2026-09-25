using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <content>A language 2 run (<c>D-169</c>, <c>19</c> §Runs), bound once the model it runs against is complete.</content>
/// <remarks>
/// A run changes nothing in the model. Its settings, its events and its overrides are held on a
/// <see cref="RunSymbol"/>, and <see cref="RunProjection"/> applies them for the one run the interface plays.
/// An override of a parameter is a step at t = 0; an override of a <c>let</c> by a value is evaluated here,
/// once, into a step at t = 0 on every parameter that reads it; an override of a driver by a curve of time
/// re-points that driver's curves at the clock.
/// </remarks>
internal sealed partial class BindingRun
{
    private readonly List<RunSymbol> _runs = [];

    /// <summary>The order step 5 evaluated in, which a <c>let</c> override walks again.</summary>
    private ImmutableArray<ValueId> _order = [];

    private const string RunSettingList = "from, start, duration, frame, steady, or a let or a component's parameter to override";

    private void BindRuns()
    {
        foreach (var block in parse.Root.Statements.OfType<BlockSyntax>())
        {
            if (block.Head is RunHeadSyntax head)
            {
                _runs.Add(BindRun(block, head));
            }
        }
    }

    private RunSymbol BindRun(BlockSyntax block, RunHeadSyntax head)
    {
        var run = new RunSymbol
        {
            Title = head.Title?.StringValue ?? string.Empty,
            From = _scenarios.Count > 0 ? 0 : null,
            Span = head.Span,
        };

        var overrides = new List<ParameterSyntax>();

        foreach (var setting in block.Body.OfType<SettingLineSyntax>().SelectMany(static line => line.Assignments))
        {
            if (!setting.Name.Parts.IsDefaultOrEmpty || setting.Name.Head.Index is not null)
            {
                overrides.Add(setting);
                continue;
            }

            switch (NameResolution.Normalize(setting.Name.Head.Name.Text))
            {
                case "from":
                    run = run with { From = RunCase(setting) ?? run.From };
                    break;
                case "start":
                    run = run with { Start = RunStart(setting) };
                    break;
                case "duration":
                    run = run with { Duration = RunTime(setting)?.SiValue ?? run.Duration };
                    break;
                case "frame":
                    run = run with { Frame = RunTime(setting)?.SiValue ?? run.Frame };
                    break;
                case "steady":
                    run = run with { Steady = SteadyCircuits(setting) };
                    break;
                default:
                    overrides.Add(setting);
                    break;
            }
        }

        var events = ImmutableArray.CreateBuilder<DisturbanceSymbol>();
        var drivers = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        // After the settings, because an override is evaluated in the run's starting case.
        foreach (var setting in overrides)
        {
            Override(setting, run, events, drivers);
        }

        var start = run.Start;
        foreach (var change in block.Body.OfType<DisturbanceSyntax>())
        {
            var circuit = CircuitOf(change.Target.Component.Token.Text);
            if (Disturbance(change, circuit, range => EventTimes(range, start, run.Title)) is { } bound)
            {
                events.Add(bound);
            }
        }

        run = run with { Events = events.ToImmutable(), DriverCurves = drivers.ToImmutable() };
        ReviewRunClock(run);
        return run;
    }

    // ---- settings -----------------------------------------------------------------------------------

    private int? RunCase(ParameterSyntax setting)
    {
        var written = parse.Source.ToString(setting.Value.Span).Trim();
        var index = setting.Value is ReferenceSyntax { Parts.IsDefaultOrEmpty: true } name
            ? _scenarios.IndexOf(name.Head.Token.Text)
            : -1;

        if (index >= 0)
        {
            return index;
        }

        Report(BinderDiagnostics.UnknownDesignScenario, setting.Value.Span, ("name", written), ("names", string.Join(", ", _scenarios)));
        return null;
    }

    /// <summary>A run's <c>start</c>: a date, read as a curve's time column is (<c>D-149</c>).</summary>
    private double? RunStart(ParameterSyntax setting)
    {
        var written = setting.Value switch
        {
            DateLiteralSyntax date => date.Literal.Text,
            StringLiteralSyntax quoted => quoted.Value,
            _ => null,
        };

        if (written is not null && ReadTimestamp(written, format: null) is { } start)
        {
            return start;
        }

        Report(BinderDiagnostics.StartUnreadable, setting.Span, ("value", parse.Source.ToString(setting.Value.Span).Trim()));
        return null;
    }

    /// <summary>A positive span of time: a run's <c>duration</c> or <c>frame</c>.</summary>
    private Quantity? RunTime(ParameterSyntax setting)
    {
        if (Value(setting.Value, Dimension.Time) is not { } value)
        {
            return null;
        }

        if (value.Dimension != Dimension.Time)
        {
            Mismatch(setting, setting.Name.Text, Dimension.Time, value.Dimension);
            return null;
        }

        if (value.SiValue <= 0)
        {
            Report(BinderDiagnostics.NegativeValue, setting.Span, ("parameter", setting.Name.Text));
            return null;
        }

        return value;
    }

    /// <summary>The circuits a run holds quasi-steady, by their titles.</summary>
    private ImmutableArray<string> SteadyCircuits(ParameterSyntax setting)
    {
        IEnumerable<ExpressionSyntax> items = setting.Value is ScenarioListSyntax list
            ? list.Elements.Select(static element => element.Value)
            : [setting.Value];

        var names = ImmutableArray.CreateBuilder<string>();

        foreach (var item in items)
        {
            var name = item switch
            {
                StringLiteralSyntax quoted => quoted.Value,
                ReferenceSyntax { Parts.IsDefaultOrEmpty: true } bare => bare.Head.Token.Text,
                _ => null,
            };

            if (name is not null && _circuits.Any(circuit => string.Equals(circuit.Name, name, StringComparison.Ordinal)))
            {
                names.Add(name);
                continue;
            }

            Report(BinderDiagnostics.UnknownName, item.Span, ("name", parse.Source.ToString(item.Span).Trim()));
        }

        return names.ToImmutable();
    }

    // ---- overrides ----------------------------------------------------------------------------------

    /// <summary>An override: <c>RAD.power = 50 kW</c>, <c>outdoor = weather_jan</c>, <c>outdoor = -10 C</c>.</summary>
    private void Override(
        ParameterSyntax setting,
        RunSymbol run,
        ImmutableArray<DisturbanceSymbol>.Builder events,
        ImmutableDictionary<string, string>.Builder drivers)
    {
        var name = setting.Name;

        if (name.Parts.IsDefaultOrEmpty && name.Head.Index is null)
        {
            var let = name.Head.Name.Text;

            if (!_bindingsByName.ContainsKey(let))
            {
                Report(BinderDiagnostics.UnknownParameter, setting.Name.Span, ("kind", "run"), ("parameter", let), ("available", RunSettingList));
                return;
            }

            // A curve that runs on the clock takes the driver's place for the run (`19` §Drivers and cases).
            if (setting.Value is ReferenceSyntax { Parts.IsDefaultOrEmpty: true } curve
                && _curvesByName.ContainsKey(curve.Head.Token.Text)
                && Clocked(curve.Head.Token.Text) is not null)
            {
                drivers[let] = curve.Head.Token.Text;
                return;
            }

            events.AddRange(OverrideLet(let, setting, run.From));
            return;
        }

        // A parameter, from t = 0: the step the design solve's steady state moves off at once.
        var statement = new DisturbanceSyntax(
            new Token { Kind = TokenKind.Identifier, Text = "at", Span = new TextSpan(setting.Span.Start, 0) },
            new PointSyntax(setting.Value),
            new EndpointSyntax(name.Head.Name, name.Parts[0].Dot, new QualifiedNameSyntax(name.Parts[0].Name, name.Parts[1..])),
            setting.EqualsToken,
            setting.Value is RangeExpressionSyntax range ? new RangeSyntax(range.From, range.DotDot, range.To) : new PointSyntax(setting.Value));

        var zero = Quantity.FromSi(0, Dimension.Time);
        if (Disturbance(statement, CircuitOf(name.Head.Name.Token.Text), _ => (zero, null)) is { } bound)
        {
            events.Add(bound);
        }
    }

    /// <summary>A <c>let</c> overridden by a value: each parameter that reads it, stepped at t = 0 to what it comes to.</summary>
    /// <remarks>
    /// Walks step 5's order again over the values that read the <c>let</c>, in the run's starting case, with the
    /// <c>let</c> at the run's value, then puts every value back. A parameter that does not change is not an event.
    /// </remarks>
    private ImmutableArray<DisturbanceSymbol> OverrideLet(string let, ParameterSyntax setting, int? from)
    {
        var root = _bindingsByName[let].Id;

        if (!_pending.TryGetValue(root, out var binding) || binding.Value is not { } current
            || Value(setting.Value, current.Dimension) is not { } value)
        {
            return [];
        }

        if (value.Dimension != current.Dimension)
        {
            Mismatch(setting, let, current.Dimension, value.Dimension);
            return [];
        }

        var start = from ?? DesignCase;
        return start == DesignCase ? Overridden() : InCase(start, Overridden);

        ImmutableArray<DisturbanceSymbol> Overridden()
        {
            var readers = Readers(root);
            var saved = readers.Where(_pending.ContainsKey).ToDictionary(static id => id, id => _pending[id].Value);
            var curves = readers.OfType<ValueId.Curve>().ToDictionary(static id => id.Name, id => _curveValues.GetValueOrDefault(id.Name));
            var outer = _case;
            var rootValue = binding.Value;

            _case ??= DesignCase;
            binding.Value = value;

            foreach (var id in _order.Where(readers.Contains))
            {
                Reevaluate(id);
            }

            var events = ImmutableArray.CreateBuilder<DisturbanceSymbol>();

            foreach (var id in _order.Where(readers.Contains))
            {
                PropertyReference? target = id switch
                {
                    ValueId.ComponentParameter parameter => new PropertyReference(parameter.Component, parameter.Parameter),
                    ValueId.ScenarioParameter element when element.Scenario == start => new PropertyReference(element.Component, element.Parameter),
                    _ => null,
                };

                if (target is { } reference && _pending[id].Value is { } after && !after.Equals(saved[id]))
                {
                    var zero = Quantity.FromSi(0, Dimension.Time);
                    events.Add(new DisturbanceSymbol(CircuitOf(reference.Component), reference, zero, zero, null, after, setting.Span));
                }
            }

            binding.Value = rootValue;
            _case = outer;

            foreach (var (id, before) in saved)
            {
                _pending[id].Value = before;
            }

            foreach (var (name, before) in curves)
            {
                _curveValues[name] = before;
            }

            return events.ToImmutable();
        }
    }

    /// <summary>Everything that reads a value, directly or through a curve or another <c>let</c>.</summary>
    private HashSet<ValueId> Readers(ValueId root)
    {
        var readers = new HashSet<ValueId>();
        var reached = new HashSet<ValueId> { root };

        foreach (var id in _order)
        {
            var reads = id switch
            {
                ValueId.Curve curve => _curvesByName.TryGetValue(curve.Name, out var index) && _curves[index] is { DriverName: { } driver } symbol
                    && symbol.DriverKind switch
                    {
                        CurveDriverKind.Let => reached.Contains(new ValueId.Let(driver)),
                        CurveDriverKind.Curve => reached.Contains(new ValueId.Curve(driver)),
                        _ => false,
                    },
                _ => !id.Equals(root) && _pending.TryGetValue(id, out var pending) && pending.Dependencies.Overlaps(reached),
            };

            if (reads)
            {
                readers.Add(id);
                reached.Add(id);
            }
        }

        return readers;
    }

    /// <summary>Evaluates one value again, as step 5 did, with whatever its inputs hold now.</summary>
    private void Reevaluate(ValueId id)
    {
        if (id is ValueId.Curve curve)
        {
            EvaluateCurve(curve);
            return;
        }

        var pending = _pending[id];
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var evaluator = new ExpressionEvaluator(this, parse.Source, diagnostics, pending.Target?.Info.Dimension ?? pending.DesignRole?.Dimension);

        pending.Value = null;
        _evaluating = id;
        var result = evaluator.Evaluate(pending.Expression);
        _evaluating = null;

        _diagnostics.AddRange(diagnostics.Where(diagnostic => !Reported(diagnostic)));

        if (result is EvaluationResult.Value evaluated)
        {
            Store(pending, evaluated);
        }
    }

    // ---- times --------------------------------------------------------------------------------------

    /// <summary>An event's times: durations from t = 0, or clock times and dates when the run states its start.</summary>
    private (Quantity? From, Quantity? To) EventTimes(RangeOrPointSyntax range, double? start, string run) => range switch
    {
        PointSyntax point => (EventTime(point.Value, start, run), null),
        RangeSyntax span => (EventTime(span.From, start, run), EventTime(span.To, start, run)),
        _ => (null, null),
    };

    /// <summary>One event time as a duration from t = 0.</summary>
    /// <remarks>
    /// A clock time is the next one at or after the start: <c>06:30</c> in a run starting 2026-01-15 06:00 is
    /// 30 minutes in, and in one starting at 22:00 it is the next morning. This project's reasoning — `19` says
    /// only that a clock time needs a start.
    /// </remarks>
    private Quantity? EventTime(ExpressionSyntax expression, double? start, string run)
    {
        if (expression is not DateLiteralSyntax date)
        {
            return Value(expression, Dimension.Time);
        }

        if (start is not { } origin)
        {
            Report(Language2Diagnostics.ClockTimeWithoutStart, date.Span, ("time", date.Literal.Text), ("run", run));
            return null;
        }

        double? at = ReadTimestamp(date.Literal.Text, format: null);

        if (at is null && TimeOnly.TryParseExact(date.Literal.Text, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            var day = DateTime.UnixEpoch.AddSeconds(origin).Date;
            var candidate = (day + clock.ToTimeSpan() - DateTime.UnixEpoch).TotalSeconds;
            at = candidate < origin ? candidate + 86_400 : candidate;
        }

        if (at is not { } absolute)
        {
            Report(BinderDiagnostics.StartUnreadable, date.Span, ("value", date.Literal.Text));
            return null;
        }

        return Quantity.FromSi(absolute - origin, Dimension.Time);
    }

    /// <summary>Reports a run that follows the clock with no start to read it at (<c>FS1546</c>).</summary>
    private void ReviewRunClock(RunSymbol run)
    {
        if (run.Start is not null)
        {
            return;
        }

        var clocked = _deferred
            .SelectMany(static deferred => deferred.Dependencies.OfType<ValueId.Curve>())
            .Select(curve => ClockedInRun(curve.Name, run.DriverCurves))
            .FirstOrDefault(static name => name is not null);

        if (clocked is not null)
        {
            Report(BinderDiagnostics.ClockWithoutStart, run.Span, ("curve", clocked));
        }
    }

    /// <summary>The curve of time a curve's chain reaches in a run, its drivers re-pointed as the run says.</summary>
    private string? ClockedInRun(string name, ImmutableDictionary<string, string> drivers)
    {
        for (var depth = 0; depth <= _curves.Count && _curvesByName.TryGetValue(name, out var index); depth++)
        {
            var curve = _curves[index];

            switch (curve.DriverKind)
            {
                case CurveDriverKind.Time:
                    return curve.Name;
                case CurveDriverKind.Curve when curve.DriverName is { } driver:
                    name = driver;
                    continue;
                case CurveDriverKind.Let when curve.DriverName is { } driver && drivers.TryGetValue(driver, out var clock):
                    name = clock;
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }
}
