namespace FluidScript.Core.Units;

/// <summary>
/// The closed set of dimensions the language names.
/// </summary>
/// <remarks>
/// Adding a member is a language change and requires a decision-log entry. The set is owned by
/// <c>plan/10-language/13-type-and-unit-system.md</c>.
/// </remarks>
public enum DimensionId
{
    /// <summary>A quantity with no named dimension, produced by arithmetic and never storable.</summary>
    /// <remarks>
    /// <c>30 kW / 10 dK</c> is legal inside an expression and carries this. It becomes an error only
    /// where it is stored — a parameter, or a binding a parameter reads.
    /// </remarks>
    Unnamed,

    /// <summary>A ratio, an efficiency or a count.</summary>
    Dimensionless,

    /// <summary>A distance. SI metre; a bare number is metres.</summary>
    Length,

    /// <summary>An absolute temperature. SI kelvin; a bare number is degrees Celsius.</summary>
    Temperature,

    /// <summary>A temperature difference, written <c>dK</c> or <c>dC</c>.</summary>
    TemperatureDelta,

    /// <summary>A gauge pressure. SI pascal, relative to atmosphere; a bare number is kilopascals.</summary>
    Pressure,

    /// <summary>A pressure difference. SI pascal; a bare number is kilopascals.</summary>
    PressureDelta,

    /// <summary>A rate of energy transfer. SI watt; a bare number is kilowatts.</summary>
    Power,

    /// <summary>A quantity of energy. SI joule.</summary>
    Energy,

    /// <summary>A mass flow rate. SI kilogram per second.</summary>
    MassFlow,

    /// <summary>A volumetric flow rate. SI cubic metre per second.</summary>
    VolumeFlow,

    /// <summary>A mass. SI kilogram.</summary>
    Mass,

    /// <summary>A duration. SI second.</summary>
    Time,

    /// <summary>A speed. SI metre per second.</summary>
    Velocity,

    /// <summary>A mass per unit volume. SI kilogram per cubic metre.</summary>
    Density,

    /// <summary>A specific heat capacity. SI joule per kilogram kelvin.</summary>
    SpecificHeat,

    /// <summary>A specific enthalpy. SI joule per kilogram.</summary>
    Enthalpy,

    /// <summary>An area. SI square metre.</summary>
    Area,

    /// <summary>A volume. SI cubic metre; a bare number is cubic decimetres.</summary>
    Volume,

    /// <summary>A valve flow coefficient, in cubic metres per hour at one bar of differential.</summary>
    /// <remarks>Its own dimension so it can never be added to the volume flow it shares a unit with.</remarks>
    Kv,

    /// <summary>A pump head, in metres of the pumped fluid.</summary>
    /// <remarks>
    /// Distinct from <see cref="Length"/> despite sharing the metre, and bare-only: metres of the
    /// pumped fluid are not metres of water column, so no symbol may spell it.
    /// </remarks>
    Head,

    /// <summary>A nominal diameter designation, such as DN50.</summary>
    /// <remarks>A name rather than a measurement: DN25 steel pipe has a 27.3 mm bore.</remarks>
    NominalDiameter,

    /// <summary>A screen distance. Presentation only; never crosses into physics.</summary>
    Pixels,
}
