using System.Collections.Immutable;

using FluidScript.Core.Components;

namespace FluidScript.Core.Topology;

/// <summary>Which actuator answers which constraint: first come in <c>D-130</c>'s order, then augmented (<c>D-133</c>).</summary>
/// <remarks>
/// <para>
/// The constraints and their candidate actuators form a bipartite graph, and a promotion is a matching in
/// it. The greedy pass is <c>D-130</c> verbatim -- each constraint in order takes the first free candidate
/// in its list -- and is kept, because its choices are the ones the rules prefer. What it cannot do is
/// look back: a constraint whose only viable actuator an earlier constraint took, when that earlier
/// constraint had another, is left unmatched and the circuit reported over-specified by one with the
/// wrong statement named (<c>S-45</c>). The second pass looks back exactly that far: for each unmatched
/// constraint it searches an augmenting path -- a chain of reassignments each along an edge of the graph
/// -- and applies it when one exists. Nothing is reassigned unless that rescues a constraint, so a circuit
/// the greedy pass already squares keeps every greedy claim.
/// </para>
/// <para>
/// This is Kuhn's algorithm on a matching seeded by the greedy pass, and the result is maximum: when it
/// ends, no augmenting path exists (Berge). The circuits here have tens of constraints, so the bound of
/// constraints × edges is not worth stating twice.
/// </para>
/// <para>
/// <strong>What stays unmatched is reported as a group, not a name.</strong> For an unmatched constraint,
/// the constraints reachable from it by alternating paths and the actuators they share are a Hall
/// violator: more demands than actuators between them, and any one of the demands is the one too many.
/// The over-specification names them all and what they share, rather than the one the walk happened to
/// end on.
/// </para>
/// </remarks>
internal static class Assignment
{
    /// <summary>The result of matching constraints to actuators.</summary>
    /// <param name="Promotions">The matched pairs, in constraint order.</param>
    /// <param name="Unmatched">Each constraint left without an actuator, with what it competes with.</param>
    public sealed record Result(ImmutableArray<Promotion> Promotions, ImmutableArray<Group> Unmatched);

    /// <summary>A constraint nothing answers, and the constraints and actuators it shares its shortfall with.</summary>
    /// <param name="Constraint">The unmatched constraint.</param>
    /// <param name="Sharing">Every constraint reachable from it by an alternating path, itself first, in constraint order.</param>
    /// <param name="Actuators">Every actuator those constraints could take, as <c>component.parameter</c>, in candidate order; empty when nothing reaches the constraint.</param>
    public sealed record Group(
        ComponentConstraint Constraint,
        ImmutableArray<ComponentConstraint> Sharing,
        ImmutableArray<string> Actuators);

    /// <summary>Matches each constraint to one candidate, greedy first and then augmented.</summary>
    /// <param name="constraints">The constraints, in the order <c>D-130</c> walks them.</param>
    /// <param name="candidates">For each constraint, its candidate actuators best first; one list per constraint.</param>
    /// <returns>The promotions and the unmatched groups.</returns>
    public static Result Match(
        ImmutableArray<ComponentConstraint> constraints,
        ImmutableArray<ImmutableArray<(string Component, string Parameter)>> candidates)
    {
        var holder = new Dictionary<string, int>(StringComparer.Ordinal);
        var claim = new string?[constraints.Length];
        var named = new Dictionary<string, (string Component, string Parameter)>(StringComparer.Ordinal);

        for (var i = 0; i < constraints.Length; i++)
        {
            foreach (var candidate in candidates[i])
            {
                named.TryAdd(Ownership.Key(candidate.Component, candidate.Parameter), candidate);
            }
        }

        // D-130: first come, first free candidate.
        for (var i = 0; i < constraints.Length; i++)
        {
            foreach (var candidate in candidates[i])
            {
                var key = Ownership.Key(candidate.Component, candidate.Parameter);

                if (holder.TryAdd(key, i))
                {
                    claim[i] = key;
                    break;
                }
            }
        }

        // D-133: an augmenting path for each constraint the greedy pass left, in the same order.
        for (var i = 0; i < constraints.Length; i++)
        {
            if (claim[i] is null)
            {
                Augment(i, new HashSet<string>(StringComparer.Ordinal));
            }
        }

        var promotions = ImmutableArray.CreateBuilder<Promotion>();
        var unmatched = ImmutableArray.CreateBuilder<Group>();

        for (var i = 0; i < constraints.Length; i++)
        {
            if (claim[i] is { } key)
            {
                var (component, parameter) = named[key];
                promotions.Add(new Promotion(component, parameter, constraints[i]));
            }
            else
            {
                unmatched.Add(GroupOf(i));
            }
        }

        return new Result(promotions.ToImmutable(), unmatched.ToImmutable());

        bool Augment(int constraint, HashSet<string> visited)
        {
            foreach (var candidate in candidates[constraint])
            {
                var key = Ownership.Key(candidate.Component, candidate.Parameter);

                if (!visited.Add(key))
                {
                    continue;
                }

                if (!holder.TryGetValue(key, out var other) || Augment(other, visited))
                {
                    holder[key] = constraint;
                    claim[constraint] = key;
                    return true;
                }
            }

            return false;
        }

        Group GroupOf(int constraint)
        {
            var sharing = new List<int> { constraint };
            var actuators = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var at = 0; at < sharing.Count; at++)
            {
                foreach (var candidate in candidates[sharing[at]])
                {
                    var key = Ownership.Key(candidate.Component, candidate.Parameter);

                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    actuators.Add($"{candidate.Component}.{candidate.Parameter}");

                    if (holder.TryGetValue(key, out var other) && !sharing.Contains(other))
                    {
                        sharing.Add(other);
                    }
                }
            }

            sharing.Sort();

            return new Group(
                constraints[constraint],
                [.. sharing.Select(index => constraints[index])],
                [.. actuators]);
        }
    }
}
