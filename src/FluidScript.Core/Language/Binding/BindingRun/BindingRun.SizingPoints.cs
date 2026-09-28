using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>The per-component sizing point (<c>D-94</c>): a curve read where the component says, not where the file does.</summary>
/// <remarks>
/// <para>
/// The driver <c>let</c>'s value in the operating case, <c>let outdoor = [-26, 5] C</c> at the first case, is the
/// file's sizing point and, in a static solve, its operating point (<c>D-58</c>, <c>D-143</c>). A component may
/// name its own with <c>sized_at.outdoor = -5 C</c> (<c>D-175</c>), and every curve one of its parameters reads
/// is then evaluated at −5 instead. The value it takes is its <em>capacity</em>:
/// below the point it is written at, the machine is flat out and the rest of the plant carries the
/// difference, which is what a bivalent heat pump and its backup boiler are. In a static solve at the
/// design day that capacity is also what it delivers, so one number serves both jobs here as well.
/// </para>
/// <para>
/// <strong>It is the same machinery as the operating case with a narrower scope.</strong> The values are
/// pending expressions with the driver's dimension, so <c>sized_at.outdoor = 3 bar</c> is a dimension
/// mismatch; a curve is read at the override when its driver has one, at the file's design value otherwise,
/// and through a driving curve recursively — the chain <c>outdoor → supply → heating</c> is walked at −5 end
/// to end. Nothing here changes a curve's stored
/// value: the override lives at the reference, so two components reading one curve at two points
/// each get their own number.
/// </para>
/// <para>
/// <strong>The fraction is reported, never stated.</strong> A heat pump "sized to 60 % of peak" is the
/// outcome of choosing a bivalent point on a heating curve, and the number an engineer checks is the
/// point, not the percentage. So the parameter's basis reads <em>30 kW at outdoor=−5, 0.6 of the 50 kW
/// the design day asks</em> — both values from the same expression, evaluated twice.
/// </para>
/// </remarks>
internal sealed partial class BindingRun
{
    // Each component's own driver values, keyed by component then by canonical driver.
    private readonly Dictionary<string, Dictionary<string, DesignValue>> _sizingPoints = new(StringComparer.Ordinal);

    // The value being evaluated, so a curve reference can tell whose parameter is reading it.
    private ValueId? _evaluating;

    // Set while re-evaluating a parameter at the file's design point for its basis line.
    private bool _atDesignDay;

    // Set while evaluating a parameter at its component's own point, for its capacity (`D-175`).
    private bool _atSizingPoint;

    // Each parameter's capacity, the design case's: the first evaluation stores it, and later cases read the same point.
    private readonly Dictionary<ValueId, Quantity> _capacities = [];

    // Set when an evaluation at a sizing point actually read the point, directly or through a curve: a capacity
    // is a value the point changed, and a parameter that never reads it has none.
    private bool _pointRead;

    /// <summary>Binds a declaration's <c>sized_at</c> clause, if it wrote one.</summary>
    /// <param name="declaration">The declaration.</param>
    /// <param name="componentName">Its name, already known to be unique.</param>
    /// <param name="parameters">The parameters it stated, whose values will be read at this point.</param>
    private void DeclareSizingPoint(
        ComponentDeclarationSyntax declaration,
        string componentName,
        ImmutableDictionary<string, ParameterValue> parameters)
    {
        if (declaration.SizingPoint.IsDefaultOrEmpty)
        {
            return;
        }

        var point = new Dictionary<string, DesignValue>(StringComparer.Ordinal);

        foreach (var argument in declaration.SizingPoint)
        {
            // The driver is the `let` the point names, by its exact spelling (`D-170`, `D-175`).
            var written = argument.Name.Text;
            var key = written;

            if (point.TryGetValue(key, out var existing))
            {
                Report(
                    BinderDiagnostics.DuplicateBinding,
                    argument.Span,
                    ("name", written),
                    ("line", LineOf(existing.Span)));
                continue;
            }

            var id = new ValueId.SizingPoint(componentName, key);
            _graph.Add(id);
            _pending[id] = new PendingValue(argument.Value, id, argument.Span, null) { IsDesign = true };

            point[key] = new DesignValue(written, null, null, argument.Span);

            // Every parameter of the component may read a curve this value positions, so each is
            // ordered after it. The edges cost nothing when a parameter reads no curve.
            foreach (var canonical in parameters.Keys)
            {
                _graph.AddDependency(new ValueId.ComponentParameter(componentName, canonical), id);
            }
        }

        _sizingPoints[componentName] = point;
    }

