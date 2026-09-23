using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Names the operating cases the plant is sized for (<c>D-143</c>).</summary>
/// <param name="Keyword">The <c>scenarios</c> word.</param>
/// <param name="Names">The case names, in the order written; that order is what an array binds to.</param>
/// <remarks>
/// <para>
/// File-wide, like <c>project</c> and <c>design</c>. <strong>The order is the contract</strong>: an
/// array parameter binds to it positionally and to nothing else, so reordering this line silently
/// reassigns every array in the file. That is why the names appear in every basis string a size
/// carries -- a number a user can check the position against.
/// </para>
/// <para>
/// A file without this line is unchanged in every respect, which is what makes the whole feature
/// additive: no array can be written where there is no list to bind it to (<c>FS1541</c>).
/// </para>
/// </remarks>
public sealed record ScenariosDirectiveSyntax(
    Token Keyword,
    ImmutableArray<IdentifierSyntax> Names) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [Keyword, .. Names.SelectMany(static name => name.Tokens)];
}
