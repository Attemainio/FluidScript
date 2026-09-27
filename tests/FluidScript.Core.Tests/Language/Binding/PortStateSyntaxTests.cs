using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>
/// A port's state is written on the port -- <c>secondary.in.t = 85</c>, <c>HX1.secondary.in.flow</c> -- and the
/// model keys it as it always did (<c>D-120</c>, P5.13a).
/// </summary>
/// <remarks>
/// Two spellings meet here: what the script writes (<c>secondary.in.t</c>), and what the model, the sizes and the
/// wire key it by (<c>in2</c>). The old spellings are not read, and their notice (<c>FS1536</c>) is retired (<c>D-174</c>).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PortStateSyntaxTests
{
    private const string Substation =
        """

        circuit "heating":
          fluid = water
          number = 100
          role = heating

          HX1  heat_exchanger  power = 150 kW  primary.in.t = 40  primary.out.t = 60  secondary.in.t = 85  secondary.out.t = 45
          PP  pump
          PS  pump

          HX1.primary.out - PS - N1 - HX1.primary.in
          N1  node  p = 250
          HX1.secondary.out - PP - N2 - HX1.secondary.in
          N2  node  p = 250
        """;

    private static BindResult Bind(string body) =>
        new Binder(ComponentRegistry.Default).Bind(
            ScriptParse.Parse(new SourceText("fluidscript 2\n" + body + "\n")), "script");

    /// <summary>One component line in a circuit of its own.</summary>
    private static BindResult BindLine(string line) => Bind($"circuit \"c\":\n  {line}");

    private static string Errors(BindResult result) =>
        string.Join("; ", result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => $"{d.Code} {d.Message}"));

    [Fact]
    public void TheSecondSideIsWrittenOnItsPortsAndKeyedAsBefore()
    {
        var result = Bind(Substation);
        var exchanger = Assert.Single(result.Model.Components, static c => c.Name == "HX1");

        Assert.Equal(string.Empty, Errors(result));
        // The model's keys are the pre-D-120 names: nothing downstream of the binder changed.
        Assert.Equal(85 + 273.15, exchanger.Parameters["in2"].Value!.Value.SiValue, 6);
        Assert.Equal(45 + 273.15, exchanger.Parameters["out2"].Value!.Value.SiValue, 6);
        Assert.Equal(["in", "out", "in2", "out2"], exchanger.Ports);

        // A qualified endpoint resolves to the port key too.
        Assert.Contains(result.Model.Connections, static c => c.From is { Component: "HX1", Port: "out2" });
        Assert.Contains(result.Model.Connections, static c => c.To is { Component: "HX1", Port: "in2" });
    }

    [Theory]
    [InlineData("secondary.in.flow = 0.9", "flow2")]
    [InlineData("secondary.in.dt = 40", "dt2")]
    [InlineData("secondary.in.dp = 20", "dp2")]
    [InlineData("secondary.in.temperature = 85", "in2")]
    [InlineData("primary.in.t = 40", "in")]
    [InlineData("in.temp = 40", "in")]
    public void ASideTwoQuantityAndAnAliasAndThePrimarySideAllReachTheKey(string written, string key)
    {
        // `secondary.in.flow` is side 2's flow: a side has one flow, one drop and one rise, and they are
        // written on its inlet. `primary.in` is `in`, and a quantity may be spelled by its table name.
        var result = BindLine($"HX1  heat_exchanger  {written}");
        var exchanger = Assert.Single(result.Model.Components, static c => c.Name == "HX1");

        Assert.Equal(string.Empty, Errors(result));
        Assert.True(exchanger.Parameters.ContainsKey(key), string.Join(", ", exchanger.Parameters.Keys));
    }

    [Fact]
    public void ANewPropertySpellingInAReferenceBindsWithoutANotice()
    {
        var result = Bind("\nlet x = HX1.secondary.in.t + HX1.primary.out.t\n\ncircuit \"script\":\n  HX1  heat_exchanger  secondary.in.t = 85");

        Assert.Equal(string.Empty, Errors(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1536");
    }

    [Theory]
    [InlineData("node")]
    [InlineData("inlet")]
    public void FS1537_APortStateOnANodeIsAnErrorThatNamesTheBareQuantity(string kind)
    {
        // A node has one state and no ports, so `in.t=` on it says nothing `t=` does not; refusing it
        // keeps the port form meaning "this port" everywhere it is accepted.
        var result = BindLine($"N1  {kind}  in.t = 50");
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1537");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("'t ='", error.Message, StringComparison.Ordinal);
        Assert.Contains("'in.t ='", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1505_AnUnknownPortListsThePortsAsTheyAreWritten()
    {
        var script = Substation
            .Edited("HX1.secondary.out - PP", "HX1.tertiary.out - PP");
        var result = Bind(script);
        var error = Assert.Single(result.Diagnostics, static d => d.Code is "FS1505" or "FS1538");

        Assert.Contains("secondary.in", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("in2", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("in[2]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1503_AnUnknownParameterListsTheNamesAsTheyAreWritten()
    {
        var result = Bind("\ncircuit \"script\":\n  HX1  heat_exchanger  zzz = 1");
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1503");

        Assert.Contains("secondary.in.t", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("in[2]", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("t_in", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AScheduleTargetMayBeAPortState()
    {
        var result = Bind(
            """

            circuit "heating":
              fluid = water
              number = 100
              role = heating

              HX1  heat_exchanger  power = 150 kW  primary.in.t = 40  primary.out.t = 60  secondary.in.t = 85  secondary.out.t = 45
              PP  pump

              HX1.primary.out - N1 - HX1.primary.in
              N1  node  p = 250

            run "Transient":
              at 60 s  HX1.secondary.in.t = 70
              over 60 s..120 s  HX1.secondary.in.t = 70..85
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code is "FS1503" or "FS1104");
    }
}
