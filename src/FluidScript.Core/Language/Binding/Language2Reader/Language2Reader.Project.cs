using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Binding;

internal sealed partial class Language2Reader
{
    private const string ProjectSettings = "cases, catalog, show, scale, spacing, style";

    /// <summary>Reads the project block: its title, cases, catalogue and presentation (<c>19</c> §The project block, §Presentation).</summary>
    /// <remarks>
    /// <c>cases</c> gives the binder its cases with the first as the one it operates at: language 2 has no
    /// <c>design</c>, and the binder needs an operating case.
    /// </remarks>
    private void ReadProject(BlockSyntax block)
    {
        var head = (ProjectHeadSyntax)block.Head;
        _reading.Projects.Add(Title(head.Title, head.Keyword, "project").Text);

        var shows = new List<(int At, ImmutableArray<IdentifierSyntax> Names)>();
        var styles = new List<ImmutableArray<StyleTokenSyntax>>();
        ParameterSyntax? scale = null;

        foreach (var line in block.Body)
        {
            switch (line)
            {
                case SettingLineSyntax settings:
                    foreach (var setting in settings.Assignments)
                    {
                        if (Is(setting, "cases"))
                        {
                            Cases(setting);
                        }
                        else if (Is(setting, "catalog"))
                        {
                            Catalog(setting);
                        }
                        else if (Is(setting, "show"))
                        {
                            // Every `show` is handed on, so a second one is FS1214 as it is in language 1.
                            shows.Add((setting.Span.Start, Names(setting)));
                        }
                        else if (Is(setting, "scale"))
                        {
                            scale = setting;
                        }
                        else if (Is(setting, "spacing"))
                        {
                            Spacing(setting);
                        }
                        else
                        {
                            Unknown("project", setting, ProjectSettings);
                        }
                    }

                    break;

                case BlockSyntax { Head: StyleHeadSyntax } style:
                    styles.Add(Style(style));
                    break;

                default:
                    break;
            }
        }

        if (Merged(styles) is { } merged)
        {
            _reading.ProjectStyles.Add(merged);
        }

        if (shows.Count == 0)
        {
            if (scale is not null)
            {
                Report(
                    BinderDiagnostics.UnacceptedSymbol,
                    scale.Span,
                    ("parameter", "scale"),
                    ("available", "a range of the first property 'show' names, which this project does not state"),
                    ("written", Text(scale.Value)));
            }

            return;
        }

        RangeSyntax? range = null;
        if (scale?.Value is RangeExpressionSyntax written)
        {
            range = Range(written);
        }
        else if (scale is not null)
        {
            Report(
                BinderDiagnostics.UnacceptedSymbol,
                scale.Value.Span,
                ("parameter", "scale"),
                ("available", "a range such as 20..90 C"),
                ("written", Text(scale.Value)));
        }

        // Only a scale of two plain numbers is read, in the first property's canonical unit (`57`).
        (double, double)? stated = range is { From: NumberLiteralSyntax from, To: NumberLiteralSyntax to } ? (from.Value, to.Value) : null;

        _reading.Visualizations.Add(Visualization(shows[0], stated));
        _reading.Visualizations.AddRange(shows.Skip(1).Select(static show => Visualization(show, null)));

        // A made `show` keyword stands where the setting begins, which a second one is reported against.
        static VisualizationSymbol Visualization((int At, ImmutableArray<IdentifierSyntax> Names) show, (double, double)? scale) =>
            new([.. show.Names.Select(static name => (name.Text, name.Span))], scale, new TextSpan(show.At, 0));
    }

    /// <summary>One list of style tokens for a block's <c>style:</c> blocks, so a key stated twice is <c>FS1202</c> as a repeated token on one line is.</summary>
    /// <returns>The merged tokens; <see langword="null"/> when the block has no style.</returns>
    private static ImmutableArray<StyleTokenSyntax>? Merged(List<ImmutableArray<StyleTokenSyntax>> styles) =>
        styles.Count == 0 ? null : [.. styles.SelectMany(static style => style)];

