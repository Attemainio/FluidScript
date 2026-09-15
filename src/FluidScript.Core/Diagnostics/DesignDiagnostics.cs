using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Warnings about the design rather than the script: the <c>FS40xx</c> range.</summary>
/// <remarks>
/// <para>
/// A design warning is raised against a <em>solved or sized</em> state -- a temperature the fluid
/// cannot have, a velocity a pipe should not carry, an approach an exchanger cannot reach -- so every
/// code here lands with the stage that computes the thing it warns about, not with the binder
/// (<c>D-53</c>). <c>16</c> tabulates nine; this registers the ones a stage actually raises, which
/// is the same rule every other family follows.
/// </para>
/// <para>
/// <c>FS4004</c>-<c>FS4006</c> are today sizing <em>notes</em> rather than diagnostics, which is
/// <c>C-74</c>'s subject and not repeated here.
/// </para>
/// </remarks>
public static class DesignDiagnostics
{
    /// <summary>An extended exchanger whose achieved approach is below the minimum it must respect.</summary>
    /// <value><c>FS4008</c>, an error.</value>
    /// <remarks>
    /// Approach → 0 is ε → its arrangement maximum and NTU → ∞, so the approach bound is what stops
    /// sizing from returning an infinite exchanger (<c>22</c>). A stated <c>approach</c> is a
    /// constraint like any other (<c>D-02</c>); without one the bound is <c>hx.approach_min</c>, 3 K,
    /// below which a water/water plate exchanger's area grows faster than any plate count can follow
    /// (<c>24</c>). Live for Rated and Coupled modes since <c>P4.1</c>; Duty mode has no approach.
    /// </remarks>
    public static DiagnosticDescriptor ApproachBelowMinimum { get; } = new(
        "FS4008",
        DiagnosticSeverity.Error,
        "'{name}': the approach is {approach} K, below the {minimum} K it must respect. Raise the duty's temperature difference, or accept a closer approach with approach={approach}.");

    /// <summary>Gets every code this family emits, for the registry to collect.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        ApproachBelowMinimum,
    ];
}
