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

    /// <summary>Absolute roughness of commercial steel, and the condition it describes.</summary>
    /// <remarks>
    /// Declared above <see cref="Instance"/> for the same reason <c>Sources</c> is: static
    /// initialisers run in textual order, and a basis written after the catalogue that reads it is
    /// null in every row.
    /// </remarks>
    public static MaterialRoughness RoughnessBasis { get; } = new(
        45e-6,
        "commercial steel",
        "new",
        "Moody (1944); Colebrook (1939); Crane Technical Paper 410",
        [
            new SourceReference(
                "SimuPipe", "https://simupipe.com/resources/pipe-roughness", new DateOnly(2026, 9, 3)),
            new SourceReference(
                "EngineerExcel", "https://engineerexcel.com/pipe-roughness/", new DateOnly(2026, 9, 3)),
        ]);

    /// <summary>Absolute roughness of commercial steel.</summary>
    /// <value>m. 0.045 mm, matching <see cref="Components.Pipe"/>'s default.</value>
    public static double Roughness => RoughnessBasis.Value;

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
    /// <strong>These are the nominal preferred diameters, which is <c>D-67</c>.</strong> Every supplier
    /// table lists DN15 at 21.7 mm and DN25 at 34.2 mm, which are EN 10255's <em>upper tolerance
    /// limits</em>; a catalogue that sized against those would quietly design every circuit for the
    /// most generous pipe the standard permits. The nominal diameter is the one a manufacturer aims at
    /// and the one <c>27</c>'s worked example computes from.
    /// </para>
    /// <para>
    /// <strong>DN150 was settled by arithmetic rather than by a table.</strong> EN 10220's Series 1
    /// runs 139.7 to 168.3 with no 165.1 in it, and one merchant's page states both 165.1 and 168.30
    /// for the same product. Its published mass does not: at 7850 kg/m3,
    /// <c>pi/4 * (165.1^2 - 155.1^2)</c> is 19.74 kg/m against the 19.7 stated, where 168.3 would give
    /// 20.14. A mass per metre is a third, independent constraint on a diameter and a wall together,
    /// and it is worth reaching for whenever two sources disagree (<c>C-36</c>).
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

            // A merchant's DN150 product listing, whose stated 19.7 kg/m is what discriminates 165.1
            // from 168.3 -- the one row the two families above disagreed about.
            new SourceReference(
                "Integraflow",
                "https://www.integraflow.co.uk/shop/cs-pip-150-med-gal-pln-dn150-6-nb-medium-wt-en10255-plain-end-galvanised-pipe-10199",
                new DateOnly(2026, 9, 3)),
        ],

        // Attested 2026-09-03. Every row has two independent agreeing sources for its diameter and two
        // for its wall, and DN150 additionally reproduces its published mass. What is NOT attested here
        // is `Roughness`: 0.045 mm is a textbook value for new commercial steel, appears in no pipe
        // standard, and ages -- see C-37, which this flag does not cover and must not be read as
        // covering.
        Verified = true,
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
                DesignationBasis = DesignationBasis.NominalSize,
                OutsideDiameter = row.OdMm / 1000,
                WallThickness = row.WallMm / 1000,
                Roughness = Roughness,
                Series = Series,
            },

            Provenance = Sources,
        }),
        PipeSpec.Fault,
        static spec => spec.OutsideDiameter);
}
