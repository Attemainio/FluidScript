using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A language 2 block: a head line and the lines indented under it.</summary>
/// <param name="Head">
/// The first line: a <see cref="ProjectHeadSyntax"/>, <see cref="CircuitHeadSyntax"/>,
/// <see cref="RunHeadSyntax"/>, <see cref="StyleHeadSyntax"/>, <see cref="DriverCurveHeadSyntax"/>, or a
/// <see cref="ComponentDeclarationSyntax"/> whose parameters continue below it.
/// </param>
/// <param name="Colon">
/// The <c>:</c> ending the head line; <see langword="null"/> for a curve, whose colon sits inside its head,
/// and for a head written without one (<c>FS1812</c>).
/// </param>
/// <param name="Body">The indented lines, in source order, malformed lines and nested blocks included.</param>
/// <remarks>
/// <c>plan/10-language/19-fluidscript-2.md</c> §Lines, blocks and names. A block is a statement like any
/// other, so a script's statements stay one flat list at the top level with the nesting inside the nodes,
/// and every token still appears once, in source order: the printer needs nothing new to print a block.
/// </remarks>
public sealed record BlockSyntax(
    StatementSyntax Head,
    Token? Colon,
    ImmutableArray<StatementSyntax> Body) : StatementSyntax
{
    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
    [
        .. Head.Tokens,
        .. Colon is null ? [] : new[] { Colon },
        .. Body.SelectMany(static statement => statement.Tokens),
    ];
}
