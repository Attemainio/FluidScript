using FluidScript.Core.Catalogs;
using FluidScript.Core.Compatibility;
using FluidScript.Core.Components;

namespace FluidScript.Core.Tests.Catalogs;

/// <summary>
/// The catalogue from <c>plan/20-core-domain/27-component-catalog.md</c>: selection, validation,
/// provenance, and the bore that every hydraulic number downstream depends on.
/// </summary>
public sealed class CatalogTests
{
    /// <summary>Provenance a fixture row can carry, so verification is not what is under test.</summary>
    /// <remarks><c>example.invalid</c> is reserved precisely so a fixture cannot imply a real source.</remarks>
    private static Provenance Sourced => new()
    {
        Standard = "fixture",
        Sources =
        [
            new SourceReference("Fixture A", "https://a.example.invalid/", new DateOnly(2026, 1, 1)),
            new SourceReference("Fixture B", "https://b.example.invalid/", new DateOnly(2026, 1, 1)),
        ],
        Verified = true,
    };

    private static CatalogEntry<PipeSpec> Row(int dn, double odMm, double wallMm) => new()
    {
        Designation = "DN" + dn,
        Spec = new PipeSpec
        {
            NominalDiameter = dn,
            OutsideDiameter = odMm / 1000,
            WallThickness = wallMm / 1000,
            Roughness = 45e-6,
            Series = "fixture",
        },
        Provenance = Sourced,
    };

    private static Catalog<PipeSpec> Fixture(params CatalogEntry<PipeSpec>[] entries) => new(
        "fixture",
        "1.0",
        null,
        entries,
        static spec => spec.OutsideDiameter <= 2 * spec.WallThickness ? "has no bore left after its wall" : null,
        static spec => spec.OutsideDiameter);

