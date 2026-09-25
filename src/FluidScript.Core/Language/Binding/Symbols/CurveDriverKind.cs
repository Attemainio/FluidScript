namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>What a curve's second position turned out to name.</summary>
public enum CurveDriverKind
{
    /// <summary>Nothing resolved it. The header omitted a driver, or it named nothing (<c>FS1527</c>).</summary>
    Unresolved = 0,

    /// <summary>The clock. Every <c>x</c> in the table is a timestamp or a count of Unix seconds.</summary>
    Time,

    /// <summary>Another curve, whose <c>y</c> is this curve's <c>x</c>.</summary>
    Curve,

    /// <summary>A registered driver, supplied by <c>design</c> (<c>D-59</c>).</summary>
    Role,

    /// <summary>An unregistered name a <c>design</c> line gives a value to.</summary>
    /// <remarks>
    /// <c>D-59</c> is explicit that a plant is full of drivers nobody registered. A name with a design
    /// value behind it is one of those, and works; a name with nothing behind it anywhere is
    /// <c>FS1527</c>.
    /// </remarks>
    DesignOnly,

    /// <summary>A <c>let</c>, read at its value in each case (<c>D-167</c>). Language 2 only.</summary>
    Let,
}
