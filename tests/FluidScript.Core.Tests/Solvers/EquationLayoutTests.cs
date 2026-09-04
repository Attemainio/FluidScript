using FluidScript.Core.Components;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The residual vector's layout, checked against the counting table on every sample.</summary>
/// <remarks>
/// The row-side twin of <see cref="SystemLayoutTests"/>, and the same argument: the table is the same
/// number computed a second way, so a layout built to consume it is a real check and a layout built
/// beside it is two guesses (<c>S-9</c>).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class EquationLayoutTests
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
    public void TheRowsAssembledAreTheEquationsTheTableCounted(string sample)
    {
        var (graph, posedness) = Lower(sample);
        var layout = EquationLayout.Build(graph, posedness);

        Assert.Equal(posedness.Counting.Equations - RowAllowance.CoupledCrossings(graph), layout.Count);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ASquareCircuitAssemblesAsManyRowsAsItHasColumns(string sample)
    {
        // The whole point of both layouts, stated once. It is skipped rather than weakened where the
        // circuit is not square to begin with: an under-specified script is the user's business and
        // FS2211's, and asserting anything about its shape here would be asserting about a diagnostic.
        //
        // A free enthalpy level used to be skipped alongside those, which excused the one sample that
        // had one -- and that sample was exactly the one assembling a row more than it had columns
        // (`S-24`). The level is a dropped equation now (`D-75`), so there is nothing left to excuse.
        var (graph, posedness) = Lower(sample);
        var counting = posedness.Counting;
        var allowance = RowAllowance.CoupledCrossings(graph);

        Assert.SkipWhen(
            counting.Excess != 0 || allowance != 0,
            $"{sample} is not a square, fully-modelled circuit: excess {counting.Excess}, "
            + $"rows the table does not model {allowance}.");

        Assert.Equal(SystemLayout.Build(graph, counting).Count, EquationLayout.Build(graph, posedness).Count);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryRowKnowsItsPositionItsOwnerAndItsUnit(string sample)
    {
        var (graph, posedness) = Lower(sample);
        var layout = EquationLayout.Build(graph, posedness);

        for (var index = 0; index < layout.Count; index++)
        {
            var row = layout.Rows[index];

            Assert.Equal(index, row.Index);
            Assert.False(string.IsNullOrWhiteSpace(row.OwnerComponentId));
            Assert.False(string.IsNullOrWhiteSpace(row.Name));
            Assert.False(string.IsNullOrWhiteSpace(row.ResidualSiUnit));
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryResidualAComponentWritesLandsOnItsOwnRowOrIsDropped(string sample)
    {
        // S-7: the mapping from row to component exists only in this layer. A component evaluates into
        // a span of its own length and the assembler scatters it, so the scatter has to be a bijection
        // onto that component's rows -- with exactly the dropped residual going nowhere.
        var (graph, posedness) = Lower(sample);
        var layout = EquationLayout.Build(graph, posedness);
        var claimed = new HashSet<int>();

        for (var index = 0; index < graph.Components.Length; index++)
        {
            var component = graph.Components[index];
            var rows = layout.Components[index];

            Assert.Equal(component.EquationCount, rows.LocalCount);

            for (var local = 0; local < rows.LocalCount; local++)
            {
                var row = layout.Row(index, local);

                if (row < 0)
                {
                    Assert.Equal(rows.DroppedLocal, local);
                    continue;
                }

                Assert.InRange(row, rows.FirstRow, rows.FirstRow + rows.RowCount - 1);
                Assert.True(claimed.Add(row), $"row {row} is written by more than one component");
                Assert.Equal(component.Name, layout.Rows[row].OwnerComponentId);
            }
        }

        Assert.Equal(layout.LinkOffset, claimed.Count);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void OnlyARedundantBalanceIsEverDroppedAndOnlyWhereItsComponentSaysSo(string sample)
    {
        // Two drops, one argument (`D-75`). A closed hydraulic component's mass balances sum to zero,
        // so one of them is redundant; a closed, thermally uncoupled one's energy balances sum to zero
        // for the same reason a branch's two ends cancel, so one of those is too. Nothing else may be
        // dropped, and neither may be dropped in a component whose partition does not permit it.
        var (graph, posedness) = Lower(sample);
        var layout = EquationLayout.Build(graph, posedness);
        var levels = posedness.Counting.LevelComponents.Select(static level => level.Index).ToHashSet();

        foreach (var rows in layout.Components.Where(static rows => rows.HasDrop))
        {
            var declarations = graph.Components[rows.Component].DeclareEquations();
            var record = layout.Dropped.Single(record => record.Component == graph.Components[rows.Component].Name);

            Assert.Contains(declarations[rows.DroppedLocal].Kind, new[] { EquationKind.Mass, EquationKind.Energy });

            if (declarations[rows.DroppedLocal].Kind == EquationKind.Energy)
            {
                Assert.Contains(record.Hydraulic, levels);
            }
        }

        var closed = posedness.Hydraulics.Count(static hydraulic => !hydraulic.HasUnknownFlux);

        Assert.Equal(layout.Dropped.Length, layout.Components.Count(static rows => rows.HasDrop));
        Assert.InRange(layout.Dropped.Length, 0, closed + levels.Count);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void TheRowsAreGroupedSoTheAssemblyOwnedOnesSitTogether(string sample)
    {
        var (graph, posedness) = Lower(sample);
        var layout = EquationLayout.Build(graph, posedness);
        var counting = posedness.Counting;

        Assert.True(layout.LinkOffset <= layout.BoundaryOffset);
        Assert.True(layout.BoundaryOffset <= layout.DatumOffset);
        Assert.True(layout.DatumOffset <= layout.ConstraintOffset);
        Assert.Equal(counting.IdealLinks.Length, layout.BoundaryOffset - layout.LinkOffset);
        Assert.Equal(counting.StatedPressures, layout.DatumOffset - layout.BoundaryOffset);
        Assert.Equal(counting.Datums, layout.ConstraintOffset - layout.DatumOffset);
        Assert.Equal(counting.Constraints.Length, layout.Count - layout.ConstraintOffset);
    }

    [Fact]
    public void TheCoolingLoopAssemblesTwentyRowsAgainstTwentyColumns()
    {
        // Six nodes each declare an energy balance; N1, N2 and N3 add a mass balance and the three
        // inferred nodes do not, because a degree-two node's balance is the branch's own flow minus
        // itself. 3WV declares its balance and two Kv laws; the pump, exchanger and pipe one pressure
        // relation each. That is 15. The assembler adds the ideal link N1 - N2, the two stated
        // pressures, and the two promotion pairings -- and no datum, because a stated pressure is one.
        var (graph, posedness) = Lower("m2-cooling-loop.fluid");
        var layout = EquationLayout.Build(graph, posedness);

        Assert.Equal(15, layout.LinkOffset);
        Assert.Equal(16, layout.BoundaryOffset);
        Assert.Equal(18, layout.DatumOffset);
        Assert.Equal(18, layout.ConstraintOffset);
        Assert.Equal(20, layout.Count);
        Assert.Equal(SystemLayout.Build(graph, posedness.Counting).Count, layout.Count);

        // Both boundaries admit an unknown flux, so nothing here is closed and no balance is redundant.
        Assert.Empty(layout.Dropped);

        Assert.Equal(6, layout.Rows.Count(static row => row.Kind == EquationKind.Energy));
        Assert.Equal(4, layout.Rows.Count(static row => row.Kind == EquationKind.Mass));
    }

    [Fact]
    public void ADroppedBalanceStaysWhereItWasWhenTheScriptGrows()
    {
        // Which balance is redundant is arbitrary; which one is dropped must not move, or an edit that
        // added a component at the end would change the Jacobian's structure everywhere above it. The
        // first element in graph order is chosen precisely because graph order is declaration order.
        // A series loop has no junction at all, so no node in it carries a mass balance and there is
        // nothing redundant to drop. The three-way valve is what makes the balance exist.
        const string Closed = """
            fluidscript 1
            circuit closed 100
            fluid water

            N1 node t=60
            PU1 pump head=6 flow=0.24
            HE1 heat_exchanger power=-30
            3WV three_way_valve kv=6.3
            P1 pipe length=10 dn=25

            connections
            N1 - PU1
            PU1 - HE1
            HE1 - 3WV
            3WV - N1
            3WV - P1
            P1 - N1
            """;

        var (graph, posedness) = Lower(Closed);
        var layout = EquationLayout.Build(graph, posedness);

        Assert.NotEmpty(layout.Dropped);

        var (grown, grownPosedness) = Lower(Closed
            .Replace(
                "P1 pipe length=10 dn=25",
                "P1 pipe length=10 dn=25\nV1 valve kv=6.3",
                StringComparison.Ordinal)
            .Replace("P1 - N1", "P1 - V1\nV1 - N1", StringComparison.Ordinal));

        var after = EquationLayout.Build(grown, grownPosedness);

        Assert.Equal(layout.Dropped[0].Component, after.Dropped[0].Component);
        Assert.Equal(layout.Dropped[0].Equation, after.Dropped[0].Equation);
    }

    private static (CircuitGraph Graph, WellPosednessResult Posedness) Lower(string source)
    {
        var graph = source.EndsWith(".fluid", StringComparison.Ordinal)
            ? GraphFixture.Lower(File.ReadAllText(Path.Combine(RepositoryLayout.Samples, source))).Graph
            : GraphFixture.Lower(source).Graph;

        return (graph, WellPosedness.Check(graph));
    }
}
