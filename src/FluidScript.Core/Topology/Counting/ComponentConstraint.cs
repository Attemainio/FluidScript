namespace FluidScript.Core.Topology.Counting;

/// <summary>One stated parameter the circuit must satisfy rather than merely read.</summary>
/// <param name="Component">The component that states it.</param>
/// <param name="Parameter">The parameter's model key: <c>out2</c>, <c>flow2</c>, <c>dt</c> -- what the assembler matches on.</param>
/// <param name="Kind">What has to move to satisfy it.</param>
/// <param name="Hydraulic">The hydraulic component it constrains.</param>
/// <param name="Name">The parameter as the script spells it: <c>out[2].t</c>, <c>in[2].flow</c>, <c>dt</c> (<c>D-120</c>, <c>L-56</c>).</param>
/// <remarks>
/// <strong>Two spellings, one record.</strong> The assembler, the seed and the promotion rules read
/// <see cref="Parameter"/>, the key the physics has always used; every sentence a user sees reads
/// <see cref="Label"/>, which spells the key the way the script wrote it. Until <c>L-56</c> closed the
/// report said <c>HX1.out2</c> beside a script that says <c>out[2].t</c>.
/// </remarks>
public sealed record ComponentConstraint(
    string Component, string Parameter, ConstraintKind Kind, int Hydraulic, string Name)
{
    /// <summary>Gets the form a message names it by: the component and the script's spelling of the parameter.</summary>
    public string Label => $"{Component}.{Name}";
}
