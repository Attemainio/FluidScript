using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>
/// How the lexer classifies each shape, one test per acceptance criterion in
/// <c>plan/10-language/12-grammar.md</c>.
/// </summary>
/// <remarks>
/// Word classification is the sharp edge of this language. A word may begin with a digit, so
/// <c>3WV</c> is a name and <c>3K</c> is three kelvin; a unit symbol may be separated from its number
/// by a space, so <c>30 in=20</c> is a number and a parameter rather than thirty inches. Each of those
/// readings differs from its neighbour by a factor or by a whole statement, and none of them fails
/// loudly when it is wrong.
/// </remarks>
public sealed class LexerTests
{
    private static Token[] Significant(string text) =>
        [.. Lexer.Lex(new SourceText(text)).Tokens.Where(static token => token.Kind != TokenKind.EndOfFile)];

    private static Token Only(string text) => Assert.Single(Significant(text));

    [Theory]
    [InlineData("3WV")]
    [InlineData("HE1")]
    [InlineData("PU_MAIN")]
    [InlineData("1exchanger")]
    [InlineData("30kWx")]
    [InlineData("3E1x")]
    [Trait("Category", "Unit")]
    public void AWordThatIsNotAQuantityIsAName(string text)
    {
        var token = Only(text);

        Assert.Equal(TokenKind.Identifier, token.Kind);
        Assert.Equal(text, token.Text);
    }

