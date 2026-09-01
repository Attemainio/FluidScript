using System.Collections.Immutable;

namespace FluidScript.Core.Units;

/// <summary>
/// What a quantity measures: a named dimension such as power, or an unnamed one produced by arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// A dimension is an exponent vector plus an identity. The vector alone is not enough — temperature
/// and temperature difference share one, as do length and pump head — and the identity alone is not
/// enough, because multiplication has to produce combinations nobody named.
/// </para>
/// <para>
/// Equality is by identity, not by vector, which is the whole point: <c>Length</c> and <c>Head</c> are
/// unequal and therefore cannot be added, even though both are metres.
/// </para>
/// </remarks>
public readonly record struct Dimension
{
    private readonly DimensionId _id;
    private readonly DimensionVector _unnamedVector;

    private Dimension(DimensionId id, DimensionVector unnamedVector)
    {
        _id = id;
        _unnamedVector = unnamedVector;
    }

    /// <summary>Gets the dimension's identity.</summary>
    /// <value><see cref="DimensionId.Unnamed"/> for a combination the language does not name.</value>
    public DimensionId Id => _id;

    /// <summary>Gets a value indicating whether arithmetic may produce this dimension from a vector.</summary>
    /// <value>
    /// <see langword="false"/> for <see cref="Head"/>, <see cref="Kv"/>,
    /// <see cref="NominalDiameter"/> and <see cref="Pixels"/>, which only ever come from something a
    /// user wrote.
    /// </value>
    public bool IsSynthesisable => !IsNamed || Entries[(int)_id].Synthesisable;

    /// <summary>Gets a value indicating whether the language has a name for this dimension.</summary>
    /// <value><see langword="false"/> for a combination such as W/K, which may be computed but not stored.</value>
    public bool IsNamed => _id != DimensionId.Unnamed;

    /// <summary>Gets the exponents of the SI base dimensions.</summary>
    public DimensionVector Vector => IsNamed ? Entries[(int)_id].Vector : _unnamedVector;

    /// <summary>Gets how this dimension behaves under arithmetic.</summary>
    /// <value><see cref="DimensionCategory.Linear"/> for an unnamed dimension, which is always a ratio quantity.</value>
    public DimensionCategory Category =>
        IsNamed ? Entries[(int)_id].Category : DimensionCategory.Linear;

    /// <summary>Gets the SI base unit this dimension's values are stored in.</summary>
    /// <value>
    /// A symbol such as <c>W</c> or <c>kg/s</c>, derived from the exponents for an unnamed dimension.
    /// Empty for a dimensionless quantity and for a designation.
    /// </value>
    public string SiUnit => IsNamed ? Entries[(int)_id].SiUnit : _unnamedVector.ToSiUnitString();

    /// <summary>Gets the unit a bare number in a script means for this dimension.</summary>
    /// <value>
    /// A symbol such as <c>kW</c>, or <see langword="null"/> where a bare number is the SI value
    /// itself or the dimension is a designation. Never varies by parameter: one dimension, one
    /// canonical unit, everywhere.
    /// </value>
    public string? CanonicalUnit => IsNamed ? Entries[(int)_id].CanonicalUnit : null;

    /// <summary>Gets the unit this dimension is printed in by default.</summary>
    /// <value>
    /// A symbol such as <c>kJ/kg</c>, or <see langword="null"/> where there is nothing to print.
    /// Independent of <see cref="CanonicalUnit"/>: the wire may carry J/kg while a tooltip says kJ/kg,
    /// because a reader and a parser want different things.
    /// </value>
    public string? DisplayUnit => IsNamed ? Entries[(int)_id].DisplayUnit : null;

    /// <summary>Gets a value indicating whether a bare number means something other than the SI unit.</summary>
    /// <value>
    /// <see langword="true"/> for the five rows where a bare number carries a different scale from SI,
    /// spelling four distinct units: °C, kPa (twice), kW and dm³.
    /// </value>
    /// <remarks>
    /// The test is the conversion, not the spelling. <c>dK</c> is not one of these: it differs from
    /// <c>K</c> in what it means, not in what it is worth, and counting it would make the documented
    /// count of exceptions wrong by one.
    /// </remarks>
    public bool CanonicalDiffersFromSi => UnitTable.CanonicalUnitFor(this) is { } canonical
        && (canonical.Factor != 1 || canonical.Offset != 0);

    /// <summary>Gets the name of this dimension.</summary>
    /// <value>The <see cref="DimensionId"/> name, or the derived SI unit for an unnamed dimension.</value>
    public string Name => IsNamed ? _id.ToString() : SiUnit;

    /// <summary>Gets the difference dimension paired with this absolute one.</summary>
    /// <value><see langword="null"/> unless <see cref="Category"/> is <see cref="DimensionCategory.Absolute"/>.</value>
    public Dimension? Delta => _id switch
    {
        DimensionId.Temperature => TemperatureDelta,
        DimensionId.Pressure => PressureDelta,
        _ => null,
    };

    /// <summary>Gets the absolute dimension paired with this difference.</summary>
    /// <value><see langword="null"/> unless <see cref="Category"/> is <see cref="DimensionCategory.Delta"/>.</value>
    public Dimension? Absolute => _id switch
    {
        DimensionId.TemperatureDelta => Temperature,
        DimensionId.PressureDelta => Pressure,
        _ => null,
    };

    /// <summary>Gets every named dimension, in declaration order.</summary>
    public static ImmutableArray<Dimension> All { get; } =
        [.. Enum.GetValues<DimensionId>()
            .Where(static id => id != DimensionId.Unnamed)
            .Select(static id => new Dimension(id, default))];

    /// <summary>Gets the dimension a value with no dimension has.</summary>
    public static Dimension Dimensionless => Named(DimensionId.Dimensionless);

    /// <summary>Gets the length dimension.</summary>
    public static Dimension Length => Named(DimensionId.Length);

    /// <summary>Gets the absolute-temperature dimension.</summary>
    public static Dimension Temperature => Named(DimensionId.Temperature);

    /// <summary>Gets the temperature-difference dimension.</summary>
    public static Dimension TemperatureDelta => Named(DimensionId.TemperatureDelta);

    /// <summary>Gets the gauge-pressure dimension.</summary>
    public static Dimension Pressure => Named(DimensionId.Pressure);

    /// <summary>Gets the pressure-difference dimension.</summary>
    public static Dimension PressureDelta => Named(DimensionId.PressureDelta);

    /// <summary>Gets the power dimension.</summary>
    public static Dimension Power => Named(DimensionId.Power);

    /// <summary>Gets the energy dimension.</summary>
    public static Dimension Energy => Named(DimensionId.Energy);

    /// <summary>Gets the mass-flow dimension.</summary>
    public static Dimension MassFlow => Named(DimensionId.MassFlow);

    /// <summary>Gets the volume-flow dimension.</summary>
    public static Dimension VolumeFlow => Named(DimensionId.VolumeFlow);

    /// <summary>Gets the mass dimension.</summary>
    public static Dimension Mass => Named(DimensionId.Mass);

    /// <summary>Gets the time dimension.</summary>
    public static Dimension Time => Named(DimensionId.Time);

    /// <summary>Gets the velocity dimension.</summary>
    public static Dimension Velocity => Named(DimensionId.Velocity);

    /// <summary>Gets the density dimension.</summary>
    public static Dimension Density => Named(DimensionId.Density);

    /// <summary>Gets the specific-heat dimension.</summary>
    public static Dimension SpecificHeat => Named(DimensionId.SpecificHeat);

    /// <summary>Gets the specific-enthalpy dimension.</summary>
    public static Dimension Enthalpy => Named(DimensionId.Enthalpy);

    /// <summary>Gets the area dimension.</summary>
    public static Dimension Area => Named(DimensionId.Area);

    /// <summary>Gets the volume dimension.</summary>
    public static Dimension Volume => Named(DimensionId.Volume);

    /// <summary>Gets the valve-flow-coefficient dimension.</summary>
    public static Dimension Kv => Named(DimensionId.Kv);

    /// <summary>Gets the pump-head dimension.</summary>
    public static Dimension Head => Named(DimensionId.Head);

    /// <summary>Gets the nominal-diameter designation dimension.</summary>
    public static Dimension NominalDiameter => Named(DimensionId.NominalDiameter);

    /// <summary>Gets the screen-distance dimension.</summary>
    public static Dimension Pixels => Named(DimensionId.Pixels);

    /// <summary>Gets the dimension for a named identity.</summary>
    /// <param name="id">The identity to look up.</param>
    /// <returns>The dimension. <see cref="DimensionId.Unnamed"/> yields a dimensionless unnamed dimension.</returns>
    public static Dimension Named(DimensionId id) => new(id, default);

    /// <summary>Finds the dimension that arithmetic on an exponent vector produces.</summary>
    /// <param name="vector">The exponents the operation produced.</param>
    /// <returns>
    /// The linear dimension carrying that vector; failing that the difference dimension carrying it;
    /// otherwise an unnamed dimension holding the vector.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>An absolute dimension is never produced here, and that is deliberate.</strong>
    /// Dividing an enthalpy by a specific heat gives kelvin, but what it means is a temperature
    /// <em>difference</em> — arithmetic on ratio quantities cannot manufacture a reading on an affine
    /// scale, because nothing in the operands says where that scale's zero is. So Θ resolves to
    /// <see cref="TemperatureDelta"/>, never <see cref="Temperature"/>.
    /// </para>
    /// <para>
    /// <see cref="Head"/>, <see cref="Kv"/>, <see cref="NominalDiameter"/> and <see cref="Pixels"/>
    /// are never produced either. They can only come from something a user wrote, because otherwise a
    /// length divided by a time would silently become a valve coefficient, and a length times a number
    /// would become a pump head.
    /// </para>
    /// </remarks>
    public static Dimension FromVector(DimensionVector vector)
    {
        foreach (var category in new[] { DimensionCategory.Linear, DimensionCategory.Delta })
        {
            for (var index = 1; index < Entries.Length; index++)
            {
                var entry = Entries[index];
                if (entry.Synthesisable && entry.Category == category && entry.Vector == vector)
                {
                    return Named((DimensionId)index);
                }
            }
        }

        return new Dimension(DimensionId.Unnamed, vector);
    }

    /// <summary>Renders the dimension for a message.</summary>
    /// <returns><see cref="Name"/>.</returns>
    public override string ToString() => Name;

    private readonly record struct Entry(
        DimensionVector Vector,
        DimensionCategory Category,
        string SiUnit,
        string? CanonicalUnit,
        string? DisplayUnit,
        bool Synthesisable = true);

    // Indexed by DimensionId. The canonical column is what a bare number means; it equals the SI
    // column except on the five rows spelling the four documented exceptions -- degC, kPa (twice),
    // kW and dm3.
    private static readonly Entry[] Entries =
    [
        new(default, DimensionCategory.Linear, "", null, null),                                              // Unnamed
        new(default, DimensionCategory.Linear, "", null, null),                                              // Dimensionless
        new(new DimensionVector(0, 1, 0, 0), DimensionCategory.Linear, "m", "m", "m"),                       // Length
        new(new DimensionVector(0, 0, 0, 1), DimensionCategory.Absolute, "K", "°C", "°C"),                   // Temperature
        new(new DimensionVector(0, 0, 0, 1), DimensionCategory.Delta, "K", "dK", "K"),                       // TemperatureDelta
        new(new DimensionVector(1, -1, -2, 0), DimensionCategory.Absolute, "Pa", "kPa", "kPa"),              // Pressure
        new(new DimensionVector(1, -1, -2, 0), DimensionCategory.Delta, "Pa", "kPa", "kPa"),                 // PressureDelta
        new(new DimensionVector(1, 2, -3, 0), DimensionCategory.Linear, "W", "kW", "kW"),                    // Power
        new(new DimensionVector(1, 2, -2, 0), DimensionCategory.Linear, "J", "J", "kWh"),                    // Energy
        new(new DimensionVector(1, 0, -1, 0), DimensionCategory.Linear, "kg/s", "kg/s", "kg/s"),             // MassFlow
        new(new DimensionVector(0, 3, -1, 0), DimensionCategory.Linear, "m3/s", "m3/s", "l/s"),              // VolumeFlow
        new(new DimensionVector(1, 0, 0, 0), DimensionCategory.Linear, "kg", "kg", "kg"),                    // Mass
        new(new DimensionVector(0, 0, 1, 0), DimensionCategory.Linear, "s", "s", "s"),                       // Time
        new(new DimensionVector(0, 1, -1, 0), DimensionCategory.Linear, "m/s", "m/s", "m/s"),                // Velocity
        new(new DimensionVector(1, -3, 0, 0), DimensionCategory.Linear, "kg/m3", "kg/m3", "kg/m3"),          // Density
        new(new DimensionVector(0, 2, -2, -1), DimensionCategory.Linear, "J/(kg*K)", "J/(kg*K)", "kJ/(kg*K)"), // SpecificHeat
        new(new DimensionVector(0, 2, -2, 0), DimensionCategory.Linear, "J/kg", "J/kg", "kJ/kg"),            // Enthalpy
        new(new DimensionVector(0, 2, 0, 0), DimensionCategory.Linear, "m2", "m2", "m2"),                    // Area
        new(new DimensionVector(0, 3, 0, 0), DimensionCategory.Linear, "m3", "dm3", "l"),                    // Volume
        new(new DimensionVector(0, 3, -1, 0), DimensionCategory.Nominal, "m3/h", null, null, false),         // Kv
        new(new DimensionVector(0, 1, 0, 0), DimensionCategory.Linear, "m", null, "m", false),               // Head
        new(default, DimensionCategory.Nominal, "", null, "DN", false),                                      // NominalDiameter
        new(default, DimensionCategory.Linear, "px", "px", "px", false),                                     // Pixels
    ];
}
