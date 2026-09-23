using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Results;

/// <summary>What a component or a run of them resists at a flow, by their own laws.</summary>
/// <remarks>
/// <para>
/// <strong>Each component states its own drop, and none of them is asked how.</strong> A pressure
/// residual is written <c>p_in − p_out − Δp(law) = 0</c>, so evaluating it over a <em>flat</em>
/// pressure field leaves exactly <c>−Δp(law)</c> — the component's own contribution at that flow, from
/// the same code the solver runs. No kind appears here, a pump's rise comes out negative because that
/// is what a pump does to a loop, and a component added later is covered the day it declares a
/// pressure equation.
/// </para>
/// <para>
/// Extracted from <see cref="OuterLoop"/> so the seed can use it too. Sizing needs a run's total and
/// the seed needs each element in turn, and writing the rule twice is how this repository has already
/// lost two sessions (<c>D-86</c> was one rule in four places).
/// </para>
/// </remarks>
public static class BranchResistance
{
    /// <summary>The pressure step the port-pressure solve differentiates its laws over.</summary>
    /// <value>
    /// Pa. One pascal — small against <c>Tolerances.PressureScale</c>, and inside the range over which
    /// a valve law is regularized, so the chord it measures is taken where the law has a finite slope
    /// rather than the infinite one <c>√Δp</c> has at the origin.
    /// </value>
    private const double Probe = 1;

    /// <summary>How many Newton steps the port-pressure solve takes before giving up.</summary>
    /// <value>Twenty. A linear relation is done in one and a square-root law in a handful.</value>
    private const int Steps = 20;

    /// <summary>The step below which the port-pressure solve is finished.</summary>
    /// <value>Pa. One pascal, which is nothing next to any pressure a circuit runs at.</value>
    private const double Settled = 1;

    /// <summary>What one component resists at a flow.</summary>
    /// <param name="graph">The graph, for its substance.</param>
    /// <param name="state">The fluid to evaluate the law against.</param>
    /// <param name="element">The component.</param>
    /// <param name="flow">kg/s through it.</param>
    /// <param name="parameters">
    /// The component's resolvable parameters as the caller holds them -- a promoted Kv or head the
    /// seed has already chosen -- or <see langword="null"/> to evaluate against the component's own
    /// values. Indexed as <c>IFlowComponent.Resolvable</c> declares them (<c>S-66</c>).
    /// </param>
    /// <param name="bound">Pa. The magnitude the drop is clamped to when the law has to be solved for it; see <see cref="Across"/>.</param>
    /// <returns>Pa, positive against the flow; zero where its own laws determine none.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Negating a pressure residual covers only half the components, and the half it misses is
    /// the half a heating circuit is steered by</strong> (<c>S-46</c>). A valve does not write a drop in
    /// pascals: it writes <c>ṁ − Kv·φ(x)·√(Δp·ρ) = 0</c>, an <c>EquationKind.ComponentConstraint</c>
    /// whose residual is in kg/s. A reader that filters on <c>EquationKind.Pressure</c> and negates what
    /// it finds therefore returns <strong>zero for every valve in the circuit</strong> — silently, and
    /// with no kind named anywhere to make the omission visible. What a valve resists has to be
    /// <em>solved for</em> rather than read off, which is what <see cref="Across"/> does.
    /// </para>
    /// <para>
    /// The row is found by its position in the component's own residual buffer, not by
    /// <c>EquationDeclaration.Index</c>: that index is assigned at assembly, and a tank declares its
    /// first pressure row with an index of 0 while writing it third, behind its energy and mass rows.
    /// Reading by it returned a tank's energy imbalance in watts as though it were a pressure.
    /// </para>
    /// </remarks>
    public static double Of(
        CircuitGraph graph,
        FluidState state,
        IFlowComponent element,
        double flow,
        double[]? parameters = null,
        double bound = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(element);

        if (element.EquationCount == 0)
        {
            return 0;
        }

        var ordinal = PressureRow(element);

        if (ordinal < 0)
        {
            // No row in pascals. A two-port component still has one drop across it, and solving its own
            // law for the port pressures is the only way to learn what that drop is.
            var solved = element.Ports.Length == 2
                ? Across(graph, state, element, [flow, -flow], parameters, bound)
                : [0, 0];

            return solved[0] - solved[1];
        }

        var ports = new PortState[element.Ports.Length];
        var flows = new double[element.Ports.Length];
        var residuals = new double[element.EquationCount];

        Array.Fill(ports, Flat(state));
        Array.Fill(flows, flow);

        element.EvaluateResiduals(
            new SolveContext(graph.Substance, ports, flows, Own(element, state), parameters), residuals);

        return double.IsFinite(residuals[ordinal]) ? -residuals[ordinal] : 0;
    }

