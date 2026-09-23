using System.Collections.Immutable;
using System.Globalization;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Solvers.Equations;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Passes;

/// <summary>Phase B of <c>14</c>'s two-phase evaluation: the deferred expressions, read against a solved pass (<c>L-59</c>).</summary>
/// <remarks>
/// <para>
/// The binder evaluates everything it can and records the rest -- <c>in[2].t=HE1.out[2].t</c>,
/// <c>kv=0.7*CV2.kv</c> -- as <see cref="SemanticModel.Deferred"/>, each with the values it needs.
/// Until this existed nothing read that list: the parameter stayed null, lowering treated it as
/// absent, sizing chose a value, and the script's expression was silently ignored. This evaluates
/// each deferred expression against the pass the outer loop just solved, and
/// <see cref="Apply"/> writes the result into the model as that parameter's <em>stated</em> value --
/// stated, because the script wrote it, and the counting has to see it as the constraint it is.
/// </para>
/// <para>
/// <strong>What a reference reads.</strong> A property is resolved through the kind exactly as the
/// binder resolves it (<c>HX1.in[2].t</c> is the key <c>t_in2</c>), and its value comes from the pass
/// in this order: what the script stated, what the solver found for a promoted parameter, what the
/// ports carry (a flow, a drop, a terminal temperature or pressure, a rise), what sizing chose, the
/// registry's default. A <c>let</c> is read from the model, or from this evaluation when the
/// binding itself was deferred; deferred bindings are evaluated before the parameters that read
/// them. A value none of those supplies leaves the expression deferred for another pass and says
/// so in the loop's notes; it never invents a number.
/// </para>
/// <para>
/// <strong>Dimensions are checked here because they could not be checked earlier.</strong> The binder
/// cannot type <c>1.2*HE1.dp</c> against a pump's <c>head</c> until <c>HE1.dp</c> has a value, so
/// <c>FS1304</c> is raised on the pass that first evaluates it, with the same message the static
/// case gets. The diagnostics of the last evaluation travel with the run.
/// </para>
/// </remarks>
public static partial class DeferredEvaluation
{
    /// <summary>One deferred expression's value on a pass.</summary>
    /// <param name="Target">What the expression sets.</param>
    /// <param name="Value">The value, SI.</param>
    /// <param name="Expression">The expression as written, for the basis and for <c>FS1405</c>.</param>
    public sealed record Evaluated(ValueId Target, Quantity Value, string Expression);

