using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A number with no unit symbol.</summary>
/// <param name="Token">The literal as written.</param>
public sealed record NumberLiteralSyntax(Token Token) : ExpressionSyntax
{
    /// <summary>Gets the value.</summary>
    public double Value => Token.Value ?? double.NaN;

    /// <summary>Gets the source spelling.</summary>
    /// <value>
    /// <c>1.50</c>, <c>1.5</c> and <c>15e-1</c> are one value and three different strings, and the
    /// printer reproduces the one that was written (<c>R-25</c>).
    /// </value>
    public string Text => Token.Text;

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Token];
}
