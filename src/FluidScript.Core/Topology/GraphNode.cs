using FluidScript.Core.Components;

namespace FluidScript.Core.Topology;

/// <summary>A point in the graph carrying pressure and enthalpy.</summary>
/// <remarks>
/// <para>
/// A wrapper around the <see cref="CircuitNode"/> that writes the equations, holding what the graph
/// knows and the component does not: where the node came from, and how much of a pipe's thermal
/// volume it owns. The component is kept rather than duplicated because the solver indexes residuals
/// through it.
/// </para>
/// <para>
/// <strong>Nothing here names a syntax or semantic-model type</strong> (<c>23</c>'s invariant 7).
/// <see cref="Name"/> is a string because the graph has to be reportable, not because it is a symbol.
/// </para>
/// </remarks>
public sealed record GraphNode
{
    /// <summary>Gets the node's identifier, unique across the model.</summary>
    /// <value>
    /// What the script wrote for a declared node, and a generated name for an inferred one. Names
    /// generated here are stable across lowerings of the same model, which is what invariant 6 needs.
    /// </value>
    public required string Name { get; init; }

    /// <summary>Gets the component that carries this node's unknowns and equations.</summary>
    public required CircuitNode Component { get; init; }

    /// <summary>Gets how this node came to exist.</summary>
    public required NodeOrigin Origin { get; init; }

    /// <summary>Gets the share of a pipe's fluid volume this node's thermal cell holds.</summary>
    /// <value>
    /// m³. Zero for every node but a pipe-internal one: a pipe with <c>nodes=n</c> gives each of its n
    /// internal cells <c>V/n</c>, and its endpoint nodes own none of it (<c>23</c>, step 2). Zero is
    /// correct rather than absent — a node with no thermal storage is a steady junction, which is what
    /// every declared and inferred node is.
    /// </value>
    public double ThermalVolume { get; init; }
}

/// <summary>How a node came to be in the graph.</summary>
/// <remarks>
/// Carried rather than derived, and separate from the semantic model's own origin: the canvas must
/// show which nodes a user wrote, and a pipe-internal node is neither written nor inferred by a
/// binder rule — it is a discretization the graph created and the renderer draws differently.
/// </remarks>
public enum NodeOrigin
{
    /// <summary>The script declared it.</summary>
    Declared = 1,

    /// <summary>An inference rule created it, to join or terminate components.</summary>
    Inferred,

    /// <summary>Lowering created it, subdividing a pipe with <c>nodes=n</c>.</summary>
    PipeInternal,
}
