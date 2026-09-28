using System.Collections.Immutable;

namespace FluidScript.Core.Diagnostics.Descriptors;

/// <summary>The codes the parser and the reader emit about a script's blocks and lines.</summary>
/// <remarks>
/// <para>
/// The <c>FS18xx</c> range is the script's blocks and lines and how the reader hands them to the binder
/// (<c>plan/10-language/19-fluidscript-2.md</c>). The parser emits the codes about a line's shape; the reader
/// (<c>ScriptReader</c>) and the binder emit the ones that settle what a line means — ports, pipes, controllers,
/// a curve's driver and a run's times.
/// </para>
/// <para>
/// A line that fails for a reason another area already names keeps that area's code: an unreadable line is
/// <c>FS1104</c>, text after a complete statement <c>FS1114</c>, a malformed index <c>FS1119</c>.
/// </para>
/// </remarks>
public static class BlockDiagnostics
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
    /// <c>D-166</c>. Giving every link of the chain the properties would make <c>A - B - C  25 m</c> 50 m of
    /// pipe, so the line is refused instead of guessing which link was meant.
    /// </remarks>
    public static DiagnosticDescriptor PipeOnAChain { get; } = new(
        "FS1803",
        DiagnosticSeverity.Error,
        "A pipe's length and size describe one link, and this line has {links}. Put the pipe on a line of its own: '{first} - {second} {properties}'.");

    /// <summary>A line in the shape of a statement the language no longer has, with what replaced it (<c>D-174</c>).</summary>
    /// <value><c>FS1806</c>, an error.</value>
    public static DiagnosticDescriptor RetiredStatement { get; } = new(
        "FS1806",
        DiagnosticSeverity.Error,
        "'{word}' does not start a line this way; {instead}.");

    /// <summary>A ramp given one time or one value.</summary>
    /// <value><c>FS1807</c>, an error.</value>
    /// <remarks>
    /// Read as a step at the span's end, <c>over 60 s .. 120 s X = 45</c> would look like a ramp and not be one. The
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
    /// Raised by the reader (<c>D-166</c>): a length is known by its unit and a size by its <c>DN</c>, so a
    /// word that is neither describes nothing. The pipe keeps its length and is sized.
    /// </remarks>
    public static DiagnosticDescriptor NotAPipeSize { get; } = new(
        "FS1813",
        DiagnosticSeverity.Error,
        "'{text}' is not a pipe size. Write a DN designation such as DN25, or name the property: 'roughness = 0.05 mm'.");

    /// <summary>A sensor placed on a node with <c>at</c> that also sits in a chain.</summary>
    /// <value><c>FS1814</c>, an error.</value>
    /// <remarks>
    /// Raised by the reader (<c>D-166</c>): a sensor in a chain observes the point where it sits, so the two
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
        "'{controller}' is {type} controller, which has no '{parameter}'. It takes: {available}.");

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
        "'{controller}' is {type} controller, which the solver does not run yet.");

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
    /// <c>D-167</c>: there are no registered drivers, so a name a curve is driven by is a <c>let</c> with one value
    /// per case, or <c>time</c>. The curve binds and has no value; a static parameter reading it is <c>FS1528</c>.
    /// </remarks>
    public static DiagnosticDescriptor CurveDriverNotALet { get; } = new(
        "FS1811",
        DiagnosticSeverity.Error,
        "'{curve}' is driven by '{driver}', which is not a let. Write 'let {driver} = [...]' with one value per case, or drive it by time.");

    /// <summary>An event that starts after its run ends.</summary>
    /// <value><c>FS1817</c>, a warning.</value>
    /// <remarks>
    /// The event binds and is in the schedule, and the run stops before it: a user who typed <c>2 h</c> for
    /// <c>20 min</c>, or shortened the run, is told that what they wrote never happens (<c>L-67</c>).
    /// </remarks>
    public static DiagnosticDescriptor EventAfterRun { get; } = new(
        "FS1817",
        DiagnosticSeverity.Warning,
        "This event starts at {time}, after '{run}' ends at {duration}, so it never happens.");

    /// <summary>A second <c>project</c> block in one file.</summary>
    /// <value><c>FS1818</c>, an error.</value>
    /// <remarks>
    /// A file describes one project (<c>12</c> §The project block). Both blocks used to be read, one after the other, so
    /// a title, the cases and the catalogue came from whichever block set each last, and nothing said so (<c>L-88</c>).
    /// The first block is the project; the second is not read.
    /// </remarks>
    public static DiagnosticDescriptor SecondProjectBlock { get; } = new(
        "FS1818",
        DiagnosticSeverity.Error,
        "A file has one project block, and this is a second one, so its settings are not read. Move them into the first.");

    /// <summary>Gets every code in the <c>FS18xx</c> range, for the registry to collect.</summary>
    /// <value>Eighteen descriptors. Order does not matter; the registry sorts.</value>
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        InconsistentIndentation,
        StatementOutsideItsBlock,
        PipeOnAChain,
        RetiredStatement,
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
        EventAfterRun,
        SecondProjectBlock,
    ];
}
