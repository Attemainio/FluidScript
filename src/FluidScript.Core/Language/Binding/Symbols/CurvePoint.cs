namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One row of a curve: an <c>x</c> and the <c>y</c> it maps to.</summary>
/// <param name="X">
/// The driver's value. Bare, and in the driver role's canonical unit where it has one; Unix seconds
/// for a curve of <c>time</c>.
/// </param>
/// <param name="Y">The value the curve yields there. Always bare — no curve has a dimension.</param>
public readonly record struct CurvePoint(double X, double Y);
