using FluidScript.Core.Components;
using FluidScript.Core.Fluids;

namespace FluidScript.Core.Tests.Components;

/// <summary>The valve law and the pump curve, at hand-checked points and at their awkward ones.</summary>
/// <remarks>
/// Both kinds have an acceptance criterion that exists because the obvious implementation is wrong in a
/// way that passes a naive test: the valve's √ has infinite slope exactly where a closed valve sits,
/// and the pump's affinity laws are silent at the speed everyone writes their test at.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ValveAndPumpTests
{
    private const double Density = ConstantPropertyWater.DensityValue;

    private static readonly ISubstance Water = ConstantPropertyWater.Instance;

    private static PortState State(double pressure) => new()
    {
        Pressure = pressure,
        Enthalpy = 0,
        Temperature = 293.15,
        Density = Density,
        SpecificHeat = ConstantPropertyWater.SpecificHeatValue,
        DynamicViscosity = ConstantPropertyWater.DynamicViscosityValue,
        ThermalConductivity = ConstantPropertyWater.ThermalConductivityValue,
    };

    [Fact]
    public void TheKvFormAndTheSiFormAgree()
    {
        // 22's acceptance criterion, and the reason it exists: Kv is defined in m3/h of water at 1 bar,
        // and substituting pascals into that form is wrong by sqrt(1e5) = 316 -- two and a half orders
        // of magnitude, and entirely plausible-looking.
        //
        // Kv = 6.3, dp = 0.5 bar = 50 000 Pa, rho = 998.2 so rho_r = 0.9982.
        //   Kv units: Q = 6.3 x sqrt(0.5 / 0.9982) = 6.3 x 0.707744 = 4.45879 m3/h
        //             mdot = rho Q / 3600 = 998.2 x 4.45879 / 3600     = 1.23633 kg/s
        const double kv = 6.3;
        const double bar = 0.5;

        var relativeDensity = Density / ValveLaw.WaterDensity;
        var cubicMetresPerHour = kv * Math.Sqrt(bar / relativeDensity);
        var fromKvUnits = Density * cubicMetresPerHour / 3600;

        var fromSi = ValveLaw.MassFlow(kv, bar * 1e5, Density);

        Assert.Equal(1.23633, fromKvUnits, tolerance: 1e-5);
        Assert.Equal(fromKvUnits, fromSi, tolerance: 1e-9);
    }

    [Fact]
    public void TheValveLawIsOdd()
    {
        // 36's acceptance criterion. Forgetting the sign gives a valve that passes flow one way only,
        // which presents as a mysterious non-convergence in any circuit with a bypass rather than as a
        // wrong number.
        foreach (var drop in new[] { 1.0, 50.0, 99.9, 100.0, 100.1, 5000.0, 2.5e5 })
        {
            Assert.Equal(
                -ValveLaw.MassFlow(6.3, drop, Density),
                ValveLaw.MassFlow(6.3, -drop, Density),
                tolerance: 1e-12);
        }
    }

    [Fact]
    public void TheValveLawIsContinuousInValueAndSlopeAtTheRegularizationJoin()
    {
        // 36's acceptance criterion: C-one at 100 Pa, by finite differences either side. The quadratic
        // is built to satisfy Q(a) = K sqrt(a) and Q'(a) = K / (2 sqrt(a)); a straight line through the
        // origin would match the value and miss the slope by exactly a factor of two.
        const double a = ValveLaw.RegularizationDrop;
        const double step = 1e-6;

        var below = ValveLaw.MassFlow(6.3, a - step, Density);
        var above = ValveLaw.MassFlow(6.3, a + step, Density);

        Assert.Equal(below, above, tolerance: 1e-9);

        var slopeBelow = (ValveLaw.MassFlow(6.3, a, Density) - ValveLaw.MassFlow(6.3, a - step, Density)) / step;
        var slopeAbove = (ValveLaw.MassFlow(6.3, a + step, Density) - ValveLaw.MassFlow(6.3, a, Density)) / step;

        Assert.Equal(slopeBelow, slopeAbove, tolerance: 1e-6 * Math.Abs(slopeBelow));
    }

    [Fact]
    public void AStraightLineThroughTheOriginWouldHaveMissedTheSlopeByAFactorOfTwo()
    {
        // Not a test of the code so much as of the claim the code rests on -- and it is cheap to state
        // once rather than to rediscover when someone simplifies the quadratic away.
        const double a = ValveLaw.RegularizationDrop;
        const double step = 1e-6;

        var atJoin = ValveLaw.MassFlow(6.3, a, Density);
        var actualSlope = (atJoin - ValveLaw.MassFlow(6.3, a - step, Density)) / step;
        var lineSlope = atJoin / a;

        Assert.Equal(2.0, lineSlope / actualSlope, tolerance: 1e-4);
    }

    [Fact]
    public void AClosedValveEvaluatesAFiniteResidualAndAFiniteSlope()
    {
        // 22's acceptance criterion. sqrt(dp) has infinite slope at zero, which is exactly where a
        // closed valve sits -- so without the regularisation the first circuit that closes one fails to
        // converge, and the failure looks like a solver bug.
        var valve = new Valve("TV1", kv: 6.3);

        Span<double> residuals = stackalloc double[1];
        valve.EvaluateResiduals(new SolveContext(Water, [State(0), State(0)], [0.0, 0.0]), residuals);

        Assert.Equal(0, residuals[0], tolerance: 1e-12);

        const double step = 1e-9;
        var slope = (ValveLaw.MassFlow(6.3, step, Density) - ValveLaw.MassFlow(6.3, 0, Density)) / step;

        Assert.True(double.IsFinite(slope), $"The slope at zero drop was {slope}.");
        Assert.True(slope > 0, "A valve at rest must still open in the right direction.");
    }

    [Theory]
    [InlineData(ValveCharacteristic.Linear, 0.5, 0.5)]
    [InlineData(ValveCharacteristic.Linear, 1.0, 1.0)]
    [InlineData(ValveCharacteristic.QuickOpen, 0.25, 0.5)]
    [InlineData(ValveCharacteristic.EqualPercentage, 1.0, 1.0)]
    [InlineData(ValveCharacteristic.EqualPercentage, 0.5, 0.14142135623731)]
    public void TheCharacteristicsFollowTheirDefinitions(
        ValveCharacteristic characteristic, double position, double expected)
    {
        // Equal-percentage is R^(x-1) with R = 50, so at half travel it is 50^-0.5 = 0.1414.
        Assert.Equal(expected, ValveLaw.Opening(position, characteristic), tolerance: 1e-12);
    }

    [Fact]
    public void AClosedEqualPercentageValveStillPassesTwoPercentOfItsKv()
    {
        // Not a defect -- it is what R^(x-1) means at x = 0, and a real valve's shut-off comes from its
        // seat, which is a leakage class rather than part of the characteristic. Asserted so that the
        // consequence is visible: a bypass "closed" by an equal-percentage valve is not shut (C-18).
        Assert.Equal(1 / ValveLaw.Rangeability, ValveLaw.Opening(0, ValveCharacteristic.EqualPercentage), tolerance: 1e-12);
        Assert.Equal(0, ValveLaw.Opening(0, ValveCharacteristic.Linear), tolerance: 1e-12);
    }

    [Fact]
    public void AThreeWayValveBalancesMassWhicheverWayItIsWired()
    {
        // Mixing: two inflows at b and c, one outflow at a. The arrangement is read from the topology,
        // not declared, and one signed balance covers both it and the diverting case.
        var valve = new ThreeWayValve("TV1", kv: 6.3, position: 0.5);

        Span<double> residuals = stackalloc double[3];
        valve.EvaluateResiduals(
            new SolveContext(Water, [State(0), State(0), State(0)], [-0.4, 0.25, 0.15]),
            residuals);

        Assert.Equal(0, residuals[0], tolerance: 1e-12);
    }

    [Fact]
    public void AThreeWayValvesBypassTakesTheComplementaryOpening()
    {
        // As a-b opens, a-c closes. At half travel with a linear characteristic the two paths are equal.
        var half = new ThreeWayValve("TV1", kv: 6.3, position: 0.5);

        Span<double> residuals = stackalloc double[3];
        half.EvaluateResiduals(
            new SolveContext(Water, [State(50_000), State(0), State(0)], [0.0, 0.0, 0.0]),
            residuals);

        Assert.Equal(residuals[1], residuals[2], tolerance: 1e-12);

        var open = new ThreeWayValve("TV2", kv: 6.3, position: 1.0);

        Span<double> wideOpen = stackalloc double[3];
        open.EvaluateResiduals(
            new SolveContext(Water, [State(50_000), State(0), State(0)], [0.0, 0.0, 0.0]),
            wideOpen);

        // Fully open to b, shut to c: the controlled path demands flow and the bypass demands none.
        Assert.True(Math.Abs(wideOpen[1]) > Math.Abs(wideOpen[2]));
        Assert.Equal(0, wideOpen[2], tolerance: 1e-12);
    }

    [Theory]
    [InlineData(1.0, 8.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.0, -2.0)]
    public void ThePumpCurveDistributesSpeedSquaredOverBothTerms(double speed, double expectedHead)
    {
        // 22's acceptance criterion, and the sharpest one in the document. H0 = 10 m, k = 2, mdot = 1.
        //   n = 1.0  ->  10 - 2      =  8
        //   n = 0.5  ->  2.5 - 2     =  0.5      the WRONG form gives 2.5 - 4x2 = -5.5
        //   n = 0    ->  0 - 2       = -2        the WRONG form divides by zero
        // The error is silent at n = 1, which is where every test gets written.
        var pump = new Pump("PU1", shutOffHead: 10, curvature: 2, speed: speed);

        Assert.Equal(expectedHead, pump.Head(1.0), tolerance: 1e-12);
    }

    [Fact]
    public void AStoppedPumpIsAPureResistanceAndItsResidualIsFinite()
    {
        // 22's invariant 7. A stopped pump must evaluate, and what it evaluates to is a resistance:
        // -rho g (-k mdot^2), a pressure that falls in the direction of flow.
        var pump = new Pump("PU1", shutOffHead: 10, curvature: 2, speed: 0);

        Span<double> residuals = stackalloc double[1];
        pump.EvaluateResiduals(new SolveContext(Water, [State(0), State(0)], [0.5, -0.5]), residuals);

        Assert.True(double.IsFinite(residuals[0]), $"A stopped pump gave {residuals[0]}.");

        // At 0.5 kg/s: H = -2 x 0.25 = -0.5 m, so rho g H = 998.2 x 9.80665 x -0.5 = -4894.5 Pa, and
        // the residual is (p_in - p_out) + that = 0 - 4894.5.
        Assert.Equal(-4894.5, residuals[0], tolerance: 0.1);
    }

    [Fact]
    public void APumpRaisesPressureSoItsDropIsNegative()
    {
        // Convention 1: a pressure drop is positive when pressure falls in the nominal direction, and a
        // pump reports a negative one. The residual is zero where the outlet is above the inlet.
        var pump = new Pump("PU1", shutOffHead: 10, curvature: 2);
        var head = pump.Head(0.5);
        var rise = Density * 9.80665 * head;

        Span<double> residuals = stackalloc double[1];
        pump.EvaluateResiduals(new SolveContext(Water, [State(0), State(rise)], [0.5, -0.5]), residuals);

        Assert.True(head > 0);
        Assert.Equal(0, residuals[0], tolerance: 1e-9);
    }

    [Fact]
    public void TheDefaultCurvePassesThroughItsDutyPoint()
    {
        // Shut-off is 1.2 x the duty head, which is typical for a centrifugal pump and wrong for
        // anything else -- so it is documented and reported rather than assumed.
        var pump = Pump.FromDutyPoint("PU1", dutyHead: 5.28, dutyFlow: 0.2392);

        Assert.Equal(5.28 * Pump.DefaultShutOffFactor, pump.ShutOffHead, tolerance: 1e-12);
        Assert.Equal(5.28, pump.Head(0.2392), tolerance: 1e-9);
        // The curve reaches zero head at sqrt(H0/k) x mdot_duty. With H0 = 1.2 H_duty and
        // k = 0.2 H_duty / mdot_duty^2 that is sqrt(1.2 / 0.2) = sqrt(6) times the duty flow, not
        // sqrt(1.2) -- the shut-off factor is not the flow ratio.
        Assert.Equal(0.0, pump.Head(0.2392 * Math.Sqrt(6)), tolerance: 1e-9);
    }

    [Fact]
    public void ShaftPowerIsPositiveDespiteTheNegativeDrop()
    {
        var pump = new Pump("PU1", shutOffHead: 10, curvature: 2, efficiency: 0.7);
        var power = pump.ShaftPower(0.5, -48_900, Density);

        Assert.True(power > 0, $"Shaft power came out {power}.");
        Assert.Equal(0.5 * 48_900 / (Density * 0.7), power, tolerance: 1e-9);
    }

    [Fact]
    public void ValveAndPumpResidualsAllocateNothing()
    {
        var valve = new Valve("TV1", kv: 6.3, position: 0.4, characteristic: ValveCharacteristic.EqualPercentage);
        var threeWay = new ThreeWayValve("TV2", kv: 6.3, position: 0.4);
        var pump = new Pump("PU1", shutOffHead: 10, curvature: 2, speed: 0.8);

        var two = new[] { State(50_000), State(0) };
        var three = new[] { State(50_000), State(0), State(0) };
        var twoFlows = new[] { 0.5, -0.5 };
        var threeFlows = new[] { -0.4, 0.25, 0.15 };
        var one = new double[1];
        var threeResiduals = new double[3];

        Run();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void Run()
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                valve.EvaluateResiduals(new SolveContext(Water, two, twoFlows), one);
                pump.EvaluateResiduals(new SolveContext(Water, two, twoFlows), one);
                threeWay.EvaluateResiduals(new SolveContext(Water, three, threeFlows), threeResiduals);
            }
        }
    }
}
