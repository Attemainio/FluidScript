using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// The M2a reference circuits from <c>plan/00-foundation/01-vision-and-scope.md</c>, asserted as far
/// as binding can take them.
/// </summary>
/// <remarks>
/// <para>
/// The figures these fixtures exist for — 0.2392 kg/s of secondary flow, 5.28 m of pump head, the
/// header's 0.2871 / 0.3589 / 0.4306 kg/s — need a solver, and <c>OuterLoopTests</c> now reaches all
/// three. What is checkable here is everything <em>structural</em>: how many components each script has,
/// where each came from, which circuit owns it, and what tag it carries. Those are M2a exit criteria
/// too, and they are the ones a later package would otherwise discover were wrong while trying to solve.
/// </para>
/// <para>
/// Written before the physics on purpose, the same trade <c>08</c> records for the syntax tour: a
/// fixture built after the code it validates tends to agree with the code. Writing these three found
/// that <c>01</c>'s distribution header declared two components of a kind that does not exist and had
/// no flow path through either subcircuit — see <c>F-11</c>.
/// </para>
/// </remarks>
public sealed class ReferenceCircuitTests
{
    private static SemanticModel Model(string name)
    {
        var sample = ScriptCorpus.Samples()
            .Single(candidate => candidate.Name.EndsWith(name, StringComparison.Ordinal));

        var result = new Binder(ComponentRegistry.Default)
            .Bind(FluidScriptParser.Parse(new SourceText(sample.Text)), name);

        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            $"{name}: {string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}"))}");

        return result.Model;
    }

    private static string[] Codes(string name)
    {
        var sample = ScriptCorpus.Samples()
            .Single(candidate => candidate.Name.EndsWith(name, StringComparison.Ordinal));

        return [.. new Binder(ComponentRegistry.Default)
            .Bind(FluidScriptParser.Parse(new SourceText(sample.Text)), name)
            .Diagnostics.Select(static d => d.Code)];
    }

    private static string[] Named(SemanticModel model, string rule) =>
        [.. model.Components
            .Where(component => component.Origin is Origin.Inferred inferred && inferred.Rule == rule)
            .Select(static component => component.Name)];

    private static string[] Kinded(SemanticModel model, string keyword) =>
        [.. model.Components
            .Where(component => component.Kind?.Keyword == keyword)
            .Select(static component => component.Name)];

    // ---- the cooling loop — topology reference ------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheCoolingLoopsInferenceInventoryIsExactly01s()
    {
        // `01` states this inventory in as many words, and says any document counting three I1 nodes
        // or six inferred components here is stale. Five declared, five inferred, ten in total -- the
        // return pipe is the connection line's own since D-110 (I7) and counts among the inferred.
        var model = Model("m2-cooling-loop.fluid");

        Assert.Equal(
            ["HE1", "3WV", "PU1", "N1", "N3"],
            model.Components.Where(static c => c.Origin is Origin.Declared).Select(static c => c.Name));

        Assert.Equal(["N2"], Named(model, "I1"));
        Assert.Equal(["PU1__HE1", "HE1__3WV", "3WV__N3__in"], Named(model, "I2"));
        Assert.Empty(Named(model, "I3"));
        Assert.Equal(["3WV__N3"], Named(model, "I7"));

        Assert.Equal(10, model.Components.Length);

        // Four of the six are `node`. The two boundaries declare their roles instead, which is the
        // whole of D-64: `supply` and `return` in kind position are state points that say which way
        // fluid crosses them, and the inference inventory is unchanged by the spelling.
        Assert.Equal(4, model.Components.Count(static c => c.Kind?.Keyword == "node"));
        Assert.Equal(["N1"], Kinded(model, "inlet"));
        Assert.Equal(["N3"], Kinded(model, "outlet"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheCoolingLoopReportsExactlyFiveInferences()
    {
        // A count is the cheapest specification there is, and this one is `01`'s own: four inferred
        // nodes and, since D-110, the return pipe the connection line carries (I7) -- five FS1510 and
        // nothing else to say about the topology.
        Assert.Equal(5, Codes("m2-cooling-loop.fluid").Count(static code => code == "FS1510"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheCoolingLoopsPumpIsInsideTheSecondaryLoop()
    {
        // The part `01` says is easy to get wrong: with `PU1` on the primary branch the secondary loop
        // contains nothing that drives flow, the only solution is zero recirculation, and `HE1`'s
        // stated in=20 cannot be met. Asserted structurally so a later edit to the fixture cannot
        // quietly move it.
        var model = Model("m2-cooling-loop.fluid");

        Assert.Contains(
            model.Connections,
            connection => connection is { From.Component: "N2", To.Component: "PU1" });
        Assert.Contains(
            model.Connections,
            connection => connection is { From.Component: "3WV", To.Component: "N2" });
    }

    // ---- the simple loop — sizing and solver reference ----------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheSimpleLoopIsFiveComponentsAndFiveNodes()
    {
        var model = Model("m2-simple-loop.fluid");

        Assert.Equal(
            ["HE1", "LOAD", "CV1", "PU1"],
            model.Components.Where(static c => c.Origin is Origin.Declared).Select(static c => c.Name));

        // LOAD is what makes the ring solvable rather than merely square: a closed circuit whose duties
        // do not sum to zero has no steady state, and this one used to ask for 30 kW with nowhere to
        // put it (FS2203).
        Assert.Equal(["N1", "N2", "N3", "N4", "N5"], Named(model, "I1"));

        // One closed series loop: every node joins exactly two components, so nothing is a dead end
        // and no port needed terminating.
        Assert.Empty(Named(model, "I2"));
        Assert.Empty(Named(model, "I3"));
        Assert.DoesNotContain("FS2107", Codes("m2-simple-loop.fluid"));
    }

    // ---- the distribution header — the only multi-circuit fixture -----------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheHeadersThreeCircuitsKeepTheirStatedNumbers()
    {
        var model = Model("m2-distribution-header.fluid");

        Assert.Equal(["heating", "AHU", "radiators"], model.Circuits.Select(static c => c.Name));
        Assert.Equal([100, 101, 102], model.Circuits.Select(static c => c.Number));
        Assert.All(model.Circuits, static circuit => Assert.True(circuit.NumberIsExplicit));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheHeadersCircuitsAreSiblingsRatherThanAttachedSubcircuits()
    {
        // The consumers are wired to the header by hand rather than attached, so none of them declares a
        // parent -- see `EachConsumerIsWiredToTheHeaderAtBothEndsByHand` for why attachment cannot
        // express this shape (`F-16`/`F-17`). Asserted rather than left implicit because the previous
        // version of this test asserted the opposite, and a fixture quietly losing a language feature is
        // exactly the thing a reference circuit exists to make loud.
        var model = Model("m2-distribution-header.fluid");

        Assert.All(model.Circuits, static circuit => Assert.Null(circuit.ParentCircuit));
        Assert.All(model.Circuits, static circuit => Assert.Null(circuit.Supply));
        Assert.All(model.Circuits, static circuit => Assert.Null(circuit.Return));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TagOrdinalsRestartPerCircuit()
    {
        // `D-34`, and the reason this fixture exists: `101HE01` and `102HE01` are two different
        // components with the same ordinal, which is what a plant drawing does. Nothing keys on a tag.
        // The header has no pump of its own since the consumer pumps took over the whole loop, so the
        // source circuit contributes one tag only.
        var model = Model("m2-distribution-header.fluid");

        var tags = model.Components
            .Where(static c => c.Tag is not null)
            .ToDictionary(static c => c.Name, static c => c.Tag, StringComparer.Ordinal);

        Assert.Equal("100HE01", tags["HS1"]);
        Assert.DoesNotContain("PU_MAIN", tags.Keys);
        Assert.Equal("101HE01", tags["HE_AHU"]);
        Assert.Equal("101TV01", tags["TV_AHU"]);
        Assert.Equal("101PU01", tags["PU_AHU"]);
        Assert.Equal("102HE01", tags["HE_RAD"]);
        Assert.Equal("102TV01", tags["TV_RAD"]);
        Assert.Equal("102PU01", tags["PU_RAD"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoHeaderNodeIsReportedAsADeadEnd()
    {
        // `F-12`. Every header node now carries both its connections in writing, so this passes for a
        // plainer reason than it used to: it was the *attachment* that supplied each node's second edge,
        // one stage after this check runs, and warning about them told a user to fix a circuit that was
        // already right. The guard still earns its place -- `N4` and `N6` are one connection each until
        // the consumer circuits are read.
        Assert.DoesNotContain("FS2107", Codes("m2-distribution-header.fluid"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EachConsumerIsWiredToTheHeaderAtBothEndsByHand()
    {
        // `F-16`/`F-17`. These four connections were written as `supply N3` / `return N5` attachments
        // until measurement showed attachment cannot express a mixing subcircuit: the rule takes the
        // subcircuit's *first unconnected inlet*, and on this shape that is the valve's outlet, which
        // wires the consumer's return to the supply header and leaves every header branch at zero flow.
        // Attachment itself is still exercised -- `m1-syntax-tour` uses it on a shape it suits.
        var model = Model("m2-distribution-header.fluid");

        Assert.Empty(Named(model, "I3"));

        // The tap pipes are the wiring, so they are what this asserts: each consumer reaches the supply
        // header through one and returns through another, both ends written out. Since D-110 the taps are
        // the connection lines' own properties (rule I7), named after their ends. The supply tap ends at
        // the valve's hot port `a` and the return tap starts at the coil-return node, so the header water
        // enters the valve and the coil return both recirculates through port `b` and leaves through the
        // tap. `N3` is already a node so the tap needs no node on that side, while its outlet joins a
        // component port and I2 puts a node between them, named after the pipe's port.
        Assert.Equal(
            [
                "N3->N3__TV_AHU.in", "N3__TV_AHU.out->N3__TV_AHU__out", "NM_AHU->NM_AHU__N5.in", "NM_AHU__N5.out->N5",
                "N4->N4__TV_RAD.in", "N4__TV_RAD.out->N4__TV_RAD__out", "NM_RAD->NM_RAD__N6.in", "NM_RAD__N6.out->N6",
            ],
            model.Connections
                .Where(static connection => Tap(connection.From.Component)
                    || Tap(connection.To.Component))
                .Select(static connection =>
                    $"{Label(connection.From)}->{Label(connection.To)}")
                .ToArray());
    }

    private static bool Tap(string component) =>
        component is "N3__TV_AHU" or "NM_AHU__N5" or "N4__TV_RAD" or "NM_RAD__N6";

    private static string Label(EndpointSymbol endpoint) =>
        endpoint.Port.Length == 0 ? endpoint.Component : $"{endpoint.Component}.{endpoint.Port}";
}
