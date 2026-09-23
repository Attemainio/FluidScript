using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Layout.Routing;
using FluidScript.Core.Model;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    // ---- instruments on their hosts (C15, D-151) ------------------------------------------------------------------

    /// <summary>One instrument drawn on its host: a sensor on the node it reads, a controller on the device it drives.</summary>
    /// <param name="Element">The hint that names it.</param>
    /// <param name="SymbolId">Its symbol.</param>
    /// <param name="Size">Its bubble's side, world units.</param>
    private sealed record Hat(NonFlowElementHint Element, string SymbolId, double Size);

    private Dictionary<int, List<Hat>>? _hats;
    private readonly Dictionary<string, (int Host, Direction Side)> _hatSide = new(StringComparer.Ordinal);

    /// <summary>The instruments on each host, in the hints' order, found once.</summary>
    private Dictionary<int, List<Hat>> Hats()
    {
        if (_hats is not null)
        {
            return _hats;
        }

        _hats = [];

        foreach (var element in _hints.NonFlowElements)
        {
            if (!_index.TryGetValue(element.PlacementAnchorId, out var host))
            {
                continue;
            }

            var kind = _model.Components.FirstOrDefault(c => c.Name == element.ComponentId)?.Kind?.Keyword ?? "controller";
            var symbol = SymbolCatalog.All.First(s => s.Id == SymbolCatalog.IdFor(kind));

            if (!_hats.TryGetValue(host, out var list))
            {
                _hats[host] = list = [];
            }

            list.Add(new Hat(element, symbol.Id, symbol.ViewBox[2]));
        }

        return _hats;
    }

    /// <summary>
    /// C15 (<c>D-151</c>): gives every instrument on a placed host its side -- the first of the host's free sides, the sides
    /// no connection leaves it by, whose bubble one margin out is clear of every box, bubble and drawn pipe; the first free
    /// side when none is. The order is a device's actuator stem first, then up, down, left, right.
    /// </summary>
    /// <param name="hosts">The components whose instruments to place, once each.</param>
    private void ChooseHats(IEnumerable<int> hosts)
    {
        foreach (var host in hosts)
        {
            if (!_placed[host] || !Hats().TryGetValue(host, out var hats))
            {
                continue;
            }

            var taken = Taken(host);

            foreach (var hat in hats)
            {
                if (_hatSide.ContainsKey(hat.Element.ComponentId))
                {
                    continue;
                }

                var free = Preference(host, _transform[host]).Where(d => !taken.Contains(d)).Distinct().ToList();

                if (free.Count == 0)
                {
                    continue;
                }

                var side = free.FirstOrDefault(d => BubbleClear(host, BoxOn(host, d, hat.Size)), free[0]);
                _hatSide[hat.Element.ComponentId] = (host, side);
                taken.Add(side);
                Note(hat.Element.ComponentId, "C15", $"on {_graph.Components[host].Name}'s {Name(side)} side, one margin out");
            }
        }
    }

    /// <summary>The sides a connection leaves a host by: a port's outward direction, or the side an inline point's run takes.</summary>
    private HashSet<Direction> Taken(int host)
    {
        var taken = new HashSet<Direction>();

        foreach (var link in _links)
        {
            foreach (var (c, p) in new[] { (link.From, link.FromPort), (link.To, link.ToPort) })
            {
                if (c != host)
                {
                    continue;
                }

                if (_inline[host] || Wildcard(host))
                {
                    if (_side.TryGetValue((host, p), out var along))
                    {
                        taken.Add(along);
                    }
                }
                else if (AnchorOffset(host, p, _transform[host]) is { } anchor)
                {
                    taken.Add(anchor.Outward);
                }
            }
        }

        return taken;
    }

    /// <summary>The order a host's sides are tried in: its actuator stem first where it has one, then up, down, left, right.</summary>
    /// <remarks>
    /// The stem is on a side no port uses -- a valve's is up in its drawn default, a three-way valve's is right, opposite its
    /// angle port -- and it is where the actuator physically is, so a controller stands on it when it is free. A pump has none,
    /// and its controller goes up: on top of the pump.
    /// </remarks>
    /// <param name="host">The host.</param>
    /// <param name="t">Its transform, placed or being tried.</param>
    private IEnumerable<Direction> Preference(int host, Transform t)
    {
        Direction? stem = _symbol[host].Id switch
        {
            "valve.standard" => Direction.Up,
            "three_way_valve.standard" => Direction.Right,
            _ => null,
        };

        if (stem is { } local)
        {
            yield return t.Apply(local);
        }

        yield return Direction.Up;
        yield return Direction.Down;
        yield return Direction.Left;
        yield return Direction.Right;
    }

    /// <summary>The bubble of a given size on a host's side: its near edge one margin off the host's box, or off an inline point.</summary>
    private Box BoxOn(int host, Direction side, double size)
    {
        var box = InnerOf(host);
        var edge = box.Centre.Towards(side, side.Horizontal ? box.Width / 2 : box.Height / 2);
        return Box.Around(edge.Towards(side, _margin + (size / 2)), size, size);
    }

    /// <summary>An instrument's bubble where its side has been chosen.</summary>
    private Box? HatBox(Hat hat) =>
        _hatSide.TryGetValue(hat.Element.ComponentId, out var at) ? BoxOn(at.Host, at.Side, hat.Size) : null;

    /// <summary>Every bubble chosen so far on the given hosts.</summary>
    private IEnumerable<Box> HatBoxes(IEnumerable<int> hosts)
    {
        foreach (var host in hosts)
        {
            if (!Hats().TryGetValue(host, out var hats))
            {
                continue;
            }

            foreach (var hat in hats)
            {
                if (HatBox(hat) is { } box)
                {
                    yield return box;
                }
            }
        }
    }

    /// <summary>Whether a bubble on a host keeps the clearance from every other placed box, every bubble already chosen and every pipe drawn so far.</summary>
    /// <remarks>
    /// The host's own pipes count too: one may leave by another side and turn across this one. An inline point's own run lies
    /// exactly on the grown bubble's edge, which <see cref="Passes"/> does not count as entering.
    /// </remarks>
    private bool BubbleClear(int host, Box bubble)
    {
        var outer = bubble.Grow(_margin);

        for (var i = 0; i < _n; i++)
        {
            if (i != host && _placed[i] && !_inline[i] && InnerOf(i).Intersects(outer))
            {
                return false;
            }
        }

        // Only what is placed now: while a fragment is drawn at its own origin, the ones before it stand elsewhere (C17).
        if (HatBoxes(Hats().Keys.Where(i => _placed[i])).Any(other => other.Intersects(outer)))
        {
            return false;
        }

        for (var k = 0; k < _links.Count; k++)
        {
            if (_routeOf[k] is not { } points || !_placed[_links[k].From] || !_placed[_links[k].To])
            {
                continue;
            }

            for (var s = 1; s < points.Length; s++)
            {
                if (Passes(outer, points[s - 1], points[s]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The shortest pipe a run from component <paramref name="j"/>'s port <paramref name="q"/> needs so that every bubble on an
    /// inline point along it keeps a margin from the boxes at both ends and from the next bubble (C15, <c>D-151</c>).
    /// </summary>
    /// <remarks>
    /// A5 cuts a run's inline points at even fractions of its length, the t-th of c at t/(c+1). A bubble of side s on the t-th
    /// point spans s/2 either way along the run, so its edge is a margin m from the nearer end when
    /// L &#183; min(t, c+1-t)/(c+1) &#8805; m + s/2, and two bubbles t1 &lt; t2 -- on one side of the run, the case to allow
    /// for, as the sides are chosen later -- are a margin apart when L &#183; (t2-t1)/(c+1) &#8805; s + m. Worked: the syntax
    /// tour's return, TV5, NR2 with TE5, NR1, PU5 at spacing 0.75 -- c = 2, TE5 at t = 1, s = 0.6 -- needs
    /// L &#8805; 1.05 &#183; 3 = 3.15, where one clearance gave 0.75 and TE5's bubble lay over both boxes.
    /// </remarks>
    /// <returns>World units; zero when nothing on the run carries an instrument.</returns>
    private double Reserve(int j, int q)
    {
        var walk = Walk(j, q);
        var count = walk.Inline.Count;
        var need = 0.0;
        int? previous = null;

        for (var t = 1; t <= count; t++)
        {
            if (!Hats().TryGetValue(walk.Inline[t - 1].Element, out var hats) || hats.Count == 0)
            {
                continue;
            }

            var size = hats.Max(static hat => hat.Size);
            need = Math.Max(need, (_margin + (size / 2)) * (count + 1) / Math.Min(t, count + 1 - t));

            if (previous is { } before)
            {
                need = Math.Max(need, (size + _margin) * (count + 1) / (t - before));
            }

            previous = t;
        }

        return need;
    }

    /// <summary>The bubbles a boxed device would carry under a transform at a centre: each on the first side in <see cref="Preference"/> that no connection takes, before any other placement is looked at.</summary>
    /// <remarks>What the forms test for clearance while they still choose a device's place; <see cref="ChooseHats"/> settles the sides once the fragment is drawn.</remarks>
    private IEnumerable<Box> Provisional(int j, Transform t, Point centre)
    {
        if (Wildcard(j) || !Hats().TryGetValue(j, out var hats))
        {
            yield break;
        }

        var taken = new HashSet<Direction>();

        foreach (var link in _links)
        {
            foreach (var (c, p) in new[] { (link.From, link.FromPort), (link.To, link.ToPort) })
            {
                if (c == j && AnchorOffset(j, p, t) is { } anchor)
                {
                    taken.Add(anchor.Outward);
                }
            }
        }

        var (w, h) = t.Size(_symbol[j]);

        foreach (var hat in hats)
        {
            if (Preference(j, t).FirstOrDefault(d => !taken.Contains(d), Direction.Up) is var side && taken.Add(side))
            {
                var edge = centre.Towards(side, side.Horizontal ? w / 2 : h / 2);
                yield return Box.Around(edge.Towards(side, _margin + (hat.Size / 2)), hat.Size, hat.Size);
            }
        }
    }
}
