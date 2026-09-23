using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Layout.Drawing;
using FluidScript.Core.Layout.Routing;

namespace FluidScript.Core.Layout;

internal sealed partial class LayoutEngine
{
    // ---- loops ----------------------------------------------------------------------------------------------------

    /// <summary>One member of a flow loop: the component, the port the loop enters it by, and the port it leaves by.</summary>
    private readonly record struct Member(int Component, int InPort, int OutPort);

    /// <summary>
    /// The consumer side of a ring, laid out at a provisional place: one member, or a whole inner loop as a
    /// block (A8, C11). The ring slides it into place and shifts its runs with it.
    /// </summary>
    /// <param name="Members">The boxed components the unit moves as one.</param>
    /// <param name="In">Where the ring's top rail enters, facing left (a corner) or up (from the rail above).</param>
    /// <param name="Out">Where the ring's right side leaves, facing down or right.</param>
    /// <param name="OutFrom">The member and port the leaving run walks from.</param>
    /// <param name="Bottom">The lowest inner-box edge, provisional units.</param>
    /// <param name="Runs">The unit's own runs, provisional units, assigned by the ring after the shift.</param>
    private sealed record Unit(List<int> Members, PlacedAnchor In, PlacedAnchor Out, Member OutFrom, double Bottom, List<(Member From, List<Point> Points)> Runs);

    /// <summary>One thing on a rail: a member placed by <see cref="OnRail"/>, or a block laid out as a unit (C11).</summary>
    private readonly record struct Item(Member? Member, Unit? Unit);

    /// <summary>A branch block hanging between the rails (C14): placed under its top junction, its return still to be laid to its bottom junction, which stands under the top one.</summary>
    private sealed record Hanger(PlacedAnchor Out, Member OutFrom, int Top, int Bottom, int BottomPort);

    private bool Loop(int source)
    {
        var attempt = new Attempt(this);
        if (!IsSource(source))
        {
            return attempt.Decline("the head is not a source (a positive duty)");
        }

        if (Cycle(source) is not { } cycle)
        {
            return attempt.Decline("no cycle of boxed members returns to the source");
        }

        var s = cycle[0];
        var ts = Admitted(s.Component).Where(t => t.Arrangement == "default" && Outward(s.Component, s.OutPort, t) == Direction.Up && Outward(s.Component, s.InPort, t) == Direction.Down).ToList();

        if (ts.Count == 0)
        {
            return attempt.Decline("the source has no default arrangement with its outlet up and its inlet down");
        }

        _loopCentre = new Point(0, 0);

        foreach (var member in cycle)
        {
            _loop[member.Component] = true;
        }

        var ring = cycle.Select(static m => m.Component).ToHashSet();
        HashSet<int> avoid = [source];
        var mark = _groups.Count;

        // C11: every inner loop along the ring is a block. The last in flow order is the ring's right side with its outlet facing back; the others stand on the top rail with their outlets facing on, so a chain steps from block to block. Without any, the consumer stands on the right alone.
        var (unit, _, unitEnd, items, _, _) = RailItems(cycle, avoid, mark);

        if (unit is null)
        {
            return attempt.Decline("no consumer unit was found on the cycle");
        }

        Unplace(items, unit);

        Place(s.Component, ts[0], new Point(0, 0), "C2", "the loop's source at the origin, outlet up and inlet down");
        var sOut = AnchorOf(s.Component, s.OutPort);
        var sIn = AnchorOf(s.Component, s.InPort);
        var yTop = sOut.Along(_margin).Y;
        var bottomMembers = cycle.GetRange(unitEnd + 1, cycle.Count - unitEnd - 1);
        var runs = new List<(Member From, List<Point> Points)>();
        var hangers = new List<Hanger>();
        List<Point> topStart = [sOut.At, new Point(sOut.At.X, yTop)];

        if (Top(Anchor(topStart[^1], Direction.Right, Direction.Right), topStart, s, items, ring, bottomMembers, avoid, runs, hangers) is not { } top)
        {
            return attempt.Decline("the top rail could not be laid");
        }

        var (_, rightJunction) = Corners(bottomMembers, unit, leftFirst: false);
        var (drop, yBottom) = Bottom(unit, top.End.At.Y, sIn.Along(_margin).Y, rightJunction?.Component ?? -1);
        yBottom = Math.Min(yBottom, Under(hangers));

        // C12: a member on a side with slack sits at the side's middle. The source moves down by half the excess of the rails' span over its own.
        var slack = sIn.Along(_margin).Y - yBottom;
        Place(s.Component, ts[0], new Point(0, -slack / 2), "C12", $"the source at the middle of its side, half the rails' slack ({slack:0.##}) down");
        sOut = AnchorOf(s.Component, s.OutPort);
        sIn = AnchorOf(s.Component, s.InPort);
        topStart[0] = sOut.At;

        if (!Close(top, unit, drop, yBottom, bottomMembers, [sIn.At, sIn.Along(_margin), new Point(sIn.At.X, yBottom)], null, rightJunction, hangers, runs))
        {
            return attempt.Decline("the ring could not be closed along the bottom rail");
        }

        // The ring is a group (A8), listed before the blocks it holds.
        _groups.Insert(mark, LoopGroup(cycle.Select(static m => m.Component).ToList(), true));

        AssignRuns(runs);

        return true;
    }

