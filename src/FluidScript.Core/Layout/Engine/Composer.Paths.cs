using FluidScript.Core.Layout.Engine.Structures;

namespace FluidScript.Core.Layout.Engine;

internal sealed partial class Composer
{
    // ---- a path read from the decomposition ------------------------------------------------------------------------

    /// <summary>One member of a path: the element, the port the path enters it by, and the port it leaves by.</summary>
    private readonly record struct Member(int Component, int InPort, int OutPort);

    /// <summary>The members of a path a loop takes, from its entry to its exit, and the loop (C11).</summary>
    private readonly record struct Span(int Start, int End, LoopStructure Loop);

    /// <summary>A header's branch off a path: its split and the port it leaves by, its structure, and the merge and port it returns to (C14).</summary>
    private sealed record Branch(int Split, int SplitPort, Structure Body, int Merge, int MergePort);

    /// <summary>
    /// A path through boxed elements in flow order, read from a structure: its members (the first is where it starts),
    /// the loops along it as spans, the branches that hang from each header's split, and the port it arrives by at its
    /// far end.
    /// </summary>
    private sealed record Path(List<Member> Members, List<Span> Blocks, Dictionary<int, List<Branch>> Branches, int EndPort);

    /// <summary>
    /// The path from <paramref name="start"/> through structures laid end to end: <paramref name="start"/> with the port
    /// it leaves by, then every element where one structure hands on to the next or a run reaches the next boxed one.
    /// A header contributes its spine and hangs its other branches from its split; a loop contributes its forward way
    /// and is a span. The far end is not a member.
    /// </summary>
    /// <returns>The path, or null where a part could not be read (a loose structure).</returns>
    private Path? PathOf(List<Structure> parts, int start)
    {
        var members = new List<Member> { new(start, -1, -1) };
        var loops = new List<LoopStructure>();
        var branches = new Dictionary<int, List<Branch>>();
        var (from, to) = Inside(parts[0], members, loops, branches);

        for (var p = 1; p < parts.Count && from >= 0 && to >= 0; p++)
        {
            (_, to) = Joint(parts[p], to, members, loops, branches);
        }

        if (from < 0 || to < 0)
        {
            return null;
        }

        members[0] = members[0] with { OutPort = from };
        var index = new Dictionary<int, int>();

        for (var m = 0; m < members.Count; m++)
        {
            index.TryAdd(members[m].Component, m);
        }

        var blocks = new List<Span>();

        foreach (var loop in loops)
        {
            if (index.TryGetValue(loop.From, out var s) && index.TryGetValue(loop.To, out var e) && s < e)
            {
                blocks.Add(new Span(s, e, loop));
            }
        }

        return new Path(members, blocks, branches, to);
    }

    /// <summary>
    /// The members strictly inside a two-terminal structure, appended in flow order, and the ports its path leaves its
    /// start by and reaches its end at; -1 where it cannot be read.
    /// </summary>
    private (int From, int To) Inside(Structure structure, List<Member> members, List<LoopStructure> loops, Dictionary<int, List<Branch>> branches)
    {
        switch (structure)
        {
            case RunLeaf leaf:
            {
                var run = _view.Runs[leaf.Run];
                return leaf.WithFlow ? (run.Start.Port, run.End.Port) : (run.End.Port, run.Start.Port);
            }

            case SeriesStructure series:
            {
                var (from, to) = Inside(series.Parts[0], members, loops, branches);

                for (var p = 1; p < series.Parts.Length && from >= 0 && to >= 0; p++)
                {
                    (_, to) = Joint(series.Parts[p], to, members, loops, branches);
                }

                return (from, to);
            }

            case HeaderStructure header:
            {
                var spine = Inside(header.Branches[header.Spine], members, loops, branches);

                for (var b = 0; b < header.Branches.Length; b++)
                {
                    if (b != header.Spine)
                    {
                        var ends = Inside(header.Branches[b], [], [], []);

                        if (!branches.TryGetValue(header.From, out var list))
                        {
                            list = [];
                            branches[header.From] = list;
                        }

                        list.Add(new Branch(header.From, ends.From, header.Branches[b], header.To, ends.To));
                    }
                }

                return spine;
            }

            case LoopStructure loop:
                // Only the outermost loops along a path are its spans; a loop inside one belongs to its block.
                loops.Add(loop);
                return Inside(loop.Forward, members, [], branches);

            default:
                return (-1, -1);
        }
    }

    /// <summary>Appends the element where the path hands on to <paramref name="part"/>, entered by <paramref name="arriving"/>, then the part's own members.</summary>
    private (int From, int To) Joint(Structure part, int arriving, List<Member> members, List<LoopStructure> loops, Dictionary<int, List<Branch>> branches)
    {
        var at = members.Count;
        members.Add(new Member(part.From, arriving, -1));
        var (from, to) = Inside(part, members, loops, branches);
        members[at] = members[at] with { OutPort = from };
        return (from, to);
    }

    /// <summary>A ring's path from its cut round to the cut: the cut first, entered by the port the ring returns to it by.</summary>
    private Path? RingPath(Structure body, int cut)
    {
        if (PathOf([body], cut) is not { } path)
        {
            return null;
        }

        path.Members[0] = path.Members[0] with { InPort = path.EndPort };
        return path;
    }

    /// <summary>
    /// A loop as its own ring (C11): forward from its entry to its exit and back, the first member
    /// <paramref name="first"/> -- by default its consumer (<see cref="ConsumerOf"/>) -- and each loop inside it a span
    /// that does not wrap round the start.
    /// </summary>
    private Path? InnerPath(LoopStructure loop, int first = -1)
    {
        if (PathOf([loop.Forward, loop.Back], loop.From) is not { } path)
        {
            return null;
        }

        var members = path.Members;
        members[0] = members[0] with { InPort = path.EndPort };
        var at = first < 0 ? ConsumerOf(members, 0) : members.FindIndex(m => m.Component == first);

        return at < 0 ? null : Rotated(path, at);
    }

    /// <summary>A closed path started at another of its members; a span that would wrap round the new start is dropped.</summary>
    private static Path Rotated(Path path, int at)
    {
        var n = path.Members.Count;
        List<Member> members = [.. path.Members.Skip(at), .. path.Members.Take(at)];
        var blocks = path.Blocks
            .Select(b => new Span((b.Start - at + n) % n, (b.End - at + n) % n, b.Loop))
            .Where(static b => b.Start < b.End)
            .ToList();
        return new Path(members, blocks, path.Branches, path.EndPort);
    }
}
