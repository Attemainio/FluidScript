using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics.Descriptors;

/// <summary>What exceeding an input limit has to say: the <c>FS46xx</c> range (<c>42</c>, <c>07</c>).</summary>
/// <remarks>
/// A limit is a stated ceiling on what one request may ask for -- declarations, tokens, solver
/// unknowns -- and reaching one is not a fault in the script, so the message names the number and the
/// ceiling and nothing else. The source-size limit is not here: a body over it never reaches the
/// pipeline, and the host answers it at the transport (<c>42</c>, 413).
/// </remarks>
public static class LimitDiagnostics
{
    /// <summary>The script is over one of the input limits and is not solved.</summary>
    /// <value>
    /// <c>FS4601</c>, an error. <c>{what}</c> is <c>declarations</c>, <c>tokens</c> or <c>unknowns</c>;
    /// <c>{count}</c> what the script has and <c>{max}</c> the limit.
    /// </value>
    public static DiagnosticDescriptor OverLimit { get; } = new(
        "FS4601",
        DiagnosticSeverity.Error,
        "The script has {count} {what}; the limit is {max}, so it is not solved.");

    /// <summary>Gets every code this family emits, for the registry to collect.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } = [OverLimit];
}
