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

    /// <summary>A ramp given one time or one value.</summary>
    /// <value><c>FS1807</c>, an error.</value>
    /// <remarks>
    /// Language 1 reads <c>over 60 s .. 120 s X = 45</c> as a step at the span's end, which looks like a ramp. The
    /// message names the half that has one end, since a ramp over <c>5 min</c> with a range of values is wrong in its
    /// time and a ramp over a span with one value is wrong in its value.
    /// </remarks>
    public static DiagnosticDescriptor RampWithOneValue { get; } = new(
        "FS1807",
        DiagnosticSeverity.Error,
        "A ramp needs both ends of {half}, such as '{example}'. For a step, write 'at'.");

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

    /// <summary>A connection whose port the flow-direction rule cannot settle.</summary>
    /// <value><c>FS1804</c>, an error.</value>
    /// <remarks>
    /// <c>D-166</c> rule 6: what the rule cannot settle is an error, never a guess. The connection is left without
    /// a port and binds as far as it can; the message names what the component has and what to write.
    /// </remarks>
    public static DiagnosticDescriptor PortNotInferred { get; } = new(
        "FS1804",
        DiagnosticSeverity.Error,
        "'{component}' cannot take this connection: {reason}. Name the port, such as '{example}'.");

    /// <summary>A valve written as mixing or diverting whose connections say the other function.</summary>
    /// <value><c>FS1805</c>, an error.</value>
    /// <remarks>
    /// <c>D-166</c> rule 2: <c>mixing_valve</c> and <c>diverting_valve</c> assert the function the connections
    /// otherwise decide. The ports follow the connections, which is what the plant will do.
    /// </remarks>
    public static DiagnosticDescriptor ValveFunctionContradicted { get; } = new(
        "FS1805",
        DiagnosticSeverity.Error,
        "'{component}' is written as a {asserted} valve, and its connections make it {actual}: {inflows} in and {outflows} out.");

    /// <summary>The ports a multi-port component was given by the flow-direction rule.</summary>
    /// <value><c>FS1815</c>, information.</value>
    /// <remarks>
    /// <c>19</c> §Connections: every inference is stated once, so the reading is visible without opening the
    /// drawing. Raised for a three-way valve, an exchanger wired on both sides, and a tank; a two-port component's
    /// inlet and outlet say nothing a reader could doubt.
    /// </remarks>
    public static DiagnosticDescriptor PortsInferred { get; } = new(
        "FS1815",
        DiagnosticSeverity.Info,
        "'{component}' is wired as {wiring}.");

    /// <summary>A controller setting its stated type does not have.</summary>
    /// <value><c>FS1808</c>, an error.</value>
    /// <remarks>
    /// <c>19</c> §Controllers: <c>td</c> on a <c>PI</c>, <c>differential</c> on a <c>PI</c>, <c>band</c> on an <c>onoff</c>.
    /// The setting is left out, so the controller binds as the type it states.
    /// </remarks>
    public static DiagnosticDescriptor ControllerSettingNotOfType { get; } = new(
        "FS1808",
        DiagnosticSeverity.Error,
        "'{controller}' is a {type} controller, which has no '{parameter}'. A {type} controller takes: {available}.");

    /// <summary>A controller stating both its proportional band and its gain.</summary>
    /// <value><c>FS1809</c>, an error.</value>
    /// <remarks><c>kp = range / band</c> (<c>19</c>): one says the other, so two can disagree. The band is kept.</remarks>
    public static DiagnosticDescriptor BandAndGain { get; } = new(
        "FS1809",
        DiagnosticSeverity.Error,
        "'{controller}' states both band and kp, and each says the other. State one.");

    /// <summary>A controller of a type the solver does not run yet.</summary>
    /// <value><c>FS1810</c>, a warning.</value>
    /// <remarks>
    /// <c>19</c> §Controllers: until P6.3 builds them, the types other than <c>PI</c> bind and say so, and are
    /// never run as <c>PI</c> in silence.
    /// </remarks>
    public static DiagnosticDescriptor ControllerTypeNotRun { get; } = new(
        "FS1810",
        DiagnosticSeverity.Warning,
        "'{controller}' is a {type} controller, which the solver does not run yet.");

    /// <summary>A clock time in a run that states no start.</summary>
    /// <value><c>FS1816</c>, an error.</value>
    /// <remarks><c>19</c> §Runs: a time is a duration from t = 0, or a clock time when the run states <c>start</c>.</remarks>
    public static DiagnosticDescriptor ClockTimeWithoutStart { get; } = new(
        "FS1816",
        DiagnosticSeverity.Error,
        "'{time}' is a clock time, and '{run}' states no start. Write 'start = 2026-01-15 06:00' in the run, or a duration such as '30 min'.");

    /// <summary>A curve whose driver is neither a <c>let</c> nor the clock.</summary>
    /// <value><c>FS1811</c>, an error.</value>
    /// <remarks>
    /// <c>D-167</c>: language 2 has no registered drivers and no <c>design</c> line, so a name a curve is driven by
    /// is a <c>let</c> with one value per case, or <c>time</c>. The curve binds and has no value; a static
    /// parameter reading it is <c>FS1528</c> as in language 1.
    /// </remarks>
    public static DiagnosticDescriptor CurveDriverNotALet { get; } = new(
        "FS1811",
        DiagnosticSeverity.Error,
        "'{curve}' is driven by '{driver}', which is not a let. Write 'let {driver} = [...]' with one value per case, or drive it by time.");

    /// <summary>Gets every code language 2's parser and translation emit, for the registry to collect.</summary>
    /// <value>Sixteen descriptors. Order does not matter; the registry sorts.</value>
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
        PortNotInferred,
        ValveFunctionContradicted,
        PortsInferred,
        CurveDriverNotALet,
        ControllerSettingNotOfType,
        BandAndGain,
        ControllerTypeNotRun,
        ClockTimeWithoutStart,
    ];
}
