using FluidScript.Core.Components;
using FluidScript.Core.Components.Declarations;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Results;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Seeding;

public static partial class SolutionSeed
{
    /// <summary>A component's resolvable parameters as the seed holds them, promoted values included.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the state vector keeps each promoted parameter.</param>
    /// <param name="values">The seed so far; <see cref="Promoted"/> must have run.</param>
    /// <param name="element">The component.</param>
    /// <returns>
    /// One value per <c>IFlowComponent.Resolvable</c> entry, the promoted ones read from the seed, or
    /// <see langword="null"/> when nothing of this component's is promoted and its own values serve.
    /// </returns>
    private static double[]? Parameters(CircuitGraph graph, SystemLayout layout, double[] values, IFlowComponent element)
    {
        double[]? parameters = null;

        foreach (var (index, _, parameter) in PromotedColumns(layout, element.Name))
        {
            // Kv and head only. A promoted position is seeded at mid-travel (`S-50`), and letting the
            // walk lay the legs' drops at 0.5 instead of the valve's own 1.0 was measured (2026-09-20):
            // it cost one to two first-pass iterations on every three-way-valve circuit in the corpus
            // (cooling loop 5 to 7, header 5 to 6, s55 9 to 11) and bought nothing the solve needed.
            // The Kv and head columns are the ones that were nearly singular; the position column was not.
            if (string.Equals(parameter, "position", StringComparison.Ordinal))
            {
                continue;
            }

            for (var slot = 0; slot < element.Resolvable.Length; slot++)
            {
                if (string.Equals(element.Resolvable[slot].Name, parameter, StringComparison.Ordinal))
                {
                    parameters ??= [.. element.Resolvable.Select(static resolvable => resolvable.Value)];
                    parameters[slot] = values[index];
                }
            }
        }

        return parameters;
    }

    /// <summary>The promoted columns of the layout: each one's index, its owner and the parameter it holds.</summary>
    /// <param name="layout">The unknown layout.</param>
    /// <param name="owner">An owner to keep to, or <see langword="null"/> for every column.</param>
    /// <returns>In column order.</returns>
    /// <remarks>
    /// A promoted column's name is the promotion's label, <c>3WV.position</c>, so the parameter is whatever
    /// follows the owner's name and the dot. Three walks read it that way before <c>70</c>'s R4; this is the one.
    /// </remarks>
    private static IEnumerable<(int Index, string Owner, string Parameter)> PromotedColumns(SystemLayout layout, string? owner = null)
    {
        for (var index = layout.PromotionOffset; index < layout.Count; index++)
        {
            var declaration = layout.Unknowns[index];

            if (owner is not null && !string.Equals(declaration.OwnerComponentId, owner, StringComparison.Ordinal))
            {
                continue;
            }

            yield return (index, declaration.OwnerComponentId, declaration.Name[(declaration.OwnerComponentId.Length + 1)..]);
        }
    }

    /// <summary>Seeds every promoted parameter from the value its own component holds.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the state vector keeps each unknown.</param>
    /// <param name="values">The seed being filled.</param>
    /// <remarks>
    /// <para>
    /// <strong>Zero is not a neutral starting point for a promoted parameter; for two of the three
    /// promotable kinds it is a <em>bound</em>.</strong> <see cref="FluidScript.Core.Components.Valves.ValveLaw.Opening"/>
    /// clamps position into <c>[0, 1]</c>, so a column seeded at 0 has a one-sided derivative at best
    /// and a dead one at worst -- and <c>m2-distribution-header</c> came out <c>Singular</c> at
    /// <em>iteration zero</em> with both its promoted positions sitting exactly there. A pump seeded at
    /// zero head is a loop with no driver, which is the same problem one variable over (<c>S-26</c>).
    /// </para>
    /// <para>
    /// <strong>The component is the only thing that knows.</strong> <c>Resolvable</c> exists to say what
    /// a component uses when nothing supplies one, in SI, and <see cref="SystemLayout"/> already reads
    /// the unit off it for the same reason (<c>D-30</c>). Reading the value here needs no knowledge of
    /// the kind, which is what keeps this seed free of a table of defaults that would drift.
    /// </para>
    /// <para>
    /// The old behaviour was a placeholder and said so: "a promoted parameter carries no unit yet and
    /// falls to zero, which is honest and is the thing the outer loop replaces when promotion becomes
    /// live". Promotion is live.
    /// </para>
    /// <para>
    /// <strong>One is a bound as surely as zero is, and a valve's default position sits on it</strong>
    /// (<c>S-50</c>). <c>ThreeWayValve.Position</c> defaults to 1 -- correct for a valve nobody is
    /// controlling, and the worst place to start one the solver has to move. On the equal-percentage
    /// characteristic every promotable position uses, phi(1) = 1 with slope 3.91 while the complementary
    /// leg sits at phi(0) = 0.02 with slope 0.078: **fifty times flatter**, so the column is dominated by
    /// the control leg and carries almost no signal about the bypass. Mid-travel is symmetric --- both
    /// legs at 0.141, both slopes 0.553 --- and it is where a valve sized for authority 0.5 is meant to
    /// sit anyway. So a promoted parameter bounded on both sides whose component value lies *on* a bound
    /// is seeded at the middle of its range instead.
    /// </para>
    /// <para>
    /// It applies to nothing else in the corpus: <c>kv</c> is bounded below only and <c>head</c> not at
    /// all, so both keep the component's own value, which is what the paragraph above is about.
    /// </para>
    /// <para>
    /// <strong>A promoted <c>kv</c> is the exception, and it is seeded from the Kv law</strong>
    /// (<see cref="PromotedKv"/>). Its own value is the bootstrap's provisional -- the catalogue's largest
    /// row, 630, chosen to disturb the bootstrap least (<c>D-96</c>) -- which puts a few pascals across a
    /// valve the solve has to close to a hundred kilopascals. The law's slope in <c>√Δp</c> is steepest
    /// exactly there, and on the substation Newton never recovered from it (<c>P4.1</c>); on the
    /// <c>head=15</c> loop it cost six iterations where two suffice (<c>C-75</c>).
    /// </para>
    /// </remarks>
    private static void Promoted(CircuitGraph graph, SystemLayout layout, double[] values)
    {
        foreach (var (index, name, parameter) in PromotedColumns(layout))
        {
            var owner = graph.Components.FirstOrDefault(element => string.Equals(element.Name, name, StringComparison.Ordinal));

            if (owner is null)
            {
                continue;
            }

            foreach (var resolvable in owner.Resolvable)
            {
                if (!string.Equals(resolvable.Name, parameter, StringComparison.Ordinal)
                    || !double.IsFinite(resolvable.Value))
                {
                    continue;
                }

                values[index] = owner is PumpComponent && resolvable.Name is "head" && resolvable.Value == 0
                            ? NominalPumpHead
                            : owner is ValveComponent && resolvable.Name is "kv" && PromotedKv(graph, layout, values, owner) is { } kv
                            ? kv
                            : owner is HeatExchangerComponent exchanger && resolvable.Name is "power" && PromotedPower(graph, layout, values, exchanger) is { } power
                            ? power
                            : Interior(resolvable);
            }
        }
    }

