namespace FluidScript.Core.Catalogs.Pipes;

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
/// number. So <c>dn=15</c> is a 16.1 mm bore in steel and a 13.0 mm bore in copper: the same script
/// value, a 19 % difference in bore, and a factor of two in pressure gradient. <see cref="PipeSpec"/>
/// carries <see cref="DesignationBasis"/> so the difference is stated rather than implied.
/// </para>
/// </remarks>
public static class CopperEn1057
{
    /// <summary>The catalogue id a script pins.</summary>
    public const string Id = "copper_en1057";

    private const string Series = "copper, EN 1057, Finnish type-approved range";

    /// <summary>Gets the roughness the rows carry, with its literature basis (<c>D-68</c>).</summary>
    public static MaterialRoughness RoughnessBasis { get; } = new(
        1.5e-6,
        "drawn copper",
        "new",
        "Moody (1944); Colebrook (1939); Crane Technical Paper 410",
        [
            new SourceReference(
                "SimuPipe", "https://simupipe.com/resources/pipe-roughness", new DateOnly(2026, 9, 3)),
        ]);

    private static readonly DateOnly Retrieved = new(2026, 9, 21);

    // The range a Finnish wholesaler stocks (D-128, C-38): EN 1057 leaves the wall to national type
    // approval, and the Finnish approved range (Cupori 110 Premium, the market's plain tube) runs
    // 1.0 mm to 22, 1.2 at 28, 1.5 from 35 to 54, 2.0 at 88.9 and 2.5 at 108 -- not the UK Table X
    // walls the rows once carried, nor the German 28 x 1.5 / 54 x 2.0. Each row cites two
    // independent public listings stating the outside diameter and either the wall or the bore; a
    // merchant that prints a rounded bore (K-Rauta's "28x25") is not used for that row. 64 and 76.1
    // are in the standard and in KME's German range but in no Finnish listing found, so they are not
    // rows: a size the sizer would choose has to be one the market sells.
    private static readonly (int Dn, double OdMm, double WallMm, string Designation, SourceReference[] Sources)[] Rows =
    [
        (12, 12, 1.0, "12 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-premium-110-12x3000"),
            Source("Jukira Oy", "https://jukira.fi/tuoteryhma/08-putket-komposiitti-kupari-muovi-teras-ja-valurauta/kupariputket-suorat-kiepit-muovipinnoitetut-kromatut-ja-valkoiset/"),
        ]),
        (15, 15, 1.0, "15 mm",
        [
            Source("LVI-Tarvikkeet", "https://www.lvitarvikkeet.fi/tuotteet.html?id=51709/704731"),
            Source("Jukira Oy", "https://jukira.fi/tuoteryhma/08-putket-komposiitti-kupari-muovi-teras-ja-valurauta/kupariputket-suorat-kiepit-muovipinnoitetut-kromatut-ja-valkoiset/"),
        ]),
        (18, 18, 1.0, "18 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-110-premium-18x3000"),
            Source("Jukira Oy", "https://jukira.fi/tuoteryhma/08-putket-komposiitti-kupari-muovi-teras-ja-valurauta/kupariputket-suorat-kiepit-muovipinnoitetut-kromatut-ja-valkoiset/"),
        ]),
        (22, 22, 1.0, "22 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-110-premium-22x20-mm-5-m"),
            Source("Jukira Oy", "https://jukira.fi/tuoteryhma/08-putket-komposiitti-kupari-muovi-teras-ja-valurauta/kupariputket-suorat-kiepit-muovipinnoitetut-kromatut-ja-valkoiset/"),
        ]),
        (28, 28, 1.2, "28 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-110-premium-28x25-6-mm-5-m"),
            Source("Jukira Oy", "https://jukira.fi/tuoteryhma/08-putket-komposiitti-kupari-muovi-teras-ja-valurauta/kupariputket-suorat-kiepit-muovipinnoitetut-kromatut-ja-valkoiset/"),
        ]),
        (35, 35, 1.5, "35 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-110-premium-35x32-mm-5-m"),
            Source("Cronvall", "https://cronvall.fi/variant/10175"),
        ]),
        (42, 42, 1.5, "42 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-110-premium-42x39-mm-5-m"),
            Source("Onninen", "https://www.onninen.fi/cupori-kupariputki-kova-cupori-110-42x39-l-3m-premium/p/AMM025"),
        ]),
        (54, 54, 1.5, "54 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-110-premium-54x3000"),
            Source("Onninen", "https://www.onninen.fi/cupori-kupariputki-kova-cupori-110-54x51-l-3m-premium/p/AMM026"),
        ]),
        (89, 88.9, 2.0, "88.9 mm",
        [
            Source("Taloon.com", "https://www.taloon.com/kupariputki-cupori-110-premium-88-9x84-9-mm-5-m"),
            Source("KME Germany GmbH & Co. KG, Product Data Sheet Plumbing Tubes 2017", "https://www.kme.com/fileadmin/user_upload/Product_Data_Sheet_Plumbing_Tubes_2017_EN.pdf"),
        ]),
        (108, 108, 2.5, "108 mm",
        [
            Source("Onninen", "https://www.onninen.fi/cupori-kupariputki-kova-cupori-110-108x103-l-5m-premium/p/AHB217"),
            Source("KME Germany GmbH & Co. KG, Product Data Sheet Plumbing Tubes 2017", "https://www.kme.com/fileadmin/user_upload/Product_Data_Sheet_Plumbing_Tubes_2017_EN.pdf"),
        ]),
    ];

    private static SourceReference Source(string publisher, string url) => new(publisher, url, Retrieved);

    /// <summary>Gets the shipped copper catalogue: the Finnish type-approved range, verified 2026-09-21 (<c>D-128</c>).</summary>
    public static ICatalog<PipeSpec> Instance { get; } = new Catalog<PipeSpec>(
        Id,
        "2026.1",
        "EN 1057",
        Rows.Select(static row => new CatalogEntry<PipeSpec>
        {
            // The designation a merchant and a script both use is the outside diameter itself; the
            // one non-integer size, 88.9, is `dn=89`, which is how Finnish listings print it.
            Designation = row.Designation,
            Spec = new PipeSpec
            {
                NominalDiameter = row.Dn,
                DesignationBasis = DesignationBasis.OutsideDiameter,
                OutsideDiameter = row.OdMm / 1000,
                WallThickness = row.WallMm / 1000,
                Roughness = RoughnessBasis.Value,
                Series = Series,
            },
            Provenance = new Provenance
            {
                Standard = "EN 1057",
                Sources = [.. row.Sources],

                // Attested 2026-09-21 (D-128): two independent Finnish listings, or one and KME's
                // published range, agree on the outside diameter and the wall of every row.
                Verified = true,
            },
        }),
        PipeSpec.Fault,
        static spec => spec.OutsideDiameter);
}
