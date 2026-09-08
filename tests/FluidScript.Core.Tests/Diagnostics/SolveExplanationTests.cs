using FluidScript.Core.Catalogs;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Diagnostics;

/// <summary>What the solve report has to say to be worth reading.</summary>
public sealed class SolveExplanationTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("m2-simple-loop.fluid")]
    [InlineData("m2-cooling-loop.fluid")]
    [InlineData("m2-distribution-header.fluid")]
    [InlineData("m2-substation.fluid")]
    [InlineData("m4-storage-header.fluid")]
    public async Task EverySampleExplainsItselfWithoutThrowing(string sample)
    {
        // Including the ones that do not solve, which are the ones worth explaining: a circuit that is
        // refused by counting, one that goes `Singular`, and one that leaves the fluid's domain all reach
        // different branches of this report, and none of them may throw (`No pipeline stage throws`).
        var report = await Explain(sample);

        Assert.Contains("counting table", report, StringComparison.Ordinal);
        Assert.Contains("constraints, and what answers each", report, StringComparison.Ordinal);
        Assert.Contains("unknowns, seeded and solved", report, StringComparison.Ordinal);
        Assert.Contains("rank and conditioning", report, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EachConstraintIsShownAgainstWhatAnswersIt()
    {
        // The question that took a dozen throwaway probes to answer: which parameter did this constraint
        // claim? Every constraint appears, answered or not, because one that is answered by nothing is
        // invisible in a list of promotions.
        var report = await Explain("m2-distribution-header.fluid");

        Assert.Contains(
            "FixedFlow    on HE_AHU     -> solved for as PU_AHU.head", report, StringComparison.Ordinal);
        Assert.Contains(
            "MixedInlet   on HE_AHU     -> solved for as TV_AHU.position", report, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AConstraintWithNoPromotionIsReportedAgainstTheLevelsThatPayForIt()
    {
        // This report's own first bug, and worth a test because it is the mistake the report exists to
        // stop a reader making. `m2-simple-loop` has a `MixedInlet` no promotion answers and still counts
        // square: its stated inlet is paid for by the enthalpy level it removes, not by an unknown it
        // adds. Calling that over-specification would have been the report confidently misleading.
        var report = await Explain("m2-simple-loop.fluid");

        Assert.Contains("MixedInlet   on HE1        -> no promotion", report, StringComparison.Ordinal);
        Assert.Contains(
            "1 with no promotion, against 1 enthalpy level(s) dropped",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "0 unknown(s) nothing determines, 0 equation(s) the others imply",
            report,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ACircuitTheCountRefusesIsStillRankedAndNamesTheUnknownNothingDetermines()
    {
        // The header, since `S-41`. Gating the rank on a balanced count withheld the measurement from the
        // only circuits that need it: the count can say the shortfall is one, and only the matrix can say
        // *which* unknown nothing determines. `PU_RAD.head` at weight 1 against its valve's position is
        // `S-38`, stated in one line instead of inferred from a dozen probes.
        var report = await Explain("m2-distribution-header.fluid");

        Assert.Contains("45 unknowns, 44 equations — under-specified by 1", report, StringComparison.Ordinal);
        Assert.Contains("one energy balance dropped as its level", report, StringComparison.Ordinal);
        Assert.Contains("44 equations x 45 unknowns", report, StringComparison.Ordinal);
        Assert.Contains(
            "rank         44: 1 unknown(s) nothing determines, 0 equation(s) the others imply",
            report,
            StringComparison.Ordinal);
        Assert.Contains("PU_RAD.head", report, StringComparison.Ordinal);

        // The row direction is withheld rather than padded, because a zero row invented to square the
        // matrix is trivially dependent and would be named ahead of any real redundancy.
        Assert.Contains("the row direction is not reported on a 44x45 system", report, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AConvergedCircuitReportsFullRankAndNoNullDirections()
    {
        var report = await Explain("m2-cooling-loop.fluid");

        Assert.Contains(
            "0 unknown(s) nothing determines, 0 equation(s) the others imply",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain("the row direction", report, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACircuitThatWasNeverSolvedStillExplainsItself()
    {
        // The overload for a graph with no run behind it: counting, constraints and the seed still say
        // most of what a user needs, and the sections that need a solve say so instead of failing.
        var graph = GraphFixture.Lower(File.ReadAllText(
            Path.Combine(RepositoryLayout.Samples, "m2-simple-loop.fluid"))).Graph;

        var report = SolveExplanation.Render(graph, "unsolved");

        Assert.Contains("solve        not run", report, StringComparison.Ordinal);
        Assert.Contains("evaluated at the seed", report, StringComparison.Ordinal);
    }

    private static async Task<string> Explain(string sample)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        var run = await new OuterLoop(
                new NewtonSolver(),
                new CatalogBoreLookup(resolved.Value.Catalog),
                OuterLoop.Rules(resolved.Value.Catalog),
                10)
            .RunAsync(
                GraphFixture.Bind(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))),
                Water.Instance, sample, TestContext.Current.CancellationToken);

        return run.IsSuccess
            ? SolveExplanation.Render(run.Value, sample)
            : SolveExplanation.Render(
                GraphFixture.Lower(
                    File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph, sample);
    }
}
