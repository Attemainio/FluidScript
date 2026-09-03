using System.Collections.Immutable;

namespace FluidScript.Core.Catalogs;

/// <summary>The absolute wall roughness of a material, and the condition it applies to.</summary>
/// <param name="Value">m. Absolute roughness, the epsilon in Colebrook-White.</param>
/// <param name="Material">What it is the roughness of, as a user would read it.</param>
/// <param name="Condition">
/// The state the value describes -- <c>"new"</c> for every value v1 ships. Not decoration: the same
/// pipe is a different hydraulic object after twenty years, and a number with no condition on it
/// silently claims to cover both.
/// </param>
/// <param name="Citation">The primary literature the value comes from.</param>
/// <param name="Sources">Where that literature was read from.</param>
/// <remarks>
/// <para>
/// <strong>Roughness is not a dimension, and provenancing it as one is a category error.</strong> An
/// outside diameter is a fact about a physical object, checkable against two manufacturers' tables.
/// A roughness is a fitted representative value from the Moody-chart literature: it appears in no pipe
/// standard, no manufacturer's dimension table carries it, and its published tolerance is +/-30 % to
/// +/-50 %. <c>27</c> already draws this line for the plate Nusselt constants; this is the same line
/// (<c>D-68</c>, <c>C-37</c>).
/// </para>
/// <para>
/// <strong>Every v1 value is for new pipe, and that is a sizing decision rather than an omission.</strong>
/// Ageing depends on water chemistry, oxygen ingress and treatment, none of which the model has, so an
/// aged default would be a fabricated number wearing a citation. A script that wants one writes
/// <c>roughness=0.3 mm</c> on the pipe, which is exactly the constraint mechanism <c>D-02</c> already
/// gives every other parameter.
/// </para>
/// </remarks>
public sealed record MaterialRoughness(
    double Value,
    string Material,
    string Condition,
    string Citation,
    ImmutableArray<SourceReference> Sources)
{
    /// <summary>Whether this value may be sized against.</summary>
    /// <value>
    /// <see langword="true"/> when it is positive, names its condition, and carries a citation and at
    /// least one source. Deliberately <em>one</em> source and not two: the two-source rule exists to
    /// catch a transcription error in a dimension, and a roughness is not transcribed off an object --
    /// what defends it is the citation and the stated condition.
    /// </value>
    public bool IsUsable =>
        Value > 0
        && !string.IsNullOrWhiteSpace(Condition)
        && !string.IsNullOrWhiteSpace(Citation)
        && Sources.Length >= 1;
}
