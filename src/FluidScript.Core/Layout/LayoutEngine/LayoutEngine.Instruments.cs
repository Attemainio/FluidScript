using System.Collections.Immutable;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Layout.Routing;
using FluidScript.Core.Model;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    // ---- instruments and labels -----------------------------------------------------------------------------------

    /// <summary>
    /// C15 (<c>D-151</c>): every instrument stands on its host's chosen side (<see cref="ChooseHats"/>), joined to it by a
    /// straight line one margin long -- a sensor to the point of the node it reads, a controller to the edge of the device it
    /// drives -- and a controller that reads through a sensor is joined to that sensor by the one routed signal.
    /// </summary>
    private void PlaceInstruments()
    {
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        var placed = new List<(NonFlowElementHint Element, int Anchor, string SymbolId)>();
        var hatOf = Hats()
            .SelectMany(static pair => pair.Value.Select(hat => (Host: pair.Key, Hat: hat)))
            .ToDictionary(static entry => entry.Hat.Element.ComponentId, StringComparer.Ordinal);

        foreach (var element in _hints.NonFlowElements)
        {
            if (!hatOf.TryGetValue(element.ComponentId, out var entry) || HatBox(entry.Hat) is not { } inner)
            {
                continue;
            }

            boxes[element.ComponentId] = inner;
            placed.Add((element, entry.Host, entry.Hat.SymbolId));
        }

        var owners = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (element, _, _) in placed)
        {
            owners[element.ComponentId] = _n + _instrumentBoxes.Count;
            _instrumentBoxes.Add(boxes[element.ComponentId]);
        }

        foreach (var (element, host, symbolId) in placed)
        {
            var inner = boxes[element.ComponentId];
            var owner = owners[element.ComponentId];
            var side = _hatSide[element.ComponentId].Side;

            _instruments.Add(new Placement
            {
                ComponentId = element.ComponentId,
                SymbolId = symbolId,
                Inner = inner,
                Outer = inner.Grow(_margin),
                Rotation = 0,
                Mirrored = false,
                Arrangement = "default",
                Anchors = ImmutableSortedDictionary<string, PlacedAnchor>.Empty.Add("*", Anchor(inner.Centre.Towards(side.Opposite, inner.Width / 2), side.Opposite, side.Opposite)),
                LabelAt = inner.Centre,
                LabelBox = LabelLayout.BoxFor(TextOf(element.ComponentId), inner.Centre),
                LabelClear = true,
                Source = "computed",
            });

            // The line to the host: a sensor's to its node, a controller's to its actuator.
            Stalk(element.ComponentId + (element.ActuationTargetId is null ? ":measures" : ":actuates"), host, side, inner);

            if (element.ActuationTargetId is null || element.MeasurementTargetId == element.PlacementAnchorId)
            {
                continue;
            }

            if (SensorOf(placed, element) is { } sensor)
            {
                Signal(element.ComponentId + ":measures", boxes[sensor.Element.ComponentId], owners[sensor.Element.ComponentId], inner, owner, toCentre: false);
            }
            else
            {
                Signal(element.ComponentId + ":measures", inner, owner, element.MeasurementTargetId);
            }
        }
    }

    /// <summary>The straight line one margin long from a host -- the point of an inline node, else the middle of its box's edge -- to the near edge of its instrument's bubble.</summary>
    private void Stalk(string id, int host, Direction side, Box bubble)
    {
        var box = InnerOf(host);
        var from = box.Centre.Towards(side, side.Horizontal ? box.Width / 2 : box.Height / 2);
        var to = bubble.Centre.Towards(side.Opposite, side.Horizontal ? bubble.Width / 2 : bubble.Height / 2);
        _routes.Add((-1, new Route(id, "signal", "signal", [from, to], [])));
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

    /// <summary>The text a component's label carries: its tag where the declaration has one, else its id (<c>D-34</c>) — what the canvas draws, and so what the label's box is sized for (<c>C-84</c>).</summary>
    private string TextOf(string componentId)
    {
        foreach (var component in _model.Components)
        {
            if (string.Equals(component.Name, componentId, StringComparison.Ordinal))
            {
                return component.Tag ?? componentId;
            }
        }

        return componentId;
    }

    /// <summary>The label's first position, just outside the placed box on the side the symbol's label anchor names: text does not turn with the symbol. <see cref="LabelLayout.Place"/> moves it from here when it collides.</summary>
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
