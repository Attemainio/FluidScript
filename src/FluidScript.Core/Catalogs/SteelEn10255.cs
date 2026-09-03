using System.Globalization;

namespace FluidScript.Core.Catalogs;

/// <summary>Medium-series non-alloy steel tube, the common European hydronic default.</summary>
/// <remarks>
/// <para>
/// <strong>These rows are not yet verified and this catalogue will not size anything.</strong>
/// <c>27</c>'s invariant 1 requires two independent public sources per row and a person's attestation
/// that they agree; the dimensions below were authored from engineering knowledge of the series and
/// carry neither. <see cref="Catalog{TSpec}.Validate"/> therefore reports <c>FS2605</c> and resolution
/// refuses them, which is the designed behaviour rather than a defect. Filling in
/// <c>src/FluidScript.Core/Catalogs/SOURCES.md</c> and flipping <c>Verified</c> is a one-time,
/// human-reviewed task, and a test asserts the refusal until it is done.
/// </para>
/// <para>
/// <strong>The range stops at DN150 because the standard does.</strong> <c>27</c>'s open-questions
/// section promises DN15-DN300 from this series, and EN 10255 does not extend past DN150; anything
/// above it is a second series and a second set of sources. Recorded as <c>C-31</c>.
/// </para>
/// <para>
/// Nothing here reproduces a standard's table. The dimensions are facts about physical objects, the
/// standard is cited by number as the authority a row conforms to, and the arrangement is this file's.
/// </para>
/// </remarks>
public static class SteelEn10255
{
    /// <summary>The catalogue id a script pins.</summary>
    public const string Id = "steel_en10255";

    /// <summary>Absolute roughness of commercial steel.</summary>
    /// <value>m. 0.045 mm, matching <see cref="Components.Pipe"/>'s default.</value>
    public const double Roughness = 45e-6;

    private const string Series = "steel, EN 10255 medium";

    /// <summary>Outside diameter and wall thickness per DN, millimetres.</summary>
    /// <remarks>
    /// Millimetres here and metres in <see cref="PipeSpec"/>: these are the numbers a manufacturer's
    /// catalogue prints, and transcribing them in the unit they were read in is what makes a row
    /// checkable against its source by eye.
    /// </remarks>
    private static readonly (int Dn, double OdMm, double WallMm)[] Rows =
    [
        (15, 21.3, 2.6),
        (20, 26.9, 2.6),
        (25, 33.7, 3.2),
        (32, 42.4, 3.2),
        (40, 48.3, 3.2),
        (50, 60.3, 3.6),
        (65, 76.1, 3.6),
        (80, 88.9, 4.0),
        (100, 114.3, 4.5),
        (125, 139.7, 5.0),
        (150, 165.1, 5.0),
    ];

    /// <summary>The catalogue, ascending by nominal size.</summary>
    public static ICatalog<PipeSpec> Instance { get; } = new Catalog<PipeSpec>(
        Id,
        "2026.1",
        "EN 10255",
        Rows.Select(static row => new CatalogEntry<PipeSpec>
        {
            Designation = "DN" + row.Dn.ToString(CultureInfo.InvariantCulture),
            Spec = new PipeSpec
            {
                NominalDiameter = row.Dn,
                OutsideDiameter = row.OdMm / 1000,
                WallThickness = row.WallMm / 1000,
                Roughness = Roughness,
                Series = Series,
            },

            // Awaiting the one-time human retrieval described above. Two sources and Verified = true
            // arrive together or not at all: either alone is a row nobody has actually checked.
            Provenance = new Provenance { Standard = "EN 10255", Verified = false },
        }),
        Plausible,
        static spec => spec.OutsideDiameter);

    /// <summary>What is wrong with one row, or <see langword="null"/>.</summary>
    /// <param name="spec">The row to check.</param>
    /// <returns>The fault, phrased to complete "'DN25' ...".</returns>
    /// <remarks>
    /// <c>27</c>'s invariant 7, and the cheapest guard there is against the realistic failure mode of
    /// hand-curated data. A wall thickness transcribed as 32 instead of 3.2 gives a negative bore,
    /// which is caught here; one transcribed as 3.6 instead of 3.2 gives a plausible bore, which is
    /// what the two-source rule is for.
    /// </remarks>
    private static string? Plausible(PipeSpec spec) => spec switch
    {
        { WallThickness: <= 0 } => "has a non-positive wall thickness",
        { Roughness: <= 0 } => "has a non-positive roughness",
        _ when spec.OutsideDiameter <= 2 * spec.WallThickness => "has no bore left after its wall",
        _ => null,
    };
}
