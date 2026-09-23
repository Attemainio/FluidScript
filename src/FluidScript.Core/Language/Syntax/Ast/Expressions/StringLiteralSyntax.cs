using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A double-quoted string. Cannot span a newline (invariant 6).</summary>
/// <param name="Token">The literal as written, quotes included.</param>
public sealed record StringLiteralSyntax(Token Token) : ExpressionSyntax
{
    /// <summary>Gets the content, without the quotes.</summary>
    /// <value>There are no escape sequences in v1, so this is the source slice verbatim.</value>
    public string Value => Token.StringValue ?? string.Empty;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Token];
}
