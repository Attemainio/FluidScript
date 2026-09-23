using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Expressions;

/// <summary>A parameter's name: a word, an indexed port, or a port's quantity — <c>power</c>, <c>in[2]</c>, <c>in[2].t</c> (<c>D-120</c>).</summary>
/// <param name="Head">The first name, with its index when it has one.</param>
/// <param name="Parts">The <c>.name</c> steps after it. Empty for a plain parameter.</param>
/// <remarks>
/// The parser accepts any depth; the binder decides what a kind admits. One step is the whole of
/// <c>D-120</c> (<c>in[2].t</c>), and a deeper name reaches the registry as an unknown parameter,
/// which is <c>FS1503</c> with the accepted list — the parser has no better message to give.
/// </remarks>
public sealed record QualifiedNameSyntax(
    IndexedNameSyntax Head,
    ImmutableArray<QualifiedNamePart> Parts) : SyntaxNode
{
    /// <summary>Gets the whole name in the registry's spelling: <c>in[2].t</c>.</summary>
    /// <value>
    /// The written text with any whitespace removed, which the grammar already forbids inside a name;
    /// <c>in[1]</c> is kept as written here and folded to <c>in</c> by the binder, since the two are
    /// one port and the diagnostic that says so belongs there.
    /// </value>
    public string Text => Parts.IsDefaultOrEmpty
        ? Head.Text
        : string.Concat(Head.Text, string.Concat(Parts.Select(static part => "." + part.Name.Text)));

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        Parts.IsDefaultOrEmpty ? Head.Tokens : [.. Head.Tokens, .. Parts.SelectMany(static part => part.Tokens)];
}
