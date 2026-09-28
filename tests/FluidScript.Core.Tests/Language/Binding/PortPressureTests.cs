using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;


namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>
/// A port's pressure is the pressure of the node the port touches, and stating it states that node
/// (<c>D-120</c> rule 3, decided as <c>D-124</c>): <c>V1 valve out.p=100</c> is <c>N1 node p=100</c>
/// with <c>V1 - N1</c>.
/// </summary>
/// <remarks>
/// The pressure lives on the node in every reader -- the counting, the datum pick, <c>FS2210</c>, the
/// solve -- so the tests assert on the node symbol, the lowered node and the solved field, never on the
/// component. The one thing the component keeps is the line: a diagnostic about the copied pressure
/// points at <c>out.p=</c>.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PortPressureTests
{
    private const string Loop =
        """
        fluidscript 2

        circuit "loop":
          fluid = water

          HE1  heat_exchanger  power = 30  in.t = 40  out.t = 60
          PU1  pump
          CV1  valve

          HE1.out - N1 - PU1 - N2 - CV1 - N3 - HE1.in
        """;

    private static BindResult Bind(string source) =>
        new Binder(ComponentRegistry.Default).Bind(ScriptParse.Parse(new SourceText(source)), "script");

    private static ComponentSymbol Symbol(SemanticModel model, string name) =>
        model.Components.Single(component => string.Equals(component.Name, name, StringComparison.Ordinal));

    private static double? StatedPressure(SemanticModel model, string node) =>
        Symbol(model, node).Parameters.TryGetValue("p", out var value) ? value.Value?.SiValue : null;

    [Fact]
    public void APortPressureIsTheTouchingNodesPressure()
    {
        // `PU1 out.p=250` and `N2 node p=250` are one statement: N2 is the node PU1.out is wired to.
        var model = GraphFixture.Bind(Loop.Edited("PU1  pump", "PU1  pump  out.p = 250"));

        Assert.Equal(250_000, StatedPressure(model, "N2"));
        Assert.Null(StatedPressure(model, "N1"));
        Assert.Null(StatedPressure(model, "N3"));

        // The component still records what the line said, under the key the registry gives it.
        Assert.Equal(250_000, Symbol(model, "PU1").Parameters["p_out"].Value?.SiValue);

        // And the lowered node carries it as a stated pressure like any other, so it is the datum.
        var lowered = GraphFixture.Lower(Loop.Edited("PU1  pump", "PU1  pump  out.p = 250"));
        var posedness = FluidScript.Core.Topology.Counting.WellPosedness.Check(lowered.Graph);

        Assert.Equal(0, posedness.Counting.Excess);
        Assert.Contains(posedness.Hydraulics, block => block.Datum == "N2" && block.DatumWasStated);
    }

    [Fact]
    public void APortPressureLandsOnTheNodeInferenceInserted()
    {
        // With no node named, I2's `PU1__CV1` is the node the port touches, and it takes the pressure.
        var source = Loop.Edited("PU1  pump", "PU1  pump  out.p = 250")
            .Edited("HE1.out - N1 - PU1 - N2 - CV1 - N3 - HE1.in", "HE1.out - N1 - PU1 - CV1 - N3 - HE1.in");
        var model = GraphFixture.Bind(source);

        Assert.Equal(250_000, StatedPressure(model, "PU1__CV1"));
    }

    private const string Balanced =
        """
        fluidscript 2

        circuit "loop":
          fluid = water

          HE1  heat_exchanger  power = 30  in.t = 40  out.t = 60
          LO1  heat_exchanger  power = -30
          PU1  pump
          CV1  valve

          HE1.out - N1 - PU1 - N2 - CV1 - N3 - LO1 - N4 - HE1.in
        """;

    [Fact]
    public async Task StatingAPortPressureAndStatingTheNodeSolveToTheSameField()
    {
        // The equivalence the rule is defined by, measured: every solved pressure identical to the
        // last pascal, because the two scripts lower to the same circuit. Not to the last bit: the two
        // lowerings anchor the loop's one branch at different nodes (N1 and N2), so Newton starts from
        // different residuals and the two converged fields agree to rounding -- 4e-5 Pa of 144 kPa once
        // a valve's authority was read without its branch's pump (S-91), where a 1e-9 Pa bound had held
        // only because the rounding happened to match.
        var byPort = await Solve(Balanced.Edited("CV1  valve", "CV1  valve  in.p = 250"));
        var byNode = await Solve(Balanced + "\n  N2  node  p = 250\n");

        foreach (var node in new[] { "N1", "N2", "N3", "N4" })
        {
            Assert.Equal(Pressure(byNode, node), Pressure(byPort, node), 1e-3);
        }

        Assert.Equal(250_000, Pressure(byPort, "N2"), 1e-6);
    }

    [Fact]
    public void FS1539_ANodeStatedByItselfAndByAPortIsStatedTwice()
    {
        // Agreeing or not: the second copy is the line that will disagree after the next edit.
        var result = Bind(Loop.Edited("PU1  pump", "PU1  pump  out.p = 250") + "\n  N2  node  p = 250\n");
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1539");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("PU1 out.p", error.Message, StringComparison.Ordinal);
        Assert.Contains("'N2'", error.Message, StringComparison.Ordinal);
        Assert.Contains("N2 p", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1539_TwoPortsOnOneNodeStateItTwice()
    {
        // PU1.out and CV1.in are both N2; the later line is the one reported and names the first.
        var result = Bind(Loop.Edited("PU1  pump", "PU1  pump  out.p = 250")
            .Edited("CV1  valve", "CV1  valve  in.p = 240"));
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1539");

        Assert.Contains("'CV1 in.p'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'PU1 out.p'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatedInletPressureIsAPressureAndNotANearMissForATemperature()
    {
        // Before D-124 `in.p=300` on an exchanger scored 0.75 against `in.t` and bound as 300 °C under
        // an information notice. It is a pressure, on the node HX1.in touches.
        var result = Bind(Loop.Edited("HE1  heat_exchanger  power = 30  in.t = 40  out.t = 60", "HE1  heat_exchanger  power = 30  in.t = 40  out.t = 60  in.p = 300"));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code is "FS1512" or "FS1503" or "FS1538");
        Assert.Equal(300_000, StatedPressure(result.Model, "N3"));
        Assert.Equal(40, Symbol(result.Model, "HE1").Parameters["in"].Value?.SiValue - 273.15 ?? double.NaN, 6);
    }

    [Theory]
    [InlineData("PU1  pump  in.h = 5", "pump", "in", "h", "p")]
    [InlineData("HE1  heat_exchanger  power = 30  in.rho = 990", "heat_exchanger", "in", "rho", "dp, dt, flow, p, t, vflow")]
    [InlineData("HE1  heat_exchanger  power = 30  secondary.out.h = 5", "heat_exchanger", "secondary.out", "h", "p, t")]
    public void FS1538_APortQuantityTheKindDoesNotTakeListsWhatThePortTakes(string line, string kind, string port, string quantity, string takes)
    {
        var source = Loop.Edited(line.StartsWith("PU1", StringComparison.Ordinal) ? "PU1  pump" : "HE1  heat_exchanger  power = 30  in.t = 40  out.t = 60", line);
        var result = Bind(source);
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1538");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal($"The {kind}'s '{port}' has no '{quantity}'. It takes: {takes}.", error.Message);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1512");
    }

    [Fact]
    public void ATanksIndexedPortTakesAPressureThroughItsFamily()
    {
        var source =
            """
            fluidscript 2

            circuit "tank":
              fluid = water

              T1  tank  in[3].p = 120
              S1  inlet  t = 60  flow = 0.1
              S2  inlet  t = 40  flow = 0.1
              R1  outlet
              S1 - T1.in
              S2 - T1.in[3]
              T1.out - R1
            """;
        var result = Bind(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(120_000, Symbol(result.Model, "T1").Parameters["p_in3"].Value?.SiValue);
        Assert.Equal(120_000, StatedPressure(result.Model, "S2"));
    }

    [Fact]
    public void APortPressureReadsBackAsAPropertyOfTheComponent()
    {
        // D-120's own example: a deferred read of the node's solved pressure, spelled on the port.
        var result = Bind(Loop.Edited("PU1  pump", "PU1  pump  head = 1.2*HE1.in.p"));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1406");
        var deferred = Assert.Single(result.Model.Deferred);
        Assert.Contains(deferred.Dependencies, static id => id.ToString() == "HE1.p_in");
    }

    [Fact]
    public void FS2210_TwoPortPressuresInOneClosedLoopNameThePressuresNotTheInlet()
    {
        // `PU1 pump in.p=100 out.p=250` states two levels of one loop. The redundant row is one of the
        // two stated pressures, and the message said "remove HE1.in.t" -- the inlet the dropped
        // enthalpy level had already paid for (found by P5.13b, fixed with it).
        var posedness = FluidScript.Core.Topology.Counting.WellPosedness.Check(
            GraphFixture.Lower(Loop.Edited("PU1  pump", "PU1  pump  in.p = 100  out.p = 250")).Graph);
        var over = Assert.Single(posedness.Diagnostics, static d => d.Code == "FS2210");

        Assert.Equal(1, posedness.Counting.Excess);
        // Named as the script wrote them, not as the nodes the binder copied them onto (`PressureStatedAs`).
        Assert.Contains("PU1 in.p, PU1 out.p", over.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("HE1.in.t", over.Message, StringComparison.Ordinal);
    }

    private static async Task<OuterLoopResult> Solve(string script)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var result = await loop.RunAsync(GraphFixture.Bind(script), Water.Instance, "loop", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);

        return result.Value;
    }

    private static double Pressure(OuterLoopResult run, string node)
    {
        var layout = SystemLayout.Build(run.Graph, FluidScript.Core.Topology.Counting.WellPosedness.Check(run.Graph).Counting);
        var unknown = layout.Unknowns.Single(u => u.Kind == UnknownKind.NodePressure && u.OwnerComponentId == node);

        return run.Solve.Solution.Values[unknown.Index];
    }
}
