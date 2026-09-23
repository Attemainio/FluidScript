namespace FluidScript.Core.Model.Contract;

/// <summary>The declared label metric (<c>D-73</c>).</summary>
/// <param name="Size">The label's height, world units: the canvas label's size at one world unit's pixels.</param>
/// <param name="Advance">The advance per character, in em; a label's width is <c>Advance × characters × Size</c>.</param>
public sealed record LabelMetricWire(double Size, double Advance);
