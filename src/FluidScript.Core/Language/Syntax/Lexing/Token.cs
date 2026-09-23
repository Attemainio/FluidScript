using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Syntax;

/// <summary>One token, with the trivia attached to it.</summary>
/// <remarks>
/// <para>
/// Trivia is attached rather than interleaved so that losslessness is a property of the token list
/// alone: concatenating, for every token in order, its leading trivia, its own text and its trailing
/// trivia reproduces the source byte for byte. Every character of the input is in exactly one of those
/// three places.
/// </para>
/// <para>
/// <strong>Trailing trivia stops before the line break.</strong> A run of whitespace and a comment
/// after a token on the same line are its trailing trivia; the newline that ends the line, and
/// everything after it, is the *next* token's leading trivia. That is what lets the printer put a
/// trailing comment back in the same column, and it keeps a blank line owned by the statement that
/// follows it rather than the one before.
/// </para>
/// </remarks>
public sealed record Token
{
    /// <summary>Gets what this token is.</summary>
    public required TokenKind Kind { get; init; }

    /// <summary>Gets where the token sits, excluding its trivia.</summary>
    /// <value>Empty only for <see cref="TokenKind.EndOfFile"/>.</value>
    public required TextSpan Span { get; init; }

    /// <summary>Gets the token exactly as written.</summary>
    /// <value>
    /// The source slice <see cref="Span"/> covers. For a quantity this includes any whitespace between
    /// the number and its unit, because that whitespace is inside the token and the printer must
    /// reproduce it. Empty for <see cref="TokenKind.EndOfFile"/>.
    /// </value>
    public required string Text { get; init; }

    /// <summary>Gets the trivia before this token, in source order.</summary>
    /// <value>Empty when the token follows the previous one directly.</value>
    public ImmutableArray<Trivia> LeadingTrivia { get; init; } = [];

    /// <summary>Gets the trivia after this token up to the end of its line, in source order.</summary>
    /// <value>Empty when the token is followed directly by another, or by a line break.</value>
    public ImmutableArray<Trivia> TrailingTrivia { get; init; } = [];

    /// <summary>Gets which reserved word this is.</summary>
    /// <value><see cref="ReservedWord.None"/> unless <see cref="Kind"/> is <see cref="TokenKind.Keyword"/>.</value>
    public ReservedWord Keyword { get; init; } = ReservedWord.None;

    /// <summary>Gets the numeric value as written, before any unit conversion.</summary>
    /// <value>
    /// <see langword="null"/> unless <see cref="Kind"/> is <see cref="TokenKind.NumberLiteral"/> or
    /// <see cref="TokenKind.QuantityLiteral"/>. <strong>Not SI.</strong> A quantity's unit may denote either
    /// of two dimensions — <c>kPa</c> is both a pressure and a pressure difference — and which one it
    /// is depends on the parameter it is being read into, so the conversion happens at bind time and
    /// never here.
    /// </value>
    public double? Value { get; init; }

    /// <summary>Gets the numeric part of the token as written.</summary>
    /// <value>
    /// <see langword="null"/> unless <see cref="Kind"/> is <see cref="TokenKind.NumberLiteral"/> or
    /// <see cref="TokenKind.QuantityLiteral"/>. Retained because <c>1.50</c>, <c>1.5</c> and <c>15e-1</c> are
    /// one value and three spellings, and the printer reproduces the one that was written.
    /// </value>
    public string? NumberText { get; init; }

    /// <summary>Gets the unit symbol as written.</summary>
    /// <value>
    /// <see langword="null"/> unless <see cref="Kind"/> is <see cref="TokenKind.QuantityLiteral"/>. The
    /// spelling from the script, not a canonical one.
    /// </value>
    public string? Unit { get; init; }

    /// <summary>Gets a string literal's content, without the quotes.</summary>
    /// <value>
    /// <see langword="null"/> unless <see cref="Kind"/> is <see cref="TokenKind.StringLiteral"/>. There are no
    /// escape sequences in v1, so this is the source slice between the quotes verbatim; for an
    /// unterminated literal it is everything from the opening quote to the end of the line.
    /// </value>
    public string? StringValue { get; init; }

    /// <summary>Returns a short description for a failure message.</summary>
    /// <returns>The kind and the text, for example <c>Quantity "30 kW"</c>.</returns>
    public override string ToString() => $"{Kind} \"{Text}\"";
}
