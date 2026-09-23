using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Syntax;

/// <summary>Everything one run of the lexer produced.</summary>
/// <param name="Source">The text that was lexed.</param>
/// <param name="Tokens">
/// Every token in source order, ending with exactly one <see cref="TokenKind.EndOfFile"/>. Never
/// empty, whatever the input was.
/// </param>
/// <param name="Diagnostics">
/// What the lexer had to say. Empty for a clean script, and a normal, non-fatal result otherwise:
/// tokens are produced for malformed input too, because a script under editing is malformed most of
/// the time and the stages above still have to run on it.
/// </param>
public sealed record LexResult(
    SourceText Source,
    ImmutableArray<Token> Tokens,
    ImmutableArray<Diagnostic> Diagnostics);
