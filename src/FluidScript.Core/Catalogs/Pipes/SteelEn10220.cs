namespace FluidScript.Core.Catalogs.Pipes;

/// <summary>Welded steel tube on EN 10220's Series 1 diameters, the walls a Finnish wholesaler stocks for heating pipe.</summary>
/// <remarks>
/// <para>
/// <strong>A different pipe from <see cref="SteelEn10255"/>, and the reason it exists.</strong> EN 10255 is
/// the threadable medium series and stops at DN150, where its tube is 165.1 mm; EN 10220's Series 1 is
/// the preferred-diameter sequence every fitting is made to, 168.3 at DN150 and on up to DN300 here
/// (<c>D-67</c>, <c>C-36</c>, <c>C-110</c>). Welded tube to EN 10217-1 in grade P235TR1 is what the
/// market sells on these diameters for heating and cooling mains, and the walls below are the ones two
/// wholesalers both stock, chosen the way <c>D-129</c> chose copper's: the tube an installer buys, not
/// a wall series a standard merely permits. EN 10220 offers a dozen walls per diameter and settles
/// nothing; it is cited as the authority the diameters conform to.
/// </para>
/// <para>
/// <strong>Verified 2026-09-21.</strong> Two independent public sources agree on every diameter and
/// every wall (the wholesalers' own listings), two more on the DN designation each diameter carries,
/// and the attestation is recorded in <c>src/FluidScript.Core/Catalogs/SOURCES.md</c>. One source gave
/// DN250 as 273.1 mm; the three others and every listing say 273.0, which is what ships.
/// </para>
/// <para>
/// Nothing here reproduces a standard's table. The dimensions are facts about physical objects, the
/// standard is cited by number as the authority a row conforms to, and the arrangement is this file's.
/// </para>
/// </remarks>
public static class SteelEn10220
{
    /// <summary>The catalogue id a script pins.</summary>
    public const string Id = "steel_en10220";

    /// <summary>Absolute roughness of commercial steel: the same tube surface as <see cref="SteelEn10255"/>, the same basis.</summary>
    public static MaterialRoughness RoughnessBasis => SteelEn10255.RoughnessBasis;

    /// <summary>Absolute roughness of commercial steel.</summary>
    /// <value>m. 0.045 mm, matching <see cref="Components.PipeComponent"/>'s default.</value>
    public static double Roughness => RoughnessBasis.Value;

    private const string Series = "steel, EN 10220 series 1, welded EN 10217-1 stock walls";

    private static readonly DateOnly Retrieved = new(2026, 9, 21);

    /// <summary>Outside diameter and wall thickness per DN, millimetres, as the wholesalers print them.</summary>
    private static readonly (int Dn, double OdMm, double WallMm)[] Rows =
    [
        (15, 21.3, 2.0),
        (20, 26.9, 2.3),
        (25, 33.7, 2.6),
        (32, 42.4, 2.6),
        (40, 48.3, 2.6),
        (50, 60.3, 2.9),
        (65, 76.1, 2.9),
        (80, 88.9, 3.2),
        (100, 114.3, 3.6),
        (125, 139.7, 4.0),
        (150, 168.3, 4.5),
        (200, 219.1, 4.5),
        (250, 273.0, 5.0),
        (300, 323.9, 5.6),
    ];

    /// <summary>The public sources these rows were read from.</summary>
    /// <remarks>
    /// <para>
    /// Declared above <see cref="Instance"/>: static initialisers run in textual order, and a source
    /// list written after the catalogue that reads it is silently null in every row.
    /// </para>
    /// <para>
    /// The first two carry every diameter and wall as stocked items, and are the two-source rule for
    /// the dimensions; the last two carry the DN each diameter is sold under, which a wholesaler's
    /// listing does not print. At DN150 and DN200 both wholesalers stock two walls (168.3 × 4.0 and
    /// 4.5, 219.1 × 4.5 and 6.3); the row takes the one both list as painted heating tube.
    /// </para>
    /// </remarks>
    private static Provenance Sources { get; } = new()
    {
        Standard = "EN 10220",
        Sources =
        [
            // Every row as a stocked item, welded EN 10217-1 P235TR1, painted for heating.
            new SourceReference("Dahl Suomi Oy", "https://www.dahl.fi/tuoteryhma/hitsatut-putket-lv/", Retrieved),
            new SourceReference(
                "Onninen Oy",
                "https://www.onninen.fi/lampo-ja-vesi-seka-prosessiputkistot/hitsatut-hiiliterasputket/c/7",
                Retrieved),

            // The DN each Series 1 diameter carries, DN15 to DN300, from two tables that print both.
            new SourceReference(
                "Eastern Steel Manufacturing Co., Ltd",
                "https://www.eastern-steels.com/newsdetail/din-en10220-seamless-steel-pipes.html",
                Retrieved),
            new SourceReference(
                "Pipe Flow Calculations",
                "https://www.pipeflowcalculations.com/tables/en10220-DN15.xhtml",
                Retrieved),
        ],

        // Attested 2026-09-21: every diameter and wall is a stocked item at both wholesalers, and every
        // DN designation is printed by both tables. `Roughness` is not covered by this flag; it is
        // steel's textbook value for new tube and C-37 still owns its condition.
        Verified = true,
    };

    /// <summary>The catalogue, ascending by nominal size.</summary>
    public static ICatalog<PipeSpec> Instance { get; } =
        PipeCatalogBuilder.Build(Id, "2026.1", "EN 10220", Series, Roughness, Rows, Sources);
}
