using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Seeding;

public static partial class SolutionSeed
{
    /// <summary>The branch graph, spanned and solved for a divergence-free flow field.</summary>
    /// <param name="graph">The lowered circuit.</param>
    private sealed partial class Field(CircuitGraph graph)
    {
        private readonly Dictionary<object, int> _vertexOf = new(ReferenceEqualityComparer.Instance);
        private readonly List<IFlowComponent> _vertices = [];
        private readonly List<List<(int Branch, int Sign)>> _incident = [];
        private readonly List<int> _order = [];
        private readonly Dictionary<object, double> _injection = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, int> _componentOf = Index(graph);

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
        /// <remarks>
        /// <para>
        /// <strong>Whichever branch closes a vertex's balance can close it at zero, and a branch seeded
        /// at zero is a singular row rather than a poor guess</strong> (<c>S-51</c>). A pipe's momentum
        /// relation is <c>Δp = R·ṁ|ṁ|</c>, whose slope <c>2R|ṁ|</c> vanishes at <c>ṁ = 0</c>, and a pump
        /// curve's does the same; <c>S-21</c> is the whole of why the seed exists. The forest cannot know
        /// in advance which parent will land there, so the answer is measured rather than predicted: span,
        /// solve, and if some branch came out at a standstill, bar it from being a parent and span again.
        /// </para>
        /// <para>
        /// <strong>Measured on the decoupled injection header</strong>, where the primary loop through
        /// <c>HS1</c> and the decoupler both run <c>N7</c> to <c>N8</c>. The walk reached <c>N8</c> by the
        /// primary, so the primary became the parent and closed at <strong>−0</strong>: a 54 kW source
        /// moving no water, every node along it enthalpy-undetermined, and the system <c>Singular</c> at
        /// iteration zero with rank 57 of 59. Barring it moves the leftover onto the decoupler, which is
        /// the branch that should carry it — the difference between primary and secondary flow is the
        /// whole reason a decoupler is fitted.
        /// </para>
        /// <para>
        /// <strong>A bar is kept only while it pays.</strong> Deferring an edge changes the whole walk,
        /// so it can trade one standstill for two somewhere else; a retry that does not lower the count is
        /// undone and the search stops. That makes the loop monotone in the number of stalled branches,
        /// so it terminates, and it can never leave the seed worse than the plain walk left it — which
        /// matters because a genuinely dead leg is zero under every forest and must simply be accepted.
        /// </para>
        /// </remarks>
        public void Solve(ImmutableArray<BranchFlow> estimates)
        {
            foreach (var branch in graph.Branches)
            {
                Attach(branch.From.Element, branch.Index, -1);
                Attach(branch.To.Element, branch.Index, +1);
            }

            var barred = new bool[graph.Branches.Length];
            var best = Fill(estimates, barred, out var stalled);

            while (best > 0 && !barred[stalled])
            {
                barred[stalled] = true;

                var count = Fill(estimates, barred, out var next);

                if (count < best)
                {
                    best = count;
                    stalled = next;

                    continue;
                }

                barred[stalled] = false;
                Fill(estimates, barred, out _);

                return;
            }
        }

        /// <summary>Spans the graph, then solves every tree branch into <see cref="Flows"/>.</summary>
        /// <param name="estimates">One unsigned magnitude per branch.</param>
        /// <param name="barred">Branches held back from parenting a vertex, by branch index.</param>
        /// <param name="stalled">Receives the first branch left at a standstill, or -1 for none.</param>
        /// <returns>How many branches the forest solved to a standstill.</returns>
        private int Fill(ImmutableArray<BranchFlow> estimates, bool[] barred, out int stalled)
        {
            _order.Clear();
            Array.Clear(Flows);

            Span(estimates, barred, out var chords);
            Boundaries(estimates);

            foreach (var chord in chords)
            {
                Flows[chord] = Orientation(graph.Branches[chord], estimates[chord]) * estimates[chord].Magnitude;
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

            return Stalls(out stalled);
        }

        /// <summary>Counts the branches the field left at a standstill.</summary>
        /// <param name="stalled">Receives the first such branch's index, or -1 when there is none.</param>
        /// <returns>How many branches carry less than <see cref="Tolerances.FlowZero"/>.</returns>
        private int Stalls(out int stalled)
        {
            var count = 0;

            stalled = -1;

            for (var branch = 0; branch < Flows.Length; branch++)
            {
                if (Math.Abs(Flows[branch]) > Tolerances.FlowZero)
                {
                    continue;
                }

                count++;
                stalled = stalled < 0 ? branch : stalled;
            }

            return count;
        }

            /// <summary>Indexes every component by reference, for <see cref="PortAdjacency"/> lookups.</summary>
            /// <param name="graph">The lowered circuit.</param>
            /// <returns>Component to its position in <c>graph.Components</c>.</returns>
            private static Dictionary<object, int> Index(CircuitGraph graph)
            {
                var index = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

                for (var component = 0; component < graph.Components.Length; component++)
                {
                    index[graph.Components[component]] = component;
                }

                return index;
            }

        /// <summary>What reaching a three-way valve by a switched leg adds to a branch's cost in <see cref="Span"/>.</summary>
        /// <value>More than any basis, so the common port is still preferred over every estimate; less than <see cref="BarredPenalty"/>.</value>
        private const int SwitchedLegPenalty = 10;

        /// <summary>What a barred branch adds to its cost in <see cref="Span"/>.</summary>
        /// <value>More than a switched leg and any basis together, so a bar is only overridden when nothing else reaches the vertex.</value>
        private const int BarredPenalty = 100;
    }
}