    /// <summary>A curve's value as the value under evaluation should read it.</summary>
    /// <param name="name">The curve's name.</param>
    /// <returns>The value, or <see langword="null"/> when nothing positions it yet.</returns>
    /// <remarks>
    /// The file's design value unless the reading parameter belongs to a component with its own
    /// sizing point, in which case the curve is walked afresh with that point's overrides. The basis
    /// pass sets <see cref="_atDesignDay"/> to read the same expression at the file's point.
    /// </remarks>
    private double? CurveValueFor(string name) => CurveValueSeenBy(_atDesignDay ? null : _evaluating, name);

    /// <summary>A curve's value as one particular reader sees it.</summary>
    /// <param name="reader">The value reading the curve, or <see langword="null"/> for the file's own point.</param>
    /// <param name="name">The curve's name.</param>
    /// <remarks>
    /// A component with its own point reads the curve there only for its capacity (<c>D-175</c>); its value in a case
    /// is the curve as the case reads it, held to that capacity afterwards (<see cref="HeldToCapacity"/>). Where the
    /// file gives the curve no value at all, the component's own point is all there is to read it at.
    /// </remarks>
    private double? CurveValueSeenBy(ValueId? reader, string name)
    {
        if (reader is not ValueId.ComponentParameter parameter
            || !_sizingPoints.TryGetValue(parameter.Component, out var point)
            || point.Count == 0)
        {
            return _curveValues.GetValueOrDefault(name);
        }

        return _atSizingPoint
            ? CurveAt(name, parameter.Component, point, depth: 0)
            : _curveValues.GetValueOrDefault(name) ?? CurveAt(name, parameter.Component, point, depth: 0);
    }

    /// <summary>A parameter's value held to its capacity, the value it takes at its component's own point (<c>D-175</c>).</summary>
    /// <param name="pending">The parameter.</param>
    /// <param name="value">Its value in the case being evaluated.</param>
    /// <returns>The value, or the capacity with the value's sign when the value is larger in magnitude.</returns>
    /// <remarks>
    /// In magnitude, so a heating curve and a cooling one are held alike: a heat pump sized at −5 °C gives the
    /// curve's 27.2 kW on a −26 °C day and the whole of a 5 °C day's 16.3 kW. <c>D-94</c> read the curve at the point
    /// in every case, which is right on the design day only; above the bivalence point a heat pump turns down.
    /// </remarks>
    private Quantity HeldToCapacity(PendingValue pending, Quantity value)
    {
        if (pending.Id is not ValueId.ComponentParameter parameter
            || !_sizingPoints.TryGetValue(parameter.Component, out var point)
            || point.Count == 0
            || !pending.Dependencies.Any(static dependency => dependency is ValueId.Curve or ValueId.Let)
            || AtSizingPoint(pending) is not { } capacity
            || capacity.Dimension != value.Dimension)
        {
            return value;
        }

        _capacities.TryAdd(pending.Id, capacity);

        return Math.Abs(value.SiValue) > Math.Abs(capacity.SiValue)
            ? Quantity.FromSi(Math.CopySign(capacity.SiValue, value.SiValue), value.Dimension)
            : value;
    }

