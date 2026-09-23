using System.Collections.Immutable;

namespace FluidScript.Core.Solvers.Steady;

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
    /// <summary>A share of one null direction, carried by one unknown or by one equation.</summary>
    /// <param name="Index">
    /// Which one: a <strong>column</strong> of the system for a direction from <see cref="Of(double[], int)"/>, and a
    /// <strong>row</strong> for one from <see cref="Redundancy"/>. The two null spaces are the same
    /// elimination on a matrix and on its transpose, so they share this type and mean different things
    /// by it — which is why it is not called <c>Column</c>.
    /// </param>
    /// <param name="Weight">
    /// Its signed share, normalised so the largest participant is exactly 1. Dimensionless, and
    /// meaningful only relative to the other participants in the same direction.
    /// </param>
    public readonly record struct Participant(int Index, double Weight);

    /// <summary>The share of a null direction below which a participant is not worth naming.</summary>
    /// <value>
    /// 0.02 of the largest. Small entries are the finite-difference Jacobian's own noise as much as
    /// anything real, and a message listing eleven names of which three matter is worse than one
    /// listing three.
    /// </value>
    public const double Significant = 0.02;

    /// <summary>How far a pivot must collapse, against the largest, before its column counts as free.</summary>
    /// <remarks>
    /// <para>
    /// A ratio rather than a magnitude: a scaled Jacobian's entries still span orders, and only the
    /// spread carries information. Measured across this corpus the gap is wide -- a circuit that solves
    /// runs a smallest-to-largest pivot ratio between 4e-2 and 6e-4, and one that is rank deficient runs
    /// 6e-12 to 2e-14. Six orders of clear water, so the constant is not delicate.
    /// </para>
    /// <para>
    /// <strong>Deliberately far above machine epsilon and below the finite-difference noise floor.</strong>
    /// The Jacobian is built by forward differences, whose relative error is about <c>sqrt(eps)</c>, near
    /// 1e-8 -- a ratio below that says nothing about the circuit, and one at 1e-14 is a structural zero
    /// rather than arithmetic. Sitting between them is what lets this report a deficiency without
    /// inventing one.
    /// </para>
    /// </remarks>
    public const double RankTolerance = 1e-10;

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
    public static ImmutableArray<Participant> Of(double[] matrix, int order) =>
        Of(matrix, order, RankTolerance);

    /// <summary>Finds the combination of unknowns a matrix leaves undetermined, or nearly so.</summary>
    /// <param name="matrix">As <see cref="Of(double[], int)"/>: the scaled Jacobian, overwritten.</param>
    /// <param name="order">The number of rows, which equals the number of columns.</param>
    /// <param name="rankTolerance">
    /// How far a pivot must collapse, against the largest entry, before its column counts as free.
    /// <see cref="RankTolerance"/> asks for an exact deficiency; <see cref="Tolerances.JacobianValley"/>
    /// asks for a valley — a direction the equations fix so weakly that a change in the fluid's sixth
    /// digit slides the answer along it (<c>S-37</c>).
    /// </param>
    /// <returns>As <see cref="Of(double[], int)"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="matrix"/> is not <paramref name="order"/> squared.
    /// </exception>
    public static ImmutableArray<Participant> Of(double[] matrix, int order, double rankTolerance)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != order * order)
        {
            throw new ArgumentException(
                $"Expected {order * order} entries for an order-{order} matrix, got {matrix.Length}.",
                nameof(matrix));
        }

        var largestEntry = 0.0;

        foreach (var entry in matrix)
        {
            largestEntry = Math.Max(largestEntry, Math.Abs(entry));
        }

        // Relative to the largest entry -- which under full pivoting is the elimination's first pivot --
        // because that is the only question rank has an answer to: how far has a pivot collapsed against
        // the ones before it.
        //
        // It was `Tolerances.JacobianSingular` times the matrix norm, which is `DenseLu`'s floor and
        // belongs there: `DenseLu` decides whether a Newton step can be taken, and a pivot near the
        // arithmetic's own noise is what stops it. Rank is a different question, and asking it with a
        // norm-scaled floor made this method disagree with the report that calls it. On the header with
        // one pump head stated -- largest pivot 481.9, smallest 2.754e-9 -- that floor lands near 1e-9 and
        // calls the collapsed pivot live, so one report printed `1 unknown nothing determines` and
        // `(none found)` two lines apart. **A rank criterion that differs between an instrument and its
        // caller is worse than either criterion, because the disagreement is silent.**
        var floor = rankTolerance * largestEntry;
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

    /// <summary>Finds the equation a matrix's other rows already imply.</summary>
    /// <param name="matrix">
    /// The scaled Jacobian, <paramref name="order"/> squared and row-major. Read only — unlike
    /// <see cref="Of(double[], int)"/> this transposes into its own working copy, because a caller wanting both
    /// directions would otherwise have to keep two matrices.
    /// </param>
    /// <param name="order">The number of rows, which equals the number of columns.</param>
    /// <returns>
    /// The participants in the first left null direction, largest share first, normalised so the largest
    /// is 1. Each <see cref="Participant.Index"/> is a <strong>row</strong> of the system. Empty on a
    /// matrix of full rank, and on one of no rank at all.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="matrix"/> is not <paramref name="order"/> squared.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>A square system short by one has two null directions, and only this one is the
    /// defect.</strong> <see cref="Of(double[], int)"/> answers "which unknowns are free", which reads like a cause and
    /// is not: after full pivoting the free columns are whichever combination the elimination happened
    /// to leave over. This answers "which equation says nothing the others did not", and that is the
    /// redundancy itself — the row the user has to change, or supply a different one in place of.
    /// </para>
    /// <para>
    /// <strong><c>S-36</c> is why both are reported.</strong> On <c>m2-distribution-header</c> the
    /// column direction named pumps every time, and three sessions of pump arrangements followed it —
    /// four variants built, measured and eliminated, each deficient by exactly one, because the
    /// deficiency was never about pumps. The row direction on the same system names one node's
    /// <em>mass</em> balance against <em>every energy balance in the circuit</em>. Neither answer is
    /// wrong; they answer different questions, and only one of them is the question.
    /// </para>
    /// <para>
    /// The transpose is the whole implementation. The left null space of <c>A</c> is the right null
    /// space of <c>A</c> transposed, so this is <see cref="Of(double[], int)"/> on transposed data and inherits its
    /// pivoting, its significance floor and its one-direction-only rule.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Participant> Redundancy(double[] matrix, int order)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != order * order)
        {
            throw new ArgumentException(
                $"Expected {order * order} entries for an order-{order} matrix, got {matrix.Length}.",
                nameof(matrix));
        }

        var transposed = new double[matrix.Length];

        for (var row = 0; row < order; row++)
        {
            for (var column = 0; column < order; column++)
            {
                transposed[(column * order) + row] = matrix[(row * order) + column];
            }
        }

        return Of(transposed, order);
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
