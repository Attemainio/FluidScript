using System.Collections.Immutable;
using FluidScript.Core.Components;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Seeding;

public static partial class SolutionSeed
{
    private sealed partial class Field
    {
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
                if (_vertices[vertex] is not NodeComponent node)
                {
                    continue;
                }

                if (HydraulicPartition.Stated(node, HydraulicPartition.Flow) is { } stated)
                {
                    var flux = node.Boundary is BoundaryRole.Outlet ? -stated : stated;

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
                    var node = (NodeComponent)_vertices[vertex];
                    var flux = node.Boundary is BoundaryRole.Outlet ? -scale[component] : scale[component];

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
        /// <para>
        /// <strong>It is the third copy of one rule, and `D-86` had to change all three.</strong>
        /// Changing only <c>HydraulicComponent</c> left the table an equation short; changing that and
        /// <c>WellPosedness</c> left this one seeding a flux into a node that no longer has a column, so
        /// <c>m1-syntax-tour</c>'s <c>NB2</c> came out 0.167 kg/s out of balance and the seed's
        /// divergence-free claim was false (<c>S-39</c>).
        /// </para>
        /// </remarks>
        private static bool Free(NodeComponent node) =>
            node.CarriesMassBalance
            && node.Boundary is not BoundaryRole.Interior;
    }
}
