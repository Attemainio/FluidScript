using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;

namespace FluidScript.Core.Language.Binding;

/// <content>Binding step 8b: propagate heights (<c>D-70</c>, <c>D-95</c>).</content>
/// <remarks>
/// <para>
/// <strong>A height is where a thing is, and everything wired straight to it is there too.</strong>
/// The air handling unit is on the roof; the valve and the pump connected to it without a pipe in
/// between are on the roof as well, whether or not the script says so on their lines. The only kinds
/// that span two heights are a pipe and a bare node-to-node connection, so heights flood from every
/// stated <c>elevation</c> through every other component and stop at those. A pipe's rise is then
/// the difference between what it connects, which is what makes the hydrostatic terms around any
/// closed loop cancel by construction — the property <c>D-70</c> chose absolute heights for.
/// </para>
/// <para>
/// <strong>Two stated heights meeting without a pipe between them is a missing riser</strong>, not a
/// choice for the tool to make: <c>FS2219</c> names both components and says so. An omitted height
/// is inherited from the neighbourhood and 0 only where nothing states one, which amends
/// <c>D-70</c>'s "an omitted height is 0" (<c>D-95</c>); the part that matters — that a height is
/// never sized — stands.
/// </para>
/// </remarks>
internal sealed partial class BindingRun
{
    /// <summary>Two heights closer than this are one height.</summary>
    /// <value>m. A millimetre: below anything a drawing states, above floating-point noise.</value>
    private const double HeightTolerance = 1e-3;

    private HeightMap _heights = HeightMap.Empty;

    private void AssignHeights()
    {
        var kinds = new Dictionary<string, ComponentSymbol>(StringComparer.Ordinal);

        foreach (var component in _components)
        {
            kinds[component.Name] = component;
        }

        // Each endpoint is a key; a single-height component's ports share its key and a pipe's do not.
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);

        string Key(EndpointSymbol endpoint) =>
            kinds.TryGetValue(endpoint.Component, out var symbol) && SpansHeights(symbol)
                ? $"{endpoint.Component}.{endpoint.Port}"
                : endpoint.Component;

        string Find(string key)
        {
            if (!parent.TryGetValue(key, out var up))
            {
                parent[key] = key;
                return key;
            }

            if (up == key)
            {
                return key;
            }

            var root = Find(up);
            parent[key] = root;
            return root;
        }

        foreach (var connection in _connections)
        {
            // A bare connection between two nodes is D-25's ideal link, and D-70 gives it a
            // hydrostatic term of its own: it spans heights like a pipe, so it does not join them.
            if (IsNode(connection.From.Component) && IsNode(connection.To.Component))
            {
                _ = Find(Key(connection.From));
                _ = Find(Key(connection.To));
                continue;
            }

            var a = Find(Key(connection.From));
            var b = Find(Key(connection.To));

            if (a != b)
            {
                parent[a] = b;
            }
        }

        // What each class states: the first height wins the class, and any different second one is
        // the diagnostic. Declaration order, so the message points at the later line.
        var stated = new Dictionary<string, (string Component, double Height)>(StringComparer.Ordinal);

        foreach (var component in _components)
        {
            if (SpansHeights(component)
                || !component.Parameters.TryGetValue("elevation", out var parameter)
                || parameter.Value is not { } quantity)
            {
                continue;
            }

            var height = quantity.SiValue;
            var root = Find(component.Name);

            if (!stated.TryGetValue(root, out var first))
            {
                stated[root] = (component.Name, height);
                continue;
            }

            if (Math.Abs(first.Height - height) > HeightTolerance)
            {
                Report(
                    TopologyDiagnostics.HeightsMeetWithoutAPipe,
                    parameter.Span,
                    ("second", component.Name),
                    ("b", Metres(height)),
                    ("first", first.Component),
                    ("a", Metres(first.Height)));
            }
        }

        var heights = ImmutableDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);

        foreach (var component in _components)
        {
            if (component.Kind is null || component.Kind.IsObserver)
            {
                continue;
            }

            if (SpansHeights(component))
            {
                foreach (var port in component.Ports)
                {
                    var key = $"{component.Name}.{port}";

                    if (stated.TryGetValue(Find(key), out var placed))
                    {
                        heights[key] = placed.Height;
                    }
                }
            }
            else if (stated.TryGetValue(Find(component.Name), out var placed))
            {
                heights[component.Name] = placed.Height;
            }
        }

        _heights = new HeightMap { Heights = heights.ToImmutable() };
    }

    /// <summary>Whether a component's ports may sit at different heights.</summary>
    /// <remarks>Only a pipe. A tank's ports take the vessel's one height until it has a height of its own (P6.2).</remarks>
    private static bool SpansHeights(ComponentSymbol component) => component.Kind?.Keyword == "pipe";

    private static string Metres(double height) =>
        height.ToString("0.##", CultureInfo.InvariantCulture);
}
