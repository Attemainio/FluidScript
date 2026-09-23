using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Begins a circuit's transient disturbances.</summary>
/// <param name="Keyword">The <c>schedule</c> word.</param>
public sealed record ScheduleHeaderSyntax(Token Keyword) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [Keyword];
}
