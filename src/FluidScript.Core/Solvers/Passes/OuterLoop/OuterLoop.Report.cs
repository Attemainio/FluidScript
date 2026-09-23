using System.Collections.Immutable;
using System.Globalization;

using FluidScript.Core.Components;
using FluidScript.Core.Components.Exchangers;
using FluidScript.Core.Components.Valves;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Primitives;
using FluidScript.Core.Sizing;
using FluidScript.Core.Sizing.Flows;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Counting;
using FluidScript.Core.Topology.Graph;
using FluidScript.Core.Topology.Hydraulics;

namespace FluidScript.Core.Solvers.Passes;

public sealed partial class OuterLoop
{
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
            FluidScript.Core.Diagnostics.Descriptors.FluidDiagnostics.PropertyNotEvaluable,
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
    /// <param name="failedPass">The pass that did not converge, or <see langword="null"/> when the last one did (<c>L-62</c>).</param>
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
        int? failedPass,
        ImmutableArray<Diagnostics.Diagnostic> unsettled,
        ImmutableArray<Diagnostics.Diagnostic> closing) =>
        solve with
        {
            Diagnostics = solve.Diagnostics
                .AddRange(raised)
                .AddRange(loopSaid)
                .AddRange(evaluationSaid)
                .AddRange(unsettled)
                .AddRange(DeferredEvaluation.NeverEvaluated(current, histories, failedPass))
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
                ? solve with { Diagnostics = solve.Diagnostics.AddRange(AfterSolve(graph, solve.Solution)) }
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

    /// <summary>What a converged field can be read for that no earlier stage could see: reversed flow, a part under vacuum, a three-way valve balancing instead of mixing.</summary>
    /// <remarks>The posedness check and the layout are built once here and shared, where each report used to rebuild them.</remarks>
    private static ImmutableArray<Diagnostics.Diagnostic> AfterSolve(CircuitGraph graph, StateVector solution)
    {
        var posedness = WellPosedness.Check(graph);
        var layout = SystemLayout.Build(graph, posedness.Counting);

        return
        [
            .. Reversals(graph, layout, solution),
            .. FillPressure.ReportSolved(graph, posedness.Hydraulics, layout, solution),
            .. BypassBalance.ReportSolved(graph, layout, solution),
            .. Arrangements(graph, layout, solution),
        ];
    }

    /// <summary><c>FS4012</c> on every three-way valve whose spelling claims one service and whose solved flows run the other (<c>C-65</c>, <c>D-136</c>).</summary>
    /// <param name="graph">The solved graph.</param>
    /// <param name="layout">The state vector's layout.</param>
    /// <param name="solution">The converged iterate.</param>
    /// <returns>At most one diagnostic per valve, on the valve.</returns>
    private static ImmutableArray<Diagnostics.Diagnostic> Arrangements(CircuitGraph graph, SystemLayout layout, StateVector solution)
    {
        PortMap? ports = null;
        var said = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        for (var index = 0; index < graph.Components.Length; index++)
        {
            if (graph.Components[index] is not ThreeWayValveComponent { BypassConnected: true, Arrangement: not ValveArrangement.Unspecified } three)
            {
                continue;
            }

            ports ??= PortMap.Build(graph);

            if (BypassBalance.Read(graph, ports, layout, solution.Values.AsSpan(), index) is not { } legs)
            {
                continue;
            }

            var runs = legs.Mixing ? ValveArrangement.Mixing : ValveArrangement.Diverting;

            if (runs == three.Arrangement)
            {
                continue;
            }

            var detail = legs.Mixing
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{legs.FlowA:0.###} kg/s enters at {three.Ports[1].Name} and {legs.FlowB:0.###} kg/s at {three.Ports[2].Name}, leaving together at {three.Ports[0].Name}")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{legs.FlowA + legs.FlowB:0.###} kg/s enters at {three.Ports[0].Name} and leaves {legs.FlowA:0.###} kg/s by {three.Ports[1].Name} and {legs.FlowB:0.###} kg/s by {three.Ports[2].Name}");

            said.Add(Diagnostics.Diagnostic.Create(
                FluidScript.Core.Diagnostics.Descriptors.DesignDiagnostics.ArrangementContradictsKind,
                span: null,
                new Diagnostics.DiagnosticArgument("name", three.Name),
                new Diagnostics.DiagnosticArgument("declared", three.Arrangement == ValveArrangement.Mixing ? "mixing" : "diverting"),
                new Diagnostics.DiagnosticArgument("actual", legs.Mixing ? "mixing" : "diverting"),
                new Diagnostics.DiagnosticArgument("detail", detail))
                with
            { ComponentName = three.Name });
        }

        return said.ToImmutable();
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
            FluidScript.Core.Diagnostics.Descriptors.SizingDiagnostics.NotSettled,
            span: null,
            new Diagnostics.DiagnosticArgument("list", moving.Count == 0 ? "the sized values" : string.Join(", ", moving)));
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

    /// <summary>Names every pump and exchanger a converged solution runs backwards.</summary>
    /// <param name="graph">The solved graph.</param>
    /// <param name="layout">The state vector's layout.</param>
    /// <param name="solution">The converged iterate.</param>
    /// <returns>One <c>FS3013</c> per reversed component, in graph order.</returns>
    /// <remarks>
    /// Read off the port map rather than the branch orientation: a branch's sign says which way the
    /// walk crossed it, and a component walked from its outlet end is entered at <c>out</c> even when the
    /// branch flow is positive. What the map's sign gives is the flow <em>into</em> the component at a
    /// port, and a negative inflow at <c>in</c> is the reversal whatever the walk did (<c>S-10</c>).
    /// Only a converged iterate is read; the directions of one that is not are not answers.
    /// </remarks>
    private static ImmutableArray<Diagnostics.Diagnostic> Reversals(CircuitGraph graph, SystemLayout layout, StateVector solution)
    {
        var ports = PortMap.Build(graph);
        var reversed = ImmutableArray.CreateBuilder<Diagnostics.Diagnostic>();

        for (var element = 0; element < graph.Components.Length; element++)
        {
            var component = graph.Components[element];

            if (component is not (PumpComponent or HeatExchangerComponent))
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
                .Where(parameter => Ownership.Of(component, parameter) is ParameterState.Stated)
                .Select(parameter => Ownership.Key(component.Name, parameter))
                .ToArray();
            var note = terminals.Length == 0
                ? string.Empty
                : $"; its stated {string.Join(" and ", terminals)} {(terminals.Length == 1 ? "is" : "are")} on the port the water leaves by";

            reversed.Add(Diagnostics.Diagnostic.Create(
                FluidScript.Core.Diagnostics.Descriptors.SolverDiagnostics.ReversedFlow,
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
}
