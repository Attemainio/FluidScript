using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;
using FluidScript.Core.Units;

namespace FluidScript.Core.Binding;

/// <summary>The per-component sizing point (<c>D-94</c>): a curve read where the component says, not where the file does.</summary>
/// <remarks>
/// <para>
/// <c>design tout=-26</c> is the file's sizing point and, in a static solve, its operating point
/// (<c>D-58</c>). A component may name its own with <c>sized_at tout=-5</c>, and every curve one of
/// its parameters reads is then evaluated at −5 instead. The value it takes is its <em>capacity</em>:
/// below the point it is written at, the machine is flat out and the rest of the plant carries the
/// difference, which is what a bivalent heat pump and its backup boiler are. In a static solve at the
/// design day that capacity is also what it delivers, so one number serves both jobs here as well.
/// </para>
/// <para>
/// <strong>It is the same machinery as <c>design</c> with a narrower scope.</strong> The values are
/// pending expressions with the driver's role as their dimension, so <c>sized_at tout=3 bar</c> is the
/// same mismatch <c>design tout=3 bar</c> is; a curve is read at the override when its driver has one,
/// at the file's design value otherwise, and through a driving curve recursively — the chain
/// <c>tout → outdoor → heating</c> is walked at −5 end to end. Nothing here changes a curve's stored
/// value: the override lives at the reference, so two components reading one curve at two points
/// each get their own number.
/// </para>
/// <para>
/// <strong>The fraction is reported, never stated.</strong> A heat pump "sized to 60 % of peak" is the
/// outcome of choosing a bivalent point on a heating curve, and the number an engineer checks is the
/// point, not the percentage. So the parameter's basis reads <em>30 kW at tout=−5, 0.6 of the 50 kW
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
            var written = argument.Name.Text;
            var role = ScheduleRoleRegistry.Resolve(written);
            var key = role?.CanonicalName ?? written;

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
            _pending[id] = new PendingValue(argument.Value, id, argument.Span, null)
            {
                DesignRole = role,
                IsDesign = true,
            };

            point[key] = new DesignValue(written, role, null, null, argument.Span);

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
    private double? CurveValueSeenBy(ValueId? reader, string name)
    {
        if (reader is not ValueId.ComponentParameter parameter
            || !_sizingPoints.TryGetValue(parameter.Component, out var point)
            || point.Count == 0)
        {
            return _curveValues.GetValueOrDefault(name);
        }

        return CurveAt(name, parameter.Component, point, depth: 0);
    }

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

        var key = curve.DriverRole?.CanonicalName ?? driver;

        double? x = point.TryGetValue(key, out var stated)
            ? Number(new ValueId.SizingPoint(component, key), stated.Role)
            : curve.DriverKind == CurveDriverKind.Curve
                ? CurveAt(driver, component, point, depth + 1)
                : DesignNumber(key);

        return x is { } at ? curve.Evaluate(at) : null;
    }

    /// <summary>Reads one pending design-like value as the bare number a curve's table is written in.</summary>
    private double? Number(ValueId id, ScheduleRole? role)
    {
        if (!_pending.TryGetValue(id, out var pending) || pending.Value is not { } quantity)
        {
            return null;
        }

        return role?.Dimension is { } dimension && UnitTable.CanonicalUnitFor(dimension) is { } unit
            ? quantity.ValueIn(unit)
            : quantity.SiValue;
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
            published[key] = entry with { Value = pending?.Value, Number = Number(id, entry.Role) };
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
            || !pending.Dependencies.Any(static dependency => dependency is ValueId.Curve))
        {
            return null;
        }

        var at = string.Join(
            " ",
            point.Select(entry =>
                $"{entry.Value.WrittenName}={FormatNumber(Number(new ValueId.SizingPoint(componentName, entry.Key), entry.Value.Role))}"));

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
