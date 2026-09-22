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

        // `D-141` (P6.0): the control line's setpoint holds `N2` at 20 C in the design solve and chooses
        // the valve position, so the run's t = 0 is the cooling loop's design state -- 0.2393 kg/s
        // secondary, 0.0763 recirculating, the valve at 0.501, 2.55 m of head. Without that rule the
        // valve defaulted to 1 and `HE1.out.t`'s promotion drove the pump non-finite (`S-75`); with the
        // segments of `PB` reading their stated DN20 (`C-113`) the transport figures in `33` hold.
        { "m4-demand-step.fluid", SolveTermination.Converged },

        // Converged, after three fixes in a row: `S-30b` anchored the seed's temperatures, `S-35` stopped
        // a component in the bypass leg collapsing the valve's three ports onto one pressure, and
        // `C-63`'s three-way pass finally chose `3WV.kv` rather than leaving it on the bootstrap Kv 630.
        // It reaches `01`'s figures: the mixing node at 19.99 C against 20, the return at 49.94 against
        // 50, and 0.0763 kg/s recirculating. **An `M2a` exit criterion, met.**
        { "m2-cooling-loop.fluid", SolveTermination.Converged },

        // `D-91`: positive load capacity now reaches the signed core correctly. The experimental
        // Converged, and on `01`'s own figures: 0.1914 and 0.2392 kg/s drawn from the 60 C header, 0.4306
        // kg/s through the source, the coils at 50 C. `S-58` was the whole of it after `D-90`/`D-91`: a
        // junction mixed its inlets by a plain average, so the valve positions moved nothing and Newton
        // ran them to a stop. **The last `M2a` exit criterion, met** -- `OuterLoopTests` holds the numbers.
        // `S-32` closed by `P4.1`: the coupled exchanger's design point pins the primary's flow (`D-97`),
        // the datum sits at the pump suction (`D-98`), and the solve reaches `01`'s figures -- UA 12.07
        // kW/K, 3.66 m2, 0.895 kg/s primary. `OuterLoopTests` holds the numbers.
        { "m2-substation.fluid", SolveTermination.Converged },
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task EachSampleStandsWhereItStood(string sample, SolveTermination? expected)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value),
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
