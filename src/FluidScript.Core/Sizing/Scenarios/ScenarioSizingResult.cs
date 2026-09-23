using System.Collections.Immutable;

namespace FluidScript.Core.Sizing.Scenarios;

/// <summary>What sizing a plant over its scenarios produced (<c>D-143</c>).</summary>
/// <param name="Operating">Each scenario against the merged plant, in declaration order.</param>
/// <param name="Sizes">The merged sizes: one plant that covers every case.</param>
/// <param name="Governing">Scenario name per <c>component.parameter</c>, for each size's basis.</param>
/// <param name="Rounds">How many times step 2's merge ran before the envelope stopped moving.</param>
/// <param name="Converged">Whether it stopped moving, rather than hitting the round cap.</param>
/// <param name="Notes">What happened, in the order it happened.</param>
public sealed record ScenarioSizingResult(
    ImmutableArray<ScenarioSolve> Operating,
    SizingOverlay Sizes,
    ImmutableDictionary<string, string> Governing,
    int Rounds,
    bool Converged,
    ImmutableArray<string> Notes)
{
    /// <summary>Gets the scenario the file operates at — the one the canvas draws.</summary>
    /// <value>Its solve, or the only solve when the file declares no scenarios.</value>
    public ScenarioSolve Design => Operating[DesignIndex];

    /// <summary>Gets the design scenario's position in <see cref="Operating"/>.</summary>
    public int DesignIndex { get; init; }

    /// <summary>Gets what the merge itself has to report as a code rather than a sentence.</summary>
    /// <value>
    /// <c>FS2314</c> per component every declared case leaves inert, then <c>FS4013</c> per control
    /// valve whose lightest case is below its turn-down. Empty for a file with one case: a single case
    /// can be missing no interior one, and has no turn-down to check.
    /// </value>
    public ImmutableArray<Diagnostics.Diagnostic> Said { get; init; } = [];
}
