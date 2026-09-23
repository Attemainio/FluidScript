using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A call to one of the language's fixed set of functions.</summary>
/// <param name="Name">The function name. The set is closed and the binder owns it.</param>
/// <param name="OpenParen">The opening parenthesis.</param>
/// <param name="Arguments">The arguments, in order. May be empty.</param>
/// <param name="CloseParen">The closing parenthesis.</param>
public sealed record CallSyntax(
    IdentifierSyntax Name,
    Token OpenParen,
    ImmutableArray<ArgumentSyntax> Arguments,
    Token CloseParen) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        .. Name.Tokens,
        OpenParen,
        .. Arguments.SelectMany(static argument => argument.Tokens),
        CloseParen,
    ];
}
