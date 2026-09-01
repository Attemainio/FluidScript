namespace FluidScript.Core.Syntax;

/// <summary>A position in a script expressed as a line and an offset within that line.</summary>
/// <param name="Line">The zero-based line index.</param>
/// <param name="Character">The zero-based offset of the character within its line.</param>
/// <remarks>
/// Zero-based on both axes, matching the editor protocols the frontend speaks. Anything that shows a
/// position to a person adds one to each; that conversion belongs at the boundary that renders it, and
/// stating the convention here is what keeps it from being applied twice.
/// </remarks>
public readonly record struct LinePosition(int Line, int Character);
