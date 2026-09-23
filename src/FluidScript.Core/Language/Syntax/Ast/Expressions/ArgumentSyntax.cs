using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>One argument of a call, with the comma that precedes it.</summary>
/// <param name="LeadingComma">The comma before this argument; <see langword="null"/> for the first.</param>
/// <param name="Value">The argument.</param>
public sealed record ArgumentSyntax(Token? LeadingComma, ExpressionSyntax Value) : SyntaxNode
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        LeadingComma is null ? Value.Tokens : [LeadingComma, .. Value.Tokens];
}
