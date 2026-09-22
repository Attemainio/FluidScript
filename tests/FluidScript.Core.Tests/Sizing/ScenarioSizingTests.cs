using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Sizing;

/// <summary>
/// One plant, sized for every case it must work in (<c>D-143</c>, <c>24</c>'s four steps).
/// </summary>
/// <remarks>
/// The property under test is the one the flow trap names: **the merge is per parameter, never per
/// component**, because two cases disagree about different parts of one plant. A test that only
/// checked "the bigger case won" would pass on a merge that adopted a whole component and would miss
/// exactly the failure the design exists to prevent.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ScenarioSizingTests
{
    private static OuterLoop Loop()
    {
        var catalog = PipeCatalogs.Resolve(pin: null).Value;

        return new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(catalog), OuterLoop.Rules(catalog.Catalog));
    }

    private static Task<Result<ScenarioSizingResult>> SizeAsync(string source) =>
        ScenarioSizing.SizeAsync(Loop(), GraphFixture.Bind(source), Water.Instance, "scenarios", TestContext.Current.CancellationToken);

    // A changeover loop: hot in winter, chilled in summer. **This is `24`'s flow trap, built to bite.**
    // Winter is 50 kW over a 35/45 °C program, so 50 000 / (4180 × 10) = 1.20 kg/s. Summer is the
    // *smaller* duty, 40 kW, but over a 7/12 °C program — 40 000 / (4180 × 5) = 1.91 kg/s, **60 %
    // more flow**. A pipe merged from the case that won on duty is short in the case that won on flow,
    // which is exactly why the envelope is taken per parameter rather than per component.
    private const string Seasons =
        """
        fluidscript 1
        scenarios winter summer
        design winter
        circuit distribution
        fluid water
        HE1  heat_exchanger power=[50, -40] in.t=[35, 12] out.t=[45, 7]
        LOAD heat_exchanger power=[-50, 40] dp=0
        PU1  pump
        P1   pipe length=20
        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N1
        """;

    [Fact]
    public async Task EveryCaseIsSolvedAgainstOnePlant()
    {
        var result = await SizeAsync(Seasons);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(["winter", "summer"], result.Value.Operating.Select(static s => s.Name).ToArray());
        Assert.Equal(0, result.Value.DesignIndex);
        Assert.Equal("winter", result.Value.Design.Name);
        Assert.All(result.Value.Operating, static s => Assert.True(s.Result.Solve.Converged));
    }

    [Fact]
    public async Task TheMergeTerminates()
    {
        // `08`'s first open measurement: sizes are coupled — a larger pipe lowers the head a pump
        // asks for — so the round count is a measurement, not an assertion.
        var result = await SizeAsync(Seasons);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(
            result.Value.Converged,
            $"the envelope was still moving after {ScenarioSizing.MaxRounds} rounds: "
            + string.Join("; ", result.Value.Notes));

        Assert.InRange(result.Value.Rounds, 1, ScenarioSizing.MaxRounds);
    }

    [Fact]
    public async Task EachSizeCoversEveryCaseAndNamesTheOneThatDecidedIt()
    {
        var result = await SizeAsync(Seasons);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var merged = result.Value.Sizes;

        // The merged plant is at least as large as anything a case chose for itself.
        foreach (var (component, sizes) in merged.Values)
        {
            foreach (var (parameter, value) in sizes)
            {
                Assert.True(
                    ScenarioEnvelope.RuleFor(parameter) is not null,
                    $"{component}.{parameter} reached the merge with no envelope rule");

                Assert.True(
                    result.Value.Governing.ContainsKey(Ownership.Key(component, parameter)),
                    $"{component}.{parameter} was merged without recording which case decided it");
            }
        }

        Assert.Contains(result.Value.Governing.Values, name => name is "winter" or "summer");
    }

    [Fact]
    public async Task TheMergedPipeIsTheLargerOfTheTwoCases()
    {
        // The flow trap made concrete. Sizing each case alone gives two bores; the plant gets the
        // larger, and it is the *flow* that decides it, not the duty.
        var loop = Loop();
        var model = GraphFixture.Bind(Seasons);

        var winter = await loop.RunAsync(
            ScenarioProjection.Project(model, 0), Water.Instance, "winter", TestContext.Current.CancellationToken);
        var summer = await loop.RunAsync(
            ScenarioProjection.Project(model, 1), Water.Instance, "summer", TestContext.Current.CancellationToken);

        Assert.True(winter.IsSuccess, winter.Error?.Message);
        Assert.True(summer.IsSuccess, summer.Error?.Message);

        var alone = Math.Max(
            winter.Value.Sizes.For("P1", "dn") ?? 0,
            summer.Value.Sizes.For("P1", "dn") ?? 0);

        var merged = await SizeAsync(Seasons);

        Assert.True(merged.IsSuccess, merged.Error?.Message);
        Assert.True(
            merged.Value.Sizes.For("P1", "dn") >= alone,
            $"merged DN {merged.Value.Sizes.For("P1", "dn")} is below the larger of the two cases' {alone}");
    }

    [Fact]
    public async Task AFileWithNoScenariosIsOneCaseAndNeedsNoBranch()
    {
        var result = await SizeAsync(
            """
            fluidscript 1
            circuit distribution
            fluid water
            HE1  heat_exchanger power=50 in.t=35 out.t=45
            LOAD heat_exchanger power=-50 dp=0
            PU1  pump
            P1   pipe length=20
            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N1
            """);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var only = Assert.Single(result.Value.Operating);

        Assert.Equal("design", only.Name);
        Assert.True(result.Value.Converged);
        Assert.Empty(result.Value.Governing);
    }

    [Fact]
    public async Task TheSmallerDutyGovernsBecauseItCarriesTheLargerFlow()
    {
        // **The flow trap, measured.** Winter is 50 kW over 35/45 °C — 50 000 / (4180 × 10) =
        // 1.20 kg/s. Summer is 40 kW over 7/12 °C — 40 000 / (4180 × 5) = 1.91 kg/s, 59 % more.
        // So the *smaller* duty governs every flow-driven size, and a plant merged from the duty's
        // winner would be a pipe size short.
        //
        // Measured 2026-09-22: HE1.flow 1.906 kg/s and P1 DN65, both governed by summer; two rounds
        // to settle; 2 Newton iterations and about 1.5 ms per case.
        var result = await SizeAsync(Seasons);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1.906, result.Value.Sizes.For("HE1", "flow") ?? 0, 0.01);
        Assert.Equal("summer", result.Value.Governing[Ownership.Key("HE1", "flow")]);
        Assert.Equal("summer", result.Value.Governing[Ownership.Key("P1", "dn")]);

        // And it is not merely that summer is larger everywhere: winter's own solve wants the
        // smaller flow, which is what makes taking the maximum a decision rather than a tautology.
        var winter = await Loop().RunAsync(
            ScenarioProjection.Project(GraphFixture.Bind(Seasons), 0),
            Water.Instance,
            "winter",
            TestContext.Current.CancellationToken);

        Assert.True(winter.IsSuccess, winter.Error?.Message);
        Assert.True(
            (winter.Value.Sizes.For("HE1", "flow") ?? 0) < 1.3,
            $"winter alone wanted {winter.Value.Sizes.For("HE1", "flow")} kg/s, so the two cases do not disagree");
    }

    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task TheScenarioReportIsWritten()
    {
        // Alongside `circuit-reports.md`: the numbers this package produces, where they can be read
        // rather than re-derived. The timing line is what decides whether `24`'s chunked workers are
        // ever worth building.
        var source = await File.ReadAllTextAsync(
            Path.Combine(RepositoryLayout.Samples, "m5-scenarios.fluid"), TestContext.Current.CancellationToken);

        var model = GraphFixture.Bind(source);
        var result = await ScenarioSizing.SizeAsync(
            Loop(), model, Water.Instance, "m5-scenarios", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var report = Path.Combine(RepositoryLayout.Diagnostics, "scenario-sizing.md");

        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await File.WriteAllTextAsync(
            report,
            "# Scenario sizing\n\nWritten by `ScenarioSizingTests`. One `ScenarioExplanation` per\n"
            + "scenario-bearing sample: which case governed each size, what each case does in the\n"
            + "merged plant, and what the pass cost.\n\n```\n"
            + ScenarioExplanation.Explain(result.Value, model.Project.Scenarios)
            + "```\n",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void EverySizerParameterHasAnEnvelopeRule()
    {
        // The closed-set guard. A sizer that gains a parameter without a rule stops the merge rather
        // than quietly acquiring a maximum, and this says so at build time rather than on a plant.
        var catalog = PipeCatalogs.Resolve(pin: null).Value;
        var unruled = OuterLoop.Rules(catalog.Catalog)
            .SelectMany(static sizer => sizer.Parameters)
            .Distinct(StringComparer.Ordinal)
            .Where(static parameter => ScenarioEnvelope.RuleFor(parameter) is null)
            .ToArray();

        Assert.True(
            unruled.Length == 0,
            "24's envelope table is where a merge rule is decided. Without one: " + string.Join(", ", unruled));
    }
}