    /// <summary>Whether a bare pump's head is an unknown this solve is expected to choose.</summary>
    private static bool PromotesHead(SystemLayout layout, PumpComponent pump) =>
        PromotedColumns(layout, pump.Name).Any(static column => column.Parameter is "head");

    /// <summary>The Kv a promoted valve is seeded at: the Kv law at the seeded flow, taking half of what the circuit offers.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where branch flows are stored.</param>
    /// <param name="values">The seed so far, with its flows and pressures laid.</param>
    /// <param name="valve">The valve whose <c>kv</c> is promoted.</param>
    /// <returns>Kv in m³/h, or <see langword="null"/> when nothing says what the circuit offers.</returns>
    /// <remarks>
    /// What the circuit offers is the difference between the stated pressures at the branch's two ends,
    /// else the stated head of a pump on the valve's branch; the valve is seeded to take half
    /// of it, which is the authority every sizing rule aims at (<c>24</c>). A seed, not a claim: the solve
    /// moves it wherever the promotion's constraint needs, and this only starts it on the right side of
    /// the square root.
    /// </remarks>
    private static double? PromotedKv(CircuitGraph graph, SystemLayout layout, double[] values, IFlowComponent valve)
    {
        var branch = graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(valve));

        if (branch is null)
        {
            return null;
        }

        var flow = Math.Abs(values[layout.BranchFlow(branch.Index)]);

        // The branch's own ends first -- an open circuit's supply and return, which is what the
        // substation's primary offers its valve -- then a stated head on the branch.
        double? offered =
            HydraulicPartition.Stated(branch.From.Element, HydraulicPartition.Pressure) is { } from
            && HydraulicPartition.Stated(branch.To.Element, HydraulicPartition.Pressure) is { } to
                ? Math.Abs(from - to)
                : null;

        if (offered is null)
        {
            foreach (var element in branch.Path)
            {
                // A stated rise is the drop the loop has to spend; a stated head is that rise at the
                // reference density (C-109).
                if (element is PumpComponent { StatedRise: { } rise })
                {
                    offered = rise;
                    break;
                }

                if (element is PumpComponent && HydraulicPartition.Stated(element, "head") is { } head)
                {
                    offered = Hydrostatic.Pressure(ReferenceDensity, head);
                    break;
                }
            }
        }

        if (offered is { } drop && flow > 0)
        {
            var kv = ValveLaw.RequiredKv(flow, 0.5 * drop, ReferenceDensity);

            return double.IsFinite(kv) && kv > 0 ? kv : null;
        }

