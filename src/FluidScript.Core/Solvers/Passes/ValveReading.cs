using FluidScript.Core.Components.Valves;

namespace FluidScript.Core.Solvers.Passes;

/// <summary>One control valve at one operating point of a plant whose Kv is already chosen (<c>C-121</c>).</summary>
/// <param name="Name">The valve's name.</param>
/// <param name="Authority">Dimensionless: its drop fully open over the branch total, at <paramref name="MassFlow"/>.</param>
/// <param name="MassFlow">kg/s through the path it controls, positive.</param>
/// <param name="ValveDrop">Pa, the valve's own drop fully open at that flow.</param>
/// <param name="BranchDrop">Pa, the rest of the branch at that flow.</param>
/// <param name="Characteristic">Its trim, which sets the rangeability the turn-down check reads.</param>
public readonly record struct ValveReading(
    string Name,
    double Authority,
    double MassFlow,
    double ValveDrop,
    double BranchDrop,
    ValveCharacteristic Characteristic);
