using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Components;
using FluidScript.Core.Language.Compatibility;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;

namespace FluidScript.Core.Tests.Sizing.Sizers;

/// <summary>
/// <c>C-36</c>: a pipe's <c>material</c> names the catalogue its <c>dn</c> is read in, so a copper run
/// and a steel run can share one script whose <c>catalog</c> line is the default for the rest.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PipeMaterialTests
{
    [Fact]
    public void AStatedMaterialReadsTheDnInItsOwnCatalogue()
    {
        var lowered = GraphFixture.Lower(
            """
            fluidscript 1
            circuit loop
            fluid water
            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30 dp=0
            PU1  pump
            P1   pipe dn=15 length=10
            P2   pipe dn=15 length=10 material=copper_en1057
            P3   pipe dn=15 length=10 material=steel_en10255
            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N5 - P2 - N6 - P3 - N1
            """);

        double Bore(string name) => Assert.IsType<PipeComponent>(lowered.Graph.Components.Single(c => c.Name == name)).InsideDiameter;

        // The script's catalogue is the shipped steel default: dn=15 is a 16.1 mm bore. The same
        // number under copper is the 15 mm tube, 13.0 mm.
        Assert.Equal(0.0161, Bore("P1"), 4);
        Assert.Equal(0.013, Bore("P2"), 4);
        Assert.Equal(0.0161, Bore("P3"), 4);
        Assert.Equal("copper_en1057", Assert.IsType<PipeComponent>(lowered.Graph.Components.Single(static c => c.Name == "P2")).Material);
    }

    [Fact]
    public void ASizedCopperPipeIsChosenFromTheCopperSeries()
    {
        // 0.2392 kg/s at 150 Pa/m: steel picks DN25 (27.3 mm bore, 94 Pa/m); copper's 28 mm tube has
        // a 25.6 mm bore, about 130 Pa/m at the same flow, and still meets the target -- the series
        // the sizer walked is copper's, which the bore proves.
        var lowered = GraphFixture.Lower(
            """
            fluidscript 1
            circuit loop
            fluid water
            HE1  heat_exchanger power=30 in.t=20 out.t=50
            LOAD heat_exchanger power=-30 dp=0
            PU1  pump
            P1   pipe length=10
            P2   pipe length=10 material=copper_en1057
            connections
            N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N5 - P2 - N1
            """);

        var steel = Assert.IsType<PipeComponent>(lowered.Graph.Components.Single(static c => c.Name == "P1"));
        var copper = Assert.IsType<PipeComponent>(lowered.Graph.Components.Single(static c => c.Name == "P2"));

        Assert.Equal(25, steel.SizedParameters["dn"].SiValue, 6);
        Assert.Equal(28, copper.SizedParameters["dn"].SiValue, 6);
        Assert.Equal(0.0256, copper.InsideDiameter, 6);
    }

    [Fact]
    public async Task AScriptOnTheCopperCatalogueReadsAnUnstatedMaterialAsCopper()
    {
        // The registry's default literal is the shipped default; the *script's* catalogue is what an
        // unstated material means, so a copper script must not fall back to steel through it.
        var resolved = PipeCatalogs.Resolve(new CatalogPin(CopperEn1057.Id, null));
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value, PipeCatalogs.All),
            OuterLoop.Rules(resolved.Value.Catalog, available: PipeCatalogs.All));
        var prepared = loop.Prepare(
            GraphFixture.Bind(
                """
                fluidscript 1
                catalog copper_en1057
                circuit loop
                fluid water
                HE1  heat_exchanger power=30 in.t=20 out.t=50
                LOAD heat_exchanger power=-30 dp=0
                PU1  pump
                P1   pipe dn=15 length=10
                P2   pipe dn=15 length=10 material=steel_en10255
                connections
                N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N5 - P2 - N1
                """),
            Water.Instance);

        double Bore(string name) => Assert.IsType<PipeComponent>(prepared.Lowered.Graph.Components.Single(c => c.Name == name)).InsideDiameter;

        Assert.Equal(0.013, Bore("P1"), 4);
        Assert.Equal(0.0161, Bore("P2"), 4);
        await Task.CompletedTask;
    }
}
