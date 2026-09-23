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
    /// <summary>What the number a script writes in <c>dn</c> means for this series.</summary>
    /// <remarks>
    /// <strong>It is not the same thing in every catalogue, and the difference is large.</strong>
    /// Steel's <c>dn=15</c> is a designation whose bore is 16.1 mm; copper's <c>dn=15</c> is a 15 mm
    /// tube whose bore is 13.6 mm. The same number in the same script, a 24 % difference in bore and
    /// about a factor of two in pressure gradient. Stating the basis per series is what stops that
    /// being something a reader has to already know.
    /// </remarks>
    public required DesignationBasis DesignationBasis { get; init; }

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

    /// <summary>What is wrong with one row, or <see langword="null"/>.</summary>
    /// <param name="spec">The row to check.</param>
    /// <returns>The fault, phrased to complete "'DN25' …".</returns>
    /// <remarks>
    /// <c>27</c>'s invariant 7, and the cheapest guard against the realistic failure mode of
    /// hand-curated data. A wall transcribed as 32 instead of 3.2 leaves no bore and is caught here;
    /// one transcribed as 3.6 instead of 3.2 gives a plausible bore, is caught by nothing but a second
    /// source, and moves the pump head by several percent. Two checks, two different mistakes, and
    /// neither substitutes for the other.
    /// </remarks>
    public static string? Fault(PipeSpec spec) => spec switch
    {
        null => "is absent",
        { WallThickness: <= 0 } => "has a non-positive wall thickness",
        { Roughness: <= 0 } => "has a non-positive roughness",
        _ when spec.OutsideDiameter <= 2 * spec.WallThickness => "has no bore left after its wall",
        _ => null,
    };
}

/// <summary>What the number a script writes in <c>dn</c> designates.</summary>
public enum DesignationBasis
{
    /// <summary>A nominal size: a label, not a length. Steel's DN25 has a 27.3 mm bore.</summary>
    NominalSize,

    /// <summary>The outside diameter in millimetres. Copper's 22 mm tube has a 20.2 mm bore.</summary>
    OutsideDiameter,
}
