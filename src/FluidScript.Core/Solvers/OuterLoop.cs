using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Binding;
using FluidScript.Core.Components;
using FluidScript.Core.Fluids;
using FluidScript.Core.Sizing;
using FluidScript.Core.Topology;
using FluidScript.Core.Units;

namespace FluidScript.Core.Solvers;

/// <summary>What the outer loop settled on.</summary>
public sealed record OuterLoopResult
{
    /// <summary>Gets the graph as it stood on the last pass, sizes and all.</summary>
    public required CircuitGraph Graph { get; init; }

    /// <summary>Gets the last solve.</summary>
    public required SolveResult Solve { get; init; }

    /// <summary>Gets what sizing chose, by component and parameter.</summary>
    public required SizingOverlay Sizes { get; init; }

    /// <summary>Gets the basis of every sized value, as <c>"PU1.head"</c> to a sentence.</summary>
    /// <remarks>
    /// <c>24</c>'s invariant 2: an unexplained sized value is a defect. This is where the explanation
    /// lives until the model contract carries it.
    /// </remarks>
    public required ImmutableDictionary<string, string> Bases { get; init; }

    /// <summary>Gets what happened on the way, in order.</summary>
    public required ImmutableArray<string> Notes { get; init; }

    /// <summary>Gets how many passes ran.</summary>
    public required int Passes { get; init; }

    /// <summary>Gets whether the sizes stopped moving.</summary>
    /// <value>
    /// <see langword="false"/> means the cap was reached with sizes still changing — <c>FS2301</c>'s
    /// case, reported rather than hidden, with the last values kept because they are what the last
    /// solve actually used.
    /// </value>
    public required bool Settled { get; init; }

    /// <summary>The full solve report: counting, constraints, unknowns, equations, sizes and rank.</summary>
    /// <returns>The report, as lines of text.</returns>
    /// <remarks>
    /// On the record so that anywhere holding a result can print one without assembling the call ---
    /// including a debugger watch window, which is where an unexpected termination is usually first met.
    /// A run that never produced a result explains itself through
    /// <see cref="Diagnostics.SolveExplanation.Render(CircuitGraph, string)"/> instead.
    /// </remarks>
    public override string ToString() => Diagnostics.SolveExplanation.Render(this);
}
/// <summary>A model lowered with sizing applied, before anything is solved.</summary>
/// <param name="Lowered">The graph, and whatever could still not be built.</param>
/// <param name="Sizes">What sizing chose from the seed's flow estimates.</param>
/// <param name="Bases">Why it chose each one, keyed <c>"P1.dn"</c>.</param>
/// <param name="Notes">What happened on the way.</param>
public sealed record PreparedModel(
    LoweringResult Lowered,
    SizingOverlay Sizes,
    ImmutableDictionary<string, string> Bases,
    ImmutableArray<string> Notes);

