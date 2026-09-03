namespace FluidScript.Core.Catalogs;

/// <summary>The dimensions of one nominal pipe size in one series.</summary>
/// <remarks>
/// <para>
/// Every length here is metres, matching the rest of Core. <c>27</c> writes these fields as
/// <c>Quantity</c> and derives the bore as <c>OutsideDiameter - 2 * WallThickness</c>, which does not
/// compile: <c>Quantity</c> has no arithmetic operators, only <c>TryAdd</c>/<c>TrySubtract</c>, because
/// unit arithmetic can fail and a silent operator would hide it. Plain metres also matches
/// <see cref="Components.Pipe"/>, which is the only consumer.
/// </para>
/// <para>
/// <strong><see cref="InsideDiameter"/> is computed, never stored.</strong> A record carrying all
/// three independently will eventually hold a row where they disagree, and nothing downstream can tell
/// which of the three was the wrong one.
/// </para>
/// </remarks>
public sealed record PipeSpec
{
    /// <summary>The nominal diameter designation, dimensionless by definition.</summary>
    /// <value>
    /// The DN number: 25 for DN25. <strong>Not a length.</strong> DN25 steel pipe is 33.7 mm outside
    /// and 27.3 mm bore, so an area computed from 25 mm is 16 % small and the pressure gradient roughly
    /// a factor of two out. Nothing hydraulic may read this field; read <see cref="InsideDiameter"/>.
    /// </value>
    public required int NominalDiameter { get; init; }

    /// <summary>Outside diameter.</summary>
    /// <value>m.</value>
    public required double OutsideDiameter { get; init; }

    /// <summary>Wall thickness.</summary>
    /// <value>m.</value>
    public required double WallThickness { get; init; }

    /// <summary>Absolute roughness of this material.</summary>
    /// <value>m.</value>
    public required double Roughness { get; init; }

    /// <summary>Material and series, for a basis string a user can read.</summary>
    /// <value><c>"steel, EN 10255 medium"</c>.</value>
    public required string Series { get; init; }

    /// <summary>Inside diameter — the hydraulically relevant one.</summary>
    /// <value>m. Derived as OD − 2·wall, so the three can never disagree.</value>
    public double InsideDiameter => OutsideDiameter - (2 * WallThickness);
}
