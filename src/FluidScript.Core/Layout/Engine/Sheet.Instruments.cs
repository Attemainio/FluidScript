using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Layout.Routing;
using FluidScript.Core.Model;

namespace FluidScript.Core.Layout.Engine;

internal sealed partial class Sheet
{
    // ---- instruments on their hosts (C15, D-151) ------------------------------------------------------------------

    /// <summary>One instrument drawn on its host.</summary>
    /// <param name="Element">The hint that names it.</param>
    /// <param name="SymbolId">Its symbol.</param>
    /// <param name="Size">Its bubble's side, world units.</param>
    public sealed record Hat(NonFlowElementHint Element, string SymbolId, double Size);

    private Dictionary<int, List<Hat>>? _hats;

    /// <summary>Gets each instrument's host and chosen side, by the instrument's id.</summary>
    public Dictionary<string, (int Host, Direction Side)> HatSide { get; } = new(StringComparer.Ordinal);

    /// <summary>The instruments on each host, in the hints' order, found once.</summary>
    public Dictionary<int, List<Hat>> Hats()
    {
        if (_hats is not null)
        {
            return _hats;
        }

        _hats = [];

        foreach (var element in View.Hints.NonFlowElements)
        {
            if (!View.Index.TryGetValue(element.PlacementAnchorId, out var host))
            {
                continue;
            }

            var kind = View.Model.Components.FirstOrDefault(c => c.Name == element.ComponentId)?.Kind?.Keyword ?? "controller";
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
    /// C15 (<c>D-151</c>): gives every instrument on a placed host its side -- the first of the host's free sides, the
    /// sides no connection leaves it by, whose bubble one margin out is clear of every box, bubble and laid run; the
    /// first free side when none is. The order is a device's actuator stem first, then up, down, left, right.
    /// </summary>
    public void ChooseHats(IEnumerable<int> hosts)
    {
        foreach (var host in hosts)
        {
            if (!Placed[host] || !Hats().TryGetValue(host, out var hats))
            {
                continue;
            }

            var taken = Taken(host);

            foreach (var hat in hats)
            {
                if (HatSide.ContainsKey(hat.Element.ComponentId))
                {
                    continue;
                }

                var free = Preference(host, Transform[host]).Where(d => !taken.Contains(d)).Distinct().ToList();

                if (free.Count == 0)
                {
                    continue;
                }

                var side = free.FirstOrDefault(d => BubbleClear(host, BoxOn(host, d, hat.Size)), free[0]);
                HatSide[hat.Element.ComponentId] = (host, side);
                taken.Add(side);
                Note(hat.Element.ComponentId, "C15", $"on {View.Name(host)}'s {Name(side)} side, one margin out");
            }
        }
    }

    /// <summary>The sides a connection leaves a host by: a port's outward direction, or the side a node's or an inline point's port takes.</summary>
    private HashSet<Direction> Taken(int host)
    {
        var taken = new HashSet<Direction>();

        foreach (var p in View.Connected(host))
        {
            if (View.IsInline(host) || View.Wildcard(host))
            {
                if (Side.TryGetValue((host, p), out var along))
                {
                    taken.Add(along);
                }
            }
            else if (AnchorOffset(host, p, Transform[host]) is { } anchor)
            {
                taken.Add(anchor.Outward);
            }
        }

        return taken;
    }

    /// <summary>The order a host's sides are tried in: its actuator stem first where it has one, then up, down, left, right.</summary>
    private IEnumerable<Direction> Preference(int host, Transform t)
    {
        Direction? stem = View.Symbols[host].Id switch
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
    public Box BoxOn(int host, Direction side, double size)
    {
        var box = InnerOf(host);
        var edge = box.Centre.Towards(side, side.Horizontal ? box.Width / 2 : box.Height / 2);
        return Box.Around(edge.Towards(side, Margin + (size / 2)), size, size);
    }

    /// <summary>An instrument's bubble where its side has been chosen.</summary>
    public Box? HatBox(Hat hat) =>
        HatSide.TryGetValue(hat.Element.ComponentId, out var at) ? BoxOn(at.Host, at.Side, hat.Size) : null;

    /// <summary>Every bubble chosen so far on the given hosts.</summary>
    public IEnumerable<Box> HatBoxes(IEnumerable<int> hosts)
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

    /// <summary>Whether a bubble on a host keeps the clearance from every other placed box, every bubble chosen so far and every run laid between placed elements.</summary>
    private bool BubbleClear(int host, Box bubble)
    {
        var outer = bubble.Grow(Margin);

        for (var i = 0; i < View.Count; i++)
        {
            if (i != host && Placed[i] && !View.IsInline(i) && InnerOf(i).Intersects(outer))
            {
                return false;
            }
        }

        if (HatBoxes(Hats().Keys.Where(i => Placed[i])).Any(other => other.Intersects(outer)))
        {
            return false;
        }

        foreach (var run in View.Runs)
        {
            if (_runs[run.Index] is not { } points || !Placed[run.Start.Component] || !Placed[run.End.Component])
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
    /// The shortest a run may be for the bubbles on its inline points (<c>D-151</c>): cut evenly (A5), the t-th of c
    /// points with a bubble of side s keeps a margin m from the boxes at both ends when
    /// L &#183; min(t, c+1-t)/(c+1) &#8805; m + s/2, and two bubbles t1 &lt; t2 keep a margin apart when
    /// L &#183; (t2-t1)/(c+1) &#8805; s + m. At least one margin in every case.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>World units.</returns>
    public double RunLength(Run run)
    {
        var count = run.Inline.Length;
        var need = Margin;
        int? previous = null;

        for (var t = 1; t <= count; t++)
        {
            if (!Hats().TryGetValue(run.Inline[t - 1].Element, out var hats) || hats.Count == 0)
            {
                continue;
            }

            var size = hats.Max(static hat => hat.Size);
            need = Math.Max(need, (Margin + (size / 2)) * (count + 1) / Math.Min(t, count + 1 - t));

            if (previous is { } before)
            {
                need = Math.Max(need, (size + Margin) * (count + 1) / (t - before));
            }

            previous = t;
        }

        return need;
    }

    /// <summary>The bubbles a boxed device would carry under a transform at a centre: each on the first side in the preference order that no connection takes.</summary>
    /// <remarks>What placement tests for clearance while a device's place is still being chosen; <see cref="ChooseHats"/> settles the sides once its structure is drawn.</remarks>
    private IEnumerable<Box> Bubbles(int c, Transform t, Point centre)
    {
        if (View.IsInline(c) || !Hats().TryGetValue(c, out var hats))
        {
            yield break;
        }

        // A device's connections leave by its symbol's anchors; a node's by the sides its ports have taken so far.
        var taken = new HashSet<Direction>();

        foreach (var p in View.Connected(c))
        {
            if (AnchorOffset(c, p, t) is { } anchor)
            {
                taken.Add(anchor.Outward);
            }
            else if (Side.TryGetValue((c, p), out var side))
            {
                taken.Add(side);
            }
        }

        var (w, h) = t.Size(View.Symbols[c]);

        foreach (var hat in hats)
        {
            if (Preference(c, t).FirstOrDefault(d => !taken.Contains(d), Direction.Up) is var side && taken.Add(side))
            {
                var edge = centre.Towards(side, side.Horizontal ? w / 2 : h / 2);
                yield return Box.Around(edge.Towards(side, Margin + (hat.Size / 2)), hat.Size, hat.Size);
            }
        }
    }
}