    /// <summary>The pressures a component's own laws put on its ports at the flows it carries.</summary>
    /// <param name="graph">The graph, for its substance.</param>
    /// <param name="state">The fluid to evaluate the laws against.</param>
    /// <param name="element">The component.</param>
    /// <param name="flows">kg/s into the component at each of its ports, one entry per port.</param>
    /// <param name="parameters">
    /// The component's resolvable parameters as the caller holds them, or <see langword="null"/> for
    /// its own values; see <see cref="Of"/>.
    /// </param>
    /// <param name="bound">
    /// Pa. The magnitude no port offset exceeds; the seed passes
    /// <see cref="Tolerances.SeedValveExcursion"/> because the flows it reads the laws at are its own
    /// guesses, and sizing passes nothing because its flows are solved (<c>S-47</c>).
    /// </param>
    /// <returns>
    /// Pa at each port, relative to port 0, which is zero. All zero where the component's own laws do
    /// not determine them.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>A junction element's ports are not at one pressure, and assuming they are is how a
    /// three-way valve loses its column</strong> (<c>S-25</c>, <c>S-35</c>). Its two Kv laws are exactly
    /// what sets <c>ab</c> against <c>a</c> and <c>b</c>, so its ports are known the moment its flows
    /// are — by solving those laws, since they are written in kg/s and cannot be read as pressures.
    /// </para>
    /// <para>
    /// <strong>Which rows determine the ports is discovered, not declared.</strong> Every row is
    /// differentiated with respect to every port pressure; the rows that move are the ones that have
    /// something to say about pressure, whatever kind they call themselves. A mass balance does not
    /// move and a tank's equal-pressure rows do. The port pressures are determined exactly when that
    /// count is one short of the port count — one short because a pressure field has a datum, and this
    /// returns differences with port 0 as its own. Anything else (a four-port exchanger, whose two
    /// sides are two independent runs) returns zeros rather than an invented answer.
    /// </para>
    /// <para>
    /// Newton with a forward-difference Jacobian, on a system of at most a handful of rows. A tank's
    /// rows are linear and settle in one step; a valve's square root takes a few more. Twenty is the
    /// cap, and an unconverged or singular solve returns zeros — a seed may be wrong, and a wrong seed
    /// is better than a guess dressed as a law.
    /// </para>
    /// </remarks>
    public static double[] Across(
        CircuitGraph graph,
        FluidState state,
        IFlowComponent element,
        double[] flows,
        double[]? parameters = null,
        double bound = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(flows);

        var count = element.Ports.Length;
        var pressures = new double[count];

        if (count < 2 || flows.Length != count || element.EquationCount == 0)
        {
            return pressures;
        }

        var unknowns = Own(element, state);
        var ports = new PortState[count];
        var residuals = new double[element.EquationCount];
        var probed = new double[element.EquationCount];
        var jacobian = new double[element.EquationCount * (count - 1)];
        int[]? rows = null;

        for (var step = 0; step < Steps; step++)
        {
            Fill(ports, state, pressures);
            element.EvaluateResiduals(
                new SolveContext(graph.Substance, ports, flows, unknowns, parameters), residuals);

            for (var port = 1; port < count; port++)
            {
                pressures[port] += Probe;
                Fill(ports, state, pressures);
                element.EvaluateResiduals(
                    new SolveContext(graph.Substance, ports, flows, unknowns, parameters), probed);
                pressures[port] -= Probe;

                for (var row = 0; row < residuals.Length; row++)
                {
                    jacobian[(row * (count - 1)) + port - 1] = (probed[row] - residuals[row]) / Probe;
                }
            }

            rows ??= Determined(jacobian, residuals.Length, count - 1);

            if (rows.Length != count - 1)
            {
                return new double[count];
            }

            var matrix = new double[rows.Length * rows.Length];
            var correction = new double[rows.Length];

            for (var row = 0; row < rows.Length; row++)
            {
                correction[row] = -residuals[rows[row]];

                for (var column = 0; column < rows.Length; column++)
                {
                    matrix[(row * rows.Length) + column] = jacobian[(rows[row] * (count - 1)) + column];
                }
            }

            var factored = DenseLu.Factor(matrix, rows.Length);

            if (factored.IsSingular)
            {
                return new double[count];
            }

            factored.Solve(correction);

            var moved = 0.0;

            for (var port = 1; port < count; port++)
            {
                if (!double.IsFinite(correction[port - 1]))
                {
                    return new double[count];
                }

                pressures[port] += correction[port - 1];
                moved = Math.Max(moved, Math.Abs(correction[port - 1]));
            }

            if (moved < Settled)
            {
                break;
            }
        }

        // Bounded only where the caller asks, and a nearly-shut valve is why the seed asks. A valve
        // passes the flow it is given at a drop of (m/(Kv phi))^2/rho, and phi of a bypass at position 1
        // is a couple of percent, so a flow the seed invented -- not one the circuit settles at -- is
        // bought at megapascals: measured, the header's TV_AHU bypass wanted -6.8 MPa and
        // m2-cooling-loop's 3WV -11.8 MPa, which put a node far outside the range water is defined
        // over and ended the seed in a failed property call rather than a bad guess. The law gives the
        // direction and the ordering of the ports, which is what a seed needs from it; the excursion
        // is capped, as `SolutionSeed.Band` caps the temperature walk. Only a valve reaches this
        // clamp: every other component writes a pressure row and is read off it. Sizing reads the
        // same laws at solved flows and passes no bound (`S-47`).
        for (var port = 1; port < count; port++)
        {
            pressures[port] = Math.Clamp(pressures[port], -bound, bound);
        }

        return pressures;
    }

