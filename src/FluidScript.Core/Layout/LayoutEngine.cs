using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Language;
using FluidScript.Core.Model;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Layout;

/// <summary>A symbol's orientation: which arrangement of its anchors, whether it is mirrored left-to-right first, and its clockwise quarter turn.</summary>
/// <param name="Arrangement"><c>default</c> or one of the symbol's alternatives (<c>D-102</c>).</param>
/// <param name="Mirrored">Whether x is negated before the turn.</param>
/// <param name="Rotation">Clockwise degrees: 0, 90, 180 or 270.</param>
internal readonly record struct Transform(string Arrangement, bool Mirrored, int Rotation)
{
    /// <summary>Gets the untransformed default.</summary>
    public static Transform Identity => new("default", false, 0);

    /// <summary>Every transform a symbol admits, identity first so ties keep the drawn default.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>Arrangements × turns × mirrors, unmirrored first within each turn.</returns>
    public static IEnumerable<Transform> All(SymbolWire symbol)
    {
        var arrangements = new List<string> { "default" };
        arrangements.AddRange((symbol.Alternatives ?? new Dictionary<string, IReadOnlyDictionary<string, AnchorWire>>()).Keys.Order(StringComparer.Ordinal));

        foreach (var arrangement in arrangements)
        {
            foreach (var rotation in new[] { 0, 90, 180, 270 })
            {
                yield return new Transform(arrangement, false, rotation);
                yield return new Transform(arrangement, true, rotation);
            }
        }
    }

    /// <summary>The anchor set this transform's arrangement uses.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>Port name to anchor.</returns>
    public IReadOnlyDictionary<string, AnchorWire> Anchors(SymbolWire symbol) =>
        Arrangement == "default" || symbol.Alternatives is null ? symbol.PortAnchors : symbol.Alternatives[Arrangement];

    /// <summary>A symbol-space point after mirroring and turning.</summary>
    /// <param name="x">Symbol x.</param>
    /// <param name="y">Symbol y, up.</param>
    /// <returns>The point relative to the centre.</returns>
    public Point Apply(double x, double y)
    {
        if (Mirrored)
        {
            x = -x;
        }

        return Rotation switch
        {
            90 => new Point(y, -x),
            180 => new Point(-x, -y),
            270 => new Point(-y, x),
            _ => new Point(x, y),
        };
    }

    /// <summary>A symbol-space direction after mirroring and turning.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The turned direction.</returns>
    public Direction Apply(Direction direction) => Direction.Of(Apply(direction.X, direction.Y)) ?? direction;

    /// <summary>The box size after the turn.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>Width and height.</returns>
    public (double Width, double Height) Size(SymbolWire symbol) =>
        Rotation is 90 or 270 ? (symbol.ViewBox[3], symbol.ViewBox[2]) : (symbol.ViewBox[2], symbol.ViewBox[3]);
}

/// <summary>The rule-based layout engine (<c>28</c>, <c>D-106</c>), built one rule at a time against the ladder in <c>29-layout-ladder</c>.</summary>
/// <remarks>
/// Every rule is numbered by the ladder step that established it and is marked in the code with that
/// number. What no rule covers yet is not guessed: the component is put in the fallback column below
/// everything placed, and its connections go to the router, so a picture shows exactly how far the
/// rules reach. <c>y</c> grows upward.
/// </remarks>
internal sealed partial class LayoutEngine
{
    private const double Eps = 1e-9;

    private readonly CircuitGraph _graph;
    private readonly SemanticModel _model;
    private readonly LayoutHints _hints;
    private readonly double _margin;
    private readonly int _n;
    private readonly Dictionary<string, int> _index;
    private readonly List<Link> _links;
    private readonly SymbolWire[] _symbol;

    private readonly bool[] _placed;
    private readonly bool[] _fallback;
    private readonly bool[] _loop;
    private Point _loopCentre;
    private readonly bool[] _inline;
    
    private readonly Point[] _centre;
    private readonly Transform[] _transform;
    private readonly Dictionary<(int Component, int Port), Direction> _side = [];
    private readonly ImmutableArray<Point>?[] _routeOf;
    private readonly List<(int Link, Route Route)> _routes = [];
    private readonly List<PlacementNote> _trace = [];
    private readonly List<Placement> _instruments = [];
    /// <summary>Every instrument's inner box in placement order; the box at index k is owned by <c>_n + k</c> for the signal router (C-94).</summary>
    private readonly List<Box> _instrumentBoxes = [];

