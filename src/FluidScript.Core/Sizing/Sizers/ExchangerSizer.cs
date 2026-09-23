using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Units;

namespace FluidScript.Core.Sizing;

/// <summary>Pairs an exchanger's design pressure drop with the flow it is measured at (<c>24</c>).</summary>
/// <remarks>
/// <para>
/// <strong>A pressure drop is not a resistance until something says at what flow.</strong>
/// <see cref="HeatExchanger"/> holds <c>Δp = dp·(ṁ/ṁ_design)²</c>, so a stated or defaulted <c>dp</c>
/// is only half a law — the other half is the design flow, and a script never writes it. It is the
/// flow the circuit runs at, which is what the outer loop already knows.
/// </para>
/// <para>
/// <strong>So this rule sizes <c>flow</c>, not <c>dp</c>.</strong> <c>dp</c> is a visible decided
/// default of 20 kPa that a script may override; the design flow is computed. At convergence the two
/// agree by construction — the exchanger drops exactly its <c>dp</c> — and away from it the law is
/// quadratic, which is what keeps the Jacobian honest between passes rather than leaving a component
/// whose drop is a constant regardless of what flows through it.
/// </para>
/// <para>
/// <strong>Every exchanger gets one, including a load.</strong> A radiator network resists flow, a
/// boiler resists flow, and a block called <c>LOAD</c> that resists nothing is a modelling choice
/// nobody made deliberately. <c>24</c>'s worked example counts one exchanger drop on a circuit that has
/// two, which is <c>C-59</c>.
/// </para>
/// </remarks>
public sealed class ExchangerSizer : ISizer
{
    /// <inheritdoc/>
    public ImmutableArray<string> Parameters { get; } = ["flow"];

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing. An exchanger builds without a design flow — it is an ideal block until one arrives,
    /// which is what the bootstrap pass wants, since a resistance guessed before any flow is known
    /// would be a resistance every other rule then sized against.
    /// </remarks>
    public ImmutableDictionary<string, Quantity> Provisional => [];

    /// <inheritdoc/>
    public bool CanSize(IFlowComponent component) => component is HeatExchanger;

    /// <inheritdoc/>
    public Result<SizingResult> Size(IFlowComponent component, in SizingContext context)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component is not HeatExchanger exchanger)
        {
            return Result.Failure<SizingResult>(ResultError.From(
                Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a design flow"),
                ("name", component.Name),
                ("state", "a component that is not a heat exchanger")));
        }

        var flow = Math.Abs(context.MassFlow);
        var density = context.State.Density.SiValue;

        var off = Topology.WellPosedness.ZeroDuty(exchanger);

        if (off || !double.IsFinite(flow) || flow <= Solvers.Tolerances.FlowZero)
        {
            // No flow yet, so no design point. The exchanger stays ideal for this pass and the next one
            // sizes it -- reporting nothing rather than pinning a design flow of zero, which would make
            // its resistance infinite the moment anything did flow. A coil that is *off* stays here for
            // the life of the run whatever flow it is handed -- the seed's nominal estimate on the first
            // pass, 1.8e-27 kg/s on a later one, which once passed the `<= 0` test and became the flow
            // its 20 kPa was measured at, a resistance of 1e44 -- and says so (`S-56`).
            var notes = ImmutableArray.CreateBuilder<string>();

            if (off)
            {
                var kilopascals = Drop(exchanger) / 1000;

                notes.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{exchanger.Name} is off (power=0): its {kilopascals:0.#} kPa has no design flow to be measured at, so it resists nothing this run."));
            }

            return Result.Success(new SizingResult
            {
                Values = ImmutableDictionary<string, SizedValue>.Empty,
                Notes = notes.ToImmutable(),
            });
        }

        var drop = Drop(exchanger);


        // Built separately rather than concatenated inside `string.Create`: a ternary in that position
        // breaks the interpolated-string handler and the error names the wrong argument (`CS1620`).
        var volume = double.IsFinite(density) && density > 0
            ? string.Create(CultureInfo.InvariantCulture, $", {SizingContext.LitresPerSecond(flow, density):0.###} l/s")
            : string.Empty;

        var measured = string.Create(
            CultureInfo.InvariantCulture,
            $"{flow:0.####} kg/s — the flow {exchanger.Name}'s {drop / 1000:0.#} kPa is measured at");

        var basis = measured + volume;

        return Result.Success(new SizingResult
        {
            Values = ImmutableDictionary<string, SizedValue>.Empty.Add(
                "flow",
                new SizedValue(Quantity.FromSi(flow, Dimension.MassFlow), basis, FromDefault: false)),
            Notes = [],
        });
    }

    /// <summary>The design drop this exchanger will be built with.</summary>
    /// <param name="exchanger">The exchanger.</param>
    /// <returns>Pa. A stated <c>dp</c> if there is one, otherwise the registry's decided default.</returns>
    /// <remarks>
    /// Read for the basis string only. The value that reaches the component is resolved by
    /// <c>ComponentFactory</c> from the same three maps, so this cannot disagree with it without the
    /// maps disagreeing first.
    /// </remarks>
    private static double Drop(HeatExchanger exchanger) => Ownership.Decided(exchanger, "dp") ?? 0;
}
