namespace FluidScript.Core.Components.Declarations;

/// <summary>One unknown the solver varies.</summary>
/// <param name="Index">Its position in the state vector, assigned at assembly.</param>
/// <param name="Kind">What it stands for.</param>
/// <param name="OwnerComponentId">The component that declared it.</param>
/// <param name="Name">A human-readable name, for diagnostics.</param>
/// <param name="SiUnit">The SI unit of its value, so a report can say what a number means.</param>
public sealed record UnknownDeclaration(
    int Index,
    UnknownKind Kind,
    string OwnerComponentId,
    string Name,
    string SiUnit);
