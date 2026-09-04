using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Solvers;

/// <summary>The iterate a solve starts from: <c>31</c>'s <c>seedFromStatedDuties</c>.</summary>
/// <remarks>
/// <para>
/// <strong>A seed is not a convenience, and zero is not a neutral one.</strong> A pipe's momentum
/// relation is <c>Δp = R·ṁ|ṁ|</c> and a pump curve is <c>H₀ − kṁ²</c>; both have a derivative of
/// exactly zero at <c>ṁ = 0</c>, so a zero-flow start is a genuinely singular Jacobian rather than a
/// poor guess, and every solve from one reports <c>FS3002</c> however well-posed the circuit is
/// (<c>S-21</c>).
/// </para>
/// <para>
/// <strong>Non-zero is not enough either, and this is the half that is easy to miss.</strong> A
/// branch's orientation is the decomposition's choice, so seeding every flow to the same positive
/// number leaves some node with every port an inflow — and a node nothing leaves is a node whose own
/// enthalpy enters no equation, which is a zero column and a singular Jacobian again. The fix is not a
/// sign heuristic: it is that the seed <em>satisfies the mass balances</em>, at which point every node
/// with an inflow has an outflow by construction.
/// </para>
/// <para>
/// <strong>How the field is made mass-consistent.</strong> The branch graph is spanned by a forest.
/// Every non-tree branch — one per independent loop (<c>23</c>) — takes its own estimate outright, and
/// every boundary flux is chosen so the fluxes of a hydraulic component sum to zero. The tree branches
/// are then <em>solved</em>, leaves inward: at each vertex all but the parent branch is known, so the
/// parent's flow is whatever closes that vertex's balance. The last vertex closes identically because
/// its component's fluxes were made to sum to zero, which is the same statement one level up.
/// </para>
/// <para>
/// This is exactly the decomposition <c>23</c>'s cycle basis describes, used for its other purpose: a
/// divergence-free field on a graph is a particular solution plus the cycle space, so choosing the
/// chords and the boundary freely and solving for the tree reaches every such field and nothing else.
/// </para>
/// </remarks>
public static class SolutionSeed
{
    /// <summary>The temperature a node's state falls back to when the script fixes none.</summary>
    /// <value>K. 20 °C — room temperature, valid for every substance the catalogue carries.</value>
    public const double ReferenceTemperature = 293.15;

    /// <summary>The pressure step the seed puts between one node and the next along a branch.</summary>
    /// <value>
    /// Pa. 10 kPa — a tenth of <c>Tolerances.PressureScale</c>, so a circuit of a dozen nodes stays
    /// inside a plausible range while no two adjacent nodes agree. The magnitude is not a claim about
    /// any circuit; being non-zero is the whole of it.
    /// </value>
    public const double NominalDrop = 1e4;

    /// <summary>The temperature step the seed puts between one node and the next along a branch.</summary>
    /// <value>
    /// K. Two degrees — small enough that <see cref="Band"/> steps stay well inside any fluid's
    /// validated range, large enough that an enthalpy difference is far from the noise floor of a
    /// finite-difference derivative.
    /// </value>
    public const double NominalRise = 2;

    /// <summary>How many steps the seed takes before wrapping back to the start of its band.</summary>
    /// <value>
    /// Five. A cumulative walk down a long branch leaves the fluid's validated range; wrapping bounds
    /// the excursion at four steps while still giving every pair of adjacent nodes different values,
    /// which is the only property the seed needs from this.
    /// </value>
    public const int Band = 5;

    /// <summary>Builds the starting iterate for a graph.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">The state vector's layout, which fixes where each value goes.</param>
    /// <returns>One value per unknown, in SI, in the layout's order.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static StateVector Build(CircuitGraph graph, SystemLayout layout)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);

        var values = new double[layout.Count];
        var field = new Field(graph);

        field.Solve(BranchFlows.Estimate(graph));

