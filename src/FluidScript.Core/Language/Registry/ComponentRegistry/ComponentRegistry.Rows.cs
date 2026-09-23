using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

public sealed partial class ComponentRegistry
{
    // Expression-bodied on purpose (C-99). `Default` is initialised at the top of the class, before a
    // static field declared below it would be, so a stored value here was still `default(Dimension)` --
    // unnamed, with no vector -- when the shared registry read it, and u, ua and fouling were
    // dimensionless in every model. Computing on access has no order to get wrong.

    // W/K: the exchanger's thermal size, independent of how it is achieved. Unnamed: `13` names no
    // conductance, and `ToSiUnitString` spells the vector W/K.
    private static Dimension ConductancePerKelvin =>
        Dimension.FromVector(new DimensionVector(Mass: 1, Length: 2, Time: -3, Temperature: -1));

    // W/(m²·K) and m²·K/W are named since D-119 (L-54), so a script can write the unit and the wire reports it.
    private static Dimension HeatTransferCoefficient => Dimension.HeatTransferCoefficient;

    private static Dimension FoulingResistance => Dimension.ThermalResistance;

    private static PortInfo Port(string name, PortRole role, bool optional = false) =>
        new() { Name = name, Role = role, IsOptional = optional };

    // `D-120` respelled the surface and not the model: a row written `in[2].t` is stored as `in2`,
    // which every reader of StatedParameters, every port key and every published property has used
    // since before the spelling changed, and the old spelling is exactly that key -- so one word
    // says all three. The suggestion `FS1536` offers is the row's Name.
    private static ParameterInfo Keyed(ParameterInfo row, string key) =>
        row with { Key = key, LegacySpellings = [key] };

    private static PropertyInfo Keyed(PropertyInfo row, string key) =>
        row with { Key = key, LegacySpellings = [key] };

    private static PortInfo Keyed(PortInfo row, string key) =>
        row with { Key = key, LegacySpellings = [key] };