    /// <summary>A title as the name language 1 gives the project or a circuit: the quoted text, or a name made up where none is written.</summary>
    private IdentifierSyntax Title(Token? title, Token keyword, string fallback) =>
        title is { Kind: TokenKind.StringLiteral }
            ? Identifier(title.StringValue ?? string.Empty, title.Span)
            : Identifier(fallback == "project" ? fallback : $"{fallback} {++_untitled}", keyword.Span);

    private void Cases(ParameterSyntax setting)
    {
        var names = Names(setting);
        if (names.IsEmpty)
        {
            return;
        }

        var at = setting.Span.Start;
        _reading.FileLines.Add(new BindingRun.CaseNames(
            [.. names.Select(static name => (name.Token.Text, name.Span))], TextSpan.FromBounds(at, names[^1].Span.End)));
        _reading.FileLines.Add(new BindingRun.DesignLine(
            (names[0].Token.Text, names[0].Span), TextSpan.FromBounds(at, names[0].Span.End)));
    }

    /// <summary>Reads a setting whose value is one name or a bracketed list of names: <c>cases</c>, <c>show</c>.</summary>
    /// <returns>The names; empty, with <c>FS1514</c> reported, when any item is not a plain name.</returns>
    private ImmutableArray<IdentifierSyntax> Names(ParameterSyntax setting)
    {
        ImmutableArray<ExpressionSyntax> items = setting.Value is ScenarioListSyntax list
            ? [.. list.Elements.Select(static element => element.Value)]
            : [setting.Value];

        var names = ImmutableArray.CreateBuilder<IdentifierSyntax>(items.Length);

        foreach (var item in items)
        {
            if (item is not ReferenceSyntax { Parts.IsDefaultOrEmpty: true } reference)
            {
                Report(
                    BinderDiagnostics.UnacceptedSymbol,
                    item.Span,
                    ("parameter", setting.Name.Text),
                    ("available", "names, such as [winter, mild]"),
                    ("written", Text(item)));
                return [];
            }

            names.Add(reference.Head);
        }

        return names.MoveToImmutable();
    }

    /// <summary>Checks <c>catalog = steel_en10255@2026.1</c>.</summary>
    /// <remarks>
    /// The pipeline reads the pin from the text before anything parses (<c>18</c>), so there is nothing here to
    /// bind; what matters is that a value the text pattern cannot read is said, rather than the default catalogue
    /// being used in silence.
    /// </remarks>
    private void Catalog(ParameterSyntax setting)
    {
        switch (setting.Value)
        {
            case CatalogReferenceSyntax:
            case ReferenceSyntax { Parts.IsDefaultOrEmpty: true }:
                break;

            default:
                Report(
                    BinderDiagnostics.UnacceptedSymbol,
                    setting.Value.Span,
                    ("parameter", "catalog"),
                    ("available", "a catalogue name, such as steel_en10255@2026.1"),
                    ("written", Text(setting.Value)));
                break;
        }
    }

    private void Spacing(ParameterSyntax setting)
    {
        if (setting.Value is NumberLiteralSyntax number)
        {
            _reading.Spacing = number.Value;
            return;
        }

        Report(
            BinderDiagnostics.UnacceptedSymbol,
            setting.Value.Span,
            ("parameter", "spacing"),
            ("available", "a plain number, such as 1.2"),
            ("written", Text(setting.Value)));
    }

    /// <summary>Reads a <c>style:</c> block into the style tokens the binder classifies (<c>D-171</c>).</summary>
    /// <remarks>
    /// Each key becomes the token language 1's style line would hold for it, and the binder's merge does the rest:
    /// a circuit's style line follows its header, so a circuit that states only <c>colour</c> keeps the project's
    /// width and pattern, which is the override <c>D-171</c> asks for.
    /// </remarks>
    private ImmutableArray<StyleTokenSyntax> Style(BlockSyntax block)
    {
        var parts = ImmutableArray.CreateBuilder<StyleTokenSyntax>();

        foreach (var setting in block.Body.OfType<SettingLineSyntax>().SelectMany(static line => line.Assignments))
        {
            if (StylePart(setting) is { } part)
            {
                parts.Add(part);
            }
        }

        return parts.ToImmutable();
    }

