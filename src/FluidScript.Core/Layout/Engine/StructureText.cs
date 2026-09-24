using System.Globalization;

using FluidScript.Core.Layout.Engine.Structures;

namespace FluidScript.Core.Layout.Engine;

/// <summary>A fragment's plan as the lines of the trace (<c>28</c> A10, E2): one line per structure, indented under its parent, before any placement.</summary>
internal static class StructureText
{
    /// <summary>The plan's lines.</summary>
    /// <param name="view">The circuit view.</param>
    /// <param name="plan">The fragment's plan.</param>
    /// <returns>The lines, the form first.</returns>
    public static IEnumerable<string> Lines(CircuitView view, FragmentPlan plan)
    {
        var cut = plan.Cut >= 0 ? $", cut at {view.Name(plan.Cut)}" : string.Empty;
        yield return $"{Kind(plan.Kind)}, head {view.Name(plan.Head)}{cut}";

        if (plan.Body is { } body)
        {
            foreach (var line in Lines(view, plan, body, 1))
            {
                yield return line;
            }
        }

        foreach (var pendant in plan.Pendants)
        {
            var at = pendant.Port >= 0 ? Port(view, pendant.At, pendant.Port) : view.Name(pendant.At);
            var members = pendant.Members.Length == 0 ? "no boxed member" : string.Join(", ", pendant.Members.Select(view.Name));
            var runs = pendant.Runs.Length == 1 ? "1 run" : $"{pendant.Runs.Length.ToString(CultureInfo.InvariantCulture)} runs";
            var attached = plan.Rings.Any(r => r.Runs.Any(pendant.Runs.Contains));
            yield return $"  pendant at {at}: {members}; {runs}{(pendant.Tree ? string.Empty : attached ? " (an attached ring, below)" : " (not a tree)")}";
        }

        foreach (var ring in plan.Rings)
        {
            yield return $"  attached ring ({(plan.Pendants.Any(p => p.Members.Contains(ring.At)) ? "D-160" : "D-157")}) from {Port(view, ring.At, ring.OutPort)} back to {Port(view, ring.At, ring.InPort)}";

            foreach (var line in Lines(view, plan with { Cut = ring.At }, ring.Body, 2))
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<string> Lines(CircuitView view, FragmentPlan plan, Structure structure, int depth)
    {
        var indent = new string(' ', depth * 2);

        switch (structure)
        {
            case RunLeaf leaf:
                yield return $"{indent}run {Run(view, view.Runs[leaf.Run])}{(leaf.WithFlow ? string.Empty : " (against the reading)")}";
                break;

            case SeriesStructure series:
                yield return $"{indent}series {Vertex(view, plan, series.From)} to {Vertex(view, plan, series.To)}";

                foreach (var part in series.Parts)
                {
                    foreach (var line in Lines(view, plan, part, depth + 1))
                    {
                        yield return line;
                    }
                }

                break;

            case HeaderStructure header:
                yield return $"{indent}header {Vertex(view, plan, header.From)} to {Vertex(view, plan, header.To)}, {header.Branches.Length} branches";

                for (var b = 0; b < header.Branches.Length; b++)
                {
                    yield return $"{indent}  branch {(b + 1).ToString(CultureInfo.InvariantCulture)}{(b == header.Spine ? " (spine)" : string.Empty)}";

                    foreach (var line in Lines(view, plan, header.Branches[b], depth + 2))
                    {
                        yield return line;
                    }
                }

                break;

            case LoopStructure loop:
                yield return $"{indent}loop {Vertex(view, plan, loop.From)} to {Vertex(view, plan, loop.To)}";
                yield return $"{indent}  forward";

                foreach (var line in Lines(view, plan, loop.Forward, depth + 2))
                {
                    yield return line;
                }

                yield return $"{indent}  back";

                foreach (var line in Lines(view, plan, loop.Back, depth + 2))
                {
                    yield return line;
                }

                break;

            case LooseStructure loose:
                yield return $"{indent}loose {Vertex(view, plan, loose.From)} to {Vertex(view, plan, loose.To)}: {loose.Runs.Length} runs";
                break;
        }
    }

    private static string Kind(FragmentKind kind) => kind switch
    {
        FragmentKind.Sourced => "sourced ring (C2)",
        FragmentKind.SelfLoop => "ring of one (C20)",
        FragmentKind.Open => "open form (C19)",
        FragmentKind.Unsourced => "unsourced ring (C18)",
        _ => "chain (C1)",
    };

    /// <summary>A vertex's name: a component, or a ring's cut member's outlet or inlet side.</summary>
    private static string Vertex(CircuitView view, FragmentPlan plan, int vertex) =>
        vertex == plan.Plus ? $"{view.Name(plan.Cut)}(out)" : vertex == plan.Minus ? $"{view.Name(plan.Cut)}(in)" : view.Name(vertex);

    /// <summary>A run in the trace's words: its start, its inline points, its end.</summary>
    public static string Run(CircuitView view, Run run) =>
        string.Join(" > ", [Port(view, run.Start.Component, run.Start.Port), .. run.Inline.Select(i => view.Name(i.Element)), Port(view, run.End.Component, run.End.Port)]);

    /// <summary>A port as the trace names it: <c>PU1.out</c>, or <c>N2.#1</c> for a node's unnamed port.</summary>
    public static string Port(CircuitView view, int component, int port)
    {
        var name = view.Graph.Components[component].Ports[port].Name;
        return $"{view.Name(component)}.{(name.Length == 0 ? "#" + port.ToString(CultureInfo.InvariantCulture) : name)}";
    }
}