    // ---- provenance -------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheShippedPipeCatalogueIsNotVerifiedYet()
    {
        // DELETE THIS TEST when Catalogs/SOURCES.md is filled in and the rows carry two sources each.
        // It exists so that "authored but unverified" cannot quietly become permanent: the dimensions
        // in SteelEn10255 were written from knowledge of the series, and 27's invariant 1 wants two
        // independent public sources and a person who checked them. Until then the catalogue refuses
        // to size anything, which is the designed behaviour and not a defect.
        var faults = SteelEn10255.Instance.Validate();

        var unverified = Assert.Single(faults, static fault => fault.Code == "FS2605");
        Assert.Contains("DN15", unverified.Message, StringComparison.Ordinal);

        Assert.False(PipeCatalogs.Resolve(pin: null).IsSuccess);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AVerifiedRowNeedsBothTheAttestationAndTheSources()
    {
        // Either alone is a row nobody actually checked. A tick with no sources is an unsupported
        // claim; two sources with no tick is a claim nobody made.
        Assert.True(Sourced.IsUsable);
        Assert.False((Sourced with { Verified = false }).IsUsable);
        Assert.False((Sourced with { Sources = [Sourced.Sources[0]] }).IsUsable);
    }

    // ---- validation -------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void ARowWithNoBoreLeftIsReported()
    {
        // 27's invariant 7. A wall transcribed as 32 rather than 3.2 gives a negative bore, which is
        // caught here; one transcribed as 3.6 rather than 3.2 gives a plausible bore, which is what
        // the two-source rule is for. The two checks catch different mistakes and neither substitutes.
        var faults = Fixture(Row(25, 33.7, 32.0)).Validate();

        var invalid = Assert.Single(faults, static fault => fault.Code == "FS2604");
        Assert.Contains("DN25", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ASeriesThatStopsAscendingIsReported()
    {
        // SmallestSatisfying takes the first match as the smallest, so a table out of order does not
        // fail -- it quietly answers with the wrong row.
        var faults = Fixture(Row(25, 33.7, 3.2), Row(20, 26.9, 2.6)).Validate();

        Assert.Single(faults, static fault => fault.Code == "FS2604");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignationUsedTwiceIsReported()
    {
        var faults = Fixture(Row(25, 33.7, 3.2), Row(25, 42.4, 3.2)).Validate();

        Assert.Contains(faults, static fault => fault.Code == "FS2604");
    }

    // ---- resolution -------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void AnUnknownPinIsAnErrorRatherThanAFallback()
    {
        // Falling back to the default would size a design against a series nobody chose, and the
        // script would still say steel_en10225. The available list is in the message because the
        // alternative is a user guessing at ids.
        var result = PipeCatalogs.Resolve(new CatalogPin("steel_en10225", null));

        Assert.False(result.IsSuccess);
        Assert.Equal("FS2603", result.Error!.Code);
        Assert.Contains(SteelEn10255.Id, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoPinSelectsTheDefaultAndSaysWhich()
    {
        var fixture = Fixture(Row(25, 33.7, 3.2));

        var resolved = PipeCatalogs.Resolve(
            pin: null,
            new Dictionary<string, ICatalog<PipeSpec>>(StringComparer.Ordinal) { ["fixture"] = fixture },
            fixture);

        Assert.True(resolved.IsSuccess);
        var note = Assert.Single(resolved.Value.Notes);
        Assert.Equal("FS2606", note.Code);
        Assert.Contains("fixture", note.Message, StringComparison.Ordinal);
    }

    // ---- selection --------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void SmallestSatisfyingTakesTheFirstRowThatFits()
    {
        var catalog = Fixture(Row(15, 21.3, 2.6), Row(20, 26.9, 2.6), Row(25, 33.7, 3.2));

        var chosen = catalog.SmallestSatisfying(static spec => spec.InsideDiameter >= 0.020);

        Assert.Equal("DN20", chosen.Entry.Designation);
        Assert.Equal(CatalogFit.Exact, chosen.Fit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NothingBigEnoughClampsToTheLargestAndSaysSo()
    {
        // The clamp rather than a failure: a design needing more than the series still gets a number
        // and a warning naming the component, which P3.7 builds because the catalogue does not know
        // who asked. Refusing outright would leave the diagram blank over a solvable circuit.
        var catalog = Fixture(Row(15, 21.3, 2.6), Row(20, 26.9, 2.6));

        var chosen = catalog.SmallestSatisfying(static spec => spec.InsideDiameter >= 1.0);

        Assert.Equal("DN20", chosen.Entry.Designation);
        Assert.Equal(CatalogFit.ClampedToLargest, chosen.Fit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheSmallestRowAlreadyFittingMeansTheWantedSizeIsBelowTheSeries()
    {
        var catalog = Fixture(Row(15, 21.3, 2.6), Row(20, 26.9, 2.6));

        var chosen = catalog.SmallestSatisfying(static spec => spec.InsideDiameter >= 0.001);

        Assert.Equal("DN15", chosen.Entry.Designation);
        Assert.Equal(CatalogFit.ClampedToSmallest, chosen.Fit);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NearestBelowNeverGoesAbove()
    {
        // Below, not above: an undersized valve authority is recoverable, an oversized one is a valve
        // that controls nothing over most of its travel.
        var catalog = Fixture(Row(15, 21.3, 2.6), Row(20, 26.9, 2.6), Row(25, 33.7, 3.2));

        var chosen = catalog.NearestBelow(0.0250, static spec => spec.InsideDiameter);

        Assert.Equal("DN20", chosen.Entry.Designation);
        Assert.Equal(CatalogFit.Exact, chosen.Fit);
    }

    // ---- the bore, which everything downstream depends on ------------------------------------------

    [Fact]
    [Trait("Category", "Unit")]
    public void TheBoreOfDn25Is27Point3Millimetres()
    {
        // The number this whole document exists to get right. DN is a designation: an area computed
        // from 25 mm is 16 % small and the pressure gradient roughly a factor of two out, with nothing
        // in the result looking wrong.
        var lookup = new CatalogBoreLookup(SteelEn10255.Instance);

        Assert.Equal(0.0273, lookup.BoreFor(25)!.Value, 6);
        Assert.Null(lookup.BoreFor(27));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryShippedBoreIsTheOutsideDiameterLessTwoWalls()
    {
        foreach (var entry in SteelEn10255.Instance.Entries)
        {
            var spec = entry.Spec;

            Assert.Equal(spec.OutsideDiameter - (2 * spec.WallThickness), spec.InsideDiameter, 12);
            Assert.True(spec.InsideDiameter > 0, $"{entry.Designation} has no bore.");
            Assert.NotEqual(spec.NominalDiameter / 1000.0, spec.InsideDiameter, 4);
        }
    }

    // ---- 27's worked example, recomputed --------------------------------------------------------

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(15, 1.183, 1299)]
    [InlineData(20, 0.651, 292)]
    [InlineData(25, 0.411, 94.1)]
    [InlineData(32, 0.237, 24.4)]
    public void TheGradientTableIsRecomputedRatherThanTranscribed(
        int dn, double expectedVelocity, double expectedGradient)
    {
        // 27's acceptance criterion: the published table is computed by the same code the sizer uses,
        // and a discrepancy above 1 % fails. Two copies of a pressure-gradient table is how the tree
        // ended up with two different answers for the same pipe.
        //
        // The simple loop at 0.241 l/s, water at its 35 C mean: rho 994 kg/m3, mu 0.7225e-3 Pa s.
        const double flow = 0.241e-3;
        const double density = 994;
        const double viscosity = 0.7225e-3;

        var bore = new CatalogBoreLookup(SteelEn10255.Instance).BoreFor(dn)!.Value;
        var velocity = flow / (Math.PI * bore * bore / 4);

        // One metre, so the drop is the gradient.
        var gradient = new Pipe("P", length: 1, insideDiameter: bore, roughness: SteelEn10255.Roughness)
            .PressureDrop(velocity, density, viscosity);

        Assert.Equal(expectedVelocity, velocity, expectedVelocity * 0.01);
        Assert.Equal(expectedGradient, gradient, expectedGradient * 0.01);
    }
}
