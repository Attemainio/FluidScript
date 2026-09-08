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
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);
    }

    [Fact]
    public async Task AThreeWayValvesKvSaysItWasNeverChosen()
    {
        // `Bootstrap` hands out `ISizer.Provisional` by *kind* -- every parameter the registry marks
        // sizable -- while a rule applies to a *type*: `ValveSizer.CanSize` is `component is Valve`. So a
        // three-way valve is given the largest row in the series, deliberately chosen to behave like an
        // open port for one pass, and then nothing ever replaces it. Before this it was reported as a
        // size with no basis at all, which is `D-02`'s "absence, never null" read backwards.
        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-cooling-loop.fluid"));
        var run = await Loop().RunAsync(
            GraphFixture.Bind(source), Water.Instance, "cooling", TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        // The value is still there -- the model has to build -- but it now explains itself.
        Assert.Equal(630.0, run.Value.Sizes.For("3WV", "kv"));
        Assert.Contains("provisional, not chosen", run.Value.Bases["3WV.kv"], StringComparison.Ordinal);

        Assert.Contains(
            run.Value.Notes,
            static note => note.Contains("3WV.kv is still the bootstrap value", StringComparison.Ordinal));
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
        Assert.DoesNotContain("provisional", run.Value.Bases["P1.dn"], StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Value.Notes,
            static note => note.Contains("bootstrap value", StringComparison.Ordinal));
    }
}
