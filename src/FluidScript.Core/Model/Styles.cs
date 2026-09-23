using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Model.Contract;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Model;

/// <summary>Resolves every component's style for the wire (<c>D-104</c>).</summary>
/// <remarks>
/// A declared component carries the style the binder gave it -- the circuit's in force where it was
/// written, merged with its own <c>style=</c>; an inferred node, which no line declares, takes the
/// style of the first declared component it touches, walking outward; a route takes the style of the
/// component it leaves. What no style states stays <see langword="null"/>, which the renderer reads as
/// the theme's default, so no colour literal lives here (<c>55</c>).
/// </remarks>
public sealed class Styles
{
    private readonly Dictionary<string, ResolvedStyleWire> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string From, string To)> _ends = new(StringComparer.Ordinal);

    /// <summary>Resolves the model's styles.</summary>
    /// <param name="model">The bound model.</param>
    /// <param name="graph">The lowered graph, for the inferred nodes' neighbours.</param>
    public Styles(SemanticModel model, CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(graph);

        var declared = model.Components
            .Where(static c => c.Style is not null)
            .ToDictionary(static c => c.Name, c => model.Style.Default.Merge(c.Style!), StringComparer.Ordinal);
        var fallback = Resolve(model.Style.Default);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < graph.Components.Length; i++)
        {
            index[graph.Components[i].Name] = i;
        }

        for (var i = 0; i < graph.Components.Length; i++)
        {
            var name = graph.Components[i].Name;
            _resolved[name] = declared.TryGetValue(name, out var own)
                ? Resolve(own)
                : Inherited(graph, i, declared, out var spec) ? Resolve(spec) : fallback;
        }

        foreach (var component in model.Components.Where(c => !_resolved.ContainsKey(c.Name)))
        {
            _resolved[component.Name] = component.Style is { } style ? Resolve(model.Style.Default.Merge(style)) : fallback;
        }

        for (var i = 0; i < model.Connections.Length; i++)
        {
            _ends[$"c{i}"] = (model.Connections[i].From.Component, model.Connections[i].To.Component);
        }
    }

    /// <summary>The wire form of a spec: unstated categories stay <see langword="null"/>, a pattern defaults to solid.</summary>
    /// <param name="spec">The spec.</param>
    /// <returns>The resolved style.</returns>
    public static ResolvedStyleWire Resolve(StyleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new ResolvedStyleWire(spec.Stroke, spec.StrokeWidth, spec.Pattern ?? "solid", spec.Fill, spec.Corner);
    }

    /// <summary>The style of a component, or <see langword="null"/> where nothing is stated and the theme's defaults apply.</summary>
    /// <param name="componentId">The component.</param>
    /// <returns>The resolved style, or <see langword="null"/>.</returns>
    public ResolvedStyleWire? Of(string componentId) =>
        _resolved.TryGetValue(componentId, out var style) && style != Unstated ? style : null;

    private static readonly ResolvedStyleWire Unstated = Resolve(StyleSpec.Empty);

    /// <summary>The component a route leaves: the connection's <c>from</c>, or the instrument of a signal line.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>A component id.</returns>
    public string FromComponentOf(string routeId) =>
        _ends.TryGetValue(routeId, out var ends) ? ends.From : routeId[..Math.Max(0, routeId.IndexOf(':'))];

    /// <summary>The component a route reaches.</summary>
    /// <param name="routeId">The route id.</param>
    /// <returns>A component id, or the route id itself for a signal line.</returns>
    public string ToComponentOf(string routeId) =>
        _ends.TryGetValue(routeId, out var ends) ? ends.To : routeId;


    /// <summary>A box as <c>[x, y, width, height]</c>, rounded.</summary>
    /// <param name="box">The box.</param>
    /// <returns>Four numbers.</returns>
    public static ImmutableArray<double> BoxOf(FluidScript.Core.Layout.Drawing.Box box) =>
        [Math.Round(box.X, 4), Math.Round(box.Y, 4), Math.Round(box.Width, 4), Math.Round(box.Height, 4)];

    private static bool Inherited(CircuitGraph graph, int start, Dictionary<string, StyleSpec> declared, out StyleSpec spec)
    {
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();

            for (var port = 0; port < graph.Adjacency.PortCount(at); port++)
            {
                var peer = graph.Adjacency.Peer(at, port);

                if (!peer.Exists || !seen.Add(peer.Component))
                {
                    continue;
                }

                if (declared.TryGetValue(graph.Components[peer.Component].Name, out spec!))
                {
                    return true;
                }

                if (graph.Components[peer.Component] is NodeComponent)
                {
                    queue.Enqueue(peer.Component);
                }
            }
        }

        spec = StyleSpec.Empty;
        return false;
    }
}
