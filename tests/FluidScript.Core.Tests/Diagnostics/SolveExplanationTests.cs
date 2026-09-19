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
    /// <summary>Every script in <c>samples/</c>, enumerated rather than listed.</summary>
    /// <remarks>
    /// A list would cover the samples that existed when it was written. The report is meant to be what
    /// *every* solution carries, so the guarantee has to be stated over the directory: a sample added
    /// tomorrow is covered without anyone remembering to add it.
    /// </remarks>
    public static TheoryData<string> Samples =>
        [.. Directory.EnumerateFiles(RepositoryLayout.Samples, "*.fluid")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];

    [Theory]
    [Trait("Category", "Unit")]
    [MemberData(nameof(Samples))]
    public async Task EverySolutionCarriesTheSameReport(string sample)
    {
        // Every section, on every script, whatever happened to it. The ones that do not solve are the ones
        // worth explaining --- a circuit refused by counting, one that goes `Singular`, one that leaves the
        // fluid's property domain and one that is a syntax tour rather than a circuit all reach different
        // branches, and none of them may throw or quietly drop a section (`No pipeline stage throws`).
        var report = await Explain(sample);

        Assert.StartsWith($"=== {sample}", report, StringComparison.Ordinal);

        foreach (var section in (string[])
            [
                "    counting     ",
                "--- counting table",
                "--- hydraulic partition",
                "--- constraints, and what answers each",
                "--- unknowns, seeded and solved",
                "--- equations, and how far each is from satisfied",
                "--- values chosen by a sizing rule",
                "--- rank and conditioning",
            ])
        {
            Assert.Contains(section, report, StringComparison.Ordinal);
        }
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
        // Gating the rank on a balanced count withheld the measurement from the only circuits that need
        // it: the count can say the shortfall is one, and only the matrix can say *which* unknown nothing
        // determines. The header was the subject until it gained its stated source outlet and counted
        // square, so the fixture is now the plainest deficient circuit there is: a closed loop with no
        // temperature stated anywhere. Its level is dropped and nothing pays for it, and the unknown
        // nothing determines is the uniform enthalpy offset -- every node's `h` at weight 1, which is the
        // one answer a reader can check by hand.
        const string source = """
            fluidscript 1
            circuit loop
            fluid water
            HE1  heat_exchanger power=30
            LOAD heat_exchanger power=-30 dp=0
            CV1  valve
            PU1  pump
            P1   pipe length=25
            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
            """;

        var report = await Explain(source, "loop-no-temperature");

        Assert.Contains("11 unknowns, 10 equations — under-specified by 1", report, StringComparison.Ordinal);
        Assert.Contains("one energy balance dropped as its level", report, StringComparison.Ordinal);
        Assert.Contains("10 equations x 11 unknowns", report, StringComparison.Ordinal);
        Assert.Contains(
            "rank         10: 1 unknown(s) nothing determines, 0 equation(s) the others imply",
            report,
            StringComparison.Ordinal);

        foreach (var node in (string[])["N1", "N2", "N3", "N4", "N5"])
        {
            Assert.Contains($"           1  {node}.h", report, StringComparison.Ordinal);
        }

        // Every row is independent here, so there is no redundancy section at all. When a rectangular
        // system does have one, it is reported only if squaring the matrix could not have invented it:
        // padding a wider-than-tall system adds rows, and an added row is dependent by construction.
        Assert.DoesNotContain("the row direction", report, StringComparison.Ordinal);
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

    private static Task<string> Explain(string sample) =>
        Explain(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)), sample);

    private static async Task<string> Explain(string source, string name)
    {
        // One call, whether or not the run produced a result. Every caller used to write this branch
        // itself and half of them wrote the fallback wrong -- a circuit refused before the solver is
        // exactly the one whose report is worth reading.
        var resolved = PipeCatalogs.Resolve(pin: null);
        var run = await new OuterLoop(
                new NewtonSolver(),
                new CatalogBoreLookup(resolved.Value),
                OuterLoop.Rules(resolved.Value.Catalog),
                10)
            .RunAsync(
                GraphFixture.Bind(source), Water.Instance, name, TestContext.Current.CancellationToken);

        return SolveExplanation.Render(run, GraphFixture.Lower(source).Graph, name);
    }
}