    /// <summary>The links on the supply side (C16), found once when the first route asks.</summary>
    private HashSet<int>? _supply;

    // The groups laid out as one object (A8): every ring and every block, outer before inner, as (kind, members, whether it is the ring that holds the heat source).
    private readonly List<(string Kind, List<int> Members, bool Top)> _groups = [];

    /// <summary>A ring's group entry: its members, and whether it is a top-rail ring or a hanging one.</summary>
    /// <param name="members">The ring's components.</param>
    /// <param name="top">Whether the ring stands on the top rail.</param>
    /// <returns>The group.</returns>
    private static (string Kind, List<int> Members, bool Top) LoopGroup(List<int> members, bool top) => ("loop", members, top);

    public LayoutEngine(CircuitGraph graph, SemanticModel model, LayoutHints hints, double margin)
    {
        _graph = graph;
        _model = model;
        _hints = hints;
        _margin = margin;
        _n = graph.Components.Length;
        _index = Index(graph);
        _links = Links(graph, model, _index);
        _symbol = graph.Components.Select(static c => SymbolCatalog.All.First(s => s.Id == SymbolCatalog.IdFor(c.Kind))).ToArray();
        _placed = new bool[_n];
        _fallback = new bool[_n];
        _inline = new bool[_n];
        _loop = new bool[_n];
        _centre = new Point[_n];
        _transform = new Transform[_n];
        _routeOf = new ImmutableArray<Point>?[_links.Count];

        for (var i = 0; i < _n; i++)
        {
            _transform[i] = Transform.Identity;
        }
    }

    /// <summary>One model connection between two graph components, as the script wrote it.</summary>
    private readonly record struct Link(int Connection, int From, int FromPort, int To, int ToPort);

    /// <summary>A form's attempt on the engine: the placement state as it stood, restored if the form declines (C2, C18, C19, C20).</summary>
    /// <remarks>
    /// Before <c>70</c>'s R5 each form rolled back by hand and each differently: <c>Open</c> restored what
    /// it had placed and the sides it had taken, <c>Closed</c> its corner's inline flag, <c>Loop</c> only
    /// its groups -- and <c>Loop</c> is tried first, so what it placed before declining was still marked
    /// placed when the next form ran. One snapshot, one restore: the groups, the placed, loop and inline
    /// flags, the sides taken and the loop centre. The trace is not restored: a declined attempt's notes
    /// are the provenance the report shows (<c>C-107</c>).
    /// </remarks>
    private sealed class Attempt
    {
        private readonly LayoutEngine _engine;
        private readonly int _groups;
        private readonly bool[] _placed;
        private readonly bool[] _loop;
        private readonly bool[] _inline;
        private readonly Dictionary<(int Component, int Port), Direction> _side;
        private readonly Point _loopCentre;

        public Attempt(LayoutEngine engine)
        {
            _engine = engine;
            _groups = engine._groups.Count;
            _placed = (bool[])engine._placed.Clone();
            _loop = (bool[])engine._loop.Clone();
            _inline = (bool[])engine._inline.Clone();
            _side = new Dictionary<(int Component, int Port), Direction>(engine._side);
            _loopCentre = engine._loopCentre;
        }

        /// <summary>Restores the state the attempt started from, then declines with the reason.</summary>
        /// <param name="reason">Why the form declined.</param>
        /// <returns><see langword="false"/>, so a form can <c>return</c> it.</returns>
        public bool Decline(string reason)
        {
            _engine._groups.RemoveRange(_groups, _engine._groups.Count - _groups);
            _placed.CopyTo(_engine._placed, 0);
            _loop.CopyTo(_engine._loop, 0);
            _inline.CopyTo(_engine._inline, 0);
            _engine._side.Clear();

            foreach (var (key, value) in _side)
            {
                _engine._side[key] = value;
            }

            _engine._loopCentre = _loopCentre;
            return _engine.Decline(reason);
        }
    }
}
