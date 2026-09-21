using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Topology;

/// <summary><c>D-130</c>, <c>70</c> R3: one answer to who owns a parameter, and the factory's three maps it is read from.</summary>
[Trait("Category", "Unit")]
public sealed class OwnershipTests
{
    private const string Script = """
        fluidscript 1
        circuit simpleLoop
        fluid water

        HE1  heat_exchanger power=30 in.t=20 out.t=50
        LOAD heat_exchanger dp=0
        CV1  valve
        PU1  pump head=15
        P1   pipe length=25 dn=25

        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5 - P1 - N1
        """;

    private static IFlowComponent Component(CircuitGraph graph, string name) =>
        Assert.IsAssignableFrom<IFlowComponent>(graph.Components.Single(component => component.Name == name));

    [Fact]
    public void EachStateIsReadFromTheMapsInThePrecedenceTheModelStates()
    {
        // The bootstrap lowering: the script's numbers are stated, the registry's visible defaults are
        // decided, every unstated valve carries a placeholder Kv so the valve exists (`D-96`), and a
        // parameter nothing has touched is free.
        var graph = GraphFixture.Lower(Script).Graph;

        Assert.Equal(ParameterState.Stated, Ownership.Of(Component(graph, "PU1"), "head", graph.ProvisionalParameters));
        Assert.Equal(ParameterState.Defaulted, Ownership.Of(Component(graph, "P1"), "roughness", graph.ProvisionalParameters));
        Assert.Equal(ParameterState.SizedProvisional, Ownership.Of(Component(graph, "CV1"), "kv", graph.ProvisionalParameters));
        // The bootstrap already fills an omitted duty from the closed-circuit balance, and that is a rule's
        // choice, not a placeholder; what nothing has decided is a parameter no map holds at all.
        Assert.Equal(ParameterState.SizedFinal, Ownership.Of(Component(graph, "LOAD"), "power", graph.ProvisionalParameters));
        Assert.Equal(ParameterState.Free, Ownership.Of(Component(graph, "PU1"), "kv", graph.ProvisionalParameters));

        // A promotion outranks the placeholder it keeps: the label set says so, the maps cannot.
        var promoted = new HashSet<string>(StringComparer.Ordinal) { Ownership.Key("CV1", "kv") };
        Assert.Equal(ParameterState.Promoted, Ownership.Of(Component(graph, "CV1"), "kv", graph.ProvisionalParameters, promoted));

        // A stated value is never promoted, whatever the label set claims.
        promoted.Add(Ownership.Key("PU1", "head"));
        Assert.Equal(ParameterState.Stated, Ownership.Of(Component(graph, "PU1"), "head", graph.ProvisionalParameters, promoted));
    }

    [Fact]
    public void ARuleSizedValueIsFinalAndNoLongerFree()
    {
        var chosen = SizingOverlay.Empty.With("CV1", "kv", Quantity.FromSi(1.6, Dimension.Kv));
        var lowered = Lowering.Lower(
            GraphFixture.Bind(Script), ConstantPropertyWater.Instance, new ComponentFactory(GraphFixture.Bores(), chosen));

        var state = Ownership.Of(Component(lowered.Graph, "CV1"), "kv", lowered.Graph.ProvisionalParameters);

        Assert.Equal(ParameterState.SizedFinal, state);
        Assert.False(Ownership.IsFree(state));
        Assert.True(Ownership.IsFree(ParameterState.SizedProvisional));
        Assert.True(Ownership.IsFree(ParameterState.Free));
    }

    [Fact]
    public void TheFactoryKeepsAStatedValueOutOfTheSizedMap()
    {
        // `24`'s invariant 1 at the factory: an overlay entry for a stated or defaulted parameter is dropped
        // rather than applied, so a sizer reaching past the user's own number cannot land in the maps.
        var chosen = SizingOverlay.Empty
            .With("PU1", "head", Quantity.FromSi(3, Dimension.Head))
            .With("P1", "roughness", Quantity.FromSi(1e-3, Dimension.Length))
            .With("CV1", "kv", Quantity.FromSi(1.6, Dimension.Kv));
        var graph = Lowering.Lower(
            GraphFixture.Bind(Script), ConstantPropertyWater.Instance, new ComponentFactory(GraphFixture.Bores(), chosen)).Graph;

        var pump = Component(graph, "PU1");
        Assert.Equal(15, pump.StatedParameters["head"].SiValue);
        Assert.False(pump.SizedParameters.ContainsKey("head"));

        var pipe = Component(graph, "P1");
        Assert.True(pipe.DefaultParameters.ContainsKey("roughness"));
        Assert.False(pipe.SizedParameters.ContainsKey("roughness"));

        var valve = Component(graph, "CV1");
        Assert.Equal(1.6, valve.SizedParameters["kv"].SiValue);
        Assert.False(valve.StatedParameters.ContainsKey("kv"));
    }
}
