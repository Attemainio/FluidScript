using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>What serializing the model contract has to say: the <c>FS25xx</c> range (<c>26</c>).</summary>
/// <remarks>
/// The contract carries diagnostics rather than producing them; these two are the exceptions, and
/// each names a degradation the consumer can see -- a field gone <c>null</c>, states gone missing.
/// </remarks>
public static class ContractDiagnostics
{
    /// <summary>A value could not be expressed in its canonical unit because it is not finite.</summary>
    /// <value><c>FS2501</c>, an error: the field serializes as <c>null</c>.</value>
    public static DiagnosticDescriptor NotFinite { get; } = new(
        "FS2501",
        DiagnosticSeverity.Error,
        "'{component}.{field}' is {value} and cannot be written in {unit}; the field is sent empty.");

    /// <summary>The payload is over the size cap; per-component states are omitted.</summary>
    /// <value><c>FS2502</c>, a warning: <c>layout</c> is sent whole and <c>statesOmitted</c> is set.</value>
    public static DiagnosticDescriptor StatesOmitted { get; } = new(
        "FS2502",
        DiagnosticSeverity.Warning,
        "The model is {size} KiB with states, over the {cap} KiB cap; states are omitted and can be fetched per component.");

    /// <summary>Gets every code this family emits, for the registry to collect.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } = [NotFinite, StatesOmitted];
}