/// <summary>The single fixed-point loop that reconciles sizing with the solve (<c>31</c>).</summary>
/// <param name="solver">The solver each pass runs.</param>
/// <param name="bores">Where a DN designation becomes a bore.</param>
/// <param name="sizers">The rules, in the order they are offered a component.</param>
/// <param name="maxPasses">The iteration cap. <c>24</c>'s <c>MaxSizingIterations</c>, default 10.</param>
/// <remarks>
/// <para>
/// <strong>One loop, not three.</strong> Sizing needs flows, flows need sizes, and deferred expressions
/// need solved values that the parameters those expressions set determine. Nesting them would not
/// merely be slower: a sized value feeding a deferred expression feeding another sized value would
/// converge in an inner loop against a stale outer one, and the two could oscillate with neither
/// detecting it.
/// </para>
/// <para>
/// <strong>Sizing is applied by lowering again, not by mutating a component.</strong> A pipe's bore is
/// not promotable and not settable — it is a constructor argument — so the pass that uses a new
/// diameter is a pass that built a new <see cref="Pipe"/>. That keeps a solve a pure function of its
/// graph (<c>31</c>'s invariant 6) and is what <c>08</c> means by lowering having to be re-runnable
/// against changing geometry.
/// </para>
/// <para>
/// <strong>The bootstrap lowering is never solved against.</strong> A pipe cannot be built without a
/// bore, so the first graph is built from provisional sizes purely so that one exists to estimate flows
/// on — and flow estimates come from stated duties and stated flows, not from resistances, so nothing
/// they produce depends on the provisional values. The first pass then already carries real sizes,
/// which matters: solving a 0.24 l/s circuit through the smallest pipe in the catalogue is a needlessly
/// hostile first iterate.
/// </para>
/// <para>
/// <strong>What the loop intends to size is decided once and never grows.</strong> A parameter claimed
/// by no map is promotable, so one that became sized between passes would stop being promotable between
/// passes and the system's shape would change under a warm start. The set is taken from the model on
/// the bootstrap pass and only the values move afterwards.
/// </para>
/// </remarks>
public sealed class OuterLoop(
    ISolver solver, IBoreLookup bores, ImmutableArray<ISizer> sizers, int maxPasses = 10)
{
    /// <summary>The rules a v1 solve runs, in the order a component is offered to them.</summary>
    /// <param name="pipes">The pipe series diameters are chosen from.</param>
    /// <param name="valves">The Kv series valves are chosen from. R5 unless a caller says otherwise.</param>
    /// <returns>The rules.</returns>
    /// <remarks>
    /// <para>
    /// One list rather than one per caller. A fixture that assembled its own would be lowering a model
    /// sized by a different set of rules than a solve uses, which is the shape of <c>C-55</c> and cost
    /// three failures and four skips the first time it happened.
    /// </para>
    /// <para>
    /// <strong>Order is not significant and must not become so.</strong> Each rule answers for one kind,
    /// so no component is offered to two of them, and the coupling between rules runs through the
    /// <em>iterate</em> rather than through this list: a valve's Kv changes the drop the pump is sized
    /// against on the <em>next</em> pass, not on this one. A rule that needed to run after another would
    /// be a rule that had outgrown <see cref="ISizer"/>.
    /// </para>
    /// </remarks>
    public static ImmutableArray<ISizer> Rules(
        Catalogs.ICatalog<Catalogs.PipeSpec> pipes, Catalogs.ICatalog<Catalogs.ValveSpec>? valves = null) =>
    [
        new PipeSizer(pipes),
        new ValveSizer(valves ?? Catalogs.ValveKvR5.Instance),
        new ExchangerSizer(),
        new PumpSizer(),
    ];

    /// <summary>Lowers a model with sizing applied, which is the only graph a solve ever sees.</summary>
    /// <param name="model">The bound semantic model.</param>
    /// <param name="substance">The fluid.</param>
    /// <param name="name">The graph's name, for reporting.</param>
    /// <returns>The lowered graph, the sizes it was built with, their bases, and any notes.</returns>
    /// <remarks>
    /// <para>
    /// <c>24</c>'s steps 1 to 3 — seed, propagate, size — all of which run before anything is solved.
    /// It is public because a fixture that lowers a script directly would otherwise be lowering a
    /// different model than the solver does: a pipe nothing sized has no bore, so it is not built at
    /// all, and the graph quietly loses a component rather than failing.
    /// </para>
    /// <para>
    /// <strong>The bootstrap lowering inside is thrown away.</strong> Its only job is to exist so flows
    /// can be estimated on it, and flow estimates come from stated duties and stated flows rather than
    /// from resistances — so nothing that survives depends on the provisional sizes it was built with.
    /// </para>
    /// <para>
    /// <strong>The omitted duty is closed before the first count sees the graph</strong> (<c>S-59</c>).
    /// The closure needs only the stated duties, so it can run on the bootstrap; run after the count
    /// instead, the count saw the source's <c>power</c> as free and let its flow constraint promote
    /// <em>that</em>, the sizer then took the source's pump head because nothing had claimed it, and on
    /// the next pass — power now sized, head now sized — the constraint had nothing local left and
    /// reached for a consumer's pump, shifting every promotion after it by one.
    /// </para>
    /// </remarks>
    public PreparedModel Prepare(SemanticModel model, ISubstance substance, string name = "model")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);

        var overlay = Bootstrap(model);
        var bootstrap = Lowering.Lower(model, substance, new ComponentFactory(bores, overlay), name);

        if (!bootstrap.Unresolved.IsEmpty)
        {
            return new PreparedModel(bootstrap, overlay, [], []);
        }

        var closure = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var closed = overlay;

        CloseEnergyBalance(bootstrap.Graph, ref closed, closure);

        if (!ReferenceEquals(closed, overlay))
        {
            overlay = closed;
            bootstrap = Lowering.Lower(model, substance, new ComponentFactory(bores, overlay), name);
        }

        var (sized, bases, notes) = Apply(bootstrap.Graph, Seed(bootstrap.Graph), overlay);

        return new PreparedModel(
            Lowering.Lower(model, substance, new ComponentFactory(bores, sized), name), sized, bases, notes);
    }

    /// <summary>Compiles a bound model to a solved circuit, sizing whatever it left open.</summary>
    /// <param name="model">The bound semantic model.</param>
    /// <param name="substance">The fluid.</param>
    /// <param name="name">The graph's name, for reporting.</param>
    /// <param name="cancellationToken">Honoured between passes and inside the solver.</param>
    /// <returns>
    /// The last pass's graph, solve and sizes, or why no pass could run. A failed <em>solve</em> is a
    /// result rather than a failure — it carries the iterate it reached and where it was heading.
    /// </returns>
    public async Task<Result<OuterLoopResult>> RunAsync(
        SemanticModel model,
        ISubstance substance,
        string name = "model",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);

        var prepared = Prepare(model, substance, name);

        if (!prepared.Lowered.Unresolved.IsEmpty)
        {
            return Result.Failure<OuterLoopResult>(ResultError.From(
                Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a component"),
                ("name", string.Join(", ", prepared.Lowered.Unresolved)),
                ("state", "nothing chose the geometry it needs")));
        }

        var overlay = prepared.Sizes;
        var bases = prepared.Bases;
        var notes = prepared.Notes;
        var lowered = prepared.Lowered;

        StateVector? warm = null;
        SolveResult? solve = null;
        var passes = 0;

        while (passes < maxPasses)
        {
            passes++;
            cancellationToken.ThrowIfCancellationRequested();

            lowered = Lowering.Lower(model, substance, new ComponentFactory(bores, overlay), name);
            var posedness = WellPosedness.Check(lowered.Graph);

            // A malformed script is the normal case while one is being edited, and a script whose
            // equations outnumber its unknowns is malformed. Without this the system is built anyway and
            // `DenseLu.Factor` throws `ArgumentException` on a Jacobian that is not square -- a pipeline
            // stage throwing on user input, which the contract forbids outright (`S-28`).
            if (!posedness.CanSolve)
            {
                return Result.Failure<OuterLoopResult>(ResultError.From(
                    Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                    ("property", "a solution"),
                    ("name", name),
                    ("state", Unsolvable(posedness))));
            }

            var layout = SystemLayout.Build(lowered.Graph, posedness.Counting);
            var iterate = warm ?? Seed(lowered.Graph);
            var system = EquationSystem.Build(lowered.Graph, posedness, iterate);

            // `CanSolve` speaks for the counting table, which is a prediction. `Rows` and `Columns` are
            // what assembly actually produced, and the two disagree wherever a component declares fewer
            // equations than the table credits it with -- `S-14b`'s coupled exchanger is the live case.
            // The disagreement is a defect in this engine rather than in the script, but it still must
            // not reach `DenseLu`, which throws on a matrix that is not square (`S-28`).
            if (system.Rows != system.Columns)
            {
                var assembled = string.Create(
                    CultureInfo.InvariantCulture,
                    $"assembly gave {system.Rows} equations for {system.Columns} unknowns");
                var predicted = string.Create(
                    CultureInfo.InvariantCulture,
                    $"counting predicted {posedness.Counting.Equations} for {posedness.Counting.Unknowns}");

                return Result.Failure<OuterLoopResult>(ResultError.From(
                    Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                    ("property", "a solution"),
                    ("name", name),
                    ("state", $"{assembled}, though {predicted}")));
            }


            solve = await solver.SolveAsync(system, iterate, progress: null, cancellationToken)
                .ConfigureAwait(false);

            if (!solve.Converged)
            {
                break;
            }

            var (next, chosen, said) = Apply(lowered.Graph, solve.Solution, overlay, posedness, layout);

            bases = chosen;
            notes = said;

            if (next.Matches(overlay))
            {
                return Result.Success(Report(lowered.Graph, solve, next, bases, notes, passes, settled: true));
            }

            overlay = next;
            warm = solve.Solution;
        }

        return solve is null
            ? Result.Failure<OuterLoopResult>(ResultError.From(
                Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
                ("property", "a solution"),
                ("name", name),
                ("state", "the pass cap is not positive")))
            : Result.Success(Report(lowered.Graph, solve, overlay, bases, notes, passes, settled: false));
    }

    /// <summary>Why a graph cannot be handed to the solver, in one clause.</summary>
    /// <param name="posedness">The failing check.</param>
    /// <returns>The first error it reported, or the counting mismatch when it reported none.</returns>
    /// <remarks>
    /// The error is preferred because it names a place in the script; the count is the fallback for the
    /// case <see cref="WellPosednessResult.CanSolve"/> also admits, where every diagnostic is a warning
    /// and the system is simply not square.
    /// </remarks>
    private static string Unsolvable(WellPosednessResult posedness)
    {
        var error = posedness.Diagnostics.FirstOrDefault(
            static diagnostic => diagnostic.Severity == Diagnostics.DiagnosticSeverity.Error);

        return error is not null
            ? error.Message
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{posedness.Counting.Equations} equations for {posedness.Counting.Unknowns} unknowns");
    }

    private static OuterLoopResult Report(
        CircuitGraph graph,
        SolveResult solve,
        SizingOverlay sizes,
        ImmutableDictionary<string, string> bases,
        ImmutableArray<string> notes,
        int passes,
        bool settled) =>
        new()
        {
            Graph = graph,
            Solve = solve,
            Sizes = sizes,
            Bases = bases,
            Notes = notes,
            Passes = passes,
            Settled = settled,
        };

    private static StateVector Seed(CircuitGraph graph) =>
        SolutionSeed.Build(graph, SystemLayout.Build(graph, WellPosedness.Check(graph).Counting));

    /// <summary>The provisional sizes that let a first graph exist.</summary>
    /// <param name="model">The bound model.</param>
    /// <returns>An overlay covering every parameter this loop intends to size.</returns>
    /// <remarks>
    /// <strong>These are not a design and are never solved against.</strong> A pipe has no component at
    /// all without a bore, so something has to go in before flows can be estimated; a sizer's
    /// <see cref="ISizer.Provisional"/> is that something, and the first real pass replaces it.
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
                        && info.OmissionBehavior == Language.ParameterOmissionBehavior.Size)
                    {
                        overlay = overlay.With(symbol.Name, parameter, value);
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
    /// <returns>The new overlay, the bases, and the notes.</returns>
    /// <remarks>
    /// <strong>A promoted parameter is skipped, and that is the whole of the division of labour.</strong>
    /// A parameter omitted with a <c>Size</c> policy is normally chosen here, but where a stated
    /// constraint needs somewhere to go, well-posedness promotes one such parameter to an unknown and
    /// the solver determines it. Sizing it as well would give two answers to one question, with nothing
    /// saying they disagreed.
    /// </remarks>
    private (SizingOverlay Overlay, ImmutableDictionary<string, string> Bases, ImmutableArray<string> Notes) Apply(
        CircuitGraph graph,
        StateVector iterate,
        SizingOverlay previous,
        WellPosednessResult? posedness = null,
        SystemLayout? layout = null)
    {
        var posed = posedness ?? WellPosedness.Check(graph);
        var places = layout ?? SystemLayout.Build(graph, posed.Counting);
        var promoted = posed.Counting.Promotions.Select(static promotion => promotion.Label).ToHashSet(StringComparer.Ordinal);

        var overlay = previous;
        var bases = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var notes = ImmutableArray.CreateBuilder<string>();

        CloseEnergyBalance(graph, ref overlay, bases);

        foreach (var component in graph.Components)
        {
            foreach (var sizer in sizers)
            {
                if (!sizer.CanSize(component)
                    || sizer.Parameters.All(parameter => Claimed(component, parameter, promoted)))
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
                    bases[$"{component.Name}.{parameter}"] = value.Basis;
                }

                notes.AddRange(sized.Value.Notes);
            }
        }

        ThreeWay(graph, places, iterate, ref overlay, bases, notes, promoted);
        Unsized(graph, overlay, bases, notes);

        return (overlay, bases.ToImmutable(), notes.ToImmutable());
    }

    /// <summary>Sizes every three-way valve that stands as a junction element (<c>24</c>, <c>C-63</c>).</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current estimate the sizing is read off.</param>
    /// <param name="overlay">The overlay being built, added to in place.</param>
    /// <param name="bases">Why each value was chosen, keyed <c>component.parameter</c>.</param>
    /// <param name="notes">Anything the user should be told.</param>
    /// <param name="promoted">Labels the counting pass has already claimed as solver unknowns.</param>
    /// <remarks>
    /// <para>
    /// <strong>This is a pass rather than an <c>ISizer</c> because of what the context needs to be, not
    /// because the arithmetic differs.</strong> A three-way valve with its bypass connected is a junction
    /// element: it appears in no branch's <c>Path</c>, so <see cref="Context"/> finds it nothing and the
    /// ordinary loop skips it. What it needs is a context built from <em>one of its three legs</em>, and
    /// choosing which one is a comparison across sibling branches that a rule handed a single branch
    /// cannot make (<c>C-49</c>, <c>C-61</c>). Once the context exists, <see cref="ValveSizer"/> is the
    /// rule -- the same Kv law, authority definition and catalogue a two-way valve gets.
    /// </para>
    /// <para>
    /// <strong>Neither leg is identified by its port letter, and using them would be wrong on half of
    /// all scripts.</strong> <c>22</c> names the ports <c>a</c> common, <c>b</c> controlled, <c>c</c>
    /// bypass, but binding is positional -- ports take connections in the order the script writes them.
    /// Measured on <c>m2-cooling-loop</c>, <c>b</c> carries the <em>recirculation</em> and <c>c</c> the
    /// primary draw, because <c>3WV - N2</c> was written before <c>3WV - P1</c>. So both legs are found
    /// from the circuit instead: the <strong>common</strong> leg is the one carrying what the other two
    /// split, which mass balance settles, and the <strong>variable</strong> leg is the one reaching a
    /// stated pressure rather than closing back into the valve's own loop, which is the same criterion
    /// the authority definition uses.
    /// </para>
    /// <para>
    /// <strong>Whether the drop is chosen or determined is decided by looking for a free pump on the
    /// path the variable flow actually takes</strong> -- the variable leg and the common leg, which are
    /// the two the drawn flow crosses. A pump whose head is promoted or unstated makes the driving
    /// pressure free, so the authority target chooses the drop; with no such pump the boundary pressures
    /// fix it and the valve takes what the rest of the path leaves. Looking graph-wide instead would
    /// misread a pumped secondary beside a genuinely bounded primary, which is the arrangement this rule
    /// exists for.
    /// </para>
    /// </remarks>
    private void ThreeWay(
        CircuitGraph graph,
        SystemLayout layout,
        StateVector iterate,
        ref SizingOverlay overlay,
        ImmutableDictionary<string, string>.Builder bases,
        ImmutableArray<string>.Builder notes,
        HashSet<string> promoted)
    {
        if (sizers.OfType<ValveSizer>().FirstOrDefault() is not { } rule)
        {
            return;
        }

        foreach (var component in graph.Components)
        {
            if (component is not ThreeWayValve { BypassConnected: true } valve
                || Inlet(graph, layout, iterate, valve) is not { } state)
            {
                continue;
            }

            var legs = graph.Branches
                .Where(branch =>
                    ReferenceEquals(branch.From.Element, valve) || ReferenceEquals(branch.To.Element, valve))
                .ToArray();

            if (legs.Length != 3)
            {
                continue;
            }

            var flows = Array.ConvertAll(
                legs, leg => Math.Abs(iterate.Values[layout.BranchFlow(leg.Index)]));

            var common = ValveLegs.Common(legs, flows, valve);
            var variable = ValveLegs.Variable(graph, legs, common, valve);

            if (variable < 0)
            {
                Declined(
                    valve,
                    overlay,
                    bases,
                    notes,
                    "its connections name no ports, and its two switched legs are the same distance from "
                    + "the leg they split -- so nothing says which of them recirculates and which varies "
                    + "when the valve strokes. Name them: `a` is the leg it controls, `b` the bypass");

                continue;
            }

            // Not `Driven(graph, valve)`: a three-way valve with its bypass connected is a junction
            // element, so it sits in no branch's `Path` and that helper would find no circuit through it
            // and call every one of them bounded. The legs the drawn flow crosses are its equivalent.
            var driven = legs[common].Path.Concat(legs[variable].Path).Any(static element =>
                element is Pump pump && !pump.StatedParameters.ContainsKey("head"));

            var flow = flows[variable];
            var context = new SizingContext
            {
                State = state,
                MassFlow = flow,
                BranchDrop = Resistance(graph, state, legs[variable].Path, flow, valve),
                LoopDrop = Circuit(graph, layout, iterate, valve, state),
                AvailableDrop = driven ? null : Offered(graph),
            };

            if (!driven && context.AvailableDrop is null)
            {
                Declined(
                    valve,
                    overlay,
                    bases,
                    notes,
                    "no pump on its path carries a free head, so the boundary pressures determine its "
                    + "drop — and the circuit does not state exactly two of them, so which pair drives "
                    + "this valve is not decided");

                continue;
            }

            var sized = rule.Size(valve, context);

            if (!sized.IsSuccess)
            {
                Declined(valve, overlay, bases, notes, sized.Error?.Message ?? "the rule declined it");

                continue;
            }

            foreach (var (parameter, value) in sized.Value.Values)
            {
                if (Claimed(valve, parameter, promoted))
                {
                    continue;
                }

                overlay = overlay.With(valve.Name, parameter, value.Value);
                bases[$"{valve.Name}.{parameter}"] = value.Basis;
            }

            notes.AddRange(sized.Value.Notes);
        }
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
            var key = $"{valve.Name}.{parameter}";

            if (!bases.ContainsKey(key))
            {
                bases[key] = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.SiValue:0.###} — provisional, not chosen: {reason}");
            }
        }

        notes.Add(
            $"{valve.Name} is still the bootstrap value no rule replaced, because {reason}. It was chosen "
            + "to disturb the first pass as little as possible, not to suit this circuit — state a `kv`, "
            + "or read the result knowing this one number is arbitrary.");
    }

    /// <summary>The driving pressure a circuit with no free pump offers a valve.</summary>
    /// <param name="graph">The lowered circuit.</param>
    /// <returns>Pa, positive, or <see langword="null"/> when the graph does not settle it.</returns>
    /// <remarks>
    /// The span between the two stated boundary pressures. With any other count the pair the variable
    /// flow runs between is a path question rather than a set one, and answering it by taking the
    /// extremes would quietly size against a differential no fluid crosses — so the rule declines and
    /// says so rather than guessing.
    /// </remarks>
    private static double? Offered(CircuitGraph graph)
    {
        var stated = graph.Nodes
            .Select(node => HydraulicPartition.Stated(node.Component, HydraulicPartition.Pressure))
            .Where(static pressure => pressure is not null)
            .Select(static pressure => pressure!.Value)
            .ToArray();

        return stated.Length == 2 ? Math.Abs(stated[0] - stated[1]) : null;
    }

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
                var key = $"{component.Name}.{parameter}";

                if (bases.ContainsKey(key)
                    || sizers.Any(sizer =>
                        sizer.CanSize(component) && sizer.Parameters.Contains(parameter)))
                {
                    continue;
                }

                bases[key] = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.SiValue:0.###} — provisional, not chosen: no rule sizes a "
                    + $"{component.Kind}'s `{parameter}`");

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
                .Where(static exchanger =>
                    !exchanger.StatedParameters.ContainsKey("power")
                    && !exchanger.DefaultParameters.ContainsKey("power"))
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
        component.StatedParameters.ContainsKey(parameter)
        || component.DefaultParameters.ContainsKey(parameter)
        || promoted.Contains($"{component.Name}.{parameter}");

    /// <summary>Whether a free pump on a circuit through a component absorbs whatever it drops.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="component">The component being sized.</param>
    /// <returns>
    /// <see langword="true"/> when some loop through it carries a pump whose head is unstated or promoted,
    /// which makes the driving pressure a free variable rather than something the boundaries fix.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>This is what decides which way a valve's catalogue selection rounds</strong> (<c>C-62</c>,
    /// <c>D-89</c>). With a free pump the valve's drop is a <em>choice</em> and the authority target makes
    /// it, rounding down because more authority is the safe direction and the pump absorbs the extra. With
    /// no free pump the boundary pressures fix the driving pressure, the valve takes what the rest of the
    /// path leaves, and rounding down would put the design flow out of reach at every position.
    /// </para>
    /// <para>
    /// <strong>The scope is the component's own circuits, never the graph</strong> — the same reason
    /// <see cref="ThreeWay"/> asks about the legs the drawn flow crosses rather than about every pump
    /// present. Looking graph-wide would read a pumped secondary beside a genuinely bounded primary as
    /// driven, which is the arrangement the distinction exists for. A component on no loop at all answers
    /// <see langword="false"/>, which is right: an open path between two boundaries is the bounded case.
    /// </para>
    /// </remarks>
    private static bool Driven(CircuitGraph graph, IFlowComponent component) =>
        graph.Loops
            .Where(loop => loop.Branches.Any(branch => branch.Path.Contains(component)))
            .SelectMany(static loop => loop.Branches)
            .SelectMany(static branch => branch.Path)
            .Any(static element => element is Pump pump && !pump.StatedParameters.ContainsKey("head"));

    /// <summary>The flow through a component and the fluid state there.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="component">The component being sized.</param>
    /// <returns>The context, or <see langword="null"/> when no branch carries this component.</returns>
    /// <remarks>
    /// <strong><see cref="SizingContext.AvailableDrop"/> is filled here as well as in
    /// <see cref="ThreeWay"/></strong> (<c>C-62</c>). It used to be set only in the three-way pass, so a
    /// two-way valve was never told what the boundaries offer, <c>ValveSizer</c>'s <c>bounded</c> branch
    /// could not run for one, and it rounded down on a pressure-bounded circuit where that puts the design
    /// flow out of reach. The arithmetic was already there; only the context was missing.
    /// </remarks>
    private static SizingContext? Context(
        CircuitGraph graph, SystemLayout layout, StateVector iterate, IFlowComponent component)
    {
        var branch = graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(component));

        if (branch is null || Inlet(graph, layout, iterate, component) is not { } state)
        {
            return null;
        }

        var flow = iterate.Values[layout.BranchFlow(branch.Index)];

        return new SizingContext
        {
            State = state,
            MassFlow = flow,
            BranchDrop = Resistance(graph, state, branch.Path, flow, component),
            LoopDrop = Circuit(graph, layout, iterate, component, state),
            AvailableDrop = Driven(graph, component) ? null : Offered(graph),
        };
    }

    /// <summary>The fluid state at a component's own inlet.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="component">The component.</param>
    /// <returns>The state, or <see langword="null"/> when its inlet reaches no node.</returns>
    /// <remarks>
    /// <strong>Its own inlet, not the loop mean, and <c>24</c> flags the difference as a trap.</strong>
    /// A pump develops head against the fluid actually entering it, so the worked example converts
    /// 51.7 kPa at 998.2 kg/m³ and reads 5.28 m; the same drop at the loop's 35 °C mean of 994 kg/m³
    /// reads 5.30 m. Under half a percent there, and it grows with the loop's temperature spread — so
    /// an implementation that silently takes the mean disagrees with the document by more than rounding
    /// while looking right.
    /// </remarks>
    private static FluidState? Inlet(
        CircuitGraph graph, SystemLayout layout, StateVector iterate, IFlowComponent component)
    {
        var element = graph.Components.IndexOf(component);
        var peer = element < 0 ? PortRef.None : graph.Adjacency.Peer(element, 0);
        var index = peer.Exists
            ? Array.FindIndex(
                [.. graph.Nodes],
                node => ReferenceEquals(node.Component, graph.Components[peer.Component]))
            : -1;

        if (index < 0)
        {
            return null;
        }

        var state = graph.Substance.FromPressureEnthalpy(
            Quantity.FromSi(iterate.Values[layout.NodePressure(index)], Dimension.Pressure),
            Quantity.FromSi(iterate.Values[layout.NodeEnthalpy(index)], Dimension.Enthalpy));

        return state.IsSuccess ? state.Value : null;
    }

    /// <summary>The drop around the largest circuit through a component, excluding the component.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="component">The component the circuit runs through.</param>
    /// <param name="state">The fluid at it.</param>
    /// <returns>
    /// Pa, or <see langword="null"/> when no cycle contains the component at all — a different fact from
    /// a cycle that resists nothing, and only the caller can say which one matters (<c>C-57</c>).
    /// </returns>
    /// <remarks>
    /// <para>
    /// The largest, because a pump on a set of parallel circuits has to reach the worst of them — which
    /// is the index circuit, and sizing to any other one leaves a branch short of its design flow.
    /// </para>
    /// <para>
    /// <strong>A loop that carries another pump is not this pump's to drive</strong> (<c>D-93</c>). Two
    /// pumps in series on one loop have one head between them and nothing in the loop equation says how
    /// it divides (<c>S-37</c>); sizing each to the whole loop hands the loop twice its drop. Measured on
    /// two pumped sources feeding three pumped consumers: the source pump took 46.8 kPa from a loop
    /// through a consumer's own pump, the header differential that produced over-drove the direct
    /// consumer, and its promoted pump was asked for a negative head. The convention is
    /// primary–secondary practice — the primary pump is sized for the primary circuit, each secondary
    /// pump for its own — so a pump whose every loop is shared is sized to its own branch, and a loop
    /// with a second pump on it is left out of the index search. Only a pump is treated this way: a
    /// valve's circuit is still every loop through it, since a valve shares head with nothing.
    /// </para>
    /// </remarks>
    private static double? Circuit(
        CircuitGraph graph,
        SystemLayout layout,
        StateVector iterate,
        IFlowComponent component,
        FluidState state)
    {
        double? worst = null;
        var onALoop = false;

        foreach (var loop in graph.Loops)
        {
            if (!loop.Branches.Any(branch => branch.Path.Contains(component)))
            {
                continue;
            }

            onALoop = true;

            if (component is Pump
                && loop.Branches.Any(branch => branch.Path.Any(
                    element => element is Pump && !ReferenceEquals(element, component))))
            {
                continue;
            }

            var drop = 0.0;

            foreach (var branch in loop.Branches)
            {
                drop += Resistance(
                    graph, state, branch.Path, iterate.Values[layout.BranchFlow(branch.Index)], component);
            }

            worst = Math.Max(worst ?? drop, drop);
        }

        if (worst is null && onALoop
            && graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(component)) is { } own)
        {
            return Resistance(graph, state, own.Path, iterate.Values[layout.BranchFlow(own.Index)], component);
        }

        return worst;
    }

    /// <summary>What a run of components resists at a flow, by their own laws.</summary>
    /// <param name="graph">The graph, for its substance.</param>
    /// <param name="state">The fluid to evaluate the laws against.</param>
    /// <param name="path">The components along the run.</param>
    /// <param name="flow">kg/s through them.</param>
    /// <param name="exclude">The component whose own contribution is left out.</param>
    /// <returns>Pa, positive against the flow.</returns>
    /// <remarks>
    /// The rule itself moved to <see cref="BranchResistance"/> when the seed needed it as well: sizing
    /// wants a run's total and the seed wants each element in turn, and one rule in two places is how
    /// <c>D-86</c> came to be applied three times and missed a fourth.
    /// </remarks>
    private static double Resistance(
        CircuitGraph graph,
        FluidState state,
        ImmutableArray<IFlowComponent> path,
        double flow,
        IFlowComponent exclude) =>
        BranchResistance.Along(graph, state, path, flow, exclude);
}
