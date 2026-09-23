using System.Collections.Immutable;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Contracts;

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
