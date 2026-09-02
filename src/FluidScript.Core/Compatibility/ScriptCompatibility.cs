using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;

namespace FluidScript.Core.Compatibility;

/// <summary>One major version of the FluidScript language.</summary>
/// <param name="Value">The unsigned decimal the <c>fluidscript</c> directive states.</param>
/// <remarks>There is no minor. A change that needs one is a change that needs a major.</remarks>
public readonly record struct LanguageMajor(int Value);

/// <summary>A catalogue reference, optionally pinned to an exact version.</summary>
/// <param name="Id">The catalogue's ASCII id, such as <c>steel_en10255</c>.</param>
/// <param name="Version">
/// The <c>@major.minor</c> the script pinned, or <see langword="null"/> for the application's shipped
/// version — which is then recorded in provenance, so a solved result still says what it used.
/// </param>
public sealed record CatalogPin(string Id, string? Version);

/// <summary>What a file's stated version means for this application.</summary>
public enum CompatibilityDisposition
{
    /// <summary>The file states the current major.</summary>
    Current = 0,

    /// <summary>An older major this application still supports, parsed under that major's semantics.</summary>
    SupportedOld,

    /// <summary>A major newer than this application knows.</summary>
    UnsupportedNewer,

    /// <summary>An older major this application has dropped.</summary>
    UnsupportedOld,

    /// <summary>Editor text with no directive at all — recoverable, and never durably saved.</summary>
    UnversionedDraft,
}

/// <summary>Something the application may do with a file, given its disposition.</summary>
public enum CompatibilityAction
{
    /// <summary>Parse, bind and report diagnostics.</summary>
    Compile = 0,

    /// <summary>Size and solve.</summary>
    Solve,

    /// <summary>Write the edited text back over the file.</summary>
    Save,

    /// <summary>Write the bytes somewhere else, unchanged.</summary>
    SaveAsBytes,

    /// <summary>Compute and show a migration to the current major.</summary>
    PreviewMigration,
}

/// <summary>The language majors an application build understands.</summary>
/// <param name="Current">The major a new or saved file is written in.</param>
/// <param name="Supported">
/// Every major that can still be compiled and solved, including <paramref name="Current"/>.
/// </param>
public sealed record SupportedVersions(LanguageMajor Current, ImmutableArray<LanguageMajor> Supported)
{
    /// <summary>Gets what this build of FluidScript supports.</summary>
    /// <value>Major 1 only. A second entry appears the day a major 2 exists, with its migration.</value>
    public static SupportedVersions Default { get; } = new(new LanguageMajor(1), [new LanguageMajor(1)]);
}

/// <summary>What inspecting a file's version directive established.</summary>
/// <param name="DetectedMajor">
/// The major the file states, or <see langword="null"/> when it states none.
/// </param>
/// <param name="Catalog">The catalogue the file pinned, or <see langword="null"/>.</param>
/// <param name="Disposition">What that means for this application.</param>
/// <param name="Diagnostics">What to report, in source order.</param>
/// <param name="AllowedActions">
/// Everything the application may do with this file. A file is never acted on beyond this set: it is
/// the gate itself, not advice about one.
/// </param>
public sealed record CompatibilityResult(
    LanguageMajor? DetectedMajor,
    CatalogPin? Catalog,
    CompatibilityDisposition Disposition,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<CompatibilityAction> AllowedActions);

/// <summary>
/// Selects known language semantics for a file before anything parses it, implementing <c>D-27</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This reads the text, not a syntax tree</strong>, and the ordering is the whole point:
/// <c>18</c>'s invariant 2 puts version selection before parse and bind, so the gate cannot ask the
/// parser which major to parse under without asking the question it exists to answer. It scans past a
/// BOM, blank lines and comments to the first line that says anything, and matches <c>fluidscript</c>
/// followed by one unsigned decimal — a prefix fixed across majors by construction, since a major that
/// changed how its own version line is spelled could not be detected by an application that did not
/// already know its version.
/// </para>
/// <para>
/// It never mutates what it inspects (invariant 1), and it never guesses: a file naming two different
/// majors is unsupported rather than resolved by precedence.
/// </para>
/// </remarks>
public static partial class ScriptCompatibility
{
    private static readonly ImmutableArray<CompatibilityAction> Everything =
    [
        CompatibilityAction.Compile,
        CompatibilityAction.Solve,
        CompatibilityAction.Save,
        CompatibilityAction.SaveAsBytes,
    ];

    // A file this build cannot read is still a file the user owns. Copying its bytes elsewhere is the
    // one thing that stays safe, because it neither interprets nor overwrites them.
    private static readonly ImmutableArray<CompatibilityAction> BytesOnly = [CompatibilityAction.SaveAsBytes];

    /// <summary>Inspects a file's version directive and decides what may be done with it.</summary>
    /// <param name="source">The text to inspect. It is never modified.</param>
    /// <param name="supported">What this build understands, defaulting to <see cref="SupportedVersions.Default"/>.</param>
    /// <returns>The detected major, the catalogue pin, the disposition, and the allowed actions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static CompatibilityResult Inspect(SourceText source, SupportedVersions? supported = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var versions = supported ?? SupportedVersions.Default;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var directives = VersionDirectives(source);
        var catalog = Catalog(source);

