using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;

namespace FluidScript.Core.Tests.Binding;

/// <summary>Binding step 8b: heights propagate through everything but a pipe (<c>D-70</c>, <c>D-95</c>).</summary>
public sealed class HeightBindingTests
{
    private const string RoofLoop = """
        fluidscript 1
        circuit heating
        fluid water

        HE1  heat_exchanger power=30 in.t=20 out.t=50
        LOAD heat_exchanger power=-30 dp=0 elevation=32
        CV1  valve
        PU1  pump
        P1   pipe length=32
        P2   pipe length=32

        connections
        N1 - PU1 - N2 - HE1 - N3 - P1 - N4 - LOAD - N5 - CV1 - N6 - P2 - N1
        """;

    private static BindResult Bind(string text) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText(text)));

    private static SemanticModel Model(string text)
    {
        var result = Bind(text);

        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        return result.Model;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AStatedHeightSpreadsToEverythingWiredToItWithoutAPipe()
    {
        // The load is on the roof, so the nodes either side of it and the valve after it are too;
        // the pipes are what come back down, and the plant room reads 0 because nothing placed it.
        var heights = Model(RoofLoop).Heights;

        Assert.Equal(32, heights.Of("LOAD"));
        Assert.Equal(32, heights.Of("N4"));
        Assert.Equal(32, heights.Of("N5"));
        Assert.Equal(32, heights.Of("CV1"));
        Assert.Equal(32, heights.Of("N6"));
        Assert.Equal(0, heights.Of("PU1"));
        Assert.Equal(0, heights.Of("N1"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APipesRiseIsTheDifferenceOfWhatItConnects()
    {
        var heights = Model(RoofLoop).Heights;

        Assert.Equal(0, heights.Of("P1", "in"));
        Assert.Equal(32, heights.Of("P1", "out"));
        Assert.Equal(32, heights.Rise("P1"));
        Assert.Equal(-32, heights.Rise("P2"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AScriptWithNoHeightReadsZeroEverywhere()
    {
        var heights = Model(RoofLoop.Replace(" elevation=32", string.Empty, StringComparison.Ordinal)).Heights;

        Assert.Empty(heights.Heights);
        Assert.Equal(0, heights.Rise("P1"));
        Assert.Equal(0, heights.Of("LOAD"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FS2219_TwoStatedHeightsMeetingWithoutAPipeAreAMissingRiser()
    {
        // The valve says plant room, the load says roof, and N5 joins them directly. The later
        // declaration carries the diagnostic and both names are in it.
        var result = Bind(RoofLoop.Replace("CV1  valve", "CV1  valve elevation=0", StringComparison.Ordinal));

        var missing = Assert.Single(result.Diagnostics, static d => d.Code == "FS2219");

        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
        Assert.Equal(
            "'CV1' at 0 m is wired directly to 'LOAD' at 32 m. Put a pipe between them, or give them one height.",
            missing.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheSameHeightStatedTwiceIsNotAConflict()
    {
        var result = Bind(RoofLoop.Replace("CV1  valve", "CV1  valve elevation=32", StringComparison.Ordinal));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS2219");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABareConnectionBetweenNodesSpansHeightsLikeAPipe()
    {
        // N5 - N6 is D-25's ideal link. It does not join the two heights: N5 stays on the roof with
        // the load and N6 in the plant room with the pump, and the link carries the 32 m itself.
        var heights = Model("""
            fluidscript 1
            circuit heating
            fluid water

            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30 dp=0 elevation=32
            CV1  valve
            PU1  pump
            P1   pipe length=32
            P2   pipe length=32

            connections
            N1 - PU1 - N2 - HE1 - N3 - P1 - N4 - LOAD - N5
            N5 - N6
            N6 - CV1 - N7 - P2 - N1
            """).Heights;

        Assert.Equal(32, heights.Of("N5"));
        Assert.Equal(0, heights.Of("N6"));
        Assert.Equal(0, heights.Rise("P2"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADeclaredNodePlacesItselfAndAPipeHasNoHeightOfItsOwn()
    {
        var result = Bind("""
            fluidscript 1
            circuit heating
            fluid water

            P1   pipe length=10 dn=25 elevation=10

            connections
            N1 - P1 - N2

            N1 inlet t=20 p=300
            N2 outlet p=150 elevation=10
            """);

        // `elevation` on a pipe is the parameter the registry no longer has (D-70).
        var unknown = Assert.Single(result.Diagnostics, static d => d.Code == "FS1503");
        Assert.Contains("'elevation'", unknown.Message, StringComparison.Ordinal);

        Assert.Equal(10, result.Model.Heights.Of("N2"));
        Assert.Equal(10, result.Model.Heights.Rise("P1"));
    }
}
