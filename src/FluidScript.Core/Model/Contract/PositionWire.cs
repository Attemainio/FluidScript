namespace FluidScript.Core.Model.Contract;

/// <summary>A line and column, both zero-based; the column counts UTF-16 code units.</summary>
/// <param name="Line">The line.</param>
/// <param name="Character">The column.</param>
public sealed record PositionWire(int Line, int Character);
