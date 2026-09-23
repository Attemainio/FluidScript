using System.Collections.Immutable;

namespace FluidScript.Core.Topology.Graph;

/// <summary>One independent cycle of the branch graph.</summary>
/// <remarks>
/// <para>
/// Named <c>CircuitLoop</c> rather than <c>Loop</c>: the bare word is a reserved keyword in other
/// .NET languages and <c>CA1716</c> refuses it. Every message still calls it a loop.
/// </para>
/// <para>
/// <strong>A loop contributes no equations.</strong> The formulation is nodal, so every component
/// writes <c>p_in − p_out = Δp</c> and walking any cycle telescopes to zero identically, for any
/// iterate, because pressure is single-valued on the nodes. Adding one pressure equation per loop
/// over-determines the system by exactly the loop count — on the cooling loop that is 21 equations
/// against 20 unknowns.
/// </para>
/// <para>
/// It exists for layout and for reporting: the renderer partitions a diagram by loops, and
/// <c>FS2214</c> names the one nothing drives.
/// </para>
/// </remarks>
public sealed record CircuitLoop
{
    /// <summary>Gets the branches the cycle runs through, in walk order.</summary>
    public required ImmutableArray<Branch> Branches { get; init; }

    /// <summary>Gets a reportable form: each branch's starting element and what lies along it.</summary>
    /// <remarks>
    /// Enough to recognise the circuit, which is what <c>FS2214</c> needs of it — that message's exact
    /// wording belongs to well-posedness rather than here.
    /// </remarks>
    public string Label => string.Join(
        " → ",
        Branches.SelectMany(static branch =>
            new[] { branch.From.Label }.Concat(branch.Path.Select(static part => part.Name))));
}
