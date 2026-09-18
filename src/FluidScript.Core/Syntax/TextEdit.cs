using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Syntax;

/// <summary>One replacement in a script: the span to replace and the text to put there (<c>17</c>).</summary>
/// <remarks>
/// The editor applies a set of these as one undoable transaction. The spans of one result never
/// overlap (<c>17</c> invariant 6), so they may be applied in any order; an insertion is an empty span.
/// </remarks>
/// <param name="Span">The bytes to replace, in UTF-16 code units of the text the edit was computed on.</param>
/// <param name="NewText">What replaces them; empty for a deletion.</param>
public sealed record TextEdit(TextSpan Span, string NewText);
