namespace FluidScript.Core.Language.Syntax.Ast;

/// <summary>The lexical shape of one <c>style</c> token.</summary>
/// <remarks>
/// Shape only. Which category a token belongs to — colour, width, corner treatment, line pattern —
/// needs the colour and corner registries and is decided at bind time, along with <c>FS1201</c> and
/// <c>FS1202</c>.
/// </remarks>
public enum StyleTokenKind
{
    /// <summary>A bare word: a colour name, or a corner keyword.</summary>
    Word = 1,

    /// <summary>A number with no unit.</summary>
    Number,

    /// <summary>A number with a unit, such as a stroke width in <c>px</c>.</summary>
    Quantity,

    /// <summary>Quoted text, which is how a hex colour is written (<c>D-13</c>).</summary>
    Quoted,
    /// <summary>A line pattern: <c>-</c>, <c>--</c>, <c>..</c> or <c>-.</c>.</summary>
    /// <remarks>Recombined here from the two tokens the lexer produced. It is the one place the
    /// grammar is not context-free, and it is contained to this production deliberately.</remarks>
    Pattern,

    /// <summary>A keyed token, <c>fill=&quot;#e8f1f8&quot;</c>: the key, the <c>=</c>, and the value (<c>D-104</c>).</summary>
    Keyed,
}
