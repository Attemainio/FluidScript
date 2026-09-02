namespace FluidScript.Core.Syntax;

/// <summary>The words that may not be used as an identifier.</summary>
/// <remarks>
/// <para>
/// Deliberately tiny (principle P6). Component *kinds* are not here: <c>heat_exchanger</c>,
/// <c>pump</c>, <c>node</c> and <c>pipe</c> resolve through the component registry at bind time, so
/// adding a kind never breaks a script that used the name as an identifier. Reserving <c>node</c> and
/// <c>pipe</c> once made <c>P1 pipe length=45</c> unparseable, which is a line in both reference
/// circuits.
/// </para>
/// <para>
/// <strong>Adding one is a breaking language change</strong>
/// (<c>plan/10-language/18-script-compatibility.md</c>): a word that was a legal identifier stops
/// being one. Every word here introduces a statement and is therefore recognisable from a line's first
/// token, which is the standard a new one must also meet.
/// </para>
/// </remarks>
public enum ReservedWord
{
    /// <summary>Not a reserved word.</summary>
    /// <remarks>The value carried by every token that is not a keyword.</remarks>
    None = 0,

    /// <summary>The version directive that opens every script.</summary>
    Fluidscript,

    /// <summary>Names the project and sets the file-wide default solve mode (<c>D-37</c>).</summary>
    Project,

    /// <summary>Begins a circuit (<c>D-33</c>).</summary>
    Circuit,

    /// <summary>Selects the substance a circuit carries, and how it is solved.</summary>
    Fluid,

    /// <summary>Solve in time. Qualifies <c>project</c> or <c>fluid</c>.</summary>
    Dynamic,

    /// <summary>Solve as an equilibrium. Qualifies <c>project</c> or <c>fluid</c>.</summary>
    Static,

    /// <summary>Component spacing on the canvas, in world units (<c>D-37</c>).</summary>
    Spacing,

    /// <summary>Presentation for the statements that follow.</summary>
    Style,

    /// <summary>Selects the fluid properties the canvas colours by.</summary>
    Show,

    /// <summary>Binds a name to an expression.</summary>
    Let,

    /// <summary>Selects the catalogue auto-sizing draws from.</summary>
    Catalog,

    /// <summary>Begins a circuit's topology.</summary>
    Connections,

    /// <summary>Begins a circuit's transient disturbances.</summary>
    Schedule,

    /// <summary>Where a subcircuit takes flow from its parent (<c>D-33</c>).</summary>
    Supply,

    /// <summary>Where a subcircuit returns flow to its parent (<c>D-33</c>).</summary>
    Return,

    /// <summary>Binds a controller to what it actuates and what it measures (<c>D-40</c>).</summary>
    Control,

    /// <summary>Declares a named interpolated table and opens its section (<c>D-57</c>).</summary>
    Curve,

    /// <summary>Gives a curve driver its sizing, and static-solve operating, value (<c>D-58</c>).</summary>
    Design,
}
