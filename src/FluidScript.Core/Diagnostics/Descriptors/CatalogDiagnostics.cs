using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics;

/// <summary>Everything catalogue loading and selection can report.</summary>
/// <remarks>
/// <para>
/// The <c>FS26xx</c> range, whose subject is the shipped dimension tables
/// (<c>plan/20-core-domain/27-component-catalog.md</c>). A catalogue is not user input — it is data
/// this repository ships — so these are the one family where an error means <em>the build is wrong</em>
/// rather than the script is. They are still carried on a <c>Result</c> rather than thrown,
/// because a table validated inside a static initializer fails as a
/// <c>TypeInitializationException</c> from whatever unrelated line happens to touch the type first.
/// </para>
/// <para>
/// Three codes <c>27</c> tabulates are deliberately unregistered here. <c>FS2601</c> and <c>FS2602</c>
/// report a <em>component</em> clamped to the largest or smallest available size, and their message
/// names that component: the catalogue does not know who is asking, so selection returns a
/// <see cref="Catalogs.CatalogFit"/> and the sizing loop that knows the name builds the diagnostic —
/// <c>P3.7</c>. <c>FS2607</c> is a plate correlation outside its fitted range and needs plates,
/// <c>P4.1</c>. Registering any of the three now would put a code on the documentation page that
/// nothing produces.
/// </para>
/// </remarks>
public static class CatalogDiagnostics
{
    /// <summary>A script pinned a catalogue that does not exist.</summary>
    /// <value><c>FS2603</c>, an error.</value>
    /// <remarks>
    /// The available list is part of the message because the alternative is a user guessing at ids.
    /// A misspelled pin must never fall back to the default: a design silently sized against the wrong
    /// series is the failure this whole document exists to prevent.
    /// </remarks>
    public static DiagnosticDescriptor UnknownCatalog { get; } = new(
        "FS2603",
        DiagnosticSeverity.Error,
        "No catalogue '{name}'. Available: {list}.");

    /// <summary>A shipped catalogue failed its own structural checks.</summary>
    /// <value><c>FS2604</c>, an error.</value>
    /// <remarks>
    /// <c>27</c>'s invariant 7, which exists because transcription is the realistic failure mode of
    /// hand-curated data: an outside diameter at or below twice the wall, a non-positive roughness, a
    /// duplicated designation, or a series that stops ascending. Each is cheap to check and invisible
    /// once it reaches a velocity.
    /// </remarks>
    public static DiagnosticDescriptor InvalidCatalog { get; } = new(
        "FS2604",
        DiagnosticSeverity.Error,
        "Catalogue '{name}' is invalid: {reason}.");

    /// <summary>A catalogue carries rows nobody has verified against two public sources.</summary>
    /// <value><c>FS2605</c>, an error.</value>
    /// <remarks>
    /// Separate from <see cref="InvalidCatalog"/> on purpose. An implausible row is a table that is
    /// wrong on its face; an unverified row is a table that may be right and has not been checked, and
    /// only one of those can be fixed by reading the number again. A wrong wall thickness moves the
    /// pump head by several percent with nothing looking wrong, which is why this refuses rather than
    /// warns.
    /// </remarks>
    public static DiagnosticDescriptor UnverifiedCatalog { get; } = new(
        "FS2605",
        DiagnosticSeverity.Error,
        "Catalogue '{name}' has {count} row(s) without two verified public sources, starting at "
        + "'{first}'. An unverified dimension is a wrong design nobody can see.");

    /// <summary>No <c>catalog</c> directive, so the shipped default was used.</summary>
    /// <value><c>FS2606</c>, informational.</value>
    /// <remarks>
    /// Information rather than a warning: a new draft has no pin and should not open with a complaint.
    /// It matters at the moment the file becomes durable, which is why the message is phrased as the
    /// edit to make rather than as a fault.
    /// </remarks>
    public static DiagnosticDescriptor DefaultCatalogUsed { get; } = new(
        "FS2606",
        DiagnosticSeverity.Info,
        "Using catalogue '{name}'. Write 'catalog {name}' to pin it.");

    /// <summary>Gets every code this area registers.</summary>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        UnknownCatalog,
        InvalidCatalog,
        UnverifiedCatalog,
        DefaultCatalogUsed,
    ];
}
