using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Topology.Counting;

public static partial class WellPosedness
{
    /// <summary>Matches each constraint to the sized parameter that can absorb it.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="hydraulics">Its hydraulic partition.</param>
    /// <param name="constraints">The constraints, in the order they claim candidates.</param>
    /// <returns>One promotion per constraint that found a free parameter, and the groups that found none.</returns>
    /// <remarks>
    /// <para>
    /// <strong>First come, then augmented</strong> (<c>D-130</c>, <c>D-133</c>). Two parallel branches
    /// each pinning a flow cannot both be met by the one pump between them; the first takes the pump head
    /// and the second falls to its own branch's balancing valve, which is exactly what a balancing valve is
    /// for. When the second has no valve and the first had one, the greedy pass leaves the second unmatched
    /// and <see cref="Assignment"/> moves the first onto its valve: the bare branch is the index branch, and
    /// it is the pump's whichever was declared first.
    /// </para>
    /// <para>
    /// <strong>A mixed inlet accepts only a mixing split.</strong> Letting it fall back to a valve's
    /// <c>kv</c> would square the count on a circuit whose inlet temperature no parameter can reach —
    /// a closed adiabatic ring with a heat source, say — and report it solvable when it has no solution
    /// at all. An unmatched constraint is the honest answer there.
    /// </para>
    /// </remarks>
    private static Assignment.Result Promote(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        ImmutableArray<ComponentConstraint> constraints)
    {
        // The candidate lists are the physics (`Reach`); the matching is `Assignment`: first come in this
        // order, then augmented where that rescues a constraint the greedy pass left (`D-133`).
        var loops = Reach.Loops(graph);
        var candidates = ImmutableArray.CreateBuilder<ImmutableArray<(string Component, string Parameter)>>(constraints.Length);

        foreach (var constraint in constraints)
        {
            candidates.Add([.. Candidates(graph, hydraulics, loops, constraint)]);
        }

        return Assignment.Match(constraints, candidates.ToImmutable());
    }

    /// <summary>The sized parameters that could absorb one constraint, best first (<c>D-130</c>, <c>D-133</c>).</summary>
    /// <remarks>
    /// <para>
    /// One order, written once: the owner's own duty, then a pump's head, then a valve's <c>kv</c> and a leg
    /// split's <c>position</c>, and within a kind the actuator on the owner's own branch before any other. A
    /// mixed inlet or a node temperature takes only a mixing split's <c>position</c>, the nearest first.
    /// </para>
    /// <para>
    /// <strong>Only what can move the quantity is in the list</strong> (<c>D-133</c>, <c>S-45</c>). A split
    /// is offered for a temperature on the stream it mixes (<see cref="Reach.Stream"/>), a pump for a flow on
    /// a loop through it (<see cref="Reach.Loops"/>). Before that the lists reached across the whole
    /// hydraulic in graph order: with <c>PU_AHU.head</c> stated <c>HE_AHU</c>'s flow took <c>PU_RAD.head</c>,
    /// counting square at 44/44 while ranking 43; a mixed inlet took the first free split, which with one
    /// coil off (<c>S-56</c>) was the off coil's; and the injection header's setpoint took a consumer's valve
    /// downstream of it, non-finite at iteration zero. A node temperature is a setpoint held by the split
    /// whose stream reaches it (<c>S-48</c>, <c>34</c>: the circuit is <em>solved</em> into position; a
    /// controller does the same job dynamically). Reaching across the plant within those limits stays: two
    /// parallel branches below one pump both list it, and the matching decides who gets it.
    /// </para>
    /// <para>
    /// A stated <em>flow</em> never promotes the owner's <c>power</c>: the power does not appear in a flow
    /// residual, and promoting it would pair a column with a row it never enters (<c>S-72</c>). A pump with
    /// a stated rise has no head to give (<c>C-109</c>). A valve counts only on the constraint's own
    /// hydraulic: a coupled exchanger sits on a branch of each, and a valve on the other circuit cannot
    /// move this one's flow.
    /// </para>
    /// </remarks>
    private static IEnumerable<(string Component, string Parameter)> Candidates(
        CircuitGraph graph,
        ImmutableArray<HydraulicComponent> hydraulics,
        HydraulicBlocks loops,
        ComponentConstraint constraint)
    {
        var hydraulic = hydraulics.FirstOrDefault(candidate => candidate.Index == constraint.Hydraulic);
        var owner = graph.Components.FirstOrDefault(
            element => string.Equals(element.Name, constraint.Component, StringComparison.Ordinal));

        if (hydraulic is null)
        {
            return [];
        }

        // A node temperature a control line's setpoint stated is answered by the actuator the line names
        // and by nothing else (D-141): the loop said who holds it, and offering the nearest split instead
        // would put the design point on a valve the controller never touches.
        if (constraint.Kind is ConstraintKind.NodeTemperature
            && graph.Setpoints.FirstOrDefault(held =>
                held.Applied
                && string.Equals(held.Measured, constraint.Component, StringComparison.Ordinal)
                && string.Equals(held.Parameter, constraint.Parameter, StringComparison.Ordinal)) is { } setpoint)
        {
            var actuator = graph.Components.FirstOrDefault(
                element => string.Equals(element.Name, setpoint.ActuatorComponent, StringComparison.Ordinal));

            return actuator is not null && IsFree(graph, actuator, setpoint.ActuatorParameter)
                ? [(setpoint.ActuatorComponent, setpoint.ActuatorParameter)]
                : [];
        }

        return constraint.Kind switch
        {
            ConstraintKind.MixedInlet or ConstraintKind.NodeTemperature => Splits(graph, hydraulic, owner),
            ConstraintKind.FixedFlow => FlowActuators(graph, hydraulic, loops, constraint, owner),
            // Every kind the enum carries is handled above, so this is the guard for one added later: a
            // constraint nothing can absorb reports as over-specified rather than being quietly dropped.
            _ => [],
        };
    }