    /// <summary>Evaluates every deferred expression the pass can supply.</summary>
    /// <param name="model">The model as lowered for this pass.</param>
    /// <param name="graph">The graph that was solved.</param>
    /// <param name="layout">The unknown layout the solution is addressed by.</param>
    /// <param name="solution">The converged solution.</param>
    /// <param name="diagnostics">Where the evaluator's findings go: a dimension mismatch, a division by zero.</param>
    /// <param name="notes">Where an expression left deferred is explained.</param>
    /// <param name="seeding">Whether <paramref name="solution"/> is the bootstrap seed rather than a solve, in which case only what the script anchored -- a stated parameter, a <c>let</c> -- is supplied.</param>
    /// <returns>The values found, by target; empty when the model deferred nothing.</returns>
    public static ImmutableArray<Evaluated> Evaluate(
        SemanticModel model,
        CircuitGraph graph,
        SystemLayout layout,
        StateVector solution,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ImmutableArray<string>.Builder notes,
        bool seeding = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(notes);

        if (model.Deferred.IsDefaultOrEmpty)
        {
            return [];
        }

        var scope = new SolvedScope(model, graph, layout, solution, seeding);
        var results = ImmutableArray.CreateBuilder<Evaluated>();

        // Bindings first, so a parameter reading a deferred `let` finds it; a chain of deferred
        // bindings needs one round per link, bounded by their number.
        var pending = model.Deferred
            .Where(static deferred => deferred.Source is not null && deferred.Target is ValueId.Let or ValueId.ComponentParameter)
            .OrderBy(static deferred => deferred.Target is ValueId.Let ? 0 : 1)
            .ToList();
        var rounds = Math.Max(1, pending.Count);

        for (var round = 0; round < rounds && pending.Count > 0; round++)
        {
            var progressed = false;

            foreach (var deferred in pending.ToArray())
            {
                var kind = deferred.Target is ValueId.ComponentParameter parameter
                    ? model.Components
                        .FirstOrDefault(component => string.Equals(component.Name, parameter.Component, StringComparison.Ordinal))?
                        .Kind
                    : null;
                var expected = deferred.Target is ValueId.ComponentParameter target
                    ? kind?.Parameters.GetValueOrDefault(target.Parameter)?.Dimension
                    : null;
                var spelled = deferred.Target is ValueId.ComponentParameter spelt && kind is not null
                    ? $"{spelt.Component}.{kind.ParameterName(spelt.Parameter)}"
                    : deferred.Target.ToString();
                var said = ImmutableArray.CreateBuilder<Diagnostic>();
                var evaluator = new ExpressionEvaluator(scope, deferred.Source!, said, expected);

                switch (evaluator.Evaluate(deferred.Expression))
                {
                    case EvaluationResult.Value value:
                        var text = deferred.Source!.ToString(deferred.Expression.Span).Trim();

                        // The binder checks a parameter's dimension when it stores the value, which it
                        // never reached for a deferred one: `head=1.2*HE1.dp` is a pressure written into
                        // metres, and the first pass that can evaluate it is the first that can say so.
                        var assigned = value.Quantity;

                        if (expected is { } dimension && !Quantity.TryAssign(value.Quantity, dimension, out assigned))
                        {
                            diagnostics.Add(Diagnostic.Create(
                                BinderDiagnostics.ParameterDimensionMismatch,
                                deferred.Expression.Span,
                                new DiagnosticArgument("parameter", spelled),
                                new DiagnosticArgument("expected", BinderDiagnostics.Expected(dimension)),
                                new DiagnosticArgument("value", text),
                                new DiagnosticArgument("actual", value.Quantity.Dimension.Name.ToLowerInvariant())));
                            pending.Remove(deferred);
                            progressed = true;
                            break;
                        }

                        results.Add(new Evaluated(deferred.Target, assigned, text));
                        scope.Supply(deferred.Target, assigned);
                        pending.Remove(deferred);
                        progressed = true;
                        break;

                    case EvaluationResult.Failed:
                        diagnostics.AddRange(said);
                        pending.Remove(deferred);
                        progressed = true;
                        break;

                    default:
                        break;
                }
            }

            if (!progressed)
            {
                break;
            }
        }

        foreach (var left in pending)
        {
            var text = left.Source!.ToString(left.Expression.Span).Trim();
            var missing = string.Join(", ", scope.Unsupplied.Select(static id => id.ToString()).Order(StringComparer.Ordinal));
            notes.Add($"{left.Target} = {text} was not evaluated: {missing} {(scope.Unsupplied.Count == 1 ? "is" : "are")} not published by the solve.");
        }

        return results.ToImmutable();
    }

    /// <summary>Writes evaluated values into the model as stated values, for the next pass to lower.</summary>
    /// <param name="model">The model the values were evaluated against.</param>
    /// <param name="values">What <see cref="Evaluate"/> found.</param>
    /// <param name="pass">The pass they were found on, for the basis.</param>
    /// <returns>The model with each target's value and basis set; the same instance when nothing was found.</returns>
    /// <remarks>
    /// A stated value, not a sized one: <c>D-02</c>'s rule is that presence is a constraint, and the
    /// script is present. The <see cref="ParameterValue.Basis"/> records the expression and the pass, so
    /// the report's <em>values chosen</em> lines and the wire's parameter hover say where the number came
    /// from; <c>OuterLoop.WithStated</c> is what lifts it into the bases.
    /// </remarks>
    public static SemanticModel Apply(SemanticModel model, ImmutableArray<Evaluated> values, int pass)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (values.IsDefaultOrEmpty)
        {
            return model;
        }

