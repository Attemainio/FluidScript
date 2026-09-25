using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Parsing;

internal sealed partial class LineParser
{
    private Token? TakeModeKeyword() =>
        Current is { Kind: TokenKind.Keyword, Keyword: ReservedWord.Dynamic or ReservedWord.Static }
            ? Advance()
            : null;

    private IdentifierSyntax? TakeIdentifier()
    {
        if (Current is not { } token)
        {
            return null;
        }

        switch (token.Kind)
        {
            // Language 2 reserves its statement words by position rather than in the lexer (`19`), so a
            // name spelled as one is caught here, where a name belongs, with language 1's code. Before an `=`
            // it is a setting's name, which no statement starts with: a controller's `curve = heating`.
            case TokenKind.Identifier when language2 && IsStatementWord(token.Text)
                && tokens.ElementAtOrDefault(_index + 1) is not { Kind: TokenKind.Equals }:
                Report(
                    ParserDiagnostics.ReservedWordAsName,
                    token.Span,
                    new DiagnosticArgument("word", token.Text));
                return null;

            case TokenKind.Identifier:
                return new IdentifierSyntax(Advance());

            case TokenKind.Keyword:
                Report(
                    ParserDiagnostics.ReservedWordAsName,
                    token.Span,
                    new DiagnosticArgument("word", token.Text));
                return null;

            case TokenKind.QuantityLiteral:
                // `3K` is three kelvin everywhere, including where that is correct, so only here --
                // where a name belongs -- can it be reported. Swapping the parts is the fix that works.
                Report(
                    ParserDiagnostics.NameReadsAsQuantity,
                    token.Span,
                    new DiagnosticArgument("name", token.Text),
                    new DiagnosticArgument(
                        "value",
                        (token.Value ?? 0).ToString(CultureInfo.InvariantCulture)),
                    new DiagnosticArgument("unit", token.Unit ?? string.Empty),
                    new DiagnosticArgument(
                        "suggestion",
                        (token.Unit ?? string.Empty) + (token.NumberText ?? string.Empty)));
                return null;

            default:
                return null;
        }
    }

    private NumberLiteralSyntax? TakeNumber() =>
        Current is { Kind: TokenKind.NumberLiteral } ? new NumberLiteralSyntax(Advance()) : null;

    /// <summary>Reads a name and the <c>[n]</c> that may follow it (<c>D-120</c>).</summary>
    /// <returns>
    /// The name, or <see langword="null"/> when there is no name here or the index after it is not
    /// one whole number in touching brackets — reported as <c>FS1119</c>, with the bracket and
    /// whatever follows it up to the <c>]</c> consumed so the line's remaining tokens are not read as
    /// something else.
    /// </returns>
    private IndexedNameSyntax? TakeIndexedName()
    {
        var name = TakeIdentifier();
        if (name is null)
        {
            return null;
        }

        if (Current is not { Kind: TokenKind.OpenBracket } open)
        {
            return new IndexedNameSyntax(name, null);
        }

        var number = tokens.ElementAtOrDefault(_index + 1);
        var close = tokens.ElementAtOrDefault(_index + 2);

        // Touching, and a whole number: `Value` alone would accept `2.0` and `2e0`, which read as an
        // index to no one, so the text is what decides.
        var wellFormed = open.Span.Start == name.Span.End
            && number is { Kind: TokenKind.NumberLiteral }
            && number.Span.Start == open.Span.End
            && number.Text.All(static c => c is >= '0' and <= '9')
            && close is { Kind: TokenKind.CloseBracket }
            && close.Span.Start == number.Span.End;

        if (wellFormed)
        {
            return new IndexedNameSyntax(name, new IndexSyntax(Advance(), Advance(), Advance()));
        }

        var start = open.Span.Start;
        var end = open.Span.End;
        while (Current is { } stray && stray.Kind != TokenKind.EndOfFile)
        {
            end = Advance().Span.End;
            if (stray.Kind == TokenKind.CloseBracket)
            {
                break;
            }
        }

        Report(ParserDiagnostics.MalformedIndex, TextSpan.FromBounds(start, end));
        return null;
    }

    /// <summary>Reads a parameter's name: a word, an indexed port, or a port's quantity (<c>D-120</c>).</summary>
    /// <returns>The name, or <see langword="null"/> after a malformed index; the caller has already checked that a name starts here.</returns>
    private QualifiedNameSyntax? TakeQualifiedName()
    {
        var head = TakeIndexedName();
        if (head is null)
        {
            return null;
        }

        var parts = ImmutableArray.CreateBuilder<QualifiedNamePart>();

        while (Current is { Kind: TokenKind.Dot } dot
               && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Identifier } next
               && dot.Span.Start == tokens[_index - 1].Span.End
               && next.Span.Start == dot.Span.End)
        {
            Advance();
            var part = TakeIndexedName();
            if (part is null)
            {
                return null;
            }

            parts.Add(new QualifiedNamePart(dot, part));
        }

        return new QualifiedNameSyntax(head, parts.ToImmutable());
    }

    private bool TryReadHyphenated(out string written, out string underscored)
    {
        written = string.Empty;
        underscored = string.Empty;

        if (Current is not { } first || !IsNamePart(first))
        {
            return false;
        }

        var text = first.Text;
        var end = first.Span.End;
        var index = _index + 1;
        var joins = 0;

        while (index + 1 < tokens.Length
               && tokens[index].Kind == TokenKind.Minus
               && tokens[index].Span.Start == end
               && tokens[index + 1].Span.Start == tokens[index].Span.End
               && IsNamePart(tokens[index + 1]))
        {
            text += "-" + tokens[index + 1].Text;
            end = tokens[index + 1].Span.End;
            index += 2;
            joins++;
        }

        if (joins == 0)
        {
            return false;
        }

        written = text;
        underscored = text.Replace('-', '_');
        return true;
    }

    private static bool IsNamePart(Token token) =>
        token.Kind is TokenKind.Identifier or TokenKind.NumberLiteral or TokenKind.Keyword;
}
