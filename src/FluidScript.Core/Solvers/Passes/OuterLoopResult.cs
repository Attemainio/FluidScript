using System.Collections.Immutable;
using FluidScript.Core.Sizing;
using FluidScript.Core.Solvers.Seeding;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Passes;

/// <summary>What the outer loop settled on.</summary>
public sealed record OuterLoopResult
{
    /// <summary>Gets the graph as it stood on the last pass, sizes and all.</summary>
    public required CircuitGraph Graph { get; init; }

    /// <summary>Gets the last solve.</summary>
    public required SolveResult Solve { get; init; }

    /// <summary>Gets what sizing chose, by component and parameter.</summary>
    public required SizingOverlay Sizes { get; init; }

    /// <summary>Gets the basis of every sized value, as <c>"PU1.head"</c> to a sentence.</summary>
    /// <remarks>
    /// <c>24</c>'s invariant 2: an unexplained sized value is a defect. This is where the explanation
    /// lives until the model contract carries it.
    /// </remarks>
    public required ImmutableDictionary<string, string> Bases { get; init; }

    /// <summary>Gets what happened on the way, in order.</summary>
    public required ImmutableArray<string> Notes { get; init; }

    /// <summary>Gets how many passes ran.</summary>
    public required int Passes { get; init; }

    /// <summary>Gets the Newton iterations over every pass, retries included.</summary>
    /// <value>
    /// The work the run did, which is where a warm start's saving shows: the warm seed goes to the
    /// <em>first</em> pass, and <see cref="Solve"/>'s own count is the last pass's (<c>A-4</c>).
    /// </value>
    public required int Iterations { get; init; }

    /// <summary>Gets the Newton iterations of each pass in order, retries included.</summary>
    /// <value>
    /// One entry per solve the loop ran, so that a change to the seed can be read per pass rather than
    /// as one sum (<c>S-66</c>): a seed that helps the first pass and hurts the third shows here and
    /// nowhere else. Sums to <see cref="Iterations"/>.
    /// </value>
    public required ImmutableArray<int> PassIterations { get; init; }

    /// <summary>Gets whether the sizes stopped moving.</summary>
    /// <value>
    /// <see langword="false"/> means the cap was reached with sizes still changing — <c>FS2301</c>'s
    /// case, reported rather than hidden, with the last values kept because they are what the last
    /// solve actually used.
    /// </value>
    public required bool Settled { get; init; }

    /// <summary>The hash of the unknown layout the solution is indexed by; what a <see cref="WarmStart"/> must match.</summary>
    /// <value>See <see cref="OuterLoop.TopologyHash"/>.</value>
    public required string TopologyHash { get; init; }

    /// <summary>Gets each control valve's operating point, read off a solve whose sizes were given (<c>C-121</c>).</summary>
    /// <value>
    /// One per valve that carries flow, for a result of <see cref="OuterLoop.Freeze"/>; empty for an
    /// ordinary run, whose sizer already reports the same figure as <c>authority</c>.
    /// </value>
    public ImmutableArray<ValveReading> Valves { get; init; } = [];

    /// <summary>The full solve report: counting, constraints, unknowns, equations, sizes and rank.</summary>
    /// <returns>The report, as lines of text.</returns>
    /// <remarks>
    /// On the record so that anywhere holding a result can print one without assembling the call ---
    /// including a debugger watch window, which is where an unexpected termination is usually first met.
    /// A run that never produced a result explains itself through
    /// <see cref="FluidScript.Core.Diagnostics.Explanations.SolveExplanation.Render(CircuitGraph, string)"/> instead.
    /// </remarks>
    public override string ToString() => FluidScript.Core.Diagnostics.Explanations.SolveExplanation.Render(this);
}
