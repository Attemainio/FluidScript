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
    public async Task TheHeaderIsToldItsTwoConsumersMoveTogether()
    {
        // `S-38`. `FS3002` names `PU_RAD` because partial pivoting stopped there; the measured direction
        // spans both consumers at once -- each secondary's pump head against its own valve position, with
        // the two consumers symmetric. A user sent to `PU_RAD` alone would be looking at a quarter of it.
        //
        // `PU_MAIN` is absent and should be: its head is stated, so it is not a solver unknown. An earlier
        // version of this test asserted it *was* named, which was true of a fixture whose source sat in
        // the header path (`F-21`) rather than on a loop of its own.
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        var loop = new OuterLoop(
            new NewtonSolver(),
            new CatalogBoreLookup(resolved.Value.Catalog),
            OuterLoop.Rules(resolved.Value.Catalog),
            10);

        var source = File.ReadAllText(
            Path.Combine(RepositoryLayout.Samples, "m2-distribution-header.fluid"));
        var run = await loop.RunAsync(
            GraphFixture.Bind(source), Water.Instance, "header", TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        var undetermined = Assert.Single(
            run.Value.Solve.Diagnostics.Where(static d => d.Code == "FS3009"));

        Assert.Contains("PU_AHU.head", undetermined.Message, StringComparison.Ordinal);
        Assert.Contains("PU_RAD.head", undetermined.Message, StringComparison.Ordinal);
        Assert.Contains("TV_AHU.position", undetermined.Message, StringComparison.Ordinal);
        Assert.Contains("TV_RAD.position", undetermined.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PU_MAIN.head", undetermined.Message, StringComparison.Ordinal);

        // It rides alongside the termination's own code rather than replacing it.
        Assert.Contains(run.Value.Solve.Diagnostics, static d => d.Code == "FS3002");
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
    public async Task TheRowDirectionIsNotReportedOnACircuitThatHasOne()
    {
        // `S-40`, recorded as a failing expectation rather than a passing one. The header IS singular and
        // a row null direction demonstrably exists on it -- a finite-difference Jacobian at the same
        // point names seven rows, mass balances at weight 1 -- but `NullDirection.Redundancy` finds none
        // through the solver's own path, so `FS3010` is silent and the user gets only `FS3009`, the half
        // `S-36` exists to warn against trusting alone.
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

        Assert.True(run.IsSuccess, run.Error?.Message);

        // The termination and the column direction are both reported.
        Assert.Contains(run.Value.Solve.Diagnostics, static d => d.Code == "FS3002");
        Assert.Contains(run.Value.Solve.Diagnostics, static d => d.Code == "FS3009");

        // And the row direction is not, which is the defect. Flip this to `Contains` when `S-40` closes.
        Assert.DoesNotContain(run.Value.Solve.Diagnostics, static d => d.Code == "FS3010");
    }
}
