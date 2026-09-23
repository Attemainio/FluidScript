namespace FluidScript.Core.Solvers.Equations;

/// <summary>One mass balance the assembler dropped because the rest of its component imply it.</summary>
/// <param name="Hydraulic">The hydraulic component whose balances are redundant.</param>
/// <param name="Component">The element whose balance was dropped.</param>
/// <param name="Equation">The name the dropped row would have carried.</param>
public sealed record DroppedBalance(int Hydraulic, string Component, string Equation);
