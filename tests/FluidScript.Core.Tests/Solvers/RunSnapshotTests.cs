using FluidScript.Core.Catalogs;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The snapshot a run is built from (P6.0, <c>D-22</c>, <c>D-141</c>): the schedule, the initial states and the id.</summary>
[Trait("Category", "Unit")]
public sealed class RunSnapshotTests
{
    private static readonly Dictionary<string, string> Versions = new(StringComparer.Ordinal)
    {
        ["language"] = "1",
        ["catalogue"] = "test",
        ["properties"] = "test",
        ["contract"] = "test",
    };

    private static OuterLoop Loop()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
    }

    private static async Task<RunSnapshot> SnapshotAsync(string sample, TransientSettings? settings = null)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Samples, sample), TestContext.Current.CancellationToken);
        var result = await Loop().RunAsync(GraphFixture.Bind(source), Water.Instance, sample, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged, result.Value.Solve.Termination.ToString());

        var snapshot = RunSnapshot.Create(
            result.Value.Graph,
            WellPosedness.Check(result.Value.Graph),
            result.Value.Solve.Solution,
            settings ?? new TransientSettings(),
            "sha256:" + new string('0', 64),
            Versions);

        Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);

        return snapshot.Value;
    }

    [Fact]
    public async Task TheDemandStepScheduleCarriesTheLoadStepInSi()
    {
        // `at 60 s HE1.power = 45`: one step, 60 s from t = 0, to 45 kW — 45 000 W on the graph, because
        // nothing past the language boundary carries a unit (D-07).
        var snapshot = await SnapshotAsync("m4-demand-step.fluid");
        var change = Assert.Single(snapshot.Schedule);

        Assert.Equal("HE1", change.Component);
        Assert.Equal("power", change.Parameter);
        Assert.Equal(60, change.From);
        Assert.Equal(60, change.To);
        Assert.Null(change.FromValue);
        Assert.Equal(45_000, change.ToValue);
        Assert.Equal(snapshot.Schedule, snapshot.Graph.Schedule);
    }

    [Fact]
    public async Task TheDemandStepStartsFromItsDesignState()
    {
        // The four cells of PB start at the enthalpy the design solve gave their nodes, and both
        // promotions — the pump head that HE1's out.t chose and the valve position the setpoint chose —
        // are frozen at their design values (D-140): 2.55 m and 0.50 on 01's figures.
        var snapshot = await SnapshotAsync("m4-demand-step.fluid");
        var layout = snapshot.System.Unknowns;

        Assert.Equal(4, snapshot.DifferentialInitial.Length);

        for (var index = 0; index < layout.Differential.Length; index++)
        {
            Assert.Equal(snapshot.Initial.Values[layout.Differential[index].Column], snapshot.DifferentialInitial[index]);
        }

        var promotions = snapshot.Posedness.Counting.Promotions;

        Assert.Equal(["PU1.head", "3WV.position"], promotions.Select(static p => p.Label).ToArray());
        Assert.Equal(2, snapshot.PromotionInitial.Length);
        Assert.Equal(snapshot.Initial.Values[layout.PromotionOffset], snapshot.PromotionInitial[0]);
        Assert.Equal(snapshot.Initial.Values[layout.PromotionOffset + 1], snapshot.PromotionInitial[1]);
        Assert.InRange(snapshot.PromotionInitial[0], 2.4, 2.7);
        Assert.InRange(snapshot.PromotionInitial[1], 0.45, 0.55);
        Assert.Single(snapshot.Setpoints, static s => s.Applied);
    }

    [Fact]
    public async Task TheStorageHeaderLayersStartAtTheirStatedProfile()
    {
        // `layer[1].t=25 … layer[5].t=60`: a non-equilibrium initial condition the run may evolve from
        // t = 0 without a schedule (33 invariant 3). Each layer starts at water's enthalpy at its stated
        // temperature, not at the mixed 41.4 C the design solve holds T1.h at.
        var snapshot = await SnapshotAsync("m4-storage-header.fluid");
        var expected = new[] { 25.0, 30.0, 40.0, 50.0, 60.0 };

        Assert.Equal(5, snapshot.DifferentialInitial.Length);

        for (var layer = 0; layer < 5; layer++)
        {
            var state = Water.Instance.FromPressureTemperature(
                Quantity.FromSi(0, Dimension.Pressure),
                Quantity.FromSi(expected[layer] + 273.15, Dimension.Temperature));

            Assert.True(state.IsSuccess);
            Assert.Equal(state.Value.Enthalpy.SiValue, snapshot.DifferentialInitial[layer], 200.0);
        }

        var mixed = Assert.Single(snapshot.System.Unknowns.Unknowns, static u => u.Name == "T1.h");

        Assert.NotEqual(snapshot.Initial.Values[mixed.Index], snapshot.DifferentialInitial[0], 1000.0);
    }

    [Fact]
    public async Task TheSnapshotIdIsStableAndFollowsTheSettings()
    {
        var first = await SnapshotAsync("m4-demand-step.fluid");
        var second = await SnapshotAsync("m4-demand-step.fluid");
        var longer = await SnapshotAsync("m4-demand-step.fluid", new TransientSettings { Horizon = 1200 });

        Assert.StartsWith("sha256:", first.SnapshotId, StringComparison.Ordinal);
        Assert.Equal(71, first.SnapshotId.Length);
        Assert.Equal(first.SnapshotId, second.SnapshotId);
        Assert.NotEqual(first.SnapshotId, longer.SnapshotId);
        Assert.Equal(1200, longer.Settings.Horizon);
        Assert.Equal(Tolerances.TransientMaxStep, longer.Settings.MaxStep);
    }

    [Fact]
    public void AScheduleOnAControlledActuatorIsRefusedWithFs3109()
    {
        // The controller owns 3WV.position from t = 0 (D-140); a schedule that also moved it would
        // fight the loop every step. FS3109 refuses it and names the controller.
        var lowered = GraphFixture.Lower(DemandStepWith("at 60 s   3WV.position = 0.3"));
        var change = Assert.Single(lowered.Graph.Schedule);

        Assert.Equal("3WV", change.Component);
        Assert.Equal("position", change.Parameter);

        var error = Assert.Single(WellPosedness.Check(lowered.Graph).Diagnostics, static d => d.Code == "FS3109");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("'3WV.position' is driven by TC1; a schedule cannot also move it.", error.Message);
    }

    [Fact]
    public void AScheduleOnTheLoadIsNotAnActuatorAndPasses()
    {
        var lowered = GraphFixture.Lower(DemandStepWith("at 60 s   HE1.power = 45"));

        Assert.Single(lowered.Graph.Schedule);
        Assert.DoesNotContain(WellPosedness.Check(lowered.Graph).Diagnostics, static d => d.Code == "FS3109");
    }

    [Fact]
    public void AScheduleOnABoundaryTemperatureIsRefusedWithFs3105()
    {
        // `N1.t` binds — the inlet has a `t` — but a boundary state enters the model at assembly and a
        // run has no slot to write it at t > 0 (S-77). Silently ignoring it would be worse than the error.
        var lowered = GraphFixture.Lower(DemandStepWith("at 60 s   N1.t = 10"));
        var error = Assert.Single(WellPosedness.Check(lowered.Graph).Diagnostics, static d => d.Code == "FS3105");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("Cannot change 'N1.t' — a node has no parameter a run can move.", error.Message);
    }

    [Fact]
    public void AScheduleOnAPipeLengthNamesWhatARunCanMove()
    {
        var lowered = GraphFixture.Lower(DemandStepWith("at 60 s   PU1.efficiency = 0.5"));
        var error = Assert.Single(WellPosedness.Check(lowered.Graph).Diagnostics, static d => d.Code == "FS3105");

        Assert.Contains("a run can move 'head' on a pump, not 'efficiency'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARampCarriesBothEndsInSi()
    {
        var lowered = GraphFixture.Lower(DemandStepWith("over 60 s .. 120 s   HE1.power = 30 .. 45"));
        var change = Assert.Single(lowered.Graph.Schedule);

        Assert.Equal(60, change.From);
        Assert.Equal(120, change.To);
        Assert.Equal(30_000, change.FromValue);
        Assert.Equal(45_000, change.ToValue);
    }

    private static string DemandStepWith(string line) => $$"""
        circuit demandStep
        fluid water

        HE1 heat_exchanger power=30 out.t=50
        3WV three_way_valve
        PU1 pump
        P1  pipe length=25
        PB  pipe length=8 dn=20 nodes=4
        TC1 pi

        control actuate=3WV.position measure=N2.t by=TC1 setpoint=20

        connections
        N1 - N2
        N2 - PU1
        PU1 - HE1
        HE1 - 3WV
        3WV - PB - N2
        3WV - P1
        P1 - N3

        N1 inlet t=6 p=300
        N3 outlet p=280

        schedule
        {{line}}
        """;
}