    /// <summary>The mixing splits whose stream holds the owner's temperature, the nearest first (<c>D-133</c>).</summary>
    /// <remarks>
    /// A split is offered only when the owner is on the stream it mixes (<see cref="Reach.Stream"/>): a
    /// consumer's valve drawing from a header cannot hold the header's setpoint, and before this it was
    /// offered for it and took it (<c>S-45</c>). A source whose inlet is the return is on no split's stream
    /// and is reported so, rather than handed the valve at its outlet.
    /// </remarks>
    private static IEnumerable<(string Component, string Parameter)> Splits(
        CircuitGraph graph, HydraulicComponent hydraulic, IFlowComponent? owner)
    {
        if (owner is null)
        {
            return [];
        }

        return hydraulic.Elements
            .OfType<ThreeWayValveComponent>()
            .Where(split => IsFree(graph, split, "position"))
            .Select(split => (Split: split, Depth: Reach.Stream(graph, split).TryGetValue(owner, out var depth) ? depth : -1))
            .Where(static ranked => ranked.Depth >= 0)
            .OrderBy(static ranked => ranked.Depth)
            .Select(static ranked => (ranked.Split.Name, "position"));
    }

    /// <summary>The actuators that could move a pinned flow, in <c>D-130</c>'s order.</summary>
    private static IEnumerable<(string Component, string Parameter)> FlowActuators(
        CircuitGraph graph, HydraulicComponent hydraulic, HydraulicBlocks loops, ComponentConstraint constraint, IFlowComponent? owner)
    {
        // The owner's own duty, unless the constraint is itself a stated flow.
        if (owner is not null && !IsStatedFlow(constraint.Parameter) && IsFree(graph, owner, "power"))
        {
            yield return (owner.Name, "power");
        }

        // A pump on a loop through the owner's branch (`Reach.Loops`, `D-133`), the one on the owner's own
        // branch before any other. A pump sharing no cycle with the branch cannot move its flow.
        var local = Reach.Local(graph, owner);
        var branches = hydraulic.Branches.Where(branch => owner is not null && branch.Path.Contains(owner)).ToArray();
        var pumps = hydraulic.Elements
            .Where(element => element is PumpComponent { StatedRise: null }
                && IsFree(graph, element, "head")
                && hydraulic.Branches.Any(branch => branch.Path.Contains(element)
                    && branches.Any(own => loops.Share(own, branch))))
            .ToArray();

        foreach (var pump in NearFirst(pumps, local))
        {
            yield return (pump.Name, "head");
        }

        // A valve on the owner's own branch: the parallel case, where branches sharing their endpoints share
        // a pressure difference and only the branch's own resistance can move its flow.
        if (owner is null)
        {
            yield break;
        }

        foreach (var branch in branches)
        {
            foreach (var element in branch.Path)
            {
                if (element is ValveComponentBase
                    && IsFree(graph, element, "kv")
                    && hydraulic.Elements.Contains(element))
                {
                    yield return (element.Name, "kv");
                }
            }
        }

        // A split the owner's branch ends at through a leg: its position is that leg's share of the flow,
        // and on a pumpless header it is the only thing that moves the source's flow (`D-133`).
        foreach (var split in Reach.LegSplits(graph, owner))
        {
            if (IsFree(graph, split, "position") && hydraulic.Elements.Contains(split))
            {
                yield return (split.Name, "position");
            }
        }
    }

