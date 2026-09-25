using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Components;

/// <summary>
/// <c>D-69</c>'s flux: what a component adds to the energy balance of the node it discharges into.
/// </summary>
/// <remarks>
/// <para>
/// The exchanger's own share is in <c>HeatExchangerTests</c>. This covers the other half of the
/// decision — that the member is on <see cref="IFlowComponent"/> rather than on the exchanger, that a
/// pipe uses it for the energy side of its elevation, and that everything else contributes nothing
/// without having to say so.
/// </para>
/// <para>
/// <strong>The pipe's entries do not sum to zero, and that is correct.</strong> A duty is heat entering
/// the circuit and its two shares sum to <c>Q</c>; a rise is enthalpy becoming potential energy and its
/// shares sum to <c>−ṁgΔz</c>. What sums to zero is a closed loop's elevations, which is exactly the
/// property <c>D-70</c>'s absolute heights make structural.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class EnergyInjectionTests
{
    private const double Gravity = 9.80665;

    private const double Band = Tolerances.UpwindSmoothingBand;

    private static readonly ConstantPropertyWater Water = ConstantPropertyWater.Instance;

    [Fact]
    public void AlmostNothingInjectsEnergy()
    {
        // The interface default, and the reason it is a default: a valve throttles isenthalpically, a
        // pump's shaft work is not modelled as heat, and a node is a control volume that balances
        // rather than a path that adds. Each would otherwise need a method saying it does nothing.
        Assert.False(Injects(new ValveComponent("V1", kv: 6.3)));
        Assert.False(Injects(new PumpComponent("PU1", shutOffHead: 8, curvature: 20)));
        Assert.False(Injects(new NodeComponent("N1", portCount: 2, carriesMassBalance: false)));
        Assert.False(Injects(Level));

        Assert.True(Injects(new HeatExchangerComponent("HX1", power: 30_000)));
        Assert.True(Injects(Riser));

        // Reached through the interface, which is where a default member lives -- and where the
        // assembler reads it. A kind that wants to opt in overrides it and becomes visible here.
        static bool Injects(IFlowComponent component) => component.InjectsEnergy;
    }

    [Fact]
    public void OnlyAControlVolumeDeclaresAnEnergyRow()
    {
        // The structural half of D-69, swept over the whole sample corpus so the contradiction cannot
        // come back with the next kind. A node and a tank each own a control volume and balance it;
        // everything a stream passes *through* contributes a flux into somebody else's balance instead.
        // An exchanger declaring its own duty row was asserting the same relation as the node it
        // discharges into, with Q missing from one of them, and the assembled system carried one row
        // too many per exchanger -- invisible in every count until the two were compared.
        var offenders = new List<string>();

        foreach (var path in ScriptCorpus.EnumerateSampleFiles())
        {
            var graph = GraphFixture.Lower(File.ReadAllText(path)).Graph;

            offenders.AddRange(
                graph.Components
                    .Where(static component => component is not (NodeComponent or TankComponent))
                    .Where(static component =>
                        component.DeclareEquations().Any(static row => row.Kind == EquationKind.Energy))
                    .Select(component => $"{Path.GetFileName(path)}: {component.Name} ({component.Kind})"));
        }

        Assert.True(
            offenders.Count == 0,
            "An energy row belongs to whatever owns the control volume it balances. These are on the "
            + $"path instead, and should be injecting: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ALevelPipeContributesNothingAtAll()
    {
        // Not merely "reports false": the value has to be zero too, because InjectsEnergy is what the
        // assembler uses to skip the call, and a false negative there would silently drop a term.
        var injection = Injection(Level, 0.5);

        Assert.Equal(0, injection.Inlet, tolerance: 1e-12);
        Assert.Equal(0, injection.Outlet, tolerance: 1e-12);
    }

    [Fact]
    public void ARisingPipeTakesEnergyOutOfTheNodeAtTheTop()
    {
        // 0.5 kg/s up 10 m: m g dz = 0.5 x 9.80665 x 10 = 49.03 W, leaving the stream. Per kilogram
        // that is 98.1 J, which is the pv term and nothing else -- u, and therefore the temperature,
        // does not move (D-70).
        var injection = Injection(Riser, 0.5);

        Assert.Equal(-49.033, injection.Outlet, tolerance: 1e-3);
        Assert.Equal(0, injection.Inlet, tolerance: 1e-9);
    }

    [Fact]
    public void RunningTheOtherWayItPutsTheSameEnergyIntoTheNodeAtTheBottom()
    {
        // The whole point of the split. The same pipe, the same rise, the flow reversed: the energy
        // now lands on the other port and changes sign. Choosing the port structurally instead --
        // "the outlet" -- is what made a reversed circuit unsolvable rather than merely mis-signed.
        var injection = Injection(Riser, -0.5);

        Assert.Equal(49.033, injection.Inlet, tolerance: 1e-3);
        Assert.Equal(0, injection.Outlet, tolerance: 1e-9);
    }

    [Fact]
    public void TheShareSumsToWhatTheRiseCostsAtEveryFlow()
    {
        foreach (var flow in new[] { 2.0, 0.5, Band, 0.0, -Band, -0.5, -2.0 })
        {
            var injection = Injection(Riser, flow);

            Assert.Equal(
                -flow * Gravity * 10, injection.Inlet + injection.Outlet, tolerance: 1e-9);
        }
    }

    [Fact]
    public void TheSplitIsSmoothThroughZeroFlow()
    {
        // S-5's form of the check: C-1 is the one-sided derivatives agreeing at the join, not the
        // derivative being small near it. At the band edge the smoothstep's own slope is zero, so both
        // sides approach the same value as the step shrinks.
        foreach (var step in new[] { 1e-7, 1e-8, 1e-9 })
        {
            var inside = (Injection(Riser, Band).Outlet - Injection(Riser, Band - step).Outlet) / step;
            var outside = (Injection(Riser, Band + step).Outlet - Injection(Riser, Band).Outlet) / step;

            // Above the band the share is clamped at 1, so the quotient is exactly -g dz.
            Assert.Equal(-Gravity * 10, outside, tolerance: 1e-6);

            // Below it the share is still moving, and the quotient carries a truncation that has to be
            // predicted rather than tolerated: s(1 - d) = 1 - 3d^2 with d = step/(2.band), which puts
            // 3.g.dz.step/(4.band) on the inside quotient -- 7.4e-3, 7.4e-4 and 7.4e-5 across the three
            // steps. A flat tolerance either passes on a discontinuity or fails on correct code, which
            // is S-5's whole point.
            Assert.Equal(
                -Gravity * 10,
                inside,
                tolerance: (2 * 3 * Gravity * 10 * step / (4 * Band)) + 1e-6);
        }
    }

    [Fact]
    public void EvaluatingAnInjectionAllocatesNothing()
    {
        // It runs on the same hot path as EvaluateResiduals -- N+1 times per Newton iteration -- so it
        // is held to the same rule (22's invariant 2).
        var ports = new[] { State(300_000, 167_500), State(200_000, 167_500) };
        var flows = new[] { 0.5, -0.5 };
        var injection = new double[2];

        Evaluate();

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 1000; i++)
        {
            Evaluate();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        void Evaluate() =>
            Riser.EvaluateEnergyInjection(new SolveContext(Water, ports, flows), injection);
    }

    private static PipeComponent Level { get; } = new("P1", length: 10, insideDiameter: 0.0273);

    private static PipeComponent Riser { get; } = new("P2", length: 10, insideDiameter: 0.0273, rise: 10);

    private static (double Inlet, double Outlet) Injection(PipeComponent pipe, double flow)
    {
        Span<double> injection = stackalloc double[pipe.Ports.Length];
        pipe.EvaluateEnergyInjection(
            new SolveContext(
                Water,
                [State(300_000, 167_500), State(200_000, 167_500)],
                [flow, -flow]),
            injection);

        return (injection[0], injection[1]);
    }

    private static PortState State(double pressure, double enthalpy)
    {
        var state = Water.FromPressureEnthalpy(
            Quantity.FromSi(pressure, Dimension.Pressure),
            Quantity.FromSi(enthalpy, Dimension.Enthalpy));

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
    public void ACoupledExchangerTakesOutOfOneStreamWhatItPutsIntoTheOther()
    {
        // `S-31`, and it is a conservation law rather than a modelling preference. A script that wires
        // `in2`/`out2` and states `power` has said 150 kW crosses this component; injecting it on side 1
        // and nothing on side 2 **creates energy from nothing**. On `m2-substation` that showed as a
        // district primary sitting at 85 C from end to end instead of returning at 45, because nothing
        // ever took its heat away.
        var coupled = new HeatExchangerComponent(
            "HX1", power: 150_000, secondarySideConnected: true);

        Span<double> injection = stackalloc double[4];
        coupled.EvaluateEnergyInjection(
            new SolveContext(
                Water,
                [State(0, 0), State(0, 0), State(0, 0), State(0, 0)],
                [1.5, -1.5, -0.9, 0.9]),
            injection);

        // Side 1 gains the duty, side 2 loses it, and the whole component is neutral.
        Assert.Equal(150_000, injection[0] + injection[1], tolerance: 1e-9);
        Assert.Equal(-150_000, injection[2] + injection[3], tolerance: 1e-9);
        Assert.Equal(0, injection[0] + injection[1] + injection[2] + injection[3], tolerance: 1e-9);
    }

    [Fact]
    public void ADutyBlockWithNoSecondSideStillPutsItsWholeDutyIn()
    {
        // The other half of `S-31`: a one-sided exchanger is a source or a sink and is *meant* to be
        // unbalanced -- the heat comes from a boiler or goes to a room, neither of which is modelled.
        // Only a component the script wired on both sides makes the crossing claim.
        var duty = new HeatExchangerComponent("HE1", power: 30_000);

        Span<double> injection = stackalloc double[4];
        duty.EvaluateEnergyInjection(
            new SolveContext(Water, [State(0, 0), State(0, 0)], [0.24, -0.24]), injection);

        Assert.Equal(30_000, injection[0] + injection[1], tolerance: 1e-9);
        Assert.Equal(0, injection[2], tolerance: 1e-12);
        Assert.Equal(0, injection[3], tolerance: 1e-12);
    }

    [Fact]
    public void EachSideReadsItsOwnFlowDirectionBecauseTheStreamsRunOpposite()
    {
        // Counter-current is the usual arrangement, so the two sides' `ForwardShare` differ by design.
        // A shared share would put side 2's heat on the wrong port exactly when the streams are
        // counter-current, which is most of the time.
        var coupled = new HeatExchangerComponent("HX1", power: 100_000, secondarySideConnected: true);

        Span<double> injection = stackalloc double[4];
        coupled.EvaluateEnergyInjection(
            new SolveContext(
                Water,
                [State(0, 0), State(0, 0), State(0, 0), State(0, 0)],
                [2.0, -2.0, -2.0, 2.0]),
            injection);

        // Side 1 runs in at port 0, so its duty lands on `out`. Side 2 runs the other way, so its
        // withdrawal lands on `in2` -- the port its fluid leaves through.
        Assert.Equal(100_000, injection[1], tolerance: 1e-9);
        Assert.Equal(-100_000, injection[2], tolerance: 1e-9);
    }
}
