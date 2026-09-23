namespace FluidScript.Core.Physics.Cycles;

/// <summary>How much of a compressor's electrical input reaches the shaft, and where the rest goes.</summary>
/// <param name="Efficiency">Shaft power divided by electrical input, from 0 to 1.</param>
/// <param name="ReachesRefrigerant">
/// <see langword="true"/> for a hermetic machine, whose motor sits in the suction stream so its losses
/// are recovered in the condenser; <see langword="false"/> for an open drive, whose losses warm the
/// plant room instead.
/// </param>
/// <remarks>
/// <c>D-82</c>'s loss destination, in the one place it changes an answer by more than a rounding.
/// A hermetic machine at <c>η_motor</c> = 0.92 is worth about 2.5 % of heating COP over an open one,
/// because the same watts are billed either way and only one of the two arrangements sells them back.
/// </remarks>
public readonly record struct MotorLoss(double Efficiency, bool ReachesRefrigerant)
{
    /// <summary>Gets the case with no motor modelled: the shaft is the input, and nothing is lost.</summary>
    /// <value>Efficiency 1, so a result is on a shaft basis and the identities below hold exactly.</value>
    public static MotorLoss None => new(1.0, false);
}
