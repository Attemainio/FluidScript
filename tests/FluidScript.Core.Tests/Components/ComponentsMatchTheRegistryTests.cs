using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Language;

namespace FluidScript.Core.Tests.Components;

/// <summary>Every component's ports agree with the registry entry the binder reads.</summary>
/// <remarks>
/// <para>
/// <strong>The two are written down twice, and this is what stops them diverging.</strong> The binder
/// binds an unqualified connection against <see cref="ComponentKindInfo.Ports"/>, and the solver
/// indexes <see cref="SolveContext.Ports"/> against <see cref="IFlowComponent.Ports"/>. If the two
/// orders disagree, a script's second connection is wired to the wrong port and every number that
/// follows is confidently wrong — no exception, no diagnostic.
/// </para>
/// <para>
/// <c>22</c> asks for something stronger: the registry entry should be <em>built by</em> each
/// component's static registration, so the port list exists once. The registry shipped a phase before
/// any component did, and inverting that is a change to how the binder is fed rather than a change
/// here. Until then this test is the seam (<c>C-20</c>).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ComponentsMatchTheRegistryTests
{
    private static ComponentKindInfo Kind(string keyword) =>
        ComponentRegistry.Default.Kinds.Single(kind => kind.Keyword == keyword);

    /// <summary>Gets one instance of every kind whose port list is fixed.</summary>
    /// <value>
    /// The single source the theory and the two sweeps below all read. A <c>TheoryData</c> row is not
    /// indexable, so building the theory from this rather than the other way round keeps the list in one
    /// place.
    /// </value>
    private static ImmutableArray<(string Keyword, IFlowComponent Component)> FixedPort =>
    [
        ("pipe", new Pipe("P1", length: 10, insideDiameter: 0.0273)),
        ("heat_exchanger", new HeatExchanger("HX1", power: 1000)),
        ("valve", new Valve("TV1", kv: 6.3)),
        ("three_way_valve", new ThreeWayValve("TV2", kv: 6.3)),
        ("pump", new Pump("PU1", shutOffHead: 10, curvature: 2)),
    ];

    public static TheoryData<string, IFlowComponent> FixedPortKinds()
    {
        var data = new TheoryData<string, IFlowComponent>();

        foreach (var (keyword, component) in FixedPort)
        {
            data.Add(keyword, component);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixedPortKinds))]
    public void APortListMatchesItsRegistryEntryExactly(string keyword, IFlowComponent component)
    {
        var registry = Kind(keyword);

        Assert.Equal(keyword, component.Kind);
        Assert.Equal(
            registry.Ports.Select(static port => port.Name),
            component.Ports.Select(static port => port.Name));
        Assert.Equal(
            registry.Ports.Select(static port => port.Role),
            component.Ports.Select(static port => port.Role));
        Assert.Equal(
            registry.Ports.Select(static port => port.IsOptional),
            component.Ports.Select(static port => port.IsOptional));
    }

    [Fact]
    public void ANodeIsTheKindWithUnlimitedPorts()
    {
        // The registry declares no ports for a node and marks it unlimited instead, because the number
        // is whatever the script connected. So there is nothing to compare name-for-name; what must
        // hold is that the component agrees a node's ports are all bidirectional.
        var registry = Kind("node");
        var node = new CircuitNode("N1", portCount: 4, carriesMassBalance: true);

        Assert.True(registry.HasUnlimitedPorts);
        Assert.Empty(registry.Ports);
        Assert.Equal(4, node.Ports.Length);
        Assert.All(node.Ports, port => Assert.Equal(PortRole.Bidirectional, port.Role));
    }

    [Fact]
    public void ATanksFirstInletAndOutletMatchTheRegistryAndTheRestComeFromItsFamilies()
    {
        // in1 and out1 always exist and are in the registry's fixed list; in2 onwards materialize only
        // when named, which is what PortFamilies describes.
        var registry = Kind("tank");
        var minimal = new Tank("T1");

        Assert.Equal(
            registry.Ports.Select(static port => port.Name),
            minimal.Ports.Select(static port => port.Name));

        var extended = new Tank("T2", inletElevations: [0.0, 0.9], outletElevations: [0.3, 1.0]);

        Assert.Equal(["in1", "in2", "out1", "out2"], extended.Ports.Select(static port => port.Name));
        Assert.NotEmpty(registry.PortFamilies);
    }

    [Fact]
    public void EveryFlowComponentDeclaresAsManyEquationsAsItWrites()
    {
        // EquationCount is what the assembler sizes the residual span from, and DeclareEquations is what
        // names each row for a diagnostic. A component whose two disagreed would either overrun the span
        // or report a residual under the wrong name.
        var components = FixedPort
            .Select(static entry => entry.Component)
            .Concat<IFlowComponent>(
            [
                new CircuitNode("N1", 3, carriesMassBalance: true),
                new CircuitNode("N2", 2, carriesMassBalance: false),
                new Tank("T1"),
                new Tank("T2", inletElevations: [0.0, 0.9], outletElevations: [0.3, 1.0]),
            ])
            .ToImmutableArray();

        Assert.All(components, component => Assert.Equal(
            component.EquationCount,
            component.DeclareEquations().Length));
    }

    [Fact]
    public void EveryEquationNamesItsOwnerAndCarriesAResidualUnit()
    {
        // 31 makes the point that the mapping from residual row to component exists only at this layer:
        // "HX1 energy balance off by 4.2 kW" is actionable and "residual[17] = 4200" is not, and if this
        // layer does not carry the owner and the unit then no later one can recover them.
        foreach (var (_, component) in FixedPort)
        {
            Assert.All(component.DeclareEquations(), equation =>
            {
                Assert.Equal(component.Name, equation.OwnerComponentId);
                Assert.NotEmpty(equation.ResidualSiUnit);
                Assert.NotEmpty(equation.Name);
            });
        }
    }

    [Fact]
    public void OnlyTheKindsThatDriveFlowSayTheyDo()
    {
        // A pump drives flow; nothing else in v1 does. The registry is the authority and the property is
        // read by inference, so a component family added later that forgot it would silently produce a
        // circuit with no source.
        Assert.True(Kind("pump").DrivesFlow);

        foreach (var keyword in new[] { "pipe", "valve", "three_way_valve", "heat_exchanger", "tank", "node" })
        {
            Assert.False(Kind(keyword).DrivesFlow, $"'{keyword}' claims to drive flow.");
        }
    }
}
