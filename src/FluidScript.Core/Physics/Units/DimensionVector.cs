using System.Text;

namespace FluidScript.Core.Units;

/// <summary>
/// The exponents of the SI base dimensions that make up a physical quantity.
/// </summary>
/// <param name="Mass">Exponent of mass, whose SI base unit is the kilogram.</param>
/// <param name="Length">Exponent of length, whose SI base unit is the metre.</param>
/// <param name="Time">Exponent of time, whose SI base unit is the second.</param>
/// <param name="Temperature">Exponent of thermodynamic temperature, whose SI base unit is the kelvin.</param>
/// <remarks>
/// <para>
/// Four base dimensions cover everything the language models: pressure is <c>M L⁻¹ T⁻²</c>, power is
/// <c>M L² T⁻³</c>, specific heat is <c>L² T⁻² Θ⁻¹</c>. Amount of substance, electric current and
/// luminous intensity are absent because nothing in a hydronic or air-side circuit needs them, and an
/// unused base dimension is a column every vector carries and no test exercises.
/// </para>
/// <para>
/// This is what makes multiplication and division total. A named dimension is a <em>label looked up
/// from</em> a vector rather than the representation, so <c>Power ÷ (SpecificHeat × TemperatureDelta)</c>
/// resolves to <see cref="Dimension.MassFlow"/> without anyone having enumerated that combination,
/// and <c>Power ÷ TemperatureDelta</c> is computable even though W/K has no name in the language.
/// </para>
/// </remarks>
public readonly record struct DimensionVector(int Mass, int Length, int Time, int Temperature)
{
    /// <summary>Gets the vector of a dimensionless quantity, with every exponent zero.</summary>
    public static DimensionVector None => default;

    /// <summary>Gets a value indicating whether every exponent is zero.</summary>
    /// <value><see langword="true"/> for a ratio, an efficiency or a count.</value>
    public bool IsNone => this == default;

    /// <summary>Adds two vectors, which is what multiplying their quantities does.</summary>
    /// <param name="left">The left vector.</param>
    /// <param name="right">The right vector.</param>
    /// <returns>The exponent-wise sum.</returns>
    public static DimensionVector operator +(DimensionVector left, DimensionVector right) =>
        new(left.Mass + right.Mass,
            left.Length + right.Length,
            left.Time + right.Time,
            left.Temperature + right.Temperature);

    /// <summary>Subtracts two vectors, which is what dividing their quantities does.</summary>
    /// <param name="left">The left vector.</param>
    /// <param name="right">The right vector.</param>
    /// <returns>The exponent-wise difference.</returns>
    public static DimensionVector operator -(DimensionVector left, DimensionVector right) =>
        new(left.Mass - right.Mass,
            left.Length - right.Length,
            left.Time - right.Time,
            left.Temperature - right.Temperature);

    /// <summary>Negates every exponent, which is what taking a reciprocal does.</summary>
    /// <param name="value">The vector to invert.</param>
    /// <returns>The exponent-wise negation.</returns>
    public static DimensionVector operator -(DimensionVector value) => None - value;

    /// <summary>Adds two vectors.</summary>
    /// <param name="left">The left vector.</param>
    /// <param name="right">The right vector.</param>
    /// <returns>The exponent-wise sum.</returns>
    /// <remarks>The named equivalent of <see cref="op_Addition"/>, required by analyzer rule CA2225.</remarks>
    public static DimensionVector Add(DimensionVector left, DimensionVector right) => left + right;

    /// <summary>Subtracts two vectors.</summary>
    /// <param name="left">The left vector.</param>
    /// <param name="right">The right vector.</param>
    /// <returns>The exponent-wise difference.</returns>
    /// <remarks>The named equivalent of <see cref="op_Subtraction(DimensionVector, DimensionVector)"/>.</remarks>
    public static DimensionVector Subtract(DimensionVector left, DimensionVector right) => left - right;

    /// <summary>Negates every exponent.</summary>
    /// <param name="value">The vector to invert.</param>
    /// <returns>The exponent-wise negation.</returns>
    /// <remarks>The named equivalent of <see cref="op_UnaryNegation"/>.</remarks>
    public static DimensionVector Negate(DimensionVector value) => -value;

    /// <summary>Renders the vector as an SI unit a user could read in a message.</summary>
    /// <returns>
    /// The shortest reasonable spelling: a named SI unit where the whole vector is one
    /// (<c>W</c>, <c>Pa</c>), a named unit over base units where that is shorter
    /// (<c>W/K</c> for <c>M L² T⁻³ Θ⁻¹</c>), and base units otherwise (<c>kg·m²/s³</c>). Empty for a
    /// dimensionless vector.
    /// </returns>
    /// <remarks>
    /// Only diagnostics read this, and it exists because <c>FS1304</c> has to name the unit a value
    /// arrived with. <c>W/K</c> tells a user what they wrote; <c>kg·m²·s⁻³·K⁻¹</c> tells them to go
    /// and work it out.
    /// </remarks>
    public string ToSiUnitString()
    {
        if (IsNone)
        {
            return string.Empty;
        }

        foreach (var (vector, symbol) in NamedUnits)
        {
            if (vector == this)
            {
                return symbol;
            }
        }

        // A named unit plus a short remainder beats spelling the whole thing in base units: the
        // vector for W/K has four non-zero exponents and reads as one term over one.
        var best = default(string);
        foreach (var (vector, symbol) in NamedUnits)
        {
            var remainder = this - vector;
            if (CountTerms(remainder) > 1)
            {
                continue;
            }

            var candidate = Render(remainder) is { Length: > 0 } tail
                ? (tail.StartsWith('/') ? symbol + tail : $"{symbol}·{tail}")
                : symbol;
            if (best is null || candidate.Length < best.Length)
            {
                best = candidate;
            }
        }

        return best ?? Render(this).TrimStart('·');
    }

    private static readonly (DimensionVector Vector, string Symbol)[] NamedUnits =
    [
        (new DimensionVector(1, 2, -3, 0), "W"),
        (new DimensionVector(1, 2, -2, 0), "J"),
        (new DimensionVector(1, -1, -2, 0), "Pa"),
        (new DimensionVector(1, 1, -2, 0), "N"),
    ];

    private static int CountTerms(DimensionVector vector) =>
        (vector.Mass == 0 ? 0 : 1)
        + (vector.Length == 0 ? 0 : 1)
        + (vector.Time == 0 ? 0 : 1)
        + (vector.Temperature == 0 ? 0 : 1);

    private static string Render(DimensionVector vector)
    {
        var numerator = new StringBuilder();
        var denominator = new StringBuilder();

        foreach (var (exponent, symbol) in new[]
                 {
                     (vector.Mass, "kg"), (vector.Length, "m"), (vector.Time, "s"), (vector.Temperature, "K"),
                 })
        {
            if (exponent == 0)
            {
                continue;
            }

            var target = exponent > 0 ? numerator : denominator;
            if (target.Length > 0)
            {
                target.Append('·');
            }

            target.Append(symbol).Append(Superscript(Math.Abs(exponent)));
        }

        if (denominator.Length == 0)
        {
            return numerator.ToString();
        }

        var below = denominator.ToString();
        var needsBrackets = below.Contains('·', StringComparison.Ordinal);
        return $"{numerator}/{(needsBrackets ? $"({below})" : below)}";
    }

    private static string Superscript(int exponent) => exponent switch
    {
        1 => string.Empty,
        2 => "²",
        3 => "³",
        4 => "⁴",
        5 => "⁵",
        _ => $"^{exponent}",
    };
}
