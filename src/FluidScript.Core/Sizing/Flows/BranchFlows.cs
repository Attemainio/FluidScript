using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Sizing.Flows;

/// <summary>Steps 1 and 2 of <c>24</c>'s pipeline: a flow estimate for every branch, before any solve.</summary>
/// <remarks>
/// <para>
/// <strong>This is an estimate and says so in its own type.</strong> Every value carries the basis
/// that produced it, so a sizing rule can refuse to size against a guess and the seed can tell a
/// number it trusts from one it invented. A bare <c>double[]</c> would make the two
/// indistinguishable, which is the failure mode <c>24</c>'s whole "every sized value carries its
/// basis" discipline exists to prevent.
/// </para>
/// <para>
/// <strong>Propagation here is weaker than <c>24</c>'s and deliberately so.</strong> That document
/// propagates stated constraints to a fixed point over the branch graph and reports <c>FS2302</c> on a
/// conflict; what runs here spreads the largest determined estimate across a junction element's other
/// branches and reports nothing. The difference is the direction of the two errors: a seed that is
/// somewhat wrong costs Newton iterations, and a <em>diagnostic</em> that is somewhat wrong is a
/// sentence a user acts on. The exact propagation and its diagnostics arrive with the sizers, which
/// are what the diagnostics are about.
/// </para>
/// <para>
/// A junction element is degree one or degree three and up, never degree two (<c>23</c>), so there is
/// no series propagation to do: a branch already carries one flow along its whole length.
/// </para>
/// </remarks>
public static partial class BranchFlows
{
    /// <summary>The estimate a branch takes when nothing determines it.</summary>
    /// <value>
    /// kg/s. Roughly a small domestic circuit, and chosen only to be a plausible order of magnitude —
    /// it must be far enough from zero that a momentum relation's <c>∂(R·ṁ|ṁ|)/∂ṁ</c> is not itself
    /// zero (<c>S-21</c>), and nothing else about it is claimed.
    /// </value>
    public const double Nominal = 0.1;

    /// <summary>Estimates the flow every branch carries, from what the script stated.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>One estimate per branch, indexed by <see cref="Branch.Index"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static ImmutableArray<BranchFlow> Estimate(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var estimates = new BranchFlow[graph.Branches.Length];
        Array.Fill(estimates, new BranchFlow(Nominal, FlowBasis.Nominal, string.Empty));

        foreach (var branch in graph.Branches)
        {
            foreach (var part in branch.Path)
            {
                Offer(ref estimates[branch.Index], Duty(graph, branch, part), FlowBasis.Duty, part.Name);
                Offer(
                    ref estimates[branch.Index],
                    HydraulicPartition.Stated(part, HydraulicPartition.Flow),
                    FlowBasis.Stated,
                    part.Name);
                // A volume flow seeds at the density of the side's stated inlet temperature, else 20 °C;
                // the solve then holds it at the density it finds (P5.13b).
                Offer(ref estimates[branch.Index], VolumeFlow(graph, branch, part), FlowBasis.Stated, part.Name);
            }

            // A load with a duty and no temperature of its own borrows its sources' design temperatures, and
            // yields to any rating the branch has from temperatures stated on it (S-85).
            if (estimates[branch.Index].Basis < FlowBasis.Duty)
            {
                foreach (var load in branch.Path.OfType<HeatExchangerComponent>())
                {
                    if (Ownership.Of(load, "power") is not ParameterState.Free && Side(graph, branch, load) == 1)
                    {
                        Offer(ref estimates[branch.Index], DesignFlow(graph, load), FlowBasis.Duty, load.Name);
                    }
                }
            }

            // A terminal states the flux crossing the model boundary, and a terminal has one branch, so
            // that flux *is* the branch's flow. An interior junction's stated flow is not: it splits
            // among several branches and none of them carries it alone.
            foreach (var end in new[] { branch.From, branch.To })
            {
                if (end.Element.Ports.Length == 1)
                {
                    Offer(
                        ref estimates[branch.Index],
                        HydraulicPartition.Stated(end.Element, HydraulicPartition.Flow),
                        FlowBasis.Stated,
                        end.Element.Name);
                }
            }
        }

        Propagate(graph, estimates);

