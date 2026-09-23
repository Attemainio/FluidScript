using System.Collections.Immutable;

using FluidScript.Core.Components;

namespace FluidScript.Core.Sizing;

/// <summary>How one parameter's value is taken across every scenario (<c>D-143</c>, <c>24</c>'s envelope table).</summary>
public enum EnvelopeRule
{
    /// <summary>The largest value any scenario asked for.</summary>
    /// <remarks>
    /// The rule for every size that is a capacity: a bore, a conductance, an area, a plate count, a
    /// head, a Kv, a hold-up. A component built to the largest demand meets the smaller ones, which is
    /// what makes the envelope a merge rather than a compromise.
    /// </remarks>
    Largest = 1,

    /// <summary>Not merged: the value is an outcome of the sizes rather than one of them.</summary>
    /// <remarks>
    /// <para>
    /// A valve's achieved <c>authority</c> is <c>Δp_valve / (Δp_valve + Δp_rest)</c>, two drops at one
    /// flow — so the flow cancels to first order and authority is a property of the built geometry,
    /// not of the operating point. What the merge changes is that geometry, twice: the valve takes a
    /// Kv that is not the one a case's sizer reported against, and the rest of the branch takes pipes
    /// that are not that case's either.
    /// </para>
    /// <para>
    /// So every case's reported figure is stale the moment the envelope is taken, and a maximum, a
    /// minimum or a governing case's value would each report one the built plant does not have. Left
    /// out, and owed to a calculation over the re-solve (<c>C-121</c>).
    /// </para>
    /// </remarks>
    Solved,
}

/// <summary>Merges the sizes several scenarios chose into the sizes of one plant (<c>D-143</c>, step 2).</summary>
/// <remarks>
/// <para>
/// <strong>Parameter by parameter, never component by component.</strong> Adopting one scenario's whole
/// component is wrong the moment two cases disagree about different parts of it, and they usually do.
/// The flow trap in <c>24</c> is the standing example: a chilled side at 7/12 °C carries 1.91 kg/s for
/// 40 kW where a heating side at 45/35 °C carries 1.20 kg/s for 50 kW, so **the smaller duty has the
/// larger flow** — a pipe merged from the exchanger's governing case is 60 % short in the other.
/// </para>
/// <para>
/// This is the same shape structural engineering calls a design envelope: for every element, the case
/// giving the worst value of <em>each result component</em>, not the worst case's whole element.
/// </para>
/// <para>
/// <strong>The rule set is closed on purpose.</strong> A parameter no rule names stops the merge rather
/// than defaulting to a maximum, so a sizer added later cannot quietly acquire an envelope nobody chose
/// for it — the same reasoning as <c>D-02</c>'s closed set of omission behaviours.
/// </para>
/// </remarks>
public static class ScenarioEnvelope
{
    /// <summary>The rule for every parameter a sizer can choose.</summary>
    private static readonly ImmutableDictionary<string, EnvelopeRule> Rules =
        new Dictionary<string, EnvelopeRule>(StringComparer.Ordinal)
        {
            ["dn"] = EnvelopeRule.Largest,
            ["head"] = EnvelopeRule.Largest,
            ["kv"] = EnvelopeRule.Largest,
            ["authority"] = EnvelopeRule.Solved,
            ["ua"] = EnvelopeRule.Largest,
            ["area"] = EnvelopeRule.Largest,
            ["plates"] = EnvelopeRule.Largest,
            ["volume"] = EnvelopeRule.Largest,
            ["volume2"] = EnvelopeRule.Largest,
            ["flow"] = EnvelopeRule.Largest,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>What one scenario chose, and what it is called.</summary>
    /// <param name="Name">The scenario's name, as the <c>scenarios</c> line writes it.</param>
    /// <param name="Sizes">The sizes that scenario's own solve settled on.</param>
    public readonly record struct Candidate(string Name, SizingOverlay Sizes);

    /// <summary>The merged plant, and which case decided each of its sizes.</summary>
    /// <param name="Sizes">One size per parameter, covering every scenario.</param>
    /// <param name="Governing">Scenario name per <c>component.parameter</c> key, for each size's basis.</param>
    /// <param name="Unruled">Parameters no rule names, which stopped the merge. Empty on success.</param>
    public readonly record struct Envelope(
        SizingOverlay Sizes,
        ImmutableDictionary<string, string> Governing,
        ImmutableArray<string> Unruled);

    /// <summary>Gets the rule for a parameter, or <see langword="null"/> when none is declared.</summary>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns>Its rule, or <see langword="null"/>.</returns>
    public static EnvelopeRule? RuleFor(string parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return Rules.TryGetValue(parameter, out var rule) ? rule : null;
    }

    /// <summary>Merges each scenario's chosen sizes into one plant's.</summary>
    /// <param name="candidates">What each scenario chose, in the order the <c>scenarios</c> line declares.</param>
    /// <returns>The merged sizes with the case that governed each, or the parameters no rule names.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is <see langword="null"/>.</exception>
    public static Envelope Merge(IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var merged = SizingOverlay.Empty;
        var governing = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var unruled = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            foreach (var (component, chosen) in candidate.Sizes.Values)
            {
                foreach (var (parameter, value) in chosen)
                {
                    if (RuleFor(parameter) is not { } rule)
                    {
                        unruled.Add(parameter);
                        continue;
                    }

                    if (rule == EnvelopeRule.Solved)
                    {
                        continue;
                    }

                    // Compared in SI, so a DN designation, a head in metres and a Kv all order the way
                    // their own dimension does and nothing here has to know which is which.
                    var key = Ownership.Key(component, parameter);

                    if (merged.For(component, parameter) is { } standing && standing >= value.SiValue)
                    {
                        continue;
                    }

                    merged = merged.With(component, parameter, value);
                    governing[key] = candidate.Name;
                }
            }
        }

        return new Envelope(merged, governing.ToImmutable(), [.. unruled]);
    }

    /// <summary>Writes the sentence a merged size carries as its basis.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <param name="governing">The merge's governing map.</param>
    /// <param name="own">The basis the governing scenario's own sizer wrote, or <see langword="null"/>.</param>
    /// <returns>The basis, naming the case that decided the value.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static string Basis(
        string component, string parameter, ImmutableDictionary<string, string> governing, string? own)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(governing);

        if (!governing.TryGetValue(Ownership.Key(component, parameter), out var scenario)
            || string.IsNullOrEmpty(scenario))
        {
            return own ?? string.Empty;
        }

        return string.IsNullOrEmpty(own) ? $"sized at {scenario}" : $"sized at {scenario} — {own}";
    }
}
