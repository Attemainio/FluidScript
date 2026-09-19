using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Sizing;

/// <summary>What determined a branch's flow estimate.</summary>
/// <remarks>
/// Ordered by authority, weakest first: a later basis overrides an earlier one, and the enum's order
/// is what <see cref="BranchFlows"/> compares. Extending it means deciding where the new rule ranks.
/// </remarks>
public enum FlowBasis
{
    /// <summary>Nothing determined it, so the estimate is <see cref="BranchFlows.Nominal"/>.</summary>
    /// <remarks>This is the case <c>24</c>'s <c>FS2304</c> reports once a sizer asks for the number.</remarks>
    Nominal = 0,
    /// <summary>A junction element the branch reaches shares its estimate.</summary>
    Propagated = 1,

    /// <summary>A three-way valve's leg, partitioned from a common leg a duty fixed; the two legs sum to that duty.</summary>
    /// <remarks>Above <see cref="Propagated"/> because the seed's forest keeps its strongest estimates as chords (<c>S-68</c>): two partitioned legs kept, and the coil they sum to comes out at its rating.</remarks>
    Partitioned = 2,

    /// <summary>An exchanger's stated duty and terminal temperatures fix it (<c>24</c>, step 1).</summary>
    Duty = 3,

    /// <summary>The script stated a flow on the branch.</summary>
    Stated = 4,
}

