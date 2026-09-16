using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Binding;

/// <summary>What a <c>style</c> directive stated, category by category; a field is <see langword="null"/> where it said nothing.</summary>
/// <param name="Stroke">The stroke colour as <c>#rrggbb</c>.</param>
/// <param name="StrokeWidth">The stroke width in CSS pixels at scale 1.</param>
/// <param name="Corner"><c>fillet</c>, <c>round</c> or <c>sharp</c>.</param>
/// <param name="Pattern"><c>solid</c>, <c>dashed</c>, <c>dotted</c> or <c>dash-dot</c>.</param>
/// <param name="Fill">The static fill colour as <c>#rrggbb</c>; the colour scale paints over it while <c>show</c> is active (<c>D-104</c>).</param>
public sealed record StyleSpec(string? Stroke, double? StrokeWidth, string? Corner, string? Pattern, string? Fill)
{
    /// <summary>The spec that states nothing.</summary>
    public static StyleSpec Empty { get; } = new(null, null, null, null, null);

    /// <summary>Gets whether every category is unstated.</summary>
    public bool IsEmpty => Stroke is null && StrokeWidth is null && Corner is null && Pattern is null && Fill is null;

    /// <summary>Layers <paramref name="over"/> on this: a category the later one states replaces the earlier.</summary>
    /// <param name="over">The later spec.</param>
    /// <returns>The merged spec.</returns>
    public StyleSpec Merge(StyleSpec over) => new(
        over.Stroke ?? Stroke,
        over.StrokeWidth ?? StrokeWidth,
        over.Corner ?? Corner,
        over.Pattern ?? Pattern,
        over.Fill ?? Fill);
}

/// <summary>Classifies the tokens of one <c>style</c> directive (<c>12</c> <em>Style directive</em>).</summary>
/// <remarks>
/// Order-independent by decision: each token is read for what it is. A colour is a CSS named colour or
/// a quoted <c>#rrggbb</c> / <c>#rgb</c>; a width is a number, bare or in <c>px</c>; a corner is
/// <c>fillet</c>, <c>round</c> or <c>sharp</c>; a pattern is a run of dashes and dots; <c>fill=</c> is
/// the one keyed token. Anything else is <c>FS1201</c>, and a second token of a category already
/// stated is <c>FS1202</c>.
/// </remarks>
public static class StyleTokens
{
    private static readonly ImmutableHashSet<string> Corners = ["fillet", "round", "sharp"];

    /// <summary>Reads a directive's tokens into a spec.</summary>
    /// <param name="parts">The tokens as parsed.</param>
    /// <param name="report">Receives <c>FS1201</c> and <c>FS1202</c>.</param>
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

                case StyleTokenKind.Keyed when part.Parts.Length == 3 && string.Equals(part.Parts[0].Text, "fill", StringComparison.Ordinal):
                {
                    var value = part.Parts[2];
                    var colour = value.Kind == TokenKind.StringLiteral ? Hex(Unquote(value.Text)) : NamedColours.TryGet(value.Text, out var n) ? n : null;

                    if (colour is null)
                    {
                        report(StyleDiagnostics.UnclassifiableToken, span, [("token", text)]);
                        break;
                    }

                    spec = Set(spec with { Fill = colour }, "fill", text);
                    break;
                }

                default:
                    report(StyleDiagnostics.UnclassifiableToken, span, [("token", text)]);
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

    private static double? Pixels(string text) =>
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
