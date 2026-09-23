using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Topology.Construction;

/// <summary>Reads every curve of time at a run's clock, and every parameter that follows one (<c>D-149</c>).</summary>
/// <remarks>
/// <para>
/// <strong>t = 0 is the start the project line states.</strong> A time curve's <c>x</c> is Unix seconds
/// (<c>D-60</c>) and a run's <c>t</c> counts from zero, so the curve is read at <see cref="Start"/> + t.
/// The alternative, the earliest row of the earliest curve, was rejected by the user: adding a curve with
/// an earlier row would silently move every other one.
/// </para>
/// <para>
/// A parameter follows the clock when the binder deferred it for a curve in a dynamic circuit
/// (<c>BindingRun.ReviewCurveReferences</c>) and the chain of curves it reads reaches <c>time</c>. Its
/// expression is evaluated again here, by the binder's own evaluator, so <c>D-14</c>'s bare-number rule
/// and every unit check hold exactly as they did at binding. A chain ending at a role or a design value
/// has no clock and keeps its design value.
/// </para>
/// <para>
/// Not on the residual path: the run applies drives once per evaluation time, beside its schedule, so
/// allocation here is not <c>22</c>'s concern.
/// </para>
/// </remarks>
public sealed class CurveClock
{
    private readonly ImmutableDictionary<string, CurveSymbol> _curves;
    private readonly ImmutableDictionary<string, BindingSymbol> _bindings;
    private readonly SourceText _source;

    /// <summary>Creates a clock over one model's curves and bindings.</summary>
    /// <param name="start">s since the Unix epoch at t = 0.</param>
    /// <param name="drives">The parameters that follow it.</param>
    /// <param name="curves">Every curve in the file.</param>
    /// <param name="bindings">Every <c>let</c>, for a drive that reads a curve through one.</param>
    /// <param name="source">The text the expressions were parsed from.</param>
    public CurveClock(
        double start,
        ImmutableArray<CurveDrive> drives,
        ImmutableArray<CurveSymbol> curves,
        ImmutableArray<BindingSymbol> bindings,
        SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Start = start;
        Drives = drives;
        _curves = curves.ToImmutableDictionary(static curve => curve.Name, StringComparer.Ordinal);
        _bindings = bindings.ToImmutableDictionary(static binding => binding.Name, StringComparer.Ordinal);
        _source = source;
    }

    /// <summary>Gets where t = 0 sits on every time curve.</summary>
    /// <value>s since the Unix epoch.</value>
    public double Start { get; }

    /// <summary>Gets the parameters that follow the clock.</summary>
    public ImmutableArray<CurveDrive> Drives { get; }

    /// <summary>Tells whether a curve's chain of drivers reaches <c>time</c>.</summary>
    /// <param name="curves">Every curve in the file.</param>
    /// <param name="name">A curve's name.</param>
    /// <returns><see langword="true"/> when some curve on the chain is driven by the clock.</returns>
    public static bool IsClocked(ImmutableArray<CurveSymbol> curves, string name)
    {
        var byName = curves.ToDictionary(static curve => curve.Name, StringComparer.Ordinal);

        for (var depth = 0; depth <= byName.Count && byName.TryGetValue(name, out var curve); depth++)
        {
            if (curve.DriverKind == CurveDriverKind.Time)
            {
                return true;
            }

            if (curve.DriverKind != CurveDriverKind.Curve || curve.DriverName is not { } driver)
            {
                return false;
            }

            name = driver;
        }

        return false;
    }

    /// <summary>Evaluates one drive at a time on the run's clock.</summary>
    /// <param name="drive">The drive.</param>
    /// <param name="time">s from t = 0.</param>
    /// <returns>
    /// The parameter's value in its own SI unit, signed as the solver carries it, or
    /// <see langword="null"/> when the expression reads something the clock does not supply, in which
    /// case the run leaves the parameter where it was.
    /// </returns>
    public double? ValueAt(CurveDrive drive, double time)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var evaluator = new ExpressionEvaluator(
            new ClockScope(this, time, depth: 0), _source, ImmutableArray.CreateBuilder<Diagnostic>(), drive.Expected);

        if (evaluator.Evaluate(drive.Expression) is not EvaluationResult.Value value)
        {
            return null;
        }

