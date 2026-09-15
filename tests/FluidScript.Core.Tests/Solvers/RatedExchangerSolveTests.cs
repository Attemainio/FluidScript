using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

using CoreTopology = FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>
/// <c>P4.1</c> end to end: the Rated and Coupled exchanger modes of <c>D-19</c> solved through the
/// outer loop, against the figures <c>plan/00-foundation/01-vision-and-scope.md</c> works by hand.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The substation is the acceptance criterion.</strong> It was the one corpus sample that
/// never converged (<c>S-32</c>): the primary's flow was implied by nothing, so the loop ran at
/// whatever the pump curve gave and the temperatures left the property domain. Three changes closed
/// it together -- the exchanger's duty is now <c>ε·Cmin·(T_in2 − T_in1)</c> rather than a constant,
/// its design point pins the flow of a side nothing else pins (<c>D-97</c>), and a closed circuit's
/// picked datum sits at the pump suction (<c>D-98</c>) -- and this file holds the numbers that
/// prove it: UA 12.071 kW/K, 3.658 m², 0.895 kg/s of 85/45 primary against 1.793 kg/s of 40/60
/// secondary.
/// </para>
/// <para>
/// The Rated tests are the same secondary with the primary replaced by its stated profile. They must
/// reach the same thermal answer, because <c>D-19</c> says the two modes differ only in where side 2
/// lives -- a branch, or a boundary the script states.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RatedExchangerSolveTests
{
    /// <summary>The substation's secondary, with HX1 rated against the primary's stated 85/45 profile.</summary>
    private const string RatedLoop = """
        fluidscript 1
        circuit rated
        fluid water

        SP   pump
        SS   pipe length=30 dn=32
        SR   pipe length=30 dn=32
        LOAD heat_exchanger power=-150 dt=20

        HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 u=3300

        connections
        HX1.out - SS - NSUP
        NSUP - LOAD - NRET
        NRET - SR - SP - HX1.in
        """;

    private static OuterLoop Loop()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);
    }

    private static async Task<OuterLoopResult> RunAsync(string source, string name)
    {
        var result = await Loop().RunAsync(GraphFixture.Bind(source), Water.Instance, name, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        return result.Value;
    }

    private static string Substation() =>
        File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-substation.fluid"));

    private static Solved Read(OuterLoopResult run) => new(run);

    /// <summary>The solved field, addressed by label rather than by position.</summary>
    private sealed class Solved(OuterLoopResult run)
    {
        private readonly SystemLayout _layout =
            SystemLayout.Build(run.Graph, CoreTopology.WellPosedness.Check(run.Graph).Counting);

        private readonly double[] _values = [.. run.Solve.Solution.Values];

        public double Flow(string branch) => Math.Abs(_values[Index(UnknownKind.BranchFlow, branch)]);

        public double Celsius(string node) =>
            Water.Instance.FromPressureEnthalpy(
                    Quantity.FromSi(_values[Index(UnknownKind.NodePressure, node)], Dimension.Pressure),
                    Quantity.FromSi(_values[Index(UnknownKind.NodeEnthalpy, node)], Dimension.Enthalpy))
                .Value.Temperature.SiValue - 273.15;

        public double Parameter(string owner, string name) =>
            _values[Index(UnknownKind.Parameter, owner, name)];

        private int Index(UnknownKind kind, string owner, string? name = null)
        {
            for (var index = 0; index < _layout.Unknowns.Length; index++)
            {
                var unknown = _layout.Unknowns[index];

                if (unknown.Kind == kind
                    && string.Equals(unknown.OwnerComponentId, owner, StringComparison.Ordinal)
                    && (name is null || string.Equals(unknown.Name, name, StringComparison.Ordinal)))
                {
                    return index;
                }
            }

            throw new Xunit.Sdk.XunitException($"no {kind} unknown owned by {owner}{(name is null ? string.Empty : $" named {name}")}");
        }
    }

    // ---- the substation, coupled -------------------------------------------------------------------

    [Fact]
    public async Task TheSubstationConvergesToTheVisionsFigures()
    {
        // 01: 150 kW, 40/60 on the secondary and 85/45 on the primary. The secondary's 1.793 kg/s is
        // LOAD's 150 kW over its 20 K; the primary's 0.895 kg/s is the same duty over 40 K, pinned by
        // HX1's own design point (D-97) since nothing else in the open primary states a flow.
        var run = await RunAsync(Substation(), "substation");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination}: {run}");
        Assert.True(run.Settled, $"sizes were still moving after {run.Passes} passes.");

        var solved = Read(run);

        Assert.Equal(1.793, solved.Flow("NSUP->NSUP"), 0.005);
        Assert.Equal(0.895, solved.Flow("NPS->NPR"), 0.005);
        Assert.Equal(60.0, solved.Celsius("NSUP"), 0.15);
        Assert.Equal(40.0, solved.Celsius("NRET"), 0.15);
        Assert.Equal(45.0, solved.Celsius("NPR"), 0.15);
    }

    [Fact]
    public async Task TheSubstationIsSizedByTheEffectivenessRouteToTheConductanceTheLogMeanRouteGives()
    {
        // The sizer inverts ε-NTU; 01 works LMTD. Both give 12 071 W/K, and the basis line carries the
        // intermediate numbers a reader checks against the document: NTU 3.219, ε 0.8889, Cr 0.5, 5 K.
        var run = await RunAsync(Substation(), "substation");

        Assert.Equal(ReferenceNumbers.Substation.RequiredUa, run.Sizes.For("HX1", "ua")!.Value, 1.0);
        Assert.Equal(ReferenceNumbers.Substation.RequiredArea, run.Sizes.For("HX1", "area")!.Value, 0.001);
        Assert.Contains("NTU 3.219", run.Bases["HX1.ua"], StringComparison.Ordinal);
        Assert.Contains("ε 0.8889", run.Bases["HX1.ua"], StringComparison.Ordinal);
        Assert.Contains("approach 5 K", run.Bases["HX1.ua"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSubstationsPromotedValveIsSpentOnTheStatedPrimaryPressures()
    {
        // 600 kPa in, 350 kPa back, 0.895 kg/s: what the pipe does not drop, the valve must. A Kv near
        // 2.1 m³/h is that valve; the bootstrap Kv 630 it used to start from was the substance of C-75.
        var run = await RunAsync(Substation(), "substation");
        var solved = Read(run);

        Assert.Equal(2.13, solved.Parameter("PCV", "PCV.kv"), 0.05);
        Assert.Equal(10.2, solved.Parameter("SP", "SP.head"), 0.2);
    }

    [Fact]
    public async Task FS4008_ADesignBelowTheMinimumApproachIsReportedOnTheSolve()
    {
        // 62/45 on the primary against 40/60 closes the hot end to 2 K. The sizer raises it, the loop
        // carries it to the solve's diagnostics, and the solve still runs: the design is questionable,
        // not unsolvable.
        var run = await RunAsync(Substation().Replace("in2=85", "in2=62", StringComparison.Ordinal), "close");

        Assert.Contains(run.Solve.Diagnostics, static d => d.Code == "FS4008");
    }

    // ---- the same secondary, rated ----------------------------------------------------------------

    [Fact]
    public async Task ARatedExchangerReachesTheSameDesignPointWithNoSecondBranch()
    {
        // The whole of D-19's Rated mode: side 2 is the 85/45 profile, not a branch, so the graph has
        // one hydraulic and one branch -- and the same 1.793 kg/s at 40/60, because the duty relation
        // is the same ε-NTU the coupled solve uses, with the profile standing in for the primary.
        var run = await RunAsync(RatedLoop, "rated");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination}: {run}");
        Assert.Single(run.Graph.Branches);

        var solved = Read(run);

        Assert.Equal(1.793, solved.Flow("NSUP->NSUP"), 0.005);
        Assert.Equal(60.0, solved.Celsius("NSUP"), 0.15);
        Assert.Equal(40.0, solved.Celsius("NRET"), 0.15);
        Assert.Equal(ReferenceNumbers.Substation.RequiredUa, run.Sizes.For("HX1", "ua")!.Value, 1.0);
    }

    [Fact]
    public async Task ARatedExchangersDesignPointPinsTheFlowWhereNothingElseDoes()
    {
        // D-97 for the rated case. LOAD without its `dt` and HX1 stated as `in`+`dt`: the only flow pin
        // left is the exchanger's own, and the seed starts from Q/(cp·dt) rather than the nominal 0.1.
        var source = RatedLoop
            .Replace("power=-150 dt=20", "power=-150", StringComparison.Ordinal)
            .Replace("in=40 out=60", "in=40 dt=20", StringComparison.Ordinal);

        var run = await RunAsync(source, "rated");

        Assert.True(run.Solve.Converged, $"stopped at {run.Solve.Termination}: {run}");

        var posedness = CoreTopology.WellPosedness.Check(run.Graph);

        Assert.Equal(["HX1.dt"], posedness.Counting.Constraints.Select(static c => c.Label).ToArray());
        Assert.Equal(1.794, Read(run).Flow("NSUP->NSUP"), 0.005);
    }

    [Fact]
    public async Task ARatedExchangersTerminalsAreADesignPointNotConstraints()
    {
        // In Duty mode `in=40 out=60` would be a mixed-inlet demand and a flow pin, and with LOAD's `dt`
        // the loop would be over-specified by two. Rated, they are what sizes UA, and the count is square.
        var posedness = CoreTopology.WellPosedness.Check(GraphFixture.Lower(RatedLoop).Graph);

        Assert.Equal(0, posedness.Counting.Excess);
        Assert.Equal(["LOAD.dt"], posedness.Counting.Constraints.Select(static c => c.Label).ToArray());
    }
}
