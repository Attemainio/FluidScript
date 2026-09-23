using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Syntax;

/// <summary>One run of characters that carries no meaning to the binder.</summary>
/// <param name="Kind">What the run is.</param>
/// <param name="Span">Where it sits in the source text.</param>
/// <remarks>
/// The text is addressed rather than copied: a trivium is recovered with
/// <see cref="SourceText.Slice(TextSpan)"/>. Whitespace and comments are most of a well-written script
/// by character count, and holding a string for each would roughly double what a document costs.
/// </remarks>
public readonly record struct Trivia(TriviaKind Kind, TextSpan Span);
