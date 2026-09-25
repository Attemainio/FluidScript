using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A language 2 line of <c>name = value</c> pairs.</summary>
/// <param name="Assignments">The pairs, in source order; never empty.</param>
/// <remarks>
/// One shape serves three places (<c>plan/10-language/19-fluidscript-2.md</c>): a setting of the enclosing
/// block (<c>fluid = water</c> in a circuit, <c>cases = [winter, mild]</c> in the project), a parameter
/// line in a component's block (<c>primary.out.t = 45 C</c>), and an override in a run
/// (<c>outdoor = weather_jan</c>). What a pair means is the block's business, not the line's.
/// </remarks>
public sealed record SettingLineSyntax(ImmutableArray<ParameterSyntax> Assignments) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => [.. Assignments.SelectMany(static assignment => assignment.Tokens)];
}
