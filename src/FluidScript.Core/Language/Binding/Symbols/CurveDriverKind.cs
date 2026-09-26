namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>What a curve's second position turned out to name.</summary>
public enum CurveDriverKind
{
    /// <summary>Nothing resolved it: the header omitted a driver, or named neither a <c>let</c> nor <c>time</c> (<c>FS1811</c>).</summary>
    Unresolved = 0,

    /// <summary>The clock. Every <c>x</c> in the table is a timestamp or a count of Unix seconds.</summary>
    Time,

    /// <summary>Another curve, whose <c>y</c> is this curve's <c>x</c>: a run's clocked override of a driver (<c>D-169</c>).</summary>
    Curve,

    /// <summary>A <c>let</c>, read at its value in each case (<c>D-167</c>).</summary>
    Let,
}
