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
    // ---- instruments and labels -----------------------------------------------------------------------------------

    private void PlaceInstruments()
    {
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        var placed = new List<(NonFlowElementHint Element, int Anchor, string SymbolId)>();

        foreach (var element in _hints.NonFlowElements)
        {
            if (!_index.TryGetValue(element.PlacementAnchorId, out var anchor) || !_placed[anchor])
            {
                continue;
            }

            var kind = _model.Components.FirstOrDefault(c => c.Name == element.ComponentId)?.Kind?.Keyword ?? "controller";
            var symbol = SymbolCatalog.All.First(s => s.Id == SymbolCatalog.IdFor(kind));
            var size = symbol.ViewBox;
            var host = InnerOf(anchor);
            var candidates = new[]
            {
                new Point(host.Centre.X, host.Top + _margin + (size[3] / 2)),
                new Point(host.Centre.X, host.Y - _margin - (size[3] / 2)),
                new Point(host.X - _margin - (size[2] / 2), host.Centre.Y),
                new Point(host.Right + _margin + (size[2] / 2), host.Centre.Y),
            };

            var inner = candidates
                .Select(at => Box.Around(at, size[2], size[3]))
                .FirstOrDefault(box => !Collides(box, boxes.Values), Box.Around(candidates[0], size[2], size[3]));

            while (Collides(inner, boxes.Values))
            {
                inner = inner.Offset(size[2] + _margin, 0);
            }

            boxes[element.ComponentId] = inner;
            placed.Add((element, anchor, symbol.Id));
        }

        // C15: a controller reads its node through the sensor standing on it. The signal leaves the sensor level, by the side facing the controller, and turns once into it; when that level stub would be shorter than a margin, the sensor and its inline node slide along the rail to make room.
        foreach (var (element, _, _) in placed)
        {
            if (element.ActuationTargetId is null || SensorOf(placed, element) is not { } sensor)
            {
                continue;
            }

            var s = boxes[sensor.Element.ComponentId];
            var c = boxes[element.ComponentId];
            var toward = c.Centre.X >= s.Centre.X ? 1 : -1;
            var stub = toward > 0 ? c.Centre.X - s.Right : s.X - c.Centre.X;
            var shift = -toward * (_margin - stub);

            if (stub < _margin - Eps && _inline[sensor.Anchor] && Nudge(sensor.Anchor, shift))
            {
                boxes[sensor.Element.ComponentId] = s.Offset(shift, 0);
            }
        }

        var owners = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (element, _, _) in placed)
        {
            owners[element.ComponentId] = _n + _instrumentBoxes.Count;
            _instrumentBoxes.Add(boxes[element.ComponentId]);
        }

        foreach (var (element, _, symbolId) in placed)
        {
            var inner = boxes[element.ComponentId];
            var owner = owners[element.ComponentId];

            _instruments.Add(new Placement
            {
                ComponentId = element.ComponentId,
                SymbolId = symbolId,
                Inner = inner,
                Outer = inner.Grow(_margin),
                Rotation = 0,
                Mirrored = false,
                Arrangement = "default",
                Anchors = ImmutableSortedDictionary<string, PlacedAnchor>.Empty.Add("*", Anchor(inner.Centre, Direction.Down, Direction.Down)),
                LabelAt = inner.Centre,
                Source = "computed",
            });

            if (element.ActuationTargetId is { } actuated)
            {
                if (SensorOf(placed, element) is { } sensor)
                {
                    Signal(element.ComponentId + ":measures", boxes[sensor.Element.ComponentId], owners[sensor.Element.ComponentId], inner, owner, toCentre: false);
                }
                else
                {
                    Signal(element.ComponentId + ":measures", inner, owner, element.MeasurementTargetId);
                }

                if (actuated != element.MeasurementTargetId)
                {
                    Signal(element.ComponentId + ":actuates", inner, owner, actuated);
                }
            }
            else
            {
                Signal(element.ComponentId + ":measures", inner, owner, element.MeasurementTargetId);
            }
        }
    }

    /// <summary>The sensor standing on the node a controller reads, when the script placed one (C15).</summary>
    private static (NonFlowElementHint Element, int Anchor, string SymbolId)? SensorOf(List<(NonFlowElementHint Element, int Anchor, string SymbolId)> placed, NonFlowElementHint controller)
    {
        foreach (var candidate in placed)
        {
            if (candidate.Element.ActuationTargetId is null && candidate.Element.MeasurementTargetId == controller.MeasurementTargetId && candidate.Element.ComponentId != controller.ComponentId)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Moves an inline node along its level run by <paramref name="dx"/> (C15) when every route ending on it stays level and keeps a margin to its next point; the routes follow.</summary>
    private bool Nudge(int node, double dx)
    {
        var from = _centre[node];
        var to = from.Offset(dx, 0);
        var ends = new List<(int Index, bool AtStart)>();

        for (var k = 0; k < _routes.Count; k++)
        {
            var points = _routes[k].Route.Points;

            if (points.Length < 2)
            {
                continue;
            }

            foreach (var atStart in new[] { true, false })
            {
                var end = atStart ? points[0] : points[^1];
                var next = atStart ? points[1] : points[^2];

                if (end.ManhattanTo(from) > Eps)
                {
                    continue;
                }

                if (Math.Abs(next.Y - from.Y) > Eps || Math.Sign(next.X - to.X) != Math.Sign(next.X - from.X) || Math.Abs(next.X - to.X) < _margin - Eps)
                {
                    return false;
                }

                ends.Add((k, atStart));
            }
        }

        if (ends.Count == 0)
        {
            return false;
        }

        foreach (var (k, atStart) in ends)
        {
            var points = _routes[k].Route.Points;
            var moved = points.SetItem(atStart ? 0 : points.Length - 1, to);
            _routes[k] = (_routes[k].Link, _routes[k].Route with { Points = moved });

            if (_routes[k].Link >= 0)
            {
                _routeOf[_routes[k].Link] = moved;
            }
        }

        _centre[node] = to;
        Note(_graph.Components[node].Name, "C15", $"an inline node nudged along its level run by {dx:0.##} to ({to.X:0.##}, {to.Y:0.##})");
        return true;
    }

    /// <summary>Whether an instrument at a candidate box would sit within a margin of a placed symbol, another instrument, or a pipe.</summary>
    private bool Collides(Box candidate, IEnumerable<Box> instruments)
    {
        var outer = candidate.Grow(_margin);

        for (var i = 0; i < _n; i++)
        {
            if (_placed[i] && InnerOf(i).Intersects(outer))
            {
                return true;
            }
        }

        foreach (var (_, route) in _routes)
        {
            for (var k = 1; k < route.Points.Length; k++)
            {
                if (Passes(outer, route.Points[k - 1], route.Points[k]))
                {
                    return true;
                }
            }
        }

        return instruments.Any(other => other.Intersects(outer));
    }

    /// <summary>A signal line from an instrument's box to a component: to the point of an inline one, to the facing edge of a boxed one.</summary>
    private void Signal(string id, Box from, int fromOwner, string targetId)
    {
        if (!_index.TryGetValue(targetId, out var target) || !_placed[target])
        {
            return;
        }

        Signal(id, from, fromOwner, InnerOf(target), target, toCentre: _inline[target]);
    }

    /// <summary>A signal line between two boxes: one bend, level first, into the facing edge (C15) -- unless that path crosses a box, when the router takes it round every placed box instead, pipes free to cross (C-94).</summary>
    /// <param name="id">The route's id.</param>
    /// <param name="from">The instrument's box the line leaves.</param>
    /// <param name="fromOwner">That box's owner: an instrument's is <c>_n</c> plus its index.</param>
    /// <param name="to">The box the line reaches.</param>
    /// <param name="toOwner">That box's owner, a component index or an instrument's.</param>
    /// <param name="toCentre">Whether the line ends at the box's centre, as it does on an inline node's point.</param>
    private void Signal(string id, Box from, int fromOwner, Box to, int toOwner, bool toCentre)
    {
        var plumb = Math.Abs(from.Centre.X - to.Centre.X) < Eps;
        var level = Math.Abs(from.Centre.Y - to.Centre.Y) < Eps;
        var right = from.Centre.X < to.Centre.X;
        var down = from.Centre.Y > to.Centre.Y;
        var points = new List<Point>();

        if (plumb)
        {
            points.Add(new Point(from.Centre.X, down ? from.Y : from.Top));
            points.Add(toCentre ? to.Centre : new Point(to.Centre.X, down ? to.Top : to.Y));
        }
        else if (level)
        {
            points.Add(new Point(right ? from.Right : from.X, from.Centre.Y));
            points.Add(toCentre ? to.Centre : new Point(right ? to.X : to.Right, to.Centre.Y));
        }
        else
        {
            points.Add(new Point(right ? from.Right : from.X, from.Centre.Y));
            points.Add(new Point(to.Centre.X, from.Centre.Y));
            points.Add(toCentre ? to.Centre : new Point(to.Centre.X, down ? to.Top : to.Y));
        }

        if (SignalCrosses(points, fromOwner, toOwner))
        {
            var router = new OrthogonalRouter(_margin);

            for (var i = 0; i < _n; i++)
            {
                if (_placed[i] && !_inline[i])
                {
                    router.AddBox(InnerOf(i), i);
                }
            }

            for (var k = 0; k < _instrumentBoxes.Count; k++)
            {
                router.AddBox(_instrumentBoxes[k], _n + k);
            }

            // Every line drawn so far keeps the signal a margin off it along its length; crossing is free (C16).
            foreach (var (link, route) in _routes)
            {
                var (ownerA, ownerB) = link >= 0 ? (_links[link].From, _links[link].To) : (-1, -1);

                for (var s = 1; s < route.Points.Length; s++)
                {
                    router.AddPipe(route.Points[s - 1], route.Points[s], link, ownerA, ownerB);
                }
            }

            if (router.Route(Edges(from, false), fromOwner, Edges(to, toCentre), toOwner) is { } routed)
            {
                points = [.. routed.Points];
            }
        }

        _routes.Add((-1, new Route(id, "signal", "signal", Normalise(points), [])));
    }

    /// <summary>Whether a signal path passes through the inside of any placed box other than the two it joins, or runs along a line already drawn (C-94).</summary>
    private bool SignalCrosses(List<Point> points, int fromOwner, int toOwner)
    {
        for (var s = 1; s < points.Count; s++)
        {
            var (a, b) = (points[s - 1], points[s]);

            foreach (var (_, route) in _routes)
            {
                for (var t = 1; t < route.Points.Length; t++)
                {
                    if (Segments.Shared(a, b, route.Points[t - 1], route.Points[t], Eps) is not null)
                    {
                        return true;
                    }
                }
            }

            for (var i = 0; i < _n; i++)
            {
                if (i != fromOwner && i != toOwner && _placed[i] && !_inline[i] && Passes(InnerOf(i), points[s - 1], points[s]))
                {
                    return true;
                }
            }

            for (var k = 0; k < _instrumentBoxes.Count; k++)
            {
                if (_n + k != fromOwner && _n + k != toOwner && Passes(_instrumentBoxes[k], points[s - 1], points[s]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The four anchors a signal may leave or reach a box by: the middle of each edge facing out, or the centre point facing every way for an inline node.</summary>
    private static List<PlacedAnchor> Edges(Box box, bool centre) =>
        [.. Direction.All.Select(d => Anchor(centre ? box.Centre : box.Centre.Towards(d, d.Horizontal ? box.Width / 2 : box.Height / 2), d, d))];

    /// <summary>The label just outside the placed box, on the side the symbol's label anchor names: text does not turn with the symbol.</summary>
    private Point LabelFor(int component)
    {
        var box = InnerOf(component);
        var anchor = _symbol[component].LabelAnchor;
        const double gap = 0.15;

        if (Math.Abs(anchor[1]) >= Math.Abs(anchor[0]))
        {
            return new Point(box.Centre.X, anchor[1] > 0 ? box.Top + gap : box.Y - gap);
        }

        return new Point(anchor[0] < 0 ? box.X - gap : box.Right + gap, box.Centre.Y);
    }
}