    /// <summary>The ring path's unit and top-rail items (C11): the last inner loop in flow order is the unit on the ring's right side, every earlier one a block on the top rail, and with none the consumer stands on the right alone.</summary>
    /// <param name="cycle">The ring, the head first.</param>
    /// <param name="avoid">What the unit search must not find.</param>
    /// <param name="mark">Where the form's groups start, to count the unit's own.</param>
    /// <returns>The unit (<see langword="null"/> when a block could not be laid), where it starts and ends on the cycle, the items before it, its range when it is a block, and how many groups the unit's block created.</returns>
    private (Unit? Unit, int Start, int End, List<Item> Items, (int Start, int End, List<Member> Inner)? Range, int UnitGroups) RailItems(
        List<Member> cycle, HashSet<int> avoid, int mark)
    {
        var ranges = Ranges(cycle, 1, avoid);
        var items = new List<Item>();

        if (ranges.Count > 0)
        {
            var last = ranges[^1];
            var unit = Block(last.Inner, cycle[last.Start], cycle[last.End], avoid, Direction.Left);
            var unitGroups = _groups.Count - mark;
            var next = 1;

            foreach (var (start, end, inner) in ranges.SkipLast(1))
            {
                items.AddRange(cycle.GetRange(next, start - next).Select(static m => new Item(m, null)));
                var block = Block(inner, cycle[start], cycle[end], avoid, Direction.Right);

                if (block is null)
                {
                    unit = null;
                    break;
                }

                items.Add(new Item(null, block));
                next = end + 1;
            }

            if (unit is not null)
            {
                items.AddRange(cycle.GetRange(next, last.Start - next).Select(static m => new Item(m, null)));
            }

            return (unit, last.Start, last.End, items, last, unitGroups);
        }

        var consumerAt = ConsumerOf(cycle, 1);
        var single = consumerAt < 0 ? null : Single(cycle[consumerAt]);

        if (single is not null)
        {
            items.AddRange(cycle.GetRange(1, consumerAt - 1).Select(static m => new Item(m, null)));
        }

        return (single, consumerAt, consumerAt, items, null, 0);
    }

    /// <summary>Every unit stands at a provisional place until it is slid in; none is an obstacle before that.</summary>
    /// <param name="items">The top-rail items, whose blocks are units.</param>
    /// <param name="unit">The ring's unit, or <see langword="null"/>.</param>
    private void Unplace(List<Item> items, Unit? unit)
    {
        foreach (var u in items.Where(static i => i.Unit is not null).Select(static i => i.Unit!).Concat(unit is null ? [] : [unit]))
        {
            foreach (var i in u.Members)
            {
                _placed[i] = false;
            }
        }
    }

    /// <summary>Assigns every run a form laid to the walk it belongs to.</summary>
    /// <param name="runs">The runs, each from the member and port it leaves by.</param>
    private void AssignRuns(List<(Member From, List<Point> Points)> runs)
    {
        foreach (var (from, points) in runs)
        {
            Assign(Walk(from.Component, from.OutPort), Normalise(points));
        }
    }

    /// <summary>The member that takes a ring's right side: the standing consumer of the largest duty from <paramref name="from"/> on, else the first member the flow leaves the ring by.</summary>
    private int ConsumerOf(List<Member> cycle, int from)
    {
        var consumerAt = -1;

        for (var k = from; k < cycle.Count; k++)
        {
            if (Duty(cycle[k].Component) is { } duty && (consumerAt < 0 || duty < Duty(cycle[consumerAt].Component)))
            {
                consumerAt = k;
            }
        }

        for (var k = from; k < cycle.Count && consumerAt < 0; k++)
        {
            // No standing consumer: the member the loop's flow leaves by -- a valve or a junction with an off-loop outlet -- takes the right side.
            if (LeavesLoop(cycle[k]))
            {
                consumerAt = k;
            }
        }

        return consumerAt;
    }

    /// <summary>A consumer's duty, negative (W): its stated power, or a nominal amount for an exchanger whose power is sized but which is written as a load or states an inlet above its outlet; <see langword="null"/> for anything else.</summary>
    private double? Duty(int component) => _graph.Components[component] switch
    {
        HeatExchanger { Power: < 0 } h => h.Power,
        HeatExchanger { Power: 0 } when Written(component) is "load" or "radiator" or "cooler" or "chiller" => -double.Epsilon,
        HeatExchanger { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && inlet.SiValue > outlet.SiValue => -double.Epsilon,
        _ => null,
    };

    /// <summary>Whether an exchanger is a heat source: its stated power positive, or its power sized but written as a heater or boiler, or stating an outlet above its inlet.</summary>
    private bool IsSource(int component) => _graph.Components[component] switch
    {
        HeatExchanger { Power: > 0 } => true,
        HeatExchanger { Power: 0 } when Written(component) is "heater" or "boiler" => true,
        HeatExchanger { Power: 0 } h when h.StatedParameters.TryGetValue("in", out var inlet) && h.StatedParameters.TryGetValue("out", out var outlet) && outlet.SiValue > inlet.SiValue => true,
        _ => false,
    };

    /// <summary>The kind the script wrote for a component (<c>load</c>, <c>heater</c>, …), lower-cased; <see langword="null"/> for one the language inferred.</summary>
    private string? Written(int component)
    {
        var name = _graph.Components[component].Name;

        foreach (var c in _model.Components)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal))
            {
                return c.WrittenKind.ToLowerInvariant();
            }
        }

        return null;
    }
}
