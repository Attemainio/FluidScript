using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>
/// <c>D-127</c> (<c>L-61</c>): <c>dPa</c>, <c>dkPa</c> and <c>dbar</c> spell a pressure difference
/// as <c>dK</c> spells a temperature one, so a reading minus a difference is a reading. And
/// <c>L-63</c>: a node the script names only on a connection line is a name an expression can read.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DeltaPressureTests
{
    private static BindResult Bind(string text) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText("fluidscript 1\n" + text)), "script");

    [Fact]
    public void AReadingMinusADifferenceIsAReading()
    {
        var result = Bind("let p = 300 kPa - 10 dkPa\nlet q = 2 bar + 500 dPa\nlet r = 1 dbar\n");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        Quantity Let(string name) => result.Model.Bindings.Single(b => b.Name == name).Value!.Value;

        Assert.Equal(Dimension.Pressure, Let("p").Dimension);
        Assert.Equal(290_000, Let("p").SiValue, 6);
        Assert.Equal(Dimension.Pressure, Let("q").Dimension);
        Assert.Equal(200_500, Let("q").SiValue, 6);
        Assert.Equal(Dimension.PressureDelta, Let("r").Dimension);
        Assert.Equal(100_000, Let("r").SiValue, 6);
    }

    [Fact]
    public void ADifferenceIsRefusedWhereAReadingIsExpected()
    {
        // The mirror of `t=20 dK`: a difference has no datum, so it cannot be a node's pressure.
        var mismatch = Assert.Single(Bind("N1 node p=10 dkPa\n").Diagnostics, static d => d.Code == "FS1304");

        Assert.Contains("'10 dkPa' is a pressuredelta", mismatch.Message, StringComparison.Ordinal);
        Assert.Equal(Dimension.PressureDelta, UnitTable.All.Single(static u => u.Text == "dkPa").Dimension);
    }

    [Fact]
    public void ANodeNamedOnlyOnAConnectionLineCanBeReadByAnExpression()
    {
        // L-63: N3 exists by rule I1, and until now only after every expression had been evaluated.
        var result = Bind(
            "HE1 heat_exchanger power=30 in.t=20 out.t=N3.t - 20 dK\nPU1 pump\n"
            + "connections\nN1 - PU1 - N2 - HE1 - N3 - N1\n");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1404");
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1510" && d.Message.Contains("'N3'", StringComparison.Ordinal));
        Assert.Contains(result.Model.Deferred, static d => d.Target is ValueId.ComponentParameter { Component: "HE1", Parameter: "out" });
    }
}
