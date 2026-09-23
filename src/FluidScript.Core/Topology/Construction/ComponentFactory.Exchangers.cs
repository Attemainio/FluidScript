using System.Collections.Immutable;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Topology.Construction;

public sealed partial class ComponentFactory
{
    /// <summary>A duty-mode exchanger, with the resistance its design point implies.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="stated">What the script wrote.</param>
    /// <param name="sized">What a rule chose.</param>
    /// <param name="defaults">What the registry decided.</param>
    /// <returns>The component. An exchanger always builds; only its resistance waits.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The drop and the flow arrive from different places and neither is sufficient alone.</strong>
    /// <see cref="HeatExchanger"/> holds <c>Δp = dp·(ṁ/ṁ_design)²</c>, so a <c>dp</c> with no design flow
    /// is half a law. <c>dp</c> is stated or carries the registry's decided 20 kPa; the design flow is
    /// sized by <c>ExchangerSizer</c> from the flow the circuit actually runs at.
    /// </para>
    /// <para>
    /// <strong>Before the first flow estimate there is no design point, so the block is ideal.</strong>
    /// Passing a positive <c>dp</c> with a zero flow throws by contract, and rightly -- the implied
    /// resistance is infinite. The bootstrap pass therefore builds a resistance-free exchanger and the
    /// first real pass replaces it, which is the same shape as every other provisional.
    /// </para>
    /// <para>
    /// Role aliases state a positive capacity while the physics core receives signed heat flow:
    /// <c>load</c>, <c>cooler</c>, <c>radiator</c> and <c>chiller</c> remove heat; <c>heater</c> and
    /// <c>boiler</c> add it. The neutral <c>heat_exchanger</c>, <c>exchanger</c> and <c>hx</c>
    /// spellings retain an explicitly signed <c>power</c>.
    /// </para>
    /// </remarks>
    /// <param name="wiring">How many ports the script connected, and by what names.</param>
    private HeatExchanger Exchanger(
        ComponentSymbol symbol,
        ComponentKindInfo kind,
        PortWiring wiring,
        ImmutableDictionary<string, Quantity> stated,
        ImmutableDictionary<string, Quantity> sized,
        ImmutableDictionary<string, Quantity> defaults)
    {
        var design = Value(symbol, kind, "flow") ?? 0;
        var drop = design > 0 ? Value(symbol, kind, "dp") ?? 0 : 0;
        var power = Value(symbol, kind, "power") ?? 0;
        var writtenKind = NameResolution.Normalize(symbol.WrittenKind);

        power = writtenKind switch
        {
            "load" or "cooler" or "radiator" or "chiller" => -Math.Abs(power),
            "heater" or "boiler" => Math.Abs(power),
            _ => power,
        };

        // `S-14b`. Side 2's row exists whenever the script wired it, and its *resistance* only when a
        // script also states `flow2` -- no rule chooses that, because a rule sees one branch and this
        // component sits on two. An ideal side still carries `p_in2 = p_out2`, which is the row the
        // counting table was crediting and the component was not declaring.
        var secondary = wiring.Names("in2") || wiring.Names("out2");
        var secondaryFlow = Value(symbol, kind, "flow2") ?? 0;
        var secondaryDrop = secondaryFlow > 0 ? Value(symbol, kind, "dp2") ?? 0 : 0;

        return new HeatExchanger(
            symbol.Name,
            power,
            drop,
            design,
            secondaryDrop,
            secondaryFlow,
            secondary)
        {
            StatedParameters = stated,
            SizedParameters = sized,
            DefaultParameters = defaults,
            Rating = Rating(symbol, kind, secondary, power),

            // Zero on the bootstrap pass, because no area has been chosen yet. That is deliberate and
            // not a gap: a dynamic circuit declares the hold-up's enthalpy as an unknown whatever the
            // volume (`D-145`), so the counting table is the same on every pass and only the lag moves.
            HoldUp = Math.Max(0, Value(symbol, kind, "volume") ?? 0),
            HoldUp2 = Math.Max(0, Value(symbol, kind, "volume2") ?? 0),
        };
    }

