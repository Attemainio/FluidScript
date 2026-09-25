using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>Everything one run of the parser produced.</summary>
/// <param name="Source">
/// The text that was parsed. Carried here because a trivium addresses its characters rather than
/// holding them, so printing the tree back needs the text it came from, and a result that carried
/// only the tree could be printed against the wrong one.
/// </param>
/// <param name="Root">
/// The tree, always non-null. A tree containing <see cref="MalformedStatementSyntax"/> nodes is a
/// normal result, not a failure.
/// </param>
/// <param name="Diagnostics">
/// Every diagnostic the lexer and the parser produced, in source order.
/// </param>
public sealed record ParseResult(
    SourceText Source,
    ScriptSyntax Root,
    ImmutableArray<Diagnostic> Diagnostics)
{
    /// <summary>Gets the language major the tree was read in.</summary>
    /// <value>
    /// 1, unless the tree is language 2's translated into the statements the binder reads
    /// (<c>Language2Translator</c>), which says 2. The binder reads it where the two languages bind the
    /// same statement differently: a name matched by similarity (<c>D-170</c>) and a circuit's role.
    /// </value>
    public int Language { get; init; } = 1;
}
