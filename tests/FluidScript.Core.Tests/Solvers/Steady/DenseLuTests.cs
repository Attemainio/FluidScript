using FluidScript.Core.Solvers;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>The linear solve, and the singularity diagnosis that is the point of writing it here.</summary>
/// <remarks>
/// A 200×200 dense factorisation is under a millisecond, so speed is not what these assert. What they
/// assert is that a matrix with no unique solution comes back as a <em>report</em> naming a column
/// rather than as an exception or a vector of infinities.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DenseLuTests
{
    [Fact]
    public void ItSolvesASystemWhoseAnswerIsKnownByHand()
    {
        // 2x + y = 5, x + 3y = 10  ->  x = 1, y = 3.
        var factored = DenseLu.Factor([2, 1, 1, 3], 2);
        var rhs = new double[] { 5, 10 };

        Assert.False(factored.IsSingular);

        factored.Solve(rhs);

        Assert.Equal(1.0, rhs[0], 12);
        Assert.Equal(3.0, rhs[1], 12);
    }

    [Fact]
    public void ItPivotsRatherThanDividingByAZeroDiagonal()
    {
        // A zero in the first pivot position is not singular -- it is a row order. Partial pivoting is
        // what tells the two apart, and a factorisation that skipped it would divide by zero here.
        var factored = DenseLu.Factor([0, 1, 1, 0], 2);
        var rhs = new double[] { 3, 7 };

        Assert.False(factored.IsSingular);

        factored.Solve(rhs);

        Assert.Equal(7.0, rhs[0], 12);
        Assert.Equal(3.0, rhs[1], 12);
    }

    [Fact]
    public void ADuplicatedRowIsReportedAsSingularAndNamesItsColumn()
    {
        var factored = DenseLu.Factor([1, 2, 1, 2], 2);

        Assert.True(factored.IsSingular);
        Assert.InRange(factored.SingularColumn, 0, 1);
    }

    [Fact]
    public void AColumnOfZerosIsSingularAtThatColumn()
    {
        // The shape a Jacobian takes when an unknown influences nothing -- an equation row that was
        // left unevaluated, or a finite-difference step that came out zero. Naming the column is what
        // lets FS3002 name the unknown instead of an index.
        var factored = DenseLu.Factor([1, 0, 3, 0], 2);

        Assert.True(factored.IsSingular);
        Assert.Equal(1, factored.SingularColumn);
    }

    [Fact]
    public void SolvingASingularFactorisationThrowsRatherThanReturningNonsense()
    {
        var factored = DenseLu.Factor([1, 2, 2, 4], 2);

        Assert.Throws<InvalidOperationException>(() => factored.Solve(new double[2]));
    }

    [Fact]
    public void ALargerSystemRoundTripsThroughItsOwnMultiplication()
    {
        // Deterministic rather than random: a seeded generator would make a failure reproducible only
        // if the seed were printed, and a fixed matrix is reproducible without anyone remembering to.
        const int Order = 12;

        var matrix = new double[Order * Order];
        var expected = new double[Order];

        for (var row = 0; row < Order; row++)
        {
            expected[row] = 1 + (row * 0.5);

            for (var column = 0; column < Order; column++)
            {
                matrix[(row * Order) + column] = row == column ? Order + row : 1.0 / (1 + row + column);
            }
        }

        var rhs = new double[Order];

        for (var row = 0; row < Order; row++)
        {
            for (var column = 0; column < Order; column++)
            {
                rhs[row] += matrix[(row * Order) + column] * expected[column];
            }
        }

        var factored = DenseLu.Factor((double[])matrix.Clone(), Order);

        Assert.False(factored.IsSingular);

        factored.Solve(rhs);

        for (var row = 0; row < Order; row++)
        {
            Assert.Equal(expected[row], rhs[row], 9);
        }
    }
}
