namespace FluidScript.Core.Solvers;

/// <summary>An LU factorisation with partial pivoting, and what it found if it could not finish.</summary>
/// <remarks>
/// <para>
/// Dense, because v1's sizes make it uninteresting: a 200×200 factorisation is under a millisecond,
/// and every sparse structure costs more in overhead than it saves below a few hundred unknowns
/// (<c>32</c>). The Jacobian <em>is</em> sparse — a component's residual depends only on its own ports
/// — and that pattern is worth exploiting the day a model needs it, not before.
/// </para>
/// <para>
/// <strong>The interesting part is detecting singularity properly, not solving fast.</strong> A pivot
/// below <c>1e-12 · ‖J‖∞</c> means the system has no unique solution, and the useful part of that is
/// <em>which</em> unknown: the pivot's row and column map back through the two layouts to a component
/// and an equation, so the message can name a node instead of an index.
/// </para>
/// </remarks>
public sealed class DenseLu
{
    private readonly double[] _lu;
    private readonly int[] _pivots;
    private readonly int _order;

    private DenseLu(double[] lu, int[] pivots, int order, int singularRow, int singularColumn)
    {
        _lu = lu;
        _pivots = pivots;
        _order = order;
        SingularRow = singularRow;
        SingularColumn = singularColumn;
    }

    /// <summary>Gets whether the matrix could be factorised.</summary>
    public bool IsSingular => SingularColumn >= 0;

    /// <summary>Gets the row whose pivot was too small, or <c>-1</c>.</summary>
    public int SingularRow { get; }

    /// <summary>Gets the column whose pivot was too small, or <c>-1</c>.</summary>
    public int SingularColumn { get; }

    /// <summary>Factorises a square matrix held row-major.</summary>
    /// <param name="matrix">The matrix, <paramref name="order"/> squared, overwritten with its factors.</param>
    /// <param name="order">The number of rows.</param>
    /// <returns>The factorisation, singular or not.</returns>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not <paramref name="order"/> squared.</exception>
    public static DenseLu Factor(double[] matrix, int order)
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

        // Relative to the matrix's own scale, because an absolute floor means one thing for a Jacobian
        // in pascals and another for the scaled one -- and the scaled one is what is ever factorised.
        var floor = Tolerances.JacobianSingular * Math.Max(norm, 1);
        var pivots = new int[order];

        for (var index = 0; index < order; index++)
        {
            pivots[index] = index;
        }

        for (var column = 0; column < order; column++)
        {
            var best = column;
            var largest = Math.Abs(matrix[(column * order) + column]);

            for (var row = column + 1; row < order; row++)
            {
                var candidate = Math.Abs(matrix[(row * order) + column]);

                if (candidate > largest)
                {
                    largest = candidate;
                    best = row;
                }
            }

            if (largest <= floor)
            {
                return new DenseLu(matrix, pivots, order, best, column);
            }

            if (best != column)
            {
                (pivots[column], pivots[best]) = (pivots[best], pivots[column]);

                for (var index = 0; index < order; index++)
                {
                    (matrix[(column * order) + index], matrix[(best * order) + index]) =
                        (matrix[(best * order) + index], matrix[(column * order) + index]);
                }
            }

            var diagonal = matrix[(column * order) + column];

            for (var row = column + 1; row < order; row++)
            {
                var factor = matrix[(row * order) + column] / diagonal;

                matrix[(row * order) + column] = factor;

                for (var index = column + 1; index < order; index++)
                {
                    matrix[(row * order) + index] -= factor * matrix[(column * order) + index];
                }
            }
        }

        return new DenseLu(matrix, pivots, order, -1, -1);
    }

    /// <summary>Solves for one right-hand side.</summary>
    /// <param name="rhs">The right-hand side, replaced by the solution.</param>
    /// <exception cref="InvalidOperationException">The factorisation is singular.</exception>
    /// <exception cref="ArgumentException"><paramref name="rhs"/> is the wrong length.</exception>
    public void Solve(Span<double> rhs)
    {
        if (IsSingular)
        {
            throw new InvalidOperationException(
                $"The matrix is singular at row {SingularRow}, column {SingularColumn}; there is nothing "
                + "to solve. Check IsSingular first -- a singular Jacobian is a diagnostic, not an "
                + "exception.");
        }

        if (rhs.Length != _order)
        {
            throw new ArgumentException($"Expected {_order} entries, got {rhs.Length}.", nameof(rhs));
        }

        Span<double> permuted = _order <= 128 ? stackalloc double[_order] : new double[_order];

        for (var row = 0; row < _order; row++)
        {
            permuted[row] = rhs[_pivots[row]];
        }

        for (var row = 1; row < _order; row++)
        {
            var sum = permuted[row];

            for (var column = 0; column < row; column++)
            {
                sum -= _lu[(row * _order) + column] * permuted[column];
            }

            permuted[row] = sum;
        }

        for (var row = _order - 1; row >= 0; row--)
        {
            var sum = permuted[row];

            for (var column = row + 1; column < _order; column++)
            {
                sum -= _lu[(row * _order) + column] * permuted[column];
            }

            permuted[row] = sum / _lu[(row * _order) + row];
        }

        permuted.CopyTo(rhs);
    }
}
