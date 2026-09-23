using System.Collections.Immutable;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Contracts;

/// <summary>The body of a 200 from <c>compile</c> and <c>solve</c> (<c>42</c>).</summary>
/// <remarks>
/// <see cref="Model"/> carries the diagnostics; this record never repeats them (<c>42</c>'s
/// invariant 10). The one case with no model is a script whose language major this build cannot
/// read (<c>18</c>): nothing is parsed, and the diagnostics that say so travel in
/// <see cref="Diagnostics"/> instead.
/// </remarks>
public sealed record CompileResponse
{
    /// <summary>The model contract, or <see langword="null"/> when the script's language version is not supported.</summary>
    public required ModelContract? Model { get; init; }

    /// <summary>Diagnostics, only when <see cref="Model"/> is <see langword="null"/>; absent otherwise.</summary>
    [AbsentWhenNull]
    public ImmutableArray<DiagnosticWire>? Diagnostics { get; init; }

    /// <summary>Stage timings.</summary>
    public required TimingsWire Timings { get; init; }
}
