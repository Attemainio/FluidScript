using System.Collections.Immutable;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Contracts;

/// <summary>How long each stage took, whole milliseconds (<c>42</c>).</summary>
/// <param name="ParseMs">Lexing and parsing.</param>
/// <param name="BindMs">Binding: symbols, kinds, parameters, inference.</param>
/// <param name="SizeMs">Lowering with sizing applied, before the solve; zero when nothing was lowered.</param>
/// <param name="SolveMs">The outer loop; zero when nothing was solved.</param>
/// <param name="TotalMs">Request receipt to the response being built, layout and serialization included.</param>
/// <remarks>
/// Shipped in the response rather than only logged, so a status line can say "sizing took 400 ms"
/// without a profiler. Each figure is wall time of that stage alone; the total is more than their sum
/// by the layout, the contract and whatever the host did between them.
/// </remarks>
public sealed record TimingsWire(int ParseMs, int BindMs, int SizeMs, int SolveMs, int TotalMs);

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

/// <summary>The body of a 200 from <c>validate</c> (<c>42</c>): diagnostics and nothing else of the model.</summary>
public sealed record ValidateResponse
{
    /// <summary>The contract version the diagnostics are shaped by (<c>44</c>).</summary>
    public required string ContractVersion { get; init; }

    /// <summary>The language major the script declared, or <see langword="null"/> when it declared none.</summary>
    public required int? LanguageMajor { get; init; }

    /// <summary>Every diagnostic from the version gate, the parser, the binder and the limits, in <c>44</c>'s order.</summary>
    public required ImmutableArray<DiagnosticWire> Diagnostics { get; init; }

    /// <summary>Stage timings; sizing and solving are zero here.</summary>
    public required TimingsWire Timings { get; init; }
}

/// <summary>The body of a 200 from <c>format</c> (<c>42</c>, <c>17</c>): the edits that bring the script to the canonical layout.</summary>
/// <param name="Edits">One edit per line that changes, in document order, spans never overlapping; empty for a script already formatted.</param>
public sealed record FormatResponse(ImmutableArray<TextEditWire> Edits);

/// <summary>One text replacement (<c>17</c>).</summary>
/// <param name="Span">The span to replace, in UTF-16 code units of the script sent.</param>
/// <param name="NewText">What replaces it.</param>
public sealed record TextEditWire(SpanWire Span, string NewText);
