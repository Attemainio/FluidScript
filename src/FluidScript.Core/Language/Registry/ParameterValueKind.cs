namespace FluidScript.Core.Language.Registry;

/// <summary>What shape of value a parameter accepts.</summary>
public enum ParameterValueKind
{
    /// <summary>A number with a dimension, bare or with a unit.</summary>
    Quantity = 1,

    /// <summary>One name from a closed set, such as a valve characteristic.</summary>
    Symbol,

    /// <summary>Another component's property, such as a controller's measurement point.</summary>
    Reference,
}
