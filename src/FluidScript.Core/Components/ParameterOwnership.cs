namespace FluidScript.Core.Components;

/// <summary>Who owns a parameter right now (<c>D-02</c>, <c>D-96</c>, <c>D-130</c>).</summary>
/// <remarks>
/// The one question the sizing loop, well-posedness and the two renderers all ask: is this parameter a
/// constraint the script wrote, a default the registry decided, a value a rule chose, a placeholder the
/// bootstrap wrote so the component could be built, an unknown the solver was given, or nothing yet.
/// Before <c>70</c>'s R3 it was answered at eleven sites with their own dictionary reads.
/// </remarks>
public enum ParameterState
{
    /// <summary>The script wrote it: a constraint, never sized and never promoted.</summary>
    Stated,

    /// <summary>The registry decided a visible default for it, which counts as decided (<c>D-32</c>).</summary>
    Defaulted,

    /// <summary>A stated constraint turned it into a solver unknown (<c>23</c>'s promotion).</summary>
    Promoted,

    /// <summary>A sizing rule chose it.</summary>
    SizedFinal,

    /// <summary>The bootstrap wrote a placeholder so the component exists; still free to promote (<c>D-96</c>).</summary>
    SizedProvisional,

    /// <summary>Nothing has decided it.</summary>
    Free,
}

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
