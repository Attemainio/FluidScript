namespace FluidScript.Core.Language.Registry;

/// <summary>When a property becomes readable.</summary>
public enum PropertyAvailability
{
    /// <summary>Available as soon as the script is bound, because the user stated it.</summary>
    Declared = 1,

    /// <summary>Available once sizing has run.</summary>
    Sized,

    /// <summary>Available only after a solve.</summary>
    Solved,
}
