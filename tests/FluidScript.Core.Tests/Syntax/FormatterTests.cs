using FluidScript.Core.Syntax;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>
/// The formatter's layout (<c>17</c>): what it aligns, what it never touches, and that formatting
/// twice is formatting once.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FormatterTests
{
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
        const string Text = "HE1 heat_exchanger power=30   # coil\n3WV three_way_valve # valve\n\nPU1 pump # pump\n";

        var formatted = Formatter.FormatText(Text);

        Assert.Equal("HE1 heat_exchanger power=30  # coil\n3WV three_way_valve          # valve\n\nPU1 pump  # pump\n", formatted);
    }

    [Fact]
    public void ItPadsConsecutiveLetsSoTheEqualsSignsLineUp()
    {
        const string Text = "let dT = 30 dK\nlet margin=2 kW\nlet Q   =   30 kW\n";

        Assert.Equal("let dT     = 30 dK\nlet margin = 2 kW\nlet Q      = 30 kW\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItRemovesSpacesAroundAParametersEqualsAndKeepsAQuantitysInnerSpace()
    {
        const string Text = "   HE1   heat_exchanger  power = 30 kW   in=20\n";

        Assert.Equal("HE1 heat_exchanger power=30 kW in=20\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItKeepsTheWritersSpacingInsideAnExpressionCollapsedToOne()
    {
        const string Text = "let a = Q/(cp*dT)\nlet b = Q  /  ( cp * dT )\nlet c = max( Q ,24 kW )\n";

        Assert.Equal("let a = Q/(cp*dT)\nlet b = Q / (cp * dT)\nlet c = max(Q, 24 kW)\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItLeavesBlankLinesFullLineCommentsAndCurveRowsExactlyAsWritten()
    {
        const string Text = "# a comment   with   spacing\n\n\ncurve heating outdoor\n-26   50\n  0   30\n\nlet x=1\n";

        Assert.Equal("# a comment   with   spacing\n\n\ncurve heating outdoor\n-26   50\n  0   30\n\nlet x = 1\n", Formatter.FormatText(Text));
    }

    [Fact]
    public void ItReturnsOneEditPerChangedLineAtThatLinesSpan()
    {
        const string Text = "HE1 heat_exchanger power=30\nPU1   pump\n";

        var edit = Assert.Single(Formatter.Format(new SourceText(Text)));

        Assert.Equal(28, edit.Span.Start);
        Assert.Equal("PU1   pump".Length, edit.Span.Length);
        Assert.Equal("PU1 pump", edit.NewText);
    }
}
