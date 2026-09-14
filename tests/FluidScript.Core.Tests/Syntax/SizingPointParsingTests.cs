using FluidScript.Core.Syntax;
using FluidScript.Core.Syntax.Ast;

namespace FluidScript.Core.Tests.Syntax;

/// <summary>The <c>sized_at</c> clause on a component declaration (<c>D-94</c>).</summary>
public sealed class SizingPointParsingTests
{
    private static ParseResult Parse(string text) => FluidScriptParser.Parse(new SourceText(text));

    [Fact]
    [Trait("Category", "Unit")]
    public void AClauseAfterTheParametersNamesTheComponentsOwnPoint()
    {
        var parse = Parse("fluidscript 1\nHP1 heater power=heating sized_at tout=-5\n");

        Assert.Empty(parse.Diagnostics);

        var declaration = Assert.Single(parse.Root.Statements.OfType<ComponentDeclarationSyntax>());
        Assert.Equal("power", Assert.Single(declaration.Parameters).Name.Text);
        Assert.NotNull(declaration.SizedAtKeyword);

        var point = Assert.Single(declaration.SizingPoint);
        Assert.Equal("tout", point.Name.Text);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheClauseMayNameMoreThanOneDriver()
    {
        var parse = Parse("fluidscript 1\nHP1 heater power=heating sized_at tout=-5 tground=8\n");

        Assert.Empty(parse.Diagnostics);

        var declaration = Assert.Single(parse.Root.Statements.OfType<ComponentDeclarationSyntax>());
        Assert.Equal(["tout", "tground"], declaration.SizingPoint.Select(static p => p.Name.Text));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AClauseWithNoValueIsTheSameErrorAParameterWithoutOneIs()
    {
        var parse = Parse("fluidscript 1\nHP1 heater power=heating sized_at\n");

        Assert.Contains(parse.Diagnostics, static d => d.Code == "FS1105");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("fluidscript 1\nHP1 heater power=heating sized_at tout=-5\n")]
    [InlineData("fluidscript 1\nHP1  heater  power=heating   sized_at   tout=-5  # bivalent\n")]
    [InlineData("fluidscript 1\nHP1 heater power=heating sized_at tout=-5 C tground=8\n")]
    [InlineData("fluidscript 1\nHP1 heater sized_at tout=-5\n")]
    public void TheClauseRoundTripsByteForByte(string text) =>
        Assert.Equal(text, SyntaxPrinter.Print(Parse(text)));
}
