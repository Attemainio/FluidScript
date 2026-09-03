using System.Globalization;

namespace FluidScript.Core.Catalogs;

/// <summary>Copper tube for water, heating and gas -- EN 1057, the Table X wall series.</summary>
/// <remarks>
/// <para>
/// <strong>These rows are not verified and the loader refuses them.</strong> Copper turned out to be
/// materially harder to source than steel, and for a structural reason worth recording: EN 1057
/// permits <em>several wall thicknesses per outside diameter</em> -- the Y, X and Z series -- and the
/// market ships more than one of them. A 15 mm tube is 15 x 0.7 in the UK Table X range and 15 x 1.0
/// in several continental ranges, which is 13.6 mm of bore against 13.0: about 9 % in flow area. The
/// best public tables found were copies of the standard itself, which this project does not use.
/// Recorded as <c>C-38</c>; <c>Catalogs/SOURCES.md</c> carries the checklist.
/// </para>
/// <para>
/// <strong>The number in <c>dn</c> means something different here, and that is the trap.</strong>
/// Copper tube is designated by its <em>outside diameter</em>: <c>dn=22</c> is a 22 mm tube with a
/// 20.2 mm bore. Steel's <c>dn=25</c> is a designation whose bore is 27.3 mm -- larger than the
/// number. So <c>dn=15</c> is a 16.1 mm bore in steel and a 13.6 mm bore in copper: the same script
/// value, a 24 % difference in bore, and a factor of two in pressure gradient. <see cref="PipeSpec"/>
/// carries <see cref="DesignationBasis"/> so the difference is stated rather than implied.
/// </para>
/// </remarks>
public static class CopperEn1057
{
    /// <summary>The catalogue id a script pins.</summary>
    public const string Id = "copper_en1057";

    private const string Series = "copper, EN 1057 table X";

    /// <summary>Absolute roughness of drawn copper tube.</summary>
    /// <remarks>
    /// Thirty times smoother than commercial steel, which is not a rounding difference: at the same
    /// bore and flow it is a materially lower friction factor, and a circuit modelled in the wrong
    /// material is wrong in the direction the material is chosen for.
    /// </remarks>
    public static MaterialRoughness RoughnessBasis { get; } = new(
        1.5e-6,
        "drawn copper",
        "new",
        "Moody (1944); Colebrook (1939); Crane Technical Paper 410",
        [
            new SourceReference(
                "SimuPipe", "https://simupipe.com/resources/pipe-roughness", new DateOnly(2026, 9, 3)),
        ]);

    /// <summary>Outside diameter and wall thickness, millimetres, for the Table X series.</summary>
    private static readonly (double OdMm, double WallMm)[] Rows =
    [
        (15, 0.7),
        (22, 0.9),
        (28, 0.9),
        (35, 1.2),
        (42, 1.2),
        (54, 1.2),
        (76.1, 1.5),
        (108, 1.5),
    ];

    /// <summary>Provenance for a row nobody has checked yet.</summary>
    private static Provenance Unverified { get; } = new()
    {
        Standard = "EN 1057",
        Sources = [],
        Verified = false,
    };

    /// <summary>The catalogue, ascending by outside diameter.</summary>
    public static ICatalog<PipeSpec> Instance { get; } = new Catalog<PipeSpec>(
        Id,
        "2026.1",
        "EN 1057",
        Rows.Select(static row => new CatalogEntry<PipeSpec>
        {
            // The designation a merchant and a script both use is the outside diameter itself.
            Designation = row.OdMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm",
            Spec = new PipeSpec
            {
                NominalDiameter = (int)row.OdMm,
                DesignationBasis = DesignationBasis.OutsideDiameter,
                OutsideDiameter = row.OdMm / 1000,
                WallThickness = row.WallMm / 1000,
                Roughness = RoughnessBasis.Value,
                Series = Series,
            },
            Provenance = Unverified,
        }),
        PipeSpec.Fault,
        static spec => spec.OutsideDiameter);
}
