using FluidScript.Core.Language.Syntax.Lexing;
using FluidScript.Core.Language.Syntax.Printing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Language.Syntax.Printing;

/// <summary>
/// The formatter's layout (<c>17</c>): what it aligns, what it never touches, and that formatting
/// twice is formatting once.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FormatterTests
{
    /// <summary>The version line these examples need: the layout is language 1's, and a file without one is language 2 (<c>L-76</c>).</summary>
    private const string V1 = "fluidscript 1\n";

    [Fact]
    public void ItIsIdempotentOverTheCorpus()
    {
        // 17's acceptance row: Format(Format(x)) == Format(x), over every sample and every block.
        foreach (var (name, text, _) in ScriptCorpus.All())
        {
            var once = Formatter.FormatText(text);
            var twice = Formatter.FormatText(once);

            Assert.True(string.Equals(once, twice, StringComparison.Ordinal), $"{name}: formatting twice differs from formatting once");
            Assert.Empty(Formatter.Format(new SourceText(once)));
        }
    }

    [Fact]
    public void ItChangesNoTokenAndNoCommentOverTheCorpus()
    {
        foreach (var (name, text, _) in ScriptCorpus.All())
        {
            var before = Lexer.Lex(new SourceText(text));
            var after = Lexer.Lex(new SourceText(Formatter.FormatText(text)));

            Assert.Equal(
                before.Tokens.Select(static t => (t.Kind, t.Text)),
                after.Tokens.Select(static t => (t.Kind, t.Text)));
            Assert.Equal(Comments(before, text), Comments(after, Formatter.FormatText(text)));
        }

        static IEnumerable<string> Comments(LexResult lexed, string text) =>
            lexed.Tokens.SelectMany(static t => t.LeadingTrivia.Concat(t.TrailingTrivia))
                .Where(static t => t.Kind == TriviaKind.Comment)
                .Select(t => text.Substring(t.Span.Start, t.Span.Length));
    }

    [Fact]
    public void ItAlignsTrailingCommentsWithinARunAndNotAcrossABlankLine()
    {
        const string Text = V1 + "HE1 heat_exchanger power=30   # coil\n3WV three_way_valve # valve\n\nPU1 pump # pump\n";

        var formatted = Formatter.FormatText(Text);

        Assert.Equal(V1 + "HE1 heat_exchanger power=30  # coil\n3WV three_way_valve          # valve\n\nPU1 pump  # pump\n", formatted);
    }

    [Fact]
    public void ItPadsConsecutiveLetsSoTheEqualsSignsLineUp()
    {
        const string Text = V1 + "let dT = 30 dK\nlet margin=2 kW\nlet Q   =   30 kW\n";

        Assert.Equal(V1 + "let dT     = 30 dK\nlet margin = 2 kW\nlet Q      = 30 kW\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItRemovesSpacesAroundAParametersEqualsAndKeepsAQuantitysInnerSpace()
    {
        const string Text = V1 + "   HE1   heat_exchanger  power = 30 kW   in.t=20\n";

        Assert.Equal(V1 + "HE1 heat_exchanger power=30 kW in.t=20\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItKeepsTheWritersSpacingInsideAnExpressionCollapsedToOne()
    {
        const string Text = V1 + "let a = Q/(cp*dT)\nlet b = Q  /  ( cp * dT )\nlet c = max( Q ,24 kW )\n";

        Assert.Equal(V1 + "let a = Q/(cp*dT)\nlet b = Q / (cp * dT)\nlet c = max(Q, 24 kW)\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItLeavesBlankLinesFullLineCommentsAndCurveRowsExactlyAsWritten()
    {
        const string Text = V1 + "# a comment   with   spacing\n\n\ncurve heating outdoor\n-26   50\n  0   30\n\nlet x=1\n";

        Assert.Equal(V1 + "# a comment   with   spacing\n\n\ncurve heating outdoor\n-26   50\n  0   30\n\nlet x = 1\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItReturnsOneEditPerChangedLineAtThatLinesSpan()
    {
        const string Text = V1 + "HE1 heat_exchanger power=30\nPU1   pump\n";

        var edit = Assert.Single(Formatter.Format(new SourceText(Text)));

        Assert.Equal(V1.Length + 28, edit.Span.Start);
        Assert.Equal("PU1   pump".Length, edit.Span.Length);
        Assert.Equal("PU1 pump", edit.NewText);
    }

    [Theory]
    [InlineData("fluidscript 2\ncircuit \"c\":\n  P1   pump\n  P1 - P2\n")]
    [InlineData("fluidscript 1\nfluidscript 2\n  P1   pump\n")]
    [InlineData("circuit \"c\":\n  P1   pump\n")]
    [Trait("Category", "Unit")]
    public void ItLeavesAFileOfAnotherMajorAsWritten(string text)
    {
        // Language 1's first rule removes leading indentation, which is language 2's block structure.
        Assert.Empty(Formatter.Format(new SourceText(text)));
    }
}
