using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>The line index every diagnostic is eventually shown against.</summary>
/// <remarks>
/// Worth its own tests because the failure is quiet: an off-by-one in the line index puts every
/// squiggle in the editor one line away from the thing it is about, and nothing else notices.
/// </remarks>
public sealed class SourceTextTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void EmptyTextHasOneLine()
    {
        var source = new SourceText(string.Empty);

        Assert.Equal(1, source.LineCount);
        Assert.Equal(new LinePosition(0, 0), source.GetLinePosition(0));
    }

    [Theory]
    [InlineData("a\nb", 3)]
    [InlineData("a\r\nb", 3)]
    [InlineData("a\rb", 3)]
    [InlineData("a\n\nb", 4)]
    [Trait("Category", "Unit")]
    public void EveryLineEndingConventionIsIndexed(string text, int lines)
    {
        // A script may be edited on either platform and pasted from a third; the printer reproduces
        // whichever ending was there, so the index has to recognise all three.
        Assert.Equal(lines - 1, new SourceText(text).LineCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APositionResolvesToItsLineAndColumn()
    {
        var source = new SourceText("fluidscript 1\ncircuit a\nlet x = 1\n");

        Assert.Equal(new LinePosition(0, 0), source.GetLinePosition(0));
        Assert.Equal(new LinePosition(0, 12), source.GetLinePosition(12));
        Assert.Equal(new LinePosition(1, 0), source.GetLinePosition(14));
        Assert.Equal(new LinePosition(2, 4), source.GetLinePosition(28));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheEndOfTheTextHasAPosition()
    {
        // An empty span at the end of the file is where a diagnostic about something missing points,
        // so Length itself must resolve rather than throw.
        var source = new SourceText("abc");
        Assert.Equal(new LinePosition(0, 3), source.GetLinePosition(3));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APositionOutsideTheTextThrows()
    {
        // A stage that computes a position outside the text has a bug; a script cannot cause this,
        // so it is the one place in the pipeline where throwing is right.
        var source = new SourceText("abc");

        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLinePosition(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLinePosition(4));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TextIsKeptExactly()
    {
        const string Text = "  fluidscript 1  \r\n\r\n";
        var source = new SourceText(Text);

        Assert.Equal(Text, source.Text);
        Assert.Equal(Text, source.ToString());
        Assert.Equal("fluidscript", source.ToString(new TextSpan(2, 11)));
    }
}
