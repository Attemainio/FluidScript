using FluidScript.Core.Binding;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Topology;


/// <summary>Binds a script and lowers it, which every test in this folder starts with.</summary>
public static class GraphFixture
{
    /// <summary>Binds a script, asserting it produced no errors.</summary>
    /// <param name="source">The script, with its <c>fluidscript 1</c> header already on it.</param>
    /// <returns>The bound model.</returns>
    public static SemanticModel Bind(string source)
    {
        var result = new Binder(ComponentRegistry.Default)
            .Bind(FluidScriptParser.Parse(new SourceText(source)), "script");

        Assert.True(
            result.Diagnostics.All(static d => d.Severity != Core.Diagnostics.DiagnosticSeverity.Error),
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        return result.Model;
    }

    /// <summary>The shipped catalogue's bores, resolved the way a solve resolves them.</summary>
    /// <returns>A lookup over the default pipe catalogue.</returns>
    /// <remarks>
    /// <para>
    /// This was a six-row hand-written fixture until <c>P3.5</c> verified the real table, and the two
    /// disagreed: the fixture gave DN32 a 35.9 mm bore where the catalogue and <c>27</c>'s own gradient
    /// table give 36.0, and DN40 41.8 against 41.9. Neither number was wrong for the standard it was
    /// drawn from -- the fixture said EN 10220 and the catalogue is EN 10255 -- but two tables in one
    /// repository quietly answering the same question differently is how the real one gets doubted
    /// (<c>C-32</c>).
    /// </para>
    /// <para>
    /// It resolves through <see cref="PipeCatalogs.Resolve"/> rather than reading the instance,
    /// because that is the path a solve takes and it is the one that enforces provenance. If the
    /// shipped rows ever stop being verified, every test in this folder says so.
    /// </para>
    /// </remarks>
    public static IBoreLookup Bores()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return new CatalogBoreLookup(resolved.Value.Catalog);
    }

    /// <summary>Binds and lowers a script.</summary>
    /// <param name="source">The script.</param>
    /// <returns>The lowering result, graph and all.</returns>
    public static LoweringResult Lower(string source) =>
        Lowering.Lower(
            Bind(source),
            ConstantPropertyWater.Instance,
            new ComponentFactory(Bores()));

    /// <summary>The cooling loop <c>23</c> tabulates, and <c>01</c> gives solved values for.</summary>
    /// <remarks>
    /// Six nodes, ten components, four branches and one loop. <c>N2</c> comes from rule I1 and the
    /// three <c>__</c> nodes from I2; <c>3WV</c>'s three ports are all connected and so are both ports
    /// of every two-port component, so I3 does not fire at all.
    /// </remarks>
    public const string CoolingLoop = """
        fluidscript 1
        circuit cooling 100
        fluid water

        N1 node t=6 p=300
        N3 node p=280
        PU1 pump head=6 flow=0.24
        HE1 heat_exchanger power=30
        3WV three_way_valve kv=6.3
        P1 pipe length=10 dn=25

        connections
        N1 - N2
        N2 - PU1
        PU1 - HE1
        HE1 - 3WV
        3WV - N2
        3WV - P1
        P1 - N3
        """;
}