        var components = model.Components.ToBuilder();
        var bindings = model.Bindings.ToBuilder();

        foreach (var evaluated in values)
        {
            switch (evaluated.Target)
            {
                case ValueId.ComponentParameter parameter:
                    for (var i = 0; i < components.Count; i++)
                    {
                        if (!string.Equals(components[i].Name, parameter.Component, StringComparison.Ordinal)
                            || !components[i].Parameters.TryGetValue(parameter.Parameter, out var existing))
                        {
                            continue;
                        }

                        var basis = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{Describe(evaluated.Value)} from `{evaluated.Expression}` at pass {pass}");
                        // The design scenario's slot is filled here too, from the same evaluation:
                        // it is one expression with two homes, and a projection that found the slot
                        // empty would read the design case as unstated (`D-143`).
                        var design = model.Project.DesignScenarioIndex;
                        var bound = existing with { Value = evaluated.Value, Basis = basis };

                        if (design >= 0 && design < bound.Scenarios.Length)
                        {
                            bound = bound with
                            {
                                Scenarios = bound.Scenarios.SetItem(
                                    design,
                                    bound.Scenarios[design] with { Value = evaluated.Value, Basis = basis }),
                            };
                        }

                        components[i] = components[i] with
                        {
                            Parameters = components[i].Parameters.SetItem(parameter.Parameter, bound),
                        };
                    }

                    break;

                case ValueId.ScenarioParameter element:
                    for (var i = 0; i < components.Count; i++)
                    {
                        if (!string.Equals(components[i].Name, element.Component, StringComparison.Ordinal)
                            || !components[i].Parameters.TryGetValue(element.Parameter, out var listed)
                            || element.Scenario >= listed.Scenarios.Length)
                        {
                            continue;
                        }

                        // Only the element. `Value` belongs to the design scenario, whose element
                        // carries an ordinary `ComponentParameter` id and is written by the case
                        // above -- which is what keeps one field scalar for every reader (`D-143`).
                        var written = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{Describe(evaluated.Value)} from `{evaluated.Expression}` at pass {pass}");

                        components[i] = components[i] with
                        {
                            Parameters = components[i].Parameters.SetItem(
                                element.Parameter,
                                listed with
                                {
                                    Scenarios = listed.Scenarios.SetItem(
                                        element.Scenario,
                                        listed.Scenarios[element.Scenario] with
                                        {
                                            Value = evaluated.Value,
                                            Basis = written,
                                        }),
                                }),
                        };
                    }

                    break;

                case ValueId.Let let:
                    for (var i = 0; i < bindings.Count; i++)
                    {
                        if (string.Equals(bindings[i].Name, let.Name, StringComparison.Ordinal))
                        {
                            bindings[i] = bindings[i] with { Value = evaluated.Value };
                        }
                    }

                    break;

                default:
                    break;
            }
        }

