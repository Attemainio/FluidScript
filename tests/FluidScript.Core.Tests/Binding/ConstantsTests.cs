using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// <c>D-126</c>: <c>pi</c> and <c>g</c> are reserved names an expression reads anywhere, a
/// <c>let</c> of either is refused, and a head parameter accepts a length -- which is what lets
/// <c>dp / (rho * g)</c> be written into a pump.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConstantsTests
{
    private static BindResult Bind(string text) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText("fluidscript 1\n" + text)), "script");

    private static Quantity Let(string text, string name)
    {
        var result = Bind(text);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        return result.Model.Bindings.Single(binding => binding.Name == name).Value!.Value;
    }

    [Fact]
    public void PiAndGAreReadByNameWithTheirDimensions()
    {
        var circumference = Let("let c = 2 * pi * 0.5 m\n", "c");
        var column = Let("let dp = 1000 kg/m3 * g * 12 m\n", "dp");

        Assert.Equal(Dimension.Length, circumference.Dimension);
        Assert.Equal(Math.PI, circumference.SiValue, 9);

        // ρ·g·h is a pressure: 1000 × 9.80665 × 12 = 117 679.8 Pa, and a delta, since nothing absolute went in.
        Assert.Equal(Dimension.PressureDelta, column.Dimension);
        Assert.Equal(117_679.8, column.SiValue, 3);
    }

    [Fact]
    public void ALetOfAConstantsNameIsRefusedAsReserved()
    {
        var result = Bind("let g = 9.81 m/s2\nlet pi = 3\n");
        var messages = result.Diagnostics.Where(static d => d.Code == "FS1411").Select(static d => d.Message).ToArray();

        Assert.Equal(2, messages.Length);
        Assert.Equal("'g' is reserved for the standard acceleration of gravity, 9.80665 m/s2. Choose another name.", messages[0]);
        Assert.Equal("'pi' is reserved for the circle constant, 3.14159.... Choose another name.", messages[1]);
    }

    [Fact]
    public void TwoGramsIsNotTwiceGravity()
    {
        // `g` after a number is the mass unit, because a unit symbol is what follows a number; the
        // constant needs its operator. Both read, differently, and the docs say so.
        Assert.Equal(Dimension.Mass, Let("let m = 2 g\n", "m").Dimension);
        Assert.Equal(Dimension.Acceleration, Let("let a = 2 * g\n", "a").Dimension);
        Assert.Equal(2 * 9.80665, Let("let a = 2 * g\n", "a").SiValue, 9);
    }

    [Fact]
    public void AHeadParameterAcceptsALengthAsMetresOfThePumpedFluid()
    {
        var result = Bind("let drop = 25 m\nPU1 pump head=12 m\nPU2 pump head=drop\n");
        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        var stated = result.Model.Components.Single(static c => c.Name == "PU1").Parameters["head"].Value!.Value;
        var borrowed = result.Model.Components.Single(static c => c.Name == "PU2").Parameters["head"].Value!.Value;

        Assert.Equal(Dimension.Head, stated.Dimension);
        Assert.Equal(12, stated.SiValue, 9);

        // The flexibility D-126 chose, and its cost: any length is a head if you say so.
        Assert.Equal(Dimension.Head, borrowed.Dimension);
        Assert.Equal(25, borrowed.SiValue, 9);
    }

    [Fact]
    public void AHeadParameterStillRefusesAPressureAndSaysWhatAHeadIs()
    {
        var mismatch = Assert.Single(Bind("PU1 pump head=30 kPa\n").Diagnostics, static d => d.Code == "FS1304");

        Assert.Equal(
            "'head' is a head, metres of the pumped fluid: dp / (rho * g) at the inlet, which is a length; '30 kPa' is a pressure.",
            mismatch.Message);
    }
}
