using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Tests.Topology;

namespace FluidScript.Core.Tests.Components;

/// <summary>
/// <c>22</c>'s invariant 3: residual evaluation is deterministic and free of side effects.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The cheapest invariant to state and the easiest to lose.</strong> A component that caches a
/// property lookup, remembers its last flow direction, or lazily fills a field on first use still
/// passes every test that evaluates it once. It fails the moment the solver evaluates it N+1 times per
/// Newton iteration with a perturbed unknown, and the symptom is a Jacobian column that is subtly
/// wrong rather than an exception.
/// </para>
/// <para>
/// Run over the components a real lowering produces rather than over hand-built ones, so a component
/// kind added to the registry is covered without anyone remembering to add it here.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ResidualDeterminismTests
{
    private static readonly ISubstance Water = ConstantPropertyWater.Instance;

    private static PortState State(double enthalpy, double pressure) => new()
    {
        Pressure = pressure,
        Enthalpy = enthalpy,
        Temperature = 293.15,
        Density = ConstantPropertyWater.DensityValue,
        SpecificHeat = ConstantPropertyWater.SpecificHeatValue,
        DynamicViscosity = ConstantPropertyWater.DynamicViscosityValue,
        ThermalConductivity = ConstantPropertyWater.ThermalConductivityValue,
    };

    /// <summary>Evaluates one component at a fixed, arbitrary but reproducible iterate.</summary>
    /// <param name="component">The component to evaluate.</param>
    /// <returns>Its residual vector.</returns>
    /// <remarks>
    /// The port states are deliberately unequal, so a component that ignores its inputs and returns
    /// zeros would still have to return the <em>same</em> zeros twice — which it would — while one that
    /// mixes or upwinds produces numbers that differ port to port and so could differ run to run.
    /// </remarks>
    private static double[] Evaluate(IFlowComponent component)
    {
        var ports = new PortState[component.Ports.Length];
        var flows = new double[component.Ports.Length];

        for (var port = 0; port < ports.Length; port++)
        {
            ports[port] = State(80_000 + (port * 10_000), 300_000 - (port * 5_000));
            flows[port] = port % 2 == 0 ? 0.24 : -0.24;
        }

        var residuals = new double[component.EquationCount];
        var unknowns = new double[component.DeclareUnknowns().Length];

        component.EvaluateResiduals(new SolveContext(Water, ports, flows, unknowns), residuals);

        return residuals;
    }

    private static IFlowComponent[] Components() =>
        [.. GraphFixture.Lower(GraphFixture.CoolingLoop).Graph.Components];

    [Fact]
    public void EvaluatingTheSameComponentTwiceGivesTheSameResidualsBitForBit()
    {
        // Bit for bit rather than within a tolerance: two evaluations of one function at one iterate
        // have no reason to differ in the last place, and a tolerance here would hide exactly the
        // cached-and-stale value the invariant exists to catch.
        foreach (var component in Components())
        {
            Assert.Equal(Evaluate(component), Evaluate(component));
        }
    }

    [Fact]
    public void EvaluatingEveryOtherComponentInBetweenChangesNothing()
    {
        // Side-effect freedom across components, not just within one: static or shared mutable state
        // shows up here and nowhere else, and the solver interleaves every component's evaluation.
        var components = Components();

        foreach (var component in components)
        {
            var first = Evaluate(component);

            foreach (var other in components)
            {
                Evaluate(other);
            }

            Assert.Equal(first, Evaluate(component));
        }
    }

    [Fact]
    public void EvaluationLeavesTheComponentsOwnParametersAlone()
    {
        // A residual reads the iterate and writes the residual span. Anything it writes to the
        // component would make the next outer sizing iteration start from a state nobody chose.
        foreach (var component in Components())
        {
            var stated = component.StatedParameters;
            var defaults = component.DefaultParameters;
            var equations = component.EquationCount;

            Evaluate(component);

            Assert.Same(stated, component.StatedParameters);
            Assert.Same(defaults, component.DefaultParameters);
            Assert.Equal(equations, component.EquationCount);
        }
    }
}
