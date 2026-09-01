using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Tests.Diagnostics;

public sealed class TextSpanTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void End_IsOnePastTheLastCharacter()
    {
        var span = new TextSpan(start: 12, length: 5);

        Assert.Equal(17, span.End);
        Assert.False(span.IsEmpty);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromBounds_ProducesTheHalfOpenInterval()
    {
        var span = TextSpan.FromBounds(12, 17);

        Assert.Equal(new TextSpan(12, 5), span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromBounds_EndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSpan.FromBounds(17, 12));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(-1, 5)]
    [InlineData(5, -1)]
    public void Constructor_NegativeArgument_Throws(int start, int length)
    {
        // A negative offset is a stage miscomputing a span, never a script -- so it throws rather
        // than clamping, and the throw names which argument was wrong.
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(start, length));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_LengthThatWouldOverflowTheEnd_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(int.MaxValue - 3, 4));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EmptySpan_IsAPositionRatherThanARange()
    {
        var caret = new TextSpan(start: 12, length: 0);

        Assert.True(caret.IsEmpty);
        Assert.Equal(12, caret.End);

        // A caret contains no position at all, not even the one it sits on. A diagnostic about a
        // missing token points at where the token should go, and nothing is under it.
        Assert.False(caret.Contains(12));
        Assert.False(caret.OverlapsWith(new TextSpan(10, 5)));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void Contains_TreatsTheEndAsExclusive(int position, bool expected)
    {
        Assert.Equal(expected, new TextSpan(12, 5).Contains(position));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Contains_NestedSpan_IsTrueIncludingTheEdges()
    {
        var outer = new TextSpan(10, 10);

        Assert.True(outer.Contains(new TextSpan(10, 10)));
        Assert.True(outer.Contains(new TextSpan(14, 2)));
        Assert.False(outer.Contains(new TextSpan(14, 7)));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OverlapsWith_TouchingSpans_DoNotOverlap()
    {
        // Adjacent tokens share a boundary offset and must not be reported as overlapping, or every
        // pair of neighbours in a script would be.
        Assert.False(new TextSpan(10, 5).OverlapsWith(new TextSpan(15, 5)));
        Assert.True(new TextSpan(10, 5).OverlapsWith(new TextSpan(14, 5)));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_StatesTheIntervalConvention()
    {
        Assert.Equal("[12..17)", new TextSpan(12, 5).ToString());
    }
}