        return flow > 0 ? SiblingKv(graph, layout, values, branch, valve, flow) : null;
    }

    /// <summary>The Kv a balancing valve on a parallel branch needs: what the sibling branch drops at its seeded flow, less what the valve's own branch drops without it.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where branch flows are stored.</param>
    /// <param name="values">The seed so far.</param>
    /// <param name="branch">The valve's branch.</param>
    /// <param name="valve">The valve.</param>
    /// <param name="flow">kg/s along the valve's branch, positive.</param>
    /// <returns>Kv in m³/h, or <see langword="null"/> when no sibling shares the branch's ends or the remainder is not a drop the valve can take.</returns>
    /// <remarks>
    /// Two branches between the same two junctions share their pressure difference, and a balancing valve
    /// on one exists to make its branch's drop match the other's at the design flows (<c>23</c>'s parallel
    /// row). The remainder is the whole of what the valve takes, not the half a stated head is shared at:
    /// the sibling has already said what the difference is. Seeded at the catalogue's largest Kv the valve
    /// dropped a few pascals where it had to drop ten kilopascals, and its column was flat enough that the
    /// first Newton step ran it to zero (<c>S-73</c>).
    /// </remarks>
    private static double? SiblingKv(
        CircuitGraph graph, SystemLayout layout, double[] values, Branch branch, IFlowComponent valve, double flow)
    {
        if (!graph.Substance.FromPressureTemperature(
                Quantity.FromSi(Tolerances.PressureScale, Dimension.Pressure),
                Quantity.FromSi(Datum(graph), Dimension.Temperature)).TryGetValue(out var state))
        {
            return null;
        }

        double Drops(Branch candidate, IFlowComponent? except)
        {
            var carried = Math.Abs(values[layout.BranchFlow(candidate.Index)]);

            return candidate.Path
                .Where(part => !ReferenceEquals(part, except))
                .Sum(part => BranchResistance.Of(
                    graph, state, part, carried, Parameters(graph, layout, values, part), Tolerances.SeedValveExcursion));
        }

        foreach (var sibling in graph.Branches)
        {
            var parallel = sibling.Index != branch.Index
                && ((ReferenceEquals(sibling.From.Element, branch.From.Element) && ReferenceEquals(sibling.To.Element, branch.To.Element))
                    || (ReferenceEquals(sibling.From.Element, branch.To.Element) && ReferenceEquals(sibling.To.Element, branch.From.Element)));

            if (!parallel)
            {
                continue;
            }

            var remainder = Drops(sibling, except: null) - Drops(branch, valve);
            var kv = ValveLaw.RequiredKv(flow, remainder, ReferenceDensity);

            if (remainder > 0 && double.IsFinite(kv) && kv > 0)
            {
                return kv;
            }
        }

        return null;
    }

    /// <summary>A promoted duty's seed: the seeded flow times the enthalpy change its stated temperatures span.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where branch flows are stored.</param>
    /// <param name="values">The seed so far, with its flows laid.</param>
    /// <param name="exchanger">The exchanger whose <c>power</c> is promoted.</param>
    /// <returns>W, positive into the fluid, or <see langword="null"/> without both side-1 temperatures.</returns>
    /// <remarks>
    /// A promoted power's own value is zero, which is not a neutral start: every temperature row around
    /// the coil already assumes its duty, and a coil seeded at no duty on a stream seeded at its design
    /// flow puts the whole duty into the first Newton step (<c>S-69</c>). The stated <c>in</c> and
    /// <c>out</c> with the seeded flow are the duty's own definition.
    /// </remarks>
    private static double? PromotedPower(CircuitGraph graph, SystemLayout layout, double[] values, HeatExchangerComponent exchanger)
    {
        var branch = graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(exchanger));

        if (branch is null
            || HydraulicPartition.Stated(exchanger, "in") is not { } inlet
            || HydraulicPartition.Stated(exchanger, "out") is not { } outlet)
        {
            return null;
        }

        var flow = Math.Abs(values[layout.BranchFlow(branch.Index)]);
        var power = flow * (Enthalpy(graph.Substance, Tolerances.PressureScale, outlet) - Enthalpy(graph.Substance, Tolerances.PressureScale, inlet));

        return double.IsFinite(power) && flow > 0 ? power : null;
    }

    /// <summary>Where a promoted parameter starts: its own value, unless that value is a bound.</summary>
    /// <param name="resolvable">The component's declaration of the parameter.</param>
    /// <returns>
    /// The middle of the range for a two-sided parameter sitting on either bound, and the component's own
    /// value otherwise. Dimensionless or SI, whichever the parameter is.
    /// </returns>
    /// <remarks>
    /// <strong>A bound is not somewhere the iterate may not go; it is somewhere the derivative stops
    /// existing</strong> --- <see cref="FluidScript.Core.Components.Valves.ValveLaw.Opening"/> makes that argument for the clamp
    /// it had to remove, and starting on one is the same mistake made a step earlier. Only a parameter
    /// bounded on <em>both</em> sides has a middle to fall back to; one bounded on one side has no
    /// non-arbitrary interior point, so its own value stands.
    /// </remarks>
    private static double Interior(ResolvedParameter resolvable)
    {
        if (resolvable.Minimum is not { } minimum || resolvable.Maximum is not { } maximum
            || !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum)
        {
            return resolvable.Value;
        }

        return resolvable.Value <= minimum || resolvable.Value >= maximum
            ? (minimum + maximum) / 2
            : resolvable.Value;
    }
}
