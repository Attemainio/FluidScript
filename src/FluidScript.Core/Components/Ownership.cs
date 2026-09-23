namespace FluidScript.Core.Components;

/// <summary>Resolves a parameter's <see cref="ParameterState"/> from a component's three maps and the two label sets.</summary>
public static class Ownership
{
    /// <summary>The label every set and basis map keys a parameter by: <c>Name.parameter</c>.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns>The label.</returns>
    public static string Key(string component, string parameter) => $"{component}.{parameter}";

    /// <summary>Who owns a parameter.</summary>
    /// <param name="component">The component.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <param name="provisional">The labels of sized values that are bootstrap placeholders, or <see langword="null"/> when none are.</param>
    /// <param name="promoted">The labels of parameters the solver was given, or <see langword="null"/> before promotion has run.</param>
    /// <returns>The state, in the precedence <c>15</c> states: stated, then defaulted, then promoted, then sized, else free.</returns>
    public static ParameterState Of(
        IComponent component, string parameter, IReadOnlySet<string>? provisional = null, IReadOnlySet<string>? promoted = null)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.StatedParameters.ContainsKey(parameter))
        {
            return ParameterState.Stated;
        }

        if (component.DefaultParameters.ContainsKey(parameter))
        {
            return ParameterState.Defaulted;
        }

        var key = Key(component.Name, parameter);

        if (promoted?.Contains(key) == true)
        {
            return ParameterState.Promoted;
        }

        if (component.SizedParameters.ContainsKey(parameter))
        {
            return provisional?.Contains(key) == true ? ParameterState.SizedProvisional : ParameterState.SizedFinal;
        }

        return ParameterState.Free;
    }

    /// <summary>The value the script or the registry decided for a parameter, SI, or nothing when neither did.</summary>
    /// <param name="component">The component.</param>
    /// <param name="parameter">The canonical parameter name.</param>
    /// <returns>The stated value, else the visible default, else <see langword="null"/>: what a rule may read as given and must never size.</returns>
    public static double? Decided(IComponent component, string parameter)
    {
        ArgumentNullException.ThrowIfNull(component);

        return component.StatedParameters.TryGetValue(parameter, out var stated) ? stated.SiValue
            : component.DefaultParameters.TryGetValue(parameter, out var defaulted) ? defaulted.SiValue
            : null;
    }

    /// <summary>Whether a state leaves the parameter for a constraint to promote.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> for <see cref="ParameterState.Free"/> and <see cref="ParameterState.SizedProvisional"/>.</returns>
    public static bool IsFree(ParameterState state) => state is ParameterState.Free or ParameterState.SizedProvisional;
}
