using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;

namespace FluidScript.Core.Tests.Solvers.Passes;

/// <summary>
/// <c>14</c>'s Phase B: a deferred expression is evaluated against each pass and written in as the
/// parameter's stated value until it settles (<c>L-59</c>). Until this existed the binder recorded the
/// deferral and nothing read it -- <c>head=1.2*HE1.dp</c> sized the head as if nothing were written.
/// </summary>
/// <remarks>
/// The cases are the ones <c>14</c> reasons about: an anchored reference (a rated exchanger's leaving
/// temperature feeding the next exchanger's profile) converges in one extra pass; a <c>let</c> chain
/// carries it; a dimension the binder could not check is refused on the first pass that can
/// (<c>FS1304</c>); a walk at the pass cap is reported with its last three values (<c>FS1405</c>);
/// and two references each waiting on the other are reported as never evaluated (<c>FS1410</c>).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DeferredEvaluationTests
{
    private const string Chained =
        """
        fluidscript 1
        circuit chained
        fluid water
        HE1  heat_exchanger power=30 in.t=20 out.t=50 in[2].t=85 in[2].flow=0.4
        HE2  heat_exchanger power=20 in[2].t=HE1.out[2].t in[2].flow=0.4
        LOAD heat_exchanger power=-50 dp=0
        CV1  valve
        PU1  pump
        P1   pipe length=25
        connections
        N1 - PU1 - N2 - HE1 - N6 - HE2 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
        """;

    private const string Substation =
        """
        fluidscript 1
        circuit substation
        fluid water
        NPS inlet t=85 p=600
        NPR outlet p=350
        PCV valve
        SP   pump
        LOAD heat_exchanger power=-150 dt=20
        HX1 heat_exchanger power=150 in.t=40 out.t=60 in[2].t=85 out[2].t=45 u=3300
        connections
        NPS - PCV
        PCV - HX1.in[2] length=12 dn=25
        HX1.out[2] - NPR
        HX1.out - NSUP length=30 dn=32
        NSUP - LOAD - NRET
        NRET - SP length=30 dn=32
        SP - HX1.in
        """;

    [Fact]
    public async Task ARatedExchangersLeavingTemperatureFeedsTheNextOnesProfile()
    {
        // HE1's primary leaves at 85 - 30 kW / (0.4 kg/s * 4.19 kJ/kgK) = 67.1 °C; HE2's stated
        // `in[2].t=HE1.out[2].t` is that number, written in on the first pass that could rate HE1 and
        // settled on the next. The bootstrap's ideal exchanger has no rating, so pass 0 cannot supply it.
        var run = await Solve(Chained, "chained");
        var he2 = Assert.IsType<HeatExchangerComponent>(run.Graph.Components.Single(static c => c.Name == "HE2"));

        Assert.True(run.Settled);
        Assert.Equal(2, run.Passes);
        Assert.Equal(67.15, he2.StatedParameters["in2"].SiValue - 273.15, 0.05);
        Assert.Contains("from `HE1.out[2].t` at pass 1", run.Bases["HE2.in2"], StringComparison.Ordinal);
        Assert.DoesNotContain(run.Solve.Diagnostics, static d => d.Code is "FS1304" or "FS1405");
    }

    [Fact]
    public async Task ADeferredLetCarriesASolvedValueIntoAParameter()
    {
        // `let tprim = HE1.out[2].t` is itself deferred; the parameter reading it is evaluated after it,
        // in the same pass. `dK`, because `2 K` is an absolute and 13 refuses to guess.
        var source = Chained
            .Replace("HE2  heat_exchanger power=20 in[2].t=HE1.out[2].t", "let tprim = HE1.out[2].t\nHE2  heat_exchanger power=20 in[2].t=tprim - 2 dK", StringComparison.Ordinal);
        var run = await Solve(source, "let-chain");
        var he2 = Assert.IsType<HeatExchangerComponent>(run.Graph.Components.Single(static c => c.Name == "HE2"));

        Assert.True(run.Settled);
        Assert.Equal(65.15, he2.StatedParameters["in2"].SiValue - 273.15, 0.05);
        Assert.Contains("from `tprim - 2 dK`", run.Bases["HE2.in2"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADimensionTheBinderCouldNotCheckIsRefusedWhenTheValueExists()
    {
        // 14's own flagship line does not type: a head is metres of fluid and a drop is a pressure, and
        // no coercion joins them (13 invariant 3, D-50). The binder saw only a deferral; the first pass
        // that evaluates it says so, and the head is sized as if the line were absent -- now with a word.
        var source = Chained
            .Replace("HE2  heat_exchanger power=20 in[2].t=HE1.out[2].t in[2].flow=0.4\n", string.Empty, StringComparison.Ordinal)
            .Replace("LOAD heat_exchanger power=-50 dp=0", "LOAD heat_exchanger power=-30 dp=0", StringComparison.Ordinal)
            .Replace("PU1  pump", "PU1  pump head=1.2*HE1.dp", StringComparison.Ordinal)
            .Replace("N2 - HE1 - N6 - HE2 - N3", "N2 - HE1 - N3", StringComparison.Ordinal);
        var run = await Solve(source, "head-from-dp");
        var mismatch = Assert.Single(run.Solve.Diagnostics, static d => d.Code == "FS1304");

        Assert.Contains("'PU1.head' is a head, metres of the pumped fluid: dp / (rho * g) at the inlet, which is a length", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains("'1.2*HE1.dp' is a pressuredelta", mismatch.Message, StringComparison.Ordinal);
        Assert.True(run.Settled);
        Assert.False(run.Graph.Components.Single(static c => c.Name == "PU1").StatedParameters.ContainsKey("head"));
    }

    [Fact]
    public void AWalkAtThePassCapIsReportedWithItsLastThreeValues()
    {
        // 14's degenerate case: a reference anchored by nothing but the parameter it sets moves every
        // pass. The run stops at the cap and FS1405 shows the walk; the last value stands in the run.
        // No well-posed script reached this in P5.13c -- see L-62 -- so the builder is pinned on its own.
        var target = new ValueId.ComponentParameter("PU1", "head");
        var histories = new Dictionary<ValueId, List<DeferredEvaluation.Evaluated>>
        {
            [target] =
            [
                new(target, Quantity.FromSi(5.0, Dimension.Head), "1.2*HE1.dp"),
                new(target, Quantity.FromSi(6.0, Dimension.Head), "1.2*HE1.dp"),
                new(target, Quantity.FromSi(7.2, Dimension.Head), "1.2*HE1.dp"),
                new(target, Quantity.FromSi(8.64, Dimension.Head), "1.2*HE1.dp"),
            ],
        };

        var walk = Assert.Single(DeferredEvaluation.Unsettled(histories));

        Assert.Equal("FS1405", walk.Code);
        Assert.Equal("'PU1.head = 1.2*HE1.dp' did not settle: 6 m then 7.2 m then 8.64 m. Try stating a value directly.", walk.Message);
        Assert.Empty(DeferredEvaluation.Unsettled(new Dictionary<ValueId, List<DeferredEvaluation.Evaluated>>
        {
            [target] = [new(target, Quantity.FromSi(5.0, Dimension.Head), "x"), new(target, Quantity.FromSi(5.0, Dimension.Head), "x")],
        }));
    }

    [Fact]
    public async Task TwoReferencesEachWaitingOnTheOtherAreReportedAsNeverEvaluated()
    {
        // HE1's profile needs HE2 rated, HE2's needs HE1 rated: neither ever is, both lines do nothing,
        // and the run settles on sizing's choices. Silence here is L-59's original fault; FS1410 is the
        // word. Warnings, because the run stands.
        var source = Chained.Replace("in[2].t=85 in[2].flow=0.4", "in[2].t=HE2.out[2].t + 28 dK in[2].flow=0.4", StringComparison.Ordinal);
        var run = await Solve(source, "mutual");
        var never = run.Solve.Diagnostics.Where(static d => d.Code == "FS1410").Select(static d => d.Message).ToArray();

        Assert.True(run.Settled);
        Assert.Equal(2, never.Length);
        Assert.Contains("'HE1.in2 = HE2.out[2].t + 28 dK' was never evaluated: HE2.t_out2 is not published by any pass", never[0], StringComparison.Ordinal);
        Assert.Contains("'HE2.in2 = HE1.out[2].t' was never evaluated: HE1.t_out2 is not published by any pass", never[1], StringComparison.Ordinal);
        Assert.False(run.Graph.Components.Single(static c => c.Name == "HE2").StatedParameters.ContainsKey("in2"));
    }

    [Fact]
    public async Task ALineStillWaitingWhenTheFirstPassFailsIsNamedWithThePass()
    {
        // `L-62`. The primary's only temperature reads a solved value, so pass 1 is lowered without it
        // and is singular; FS1410's "not published by any pass" would be false here -- no pass
        // completed -- and FS1412 says what happened instead: the line was still waiting when pass 1
        // failed. A warning, attached to the failed solve.
        var source = Substation.Replace("NPS inlet t=85 p=600", "NPS inlet t=NPR.t + 40 dK p=600", StringComparison.Ordinal);
        var resolved = PipeCatalogs.Resolve(pin: null);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var result = await loop.RunAsync(GraphFixture.Bind(source), Water.Instance, "waiting", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var run = result.Value;
        var waiting = run.Solve.Diagnostics.Where(static d => d.Code == "FS1412").Select(static d => d.Message).ToArray();

        Assert.False(run.Solve.Converged);
        Assert.Single(waiting);
        Assert.Contains("'NPS.t = NPR.t + 40 dK' was still waiting on NPR.t when pass 1 failed", waiting[0], StringComparison.Ordinal);
        Assert.DoesNotContain(run.Solve.Diagnostics, static d => d.Code == "FS1410");
    }

    [Fact]
    public async Task AHeadWrittenAsALengthFromTheDesignDropIsStatedFromTheSeed()
    {
        // 14's worked example, as the language can write it since D-126: dp / (rho * g) is a length,
        // and a head parameter reads a length as metres of the pumped fluid. HE1.dp is the stated
        // design drop, so the seed can evaluate it and the head is stated before sizing decides.
        var source = Chained
            .Replace("HE2  heat_exchanger power=20 in[2].t=HE1.out[2].t in[2].flow=0.4\n", string.Empty, StringComparison.Ordinal)
            .Replace("LOAD heat_exchanger power=-50 dp=0", "LOAD heat_exchanger power=-30 dp=0", StringComparison.Ordinal)
            .Replace("PU1  pump", "PU1  pump head=1.2*HE1.dp/(998 kg/m3*g)", StringComparison.Ordinal)
            .Replace("N2 - HE1 - N6 - HE2 - N3", "N2 - HE1 - N3", StringComparison.Ordinal);
        var run = await Solve(source, "head-formula", requireSettled: false);
        var pump = run.Graph.Components.Single(static c => c.Name == "PU1");

        Assert.Equal(1.2 * 20000 / (998 * 9.80665), pump.StatedParameters["head"].SiValue, 4);
        Assert.Contains("from `1.2*HE1.dp/(998 kg/m3*g)` at pass 0", run.Bases["PU1.head"], StringComparison.Ordinal);
        Assert.DoesNotContain(run.Solve.Diagnostics, static d => d.Code is "FS1304" or "FS1405" or "FS1410");
    }

    private static async Task<OuterLoopResult> Solve(string script, string name, bool requireSettled = true)
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(new NewtonSolver(), new CatalogBoreLookup(resolved.Value), OuterLoop.Rules(resolved.Value.Catalog), 10);
        var result = await loop.RunAsync(GraphFixture.Bind(script), Water.Instance, name, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Solve.Converged);
        Assert.True(!requireSettled || result.Value.Settled);

        return result.Value;
    }
}
