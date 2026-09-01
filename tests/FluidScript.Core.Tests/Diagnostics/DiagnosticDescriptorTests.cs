using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Tests.Diagnostics;

public sealed class DiagnosticDescriptorTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("FS1002", DiagnosticArea.Lexer)]
    [InlineData("FS1302", DiagnosticArea.Units)]
    [InlineData("FS1509", DiagnosticArea.Binder)]
    [InlineData("FS4001", DiagnosticArea.DesignWarning)]
    [InlineData("FS4501", DiagnosticArea.Realtime)]
    [InlineData("FS9001", DiagnosticArea.Internal)]
    public void Area_IsDerivedFromTheCode(string code, DiagnosticArea expected)
    {
        var descriptor = new DiagnosticDescriptor(code, DiagnosticSeverity.Error, "Something is wrong.");

        Assert.Equal(expected, descriptor.Area);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("FS100")]
    [InlineData("FS10002")]
    [InlineData("XX1000")]
    [InlineData("fs1000")]
    [InlineData("FS10O2")]
    [InlineData("FS 1002")]
    public void Constructor_MalformedCode_Throws(string code)
    {
        Assert.Throws<ArgumentException>(() =>
            new DiagnosticDescriptor(code, DiagnosticSeverity.Error, "Something is wrong."));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_CodeInAnUnallocatedRange_Throws()
    {
        // FS41xx sits inside the design-warning family's reserved block but is not an allocated
        // area. Taking a code from it would file the message under a subject nobody can name, which
        // is what a reader follows to find the rule behind the message.
        var exception = Assert.Throws<ArgumentException>(() =>
            new DiagnosticDescriptor("FS4101", DiagnosticSeverity.Warning, "Something is wrong."));

        Assert.Contains("FS41xx", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ArgumentNames_AreThePlaceholdersInFirstAppearanceOrder()
    {
        var descriptor = new DiagnosticDescriptor(
            "FS1503",
            DiagnosticSeverity.Error,
            "A {kind} has no '{name}'. It accepts: {accepted}.");

        Assert.Equal(["kind", "name", "accepted"], descriptor.ArgumentNames);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ArgumentNames_ARepeatedPlaceholderIsNamedOnce()
    {
        var descriptor = new DiagnosticDescriptor(
            "FS1108",
            DiagnosticSeverity.Error,
            "'{text}' -- a name cannot contain '-'. Write '{underscored}' instead of '{text}'.");

        Assert.Equal(["text", "underscored"], descriptor.ArgumentNames);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Render_SubstitutesByNameRatherThanPosition()
    {
        var descriptor = new DiagnosticDescriptor(
            "FS1503",
            DiagnosticSeverity.Error,
            "A {kind} has no '{name}'. It accepts: {accepted}.");

        var message = descriptor.Render(
            new DiagnosticArgument("accepted", "power, in, out"),
            new DiagnosticArgument("name", "pwor"),
            new DiagnosticArgument("kind", "heat_exchanger"));

        Assert.Equal("A heat_exchanger has no 'pwor'. It accepts: power, in, out.", message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Render_RepeatedPlaceholder_IsSubstitutedEveryTime()
    {
        var descriptor = new DiagnosticDescriptor(
            "FS1108",
            DiagnosticSeverity.Error,
            "'{text}' -- a name cannot contain '-'. Write '{underscored}' instead of '{text}'.");

        var message = descriptor.Render(
            new DiagnosticArgument("text", "heat-exchanger"),
            new DiagnosticArgument("underscored", "heat_exchanger"));

        Assert.Equal(
            "'heat-exchanger' -- a name cannot contain '-'. Write 'heat_exchanger' instead of 'heat-exchanger'.",
            message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Render_NoPlaceholders_ReturnsTheTemplate()
    {
        var descriptor = new DiagnosticDescriptor(
            "FS1001", DiagnosticSeverity.Error, "Unterminated string; add a closing quote.");

        Assert.Equal("Unterminated string; add a closing quote.", descriptor.Render());
        Assert.Empty(descriptor.ArgumentNames);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Render_MissingArgument_LeavesThePlaceholderVisibleRatherThanThrowing()
    {
        // The alternative is throwing from an emit site while the user is mid-keystroke, which is
        // exactly the failure the never-throw rule exists to prevent. A visible {name} is ugly and
        // caught by the registry's coverage test; a thrown exception blanks the diagram.
        var descriptor = new DiagnosticDescriptor(
            "FS1002", DiagnosticSeverity.Error, "'{ch}' is not valid here.");

        Assert.Equal("'{ch}' is not valid here.", descriptor.Render());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Render_ExtraArgument_IsIgnored()
    {
        var descriptor = new DiagnosticDescriptor(
            "FS1002", DiagnosticSeverity.Error, "'{ch}' is not valid here.");

        var message = descriptor.Render(
            new DiagnosticArgument("ch", "@"),
            new DiagnosticArgument("line", "4"));

        Assert.Equal("'@' is not valid here.", message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Render_DoubledBraces_AreOneLiteralBrace()
    {
        var descriptor = new DiagnosticDescriptor(
            "FS1201", DiagnosticSeverity.Warning, "Ignoring '{{{token}}}'.");

        Assert.Equal(["token"], descriptor.ArgumentNames);
        Assert.Equal("Ignoring '{dashed}'.", descriptor.Render(new DiagnosticArgument("token", "dashed")));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("An unopened } brace.")]
    [InlineData("An unterminated {placeholder.")]
    [InlineData("An empty {} placeholder.")]
    public void Constructor_MalformedTemplate_Throws(string template)
    {
        Assert.Throws<ArgumentException>(() =>
            new DiagnosticDescriptor("FS1000", DiagnosticSeverity.Error, template));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankTemplate_Throws(string template)
    {
        Assert.Throws<ArgumentException>(() =>
            new DiagnosticDescriptor("FS1000", DiagnosticSeverity.Error, template));
    }
}
