using System.Collections.Immutable;

using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

/// <summary>Language 2's statement words and every block's settings: the one table the parser, the reader and the metadata read (<c>19</c>, <c>L-86</c>).</summary>
/// <remarks>
/// <para>
/// A component's parameters are the component registry's; everything else a script writes before an <c>=</c> is a
/// block's setting, and the words that open a statement are recognised by position. Those vocabularies were string
/// literals in five places -- the parser's statement words, three comma-joined lists in the reader's messages, a
/// controller's array and a run's <c>switch</c> -- which nothing else could read, so an editor that offers them had
/// to write a sixth copy (<c>A-9</c>). This table is the copy they all read; a test holds the reader and the run
/// binder to it by writing each setting and checking none is refused as unknown.
/// </para>
/// <para>
/// The order in each block is <c>19</c>'s, which is the order an unknown setting's message lists them in and the
/// order an editor offers them in.
/// </para>
/// </remarks>
public static class SettingRegistry
{
    /// <summary>Gets the words that open a statement at a line's start (<c>19</c> §Lines, blocks and names).</summary>
    /// <value>A component may not be named one of these; before an <c>=</c> each is a setting's name instead.</value>
    public static ImmutableArray<string> StatementWords { get; } = ["fluidscript", "project", "let", "curve", "circuit", "run"];

    /// <summary>Gets the words that open an event, inside a run only: a step and a ramp (<c>19</c> §Runs).</summary>
    public static ImmutableArray<string> EventWords { get; } = ["at", "over"];

    /// <summary>Gets the project block's settings.</summary>
    public static ImmutableArray<SettingInfo> Project { get; } =
    [
        new("cases", "The operating cases every list is read against, in order", SettingValueKind.Names),
        new("catalog", "The pipe catalogue and its version", SettingValueKind.Catalog),
        new("show", "The property the diagram colours by, one or a list", SettingValueKind.Names),
        new("scale", "The colour scale's range for the first property shown", SettingValueKind.Range),
        new("spacing", "The layout's spacing factor", SettingValueKind.Quantity) { Dimension = Dimension.Dimensionless },
        new("style", "The drawing's defaults, a block of colour, width, corner and line", SettingValueKind.Block),
    ];

    /// <summary>Gets a circuit block's settings.</summary>
    public static ImmutableArray<SettingInfo> Circuit { get; } =
    [
        new("fluid", "The substance the circuit carries", SettingValueKind.Substance),
        new("number", "The tag prefix", SettingValueKind.Quantity) { Dimension = Dimension.Dimensionless },
        new("role", "The circuit's role, where the drawing places it", SettingValueKind.CircuitRole),
        new("style", "Overrides the project's style for this circuit", SettingValueKind.Block),
    ];

    /// <summary>Gets a run block's settings; any other <c>name =</c> in a run is an override.</summary>
    public static ImmutableArray<SettingInfo> Run { get; } =
    [
        new("from", "The case whose steady state the run starts from", SettingValueKind.Case),
        new("start", "Where the clock sits on curves of time", SettingValueKind.Date),
        new("duration", "Simulated time", SettingValueKind.Quantity) { Dimension = Dimension.Time },
        new("frame", "Simulated time between kept states", SettingValueKind.Quantity) { Dimension = Dimension.Time },
        new("steady", "Circuits held quasi-steady in this run", SettingValueKind.Circuits),
    ];

    /// <summary>Gets a <c>style:</c> block's settings, in a project or a circuit.</summary>
    public static ImmutableArray<SettingInfo> Style { get; } =
    [
        new("colour", "A colour name, or a quoted hex since # starts a comment", SettingValueKind.Colour) { Aliases = ["color"] },
        new("width", "The line width in pixels", SettingValueKind.Quantity) { Dimension = Dimension.Pixels },
        new("corner", "How a route turns", SettingValueKind.Word) { Values = ["sharp", "fillet"] },
        new("line", "The line's pattern", SettingValueKind.Word) { Values = ["solid", "dashed", "dotted", "dashdot"] },
    ];

