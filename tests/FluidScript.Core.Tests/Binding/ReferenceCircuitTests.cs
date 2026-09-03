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
/// header's 0.2871 / 0.3589 / 0.6460 kg/s — need a solver and belong to <c>P3.4</c> onwards. What is
/// checkable now is everything <em>structural</em>: how many components each script has, where each
/// came from, which circuit owns it, and what tag it carries. Those are M2a exit criteria too, and
/// they are the ones a later package would otherwise discover were wrong while trying to solve.
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
        // or six inferred components here is stale. Six declared, four inferred, ten in total.
        var model = Model("m2-cooling-loop.fluid");

        Assert.Equal(
            ["HE1", "3WV", "PU1", "P1", "N1", "N3"],
            model.Components.Where(static c => c.Origin is Origin.Declared).Select(static c => c.Name));

        Assert.Equal(["N2"], Named(model, "I1"));
        Assert.Equal(["PU1__HE1", "HE1__3WV", "3WV__P1"], Named(model, "I2"));
        Assert.Empty(Named(model, "I3"));

        Assert.Equal(10, model.Components.Length);

        // Four of the six are `node`. The two boundaries declare their roles instead, which is the
        // whole of D-64: `supply` and `return` in kind position are state points that say which way
        // fluid crosses them, and the inference inventory is unchanged by the spelling.
        Assert.Equal(4, model.Components.Count(static c => c.Kind?.Keyword == "node"));
        Assert.Equal(["N1"], Kinded(model, "supply"));
        Assert.Equal(["N3"], Kinded(model, "return"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheCoolingLoopReportsExactlyFourInferences()
    {
        // A count is the cheapest specification there is, and this one is `01`'s own: four inferred
        // components, so four FS1510 and nothing else to say about the topology.
        Assert.Equal(4, Codes("m2-cooling-loop.fluid").Count(static code => code == "FS1510"));
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
            ["HE1", "LOAD", "CV1", "PU1", "P1"],
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
    public void BothSubcircuitsAttachToTheSameParent()
    {
        var model = Model("m2-distribution-header.fluid");

        Assert.Null(model.Circuits[0].ParentCircuit);
        Assert.Equal("heating", model.Circuits[1].ParentCircuit);
        Assert.Equal("heating", model.Circuits[2].ParentCircuit);

        Assert.Equal("N3", model.Circuits[1].Supply!.ParentComponentName);
        Assert.Equal("N5", model.Circuits[1].Return!.ParentComponentName);
        Assert.Equal("N4", model.Circuits[2].Supply!.ParentComponentName);
        Assert.Equal("N6", model.Circuits[2].Return!.ParentComponentName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TagOrdinalsRestartPerCircuit()
    {
        // `D-34`, and the reason this fixture exists: `101HE01` and `102HE01` are two different
        // components with the same ordinal, which is what a plant drawing does. Nothing keys on a tag.
        var model = Model("m2-distribution-header.fluid");

        var tags = model.Components
            .Where(static c => c.Tag is not null)
            .ToDictionary(static c => c.Name, static c => c.Tag, StringComparer.Ordinal);

        Assert.Equal("100HE01", tags["HS1"]);
        Assert.Equal("100PU01", tags["PU_MAIN"]);
        Assert.Equal("101HE01", tags["HE_AHU"]);
        Assert.Equal("101TV01", tags["TV_AHU"]);
        Assert.Equal("101PU01", tags["PU_AHU"]);
        Assert.Equal("102HE01", tags["HE_RAD"]);
        Assert.Equal("102TV01", tags["TV_RAD"]);
        Assert.Equal("102PU01", tags["PU_RAD"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AHeaderNodeASubcircuitAttachesToIsNotADeadEnd()
    {
        // `F-12`. `N4` and `N6` each carry one written connection, and the subcircuit that attaches to
        // them supplies the second — `23` lowers `supply N4` to exactly that edge, one stage after
        // this check runs. Warning about them told a user to fix a circuit that is already right.
        Assert.DoesNotContain("FS2107", Codes("m2-distribution-header.fluid"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EachBranchIsOpenAtBothEndsForItsAttachmentToJoin()
    {
        // `23`: `supply` becomes a connection from the parent's node to the subcircuit's *first
        // unconnected inlet*, and `return` one from its last unconnected outlet. Both ends must be open
        // for that, and the four dead-leg nodes I3 used to stand at them are exactly what these four
        // connections replace -- so the branch closing is the same fact as I3 having nothing left to do.
        var model = Model("m2-distribution-header.fluid");

        Assert.Empty(Named(model, "I3"));

        Assert.Equal(
            ["N3->PU_AHU.in", "TV_AHU.b->N5", "N4->PU_RAD.in", "TV_RAD.b->N6"],
            model.Connections
                .Where(static connection => connection.From.Component.StartsWith('N')
                    || connection.To.Component.StartsWith('N'))
                .Select(static connection =>
                    $"{Label(connection.From)}->{Label(connection.To)}")
                .Where(static label => label.Contains("_AHU", StringComparison.Ordinal)
                    || label.Contains("_RAD", StringComparison.Ordinal))
                .ToArray());
    }

    private static string Label(EndpointSymbol endpoint) =>
        endpoint.Port.Length == 0 ? endpoint.Component : $"{endpoint.Component}.{endpoint.Port}";
}
