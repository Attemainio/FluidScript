using System.Collections.Immutable;

namespace FluidScript.Core.Model;

/// <summary>The symbol definition every kind is drawn with (<c>D-20</c>, <c>D-24</c>, <c>D-102</c>).</summary>
/// <remarks>
/// <para>
/// <strong>The box and the anchors are the contract; the strokes are the picture.</strong> Layout
/// reasons on <see cref="SymbolWire.ViewBox"/> and <see cref="SymbolWire.PortAnchors"/> and never on a
/// primitive (<c>53</c>), so a symbol whose strokes change without its box or anchors changing moves
/// nothing. Every anchor carries the outward direction a connection leaves it in, and the box may be
/// nothing. Every anchor carries the outward direction a connection leaves it in, and the box may be
/// rotated by the layout in quarter turns, which rotates the directions with it. Symbol space
/// is the layout plane's: <c>y</c> grows upward (<c>28</c> §1, <c>D-106</c>), so <c>Up</c> is
/// <c>(0, 1)</c> and a rectangle's <c>Y</c> is its bottom edge; the renderer flips once, where it maps
/// units to pixels.
/// </para>
/// <para>
/// A symbol may offer <see cref="SymbolWire.Alternatives"/>: other complete arrangements of the same
/// ports on the same box. The exchanger's default is the through-pass -- each side enters at one end
/// and leaves at the other -- and its alternative <c>u</c> brings each side in and out on its own
/// flank, which is what a substation drawn with the primary to the left wants. The renderer chooses the
/// arrangement and the rotation per instance by the Manhattan length of the connections it has to make
/// (<c>53</c>); Core offers, it does not choose.
/// </para>
/// <para>
/// A node's ports are not named in advance, so its one anchor is the wildcard <c>*</c> at the
/// centre with no direction: every port of a node attaches there. Instruments and controllers likewise
/// -- they have no ports, and the wildcard is where a signal line meets them.
/// </para>
/// </remarks>
public static class SymbolCatalog
{
    private const string State = "state";
    private const string Stroke = "stroke";
    private static readonly ImmutableArray<double> UnitBox = [-0.5, -0.5, 1, 1];
    private static readonly ImmutableArray<double> Above = [0, 0.65];
    private static readonly ImmutableArray<double> Centre = [0, 0];
    private static readonly ImmutableArray<double> Left = [-1, 0];
    private static readonly ImmutableArray<double> Right = [1, 0];
    private static readonly ImmutableArray<double> Up = [0, 1];
    private static readonly ImmutableArray<double> Down = [0, -1];

