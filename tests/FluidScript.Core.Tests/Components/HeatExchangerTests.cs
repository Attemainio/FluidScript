using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
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

        // And the residual is zero at exactly that flow, computed the other way round -- from the
        // solved port enthalpies rather than from cp and a temperature rise.
        Span<double> residuals = stackalloc double[2];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(50)], [implied, -implied]),
            residuals);

        Assert.Equal(0, residuals[0], tolerance: 1e-6);
    }

    [Fact]
    public void ANegativePowerIsAConsumerAndCoolsTheStream()
    {
        // One kind covers source and consumer; the sign of power is the whole difference. -70 kW at
        // 0.5578 kg/s drops 30 K, so the outlet is below the inlet and the residual is zero there.
        var radiator = new HeatExchanger("RAD1", power: -70_000);
        var flow = radiator.ImpliedFlow(SpecificHeat, temperatureRise: 30);

        Span<double> residuals = stackalloc double[2];
        radiator.EvaluateResiduals(
            new SolveContext(Water, [At(50), At(20)], [flow, -flow]),
            residuals);

        Assert.Equal(0, residuals[0], tolerance: 1e-6);
        Assert.True(radiator.Power < 0);
    }

    [Fact]
    public void TheEnergyResidualIsTheDutyNotDelivered()
    {
        // Off the solution the residual has to be a duty in watts, because that is what its declaration
        // says its unit is -- and a diagnostic that reads "HX1 duty off by 4.2 kW" depends on it.
        var exchanger = new HeatExchanger("HX1", power: 30_000);

        Span<double> residuals = stackalloc double[2];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(50)], [0.1, -0.1]),
            residuals);

        // 0.1 kg/s across 4184 x 30 = 12 552 W delivered against 30 000 asked: 17 448 W short.
        Assert.Equal(17_448, residuals[0], tolerance: 1.0);
        Assert.Equal("W", exchanger.DeclareEquations()[0].ResidualSiUnit);
    }

    [Fact]
    public void AnIdealBlockHasNoPressureDrop()
    {
        // Duty mode makes no area, effectiveness or approach claim, and with no stated dp it makes no
        // hydraulic claim either -- rather than inventing a plausible resistance.
        var exchanger = new HeatExchanger("HX1", power: 30_000);

        Span<double> residuals = stackalloc double[2];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(50)], [0.24, -0.24]),
            residuals);

        Assert.Equal(0, residuals[1], tolerance: 1e-12);
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
            Span<double> residuals = stackalloc double[2];
            exchanger.EvaluateResiduals(
                new SolveContext(Water, [At(20, 100_000), At(50, 100_000)], [flow, -flow]),
                residuals);

            // The residual is (p_in - p_out) - dp; with equal port pressures it is -dp.
            return -residuals[1];
        }
    }

    [Fact]
    public void AReversedFlowLosesPressureInTheDirectionItIsGoing()
    {
        var exchanger = new HeatExchanger("HX1", power: 0, designPressureDrop: 25_000, designFlow: 0.5);

        Span<double> forward = stackalloc double[2];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(20)], [0.5, -0.5]), forward);

        Span<double> reverse = stackalloc double[2];
        exchanger.EvaluateResiduals(
            new SolveContext(Water, [At(20), At(20)], [-0.5, 0.5]), reverse);

        Assert.Equal(forward[1], -reverse[1], tolerance: 1e-9);
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