/// <summary>One branch's flow estimate, and what determined it.</summary>
/// <param name="Magnitude">
/// kg/s, unsigned. Orientation is the branch decomposition's choice and means nothing to a sizing
/// rule, all of which are written on <c>|ṁ|</c>; <see cref="Solvers.SolutionSeed"/> is what turns
/// magnitudes into a signed, mass-consistent field.
/// </param>
/// <param name="Basis">What determined it.</param>
/// <param name="Source">
/// The component the estimate came from, or the empty string for <see cref="FlowBasis.Nominal"/>.
/// </param>
public readonly record struct BranchFlow(double Magnitude, FlowBasis Basis, string Source);

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
public static class BranchFlows
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
                if (junction is ThreeWayValve { BypassConnected: true } valve)
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

    /// <summary>Propagates one common circulation estimate as a partition across a three-way valve.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="estimates">The estimates being completed.</param>
    /// <param name="valve">The mixing or diverting junction.</param>
    /// <returns><see langword="true"/> when an estimate was added.</returns>
    private static bool PropagateThreeWay(
        CircuitGraph graph,
        BranchFlow[] estimates,
        ThreeWayValve valve)
    {
        var common = -1;
        var first = -1;
        var second = -1;

        foreach (var branch in graph.Branches)
        {
            if (!Meets(branch, valve))
            {
                continue;
            }

            switch (FluidScript.Core.Solvers.ValveLegs.PortName(branch, valve))
            {
                case "ab":
                    common = branch.Index;
                    break;
                case "a":
                    first = branch.Index;
                    break;
                case "b":
                    second = branch.Index;
                    break;
            }
        }

        if (common < 0 || first < 0 || second < 0)
        {
            return false;
        }

        var commonKnown = estimates[common].Basis > FlowBasis.Nominal;
        var firstKnown = estimates[first].Basis > FlowBasis.Nominal;
        var secondKnown = estimates[second].Basis > FlowBasis.Nominal;

        // A leg partitioned from a common leg that a duty fixed carries that duty's authority: the two
        // legs sum to the coil's flow exactly, and the seed's forest keeps them as chords so that the
        // coil, their sum, comes out at its rating (`S-68`).
        var partitioned = estimates[common].Basis >= FlowBasis.Duty ? FlowBasis.Partitioned : FlowBasis.Propagated;

        if (commonKnown && !firstKnown && !secondKnown)
        {
                var firstMagnitude = estimates[common].Magnitude
                    * (MixingFraction(graph, estimates[common].Source, graph.Branches[first], valve) ?? 0.5);
                var secondMagnitude = estimates[common].Magnitude - firstMagnitude;

                estimates[first] =
                    new BranchFlow(firstMagnitude, partitioned, estimates[common].Source);
                estimates[second] =
                    new BranchFlow(secondMagnitude, partitioned, estimates[common].Source);

            return true;
        }

        if (commonKnown && firstKnown != secondKnown)
        {
            var known = firstKnown ? first : second;
            var missing = firstKnown ? second : first;
            var remainder = estimates[common].Magnitude - estimates[known].Magnitude;

            if (remainder > Solvers.Tolerances.FlowZero)
            {
                estimates[missing] =
                    new BranchFlow(remainder, partitioned, estimates[common].Source);
                return true;
            }
        }

        if (!commonKnown && firstKnown && secondKnown)
        {
            estimates[common] = new BranchFlow(
                estimates[first].Magnitude + estimates[second].Magnitude,
                FlowBasis.Propagated,
                estimates[first].Source);
            return true;
        }

        return false;
    }
    /// <summary>Returns the hot-leg fraction implied by a load's design temperatures and the temperature that feeds its valve's <c>a</c> port.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="loadName">The exchanger that supplied the common-leg duty estimate.</param>
    /// <param name="feed">The branch on the valve's <c>a</c> port, which is where the hot water arrives from.</param>
    /// <param name="valve">The valve, so the walk along <paramref name="feed"/> starts from its end.</param>
    /// <returns>A fraction strictly between zero and one, or <see langword="null"/> without enough evidence.</returns>
    /// <remarks>
    /// The hot temperature is what the <c>a</c> port actually receives, not the hottest source in the
    /// graph: on a series header the second block's valve is fed by the first block's return (<c>S-63</c>),
    /// and reading the boiler's 60 &#176;C there made a 35/30 coil's stream a sixth of its circulation
    /// where it is half. <see cref="FeedTemperature"/> walks the feed branch for the exchanger that
    /// last touched the water; the hottest source is the fallback when the walk finds nothing.
    /// </remarks>
    private static double? MixingFraction(CircuitGraph graph, string loadName, Branch feed, ThreeWayValve valve)
    {
        var load = graph.Components
            .OfType<HeatExchanger>()
            .SingleOrDefault(component => string.Equals(component.Name, loadName, StringComparison.Ordinal));

        if (load is null
            || !load.StatedParameters.TryGetValue("in", out var mixed)
            || !load.StatedParameters.TryGetValue("out", out var cold))
        {
            return null;
        }

        var hot = FeedTemperature(graph, feed, valve);

        if (hot is null)
        {
            foreach (var source in graph.Components.OfType<HeatExchanger>())
            {
                if (source.Power > 0
                    && source.StatedParameters.TryGetValue("out", out var outlet)
                    && (hot is null || outlet.SiValue > hot.Value.SiValue))
                {
                    hot = outlet;
                }
            }
        }

        if (hot is null)
        {
            return null;
        }

        var reference = Quantity.FromSi(0, Dimension.Pressure);
        if (!graph.Substance.FromPressureTemperature(reference, cold).TryGetValue(out var coldState)
            || !graph.Substance.FromPressureTemperature(reference, mixed).TryGetValue(out var mixedState)
            || !graph.Substance.FromPressureTemperature(reference, hot.Value).TryGetValue(out var hotState))
        {
            return null;
        }

        var span = hotState.Enthalpy.SiValue - coldState.Enthalpy.SiValue;
        var fraction = (mixedState.Enthalpy.SiValue - coldState.Enthalpy.SiValue) / span;

        return span > 0 && fraction > 0 && fraction < 1 ? fraction : null;
    }

    /// <summary>The temperature of the water arriving at a valve's <c>a</c> port, read from the exchanger that last touched it.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="feed">The branch on the <c>a</c> port.</param>
    /// <param name="valve">The valve the branch ends at.</param>
    /// <returns>A stated outlet temperature, or <see langword="null"/> when nothing on the way states one.</returns>
    /// <remarks>
    /// Walks the feed branch away from the valve: a source in the path (the boiler on the parallel
    /// header) gives its <c>out</c>. Reaching the far end without one, the far junction is another
    /// block's mixing node, and the exchanger on any branch meeting it that states an <c>out</c> is
    /// what discharges there -- the first block's load on the series header. A stated temperature on
    /// the junction node itself wins over both.
    /// </remarks>
    private static Quantity? FeedTemperature(CircuitGraph graph, Branch feed, ThreeWayValve valve)
    {
        var forward = ReferenceEquals(feed.To.Element, valve);
        IEnumerable<IFlowComponent> path = feed.Path;

        if (forward)
        {
            path = path.Reverse();
        }

        foreach (var element in path)
        {
            if (element is HeatExchanger { Power: > 0 } source
                && source.StatedParameters.TryGetValue("out", out var outlet))
            {
                return outlet;
            }
        }

        var far = forward ? feed.From.Element : feed.To.Element;

        if (far is CircuitNode node && node.StatedParameters.TryGetValue("t", out var stated))
        {
            return stated;
        }

        foreach (var branch in graph.Branches)
        {
            if (branch.Index == feed.Index || !Meets(branch, far))
            {
                continue;
            }

            foreach (var element in branch.Path)
            {
                if (element is HeatExchanger exchanger
                    && exchanger.StatedParameters.TryGetValue("out", out var outlet))
                {
                    return outlet;
                }
            }
        }

        return null;
    }

    /// <summary>Whether a branch terminates at any connected three-way valve.</summary>
    /// <param name="branch">The branch.</param>
    /// <returns><see langword="true"/> when either end is a three-way valve.</returns>
    private static bool MeetsThreeWay(Branch branch) =>
        branch.From.Element is ThreeWayValve || branch.To.Element is ThreeWayValve;

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
    /// An automatically closed duty lives in <c>SizedParameters</c>, not <c>StatedParameters</c>. For a
    /// positive source with only its outlet stated, a common outlet temperature on every opposing load is
    /// the return design temperature. Using it here seeds a flow; it does not add a constraint to the solve.
    /// If the returns disagree, the estimate declines rather than inventing a mixed temperature.
    /// </para>
    /// <para>
    /// The arithmetic is <see cref="RatedFlow"/>'s; this decides which inlet to hand it. A side stated as
    /// a difference (<c>dt</c>, <c>dt2</c>) rather than two terminals is <c>Q / (cp · dt)</c> with <c>cp</c> at
    /// whichever terminal it did state, and at 50 °C when it stated none -- a seed, not a rating.
    /// </para>
    /// </remarks>
    private static double? Duty(CircuitGraph graph, Branch branch, IFlowComponent component)
    {
        if (component is not HeatExchanger exchanger
            || (!component.StatedParameters.ContainsKey("power")
                && !component.SizedParameters.ContainsKey("power")
                && !component.DefaultParameters.ContainsKey("power")))
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
            : CommonReturn(graph, exchanger);

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
        ISubstance substance, HeatExchanger exchanger, string inlet, string outlet, string change)
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
    public static int Side(CircuitGraph graph, Branch branch, HeatExchanger exchanger)
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

    /// <summary>Finds the one return temperature all opposing loads state.</summary>
    private static Quantity? CommonReturn(CircuitGraph graph, HeatExchanger source)
    {
        if (source.Power <= 0)
        {
            return null;
        }

        var returns = graph.Components
            .OfType<HeatExchanger>()
            .Where(candidate => candidate.Power < 0)
            .Select(candidate => candidate.StatedParameters.TryGetValue("out", out var outlet)
                ? (Quantity?)outlet
                : null)
            .ToArray();

        if (returns.Length == 0
            || returns.Any(static candidate => candidate is null)
            || returns.Any(candidate => !candidate!.Value.IsCloseTo(returns[0]!.Value)))
        {
            return null;
        }

        return returns[0];
    }
}
