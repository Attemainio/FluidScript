using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Compatibility;

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
            // The fix is certain (16 invariant 5): the current major as a first line, after a BOM when there is one.
            var current = versions.Current.Value.ToString(CultureInfo.InvariantCulture);
            var at = source.Length > 0 && source[0] == '﻿' ? 1 : 0;
            diagnostics.Add(Diagnostic.Create(
                CompatibilityDiagnostics.UnversionedDraft,
                new TextSpan(0, 0),
                new DiagnosticArgument("major", current)) with
            {
                Suggestion = new Suggestion($"Add 'fluidscript {current}'", new TextSpan(at, 0), $"fluidscript {current}\n"),
            });

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

        if (versions.Supported.Contains(major) && major.Value > versions.Current.Value)
        {
            // Language 2 while it is built beside language 1 (`D-164`): its own parser, every action, and
            // nothing to migrate to, since the current major is the older one.
            return new CompatibilityResult(
                major, catalog, CompatibilityDisposition.SupportedNewer, diagnostics.ToImmutable(), Everything);
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

    // Language 1's `catalog id@version` line, and language 2's `catalog = id@version` setting inside the
    // project block (`18`); neither form means anything else in either language.
    [GeneratedRegex(@"^\s*catalog(?:\s+|\s*=\s*)(?<id>[A-Za-z_][A-Za-z0-9_]*)(?:@(?<version>\d+\.\d+))?\s*$")]
    private static partial Regex CatalogLine();
}
