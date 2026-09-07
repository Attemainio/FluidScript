using System.Collections.Immutable;

namespace FluidScript.Core.Solvers;

/// <summary>The combination of unknowns a singular Jacobian leaves undetermined.</summary>
/// <remarks>
/// <para>
/// <strong>A singular system is not "singular around one component", and saying so sends a user to the
/// wrong place.</strong> <c>S-33</c> is the case that forced this: on <c>m2-distribution-header</c>
/// <see cref="DenseLu"/> stops at whichever column its partial pivoting reaches first, and
/// <c>FS3002</c> then names that column's owner and suggests checking for a missing pressure datum --
/// on a script that states one. What is actually undetermined there is
/// <c>PU_AHU.head + PU_RAD.head - PU_MAIN.head</c>: three pumps whose individual heads no equation
/// fixes, only their combination. No single name can express that, and the null direction is the only
/// thing that can.
/// </para>
/// <para>
/// <strong>It is a diagnostic path and is allowed to be expensive.</strong> This re-eliminates the
/// whole matrix at <em>O(n³)</em> with full pivoting, having already paid for a partial-pivot
/// factorisation that failed. That is acceptable exactly once, on a run that is over: nothing here
/// executes on a converging solve. Full pivoting rather than partial is the whole point -- partial
/// pivoting cannot tell a dependent column from one that merely sorts late, which is how
/// <c>FS3002</c> came to name <c>PU_RAD</c> for a direction in which <c>PU_MAIN</c> participates just
/// as strongly.
/// </para>
/// <para>
/// <strong>Only the first null direction is reported when there are several.</strong> A deficiency of
/// more than one means several independent things are undetermined at once, and a user who fixes the
/// first will be shown the second; listing all of them at once describes a circuit so under-specified
/// that the list is not the useful part.
/// </para>
/// </remarks>
public static class NullDirection
{
    /// <summary>A share of one null direction, carried by one unknown.</summary>
    /// <param name="Column">The unknown's column in the system.</param>
    /// <param name="Weight">
    /// Its signed share, normalised so the largest participant is exactly 1. Dimensionless, and
    /// meaningful only relative to the other participants in the same direction.
    /// </param>
    public readonly record struct Participant(int Column, double Weight);

    /// <summary>The share of a null direction below which a participant is not worth naming.</summary>
    /// <value>
    /// 0.02 of the largest. Small entries are the finite-difference Jacobian's own noise as much as
    /// anything real, and a message listing eleven names of which three matter is worse than one
    /// listing three.
    /// </value>
    public const double Significant = 0.02;

    /// <summary>Finds the combination of unknowns a matrix leaves undetermined.</summary>
    /// <param name="matrix">
    /// The scaled Jacobian, <paramref name="order"/> squared and row-major. <strong>Overwritten</strong>
    /// with the elimination's intermediate state, exactly as <see cref="DenseLu.Factor"/> overwrites
    /// its own — pass a copy, or a matrix nothing reads afterwards.
    /// </param>
    /// <param name="order">The number of rows, which equals the number of columns.</param>
    /// <returns>
    /// The participants in the first null direction, largest share first, normalised so the largest is
    /// 1. Empty when the matrix has full rank, and empty when it has no rank at all — a matrix of zeros
    /// leaves everything undetermined, which names nothing.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="matrix"/> is not <paramref name="order"/> squared.
    /// </exception>
    public static ImmutableArray<Participant> Of(double[] matrix, int order)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != order * order)
        {
            throw new ArgumentException(
                $"Expected {order * order} entries for an order-{order} matrix, got {matrix.Length}.",
                nameof(matrix));
        }

        var norm = 0.0;

        for (var row = 0; row < order; row++)
        {
            var sum = 0.0;

            for (var column = 0; column < order; column++)
            {
                sum += Math.Abs(matrix[(row * order) + column]);
            }

            norm = Math.Max(norm, sum);
        }

        // The same relative floor DenseLu uses, for the same reason: an absolute one means different
        // things for differently scaled systems, and only the scaled system is ever factorised.
        var floor = Tolerances.JacobianSingular * Math.Max(norm, 1);
        var columnOf = new int[order];

        for (var index = 0; index < order; index++)
        {
            columnOf[index] = index;
        }

        var rank = Eliminate(matrix, order, floor, columnOf);

        if (rank == order || rank == 0)
        {
            return [];
        }

        // Back-substitute with the first free unknown set to 1 and the rest to 0. The result is a
        // vector the matrix maps to zero: a direction every equation is blind to.
        var direction = new double[order];
        direction[rank] = 1;

        for (var row = rank - 1; row >= 0; row--)
        {
            var sum = 0.0;

            for (var column = row + 1; column < order; column++)
            {
                sum += matrix[(row * order) + column] * direction[column];
            }

            direction[row] = -sum / matrix[(row * order) + row];
        }

        var largest = 0.0;

        foreach (var value in direction)
        {
            largest = Math.Max(largest, Math.Abs(value));
        }

        if (largest == 0)
        {
            return [];
        }

        var participants = ImmutableArray.CreateBuilder<Participant>();

        for (var index = 0; index < order; index++)
        {
            var weight = direction[index] / largest;

            if (Math.Abs(weight) >= Significant)
            {
                participants.Add(new Participant(columnOf[index], weight));
            }
        }

        participants.Sort(static (left, right) => Math.Abs(right.Weight).CompareTo(Math.Abs(left.Weight)));

        return participants.ToImmutable();
    }

    /// <summary>Reduces the matrix with full pivoting, recording where its columns went.</summary>
    /// <param name="matrix">The matrix, overwritten.</param>
    /// <param name="order">Its order.</param>
    /// <param name="floor">The magnitude below which a pivot counts as zero.</param>
    /// <param name="columnOf">The permutation, updated in step with the column swaps.</param>
    /// <returns>The rank.</returns>
    private static int Eliminate(double[] matrix, int order, double floor, int[] columnOf)
    {
        var rank = 0;

        for (var step = 0; step < order; step++)
        {
            var largest = 0.0;
            var pivotRow = -1;
            var pivotColumn = -1;

            for (var row = step; row < order; row++)
            {
                for (var column = step; column < order; column++)
                {
                    var candidate = Math.Abs(matrix[(row * order) + column]);

                    if (candidate > largest)
                    {
                        largest = candidate;
                        pivotRow = row;
                        pivotColumn = column;
                    }
                }
            }

            if (largest <= floor)
            {
                return rank;
            }

            if (pivotRow != step)
            {
                for (var column = 0; column < order; column++)
                {
                    (matrix[(step * order) + column], matrix[(pivotRow * order) + column]) =
                        (matrix[(pivotRow * order) + column], matrix[(step * order) + column]);
                }
            }

            if (pivotColumn != step)
            {
                for (var row = 0; row < order; row++)
                {
                    (matrix[(row * order) + step], matrix[(row * order) + pivotColumn]) =
                        (matrix[(row * order) + pivotColumn], matrix[(row * order) + step]);
                }

                (columnOf[step], columnOf[pivotColumn]) = (columnOf[pivotColumn], columnOf[step]);
            }

            var diagonal = matrix[(step * order) + step];

            for (var row = step + 1; row < order; row++)
            {
                var factor = matrix[(row * order) + step] / diagonal;

                for (var column = step; column < order; column++)
                {
                    matrix[(row * order) + column] -= factor * matrix[(step * order) + column];
                }
            }

            rank++;
        }

        return rank;
    }
}
