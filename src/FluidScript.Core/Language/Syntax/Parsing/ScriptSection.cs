namespace FluidScript.Core.Language.Syntax.Parsing;

/// <summary>Which of a circuit's three sections a statement sits in.</summary>
/// <remarks>
/// Scoped to a circuit rather than to the file (<c>D-52</c>): a <c>circuit</c> header ends whatever
/// section the previous circuit was in and opens the new circuit's declaration section.
/// </remarks>
public enum ScriptSection
{
    /// <summary>Before any section marker: directives, bindings and declarations.</summary>
    Declaration = 1,

    /// <summary>After a <c>connections</c> line.</summary>
    Connections,

    /// <summary>After a <c>schedule</c> line.</summary>
    Schedule,

    /// <summary>
    /// After a <c>curve</c> line, holding that curve's rows and nothing else.
    /// </summary>
    /// <remarks>
    /// The one section that is <strong>file-wide</strong> rather than circuit-scoped: a curve is shared
    /// by every circuit that reads it, so <c>D-52</c> does not apply and it is declared with the other
    /// file-wide statements, before the first <c>circuit</c>. It is ended by the next <c>curve</c>
    /// header or the first <c>circuit</c>, and nothing else closes it.
    /// </remarks>
    Curve,
}
