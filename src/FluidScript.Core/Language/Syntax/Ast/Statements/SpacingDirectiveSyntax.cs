using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Component spacing on the canvas, in world units.</summary>
/// <param name="Keyword">The <c>spacing</c> word.</param>
/// <param name="Value">A bare number.</param>
/// <remarks>
/// Never a quantity: world units have no physical dimension, and accepting <c>20 mm</c> would imply
/// the canvas has a scale it does not have. A quantity here is <c>FS1113</c>.
/// </remarks>
public sealed record SpacingDirectiveSyntax(Token Keyword, NumberLiteralSyntax Value) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword, .. Value.Tokens];
}