    /// <summary>Whether a declaration states any of the side-2 profile, which is what promotes Duty to Rated.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <returns><see langword="true"/> when <c>in2</c>, <c>out2</c>, <c>dt2</c> or <c>flow2</c> is written.</returns>
    public static bool StatesSecondaryProfile(ComponentSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        foreach (var name in SecondaryProfile)
        {
            if (symbol.Parameters.ContainsKey(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What an exchanger rates against, or <see langword="null"/> for a duty block.</summary>
    /// <param name="symbol">The bound declaration.</param>
    /// <param name="kind">Its registry entry.</param>
    /// <param name="coupled">Whether the secondary ports are wired.</param>
    /// <param name="power">W, the signed duty the script stated, for the direction a side-2 profile runs.</param>
    /// <returns>The rating, whose <see cref="ExchangerRating.CanRate"/> says whether it is complete.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The mode is evidence of a second side, in <c>D-19</c>'s precedence.</strong> Wired secondary
    /// ports are Coupled whatever else is stated; a stated <c>in2</c>, <c>out2</c>, <c>dt2</c> or
    /// <c>flow2</c> with open ports is Rated; and <c>ua</c>, <c>u</c>, <c>area</c> or geometry alone promote
    /// nothing, because they say how heat crosses and not what it crosses to.
    /// </para>
    /// <para>
    /// <strong>The size is whatever is known first:</strong> a stated or sized <c>ua</c>, else
    /// <c>u · area</c> when both are known. Nothing here sizes; the thermal rule does, and this reads its
    /// answer back through the overlay like every other sized value.
    /// </para>
    /// <para>
    /// <strong>A Rated profile is resolved to the two numbers ε-NTU needs</strong>: the temperature side 2
    /// enters at, and its capacity rate. <c>in2</c> gives the first outright; <c>out2</c> with <c>dt2</c>
    /// gives it in the direction the duty runs -- a side that gives heat enters hotter. The flow is
    /// <c>flow2</c> when stated, else what the duty implies across the side's temperature change, at
    /// the fluid's specific heat at the side's mean temperature (<c>S-32</c>).
    /// </para>
    /// </remarks>
    private ExchangerRating? Rating(ComponentSymbol symbol, ComponentKindInfo kind, bool coupled, double power)
    {
        if (!coupled && !StatesSecondaryProfile(symbol))
        {
            return null;
        }

        var conductance = Value(symbol, kind, "ua")
            ?? (Value(symbol, kind, "u") is { } u && Value(symbol, kind, "area") is { } area ? u * area : 0);

        var arrangement = symbol.Parameters.TryGetValue("arrangement", out var stated) && stated.Symbol is { } name
            ? name switch
            {
                "parallel" => ExchangerArrangement.Parallel,
                "crossflow" => ExchangerArrangement.Crossflow,
                _ => ExchangerArrangement.Counter,
            }
            : ExchangerArrangement.Counter;

        if (coupled)
        {
            return new ExchangerRating
            {
                Mode = ExchangerMode.Coupled,
                Arrangement = arrangement,
                Conductance = conductance,
            };
        }

        var inlet = Value(symbol, kind, "in2");
        var outlet = Value(symbol, kind, "out2");
        var change = Value(symbol, kind, "dt2");

        // Side 2 loses what side 1 gains: with power > 0 it enters hotter than it leaves.
        var direction = power >= 0 ? 1 : -1;

        inlet ??= outlet is { } leaving && change is { } across ? leaving + (direction * across) : null;
        outlet ??= inlet is { } entering && change is { } across2 ? entering - (direction * across2) : null;

        var capacity = 0.0;

        if (inlet is { } entering2 && substance is not null)
        {
            var mean = outlet is { } leaving2 ? 0.5 * (entering2 + leaving2) : entering2;
            var state = substance.FromPressureTemperature(
                Quantity.FromSi(0, Dimension.Pressure), Quantity.FromSi(mean, Dimension.Temperature));

            if (state.IsSuccess)
            {
                var heat = state.Value.SpecificHeat.SiValue;
                var flow = Value(symbol, kind, "flow2")
                    ?? (outlet is { } leaving3 && Math.Abs(entering2 - leaving3) > 0
                        ? Math.Abs(power) / (heat * Math.Abs(entering2 - leaving3))
                        : null);

                capacity = flow is { } rate ? rate * heat : 0;
            }
        }

        return new ExchangerRating
        {
            Mode = ExchangerMode.Rated,
            Arrangement = arrangement,
            Conductance = conductance,
            SecondaryInletTemperature = inlet ?? double.NaN,
            SecondaryCapacityRate = capacity,
        };
    }
}
