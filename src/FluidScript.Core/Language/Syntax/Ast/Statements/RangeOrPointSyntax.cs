namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Either one value or a span between two.</summary>
/// <remarks>
/// The two cases are sibling records rather than nested ones, so that <see cref="RangeSyntax"/> can be
/// named on its own — <c>show</c> takes a range and never a point.
/// </remarks>
public abstract record RangeOrPointSyntax : SyntaxNode;
