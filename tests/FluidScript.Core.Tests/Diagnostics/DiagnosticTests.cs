using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Tests.Diagnostics;

public sealed class DiagnosticTests
{
    private static readonly DiagnosticDescriptor UnknownParameter = new(
        "FS1503",
        DiagnosticSeverity.Error,
        "A {kind} has no '{name}'. It accepts: {accepted}.");

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_TakesCodeAndSeverityFromTheDescriptor()
    {
        var diagnostic = Diagnostic.Create(
            UnknownParameter,
            new TextSpan(20, 4),
            new DiagnosticArgument("kind", "heat_exchanger"),
            new DiagnosticArgument("name", "pwor"),
            new DiagnosticArgument("accepted", "power, in, out"));

        Assert.Equal("FS1503", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("A heat_exchanger has no 'pwor'. It accepts: power, in, out.", diagnostic.Message);
        Assert.Equal(new TextSpan(20, 4), diagnostic.Span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_WithoutASpan_IsAboutTheDesignRatherThanTheScript()
    {
        // A physical warning about an inferred component has no source text behind it, so a null
        // span is an ordinary case and not a missing value.
        var diagnostic = Diagnostic.Create(UnknownParameter, span: null);

        Assert.Null(diagnostic.Span);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Related_DefaultsToEmptyRatherThanAnUninitializedArray()
    {
        // A default ImmutableArray throws when enumerated, and this one is enumerated by the API
        // layer on every diagnostic that crosses the wire.
        var diagnostic = Diagnostic.Create(UnknownParameter, span: null);

        Assert.False(diagnostic.Related.IsDefault);
        Assert.Empty(diagnostic.Related);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void With_AddsTheOptionalPartsWithoutASecondFactory()
    {
        var diagnostic = Diagnostic.Create(UnknownParameter, new TextSpan(20, 4)) with
        {
            ComponentName = "HE1",
            Suggestion = new Suggestion("Change 'pwor' to 'power'", new TextSpan(20, 4), "power"),
            Related = [new RelatedLocation(new TextSpan(4, 3), "first declared here")],
        };

        Assert.Equal("HE1", diagnostic.ComponentName);
        Assert.Equal("power", diagnostic.Suggestion?.Replacement);
        Assert.Single(diagnostic.Related);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_NullDescriptor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Diagnostic.Create(null!, span: null));
    }
}
