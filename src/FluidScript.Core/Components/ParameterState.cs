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
