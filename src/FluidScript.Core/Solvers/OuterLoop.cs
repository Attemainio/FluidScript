using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>Gets the Newton iterations over every pass, retries included.</summary>
    /// <value>
    /// The work the run did, which is where a warm start's saving shows: the warm seed goes to the
    /// <em>first</em> pass, and <see cref="Solve"/>'s own count is the last pass's (<c>A-4</c>).
    /// </value>
    public required int Iterations { get; init; }

    /// <summary>Gets the Newton iterations of each pass in order, retries included.</summary>
    /// <value>
    /// One entry per solve the loop ran, so that a change to the seed can be read per pass rather than
    /// as one sum (<c>S-66</c>): a seed that helps the first pass and hurts the third shows here and
    /// nowhere else. Sums to <see cref="Iterations"/>.
    /// </value>
    public required ImmutableArray<int> PassIterations { get; init; }

    /// <summary>Gets whether the sizes stopped moving.</summary>
    /// <value>
    /// <see langword="false"/> means the cap was reached with sizes still changing — <c>FS2301</c>'s
    /// case, reported rather than hidden, with the last values kept because they are what the last
    /// solve actually used.
    /// </value>
    public required bool Settled { get; init; }

    /// <summary>The hash of the unknown layout the solution is indexed by; what a <see cref="WarmStart"/> must match.</summary>
    /// <value>See <see cref="OuterLoop.TopologyHash"/>.</value>
    public required string TopologyHash { get; init; }

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
    ImmutableArray<string> Notes)
{
    /// <summary>Gets the model the passes lower: the bound model with every deferred expression the seed could evaluate written in (<c>L-59</c>).</summary>
    /// <value><see langword="null"/> when the model deferred nothing, in which case the bound model is the one.</value>
    public SemanticModel? Model { get; init; }

    /// <summary>Gets what evaluating against the seed had to say: a dimension a deferred value could not have.</summary>
    public ImmutableArray<Diagnostics.Diagnostic> Said { get; init; } = [];

    /// <summary>Gets what the seed evaluated, so the first pass compares against it rather than counting every value as moved.</summary>
    /// <remarks>
    /// Without this a value stated from the seed was "moved" on pass 1 by having no history, which
    /// forced a pass 2 whose overlay carried the valve the head had promoted as a size -- and a stated
    /// head beside a sized valve is over-specified. The seed's value is pass 0's, and it counts.
    /// </remarks>
    public ImmutableArray<DeferredEvaluation.Evaluated> Seeded { get; init; } = [];
}

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
        Catalogs.ICatalog<Catalogs.PipeSpec> pipes,
        Catalogs.ICatalog<Catalogs.ValveSpec>? valves = null,
        IReadOnlyDictionary<string, Catalogs.ICatalog<Catalogs.PipeSpec>>? available = null) =>
    [
        new PipeSizer(pipes, available: available),
        new ValveSizer(valves ?? Catalogs.ValveKvR5.Instance),
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
    public PreparedModel Prepare(SemanticModel model, ISubstance substance, string name = "model")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(substance);

        var overlay = Bootstrap(model);
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
                overlay = Bootstrap(model);
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
        var (first, _, _, _) = Apply(bootstrap.Graph, Seed(bootstrap.Graph), overlay);
        var resistive = Lowering.Lower(model, substance, new ComponentFactory(bores, first, substance), name);
        var (sized, bases, notes, _) = Apply(resistive.Graph, Seed(resistive.Graph), first);

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
                return Refused("a solution", name, Unsolvable(posedness), posedness.Diagnostics);
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

                return Refused("a solution", name, $"{assembled}, though {predicted}");
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
                    loopSaid.Add(Diagnostics.Diagnostic.Create(Diagnostics.SolverDiagnostics.RestartedFromSeed, span: null));
                    continue;
                }

                break;
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
                        Annotated(solve, raised, loopSaid, evaluationSaid, current, histories, unsettled: [], closing: []),
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

    /// <summary>A run the loop refuses before or between passes: the one error every refusal is spelled as, with its clause.</summary>
    /// <param name="property">What could not be produced: <c>a component</c> or <c>a solution</c>.</param>
    /// <param name="name">Whose.</param>
    /// <param name="state">Why, in one clause.</param>
    /// <param name="diagnostics">What the check that refused had to say, travelling with the refusal (<c>S-65</c>).</param>
    /// <returns>The failure.</returns>
    private static Result<OuterLoopResult> Refused(
        string property, string name, string state, ImmutableArray<Diagnostics.Diagnostic>? diagnostics = null)
    {
        var error = ResultError.From(
            Diagnostics.FluidDiagnostics.PropertyNotEvaluable,
            ("property", property),
            ("name", name),
            ("state", state));

        return Result.Failure<OuterLoopResult>(diagnostics is { } said ? error with { Diagnostics = said } : error);
    }

    /// <summary>The last solve with everything the loop has to say attached, in the one order both exits use.</summary>
    /// <param name="solve">The last solve.</param>
    /// <param name="raised">What the sizers raised on the last pass.</param>
    /// <param name="loopSaid">What the loop itself said: a warm start discarded.</param>
    /// <param name="evaluationSaid">What the deferred evaluation said on the last pass.</param>
    /// <param name="current">The model the last pass lowered, for what was never evaluated.</param>
    /// <param name="histories">Every deferred target's values so far.</param>
    /// <param name="unsettled">The deferred values still moving at the cap, or empty.</param>
    /// <param name="closing">What closes the list: the not-settled warning, or nothing.</param>
    /// <returns>The solve, annotated.</returns>
    private static SolveResult Annotated(
        SolveResult solve,
        ImmutableArray<Diagnostics.Diagnostic> raised,
        ImmutableArray<Diagnostics.Diagnostic>.Builder loopSaid,
        ImmutableArray<Diagnostics.Diagnostic> evaluationSaid,
        SemanticModel current,
        Dictionary<ValueId, List<DeferredEvaluation.Evaluated>> histories,
        ImmutableArray<Diagnostics.Diagnostic> unsettled,
        ImmutableArray<Diagnostics.Diagnostic> closing) =>
        solve with
        {
            Diagnostics = solve.Diagnostics
                .AddRange(raised)
                .AddRange(loopSaid)
                .AddRange(evaluationSaid)
                .AddRange(unsettled)
                .AddRange(DeferredEvaluation.NeverEvaluated(current, histories))
                .AddRange(closing),
        };

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
        int iterations,
        ImmutableArray<int> passIterations,
        bool settled,
        string topologyHash) =>
        new()
        {
            Graph = graph,
            Solve = solve.Converged
                ? solve with { Diagnostics = solve.Diagnostics.AddRange(Reversals(graph, solve.Solution)).AddRange(UnderVacuum(graph, solve.Solution)) }
                : solve,
            Sizes = sizes,
            Bases = bases,
            Notes = notes,
            Passes = passes,
            Iterations = iterations,
            PassIterations = passIterations,
            Settled = settled,
            TopologyHash = topologyHash,
        };

    /// <summary><c>FS2221</c> on the lowest node of each hydraulic part the converged field puts below atmospheric (<c>S-29</c>, <c>D-121</c>).</summary>
    /// <remarks>
    /// After the solve rather than before it, because where a loop's pressures fall relative to its
    /// datum is what the solve finds out: a second pump on a ring puts its own suction its head below
    /// the datum at the first pump's suction, and no height check can see that.
    /// </remarks>
    private static ImmutableArray<Diagnostics.Diagnostic> UnderVacuum(CircuitGraph graph, StateVector solution)
    {
        var posedness = WellPosedness.Check(graph);

        return FillPressure.ReportSolved(graph, posedness.Hydraulics, SystemLayout.Build(graph, posedness.Counting), solution);
    }

    /// <summary><c>FS2301</c>: the pass cap was reached with sizes still moving, naming what moved between the last two passes.</summary>
    /// <param name="previous">The overlay the last pass started from, or <see langword="null"/> when only one pass ran.</param>
    /// <param name="last">The overlay the last pass produced.</param>
    private static Diagnostics.Diagnostic NotSettled(SizingOverlay? previous, SizingOverlay last)
    {
        var moving = new List<string>();

        foreach (var (component, chosen) in last.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var (parameter, value) in chosen.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var before = previous?.For(component, parameter);

                if (before is null || Math.Abs(before.Value - value.SiValue) > 1e-6 * Math.Max(1, Math.Abs(value.SiValue)))
                {
                    moving.Add(Ownership.Key(component, parameter));
                }
            }
        }

        return Diagnostics.Diagnostic.Create(
            Diagnostics.SizingDiagnostics.NotSettled,
            span: null,
            new Diagnostics.DiagnosticArgument("list", moving.Count == 0 ? "the sized values" : string.Join(", ", moving)));
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

    /// <summary>Adds the bases the script states itself to the ones a sizing rule chose.</summary>
    /// <param name="model">The bound model.</param>
    /// <param name="bases">What sizing chose this pass, keyed <c>"P1.dn"</c>.</param>
    /// <returns>The same map with every parameter read at its component's own sizing point added (<c>D-94</c>).</returns>
    /// <remarks>
    /// A stated parameter is not sized, but one read at a <c>sized_at</c> point was arrived at rather
    /// than typed, and the report is where an engineer checks the bivalent fraction it implies. Sizing
    /// never writes a stated parameter, so the two sets cannot collide.
    /// </remarks>
    private static ImmutableDictionary<string, string> WithStated(
        SemanticModel model,
        ImmutableDictionary<string, string> bases)
    {
        var merged = bases.ToBuilder();

        foreach (var component in model.Components)
        {
            foreach (var (parameter, value) in component.Parameters)
            {
                if (value.Basis is { } basis)
                {
                    merged[Ownership.Key(component.Name, parameter)] = basis;
                }
            }
        }

        return merged.ToImmutable();
    }

    private static readonly ImmutableArray<string> SideOneTerminals = ["in", "out"];

    /// <summary>Names every pump and exchanger a converged solution runs backwards.</summary>
    /// <param name="graph">The solved graph.</param>
    /// <param name="solution">The converged iterate.</param>
    /// <returns>One <c>FS3013</c> per reversed component, in graph order.</returns>
    /// <remarks>
    /// Read off the port map rather than the branch orientation: a branch's sign says which way the
    /// walk crossed it, and a component walked from its outlet end is entered at <c>out</c> even when the
    /// branch flow is positive. What the map's sign gives is the flow <em>into</em> the component at a
    /// port, and a negative inflow at <c>in</c> is the reversal whatever the walk did (<c>S-10</c>).
    /// Only a converged iterate is read; the directions of one that is not are not answers.
    /// </remarks>
    private static ImmutableArray<Diagnostics.Diagnostic> Reversals(CircuitGraph graph, StateVector solution)
    {
        var layout = SystemLayout.Build(graph, WellPosedness.Check(graph).Counting);
        var ports = PortMap.Build(graph);
        var reversed = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var component = graph.Components[element];

            if (component is not (Pump or HeatExchanger))
            {
                continue;
            }

            var inlet = IndexOfPort(component, "in");
            var outlet = IndexOfPort(component, "out");

            if (inlet < 0 || outlet < 0 || !ports[element, inlet].CarriesFlow)
            {
                continue;
            }

            var binding = ports[element, inlet];
            var entering = binding.Sign * solution.Values[layout.BranchFlow(binding.Branch)];

            if (entering >= -Tolerances.FlowZero)
            {
                continue;
            }

            var terminals = SideOneTerminals
                .Where(parameter => component.StatedParameters.ContainsKey(parameter))
                .Select(parameter => Ownership.Key(component.Name, parameter))
                .ToArray();
            var note = terminals.Length == 0
                ? string.Empty
                : $"; its stated {string.Join(" and ", terminals)} {(terminals.Length == 1 ? "is" : "are")} on the port the water leaves by";

            reversed.Add(Diagnostics.Diagnostic.Create(
                Diagnostics.SolverDiagnostics.ReversedFlow,
                span: null,
                new Diagnostics.DiagnosticArgument("component", component.Name),
                new Diagnostics.DiagnosticArgument("flow", Math.Abs(entering).ToString("0.###", CultureInfo.InvariantCulture)),
                new Diagnostics.DiagnosticArgument("outlet", component.Ports[outlet].Name),
                new Diagnostics.DiagnosticArgument("inlet", component.Ports[inlet].Name),
                new Diagnostics.DiagnosticArgument("note", note)));
        }

        return reversed.ToImmutable();
    }

    private static int IndexOfPort(IFlowComponent component, string name)
    {
        for (var port = 0; port < component.Ports.Length; port++)
        {
            if (string.Equals(component.Ports[port].Name, name, StringComparison.Ordinal))
            {
                return port;
            }
        }

        return -1;
    }

    private static StateVector Seed(CircuitGraph graph) =>
        SolutionSeed.Build(graph, SystemLayout.Build(graph, WellPosedness.Check(graph).Counting));

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
                        && info.OmissionBehavior == Language.ParameterOmissionBehavior.Size)
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
        SystemLayout? layout = null)
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
        Unsized(graph, overlay, bases, notes);

        return (overlay, bases.ToImmutable(), notes.ToImmutable(), raised.ToImmutable());
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
            // element, so it sits in no branch's `Path`. The legs the drawn flow crosses answer instead --
            // by their block (S-55): the pump-free mixing header's main valve has no pump on either leg
            // and is driven by the consumer pumps that draw from its common port through the same block.
            var blocks = HydraulicBlocks.ForFreePumps(graph);
            var driven = blocks.Drives(legs[common]) || blocks.Drives(legs[variable]);

            var flow = flows[variable];
            var context = new SizingContext
            {
                State = state,
                MassFlow = flow,
                BranchDrop = Resistance(graph, state, legs[variable], flow, valve),
                LoopDrop = Circuit(graph, layout, iterate, valve, state),
                AvailableDrop = driven ? null : Offered(graph),
                CommonFlow = flows[common],
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
                bases[Ownership.Key(valve.Name, parameter)] = value.Basis;
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
    private static bool Driven(CircuitGraph graph, IFlowComponent component)
    {
        // The component's block, not its fundamental cycle (S-55): a free pump anywhere in the same
        // biconnected block reaches this branch. The boundaries do not join the blocks here, so a bounded
        // primary beside a pumped secondary keeps reading as bounded (D-89).
        var blocks = HydraulicBlocks.ForFreePumps(graph);

        return graph.Branches.Any(branch => branch.Path.Contains(component) && blocks.Drives(branch));
    }

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
        // A coupled exchanger sits on a branch of each side, and every rule that reads a flow through it
        // means side 1's -- the side the unsuffixed parameters describe.
        var branch = graph.Branches.FirstOrDefault(candidate =>
            candidate.Path.Contains(component)
            && (component is not HeatExchanger exchanger || BranchFlows.Side(graph, candidate, exchanger) == 1));

        if (branch is null || Inlet(graph, layout, iterate, component) is not { } state)
        {
            return null;
        }

        var flow = iterate.Values[layout.BranchFlow(branch.Index)];

        return new SizingContext
        {
            State = state,
            MassFlow = flow,
            BranchDrop = Resistance(graph, state, branch, flow, component),
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
                    graph, state, branch, iterate.Values[layout.BranchFlow(branch.Index)], component);
            }

            worst = Math.Max(worst ?? drop, drop);
        }

        if (worst is null && onALoop
            && graph.Branches.FirstOrDefault(candidate => candidate.Path.Contains(component)) is { } own)
        {
            return Resistance(graph, state, own, iterate.Values[layout.BranchFlow(own.Index)], component);
        }

        return worst;
    }

    /// <summary>What a run of components resists at a flow, by their own laws.</summary>
    /// <param name="graph">The graph, for its substance.</param>
    /// <param name="state">The fluid to evaluate the laws against.</param>
    /// <param name="branch">The branch, ends included.</param>
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
        Branch branch,
        double flow,
        IFlowComponent exclude) =>
        BranchResistance.Along(graph, state, branch, flow, exclude);
}
