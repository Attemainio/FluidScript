using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Sizing;

/// <summary>A bootstrap value no rule can replace is reported as one (<c>C-60</c>).</summary>
public sealed class SurvivingProvisionalTests
{
    private static OuterLoop Loop()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);
    }

    [Fact]
    public async Task AThreeWayValvesKvIsChosenNowThatARuleClaimsIt()
    {
        // `C-60` recorded this valve as the standing example of a bootstrap value nothing replaced:
        // `Bootstrap` hands out `ISizer.Provisional` by *kind*, while a rule applied to a *type* that
        // excluded it, so `3WV` kept the largest row in the series -- chosen to behave like an open port
        // for one pass -- for the life of the run. `C-63`'s three-way pass is the rule that claims it, so
        // what this now pins is that the example is closed rather than that it still stands.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"));
        var run = await Loop().RunAsync(
            GraphFixture.Bind(source), Water.Instance, "cooling", TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        Assert.NotEqual(630.0, run.Value.Sizes.For("3WV", "kv"));
        Assert.DoesNotContain("provisional", run.Value.Bases["3WV.kv"], StringComparison.Ordinal);
        Assert.Contains("R5 preferred numbers", run.Value.Bases["3WV.kv"], StringComparison.Ordinal);

        // And it says which of `24`'s two shapes chose the drop, because the answer is not recoverable
        // from the Kv alone: the same row means different things on a bounded and a pump-driven circuit.
        Assert.Contains("free pump absorbs", run.Value.Bases["3WV.kv"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AThreeWayTheRuleCannotSizeStillSaysItsKvWasNeverChosen()
    {
        // The hole widening `CanSize` opened, closed deliberately. `Unsized` asks a *static* question --
        // does any sizer both `CanSize` this component and list this parameter -- so that a value sized on
        // an earlier pass is not slandered on a later one. `ValveSizer` now answers yes for every
        // three-way valve, so one the pass *declines* would sail through that check and be reported with
        // no basis at all, which is `D-02`'s "absence, never null" read backwards and the whole of `C-60`.
        //
        // An authority outside (0, 1) is the cheapest way to make the rule decline on a circuit that is
        // otherwise fine: `a / (1 - a)` is negative above 1, so there is no honest Kv to return.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"))
            .Replace("3WV three_way_valve", "3WV three_way_valve authority=1.5", StringComparison.Ordinal);

        var run = await Loop().RunAsync(
            GraphFixture.Bind(source), Water.Instance, "cooling", TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        Assert.Equal(630.0, run.Value.Sizes.For("3WV", "kv"));
        Assert.Contains("provisional, not chosen", run.Value.Bases["3WV.kv"], StringComparison.Ordinal);

        Assert.Contains(
            run.Value.Notes,
            static note => note.Contains("3WV is still the bootstrap value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AParameterARuleActuallySizedIsNotReportedAsProvisional()
    {
        // The check is static -- does any sizer both `CanSize` this component and list this parameter --
        // rather than "did a basis get written this pass". A value sized on an earlier pass and skipped on
        // a later one must not be slandered as a bootstrap leftover.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-simple-loop.fluid"));
        var run = await Loop().RunAsync(
            GraphFixture.Bind(source), Water.Instance, "simple", TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        Assert.DoesNotContain("provisional", run.Value.Bases["CV1.kv"], StringComparison.Ordinal);
        Assert.DoesNotContain("provisional", run.Value.Bases["N5__N1.dn"], StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Value.Notes,
            static note => note.Contains("bootstrap value", StringComparison.Ordinal));
    }
}
