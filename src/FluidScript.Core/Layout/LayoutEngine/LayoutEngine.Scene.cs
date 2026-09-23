using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    // ---- output ---------------------------------------------------------------------------------------------------

    private Scene ToScene()
    {
        var placements = ImmutableArray.CreateBuilder<Placement>();
        var extent = (Box?)null;

        // The groups (A8): the rings and blocks that are one component to the rest of the system -- exactly one connection enters and one leaves. A closed circuit crosses nothing and is the drawing itself; a header with several taps is not one thing either. Outer before inner; bounds are the members' inner boxes and the routes between them.
        var groups = new List<LayoutGroup>();
        var kept = new List<List<int>>();

        foreach (var (kind, members, top) in _groups)
        {
            var set = members.ToHashSet();
            var ins = 0;
            var outs = 0;
            var inside = new HashSet<int>();

            foreach (var i in members)
            {
                var ports = _graph.Components[i].Ports;

                for (var p = 0; p < ports.Length; p++)
                {
                    var walk = Walk(i, p);

                    if (walk.Far < 0)
                    {
                        continue;
                    }

                    if (set.Contains(walk.Far))
                    {
                        // A route belongs to the group when its walk runs from one member to another; a stub out to something else does not.
                        inside.UnionWith(walk.Links.Select(static l => l.Link));
                    }
                    else if (top && Returns(walk.Far, set, walk.Links.Select(static l => l.Link).ToHashSet()))
                    {
                        // The ring that holds the source: a tap whose flow comes back to the ring is a branch of the closed circuit, not an inlet or an outlet.
                    }
                    else if (Enters(i, p, ports[p].Role))
                    {
                        ins++;
                    }
                    else
                    {
                        outs++;
                    }
                }
            }

            if (ins != 1 || outs != 1)
            {
                continue;
            }

            Box? bounds = null;

            foreach (var i in members)
            {
                bounds = bounds is { } b ? b.Union(InnerOf(i)) : InnerOf(i);
            }

            foreach (var (link, route) in _routes)
            {
                if (inside.Contains(link))
                {
                    foreach (var point in route.Points)
                    {
                        var dot = new Box(point.X, point.Y, 0, 0);
                        bounds = bounds is { } b ? b.Union(dot) : dot;
                    }
                }
            }

            groups.Add(new LayoutGroup($"loop-{groups.Count + 1}", kind, "cw", [.. members.Select(i => _graph.Components[i].Name)], Rounded(bounds ?? new Box(0, 0, 0, 0))));
            kept.Add(members);
        }

        // The innermost group that placed a component: groups are listed outer before inner.
        string? GroupOf(int i)
        {
            var k = kept.FindLastIndex(m => m.Contains(i));
            return k < 0 ? null : $"loop-{k + 1}";
        }

        // Whether the flow that left a set of members and arrived here by the given links can reach the set again by some other path.
        bool Returns(int from, HashSet<int> set, HashSet<int> arrived)
        {
            var seen = new HashSet<int> { from };
            var queue = new Queue<int>();
            queue.Enqueue(from);

            while (queue.Count > 0)
            {
                var n = queue.Dequeue();

                for (var k = 0; k < _links.Count; k++)
                {
                    if (arrived.Contains(k))
                    {
                        continue;
                    }

                    var link = _links[k];
                    var other = link.From == n ? link.To : link.To == n ? link.From : -1;

                    if (other < 0)
                    {
                        continue;
                    }

                    if (set.Contains(other))
                    {
                        return true;
                    }

                    if (seen.Add(other))
                    {
                        queue.Enqueue(other);
                    }
                }
            }

            return false;
        }

        foreach (var i in Ordered(Enumerable.Range(0, _n)))
        {
            var flow = _graph.Components[i];
            var anchors = ImmutableSortedDictionary.CreateBuilder<string, PlacedAnchor>(StringComparer.Ordinal);

            for (var port = 0; port < flow.Ports.Length; port++)
            {
                anchors[flow.Ports[port].Name.Length == 0 ? $"#{port}" : flow.Ports[port].Name] = AnchorOf(i, port);
            }

            var inner = InnerOf(i);
            var placement = new Placement
            {
                ComponentId = flow.Name,
                SymbolId = _symbol[i].Id,
                Inner = inner,
                Outer = _inline[i] ? inner : inner.Grow(_margin),
                Rotation = _transform[i].Rotation,
                Mirrored = _transform[i].Mirrored,
                Arrangement = _transform[i].Arrangement,
                Anchors = anchors.ToImmutable(),
                LabelAt = LabelFor(i),
                LabelBox = LabelLayout.BoxFor(TextOf(flow.Name), LabelFor(i)),
                LabelClear = true,
                Source = "computed",
                Group = _fallback[i] ? "fallback" : GroupOf(i),
            };

            placements.Add(placement);
            extent = extent is { } e ? e.Union(placement.Outer) : placement.Outer;
        }

        foreach (var instrument in _instruments)
        {
            placements.Add(instrument);
            extent = extent is { } e ? e.Union(instrument.Outer) : instrument.Outer;
        }

        var routes = _routes
            .Select(static r => r.Route)
            .OrderBy(static r => r.Kind == "pipe" ? 0 : 1)
            .ThenBy(static r => r.Kind == "pipe" && int.TryParse(r.ConnectionId[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : int.MaxValue)
            .ThenBy(static r => r.ConnectionId, StringComparer.Ordinal)
            .ToImmutableArray();

        foreach (var route in routes)
        {
            foreach (var point in route.Points)
            {
                var dot = new Box(point.X, point.Y, 0, 0);
                extent = extent is { } e ? e.Union(dot) : dot;
            }
        }

        // Labels are laid out last, against everything else in place (53 label geometry, C-84): a label
        // moves along its owner's edge, or to another side, until its box is clear of symbols, lines and
        // the labels placed before it. The extent takes the boxes in, so a label never hangs off the picture.
        var labelled = LabelLayout.Place(placements, routes, p => TextOf(p.ComponentId));

        foreach (var placement in labelled)
        {
            if (!placement.IsInline)
            {
                extent = extent is { } e ? e.Union(placement.LabelBox) : placement.LabelBox;
            }
        }

        return new Scene
        {
            Placements = [.. labelled.Select(Rounded)],
            Routes = [.. routes.Select(static r => r with { Points = [.. r.Points.Select(Rounded)], Hops = [.. r.Hops.Select(Rounded)] })],
            Extent = Rounded(extent ?? new Box(0, 0, 0, 0)),
            Margin = _margin,
            Groups = [.. groups],
            Provenance = [.. _trace],
        };
    }

    // Every coordinate leaves rounded to a micro-unit, so a point reached two ways is the same point to a reader and to an equality test.
    private static double Rounded(double v) => Math.Round(v, 6);

    private static Point Rounded(Point p) => new(Rounded(p.X), Rounded(p.Y));

    private static Box Rounded(Box b) => new(Rounded(b.X), Rounded(b.Y), Rounded(b.Width), Rounded(b.Height));

    private static PlacedAnchor Rounded(PlacedAnchor a) => a with { At = Rounded(a.At) };

    private static Placement Rounded(Placement p) => p with
    {
        Inner = Rounded(p.Inner),
        Outer = Rounded(p.Outer),
        LabelAt = Rounded(p.LabelAt),
        LabelBox = Rounded(p.LabelBox),
        Anchors = p.Anchors.ToImmutableSortedDictionary(static a => a.Key, static a => Rounded(a.Value), StringComparer.Ordinal),
    };

    // ---- helpers ----------------------------------------------------------------------------------------------------

    /// <summary>The links the layout draws (<see cref="GraphLinks.Of"/>).</summary>
    private static List<Link> Links(CircuitGraph graph, SemanticModel model, Dictionary<string, int> index) =>
        [.. GraphLinks.Of(graph, model, index).Select(static l => new Link(l.Id, l.From, l.FromPort, l.To, l.ToPort))];
}
