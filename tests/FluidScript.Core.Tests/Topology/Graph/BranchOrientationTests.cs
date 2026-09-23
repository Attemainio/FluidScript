using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Topology.Graph;

/// <summary>The canonical direction a branch is written in (<c>C-25</c>).</summary>
public sealed class BranchOrientationTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("m2-simple-loop.fluid")]
    [InlineData("m2-cooling-loop.fluid")]
    [InlineData("m2-distribution-header.fluid")]
    [InlineData("m4-storage-header.fluid")]
    [InlineData("m2-substation.fluid")]
    public void ABranchRunsFromItsLowerNumberedEndToItsHigher(string sample)
    {
        // `C-25`. A decomposition walks from whichever junction it reached first, so `Path` comes out in
        // that order or its reverse, and both readings were defensible. The solver does not care; a
        // golden test over a rendered branch table does, and so will write-back. Measured across the
        // corpus, **every** branch already runs `From` -> `To` in ascending component order -- 20 of 20
        // when this was written -- so the canonical orientation is the one the code already produces,
        // and this pins it rather than changing it.
        var graph = GraphFixture.Lower(
            File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample))).Graph;

        var index = graph.Components
            .Select(static (component, i) => (component, i))
            .ToDictionary(static p => (object)p.component, static p => p.i);

        Assert.NotEmpty(graph.Branches);

        foreach (var branch in graph.Branches)
        {
            Assert.True(
                index[branch.From.Element] <= index[branch.To.Element],
                $"branch {branch.Index} runs {branch.From.Element.Name} -> {branch.To.Element.Name}, "
                + "which is descending; a branch is written from its lower-numbered end");
        }
    }
}