        return model with { Components = components.ToImmutable(), Bindings = bindings.ToImmutable() };
    }

    /// <summary>A value in its canonical unit, for a basis or a message.</summary>
    /// <param name="value">The value.</param>
    /// <returns><c>4.3 m</c>, <c>45.2 °C</c>, <c>12.5 kPa</c>.</returns>
    public static string Describe(Quantity value)
    {
        var unit = UnitTable.CanonicalUnitFor(value.Dimension);

        return unit is null
            ? string.Create(CultureInfo.InvariantCulture, $"{value.SiValue:0.####} {value.Dimension.SiUnit}").TrimEnd()
            : string.Create(CultureInfo.InvariantCulture, $"{value.ValueIn(unit):0.####} {unit.Text}");
    }

    /// <summary>Whether two passes' values of one target differ beyond the fixed-point tolerance.</summary>
    /// <param name="before">The earlier value, SI.</param>
    /// <param name="after">The later value, SI.</param>
    /// <returns><see langword="true"/> when the target moved.</returns>
    /// <remarks>Relative 1e-6, the same tolerance the sizes settle to, so one pass answers both questions.</remarks>
    public static bool Moved(double before, double after) =>
        Math.Abs(before - after) > 1e-6 * Math.Max(1, Math.Abs(after));

    /// <summary>One <c>FS1405</c> per deferred expression still moving at the pass cap, with its last three values.</summary>
    /// <param name="histories">Every target's values, pass by pass.</param>
    /// <returns>The diagnostics, in target order.</returns>
    /// <remarks>
    /// The last value stands in the run, as <c>FS2301</c>'s sizes do: the report shows what the walk
    /// reached, and the three values show which way it was going.
    /// </remarks>
    public static ImmutableArray<Diagnostic> Unsettled(IReadOnlyDictionary<ValueId, List<Evaluated>> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);

        var result = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var (target, history) in histories.OrderBy(static pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            if (history.Count < 2 || !Moved(history[^2].Value.SiValue, history[^1].Value.SiValue))
            {
                continue;
            }

            var last = history.TakeLast(3).Select(static value => Describe(value.Value)).ToArray();

            result.Add(Diagnostic.Create(
                BinderDiagnostics.FixedPointNotSettled,
                span: null,
                new DiagnosticArgument("expr", $"{target} = {history[^1].Expression}"),
                new DiagnosticArgument("v1", last.Length > 2 ? last[^3] : last[0]),
                new DiagnosticArgument("v2", last.Length > 1 ? last[^2] : last[0]),
                new DiagnosticArgument("v3", last[^1])));
        }

        return result.ToImmutable();
    }

    /// <summary>One <c>FS1410</c> per deferred expression no pass could evaluate, naming what it waited for -- or <c>FS1412</c> when the run failed at a pass the line was absent from.</summary>
    /// <param name="model">The model the run ended on.</param>
    /// <param name="histories">Every target's values, pass by pass; a target absent here was never evaluated.</param>
    /// <param name="failedPass">The pass that was refused or did not converge, or <see langword="null"/> when the run stood (<c>L-62</c>).</param>
    /// <returns>The diagnostics, in target order.</returns>
    /// <remarks>
    /// A line the run could not use is otherwise silent -- the parameter is absent to lowering and a
    /// sizing rule chooses in its place, which is the fault <c>L-59</c> was filed for. Two exchangers
    /// each reading the other's leaving temperature is the usual way here: neither has a design point
    /// until the other is rated, so neither ever is.
    /// </remarks>
    public static ImmutableArray<Diagnostic> NeverEvaluated(SemanticModel model, IReadOnlyDictionary<ValueId, List<Evaluated>> histories, int? failedPass = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(histories);

        if (model.Deferred.IsDefaultOrEmpty)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var deferred in model.Deferred.OrderBy(static deferred => deferred.Target.ToString(), StringComparer.Ordinal))
        {
            if (deferred.Source is null || histories.ContainsKey(deferred.Target))
            {
                continue;
            }

            var waited = string.Join(", ", deferred.Dependencies.Select(static id => id.ToString()).Order(StringComparer.Ordinal));

            var target = new DiagnosticArgument("target", deferred.Target.ToString());
            var expression = new DiagnosticArgument("expr", deferred.Source.ToString(deferred.Expression.Span).Trim());

            result.Add(failedPass is { } pass
                ? Diagnostic.Create(
                    BinderDiagnostics.DeferredStillWaiting,
                    deferred.Expression.Span,
                    target,
                    expression,
                    new DiagnosticArgument("waited", waited),
                    new DiagnosticArgument("pass", pass.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                : Diagnostic.Create(
                    BinderDiagnostics.DeferredNeverEvaluated,
                    deferred.Expression.Span,
                    target,
                    expression,
                    new DiagnosticArgument("waited", waited)));
        }

        return result.ToImmutable();
    }
}
