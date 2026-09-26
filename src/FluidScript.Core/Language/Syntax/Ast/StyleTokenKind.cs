namespace FluidScript.Core.Language.Syntax.Ast;

/// <summary>The shape of one style setting's value, as the reader hands it to the classifier.</summary>
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

    /// <summary>A line pattern: <c>-</c>, <c>--</c>, <c>..</c> or <c>-.</c>, made from a <c>line</c> setting's word.</summary>
    Pattern,
}
