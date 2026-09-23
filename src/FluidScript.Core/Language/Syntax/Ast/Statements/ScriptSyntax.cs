using System.Collections.Immutable;

using FluidScript.Core.Language.Syntax.Lexing;

namespace FluidScript.Core.Language.Syntax.Ast.Statements;

/// <summary>A whole script: one ordered list of statements, and the end token that closes it.</summary>
/// <param name="Statements">Every statement in source order, malformed lines included.</param>
/// <param name="EndOfFile">
/// The zero-length token at the end of the text. Trivia after the last statement attaches to it, so a
/// file ending in comments or blank lines round-trips.
/// </param>
/// <remarks>
/// The version directive is a statement in this list rather than a field of its own (<c>D-54</c>),
/// even though the grammar writes it as <c>script = version-directive , { statement }</c>. A script
/// under editing has it missing, duplicated, or not first, and all three have to round-trip; one
/// ordered list is what makes that true and keeps the printer walking a single sequence.
/// </remarks>
public sealed record ScriptSyntax(
    ImmutableArray<StatementSyntax> Statements,
    Token EndOfFile) : SyntaxNode
{
    /// <summary>Gets the first version directive, if the script has one.</summary>
    /// <value>
    /// <see langword="null"/> when none was written. Whether that is an unsaved draft or a broken file
    /// is <c>18-script-compatibility</c>'s to decide, not the parser's.
    /// </value>
    public VersionDirectiveSyntax? Version =>
        Statements.OfType<VersionDirectiveSyntax>().FirstOrDefault();

    /// <inheritdoc/>
    public override ImmutableArray<Token> Tokens =>
        [.. Statements.SelectMany(static statement => statement.Tokens), EndOfFile];
}
