using System.Collections.Immutable;

namespace FluidScript.Core.Language.Binding;

/// <summary>Turns one bound model carrying per-scenario lists into the model of a single case (<c>D-143</c>).</summary>
/// <remarks>
/// <para>
/// <strong>This is the whole of what scenarios cost the rest of the system.</strong> A parameter
/// written <c>power=[30, 10]</c> keeps <see cref="ParameterValue.Value"/> a scalar throughout — the
/// design scenario's element — and carries the other cases beside it in
/// <see cref="ParameterValue.Scenarios"/>. Projecting to case <c>i</c> rewrites that one field from
/// that array and changes nothing else, so lowering, the component factory, the sizers and the
/// solvers run against an ordinary model and never learn that more than one case exists.
/// </para>
/// <para>
/// The alternative was a model that carried lists all the way down, with a selector at each of the
/// thirty-odd sites that read a parameter's value. That is the same feature with thirty places to get
/// it wrong, and every one of them on a path that files without scenarios also take.
/// </para>
/// <para>
/// <strong>It projects values and nothing else.</strong> The project settings, including the scenario
/// list itself, travel unchanged: a projected model still knows which case it is, which is what lets
/// a size's basis name the case that governed it.
/// </para>
/// </remarks>
public static class ScenarioProjection
{
    /// <summary>Projects a bound model onto one declared scenario.</summary>
    /// <param name="model">The bound model.</param>
    /// <param name="scenario">The case's position in <see cref="ProjectSettings.Scenarios"/>.</param>
    /// <returns>
    /// The same model with every list-valued parameter reduced to that case's value. The model itself
    /// when it declares no scenarios, or when <paramref name="scenario"/> is the design case — both are
    /// already the model of exactly one case, so neither allocates.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scenario"/> is not a declared case.</exception>
    public static SemanticModel Project(SemanticModel model, int scenario)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Project.Scenarios.IsEmpty)
        {
            return model;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(scenario);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(scenario, model.Project.Scenarios.Length);

        if (scenario == model.Project.DesignScenarioIndex)
        {
            return model;
        }

        var components = ImmutableArray.CreateBuilder<ComponentSymbol>(model.Components.Length);

        foreach (var component in model.Components)
        {
            components.Add(Project(component, scenario));
        }

        // A setpoint that follows a driver holds that case's value in that case's design solve (`D-167`).
        var bindings = model.ControlBindings
            .Select(binding => scenario < binding.Setpoints.Length ? binding with { Setpoint = binding.Setpoints[scenario] } : binding)
            .ToImmutableArray();

        return model with { Components = components.MoveToImmutable(), ControlBindings = bindings };
    }

    /// <summary>Reduces one component's list-valued parameters to a single case.</summary>
    /// <param name="component">The component.</param>
    /// <param name="scenario">The case's position.</param>
    /// <returns>The component, unchanged when it states no list.</returns>
    private static ComponentSymbol Project(ComponentSymbol component, int scenario)
    {
        ImmutableDictionary<string, ParameterValue>.Builder? projected = null;

        foreach (var (key, value) in component.Parameters)
        {
            if (scenario >= value.Scenarios.Length)
            {
                continue;
            }

            projected ??= component.Parameters.ToBuilder();

            // The element's own value, span and basis become the parameter's. The list travels with
            // it rather than being cleared: a projected model is still evidence of what the file
            // wrote, which a report naming the governing case needs.
            var element = value.Scenarios[scenario];

            projected[key] = value with
            {
                Value = element.Value,
                Symbol = element.Symbol,
                Reference = element.Reference,
                Basis = element.Basis,
            };
        }

        return projected is null ? component : component with { Parameters = projected.ToImmutable() };
    }
}
