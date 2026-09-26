using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Binding;

/// <summary>Classifies the settings of one <c>style:</c> block into a spec (<c>19</c> §The project block, <c>D-171</c>).</summary>
/// <remarks>
/// Each setting arrives as the token the reader made of it, already checked against its key: a colour is a CSS
/// named colour or a quoted <c>#rrggbb</c> / <c>#rgb</c>; a width is a number, bare or in <c>px</c>; a corner is
/// <c>fillet</c> or <c>sharp</c>; a line is a run of dashes and dots. A second setting of a category already
/// stated is <c>FS1202</c>.
/// </remarks>
public static class StyleTokens
{
    private static readonly ImmutableHashSet<string> Corners = ["fillet", "round", "sharp"];

    /// <summary>Reads a directive's tokens into a spec.</summary>
    /// <param name="parts">The tokens as parsed.</param>
    /// <param name="report">Receives <c>FS1202</c>.</param>
    /// <returns>The spec, with a category unstated where no token stated it.</returns>
    public static StyleSpec Classify(ImmutableArray<StyleTokenSyntax> parts, Action<DiagnosticDescriptor, TextSpan, (string Name, string Value)[]> report)
    {
        var spec = StyleSpec.Empty;
        var stated = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in parts)
        {
            var text = part.Text;
            var span = part.Span;

            switch (part.Kind)
            {
                case StyleTokenKind.Word when Corners.Contains(text):
                    spec = Set(spec with { Corner = text }, "corner", text);
                    break;

                case StyleTokenKind.Word when NamedColours.TryGet(text, out var named):
                    spec = Set(spec with { Stroke = named }, "colour", text);
                    break;

                case StyleTokenKind.Quoted when Hex(Unquote(text)) is { } hex:
                    spec = Set(spec with { Stroke = hex }, "colour", text);
                    break;

                case StyleTokenKind.Number when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bare) && bare > 0:
                    spec = Set(spec with { StrokeWidth = bare }, "width", text);
                    break;

                case StyleTokenKind.Quantity when Pixels(text) is { } px:
                    spec = Set(spec with { StrokeWidth = px }, "width", text);
                    break;

                case StyleTokenKind.Pattern when PatternName(text) is { } pattern:
                    spec = Set(spec with { Pattern = pattern }, "pattern", text);
                    break;

                // The reader checked every value against its key and reported what it refused (`FS1514`), so
                // nothing else arrives here.
                default:
                    break;
            }

            StyleSpec Set(StyleSpec next, string category, string written)
            {
                if (stated.TryGetValue(category, out var earlier))
                {
                    report(StyleDiagnostics.OverriddenToken, span, [("a", written), ("b", earlier)]);
                }

                stated[category] = written;
                return next;
            }
        }

        return spec;
    }

    /// <summary>Normalizes a colour written as <c>#rrggbb</c> or <c>#rgb</c> to lower-case <c>#rrggbb</c>.</summary>
    /// <param name="text">The text without quotes.</param>
    /// <returns>The colour, or <see langword="null"/> when the text is not a hex colour.</returns>
    public static string? Hex(string text)
    {
        if (text.Length is not (4 or 7) || text[0] != '#' || !text.Skip(1).All(Uri.IsHexDigit))
        {
            return null;
        }

        return text.Length == 7
            ? text.ToLowerInvariant()
            : string.Create(7, text, static (span, t) =>
            {
                span[0] = '#';
                for (var i = 0; i < 3; i++)
                {
                    var c = char.ToLowerInvariant(t[i + 1]);
                    span[(2 * i) + 1] = c;
                    span[(2 * i) + 2] = c;
                }
            });
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    internal static double? Pixels(string text) =>
        text.EndsWith("px", StringComparison.Ordinal)
        && double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && value > 0
            ? value
            : null;

    private static string? PatternName(string text) => text switch
    {
        "-" => "solid",
        "--" => "dashed",
        ".." => "dotted",
        "-." => "dash-dot",
        _ => null,
    };
}
