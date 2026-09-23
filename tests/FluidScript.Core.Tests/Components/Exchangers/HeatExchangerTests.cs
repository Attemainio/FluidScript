using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers;

namespace FluidScript.Core.Tests.Components.Exchangers;

/// <summary>The exchanger: its energy relation, its drop, the flow a duty implies, and the rated relation.</summary>
/// <remarks>
/// <c>62</c>'s worked example lives here — 30 kW across 20 to 50 °C implying 0.239 kg/s — computed from
/// the fake's own declared <c>cp</c> so the arithmetic in the comment and the arithmetic in the code
/// use the same number. The rated relation (<c>P4.1</c>) is checked against the substation's design
/// point; the sizing that produces its rating is <c>ThermalSizerTests</c>' and the solved circuit is
/// <c>RatedExchangerSolveTests</c>'.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class HeatExchangerTests
{
    private const double SpecificHeat = ConstantPropertyWater.SpecificHeatValue;

    private static readonly ConstantPropertyWater Water = ConstantPropertyWater.Instance;

    // 36's upwind.smoothing_band, read from the table rather than written again here.
    private const double Band = Tolerances.UpwindSmoothingBand;

    private static PortState At(double celsius, double pressure = 0)
    {
        var state = Water.FromPressureTemperature(
            Quantity.FromSi(pressure, Dimension.Pressure),
            Quantity.FromSi(celsius + 273.15, Dimension.Temperature));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return new PortState
        {
            Pressure = state.Value.Pressure.SiValue,
            Enthalpy = state.Value.Enthalpy.SiValue,
            Temperature = state.Value.Temperature.SiValue,
            Density = state.Value.Density.SiValue,
            SpecificHeat = state.Value.SpecificHeat.SiValue,
            DynamicViscosity = state.Value.DynamicViscosity.SiValue,
            ThermalConductivity = state.Value.ThermalConductivity.SiValue,
        };
    }

    /// <summary>The energy the exchanger puts into the node at each of its two side-1 ports.</summary>
    /// <param name="exchanger">The exchanger.</param>
    /// <param name="flow">kg/s at port 0, positive into the exchanger.</param>
    /// <returns>Watts into the node at <c>in</c>, and watts into the node at <c>out</c>.</returns>
    private static (double Inlet, double Outlet) Injection(HeatExchangerComponent exchanger, double flow)
    {
        Span<double> injection = stackalloc double[exchanger.Ports.Length];
        exchanger.EvaluateEnergyInjection(
            new SolveContext(Water, [At(20), At(50)], [flow, -flow]),
            injection);

        return (injection[0], injection[1]);
    }

    /// <summary>The energy row of the node the exchanger discharges into, injection included.</summary>
    /// <param name="exchanger">The exchanger.</param>
    /// <param name="flow">kg/s, positive from <c>in</c> to <c>out</c>.</param>
    /// <param name="inlet">The inlet node's temperature, °C.</param>
    /// <param name="outlet">The outlet node's temperature, °C.</param>
    /// <returns>Watts. Zero when the duty and the enthalpy rise agree.</returns>
    /// <remarks>
    /// A real <see cref="NodeComponent"/> rather than the arithmetic written out, because what is being
    /// checked is that the two halves <em>compose</em>: the node carries the transport and the
    /// exchanger carries the heat, and neither is a whole equation on its own (<c>D-69</c>).
    /// </remarks>
    private static double DownstreamNodeEnergy(
        HeatExchangerComponent exchanger, double flow, double inlet, double outlet)
    {
        Span<double> injection = stackalloc double[exchanger.Ports.Length];
        exchanger.EvaluateEnergyInjection(
            new SolveContext(Water, [At(inlet), At(outlet)], [flow, -flow]),
            injection);

        // Two ports: the stream arrives through the exchanger carrying the inlet node's enthalpy, and
        // leaves through the other one at this node's own.
        var node = new NodeComponent("N", portCount: 2, carriesMassBalance: false);

        Span<double> residuals = stackalloc double[node.EquationCount];
        node.EvaluateResiduals(
            new SolveContext(
                Water,
                [At(inlet), At(outlet)],
                [flow, -flow],
                [0.0, At(outlet).Enthalpy]),
            residuals);

        return residuals[0] + injection[1];
    }


    [Fact]
    public void ThirtyKilowattsAcrossThirtyKelvinImpliesTheExpectedFlow()
    {
        // 62's worked example. 30 000 W / (4184 J/(kg K) x 30 K) = 0.239006 kg/s, using the fake's own
        // declared cp so the test's arithmetic and the code under test agree exactly.
        //
        // 62 states 0.23912 against a cp of 4182; ConstantPropertyWater declares 4184, and the two
        // figures are both right for their own fluid. They must not be reconciled.
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000);
        var implied = exchanger.ImpliedFlow(SpecificHeat, temperatureRise: 30);

        Assert.Equal(0.239006, implied, tolerance: 1e-6);

        // And the duty balances at exactly that flow, computed the other way round -- from the solved
        // port enthalpies rather than from cp and a temperature rise. It is the *node's* row that has
        // to come out zero: D-69 moved the duty out of the exchanger and into the balance of whatever
        // it discharges into.
        Assert.Equal(0, DownstreamNodeEnergy(exchanger, implied, inlet: 20, outlet: 50), tolerance: 1e-6);
    }

    [Fact]
    public void ANegativePowerIsAConsumerAndCoolsTheStream()
    {
        // One kind covers source and consumer; the sign of power is the whole difference. -70 kW at
        // 0.5578 kg/s drops 30 K, so the outlet is below the inlet and the node balances there.
        var radiator = new HeatExchangerComponent("RAD1", power: -70_000);
        var flow = radiator.ImpliedFlow(SpecificHeat, temperatureRise: 30);

        Assert.Equal(0, DownstreamNodeEnergy(radiator, flow, inlet: 50, outlet: 20), tolerance: 1e-6);
        Assert.True(radiator.Power < 0);
    }

    [Fact]
    public void TheDutyNotDeliveredIsWhatTheNodesEnergyRowIsShortBy()
    {
        // The number this used to assert as the exchanger's own residual, now landing where D-69 puts
        // it. 0.1 kg/s across 4184 x 30 = 12 552 W carried against 30 000 injected: the node's energy
        // row is 17 448 W short, in watts, which is what "HX1 is 17.4 kW short" is built from.
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000);

        Assert.Equal(17_448, DownstreamNodeEnergy(exchanger, 0.1, inlet: 20, outlet: 50), tolerance: 1.0);
    }

    [Fact]
    public void TheWholeDutyLandsOnTheSideItDischargesThrough()
    {
        // The invariant that replaces the duty row: however the fluid runs, the two entries sum to the
        // stated power -- the exchanger moves a fixed amount of heat into the circuit -- and all of it
        // is on the downstream side. Nailing it to a port instead is what made a reversal unsolvable.
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000);

        var forward = Injection(exchanger, 0.5);
        var reverse = Injection(exchanger, -0.5);

        Assert.Equal(30_000, forward.Inlet + forward.Outlet, tolerance: 1e-9);
        Assert.Equal(30_000, reverse.Inlet + reverse.Outlet, tolerance: 1e-9);

        Assert.Equal(30_000, forward.Outlet, tolerance: 1e-9);
        Assert.Equal(0, forward.Inlet, tolerance: 1e-9);

        Assert.Equal(30_000, reverse.Inlet, tolerance: 1e-9);
        Assert.Equal(0, reverse.Outlet, tolerance: 1e-9);
    }

    [Fact]
    public void TheDutySplitIsSmoothThroughAReversal()
    {
        // S-5's lesson: C-1 is a claim about the one-sided derivatives agreeing at the join, not about
        // the derivative being small near it. Probed at the band edge with a shrinking step, the two
        // sides have to meet -- the smoothstep's own slope is zero there, so both are zero.
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000);

        foreach (var step in new[] { 1e-6, 1e-7, 1e-8 })
        {
            var inside = (Injection(exchanger, Band).Outlet - Injection(exchanger, Band - step).Outlet) / step;
            var outside = (Injection(exchanger, Band + step).Outlet - Injection(exchanger, Band).Outlet) / step;

            // Both one-sided derivatives are zero at the join, because the smoothstep's own slope is
            // zero there -- that is what C-1 buys, and it is why the duty stops changing exactly at
            // the band edge rather than kinking. The inside quotient's truncation is 3Q.step/(4.band^2)
            // = 2.3e4, 2.3e3 and 225 W per kg/s across the three steps, so a shrinking step is the
            // assertion and a flat tolerance would be nearly vacuous at the first one.
            Assert.Equal(0, outside, tolerance: 1e-6);
            Assert.Equal(0, inside, tolerance: 2 * 3 * 30_000 * step / (4 * Band * Band));
        }

        // And nothing is approximated outside the band, which is where a converged solution sits.
        Assert.Equal(30_000, Injection(exchanger, Band).Outlet, tolerance: 1e-9);
        Assert.Equal(0, Injection(exchanger, -Band).Outlet, tolerance: 1e-9);
    }

    [Fact]
    public void AnIdealBlockHasNoPressureDrop()
    {
        // Duty mode makes no area, effectiveness or approach claim, and with no stated dp it makes no
        // hydraulic claim either -- rather than inventing a plausible resistance.
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000);

        Span<double> residuals = stackalloc double[exchanger.EquationCount];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(50)], [0.24, -0.24]),
            residuals);

        Assert.Equal(0, residuals[0], tolerance: 1e-12);
    }

    [Fact]
    public void TheDropFollowsTheSquareOfTheFlowRatio()
    {
        // dp = dp_design x (mdot / mdot_design)^2. At twice the design flow, four times the drop.
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000, designPressureDrop: 25_000, designFlow: 0.5);

        Assert.Equal(25_000, Drop(0.5), 1e-9);
        Assert.Equal(100_000, Drop(1.0), 1e-9);
        Assert.Equal(6_250, Drop(0.25), 1e-9);

        double Drop(double flow)
        {
            Span<double> residuals = stackalloc double[exchanger.EquationCount];
            exchanger.EvaluateResiduals(
                new SolveContext(Water, [At(20, 100_000), At(50, 100_000)], [flow, -flow]),
                residuals);

            // The residual is (p_in - p_out) - dp; with equal port pressures it is -dp.
            return -residuals[0];
        }
    }

    [Fact]
    public void AReversedFlowLosesPressureInTheDirectionItIsGoing()
    {
        var exchanger = new HeatExchangerComponent("HX1", power: 0, designPressureDrop: 25_000, designFlow: 0.5);

        Span<double> forward = stackalloc double[exchanger.EquationCount];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(20)], [0.5, -0.5]), forward);

        Span<double> reverse = stackalloc double[exchanger.EquationCount];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(20)], [-0.5, 0.5]), reverse);

        Assert.Equal(forward[0], -reverse[0], tolerance: 1e-9);
    }

    [Fact]
    public void TheSecondaryPortsAreOptionalSoADutyDeclarationIsComplete()
    {
        // Inference rule I3 skips optional ports, which is what lets a Duty exchanger be written with
        // two connections and no fabricated nodes for a side that is not modelled.
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000);

        Assert.Equal(["in", "out", "in2", "out2"], exchanger.Ports.Select(static port => port.Name));
        Assert.Equal([false, false, true, true], exchanger.Ports.Select(static port => port.IsOptional));
    }

    [Fact]
    public void ItReportsDutyModeAndNotAScriptParameter()
    {
        // There is no mode= in the language: lowering computes exactly one mode from what was connected
        // and stated, so that adding real connections to an external-profile design has one meaning.
        Assert.Equal("duty", new HeatExchangerComponent("HX1", power: 1000).Mode);
    }

    [Fact]
    public void EvaluateResidualsAllocatesNothing()
    {
        var exchanger = new HeatExchangerComponent("HX1", power: 30_000, designPressureDrop: 25_000, designFlow: 0.5);
        var ports = new[] { At(20, 300_000), At(50, 275_000) };
        var flows = new[] { 0.5, -0.5 };
        var residuals = new double[2];

        Run();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void Run()
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                exchanger.EvaluateResiduals(new SolveContext(Water, ports, flows), residuals);
            }
        }
    }

    // ---- the rated relation (P4.1) ----------------------------------------------------------------

    /// <summary>The substation's HX1 as a Rated exchanger: UA 12 071 W/K against the 85/45 profile.</summary>
    private static HeatExchangerComponent Rated(double power = 150_000) =>
        new("HX1", power)
        {
            Rating = new ExchangerRating
            {
                Mode = ExchangerMode.Rated,
                Conductance = 12_071,
                SecondaryInletTemperature = 85 + 273.15,
                SecondaryCapacityRate = 3750,
            },
        };

    [Fact]
    public void ARatedExchangerDeliversItsDesignDutyAtItsDesignPoint()
    {
        // 7500 W/K entering at 40 C against 3750 W/K at 85 C through UA 12 071: NTU 3.219 on Cmin, Cr 0.5,
        // ε 0.8889, and 0.8889 * 3750 * 45 = 150 kW. The number 01 sized the exchanger for, recovered
        // from the rating rather than read from `power`.
        var duty = HeatExchangerComponent.Duty(Rated().Rating!, capacity1: 7500, inlet1: 40 + 273.15, capacity2: 3750, inlet2: 85 + 273.15);

        Assert.Equal(150_000, duty, 150.0);
    }

    [Fact]
    public void ARatedExchangersDutyFallsAsItsInletRisesAndStatedPowerIsNotConsulted()
    {
        // The whole reason a rated exchanger fixes a closed loop's temperature level: warm the inlet
        // and it transfers less, cool it and it transfers more. A constant `power` could not do that.
        var rating = Rated().Rating!;

        var cold = HeatExchangerComponent.Duty(rating, 7500, 35 + 273.15, 3750, 85 + 273.15);
        var design = HeatExchangerComponent.Duty(rating, 7500, 40 + 273.15, 3750, 85 + 273.15);
        var warm = HeatExchangerComponent.Duty(rating, 7500, 45 + 273.15, 3750, 85 + 273.15);

        Assert.True(cold > design && design > warm, $"{cold} / {design} / {warm}");
        Assert.Equal(0, HeatExchangerComponent.Duty(rating, 7500, 85 + 273.15, 3750, 85 + 273.15), 1e-9);
    }

    [Fact]
    public void TheDutyIsContinuousWhereTheCapacityRatesCross()
    {
        // Cr -> 1 is where the counterflow closed form divides by zero, and the crossover is where Cmin
        // changes sides. Both are blended (Effectiveness.BalancedBand), so stepping C1 across C2 moves
        // the duty by no more than the step deserves: no jump for Newton to fall into.
        var rating = Rated().Rating!;

        var below = HeatExchangerComponent.Duty(rating, 3750 * (1 - 1e-5), 40 + 273.15, 3750, 85 + 273.15);
        var at = HeatExchangerComponent.Duty(rating, 3750, 40 + 273.15, 3750, 85 + 273.15);
        var above = HeatExchangerComponent.Duty(rating, 3750 * (1 + 1e-5), 40 + 273.15, 3750, 85 + 273.15);

        Assert.Equal(at, below, at * 1e-4);
        Assert.Equal(at, above, at * 1e-4);
    }

    [Fact]
    public void ARatedInjectionReadsThePortStatesAndAllocatesNothing()
    {
        // 1.7932 kg/s of the fake's 4184 J/(kg K) water is 7503 W/K at 40 C: the design point, so the
        // injection into the outlet node is the design 150 kW -- and the hot path stays allocation-free
        // with the rating in play, which is the invariant every component shares (22).
        var exchanger = Rated();
        var ports = new[] { At(40), At(60) };
        var flows = new[] { 1.7932, -1.7932 };
        var injection = new double[exchanger.Ports.Length];

        Run();

        Assert.Equal(150_000, injection[1], 300.0);
        Assert.Equal(0, injection[0], 1e-9);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void Run()
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                exchanger.EvaluateEnergyInjection(new SolveContext(Water, ports, flows), injection);
            }
        }
    }
}
