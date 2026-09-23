namespace FluidScript.Core.Components.Declarations;

/// <summary>What a solver unknown stands for.</summary>
/// <remarks>Transcribed from <c>31</c>, which specifies it. Tier 30 owns the meaning.</remarks>
public enum UnknownKind
{
    /// <summary>The one mass flow a whole branch shares.</summary>
    BranchFlow,

    /// <summary>A node's pressure.</summary>
    NodePressure,

    /// <summary>A node's specific enthalpy.</summary>
    NodeEnthalpy,

    /// <summary>Flow injected into or extracted from the circuit at a terminal.</summary>
    ExternalMassFlux,

    /// <summary>A component parameter promoted to an unknown by sizing.</summary>
    Parameter,
}