    private static ParameterInfo Sized(
        string name, Dimension dimension, double min, double max, int precision) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Quantity,
            Dimension = dimension,
            OmissionBehavior = ParameterOmissionBehavior.Size,
            UsualRange = SiRange(min, max, dimension),
            DisplayPrecision = precision,
        };

    private static ParameterInfo Defaulted(
        string name,
        Dimension dimension,
        double min,
        double max,
        string literal,
        string basis,
        int precision) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Quantity,
            Dimension = dimension,
            OmissionBehavior = ParameterOmissionBehavior.Default,
            DefaultLiteral = literal,
            DefaultBasis = basis,
            UsualRange = SiRange(min, max, dimension),
            DisplayPrecision = precision,
        };

    private static ParameterInfo Symbol(
        string name, ImmutableArray<string> accepted, string literal, string basis) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Symbol,
            Dimension = Dimension.Dimensionless,
            AcceptedSymbols = accepted,
            OmissionBehavior = ParameterOmissionBehavior.Default,
            DefaultLiteral = literal,
            DefaultBasis = basis,
            DisplayPrecision = 0,
        };

    // The ranges in `22`'s tables are written the way a user writes a value: a bare number in the
    // dimension's canonical unit. Converting here rather than hand-writing SI numbers is what keeps
    // `-50 … 300` for a temperature from being transcribed as -50 K.
    private static Range<double> SiRange(double min, double max, Dimension dimension) =>
        new(
            Quantity.FromBareNumber(min, dimension).SiValue,
            Quantity.FromBareNumber(max, dimension).SiValue);

    private static PropertyInfo Declared(string name, Dimension dimension) =>
        Property(name, dimension, PropertyAvailability.Declared);

    private static PropertyInfo Sized(string name, Dimension dimension) =>
        Property(name, dimension, PropertyAvailability.Sized);

    private static PropertyInfo Solved(string name, Dimension dimension) =>
        Property(name, dimension, PropertyAvailability.Solved);

    private static PropertyInfo Property(string name, Dimension dimension, PropertyAvailability availability) =>
        new()
        {
            Name = name,
            Dimension = dimension,
            Availability = availability,
            CanonicalUnit = UnitTable.CanonicalUnitFor(dimension)?.Text ?? dimension.SiUnit,
        };

    /// <summary>The bounds outside which a parameter's value is an error, with the code that says so.</summary>
    /// <param name="descriptor">The code raised for a value outside the range.</param>
    /// <param name="low">The lowest accepted value, in the dimension's canonical unit.</param>
    /// <param name="high">The highest accepted value, in the dimension's canonical unit.</param>
    /// <param name="wholeNumber">Whether a fractional value is an error as well.</param>
    /// <returns>The validity rule to hang on a parameter.</returns>
    /// <remarks>
    /// Every bounded parameter in v1 is dimensionless, so no conversion is involved yet. It goes
    /// through <see cref="SiRange"/> anyway, for the reason that method exists: a bound written the way
    /// a user writes a value is the only form the tables in <c>22</c> can be checked against by eye.
    /// </remarks>
    private static ParameterValidity Bounded(
        DiagnosticDescriptor descriptor, double low, double high, bool wholeNumber = false) => new()
        {
            Range = SiRange(low, high, Dimension.Dimensionless),
            Descriptor = descriptor,
            RequiresWholeNumber = wholeNumber,
        };

    /// <summary>One relation over a kind's parameters, for the over-determination count.</summary>
    /// <param name="descriptor">The code raised when too many members are stated.</param>
    /// <param name="freedoms">How many members may be stated before the group is over-determined.</param>
    /// <param name="parameters">The canonical parameter names the relation ties together.</param>
    /// <returns>The group to hang on a kind.</returns>
    private static ParameterGroupInfo Group(
        DiagnosticDescriptor descriptor, int freedoms, params ReadOnlySpan<string> parameters) => new()
        {
            Parameters = [.. parameters],
            Freedoms = freedoms,
            Descriptor = descriptor,
        };

    /// <summary>A relation whose members must be stated a fixed number of times, no more and no fewer.</summary>
    /// <param name="over">The code raised when too many members are stated.</param>
    /// <param name="under">The code raised when too few are.</param>
    /// <param name="freedoms">How many members must be stated.</param>
    /// <param name="parameters">The canonical parameter names the relation ties together.</param>
    /// <returns>The group to hang on a kind.</returns>
    /// <remarks>
    /// A boundary's <c>flow</c> and <c>p</c> are the case: exactly one, because a stream is fixed by one
    /// hydraulic condition and over-determined by two. <see cref="Group"/> is the same thing with no
    /// lower bound, which is what every other relation in the registry wants — an exchanger with none of
    /// <c>power</c>, <c>in</c>, <c>out</c> and <c>flow</c> stated is a component sizing has yet to reach,
    /// not an error.
    /// </remarks>
    private static ParameterGroupInfo Exactly(
        DiagnosticDescriptor over,
        DiagnosticDescriptor under,
        int freedoms,
        params ReadOnlySpan<string> parameters) => new()
        {
            Parameters = [.. parameters],
            Freedoms = freedoms,
            Minimum = freedoms,
            Descriptor = over,
            MinimumDescriptor = under,
        };

    /// <summary>A parameter the kind has no answer without (<c>D-64</c>).</summary>
    /// <param name="name">The canonical parameter name.</param>
    /// <param name="dimension">Its dimension.</param>
    /// <param name="min">The low end of its usual range, in the canonical unit.</param>
    /// <param name="max">The high end.</param>
    /// <param name="precision">Decimal places when the value is displayed.</param>
    /// <returns>The parameter to hang on a kind.</returns>
    /// <remarks>
    /// Deliberately rare. Use it only where every substitute would be a guess about the plant rather
    /// than about the model — which, so far, is a boundary's temperature and nothing else.
    /// </remarks>
    private static ParameterInfo Required(
        string name, Dimension dimension, double min, double max, int precision) => new()
        {
            Name = name,
            ValueKind = ParameterValueKind.Quantity,
            Dimension = dimension,
            OmissionBehavior = ParameterOmissionBehavior.Require,
            UsualRange = SiRange(min, max, dimension),
            DisplayPrecision = precision,
        };

    private static ImmutableDictionary<string, ParameterInfo> Parameters(params ParameterInfo[] parameters) =>
        parameters.ToImmutableDictionary(static parameter => parameter.Key, StringComparer.Ordinal);

    private static ImmutableDictionary<string, PropertyInfo> Properties(params PropertyInfo[] properties) =>
        properties.ToImmutableDictionary(static property => property.Name, StringComparer.Ordinal);
}
