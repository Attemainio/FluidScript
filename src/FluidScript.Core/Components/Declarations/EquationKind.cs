namespace FluidScript.Core.Components.Declarations;

/// <summary>What an equation asserts.</summary>
/// <remarks>Transcribed from <c>31</c>, which specifies it.</remarks>
public enum EquationKind
{
    /// <summary>A pressure relation along a branch or across a component.</summary>
    Pressure,

    /// <summary>A mass balance.</summary>
    Mass,

    /// <summary>An energy balance.</summary>
    Energy,

    /// <summary>A stated boundary condition.</summary>
    Boundary,

    /// <summary>A constraint a component imposes on its own state.</summary>
    ComponentConstraint,
}
