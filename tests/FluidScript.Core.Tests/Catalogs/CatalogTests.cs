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
    public void TheShippedPipeCatalogueIsVerifiedAndResolves()
    {
        // Attested 2026-09-03 against the sources in Catalogs/SOURCES.md, on the nominal preferred
        // diameters (D-67). This replaced a test asserting the opposite, which existed so that
        // "authored but unverified" could not quietly become permanent.
        Assert.Empty(SteelEn10255.Instance.Validate());

        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.All(
            resolved.Value.Catalog.Entries,
            static entry => Assert.True(entry.Provenance.IsUsable, entry.Designation));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Dn150ReproducesItsPublishedMassPerMetre()
    {
        // The arithmetic that settled DN150 when the sources disagreed. EN 10220's series runs
        // 139.7 -> 168.3 with no 165.1 in it, and one merchant's page states both diameters for the
        // same product -- but only one of them reproduces the 19.7 kg/m it also publishes.
        //
        // A mass per metre constrains a diameter and a wall *together*, which is what makes it a third
        // source rather than a restatement of either. Kept as a test because the next series added will
        // face the same disagreement, and this is the cheapest way through it.
        const double steelDensity = 7850;

        var dn150 = SteelEn10255.Instance.Entries.Single(
            static entry => entry.Spec.NominalDiameter == 150).Spec;

        var area = Math.PI / 4
            * ((dn150.OutsideDiameter * dn150.OutsideDiameter)
                - (dn150.InsideDiameter * dn150.InsideDiameter));

        Assert.Equal(19.7, area * steelDensity, 0.2);
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
    public void AnUnverifiedRowRefusesTheWholeCatalogue()
    {
        // FS2605, asserted on a fixture. It used to be asserted on the shipped table, which was
        // unverified by design until D-67 settled the diameter basis -- and when that assertion was
        // deleted the coverage gate caught that nothing else named the code. A diagnostic tied to a
        // temporary state of real data has nowhere to live once the data is fixed.
        //
        // One unchecked row refuses the catalogue it is in rather than being skipped: a sizer takes the
        // table or it does not, and a table with a hole in it is the shape that gets used anyway.
        var unchecked_ = Row(25, 33.7, 3.2) with { Provenance = Sourced with { Verified = false } };

        var fault = Assert.Single(Fixture(unchecked_).Validate(), static f => f.Code == "FS2605");

        Assert.Contains("DN25", fault.Message, StringComparison.Ordinal);
        Assert.False(
            PipeCatalogs.Resolve(
                new CatalogPin("fixture", null),
                new Dictionary<string, ICatalog<PipeSpec>>(StringComparer.Ordinal)
                {
                    ["fixture"] = Fixture(unchecked_),
                },
                Fixture(unchecked_)).IsSuccess);
    }

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
