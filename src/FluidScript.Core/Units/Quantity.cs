using System.Globalization;

using FluidScript.Core.Solvers;

namespace FluidScript.Core.Units;

/// <summary>
/// A number with a dimension: the only representation of a dimensioned value that crosses a public
/// boundary in Core.
/// </summary>
/// <remarks>
/// <para>
/// Stored in SI base units, always. The unit a value was written in is kept for round-tripping and
/// display and never takes part in arithmetic, so a value converts once on the way in and once on the
/// way out and never accumulates conversions in between.
/// </para>
/// <para>
/// <c>double</c> rather than <c>decimal</c>: property calls return doubles, correlations are empirical
/// to three or four significant figures, and the solver runs this inside its iteration. Exactness the
/// underlying physics does not have would cost an order of magnitude of speed.
/// </para>
/// </remarks>
public readonly record struct Quantity
{
    // 36's quantity.compare_rel_tol. Comparison, not convergence -- but still one of that table's
    // rows, so it is read from there rather than kept as a second copy (S-6).
    private const double DefaultRelativeTolerance = Tolerances.QuantityCompareRelative;

    /// <summary>Gets the magnitude in the dimension's SI base unit.</summary>
    /// <value>
    /// W for power, K for temperature, Pa <em>gauge</em> for pressure, m³ for volume. A pressure is
    /// stored relative to atmosphere, so an absolute spelling has already had the atmosphere removed.
    /// </value>
    public double SiValue { get; init; }

    /// <summary>Gets the dimension this quantity belongs to.</summary>
    public Dimension Dimension { get; init; }

    /// <summary>Gets the unit the value was written in.</summary>
    /// <value>
    /// <see langword="null"/> when the source was a bare number or the value came from arithmetic.
    /// Presentation state, kept here rather than in a parallel map because a parallel map has to be
    /// threaded through every stage and desynchronises the first time one forgets.
    /// </value>
    public UnitSymbol? SourceUnit { get; init; }

    /// <summary>Creates a quantity from a magnitude already in SI.</summary>
    /// <param name="siValue">The magnitude in the dimension's SI base unit.</param>
    /// <param name="dimension">The dimension.</param>
    /// <returns>A quantity with no source unit.</returns>
    public static Quantity FromSi(double siValue, Dimension dimension) =>
        new() { SiValue = siValue, Dimension = dimension };

    /// <summary>Creates a quantity from a value written with an explicit unit.</summary>
    /// <param name="value">The magnitude as written.</param>
    /// <param name="unit">The unit it was written in.</param>
    /// <returns>A quantity in SI, remembering the spelling for the round trip.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unit"/> is <see langword="null"/>.</exception>
    public static Quantity FromUnit(double value, UnitSymbol unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return new Quantity { SiValue = unit.ToSi(value), Dimension = unit.Dimension, SourceUnit = unit };
    }

    /// <summary>Creates a quantity from a bare number, in the dimension's canonical script unit.</summary>
    /// <param name="value">The magnitude as written, with no unit.</param>
    /// <param name="dimension">The dimension the destination declares.</param>
    /// <returns>
    /// A quantity in SI. <c>power=30</c> becomes 30 000 W and <c>in=20</c> becomes 293.15 K, because
    /// the canonical unit is a property of the dimension and never of the parameter.
    /// </returns>
    public static Quantity FromBareNumber(double value, Dimension dimension) =>
        UnitTable.CanonicalUnitFor(dimension) is { } canonical
            ? new Quantity { SiValue = canonical.ToSi(value), Dimension = dimension }
            : FromSi(value, dimension);

    /// <summary>Converts the magnitude to a unit.</summary>
    /// <param name="unit">The unit to express the value in.</param>
    /// <returns>The magnitude as it would be written in <paramref name="unit"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unit"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The unit belongs to another dimension. Reaching this is a bug in Core rather than anything a
    /// script can cause, because a script's units are checked before a value is built.
    /// </exception>
    public double ValueIn(UnitSymbol unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return unit.Dimension == Dimension
            ? unit.FromSi(SiValue)
            : throw new ArgumentException(
                $"'{unit.Text}' is a {unit.Dimension} unit; this quantity is a {Dimension}.", nameof(unit));
    }

    /// <summary>Adds two quantities.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="result">The sum, or <c>default</c> when the operation is not defined.</param>
    /// <param name="error">Why the operation was refused, or <see cref="QuantityError.None"/>.</param>
    /// <returns><see langword="true"/> when a value was produced.</returns>
    /// <remarks>
    /// A reading plus a difference is a reading, in either order. Two readings are refused: that is
    /// <c>20 °C + 30 °C</c>, and the whole reason temperature and temperature difference are separate
    /// dimensions.
    /// </remarks>
    public static bool TryAdd(Quantity left, Quantity right, out Quantity result, out QuantityError error)
    {
        if (Refuse(left, right, out error))
        {
            result = default;
            return false;
        }

        if (left.Dimension == right.Dimension)
        {
            if (left.Dimension.Category == DimensionCategory.Absolute)
            {
                return Fail(QuantityError.AbsoluteAddition, out result, out error);
            }

            return Succeed(left.SiValue + right.SiValue, left.Dimension, out result, out error);
        }

        if (IsAbsoluteWithItsDifference(left, right, out var absolute))
        {
            return Succeed(left.SiValue + right.SiValue, absolute, out result, out error);
        }

        return Fail(QuantityError.DimensionMismatch, out result, out error);
    }

    /// <summary>Subtracts one quantity from another.</summary>
    /// <param name="left">The value subtracted from.</param>
    /// <param name="right">The value subtracted.</param>
    /// <param name="result">The difference, or <c>default</c> when the operation is not defined.</param>
    /// <param name="error">Why the operation was refused, or <see cref="QuantityError.None"/>.</param>
    /// <returns><see langword="true"/> when a value was produced.</returns>
    /// <remarks>
    /// Deliberately asymmetric. A reading minus a reading is a difference, and a reading minus a
    /// difference is a reading; a difference minus a reading is refused. Subtraction does not commute,
    /// and a type system that pretended it did would accept <c>30 dK − 20 °C</c> and return something.
    /// </remarks>
    public static bool TrySubtract(Quantity left, Quantity right, out Quantity result, out QuantityError error)
    {
        if (Refuse(left, right, out error))
        {
            result = default;
            return false;
        }

        var difference = left.SiValue - right.SiValue;

        if (left.Dimension == right.Dimension)
        {
            return left.Dimension.Category == DimensionCategory.Absolute
                ? Succeed(difference, left.Dimension.Delta!.Value, out result, out error)
                : Succeed(difference, left.Dimension, out result, out error);
        }

        if (left.Dimension.Category == DimensionCategory.Absolute
            && left.Dimension.Delta == right.Dimension)
        {
            return Succeed(difference, left.Dimension, out result, out error);
        }

        if (left.Dimension.Category == DimensionCategory.Delta
            && left.Dimension.Absolute == right.Dimension)
        {
            return Fail(QuantityError.AbsoluteSubtractedFromDifference, out result, out error);
        }

        return Fail(QuantityError.DimensionMismatch, out result, out error);
    }

    /// <summary>Multiplies two quantities.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="result">The product, or <c>default</c> when the operation is not defined.</param>
    /// <param name="error">Why the operation was refused, or <see cref="QuantityError.None"/>.</param>
    /// <returns><see langword="true"/> when a value was produced.</returns>
    /// <remarks>
    /// Exponent vectors add, so the result may have a dimension nobody named — legal inside an
    /// expression and refused only where it is stored. Scaling by a plain number is the exception that
    /// keeps its operand's dimension exactly, which is what makes <c>2 × 30 dK</c> a temperature
    /// difference rather than an anonymous kelvin.
    /// </remarks>
    public static bool TryMultiply(Quantity left, Quantity right, out Quantity result, out QuantityError error) =>
        TryCombine(left, right, left.SiValue * right.SiValue, multiply: true, out result, out error);

    /// <summary>Divides one quantity by another.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="result">The quotient, or <c>default</c> when the operation is not defined.</param>
    /// <param name="error">Why the operation was refused, or <see cref="QuantityError.None"/>.</param>
    /// <returns><see langword="true"/> when a value was produced.</returns>
    public static bool TryDivide(Quantity left, Quantity right, out Quantity result, out QuantityError error)
    {
        if (right.SiValue == 0)
        {
            return Fail(QuantityError.DivisionByZero, out result, out error);
        }

        return TryCombine(left, right, left.SiValue / right.SiValue, multiply: false, out result, out error);
    }

    /// <summary>Negates a quantity.</summary>
    /// <param name="value">The quantity to negate.</param>
    /// <param name="result">The negation, or <c>default</c> when the operation is not defined.</param>
    /// <param name="error">Why the operation was refused, or <see cref="QuantityError.None"/>.</param>
    /// <returns><see langword="true"/> when a value was produced.</returns>
    /// <remarks>Negating a reading on an affine scale is never what was meant, so it is refused.</remarks>
    public static bool TryNegate(Quantity value, out Quantity result, out QuantityError error) =>
        value.Dimension.Category switch
        {
            DimensionCategory.Absolute => Fail(QuantityError.AffineOperand, out result, out error),
            DimensionCategory.Nominal => Fail(QuantityError.NominalOperand, out result, out error),
            _ => Succeed(-value.SiValue, value.Dimension, out result, out error),
        };

    /// <summary>Determines whether two quantities are equal in value and dimension.</summary>
    /// <param name="other">The quantity to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when the SI magnitudes and dimensions match. <see cref="SourceUnit"/> is
    /// ignored: 30 kW and 30 000 W are the same quantity, and comparing the spelling would make them
    /// differ for a reason no user would recognise.
    /// </returns>
    public bool Equals(Quantity other) =>
        SiValue.Equals(other.SiValue) && Dimension == other.Dimension;

    /// <summary>Determines whether two quantities are equal including the unit they were written in.</summary>
    /// <param name="other">The quantity to compare with.</param>
    /// <returns><see langword="true"/> when value, dimension and spelling all match.</returns>
    /// <remarks>For the printer, which has to reproduce what was written rather than what it meant.</remarks>
    public bool EqualsExactly(Quantity other) =>
        Equals(other) && SourceUnit == other.SourceUnit;

    /// <summary>Serves as the hash function, consistent with <see cref="Equals(Quantity)"/>.</summary>
    /// <returns>A hash over the SI value and the dimension, excluding the source unit.</returns>
    public override int GetHashCode() => HashCode.Combine(SiValue, Dimension);

    /// <summary>Determines whether two quantities agree within tolerance.</summary>
    /// <param name="other">The quantity to compare with.</param>
    /// <param name="relativeTolerance">Relative tolerance; 1e-9 by default.</param>
    /// <returns>
    /// <see langword="true"/> when the dimensions match and the magnitudes differ by no more than
    /// <c>max(relativeTolerance × max(|a|, |b|), floor)</c>, where the floor is per dimension.
    /// </returns>
    /// <remarks>
    /// The floor is what stops a comparison near zero from demanding exactness that binary floating
    /// point cannot deliver: 0.1 + 0.2 differs from 0.3 by 5.6e-17, and without a floor that reaches a
    /// user-visible assertion.
    /// </remarks>
    public bool IsCloseTo(Quantity other, double relativeTolerance = DefaultRelativeTolerance)
    {
        if (Dimension != other.Dimension)
        {
            return false;
        }

        var scale = Math.Max(Math.Abs(SiValue), Math.Abs(other.SiValue));
        var tolerance = Math.Max(relativeTolerance * scale, AbsoluteFloor(Dimension));
        return Math.Abs(SiValue - other.SiValue) <= tolerance;
    }

    /// <summary>Renders the quantity for a message or a failing assertion.</summary>
    /// <returns>The SI magnitude and the dimension's SI unit, such as <c>30000 W</c>.</returns>
    public override string ToString()
    {
        var magnitude = SiValue.ToString("G6", CultureInfo.InvariantCulture);
        var unit = Dimension.SiUnit;
        return unit.Length == 0 ? magnitude : $"{magnitude} {unit}";
    }

    private static double AbsoluteFloor(Dimension dimension) => dimension.Id switch
    {
        DimensionId.Temperature or DimensionId.TemperatureDelta => 1e-9,
        DimensionId.Pressure or DimensionId.PressureDelta => 1e-6,
        _ => 1e-12,
    };

    private static bool TryCombine(
        Quantity left, Quantity right, double value, bool multiply, out Quantity result, out QuantityError error)
    {
        if (Refuse(left, right, out error))
        {
            result = default;
            return false;
        }

        if (left.Dimension.Category == DimensionCategory.Absolute
            || right.Dimension.Category == DimensionCategory.Absolute)
        {
            return Fail(QuantityError.AffineOperand, out result, out error);
        }

        // Scaling keeps the operand's dimension exactly, tags and all, so 2 x 30 dK stays a
        // temperature difference instead of resolving to an anonymous kelvin.
        if (right.Dimension == Dimension.Dimensionless)
        {
            return Succeed(value, left.Dimension, out result, out error);
        }

        if (multiply && left.Dimension == Dimension.Dimensionless)
        {
            return Succeed(value, right.Dimension, out result, out error);
        }

        var vector = multiply
            ? left.Dimension.Vector + right.Dimension.Vector
            : left.Dimension.Vector - right.Dimension.Vector;
        return Succeed(value, Dimension.FromVector(vector), out result, out error);
    }

    private static bool IsAbsoluteWithItsDifference(Quantity left, Quantity right, out Dimension absolute)
    {
        if (left.Dimension.Category == DimensionCategory.Absolute && left.Dimension.Delta == right.Dimension)
        {
            absolute = left.Dimension;
            return true;
        }

        if (right.Dimension.Category == DimensionCategory.Absolute && right.Dimension.Delta == left.Dimension)
        {
            absolute = right.Dimension;
            return true;
        }

        absolute = default;
        return false;
    }

    private static bool Refuse(Quantity left, Quantity right, out QuantityError error)
    {
        error = left.Dimension.Category == DimensionCategory.Nominal
                || right.Dimension.Category == DimensionCategory.Nominal
            ? QuantityError.NominalOperand
            : QuantityError.None;
        return error != QuantityError.None;
    }

    private static bool Fail(QuantityError reason, out Quantity result, out QuantityError error)
    {
        result = default;
        error = reason;
        return false;
    }

    private static bool Succeed(double value, Dimension dimension, out Quantity result, out QuantityError error)
    {
        result = FromSi(value, dimension);
        error = QuantityError.None;
        return true;
    }
}
