namespace FluidScript.Core.Language.Compatibility;

/// <summary>Something the application may do with a file, given its disposition.</summary>
public enum CompatibilityAction
{
    /// <summary>Parse, bind and report diagnostics.</summary>
    Compile = 0,

    /// <summary>Size and solve.</summary>
    Solve,

    /// <summary>Write the edited text back over the file.</summary>
    Save,

    /// <summary>Write the bytes somewhere else, unchanged.</summary>
    SaveAsBytes,

    /// <summary>Compute and show a migration to the current major.</summary>
    PreviewMigration,
}
