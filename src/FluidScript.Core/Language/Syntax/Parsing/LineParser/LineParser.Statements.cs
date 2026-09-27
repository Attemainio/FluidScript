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

    // ---- pieces -----------------------------------------------------------------------------

    private ImmutableArray<ParameterSyntax> ParseParameters(out bool failed)
    {
        var parameters = ImmutableArray.CreateBuilder<ParameterSyntax>();
        failed = false;

        // A sizing point is a setting per driver, `sized_at.outdoor = -5 C` (`D-175`): an ordinary parameter here.
        while (Current is { Kind: TokenKind.Identifier } nameToken)
        {
            var parameter = ParseParameter(nameToken);
            if (parameter is null)
            {
                failed = true;
                return [];
            }

            parameters.Add(parameter);
        }

        return parameters.ToImmutable();
    }

    /// <summary>Reads one <c>name=value</c> pair starting at the current token.</summary>
    /// <param name="nameToken">The current token, which starts the name.</param>
    /// <returns>The pair, or <see langword="null"/> after a diagnostic explaining why it is not one.</returns>
    private ParameterSyntax? ParseParameter(Token nameToken)
    {
        if (!EqualsFollowsName())
        {
            Report(
                ParserDiagnostics.ParameterWithoutValue,
                nameToken.Span,
                new DiagnosticArgument("token", nameToken.Text));
            return null;
        }

        var name = TakeQualifiedName();
        if (name is null)
        {
            return null;
        }

        var equals = Advance();
        var explained = diagnostics.Count;

        // A value may be a list with a unit after it, or a range (`ParseValue`).
        var value = ParseValue();

        if (value is null)
        {
            // A value that failed with its own message (`FS1119`) does not also need the general one.
            if (diagnostics.Count == explained)
            {
                Report(ParserDiagnostics.UnclassifiableStatement, LineSpan);
            }

            return null;
        }

        return new ParameterSyntax(name, equals, value);
    }

    /// <summary>Whether the name starting at the current token — a word, its index, its dotted quantity — is followed by <c>=</c>.</summary>
    /// <remarks>
    /// The one-token lookahead of <c>12</c> is over statements, not over a name: a parameter name is
    /// one lexical unit to the grammar and several tokens to the lexer, so deciding "is this a
    /// parameter" reads to the end of the name. Nothing else can start with a word and a bracket.
    /// </remarks>
    private bool EqualsFollowsName()
    {
        var index = _index + 1;

        while (tokens.ElementAtOrDefault(index) is { } token
               && token.Kind is TokenKind.OpenBracket or TokenKind.NumberLiteral or TokenKind.CloseBracket
                   or TokenKind.Dot or TokenKind.Identifier)
        {
            index++;
        }

        return tokens.ElementAtOrDefault(index) is { Kind: TokenKind.Equals };
    }

    private EndpointSyntax? TakeEndpoint()
    {
        var component = TakeIdentifier();
        if (component is null)
        {
            return null;
        }

        if (Current is { Kind: TokenKind.Dot }
            && tokens.ElementAtOrDefault(_index + 1) is { Kind: TokenKind.Identifier })
        {
            var dot = Advance();
            var port = TakeQualifiedName();
            return port is null ? null : new EndpointSyntax(component, dot, port);
        }

        return new EndpointSyntax(component, null, null);
    }
}
