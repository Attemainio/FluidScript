using FluidScript.Core.Binding;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;
using FluidScript.Core.Topology;

namespace FluidScript.Core.Tests.Topology;

/// <summary>The four DN sizes this tree's reference circuits use, as a stand-in for the catalogue.</summary>
/// <remarks>
/// <para>
/// <strong>This is a fixture, not a catalogue.</strong> <c>27</c> owns the real table, with two public
/// sources per row, and it ships in <c>P3.5</c>; lowering exists a package earlier and needs a bore to
/// build a pipe at all (<c>C-24</c>). Four rows of EN 10220 medium-series steel are enough for every
/// circuit under test and small enough that nobody mistakes it for the thing it stands in for.
/// </para>
/// <para>
/// The numbers matter even here: <strong>DN25 is a 27.3 mm bore, not 25 mm.</strong> Using the
/// designation as a diameter is a 16 % area error and roughly a factor of two in pressure gradient,
/// with nothing in the result looking wrong — which is exactly why a fixture that quietly returned
/// <c>dn / 1000</c> would be worse than no fixture.
/// </para>
/// </remarks>
public sealed class ReferenceBores : IBoreLookup
{
    /// <inheritdoc/>
    public double? BoreFor(double nominalDiameter) => nominalDiameter switch
    {
        15 => 16.1e-3,
        20 => 21.7e-3,
        25 => 27.3e-3,
        32 => 35.9e-3,
        40 => 41.8e-3,
        50 => 53.1e-3,
        _ => null,
    };
}

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

    /// <summary>Binds and lowers a script.</summary>
    /// <param name="source">The script.</param>
    /// <returns>The lowering result, graph and all.</returns>
    public static LoweringResult Lower(string source) =>
        Lowering.Lower(
            Bind(source),
            ConstantPropertyWater.Instance,
            new ComponentFactory(new ReferenceBores()));

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
