using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Components;

/// <summary>The duty-mode exchanger: its energy relation, its drop, and the flow a duty implies.</summary>
/// <remarks>
/// <c>62</c>'s worked example lives here — 30 kW across 20 to 50 °C implying 0.239 kg/s — computed from
/// the fake's own declared <c>cp</c> so the arithmetic in the comment and the arithmetic in the code
/// use the same number. The rated and coupled modes are <c>P4.1</c>'s and are not exercised.
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
    private static (double Inlet, double Outlet) Injection(HeatExchanger exchanger, double flow)
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
    /// A real <see cref="CircuitNode"/> rather than the arithmetic written out, because what is being
    /// checked is that the two halves <em>compose</em>: the node carries the transport and the
    /// exchanger carries the heat, and neither is a whole equation on its own (<c>D-69</c>).
    /// </remarks>
    private static double DownstreamNodeEnergy(
        HeatExchanger exchanger, double flow, double inlet, double outlet)
    {
        Span<double> injection = stackalloc double[exchanger.Ports.Length];
        exchanger.EvaluateEnergyInjection(
            new SolveContext(Water, [At(inlet), At(outlet)], [flow, -flow]),
            injection);

        // Two ports: the stream arrives through the exchanger carrying the inlet node's enthalpy, and
        // leaves through the other one at this node's own.
        var node = new CircuitNode("N", portCount: 2, carriesMassBalance: false);

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
        var exchanger = new HeatExchanger("HX1", power: 30_000);
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
        var radiator = new HeatExchanger("RAD1", power: -70_000);
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
        var exchanger = new HeatExchanger("HX1", power: 30_000);

        Assert.Equal(17_448, DownstreamNodeEnergy(exchanger, 0.1, inlet: 20, outlet: 50), tolerance: 1.0);
    }

    [Fact]
    public void TheWholeDutyLandsOnTheSideItDischargesThrough()
    {
        // The invariant that replaces the duty row: however the fluid runs, the two entries sum to the
        // stated power -- the exchanger moves a fixed amount of heat into the circuit -- and all of it
        // is on the downstream side. Nailing it to a port instead is what made a reversal unsolvable.
        var exchanger = new HeatExchanger("HX1", power: 30_000);

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
        var exchanger = new HeatExchanger("HX1", power: 30_000);

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
        var exchanger = new HeatExchanger("HX1", power: 30_000);

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
        var exchanger = new HeatExchanger("HX1", power: 30_000, designPressureDrop: 25_000, designFlow: 0.5);

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
        var exchanger = new HeatExchanger("HX1", power: 0, designPressureDrop: 25_000, designFlow: 0.5);

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
        var exchanger = new HeatExchanger("HX1", power: 30_000);

        Assert.Equal(["in", "out", "in2", "out2"], exchanger.Ports.Select(static port => port.Name));
        Assert.Equal([false, false, true, true], exchanger.Ports.Select(static port => port.IsOptional));
    }

    [Fact]
    public void ItReportsDutyModeAndNotAScriptParameter()
    {
        // There is no mode= in the language: lowering computes exactly one mode from what was connected
        // and stated, so that adding real connections to an external-profile design has one meaning.
        Assert.Equal("duty", new HeatExchanger("HX1", power: 1000).Mode);
    }

    [Fact]
    public void EvaluateResidualsAllocatesNothing()
    {
        var exchanger = new HeatExchanger("HX1", power: 30_000, designPressureDrop: 25_000, designFlow: 0.5);
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
}
