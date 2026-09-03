using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// The component-model diagnostics the binder can raise on its own: the parameter checks from
/// <c>plan/20-core-domain/22-component-model.md</c>'s error table, plus <c>FS1307</c> from <c>13</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every code here is decided by counting or comparing stated values, and nothing else.</strong>
/// That is the line this file sits on: the rest of <c>22</c>'s table — a stated head against a stated
/// <c>dp</c>, a duty against what the inlet temperatures allow, a tank's substance — needs a fluid, and
/// the binder holds a fluid's <em>name</em>. Those codes belong to lowering and to sizing.
/// </para>
/// <para>
/// Assertions are on codes rather than on rendered text. The message is the descriptor's, and
/// <c>DiagnosticRegistryTests</c> already holds it to the style rules; asserting it twice would make
/// a wording change break tests that are not about wording.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ComponentDiagnosticsTests
{
    private static BindResult Bind(string body) =>
        new Binder(ComponentRegistry.Default).Bind(
            FluidScriptParser.Parse(new SourceText("fluidscript 1\n" + body + "\n")), "script");

    private static Diagnostic Only(string body, string code)
    {
        var result = Bind(body);
        var matching = result.Diagnostics.Where(d => d.Code == code).ToArray();

        Assert.True(
            matching.Length == 1,
            $"Expected exactly one {code}; got "
            + (result.Diagnostics.IsEmpty
                ? "none at all"
                : string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"))));

        return matching[0];
    }

    private static void None(string body, string code)
    {
        var result = Bind(body);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == code);
    }

    // ---- FS1307: the sign of a value ------------------------------------------------------------

    [Fact]
    public void FS1307_ANegativeTemperatureRise()
    {
        // 22's criterion, and the pair matters more than either half. A cooler is written by making
        // the duty negative, so `dt` -- which is a magnitude -- has no negative reading left to take.
        var diagnostic = Only("HE1 heat_exchanger power=-70 dt=-20", "FS1307");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("dt", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1307_DoesNotFireOnANegativeDuty() =>
        None("HE1 heat_exchanger power=-70 dt=20", "FS1307");

    [Fact]
    public void FS1307_DoesNotFireOnAParameterWhoseRangeGoesNegative() =>
        // A pipe that falls two metres is ordinary, and `elevation` is declared -500 to 500.
        None("P1 pipe length=10 dn=25 elevation=-2", "FS1307");

    [Fact]
    public void FS1307_DoesNotFireOnASubZeroCelsiusTemperature()
    {
        // -10 C is 263.15 K, so nothing is negative in SI at all. The exemption in CheckSign is for
        // the case that does go below zero -- and this asserts the ordinary case never reaches it.
        None("N1 node t=-10", "FS1307");
        None("N1 node t=-10", "FS1306");
    }

    [Fact]
    public void FS1306_StillReportsATemperatureBelowAbsoluteZero() =>
        // Below 0 K the value is negative in SI, and "t cannot be negative" would be the wrong
        // sentence when t=-10 is legal. It stays the out-of-range warning it is.
        Assert.Equal(DiagnosticSeverity.Warning, Only("N1 node t=-400", "FS1306").Severity);

    [Fact]
    public void FS1307_SuppressesTheUsualRangeWarningForTheSameValue()
    {
        // One mistake, one message. -20 K is outside dt's 0.1-200 range as well, and reporting both
        // would leave the user reading a warning that adds nothing to the error above it.
        Only("HE1 heat_exchanger power=-70 dt=-20", "FS1307");
        None("HE1 heat_exchanger power=-70 dt=-20", "FS1306");
    }

    // ---- FS2101: over-determined groups ---------------------------------------------------------

    [Fact]
    public void FS2101_AllFourOfPowerInOutAndFlow()
    {
        var diagnostic = Only("HE1 heat_exchanger power=30 in=20 out=50 flow=0.24", "FS2101");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("power, in, out, flow", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2101_ThreeOfThemAreFine() =>
        None("HE1 heat_exchanger power=30 in=20 out=50", "FS2101");

    [Fact]
    public void FS2101_UaAreaAndUTogether()
    {
        // 22's second criterion for this code, and the reason the check is a registry group rather
        // than a rule about exchanger temperatures: UA = U x A has one freedom fewer.
        var diagnostic = Only("HE1 heat_exchanger ua=12000 area=6 u=2000", "FS2101");

        Assert.Contains("ua, area, u", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2101_IsReportedOnTheLastStatedMember()
    {
        // The caret goes on what a fix deletes. Anywhere else and the user is told four assignments
        // are one too many, with no indication which.
        var source = "fluidscript 1\nHE1 heat_exchanger power=30 in=20 out=50 flow=0.24\n";
        var result = new Binder(ComponentRegistry.Default)
            .Bind(FluidScriptParser.Parse(new SourceText(source)), "script");

        var span = result.Diagnostics.Single(static d => d.Code == "FS2101").Span!.Value;

        Assert.Equal("flow=0.24", source.Substring(span.Start, span.Length));
    }

    // ---- FS2117 and FS2118: what a boundary must state -------------------------------------------

    [Fact]
    public void FS2117_ASupplyWithNoTemperature()
    {
        // D-64's third omission policy. There is no default to fall back on and nothing to size: the
        // temperature entering a plant is a fact about the plant, and every substitute would be a guess.
        var diagnostic = Only("S1 supply flow=0.2", "FS2117");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("must state t", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2118_ASupplyThatStatesNeitherFlowNorPressure()
    {
        // The lower half of a parameter group, and the only place one has a minimum: a boundary that
        // says how hot but not how much drives nothing, and every result downstream of it is invented.
        var diagnostic = Only("S1 supply t=60", "FS2118");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("one of flow, p", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2101_ASupplyThatStatesBoth()
    {
        // The upper half of the same group, and the reason it is a group rather than two rules: state
        // the flow and the pressure follows, state the pressure and the flow does.
        var diagnostic = Only("S1 supply t=60 p=300 flow=0.2", "FS2101");

        Assert.Contains("flow, p", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("S1 supply t=60 flow=0.2")]
    [InlineData("S1 supply t=60 p=300")]
    public void ASupplyWithATemperatureAndExactlyOneOfTheTwoIsAccepted(string body)
    {
        None(body, "FS2117");
        None(body, "FS2118");
        None(body, "FS2101");
    }

    [Fact]
    public void AReturnRequiresNothingAtAll()
    {
        // Deliberately asymmetric. A supply states the condition the circuit starts from; a return is
        // where whatever the circuit delivers leaves, and demanding a number there would be inventing
        // an answer the solve is meant to produce.
        None("R1 return", "FS2117");
        None("R1 return", "FS2118");
    }

    [Fact]
    public void FS2103_KvBesideItsOwnConsequence()
    {
        // A warning, not an error: the two do not contradict, and the solve will say whether the
        // stated drop is the one this Kv produces.
        var diagnostic = Only("V1 valve kv=6.3 dp=20", "FS2103");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("kv=6.3", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2103_AppliesToTheThreeWayValveToo() =>
        Assert.Equal("FS2103", Only("TV1 three_way_valve kv=6.3 dp=20", "FS2103").Code);

    // ---- FS2105 and FS2108: values with no reading outside their range --------------------------

    [Theory]
    [InlineData("V1 valve kv=6.3 position=1.4")]
    [InlineData("V1 valve kv=6.3 position=-0.1")]
    [InlineData("TV1 three_way_valve kv=6.3 position=1.4")]
    public void FS2105_AnOpeningOutsideZeroToOne(string body) =>
        Assert.Equal(DiagnosticSeverity.Error, Only(body, "FS2105").Severity);

    [Fact]
    public void FS2105_SuppressesTheUsualRangeWarning()
    {
        // position's usual range and its hard bound are the same 0-1, so without the ordering in
        // CheckRange every out-of-range opening would carry an error and a warning saying the same.
        Only("V1 valve kv=6.3 position=1.4", "FS2105");
        None("V1 valve kv=6.3 position=1.4", "FS1306");
    }

    [Fact]
    public void FS2108_AnEfficiencyOutsideZeroToOne() =>
        Assert.Equal(DiagnosticSeverity.Error, Only("PU1 pump head=6 efficiency=1.3", "FS2108").Severity);

    [Fact]
    public void FS1306_StillWarnsInsideTheHardBoundButOutsideTheUsualOne() =>
        // An efficiency of 0.05 is possible and implausible, which is exactly the split between the
        // two ranges: no error, one warning.
        Assert.Equal(DiagnosticSeverity.Warning, Only("PU1 pump head=6 efficiency=0.05", "FS1306").Severity);

    // ---- FS2113, FS2114, FS2115: the tank -------------------------------------------------------

    [Fact]
    public void FS2113_TheBulkTemperatureBesideAnIndexedOne() =>
        Assert.Equal(DiagnosticSeverity.Error, Only("T1 tank layers=2 t=60 t1=50 t2=70", "FS2113").Severity);

    [Fact]
    public void FS2113_APartialProfile() =>
        // Two of three layers. The third has no value and no default that would not be an invention.
        Assert.Equal("FS2113", Only("T1 tank layers=3 t1=50 t2=60", "FS2113").Code);

    [Fact]
    public void FS2113_APartialProfileAgainstTheDefaultLayerCount() =>
        // No `layers`, so the tank has the five its visible default gives it and t1..t3 is partial.
        Assert.Equal("FS2113", Only("T1 tank t1=50 t2=60 t3=70", "FS2113").Code);

    [Fact]
    public void FS2113_ACompleteProfileIsFine() =>
        None("T1 tank layers=3 t1=50 t2=60 t3=70", "FS2113");

    [Fact]
    public void FS2113_TheBulkTemperatureAloneIsFine() =>
        None("T1 tank layers=3 t=60", "FS2113");

    [Fact]
    public void FS2113_IsNotReportedWhenTheLayerCountIsItselfInvalid()
    {
        // One mistake, one message again: `layers=2.5` already has FS2114, and adding "your profile
        // does not have 2.5 entries" underneath would count the same error twice.
        Only("T1 tank layers=2.5 t1=50 t2=60", "FS2114");
        None("T1 tank layers=2.5 t1=50 t2=60", "FS2113");
    }

    [Theory]
    [InlineData("T1 tank layers=2.5")]
    [InlineData("T1 tank layers=0")]
    [InlineData("T1 tank layers=140")]
    public void FS2114_ALayerCountThatIsNotAWholeNumberInRange(string body) =>
        Assert.Equal(DiagnosticSeverity.Error, Only(body, "FS2114").Severity);

    [Fact]
    public void FS2114_FiveLayersIsFine() =>
        None("T1 tank layers=5", "FS2114");

    [Theory]
    [InlineData("T1 tank in1_elevation=1.4")]
    [InlineData("T1 tank out2_elevation=-0.2")]
    public void FS2115_APortAboveOrBelowItsOwnTank(string body)
    {
        var diagnostic = Only(body, "FS2115");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("elevation", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2115_TheEndsOfTheRangeAreInside()
    {
        // Bottom and top are legal heights, and 22's layer mapping depends on them being so: 0 is
        // layer 1 and 1.0 is the top layer rather than a sixth that does not exist.
        None("T1 tank in1_elevation=0 out1_elevation=1", "FS2115");
    }

    // ---- what the registry itself guarantees ----------------------------------------------------

    [Fact]
    public void EveryGroupNamesRealParametersAndLeavesAFreedom()
    {
        // ComponentRegistry.Verify throws on either failure as it builds, so reaching Default at all
        // is most of this assertion; the loop is what says so out loud when it changes.
        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            foreach (var group in kind.ParameterGroups)
            {
                Assert.All(group.Parameters, name => Assert.Contains(name, kind.Parameters.Keys));
                Assert.InRange(group.Freedoms, 1, group.Parameters.Length - 1);
            }
        }
    }

    [Fact]
    public void EveryValidityRangeSitsInsideOrOnItsUsualRange()
    {
        // The two are ordered by construction: FS1306 warns about the implausible and a validity
        // bound rejects the impossible, so a usual range wider than the hard one would warn about
        // values that were already refused, and a narrower one is the ordinary case.
        foreach (var kind in ComponentRegistry.Default.Kinds)
        {
            foreach (var parameter in kind.Parameters.Values)
            {
                if (parameter is { Validity: { } validity, UsualRange: { } usual })
                {
                    Assert.True(
                        validity.Range.Min <= usual.Min && validity.Range.Max >= usual.Max,
                        $"'{kind.Keyword}.{parameter.Name}' has a usual range outside its hard bound.");
                }
            }
        }
    }
}
