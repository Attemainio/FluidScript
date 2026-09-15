using System.Collections.Immutable;

namespace FluidScript.Core.Model;

/// <summary>The symbol definition every kind is drawn with (<c>D-20</c>, <c>D-24</c>).</summary>
/// <remarks>
/// <para>
/// <strong>The box and the anchors are the contract; the strokes are the picture.</strong> Layout
/// reasons on <see cref="SymbolWire.ViewBox"/> and <see cref="SymbolWire.PortAnchors"/> and never on a
/// primitive (<c>53</c>), so a symbol whose strokes change without its box or anchors changing moves
/// nothing. The anchors here agree with <c>25</c>'s <c>PortSides</c> by construction: an inlet on the
/// west edge, an outlet on the east, a three-way valve's <c>a</c> north and <c>b</c> south, an
/// exchanger's second side north and south. The port side and the anchor are the same fact stated
/// once each in two forms, and a test holds them together.
/// </para>
/// <para>
/// A node's ports are not named in advance, so its one anchor is the wildcard <c>*</c> at the
/// centre: every port of a node attaches there. Instruments and controllers likewise -- they have no
/// ports, and the wildcard is where a signal line meets them.
/// </para>
/// </remarks>
public static class SymbolCatalog
{
    private static readonly ImmutableArray<double> UnitBox = [-0.5, -0.5, 1, 1];
    private static readonly ImmutableArray<double> Above = [0, -0.65];
    private static readonly ImmutableArray<double> West = [-0.5, 0];
    private static readonly ImmutableArray<double> East = [0.5, 0];
    private static readonly ImmutableArray<double> North = [0, -0.5];
    private static readonly ImmutableArray<double> South = [0, 0.5];
    private static readonly ImmutableArray<double> Centre = [0, 0];

    /// <summary>Gets every definition, in kind order.</summary>
    public static ImmutableArray<SymbolWire> All { get; } =
    [
        new()
        {
            Id = "node.junction",
            ViewBox = [-0.15, -0.15, 0.3, 0.3],
            Primitives = [],
            PortAnchors = Anchors(("*", Centre)),
            LabelAnchor = [0, -0.35],
        },
        new()
        {
            Id = "pipe.standard",
            ViewBox = [-0.5, -0.1, 1, 0.2],
            Primitives = [],
            PortAnchors = Anchors(("in", West), ("out", East)),
            LabelAnchor = [0, -0.3],
        },
        new()
        {
            Id = "heat_exchanger.standard",
            ViewBox = UnitBox,
            Primitives = [],
            PortAnchors = Anchors(("in", West), ("out", East), ("in2", North), ("out2", South)),
            LabelAnchor = Above,
        },
        new()
        {
            Id = "valve.standard",
            ViewBox = [-0.5, -0.3, 1, 0.6],
            Primitives = [],
            PortAnchors = Anchors(("in", West), ("out", East)),
            LabelAnchor = [0, -0.45],
        },
        new()
        {
            Id = "three_way_valve.standard",
            ViewBox = UnitBox,
            Primitives = [],
            PortAnchors = Anchors(("ab", West), ("a", North), ("b", South)),
            LabelAnchor = Above,
        },
        new()
        {
            Id = "pump.standard",
            ViewBox = UnitBox,
            Primitives = [],
            PortAnchors = Anchors(("in", West), ("out", East)),
            LabelAnchor = Above,
        },
        new()
        {
            Id = "tank.stratified",
            ViewBox = [-0.5, -0.8, 1, 1.6],
            Primitives = [],
            PortAnchors = Anchors(),
            IndexedPortAnchors =
            [
                new() { Prefix = "in", Side = "west", VerticalCoordinate = "port.elevation", MinIndex = 1, MaxIndex = 16 },
                new() { Prefix = "out", Side = "east", VerticalCoordinate = "port.elevation", MinIndex = 1, MaxIndex = 16 },
            ],
            LabelAnchor = [0, -0.95],
        },
        Instrument("t_sensor.standard"),
        Instrument("p_sensor.standard"),
        Instrument("flow_sensor.standard"),
        Instrument("controller.standard"),
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

    private static SymbolWire Instrument(string id) => new()
    {
        Id = id,
        ViewBox = [-0.3, -0.3, 0.6, 0.6],
        Primitives = [],
        PortAnchors = Anchors(("*", Centre)),
        LabelAnchor = Centre,
    };

    private static Dictionary<string, ImmutableArray<double>> Anchors(params (string Port, ImmutableArray<double> At)[] anchors)
    {
        var result = new Dictionary<string, ImmutableArray<double>>(StringComparer.Ordinal);

        foreach (var (port, at) in anchors)
        {
            result[port] = at;
        }

        return result;
    }
}
