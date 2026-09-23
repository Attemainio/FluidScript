using System.Collections.Immutable;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

/// <summary>What the well-posedness pass found.</summary>
/// <param name="Counting">The counting argument, term by term.</param>
/// <param name="Hydraulics">The hydraulic connected components, each with its datum.</param>
/// <param name="Diagnostics">Everything worth telling the user, in a stable order.</param>
public sealed record WellPosednessResult(
    CountingTable Counting,
    ImmutableArray<HydraulicComponent> Hydraulics,
    ImmutableArray<Diagnostic> Diagnostics)
{
    /// <summary>Gets whether the circuit can be handed to the solver.</summary>
    /// <value>
    /// <see langword="true"/> when the system is square and nothing was reported as an error. A warning
    /// does not block a solve: a loop with no driver still has an answer, and the answer is zero flow.
    /// </value>
    public bool CanSolve =>
        Counting.Excess == 0
        && !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
