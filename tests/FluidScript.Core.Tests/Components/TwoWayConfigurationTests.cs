using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Tests.Topology;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Components;

/// <summary>
/// A three-way valve whose optional bypass nothing connects is a two-way valve (<c>S-14a</c>).
/// </summary>
/// <remarks>
/// The registry has always made port <c>c</c> optional and the page has always said that leaving it
/// open is how a two-way valve is written. What the component did with that was declare three
/// equations regardless, including a Kv law reading a port with no node behind it — so
/// <c>m2-distribution-header</c> and <c>m1-syntax-tour</c> reported square while the system they would
/// assemble was over-specified by two. Nothing failed, because nothing assembled yet.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TwoWayConfigurationTests
{
    private static readonly ConstantPropertyWater Water = ConstantPropertyWater.Instance;

    [Fact]
    public void WiredAsATwoWayItIsAPassThroughWithOneKvLaw()
    {
        var valve = new ThreeWayValve("TV1", kv: 6.3, bypassConnected: false);

        Assert.Equal(["a", "b"], valve.Ports.Select(static port => port.Name));
        Assert.Equal([0, 0], valve.FlowGroups);
        Assert.Equal(1, valve.EquationCount);
        Assert.Equal("two_way", valve.Mode);

        // One group of two is a pass-through, so a branch walks straight through it and the branch's
        // single flow makes the mass balance an identity -- which is why the row is gone rather than
        // present and always zero.
        Assert.False(CircuitGraph.IsJunctionElement(valve));
    }

    [Fact]
    public void WiredAsAThreeWayNothingChanges()
    {
        var valve = new ThreeWayValve("TV1", kv: 6.3);

        Assert.Equal(["a", "b", "c"], valve.Ports.Select(static port => port.Name));
        Assert.Equal([0, 0, 0], valve.FlowGroups);
        Assert.Equal(3, valve.EquationCount);
        Assert.Equal("three_way", valve.Mode);
        Assert.True(CircuitGraph.IsJunctionElement(valve));
    }

    [Fact]
    public void TheTwoWayResidualIsTheSameControlledPathTheThreeWayWrites()
    {
        // Not a new law: the row a two-way valve keeps is the a-b Kv relation the three-way already
        // had, at the same opening and the same drop. A different number here would mean the two-way
        // form had quietly become a different component.
        var threeWay = new ThreeWayValve("TV1", kv: 6.3, position: 0.4);
        var twoWay = new ThreeWayValve("TV1", kv: 6.3, position: 0.4, bypassConnected: false);

        Span<double> full = stackalloc double[threeWay.EquationCount];
        threeWay.EvaluateResiduals(
            new SolveContext(Water, [At(300_000), At(250_000), At(250_000)], [0.5, -0.3, -0.2]), full);

        Span<double> reduced = stackalloc double[twoWay.EquationCount];
        twoWay.EvaluateResiduals(
            new SolveContext(Water, [At(300_000), At(250_000)], [0.3, -0.3]), reduced);

        Assert.Equal(full[1], reduced[0], tolerance: 1e-12);
    }

    [Fact]
    public void LoweringDecidesItFromTheConnectionsAndNotFromTheScript()
    {
        // The decision is topology's, exactly as an exchanger's mode is. Two connections and no
        // qualified `c` is a two-way; a third connection makes it a three-way with nothing else
        // changing in the script.
        const string TwoWay = """
            fluidscript 1

            fluid water

            circuit c 100

            N1  node p=300
            N2  node p=280
            TV1 three_way_valve kv=6.3

            connections
            N1 - TV1 - N2
            """;

        var valve = Valve(GraphFixture.Lower(TwoWay).Graph);

        Assert.Equal("two_way", valve.Mode);
        Assert.Equal(1, valve.EquationCount);

        var threeWay = Valve(GraphFixture.Lower(TwoWay.Replace(
            "N1 - TV1 - N2", "N1 - TV1 - N2\nTV1.c - N3", StringComparison.Ordinal)).Graph);

        Assert.Equal("three_way", threeWay.Mode);
        Assert.Equal(3, threeWay.EquationCount);
    }

    private static ThreeWayValve Valve(CircuitGraph graph) =>
        graph.Components.OfType<ThreeWayValve>().Single();

    private static PortState At(double pressure)
    {
        var state = Water.FromPressureTemperature(
            Quantity.FromSi(pressure, Dimension.Pressure),
            Quantity.FromSi(293.15, Dimension.Temperature));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return new PortState
        {
            Pressure = state.Value.Pressure.SiValue,
            Enthalpy = state.Value.Enthalpy.SiValue,
            Temperature = state.Value.Temperature.SiValue,
            Density = state.Value.Density.SiValue,
            SpecificHeat = state.Value.SpecificHeat.SiValue,
            DynamicViscosity = state.Value.DynamicViscosity.SiValue,
            ThermalConductivity = state.Value.ThermalConductivity.SiValue,
        };
    }
}
