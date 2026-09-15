using System.Collections.Immutable;

using FluidScript.Core.Model;

namespace FluidScript.Core.Tests.Model;

/// <summary>The symbol catalogue's own invariants: strokes inside the box, anchors on its edge, the conventions <c>53</c> lists.</summary>
[Trait("Category", "Unit")]
public sealed class SymbolCatalogTests
{
    private static readonly ImmutableHashSet<string> Kinds = ["rect", "line", "circle", "polyline", "polygon"];
    private static readonly ImmutableHashSet<string> Fills = ["state", "stroke"];
    private static readonly ImmutableHashSet<string> Closed = ["rect", "circle", "polygon"];

    [Fact]
    public void EverySymbolHasStrokes() =>
        Assert.All(SymbolCatalog.All, static symbol => Assert.NotEmpty(symbol.Primitives));

    [Fact]
    public void EveryPrimitiveNamesItsKindAndCarriesOnlyThatKindsFields() =>
        Assert.All(SymbolCatalog.All.SelectMany(static s => s.Primitives), static primitive =>
        {
            Assert.Contains(primitive.Kind, Kinds);

            switch (primitive.Kind)
            {
                case "rect":
                    Assert.NotNull(primitive.X);
                    Assert.NotNull(primitive.Y);
                    Assert.NotNull(primitive.Width);
                    Assert.NotNull(primitive.Height);
                    Assert.Null(primitive.R);
                    Assert.Null(primitive.Points);
                    break;
                case "circle":
                    Assert.NotNull(primitive.X);
                    Assert.NotNull(primitive.Y);
                    Assert.NotNull(primitive.R);
                    Assert.Null(primitive.Width);
                    break;
                case "line":
                    Assert.NotNull(primitive.From);
                    Assert.NotNull(primitive.To);
                    Assert.Null(primitive.Fill);
                    break;
                default:
                    Assert.NotNull(primitive.Points);
                    Assert.True(primitive.Points!.Value.Length >= 6 && primitive.Points.Value.Length % 2 == 0);
                    break;
            }

            if (primitive.Fill is not null)
            {
                Assert.Contains(primitive.Fill, Fills);
            }
        });

    [Fact]
    public void EveryStrokeStaysInsideTheBox() =>
        Assert.All(SymbolCatalog.All, static symbol =>
        {
            var (x0, y0, x1, y1) = Box(symbol);

            foreach (var point in symbol.Primitives.SelectMany(Points))
            {
                Assert.True(
                    point.X >= x0 - 1e-9 && point.X <= x1 + 1e-9 && point.Y >= y0 - 1e-9 && point.Y <= y1 + 1e-9,
                    $"{symbol.Id}: ({point.X}, {point.Y}) is outside [{x0}, {y0}]..[{x1}, {y1}].");
            }
        });

    [Fact]
    public void EveryNamedAnchorSitsOnTheBoxEdgeAndFacesOutward() =>
        Assert.All(SymbolCatalog.All, static symbol =>
        {
            var (x0, y0, x1, y1) = Box(symbol);

            foreach (var (set, anchors) in Sets(symbol))
            {
                foreach (var (port, anchor) in anchors)
                {
                    var at = anchor.At;

                    if (port == "*")
                    {
                        Assert.Equal([0, 0], at);
                        Assert.Null(anchor.Direction);
                        continue;
                    }

                    var expected = (Math.Abs(at[0] - x0) < 1e-9, Math.Abs(at[0] - x1) < 1e-9, Math.Abs(at[1] - y0) < 1e-9, Math.Abs(at[1] - y1) < 1e-9) switch
                    {
                        (true, _, _, _) => (-1.0, 0.0),
                        (_, true, _, _) => (1.0, 0.0),
                        (_, _, true, _) => (0.0, -1.0),
                        (_, _, _, true) => (0.0, 1.0),
                        _ => throw new Xunit.Sdk.XunitException($"{symbol.Id}[{set}].{port} at ({at[0]}, {at[1]}) is not on the box edge."),
                    };

                    Assert.True(anchor.Direction.HasValue, $"{symbol.Id}[{set}].{port} has no direction.");
                    Assert.Equal(expected, (anchor.Direction.Value[0], anchor.Direction.Value[1]));
                }
            }
        });

    [Fact]
    public void AnAlternativeArrangementNamesTheSamePorts() =>
        Assert.All(SymbolCatalog.All.Where(static s => s.Alternatives is not null), static symbol =>
            Assert.All(symbol.Alternatives!, alternative =>
                Assert.Equal(symbol.PortAnchors.Keys.Order(StringComparer.Ordinal), alternative.Value.Keys.Order(StringComparer.Ordinal))));

