using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// A file may declare named operating cases and state one value per case (<c>D-143</c>).
/// </summary>
/// <remarks>
/// <para>
/// The property these tests exist to hold is that <see cref="ParameterValue.Value"/> stays a
/// <em>scalar</em> whether or not a list was written. Everything downstream of the binder reads that
/// field and nothing else, so as long as it holds the design case's number, a file with scenarios is
/// an ordinary file to the factory, the sizers and the solvers — and
/// <see cref="ScenarioProjection"/> is the only thing that ever looks at the other cases.
/// </para>
/// <para>
/// Binding is four rules and every one of them is a diagnostic rather than a repair: nothing is
/// padded, nothing is matched by name, and no case is defaulted.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ScenarioBindingTests
{
    private const string Two =
        """
        fluidscript 1
        scenarios winter summer
        design winter
        circuit plant
        fluid water
        HX1 heat_exchanger power=[30, 10] out.t=[60, 45]
        PU1 pump
        connections
        HX1.out - N1 - PU1 - N2 - HX1.in
        """;

    private static BindResult Bind(string source) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText(source)), "script");

    private static ComponentSymbol Symbol(SemanticModel model, string name) =>
        model.Components.Single(component => string.Equals(component.Name, name, StringComparison.Ordinal));

    private static string Code(BindResult result) =>
        result.Diagnostics.IsEmpty ? "none" : string.Join(", ", result.Diagnostics.Select(static d => d.Code));

    [Fact]
    public void TheDeclaredNamesAreTheOrderAnArrayBindsTo()
    {
        var result = Bind(Two);

        // Errors only: this toy circuit draws the ordinary inference infos (FS1510) and a role note.
        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            Code(result));

        Assert.Equal("plant", result.Model.Circuits.Single().Name);
        Assert.Equal(["winter", "summer"], result.Model.Project.Scenarios);
        Assert.Equal("winter", result.Model.Project.DesignScenario);
        Assert.Equal(0, result.Model.Project.DesignScenarioIndex);
    }

    [Fact]
    public void ValueHoldsTheDesignCaseSoNothingDownstreamSeesAList()
    {
        // The whole additive claim in one assertion: `power` reads 30 kW as a plain number, the way
        // the component factory and the contract builder will read it, with the list beside it.
        var model = Bind(Two).Model;
        var power = Symbol(model, "HX1").Parameters["power"];

        Assert.Equal(30_000, power.Value?.SiValue);
        Assert.Equal(2, power.Scenarios.Length);
        Assert.Equal(30_000, power.Scenarios[0].Value?.SiValue);
        Assert.Equal(10_000, power.Scenarios[1].Value?.SiValue);
    }

    [Fact]
    public void TheDesignCaseNeedNotBeTheFirstOne()
    {
        // `Value` follows `design`, not the position. If it followed position 0 this file would draw
        // winter's numbers while saying summer.
        var model = Bind(Two.Replace("design winter", "design summer", StringComparison.Ordinal)).Model;
        var parameters = Symbol(model, "HX1").Parameters;

        Assert.Equal(1, model.Project.DesignScenarioIndex);
        Assert.Equal(10_000, parameters["power"].Value?.SiValue);
        Assert.Equal(318.15, parameters["out"].Value!.Value.SiValue, 0.01);

        // Both slots are filled even though one expression served two homes.
        Assert.Equal(30_000, parameters["power"].Scenarios[0].Value?.SiValue);
        Assert.Equal(10_000, parameters["power"].Scenarios[1].Value?.SiValue);
    }

    [Fact]
    public void ProjectingSwapsTheScalarAndTouchesNothingElse()
    {
        var model = Bind(Two).Model;
        var summer = ScenarioProjection.Project(model, 1);

        Assert.Equal(10_000, Symbol(summer, "HX1").Parameters["power"].Value?.SiValue);
        Assert.Equal(318.15, Symbol(summer, "HX1").Parameters["out"].Value!.Value.SiValue, 0.01);

        // The pump states no list and is the same object; the case list travels with the projection,
        // so a projected model still knows which case it is.
        Assert.Same(Symbol(model, "PU1"), Symbol(summer, "PU1"));
        Assert.Equal(["winter", "summer"], summer.Project.Scenarios);
        Assert.Equal("winter", summer.Project.DesignScenario);

        // Projecting onto the design case is the model itself: it already is that case.
        Assert.Same(model, ScenarioProjection.Project(model, 0));
    }

    [Fact]
    public void AFileWithNoScenariosProjectsToItself()
    {
        var model = Bind(Two
            .Replace("scenarios winter summer\ndesign winter\n", string.Empty, StringComparison.Ordinal)
            .Replace("power=[30, 10] out.t=[60, 45]", "power=30 out.t=60", StringComparison.Ordinal)).Model;

        Assert.Empty(model.Project.Scenarios);
        Assert.Null(model.Project.DesignScenario);
        Assert.Same(model, ScenarioProjection.Project(model, 3));
        Assert.Empty(Symbol(model, "HX1").Parameters["power"].Scenarios);
    }

    [Fact]
    public void AnElementIsAnOrdinaryValueWithItsOwnUnitsAndExpressions()
    {
        var model = Bind(Two.Replace("power=[30, 10]", "power=[30 kW, 10000 W * 1]", StringComparison.Ordinal)).Model;
        var power = Symbol(model, "HX1").Parameters["power"];

        Assert.Equal(30_000, power.Scenarios[0].Value?.SiValue);
        Assert.Equal(10_000, power.Scenarios[1].Value?.SiValue);
    }

    [Fact]
    public void FS1540_AListOfTheWrongLengthBindsNothingRatherThanBeingPadded()
    {
        var result = Bind(Two.Replace("power=[30, 10]", "power=[30, 10, 5]", StringComparison.Ordinal));
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1540");

        Assert.Contains("3 values for 2 scenarios", error.Message, StringComparison.Ordinal);
        Assert.Contains("winter, summer", error.Message, StringComparison.Ordinal);

        // Nothing bound: a half-bound parameter would size the plant from a case nobody stated.
        Assert.False(Symbol(result.Model, "HX1").Parameters.ContainsKey("power"));
    }

    [Fact]
    public void FS1540_TooFewIsTheSameError()
    {
        var result = Bind(Two.Replace("power=[30, 10]", "power=[30]", StringComparison.Ordinal));

        Assert.Single(result.Diagnostics, static d => d.Code == "FS1540");
    }

    [Fact]
    public void FS1541_AListNeedsAScenariosLine()
    {
        var result = Bind(Two.Replace("scenarios winter summer\ndesign winter\n", string.Empty, StringComparison.Ordinal));

        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1541");
    }

    [Fact]
    public void FS1542_DesignMustNameADeclaredCase()
    {
        var result = Bind(Two.Replace("design winter", "design autumn", StringComparison.Ordinal));
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1542");

        Assert.Contains("winter, summer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1543_TheFirstCaseIsAPositionAndNotADefault()
    {
        var result = Bind(Two.Replace("design winter\n", string.Empty, StringComparison.Ordinal));
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1543");

        Assert.Contains("Add 'design winter'", error.Message, StringComparison.Ordinal);
        Assert.Null(result.Model.Project.DesignScenario);
    }

    [Fact]
    public void FS1544_ARepeatedNameIsRefusedAndDoesNotShiftThePositions()
    {
        var result = Bind(Two
            .Replace("scenarios winter summer", "scenarios winter winter summer", StringComparison.Ordinal)
            .Replace("power=[30, 10]", "power=[30, 10]", StringComparison.Ordinal));

        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1544");

        // Two names survive, so the two-element lists still bind: skipping the repeat rather than
        // keeping it is what stops every list in the file from also failing FS1540.
        Assert.Equal(["winter", "summer"], result.Model.Project.Scenarios);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1540");
    }

    [Fact]
    public void AScalarIsNotAShortList()
    {
        // `PU1 pump` and a stated scalar mean the same value in every case, which is what they
        // already meant — so no file written before scenarios acquires a length.
        var model = Bind(Two.Replace("power=[30, 10]", "power=30", StringComparison.Ordinal)).Model;
        var power = Symbol(model, "HX1").Parameters["power"];

        Assert.Equal(30_000, power.Value?.SiValue);
        Assert.Empty(power.Scenarios);
        Assert.Equal(30_000, Symbol(ScenarioProjection.Project(model, 1), "HX1").Parameters["power"].Value?.SiValue);
    }
}
