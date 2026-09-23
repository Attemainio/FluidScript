using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Sizers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Passes;

public sealed partial class OuterLoop
{
    /// <summary>The provisional sizes that let a first graph exist.</summary>
    /// <param name="model">The bound model.</param>
    /// <returns>An overlay covering every parameter this loop intends to size.</returns>
    /// <remarks>
    /// <strong>These are not a design.</strong> A pipe has no component at all without a bore, so
    /// something has to go in before flows can be estimated; a sizer's <see cref="ISizer.Provisional"/>
    /// is that something, and the first real pass replaces it. Each is flagged provisional in the overlay
    /// so that counting treats it as undecided (<c>D-96</c>): a constraint the loop cannot otherwise meet
    /// may promote it, and then it <em>is</em> solved against, by the solver rather than by a rule.
    /// </remarks>
    private SizingOverlay Bootstrap(SemanticModel model)
    {
        var overlay = SizingOverlay.Empty;

        foreach (var symbol in model.Components)
        {
            if (symbol.Kind is not { } kind)
            {
                continue;
            }

            foreach (var sizer in sizers)
            {
                foreach (var (parameter, value) in sizer.Provisional)
                {
                    if (!symbol.Parameters.ContainsKey(parameter)
                        && kind.Parameters.TryGetValue(parameter, out var info)
                        && info.OmissionBehavior == FluidScript.Core.Language.Registry.ParameterOmissionBehavior.Size)
                    {
                        overlay = overlay.With(symbol.Name, parameter, value, provisional: true);
                    }
                }
            }
        }

        return overlay;
    }

    /// <summary>Runs every rule over every component it applies to.</summary>
    /// <param name="graph">The graph as lowered for this pass.</param>
    /// <param name="iterate">The flows and states to size against.</param>
    /// <param name="previous">Last pass's overlay, kept for anything no rule spoke about.</param>
    /// <param name="posedness">Which parameters the solver claimed, or <see langword="null"/> before the first check.</param>
    /// <param name="layout">Where the iterate keeps each unknown, or <see langword="null"/> to derive it.</param>
    /// <param name="solved">Whether <paramref name="iterate"/> is a converged solution rather than the seed; a rule that reads pressures off a solved field only waits when it is not.</param>
    /// <returns>The new overlay, the bases, and the notes.</returns>
    /// <remarks>
    /// <strong>A promoted parameter is skipped, and that is the whole of the division of labour.</strong>
    /// A parameter omitted with a <c>Size</c> policy is normally chosen here, but where a stated
    /// constraint needs somewhere to go, well-posedness promotes one such parameter to an unknown and
    /// the solver determines it. Sizing it as well would give two answers to one question, with nothing
    /// saying they disagreed. A rule is skipped whole when <em>any</em> of its parameters is promoted:
    /// <see cref="ValveSizer"/> reports an <c>authority</c> for the Kv it chose, and a Kv the solver is
    /// choosing instead would make that authority a number about a valve that does not exist (<c>C-75</c>).
    /// </remarks>
    private (SizingOverlay Overlay, ImmutableDictionary<string, string> Bases, ImmutableArray<string> Notes, ImmutableArray<Diagnostics.Diagnostic> Raised) Apply(
        CircuitGraph graph,
        StateVector iterate,
        SizingOverlay previous,
        WellPosednessResult? posedness = null,
        SystemLayout? layout = null,
        bool solved = true)
    {
        var posed = posedness ?? WellPosedness.Check(graph);
        var places = layout ?? SystemLayout.Build(graph, posed.Counting);
        var promoted = posed.Counting.Promotions.Select(static promotion => promotion.Label).ToHashSet(StringComparer.Ordinal);

        var overlay = previous;
        var bases = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var notes = ImmutableArray.CreateBuilder<string>();
        var raised = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        CloseEnergyBalance(graph, ref overlay, bases);

        foreach (var component in graph.Components)
        {
            // A two-way valve on a three-way valve's switched leg with nothing deciding its `kv` is a
            // balancing valve, and the authority rule is the wrong rule for it: `BypassValves` sets it.
            if (component is Valve balancing && OnSwitchedLeg(graph, balancing) is not null && !Claimed(balancing, "kv", promoted))
            {
                continue;
            }

            foreach (var sizer in sizers)
            {
                if (!sizer.CanSize(component)
                    || sizer.Parameters.All(parameter => Claimed(component, parameter, promoted))
                    || sizer.Parameters.Any(parameter => promoted.Contains(Ownership.Key(component.Name, parameter))))
                {
                    continue;
                }

                if (Context(graph, places, iterate, component) is not { } context)
                {
                    continue;
                }

                var sized = sizer.Size(component, context);

                if (!sized.IsSuccess)
                {
                    continue;
                }

                foreach (var (parameter, value) in sized.Value.Values)
                {
                    if (Claimed(component, parameter, promoted))
                    {
                        continue;
                    }

                    overlay = overlay.With(
                        component.Name,
                        parameter,
                        value.Value);
                    bases[Ownership.Key(component.Name, parameter)] = value.Basis;
                }

                notes.AddRange(sized.Value.Notes);
                raised.AddRange(sized.Value.Diagnostics);
            }
        }

        ThreeWay(graph, places, iterate, ref overlay, bases, notes, promoted);
        BypassValves(graph, places, iterate, ref overlay, bases, notes, promoted, solved);
        Unsized(graph, overlay, bases, notes);

        return (overlay, bases.ToImmutable(), notes.ToImmutable(), raised.ToImmutable());
    }

