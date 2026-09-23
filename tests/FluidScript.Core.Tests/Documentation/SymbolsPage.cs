using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using FluidScript.Core.Model;
using FluidScript.Core.Model.Contract;

namespace FluidScript.Core.Tests.Documentation;

/// <summary>Renders the symbol catalogue table of <c>docs/functions/model-contract.md</c> from <see cref="SymbolCatalog.All"/>.</summary>
public static class SymbolsPage
{
    public const string Region = "symbol-catalog";

    public static string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Symbol | Box `[x, y, w, h]` | Port anchors, facing | Strokes | Label at |");
        builder.AppendLine("|---|---|---|---|---|");

        foreach (var symbol in SymbolCatalog.All)
        {
            var anchors = Set(symbol.PortAnchors)
                .Concat((symbol.IndexedPortAnchors ?? []).Select(static rule => $"`{rule.Prefix}{{{rule.MinIndex}..{rule.MaxIndex}}}` on the {rule.Side} at `{rule.VerticalCoordinate}` {Arrow(rule.Direction)}"))
                .ToList();
            var cell = string.Join(", ", anchors);

            foreach (var (name, set) in (symbol.Alternatives ?? new Dictionary<string, IReadOnlyDictionary<string, AnchorWire>>()).OrderBy(static a => a.Key, StringComparer.Ordinal))
            {
                cell += $"<br>*or `{name}`:* {string.Join(", ", Set(set))}";
            }

            builder.AppendLine(
                $"| `{symbol.Id}` | `{Numbers(symbol.ViewBox)}` | {cell} | {Strokes(symbol)} | {Point(symbol.LabelAnchor)} |");
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static IEnumerable<string> Set(IReadOnlyDictionary<string, AnchorWire> anchors) =>
        anchors
            .OrderBy(static a => a.Key, StringComparer.Ordinal)
            .Select(static a => $"`{a.Key}` {Point(a.Value.At)}{(a.Value.Direction is { } d ? " " + Arrow(d) : string.Empty)}");

    // Symbol space is y up (28 A1): (0, 1) points up the page.
    private static string Arrow(ImmutableArray<double> direction) =>
        (direction[0], direction[1]) switch
        {
            ( < 0, _) => "←",
            ( > 0, _) => "→",
            (_, > 0) => "↑",
            _ => "↓",
        };

    private static string Strokes(SymbolWire symbol)
    {
        var groups = symbol.Primitives
            .GroupBy(static p => (p.Kind, p.Fill, Dashed: p.Dashed == true))
            .Select(static g =>
            {
                var count = g.Count();
                var name = count == 1 ? g.Key.Kind : $"{count} {g.Key.Kind}s";
                var qualifier = (g.Key.Fill, g.Key.Dashed) switch
                {
                    ("state", _) => " (state fill)",
                    ("stroke", _) => " (solid)",
                    (_, true) => " (dashed)",
                    _ => string.Empty,
                };
                return name + qualifier;
            });

        return string.Join(", ", groups);
    }

    private static string Point(IReadOnlyList<double> at) => $"({Numbers(at)})";

    private static string Numbers(IReadOnlyList<double> values) =>
        string.Join(", ", values.Select(static v => v.ToString("0.##", CultureInfo.InvariantCulture)));
}
