using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Parsing;

internal sealed partial class LineParser
{
    // ---- statements -------------------------------------------------------------------------

    private StatementSyntax ParseVersion()
    {
        var keyword = Advance();
        var major = TakeNumber();
        if (major is null)
        {
            return Fail(ParserDiagnostics.UnclassifiableStatement, LineSpan);
        }

        return new VersionDirectiveSyntax(keyword, major);
    }

    private string? HexComment(Token keyword)
    {
        foreach (var trivia in keyword.TrailingTrivia)
        {
            if (trivia.Kind != TriviaKind.Comment)
            {
                continue;
            }

            // The comment begins at the '#' the user meant as a colour, so the hex run is what
            // follows it. Three or six digits, and nothing else -- a prose comment is not a colour.
            var text = source.Slice(trivia.Span);
            var digits = text[1..];
            var end = 0;
            while (end < digits.Length && Uri.IsHexDigit(digits[end]))
            {
                end++;
            }

            if (end is 3 or 6 && (end == digits.Length || digits[end] is ' ' or '\t'))
            {
                return text[..(end + 1)].ToString();
            }
        }

        return null;
    }

}
