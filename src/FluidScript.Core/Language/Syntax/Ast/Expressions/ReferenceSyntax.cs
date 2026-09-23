using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A reference to a value: a name, or a component's property.</summary>
/// <param name="Head">The first name.</param>
/// <param name="Parts">The <c>.name</c> steps after it. Empty for a plain name.</param>
/// <remarks>
/// This is also what a <c>symbol</c> parameter value parses as —
/// <c>characteristic=equal_percentage</c> is a head with no parts, bound rather than evaluated. The
/// parser cannot tell the two apart and does not try: nothing distinguishes them until a parameter's
/// declared kind says which it wanted.
/// </remarks>
public sealed record ReferenceSyntax(
    IdentifierSyntax Head,
    ImmutableArray<QualifiedNamePart> Parts) : ExpressionSyntax
{
    /// <summary>Gets everything after the head as one property name: <c>dp</c>, <c>in[2].t</c>, <c>layer[3].t</c>.</summary>
    /// <returns>The parts joined with <c>.</c>; empty for a plain name.</returns>
    /// <remarks>
    /// A property is one name to the registry however many dots it carries (<c>D-120</c>), so the
    /// binder resolves the whole path and never the last step alone — <c>HX1.in[2].t</c> is the
    /// property <c>in[2].t</c> of <c>HX1</c>, not the <c>t</c> of something called <c>in[2]</c>.
    /// </remarks>
    public string PropertyPath() =>
        Parts.IsDefaultOrEmpty ? string.Empty : string.Join('.', Parts.Select(static part => part.Name.Text));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [.. Head.Tokens, .. Parts.SelectMany(static part => part.Tokens)];
}
