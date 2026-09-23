using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>
/// The grammar half of <c>D-57</c> and <c>D-58</c>: the <c>curve</c> section, its rows, the
/// <c>design</c> directive, and the <c>at</c> clause that places an observer (<c>D-61</c>).
/// </summary>
public sealed class CurveParsingTests
{
    private static ParseResult Parse(string text) => FluidScriptParser.Parse(new SourceText(text));

    private static ImmutableArray<string> Codes(ParseResult parse) =>
        [.. parse.Diagnostics.Select(static d => d.Code)];

    private const string TwoCurves = """
        fluidscript 1
        curve outdoor time
        0   -1
        60  -3

        curve heating outdoor extrapolated
        -26  50
         20   0
        """;

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveHeaderTakesThreeFixedPositions()
    {
        var parse = Parse(TwoCurves + "\n");

        Assert.Empty(parse.Diagnostics);

        var curves = parse.Root.Statements.OfType<CurveHeaderSyntax>().ToArray();
        Assert.Equal(2, curves.Length);

        Assert.Equal("outdoor", curves[0].Name.Text);
        Assert.Equal("time", curves[0].Driver!.Text);
        Assert.Empty(curves[0].Modifiers);

        Assert.Equal("heating", curves[1].Name.Text);
        Assert.Equal("outdoor", curves[1].Driver!.Text);
        Assert.Equal(["extrapolated"], curves[1].Modifiers.Select(static m => m.Text));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveHeaderEndsThePreviousCurvesSection()
    {
        // Nothing else closes one. Two curves in a row is the ordinary case, and if the second header
        // did not end the first, its rows would be read into the wrong table.
        var parse = Parse(TwoCurves + "\n");

        Assert.Equal(4, parse.Root.Statements.OfType<CurveRowSyntax>().Count());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACircuitHeaderEndsACurveSection()
    {
        var parse = Parse("fluidscript 1\ncurve heating tout\n-26 50\n20 0\n\ncircuit ahu 300\nPU1 pump\n");

        Assert.Empty(parse.Diagnostics);
        Assert.Single(parse.Root.Statements.OfType<ComponentDeclarationSyntax>());
        Assert.Equal(2, parse.Root.Statements.OfType<CurveRowSyntax>().Count());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ARowOutsideACurveSectionSaysWhatItIs()
    {
        // FS1115 rather than "this line did not parse". Two bare values are a statement nowhere else,
        // so the message can name the thing the user wrote.
        var parse = Parse("fluidscript 1\n-26 50\n");

        Assert.Contains("FS1115", Codes(parse));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveWithNoDriverIsReportedAndStillOpensItsSection()
    {
        // The section opens anyway, or every row below would become its own FS1115 and bury the one
        // diagnostic that matters.
        var parse = Parse("fluidscript 1\ncurve heating\n-26 50\n20 0\n");

        Assert.Equal(["FS1116"], Codes(parse));
        Assert.Equal(2, parse.Root.Statements.OfType<CurveRowSyntax>().Count());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveAfterTheFirstCircuitIsOutOfPlace()
    {
        // File-wide, unlike every other section: a curve is read by every circuit that names it.
        var parse = Parse("fluidscript 1\ncircuit ahu 300\ncurve heating tout\n-26 50\n");

        Assert.Contains("FS1112", Codes(parse));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADeclarationInsideACurveSectionIsWrongSection()
    {
        var parse = Parse("fluidscript 1\ncurve heating tout\n-26 50\nPU1 pump\n");

        var diagnostic = Assert.Single(parse.Diagnostics, static d => d.Code == "FS1103");
        Assert.Contains("curve", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ACurveTakesNamedArgumentsAfterItsModifiers()
    {
        var parse = Parse("fluidscript 1\ncurve outdoor time format=\"dd/MM/yyyy HH:mm:ss\"\n0 -1\n60 -3\n");

        Assert.Empty(parse.Diagnostics);

        var curve = Assert.Single(parse.Root.Statements.OfType<CurveHeaderSyntax>());
        var argument = Assert.Single(curve.Arguments);

        Assert.Equal("format", argument.Name.Text);
        Assert.Equal("dd/MM/yyyy HH:mm:ss", Assert.IsType<StringLiteralSyntax>(argument.Value).Token.StringValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignDirectiveTakesNamedValues()
    {
        var parse = Parse("fluidscript 1\ndesign tout=-26\n");

        Assert.Empty(parse.Diagnostics);

        var design = Assert.Single(parse.Root.Statements.OfType<DesignDirectiveSyntax>());
        Assert.Equal("tout", Assert.Single(design.Arguments).Name.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASecondDesignLineIsLegal()
    {
        // Unlike `project` and `spacing`: one driver per line reads better than one long line, and
        // there is nothing for a second to contradict.
        var parse = Parse("fluidscript 1\ndesign tout=-26\ndesign tground=8\n");

        Assert.Empty(parse.Diagnostics);
        Assert.Equal(2, parse.Root.Statements.OfType<DesignDirectiveSyntax>().Count());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AnObserverIsPlacedWithAnAtClause()
    {
        var parse = Parse("fluidscript 1\nTE1 t_sensor at N2\n");

        Assert.Empty(parse.Diagnostics);

        var declaration = Assert.Single(parse.Root.Statements.OfType<ComponentDeclarationSyntax>());
        Assert.Equal("t_sensor", declaration.Kind.Text);
        Assert.Equal("N2", declaration.AttachedTo!.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AtIsStillAnOrdinaryNameElsewhere()
    {
        // `at` is position-classified, not reserved. A component genuinely called `at` keeps working,
        // which is the whole reason it was not reserved.
        var parse = Parse("fluidscript 1\nat pump\n");

        Assert.Empty(parse.Diagnostics);
        Assert.Equal("at", Assert.Single(parse.Root.Statements.OfType<ComponentDeclarationSyntax>()).Name.Text);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("fluidscript 1\ncurve heating tout extrapolated\n-26  50\n 20   0\n")]
    [InlineData("fluidscript 1\ncurve outdoor time format=\"dd/MM/yyyy\"\n01/01/2026 -1\n")]
    [InlineData("fluidscript 1\ndesign tout=-26   # the design day\n")]
    [InlineData("fluidscript 1\nTE1 t_sensor at N2    # on the return\n")]
    [InlineData("fluidscript 1\ncurve heating\n\n-26 50\n")]
    public void EveryNewFormRoundTripsByteForByte(string text)
    {
        // The invariant canvas write-back rests on. A curve row is held as raw tokens precisely so the
        // printer can reproduce a line whose x it never interpreted.
        Assert.Equal(text, SyntaxPrinter.Print(Parse(text)));
    }
}
