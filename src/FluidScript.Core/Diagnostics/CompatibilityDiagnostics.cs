using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything the compatibility gate can report.</summary>
/// <remarks>
/// <para>
/// <c>FS17xx</c> is file compatibility and migration, owned by <c>18</c>. Three of its five codes are
/// here; the other two need something that does not exist yet, and registering a code the application
/// cannot raise would put it on the documentation page as though a user might one day see it.
/// </para>
/// <para>
/// <c>FS1703</c> (a pinned catalogue that is absent or unsupported) waits for a catalogue to be absent
/// from — P3.5. <c>FS1704</c> (the source changed after a migration preview) waits for a language
/// major 2, because a migration from 1 to nothing has no target: it is deferred with that as its
/// stated trigger rather than left unscheduled.
/// </para>
/// </remarks>
public static class CompatibilityDiagnostics
{
    /// <summary>Editor text with no <c>fluidscript</c> directive.</summary>
    /// <value><c>FS1701</c>, informational.</value>
    /// <remarks>
    /// Info, because a draft under editing is the normal state of unsaved text and the script compiles
    /// and solves exactly as it would with the line present. What it withholds is
    /// <see cref="Compatibility.CompatibilityAction.Save"/>: a durable file with no major cannot be
    /// reopened under known semantics once the language has moved on, which is the whole of
    /// <c>D-27</c>.
    /// </remarks>
    public static DiagnosticDescriptor UnversionedDraft { get; } = new(
        "FS1701",
        DiagnosticSeverity.Info,
        "This draft states no language version. Add 'fluidscript {major}' as its first line to save it.");

    /// <summary>A major this build cannot read.</summary>
    /// <value><c>FS1702</c>, an error.</value>
    /// <remarks>
    /// Covers both directions. A newer file must not be interpreted under older rules, and an older
    /// file whose semantics this build has dropped must not be interpreted under newer ones — the two
    /// are one condition, "this application does not know what this text means".
    /// </remarks>
    public static DiagnosticDescriptor UnsupportedMajor { get; } = new(
        "FS1702",
        DiagnosticSeverity.Error,
        "This file is FluidScript {major}, which this version cannot read. It understands {supported}.");

    /// <summary>Two version directives naming different majors.</summary>
    /// <value><c>FS1705</c>, an error.</value>
    /// <remarks>
    /// The parser sees two well-formed statements and reports the duplicate as <c>FS1112</c>. Only the
    /// gate can say that no semantics can be selected from the pair, and taking the first would be
    /// exactly the silent guess <c>D-27</c> exists to prevent. Two directives naming the <em>same</em>
    /// major is an ordinary duplicate and stays <c>FS1112</c>.
    /// </remarks>
    public static DiagnosticDescriptor ContradictoryMajor { get; } = new(
        "FS1705",
        DiagnosticSeverity.Error,
        "This file says it is FluidScript {first} and also {second}. Delete the line that is wrong.");

    /// <summary>Gets every code the compatibility gate emits, for the registry to collect.</summary>
    /// <value>Three descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        UnversionedDraft,
        UnsupportedMajor,
        ContradictoryMajor,
    ];
}
