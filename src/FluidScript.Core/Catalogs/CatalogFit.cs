namespace FluidScript.Core.Catalogs;

/// <summary>How well the chosen entry matched what was asked for.</summary>
/// <remarks>
/// The catalogue reports the fit and stops there. <c>FS2601</c> and <c>FS2602</c> name the
/// <em>component</em> that got clamped, and a catalogue that knew the caller's name would be a
/// catalogue that had to be told it — so the sizing loop builds those diagnostics (<c>P3.7</c>).
/// </remarks>
public enum CatalogFit
{
    /// <summary>An entry satisfied the request with something smaller available below it.</summary>
    Exact,

    /// <summary>Nothing satisfied the request; the largest entry was taken instead.</summary>
    ClampedToLargest,

    /// <summary>The smallest entry already satisfied the request, so the ideal size is below the series.</summary>
    ClampedToSmallest,
}
