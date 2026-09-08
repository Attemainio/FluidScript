using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Solvers;
using FluidScript.Core.Tests.Topology;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

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
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);

        var source = File.ReadAllText(Path.Combine(RepositoryLayout.Samples, "m2-simple-loop.fluid"));
        var run = await loop.RunAsync(
            GraphFixture.Bind(source), Water.Instance, "loop", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(run.Value.Solve.Diagnostics, static d => d.Code == "FS3009");
        Assert.DoesNotContain(run.Value.Solve.Diagnostics, static d => d.Code == "FS3010");
    }

    [Fact]
    public async Task TheHeadersRedundancyIsNowCaughtBeforeTheSolverRatherThanAsAZeroPivot()
    {
        // What `S-40` and `S-38` used to assert here, and why it moved.
        //
        // The header was square with rank 44 of 45, so `FS3009` and `FS3010` fired and this file held the
        // only end-to-end coverage of both. `S-41` then found that a stated pressure on an *interior*
        // datum was making a closed circuit read as open -- `D-86`'s rule, applied to `HasUnknownFlux` and
        // missed on `IsClosed`. A closed circuit's energy balances are one short of independent, because a
        // uniform enthalpy offset satisfies every one of them; nothing dropped that redundancy, and it sat
        // in the matrix as the dependent row `FS3010` was naming.
        //
        // The deficiency did not change. Where it is reported did: the count is now short by one and the
        // check refuses the circuit, which is a message a user can act on instead of a zero pivot.
        //
        // **This leaves `FS3009` and `FS3010` with no end-to-end subject in the corpus**, since no sample
        // now reaches the solver singular. The matrix-level tests above still pin their content. A fixture
        // that is square and singular on purpose is wanted, and is recorded as such.
        var resolved = PipeCatalogs.Resolve(pin: null);
        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);

        var source = File.ReadAllText(
            Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"));
        var run = await loop.RunAsync(
            GraphFixture.Bind(source), Water.Instance, "header", TestContext.Current.CancellationToken);

        Assert.False(run.IsSuccess);
        Assert.Contains("under-specified by 1", run.Error?.Message, StringComparison.Ordinal);
    }
}
