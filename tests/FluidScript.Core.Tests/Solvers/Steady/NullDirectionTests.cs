using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers.Steady;

/// <summary>What a singular system leaves undetermined, and how <c>FS3009</c> says it.</summary>
public sealed class NullDirectionTests
{
    [Fact]
    public void AMatrixWithAnInverseLeavesNothingUndetermined()
    {
        double[] matrix = [2, 0, 0, 3];

        Assert.Empty(NullDirection.Of(matrix, 2));
        Assert.Empty(NullDirection.Redundancy(matrix, 2));
    }

    [Fact]
    public void ARowTheOthersAlreadyImplyIsNamedRatherThanTheColumnsItLeavesFree()
    {
        // `S-36`, and `FS3010`'s content. Row 2 is row 0 plus row 1, so it states nothing the first two
        // did not -- that is the defect. The *columns* this leaves free are a different question with a
        // different answer, and reporting only those is what sent three sessions after the header's
        // pumps: the free columns are wherever the elimination's pivoting landed, not the cause.
        double[] matrix =
        [
            1, 0, 0,
            0, 1, 0,
            1, 1, 0,
        ];

        var implied = NullDirection.Redundancy([.. matrix], 3);

        // The relation is row0 + row1 - row2 = 0, so all three participate with equal magnitude and the
        // implied row carries the opposite sign to the two implying it. Which overall sign the
        // back-substitution lands on is arbitrary, so the signs are read relative to each other -- and
        // they do *not* sum to zero, which is what a first version of this test wrongly asserted.
        Assert.Equal(3, implied.Length);
        Assert.Equal([0, 1, 2], implied.Select(static p => p.Index).Order());
        Assert.All(implied, static p => Assert.Equal(1, Math.Abs(p.Weight), 1e-9));

        var byRow = implied.ToDictionary(static p => p.Index, static p => p.Weight);

        Assert.Equal(byRow[0], byRow[1], 1e-9);
        Assert.Equal(-byRow[0], byRow[2], 1e-9);

        // The same matrix read the other way names column 2, which no equation touches at all. Both
        // answers are right and only one of them is the redundancy.
        var free = NullDirection.Of([.. matrix], 3);

        Assert.Equal(2, Assert.Single(free).Index);
    }

    [Fact]
    public void TwoIdenticalColumnsAreNamedAsOneCombinationRatherThanTwoUnknowns()
    {
        // Columns 0 and 1 are the same, so nothing distinguishes x0 from x1 -- only x0 - x1 is fixed.
        // This is the shape `m2-distribution-header` has: not a dead column, which would mean an unknown
        // nothing depends on, but two the equations cannot tell apart.
        double[] matrix =
        [
            1, 1, 0,
            2, 2, 1,
            0, 0, 3,
        ];

        var direction = NullDirection.Of(matrix, 3);

        Assert.Equal(2, direction.Length);
        Assert.Equal([0, 1], direction.Select(static p => p.Index).Order());

        // Equal magnitude, opposite sign: move one up and the other down and every equation is blind.
        Assert.Equal(1, Math.Abs(direction[0].Weight), 1e-9);
        Assert.Equal(1, Math.Abs(direction[1].Weight), 1e-9);
        Assert.Equal(0, direction[0].Weight + direction[1].Weight, 1e-9);
    }

    [Fact]
    public void AColumnNoEquationTouchesIsNamedOnItsOwn()
    {
        // The degenerate case the S-26 clamp used to produce: a column of zeros. It is undetermined by
        // itself rather than in combination, and the direction says so with a single participant.
        double[] matrix =
        [
            1, 0, 0,
            0, 0, 0,
            0, 0, 2,
        ];

        var participant = Assert.Single(NullDirection.Of(matrix, 3));

        Assert.Equal(1, participant.Index);
    }

    [Fact]
    public void AMatrixOfZerosNamesNothingBecauseNamingEverythingHelpsNobody()
    {
        Assert.Empty(NullDirection.Of(new double[9], 3));
    }

    [Fact]
    public void ADirectionIsFoundWhereAPivotCollapsesRelativeToTheLargestRatherThanTowardsZero()
    {
        // The `rad-head` variant of `m2-distribution-header`, reduced: largest pivot 481.9, smallest
        // 2.754e-9. Twelve orders apart, which is rank deficiency and not conditioning -- but 2.754e-9 is
        // not *small*, and a floor scaled to `Tolerances.JacobianSingular` (1e-12) times the matrix norm
        // lands near 1e-9 and calls it a live pivot.
        //
        // The report was measuring rank against the largest pivot and getting "deficient by 1", while this
        // method measured it against the norm and returned nothing, so one report said `1 unknown nothing
        // determines` and `(none found)` two lines apart. **A rank criterion that differs between the
        // instrument and its caller is worse than either criterion**, because the disagreement is silent.
        double[] matrix =
        [
            481.9, 0, 0,
            0, 12.4, 0,
            0, 0, 2.754e-9,
        ];

        var direction = NullDirection.Of([.. matrix], 3);

        var participant = Assert.Single(direction);

        Assert.Equal(2, participant.Index);
    }

    [Fact]
    public void ShareBelowTheSignificanceCutIsNotNamed()
    {
        // The third unknown participates at 1e-6 of the largest share, which is the finite-difference
        // Jacobian's own noise floor rather than a physical coupling. Naming it would pad the message.
        double[] matrix =
        [
            1, 1, 1e-6,
            2, 2, 2e-6,
            0, 0, 1,
        ];

        Assert.All(NullDirection.Of(matrix, 3), static p => Assert.NotEqual(2, p.Index));
    }

