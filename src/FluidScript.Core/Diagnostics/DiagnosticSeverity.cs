namespace FluidScript.Core.Diagnostics;

/// <summary>
/// How much a diagnostic affects the element it is about.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>Fatal</c>, deliberately. No single diagnostic stops the pipeline — that is
/// principle P4 in <c>plan/10-language/11-language-overview.md</c>, and it is what lets the canvas
/// keep rendering while a half-written script is on screen. The only thing that stops a run is an
/// internal fault, which is reported as an <c>FS90xx</c> error about the tool rather than about the
/// script.
/// </para>
/// <para>
/// The members are ordered by increasing seriousness so that a comparison — <c>severity >=
/// DiagnosticSeverity.Warning</c> — means what it reads as. The numeric values are not part of any
/// wire contract; the wire carries the name.
/// </para>
/// </remarks>
public enum DiagnosticSeverity
{
    /// <summary>Something was decided for the user, and they may want to know what.</summary>
    /// <remarks>Never squiggled. It appears in the log, collapsed, because the script is correct.</remarks>
    Info,

    /// <summary>The element was processed, but probably does not say what was meant.</summary>
    /// <remarks>Processing continues unchanged; only the presentation differs from <see cref="Info"/>.</remarks>
    Warning,

    /// <summary>The element cannot be processed and is skipped.</summary>
    /// <remarks>
    /// Everything else in the script still runs. An error is about one component, one connection or
    /// one line — never about the file.
    /// </remarks>
    Error,
}
