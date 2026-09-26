using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests.Language.Syntax.Lexing;

/// <summary>
/// Where the lexer reads language 2's text differently from what its characters alone suggest
/// (<c>plan/10-language/19-fluidscript-2.md</c> §Values, units, lists and ranges).
/// </summary>
public sealed class Language2LexerTests
{
    private static Token[] Significant(string text) =>
        [.. Lexer.Lex(new SourceText(text)).Tokens.Where(static token => token.Kind != TokenKind.EndOfFile)];

    private static string Shape(string text) =>
        string.Join(" ", Significant(text).Select(static token => $"{token.Kind}:{token.Text}"));

    [Fact]
    [Trait("Category", "Unit")]
    public void TIsANameBeforeASpacedEquals()
    {
        // The review's measured case: without the rule this is three hundred tonnes and a stray `=`.
        Assert.Equal(
            "Identifier:p Equals:= NumberLiteral:300 Identifier:t Equals:= NumberLiteral:6",
            Shape("p = 300 t = 6"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnHourBeforeASpacedEqualsIsTheNextParameter()
    {
        // `h` is an hour and an enthalpy; `19` keeps the rule that a unit never precedes `=`, past spaces.
        Assert.Equal(
            "Identifier:flow Equals:= NumberLiteral:5 Identifier:h Equals:= NumberLiteral:2000",
            Shape("flow = 5 h = 2000"));

        Assert.Equal(
            "Identifier:duration Equals:= QuantityLiteral:2 h",
            Shape("duration = 2 h"));
    }

    [Theory]
    [InlineData("30 in")]
    [InlineData("30 t")]
    [Trait("Category", "Unit")]
    public void InchAndTonneAreNotUnits(string text)
    {
        var tokens = Significant(text);
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
        var token = Assert.Single(Significant(text));

        Assert.Equal(TokenKind.DateLiteral, token.Kind);
        Assert.Equal(text, token.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADateRowKeepsItsValueApart()
    {
        // A curve of time: the date, then the value, which is a separate number.
        Assert.Equal(
            "DateLiteral:2026-01-15 06:00 Minus:- NumberLiteral:18",
            Shape("2026-01-15 06:00   -18"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStatementWordIsAnIdentifier()
    {
        // The lexer reserves nothing; the parser knows `circuit` by where it stands.
        Assert.Equal(TokenKind.Identifier, Significant("circuit")[0].Kind);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortQuantityIsNotAnInch()
    {
        Assert.Equal(
            "Identifier:primary Dot:. Identifier:in Dot:. Identifier:t Equals:= QuantityLiteral:45 C",
            Shape("primary.in.t = 45 C"));
    }
}