    /// <summary>A parameter's expression evaluated at its component's own point, in the parameter's dimension.</summary>
    private Quantity? AtSizingPoint(PendingValue pending)
    {
        _atSizingPoint = true;
        _pointRead = false;
        _evaluating = pending.Id;

        try
        {
            var evaluator = new ExpressionEvaluator(
                this,
                parse.Source,
                ImmutableArray.CreateBuilder<Diagnostic>(),
                pending.Target?.Info.Dimension);

            if (evaluator.Evaluate(pending.Expression) is not EvaluationResult.Value at || !_pointRead)
            {
                return null;
            }

            return at.IsBare && pending.Target is { Info.ValueKind: ParameterValueKind.Quantity } target
                ? Quantity.FromBareNumber(at.Quantity.SiValue, target.Info.Dimension)
                : at.Quantity;
        }
        finally
        {
            _atSizingPoint = false;
            _evaluating = null;
        }
    }

    /// <summary>The value a component's own point gives a <c>let</c> it names, while that component's capacity is read.</summary>
    /// <param name="name">The <c>let</c>'s name.</param>
    /// <returns>The point's value, a bare number in the <c>let</c>'s own unit; <see langword="null"/> outside a capacity read.</returns>
    private Quantity? PointValueFor(string name)
    {
        if (!_atSizingPoint
            || _evaluating is not ValueId.ComponentParameter parameter
            || !_sizingPoints.TryGetValue(parameter.Component, out var point))
        {
            return null;
        }

        var key = point.ContainsKey(name) ? name : null;
        if (key is null
            || !point.ContainsKey(key)
            || !_pending.TryGetValue(new ValueId.SizingPoint(parameter.Component, key), out var given)
            || given.Value is not { } value
            || !_bindingsByName.TryGetValue(name, out var slot)
            || !_pending.TryGetValue(slot.Id, out var let)
            || let.Value is not { } current)
        {
            return null;
        }

        _pointRead = true;

        // A bare point is written in the `let`'s own unit, as a curve's rows are.
        return value.Dimension == Dimension.Dimensionless && current.Dimension != Dimension.Dimensionless
            && LetUnit(let, current.Dimension) is { } unit
                ? Quantity.FromUnit(value.SiValue, unit)
                : value;
    }

    /// <summary>A <c>let</c> that reads a component's own point further down its chain, evaluated again under the point (<c>L-74</c>).</summary>
    /// <param name="let">The <c>let</c> read.</param>
    /// <returns>
    /// Its value at the point, or <see langword="null"/> outside a capacity read and for a <c>let</c> whose chain reads
    /// neither a named <c>let</c> nor a curve, whose value in the case is already the value at the point.
    /// </returns>
    /// <remarks>
    /// <c>let share = demand * 1</c> is <c>demand</c>'s case value times one when stored; a heat pump whose
    /// <c>power = share  sized_at.demand = 27.2 kW</c> has to see 27.2 kW through it, as it does when it reads
    /// <c>demand</c> directly. The component being read is still <see cref="_evaluating"/>, so the <c>let</c> named by the
    /// point and every curve down the chain are read at the point, as for the parameter's own expression.
    /// </remarks>
    private Quantity? PointValueThrough(ValueId let)
    {
        if (!_atSizingPoint
            || _evaluating is not ValueId.ComponentParameter parameter
            || !_sizingPoints.TryGetValue(parameter.Component, out var point)
            || !_pending.TryGetValue(let, out var pending)
            || !ReachesPoint(pending, point, depth: 0))
        {
            return null;
        }

        var expression = _caseExpressions.TryGetValue(let, out var cases) ? cases[_case ?? DesignCase] : pending.Expression;
        var evaluator = new ExpressionEvaluator(this, parse.Source, ImmutableArray.CreateBuilder<Diagnostic>());

        return evaluator.Evaluate(expression) is EvaluationResult.Value value ? value.Quantity : null;
    }

