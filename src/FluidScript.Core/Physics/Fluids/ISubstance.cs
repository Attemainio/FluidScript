using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Physics.Fluids;

/// <summary>A substance whose thermodynamic properties can be evaluated.</summary>
/// <remarks>
/// <para>
/// Core depends on this and on nothing beneath it. Exactly one type in Core touches the property
/// backend, for three reasons in order of how much they matter: property calls dominate every solver
/// iteration and a constant-property double makes component tests run in microseconds; humid air,
/// incompressible approximations and future mixtures all satisfy one shape; and a backend's packaging
/// is a risk best confined to one file.
/// </para>
/// <para>
/// <strong>Every method returns <see cref="Result{T}"/> and none throws for a state that does not
/// exist.</strong> A solver overshoots during iteration and asks for something impossible, then
/// backtracks — that is a normal run, not an error, and a failure there emits no diagnostic.
/// </para>
/// </remarks>
public interface ISubstance
{
    /// <summary>Gets the name a script writes, such as <c>water</c>.</summary>
    string Name { get; }

    /// <summary>Gets the domain this substance's data is validated over.</summary>
    /// <remarks>
    /// Checked before every call, so a caller gets a failure rather than a silently extrapolated
    /// number. The backend cannot be relied on for this: above its upper bound CoolProp returns a
    /// number without complaint.
    /// </remarks>
    StateRange ValidRange { get; }

    /// <summary>Fixes a state from pressure and temperature.</summary>
    /// <param name="gaugePressure">
    /// The pressure, as a <see cref="Dimension.Pressure"/> quantity — which is gauge by definition in
    /// <c>13</c>, and is what the whole model stores. Adding the atmosphere is the implementation's
    /// job, not the caller's.
    /// </param>
    /// <param name="temperature">The temperature, as a <see cref="Dimension.Temperature"/> quantity.</param>
    /// <returns>The state, or why the pair does not describe one.</returns>
    Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature);

    /// <summary>Fixes a state from pressure and specific enthalpy.</summary>
    /// <param name="gaugePressure">The pressure, gauge, as above.</param>
    /// <param name="enthalpy">The specific enthalpy, J per kg of fluid.</param>
    /// <returns>The state, or why the pair does not describe one.</returns>
    /// <remarks>
    /// The pair the solver uses. An energy balance produces an enthalpy, and going back through
    /// temperature would mean inverting <c>cp</c> — which is what a property backend does correctly
    /// and a hand-rolled inversion does not.
    /// </remarks>
    Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy);

    /// <summary>Finds the freezing point at a pressure.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <returns>The freezing temperature, in K, or why it is unknown.</returns>
    Result<Quantity> FreezingPoint(Quantity gaugePressure);

    /// <summary>Finds the saturation pressure at a temperature.</summary>
    /// <param name="temperature">The temperature.</param>
    /// <returns>
    /// The saturation pressure as a gauge quantity, or why it is unknown. Backs the boiling and
    /// cavitation checks, which compare it against a gauge pressure the model holds.
    /// </returns>
    Result<Quantity> SaturationPressure(Quantity temperature);

    /// <summary>Fixes a state from pressure and specific entropy.</summary>
    /// <param name="gaugePressure">The pressure, gauge, as above.</param>
    /// <param name="entropy">The specific entropy, J/(kg·K) on this substance's own datum.</param>
    /// <returns>The state, or why the pair does not describe one.</returns>
    /// <remarks>
    /// The pair a compressor needs and nothing else does: an isentropic discharge point is
    /// <c>(p_high, s_suction)</c> by definition (<c>D-78</c>). A substance with no vapour phase has no
    /// use for it and says so rather than answering.
    /// </remarks>
    Result<FluidState> FromPressureEntropy(Quantity gaugePressure, Quantity entropy);

    /// <summary>Finds the saturation temperature at a pressure.</summary>
    /// <param name="gaugePressure">The pressure, gauge.</param>
    /// <returns>
    /// The saturation temperature in K, or why it is unknown — including because this substance has no
    /// saturation curve to be on.
    /// </returns>
    /// <remarks>
    /// The inverse of <see cref="SaturationPressure"/>, and the direction a cycle asks in: a condensing
    /// or evaporating temperature is chosen by the water it exchanges with, and the pressure follows.
    /// </remarks>
    Result<Quantity> SaturationTemperature(Quantity gaugePressure);
}