    /// <summary>The candidates in the near set first, in their given order, then the rest in theirs.</summary>
    private static IEnumerable<IFlowComponent> NearFirst(IFlowComponent[] candidates, HashSet<IFlowComponent> near) =>
        candidates.Where(near.Contains).Concat(candidates.Where(candidate => !near.Contains(candidate)));

    /// <summary>Whether a parameter is available to be promoted.</summary>
    /// <param name="graph">The graph, for which sized values are provisionals.</param>
    /// <param name="component">The component that owns it.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns><see langword="true"/> when nothing has decided it yet.</returns>
    /// <remarks>
    /// Free means the script did not state it and the registry declares no visible default for it —
    /// which is exactly the <c>Sized</c> omission policy, and exactly what <c>D-02</c> leaves for
    /// something else to choose. A stated parameter is never promoted: two things setting one unknown
    /// is the trap <c>D-02</c> creates, and it reports as an over-specification naming both.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <strong>All three maps, and the third one was missing.</strong> <c>ComponentFactory.Defaults</c>
    /// already wrote down the rule this enforces — "well-posedness looks for a parameter no map claims"
    /// — but the check read two of them, so once the outer loop began filling
    /// <see cref="IComponent.SizedParameters"/> a parameter could be sized and promoted at once: chosen
    /// by a rule and solved for as an unknown, with the two answers disagreeing and nothing saying so.
    /// It cost nothing before <c>P3.7b</c>, because no map was ever filled.
    /// </para>
    /// <para>
    /// <strong>A sized value that is a bootstrap provisional is still free</strong> (<c>D-96</c>,
    /// <c>C-75</c>). The bootstrap writes a Kv into every unstated valve so that the valve exists, and
    /// from the third map alone that placeholder looked decided — so the balancing-valve promotion below
    /// never fired after <c>C-58</c>, and a pump with a stated head was refused with the exchanger's
    /// temperatures named instead of the valve closing on the surplus. The graph says which sized values
    /// are placeholders; a rule that later sizes one clears it, and a promotion keeps it a placeholder.
    /// </para>
    /// </remarks>
    private static bool IsFree(CircuitGraph graph, IFlowComponent component, string parameter) =>
        Ownership.IsFree(Ownership.Of(component, parameter, graph.ProvisionalParameters));

    /// <summary>Which hydraulic component an element belongs to.</summary>
    /// <returns>Its index, or zero when nothing claims it.</returns>
    private static int Owner(ImmutableArray<HydraulicComponent> hydraulics, IFlowComponent element)
    {
        foreach (var hydraulic in hydraulics)
        {
            if (hydraulic.Elements.Contains(element))
            {
                return hydraulic.Index;
            }
        }

        return 0;
    }
}
