using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Ast;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>A <c>let</c> binding: a name for a value used more than once.</summary>
/// <param name="Name">The bound name.</param>
/// <param name="Expression">What it was bound to, retained for write-back.</param>
/// <param name="Id">This binding's identity in the dependency graph.</param>
/// <param name="Value">
/// The evaluated value, or <see langword="null"/> when the expression was deferred or failed.
/// </param>
/// <param name="DeclarationSpan">Where the binding sits in the source.</param>
/// <param name="Dimension">
/// The dimension the expression has, whether or not it has a value yet (<c>U-5</c>): the value's own when
/// evaluated, else what a dimension-only pass over the expression tree derives from the units it writes,
/// the properties it references and the bindings it reads. <see langword="null"/> when nothing in the
/// expression says -- a bare number, a curve, or a reference nothing resolves.
/// </param>
public sealed record BindingSymbol(
    string Name,
    ExpressionSyntax Expression,
    ValueId Id,
    Quantity? Value,
    TextSpan DeclarationSpan,
    Dimension? Dimension = null);
