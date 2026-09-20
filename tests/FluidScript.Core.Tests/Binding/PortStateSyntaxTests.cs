using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// A port's state is written on the port -- <c>in[2].t=85</c>, <c>HX1.in[2].flow</c> -- and the model
/// keys it as it always did (<c>D-120</c>, P5.13a).
/// </summary>
/// <remarks>
/// Three spellings meet here: what the script writes (<c>in[2].t</c>), what the registry row is
/// named (the same), and what the model, the sizes and the wire key it by (<c>in2</c>). The old
/// spellings are the keys, which is why they still resolve: a script written before P5.13 binds to
/// the same parameter and is told once, per site, how it is written now (<c>FS1536</c>).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PortStateSyntaxTests
{
    private const string Substation =
        """
        circuit heating 100
        fluid water

        HX1 heat_exchanger power=150 kW in.t=40 out.t=60 in[2].t=85 out[2].t=45
        PP  pump
        PS  pump

        connections
        HX1.out - PS - N1 - HX1.in
        N1 node p=250
        HX1.out[2] - PP - N2 - HX1.in[2]
        N2 node p=250
        """;

    private static BindResult Bind(string body) =>
        new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText("fluidscript 1\n" + body + "\n")), "script");

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
        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS1536");

        // The model's keys are the pre-D-120 names: nothing downstream of the binder changed.
        Assert.Equal(85 + 273.15, exchanger.Parameters["in2"].Value!.Value.SiValue, 6);
        Assert.Equal(45 + 273.15, exchanger.Parameters["out2"].Value!.Value.SiValue, 6);
        Assert.Equal(["in", "out", "in2", "out2"], exchanger.Ports);

        // A qualified endpoint resolves to the port key too.
        Assert.Contains(result.Model.Connections, static c => c.From is { Component: "HX1", Port: "out2" });
        Assert.Contains(result.Model.Connections, static c => c.To is { Component: "HX1", Port: "in2" });
    }

    [Theory]
    [InlineData("in[2].flow=0.9", "flow2")]
    [InlineData("in[2].dt=40", "dt2")]
    [InlineData("in[2].dp=20", "dp2")]
    [InlineData("in[2].temperature=85", "in2")]
    [InlineData("in[1].t=40", "in")]
    [InlineData("in.temp=40", "in")]
    public void ASideTwoQuantityAndAnAliasAndAFoldedIndexAllReachTheKey(string written, string key)
    {
        // `in[2].flow` is side 2's flow: a side has one flow, one drop and one rise, and they are
        // written on its inlet. `in[1]` is `in`, and a quantity may be spelled by its table name.
        var result = Bind($"HX1 heat_exchanger {written}");
        var exchanger = Assert.Single(result.Model.Components, static c => c.Name == "HX1");

        Assert.Equal(string.Empty, Errors(result));
        Assert.True(exchanger.Parameters.ContainsKey(key), string.Join(", ", exchanger.Parameters.Keys));
    }

    [Theory]
    [InlineData("in=40", "in", "in.t")]
    [InlineData("out=60", "out", "out.t")]
    [InlineData("in2=85", "in2", "in[2].t")]
    [InlineData("flow2=0.9", "flow2", "in[2].flow")]
    [InlineData("dt2=40", "dt2", "in[2].dt")]
    [InlineData("dp2=20", "dp2", "in[2].dp")]
    public void FS1536_AnOldParameterSpellingBindsAndSaysHowItIsWrittenNow(string written, string key, string current)
    {
        var result = Bind($"HX1 heat_exchanger {written}");
        var exchanger = Assert.Single(result.Model.Components, static c => c.Name == "HX1");
        var notice = Assert.Single(result.Diagnostics, static d => d.Code == "FS1536");

        // Bound exactly as the new spelling would be: the notice is information, not a refusal.
        Assert.True(exchanger.Parameters.ContainsKey(key));
        Assert.Equal(DiagnosticSeverity.Info, notice.Severity);
        Assert.Contains($"'{current}'", notice.Message, StringComparison.Ordinal);

        // The quick fix replaces the name and keeps the value: the span is the name's alone.
        Assert.Equal(current, notice.Suggestion!.Replacement);
        Assert.Equal(written.IndexOf('=', StringComparison.Ordinal), notice.Span!.Value.Length);
    }

    [Theory]
    [InlineData("in1_level=0.2", "in1_level", "in.level")]
    [InlineData("in3_level=0.2", "in3_level", "in[3].level")]
    [InlineData("t2=60", "t2", "layer[2].t")]
    public void FS1536_ATanksOldIndexedSpellingsBindAndSuggestTheFamilysForm(string written, string key, string current)
    {
        var result = Bind($"T1 tank layers=3 {written}");
        var tank = Assert.Single(result.Model.Components, static c => c.Name == "T1");
        var notice = Assert.Single(result.Diagnostics, static d => d.Code == "FS1536");

        Assert.True(tank.Parameters.ContainsKey(key), string.Join(", ", tank.Parameters.Keys));
        Assert.Equal(current, notice.Suggestion!.Replacement);
    }

    [Fact]
    public void FS1536_AnOldPortSpellingOnAnEndpointIsReportedOnceAtTheEndpoint()
    {
        var script = Substation
            .Replace("HX1.out[2] - PP", "HX1.out2 - PP", StringComparison.Ordinal);
        var result = Bind(script);
        var notice = Assert.Single(result.Diagnostics, static d => d.Code == "FS1536");

        Assert.Equal(string.Empty, Errors(result));
        Assert.Equal("out[2]", notice.Suggestion!.Replacement);
        Assert.Contains(result.Model.Connections, static c => c.From is { Component: "HX1", Port: "out2" });
    }

    [Theory]
    [InlineData("HX1.t_in2", "in[2].t")]
    [InlineData("HX1.t_out", "out.t")]
    [InlineData("HX1.flow2", "in[2].flow")]
    [InlineData("T1.t2", "layer[2].t")]
    [InlineData("T1.in2_t", "in[2].t")]
    public void FS1536_AnOldPropertySpellingInAReferenceBindsAndSuggestsTheNewOne(string reference, string current)
    {
        var result = Bind($"HX1 heat_exchanger in[2].t=85\nT1 tank layers=3\nlet x = {reference}");
        var notice = Assert.Single(result.Diagnostics, static d => d.Code == "FS1536");

        Assert.Equal(string.Empty, Errors(result));
        Assert.Equal(current, notice.Suggestion!.Replacement);
        Assert.Contains(current, notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewPropertySpellingInAReferenceBindsWithoutANotice()
    {
        var result = Bind("HX1 heat_exchanger in[2].t=85\nlet x = HX1.in[2].t + HX1.out.t");

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
        var result = Bind($"N1 {kind} in.t=50");
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1537");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("'t='", error.Message, StringComparison.Ordinal);
        Assert.Contains("'in.t='", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1505_AnUnknownPortListsThePortsAsTheyAreWritten()
    {
        var script = Substation
            .Replace("HX1.out[2] - PP", "HX1.out[3] - PP", StringComparison.Ordinal);
        var result = Bind(script);
        var error = Assert.Single(result.Diagnostics, static d => d.Code is "FS1505" or "FS1538");

        Assert.Contains("in[2]", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("in2", error.Message.Replace("in[2]", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void FS1503_AnUnknownParameterListsTheNamesAsTheyAreWritten()
    {
        var result = Bind("HX1 heat_exchanger zzz=1");
        var error = Assert.Single(result.Diagnostics, static d => d.Code == "FS1503");

        Assert.Contains("in[2].t", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("t_in", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AScheduleTargetMayBeAPortState()
    {
        var result = Bind(
            """
            circuit heating 100
            fluid dynamic water

            HX1 heat_exchanger power=150 kW in.t=40 out.t=60 in[2].t=85 out[2].t=45
            PP  pump

            connections
            HX1.out - N1 - HX1.in
            N1 node p=250

            schedule
            at 60 s              HX1.in[2].t = 70
            over 60 s .. 120 s   HX1.in[2].t = 70 .. 85
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code is "FS1503" or "FS1104");
    }
}
