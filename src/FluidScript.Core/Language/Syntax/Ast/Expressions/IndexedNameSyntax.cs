using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A name with an optional bracketed index: <c>in</c>, <c>in[2]</c>, <c>layer[3]</c>.</summary>
/// <param name="Name">The word.</param>
/// <param name="Index">The index after it, or <see langword="null"/> when there is none.</param>
/// <remarks>
/// A port family and its member are the same shape to the parser; whether <c>in[2]</c> names a port
/// the kind has is the binder's question. <see cref="Text"/> is the registry's spelling of what was
/// written, which is what binding matches on, and it never carries whitespace because the grammar
/// admits none inside an indexed name.
/// </remarks>
public sealed record IndexedNameSyntax(IdentifierSyntax Name, IndexSyntax? Index) : SyntaxNode
{
    /// <summary>Gets the name as written, index included: <c>in[2]</c> for <c>in[2]</c>, <c>in</c> for <c>in</c>.</summary>
    public string Text =>
        Index is null
            ? Name.Text
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name.Text}[{Index.Value}]");

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens => Index is null ? Name.Tokens : [.. Name.Tokens, .. Index.Tokens];
}
