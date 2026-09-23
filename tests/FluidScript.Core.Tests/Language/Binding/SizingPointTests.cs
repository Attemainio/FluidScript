using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>A component's own sizing point (<c>D-94</c>): a curve read where the component says.</summary>
/// <remarks>
/// The plant in every test is the bivalent pair the clause exists for: a heat pump sized at −5 on the
/// heating curve, and a boiler that reads the same curve at the file's −26. What the heat pump takes
/// from the curve is its capacity; the difference to the design day is what the boiler is for.
/// </remarks>
public sealed class SizingPointTests
{
    private const string Plant = """
        fluidscript 1
        design tout=-26
        curve heating tout
        -26  50
         20   0

        circuit ahu 300
        fluid static water
        HP1 load in.t=50 out.t=30 power=heating sized_at tout=-5
        BL1 load in.t=50 out.t=30 power=heating
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

    private static ComponentSymbol Component(SemanticModel model, string name) =>
        Assert.Single(model.Components, c => c.Name == name);

    /// <summary>The power of one heat exchanger, in watts.</summary>
    private static double Power(SemanticModel model, string component) =>
        Component(model, component).Parameters["power"].Value!.Value.SiValue;

    [Fact]
    [Trait("Category", "Unit")]
    public void AComponentReadsItsCurveAtItsOwnPointAndTheRestOfTheFileAtTheDesignDay()
    {
        var model = Model(Plant);

        // Linear between (−26, 50) and (20, 0): at −5 the curve reads 50 × 25 / 46 = 27.174 kW.
        Assert.Equal(27_173.913, Power(model, "HP1"), 3);
        Assert.Equal(50_000, Power(model, "BL1"), 6);

        // The point itself is carried, evaluated, as `design` is.
        var point = Assert.Single(Component(model, "HP1").SizingPoint);
        Assert.Equal("tout", point.Key);
        Assert.Equal(-5, point.Value.Number);
        Assert.Empty(Component(model, "BL1").SizingPoint);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheBasisReportsThePointAndTheFractionOfTheDesignDay()
    {
        var model = Model(Plant);

        // The fraction is an outcome of the point, never something the file states: 27.174 / 50.
        Assert.Equal(
            "27.174 kW at tout=-5, 0.54 of the 50 kW the design day asks",
            Component(model, "HP1").Parameters["power"].Basis);
        Assert.Null(Component(model, "BL1").Parameters["power"].Basis);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AParameterThatReadsNoCurveHasNoBasis()
    {
        // `in` and `out` are plain numbers; the point only changes what a curve reference yields.
        var model = Model(Plant);

        Assert.Null(Component(model, "HP1").Parameters["in"].Basis);
        Assert.Equal(50 + 273.15, Component(model, "HP1").Parameters["in"].Value!.Value.SiValue, 6);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("sized_at tout=-5")]
    [InlineData("sized_at tout=-5 C")]
    [InlineData("sized_at outdoor=-5")]
    public void TheRoleIsWhatMakesThePointCheckableAndSpellingIndependent(string clause)
    {
        // `D-59` applies unchanged: `tout` and `outdoor` are one driver, and a bare −5 and −5 °C are
        // the same row of a table written in degrees.
        var model = Model(Plant.Replace("sized_at tout=-5", clause, StringComparison.Ordinal));

        Assert.Equal(27_173.913, Power(model, "HP1"), 3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void APointInTheWrongDimensionIsCaught()
    {
        Assert.Contains(
            "FS1304",
            Bind(Plant.Replace("sized_at tout=-5", "sized_at tout=3 bar", StringComparison.Ordinal))
                .Diagnostics.Select(static d => d.Code));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADriverNamedTwiceOnOneClauseIsADuplicate()
    {
        Assert.Contains(
            "FS1401",
            Bind(Plant.Replace("sized_at tout=-5", "sized_at tout=-5 outdoor=-7", StringComparison.Ordinal))
                .Diagnostics.Select(static d => d.Code));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ThePointIsWalkedThroughACurveThatDrivesAnother()
    {
        // `heating` is driven by `outdoor`, itself a curve of `tout`. The override on `tout` has to
        // reach `heating` through `outdoor`, evaluated afresh at −5 rather than at the file's −26.
        var model = Model("""
            fluidscript 1
            design tout=-26
            curve outdoor tout
            -26  -26
             20   20

            curve heating outdoor
            -26  50
             20   0

            circuit ahu 300
            fluid static water
            HP1 load in.t=50 out.t=30 power=heating sized_at tout=-5
            BL1 load in.t=50 out.t=30 power=heating
            """);

        Assert.Equal(27_173.913, Power(model, "HP1"), 3);
        Assert.Equal(50_000, Power(model, "BL1"), 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AComponentsOwnPointPositionsACurveTheFileNeverDid()
    {
        // No `design` line at all: the heat pump's clause is enough for its own reference, and only
        // the boiler's, which has nothing to read the curve at, is FS1528.
        var result = Bind(Plant.Replace("design tout=-26\n", string.Empty, StringComparison.Ordinal));

        var missing = Assert.Single(result.Diagnostics, static d => d.Code == "FS1528");
        var boiler = Assert.Single(result.Model.Components, static c => c.Name == "BL1");
        Assert.Equal(boiler.Parameters["power"].Span, missing.Span);

        var heatPump = Assert.Single(result.Model.Components, static c => c.Name == "HP1");
        Assert.Equal(27_173.913, heatPump.Parameters["power"].Value!.Value.SiValue, 3);
        Assert.Equal("27.174 kW at tout=-5", heatPump.Parameters["power"].Basis);
    }
}
