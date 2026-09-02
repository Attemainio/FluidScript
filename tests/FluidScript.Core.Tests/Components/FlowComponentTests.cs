using FluidScript.Core.Components;
using FluidScript.Core.Fluids;

namespace FluidScript.Core.Tests.Components;

/// <summary>The node and the pipe: their equation counts, and their residuals at hand-checked states.</summary>
/// <remarks>
/// Every case here runs against <see cref="ConstantPropertyWater"/>, so the arithmetic in a comment and
/// the arithmetic in the code use the same numbers and a failure means the code is wrong rather than
/// the fluid having moved. No solver is involved: a residual is a function, and this is what it
/// returns.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FlowComponentTests
{
    private static readonly ISubstance Water = ConstantPropertyWater.Instance;

    private static PortState State(double enthalpy = 0, double pressure = 0) => new()
    {
        Pressure = pressure,
        Enthalpy = enthalpy,
        Temperature = 293.15,
        Density = ConstantPropertyWater.DensityValue,
        SpecificHeat = ConstantPropertyWater.SpecificHeatValue,
        DynamicViscosity = ConstantPropertyWater.DynamicViscosityValue,
        ThermalConductivity = ConstantPropertyWater.ThermalConductivityValue,
    };

    [Fact]
    public void AnInteriorNodeCarriesEnergyOnlyAndAJunctionCarriesBoth()
    {
        // The asymmetry 22 insists on. A degree-two node inside a branch has one flow in and the same
        // flow out, so its mass balance would be a row of zeros — singular by construction.
        Assert.Equal(1, new CircuitNode("N1", 2, carriesMassBalance: false).EquationCount);
        Assert.Equal(2, new CircuitNode("N2", 3, carriesMassBalance: true).EquationCount);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    public void ANodesEquationCountDoesNotDependOnTheSolvedFlowDirection(double flow)
    {
        // 22's acceptance criterion, and the reason the energy balance is written over every port with
        // signed flows rather than only over ports that carry flow: a count that moved with a solved
        // direction would change the system's size between Newton iterations.
        var node = new CircuitNode("N1", 2, carriesMassBalance: false);
        var before = node.EquationCount;

        Span<double> residuals = stackalloc double[node.EquationCount];
        node.EvaluateResiduals(
            new SolveContext(Water, [State(), State()], [flow, -flow], [0.0, 0.0]),
            residuals);

        Assert.Equal(before, node.EquationCount);
    }

    [Fact]
    public void ANodeMixesTwoStreamsByMassWeightedEnthalpy()
    {
        // 0.3 kg/s at 200 kJ/kg and 0.1 kg/s at 100 kJ/kg leave together at 0.4 kg/s, so the mixed
        // enthalpy is (0.3 x 200 + 0.1 x 100) / 0.4 = 70/0.4 = 175 kJ/kg. The energy residual is zero
        // there and nowhere else.
        var node = new CircuitNode("N1", 3, carriesMassBalance: true);

        Span<double> residuals = stackalloc double[2];
        node.EvaluateResiduals(
            new SolveContext(
                Water,
                [State(enthalpy: 200_000), State(enthalpy: 100_000), State(enthalpy: 175_000)],
                [0.3, 0.1, -0.4],
                [0.0, 175_000]),
            residuals);

        Assert.Equal(0, residuals[0], 1e-12);
        Assert.Equal(0, residuals[1], 1e-6);
    }

    [Fact]
    public void ANodesMassResidualIsTheSumOfItsFlows()
    {
        var node = new CircuitNode("N1", 3, carriesMassBalance: true);

        Span<double> residuals = stackalloc double[2];
        node.EvaluateResiduals(
            new SolveContext(Water, [State(), State(), State()], [0.3, 0.1, -0.35], [0.0, 0.0]),
            residuals);

        Assert.Equal(0.05, residuals[0], 1e-12);
    }

    [Fact]
    public void UpwindingIsContinuousAndSmoothThroughZeroFlow()
    {
        // The band is 1e-3 kg/s (36's upwind.smoothing_band). Outside it nothing is approximated;
        // inside it the blend has to be continuous in value and in slope, or a reversal puts a cliff in
        // the Jacobian.
        const double Band = 1e-3;
        // Outside the band nothing is approximated at all.
        Assert.Equal(50_000, Enthalpy(-2e-3), 1e-9);
        Assert.Equal(100_000, Enthalpy(2e-3), 1e-9);

        // Inside it, monotone from one side to the other.
        var previous = double.NegativeInfinity;

        for (var flow = -1.2e-3; flow <= 1.2e-3; flow += 1e-5)
        {
            var value = Enthalpy(flow);

            Assert.True(value >= previous - 1e-9, $"h fell at {flow:G4} kg/s.");
            previous = value;
        }

        // And C¹ at the joins. A finite difference cannot show that by comparing neighbouring slopes:
        // the true derivative sweeps from 0 to 3.75e7 J/kg per kg/s across a band 2 g/s wide, so
        // neighbouring differences are *meant* to be far apart. What C¹ actually claims is that the
        // one-sided derivative at each edge is zero — the blend meets each branch with that branch's own
        // slope, and outside the band that slope is flat. So probe the edge with a shrinking step and
        // require the measured derivative to shrink with it. A cliff would hold it constant.
        foreach (var edge in new[] { -Band, Band })
        {
            var coarse = Math.Abs(Slope(edge, 1e-7));
            var fine = Math.Abs(Slope(edge, 1e-8));

            Assert.True(
                fine < coarse / 5,
                $"At {edge:G3} kg/s the one-sided slope barely moved when the probe shrank tenfold — "
                + $"{coarse:G4} then {fine:G4}. That is a corner, not a smooth join.");
        }

        static double Slope(double edge, double step) =>
            edge < 0
                ? (Enthalpy(edge + step) - Enthalpy(edge)) / step
                : (Enthalpy(edge) - Enthalpy(edge - step)) / step;

        static double Enthalpy(double flow)
        {
            var node = new CircuitNode("N1", 1, carriesMassBalance: false);

            Span<double> residuals = stackalloc double[1];
            node.EvaluateResiduals(
                new SolveContext(Water, [State(enthalpy: 100_000)], [flow], [0.0, 50_000]),
                residuals);

            // The single-port residual is flow x h(flow), so dividing recovers the upwinded enthalpy.
            return flow == 0 ? 50_000 : residuals[0] / flow;
        }
    }

    [Theory]
    [InlineData(4_000, 0.0)]
    [InlineData(1e5, 0.0)]
    [InlineData(1e5, 0.001)]
    [InlineData(1e6, 0.01)]
    [InlineData(1e7, 0.05)]
    [InlineData(1e8, 0.05)]
    [InlineData(1e8, 0.0)]
    public void TheFrictionFactorMatchesColebrookWhite(double reynolds, double relativeRoughness)
    {
        // 22's acceptance criterion: within 0.01 % over Re = 4e3 to 1e8 and eps/D = 0 to 0.05. The
        // oracle is the implicit Colebrook-White equation solved here by fixed-point iteration, which
        // shares no code with Serghide's explicit approximation of it.
        const double diameter = 0.1;

        var pipe = new Pipe("P1", length: 1, insideDiameter: diameter,
            roughness: relativeRoughness * diameter);

        var serghide = pipe.FrictionFactor(reynolds);
        var colebrook = Colebrook(reynolds, relativeRoughness);
        var deviation = Math.Abs(serghide - colebrook) / colebrook;

        Assert.True(
            deviation <= 1e-4,
            $"Re={reynolds:G3}, eps/D={relativeRoughness}: Serghide {serghide:G8} against "
            + $"Colebrook {colebrook:G8}, {deviation:P4} apart.");

        static double Colebrook(double reynolds, double relativeRoughness)
        {
            // 1/sqrt(f) = -2 log10(eps/(3.7 D) + 2.51/(Re sqrt(f))), iterated to machine agreement.
            var inverseRoot = 4.0;

            for (var iteration = 0; iteration < 200; iteration++)
            {
                var next = -2 * Math.Log10(
                    (relativeRoughness / 3.7) + (2.51 * inverseRoot / reynolds));

                if (Math.Abs(next - inverseRoot) < 1e-13)
                {
                    inverseRoot = next;

                    break;
                }

                inverseRoot = next;
            }

            return 1 / (inverseRoot * inverseRoot);
        }
    }

    [Fact]
    public void APipesPressureDropMatchesAHandComputedCase()
    {
        // DN25 steel, 27.3 mm bore, 10 m, 0.5 kg/s of 20 C water.
        //   A = pi x 0.0273^2 / 4        = 5.8535e-4 m^2
        //   v = 0.5 / (998.2 x A)        = 0.8557 m/s
        //   Re = 998.2 x 0.8557 x 0.0273 / 1.002e-3 = 23 271        -> turbulent
        //   f (Serghide, eps = 0.045 mm) = 0.028445
        //   dp = f x (L/D) x rho v^2 / 2 = 0.028445 x 366.30 x 365.45 = 3 807 Pa
        var pipe = new Pipe("P1", length: 10, insideDiameter: 0.0273);
        var velocity = 0.5 / (ConstantPropertyWater.DensityValue * pipe.FlowArea);

        var drop = pipe.PressureDrop(
            velocity, ConstantPropertyWater.DensityValue, ConstantPropertyWater.DynamicViscosityValue);

        Assert.Equal(3807.0, drop, tolerance: 40.0);
    }

    [Fact]
    public void APipeAtRestHasNoPressureDropAndAFiniteSlope()
    {
        // The case f = 64/Re cannot evaluate: the factor diverges as the term it multiplies vanishes.
        // Written as 32 mu L v / D^2 the laminar branch is linear, exactly zero here, and has a slope.
        var pipe = new Pipe("P1", length: 10, insideDiameter: 0.0273);

        var atRest = pipe.PressureDrop(0, ConstantPropertyWater.DensityValue, ConstantPropertyWater.DynamicViscosityValue);
        var nudged = pipe.PressureDrop(1e-9, ConstantPropertyWater.DensityValue, ConstantPropertyWater.DynamicViscosityValue);

        Assert.Equal(0, atRest);
        Assert.True(double.IsFinite(nudged), $"A nudge off rest gave {nudged}.");
        Assert.True(double.IsFinite((nudged - atRest) / 1e-9), "The slope at rest is not finite.");
    }

    [Fact]
    public void ReversedFlowOpposesItself()
    {
        // v|v| rather than v^2: a reversed flow must lose pressure in the direction it is going, not
        // gain it. The magnitudes match and the signs do not.
        var pipe = new Pipe("P1", length: 10, insideDiameter: 0.0273);

        var forward = pipe.PressureDrop(0.9, ConstantPropertyWater.DensityValue, ConstantPropertyWater.DynamicViscosityValue);
        var reverse = pipe.PressureDrop(-0.9, ConstantPropertyWater.DensityValue, ConstantPropertyWater.DynamicViscosityValue);

        Assert.Equal(forward, -reverse, 1e-9);
        Assert.True(forward > 0);
    }

    [Fact]
    public void TheLaminarTurbulentTransitionIsContinuousInValueAndSlope()
    {
        // 22's acceptance criterion. Blended rather than switched, because a discontinuity in the
        // residual is a discontinuity in the Jacobian and Newton does not survive one.
        var pipe = new Pipe("P1", length: 10, insideDiameter: 0.0273);
        var step = 1e-5;
        var previous = double.NaN;

        for (var velocity = 0.02; velocity < 0.30; velocity += 1e-3)
        {
            var slope = (Drop(velocity + step) - Drop(velocity)) / step;

            if (!double.IsNaN(previous))
            {
                Assert.True(
                    Math.Abs(slope - previous) < 0.2 * Math.Max(Math.Abs(previous), 1),
                    $"At v={velocity:F3} m/s the slope jumped from {previous:G5} to {slope:G5}.");
            }

            previous = slope;
        }

        double Drop(double velocity) => pipe.PressureDrop(
            velocity, ConstantPropertyWater.DensityValue, ConstantPropertyWater.DynamicViscosityValue);
    }

    [Fact]
    public void APipesResidualIsTheStatedDropMinusTheComputedOne()
    {
        var pipe = new Pipe("P1", length: 10, insideDiameter: 0.0273);
        var velocity = 0.5 / (ConstantPropertyWater.DensityValue * pipe.FlowArea);
        var expected = pipe.PressureDrop(
            velocity, ConstantPropertyWater.DensityValue, ConstantPropertyWater.DynamicViscosityValue);

        Span<double> residuals = stackalloc double[1];
        pipe.EvaluateResiduals(
            new SolveContext(Water, [State(pressure: expected), State(pressure: 0)], [0.5, -0.5]),
            residuals);

        Assert.Equal(0, residuals[0], 1e-9);
    }

    [Fact]
    public void ElevationCostsRhoGH()
    {
        // 10 m up, 998.2 kg/m3: 998.2 x 9.80665 x 10 = 97 890 Pa, and the residual carries it whether
        // or not anything is flowing.
        var pipe = new Pipe("P1", length: 10, insideDiameter: 0.0273, elevation: 10);

        Span<double> residuals = stackalloc double[1];
        pipe.EvaluateResiduals(
            new SolveContext(Water, [State(), State()], [0.0, 0.0]),
            residuals);

        Assert.Equal(-97_890.0, residuals[0], tolerance: 5.0);
    }

    [Fact]
    public void EvaluateResidualsAllocatesNothing()
    {
        // 22's acceptance criterion, and the reason SolveContext carries evaluated properties instead
        // of a substance to ask: this runs N+1 times per Newton iteration.
        var node = new CircuitNode("N1", 3, carriesMassBalance: true);
        var pipe = new Pipe("P1", length: 10, insideDiameter: 0.0273);

        // Every buffer is built once, outside the measured region. The first version of this test
        // allocated its own argument arrays inside the loop and reported 21 600 bytes -- all of them the
        // test's, none of them the components'. A zero-allocation assertion that measures its own
        // fixture is worse than none, because it fails for a reason the component cannot fix.
        var nodeStates = new[] { State(), State(), State() };
        var nodeFlows = new[] { 0.3, 0.1, -0.4 };
        var nodeUnknowns = new[] { 0.0, 0.0 };
        var pipeStates = new[] { State(pressure: 3000), State() };
        var pipeFlows = new[] { 0.5, -0.5 };
        var nodeResiduals = new double[2];
        var pipeResiduals = new double[1];

        Run();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);

        void Run()
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                node.EvaluateResiduals(
                    new SolveContext(Water, nodeStates, nodeFlows, nodeUnknowns),
                    nodeResiduals);

                pipe.EvaluateResiduals(new SolveContext(Water, pipeStates, pipeFlows), pipeResiduals);
            }
        }
    }
}
