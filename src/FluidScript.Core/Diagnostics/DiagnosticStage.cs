namespace FluidScript.Core.Diagnostics;

/// <summary>
/// The pipeline stage that produces a family of diagnostic codes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The numeric value of each member is the first two digits of its code range</strong>, and
/// that is load-bearing rather than incidental: <c>FS1002</c> divided by 100 is 10, which is
/// <see cref="Lexer"/>. A descriptor therefore derives its stage from its code instead of being told
/// it, so the two can never disagree — the failure this removes is a code filed under the wrong
/// stage, which is invisible until someone debugging asks the first question they always ask, namely
/// which stage produced this.
/// </para>
/// <para>
/// There is no zero member. Stage 0 is not an allocated range, so a <c>default</c> value would be a
/// stage that cannot exist. The range table is owned by
/// <c>plan/10-language/16-diagnostics.md</c>; adding a range means adding a member here.
/// </para>
/// </remarks>
public enum DiagnosticStage
{
    /// <summary>Characters to tokens. <c>FS10xx</c>.</summary>
    Lexer = 10,

    /// <summary>Tokens to a syntax tree. <c>FS11xx</c>.</summary>
    Parser = 11,

    /// <summary>The <c>style</c> directive's own vocabulary. <c>FS12xx</c>.</summary>
    StyleDirective = 12,

    /// <summary>Units and dimensional analysis. <c>FS13xx</c>.</summary>
    Units = 13,

    /// <summary>Expressions and references between components. <c>FS14xx</c>.</summary>
    Expressions = 14,

    /// <summary>Binding and structural inference. <c>FS15xx</c>.</summary>
    Binder = 15,

    /// <summary>Printing and canvas write-back. <c>FS16xx</c>.</summary>
    Printer = 16,

    /// <summary>Opening a script written by another version. <c>FS17xx</c>.</summary>
    Compatibility = 17,

    /// <summary>Substances and their thermodynamic properties. <c>FS20xx</c>.</summary>
    Substances = 20,

    /// <summary>Component behaviour and parameter consistency. <c>FS21xx</c>.</summary>
    Components = 21,

    /// <summary>Graph construction and well-posedness. <c>FS22xx</c>.</summary>
    Topology = 22,

    /// <summary>Auto-sizing and the constraints it works under. <c>FS23xx</c>.</summary>
    Sizing = 23,

    /// <summary>Layout hints Core supplies to the canvas. <c>FS24xx</c>.</summary>
    LayoutHints = 24,

    /// <summary>Serialization of the model contract. <c>FS25xx</c>.</summary>
    ModelContract = 25,

    /// <summary>Loading catalogue tables and selecting from them. <c>FS26xx</c>.</summary>
    Catalog = 26,

    /// <summary>Steady-state solving. <c>FS30xx</c>.</summary>
    Solver = 30,

    /// <summary>Time-domain integration. <c>FS31xx</c>.</summary>
    Transient = 31,

    /// <summary>Controllers and their bindings. <c>FS32xx</c>.</summary>
    Controllers = 32,

    /// <summary>Evolutionary sizing. <c>FS35xx</c>.</summary>
    Optimization = 35,

    /// <summary>Warnings about the design rather than the script. <c>FS40xx</c>.</summary>
    /// <remarks>
    /// The design-warning family reserves <c>FS40xx</c> through <c>FS44xx</c>. Only <c>FS40xx</c> is
    /// allocated; the gap is deliberate, so that a reader seeing <c>FS4</c> can assume the message is
    /// about the plant without checking. <see cref="Realtime"/> sits at 45 for that reason and not
    /// because 43 was taken.
    /// </remarks>
    DesignWarning = 40,

    /// <summary>The realtime protocol between the API and the browser. <c>FS45xx</c>.</summary>
    Realtime = 45,

    /// <summary>Frontend layout and rendering. <c>FS50xx</c>.</summary>
    Rendering = 50,

    /// <summary>The tool itself failed. <c>FS90xx</c>.</summary>
    /// <remarks>
    /// The one stage whose messages may use internal vocabulary, because they are bug reports rather
    /// than statements about the user's script.
    /// </remarks>
    Internal = 90,
}