        return [.. estimates];
    }

    /// <summary>A stated volume flow on a path element, as a mass flow at its inlet's stated temperature or 20 °C.</summary>
    /// <param name="graph">The lowered circuit, for the substance.</param>
    /// <param name="branch">The branch, which says which side of an exchanger the element runs in.</param>
    /// <param name="part">The element.</param>
    /// <returns>kg/s, or <see langword="null"/> when no volume flow is stated on this side.</returns>
    private static double? VolumeFlow(CircuitGraph graph, Branch branch, IFlowComponent part)
    {
        var side = part is HeatExchangerComponent exchanger ? Side(graph, branch, exchanger) : 1;
        var suffix = side == 2 ? "2" : string.Empty;

        if (HydraulicPartition.Stated(part, "vflow" + suffix) is not { } volume)
        {
            return null;
        }

        var inlet = HydraulicPartition.Stated(part, "in" + suffix) ?? 293.15;
        var state = graph.Substance.FromPressureTemperature(
            Quantity.FromSi(0, Dimension.Pressure), Quantity.FromSi(inlet, Dimension.Temperature));

        return state.IsSuccess ? volume * state.Value.Density.SiValue : null;
    }

    /// <summary>Takes a candidate estimate when it outranks the one already held.</summary>
    /// <param name="held">The estimate so far, replaced in place.</param>
    /// <param name="candidate">The proposed magnitude, or <see langword="null"/> when the rule did not apply.</param>
    /// <param name="basis">What produced the candidate.</param>
    /// <param name="source">The component it came from.</param>
    /// <remarks>
    /// A zero candidate is rejected rather than taken. <c>flow=0</c> on a shut-off is a legitimate
    /// statement about the answer and a useless statement about where to start looking for it, and
    /// seeding a branch at exactly zero is the singularity <c>S-21</c> is about.
    /// </remarks>
    private static void Offer(ref BranchFlow held, double? candidate, FlowBasis basis, string source)
    {
        if (candidate is not { } magnitude || Math.Abs(magnitude) <= Solvers.Tolerances.FlowZero)
        {
            return;
        }

        if (basis >= held.Basis)
        {
            held = new BranchFlow(Math.Abs(magnitude), basis, source);
        }
    }

    /// <summary>Spreads each junction element's largest determined estimate onto its undetermined branches.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="estimates">The estimates so far, written in place.</param>
    /// <remarks>
    /// Bounded by the branch count because each pass raises at least one branch's basis above
    /// <see cref="FlowBasis.Nominal"/> or changes nothing, and a basis never falls.
    /// </remarks>
    private static void Propagate(CircuitGraph graph, BranchFlow[] estimates)
    {
        for (var pass = 0; pass < graph.Branches.Length; pass++)
        {
            var moved = false;

            foreach (var junction in graph.JunctionElements)
            {
                if (junction is ThreeWayValveComponent { BypassConnected: true } valve)
                {
                    moved |= PropagateThreeWay(graph, estimates, valve);
                    continue;
                }

                var best = 0.0;
                var source = string.Empty;

                foreach (var branch in graph.Branches)
                {
                    if (Meets(branch, junction)
                        && estimates[branch.Index].Basis > FlowBasis.Nominal
                        && estimates[branch.Index].Magnitude > best)
                    {
                        best = estimates[branch.Index].Magnitude;
                        source = estimates[branch.Index].Source;
                    }
                }

                if (best <= 0)
                {
                    continue;
                }

                foreach (var branch in graph.Branches)
                {
                    if (!Meets(branch, junction)
                        || MeetsThreeWay(branch)
                        || estimates[branch.Index].Basis > FlowBasis.Nominal)
                    {
                        continue;
                    }

                    estimates[branch.Index] = new BranchFlow(best, FlowBasis.Propagated, source);
                    moved = true;
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }

    /// <summary>A second estimate for the mixing valves the first could not partition, from the flows a mass-consistent field found at their feed legs.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="estimates">The estimate the field was solved from.</param>
    /// <param name="flows">The field's flow per branch, in the branch's orientation.</param>
    /// <returns>The estimate with each such valve's legs partitioned, or <see langword="null"/> when no valve gained anything.</returns>
    /// <remarks>
    /// <see cref="Propagate"/> never hands a three-way valve's leg a node's flow, because a leg's share is
    /// the valve's to decide; on a series ring that leaves a block whose coil has no duty with nothing
    /// known at any port, and the field then closes its feed by balance alone (<c>S-69</c>). Told what the
    /// balance found, the same three-way rule partitions the coil and the recirculating leg from the
    /// stated temperatures, exactly as it would have from an estimate.
    /// </remarks>
    public static ImmutableArray<BranchFlow>? Refine(CircuitGraph graph, ImmutableArray<BranchFlow> estimates, double[] flows)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(flows);

        var refined = estimates.ToArray();
        var moved = false;

        foreach (var valve in graph.JunctionElements.OfType<ThreeWayValveComponent>())
        {
            // A coil switched off asks nothing of its split and its `in` is documentation, not a demand
            // (`S-56`); partitioning its legs from those temperatures would seed a stopped branch running.
            var off = graph.Branches.Any(branch =>
                Meets(branch, valve)
                && FluidScript.Core.Solvers.Results.ValveLegs.PortName(branch, valve) == "ab"
                && branch.Path.OfType<HeatExchangerComponent>().Any(FluidScript.Core.Topology.Counting.WellPosedness.ZeroDuty));

            if (!valve.BypassConnected || off)
            {
                continue;
            }

            foreach (var branch in graph.Branches)
            {
                if (Meets(branch, valve)
                    && FluidScript.Core.Solvers.Results.ValveLegs.PortName(branch, valve) == "a"
                    && refined[branch.Index].Basis == FlowBasis.Nominal
                    && Math.Abs(flows[branch.Index]) > Solvers.Tolerances.FlowZero)
                {
                    refined[branch.Index] = new BranchFlow(Math.Abs(flows[branch.Index]), FlowBasis.Propagated, "field");
                    moved |= PropagateThreeWay(graph, refined, valve);
                }
            }
        }

        return moved ? [.. refined] : null;
    }

    /// <summary>Whether a branch has an end at a given junction element.</summary>
    /// <param name="branch">The branch.</param>
    /// <param name="junction">The element.</param>
    /// <returns><see langword="true"/> when either end names it.</returns>
    private static bool Meets(Branch branch, IFlowComponent junction) =>
        ReferenceEquals(branch.From.Element, junction) || ReferenceEquals(branch.To.Element, junction);

    /// <summary>The flow an exchanger's known duty and terminal temperatures imply.</summary>
    /// <param name="graph">The lowered circuit, including sized duties and other design terminals.</param>
    /// <param name="branch">The branch being estimated, which decides which side of a coupled exchanger it crosses.</param>
    /// <param name="component">The candidate component.</param>
    /// <returns>kg/s, or <see langword="null"/> when the rule does not apply here.</returns>
    /// <remarks>
    /// <para>
    /// An automatically closed duty lives in <c>SizedParameters</c>, not <c>StatedParameters</c>. With only
    /// the outlet stated, the inlet is first the outlet stated upstream, across elements that leave the
    /// temperature alone (<see cref="UpstreamOutlet"/>, <c>S-83</c>): a load fed by a machine that holds
    /// its leaving temperature. Failing that, for a positive source, a common outlet temperature on every
    /// opposing load of its own circuit is the return design temperature. Using either here seeds a flow;
    /// it does not add a constraint to the solve. If the returns disagree, the estimate declines rather
    /// than inventing a mixed temperature.
    /// </para>
    /// <para>
    /// The arithmetic is <see cref="RatedFlow"/>'s; this decides which inlet to hand it. A side stated as
    /// a difference (<c>dt</c>, <c>dt2</c>) rather than two terminals is <c>Q / (cp · dt)</c> with <c>cp</c> at
    /// whichever terminal it did state, and at 50 °C when it stated none -- a seed, not a rating. A load that
    /// states its duty and no terminal at all is rated afterwards, at its sources' design temperatures (<see cref="DesignFlow"/>).
    /// </para>
    /// </remarks>
    private static double? Duty(CircuitGraph graph, Branch branch, IFlowComponent component)
    {
        if (component is not HeatExchangerComponent exchanger || Ownership.Of(component, "power") is ParameterState.Free)
        {
            return null;
        }

        // A coupled exchanger sits on a branch of each side, and each side's design point is its own:
        // the substation's primary is 150 kW over 85/45, not over the secondary's 40/60 (`S-32`).
        if (Side(graph, branch, exchanger) == 2)
        {
            return component.StatedParameters.TryGetValue("in2", out var entering)
                && component.StatedParameters.TryGetValue("out2", out var leaving)
                    ? RatedFlow(graph.Substance, exchanger.Power, entering, leaving)
                    : DifferenceFlow(graph.Substance, exchanger, "in2", "out2", "dt2");
        }

        if (!component.StatedParameters.TryGetValue("out", out var outlet))
        {
            return DifferenceFlow(graph.Substance, exchanger, "in", "out", "dt");
        }

        var inlet = component.StatedParameters.TryGetValue("in", out var statedInlet)
            ? statedInlet
            : UpstreamOutlet(graph, exchanger) ?? CommonReturn(graph, exchanger);

        return inlet is null ? null : RatedFlow(graph.Substance, exchanger.Power, inlet.Value, outlet);
    }

    /// <summary>The flow a duty over a stated temperature difference implies, in kg/s.</summary>
    /// <param name="substance">The circuit's substance, for <c>cp</c>.</param>
    /// <param name="exchanger">The exchanger.</param>
    /// <param name="inlet">The side's inlet parameter name.</param>
    /// <param name="outlet">The side's outlet parameter name.</param>
    /// <param name="change">The side's difference parameter name.</param>
    /// <returns>kg/s, or <see langword="null"/> when no difference is stated or <c>cp</c> cannot be read.</returns>
    private static double? DifferenceFlow(
        ISubstance substance, HeatExchangerComponent exchanger, string inlet, string outlet, string change)
    {
        if (!exchanger.StatedParameters.TryGetValue(change, out var difference))
        {
            return null;
        }

        var at = exchanger.StatedParameters.TryGetValue(inlet, out var entering) ? entering
            : exchanger.StatedParameters.TryGetValue(outlet, out var leaving) ? leaving
            : Quantity.FromSi(323.15, Dimension.Temperature);

        if (!substance.FromPressureTemperature(Quantity.FromSi(0, Dimension.Pressure), at).TryGetValue(out var state))
        {
            return null;
        }

        var implied = exchanger.ImpliedFlow(state.SpecificHeat.SiValue, difference.SiValue);

        return double.IsFinite(implied) ? implied : null;
    }

    /// <summary>Which side of an exchanger a branch runs through.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="branch">The branch, which holds the exchanger in its path.</param>
    /// <param name="exchanger">The exchanger.</param>
    /// <returns>1 or 2. Read from the port the branch arrives by; 1 when it cannot be told.</returns>
    public static int Side(CircuitGraph graph, Branch branch, HeatExchangerComponent exchanger)
    {
        var position = branch.Path.IndexOf(exchanger);
        var index = graph.Components.IndexOf(exchanger);

        if (position < 0 || index < 0)
        {
            return 1;
        }

        var before = position > 0 ? branch.Path[position - 1] : branch.From.Element;
        var arrival = graph.Components.IndexOf(before);

        for (var port = 0; port < exchanger.Ports.Length; port++)
        {
            var peer = graph.Adjacency.Peer(index, port);

            if (peer.Exists && peer.Component == arrival)
            {
                return port >= 2 ? 2 : 1;
            }
        }

        return 1;
    }

    /// <summary>The flow a duty moves between two terminal temperatures.</summary>
    /// <param name="substance">The fluid, for its enthalpy table.</param>
    /// <param name="power">W, signed as the core carries it; only its magnitude matters here.</param>
    /// <param name="inlet">The entering temperature.</param>
    /// <param name="outlet">The leaving temperature.</param>
    /// <returns>kg/s, or <see langword="null"/> when the two temperatures coincide or the substance cannot be at one of them.</returns>
    /// <remarks>
    /// <para>
    /// <c>ṁ = |Q̇| / |h(out) − h(in)|</c>, evaluated at the substance rather than at a constant
    /// specific heat: water's <c>cp</c> moves 1 % between 20 °C and 90 °C and the whole point of this
    /// number is that a user can check it against the enthalpy table.
    /// </para>
    /// <para>
    /// <strong>One home for the arithmetic, on purpose</strong> (<c>S-57</c>). The seed uses it to start a
    /// branch and the equation system uses it as a fixed-flow row's target; until they shared it the row
    /// read the seed's <em>estimate</em> for the branch, which is this number only when nothing has
    /// propagated over it, and a row in the Jacobian that depends on a seeding heuristic is a coupling in
    /// the wrong direction.
    /// </para>
    /// </remarks>
    public static double? RatedFlow(ISubstance substance, double power, Quantity inlet, Quantity outlet)
    {
        ArgumentNullException.ThrowIfNull(substance);

        var reference = Quantity.FromSi(0, Dimension.Pressure);

        if (!substance.FromPressureTemperature(reference, inlet).TryGetValue(out var entering)
            || !substance.FromPressureTemperature(reference, outlet).TryGetValue(out var leaving))
        {
            return null;
        }

        var rise = Math.Abs(leaving.Enthalpy.SiValue - entering.Enthalpy.SiValue);

        return rise > 0 ? Math.Abs(power) / rise : null;
    }
}
