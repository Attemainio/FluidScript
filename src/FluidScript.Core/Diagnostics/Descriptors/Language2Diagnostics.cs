using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics.Descriptors;

/// <summary>The codes language 2's parser and its translation emit.</summary>
/// <remarks>
/// <para>
/// The <c>FS18xx</c> range is language 2's syntax and its translation to the binder
/// (<c>plan/10-language/19-fluidscript-2.md</c>, <c>D-164</c>). The codes the translation emits —
/// <c>FS1804</c>, <c>FS1805</c>, <c>FS1808</c> to <c>FS1811</c> — land with the translation, as a descriptor
/// lands with its emitter (<c>D-53</c>); these are the parser's.
/// </para>
/// <para>
/// A language 2 line that fails for a reason language 1 already names keeps language 1's code: an unreadable
/// line is <c>FS1104</c>, text after a complete statement <c>FS1114</c>, a malformed index <c>FS1119</c>.
/// </para>
/// </remarks>
public static class Language2Diagnostics
{
    /// <summary>A block body whose lines are not indented alike.</summary>
    /// <value><c>FS1801</c>, an error.</value>
    /// <remarks>
    /// Raised on the line that disagrees, which is then read as belonging to the nearer level: a line
    /// between two levels, or one whose indentation mixes tabs and spaces with its block's.
    /// </remarks>
    public static DiagnosticDescriptor InconsistentIndentation { get; } = new(
        "FS1801",
        DiagnosticSeverity.Error,
        "This line is indented unlike the rest of its block. Indent it as the line above it is.");

    /// <summary>A statement outside the block it belongs in.</summary>
    /// <value><c>FS1802</c>, an error.</value>
    /// <remarks>
    /// Both directions: a setting at the top level belongs inside a block, and a <c>circuit</c> line indented
    /// under another block belongs at the top level. The line is still parsed as what it is, so the one
    /// misplaced line costs one message.
    /// </remarks>
    public static DiagnosticDescriptor StatementOutsideItsBlock { get; } = new(
        "FS1802",
        DiagnosticSeverity.Error,
        "{statement} belongs {place}.");

    /// <summary>Pipe properties on a line with more than one link.</summary>
    /// <value><c>FS1803</c>, an error.</value>
    /// <remarks>
    /// <c>D-166</c>. Language 1 gives every link of the chain the properties, so <c>A - B - C length=25</c> is
    /// 50 m of pipe; language 2 asks instead of guessing which link was meant.
    /// </remarks>
    public static DiagnosticDescriptor PipeOnAChain { get; } = new(
        "FS1803",
        DiagnosticSeverity.Error,
        "A pipe's length and size describe one link, and this line has {links}. Put the pipe on a line of its own: '{first} - {second} {properties}'.");

    /// <summary>A language 1 statement in a language 2 file.</summary>
    /// <value><c>FS1806</c>, an error.</value>
    public static DiagnosticDescriptor Language1Statement { get; } = new(
        "FS1806",
        DiagnosticSeverity.Error,
        "'{word}' is language 1. In language 2, {instead}.");

    /// <summary>A ramp given one value.</summary>
    /// <value><c>FS1807</c>, an error.</value>
    /// <remarks>Language 1 reads <c>over 60 s .. 120 s X = 45</c> as a step at the span's end, which looks like a ramp.</remarks>
    public static DiagnosticDescriptor RampWithOneValue { get; } = new(
        "FS1807",
        DiagnosticSeverity.Error,
        "A ramp needs both ends, such as '{target} = 30..45'. For a step, write 'at'.");

    /// <summary>A block head without its colon.</summary>
    /// <value><c>FS1812</c>, an error.</value>
    /// <remarks>The lines indented under it are still read as its block, so one missing character costs one message.</remarks>
    public static DiagnosticDescriptor HeadWithoutColon { get; } = new(
        "FS1812",
        DiagnosticSeverity.Error,
        "A {head} line opens a block and ends with ':'.");

    /// <summary>A word after a pipe's link that is not a DN designation.</summary>
    /// <value><c>FS1813</c>, an error.</value>
    /// <remarks>
    /// Raised by the translation (<c>D-166</c>): a length is known by its unit and a size by its <c>DN</c>, so a
    /// word that is neither describes nothing. The pipe keeps its length and is sized.
    /// </remarks>
    public static DiagnosticDescriptor NotAPipeSize { get; } = new(
        "FS1813",
        DiagnosticSeverity.Error,
        "'{text}' is not a pipe size. Write a DN designation such as DN25, or name the property: 'roughness = 0.05 mm'.");

    /// <summary>A sensor placed on a node with <c>at</c> that also sits in a chain.</summary>
    /// <value><c>FS1814</c>, an error.</value>
    /// <remarks>
    /// Raised by the translation (<c>D-166</c>): a sensor in a chain observes the point where it sits, so the two
    /// placements name two points for one reading. The chain's is kept.
    /// </remarks>
    public static DiagnosticDescriptor SensorPlacedTwice { get; } = new(
        "FS1814",
        DiagnosticSeverity.Error,
        "'{sensor}' sits in a chain and is also placed at '{node}'. Keep one: in a chain it reads the point where it sits.");

    /// <summary>Gets every code language 2's parser and translation emit, for the registry to collect.</summary>
    /// <value>Eight descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        InconsistentIndentation,
        StatementOutsideItsBlock,
        PipeOnAChain,
        Language1Statement,
        RampWithOneValue,
        HeadWithoutColon,
        NotAPipeSize,
        SensorPlacedTwice,
    ];
}
