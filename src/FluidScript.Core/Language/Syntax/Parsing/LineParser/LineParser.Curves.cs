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

    /// <summary>Takes one <c>x y</c> row whole, without interpreting either column.</summary>
    /// <remarks>
    /// The split is the binder's, because <c>x</c> may be a timestamp, and only an ISO one,
    /// <c>2026-01-01T00:00:00</c>, lexes as one date token; a curve's own <c>format</c>, such as
    /// <c>01/01/2026 00:00:00</c>, lexes as many. The parser's job here is to keep every token on the
    /// line so the printer stays exact.
    /// </remarks>
    private StatementSyntax ParseCurveRow()
    {
        // Counted in whitespace-separated parts, not tokens, because that is how the binder splits the
        // row: `-26` is two tokens and one value, and `01/01/2026 00:00:00 -1` is many tokens and
        // three parts, of which the first two are one timestamp.
        if (source.ToString(LineSpan).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            return Fail(ParserDiagnostics.MalformedCurveRow, LineSpan);
        }

        _index = tokens.Length;
        return new CurveRowSyntax(tokens);
    }

    /// <summary>Parses the bracketed value list a parameter may take after <c>=</c> (<c>D-143</c>).</summary>
    /// <returns>The list, or <see langword="null"/> after reporting <c>FS1121</c>.</returns>
    /// <remarks>
    /// The <c>[</c> is already known to be here, and the elements are ordinary values, so this is a
    /// comma loop around <see cref="ParseExpression"/>. It never recurses into itself: a nested list
    /// would be an element whose first token is <c>[</c>, and <c>ParseExpression</c> has no rule for
    /// one, so it fails there with its own message rather than parsing into a shape with no meaning.
    /// </remarks>
    private ScenarioListSyntax? ParseScenarioList()
    {
        var open = Advance();
        var elements = ImmutableArray.CreateBuilder<ArgumentSyntax>();
        Token? comma = null;

        while (true)
        {
            if (Current is not { } token || token.Kind == TokenKind.CloseBracket)
            {
                // `[]`, `[30,]` and an unterminated `[30` all land here: every one of them is a slot
                // with no value in it, and none is worth its own message.
                Report(ParserDiagnostics.MalformedScenarioList, (comma ?? open).Span);
                return null;
            }

            var explained = diagnostics.Count;
            var value = ParseExpression();

            if (value is null)
            {
                if (diagnostics.Count == explained)
                {
                    Report(ParserDiagnostics.MalformedScenarioList, token.Span);
                }

                return null;
            }

            elements.Add(new ArgumentSyntax(comma, value));

            if (Current is { Kind: TokenKind.Comma })
            {
                comma = Advance();
                continue;
            }

            if (Current is { Kind: TokenKind.CloseBracket })
            {
                return new ScenarioListSyntax(open, elements.ToImmutable(), Advance());
            }

            // Two values with nothing between them -- `[30 10]` -- or a line that ended open.
            Report(ParserDiagnostics.MalformedScenarioList, (Current ?? open).Span);
            return null;
        }
    }
}
