namespace FluidScript.Core.Language;

/// <summary>An inclusive range of plausible values.</summary>
/// <typeparam name="T">The value type being bounded.</typeparam>
/// <param name="Min">The lowest plausible value, inclusive.</param>
/// <param name="Max">The highest plausible value, inclusive.</param>
/// <remarks>
/// Plausibility, not validity. A parameter outside its range binds and solves; it produces
/// <c>FS1306</c> so the user sees that a pipe 45 kilometres long is probably 45 metres with the wrong
/// unit. Bounds are in SI, like everything else held internally.
/// </remarks>
public readonly record struct Range<T>(T Min, T Max)
    where T : notnull, IComparable<T>
{
    /// <summary>Tells whether a value falls inside the range.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is between the bounds, inclusive.</returns>
    public bool Contains(T value) =>
        Min.CompareTo(value) <= 0 && Max.CompareTo(value) >= 0;
}
