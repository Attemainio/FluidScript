using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A bracketed whole number after a name: the <c>[2]</c> of <c>in[2]</c> (<c>D-120</c>).</summary>
/// <param name="OpenBracket">The <c>[</c>.</param>
/// <param name="Number">The index, a whole number with no unit.</param>
/// <param name="CloseBracket">The <c>]</c>.</param>
/// <remarks>
/// The parser admits it only when the three tokens and the name before them touch: <c>in [2]</c>
/// and <c>in[ 2 ]</c> are <c>FS1119</c>, because a port name is one word to the eye and the printer
/// would otherwise have to decide whether the space was meant.
/// </remarks>
public sealed record IndexSyntax(Token OpenBracket, Token Number, Token CloseBracket) : SyntaxNode
{
    /// <summary>Gets the index.</summary>
    /// <value>A positive whole number; the parser refused anything else.</value>
    public int Value => (int)(Number.Value ?? 0);

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [OpenBracket, Number, CloseBracket];
}
