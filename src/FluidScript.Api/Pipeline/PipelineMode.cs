namespace FluidScript.Api.Pipeline;

/// <summary>How far a request asks the pipeline to go, and how strict it is.</summary>
public enum PipelineMode
{
    /// <summary>Parse and bind only: diagnostics, no physics (<c>/validate</c>).</summary>
    Validate = 1,

    /// <summary>The debounce path: lower, size and solve, tolerating an unconnected component (<c>/compile</c>).</summary>
    Compile,

    /// <summary>The Solve button: as <see cref="Compile"/>, with <c>FS1507</c> and <c>FS1511</c> raised to errors (<c>/solve</c>).</summary>
    Solve,
}
