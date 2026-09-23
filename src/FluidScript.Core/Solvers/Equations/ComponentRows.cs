using FluidScript.Core.Components;
using FluidScript.Core.Topology.Graph;

namespace FluidScript.Core.Solvers.Equations;

/// <summary>Where one component's residuals land in the system's residual vector.</summary>
/// <param name="Component">The component's index in <see cref="CircuitGraph.Components"/>.</param>
/// <param name="FirstRow">The row its first kept residual occupies.</param>
/// <param name="LocalCount">How many residuals it writes, which is its <c>EquationCount</c>.</param>
/// <param name="DroppedLocal">
/// The local residual index the assembler discards, or <c>-1</c> when it keeps all of them. At most one
/// per component, and always a mass balance.
/// </param>
/// <remarks>
/// <strong>The component still writes every residual it declares; the assembler decides what to keep.</strong>
/// Asking a component to write one fewer residual when it happens to be the redundant one would make its
/// <c>EquationCount</c> depend on the partition it sits in, which nothing in
/// <see cref="IFlowComponent"/>'s contract lets it see.
/// </remarks>
public readonly record struct ComponentRows(int Component, int FirstRow, int LocalCount, int DroppedLocal)
{
    /// <summary>Gets whether one of this component's residuals is dropped as redundant.</summary>
    public bool HasDrop => DroppedLocal >= 0;

    /// <summary>Gets how many rows this component actually occupies.</summary>
    public int RowCount => HasDrop ? LocalCount - 1 : LocalCount;

    /// <summary>Finds the system row one of this component's residuals writes to.</summary>
    /// <param name="local">The residual's index within the component, as it writes them.</param>
    /// <returns>The row, or <c>-1</c> for the residual the assembler discards.</returns>
    public int Row(int local) =>
        local == DroppedLocal ? -1
            : FirstRow + (HasDrop && local > DroppedLocal ? local - 1 : local);
}
