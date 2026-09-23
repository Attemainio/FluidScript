using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>One <c>x y</c> row of a curve.</summary>
/// <param name="Parts">Every token on the line, in order.</param>
/// <remarks>
/// <para>
/// The row keeps its tokens rather than a parsed pair, and the split into <c>x</c> and <c>y</c> is the
/// binder's. The reason is <c>x</c>: a timestamp is not one token. <c>2026-01-01T00:00:00</c> lexes as
/// six tokens and an identifier, and <c>01/01/2026</c> as five — there is no context-free way to lex
/// either as a unit, because <c>2026-01-01</c> is also a perfectly good subtraction.
/// </para>
/// <para>
/// So the binder reads the row's <em>text</em> and splits it at the last run of whitespace: everything
/// before is <c>x</c>, parsed by the curve's format; the rest is <c>y</c>. Holding the tokens is what
/// keeps the printer exact, since it reproduces them without knowing what they meant.
/// </para>
/// </remarks>
public sealed record CurveRowSyntax(ImmutableArray<Token> Parts) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Parts;
}
