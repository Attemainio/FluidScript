using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using FluidScript.Core.Components;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Primitives;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Sizers;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Solvers.Seeding;
using FluidScript.Core.Topology.Construction;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Passes;

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
/// diameter is a pass that built a new <see cref="PipeComponent"/>. That keeps a solve a pure function of its
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
public sealed partial class OuterLoop(
    ISolver solver, IBoreLookup bores, ImmutableArray<ISizer> sizers, int maxPasses = 10)
{
    /// <summary>The rules a v1 solve runs, in the order a component is offered to them.</summary>
    /// <param name="pipes">The pipe series diameters are chosen from.</param>
    /// <param name="valves">The Kv series valves are chosen from. R5 unless a caller says otherwise.</param>
    /// <param name="available">Every shipped pipe catalogue by id, for a pipe whose own <c>material</c> names one (<c>C-36</c>).</param>
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
        Catalogs.ICatalog<FluidScript.Core.Catalogs.Pipes.PipeSpec> pipes,
        Catalogs.ICatalog<FluidScript.Core.Catalogs.Valves.ValveSpec>? valves = null,
        IReadOnlyDictionary<string, Catalogs.ICatalog<FluidScript.Core.Catalogs.Pipes.PipeSpec>>? available = null) =>
    [
        new PipeSizer(pipes, available: available),
        new ValveSizer(valves ?? FluidScript.Core.Catalogs.Valves.ValveKvR5.Instance),
        new ExchangerSizer(),
        new ThermalSizer(),
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
    /// <strong>The bootstrap lowering inside is thrown away, and the sizing is applied to it twice</strong>
    /// (<c>S-68</c>). Flow estimates come from stated duties and stated flows rather than from
    /// resistances, so the first application does not depend on the provisional sizes the bootstrap was
    /// built with. But an exchanger with no stated flow and no duty is built <em>ideal</em> until a rule
    /// gives it a design point, and the rules that read a loop's resistance -- a pump's head, a valve's
    /// Kv and authority -- read it off the very graph the exchanger is ideal in. One application sized a
    /// pump whose head no constraint had claimed to zero, "no modelled resistance", on a loop whose coil
    /// drops 20 kPa; the first solve then ran on a graph that had no answer near its seed, and the
    /// second pass that would have corrected the head never came. Applying the rules again on a graph
    /// lowered from the first application's sizes lets each rule see what the others chose. Measured on
    /// the corpus: the simple loop settles in two passes instead of three, and nothing else moved.
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
    /// <param name="from">
    /// Sizes to start from instead of the bootstrap's provisional ones — the merged envelope, when a
    /// scenario is being re-sized against the plant the other cases built (<c>D-143</c>). Not a floor:
    /// the rules still choose freely, and what this changes is the resistances they read.
    /// </param>
    public PreparedModel Prepare(
        SemanticModel model, ISubstance substance, string name = "model", SizingOverlay? from = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);

        // `from` is where the sizing starts, not a floor: the rules still choose freely, and what it
        // buys is that they choose against the *merged* plant's resistances rather than this case's
        // own (`D-143`, step 3). A pipe one case enlarged changes the head another case's pump needs,
        // and that coupling is the reason the merge is iterated rather than taken once.
        var overlay = from ?? Bootstrap(model);
        var bootstrap = Lowering.Lower(model, substance, new ComponentFactory(bores, overlay, substance), name);

        if (!bootstrap.Unresolved.IsEmpty)
        {
            return new PreparedModel(bootstrap, overlay, [], []);
        }

        // 14's Phase B, pass 0 (L-59): every deferred expression the bootstrap's seed can supply is
        // written in as a stated value *before* sizing decides what it owns. A deferred `head` that
        // turned from sized to stated between passes left the flow constraint nothing to promote, because
        // the valve's Kv was already sizing's; stated from the start, the promotion lands on the valve
        // and the system keeps one shape. The seed supplies only what the script anchored -- a stated
        // parameter, a `let` -- because its guess at an unstated node is a placeholder, and a placeholder
        // written in as a stated value is what the first solve is then held to. A solved state, and a
        // rated exchanger's second side, wait for the first solve.
        var seedSaid = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();
        var seedNotes = ImmutableArray.CreateBuilder<string>();
        var seededValues = ImmutableArray<DeferredEvaluation.Evaluated>.Empty;

        if (!model.Deferred.IsDefaultOrEmpty)
        {
            var layout = SystemLayout.Build(bootstrap.Graph, WellPosedness.Check(bootstrap.Graph).Counting);
            var seeded = DeferredEvaluation.Evaluate(
                model, bootstrap.Graph, layout, SolutionSeed.Build(bootstrap.Graph, layout), seedSaid, seedNotes, seeding: true);

            if (!seeded.IsEmpty)
            {
                model = DeferredEvaluation.Apply(model, seeded, pass: 0);
                overlay = from ?? Bootstrap(model);
                bootstrap = Lowering.Lower(model, substance, new ComponentFactory(bores, overlay, substance), name);
            }

            seededValues = seeded;
        }

        var closure = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var closed = overlay;

        CloseEnergyBalance(bootstrap.Graph, ref closed, closure);

        if (!ReferenceEquals(closed, overlay))
        {
            overlay = closed;
            bootstrap = Lowering.Lower(model, substance, new ComponentFactory(bores, overlay, substance), name);
        }

        // Twice, because the bootstrap's exchangers are ideal: the first application gives each one its
        // design point, and only the second lets the rules that read resistances -- a pump's head, a
        // valve's Kv -- read the drops those design points create. See the remarks.
        var (first, _, _, _) = Apply(bootstrap.Graph, Seed(bootstrap.Graph), overlay, solved: false);
        var resistive = Lowering.Lower(model, substance, new ComponentFactory(bores, first, substance), name);
        var (sized, bases, notes, _) = Apply(resistive.Graph, Seed(resistive.Graph), first, solved: false);

        return new PreparedModel(
            Lowering.Lower(model, substance, new ComponentFactory(bores, sized, substance), name),
            sized,
            WithStated(model, bases),
            notes.AddRange(seedNotes))
        {
            Model = model.Deferred.IsDefaultOrEmpty ? null : model,
            Said = seedSaid.ToImmutable(),
            Seeded = seededValues,
        };
    }

    /// <summary>Prepares a model to be solved against sizes already chosen, running no sizer (<c>D-143</c>).</summary>
    /// <param name="model">The bound model, projected onto one scenario.</param>
    /// <param name="substance">The fluid.</param>
    /// <param name="sizes">The sizes to hold, normally the merged envelope of every scenario.</param>
    /// <param name="name">The model's name, for diagnostics.</param>
    /// <returns>A prepared model whose <see cref="PreparedModel.Frozen"/> is set.</returns>
    /// <remarks>
    /// <para>
    /// Step 3 of <c>24</c>'s scenario pipeline. After merging, the sizes exceed what any single case's
    /// own solve used, so <em>none</em> of those solves describes the merged plant: a case solved with a
    /// DN20 pipe is not a state of a plant that ended up with DN32. This is what produces the operating
    /// states, and what catches a component still short somewhere.
    /// </para>
    /// <para>
    /// The lowering is the one the sizes ask for and nothing else runs: no bootstrap sizing, no
    /// two-pass exchanger warm-up, no rule reading a resistance. A deferred expression is still
    /// evaluated, because a value the script wrote as an expression is the script's and not a size.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PreparedModel Freeze(SemanticModel model, ISubstance substance, SizingOverlay sizes, string name = "model")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);
        ArgumentNullException.ThrowIfNull(sizes);

        return new PreparedModel(
            Lowering.Lower(model, substance, new ComponentFactory(bores, sizes, substance), name),
            sizes,
            ImmutableDictionary<string, string>.Empty,
            [])
        {
            Model = model.Deferred.IsDefaultOrEmpty ? null : model,
            Frozen = true,
        };
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
    public Task<Result<OuterLoopResult>> RunAsync(
        SemanticModel model,
        ISubstance substance,
        string name = "model",
        CancellationToken cancellationToken = default) =>
        RunAsync(model, substance, warmStart: null, name, cancellationToken);

    /// <summary>Compiles a bound model to a solved circuit, seeding the first pass from an earlier solution when it still fits.</summary>
    /// <param name="model">The bound semantic model.</param>
    /// <param name="substance">The fluid.</param>
    /// <param name="warmStart">
    /// An earlier run's solution and topology hash, or <see langword="null"/> for a cold start. Used
    /// only when its hash is the hash of the system this run builds; otherwise ignored without a word,
    /// since a host offers it on every request and most requests change the topology.
    /// </param>
    /// <param name="name">The graph's name, for reporting.</param>
    /// <param name="cancellationToken">Honoured between passes and inside the solver.</param>
    /// <returns>As <see cref="RunAsync(SemanticModel, ISubstance, string, CancellationToken)"/>.</returns>
    public Task<Result<OuterLoopResult>> RunAsync(
        SemanticModel model,
        ISubstance substance,
        WarmStart? warmStart,
        string name = "model",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);

        return RunAsync(Prepare(model, substance, name), model, substance, warmStart, name, cancellationToken);
    }

    /// <summary>Runs the loop from a model already prepared, so a host that read the unknown count first does not prepare twice (<c>A-1</c>).</summary>
    /// <param name="prepared">What <see cref="Prepare"/> returned for <paramref name="model"/>.</param>
    /// <param name="model">The bound semantic model.</param>
    /// <param name="substance">The fluid.</param>
    /// <param name="warmStart">As <see cref="RunAsync(SemanticModel, ISubstance, WarmStart?, string, CancellationToken)"/>.</param>
    /// <param name="name">The graph's name, for reporting.</param>
    /// <param name="cancellationToken">Honoured between passes and inside the solver.</param>
    /// <returns>As <see cref="RunAsync(SemanticModel, ISubstance, string, CancellationToken)"/>.</returns>
    public async Task<Result<OuterLoopResult>> RunAsync(
        PreparedModel prepared,
        SemanticModel model,
        ISubstance substance,
        WarmStart? warmStart,
        string name = "model",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);

        if (!prepared.Lowered.Unresolved.IsEmpty)
        {
            return Refused("a component", string.Join(", ", prepared.Lowered.Unresolved), "nothing chose the geometry it needs");
        }

        var overlay = prepared.Sizes;
        var bases = prepared.Bases;
        var notes = prepared.Notes;
        var lowered = prepared.Lowered;

        StateVector? warm = null;
        SolveResult? solve = null;
        var passes = 0;
        var iterations = 0;
        var perPass = ImmutableArray.CreateBuilder<int>();
        var hash = string.Empty;

        // What the loop itself has to say, beyond what the solver and the sizers said: a warm start
        // discarded (`FS3012`). Attached to the last solve whichever way the loop ends.
        var loopSaid = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        // What the sizers raised on the last pass, and the overlay before it, kept outside the loop so
        // the unsettled exit can attach them and say what was still moving (FS2301).
        ImmutableArray<Diagnostics.Diagnostic> raised = [];
        SizingOverlay? previous = null;

        // The model as this pass lowers it: the bound model, plus every deferred expression the last
        // pass could evaluate, written in as a stated value (L-59, 14's Phase B). What each target has
        // been so far is kept for FS1405, which shows the last three values when one never settles.
        var current = prepared.Model ?? model;
        var histories = new Dictionary<ValueId, List<DeferredEvaluation.Evaluated>>();

        foreach (var seeded in prepared.Seeded)
        {
            histories[seeded.Target] = [seeded];
        }
        ImmutableArray<Diagnostics.Diagnostic> evaluationSaid = prepared.Said;
        var deferredMoved = false;

        while (passes < maxPasses)
        {
            passes++;
            cancellationToken.ThrowIfCancellationRequested();

            lowered = Lowering.Lower(current, substance, new ComponentFactory(bores, overlay, substance), name);
            var posedness = WellPosedness.Check(lowered.Graph);

            // A malformed script is the normal case while one is being edited, and a script whose
            // equations outnumber its unknowns is malformed. Without this the system is built anyway and
            // `DenseLu.Factor` throws `ArgumentException` on a Jacobian that is not square -- a pipeline
            // stage throwing on user input, which the contract forbids outright (`S-28`). The check's own
            // diagnostics travel with the refusal, each with its code, component and range (`S-65`); the
            // error's text is the summary for a caller that reports one line.
            if (!posedness.CanSolve)
            {
                return Refused(
                    "a solution",
                    name,
                    Unsolvable(posedness),
                    posedness.Diagnostics.AddRange(DeferredEvaluation.NeverEvaluated(current, histories, passes)));
            }

            var layout = SystemLayout.Build(lowered.Graph, posedness.Counting);
            hash = TopologyHash(layout);
            var fromWarm = warm is null && warmStart is { } offered
                && string.Equals(offered.TopologyHash, hash, StringComparison.Ordinal)
                && offered.Solution.Count == layout.Count;
            // A deferred value written in as stated can change the system's shape between passes -- a
            // head that was promotable is now a constraint -- and the last pass's iterate no longer
            // addresses it; the pass then starts from the seed like a first one.
            var iterate = warm is { } carried && carried.Count == layout.Count
                ? carried
                : fromWarm ? warmStart!.Solution : Seed(lowered.Graph);
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

                return Refused("a solution", name, $"{assembled}, though {predicted}", DeferredEvaluation.NeverEvaluated(current, histories, passes));
            }

            solve = await solver.SolveAsync(system, iterate, progress: null, cancellationToken)
                .ConfigureAwait(false);
            iterations += solve.Iterations;
            perPass.Add(solve.Iterations);

            if (!solve.Converged)
            {
                // A warm start is a cache. One that does not converge is thrown away and the same pass
                // runs again from the cold seed, so a stale iterate can cost time but never an answer.
                if (fromWarm)
                {
                    warmStart = null;
                    loopSaid.Add(Diagnostics.Diagnostic.Create(FluidScript.Core.Diagnostics.Descriptors.SolverDiagnostics.RestartedFromSeed, span: null));
                    continue;
                }

                break;
            }

            // Sizes that were given are not chosen again (`D-143`, step 3): one converged solve against
            // this plant is the whole answer, and running the rules here would size each case back to
            // what it alone needs, undoing the merge this pass exists to check.
            if (prepared.Frozen)
            {
                // Authority is the one reported figure that is an outcome of the sizes rather than one
                // of them, so it is the one thing a frozen solve still has to compute (C-121).
                var (read, readBases, valves) = Readings(lowered.Graph, layout, solve.Solution, overlay, bases, posedness);

                return Result.Success(
                    Report(
                        lowered.Graph,
                        Annotated(solve, raised, loopSaid, evaluationSaid, current, histories, failedPass: null, unsettled: [], closing: []),
                        read,
                        WithStated(current, readBases),
                        notes,
                        passes,
                        iterations,
                        perPass.ToImmutable(),
                        settled: true,
                        hash) with
                    { Valves = valves });
            }

            var (next, chosen, said, raisedNow) = Apply(lowered.Graph, solve.Solution, overlay, posedness, layout);
            raised = raisedNow;

            bases = chosen;
            notes = said;

            // 14's step 2: every deferred expression against this pass; step 3: go round again when
            // one moved. Evaluated after sizing so a reference to a sized value reads this pass's
            // choice, and written into the model the next pass lowers.
            var evaluationDiagnostics = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();
            var evaluationNotes = ImmutableArray.CreateBuilder<string>();
            var evaluated = DeferredEvaluation.Evaluate(current, lowered.Graph, layout, solve.Solution, evaluationDiagnostics, evaluationNotes);
            evaluationSaid = evaluationDiagnostics.ToImmutable();
            notes = notes.AddRange(evaluationNotes);
            deferredMoved = false;

            foreach (var value in evaluated)
            {
                if (!histories.TryGetValue(value.Target, out var history))
                {
                    histories[value.Target] = history = [];
                }

                if (history.Count == 0 || DeferredEvaluation.Moved(history[^1].Value.SiValue, value.Value.SiValue))
                {
                    deferredMoved = true;
                }

                history.Add(value);
            }

            if (deferredMoved)
            {
                current = DeferredEvaluation.Apply(current, evaluated, passes);
            }

            if (next.Matches(overlay) && !deferredMoved)
            {
                return Result.Success(
                    Report(
                        lowered.Graph,
                        Annotated(solve, raised, loopSaid, evaluationSaid, current, histories, failedPass: null, unsettled: [], closing: []),
                        next,
                        WithStated(current, bases),
                        notes,
                        passes,
                        iterations,
                        perPass.ToImmutable(),
                        settled: true,
                        hash));
            }

            previous = overlay;
            overlay = next;
            warm = solve.Solution;
        }

        return solve is null
            ? Refused("a solution", name, "the pass cap is not positive")
            : Result.Success(
                Report(
                    lowered.Graph,
                    Annotated(
                        solve,
                        raised,
                        loopSaid,
                        evaluationSaid,
                        current,
                        histories,
                        failedPass: solve.Converged ? null : passes,
                        unsettled: deferredMoved ? DeferredEvaluation.Unsettled(histories) : [],
                        closing: [NotSettled(previous, overlay)]),
                    overlay,
                    WithStated(current, bases),
                    notes,
                    passes,
                    iterations,
                    perPass.ToImmutable(),
                    settled: false,
                    hash));
    }

    /// <summary>Names the unknown layout a solution is indexed by, so a later run can tell whether it may reuse it.</summary>
    /// <param name="layout">The layout of the system about to be solved.</param>
    /// <returns>Sixteen hex digits of the SHA-256 over every unknown's kind, owner and name, in order.</returns>
    /// <remarks>
    /// The hash is of the <em>layout</em>, not the text or the bound model: a whitespace edit, a
    /// changed value or a renamed parameter leaves it alone, and only a change that adds, removes or
    /// reorders an unknown moves it -- which is exactly when a stored iterate stops meaning what it
    /// meant (<c>41</c>). Sixteen digits is far past what a per-session cache can collide on.
    /// </remarks>
    public static string TopologyHash(SystemLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var text = new StringBuilder();

        foreach (var unknown in layout.Unknowns)
        {
            text.Append(unknown.Kind).Append(':').Append(unknown.OwnerComponentId).Append(':').Append(unknown.Name).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static readonly ImmutableArray<string> SideOneTerminals = ["in", "out"];

    private static StateVector Seed(CircuitGraph graph) =>
        SolutionSeed.Build(graph, SystemLayout.Build(graph, WellPosedness.Check(graph).Counting));
}
