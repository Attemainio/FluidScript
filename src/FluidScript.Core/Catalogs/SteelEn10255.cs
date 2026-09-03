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

    /// <summary>The public sources these rows were read from, and why they are still not verified.</summary>
    /// <remarks>
    /// <para>
    /// Declared above <see cref="Instance"/> and not below it: static initializers run in textual
    /// order, so a source list written after the catalogue that reads it is silently null in every row.
    /// </para>
    /// <para>
    /// Shared by every row because every row was read from the same four documents. Two cover the
    /// outside diameters and two the medium-series wall thicknesses, which is the two-source rule
    /// satisfied -- and the split is not tidiness: <strong>no single source covered both correctly.</strong>
    /// </para>
    /// <para>
    /// <strong><see cref="Provenance.Verified"/> stays false, and the reason is an open question rather
    /// than a missing signature.</strong> Every supplier table found lists DN15 at 21.7 mm and DN25 at
    /// 34.2 mm, which are EN 10255's <em>upper tolerance limits</em>; the 21.3 and 33.7 used here are
    /// EN 10220's Series 1 preferred diameters, which is also what <c>27</c>'s worked example computes
    /// from. The two differ by about 2 % in bore and 5 % in area, so the choice is a hydraulic decision
    /// and belongs to a person. Recorded as <c>C-35</c>.
    /// </para>
    /// </remarks>
    private static Provenance Sources { get; } = new()
    {
        Standard = "EN 10255",
        Sources =
        [
            // EN 10220 Series 1 preferred outside diameters. These two agree on the whole sequence, and
            // both run 139.7 -> 168.3 with no 165.1 in it, which is how the DN150 row below is known to
            // be EN 10255's diameter rather than EN 10220's (C-36).
            new SourceReference(
                "Botop Steel Pipes",
                "https://www.botopsteelpipes.com/steel-pipe-weight-chart-en-10220/",
                new DateOnly(2026, 9, 3)),
            new SourceReference(
                "Eastern Steel Manufacturing Co., Ltd",
                "https://www.eastern-steels.com/newsdetail/din-en10220-seamless-steel-pipes.html",
                new DateOnly(2026, 9, 3)),

            // EN 10255 medium-series wall thicknesses, and the inch column that pins the DN mapping --
            // half-inch is DN15 and six-inch is DN150. Both agree with every wall thickness below.
            new SourceReference(
                "Durgapur Tubes Pvt Ltd",
                "https://durgapurtubes.com/en-10255.html",
                new DateOnly(2026, 9, 3)),
            new SourceReference(
                "Union Steel Industry Co., Ltd",
                "https://www.union-steels.com/standards/en-10255.html",
                new DateOnly(2026, 9, 3)),
        ],

        // A person decides C-35 first. Sources without the attestation is a row nobody has checked.
        Verified = false,
    };

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

            Provenance = Sources,
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