    /// <summary>Whether a value reads a <c>let</c> the point names, or a curve, directly or through other <c>let</c>s.</summary>
    private bool ReachesPoint(PendingValue pending, Dictionary<string, DesignValue> point, int depth) =>
        // A cycle is already `FS1402`; the depth only has to stop one.
        depth <= _pending.Count
        && pending.Dependencies.Any(dependency => dependency switch
        {
            ValueId.Let { Name: var name } when point.ContainsKey(name) => true,
            ValueId.Curve => true,
            ValueId.Let other => _pending.TryGetValue(other, out var next) && ReachesPoint(next, point, depth + 1),
            _ => false,
        });

    /// <summary>The number a point is reported at: in the unit its <c>let</c> is written in.</summary>
    private double? PointNumber(string component, string key)
    {
        var id = new ValueId.SizingPoint(component, key);

        if (!_pending.TryGetValue(id, out var given) || given.Value is not { } value)
        {
            return null;
        }

        // A `let` gives its own unit; a name no `let` has, the value's own dimension's.
        return _bindingsByName.ContainsKey(key)
            ? LetNumber(key, value)
            : UnitTable.CanonicalUnitFor(value.Dimension) is { } unit ? value.ValueIn(unit) : value.SiValue;
    }

    /// <summary>Warns of a sizing point none of a component's parameters read (<c>FS1549</c>).</summary>
    private void ReviewSizingPoints()
    {
        foreach (var (component, point) in _sizingPoints)
        {
            if (point.Count == 0 || _capacities.Keys.Any(id => id is ValueId.ComponentParameter parameter && parameter.Component == component))
            {
                continue;
            }

            // A point in another dimension than its `let` is that mismatch, not a point nothing reads.
            var mismatched = false;
            foreach (var (key, entry) in point)
            {
                if (_bindingsByName.TryGetValue(key, out var slot)
                    && _pending.TryGetValue(slot.Id, out var let) && let.Value is { } current
                    && _pending.TryGetValue(new ValueId.SizingPoint(component, key), out var given) && given.Value is { } value
                    && value.Dimension != Dimension.Dimensionless && value.Dimension != current.Dimension)
                {
                    Report(
                        BinderDiagnostics.ParameterDimensionMismatch,
                        entry.Span,
                        ("parameter", $"sized_at.{entry.WrittenName}"),
                        ("expected", BinderDiagnostics.Expected(current.Dimension)),
                        ("value", parse.Source.ToString(given.Expression.Span).Trim()),
                        ("actual", BinderDiagnostics.Phrase(value.Dimension)));
                    mismatched = true;
                }
            }

            if (mismatched)
            {
                continue;
            }

            var first = point.Values.First();
            Report(
                BinderDiagnostics.SizingPointUnread,
                first.Span,
                ("component", component),
                ("point", string.Join(" ", point.Select(entry => $"{entry.Value.WrittenName}={FormatNumber(PointNumber(component, entry.Key))}"))),
                ("driver", first.WrittenName));
        }
    }

    /// <summary>The capacities of one component's parameters, by canonical name.</summary>
    private ImmutableDictionary<string, Quantity> PublishCapacities(string componentName) =>
        _capacities
            .Where(entry => entry.Key is ValueId.ComponentParameter parameter && parameter.Component == componentName)
            .ToImmutableDictionary(static entry => ((ValueId.ComponentParameter)entry.Key).Parameter, static entry => entry.Value);

