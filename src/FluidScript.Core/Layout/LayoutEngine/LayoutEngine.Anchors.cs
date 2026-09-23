using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Language;
using FluidScript.Core.Model;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    // ---- fallback -------------------------------------------------------------------------------------------------

    /// <summary>What no rule has placed goes in a column below everything placed, in order, at the clearance, in its drawn default.</summary>
    private void Fallback(List<int> order)
    {
        var bottom = Enumerable.Range(0, _n).Where(i => _placed[i]).Select(i => InnerOf(i).Y).DefaultIfEmpty(0).Min();

        foreach (var i in order)
        {
            if (_placed[i])
            {
                continue;
            }

            var (_, h) = Transform.Identity.Size(_symbol[i]);
            Place(i, Transform.Identity, new Point(0, bottom - _margin - (h / 2)), "fallback", "placed by no rule; stacked under the drawing");
            _fallback[i] = true;
            bottom = InnerOf(i).Y;
        }
    }

    // ---- geometry -------------------------------------------------------------------------------------------------

    private bool Wildcard(int i) => _symbol[i].PortAnchors.ContainsKey("*");

    private (double Width, double Height) SizeOf(int i) => _inline[i] ? (0, 0) : _transform[i].Size(_symbol[i]);

    private Box InnerOf(int i)
    {
        var (w, h) = SizeOf(i);
        return Box.Around(_centre[i], w, h);
    }

    /// <summary>A named or indexed port's anchor relative to the centre, and its outward direction; null for a wildcard.</summary>
    private (Point Offset, Direction Outward)? AnchorOffset(int i, int port, Transform t)
    {
        var flow = _graph.Components[i];
        var symbol = _symbol[i];
        var name = flow.Ports[port].Name;

        if (t.Anchors(symbol).TryGetValue(name, out var anchor) && anchor.Direction is { } direction)
        {
            return (t.Apply(anchor.At[0], anchor.At[1]), t.Apply(Direction.Of(new Point(direction[0], direction[1])) ?? Direction.Right));
        }

        foreach (var rule in symbol.IndexedPortAnchors ?? [])
        {
            if (name.StartsWith(rule.Prefix, StringComparison.Ordinal) && flow is Tank tank)
            {
                var box = symbol.ViewBox;
                var x = rule.Side == "west" ? box[0] : box[0] + box[2];
                var level = tank.PortLevels[port];
                var y = box[1] + (level * box[3]);
                return (t.Apply(x, y), t.Apply(Direction.Of(new Point(rule.Direction[0], rule.Direction[1])) ?? Direction.Right));
            }
        }

        return null;
    }

    /// <summary>The placed anchor of a port: for a wildcard, on the side assigned to it, or the side facing right until one is.</summary>
    private PlacedAnchor AnchorOf(int i, int port)
    {
        var role = _graph.Components[i].Ports[port].Role;

        if (_inline[i])
        {
            var along = _side.GetValueOrDefault((i, port), Direction.Right);
            return Anchor(_centre[i], along, FlowOf(i, port, along, role));
        }

        if (AnchorOffset(i, port, _transform[i]) is { } named)
        {
            return Anchor(_centre[i].Offset(named.Offset.X, named.Offset.Y), named.Outward, FlowOf(i, port, named.Outward, role));
        }

        var side = _side.GetValueOrDefault((i, port), Direction.Right);
        var (w, h) = SizeOf(i);
        var at = _centre[i].Towards(side, side.Horizontal ? w / 2 : h / 2);
        return Anchor(at, side, FlowOf(i, port, side, role));
    }

    /// <summary>The flow vector at a port (<c>28</c> A3): inward where the fluid enters, outward where it leaves.</summary>
    private Direction FlowOf(int i, int port, Direction outward, PortRole role) => Enters(i, port, role) ? outward.Opposite : outward;

    /// <summary>Whether the nominal flow enters component <paramref name="i"/> at <paramref name="port"/> (<c>28</c> A3): a return boundary receives and a supply sends; else by the port's role; else by the joined port's; else the way the connection was written.</summary>
    /// <param name="i">The component.</param>
    /// <param name="port">Its port index.</param>
    /// <param name="role">The port's role.</param>
    /// <returns>True where fluid flows into the component at that port.</returns>
    private bool Enters(int i, int port, PortRole role)
    {
        var flow = _graph.Components[i];

        if (flow is CircuitNode { Boundary: BoundaryRole.Inlet })
        {
            return false;
        }

        if (flow is CircuitNode { Boundary: BoundaryRole.Outlet })
        {
            return true;
        }

        switch (role)
        {
            case PortRole.Inlet:
                return true;
            case PortRole.Outlet:
                return false;
            default:
                foreach (var link in _links)
                {
                    var (peer, peerPort, towards) = link.From == i && link.FromPort == port ? (link.To, link.ToPort, false)
                        : link.To == i && link.ToPort == port ? (link.From, link.FromPort, true)
                        : (-1, -1, false);

                    if (peer < 0)
                    {
                        continue;
                    }

                    return _graph.Components[peer] switch
                    {
                        CircuitNode { Boundary: BoundaryRole.Inlet } => true,
                        CircuitNode { Boundary: BoundaryRole.Outlet } => false,
                        _ => _graph.Components[peer].Ports[peerPort].Role switch
                        {
                            PortRole.Inlet => false,
                            PortRole.Outlet => true,
                            _ => towards,
                        },
                    };
                }

                return false;
        }
    }

    private static PlacedAnchor Anchor(Point at, Direction outward, Direction flow) => new(at, outward.AsPoint, flow);

    private void Place(int i, Transform t, Point centre, string rule, string reason)
    {
        _transform[i] = t;
        _centre[i] = centre;
        _placed[i] = true;
        Note(_graph.Components[i].Name, rule, $"{reason}; centre ({centre.X:0.##}, {centre.Y:0.##}), {Describe(t)}");
    }

    /// <summary>Records one decision for the layout report (<c>C-107</c>).</summary>
    private void Note(string subject, string rule, string reason) => _trace.Add(new PlacementNote(subject, rule, reason));

    private static string Name(Direction d) =>
        d == Direction.Right ? "rightwards" : d == Direction.Left ? "leftwards" : d == Direction.Up ? "upwards" : d == Direction.Down ? "downwards" : d.ToString();

    private static string Describe(Transform t) =>
        $"{t.Arrangement} rot {t.Rotation}{(t.Mirrored ? " mirrored" : string.Empty)}";

    private IEnumerable<int> Ordered(IEnumerable<int> components)
    {
        var position = new Dictionary<int, int>();
        for (var i = 0; i < _hints.Order.Length; i++)
        {
            if (_index.TryGetValue(_hints.Order[i], out var index))
            {
                position[index] = i;
            }
        }

        return components.OrderBy(i => position.GetValueOrDefault(i, _hints.Order.Length + i));
    }
}