    /// <summary>Gets every definition, in kind order.</summary>
    /// <remarks>
    /// The glyphs are <c>53</c>'s inventory: a filled dot for a node, the line itself for a pipe, a
    /// crossed rectangle for an exchanger, opposed triangles with a general actuator for a valve, three
    /// triangles for a three-way valve, a circle with a triangle pointing the flow's way for a pump, a
    /// vessel for a tank, and ISA-5.1's instrument bubble -- dashed for a controller. A closed shape
    /// with <c>Fill = "state"</c> is the slot the active colour scale paints (<c>57</c>); one with
    /// <c>Fill = "stroke"</c> is a solid mark. Position bars, layer bands, heat arrows and badges are the
    /// renderer's, drawn from the state, not from here.
    /// </remarks>
    public static ImmutableArray<SymbolWire> All { get; } =
    [
        new()
        {
            Id = "node.junction",
            ViewBox = [-0.1, -0.1, 0.2, 0.2],
            Primitives = [Circle(0, 0, 0.08, Stroke)],
            PortAnchors = Anchors(("*", Centre, null)),
            LabelAnchor = [0, 0.3],
        },
        new()
        {
            Id = "pipe.standard",
            ViewBox = [-0.5, -0.1, 1, 0.2],
            Primitives = [Line(-0.5, 0, 0.5, 0)],
            PortAnchors = Anchors(("in", [-0.5, 0], Left), ("out", [0.5, 0], Right)),
            LabelAnchor = [0, 0.3],
        },
        new()
        {
            // Tall, the primary on the left flank and the secondary on the right (53's vertical default).
            Id = "heat_exchanger.standard",
            ViewBox = [-0.25, -0.5, 0.5, 1],
            Primitives =
            [
                Rect(-0.25, -0.5, 0.5, 1, State),
                Line(-0.25, -0.5, 0.25, 0.5),
                Line(-0.25, 0.5, 0.25, -0.5),
            ],
            PortAnchors = Anchors(
                ("in", [-0.15, 0.5], Up),
                ("out", [-0.15, -0.5], Down),
                ("in2", [0.15, -0.5], Down),
                ("out2", [0.15, 0.5], Up)),
            Alternatives = new Dictionary<string, IReadOnlyDictionary<string, AnchorWire>>(StringComparer.Ordinal)
            {
                ["u"] = Anchors(
                    ("in", [-0.25, 0.3], Left),
                    ("out", [-0.25, -0.3], Left),
                    ("in2", [0.25, -0.3], Right),
                    ("out2", [0.25, 0.3], Right)),
            },
            LabelAnchor = [0, 0.65],
            TransformClass = "standing",
        },
        new()
        {
            Id = "valve.standard",
            ViewBox = [-0.5, -0.3, 1, 0.6],
            Primitives =
            [
                Polygon(State, -0.45, -0.22, 0, 0, -0.45, 0.22),
                Polygon(State, 0.45, -0.22, 0, 0, 0.45, 0.22),
                Line(-0.5, 0, -0.45, 0),
                Line(0.45, 0, 0.5, 0),
                Line(0, 0, 0, 0.22),
                Line(-0.12, 0.22, 0.12, 0.22),
            ],
            PortAnchors = Anchors(("in", [-0.5, 0], Left), ("out", [0.5, 0], Right)),
            LabelAnchor = [0, 0.45],
        },
        new()
        {
            Id = "three_way_valve.standard",
            ViewBox = UnitBox,
            Primitives =
            [
                Polygon(State, -0.45, -0.22, 0, 0, -0.45, 0.22),
                Polygon(State, -0.22, 0.45, 0, 0, 0.22, 0.45),
                Polygon(State, -0.22, -0.45, 0, 0, 0.22, -0.45),
                Line(-0.5, 0, -0.45, 0),
                Line(0, 0.5, 0, 0.45),
                Line(0, -0.45, 0, -0.5),
                Line(0, 0, 0.28, 0),
                Line(0.28, -0.12, 0.28, 0.12),
            ],
            // The body a manufacturer builds: A to AB is the straight run, B the angle port (Belimo G2/G3 manual;
            // Siemens VXG: port I = AB, II = A straight through, III = B). On the drawing the two switched
            // ports are interchangeable (D-112): the common port stays on the straight run, and the layout may
            // give either of `a` and `b` the angle when that is what turns a corner; the renderer labels them.
            PortAnchors = Anchors(("a", [0, 0.5], Up), ("ab", [0, -0.5], Down), ("b", [-0.5, 0], Left)),
            Alternatives = new Dictionary<string, IReadOnlyDictionary<string, AnchorWire>>(StringComparer.Ordinal)
            {
                ["swapped"] = Anchors(("b", [0, 0.5], Up), ("ab", [0, -0.5], Down), ("a", [-0.5, 0], Left)),
            },
            LabelAnchor = Above,
        },
        new()
        {
            Id = "pump.standard",
            ViewBox = UnitBox,
            Primitives =
            [
                Circle(0, 0, 0.45, State),
                Polygon(Stroke, -0.2, -0.28, 0.36, 0, -0.2, 0.28),
                Line(-0.5, 0, -0.45, 0),
                Line(0.45, 0, 0.5, 0),
            ],
            PortAnchors = Anchors(("in", [-0.5, 0], Left), ("out", [0.5, 0], Right)),
            LabelAnchor = Above,
            // A pump pumps left or right (D-113): a quarter turn is admitted, but only where nothing level fits.
            TransformClass = "level",
        },
        new()
        {
            Id = "tank.stratified",
            ViewBox = [-0.5, -0.8, 1, 1.6],
            Primitives = [Rect(-0.4, -0.75, 0.8, 1.5, State)],
            PortAnchors = Anchors(),
            IndexedPortAnchors =
            [
                new() { Prefix = "in", Side = "west", Direction = Left, VerticalCoordinate = "port.elevation", MinIndex = 1, MaxIndex = 16 },
                new() { Prefix = "out", Side = "east", Direction = Right, VerticalCoordinate = "port.elevation", MinIndex = 1, MaxIndex = 16 },
            ],
            LabelAnchor = [0, 0.95],
            TransformClass = "upright",
        },
        Instrument("t_sensor.standard"),
        Instrument("p_sensor.standard"),
        Instrument("flow_sensor.standard"),
        Instrument("controller.standard", dashed: true),
    ];

    /// <summary>The symbol a kind is drawn with.</summary>
    /// <param name="kind">The registry keyword; a boundary node's <c>supply</c> or <c>return</c> is a node.</param>
    /// <returns>The symbol id, always one of <see cref="All"/>.</returns>
    public static string IdFor(string kind) => kind switch
    {
        "node" or "supply" or "return" => "node.junction",
        "tank" => "tank.stratified",
        _ when All.Any(symbol => string.Equals(symbol.Id, kind + ".standard", StringComparison.Ordinal)) => kind + ".standard",
        _ => "node.junction",
    };


    private static SymbolWire Instrument(string id, bool dashed = false) => new()
    {
        Id = id,
        ViewBox = [-0.3, -0.3, 0.6, 0.6],
        Primitives = [Circle(0, 0, 0.28, dashed: dashed)],
        PortAnchors = Anchors(("*", Centre, null)),
        LabelAnchor = Centre,
    };

    private static PrimitiveWire Rect(double x, double y, double width, double height, string? fill = null) =>
        new() { Kind = "rect", X = x, Y = y, Width = width, Height = height, Fill = fill };

    private static PrimitiveWire Circle(double x, double y, double r, string? fill = null, bool dashed = false) =>
        new() { Kind = "circle", X = x, Y = y, R = r, Fill = fill, Dashed = dashed ? true : null };

    private static PrimitiveWire Line(double x0, double y0, double x1, double y1) =>
        new() { Kind = "line", From = [x0, y0], To = [x1, y1] };

    private static PrimitiveWire Polygon(string? fill, params double[] points) =>
        new() { Kind = "polygon", Points = [.. points], Fill = fill };

    private static Dictionary<string, AnchorWire> Anchors(params (string Port, ImmutableArray<double> At, ImmutableArray<double>? Direction)[] anchors)
    {
        var result = new Dictionary<string, AnchorWire>(StringComparer.Ordinal);

        foreach (var (port, at, direction) in anchors)
        {
            result[port] = new AnchorWire { At = at, Direction = direction };
        }

        return result;
    }
}
