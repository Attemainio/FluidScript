using System.Collections.Immutable;

using FluidScript.Core.Layout;
using FluidScript.Core.Model;
using FluidScript.Core.Tests.Model;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Layout;

/// <summary>
/// A label is laid out with a box, not a point (<c>53</c> label geometry, <c>C-84</c>): the box is the canvas's own
/// metric, the layout moves the label until the box is clear, and when nothing is clear it says so instead of
/// pretending.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LabelLayoutTests
{
    private static string Ladder => Path.Combine(RepositoryLayout.Tests, "FluidScript.Core.Tests", "Layout", "Ladder");

    private static (Scene Scene, ModelContractInput Input) Solve(string source)
    {
        var input = ContractFixture.Compile(source);
        var (hints, _) = LayoutHintsDerivation.Derive(input.Graph, input.Model, null);
        return (LayoutSolver.Solve(input.Graph, input.Model, hints, LayoutSolver.MarginOf(input.Model)), input);
    }

    [Fact]
    public void TheBoxIsTheCanvasMetric()
    {
        // 55 draws an 11 px label at 60 px per world unit and reserves 0.62 em per character; the box the layout
        // reserves is exactly that, so what the canvas draws is what the layout kept clear.
        Assert.Equal(11.0 / 60.0, LabelLayout.Size, 12);
        Assert.Equal(0.62, LabelLayout.Advance, 12);

        var box = LabelLayout.BoxFor("PU_RAD", new Point(2, 3));
        Assert.Equal(6 * 0.62 * LabelLayout.Size, box.Width, 9);
        Assert.Equal(LabelLayout.Size, box.Height, 9);
        Assert.Equal(new Point(2, 3), box.Centre);

        // An empty text still reserves one advance, so a box is never degenerate.
        Assert.Equal(0.62 * LabelLayout.Size, LabelLayout.BoxFor(string.Empty, new Point(0, 0)).Width, 9);
    }

    [Fact]
    public void ALabelStartsOnItsSymbolsSideAndStaysThereWhenClear()
    {
        // A pump alone: nothing to collide with, so the label sits where the symbol's anchor says, just off the box.
        var (scene, _) = Solve(File.ReadAllText(Path.Combine(Ladder, "step-01-pump.fluid")));
        var pump = scene.Placements.Single(static p => p.ComponentId == "PU1");

        Assert.True(pump.LabelClear);
        Assert.False(pump.Inner.Intersects(pump.LabelBox));
        // The box and its centre are each rounded to a micro-unit on the way out (ToScene), so they agree to that.
        Assert.Equal(pump.LabelAt.X, pump.LabelBox.Centre.X, 1e-5);
        Assert.Equal(pump.LabelAt.Y, pump.LabelBox.Centre.Y, 1e-5);
        Assert.Equal(pump.Inner.Centre.X, pump.LabelAt.X, 9);
    }

    [Fact]
    public void ALabelMovesOffALineThatWouldCrossIt()
    {
        // A placement whose first label position sits on a pipe: the layout slides it along the edge, and the
        // box it ends in touches neither the line nor the owner.
        var owner = Box.Around(new Point(0, 0), 1, 1);
        var placement = new Placement
        {
            ComponentId = "PU1",
            SymbolId = "pump",
            Inner = owner,
            Outer = owner.Grow(0.25),
            Rotation = 0,
            Mirrored = false,
            Arrangement = "",
            Anchors = ImmutableSortedDictionary<string, PlacedAnchor>.Empty,
            LabelAt = new Point(0, owner.Top + 0.15),
            LabelBox = new Box(0, 0, 0, 0),
            LabelClear = false,
            Source = "computed",
            Group = "",
        };
        var line = new Route("C1", "pipe", "supply", [new Point(-3, owner.Top + 0.15), new Point(3, owner.Top + 0.15)], []);

        var laid = LabelLayout.Place([placement], [line], static p => p.ComponentId);
        var label = laid.Single();

        Assert.True(label.LabelClear);
        Assert.False(label.LabelBox.Intersects(owner));
        Assert.NotEqual(placement.LabelAt, label.LabelAt);
    }

    [Fact]
    public void ALabelThatCannotBeClearSaysSo()
    {
        // Every side of the owner walled in by a box: the label lands somewhere, and LabelClear = false is what
        // tells the canvas to draw a leader instead of leaving a reader to guess which symbol it names.
        var owner = Box.Around(new Point(0, 0), 1, 1);
        var wall = Box.Around(new Point(0, 0), 8, 8);
        var placement = new Placement
        {
            ComponentId = "PU1",
            SymbolId = "pump",
            Inner = owner,
            Outer = owner.Grow(0.25),
            Rotation = 0,
            Mirrored = false,
            Arrangement = "",
            Anchors = ImmutableSortedDictionary<string, PlacedAnchor>.Empty,
            LabelAt = new Point(0, owner.Top + 0.15),
            LabelBox = new Box(0, 0, 0, 0),
            LabelClear = false,
            Source = "computed",
            Group = "",
        };
        var blocker = placement with { ComponentId = "HE1", SymbolId = "heat_exchanger", Inner = wall, Outer = wall.Grow(0.25), LabelAt = new Point(0, wall.Top + 0.15) };

        var laid = LabelLayout.Place([placement, blocker], [], static p => p.ComponentId);

        Assert.False(laid[0].LabelClear);
        Assert.True(laid[1].LabelClear);
    }

    [Theory]
    [InlineData("step-08e-header-mixed")]
    [InlineData("step-10-instruments")]
    [InlineData("step-11c-tour-loops")]
    public void EveryLabelOnALadderStepIsClear(string step)
    {
        // The busiest steps of the ladder: every label ends clear of every symbol, every line and every other label,
        // which is what the audit's three label findings measure.
        var (scene, input) = Solve(File.ReadAllText(Path.Combine(Ladder, step + ".fluid")));
        var findings = SceneAudit.Findings(scene, input.Model).Where(static f => f.Kind is "label-in-inner" or "label-in-label" or "line-in-label").ToList();

        Assert.True(findings.Count == 0, step + ":\n" + string.Join("\n", findings));
        Assert.All(scene.Placements.Where(static p => !p.IsInline), static p => Assert.True(p.LabelClear, p.ComponentId));
    }

    [Fact]
    public void TheExtentTakesTheLabelsIn()
    {
        // A label never hangs off the picture: the scene's extent contains every placed label's box.
        var (scene, _) = Solve(File.ReadAllText(Path.Combine(Ladder, "step-05-primary.fluid")));

        foreach (var placement in scene.Placements.Where(static p => !p.IsInline))
        {
            var box = placement.LabelBox;
            var extent = scene.Extent;
            Assert.True(
                box.X >= extent.X - 1e-6 && box.Right <= extent.Right + 1e-6 && box.Y >= extent.Y - 1e-6 && box.Top <= extent.Top + 1e-6,
                placement.ComponentId);
        }
    }
}
