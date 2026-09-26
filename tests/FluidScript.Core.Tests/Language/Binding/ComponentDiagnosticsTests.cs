using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;

namespace FluidScript.Core.Tests.Language.Binding;

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
            ScriptParse.Parse(new SourceText("fluidscript 2\n" + body + "\n")), "script");

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
        var diagnostic = Only("\ncircuit \"script\":\n  HE1  heat_exchanger  power = -70  dt = -20", "FS1307");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("dt", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1307_DoesNotFireOnANegativeDuty() =>
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  power = -70  dt = 20", "FS1307");

    // ---- FS2119: a signed duty against the direction its terminals give ------------------------

    [Fact]
    public void FS2119_APositiveDutyWithACoolingSideOne()
    {
        // C-67. `power` is positive when side 1 gains heat; 50 -> 30 is the water cooling. The two cannot
        // both hold, and until now the contradiction surfaced only as a convergence residual.
        var diagnostic = Only("\ncircuit \"script\":\n  HE1  heat_exchanger  in.t = 50  out.t = 30  power = 24", "FS2119");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("the primary side gains heat", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("cools", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("in.t = 50 °C", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2119_ANegativeDutyWithAWarmingSideOne() =>
        Assert.Contains("the primary side loses heat", Only("\ncircuit \"script\":\n  HE1  heat_exchanger  in.t = 30  out.t = 50  power = -24", "FS2119").Message, StringComparison.Ordinal);

    [Fact]
    public void FS2119_SideTwoIsTheMirror()
    {
        // A positive duty leaves side 2, so in2 must be the warmer end: 45 -> 85 on side 2 is wrong.
        var diagnostic = Only("\ncircuit \"script\":\n  HX1  heat_exchanger  primary.in.t = 40  primary.out.t = 60  secondary.in.t = 45  secondary.out.t = 85  power = 150", "FS2119");

        Assert.Contains("the secondary side loses heat", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("secondary.in.t = 45 °C", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2119_DoesNotFireWhenTheSignsAgree()
    {
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  in.t = 30  out.t = 50  power = 24", "FS2119");
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  in.t = 50  out.t = 30  power = -24", "FS2119");
        None("\ncircuit \"script\":\n  HX1  heat_exchanger  primary.in.t = 40  primary.out.t = 60  secondary.in.t = 85  secondary.out.t = 45  power = 150", "FS2119");
    }

    [Fact]
    public void FS2119_ARoleWordCarriesTheSignAndCannotContradict()
    {
        // D-91: `load power=24 in.t=50 out.t=30` lowers to -24 kW. The word decided the direction; the
        // magnitude has nothing to contradict, and a check here would be second-guessing the word.
        None("\ncircuit \"script\":\n  HE1  load  in.t = 50  out.t = 30  power = 24", "FS2119");
        None("\ncircuit \"script\":\n  HE1  heater  in.t = 30  out.t = 50  power = 24", "FS2119");
    }

    [Fact]
    public void FS2119_NeedsBothTerminalsAndADuty()
    {
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  in.t = 50  power = 24", "FS2119");
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  in.t = 50  out.t = 30", "FS2119");
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  dt = 20  power = 24", "FS2119");
    }

    [Fact]
    public void FS1307_DoesNotFireOnAParameterWhoseRangeGoesNegative() =>
        // A pipe that falls two metres is ordinary, and `elevation` is declared -500 to 500.
        None("\ncircuit \"script\":\n  P1  pipe  length = 10  dn = 25  elevation = -2", "FS1307");

    [Fact]
    public void FS1307_DoesNotFireOnASubZeroCelsiusTemperature()
    {
        // -10 C is 263.15 K, so nothing is negative in SI at all. The exemption in CheckSign is for
        // the case that does go below zero -- and this asserts the ordinary case never reaches it.
        None("\ncircuit \"script\":\n  N1  node  t = -10", "FS1307");
        None("\ncircuit \"script\":\n  N1  node  t = -10", "FS1306");
    }

    [Fact]
    public void FS1306_StillReportsATemperatureBelowAbsoluteZero() =>
        // Below 0 K the value is negative in SI, and "t cannot be negative" would be the wrong
        // sentence when t=-10 is legal. It stays the out-of-range warning it is.
        Assert.Equal(DiagnosticSeverity.Warning, Only("\ncircuit \"script\":\n  N1  node  t = -400", "FS1306").Severity);

    [Fact]
    public void FS1307_SuppressesTheUsualRangeWarningForTheSameValue()
    {
        // One mistake, one message. -20 K is outside dt's 0.1-200 range as well, and reporting both
        // would leave the user reading a warning that adds nothing to the error above it.
        Only("\ncircuit \"script\":\n  HE1  heat_exchanger  power = -70  dt = -20", "FS1307");
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  power = -70  dt = -20", "FS1306");
    }

    // ---- FS1308: a sign on a word that already carries one -----------------------------------

    [Fact]
    public void FS1308_ANegativePowerOnARoleWordIsTakenAsItsMagnitudeAndSaysSo()
    {
        // `D-91`: `load` reads `power` as a capacity and supplies the sign itself, so a minus beside it
        // is a typo or a misreading of the convention -- it does not make the load heat, and it does not
        // say which way the water runs (`L-47`). Kept running for older scripts; told.
        var diagnostic = Only("\ncircuit \"script\":\n  LOAD  load  power = -24 kW  in.t = 50  out.t = 30", "FS1308");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("LOAD", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("-24 kW is read as 24 kW", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS1308_DoesNotFireOnTheNeutralSpelling() =>
        // `heat_exchanger` keeps the number signed; a negative there is a cooler and says so.
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  power = -24 kW  in.t = 50  out.t = 30", "FS1308");

    [Fact]
    public void FS1308_DoesNotFireOnAPositiveCapacity() =>
        None("\ncircuit \"script\":\n  LOAD  load  power = 24 kW  in.t = 50  out.t = 30", "FS1308");

    // ---- FS2101: over-determined groups ---------------------------------------------------------

    [Fact]
    public void FS2101_AllFourOfPowerInOutAndFlow()
    {
        var diagnostic = Only("\ncircuit \"script\":\n  HE1  heat_exchanger  power = 30  in.t = 20  out.t = 50  flow = 0.24", "FS2101");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("power, in.t, out.t, flow", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2101_ThreeOfThemAreFine() =>
        None("\ncircuit \"script\":\n  HE1  heat_exchanger  power = 30  in.t = 20  out.t = 50", "FS2101");

    [Fact]
    public void FS2101_UaAreaAndUTogether()
    {
        // 22's second criterion for this code, and the reason the check is a registry group rather
        // than a rule about exchanger temperatures: UA = U x A has one freedom fewer.
        var diagnostic = Only("\ncircuit \"script\":\n  HE1  heat_exchanger  ua = 12000  area = 6  u = 2000", "FS2101");

        Assert.Contains("ua, area, u", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2101_IsReportedOnTheLastStatedMember()
    {
        // The caret goes on what a fix deletes. Anywhere else and the user is told four assignments
        // are one too many, with no indication which.
        var source = "fluidscript 2\n\ncircuit \"script\":\n  HE1  heat_exchanger  power = 30  in.t = 20  out.t = 50  flow = 0.24\n";
        var result = new Binder(ComponentRegistry.Default)
            .Bind(ScriptParse.Parse(new SourceText(source)), "script");

        var span = result.Diagnostics.Single(static d => d.Code == "FS2101").Span!.Value;

        Assert.Equal("flow = 0.24", source.Substring(span.Start, span.Length));
    }

    // ---- FS2117 and FS2118: what a boundary must state -------------------------------------------

    [Fact]
    public void FS2117_ASupplyWithNoTemperature()
    {
        // D-64's third omission policy. There is no default to fall back on and nothing to size: the
        // temperature entering a plant is a fact about the plant, and every substitute would be a guess.
        var diagnostic = Only("\ncircuit \"script\":\n  S1  inlet  flow = 0.2", "FS2117");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("must state t", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2118_ASupplyThatStatesNeitherFlowNorPressure()
    {
        // The lower half of a parameter group, and the only place one has a minimum: a boundary that
        // says how hot but not how much drives nothing, and every result downstream of it is invented.
        var diagnostic = Only("\ncircuit \"script\":\n  S1  inlet  t = 60", "FS2118");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("one of flow, p", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2101_ASupplyThatStatesBoth()
    {
        // The upper half of the same group, and the reason it is a group rather than two rules: state
        // the flow and the pressure follows, state the pressure and the flow does.
        var diagnostic = Only("\ncircuit \"script\":\n  S1  inlet  t = 60  p = 300  flow = 0.2", "FS2101");

        Assert.Contains("flow, p", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\ncircuit \"script\":\n  S1  inlet  t = 60  flow = 0.2")]
    [InlineData("\ncircuit \"script\":\n  S1  inlet  t = 60  p = 300")]
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
        None("\ncircuit \"script\":\n  R1  outlet", "FS2117");
        None("\ncircuit \"script\":\n  R1  outlet", "FS2118");
    }

    [Fact]
    public void FS2103_KvBesideItsOwnConsequence()
    {
        // A warning, not an error: the two do not contradict, and the solve will say whether the
        // stated drop is the one this Kv produces.
        var diagnostic = Only("\ncircuit \"script\":\n  V1  valve  kv = 6.3  dp = 20", "FS2103");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("kv=6.3", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2103_AppliesToTheThreeWayValveToo() =>
        Assert.Equal("FS2103", Only("\ncircuit \"script\":\n  TV1  three_way_valve  kv = 6.3  dp = 20", "FS2103").Code);

    // ---- FS2105 and FS2108: values with no reading outside their range --------------------------

    [Theory]
    [InlineData("\ncircuit \"script\":\n  V1  valve  kv = 6.3  position = 1.4")]
    [InlineData("\ncircuit \"script\":\n  V1  valve  kv = 6.3  position = -0.1")]
    [InlineData("\ncircuit \"script\":\n  TV1  three_way_valve  kv = 6.3  position = 1.4")]
    public void FS2105_AnOpeningOutsideZeroToOne(string body) =>
        Assert.Equal(DiagnosticSeverity.Error, Only(body, "FS2105").Severity);

    [Fact]
    public void FS2105_SuppressesTheUsualRangeWarning()
    {
        // position's usual range and its hard bound are the same 0-1, so without the ordering in
        // CheckRange every out-of-range opening would carry an error and a warning saying the same.
        Only("\ncircuit \"script\":\n  V1  valve  kv = 6.3  position = 1.4", "FS2105");
        None("\ncircuit \"script\":\n  V1  valve  kv = 6.3  position = 1.4", "FS1306");
    }

    [Fact]
    public void FS2108_AnEfficiencyOutsideZeroToOne() =>
        Assert.Equal(DiagnosticSeverity.Error, Only("\ncircuit \"script\":\n  PU1  pump  head = 6  efficiency = 1.3", "FS2108").Severity);

    [Fact]
    public void FS1306_StillWarnsInsideTheHardBoundButOutsideTheUsualOne() =>
        // An efficiency of 0.05 is possible and implausible, which is exactly the split between the
        // two ranges: no error, one warning.
        Assert.Equal(DiagnosticSeverity.Warning, Only("\ncircuit \"script\":\n  PU1  pump  head = 6  efficiency = 0.05", "FS1306").Severity);

    // ---- FS2113, FS2114, FS2115: the tank -------------------------------------------------------

    [Fact]
    public void FS2113_TheBulkTemperatureBesideAnIndexedOne() =>
        Assert.Equal(DiagnosticSeverity.Error, Only("\ncircuit \"script\":\n  T1  tank  layers = 2  t = 60  layer[1].t = 50  layer[2].t = 70", "FS2113").Severity);

    [Fact]
    public void FS2113_APartialProfile() =>
        // Two of three layers. The third has no value and no default that would not be an invention.
        Assert.Equal("FS2113", Only("\ncircuit \"script\":\n  T1  tank  layers = 3  layer[1].t = 50  layer[2].t = 60", "FS2113").Code);

    [Fact]
    public void FS2113_APartialProfileAgainstTheDefaultLayerCount() =>
        // No `layers`, so the tank has the five its visible default gives it and t1..layer[3].t is partial.
        Assert.Equal("FS2113", Only("\ncircuit \"script\":\n  T1  tank  layer[1].t = 50  layer[2].t = 60  layer[3].t = 70", "FS2113").Code);

    [Fact]
    public void FS2113_ACompleteProfileIsFine() =>
        None("\ncircuit \"script\":\n  T1  tank  layers = 3  layer[1].t = 50  layer[2].t = 60  layer[3].t = 70", "FS2113");

    [Fact]
    public void FS2113_TheBulkTemperatureAloneIsFine() =>
        None("\ncircuit \"script\":\n  T1  tank  layers = 3  t = 60", "FS2113");

    [Fact]
    public void FS2113_IsNotReportedWhenTheLayerCountIsItselfInvalid()
    {
        // One mistake, one message again: `layers=2.5` already has FS2114, and adding "your profile
        // does not have 2.5 entries" underneath would count the same error twice.
        Only("\ncircuit \"script\":\n  T1  tank  layers = 2.5  layer[1].t = 50  layer[2].t = 60", "FS2114");
        None("\ncircuit \"script\":\n  T1  tank  layers = 2.5  layer[1].t = 50  layer[2].t = 60", "FS2113");
    }

    [Theory]
    [InlineData("\ncircuit \"script\":\n  T1  tank  layers = 2.5")]
    [InlineData("\ncircuit \"script\":\n  T1  tank  layers = 0")]
    [InlineData("\ncircuit \"script\":\n  T1  tank  layers = 140")]
    public void FS2114_ALayerCountThatIsNotAWholeNumberInRange(string body) =>
        Assert.Equal(DiagnosticSeverity.Error, Only(body, "FS2114").Severity);

    [Fact]
    public void FS2114_FiveLayersIsFine() =>
        None("\ncircuit \"script\":\n  T1  tank  layers = 5", "FS2114");

    [Theory]
    [InlineData("\ncircuit \"script\":\n  T1  tank  in.level = 1.4")]
    [InlineData("\ncircuit \"script\":\n  T1  tank  out[2].level = -0.2")]
    public void FS2115_APortAboveOrBelowItsOwnTank(string body)
    {
        var diagnostic = Only(body, "FS2115");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("level", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FS2115_TheEndsOfTheRangeAreInside()
    {
        // Bottom and top are legal heights, and 22's layer mapping depends on them being so: 0 is
        // layer 1 and 1.0 is the top layer rather than a sixth that does not exist.
        None("\ncircuit \"script\":\n  T1  tank  in.level = 0  out.level = 1", "FS2115");
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
