using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Diagnostics.Explanations;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Diagnostics.Explanations;

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
        [.. ScriptCorpus.EnumerateSampleFiles()
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
                "--- state, in engineering units",
                "--- heat balance",
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
            "FixedFlow    on HE_AHU.out.t -> solved for as PU_AHU.head", report, StringComparison.Ordinal);
        Assert.Contains(
            "MixedInlet   on HE_AHU.in.t  -> solved for as TV_AHU.position", report, StringComparison.Ordinal);
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

        Assert.Contains("MixedInlet   on HE1.in.t     -> no promotion", report, StringComparison.Ordinal);
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
    public async Task ASolvedCircuitReplaysItsIterationsAndReadsInEngineeringUnits()
    {
        // S-71: the four things a session had to read the engine for. The trajectory, one row per step
        // with the residual it set out to remove and what moved; the seed basis beside each branch flow;
        // every node in °C and kPa and every branch against its written order; the heat balance closing;
        // and each pump and valve at its operating point.
        var report = await Explain("m2-substation.fluid");

        Assert.Contains("--- iterations", report, StringComparison.Ordinal);
        Assert.Matches(@"\n      1 +[0-9.E+-]+ +1  (HX1: HX1|LOAD: LOAD) side-1 drop +SP\.head", report);
        Assert.Contains("Converged", report, StringComparison.Ordinal);
        // 0.8953 on IAPWS-95 and 0.8957 on IF97, whose cp differs by 5e-4 (D-137): the digit the
        // formulation owns is left free.
        Assert.Matches(@"NPS -> NPR +0\.895[0-9]  forward", report);
        Assert.Matches(@"NPS +85\.00 +600\.00", report);
        Assert.Contains("kg/s  Duty(", report, StringComparison.Ordinal);
        Assert.Contains(
            "[0] sources +0 kW, loads -150 kW (HX1 -150), boundary streams +150 kW", report, StringComparison.Ordinal);
        Assert.Contains("[1] sources +150 kW (HX1 +150), loads -150 kW (LOAD -150), boundary streams +0 kW — net +0 kW", report, StringComparison.Ordinal);
        Assert.Contains("--- operating points", report, StringComparison.Ordinal);
        // The flow's fourth digit and the rise's second decimal are the formulation's (D-137).
        Assert.Matches(@"SP +pump +1\.79[34]\d kg/s  head +10\.2\d m  rise +99\.\d\d kPa  \(solved for\)", report);
        Assert.Matches(@"PCV +valve +0\.895\d kg/s  Kv +2\.13  position +1  drop +237\.\d\d kPa", report);
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

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ATwoSidedExchangerBetweenTwoRingsIsReportedSideBySide()
    {
        // S-70. A coupled exchanger sits in both hydraulics it separates, and two things read it
        // wrongly: `FS2203` named a hydraulic by its first element's circuit, so both rings could
        // carry the exchanger's circuit name; and the constraints section matched a promotion to a
        // constraint by component alone, so side 2's stated flow was answered by side 1's pump. The
        // branch direction is read off the port map too -- a ring's walk may start anywhere, and
        // both rings were labelled "reversed" while every pump pushed the way it was written.
        const string source = """
            fluidscript 1
            fluid water

            circuit first
            PU1 pump
            HX1 heat_exchanger power=40 in.t=40 out.t=60 in[2].t=80 out[2].t=60
            connections
            HX1.out - N1 length=10 dn=32
            N1 - PU1 - HX1.in

            circuit second
            PU2 pump
            connections
            HX1.out[2] - PU2 - N3 - HX1.in[2] length=10 dn=25
            """;

        var report = await Explain(source, "two-rings");

        Assert.Contains("FS2203       'first' is closed", report, StringComparison.Ordinal);
        Assert.Contains("FS2203       'second' is closed", report, StringComparison.Ordinal);
        Assert.Contains("FixedFlow    on HX1.out.t    -> solved for as PU1.head", report, StringComparison.Ordinal);
        Assert.Contains("FixedFlow    on HX1.out[2].t -> solved for as PU2.head", report, StringComparison.Ordinal);
        Assert.Matches(@"N1 -> N1 +0\.4780  forward", report);
        Assert.Matches(@"N3 -> N3 +0\.4780  forward", report);
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
