namespace FluidScript.Core.Units;

/// <summary>
/// Why an operation on two quantities could not produce a value.
/// </summary>
/// <remarks>
/// Arithmetic reports a reason rather than a diagnostic because it has no span: the expression
/// evaluator knows where the operands were written and turns a reason into the <c>FS13xx</c> code that
/// names it. Nothing here throws — a malformed expression is the normal state of a script being typed.
/// </remarks>
public enum QuantityError
{
    /// <summary>The operation produced a value.</summary>
    None,

    /// <summary>Two readings on an affine scale were added.</summary>
    /// <remarks>
    /// <c>20 °C + 30 °C</c>. Modelling temperature as one dimension makes this compile and produce
    /// 596.3 K, silently, which is the failure the whole affine split exists to prevent.
    /// </remarks>
    AbsoluteAddition,

    /// <summary>A reading was subtracted from a difference.</summary>
    /// <remarks>
    /// <c>30 dK − 20 °C</c>. The mirror case is legal — a reading minus a difference is a reading —
    /// because subtraction does not commute and a type system that pretended otherwise would produce
    /// something for both.
    /// </remarks>
    AbsoluteSubtractedFromDifference,

    /// <summary>The operands measure different things.</summary>
    /// <remarks><c>30 kW + 2 m</c>, or adding a pump head to a pipe length.</remarks>
    DimensionMismatch,

    /// <summary>A reading on an affine scale was scaled or negated.</summary>
    /// <remarks>
    /// Doubling 20 °C has no meaning, because the answer depends on where the scale's zero was put.
    /// The same is true of a gauge pressure, whose zero is the weather.
    /// </remarks>
    AffineOperand,

    /// <summary>A designation or defined coefficient was used in arithmetic.</summary>
    /// <remarks>DN50 is a name; twice DN50 is not DN100.</remarks>
    NominalOperand,

    /// <summary>The divisor was zero.</summary>
    DivisionByZero,
}
