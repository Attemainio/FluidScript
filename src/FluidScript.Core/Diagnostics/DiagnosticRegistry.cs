using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FluidScript.Core.Diagnostics;

/// <summary>
/// Every diagnostic code FluidScript defines, live and retired.
/// </summary>
/// <remarks>
/// <para>
/// Two things depend on this existing rather than being implied by string literals scattered through
/// the emit sites. The generated <c>/docs/functions/diagnostics.md</c> is rendered from it, so an
/// agent that hits <c>FS1302</c> can look up what to do; and a test asserts that every code emitted
/// anywhere in Core appears here and that every entry here is emitted by some path, which is how a
/// code invented in an ad-hoc throw gets caught.
/// </para>
/// <para>
/// <strong>The registry grows one work package at a time, not all at once.</strong> Registering the
/// whole plan's code space up front would make the second half of that both-directions test
/// unsatisfiable until the last stage exists, and a test that cannot pass yet is a test that gets
/// disabled. Each package adds its own stage's descriptors as it lands.
/// </para>
/// </remarks>
public static class DiagnosticRegistry
{
    private static readonly FrozenDictionary<string, DiagnosticDescriptor> ByCode;

    static DiagnosticRegistry()
    {
        All = [.. Areas().OrderBy(static descriptor => descriptor.Code, StringComparer.Ordinal)];
        Retired = [.. RetiredCodes().OrderBy(static retired => retired.Code, StringComparer.Ordinal)];

        var duplicate = All.GroupBy(static descriptor => descriptor.Code, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Diagnostic code '{duplicate.Key}' is defined more than once. A code means exactly one thing.");
        }

        ByCode = All.ToFrozenDictionary(static descriptor => descriptor.Code, StringComparer.Ordinal);

        var reused = Retired.FirstOrDefault(retired => ByCode.ContainsKey(retired.Code));
        if (reused is not null)
        {
            throw new InvalidOperationException(
                $"Diagnostic code '{reused.Code}' is retired and has been allocated again. A retired code "
                + "stays unallocated: every existing reference to it would silently change meaning.");
        }
    }

    /// <summary>Gets every live code, ordered by code.</summary>
    /// <value>
    /// Ordered so that the generated documentation and any failure message list codes the same way on
    /// every platform. Empty until the first stage that emits a diagnostic lands.
    /// </value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; }

    /// <summary>Gets every code that was allocated and is no longer emitted, ordered by code.</summary>
    /// <value>
    /// Kept rather than dropped so a lookup can tell <em>never allocated</em> from <em>allocated and
    /// retired</em>. Those two want opposite responses: the first is a typo, the second is a reference
    /// to a rule that changed.
    /// </value>
    public static ImmutableArray<RetiredDiagnostic> Retired { get; }

    /// <summary>Looks up a code's definition.</summary>
    /// <param name="code">The code to look up, for example <c>FS1302</c>.</param>
    /// <param name="descriptor">The definition when the code is live; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the code is live. A retired code returns <see langword="false"/>
    /// here and is found through <see cref="IsRetired"/>, so a caller can tell the two apart.
    /// </returns>
    public static bool TryGet(string code, [NotNullWhen(true)] out DiagnosticDescriptor? descriptor) =>
        ByCode.TryGetValue(code, out descriptor);

    /// <summary>Looks up a code's definition, requiring it to exist.</summary>
    /// <param name="code">The code to look up, for example <c>FS1302</c>.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">
    /// The code is not live. Codes are compile-time constants of the stage that emits them, so an
    /// unknown one is a mistake in Core rather than anything a script can cause.
    /// </exception>
    public static DiagnosticDescriptor Get(string code) =>
        TryGet(code, out var descriptor)
            ? descriptor
            : throw new ArgumentException(
                IsRetired(code)
                    ? $"Diagnostic code '{code}' is retired and is never emitted."
                    : $"Diagnostic code '{code}' is not defined.",
                nameof(code));

    /// <summary>Determines whether a code was allocated and later withdrawn.</summary>
    /// <param name="code">The code to test, for example <c>FS1509</c>.</param>
    /// <returns><see langword="true"/> when the code appears in <see cref="Retired"/>.</returns>
    public static bool IsRetired(string code) =>
        Retired.Any(retired => string.Equals(retired.Code, code, StringComparison.Ordinal));

    /// <summary>Collects the descriptors each work package registers.</summary>
    /// <remarks>
    /// One entry per work package, added by that package: the lexer's codes arrived with the lexer,
    /// and the parser's arrive with the parser. A descriptor lands with whatever emits it, which is
    /// not always the area its code belongs to (<c>D-53</c>).
    /// </remarks>
    private static IEnumerable<DiagnosticDescriptor> Areas() =>
    [
        .. LexerDiagnostics.All,
        .. ParserDiagnostics.All,
        .. BinderDiagnostics.All,
        .. CompatibilityDiagnostics.All,
        .. FluidDiagnostics.All,
        .. TopologyDiagnostics.All,
    ];

    /// <summary>Collects the codes that have been withdrawn.</summary>
    private static IEnumerable<RetiredDiagnostic> RetiredCodes() =>
    [
        new RetiredDiagnostic(
            "FS1509",
            "Meant 'more than one circuit header', which is now legal: a script may declare several "
            + "numbered circuits. Two circuits claiming one number is a different condition and took a "
            + "new code rather than inheriting this one."),
    ];
}
