namespace FluidScript.Core.Components;

/// <summary>Which end of an open circuit a node is (<c>D-64</c>).</summary>
/// <remarks>
/// <strong>Intent, not parameters.</strong> Whether a terminal is an outlet or an unfinished stub
/// changes its mass balance from an unknown external flux to a zero-flow closure, and no combination
/// of <c>t</c>, <c>p</c> and <c>flow</c> distinguishes them — which is why the script says it and the
/// checker does not guess.
/// </remarks>
public enum BoundaryRole
{
    /// <summary>An ordinary node. Its external flux is zero unless it states a pressure.</summary>
    Interior = 0,

    /// <summary>Fluid enters the model here, in a state the script states.</summary>
    Inlet,

    /// <summary>Fluid leaves the model here. Its external flux is an unknown.</summary>
    Outlet,
}
