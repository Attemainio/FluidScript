namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>What a line was classified as, before it was parsed.</summary>
/// <remarks>
/// <para>
/// Produced by <c>FluidScriptParser.Classify</c> from a line's first token, at most one token
/// of lookahead, and the section it sits in — which is
/// <c>plan/10-language/11-language-overview.md</c>'s invariant 7, expressed as a signature so that
/// breaking it does not compile.
/// </para>
/// <para>
/// Classification is not validation. <c>in N3</c> and <c>in pump power=30</c> are both
/// <see cref="Declaration"/> here, and telling them apart is the declaration parser's job — the first
/// is <c>FS1109</c> and the second is an ordinary component called <c>in</c>. That refinement needs
/// the line's length, not a third token, so the invariant holds.
/// </para>
/// </remarks>
public enum StatementKind
{
    /// <summary>The <c>fluidscript</c> version line.</summary>
    Version = 1,

    /// <summary>A <c>project</c> directive.</summary>
    Project,

    /// <summary>A <c>spacing</c> directive.</summary>
    Spacing,

    /// <summary>A <c>circuit</c> header.</summary>
    Circuit,

    /// <summary>A <c>fluid</c> directive.</summary>
    Fluid,

    /// <summary>A <c>catalog</c> directive.</summary>
    Catalog,

    /// <summary>A <c>style</c> directive.</summary>
    Style,

    /// <summary>A <c>show</c> directive.</summary>
    Show,

    /// <summary>A <c>let</c> binding.</summary>
    Let,

    /// <summary>The <c>connections</c> section marker.</summary>
    ConnectionsHeader,

    /// <summary>The <c>schedule</c> section marker.</summary>
    ScheduleHeader,

    /// <summary>A <c>supply</c> or <c>return</c> attachment.</summary>
    Attachment,

    /// <summary>A <c>control</c> binding.</summary>
    Control,

    /// <summary>A line of topology.</summary>
    Connection,

    /// <summary>A scheduled disturbance.</summary>
    Disturbance,

    /// <summary>A component declaration.</summary>
    Declaration,

    /// <summary>A <c>design</c> line, giving each driver a value (<c>D-58</c>).</summary>
    Design,

    /// <summary>A <c>scenarios</c> line, naming the cases the plant is sized for (<c>D-143</c>).</summary>
    Scenarios,

    /// <summary>A <c>curve</c> line, which names a table and opens its section (<c>D-57</c>).</summary>
    CurveHeader,

    /// <summary>One <c>x y</c> row inside a curve section.</summary>
    /// <remarks>
    /// Classified by section rather than by shape. Two bare values are a statement nowhere else in the
    /// language, so outside a curve section this is <c>FS1115</c> — a message that can say what the
    /// line is, rather than that it did not parse.
    /// </remarks>
    CurveRow,

    /// <summary>A line that could not be classified.</summary>
    Unclassifiable,
}