    [Fact]
    public void TheExchangerOffersAThroughPassAndAUPass()
    {
        var exchanger = Symbol("heat_exchanger.standard");
        var u = Assert.Contains("u", exchanger.Alternatives!);

        // Through-pass: each side enters one end and leaves the other, primary left, secondary right.
        Assert.Equal([0, -1], exchanger.PortAnchors["in"].Direction!.Value);
        Assert.Equal([0, 1], exchanger.PortAnchors["out"].Direction!.Value);
        Assert.True(exchanger.PortAnchors["in"].At[0] < 0 && exchanger.PortAnchors["in2"].At[0] > 0);

        // U-pass: each side in and out on its own flank, counterflow.
        Assert.Equal([-1, 0], u["in"].Direction!.Value);
        Assert.Equal([-1, 0], u["out"].Direction!.Value);
        Assert.Equal([1, 0], u["in2"].Direction!.Value);
        Assert.True(u["in"].At[1] < u["out"].At[1] && u["in2"].At[1] > u["out2"].At[1]);

        Assert.Equal("north", SymbolCatalog.SideOf("heat_exchanger", "in"));
        Assert.Equal("west", SymbolCatalog.SideOf("tank", "in3"));
        Assert.Null(SymbolCatalog.SideOf("node", "1"));
    }

    private static IEnumerable<(string Name, IReadOnlyDictionary<string, AnchorWire> Anchors)> Sets(SymbolWire symbol) =>
        new[] { ("default", symbol.PortAnchors) }
            .Concat((symbol.Alternatives ?? new Dictionary<string, IReadOnlyDictionary<string, AnchorWire>>()).Select(static a => (a.Key, a.Value)));

    [Fact]
    public void TheConventionsOf53Hold()
    {
        var node = Symbol("node.junction");
        Assert.Equal("stroke", Assert.Single(node.Primitives).Fill);

        var pump = Symbol("pump.standard");
        var arrow = Assert.Single(pump.Primitives, static p => p.Kind == "polygon");
        Assert.Equal("stroke", arrow.Fill);
        Assert.True(arrow.Points!.Value.Max() > 0, "the pump's triangle points east, the way in→out flows");
        Assert.Equal("state", Assert.Single(pump.Primitives, static p => p.Kind == "circle").Fill);

        Assert.Equal(2, Symbol("valve.standard").Primitives.Count(static p => p.Kind == "polygon" && p.Fill == "state"));
        Assert.Equal(3, Symbol("three_way_valve.standard").Primitives.Count(static p => p.Kind == "polygon"));
        Assert.Equal(2, Symbol("heat_exchanger.standard").Primitives.Count(static p => p.Kind == "line" && Length(p) > 0.9));

        Assert.True(Assert.Single(Symbol("controller.standard").Primitives).Dashed);
        Assert.All(SymbolCatalog.All.Where(static s => s.Id != "controller.standard").SelectMany(static s => s.Primitives), static p => Assert.Null(p.Dashed));
    }

    [Fact]
    public void EveryFillSlotIsAClosedShape() =>
        Assert.All(
            SymbolCatalog.All.SelectMany(static s => s.Primitives).Where(static p => p.Fill == "state"),
            static p => Assert.Contains(p.Kind, Closed));

    private static SymbolWire Symbol(string id) => SymbolCatalog.All.Single(s => s.Id == id);

    private static (double X0, double Y0, double X1, double Y1) Box(SymbolWire symbol) =>
        (symbol.ViewBox[0], symbol.ViewBox[1], symbol.ViewBox[0] + symbol.ViewBox[2], symbol.ViewBox[1] + symbol.ViewBox[3]);

    private static double Length(PrimitiveWire line)
    {
        var dx = line.To!.Value[0] - line.From!.Value[0];
        var dy = line.To.Value[1] - line.From.Value[1];
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static IEnumerable<(double X, double Y)> Points(PrimitiveWire primitive)
    {
        switch (primitive.Kind)
        {
            case "rect":
                yield return (primitive.X!.Value, primitive.Y!.Value);
                yield return (primitive.X.Value + primitive.Width!.Value, primitive.Y.Value + primitive.Height!.Value);
                break;
            case "circle":
                yield return (primitive.X!.Value - primitive.R!.Value, primitive.Y!.Value - primitive.R.Value);
                yield return (primitive.X.Value + primitive.R.Value, primitive.Y.Value + primitive.R.Value);
                break;
            case "line":
                yield return (primitive.From!.Value[0], primitive.From.Value[1]);
                yield return (primitive.To!.Value[0], primitive.To.Value[1]);
                break;
            default:
                for (var i = 0; i < primitive.Points!.Value.Length; i += 2)
                {
                    yield return (primitive.Points.Value[i], primitive.Points.Value[i + 1]);
                }

                break;
        }
    }
}
