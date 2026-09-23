using System.Collections.Immutable;

namespace FluidScript.Core.Solvers;

/// <summary>One iterate: a value for every unknown, in the layout's order.</summary>
/// <param name="Values">The values, in SI unless the caller says otherwise.</param>
/// <remarks>
/// A record around an array rather than a bare array, so that a scaled vector and an SI one cannot be
/// passed for each other by accident anywhere the two meet — which is at the solver's boundary, and
/// nowhere else if <c>36</c>'s second invariant holds.
/// </remarks>
public sealed record StateVector(ImmutableArray<double> Values)
{
    /// <summary>Gets how many unknowns the vector carries.</summary>
    public int Count => Values.Length;
}
