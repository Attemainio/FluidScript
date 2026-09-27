using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Ast.Expressions;
using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>Declares a component: a name, a kind, and a bag of parameters.</summary>
/// <param name="Name">The component's name, unique across the whole model (<c>D-41</c>).</param>
/// <param name="Kind">The kind, resolved against the component registry at bind time.</param>
/// <param name="AtKeyword">The <c>at</c> word, or <see langword="null"/> when there is no attachment.</param>
/// <param name="AttachedTo">
/// The node an observer is placed on (<c>D-61</c>), or <see langword="null"/>. Only an instrument
/// kind accepts one; the parser records it wherever it is written and the binder decides.
/// </param>
/// <param name="Parameters">The stated parameters. An omitted one is absence, never null.</param>
/// <param name="SizingPoint">
/// The driver values this component is sized at (<c>D-94</c>, <c>D-175</c>), each a <c>driver = value</c> pair.
/// The parser leaves it empty: a script writes each as a setting, <c>sized_at.outdoor = -5 C</c>, and the reader
/// moves those here. A heat pump written so reads its curve at −5 where the rest of the plant reads it at the
/// design day; the bivalent point is the engineer's decision and this is where it is written.
/// </param>
public sealed record ComponentDeclarationSyntax(
    IdentifierSyntax Name,
    IdentifierSyntax Kind,
    Token? AtKeyword,
    IdentifierSyntax? AttachedTo,
    ImmutableArray<ParameterSyntax> Parameters,
    ImmutableArray<ParameterSyntax> SizingPoint) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        .. Name.Tokens,
        .. Kind.Tokens,
        .. AtKeyword is { } at && AttachedTo is { } node
            ? new[] { at }.Concat(node.Tokens)
            : [],
        .. Parameters.SelectMany(static parameter => parameter.Tokens),
        .. SizingPoint.SelectMany(static parameter => parameter.Tokens),
    ];
}
