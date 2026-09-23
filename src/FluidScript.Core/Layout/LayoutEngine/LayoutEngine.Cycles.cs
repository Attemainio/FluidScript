using System.Collections.Immutable;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>A loop member placed on a rail from the cursor: a component by <see cref="PlaceFrom"/> through the port facing the cursor, a junction by <see cref="PlaceNode"/> with its other loop port turned on along the rail.</summary>
    /// <param name="cursor">Where the rail has reached.</param>
    /// <param name="m">The member.</param>
    /// <param name="facing">Its port towards the cursor.</param>
    /// <param name="onward">Its other loop port.</param>
    /// <returns>The pipe from the cursor to the facing port, or null when nothing admitted fits.</returns>
    private ImmutableArray<Point>? OnRail(PlacedAnchor cursor, Member m, int facing, int onward)
    {
        if (!Wildcard(m.Component))
        {
            return PlaceFrom(cursor, m.Component, facing);
        }

        var points = PlaceNode(cursor, m.Component, facing);
        _side[(m.Component, onward)] = cursor.Outward;
        return points;
    }

    /// <summary>Whether a loop member has a connection off the loop the fluid leaves by.</summary>
    private bool LeavesLoop(Member m)
    {
        var ports = _graph.Components[m.Component].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (p != m.InPort && p != m.OutPort && !Enters(m.Component, p, ports[p].Role) && _links.Any(l => (l.From == m.Component && l.FromPort == p) || (l.To == m.Component && l.ToPort == p)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A junction's side for a port no rule has placed: one no other port uses, the side facing away from the loop's centre first for a loop member -- vertical where the rail is level, level where it is vertical -- else the first free from right, up, left, down.</summary>
    private Direction FreeSide(int i)
    {
        var used = new HashSet<Direction>();

        for (var p = 0; p < _graph.Components[i].Ports.Length; p++)
        {
            if (_side.TryGetValue((i, p), out var s))
            {
                used.Add(s);
            }
        }

        var preferred = new List<Direction>();

        if (_loop[i])
        {
            var away = _centre[i].Offset(-_loopCentre.X, -_loopCentre.Y);
            var level = used.Contains(Direction.Left) || used.Contains(Direction.Right);
            var vertical = used.Contains(Direction.Up) || used.Contains(Direction.Down);
            var levelAway = away.X >= 0 ? Direction.Right : Direction.Left;
            var verticalAway = away.Y >= 0 ? Direction.Up : Direction.Down;

            // On a level rail the free port leaves vertically; on a vertical side, level. At a corner (C10) it leaves level, so an open end there lines up with the loop's other open ends (C7).
            preferred.AddRange(level && !vertical ? [verticalAway, levelAway] : [levelAway, verticalAway]);
        }

        preferred.AddRange([Direction.Right, Direction.Up, Direction.Left, Direction.Down]);
        return preferred.First(d => !used.Contains(d));
    }

    /// <summary>The simple flow loop through <paramref name="source"/>: from each component the first connected port the fluid leaves by, through inline nodes, until the walk returns; null where it reaches a junction, a boundary or a dead end first.</summary>
    /// <param name="source">Where the walk starts.</param>
    /// <returns>The members in flow order, the source first, or null.</returns>
    private List<Member>? Cycle(int source)
    {
        var ports = _graph.Components[source].Ports;

        for (var p = 0; p < ports.Length; p++)
        {
            if (!Enters(source, p, ports[p].Role) && Cycle(source, p) is { } members)
            {
                return members;
            }
        }

        return null;
    }

    /// <summary>The simple flow loop that leaves <paramref name="source"/> by <paramref name="leaving"/>, if that port is on one: a depth-first walk over the ports the fluid leaves by, through inline elements and junctions alike, back to the source.</summary>
    /// <param name="source">Where the walk starts.</param>
    /// <param name="leaving">The source's port to leave by.</param>
    /// <returns>The members in flow order, the source first, or null.</returns>
    private List<Member>? Cycle(int source, int leaving)
    {
        var path = new List<Member>();
        var visited = new HashSet<int> { source };
        return Extend(source, -1, leaving, source, path, visited) ? path : null;
    }

    private bool Extend(int component, int inPort, int outPort, int source, List<Member> path, HashSet<int> visited)
    {
        var (next, nextPort) = Follow(component, outPort);

        if (next < 0)
        {
            return false;
        }

        path.Add(new Member(component, inPort, outPort));

        if (next == source)
        {
            path[0] = path[0] with { InPort = nextPort };
            return true;
        }

        if (visited.Add(next))
        {
            var ports = _graph.Components[next].Ports;

            for (var p = 0; p < ports.Length; p++)
            {
                if (p != nextPort && !Enters(next, p, ports[p].Role) && _links.Any(l => (l.From == next && l.FromPort == p) || (l.To == next && l.ToPort == p)) && Extend(next, nextPort, p, source, path, visited))
                {
                    return true;
                }
            }

            visited.Remove(next);
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    /// <summary>The component and port a connection reaches from a port, passing through inline nodes.</summary>
    /// <param name="component">The component.</param>
    /// <param name="port">Its port.</param>
    /// <returns>The far end, or (-1, -1) where the port has no connection.</returns>
    private (int Component, int Port) Follow(int component, int port)
    {
        var (n, q) = (component, port);

        for (var guard = 0; guard <= _n; guard++)
        {
            var link = _links.FirstOrDefault(l => (l.From == n && l.FromPort == q) || (l.To == n && l.ToPort == q), new Link(string.Empty, -1, -1, -1, -1));

            if (link.Id.Length == 0)
            {
                return (-1, -1);
            }

            (n, q) = link.From == n && link.FromPort == q ? (link.To, link.ToPort) : (link.From, link.FromPort);

            if (!Inline(n))
            {
                return (n, q);
            }

            var other = _links.First(l => (l.From == n && l.FromPort != q) || (l.To == n && l.ToPort != q));
            q = other.From == n ? other.FromPort : other.ToPort;
        }

        return (-1, -1);
    }
}