    /// <summary>Where a component's pressure row sits in its own residual buffer.</summary>
    /// <param name="element">The component.</param>
    /// <returns>Its position, or -1 when the component writes no pressure row.</returns>
    private static int PressureRow(IFlowComponent element)
    {
        var declarations = element.DeclareEquations();

        for (var ordinal = 0; ordinal < declarations.Length; ordinal++)
        {
            if (declarations[ordinal].Kind == EquationKind.Pressure)
            {
                return ordinal;
            }
        }

        return -1;
    }

    /// <summary>Lays a pressure field over a component's ports, leaving their properties real.</summary>
    /// <param name="ports">The port states being filled.</param>
    /// <param name="state">The fluid.</param>
    /// <param name="pressures">Pa at each port.</param>
    private static void Fill(PortState[] ports, FluidState state, double[] pressures)
    {
        for (var port = 0; port < ports.Length; port++)
        {
            ports[port] = Flat(state) with { Pressure = pressures[port] };
        }
    }

    /// <summary>Which rows of a component say anything about its port pressures.</summary>
    /// <param name="jacobian">Row-major sensitivities, one column per port after the first.</param>
    /// <param name="rows">How many rows it holds.</param>
    /// <param name="columns">How many columns it holds.</param>
    /// <returns>The rows with a non-zero entry, in order.</returns>
    private static int[] Determined(double[] jacobian, int rows, int columns)
    {
        var determined = new List<int>();

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (jacobian[(row * columns) + column] != 0)
                {
                    determined.Add(row);
                    break;
                }
            }
        }

        return [.. determined];
    }

    /// <summary>A component's own unknowns, at the same flat state its ports are given.</summary>
    /// <param name="element">The component.</param>
    /// <param name="state">The fluid.</param>
    /// <returns>One value per declared unknown, in SI, in declaration order.</returns>
    /// <remarks>
    /// <strong>An empty span is not a neutral one: a component that owns an unknown reads it by index,
    /// and reads it before it looks at anything else.</strong> A tank mixes its ports against its own
    /// stored enthalpy, so evaluating one against no unknowns is an <c>IndexOutOfRangeException</c> out
    /// of the middle of the seed — and a seed that throws is a pipeline stage throwing on user input.
    /// Seeded by declared unit rather than by kind, for the reason <c>D-74</c> gives: nothing here
    /// should know what a tank is. A pressure unknown takes zero, to match <see cref="Flat"/>.
    /// </remarks>
    private static double[] Own(IFlowComponent element, FluidState state)
    {
        var declarations = element.DeclareUnknowns();
        var unknowns = new double[declarations.Length];

        for (var index = 0; index < declarations.Length; index++)
        {
            unknowns[index] = declarations[index].SiUnit switch
            {
                "J/kg" => state.Enthalpy.SiValue,
                "K" => state.Temperature.SiValue,
                _ => 0,
            };
        }

        return unknowns;
    }

    /// <summary>What a run of components resists at a flow.</summary>
    /// <param name="graph">The graph, for its substance.</param>
    /// <param name="state">The fluid to evaluate the laws against.</param>
    /// <param name="path">The components along the run.</param>
    /// <param name="flow">kg/s through them.</param>
    /// <param name="exclude">The component whose own contribution is left out, if any.</param>
    /// <returns>Pa, positive against the flow.</returns>
    public static double Along(
        CircuitGraph graph,
        FluidState state,
        ImmutableArray<IFlowComponent> path,
        double flow,
        IFlowComponent? exclude)
    {
        var total = 0.0;

        foreach (var element in path)
        {
            if (!ReferenceEquals(element, exclude))
            {
                total += Of(graph, state, element, flow);
            }
        }

        return total;
    }

    /// <summary>What a branch resists at a flow, its ends included and its bare links counted.</summary>
    /// <param name="graph">The graph, for its substance.</param>
    /// <param name="state">The fluid to evaluate the laws against.</param>
    /// <param name="branch">The branch.</param>
    /// <param name="flow">kg/s through it.</param>
    /// <param name="exclude">The component whose own contribution is left out, if any.</param>
    /// <returns>Pa, positive against the flow.</returns>
    /// <remarks>
    /// <strong>A bare connection between two nodes at different heights resists nothing and still
    /// changes the pressure</strong> (<c>D-70</c>): it carries <c>ρgΔz</c> with no component to say so,
    /// exactly as the assembler writes its row. Left out here, a loop that climbs through a pipe and
    /// comes down through a link would size its pump to the riser alone — measured at 45.8 m of head
    /// on a loop whose friction is 5.3 m — and the valve against a drop the solve never produces.
    /// </remarks>
    public static double Along(
        CircuitGraph graph,
        FluidState state,
        Branch branch,
        double flow,
        IFlowComponent? exclude)
    {
        ArgumentNullException.ThrowIfNull(branch);

        var total = Along(graph, state, branch.Path, flow, exclude);
        var previous = branch.From.Element;

        foreach (var element in branch.Path)
        {
            total += Link(state, previous, element);
            previous = element;
        }

        return total + Link(state, previous, branch.To.Element);
    }

    /// <summary>The hydrostatic drop across a bare node-to-node link, zero for anything else.</summary>
    private static double Link(FluidState state, IFlowComponent from, IFlowComponent to) =>
        from is NodeComponent lower && to is NodeComponent upper
            ? Hydrostatic.Pressure(state.Density.SiValue, upper.Elevation - lower.Elevation)
            : 0;

    /// <summary>A port state carrying real properties at zero gauge pressure.</summary>
    /// <param name="state">The fluid.</param>
    /// <returns>The port state.</returns>
    /// <remarks>
    /// Only the pressure is flattened. The properties stay real, because a pipe cannot form a Reynolds
    /// number without a density and a viscosity, and a law evaluated against invented ones would be a
    /// different law.
    /// </remarks>
    public static PortState Flat(FluidState state) => new()
    {
        Pressure = 0,
        Enthalpy = state.Enthalpy.SiValue,
        Temperature = state.Temperature.SiValue,
        Density = state.Density.SiValue,
        SpecificHeat = state.SpecificHeat.SiValue,
        DynamicViscosity = state.DynamicViscosity.SiValue,
        ThermalConductivity = state.ThermalConductivity.SiValue,
    };
}
