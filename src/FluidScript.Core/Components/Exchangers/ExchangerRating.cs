namespace FluidScript.Core.Components.Exchangers;

/// <summary>What an extended-mode exchanger transfers heat with: its size, its arrangement and, in Rated mode, the second side it works against.</summary>
/// <remarks>
/// <para>
/// Built by lowering from what the script stated and what sizing chose (<c>D-19</c>); a Duty exchanger
/// has none. Everything here is SI and fixed for one lowering -- the size is a sized or stated value,
/// and a Rated profile is a boundary condition -- so nothing in it is recomputed per iteration. What
/// <em>is</em> recomputed per iteration is the capacity rate of every solved side and therefore
/// <c>Cmin</c>, which is <see cref="HeatExchanger"/>'s business (<c>22</c>).
/// </para>
/// <para>
/// A conductance of zero means the size is not yet known: the bootstrap pass, or a design point too thin
/// to size from. The exchanger then transfers its stated duty and the outer loop says so.
/// </para>
/// </remarks>
public sealed record ExchangerRating
{
    /// <summary>Gets the mode the rating serves: <see cref="ExchangerMode.Rated"/> or <see cref="ExchangerMode.Coupled"/>.</summary>
    public required ExchangerMode Mode { get; init; }

    /// <summary>Gets how the two streams run relative to each other.</summary>
    public ExchangerArrangement Arrangement { get; init; } = ExchangerArrangement.Counter;

    /// <summary>Gets the overall conductance.</summary>
    /// <value>W/K. Zero when the size is not yet known, in which case the stated duty stands in.</value>
    public double Conductance { get; init; }

    /// <summary>Gets the temperature side 2 enters at, in Rated mode.</summary>
    /// <value>K, absolute. <see cref="double.NaN"/> in Coupled mode, where side 2 is read from its ports.</value>
    public double SecondaryInletTemperature { get; init; } = double.NaN;

    /// <summary>Gets side 2's capacity rate, in Rated mode.</summary>
    /// <value>W/K, <c>ṁ₂ · cp₂</c>. Zero in Coupled mode, where it is recomputed from the solved flow.</value>
    public double SecondaryCapacityRate { get; init; }

    /// <summary>Gets whether the rating can evaluate a duty: a size, and in Rated mode a complete profile.</summary>
    public bool CanRate =>
        Conductance > 0
        && (Mode == ExchangerMode.Coupled
            || (double.IsFinite(SecondaryInletTemperature) && SecondaryCapacityRate > 0));
}
