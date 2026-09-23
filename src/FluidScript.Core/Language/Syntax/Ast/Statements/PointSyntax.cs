using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One value.</summary>
/// <param name="Value">The value.</param>
public sealed record PointSyntax(ExpressionSyntax Value) : RangeOrPointSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Value.Tokens;
}
