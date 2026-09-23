namespace FluidScript.Core.Language.Binding;

/// <summary>Where a circuit sits in the chain from heat source to heat consumer.</summary>
public enum ThermalStageRole
{
    /// <summary>No classification — the honest answer for a name the registry does not know.</summary>
    Neutral = 0,

    /// <summary>Brings heat into the plant.</summary>
    Source,

    /// <summary>Moves heat between circuits, or changes its grade.</summary>
    Conversion,

    /// <summary>Holds heat over time.</summary>
    Storage,

    /// <summary>Takes useful heat out of the plant.</summary>
    Consumer,
}