    /// <summary>Says a balancing valve kept its bootstrap value, and why.</summary>
    private static void Kept(
        Valve valve,
        SizingOverlay overlay,
        ImmutableDictionary<string, string>.Builder bases,
        ImmutableArray<string>.Builder notes,
        string reason)
    {
        foreach (var (parameter, value) in overlay.For(valve.Name))
        {
            var key = Ownership.Key(valve.Name, parameter);

            if (!bases.ContainsKey(key))
            {
                bases[key] = ProvisionalBasis(value, reason);
            }
        }

        notes.Add(
            $"{valve.Name} is still the bootstrap value no rule replaced, because {reason}. It is read as a "
            + "balancing valve on a three-way valve's leg — state a `kv`, or read the result knowing this one number is arbitrary.");
    }

    /// <summary>Says a three-way valve kept its bootstrap value, and why (<c>C-60</c>).</summary>
    /// <param name="valve">The valve that was not sized.</param>
    /// <param name="overlay">The overlay holding its provisional values.</param>
    /// <param name="bases">Where the explanation is written, keyed <c>component.parameter</c>.</param>
    /// <param name="notes">Where the user-facing warning goes.</param>
    /// <param name="reason">What stopped the rule, as a sentence fragment.</param>
    /// <remarks>
    /// <strong><see cref="Unsized"/> cannot cover this case and must not be made to.</strong> Its check
    /// is deliberately static — does <em>any</em> sizer both <c>CanSize</c> this component and list this
    /// parameter — so that a value sized on an earlier pass and skipped on a later one is not slandered
    /// as a bootstrap leftover. <see cref="ValveSizer"/> now answers yes for every three-way valve, so a
    /// three-way this pass declines would fall through that check and be reported with <em>no basis at
    /// all</em>, which is <c>D-02</c>'s "absence, never null" read backwards and exactly the defect
    /// <c>C-60</c> recorded. The pass that declined is the only thing that knows why, so it says so.
    /// </remarks>
    private static void Declined(
        ThreeWayValve valve,
        SizingOverlay overlay,
        ImmutableDictionary<string, string>.Builder bases,
        ImmutableArray<string>.Builder notes,
        string reason)
    {
        foreach (var (parameter, value) in overlay.For(valve.Name))
        {
            var key = Ownership.Key(valve.Name, parameter);

            if (!bases.ContainsKey(key))
            {
                bases[key] = ProvisionalBasis(value, reason);
            }
        }

        notes.Add(
            $"{valve.Name} is still the bootstrap value no rule replaced, because {reason}. It was chosen "
            + "to disturb the first pass as little as possible, not to suit this circuit — state a `kv`, "
            + "or read the result knowing this one number is arbitrary.");
    }

    /// <summary>The basis line of a bootstrap value no rule replaced: the number, and why it stayed.</summary>
    /// <param name="value">The provisional.</param>
    /// <param name="reason">What stopped every rule, as a sentence fragment.</param>
    /// <returns>The basis.</returns>
    private static string ProvisionalBasis(Quantity value, string reason) =>
        string.Create(CultureInfo.InvariantCulture, $"{value.SiValue:0.###} — provisional, not chosen: {reason}");

