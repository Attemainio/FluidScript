namespace FluidScript.Core.Language.Registry;

/// <summary>What the absence of a parameter means (<c>D-02</c>).</summary>
/// <remarks>
/// <strong>The set is closed, which is the whole force of <c>D-02</c>.</strong> Every parameter falls
/// into exactly one of these, so "what happens if I leave it out" always has an answer the registry
/// states rather than the code implies.
/// </remarks>
public enum ParameterOmissionBehavior
{
    /// <summary>Sizing decides the value, and reports what it decided.</summary>
    Size = 1,

    /// <summary>An explicit, visible default applies, with a stated basis.</summary>
    Default,

    /// <summary>There is no answer without it, and its absence is a diagnostic (<c>D-64</c>).</summary>
    /// <remarks>
    /// Added for boundaries, and deliberately rare. A required parameter is one where every possible
    /// substitute would be a guess about the plant rather than about the model: a <c>supply</c> with no
    /// temperature has no state to give the fluid entering there, and inventing one produces a solved
    /// circuit whose every downstream temperature is wrong with nothing to show for it.
    /// </remarks>
    Require,
}
