using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Registry;

/// <summary>A set of parameters one relation ties together, and how many of them are free.</summary>
/// <remarks>
/// <para>
/// An exchanger's <c>power</c>, <c>in</c>, <c>out</c> and <c>flow</c> satisfy one energy balance, so
/// any three fix the fourth and stating all four asserts something the physics need not agree with.
/// <c>ua</c>, <c>area</c> and <c>u</c> are the same shape with one freedom fewer.
/// </para>
/// <para>
/// <strong>Counting is all this supports.</strong> Whether a stated fourth value <em>agrees</em> with
/// the other three is a different question, and answering it needs a fluid — the implied flow is
/// <c>Q / (cp · dT)</c>, and neither the registry nor the binder has a cp.
/// </para>
/// </remarks>
public sealed record ParameterGroupInfo
{
    /// <summary>Gets the canonical parameter names the relation ties together.</summary>
    public required ImmutableArray<string> Parameters { get; init; }

    /// <summary>Gets how many of them may be stated before the group is over-determined.</summary>
    public required int Freedoms { get; init; }

    /// <summary>Gets how many of them must be stated for the group to be determined at all.</summary>
    /// <value>
    /// Zero for a group that is optional as a whole, which is every group but a boundary's. A
    /// <c>supply</c> states exactly one of <c>flow</c> and <c>p</c>: <see cref="Freedoms"/> is what
    /// stops it stating both, and this is what stops it stating neither (<c>D-64</c>).
    /// </value>
    /// <remarks>
    /// Separate from a <see cref="ParameterOmissionBehavior.Require"/> on each member, because the
    /// requirement is on the <em>set</em>: neither <c>flow</c> nor <c>p</c> is individually required and
    /// a rule that made them so would reject every valid boundary there is.
    /// </remarks>
    public int Minimum { get; init; }

    /// <summary>Gets the code raised when more than <see cref="Freedoms"/> of them are stated.</summary>
    /// <value>
    /// Rendered with <c>name</c>, <c>parameters</c> and <c>count</c>, plus every stated member's value
    /// under its own parameter name.
    /// </value>
    public required DiagnosticDescriptor Descriptor { get; init; }

    /// <summary>Gets the code raised when fewer than <see cref="Minimum"/> of them are stated.</summary>
    /// <value>
    /// <see langword="null"/> when <see cref="Minimum"/> is zero, and required when it is not.
    /// Rendered with <c>name</c> and <c>parameters</c>.
    /// </value>
    public DiagnosticDescriptor? MinimumDescriptor { get; init; }
}
