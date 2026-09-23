using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>The <c>fluidscript</c> line and the language major it names.</summary>
/// <param name="Keyword">The <c>fluidscript</c> word.</param>
/// <param name="Major">The major version as written.</param>
/// <remarks>
/// The parser records it and judges nothing. Whether an absent directive is an unsaved draft
/// (<c>FS1701</c>) or a misplaced one (<c>FS1705</c>) depends on whether the text is a durable file,
/// which the parser cannot know.
/// </remarks>
public sealed record VersionDirectiveSyntax(Token Keyword, NumberLiteralSyntax Major) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword, .. Major.Tokens];
}
