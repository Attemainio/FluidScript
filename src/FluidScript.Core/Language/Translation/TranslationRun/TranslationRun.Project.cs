using System.Collections.Immutable;

using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Translation;

internal sealed partial class TranslationRun
{
    private const string ProjectSettings = "cases, catalog, show, scale, spacing, style";

    /// <summary>Translates the project block: its title, cases, catalogue and presentation (<c>19</c> §The project block, §Presentation).</summary>
    /// <remarks>
    /// <c>cases</c> becomes language 1's scenario list with the first case as the one <c>design</c> names
    /// (<c>19</c> §Translation): language 2 has no <c>design</c>, and the binder needs an operating case.
    /// </remarks>
    private void TranslateProject(BlockSyntax block)
    {
        var head = (ProjectHeadSyntax)block.Head;
        _fileWide.Add(new ProjectDirectiveSyntax(head.Keyword, null, Title(head.Title, head.Keyword, "project"), []));

        ImmutableArray<IdentifierSyntax> shown = [];
        ParameterSyntax? scale = null;
        Token? showKeyword = null;

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
                            showKeyword = Keyword(ReservedWord.Show, "show", setting.Span.Start);
                            shown = Names(setting);
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
                    _fileWide.Add(Style(style));
                    break;

                default:
                    break;
            }
        }

        if (showKeyword is null)
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

        _fileWide.Add(new ShowDirectiveSyntax(showKeyword, shown, range));
    }

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
        _fileWide.Add(new ScenariosDirectiveSyntax(Keyword(ReservedWord.Scenarios, "scenarios", at), names));
        _fileWide.Add(new DesignDirectiveSyntax(Keyword(ReservedWord.Design, "design", at), [], names[0]));
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

    /// <summary>Translates <c>catalog = steel_en10255@2026.1</c>.</summary>
    /// <remarks>
    /// The pipeline reads the pin from the text before anything parses (<c>18</c>), so the directive made here
    /// only mirrors it; what matters is that a value the text pattern cannot read is said, rather than the
    /// default catalogue being used in silence.
    /// </remarks>
    private void Catalog(ParameterSyntax setting)
    {
        var keyword = Keyword(ReservedWord.Catalog, "catalog", setting.Span.Start);

        switch (setting.Value)
        {
            case CatalogReferenceSyntax pinned:
                _fileWide.Add(new CatalogDirectiveSyntax(keyword, pinned.Catalog, pinned.Version));
                break;

            case ReferenceSyntax { Parts.IsDefaultOrEmpty: true } named:
                _fileWide.Add(new CatalogDirectiveSyntax(keyword, named.Head, null));
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
            _fileWide.Add(new SpacingDirectiveSyntax(Keyword(ReservedWord.Spacing, "spacing", setting.Span.Start), number));
            return;
        }

        Report(
            BinderDiagnostics.UnacceptedSymbol,
            setting.Value.Span,
            ("parameter", "spacing"),
            ("available", "a plain number, such as 1.2"),
            ("written", Text(setting.Value)));
    }

    /// <summary>Translates a <c>style:</c> block into language 1's style line (<c>D-171</c>).</summary>
    /// <remarks>
    /// Each key becomes the token language 1's style line would hold for it, and the binder's merge does the rest:
    /// a circuit's style line follows its header, so a circuit that states only <c>colour</c> keeps the project's
    /// width and pattern, which is the override <c>D-171</c> asks for.
    /// </remarks>
    private StyleDirectiveSyntax Style(BlockSyntax block)
    {
        var head = (StyleHeadSyntax)block.Head;
        var parts = ImmutableArray.CreateBuilder<StyleTokenSyntax>();

        foreach (var setting in block.Body.OfType<SettingLineSyntax>().SelectMany(static line => line.Assignments))
        {
            if (StylePart(setting) is { } part)
            {
                parts.Add(part);
            }
        }

        return new StyleDirectiveSyntax(
            Keyword(ReservedWord.Style, "style", head.Keyword.Span.Start),
            null,
            null,
            parts.ToImmutable());
    }

    private StyleTokenSyntax? StylePart(ParameterSyntax setting)
    {
        var value = setting.Value;
        var token = value.Tokens.Length == 1 ? value.Tokens[0] : null;

        if (Is(setting, "colour") || Is(setting, "color"))
        {
            return token switch
            {
                { Kind: TokenKind.StringLiteral } => new StyleTokenSyntax(StyleTokenKind.Quoted, [token]),
                { Kind: TokenKind.Identifier } => new StyleTokenSyntax(StyleTokenKind.Word, [token]),
                _ => InvalidStyle(setting, "a colour name or a quoted hex such as \"#2f6f9f\""),
            };
        }

        if (Is(setting, "width"))
        {
            return token switch
            {
                { Kind: TokenKind.NumberLiteral } => new StyleTokenSyntax(StyleTokenKind.Number, [token]),
                { Kind: TokenKind.QuantityLiteral } => new StyleTokenSyntax(StyleTokenKind.Quantity, [token]),
                _ => InvalidStyle(setting, "a width in pixels, such as 2"),
            };
        }

        if (Is(setting, "corner"))
        {
            return token is { Kind: TokenKind.Identifier }
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

    /// <summary>Translates <c>curve NAME: DRIVER</c> and its rows (<c>D-167</c>).</summary>
    /// <remarks>
    /// The rows pass through untouched: the binder splits a row's text, and the text is language 2's. What the
    /// driver means — a <c>let</c> read in each case — is <c>P6.11</c> package 3c's.
    /// </remarks>
    private void TranslateCurve(BlockSyntax block)
    {
        var head = (DriverCurveHeadSyntax)block.Head;

        _fileWide.Add(new CurveHeaderSyntax(
            head.Keyword,
            head.Name,
            head.Driver,
            [.. head.Modifiers.OfType<IdentifierSyntax>()],
            [.. head.Modifiers.OfType<ParameterSyntax>().Select(parameter => parameter with { Value = Value(parameter.Value) })]));

        _fileWide.AddRange(block.Body.OfType<CurveRowSyntax>());
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