        if (directives.Count == 0)
        {
            // A draft under editing, which is the normal state of unsaved text. Info, not an error:
            // the script compiles and solves exactly as it would with the line present. What it may
            // not do is become a durable file, because a file with no major cannot be reopened under
            // known semantics five years from now.
            diagnostics.Add(Diagnostic.Create(
                CompatibilityDiagnostics.UnversionedDraft,
                new TextSpan(0, 0),
                new DiagnosticArgument(
                    "major", versions.Current.Value.ToString(CultureInfo.InvariantCulture))));

            return new CompatibilityResult(
                null,
                catalog,
                CompatibilityDisposition.UnversionedDraft,
                diagnostics.ToImmutable(),
                [CompatibilityAction.Compile, CompatibilityAction.Solve, CompatibilityAction.SaveAsBytes]);
        }

        var distinct = directives.Select(static directive => directive.Major).Distinct().ToArray();

        if (distinct.Length > 1)
        {
            // Two well-formed statements naming different majors. The parser sees nothing wrong with
            // either; only the gate can say that no semantics can be selected from the pair, and
            // picking the first would be exactly the silent guess `D-27` forbids.
            diagnostics.Add(Diagnostic.Create(
                CompatibilityDiagnostics.ContradictoryMajor,
                directives[1].Span,
                new DiagnosticArgument("first", distinct[0].ToString(CultureInfo.InvariantCulture)),
                new DiagnosticArgument("second", distinct[1].ToString(CultureInfo.InvariantCulture))));

            return new CompatibilityResult(
                null, catalog, CompatibilityDisposition.UnsupportedOld, diagnostics.ToImmutable(), BytesOnly);
        }

        var major = new LanguageMajor(distinct[0]);

        if (major == versions.Current)
        {
            return new CompatibilityResult(
                major, catalog, CompatibilityDisposition.Current, diagnostics.ToImmutable(), Everything);
        }

        if (versions.Supported.Contains(major))
        {
            // Parsed under its own major's semantics and **not rewritten on open**. Migration is
            // offered, never applied: `18`'s invariant 3 makes it one explicit, undoable action.
            return new CompatibilityResult(
                major,
                catalog,
                CompatibilityDisposition.SupportedOld,
                diagnostics.ToImmutable(),
                [.. Everything, CompatibilityAction.PreviewMigration]);
        }

        var newer = major.Value > versions.Current.Value;

        diagnostics.Add(Diagnostic.Create(
            CompatibilityDiagnostics.UnsupportedMajor,
            directives[0].Span,
            new DiagnosticArgument("major", major.Value.ToString(CultureInfo.InvariantCulture)),
            new DiagnosticArgument(
                "supported",
                string.Join(
                    ", ",
                    versions.Supported.Select(static v => v.Value.ToString(CultureInfo.InvariantCulture))))));

        return new CompatibilityResult(
            major,
            catalog,
            newer ? CompatibilityDisposition.UnsupportedNewer : CompatibilityDisposition.UnsupportedOld,
            diagnostics.ToImmutable(),
            BytesOnly);
    }

    private static List<(int Major, TextSpan Span)> VersionDirectives(SourceText source)
    {
        var found = new List<(int Major, TextSpan Span)>();

        foreach (var (line, start) in Lines(source))
        {
            var match = VersionLine().Match(line);

            if (match.Success
                && int.TryParse(
                    match.Groups["major"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var major))
            {
                found.Add((major, new TextSpan(start + match.Index, match.Length)));
            }
        }

        return found;
    }

    private static CatalogPin? Catalog(SourceText source)
    {
        foreach (var (line, _) in Lines(source))
        {
            var match = CatalogLine().Match(line);

            if (match.Success)
            {
                var version = match.Groups["version"];

                return new CatalogPin(match.Groups["id"].Value, version.Success ? version.Value : null);
            }
        }

        return null;
    }

    /// <summary>Enumerates the file's lines with their offsets, past a BOM and with comments stripped.</summary>
    private static IEnumerable<(string Line, int Start)> Lines(SourceText source)
    {
        for (var i = 0; i < source.LineCount; i++)
        {
            var start = source.GetLineStart(i);
            var end = i + 1 < source.LineCount ? source.GetLineStart(i + 1) : source.Length;
            var line = source.Text[start..end].TrimEnd('\n').TrimEnd('\r');

            // A BOM is trivia the file may open with; it must not hide the directive behind it.
            if (i == 0)
            {
                line = line.TrimStart('\uFEFF');
            }

            var comment = line.IndexOf('#', StringComparison.Ordinal);

            yield return (comment >= 0 ? line[..comment] : line, start);
        }
    }

    [GeneratedRegex(@"^\s*fluidscript\s+(?<major>\d+)\s*$")]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^\s*catalog\s+(?<id>[A-Za-z_][A-Za-z0-9_]*)(?:@(?<version>\d+\.\d+))?\s*$")]
    private static partial Regex CatalogLine();
}