    [Theory]
    [InlineData("2px", 2, "px")]
    [InlineData("20C", 20, "C")]
    [InlineData("3K", 3, "K")]
    [InlineData("30kW", 30, "kW")]
    [InlineData("30 dK", 30, "dK")]
    [InlineData("4.18 kJ/(kg*K)", 4.18, "kJ/(kg*K)")]
    [InlineData("50 %", 50, "%")]
    [InlineData("1.2e3 W", 1200, "W")]
    [InlineData("60 s", 60, "s")]
    [Trait("Category", "Unit")]
    public void ANumberFollowedByAKnownSymbolIsOneQuantity(string text, double value, string unit)
    {
        var token = Only(text);

        Assert.Equal(TokenKind.QuantityLiteral, token.Kind);
        Assert.Equal(value, token.Value);
        Assert.Equal(unit, token.Unit);

        // The whole token, whitespace included, so the printer can put the spacing back.
        Assert.Equal(text, token.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MaximalMunchTakesTheLongerSymbolWhereItFits()
    {
        // Both kJ/kg and kJ/(kg*K) are in the table, and the shorter one is a prefix of the longer.
        // Taking the prefix would read a specific heat as an enthalpy: the same number, a different
        // dimension, and no error anywhere.
        Assert.Equal("kJ/(kg*K)", Only("4.18 kJ/(kg*K)").Unit);
        Assert.Equal("kJ/kg", Only("125 kJ/kg").Unit);
        Assert.Equal("kWh", Only("30kWh").Unit);
        Assert.Equal("mm", Only("45 mm").Unit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASymbolIsRecognisedOnlyAfterANumber()
    {
        // The one genuine ambiguity in the language: on the first line the characters are a unit, on
        // the second the same characters are operators, and the two readings differ by 4180.
        var quantity = Significant("let cp = 4.18 kJ/(kg*K)");
        Assert.Equal(TokenKind.QuantityLiteral, quantity[^1].Kind);

        var expression = Significant("let mdot = Q / (cp * dT)");
        Assert.DoesNotContain(expression, static token => token.Kind == TokenKind.QuantityLiteral);
        Assert.Equal(
            [TokenKind.Slash, TokenKind.OpenParenthesis, TokenKind.Identifier, TokenKind.Star],
            expression.Skip(4).Take(4).Select(static token => token.Kind));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnEqualsAfterASpacedSymbolMakesItAParameterName()
    {
        // Rule 5's '=' clause, and the whole safety of the whitespace-separated form. Drop it and
        // '30 in' is thirty inches, 'power' loses its value, and the line still parses.
        var tokens = Significant("HE1 heat_exchanger power=30 in=20 out=50");

        Assert.Equal(
            [
                TokenKind.Identifier, TokenKind.Identifier,
                TokenKind.Identifier, TokenKind.Equals, TokenKind.NumberLiteral,
                TokenKind.Identifier, TokenKind.Equals, TokenKind.NumberLiteral,
                TokenKind.Identifier, TokenKind.Equals, TokenKind.NumberLiteral,
            ],
            tokens.Select(static token => token.Kind));

        Assert.Equal("in", tokens[5].Text);
        Assert.Equal(30, tokens[4].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASingleLetterUnitBeforeAnEqualsIsAParameterName()
    {
        // 'l' is the litre and 'd' is the day, and both are plausible parameter names. Without the
        // clause, 'length=25 d=25' would read the second '25' as twenty-five days.
        var tokens = Significant("P1 pipe l=25 d=25 h=2");

        Assert.Equal(3, tokens.Count(static token => token.Kind == TokenKind.NumberLiteral));
        Assert.DoesNotContain(tokens, static token => token.Kind == TokenKind.QuantityLiteral);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PercentIsAUnitAndNeverAnOperator()
    {
        // D-51. There is no modulo operator to lex '%' as, so '50 %' is a quantity everywhere.
        Assert.Equal(TokenKind.QuantityLiteral, Only("50 %").Kind);
        Assert.Equal(0.5, Only("50 %").Value / 100);

        var stranded = Significant("10 % 3");
        Assert.Equal([TokenKind.QuantityLiteral, TokenKind.NumberLiteral], stranded.Select(static t => t.Kind));
    }

    [Theory]
    [InlineData("30.5", 1, 30.5)]
    [InlineData("1.2e3", 1, 1200)]
    [InlineData("1.5e-1", 1, 0.15)]
    [InlineData("1E+2", 1, 100)]
    [Trait("Category", "Unit")]
    public void ANumberIsOneTokenIncludingItsExponent(string text, int count, double value)
    {
        var tokens = Significant(text);

        Assert.Equal(count, tokens.Length);
        Assert.Equal(TokenKind.NumberLiteral, tokens[0].Kind);
        Assert.Equal(value, tokens[0].Value);
        Assert.Equal(text, tokens[0].NumberText);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADotJoinsANumberOnlyWhenADigitFollows()
    {
        // D-51. Without the restriction, maximal munch takes '30.' out of '30..60' and the range
        // production sees one dot where it needs two.
        Assert.Equal(
            [TokenKind.NumberLiteral, TokenKind.DotDot, TokenKind.NumberLiteral],
            Significant("30..60").Select(static token => token.Kind));

        Assert.Equal(
            [TokenKind.NumberLiteral, TokenKind.Dot],
            Significant("30.").Select(static token => token.Kind));

        Assert.Equal([TokenKind.NumberLiteral], Significant("30.5").Select(static token => token.Kind));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AHyphenNeverJoinsTwoWords()
    {
        // Admitting '-' into words would make 'N1-N2' one identifier, which inference rule I1 would
        // silently create a node for: a wrong answer that compiles.
        Assert.Equal(
            [
                TokenKind.NumberLiteral, TokenKind.Minus, TokenKind.Identifier,
                TokenKind.Minus, TokenKind.Identifier,
            ],
            Significant("3-way-valve").Select(static token => token.Kind));

        Assert.Equal(
            [TokenKind.Identifier, TokenKind.Minus, TokenKind.Identifier],
            Significant("N1-N2").Select(static token => token.Kind));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PatternTokensAreLeftForTheStyleParserToRecombine()
    {
        // '--', '..' and '-.' are one pattern token each to the style directive and two tokens here.
        // Recombining them is a parser concern, contained to one production on purpose.
        Assert.Equal(
            [TokenKind.Minus, TokenKind.Minus],
            Significant("--").Select(static token => token.Kind));
        Assert.Equal([TokenKind.DotDot], Significant("..").Select(static token => token.Kind));
        Assert.Equal(
            [TokenKind.Minus, TokenKind.Dot],
            Significant("-.").Select(static token => token.Kind));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryReservedWordLexesAsAKeyword()
    {
        foreach (var word in ReservedWords.All)
        {
            var token = Only(word);
            Assert.Equal(TokenKind.Keyword, token.Kind);
            Assert.Equal(word, ReservedWords.TextOf(token.Keyword));
        }

        // Kinds are not reserved: reserving 'node' and 'pipe' once made both reference circuits
        // unparseable, because neither could reach kind-name position.
        Assert.Equal(TokenKind.Identifier, Only("node").Kind);
        Assert.Equal(TokenKind.Identifier, Only("pipe").Kind);
        Assert.Equal(TokenKind.Identifier, Only("water").Kind);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AHashMakesTheRestOfTheLineTrivia()
    {
        var result = Lexer.Lex(new SourceText("let a = 1 # 2 + 3\nlet b = 4\n"));
        var numbers = result.Tokens.Where(static token => token.Kind == TokenKind.NumberLiteral).ToArray();

        Assert.Equal([1d, 4d], numbers.Select(static token => token.Value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AHashInsideAStringIsNotAComment()
    {
        // D-13's whole point: '#' begins a comment, so a hex colour is written quoted. If the string
        // scan did not win, 'style "#2f6f9f" 2px' would be a style directive with no tokens at all --
        // legal, silent, and rendered in the default colour.
        var tokens = Significant("""style "#2f6f9f" 2px""");

        Assert.Equal(
            [TokenKind.Keyword, TokenKind.StringLiteral, TokenKind.QuantityLiteral],
            tokens.Select(static token => token.Kind));
        Assert.Equal("#2f6f9f", tokens[1].StringValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnterminatedStringStopsAtTheLineBreakAndReportsFS1001()
    {
        var result = Lexer.Lex(new SourceText("style \"unclosed\nlet a = 1\n"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FS1001", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // The rest of the file still lexes, which is the point of stopping at the line break.
        Assert.Contains(result.Tokens, static token => token.Keyword == ReservedWord.Let);
    }

    [Theory]
    [InlineData("!")]
    [InlineData(";")]
    [InlineData("|")]
    [InlineData("&")]
    [InlineData("[")]
    [InlineData("é")]
    [Trait("Category", "Unit")]
    public void AnUnusedCharacterIsReportedAsFS1002AndStillHeldAsAToken(string text)
    {
        var result = Lexer.Lex(new SourceText(text));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FS1002", diagnostic.Code);
        Assert.Contains(text, diagnostic.Message, StringComparison.Ordinal);

        // Held rather than dropped: a character that reached neither a token nor a trivium would
        // vanish from a script the user is still editing.
        Assert.Equal(TokenKind.Unknown, result.Tokens[0].Kind);
        Assert.Equal(text, result.Tokens[0].Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheVerticalBarIsUnallocatedAndSaysSo()
    {
        // 12 leaves '|' free deliberately, so that reclaiming it later for a table or pipeline form is
        // a non-breaking addition. Until then it is FS1002 like any other unused character.
        var result = Lexer.Lex(new SourceText("|"));
        Assert.Equal("FS1002", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACatalogVersionArrivesAsOneNumber()
    {
        // Not '2026', '.', '1'. The lexer has no context to do otherwise, and the parser splits the
        // major and minor out of the source text -- which is also the only way '@2026.10' stays
        // distinguishable from '@2026.1'.
        var tokens = Significant("catalog steel_en10255@2026.10");

        Assert.Equal(
            [TokenKind.Keyword, TokenKind.Identifier, TokenKind.At, TokenKind.NumberLiteral],
            tokens.Select(static token => token.Kind));
        Assert.Equal("2026.10", tokens[3].NumberText);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APortQualifiedEndpointIsThreeTokens()
    {
        Assert.Equal(
            [TokenKind.Identifier, TokenKind.Dot, TokenKind.Identifier],
            Significant("3WV.b").Select(static token => token.Kind));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheSampleScriptsProduceNoLexicalDiagnostic()
    {
        // Losslessness holds for any text at all; being clean is a property only of text that is meant
        // to be a script, which is why this one is scoped to samples/ rather than the whole corpus.
        foreach (var sample in ScriptCorpus.Samples())
        {
            var result = Lexer.Lex(new SourceText(sample.Text));
            Assert.True(
                result.Diagnostics.IsEmpty,
                $"{sample.Name}: {string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"))}");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheSyntaxTourReachesEveryTokenKind()
    {
        // The tour exists to exercise every production; this is the assertion that notices when it
        // stops doing so, which is otherwise invisible until something downstream is untested.
        var tour = ScriptCorpus.Samples().Single(static s => s.Name.EndsWith("m1-syntax-tour.fluid", StringComparison.Ordinal));
        var kinds = Lexer.Lex(new SourceText(tour.Text)).Tokens.Select(static token => token.Kind).ToHashSet();

        var missing = Enum.GetValues<TokenKind>()
            .Where(kind => kind != TokenKind.Unknown && !kinds.Contains(kind))
            .ToArray();

        Assert.True(missing.Length == 0, $"The tour never produces: {string.Join(", ", missing)}");
        Assert.DoesNotContain(TokenKind.Unknown, kinds);
    }
}
