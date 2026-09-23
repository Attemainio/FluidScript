using System.Globalization;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Fluids.Substances;

/// <summary>Shared plumbing every substance needs: the atmosphere, the range check, the failures.</summary>
/// <remarks>
/// <para>
/// <strong>The gauge/absolute boundary lives here and nowhere else.</strong> <c>13</c> defines
/// <see cref="Dimension.Pressure"/> as gauge in SI — the absolute spellings <c>bara</c> and <c>kPaa</c>
/// normalise to it with an offset at the language boundary — so every pressure the model carries is
/// gauge, and the atmosphere is added once, immediately before a measurement (<c>D-26</c>).
/// </para>
/// <para>
/// <c>21</c>'s snippet names the parameter <c>absolutePressure</c>, which contradicts both that
/// definition and its own "the single adapter adds the model's recorded atmosphere". Recorded as
/// <c>C-10</c>.
/// </para>
/// </remarks>
public abstract class SubstanceBase : ISubstance
{
    /// <summary>The atmosphere a gauge pressure is measured from.</summary>
    /// <value>Pa. 101 325 in v1, per <c>D-26</c>; a model-recorded value is post-v1.</value>
    protected const double Atmosphere = UnitTable.StandardAtmosphere;

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract StateRange ValidRange { get; }

    /// <inheritdoc/>
    public abstract Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature);

    /// <inheritdoc/>
    public abstract Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy);

    /// <inheritdoc/>
    public abstract Result<Quantity> FreezingPoint(Quantity gaugePressure);

    /// <inheritdoc/>
    public abstract Result<Quantity> SaturationPressure(Quantity temperature);

    /// <inheritdoc/>
    /// <remarks>
    /// Virtual rather than abstract, and refusing rather than answering, because most substances have
    /// no vapour phase and therefore no isentropic compression to describe. That is not a gap to fill
    /// later: <c>ISubstance</c> returns <c>Result</c> precisely so "this substance cannot say" is an
    /// answer. A substance that <em>can</em> overrides.
    /// </remarks>
    public virtual Result<FluidState> FromPressureEntropy(Quantity gaugePressure, Quantity entropy) =>
        Result.Failure<FluidState>(NotEvaluable(
            "a state from pressure and entropy", Describe(double.NaN, Absolute(gaugePressure))));

    /// <inheritdoc/>
    /// <remarks>
    /// Refused by default for the same reason, and humid air is the case that proves it is the right
    /// default rather than a shortcut: moist air has a dew point, not a saturation temperature, and
    /// answering with one would be inventing a curve it does not have.
    /// </remarks>
    public virtual Result<Quantity> SaturationTemperature(Quantity gaugePressure) =>
        Result.Failure<Quantity>(NotEvaluable(
            "a saturation temperature", Describe(double.NaN, Absolute(gaugePressure))));

    /// <summary>Converts a gauge pressure the model holds into the absolute one a backend needs.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <returns>Pa absolute.</returns>
    protected static double Absolute(Quantity gaugePressure) => gaugePressure.SiValue + Atmosphere;

    /// <summary>Builds the <c>FS2003</c> a state outside the validated domain fails with.</summary>
    /// <param name="quantity">What was out of range: a temperature in K, or a pressure in Pa absolute.</param>
    /// <param name="low">The bound's lower end, in the same terms.</param>
    /// <param name="high">The bound's upper end.</param>
    /// <param name="unit">How to spell the three of them in the message.</param>
    /// <returns>The failure, carrying everything a diagnostic needs and emitting nothing.</returns>
    protected ResultError OutsideRange(double quantity, double low, double high, string unit) =>
        ResultError.From(
            FluidDiagnostics.StateOutsideValidRange,
            ("name", Name),
            ("lo", Format(low, unit)),
            ("hi", Format(high, unit)),
            ("value", Format(quantity, unit)));

    /// <summary>Builds the <c>FS2004</c> a non-finite measurement fails with.</summary>
    /// <param name="property">Which property could not be evaluated.</param>
    /// <param name="state">The state it was asked for, spelled for a reader.</param>
    /// <returns>The failure.</returns>
    protected ResultError NotEvaluable(string property, string state) =>
        ResultError.From(
            FluidDiagnostics.PropertyNotEvaluable,
            ("property", property),
            ("name", Name),
            ("state", state));

    /// <summary>Checks a state against <see cref="ValidRange"/>.</summary>
    /// <param name="temperature">K.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns>
    /// The failure naming whichever bound was crossed, or <see langword="null"/> when the state is
    /// inside the domain. Temperature is reported first when both are out, because it is the one that
    /// almost always is.
    /// </returns>
    protected ResultError? OutOfRange(double temperature, double absolutePressure)
    {
        var range = ValidRange;

        if (temperature < range.MinimumTemperature || temperature > range.MaximumTemperature)
        {
            return OutsideRange(
                temperature - 273.15, range.MinimumTemperature - 273.15, range.MaximumTemperature - 273.15, "C");
        }

        if (absolutePressure < range.MinimumAbsolutePressure || absolutePressure > range.MaximumAbsolutePressure)
        {
            return OutsideRange(
                absolutePressure / 1000, range.MinimumAbsolutePressure / 1000,
                range.MaximumAbsolutePressure / 1000, "kPa absolute");
        }

        return null;
    }

    /// <summary>Spells a state for a message.</summary>
    /// <param name="temperature">K.</param>
    /// <param name="absolutePressure">Pa absolute.</param>
    /// <returns>Something like <c>20 C, 201.3 kPa absolute</c>.</returns>
    protected static string Describe(double temperature, double absolutePressure) =>
        $"{Format(temperature - 273.15, "C")}, {Format(absolutePressure / 1000, "kPa absolute")}";

    private static string Format(double value, string unit) =>
        $"{value.ToString("0.###", CultureInfo.InvariantCulture)} {unit}";
}
