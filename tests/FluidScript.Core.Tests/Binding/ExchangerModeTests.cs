using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Tests.Topology;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// The three exchanger modes of <c>D-19</c>, decided from what the script connected and stated rather
/// than from a <c>mode=</c> parameter, and the codes that police the boundary between them:
/// <c>FS2109</c>, <c>FS2110</c> and <c>FS2112</c> from <c>plan/20-core-domain/22-component-model.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Rating parameters promote nothing.</strong> <c>ua=</c> on an exchanger with no second-side
/// profile and no secondary connection is inert, and the warning is what tells the user their number
/// went nowhere. Only a profile (<c>in2</c>, <c>out2</c>, <c>dt2</c>, <c>flow2</c>) makes a Rated
/// exchanger, and only wiring both secondary ports makes a Coupled one.
/// </para>
/// <para>
/// Modes are read off the lowered component, because that is what the solver sees; the binder's
/// diagnostics are asserted by code, for the reason <c>ComponentDiagnosticsTests</c> gives.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ExchangerModeTests
{
    /// <summary>The substation's secondary, with HX1's declaration left to each test.</summary>
    private const string Loop = """
        fluidscript 1
        circuit loop
        fluid water

        SP   pump
        SS   pipe length=30 dn=32
        SR   pipe length=30 dn=32
        LOAD heat_exchanger power=-150 dt=20
        {0}

        connections
        HX1.out - SS - NSUP
        NSUP - LOAD - NRET
        NRET - SR - SP - HX1.in
        {1}
        """;

    private static string Script(string exchanger, string secondary = "") =>
        Loop.Replace("{0}", exchanger, StringComparison.Ordinal)
            .Replace("{1}", secondary, StringComparison.Ordinal);

    private static BindResult Bind(string source) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText(source)), "script");

    private static Diagnostic Only(string source, string code)
    {
        var result = Bind(source);
        var matching = result.Diagnostics.Where(d => d.Code == code).ToArray();

        Assert.True(
            matching.Length == 1,
            $"Expected exactly one {code}; got "
            + string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        return matching[0];
    }

    private static HeatExchanger Exchanger(string source) =>
        GraphFixture.Lower(source).Graph.Components.OfType<HeatExchanger>().Single(static x => x.Name == "HX1");

    // ---- which mode a declaration lands in -----------------------------------------------------

    [Fact]
    public void ADutyWithNoSecondSideIsDutyMode()
    {
        var exchanger = Exchanger(Script("HX1 heat_exchanger power=150 in=40 out=60"));

        Assert.Equal(ExchangerMode.Duty, exchanger.ResolvedMode);
        Assert.Equal("duty", exchanger.Mode);
        Assert.Null(exchanger.Rating);
    }

    [Fact]
    public void ASecondSideProfileAloneIsRatedMode()
    {
        // No secondary connection: side 2 is the stated 85/45 profile, held outside the graph. The
        // rating carries the profile's inlet and capacity rate, and the size the bootstrap sized it to.
        var exchanger = Exchanger(Script("HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45"));

        Assert.Equal(ExchangerMode.Rated, exchanger.ResolvedMode);
        Assert.False(exchanger.SecondarySideConnected);

        var rating = Assert.IsType<ExchangerRating>(exchanger.Rating);

        Assert.Equal(85 + 273.15, rating.SecondaryInletTemperature, 1e-9);
        Assert.Equal(3750.0, rating.SecondaryCapacityRate, 40.0);
        Assert.True(rating.CanRate, "the bootstrap sized ua, so the rating can rate from the first solve");
    }

    [Fact]
    public void BothSecondaryPortsWiredIsCoupledMode()
    {
        var exchanger = Exchanger(Script(
            "HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45\nNPS supply t=85 p=600\nNPR return p=350",
            "NPS - HX1.in2\nHX1.out2 - NPR"));

        Assert.Equal(ExchangerMode.Coupled, exchanger.ResolvedMode);
        Assert.True(exchanger.SecondarySideConnected);
        Assert.Equal(2, exchanger.EquationCount);
    }

    [Fact]
    public void FS2110_ARatingParameterAloneStaysDutyModeAndIsReportedInert()
    {
        // The whole of D-19's "rating parameters promote nothing": `ua` is a size with nothing to rate
        // against, so the exchanger delivers its 150 kW and the warning says where the number went.
        var source = Script("HX1 heat_exchanger power=150 in=40 out=60 ua=12000");

        var diagnostic = Only(source, "FS2110");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'ua'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(ExchangerMode.Duty, Exchanger(source).ResolvedMode);
    }

    [Fact]
    public void FS2110_NamesEachInertParameterOnce()
    {
        var result = Bind(Script("HX1 heat_exchanger power=150 in=40 out=60 ua=12000 approach=4"));

        Assert.Equal(2, result.Diagnostics.Count(static d => d.Code == "FS2110"));
    }

    // ---- the boundaries ------------------------------------------------------------------------

    [Fact]
    public void FS2112_OneSecondaryPortWiredIsAnErrorNamingTheOpenOne()
    {
        var diagnostic = Only(
            Script("HX1 heat_exchanger power=150 in=40 out=60\nNPS supply t=85 p=600", "NPS - HX1.in2"),
            "FS2112");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("out2 is open", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2109_FourTemperaturesADutyAndASizeAreOneTooMany()
    {
        // 22: in, out, in2, out2 and power fix UA at 12 071 W/K by themselves. A stated `ua` is then a
        // second answer to the same question, and the diagnostic names the one to remove.
        var diagnostic = Only(
            Script("HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 ua=12000"),
            "FS2109");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("ua", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2109_ACoefficientWithAnAreaIsASizeToo()
    {
        Assert.Equal(
            "FS2109",
            Only(Script("HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 u=3300 area=3.66"), "FS2109").Code);
    }

    [Fact]
    public void ACoefficientAloneIsNotASizeSoTheSubstationIsNotOverDetermined()
    {
        // `u=3300` turns a sized UA into an area; it does not fix UA. The reference circuit states it.
        var result = Bind(Script("HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 u=3300"));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code is "FS2109" or "FS2110");
    }
}
