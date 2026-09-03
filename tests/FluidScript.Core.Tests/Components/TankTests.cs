using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;

namespace FluidScript.Core.Tests.Components;

/// <summary>The tank's steady contract: what it equalises, what it balances, and where a port sits.</summary>
/// <remarks>
/// The transient stack of layers is <c>33</c>'s. What is checkable here is the steady behaviour and the
/// port/parameter contract the transient will operate on — including the layer mapping, which is a rule
/// about integer boundaries and needs no solver at all.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TankTests
{
    private static readonly ConstantPropertyWater Water = ConstantPropertyWater.Instance;

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

    private static Tank FourPort() => new(
        "T1",
        inletElevations: [0.0, 0.9],
        outletElevations: [0.3, 1.0]);

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.3, 2)]
    [InlineData(0.9, 5)]
    [InlineData(1.0, 5)]
    public void AFiveLayerTankMapsElevationsToLayersExactly(double elevation, int expected)
    {
        // 22's acceptance criterion, and the reason the rule is written out rather than left to
        // rounding: a port exactly on a layer boundary must not land in different layers in two
        // implementations. 1.0 maps to the top layer, not to a sixth that does not exist.
        Assert.Equal(expected, Tank.LayerFor(elevation, layers: 5));
    }

    [Fact]
    public void ASingleLayerTankPutsEveryPortInLayerOne()
    {
        foreach (var elevation in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            Assert.Equal(1, Tank.LayerFor(elevation, layers: 1));
        }
    }

    [Fact]
    public void AFourPortTankHasOneMassBalanceAndThreePressureEqualities()
    {
        // 22's acceptance criterion. Four ports, so three equalities against the first; a junction, so
        // one mass balance; and the energy balance every tank carries.
        var tank = FourPort();
        var kinds = tank.DeclareEquations().Select(static equation => equation.Kind).ToImmutableArray();

        Assert.Equal(4, tank.Ports.Length);
        Assert.Equal(3, kinds.Count(static kind => kind == EquationKind.Pressure));
        Assert.Equal(1, kinds.Count(static kind => kind == EquationKind.Mass));
        Assert.Equal(1, kinds.Count(static kind => kind == EquationKind.Energy));
        Assert.Equal(tank.EquationCount, kinds.Length);
    }

    [Fact]
    public void ATwoPortTankCarriesNoMassBalance()
    {
        // The same reasoning as a node interior to a branch: one flow in and the same flow out, so the
        // row is an identity for every iterate and the Jacobian is singular by construction.
        var tank = new Tank("T1");

        Assert.Equal(2, tank.Ports.Length);
        Assert.False(tank.CarriesMassBalance);
        Assert.DoesNotContain(tank.DeclareEquations(), static e => e.Kind == EquationKind.Mass);
    }

    [Fact]
    public void ThePressureEqualitiesCarryNoHydrostaticTerm()
    {
        // 22's invariant. A normalized height is thermal metadata, not metres, and the script states no
        // vessel height -- so a port at the bottom and one at the top sit at the same pressure. A rho g
        // dz term here would be a different model wearing the same parameters.
        var tank = FourPort();

        Span<double> residuals = stackalloc double[tank.EquationCount];
        tank.EvaluateResiduals(
            new SolveContext(
                Water,
                [State(pressure: 250_000), State(pressure: 250_000), State(pressure: 250_000), State(pressure: 250_000)],
                [0.4, 0.0, -0.4, 0.0],
                [0.0]),
            residuals);

        // Bottom port at elevation 0 and top port at elevation 1.0, and every equality is satisfied.
        Assert.Equal(1, tank.LayerForPort(0));
        Assert.Equal(5, tank.LayerForPort(3));
        Assert.Equal(0, residuals[2], tolerance: 1e-12);
        Assert.Equal(0, residuals[3], tolerance: 1e-12);
        Assert.Equal(0, residuals[4], tolerance: 1e-12);
    }

    [Fact]
    public void APressureEqualityIsTheDifferenceFromTheFirstPort()
    {
        var tank = FourPort();

        Span<double> residuals = stackalloc double[tank.EquationCount];
        tank.EvaluateResiduals(
            new SolveContext(
                Water,
                [State(pressure: 200_000), State(pressure: 205_000), State(pressure: 200_000), State(pressure: 200_000)],
                [0.4, 0.0, -0.4, 0.0],
                [0.0]),
            residuals);

        Assert.Equal(5_000, residuals[2], tolerance: 1e-9);
    }

    [Fact]
    public void ASteadyTankGivesOneMixedEnthalpyToEveryOutflow()
    {
        // 22's acceptance criterion. 0.3 kg/s in at 200 kJ/kg and 0.1 at 100 leave together at 0.4, so
        // the mixed enthalpy is (0.3 x 200 + 0.1 x 100) / 0.4 = 175 kJ/kg, and the energy residual is
        // zero exactly there.
        var tank = new Tank("T1", inletElevations: [0.2, 0.8], outletElevations: [0.5]);

        Span<double> residuals = stackalloc double[tank.EquationCount];
        tank.EvaluateResiduals(
            new SolveContext(
                Water,
                [State(enthalpy: 200_000), State(enthalpy: 100_000), State(enthalpy: 175_000)],
                [0.3, 0.1, -0.4],
                [175_000.0]),
            residuals);

        Assert.Equal(0, residuals[0], tolerance: 1e-6);
        Assert.Equal(0, residuals[1], tolerance: 1e-12);
    }

    [Fact]
    public void TheSteadyAnswerDoesNotDependOnTheLayerCount()
    {
        // 22: volume, layers and elevations have no steady effect, which is what makes layers=1
        // identical to the steady behaviour of every larger count. Asserted rather than assumed,
        // because the transient work in 33 will be tempted to make layers matter everywhere.
        var residualsByLayerCount = new List<double>();

        // Allocated once, not per iteration: a stackalloc inside a loop keeps claiming frame space it
        // never gives back, and the analyzer is right to refuse it. Two ports and no mass balance, so
        // the count is the same for every layer count -- which is half of what this test asserts.
        var residuals = new double[2];

        foreach (var layers in new[] { 1, 2, 5, 100 })
        {
            var tank = new Tank("T1", inletElevations: [0.2], outletElevations: [0.5], layers: layers);

            Assert.Equal(residuals.Length, tank.EquationCount);
            tank.EvaluateResiduals(
                new SolveContext(
                    Water,
                    [State(enthalpy: 200_000), State(enthalpy: 160_000)],
                    [0.4, -0.4],
                    [160_000.0]),
                residuals);

            residualsByLayerCount.Add(residuals[0]);
        }

        Assert.All(residualsByLayerCount, value => Assert.Equal(residualsByLayerCount[0], value, tolerance: 1e-12));
    }

    [Fact]
    public void AnInletWithReverseFlowDrawsFromItsLayer()
    {
        // Every port is bidirectional at solve time whatever it is called: the solved sign decides. An
        // "inlet" running backwards carries the tank's own enthalpy out, not the arriving one.
        var tank = new Tank("T1", inletElevations: [0.2], outletElevations: [0.5]);

        Span<double> residuals = stackalloc double[tank.EquationCount];
        tank.EvaluateResiduals(
            new SolveContext(
                Water,
                [State(enthalpy: 200_000), State(enthalpy: 160_000)],
                [-0.4, 0.4],
                [160_000.0]),
            residuals);

        // Out of in1 at the tank's 160 kJ/kg, in through out1 at the arriving 160 kJ/kg: balanced.
        Assert.Equal(0, residuals[0], tolerance: 1e-6);
        Assert.All(tank.Ports, port => Assert.Equal(PortRole.Bidirectional, port.Role));
    }

    [Fact]
    public void TheDefaultsAreTheOnesTheDecisionFixed()
    {
        // D-32 makes these visible decided defaults rather than sized values, because the graph cannot
        // infer any of them. 300 dm3 is held as 0.3 m3.
        var tank = new Tank("T1");

        Assert.Equal(0.3, tank.Volume, tolerance: 1e-12);
        Assert.Equal(5, tank.Layers);
        Assert.Equal([0.5, 0.5], tank.PortElevations);
        Assert.Equal(["in1", "out1"], tank.Ports.Select(static port => port.Name));
    }

    [Fact]
    public void OnlyTheFirstInletAndOutletAreMandatory()
    {
        // in1 and out1 always exist; higher ports materialize only when named. Inference rule I3 skips
        // the optional ones, so a two-port declaration needs no fabricated nodes.
        var tank = FourPort();

        Assert.Equal(["in1", "in2", "out1", "out2"], tank.Ports.Select(static port => port.Name));
        Assert.Equal([false, true, false, true], tank.Ports.Select(static port => port.IsOptional));
    }

    [Fact]
    public void EvaluateResidualsAllocatesNothing()
    {
        var tank = FourPort();
        var ports = new[] { State(200_000, 250_000), State(150_000, 250_000), State(0, 250_000), State(0, 250_000) };
        var flows = new[] { 0.3, 0.1, -0.25, -0.15 };
        var unknowns = new[] { 175_000.0 };
        var residuals = new double[tank.EquationCount];

        Run();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void Run()
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                tank.EvaluateResiduals(new SolveContext(Water, ports, flows, unknowns), residuals);
            }
        }
    }
}