    /// <summary>Reports any bootstrap provisional no rule was ever able to replace.</summary>
    /// <param name="graph">The graph as lowered for this pass.</param>
    /// <param name="overlay">The overlay the passes settled on.</param>
    /// <param name="bases">The bases being built; a surviving provisional gets one saying so.</param>
    /// <param name="notes">The notes being built.</param>
    /// <remarks>
    /// <para>
    /// <strong><see cref="Bootstrap"/> promises that "the first real pass replaces it", and where no rule
    /// can size the component it never does</strong> (<c>C-60</c>). The provisional is applied by
    /// <em>kind</em> -- any parameter the registry marks sizable -- while a rule applies to a
    /// <em>type</em>, and the two do not agree. <c>ValveSizer.CanSize</c> is <c>component is Valve</c>, so
    /// a <c>three_way_valve</c> is handed <see cref="ISizer.Provisional"/> and never sized: on
    /// <c>m2-cooling-loop</c> that leaves <c>3WV</c> at <strong>Kv 630</strong>, the largest row in the
    /// series, chosen deliberately to behave like an open port during the bootstrap. It then stays one --
    /// the valve drops nothing, the primary's 300/280 kPa boundary over-drives the loop by about 14 kPa,
    /// and the pump is asked for negative head to absorb it.
    /// </para>
    /// <para>
    /// <strong>Reported rather than corrected, because the rule that would correct it does not exist
    /// yet.</strong> A three-way valve is a junction element, so it appears in no branch's <c>Path</c> and
    /// <see cref="Context"/> can build it no context -- it sits on three branches at once. That is the
    /// same structural limit as <c>C-49</c>'s parallel set and it has the same answer: a pass over
    /// <see cref="OuterLoop"/>, which can see sibling branches, rather than an <see cref="ISizer"/>, which
    /// is handed one. Until that lands the honest thing is to say the value was never chosen, because
    /// <c>D-02</c> allows a size or a visible decided default and a surviving provisional is neither.
    /// </para>
    /// </remarks>
    private void Unsized(
        CircuitGraph graph,
        SizingOverlay overlay,
        ImmutableDictionary<string, string>.Builder bases,
        ImmutableArray<string>.Builder notes)
    {
        foreach (var component in graph.Components)
        {
            foreach (var (parameter, value) in overlay.For(component.Name))
            {
                var key = Ownership.Key(component.Name, parameter);

                if (bases.ContainsKey(key)
                    || sizers.Any(sizer =>
                        sizer.CanSize(component) && sizer.Parameters.Contains(parameter)))
                {
                    continue;
                }

                bases[key] = ProvisionalBasis(value, $"no rule sizes a {component.Kind}'s `{parameter}`");

                notes.Add(
                    $"{component.Name}.{parameter} is still the bootstrap value no rule replaced, because "
                    + $"nothing sizes a {component.Kind}'s `{parameter}` yet. It was chosen to disturb the "
                    + "first pass as little as possible, not to suit this circuit — state it, or read the "
                    + "result knowing this one number is arbitrary.");
            }
        }
    }

    /// <summary>Closes a steady circuit's duty sum when exactly one one-sided duty is omitted.</summary>
    /// <param name="graph">The graph as lowered for this pass.</param>
    /// <param name="overlay">The sizing choices to add the inferred duty to.</param>
    /// <param name="bases">Where to explain the choice.</param>
    /// <remarks>
    /// <strong>One missing term in ΣQ̇ = 0 is determined; two are a design choice.</strong> Loads already
    /// carry their physical negative sign, so negating the sum of every stated/defaulted duty produces
    /// the source duty directly. Coupled exchangers are excluded because their transfer spans hydraulic
    /// components; applying a per-component closure to either side would count the same transfer twice.
    /// </remarks>
    private static void CloseEnergyBalance(
        CircuitGraph graph,
        ref SizingOverlay overlay,
        ImmutableDictionary<string, string>.Builder bases)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var hydraulic in HydraulicPartition.Of(graph))
        {
            if (!hydraulic.IsClosed)
            {
                continue;
            }

            var duties = hydraulic.Elements
                .OfType<HeatExchanger>()
                .DistinctBy(static exchanger => exchanger.Name)
                .ToArray();

            if (duties.Any(static exchanger => exchanger.SecondarySideConnected))
            {
                continue;
            }

            var omitted = duties
                .Where(static exchanger => Ownership.Of(exchanger, "power") is ParameterState.Free or ParameterState.SizedFinal)
                .ToArray();

            if (omitted.Length != 1 || !completed.Add(omitted[0].Name))
            {
                continue;
            }

            var source = omitted[0];
            var power = -duties
                .Where(exchanger => !ReferenceEquals(exchanger, source))
                .Sum(static exchanger => exchanger.Power);

            overlay = overlay.With(source.Name, "power", Quantity.FromSi(power, Dimension.Power));
            bases[$"{source.Name}.power"] =
                $"the closed-circuit energy balance: {-power / 1000:G4} kW from the other duties requires {power / 1000:G4} kW here";
        }
    }

    private static bool Claimed(IFlowComponent component, string parameter, HashSet<string> promoted) =>
        Ownership.Of(component, parameter, promoted: promoted) is ParameterState.Stated or ParameterState.Defaulted or ParameterState.Promoted;
}
