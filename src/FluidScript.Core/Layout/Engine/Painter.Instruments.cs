using System.Collections.Immutable;

using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Hints;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout.Engine;

internal sealed partial class Painter
{
    /// <summary>
    /// C15 (<c>D-151</c>): every instrument stands on its host's chosen side, joined to it by a straight line one margin
    /// long, and a controller that reads through a sensor is joined to that sensor by the one routed signal (<c>D-152</c>).
    /// </summary>
    private void PlaceInstruments()
    {
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        var placed = new List<(NonFlowElementHint Element, int Host, string SymbolId)>();
        var hatOf = _sheet.Hats()
            .SelectMany(static pair => pair.Value.Select(hat => (Host: pair.Key, Hat: hat)))
            .ToDictionary(static entry => entry.Hat.Element.ComponentId, StringComparer.Ordinal);

        foreach (var element in _view.Hints.NonFlowElements)
        {
            if (!hatOf.TryGetValue(element.ComponentId, out var entry) || _sheet.HatBox(entry.Hat) is not { } inner)
            {
                continue;
            }

            boxes[element.ComponentId] = inner;
            placed.Add((element, entry.Host, entry.Hat.SymbolId));
        }

        var owners = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (element, _, _) in placed)
        {
            owners[element.ComponentId] = _view.Count + _instrumentBoxes.Count;
            _instrumentBoxes.Add(boxes[element.ComponentId]);
        }

        foreach (var (element, host, symbolId) in placed)
        {
            var inner = boxes[element.ComponentId];
            var side = _sheet.HatSide[element.ComponentId].Side;

            _instruments.Add(new Placement
            {
                ComponentId = element.ComponentId,
                SymbolId = symbolId,
                Inner = inner,
                Outer = inner.Grow(_margin),
                Rotation = 0,
                Mirrored = false,
                Arrangement = "default",
                Anchors = ImmutableSortedDictionary<string, PlacedAnchor>.Empty.Add("*", Sheet.Anchor(inner.Centre.Towards(side.Opposite, inner.Width / 2), side.Opposite, side.Opposite)),
                LabelAt = inner.Centre,
                LabelBox = LabelLayout.BoxFor(TextOf(element.ComponentId), inner.Centre),
                LabelClear = true,
                Source = "computed",
            });

            Stalk(element.ComponentId + (element.ActuationTargetId is null ? ":measures" : ":actuates"), host, side, inner);

            if (element.ActuationTargetId is null || element.MeasurementTargetId == element.PlacementAnchorId)
            {
                continue;
            }

            if (placed.FirstOrDefault(c => c.Element.ActuationTargetId is null && c.Element.MeasurementTargetId == element.MeasurementTargetId && c.Element.ComponentId != element.ComponentId) is { Element: not null } sensor)
            {
                Signal(element.ComponentId + ":measures", boxes[sensor.Element.ComponentId], owners[sensor.Element.ComponentId], _sheet.HatSide[sensor.Element.ComponentId].Side.Opposite, inner, owners[element.ComponentId], side.Opposite, toCentre: false);
            }
            else if (_view.Index.TryGetValue(element.MeasurementTargetId ?? string.Empty, out var target) && _sheet.Stood[target])
            {
                Signal(element.ComponentId + ":measures", inner, owners[element.ComponentId], side.Opposite, _sheet.InnerOf(target), target, null, toCentre: _view.IsInline(target));
            }
        }
    }

    /// <summary>The straight line one margin long from a host -- the point of an inline node, else the middle of its box's edge -- to the near edge of its instrument's bubble.</summary>
    private void Stalk(string id, int host, Direction side, Box bubble)
    {
        var box = _sheet.InnerOf(host);
        var from = box.Centre.Towards(side, side.Horizontal ? box.Width / 2 : box.Height / 2);
        var to = bubble.Centre.Towards(side.Opposite, side.Horizontal ? bubble.Width / 2 : bubble.Height / 2);
        _routes.Add((-1, new Route(id, "signal", "signal", [from, to], [])));
    }

    /// <summary>A signal line between two boxes, routed through the drawing (C16, <c>D-152</c>): round inner boxes only, across pipes clear of their ends, the fewest bends and then the shortest way; it leaves an instrument by any edge but its stalk's.</summary>
    private void Signal(string id, Box from, int fromOwner, Direction fromStalk, Box to, int toOwner, Direction? toStalk, bool toCentre)
    {
        var router = new OrthogonalRouter(_margin, signal: true);

        for (var i = 0; i < _view.Count; i++)
        {
            if (_sheet.Stood[i] && !_view.IsInline(i))
            {
                router.AddBox(_sheet.InnerOf(i), i);
            }
        }

        for (var k = 0; k < _instrumentBoxes.Count; k++)
        {
            router.AddBox(_instrumentBoxes[k], _view.Count + k);
        }

        foreach (var (link, route) in _routes)
        {
            var (ownerA, ownerB) = link >= 0 ? (_view.Links[link].From, _view.Links[link].To) : (-1, -1);

            for (var s = 1; s < route.Points.Length; s++)
            {
                router.AddPipe(route.Points[s - 1], route.Points[s], link, ownerA, ownerB);
            }
        }

        var starts = Edges(from, false).Where(a => a.Outward != fromStalk).ToList();
        var ends = Edges(to, toCentre).Where(a => toStalk is null || a.Outward != toStalk).ToList();

        if (router.Route(starts, fromOwner, ends, toOwner) is { } routed)
        {
            _routes.Add((-1, new Route(id, "signal", "signal", Sheet.Normalise(routed.Points), [])));
        }
    }

    /// <summary>The four anchors a signal may leave or reach a box by: the middle of each edge facing out, or the centre point facing every way for an inline node.</summary>
    private static List<PlacedAnchor> Edges(Box box, bool centre) =>
        [.. Direction.All.Select(d => Sheet.Anchor(centre ? box.Centre : box.Centre.Towards(d, d.Horizontal ? box.Width / 2 : box.Height / 2), d, d))];
}
