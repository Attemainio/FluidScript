namespace FluidScript.Core.Language.Compatibility;

/// <summary>What a file's stated version means for this application.</summary>
public enum CompatibilityDisposition
{
    /// <summary>The file states the current major.</summary>
    Current = 0,

    /// <summary>An older major this application still supports, parsed under that major's semantics.</summary>
    SupportedOld,

    /// <summary>A major newer than this application knows.</summary>
    UnsupportedNewer,

    /// <summary>An older major this application has dropped.</summary>
    UnsupportedOld,

    /// <summary>Editor text with no directive at all — recoverable, and never durably saved.</summary>
    UnversionedDraft,
}
