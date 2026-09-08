using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>Where every sample in the corpus stands, pinned so a change announces itself.</summary>
/// <remarks>
/// <para>
/// <strong>This is an instrument, not an acceptance test.</strong> It asserts what each sample does
/// <em>today</em>, including the ones that fail, so that a session does not have to re-measure the
/// corpus by hand before it can prioritise -- which is how a stale claim about which sample is blocked
/// on what gets carried from one session to the next and acted on.
/// </para>
/// <para>
/// <strong>A failure here is not necessarily a regression.</strong> Fixing a solver defect will break
/// this file, and that is the point: the expectation is then updated in the same commit as the fix, so
/// the diff records that this sample changed state and which change did it. `05` owns the milestone
/// criteria; this owns the measurement.
/// </para>
/// </remarks>
public sealed class CorpusStatusTests
{
    // `null` is a sample the counting check refuses, which never reaches the solver and so has no
    // termination to record. It is a state worth distinguishing rather than folding into a failure:
    // refused-with-a-count is a better place to stand than square-and-singular, even though neither
    // solves.
    public static TheoryData<string, SolveTermination?> Corpus => new()
    {
        // Solves end to end. `24`'s worked example, reached rather than transcribed.
        { "m2-simple-loop.fluid", SolveTermination.Converged },

        // Solves in one pass: nothing in it needs sizing.
        { "m4-storage-header.fluid", SolveTermination.Converged },

        // Converged, after three fixes in a row: `S-30b` anchored the seed's temperatures, `S-35` stopped
        // a component in the bypass leg collapsing the valve's three ports onto one pressure, and
        // `C-63`'s three-way pass finally chose `3WV.kv` rather than leaving it on the bootstrap Kv 630.
        // It reaches `01`'s figures: the mixing node at 19.99 C against 20, the return at 49.94 against
        // 50, and 0.0763 kg/s recirculating. **An `M2a` exit criterion, met.**
        { "m2-cooling-loop.fluid", SolveTermination.Converged },

        // `S-38`, and no longer `Singular`. It was square and rank 44 of 45; `S-41` found that a stated
        // pressure on an interior datum was making this closed circuit read as open, so no energy balance
        // was dropped as its level. With that fixed the count itself is short by one and the check refuses
        // it before the solver sees it. The deficiency is the same one -- it is now named rather than met
        // as a zero pivot.
        { "m2-distribution-header.fluid", null },

        // `S-32`. The secondary's flow is not implied by its stated profile, so the loop runs at 2.21
        // kg/s where the duty implies 0.897 and the temperatures leave the property domain.
        { "m2-substation.fluid", SolveTermination.NonFinite },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task EachSampleStandsWhereItStood(string sample, SolveTermination? expected)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);

        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample));
        var run = await loop.RunAsync(
            GraphFixture.Bind(source), Water.Instance, sample, TestContext.Current.CancellationToken);

        if (expected is null)
        {
            Assert.False(run.IsSuccess, $"{sample} now reaches the solver; record its termination.");

            return;
        }

        Assert.True(run.IsSuccess, run.Error?.Message);
        Assert.Equal(expected, run.Value.Solve.Termination);
    }
}