    /// <summary>Gets a controller declaration's settings, which replace the kind's registry parameters in language 2 (<c>D-168</c>).</summary>
    public static ImmutableArray<SettingInfo> Controller { get; } =
    [
        new("type", "The control law; absent means PI", SettingValueKind.Word) { Values = ["P", "PI", "PID", "onoff", "curve"] },
        new("moves", "The actuator: a component, meaning its one actuated parameter, or a qualified parameter", SettingValueKind.Actuator),
        new("reads", "What is measured: a sensor, a node's property, a driver, or time", SettingValueKind.Measurement),
        new("setpoint", "The target, in the measurement's unit", SettingValueKind.Value) { Types = ["P", "PI", "PID", "onoff"] },
        new("band", "The proportional band, in the measurement's unit", SettingValueKind.Quantity) { Types = ["P", "PI", "PID"] },
        new("kp", "The gain, as an alternative to band", SettingValueKind.Quantity) { Types = ["P", "PI", "PID"] },
        new("ti", "The integral time", SettingValueKind.Quantity) { Dimension = Dimension.Time, Types = ["PI", "PID"] },
        new("td", "The derivative time", SettingValueKind.Quantity) { Dimension = Dimension.Time, Types = ["PID"] },
        new("output", "The output limits, a range in the actuator's unit", SettingValueKind.Range),
        new("action", "direct or reverse, stated only as a check", SettingValueKind.Word) { Values = ["direct", "reverse"] },
        new("differential", "The switching differential, in the measurement's unit", SettingValueKind.Quantity) { Types = ["onoff"] },
        new("curve", "The characteristic: output as a function of the reading", SettingValueKind.Curve) { Types = ["curve"] },
    ];

    /// <summary>Gets every block and its settings, by the block's name as a message and the metadata name it.</summary>
    public static ImmutableArray<(string Block, ImmutableArray<SettingInfo> Settings)> Blocks { get; } =
    [
        ("project", Project),
        ("circuit", Circuit),
        ("run", Run),
        ("style", Style),
        ("controller", Controller),
    ];

    /// <summary>The settings a controller of a type takes, in <see cref="Controller"/>'s order.</summary>
    /// <param name="type">The type as written, <c>PI</c> or <c>pi</c>.</param>
    /// <returns>The settings, or empty for a type <see cref="Controller"/>'s <c>type</c> does not list.</returns>
    public static ImmutableArray<SettingInfo> ControllerSettingsOf(string type)
    {
        var normalized = NameResolution.Normalize(type);
        var types = Controller[0].Values;

        return !types.Any(known => NameResolution.Normalize(known) == normalized)
            ? []
            : [.. Controller.Where(setting => setting.Types.IsEmpty
                || setting.Types.Any(taken => NameResolution.Normalize(taken) == normalized))];
    }

    /// <summary>Whether a word opens a statement at a line's start.</summary>
    /// <param name="text">The word as written.</param>
    /// <returns><see langword="true"/> for one of <see cref="StatementWords"/>, spelled exactly.</returns>
    public static bool IsStatementWord(string text) => StatementWords.Contains(text, StringComparer.Ordinal);

    /// <summary>The settings of a block as an unknown setting's message lists them: <c>fluid, number, role, style</c>.</summary>
    /// <param name="settings">One of this registry's blocks.</param>
    /// <returns>The names, comma-separated, in the block's order.</returns>
    public static string Listed(ImmutableArray<SettingInfo> settings) => string.Join(", ", settings.Select(static setting => setting.Name));

    /// <summary>The setting a written name spells in a block, or <see langword="null"/>.</summary>
    /// <param name="settings">One of this registry's blocks.</param>
    /// <param name="written">The name as written.</param>
    /// <returns>The setting, matched as <see cref="SettingInfo.Matches"/> does.</returns>
    public static SettingInfo? Find(ImmutableArray<SettingInfo> settings, string written) =>
        settings.FirstOrDefault(setting => setting.Matches(written));
}
