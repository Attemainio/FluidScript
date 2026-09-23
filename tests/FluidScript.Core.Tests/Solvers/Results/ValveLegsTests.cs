using FluidScript.Core.Components;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology.Graph;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers.Results;

/// <summary>How a three-way valve's bypass leg is told from the leg it exchanges flow through (<c>C-66</c>).</summary>
public sealed class ValveLegsTests
{
    [Fact]
    public void TheCommonLegIsTheOneNamedAbRatherThanTheOneCarryingTheMostFlow()
    {
        // `C-66`. The two disagree on the first pass and only there, which is the pass that matters:
        // sizing runs against the *seed*, and the seed satisfies mass balance with whichever orientation
        // the branch decomposition chose. Measured on the header's seed, the `a` leg carried 0.5736 kg/s
        // against 0.2868 on the other two, so "largest flow" named the wrong leg -- after which the two
        // remaining legs shared a far end, were equidistant, and every valve on the circuit declined.
        var (graph, legs, valve) = Legs("m2-cooling-loop.fluid");

        var flows = new double[legs.Length];
        var ab = Array.FindIndex(legs, leg => string.Equals(
            ReferenceEquals(leg.From.Element, valve) ? leg.From.PortName : leg.To.PortName,
            "ab",
            StringComparison.Ordinal));

        Assert.True(ab >= 0, "the fixture should name its ports");

        // Give a *different* leg the largest flow, so the two rules cannot agree by accident.
        flows[(ab + 1) % legs.Length] = 99;

        Assert.Equal(ab, ValveLegs.Common(legs, flows, valve));
    }

    [Fact]
    public void WithNoPortNamedAbTheLargestFlowIsStillTheFallback()
    {
        // A script may connect a valve without naming ports, where binding is positional and the letters
        // carry no meaning. Mass balance is then the only thing that can answer, and at a converged
        // iterate it is right.
        var (_, legs, valve) = Legs("m2-cooling-loop.fluid");
        var flows = new double[legs.Length];

        flows[2] = 7;

        var unnamed = Array.ConvertAll(legs, leg => new Branch
        {
            From = leg.From with { PortName = null },
            To = leg.To with { PortName = null },
            Path = leg.Path,
            Index = leg.Index,
        });

        Assert.Equal(2, ValveLegs.Common(unnamed, flows, valve));
    }

    [Fact]
    public void ALegThatLeavesThroughABoundaryCannotGetBackAndIsTheVariableOne()
    {
        // `m2-cooling-loop`: the recirculation leg reaches the mixing node and closes the secondary; the
        // controlled leg runs out through `N3 return p=280` and has no way back to the valve's own loop
        // at all. Unreachable is the strongest possible statement that a leg is not the bypass.
        var (graph, legs, valve) = Legs("m2-cooling-loop.fluid");

        var common = ValveLegs.Common(legs, new double[legs.Length], valve);
        var variable = ValveLegs.Variable(graph, legs, common, valve);

        Assert.InRange(variable, 0, legs.Length - 1);
        Assert.NotEqual(common, variable);

        var anchor = ValveLegs.Far(legs[common], valve);

        Assert.Equal(
            int.MaxValue,
            ValveLegs.Distance(graph, ValveLegs.Far(legs[variable], valve), anchor, valve));

        // And the leg it did not pick does get back, which is what makes it the bypass.
        var bypass = Enumerable.Range(0, legs.Length).Single(i => i != common && i != variable);

        Assert.NotEqual(
            int.MaxValue,
            ValveLegs.Distance(graph, ValveLegs.Far(legs[bypass], valve), anchor, valve));
    }

    [Fact]
    public void TheBypassLegIsStrictlyCloserToTheCommonLegThanTheVariableOne()
    {
        // The criterion itself, stated as the inequality it is. A first version of this test asserted that
        // barring something other than the valve leaves the far end reachable -- which is not a property of
        // this fixture at all: `m2-cooling-loop`'s controlled leg ends at `N3 return p=280`, a boundary, so
        // it is unreachable however the walk is barred.
        var (graph, legs, valve) = Legs("m2-cooling-loop.fluid");

        var common = ValveLegs.Common(legs, new double[legs.Length], valve);
        var anchor = ValveLegs.Far(legs[common], valve);
        var variable = ValveLegs.Variable(graph, legs, common, valve);
        var bypass = Enumerable.Range(0, legs.Length).Single(i => i != common && i != variable);

        Assert.True(
            ValveLegs.Distance(graph, ValveLegs.Far(legs[bypass], valve), anchor, valve)
                < ValveLegs.Distance(graph, ValveLegs.Far(legs[variable], valve), anchor, valve),
            "the bypass leg closes the valve's own loop, so it is the nearer of the two");
    }

    [Fact]
    public void AnElementIsNoDistanceFromItself()
    {
        var (graph, legs, valve) = Legs("m2-cooling-loop.fluid");
        var anchor = ValveLegs.Far(legs[0], valve);

        Assert.Equal(0, ValveLegs.Distance(graph, anchor, anchor, valve));
    }

