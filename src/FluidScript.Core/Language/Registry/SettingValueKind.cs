namespace FluidScript.Core.Language.Registry;

/// <summary>What a language 2 setting's value is, which is what an editor offers after its <c>=</c> (<c>19</c>, <c>52</c>).</summary>
public enum SettingValueKind
{
    /// <summary>A quantity, in the setting's dimension when it has one fixed dimension: <c>duration = 2 h</c>.</summary>
    Quantity,

    /// <summary>One word of a closed set: <c>corner = fillet</c>, <c>type = PI</c>.</summary>
    Word,

    /// <summary>A substance the property backend knows: <c>fluid = water</c>.</summary>
    Substance,

    /// <summary>A circuit role (<c>D-35</c>): <c>role = radiator</c>.</summary>
    CircuitRole,

    /// <summary>A catalogue and its version: <c>catalog = steel_en10255@2026.1</c>.</summary>
    Catalog,

    /// <summary>One of the cases the project names: <c>from = winter</c>.</summary>
    Case,

    /// <summary>New names, one or a list: <c>cases = [winter, mild]</c>, <c>show = temperature</c>.</summary>
    Names,

    /// <summary>Circuit titles, quoted, one or a list: <c>steady = ["District primary"]</c>.</summary>
    Circuits,

    /// <summary>A date, with or without a clock time: <c>start = 2026-01-15 06:00</c>.</summary>
    Date,

    /// <summary>A range, <c>a..b</c>, with its unit after it: <c>scale = 20..90 C</c>.</summary>
    Range,

    /// <summary>A colour name or a quoted hex: <c>colour = crimson</c>.</summary>
    Colour,

    /// <summary>Nothing after the <c>=</c>: the setting opens a block of its own, <c>style:</c>.</summary>
    Block,

    /// <summary>A component, meaning its one actuated parameter, or a qualified parameter: <c>moves = TV1</c>.</summary>
    Actuator,

    /// <summary>What a controller measures: a sensor, a node's property, a driver, or <c>time</c>.</summary>
    Measurement,

    /// <summary>A value in what is measured: a number, a curve, a driver or a list.</summary>
    Value,

    /// <summary>A curve, by name.</summary>
    Curve,
}
