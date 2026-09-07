using System.Collections.Immutable;

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
            var layout = SystemLayout.Build(lowered.Graph, posedness.Counting);
            var iterate = warm ?? Seed(lowered.Graph);
            var system = EquationSystem.Build(lowered.Graph, posedness, iterate);

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

        return (overlay, bases.ToImmutable(), notes.ToImmutable());
    }

    private static bool Claimed(IFlowComponent component, string parameter, HashSet<string> promoted) =>
        component.StatedParameters.ContainsKey(parameter)
        || component.DefaultParameters.ContainsKey(parameter)
        || promoted.Contains($"{component.Name}.{parameter}");

    /// <summary>The flow through a component and the fluid state there.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where the iterate keeps each unknown.</param>
    /// <param name="iterate">The current values.</param>
    /// <param name="component">The component being sized.</param>
    /// <returns>The context, or <see langword="null"/> when no branch carries this component.</returns>
    private static SizingContext? Context(
        CircuitGraph graph, SystemLayout layout, StateVector iterate, IFlowComponent component)
    {
        foreach (var branch in graph.Branches)
        {
            if (!branch.Path.Contains(component))
            {
                continue;
            }

            var node = branch.From.Element as CircuitNode
                ?? branch.To.Element as CircuitNode
                ?? graph.Nodes.FirstOrDefault()?.Component as CircuitNode;

            var index = node is null ? -1 : Array.FindIndex(
                [.. graph.Nodes], candidate => ReferenceEquals(candidate.Component, node));

            if (index < 0)
            {
                continue;
            }

            var state = graph.Substance.FromPressureEnthalpy(
                Quantity.FromSi(iterate.Values[layout.NodePressure(index)], Dimension.Pressure),
                Quantity.FromSi(iterate.Values[layout.NodeEnthalpy(index)], Dimension.Enthalpy));

            if (!state.IsSuccess)
            {
                return null;
            }

            return new SizingContext
            {
                State = state.Value,
                MassFlow = iterate.Values[layout.BranchFlow(branch.Index)],
            };
        }

        return null;
    }
}