    private StyleTokenSyntax? StylePart(ParameterSyntax setting)
    {
        var value = setting.Value;
        var token = value.Tokens.Length == 1 ? value.Tokens[0] : null;

        if (Is(setting, "colour") || Is(setting, "color"))
        {
            // The value is checked here, where the setting is known, so a hex that does not parse or a name no
            // colour has is said against `colour` and its options (`L-77`) -- not as a style line's unplaceable
            // word (`FS1201`) or a named style this language does not have (`FS1204`).
            return token switch
            {
                { Kind: TokenKind.StringLiteral } when StyleTokens.Hex(token.StringValue ?? string.Empty) is not null =>
                    new StyleTokenSyntax(StyleTokenKind.Quoted, [token]),
                { Kind: TokenKind.Identifier } when NamedColours.TryGet(token.Text, out _) =>
                    new StyleTokenSyntax(StyleTokenKind.Word, [token]),
                _ => InvalidStyle(setting, "a colour name or a quoted hex such as \"#2f6f9f\""),
            };
        }

        if (Is(setting, "width"))
        {
            return token switch
            {
                { Kind: TokenKind.NumberLiteral, Value: > 0 } => new StyleTokenSyntax(StyleTokenKind.Number, [token]),
                { Kind: TokenKind.QuantityLiteral } when StyleTokens.Pixels(token.Text) is not null =>
                    new StyleTokenSyntax(StyleTokenKind.Quantity, [token]),
                _ => InvalidStyle(setting, "a width in pixels above zero, such as 2"),
            };
        }

        if (Is(setting, "corner"))
        {
            return token is { Kind: TokenKind.Identifier, Text: "sharp" or "fillet" }
                ? new StyleTokenSyntax(StyleTokenKind.Word, [token])
                : InvalidStyle(setting, "sharp or fillet");
        }

        if (Is(setting, "line"))
        {
            var pattern = token?.Text switch
            {
                "solid" => "-",
                "dashed" => "--",
                "dotted" => "..",
                "dashdot" => "-.",
                _ => null,
            };

            return pattern is null
                ? InvalidStyle(setting, "solid, dashed, dotted or dashdot")
                : new StyleTokenSyntax(StyleTokenKind.Pattern, [Made(TokenKind.Minus, pattern, token!.Span)]);
        }

        Unknown("style", setting, "colour, width, corner, line");
        return null;
    }

    private StyleTokenSyntax? InvalidStyle(ParameterSyntax setting, string available)
    {
        Report(
            BinderDiagnostics.UnacceptedSymbol,
            setting.Value.Span,
            ("parameter", setting.Name.Text),
            ("available", available),
            ("written", Text(setting.Value)));
        return null;
    }

    /// <summary>Reads <c>curve NAME: DRIVER</c> and its rows (<c>D-167</c>).</summary>
    /// <remarks>
    /// The rows pass through untouched: the binder splits a row's text. The header's span runs from the keyword to its
    /// last word or argument, the words before the arguments, as the binder reports a curve against it.
    /// </remarks>
    private void ReadCurve(BlockSyntax block)
    {
        var head = (DriverCurveHeadSyntax)block.Head;
        ImmutableArray<IdentifierSyntax> words = [.. head.Modifiers.OfType<IdentifierSyntax>()];
        ImmutableArray<ParameterSyntax> arguments =
            [.. head.Modifiers.OfType<ParameterSyntax>().Select(parameter => parameter with { Value = Value(parameter.Value) })];

        var end = !arguments.IsEmpty ? arguments[^1].Span.End : !words.IsEmpty ? words[^1].Span.End : head.Driver.Span.End;

        _reading.FileLines.Add(new BindingRun.CurveDraft(
            new BindingRun.CurveHead(
                head.Name.Text,
                head.Driver.Text,
                [.. words.Select(static word => (word.Text, word.Span))],
                arguments,
                TextSpan.FromBounds(head.Keyword.Span.Start, end)),
            [.. block.Body.OfType<CurveRowSyntax>()]));
    }

    /// <summary>Reports a setting the block does not have, with <c>FS1503</c>'s list of what it takes.</summary>
    private void Unknown(string block, ParameterSyntax setting, string available) =>
        Report(
            BinderDiagnostics.UnknownParameter,
            setting.Name.Span,
            ("kind", block),
            ("parameter", setting.Name.Text),
            ("available", available));
}
