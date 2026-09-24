using FluidScript.Core.Diagnostics;
using FluidScript.Core.Topology.Counting;

namespace FluidScript.Core.Tests.Topology.Construction;

/// <summary>A control line's setpoint is the design point of its loop (<c>D-141</c>, <c>S-75</c>).</summary>
public sealed class SetpointTests
{
    private const string DemandStep = """
        fluidscript 1
        circuit demandStep
        fluid water

        HE1 heat_exchanger power=30 out.t=50
        3WV three_way_valve
        PU1 pump
        P1  pipe length=25
        PB  pipe length=8 dn=20 nodes=4
        TC1 pi

        control actuate=3WV.position measure=NS.t by=TC1 setpoint=20

        connections
        N1 - N2
        N2 - NS
        NS - PU1
        PU1 - HE1
        HE1 - 3WV
        3WV - PB - N2
        3WV - P1
        P1 - N3

        N1 inlet t=6 p=300
        N3 outlet p=280
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void ASetpointOnAnUnstatedActuatorIsANodeTemperatureAnsweredByThatActuator()
    {
        // `01`'s demand-step loop states no `in.t` and no valve position: the setpoint is what makes it
        // well posed. Lowering writes 20 C into NS's stated parameters, the constraint is a node
        // temperature, and the promotion is the valve the line names -- not the nearest split, which
        // here is the same valve but need not be.
        var graph = GraphFixture.Lower(DemandStep).Graph;
        var setpoint = Assert.Single(graph.Setpoints);

        Assert.True(setpoint.Applied);
        Assert.Equal("TC1", setpoint.Controller);
        Assert.Equal(("NS", "t", "3WV", "position"), (setpoint.Measured, setpoint.Parameter, setpoint.ActuatorComponent, setpoint.ActuatorParameter));

        var node = Assert.Single(graph.Components, static c => c.Name == "NS");
        Assert.Equal(293.15, node.StatedParameters["t"].SiValue, 1e-9);

        var result = WellPosedness.Check(graph);
        var promotion = Assert.Single(result.Counting.Promotions, static p => p.Constraint.Component == "NS");

        Assert.Equal(ConstraintKind.NodeTemperature, promotion.Constraint.Kind);
        Assert.Equal(("3WV", "position"), (promotion.Component, promotion.Parameter));
        Assert.Equal(0, result.Counting.Excess);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code is "FS3210" or "FS3211");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStatedActuatorLeavesTheSetpointOutAndSaysTheRunStartsOffIt()
    {
        // `3WV position=0.4` written by the user is the user's design; the solve cannot also hold NS
        // at 20 C with it, so the setpoint is not a constraint and FS3210 says why.
        var graph = GraphFixture.Lower(DemandStep.Replace("3WV three_way_valve", "3WV three_way_valve position=0.4", StringComparison.Ordinal)).Graph;
        var setpoint = Assert.Single(graph.Setpoints);

        Assert.False(setpoint.Applied);
        Assert.Equal("'3WV.position' is stated", setpoint.Reason);
        Assert.False(Assert.Single(graph.Components, static c => c.Name == "NS").StatedParameters.ContainsKey("t"));

        var info = Assert.Single(WellPosedness.Check(graph).Diagnostics, static d => d.Code == "FS3210");

        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
        Assert.Contains("TC1 may start off its setpoint: '3WV.position' is stated, so the design solve did not hold NS.t at 20 °C", info.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ANeighbouringStatedTerminalAlreadyFixesTheNodeAndTheSetpointIsNotAppliedTwice()
    {
        // The ladder's step 10: the sensor sits on HE1's outlet node and HE1 states out.t=50, so a
        // setpoint of 50 there is the same statement twice -- square by count, singular in truth, and
        // non-finite in five iterations before this guard.
        const string source = """
            fluidscript 1
            circuit ladder
            fluid water

            PU1  pump
            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30
            CV1  valve kv=6.3
            PID1 pid kp=2
            TE1  t_sensor at N1

            connections
            PU1 - HE1
            HE1 - N1
            N1 - LOAD
            LOAD - CV1
            CV1 - PU1

            control CV1 with TE1 by PID1 setpoint=50
            """;

        var graph = GraphFixture.Lower(source).Graph;
        var setpoint = Assert.Single(graph.Setpoints);

        // The sensor's node, not the sensor, is what is measured.
        Assert.Equal("N1", setpoint.Measured);
        Assert.False(setpoint.Applied);
        Assert.Equal("'HE1.out.t' already fixes it", setpoint.Reason);
        Assert.Contains(WellPosedness.Check(graph).Diagnostics, static d => d.Code == "FS3210");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AMeasurementTheDesignSolveCannotHoldIsSaidRatherThanDropped()
    {
        // A boundary's temperature is what enters the model, not a demand on it; the loop still runs
        // from wherever the design solve lands, and FS3211 says so.
        var graph = GraphFixture.Lower(DemandStep.Replace("measure=NS.t", "measure=N1.t", StringComparison.Ordinal)).Graph;
        var setpoint = Assert.Single(graph.Setpoints);

        Assert.False(setpoint.Applied);
        Assert.Null(setpoint.Reason);

        var info = Assert.Single(WellPosedness.Check(graph).Diagnostics, static d => d.Code == "FS3211");

        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
        Assert.Contains("TC1 measures N1.t, which the design solve cannot hold at a setpoint", info.Message, StringComparison.Ordinal);
    }

    private const string Circulator = """
        fluidscript 1
        circuit ladder
        fluid water

        PU1  pump
        HE1  heat_exchanger power=30 out.t=50
        TE_R t_sensor at NR
        TC1  pid kp=2
        LOAD heat_exchanger power=-30

        connections
        PU1 - HE1
        HE1 - NS - LOAD
        LOAD - NR - PU1

        control PU1 with TE_R by TC1 setpoint=20
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void APumpHoldsItsSetpointThroughItsHeadAtTheDesignPoint()
    {
        // `D-163`, `S-87`: a circulator in constant-temperature mode. Its speed moves nothing at the design point,
        // where the curve is anchored at the design flow, so NR.t = 20 C is answered by PU1.head -- the head that
        // sets the flow 30 kW needs over 20 -> 50 C -- and the run moves the speed from 1.
        var graph = GraphFixture.Lower(Circulator).Graph;
        var setpoint = Assert.Single(graph.Setpoints);

        Assert.True(setpoint.Applied);
        Assert.Equal(("PU1", "speed"), (setpoint.ActuatorComponent, setpoint.ActuatorParameter));

        var result = WellPosedness.Check(graph);
        var promotion = Assert.Single(result.Counting.Promotions, static p => p.Constraint.Component == "NR");

        Assert.Equal(("PU1", "head"), (promotion.Component, promotion.Parameter));
        Assert.Equal(0, result.Counting.Excess);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code is "FS3210" or "FS3211");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("PU1  pump head=4", "head")]
    [InlineData("PU1  pump flow=0.24", "flow")]
    public void APumpThatStatesItsRiseOrItsFlowHasNoHeadLeftForASetpoint(string pump, string stated)
    {
        var graph = GraphFixture.Lower(Circulator.Replace("PU1  pump", pump, StringComparison.Ordinal)).Graph;
        var setpoint = Assert.Single(graph.Setpoints);

        Assert.False(setpoint.Applied);
        Assert.Equal($"'PU1.{stated}' is stated, so the pump has no head left to hold it with", setpoint.Reason);
        Assert.Contains(WellPosedness.Check(graph).Diagnostics, static d => d.Code == "FS3210");
    }
}
