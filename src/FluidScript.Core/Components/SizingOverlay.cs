using System.Collections.Immutable;

using FluidScript.Core.Units;

namespace FluidScript.Core.Components;

/// <summary>The values sizing chose, keyed by component and parameter.</summary>
/// <remarks>
/// <para>
/// <strong>This is how a sized value reaches a component, and it deliberately is not the same door a
/// stated one comes through.</strong> <c>D-02</c> turns on the difference: a stated <c>position=1</c>
/// and a chosen <c>position=1</c> are the same number and mean opposite things to well-posedness, so an
/// overlay that merged into <see cref="IComponent.StatedParameters"/> would turn every sized value into
/// a constraint and the counting table would go with it. It lands in
/// <see cref="IComponent.SizedParameters"/> instead, which is the third map that already existed for it.
/// </para>
/// <para>
/// <para>
/// <strong>Its key set is fixed before the first solve and never grows.</strong> A parameter claimed by
/// no map is promotable, so a parameter that becomes sized <em>between</em> passes would stop being
/// promotable between passes, and the system's shape would change under a warm start. The outer loop
/// therefore decides what it intends to size once, from the model, and only the values move afterwards.
/// </para>
/// <para>
/// <strong>A provisional entry is absence with a number in it</strong> (<c>D-96</c>). The bootstrap
/// needs a Kv for a valve to exist at all, but that Kv decides nothing: well-posedness treats a
/// provisional parameter as free, so a stated constraint the loop cannot otherwise meet may promote it
/// to a solver unknown, and a rule that sizes it replaces it and clears the flag. Until <c>C-75</c> the
/// provisional landed in <see cref="IComponent.SizedParameters"/> unflagged, was counted as decided, and
/// the balancing-valve promotion <c>23</c> describes never fired.
/// </para>
/// It lives here rather than beside the sizing rules because the component model is what both sides
/// share: a rule produces one and lowering consumes one, and neither should have to know about the
/// other.
/// </para>
/// </remarks>
public sealed record SizingOverlay
{
    /// <summary>Gets the overlay that claims nothing, which is what the first lowering runs on.</summary>
    public static SizingOverlay Empty { get; } = new()
    {
        Values = ImmutableDictionary<string, ImmutableDictionary<string, Quantity>>.Empty,
        Provisional = [],
    };

    /// <summary>Gets the values, by component name and then by canonical parameter name.</summary>
    public required ImmutableDictionary<string, ImmutableDictionary<string, Quantity>> Values { get; init; }

    /// <summary>Gets the labels, <c>component.parameter</c>, whose value is a bootstrap provisional rather than a choice.</summary>
    /// <value>A subset of the labels in <see cref="Values"/>; empty once every rule has spoken.</value>
    public required ImmutableHashSet<string> Provisional { get; init; }

    /// <summary>Everything chosen for one component.</summary>
    /// <param name="component">The component's name.</param>
    /// <returns>Its parameters, or an empty map when nothing was chosen for it.</returns>
    public ImmutableDictionary<string, Quantity> For(string component) =>
        Values.TryGetValue(component, out var chosen) ? chosen : ImmutableDictionary<string, Quantity>.Empty;

    /// <summary>One chosen value.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns>The value in SI, or <see langword="null"/> when sizing did not choose one.</returns>
    public double? For(string component, string parameter) =>
        For(component).TryGetValue(parameter, out var value) ? value.SiValue : null;

    /// <summary>Adds or replaces one value.</summary>
    /// <summary>Whether one entry is a provisional rather than a choice.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns><see langword="true"/> when the value exists only so the component can be built.</returns>
    public bool IsProvisional(string component, string parameter) =>
        Provisional.Contains($"{component}.{parameter}");

    /// <summary>Adds or replaces one value.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <param name="value">The value.</param>
    /// <param name="provisional">Whether the value is a bootstrap placeholder rather than a rule's choice.</param>
    /// <returns>A new overlay; this one is unchanged. A choice written over a provisional clears the flag.</returns>
    public SizingOverlay With(string component, string parameter, Quantity value, bool provisional = false) =>
        this with
        {
            Values = Values.SetItem(component, For(component).SetItem(parameter, value)),
            Provisional = provisional
                ? Provisional.Add($"{component}.{parameter}")
                : Provisional.Remove($"{component}.{parameter}"),
        };
    /// <summary>Tells whether every value here matches another overlay's within a relative tolerance.</summary>
    /// <param name="other">The overlay to compare against.</param>
    /// <param name="relativeTolerance">The fraction two values may differ by and still count as settled.</param>
    /// <returns><see langword="true"/> when the two claim the same parameters at the same values.</returns>
    /// <remarks>
    /// The outer loop's convergence test. Catalogue values make it nearly exact in practice — DN25 is
    /// DN25 — which is what stabilises the loop: once the discrete choices settle, only a continuous
    /// value like a pump head can still move, and it moves only if a flow does.
    /// </remarks>
    public bool Matches(SizingOverlay? other, double relativeTolerance = 1e-6)
    {
        if (other is null || other.Values.Count != Values.Count)
        {
            return false;
        }

        foreach (var (component, chosen) in Values)
        {
            var theirs = other.For(component);

            if (theirs.Count != chosen.Count)
            {
                return false;
            }

            foreach (var (parameter, value) in chosen)
            {
                if (!theirs.TryGetValue(parameter, out var mine)
                    || Math.Abs(mine.SiValue - value.SiValue)
                        > relativeTolerance * Math.Max(1, Math.Abs(value.SiValue)))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
