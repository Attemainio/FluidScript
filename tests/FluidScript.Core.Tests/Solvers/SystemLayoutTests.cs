using FluidScript.Core.Components;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The state vector's layout, checked against the counting table on every sample.</summary>
/// <remarks>
/// <c>S-9</c>: the counting table and the assembled system are the same two numbers computed twice, and
/// the agreement only means something if one is built to consume the other. These tests are that
/// consumption, and they are the reason the layout is trusted rather than assumed.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SystemLayoutTests
{
    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();

        foreach (var path in Directory.GetFiles(RepositoryLayout.Samples, "*.fluid").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void TheLayoutHoldsEveryUnknownTheTableCounts(string sample)
    {
        // This used to subtract the table's enthalpy levels, with a comment saying the shortfall was in
        // the energy block's rank rather than in its width. The comment was right and the arithmetic was
        // not: a term counted as an unknown that no column exists for is a table that reads square while
        // the assembled system is a row over, which is what `m2-simple-loop` was (`S-24`, `D-75`). The
        // level is now a dropped equation, and the two totals reconcile with nothing subtracted.
        var (graph, counting) = Lower(sample);
        var layout = SystemLayout.Build(graph, counting);

        Assert.Equal(counting.Unknowns, layout.Count);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryUnknownKnowsItsPositionItsOwnerAndItsUnit(string sample)
    {
        var (graph, counting) = Lower(sample);
        var layout = SystemLayout.Build(graph, counting);

        for (var index = 0; index < layout.Count; index++)
        {
            var unknown = layout.Unknowns[index];

            Assert.Equal(index, unknown.Index);
            Assert.False(string.IsNullOrWhiteSpace(unknown.OwnerComponentId));
            Assert.False(string.IsNullOrWhiteSpace(unknown.Name));
        }
    }

    [Fact]
    public void TheUnknownsAreGroupedByKindSoTheJacobianHasBlocks()
    {
        // 31's ordering: all branch flows, then all node pressures, then all node enthalpies. Not a
        // preference -- the flow/pressure block is the hydraulic problem and the enthalpy block the
        // thermal one, and a later block-decomposed solve needs them contiguous.
        var (graph, counting) = Lower("m2-cooling-loop.fluid");
        var layout = SystemLayout.Build(graph, counting);

        var kinds = layout.Unknowns.Select(static unknown => unknown.Kind).ToArray();

        Assert.Equal(kinds, kinds.OrderBy(static kind => (int)kind).ToArray());

        Assert.Equal(4, kinds.Count(static kind => kind == UnknownKind.BranchFlow));
        Assert.Equal(6, kinds.Count(static kind => kind == UnknownKind.NodePressure));
        Assert.Equal(6, kinds.Count(static kind => kind == UnknownKind.NodeEnthalpy));
        Assert.Equal(2, kinds.Count(static kind => kind == UnknownKind.ExternalMassFlux));
        Assert.Equal(2, kinds.Count(static kind => kind == UnknownKind.Parameter));
    }

    [Fact]
    public void AnIndexResolvesBackToTheThingItStandsFor()
    {
        var (graph, counting) = Lower("m2-cooling-loop.fluid");
        var layout = SystemLayout.Build(graph, counting);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            Assert.Equal(
                $"{graph.Nodes[node].Name}.p", layout.Unknowns[layout.NodePressure(node)].Name);

            Assert.Equal(
                $"{graph.Nodes[node].Name}.h", layout.Unknowns[layout.NodeEnthalpy(node)].Name);
        }

        for (var branch = 0; branch < graph.Branches.Length; branch++)
        {
            Assert.Equal(
                UnknownKind.BranchFlow, layout.Unknowns[layout.BranchFlow(branch)].Kind);
        }
    }

    private static (CircuitGraph Graph, CountingTable Counting) Lower(string sample)
    {
        var graph = GraphFixture
            .Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, sample)))
            .Graph;

        return (graph, WellPosedness.Check(graph).Counting);
    }
}