    [Fact]
    public void AMatrixThatIsNotSquareIsRejectedRatherThanRead()
    {
        Assert.Throws<ArgumentException>(() => NullDirection.Of(new double[5], 3));
    }



    [Fact]
    public async Task ACircuitThatSolvesIsNeverToldSomethingIsUndetermined()
    {
        var resolved = PipeCatalogs.Resolve(pin: null);
        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);

        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-simple-loop.fluid"));
        var run = await loop.RunAsync(
            GraphFixture.Bind(source), Water.Instance, "loop", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(run.Value.Solve.Diagnostics, static d => d.Code == "FS3009");
        Assert.DoesNotContain(run.Value.Solve.Diagnostics, static d => d.Code == "FS3010");
    }

    [Fact]
    public async Task TheHeadersRedundancyIsNowPaidForByItsStatedSourceOutlet()
    {
        // What `S-40` and `S-38` used to assert here, and why it moved twice.
        //
        // The header was square with rank 44 of 45, so `FS3009` and `FS3010` fired and this file held the
        // only end-to-end coverage of both. `S-41` then found that a stated pressure on an *interior*
        // datum was making a closed circuit read as open -- `D-86`'s rule, applied to `HasUnknownFlux` and
        // missed on `IsClosed`. A closed circuit's energy balances are one short of independent, because a
        // uniform enthalpy offset satisfies every one of them; nothing dropped that redundancy, and it sat
        // in the matrix as the dependent row `FS3010` was naming. With the level dropped the count was
        // short by one and refused the circuit before the solver saw it.
        //
        // The sample then stated its source outlet (`HS1 out.t=80`, `F-23`), which is an `EnthalpyLevel`
        // constraint promoting nothing: it pays for the dropped level, the count is square at 39, and the
        // matrix is full rank at every iterate. So neither code fires here any more for the opposite
        // reason it used not to -- the deficiency is gone, not hidden. What the header does instead is
        // recorded in `CorpusStatusTests`.
        //
        // This left `FS3009` and `FS3010` with no end-to-end subject in the corpus (`S-42`); the fixture
        // below, two pumps in series both holding the ring's one flow, is that subject now.
        var resolved = PipeCatalogs.Resolve(pin: null);
        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);

        var source = File.ReadAllText(
            Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"));
        var run = await loop.RunAsync(
            GraphFixture.Bind(source), Water.Instance, "header", TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);
        Assert.DoesNotContain(run.Value.Solve.Diagnostics, static d => d.Code == "FS3009");
        Assert.DoesNotContain(run.Value.Solve.Diagnostics, static d => d.Code == "FS3010");
    }

    /// <summary>
    /// The one square-and-singular circuit the corpus owns on purpose (<c>S-42</c>, built from <c>S-37</c>'s shape): two pumps
    /// in series on a ring whose flow is fixed twice, once by the exchanger's duty pair and once by the second pump's own
    /// <c>flow=</c>. Each statement promotes a head, so the count is square at 15; only the heads' sum enters the ring's
    /// pressure equation, so the rank is 14. Nobody would write it as a sample, which is why it lives here.
    /// </summary>
    private const string TwoPumpsInSeriesHoldingOneFlowTwice = """
        fluidscript 1
        circuit probe
        fluid water

        HE1  heat_exchanger power=30 in.t=20 out.t=50
        LOAD heat_exchanger power=-30 dp=0
        CV1  valve
        PU1  pump
        PU2  pump flow=0.239

        connections
        N1 - PU1 - N1b - PU2 - N2 - HE1 - N3 - LOAD - N4 - CV1 - N5
        N5 - N1 length=25
        """;

    [Fact]
    public async Task TwoPumpsInSeriesHoldingOneFlowTwiceAreReadAsOneFreeDirectionAndOneRedundantStatement()
    {
        // End to end, from the script to the two messages a user reads (`S-42`). Counting passes the circuit
        // -- 15 unknowns against 15 equations, the two promotions paying for the two flow statements -- and
        // the solver finds it singular at the seed. `FS3009` names what moves together: the two heads and the
        // pressure between them, because raising one head and lowering the other by the same amount changes
        // nothing the ring can see. `FS3010` names the redundancy: the pump's flow and the exchanger's outlet
        // fix the same flow, so one of them has to change. Measured 2026-09-22 through the circuit harness on
        // the same script: rank 14 of 15 at the seed and at the last iterate, smallest pivot 0.
        var resolved = PipeCatalogs.Resolve(pin: null);
        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);

        var run = await loop.RunAsync(
            GraphFixture.Bind(TwoPumpsInSeriesHoldingOneFlowTwice), Water.Instance, "probe", TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);
        var diagnostics = run.Value.Solve.Diagnostics;

        Assert.Contains(diagnostics, static d => d.Code == "FS3002");

        var free = Assert.Single(diagnostics, static d => d.Code == "FS3009");
        Assert.Contains("PU1.head", free.Message, StringComparison.Ordinal);
        Assert.Contains("PU2.head", free.Message, StringComparison.Ordinal);

        var redundant = Assert.Single(diagnostics, static d => d.Code == "FS3010");
        Assert.Contains("PU2.flow", redundant.Message, StringComparison.Ordinal);
        Assert.Contains("HE1.out.t", redundant.Message, StringComparison.Ordinal);
    }
}
