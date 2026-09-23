using System.Globalization;

using FluidScript.Core.Catalogs;
using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Catalogs.Valves;

namespace FluidScript.Core.Tests.Catalogs.Valves;

/// <summary>
/// The R5 valve catalogue, and the regeneration that <c>D-84</c> puts in place of a second source.
/// </summary>
/// <remarks>
/// The table in <see cref="ValveKvR5"/> is literals so a reader can see it. Nothing here transcribes
/// those literals a second time: every one is recomputed from <c>10^(n/5)</c> and compared, so a typo
/// in the catalogue is a failing test rather than a valve two sizes out.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ValveKvR5Tests
{
    [Fact]
    public void EveryRowIsTheRenardValueForItsPosition()
    {
        // R5 is 10^(n/5) rounded to **two significant figures in every decade** -- 1.0, 1.6, 2.5, 4.0,
        // 6.3 and the same digits shifted, so 10^(6/5) = 15.849 is 16 rather than 15.8. The finer
        // series R10 and R20 carry more digits; R5 does not. The first value is 0.10, which is n = -5.
        for (var i = 0; i < ValveKvR5.Values.Length; i++)
        {
            var exact = Math.Pow(10, (i - 5) / 5.0);
            var scale = Math.Pow(10, 1 - Math.Floor(Math.Log10(exact)));
            var rounded = Math.Round(exact * scale, MidpointRounding.ToEven) / scale;

            Assert.Equal(rounded, ValveKvR5.Values[i], 10);
        }
    }

    [Fact]
    public void TheSeriesStepsByTheRatioR5Promises()
    {
        // 10^(1/5) = 1.5849. Every neighbouring pair, so a dropped or duplicated row shows up here
        // rather than as a valve that is one preferred number out.
        for (var i = 1; i < ValveKvR5.Values.Length; i++)
        {
            var ratio = ValveKvR5.Values[i] / ValveKvR5.Values[i - 1];

            Assert.InRange(ratio, 1.55, 1.62);
        }
    }

    [Fact]
    public void TheCatalogueValidatesAndIsUsable()
    {
        // `D-84`: no sources, and usable anyway, because `Computed` names the rule that defends it.
        Assert.Empty(ValveKvR5.Instance.Validate());

        foreach (var entry in ValveKvR5.Instance.Entries)
        {
            Assert.True(entry.Provenance.IsUsable, entry.Designation);
            Assert.Empty(entry.Provenance.Sources);
            Assert.Equal(ValveKvR5.GeneratingRule, entry.Provenance.Computed);
        }
    }

    [Fact]
    public void AMeasuredRowStillNeedsTwoSources()
    {
        // The other half of `D-84`. Dropping the redundancy requirement for a generated series must not
        // drop it for a row that states a fact about a physical object, so the pipe catalogues are held
        // to the original rule here rather than trusted to have kept it.
        foreach (var entry in SteelEn10255.Instance.Entries)
        {
            Assert.Null(entry.Provenance.Computed);
            Assert.True(entry.Provenance.Sources.Length >= 2, entry.Designation);
        }
    }

    [Fact]
    public void RoundingDownIsWhatTheCatalogueDoes()
    {
        // `24`'s worked example: a required Kv of 1.833 takes Kv 1.6, not 2.5. Rounding up would give a
        // valve that drops less than intended and controls over less of its travel.
        var chosen = ValveKvR5.Instance.NearestBelow(1.833, static spec => spec.Kvs);

        Assert.Equal(1.6, chosen.Entry.Spec.Kvs);
        Assert.Equal(CatalogFit.Exact, chosen.Fit);
        Assert.Equal("Kv 1.6", chosen.Entry.Designation);
    }

    [Fact]
    public void ARequirementBelowTheSeriesClampsAndSaysSo()
    {
        var chosen = ValveKvR5.Instance.NearestBelow(0.05, static spec => spec.Kvs);

        Assert.Equal(0.10, chosen.Entry.Spec.Kvs);
        Assert.Equal(CatalogFit.ClampedToSmallest, chosen.Fit);
    }

    [Fact]
    public void ARequirementAboveTheSeriesClampsAndSaysSo()
    {
        var chosen = ValveKvR5.Instance.NearestBelow(5000, static spec => spec.Kvs);

        Assert.Equal(630, chosen.Entry.Spec.Kvs);
        Assert.Equal(CatalogFit.ClampedToLargest, chosen.Fit);
    }

    [Fact]
    public void EveryDesignationIsTheOneAnEngineerWouldWrite()
    {
        Assert.Equal(
            ["Kv 0.1", "Kv 0.16", "Kv 0.25", "Kv 0.4", "Kv 0.63"],
            ValveKvR5.Instance.Entries.Take(5).Select(entry => entry.Designation));
    }

    [Fact]
    public void AKvsOutsideTheRangeTheLanguageBindsIsAFault()
    {
        // A catalogue may not offer a size the binder would then refuse.
        Assert.NotNull(ValveSpec.Fault(new ValveSpec { Kvs = 0.001, Series = "test" }));
        Assert.NotNull(ValveSpec.Fault(new ValveSpec { Kvs = 20_000, Series = "test" }));
        Assert.NotNull(ValveSpec.Fault(new ValveSpec { Kvs = 0, Series = "test" }));
        Assert.NotNull(ValveSpec.Fault(new ValveSpec { Kvs = double.NaN, Series = "test" }));
        Assert.Null(ValveSpec.Fault(new ValveSpec { Kvs = 1.6, Series = "test" }));
    }

    [Fact]
    public void AKvLawIsDeclaredSteepInPressureAndNothingElseIs()
    {
        // `S-74`. The Jacobian reads a pressure column twice: at a step that clears the flash's noise for
        // every row, and at sqrt(eps) for the rows a valve marks steep, whose sqrt(dp) law may sit at a
        // sub-pascal drop. Both valve kinds mark their Kv laws and nothing else; a pipe marks nothing.
        Assert.All(new FluidScript.Core.Components.Valves.ValveComponent("CV", 4).DeclareEquations(), static row => Assert.True(row.SteepInPressure));
        Assert.All(
            new FluidScript.Core.Components.Valves.ThreeWayValveComponent("TV", 6.3).DeclareEquations(),
            static row => Assert.Equal(row.Name.Contains("Kv law", StringComparison.Ordinal), row.SteepInPressure));
        Assert.All(new Core.Components.PipeComponent("P1", 10, 0.0273).DeclareEquations(), static row => Assert.False(row.SteepInPressure));
    }

    [Fact]
    public void TheLawAndItsInverseAgree()
    {
        // `ValveLaw.RequiredKv` exists so the sizing rule never writes the sqrt(1e5) out again. If the
        // round trip holds, a rule that calls it is wrong only if the law is.
        foreach (var kv in ValveKvR5.Values)
        {
            foreach (var drop in new[] { 500.0, 13_300.0, 100_000.0 })
            {
                var flow = FluidScript.Core.Components.Valves.ValveLaw.MassFlow(kv, drop, 998.2);
                var recovered = FluidScript.Core.Components.Valves.ValveLaw.RequiredKv(flow, drop, 998.2);

                Assert.Equal(kv, recovered, 9);
                Assert.Equal(drop, FluidScript.Core.Components.Valves.ValveLaw.PressureDrop(kv, flow, 998.2), 6);
            }
        }
    }

    [Fact]
    public void TheInverseRefusesInsideTheRegularisationBand()
    {
        // Below 100 Pa the law is a quadratic that exists to keep a closed valve differentiable, and no
        // valve is ever sized to sit there. Inverting it would put a sized valve inside the smoothing
        // band, where its authority is not what the arithmetic says.
        Assert.True(double.IsNaN(FluidScript.Core.Components.Valves.ValveLaw.RequiredKv(0.24, 50, 998.2)));
        Assert.True(double.IsNaN(FluidScript.Core.Components.Valves.ValveLaw.RequiredKv(0.24, 0, 998.2)));
        Assert.True(double.IsNaN(FluidScript.Core.Components.Valves.ValveLaw.RequiredKv(0.24, 13_300, 0)));
    }

    [Fact]
    public void TheWorkedExamplesRequiredKvIsTheOneTheDocumentComputes()
    {
        // `24` reads Kv 1.833 from 0.8664 m3/h across 0.2235 bar. That volume flow is 0.2392 kg/s at
        // **993.9** kg/m3 -- the loop's 35 C mean -- while the same document converts the pump's head
        // at the valve's 998.2 inlet two lines later. At the inlet the required Kv is 1.823. It is the
        // document's own inlet-versus-mean trap, and it went unnoticed because the row is Kv 1.6 from
        // either number: the R5 step is coarse enough to swallow a half-percent disagreement.
        var atInlet = FluidScript.Core.Components.Valves.ValveLaw.RequiredKv(0.2392, 22_350, 998.2);
        var atLoopMean = FluidScript.Core.Components.Valves.ValveLaw.RequiredKv(0.2392, 22_350, 993.9);

        Assert.Equal(1.823, atInlet, 2);
        Assert.Equal(1.833, atLoopMean, 2);

        var kv = atInlet;

        Assert.Equal(
            "Kv 1.6",
            ValveKvR5.Instance.NearestBelow(kv, static spec => spec.Kvs).Entry.Designation);
    }

    [Fact]
    public void TheDesignationFormatSurvivesACultureThatUsesCommas()
    {
        // The catalogue is built in a static initialiser, so its designations are whatever culture was
        // current then. A comma decimal separator in "Kv 1,6" would reach a user's report.
        Assert.All(
            ValveKvR5.Instance.Entries,
            entry => Assert.DoesNotContain(",", entry.Designation, StringComparison.Ordinal));

        Assert.Equal(
            "Kv 1.6",
            string.Create(CultureInfo.InvariantCulture, $"Kv {1.6:0.##}"));
    }
}
