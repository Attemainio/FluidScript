using System.Diagnostics;

using FluidScript.Core.Layout;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>Where the 200-component model's layout time goes (<c>07</c>'s layout-solve line, <c>D-103</c>).</summary>
public sealed class LayoutTimingTests
{
    [Fact]
    [Trait("Category", "Diagnostic")]
    public void TheTwoHundredComponentLayoutIsTimed()
    {
        var input = ContractFixture.Compile(ReferenceModels.DistributionHeader(ReferenceModels.TwoHundredComponentConsumers));

        double Best(Func<object> work)
        {
            var best = double.PositiveInfinity;
            for (var i = 0; i < 5; i++)
            {
                var clock = Stopwatch.StartNew();
                work();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }

            return best;
        }

        var hints = Best(() => LayoutHintsDerivation.Derive(input.Graph, input.Model, null));
        var (derived, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        var solve = Best(() => LayoutSolver.Solve(input.Graph, input.Model, derived));
        var composed = Best(() => LayoutSolver.Solve(input.Graph, input.Model, derived, LayoutSolver.DefaultMargin, LayoutEngineKind.Composed));

        // The composed engine (D-153) is timed beside the ladder engine while it is built, so a stage that costs too much shows before the switch.
        TestContext.Current.TestOutputHelper?.WriteLine($"hints {hints:F1} ms, layout solve {solve:F1} ms, composed {composed:F1} ms (best of 5)");
        File.WriteAllText(Path.Combine(RepositoryLayout.Diagnostics, "layout-timing.txt"), $"hints {hints:F1} ms, ladder {solve:F1} ms, composed {composed:F1} ms (best of 5)\n");
        Assert.True(solve < 250, $"layout solve took {solve:F0} ms");
    }
}
