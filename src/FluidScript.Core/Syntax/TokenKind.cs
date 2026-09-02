namespace FluidScript.Core.Syntax;

/// <summary>What one token is.</summary>
/// <remarks>
/// The set is exactly <c>plan/10-language/12-grammar.md</c>'s <c>token</c> production, plus
/// <see cref="EndOfFile"/> and <see cref="Unknown"/>. There is no <c>Percent</c>: <c>%</c> is a unit
/// symbol and the language has no modulo operator (<c>D-51</c>).
/// </remarks>
public enum TokenKind
{
    /// <summary>The zero-length token at the end of the text.</summary>
    /// <remarks>
    /// Always emitted, and always last. It is where trailing trivia at the end of a file lives, which
    /// is what makes losslessness hold for a script ending in a comment or a blank line.
    /// </remarks>
    EndOfFile = 1,

    /// <summary>A word that is not reserved: a component name, a kind name, a parameter name.</summary>
    Identifier,

    /// <summary>One of the reserved words. See <see cref="Token.Keyword"/> for which.</summary>
    Keyword,

    /// <summary>A number with no unit symbol.</summary>
    NumberLiteral,

    /// <summary>A number and the unit symbol that follows it, attached or separated by spaces.</summary>
    QuantityLiteral,

    /// <summary>A double-quoted string. Never spans a line.</summary>
    StringLiteral,

    /// <summary>The <c>=</c> that separates a parameter name from its value.</summary>
    Equals,

    /// <summary>A <c>-</c>: subtraction, unary minus, the connection operator, or a line pattern.</summary>
    Minus,

    /// <summary>A <c>+</c>.</summary>
    Plus,

    /// <summary>A <c>*</c>.</summary>
    Star,

    /// <summary>A <c>/</c>. Never part of a unit symbol: a symbol is matched whole, after a number.</summary>
    Slash,

    /// <summary>A single <c>.</c>: port qualification, or part of a line pattern.</summary>
    Dot,

    /// <summary>A <c>..</c>: a range, or the dotted line pattern.</summary>
    DotDot,

    /// <summary>A <c>,</c>, which occurs only between a function call's arguments.</summary>
    Comma,

    /// <summary>An opening parenthesis.</summary>
    OpenParenthesis,

    /// <summary>A closing parenthesis.</summary>
    CloseParenthesis,

    /// <summary>An <c>@</c>, which occurs only before a catalogue version.</summary>
    At,

    /// <summary>A colon, which separates the fields of a clock time.</summary>
    /// <remarks>
    /// It has no meaning in any expression and appears in no production. It exists so that a curve
    /// row's timestamp <em>lexes</em> — a row keeps its raw tokens and the binder reads the text
    /// (<c>D-60</c>), so all the lexer has to do is not reject the character. Anywhere else a colon
    /// reaches a parser with no place for it, and the line is <c>FS1114</c>.
    /// </remarks>
    Colon,

    /// <summary>A character the language does not use.</summary>
    /// <remarks>
    /// Carried as a token rather than dropped, because dropping it would break losslessness: the
    /// printer reproduces the source from the tokens, and a character that reached neither a token nor
    /// a trivium would silently vanish from a script the user is still editing. It is reported as
    /// <c>FS1002</c>.
    /// </remarks>
    Unknown,
}
