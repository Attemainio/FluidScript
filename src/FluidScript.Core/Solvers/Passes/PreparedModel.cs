using System.Collections.Immutable;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Sizing;
using FluidScript.Core.Topology.Construction;

namespace FluidScript.Core.Solvers.Passes;

/// <summary>A model lowered with sizing applied, before anything is solved.</summary>
/// <param name="Lowered">The graph, and whatever could still not be built.</param>
/// <param name="Sizes">What sizing chose from the seed's flow estimates.</param>
/// <param name="Bases">Why it chose each one, keyed <c>"P1.dn"</c>.</param>
/// <param name="Notes">What happened on the way.</param>
public sealed record PreparedModel(
    LoweringResult Lowered,
    SizingOverlay Sizes,
    ImmutableDictionary<string, string> Bases,
    ImmutableArray<string> Notes)
{
    /// <summary>Gets the model the passes lower: the bound model with every deferred expression the seed could evaluate written in (<c>L-59</c>).</summary>
    /// <value><see langword="null"/> when the model deferred nothing, in which case the bound model is the one.</value>
    public SemanticModel? Model { get; init; }

    /// <summary>Gets what evaluating against the seed had to say: a dimension a deferred value could not have.</summary>
    public ImmutableArray<Diagnostics.Diagnostic> Said { get; init; } = [];

    /// <summary>Gets what the seed evaluated, so the first pass compares against it rather than counting every value as moved.</summary>
    /// <remarks>
    /// Without this a value stated from the seed was "moved" on pass 1 by having no history, which
    /// forced a pass 2 whose overlay carried the valve the head had promoted as a size -- and a stated
    /// head beside a sized valve is over-specified. The seed's value is pass 0's, and it counts.
    /// </remarks>
    public ImmutableArray<DeferredEvaluation.Evaluated> Seeded { get; init; } = [];

    /// <summary>Gets whether <see cref="Sizes"/> is given rather than chosen (<c>D-143</c>, step 3).</summary>
    /// <value>
    /// <see langword="true"/> for a model prepared by <see cref="OuterLoop.Freeze"/>. The loop then
    /// solves once against these sizes and runs no sizer, so the answer is the equilibrium of
    /// <em>this</em> plant rather than of whatever the rules would have chosen for this case.
    /// </value>
    /// <remarks>
    /// <strong>Frozen is not the same as stated, and the difference is not cosmetic.</strong> A stated
    /// parameter is a <em>constraint</em> (<c>D-02</c>): it can promote an unknown, and stating two
    /// members of one freedom group is an error. So merging an exchanger that took both <c>ua</c> and
    /// <c>area</c> from two cases and writing them back as stated would raise <c>FS2101</c> on a plant
    /// that is perfectly well posed. Freezing bypasses the sizers without touching the counting table,
    /// which is the only way to re-solve a merged plant without changing what the script said.
    /// </remarks>
    public bool Frozen { get; init; }
}
