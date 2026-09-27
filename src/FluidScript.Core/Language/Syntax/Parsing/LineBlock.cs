namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>The kind of block a line sits in, which decides what the line may be.</summary>
/// <remarks><c>plan/10-language/12-grammar.md</c> §Lines and blocks, §Statements.</remarks>
internal enum LineBlock
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