        var assigned = value.Quantity;

        // A curve's `y` is bare, and a bare number takes the parameter's canonical unit, as the binder
        // reads it (`D-14`): `heating`'s 30 is thirty kilowatts on a `power`.
        if (drive.Expected is { } dimension)
        {
            if (value.IsBare)
            {
                assigned = Quantity.FromBareNumber(value.Quantity.SiValue, dimension);
            }
            else if (!Quantity.TryAssign(value.Quantity, dimension, out assigned))
            {
                return null;
            }
        }

        var si = assigned.SiValue;

        return !double.IsFinite(si)
            ? null
            : string.Equals(drive.Parameter, "power", StringComparison.Ordinal)
                ? ExchangerRoles.Duty(drive.WrittenKind, si)
                : si;
    }

    /// <summary>The next row of any time curve strictly after a time on the run's clock.</summary>
    /// <param name="time">s from t = 0.</param>
    /// <returns>s from t = 0, or <see cref="double.PositiveInfinity"/> when every row is behind it.</returns>
    /// <remarks>Only a curve driven by <c>time</c> itself: a chained curve's rows are on its driver's axis, not the clock's.</remarks>
    public double NextRow(double time)
    {
        var next = double.PositiveInfinity;

        foreach (var curve in _curves.Values)
        {
            if (curve.DriverKind != CurveDriverKind.Time)
            {
                continue;
            }

            foreach (var point in curve.Points)
            {
                var at = point.X - Start;

                if (at > time)
                {
                    next = Math.Min(next, at);
                    break;
                }
            }
        }

        return next;
    }

    /// <summary>Reads a curve at the clock, walking its chain of drivers back to <c>time</c>.</summary>
    /// <returns>The curve's bare <c>y</c>, or <see langword="null"/> when the chain does not reach the clock.</returns>
    private double? CurveAt(string name, double time, int depth)
    {
        if (depth > _curves.Count || !_curves.TryGetValue(name, out var curve))
        {
            return null;
        }

        return curve.DriverKind switch
        {
            CurveDriverKind.Time => curve.Evaluate(Start + time),
            CurveDriverKind.Curve when curve.DriverName is { } driver && CurveAt(driver, time, depth + 1) is { } x =>
                curve.Evaluate(x),
            _ => null,
        };
    }

    /// <summary>The names a drive's expression may read at one instant.</summary>
    private sealed class ClockScope(CurveClock clock, double time, int depth) : IValueScope
    {
        public ScopeLookup Lookup(ReferenceSyntax reference)
        {
            ArgumentNullException.ThrowIfNull(reference);

            var head = reference.Head.Token.Text;

            if (!reference.Parts.IsDefaultOrEmpty)
            {
                // A component's solved value is not the clock's to supply: such a drive holds.
                return new ScopeLookup.Deferred(new ValueId.ComponentProperty(head, reference.PropertyPath()));
            }

            if (clock._curves.ContainsKey(head))
            {
                var curve = new ValueId.Curve(head);

                return clock.CurveAt(head, time, 0) is { } y
                    ? new ScopeLookup.Value(Quantity.FromSi(y, Dimension.Dimensionless), IsBare: true, curve)
                    : new ScopeLookup.Deferred(curve);
            }

            if (!clock._bindings.TryGetValue(head, out var binding))
            {
                return new ScopeLookup.UnknownName(null);
            }

            // A `let` that reads a curve is read again at this instant; one that does not keeps the
            // value the binder gave it. The depth bounds a chain of lets, which the binder has already
            // refused to be cyclic.
            if (depth < clock._bindings.Count)
            {
                var evaluator = new ExpressionEvaluator(
                    new ClockScope(clock, time, depth + 1), clock._source, ImmutableArray.CreateBuilder<Diagnostic>(), binding.Dimension);

                if (evaluator.Evaluate(binding.Expression) is EvaluationResult.Value value)
                {
                    return new ScopeLookup.Value(value.Quantity, value.IsBare, binding.Id);
                }
            }

            return binding.Value is { } held
                ? new ScopeLookup.Value(held, IsBare: false, binding.Id)
                : new ScopeLookup.Deferred(binding.Id);
        }
    }
}
