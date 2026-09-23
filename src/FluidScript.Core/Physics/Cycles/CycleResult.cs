using FluidScript.Core.Physics.Fluids;

namespace FluidScript.Core.Physics.Cycles;

/// <summary>The four state points of one revolution, and what they add up to.</summary>
/// <remarks>
/// Every state here is real and inspectable, which is the point of modelling the circuit rather than
/// correlating its COP (<c>D-78</c>): a discharge temperature, a pressure ratio and a zone split are
/// things an engineer checks a machine against, and a correlation has none of them.
/// </remarks>
public sealed record CycleResult
{
    /// <summary>Gets state 1 — compressor suction, superheated vapour.</summary>
    public required FluidState Suction { get; init; }

    /// <summary>Gets state 2s — where an ideal compressor would discharge.</summary>
    /// <remarks>Carried rather than discarded because it is what the isentropic efficiency is measured against.</remarks>
    public required FluidState IsentropicDischarge { get; init; }

    /// <summary>Gets state 2 — the real discharge.</summary>
    public required FluidState Discharge { get; init; }

    /// <summary>Gets state 3 — the high side's outlet, subcooled liquid or cooled supercritical gas.</summary>
    public required FluidState HighSideOutlet { get; init; }

    /// <summary>Gets the enthalpy after the expansion valve.</summary>
    /// <value>
    /// J/kg, and equal to <see cref="HighSideOutlet"/>'s by definition: the expansion is isenthalpic,
    /// so state 4 is an assignment rather than a measurement, and nothing evaluates a property there.
    /// </value>
    public required double ExpansionOutletEnthalpy { get; init; }

    /// <summary>Gets the shaft work.</summary>
    /// <value>J/kg of refrigerant, always positive.</value>
    public required double SpecificShaftWork { get; init; }

    /// <summary>Gets the electrical input.</summary>
    /// <value>J/kg of refrigerant. Equal to <see cref="SpecificShaftWork"/> when no motor is modelled.</value>
    public required double SpecificElectricalWork { get; init; }

    /// <summary>Gets the heat rejected on the high side.</summary>
    /// <value>J/kg of refrigerant, positive out of the cycle.</value>
    public required double SpecificHeatRejected { get; init; }

    /// <summary>Gets the heat absorbed in the evaporator.</summary>
    /// <value>J/kg of refrigerant, positive into the cycle.</value>
    public required double SpecificHeatAbsorbed { get; init; }

    /// <summary>Gets the heating coefficient of performance.</summary>
    /// <value>Heat rejected divided by electrical input. Never below 1 for a physical cycle.</value>
    public required double HeatingCop { get; init; }

    /// <summary>Gets the cooling coefficient of performance.</summary>
    /// <value>Heat absorbed divided by electrical input.</value>
    public required double CoolingCop { get; init; }

    /// <summary>Gets the ratio of discharge to suction pressure, absolute.</summary>
    /// <value>Dimensionless, above 1. Above roughly 7 a single stage is marginal on most refrigerants.</value>
    public required double PressureRatio { get; init; }

    /// <summary>Gets how the high-side duty divides between its zones.</summary>
    public required ZoneShares HighSideZones { get; init; }
}
