using FluidScript.Core.Components;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Solvers.Equations;

public sealed partial class EquationSystem
{
    /// <summary>Re-evaluates one node's fluid state at the current iterate.</summary>
    /// <param name="node">The node's index in the graph.</param>
    /// <param name="x">The iterate.</param>
    /// <returns><see langword="false"/> when it left the property domain.</returns>
    /// <remarks>
    /// One node rather than all of them, because that is what a Jacobian column needs
    /// (<see cref="TryEvaluateAt"/>). This is the only place in a solve that calls the property backend,
    /// and every microsecond of a solve that is not linear algebra is spent here.
    /// </remarks>
    private bool Refresh(int node, ReadOnlySpan<double> x)
    {
        var state = _graph.Substance.FromPressureEnthalpy(
            Quantity.FromSi(x[Unknowns.NodePressure(node)], Dimension.Pressure),
            Quantity.FromSi(x[Unknowns.NodeEnthalpy(node)], Dimension.Enthalpy));

        if (!state.TryGetValue(out var fluid))
        {
            OutOfDomainNode = node;

            return false;
        }

        _nodeStates[node] = new PortState
        {
            Pressure = fluid.Pressure.SiValue,
            Enthalpy = fluid.Enthalpy.SiValue,
            Temperature = fluid.Temperature.SiValue,
            Density = fluid.Density.SiValue,
            SpecificHeat = fluid.SpecificHeat.SiValue,
            DynamicViscosity = fluid.DynamicViscosity.SiValue,
            ThermalConductivity = fluid.ThermalConductivity.SiValue,
        };

        return true;
    }

    /// <summary>Fills the scratch buffers with one component's port states and flows.</summary>
    /// <param name="element">The component's index in the graph.</param>
    /// <param name="x">The iterate.</param>
    /// <returns>How many ports were filled.</returns>
    private int Fill(int element, ReadOnlySpan<double> x)
    {
        var component = _graph.Components[element];
        var node = _nodeOf[element];

        for (var port = 0; port < component.Ports.Length; port++)
        {
            var binding = _ports[element, port];

            _flowScratch[port] = binding.CarriesFlow
                ? binding.Sign * x[Unknowns.BranchFlow(binding.Branch)]
                : 0;

            _portScratch[port] = node >= 0
                ? _nodeStates[node] with { Enthalpy = Arriving(element, port, x, node) }
                : binding.Node >= 0 ? _nodeStates[binding.Node] : Vacant;
        }

        if (node >= 0)
        {
            _ownScratch[NodeComponent.PressureIndex] = x[Unknowns.NodePressure(node)];
            _ownScratch[NodeComponent.EnthalpyIndex] = x[Unknowns.NodeEnthalpy(node)];
        }

        return component.Ports.Length;
    }

    /// <summary>Builds the context over the buffers <see cref="Fill"/> just wrote.</summary>
    /// <param name="element">The component's index in the graph.</param>
    /// <param name="ports">How many ports it has.</param>
    /// <param name="x">The iterate, which a component's own unknowns are sliced straight out of.</param>
    /// <returns>The context.</returns>
    /// <remarks>
    /// A node's two unknowns are copied into a scratch pair because they are not adjacent in the state
    /// vector — pressures and enthalpies are separate blocks, which is what gives the Jacobian its
    /// structure. A component's own unknowns <em>are</em> adjacent, by construction, so they are a slice
    /// of the iterate and cost nothing to pass (<c>D-74</c>).
    /// </remarks>
    private SolveContext Context(int element, int ports, ReadOnlySpan<double> x)
    {
        var unknowns = _nodeOf[element] >= 0
            ? _ownScratch.AsSpan()
            : x.Slice(Unknowns.ComponentUnknownOffset + _owned[element].Offset, _owned[element].Count);

        return new SolveContext(
            _graph.Substance,
            _portScratch.AsSpan(0, ports),
            _flowScratch.AsSpan(0, ports),
            unknowns,
            _parameters[element]);
    }

    /// <summary>The enthalpy arriving at one node port from whatever is attached to it.</summary>
    /// <param name="element">The node's index in the graph.</param>
    /// <param name="port">The port.</param>
    /// <param name="x">The iterate.</param>
    /// <param name="node">The node's own index.</param>
    /// <returns>J/kg.</returns>
    /// <remarks>
    /// <para>
    /// A node's port states belong to what is attached to it, and a node's energy balance reads them as
    /// the enthalpy an inflow carries. Crossing a two-port flow group gives that unambiguously — it is
    /// the node on the component's far side, and 68 of the corpus's 92 node ports are this case. A
    /// junction element has no single far side, so the arriving enthalpy is the **inflow-weighted mix**
    /// of the nodes at its other ports, which is what a mixing tee physically does:
    /// <c>Σ ṁᵢ hᵢ / Σ ṁᵢ</c> over the ports that flow in.
    /// </para>
    /// <para>
    /// <strong>The weight is the inflow itself, smoothed to zero across a reversal</strong> —
    /// <c>ṁ · ForwardShare(ṁ)</c>, which is <c>max(0, ṁ)</c> away from zero and C¹ through it as
    /// <c>36</c> requires. It was <see cref="Smoothing.ForwardShare"/> alone until <c>S-58</c>, and
    /// that is a 0-to-1 step that reads 1 for every inflow above one gram per second: a mixing valve
    /// passing 0.167 kg/s of 80 °C water and 0.063 kg/s of 30 °C water then delivered <em>55 °C</em>,
    /// the plain average, where the mass-weighted mix is 66 °C. Its position moved the split and the
    /// split moved nothing, so every constraint on a mixed temperature drove the valve to a stop. A
    /// small floor keeps the quotient defined when every other port is an outflow — a state the
    /// junction's own mass balance forbids at the solution but not on the path to it.
    /// </para>
    /// </remarks>
    private double Arriving(int element, int port, ReadOnlySpan<double> x, int node)
    {
        // Pinned view: a port of a tank delivers its layer, whatever flows into the tank elsewhere.
        var (attachedComponent, attachedPort) = _attached[element][port];

        if (attachedComponent >= 0 && !double.IsNaN(_portPin[attachedComponent][attachedPort]))
        {
            return _portPin[attachedComponent][attachedPort];
        }

        var sources = _arriving[element][port];

        if (sources.Length == 0)
        {
            return x[Unknowns.NodeEnthalpy(node)];
        }

        if (sources.Length == 1)
        {
            return x[Unknowns.NodeEnthalpy(sources[0].Node)] - sources[0].Lift;
        }

        var numerator = MixingFloor * x[Unknowns.NodeEnthalpy(node)];
        var denominator = MixingFloor;

        foreach (var source in sources)
        {
            var binding = _ports[source.Component, source.Port];
            var inflow = binding.CarriesFlow
                ? binding.Sign * x[Unknowns.BranchFlow(binding.Branch)]
                : 0;

            var weight = inflow * Smoothing.ForwardShare(inflow);

            numerator += weight * (x[Unknowns.NodeEnthalpy(source.Node)] - source.Lift);
            denominator += weight;
        }

        return numerator / denominator;
    }
}