    [Fact]
    public void AWrittenPortLetterNamesTheControlLegAndTheWalkIsNotConsulted()
    {
        // `D-88`. `m2-distribution-header` writes `TV_AHU.a - PA2 - N5` and `TV_AHU.b - NM_AHU`, so the
        // script has said which leg the valve modulates -- and it is the same thing the residuals say,
        // since `ThreeWayValve` gives port `a` the opening `position` and `b` its complement. Sizing has
        // to read the legs the way the equations do or it sizes a coefficient for the other path.
        var (graph, legs, valve) = Legs("m2-distribution-header.fluid", "TV_AHU");

        Assert.Contains("TV_AHU.a", graph.StatedPorts);

        var common = ValveLegs.Common(legs, new double[legs.Length], valve);
        var variable = ValveLegs.Variable(graph, legs, common, valve);

        Assert.Equal("a", ValveLegs.PortName(legs[variable], valve));
    }

    [Fact]
    public void AnInferredPortLetterIsNotBelieved()
    {
        // The reason `D-88` needed a new signal rather than just reading `BranchEnd.PortName`. Lowering
        // resolves an unqualified endpoint to a real port and records its name like any other, so every
        // leg carries a letter whether or not anyone wrote one. On `m2-cooling-loop`, whose valve is
        // wired `HE1 - 3WV` / `3WV - N2` / `3WV - P1`, positional binding hands out `ab`, `a`, `b` in
        // connection order -- putting `a` on the *recirculation* leg and `b` on the control leg, exactly
        // backwards. Believing that letter sizes the valve against a branch with almost no resistance
        // behind it, which asks for a large Kv and yields no authority over the path it controls;
        // measured, it also stopped the sample converging at all.
        var (graph, legs, valve) = Legs("m2-cooling-loop.fluid");

        Assert.Empty(graph.StatedPorts);

        var common = ValveLegs.Common(legs, new double[legs.Length], valve);
        var variable = ValveLegs.Variable(graph, legs, common, valve);

        // The walk's answer, and the opposite of what the inferred letter would have said.
        Assert.Equal("b", ValveLegs.PortName(legs[variable], valve));
        Assert.False(ValveLegs.Stated(graph, legs[variable], valve));
    }

    [Fact]
    public void NamingOnlyTheBypassIsEnoughToNameTheOtherLeg()
    {
        // Either word settles it: a script that writes `b` on one switched leg has said the remaining one
        // varies, whether or not it also wrote `a`. This is the shape a user reaches for when the bypass
        // is the leg they are thinking about -- it is the one carrying the balancing valve.
        //
        // The header's own shape, with the AHU's supply tap written bare. A bare connection takes the
        // first free port in `ab`, `a`, `b` order, so it has to come after the line that names `ab` or it
        // would take `ab` itself and the named one would then collide with it.
        const string source = """
            fluidscript 1
            circuit heating 100
            fluid water
            HS1     heat_exchanger out.t=80
            connections
            N1 - HS1 - N3
            N3 - N4
            N6 - N5
            N5 - N1
            N1 node p=250

            circuit AHU 101
            HE_AHU  load in.t=50 out.t=30 power=24 kW
            TV_AHU  three_way_valve
            PU_AHU  pump
            PA1     pipe length=12 dn=25
            PA2     pipe length=12 dn=25
            connections
            TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU
            NM_AHU - TV_AHU.b
            N3 - PA1 - TV_AHU
            NM_AHU - PA2 - N5

            circuit radiators 102
            HE_RAD  load in.t=50 out.t=30 power=30 kW
            TV_RAD  three_way_valve
            PU_RAD  pump
            PR1     pipe length=18 dn=25
            PR2     pipe length=18 dn=25
            connections
            N4 - PR1 - TV_RAD.a
            NM_RAD - TV_RAD.b
            TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD
            NM_RAD - PR2 - N6
            """;

        var graph = GraphFixture.Lower(source).Graph;
        var valve = graph.Components.OfType<ThreeWayValve>().Single(
            static v => string.Equals(v.Name, "TV_AHU", StringComparison.Ordinal));
        var legs = graph.Branches
            .Where(branch =>
                ReferenceEquals(branch.From.Element, valve) || ReferenceEquals(branch.To.Element, valve))
            .ToArray();

        Assert.DoesNotContain("TV_AHU.a", graph.StatedPorts);
        Assert.Contains("TV_AHU.b", graph.StatedPorts);

        var common = ValveLegs.Common(legs, new double[legs.Length], valve);
        var variable = ValveLegs.Variable(graph, legs, common, valve);

        Assert.InRange(variable, 0, legs.Length - 1);
        Assert.NotEqual("b", ValveLegs.PortName(legs[variable], valve));
    }

    private static (CircuitGraph Graph, Branch[] Legs, IFlowComponent Valve) Legs(
        string sample, string? name = null)
    {
        var graph = GraphFixture.Lower(
            File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph;
        var valve = graph.Components.OfType<ThreeWayValve>().Single(
            v => v.BypassConnected
                && (name is null || string.Equals(v.Name, name, StringComparison.Ordinal)));
        var legs = graph.Branches
            .Where(branch =>
                ReferenceEquals(branch.From.Element, valve) || ReferenceEquals(branch.To.Element, valve))
            .ToArray();

        Assert.Equal(3, legs.Length);

        return (graph, legs, valve);
    }
}
