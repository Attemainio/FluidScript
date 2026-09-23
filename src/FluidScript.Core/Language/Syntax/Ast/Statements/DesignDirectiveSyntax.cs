using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Names the operating point: a scenario (<c>D-143</c>), or each curve driver's value (<c>D-58</c>).</summary>
/// <param name="Keyword">The <c>design</c> word.</param>
/// <param name="Arguments">One <c>driver=value</c> per driver, order-independent. Empty when a scenario is named.</param>
/// <param name="Scenario">The scenario named, or <see langword="null"/> for the driver form.</param>
/// <remarks>
/// <para>
/// File-wide, like <c>project</c> and <c>spacing</c>, and for the same reason: an outdoor temperature
/// is a property of the site, not of one circuit.
/// </para>
/// <para>
/// <strong>One word, one job (<c>D-143</c>).</strong> Either spelling names where the plant
/// <em>operates</em> -- the state the canvas draws, the numbers an export carries, the inputs a run
/// starts from -- and neither sizes anything. The driver form remains for a file with no
/// <c>scenarios</c> line; <c>D-138</c>'s <c>driver=range</c>, which made this word mean a sizing
/// range as well, is withdrawn.
/// </para>
/// </remarks>
public sealed record DesignDirectiveSyntax(
    Token Keyword,
    ImmutableArray<ParameterSyntax> Arguments,
    IdentifierSyntax? Scenario = null) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Scenario is { } named
        ? [Keyword, .. named.Tokens]
        : [Keyword, .. Arguments.SelectMany(static argument => argument.Tokens)];
}
