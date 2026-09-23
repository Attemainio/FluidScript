using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A line the parser could not classify.</summary>
/// <param name="Parts">Every token on the line, so nothing is lost.</param>
/// <remarks>
/// This is what makes P4 and <c>R-05</c> real. Recovery is line-granular: a line that cannot be parsed
/// becomes one of these, the parser resumes at the next line, and every other line is unaffected. Line
/// granularity is chosen over token-level recovery because the language is line-oriented — there is no
/// construct spanning lines to resynchronise into.
/// </remarks>
public sealed record MalformedStatementSyntax(ImmutableArray<Token> Parts) : StatementSyntax
{
    /// <summary>Gets the line exactly as written, excluding its trivia.</summary>
    public string RawText => string.Concat(Parts.Select(static part => part.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Parts;
}