    private double? CurveAt(string name, string component, Dictionary<string, DesignValue> point, int depth)
    {
        // A cycle among curves is already `FS1402`; this only has to not recurse forever on one.
        if (depth > _curves.Count || !_curvesByName.TryGetValue(name, out var index))
        {
            return null;
        }

        var curve = _curves[index];

        if (curve.Points.IsEmpty || curve.DriverName is not { } driver)
        {
            return null;
        }

        // A point is keyed by the `let` it names (`D-175`), which is the driver the curve names.
        if (point.ContainsKey(driver))
        {
            _pointRead = true;
        }

        double? x = point.ContainsKey(driver)
            ? curve.DriverKind == CurveDriverKind.Let
                && _pending.TryGetValue(new ValueId.SizingPoint(component, driver), out var given) && given.Value is { } value
                    ? LetNumber(driver, value)
                    : null
            : curve.DriverKind == CurveDriverKind.Curve
                ? CurveAt(driver, component, point, depth + 1)
                : curve.DriverKind == CurveDriverKind.Let
                    ? LetNumber(driver)
                    : null;

        return x is { } at ? curve.Evaluate(at) : null;
    }

    /// <summary>The published sizing point of one component, values filled in.</summary>
    private ImmutableDictionary<string, DesignValue> PublishSizingPoint(string componentName)
    {
        if (!_sizingPoints.TryGetValue(componentName, out var point))
        {
            return ImmutableDictionary<string, DesignValue>.Empty;
        }

        var published = ImmutableDictionary.CreateBuilder<string, DesignValue>(StringComparer.Ordinal);

        foreach (var (key, entry) in point)
        {
            var id = new ValueId.SizingPoint(componentName, key);
            _pending.TryGetValue(id, out var pending);
            published[key] = entry with { Value = pending?.Value, Number = PointNumber(componentName, key) };
        }

        return published.ToImmutable();
    }

    /// <summary>Explains a parameter read at a component's own sizing point, against the design day.</summary>
    /// <param name="componentName">The component.</param>
    /// <param name="pending">The parameter's pending value, already evaluated at the component's point.</param>
    /// <returns>The basis line, or <see langword="null"/> when the parameter read no curve or has no value.</returns>
    private string? SizingBasis(string componentName, PendingValue pending)
    {
        if (!_sizingPoints.TryGetValue(componentName, out var point)
            || point.Count == 0
            || pending.Value is not { } sized
            || !_capacities.ContainsKey(pending.Id))
        {
            return null;
        }

        var at = string.Join(
            " ",
            point.Select(entry =>
                $"{entry.Value.WrittenName}={FormatNumber(PointNumber(componentName, entry.Key))}"));

        // The capacity, which is what the point sets; the design day's value is held to it and may be smaller.
        sized = _capacities.GetValueOrDefault(pending.Id, sized);
        var unit = UnitTable.CanonicalUnitFor(sized.Dimension);
        var shown = Format(unit is null ? sized.SiValue : sized.ValueIn(unit), unit?.Text);

        // The same expression at the file's point, for the ratio an engineer wants to see.
        _atDesignDay = true;
        _evaluating = pending.Id;

        try
        {
            var evaluator = new ExpressionEvaluator(
                this,
                parse.Source,
                ImmutableArray.CreateBuilder<Diagnostic>(),
                pending.Target?.Info.Dimension);

            if (evaluator.Evaluate(pending.Expression) is EvaluationResult.Value design
                && pending.Target is { } target)
            {
                var quantity = design.IsBare && target.Info.ValueKind == ParameterValueKind.Quantity
                    ? Quantity.FromBareNumber(design.Quantity.SiValue, target.Info.Dimension)
                    : design.Quantity;
                var peak = unit is null ? quantity.SiValue : quantity.ValueIn(unit);

                if (Math.Abs(peak) > 0 && quantity.Dimension == sized.Dimension)
                {
                    var fraction = (unit is null ? sized.SiValue : sized.ValueIn(unit)) / peak;

                    return $"{shown} at {at}, {fraction.ToString("0.##", CultureInfo.InvariantCulture)} of the "
                        + $"{Format(peak, unit?.Text)} the design day asks";
                }
            }
        }
        finally
        {
            _atDesignDay = false;
            _evaluating = null;
        }

        return $"{shown} at {at}";
    }

    private static string FormatNumber(double? value) =>
        value is { } number ? number.ToString("0.###", CultureInfo.InvariantCulture) : "?";
}
