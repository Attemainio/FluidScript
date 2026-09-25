using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests.Language.Syntax.Lexing;

/// <summary>
/// What language 2's lexer options change (<c>plan/10-language/19-fluidscript-2.md</c> §Values, units, lists
/// and ranges), each against language 1's reading of the same text, so a difference is shown as a
/// difference rather than asserted in isolation.
/// </summary>
public sealed class Language2LexerTests
{
    private static Token[] Significant(string text, LexerOptions options) =>
        [.. Lexer.Lex(new SourceText(text), options).Tokens.Where(static token => token.Kind != TokenKind.EndOfFile)];

    private static string Shape(string text, LexerOptions options) =>
        string.Join(" ", Significant(text, options).Select(static token => $"{token.Kind}:{token.Text}"));

    [Fact]
    [Trait("Category", "Unit")]
    public void TIsANameBeforeASpacedEquals()
    {
        // The review's measured case: language 1 reads three hundred tonnes and a stray `=`.
        Assert.Equal(
            "Identifier:p Equals:= QuantityLiteral:300 t Equals:= NumberLiteral:6",
            Shape("p = 300 t = 6", LexerOptions.Language1));

        Assert.Equal(
            "Identifier:p Equals:= NumberLiteral:300 Identifier:t Equals:= NumberLiteral:6",
            Shape("p = 300 t = 6", LexerOptions.Language2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnHourBeforeASpacedEqualsIsTheNextParameter()
    {
        // `h` is an hour and an enthalpy; `19` keeps the rule that a unit never precedes `=`, past spaces.
        Assert.Equal(
            "Identifier:flow Equals:= NumberLiteral:5 Identifier:h Equals:= NumberLiteral:2000",
            Shape("flow = 5 h = 2000", LexerOptions.Language2));

        Assert.Equal(
            "Identifier:duration Equals:= QuantityLiteral:2 h",
            Shape("duration = 2 h", LexerOptions.Language2));
    }

    [Theory]
    [InlineData("30 in")]
    [InlineData("30 t")]
    [Trait("Category", "Unit")]
    public void InchAndTonneAreNotUnits(string text)
    {
        Assert.Equal(TokenKind.QuantityLiteral, Assert.Single(Significant(text, LexerOptions.Language1)).Kind);

        var tokens = Significant(text, LexerOptions.Language2);
        Assert.Equal([TokenKind.NumberLiteral, TokenKind.Identifier], tokens.Select(static token => token.Kind));
    }

    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("2026-01-15 06:00")]
    [InlineData("2026-01-15T06:00")]
    [InlineData("2026-01-15 06:00:30")]
    [InlineData("06:30")]
    [Trait("Category", "Unit")]
    public void ADateIsOneToken(string text)
    {
        var token = Assert.Single(Significant(text, LexerOptions.Language2));

        Assert.Equal(TokenKind.DateLiteral, token.Kind);
        Assert.Equal(text, token.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LanguageOneStillReadsADateAsArithmetic()
    {
        Assert.DoesNotContain(
            Significant("2026-01-15", LexerOptions.Language1),
            static token => token.Kind == TokenKind.DateLiteral);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADateRowKeepsItsValueApart()
    {
        // A curve of time: the date, then the value, which is a separate number.
        Assert.Equal(
            "DateLiteral:2026-01-15 06:00 Minus:- NumberLiteral:18",
            Shape("2026-01-15 06:00   -18", LexerOptions.Language2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStatementWordIsAnIdentifier()
    {
        // Language 2 reserves nothing in the lexer; the parser knows `circuit` by where it stands.
        Assert.Equal(TokenKind.Keyword, Significant("circuit", LexerOptions.Language1)[0].Kind);
        Assert.Equal(TokenKind.Identifier, Significant("circuit", LexerOptions.Language2)[0].Kind);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortQuantityIsNotAnInch()
    {
        Assert.Equal(
            "Identifier:primary Dot:. Identifier:in Dot:. Identifier:t Equals:= QuantityLiteral:45 C",
            Shape("primary.in.t = 45 C", LexerOptions.Language2));
    }
}
