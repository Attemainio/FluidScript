using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using FluidScript.Core.Diagnostics.Descriptors;

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
        .. StyleDiagnostics.All,
        .. BinderDiagnostics.All,
        .. CompatibilityDiagnostics.All,
        .. BlockDiagnostics.All,
        .. FluidDiagnostics.All,
        .. TopologyDiagnostics.All,
        .. SolverDiagnostics.All,
        .. ControllerDiagnostics.All,
        .. TransientDiagnostics.All,
        .. CatalogDiagnostics.All,
        .. DesignDiagnostics.All,
        .. SizingDiagnostics.All,
        .. LayoutDiagnostics.All,
        .. ContractDiagnostics.All,
        .. LimitDiagnostics.All,
    ];

    /// <summary>Collects the codes that have been withdrawn.</summary>
    private static IEnumerable<RetiredDiagnostic> RetiredCodes() =>
    [
        new RetiredDiagnostic(
            "FS1509",
            "Meant 'more than one circuit header', which is now legal: a script may declare several "
            + "numbered circuits. Two circuits claiming one number is a different condition and took a "
            + "new code rather than inheriting this one."),
        new RetiredDiagnostic(
            "FS1101",
            "A second 'connections' or 'schedule' section in one circuit. Retired (D-174), not reused: a circuit has no sections."),
        new RetiredDiagnostic(
            "FS1102",
            "A connection above a circuit's 'connections' line. Retired (D-174), not reused: connection lines go anywhere in the circuit block."),
        new RetiredDiagnostic(
            "FS1103",
            "A statement in the wrong section of a circuit. Retired (D-174), not reused: a statement belongs to a block, and one in the wrong block is FS1802."),
        new RetiredDiagnostic(
            "FS1106",
            "A step or ramp outside a 'schedule' section. Retired (D-169, D-174), not reused: an event belongs to a run, and one outside it is FS1802."),
        new RetiredDiagnostic(
            "FS1107",
            "A schedule in a circuit with no time to run in. Retired (D-169, D-174), not reused: events belong to a run, which has its duration."),
        new RetiredDiagnostic(
            "FS1109",
            "'in' or 'out' where an 'inlet'/'outlet' attachment line was meant. Retired (D-174), not reused: circuits join through a component both name."),
        new RetiredDiagnostic(
            "FS1110",
            "A malformed 'inlet'/'outlet' attachment line. Retired (D-174), not reused: there are no attachment lines."),
        new RetiredDiagnostic(
            "FS1111",
            "A malformed 'control' line. Retired (D-168, D-174), not reused: a loop is one controller declaration."),
        new RetiredDiagnostic(
            "FS1112",
            "A 'project' or 'spacing' line after the first circuit. Retired (D-174), not reused: both are settings of the project block."),
        new RetiredDiagnostic(
            "FS1113",
            "A 'spacing' line given a quantity. Retired (D-174), not reused: spacing is a project setting, and a unit on it is FS1514."),
        new RetiredDiagnostic(
            "FS1118",
            "A 'design' line with no values. Retired (D-174), not reused: there is no design line; the first case is the operating one."),
        new RetiredDiagnostic(
            "FS1120",
            "A 'scenarios' line with no names. Retired (D-174), not reused: the project block names its cases, 'cases = [...]'."),
        new RetiredDiagnostic(
            "FS1201",
            "A token of a one-line style that was no style. Retired (L-77, D-174), not reused: each style setting is checked against its key, which is FS1514."),
        new RetiredDiagnostic(
            "FS1204",
            "A named style used and never defined. Retired (D-174), not reused: there are no named styles and no component style (19)."),
        new RetiredDiagnostic(
            "FS1205",
            "A named style defined twice. Retired (D-174), not reused: there are no named styles (19)."),
        new RetiredDiagnostic(
            "FS1508",
            "Statements before any circuit, read into an implicit circuit. Retired (L-70, D-174), not reused: a component is declared inside a circuit block, and one outside is FS1802."),
        new RetiredDiagnostic(
            "FS1512",
            "A name bound to the registered spelling it was near, with a note. Retired (D-170), not reused: only the exact spelling binds, and the near one is offered as the fix."),
        new RetiredDiagnostic(
            "FS1536",
            "A port, parameter or property in the spelling D-120 replaced (in2, t3, HX1.t_in2), bound with a note. Retired (18, L-79), not reused: those spellings are not read."),
        new RetiredDiagnostic(
            "FS1517",
            "A circuit's own mode contradicting the project's. Retired (D-169, D-174), not reused: a run states which circuits it holds steady."),
        new RetiredDiagnostic(
            "FS1518",
            "An attachment line naming no component. Retired (D-174), not reused: there are no attachment lines."),
        new RetiredDiagnostic(
            "FS1520",
            "A circuit with an inlet attachment and no outlet, or the reverse. Retired (D-174), not reused: there are no attachment lines."),
        new RetiredDiagnostic(
            "FS1526",
            "A circuit attached to two parent circuits. Retired (D-174), not reused: there are no attachment lines."),
        new RetiredDiagnostic(
            "FS1527",
            "A curve driven by a name that was no role, curve or design value. Retired (D-167, D-174), not reused: a curve's driver is a let or time, and anything else is FS1811."),
        new RetiredDiagnostic(
            "FS1543",
            "Scenarios with no 'design' line. Retired (D-174), not reused: the first case is the operating one."),
        new RetiredDiagnostic(
            "FS1547",
            "A project 'start=' with no dynamic circuit to read it. Retired (D-169, D-174), not reused: 'start' is a run setting."),
        new RetiredDiagnostic(
            "FS2217",
            "An attachment to a component of the attaching circuit itself. Retired (D-174), not reused: there are no attachment lines."),
    ];
}
