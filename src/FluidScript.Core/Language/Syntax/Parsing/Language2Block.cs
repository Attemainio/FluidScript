namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>The kind of language 2 block a line sits in, which decides what the line may be.</summary>
/// <remarks><c>plan/10-language/19-fluidscript-2.md</c> §Lines, blocks and names, §Statements.</remarks>
internal enum Language2Block
{
    /// <summary>No block: the file's top level, where the version line, the project, lets, curves, circuits and runs go.</summary>
    TopLevel,

    /// <summary>A <c>project "Title":</c> block: cases, catalogue and presentation.</summary>
    Project,

    /// <summary>A <c>circuit "Title":</c> block: settings, declarations and connection lines.</summary>
    Circuit,

    /// <summary>A <c>run "Title":</c> block: run settings, overrides and events.</summary>
    Run,

    /// <summary>A <c>style:</c> block inside the project or a circuit.</summary>
    Style,

    /// <summary>A curve's rows.</summary>
    Curve,

    /// <summary>A component declaration ending in <c>:</c>: its parameter lines.</summary>
    Declaration,
}
