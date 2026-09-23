using FluidScript.Core.Components;

namespace FluidScript.Core.Topology.Graph;

/// <summary>One end of a branch: the junction element it meets, and the port it meets it at.</summary>
/// <remarks>
/// <strong>Not a <see cref="GraphNode"/>.</strong> A branch ends at a junction <em>element</em>, and a
/// multi-port component is a junction element without being a node — the cooling loop's branches end
/// at <c>3WV.ab</c>, <c>3WV.a</c> and <c>3WV.b</c>, which no node type can name. Typing both ends as a
/// node makes the branch table <c>23</c> tabulates unrepresentable.
/// </remarks>
public sealed record BranchEnd
{
    /// <summary>Gets the junction element this end meets.</summary>
    public required IFlowComponent Element { get; init; }

    /// <summary>Gets the index of the port it meets, in <see cref="IFlowComponent.Ports"/> order.</summary>
    public required int Port { get; init; }

    /// <summary>Gets the port's name, or <see langword="null"/> when the element is a node.</summary>
    /// <value>
    /// Null for a node, whose ports are unnamed and interchangeable — there is nothing to report but
    /// the node itself. <c>a</c>, <c>b</c> or <c>c</c> for a three-way valve, where which port a
    /// branch meets is the whole content of the row.
    /// </value>
    public string? PortName { get; init; }

    /// <summary>Gets a reportable form: the element's name, and its port when the port has one.</summary>
    public string Label => PortName is null ? Element.Name : $"{Element.Name}.{PortName}";
}
