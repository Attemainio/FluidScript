namespace FluidScript.Core.Components.Valves;

/// <summary>How a valve's effective flow coefficient follows its opening.</summary>
public enum ValveCharacteristic
{
    /// <summary>φ = x. Effective Kv is proportional to the opening.</summary>
    Linear,

    /// <summary>φ = R^(x−1) with rangeability R = 50.</summary>
    EqualPercentage,

    /// <summary>φ = √x. Most of the capacity arrives in the first part of the travel.</summary>
    QuickOpen,
}
