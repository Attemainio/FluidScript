using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Sizing;

/// <summary>Chooses a pump's head from the resistance of the circuit it drives (<c>24</c>).</summary>
/// <remarks>
/// <para>
/// <strong>Head is the loop's drop at the design flow, and the design flow is not the pump's to
/// choose.</strong> It comes from a duty — a heat exchanger with a power and two temperatures fixes it
/// through an energy balance — and the pump is then sized <em>to</em> it. That ordering is what makes
/// the rule meaningful: sizing a pump against the flow it is itself producing is circular, and at a
/// converged solve it is worse than circular, because the field balances whatever head the pump was
/// given and reading the rise back returns the number it started with.
/// </para>
/// <para>
/// <strong>The conversion uses the density at the pump's own inlet, not the loop mean</strong>, and
/// <c>24</c> calls that out because it is otherwise read as an error. A pump develops head against the
/// fluid actually entering it: the worked example's 51.7 kPa at 998.2 kg/m³ is 5.28 m, and the same
/// drop at the loop's 35 °C mean of 994 kg/m³ is 5.30 m. Under half a percent on that circuit, and it
/// grows with the loop's temperature spread.
/// </para>
/// <para>
/// <strong>No hidden safety margin.</strong> <c>margin</c> is an explicit parameter defaulting to 1.0,
/// it appears in the basis string, and it multiplies only an auto-sized head. It is a deliberate design
/// allowance, not a stand-in for fittings nobody modelled — those are a pipe's <c>minor_loss</c>
/// (<c>D-25</c>).
/// </para>
/// </remarks>
public sealed class PumpSizer : ISizer
{
    /// <inheritdoc/>
    public ImmutableArray<string> Parameters { get; } = ["head"];

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing, deliberately. A pump's constructor already accepts a head of its own, so lowering can
    /// build one before anything is sized — unlike a pipe, which has no component at all without a
    /// bore. That keeps <c>head</c> claimed by no map on the bootstrap pass, which is what leaves it
    /// available for promotion when a stated constraint needs somewhere to go.
    /// </remarks>
    public ImmutableDictionary<string, Quantity> Provisional => [];

    /// <inheritdoc/>
    public bool CanSize(IFlowComponent component) => component is Pump;

    /// <inheritdoc/>
    public Result<SizingResult> Size(IFlowComponent component, in SizingContext context)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component is not Pump pump)
        {
            return Result.Failure<SizingResult>(ResultError.From(
                Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a head"),
                ("name", component.Name),
                ("state", "a component that is not a pump")));
        }

        // A stated rise is the pump's head by another name (C-109): nothing to choose.
        if (pump.StatedRise is not null)
        {
            return Result.Success(new SizingResult { Values = ImmutableDictionary<string, SizedValue>.Empty, Notes = [] });
        }

        var density = context.State.Density.SiValue;
        var drop = context.LoopDrop;

        if (!double.IsFinite(density) || density <= 0 || (drop is { } pascals && !double.IsFinite(pascals)))
        {
            return Result.Failure<SizingResult>(ResultError.From(
                Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a head"),
                ("name", pump.Name),
                ("state", "the circuit's resistance is not yet known")));
        }

        var margin = Margin(component);
        var head = Math.Max(0, drop ?? 0) / (density * UnitTable.StandardGravity) * margin;
        var litresPerSecond = Math.Abs(context.MassFlow) / density * 1000;
        var notes = ImmutableArray.CreateBuilder<string>();
        var raised = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        // A note and its code say the same thing (C-74): the note for the explanation, the code for
        // the wire, anchored to the pump.
        void Say(Diagnostics.DiagnosticDescriptor descriptor)
        {
            var diagnostic = Diagnostics.Diagnostic.Create(descriptor, span: null, new Diagnostics.DiagnosticArgument("name", pump.Name)) with { ComponentName = pump.Name };
            raised.Add(diagnostic);
            notes.Add(diagnostic.Message);
        }

        // `FS2312`'s case, and it is three cases wearing one number. A pump on no loop, a loop nothing
        // drives, and a loop of ideal links all size to zero head; they are a missing connection, a
        // missing duty, and a missing loss, and a report that says "no resistance" to all three sends two
        // readers out of three to the wrong line of their script (`C-57`).
        if (drop is null)
        {
            notes.Add(
                $"{pump.Name} sized to zero head because it is on no closed circuit. Connect it into a "
                + "loop; a pump that returns to nothing has nothing to develop head against.");
        }
        else if (drop <= 0)
        {
            if (context.MassFlow == 0)
            {
                Say(Diagnostics.SizingDiagnostics.NothingToSizeAgainst);
            }
            else
            {
                Say(Diagnostics.SizingDiagnostics.NoModelledResistance);
            }
        }

        // Stated only when it is not 1, because a margin of one is the absence of a margin and saying
        // so in every basis string would train a reader to stop reading the clause that matters.
        var allowance = margin == 1
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", margin {margin:0.##}");

        var resistance = drop is { } total
            ? string.Create(CultureInfo.InvariantCulture, $"loop drop {total / 1000:0.#} kPa")
            : "no closed circuit";

        var basis = string.Create(
            CultureInfo.InvariantCulture,
            $"{head:0.##} m at {litresPerSecond:0.###} l/s — {resistance}");

        return Result.Success(new SizingResult
        {
            Values = ImmutableDictionary<string, SizedValue>.Empty.Add(
                "head",
                new SizedValue(
                    Quantity.FromSi(head, Dimension.Head), basis + allowance, FromDefault: false)),
            Notes = notes.ToImmutable(),
            Diagnostics = raised.ToImmutable(),
        });
    }

    /// <summary>The design allowance on an auto-sized head.</summary>
    /// <param name="component">The pump.</param>
    /// <returns>A multiplier, 1.0 when the script stated none.</returns>
    /// <remarks>
    /// Read from the component rather than from the default catalogue, so a stated <c>margin=1.1</c>
    /// reaches the head and a reader can see in the basis string that it did.
    /// </remarks>
    private static double Margin(IFlowComponent component) =>
        component.StatedParameters.TryGetValue("margin", out var stated) ? stated.SiValue
        : component.DefaultParameters.TryGetValue("margin", out var defaulted) ? defaulted.SiValue
        : 1.0;
}
