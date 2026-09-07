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
        [new PipeSizer(pipes), new ValveSizer(valves ?? Catalogs.ValveKvR5.Instance), new PumpSizer()];

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
    /// The largest, because a pump on a set of parallel circuits has to reach the worst of them — which
    /// is the index circuit, and sizing to any other one leaves a branch short of its design flow.
    /// </remarks>
    private static double? Circuit(
        CircuitGraph graph,
        SystemLayout layout,
        StateVector iterate,
        IFlowComponent component,
        FluidState state)
    {
        double? worst = null;

        foreach (var loop in graph.Loops)
        {
            if (!loop.Branches.Any(branch => branch.Path.Contains(component)))
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
    /// <strong>Each component states its own drop, and none of them is asked how.</strong> A pressure
    /// residual is written <c>p_in − p_out − Δp(law) = 0</c>, so evaluating it over a <em>flat</em>
    /// pressure field leaves exactly <c>−Δp(law)</c> — the component's own contribution at that flow,
    /// from the same code the solver runs. No kind appears here, a pump's rise comes out negative
    /// because that is what a pump does to a loop, and a component added later is covered the day it
    /// declares a pressure equation.
    /// </remarks>
    private static double Resistance(
        CircuitGraph graph,
        FluidState state,
        ImmutableArray<IFlowComponent> path,
        double flow,
        IFlowComponent exclude)
    {
        var total = 0.0;

        foreach (var element in path)
        {
            if (ReferenceEquals(element, exclude) || element.EquationCount == 0)
            {
                continue;
            }

            var row = element.DeclareEquations()
                .FirstOrDefault(declaration => declaration.Kind == EquationKind.Pressure);

            if (row is null)
            {
                continue;
            }

            var ports = new PortState[element.Ports.Length];
            var flows = new double[element.Ports.Length];
            var residuals = new double[element.EquationCount];

            Array.Fill(ports, Flat(state));
            Array.Fill(flows, flow);

            element.EvaluateResiduals(new SolveContext(graph.Substance, ports, flows), residuals);

            if (double.IsFinite(residuals[row.Index]))
            {
                total -= residuals[row.Index];
            }
        }

        return total;
    }

    /// <summary>A port state carrying real properties at zero gauge pressure.</summary>
    /// <param name="state">The fluid.</param>
    /// <returns>The port state.</returns>
    /// <remarks>
    /// Only the pressure is flattened. The properties stay real, because a pipe cannot form a Reynolds
    /// number without a density and a viscosity, and a law evaluated against invented ones would be a
    /// different law.
    /// </remarks>
    private static PortState Flat(FluidState state) => new()
    {
        Pressure = 0,
        Enthalpy = state.Enthalpy.SiValue,
        Temperature = state.Temperature.SiValue,
        Density = state.Density.SiValue,
        SpecificHeat = state.SpecificHeat.SiValue,
        DynamicViscosity = state.DynamicViscosity.SiValue,
        ThermalConductivity = state.ThermalConductivity.SiValue,
    };
}