        for (var branch = 0; branch < graph.Branches.Length; branch++)
        {
            values[layout.BranchFlow(branch)] = field.Flows[branch];
        }

        for (var index = 0; index < layout.FluxNodes.Length; index++)
        {
            values[layout.ExternalFluxOffset + index] = field.Injection(layout.FluxNodes[index].Component);
        }

        Thermal(graph, layout, values);

        return new StateVector([.. values]);
    }

    /// <summary>Fills every pressure, enthalpy and component-owned state.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">The state vector's layout.</param>
    /// <param name="values">The iterate being built, written in place.</param>
    /// <remarks>
    /// <para>
    /// <strong>One temperature level for the whole graph, and a stepped pressure field rather than a
    /// level.</strong> The temperature needs no refinement: a stated value is used where the script
    /// gives one, and nothing else in the seed reads a temperature difference.
    /// </para>
    /// <para>
    /// <strong>A uniform field is a singular Jacobian, in pressure and in temperature alike</strong>
    /// (<c>S-25</c>). A valve's law is <c>ṁ = Kv·f(x)·√(Δp·ρ)</c>, so at <c>Δp = 0</c> its derivative
    /// with respect to <c>Kv</c> and to <c>position</c> is zero — the cooling loop's promoted
    /// <c>3WV.position</c> was measured as a column of zeros. A node's energy balance carries
    /// <c>ṁ(h_arriving − h_own)</c>, so at a uniform enthalpy its derivative with respect to <em>flow</em>
    /// is zero, and the simple loop's promoted <c>PU1.head</c> came out of a rank-11 12×12 system for
    /// that reason. Both are the same lesson as <c>S-21</c> one variable over: the seed must make every
    /// difference a residual reads non-zero, not merely every value.
    /// </para>
    /// <para>
    /// A component's own state is seeded from its declared SI unit rather than from its kind
    /// (<c>D-74</c>): the layout knows the unit and nothing here should know what a tank is. A promoted
    /// parameter carries no unit yet and falls to zero, which is honest and is the thing the outer loop
    /// replaces when promotion becomes live.
    /// </para>
    /// </remarks>
    private static void Thermal(CircuitGraph graph, SystemLayout layout, double[] values)
    {
        var pressures = graph.Nodes
            .Select(static node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure))
            .Where(static stated => stated is not null)
            .Select(static stated => stated!.Value)
            .ToArray();

        var level = pressures.Length > 0 ? pressures.Average() : Tolerances.PressureScale;
        var datum = Datum(graph);
        var steps = Steps(graph);

        for (var index = 0; index < graph.Nodes.Length; index++)
        {
            var node = graph.Nodes[index];

            var pressure = HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure)
                ?? level - (NominalDrop * steps[index]);

            var temperature = HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature)
                ?? datum - (NominalRise * steps[index]);

            values[layout.NodePressure(index)] = pressure;
            values[layout.NodeEnthalpy(index)] = Enthalpy(graph.Substance, pressure, temperature);
        }

        for (var index = layout.ComponentUnknownOffset; index < layout.ExternalFluxOffset; index++)
        {
            values[index] = layout.Unknowns[index].SiUnit switch
            {
                "J/kg" => Enthalpy(graph.Substance, level, datum),
                "Pa" => level,
                "K" => datum,
                _ => 0,
            };
        }
    }

    /// <summary>How many steps from its branch's start each node is, wrapped into a narrow band.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>One step index per node, indexed as <c>graph.Nodes</c> is; zero at every branch end.</returns>
    /// <remarks>
    /// <para>
    /// <strong>What this exists to guarantee is that adjacent nodes differ</strong>, in pressure and in
    /// temperature alike. Every residual the solver differentiates reads a <em>difference</em> across a
    /// component — <c>√Δp</c> in a valve law, <c>ṁ(hᵢₙ − hₒᵤₜ)</c> in an energy balance — and a uniform
    /// field makes the derivative with respect to the <em>other</em> variable vanish. That is how a
    /// promoted <c>position</c> and a promoted <c>head</c> both came out singular on well-posed circuits
    /// (<c>S-25</c>), one through pressure and one through enthalpy.
    /// </para>
    /// <para>
    /// <strong>Wrapped rather than cumulative, and the wrap is what keeps it safe.</strong> A long
    /// branch stepped monotonically would walk a seed out of the fluid's validated range — twenty nodes
    /// at 10 kPa and 2 K a step is 200 kPa and 40 K from where it started, and either end of that is a
    /// property call that fails. Consecutive indices still differ, which is the whole requirement, and
    /// the excursion is bounded by <see cref="Band"/> steps whatever the circuit.
    /// </para>
    /// <para>
    /// A branch end keeps step zero. It belongs to more than one branch and there is no walk position it
    /// could take that both would agree on; a stated boundary is usually there anyway.
    /// </para>
    /// </remarks>
    private static int[] Steps(CircuitGraph graph)
    {
        var steps = new int[graph.Nodes.Length];
        var index = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        for (var node = 0; node < graph.Nodes.Length; node++)
        {
            index[graph.Nodes[node].Component] = node;
        }

        foreach (var branch in graph.Branches)
        {
            var step = 0;

            foreach (var part in branch.Path)
            {
                if (part is not CircuitNode || !index.TryGetValue(part, out var node))
                {
                    continue;
                }

                steps[node] = ++step % Band;
            }
        }

        return steps;
    }

    /// <summary>The temperature every unstated node is seeded at.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>K.</returns>
    /// <remarks>
    /// A stated boundary temperature first, then an exchanger's stated inlet — which is the enthalpy
    /// datum of a closed circuit (<c>D-65</c>), so it is the one absolute temperature such a circuit
    /// has — and <see cref="ReferenceTemperature"/> only when the script names neither.
    /// </remarks>
    private static double Datum(CircuitGraph graph) =>
        graph.Nodes
            .Select(static node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Temperature))
            .FirstOrDefault(static stated => stated is not null)
        ?? graph.Components
            .Select(static component =>
                component.StatedParameters.TryGetValue("in", out var inlet) ? inlet.SiValue : (double?)null)
            .FirstOrDefault(static stated => stated is not null)
        ?? ReferenceTemperature;

    /// <summary>The specific enthalpy of a state, or zero when the substance cannot evaluate it.</summary>
    /// <param name="substance">The circuit's fluid.</param>
    /// <param name="pressure">Gauge pressure, Pa.</param>
    /// <param name="temperature">K.</param>
    /// <returns>J/kg.</returns>
    /// <remarks>
    /// Zero rather than a throw or a diagnostic: a state the substance refuses has already been
    /// reported by well-posedness (<c>FS2205</c>), and a seed is allowed to be wrong. Nothing here is
    /// the right place to tell the user about it a second time.
    /// </remarks>
    private static double Enthalpy(ISubstance substance, double pressure, double temperature) =>
        substance.FromPressureTemperature(
            Quantity.FromSi(pressure, Dimension.Pressure),
            Quantity.FromSi(temperature, Dimension.Temperature)).TryGetValue(out var state)
            ? state.Enthalpy.SiValue
            : 0;

    /// <summary>The branch graph, spanned and solved for a divergence-free flow field.</summary>
    /// <param name="graph">The lowered circuit.</param>
    private sealed class Field(CircuitGraph graph)
    {
        private readonly Dictionary<object, int> _vertexOf = new(ReferenceEqualityComparer.Instance);
        private readonly List<IFlowComponent> _vertices = [];
        private readonly List<List<(int Branch, int Sign)>> _incident = [];
        private readonly List<int> _order = [];
        private readonly Dictionary<object, double> _injection = new(ReferenceEqualityComparer.Instance);

        private int[] _parent = [];
        private int[] _component = [];

        /// <summary>Gets the signed flow of every branch, indexed by <see cref="Branch.Index"/>.</summary>
        /// <value>kg/s, positive in the branch's own orientation.</value>
        public double[] Flows { get; } = new double[graph.Branches.Length];

        /// <summary>The external flux seeded at one node.</summary>
        /// <param name="node">The node's component.</param>
        /// <returns>kg/s, positive into the circuit; zero for a node carrying no flux.</returns>
        public double Injection(IFlowComponent node) =>
            _injection.TryGetValue(node, out var flux) ? flux : 0;

        /// <summary>Chooses chords and boundary fluxes, then solves the tree for the rest.</summary>
        /// <param name="estimates">One unsigned magnitude per branch.</param>
        public void Solve(ImmutableArray<BranchFlow> estimates)
        {
            Span(out var chords);
            Boundaries(estimates);

            foreach (var chord in chords)
            {
                Flows[chord] = estimates[chord].Magnitude;
            }

            // Leaves inward, so that when a vertex is reached everything at it but the branch joining
            // it to its parent is already known -- and that branch is therefore determined.
            for (var index = _order.Count - 1; index >= 0; index--)
            {
                var vertex = _order[index];

                if (_parent[vertex] < 0)
                {
                    continue;
                }

                var net = Injection(_vertices[vertex]);
                var sign = 0;

                foreach (var (branch, edge) in _incident[vertex])
                {
                    if (branch == _parent[vertex])
                    {
                        sign = edge;
                    }
                    else
                    {
                        net += edge * Flows[branch];
                    }
                }

                Flows[_parent[vertex]] = -net / sign;
            }
        }

        /// <summary>Builds the vertex set, the incidence lists and a spanning forest.</summary>
        /// <param name="chords">Receives every branch the forest did not use.</param>
        /// <remarks>
        /// A branch is incident to its <see cref="Branch.From"/> end with sign −1 and to its
        /// <see cref="Branch.To"/> end with +1, matching <see cref="PortMap"/>'s convention so that a
        /// balance written here and a residual written there mean the same thing. A branch whose ends
        /// are the same vertex — a ring with one cut vertex is exactly this — lands twice with
        /// opposite signs and cancels, which is correct: a self-loop moves no mass across its vertex.
        /// </remarks>
        private void Span(out List<int> chords)
        {
            foreach (var branch in graph.Branches)
            {
                Attach(branch.From.Element, branch.Index, -1);
                Attach(branch.To.Element, branch.Index, +1);
            }

            _parent = new int[_vertices.Count];
            _component = new int[_vertices.Count];
            Array.Fill(_parent, -1);
            Array.Fill(_component, -1);

            var used = new bool[graph.Branches.Length];
            var components = 0;

            for (var root = 0; root < _vertices.Count; root++)
            {
                if (_component[root] >= 0)
                {
                    continue;
                }

                var queue = new Queue<int>();

                _component[root] = components++;
                _order.Add(root);
                queue.Enqueue(root);

                while (queue.Count > 0)
                {
                    var vertex = queue.Dequeue();

                    foreach (var (branch, sign) in _incident[vertex])
                    {
                        var other = Other(branch, sign);

                        if (_component[other] >= 0)
                        {
                            continue;
                        }

                        used[branch] = true;
                        _parent[other] = branch;
                        _component[other] = _component[vertex];
                        _order.Add(other);
                        queue.Enqueue(other);
                    }
                }
            }

            chords = [];

            for (var branch = 0; branch < used.Length; branch++)
            {
                if (!used[branch])
                {
                    chords.Add(branch);
                }
            }
        }

        /// <summary>Chooses an external flux for every boundary node, summing to zero per component.</summary>
        /// <param name="estimates">One unsigned magnitude per branch, for the scale to use.</param>
        /// <remarks>
        /// <para>
        /// A stated <c>flow</c> is taken as it is: the script named the flux and nothing here may move
        /// it. Every other boundary — a stated pressure, or a <c>return</c> (<c>D-64</c>) — has a free
        /// flux, and those are what absorb the correction: each is offered the component's largest
        /// estimate, signed by its role, and then shifted by the shared amount that closes the total.
        /// </para>
        /// <para>
        /// <strong>Without the correction the tree solve still terminates and the root's balance is
        /// simply violated</strong>, which is the failure that looks like a converged seed and is not
        /// one. Closing it here is what makes the construction's claim true rather than nearly true.
        /// </para>
        /// </remarks>
        private void Boundaries(ImmutableArray<BranchFlow> estimates)
        {
            var scale = new double[_vertices.Count == 0 ? 1 : _vertices.Count];
            var free = new List<int>[scale.Length];

            for (var index = 0; index < free.Length; index++)
            {
                free[index] = [];
            }

            foreach (var branch in graph.Branches)
            {
                var component = _component[_vertexOf[branch.From.Element]];

                scale[component] = Math.Max(scale[component], estimates[branch.Index].Magnitude);
            }

            var fixedTotal = new double[scale.Length];

            for (var vertex = 0; vertex < _vertices.Count; vertex++)
            {
                if (_vertices[vertex] is not CircuitNode node)
                {
                    continue;
                }

                if (HydraulicPartition.Stated(node, HydraulicPartition.Flow) is { } stated)
                {
                    var flux = node.Boundary is BoundaryRole.Return ? -stated : stated;

                    _injection[node] = flux;
                    fixedTotal[_component[vertex]] += flux;
                }
                else if (Free(node))
                {
                    free[_component[vertex]].Add(vertex);
                }
            }

            for (var component = 0; component < free.Length; component++)
            {
                if (free[component].Count == 0)
                {
                    continue;
                }

                var offered = 0.0;

                foreach (var vertex in free[component])
                {
                    var node = (CircuitNode)_vertices[vertex];
                    var flux = node.Boundary is BoundaryRole.Return ? -scale[component] : scale[component];

                    _injection[node] = flux;
                    offered += flux;
                }

                var correction = (offered + fixedTotal[component]) / free[component].Count;

                foreach (var vertex in free[component])
                {
                    _injection[_vertices[vertex]] -= correction;
                }
            }
        }

        /// <summary>Whether a node's external flux is an unknown the seed may choose.</summary>
        /// <param name="node">The candidate node.</param>
        /// <returns><see langword="true"/> when well-posedness gave it a flux column.</returns>
        /// <remarks>
        /// The same rule <c>WellPosedness</c> applies, restated rather than shared because the two
        /// reach it from opposite directions — it walks nodes to count columns, and this walks vertices
        /// of the branch graph to fill them. A test holds the two lists against each other.
        /// </remarks>
        private static bool Free(CircuitNode node) =>
            node.CarriesMassBalance
            && (HydraulicPartition.Stated(node, HydraulicPartition.Pressure) is not null
                || node.Boundary is BoundaryRole.Return);

        /// <summary>Records one end of a branch against the vertex it meets.</summary>
        /// <param name="element">The junction element at that end.</param>
        /// <param name="branch">The branch's index.</param>
        /// <param name="sign">−1 where the branch leaves, +1 where it arrives.</param>
        private void Attach(IFlowComponent element, int branch, int sign)
        {
            if (!_vertexOf.TryGetValue(element, out var vertex))
            {
                vertex = _vertices.Count;

                _vertexOf[element] = vertex;
                _vertices.Add(element);
                _incident.Add([]);
            }

            _incident[vertex].Add((branch, sign));
        }

        /// <summary>The vertex at a branch's other end, seen from one of its incidence entries.</summary>
        /// <param name="branch">The branch's index.</param>
        /// <param name="sign">The sign this end carries.</param>
        /// <returns>The vertex index at the far end, which is this one for a self-loop.</returns>
        private int Other(int branch, int sign) =>
            _vertexOf[sign < 0 ? graph.Branches[branch].To.Element : graph.Branches[branch].From.Element];
    }
}
