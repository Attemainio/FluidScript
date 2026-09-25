using System.Collections.Immutable;

namespace FluidScript.Core.Language.Syntax.Lexing;

/// <summary>What the lexer does differently for one language major.</summary>
/// <remarks>
/// <para>
/// Language 2 (<c>plan/10-language/19-fluidscript-2.md</c>, <c>D-164</c>) lexes the same characters into
/// the same tokens as language 1 in all but four places, so it is the same lexer with four switches
/// rather than a second lexer that would drift from the first. <see cref="Language1"/> is every switch at
/// its original position, which is what keeps language 1 byte-for-byte unchanged.
/// </para>
/// </remarks>
public sealed record LexerOptions
{
    /// <summary>Gets the options language 1 is lexed with.</summary>
    /// <value>Reserved words recognised, every unit symbol, an <c>=</c> only when it touches, no dates.</value>
    public static LexerOptions Language1 { get; } = new();

    /// <summary>Gets the options language 2 is lexed with.</summary>
    /// <value>
    /// No reserved words (the parser recognises statement words by position), <c>in</c> and <c>t</c> not
    /// units, an <c>=</c> past spaces ending a unit, and dates and clock times lexed whole.
    /// </value>
    public static LexerOptions Language2 { get; } = new()
    {
        ReservesWords = false,
        ExcludedUnitSymbols = ["in", "t"],
        UnitStopsBeforeSpacedEquals = true,
        LexesDates = true,
    };

    /// <summary>Gets a value indicating whether a reserved spelling becomes a <see cref="TokenKind.Keyword"/>.</summary>
    /// <value>
    /// <see langword="true"/> for language 1. Language 2 reserves nothing in the lexer: <c>fluid</c>,
    /// <c>style</c> and <c>inlet</c> are setting and kind names there, and its six statement words are
    /// recognised by position (<c>19</c> §Lines, blocks and names).
    /// </value>
    public bool ReservesWords { get; init; } = true;

    /// <summary>Gets the unit symbols this major does not recognise after a number.</summary>
    /// <value>Empty for language 1; <c>in</c> (inch) and <c>t</c> (tonne) for language 2. Ordinal spellings.</value>
    public ImmutableArray<string> ExcludedUnitSymbols { get; init; } = [];

    /// <summary>Gets a value indicating whether an <c>=</c> separated from a unit by spaces still ends it.</summary>
    /// <value>
    /// <see langword="false"/> for language 1, where an <c>=</c> touches its name. <see langword="true"/> for
    /// language 2, which writes <c>t = 6</c>: without it <c>p = 300 t = 6</c> reads three hundred tonnes.
    /// </value>
    public bool UnitStopsBeforeSpacedEquals { get; init; }

    /// <summary>Gets a value indicating whether a date or a clock time lexes as one <see cref="TokenKind.DateLiteral"/>.</summary>
    /// <value>
    /// <see langword="false"/> for language 1, which quotes a date because <c>2026-01-15</c> is arithmetic
    /// there. <see langword="true"/> for language 2.
    /// </value>
    public bool LexesDates { get; init; }
}
