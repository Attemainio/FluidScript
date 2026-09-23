namespace FluidScript.Core.Language.Registry;

/// <summary>A family of indexed properties, such as a tank's solved layer temperatures.</summary>
/// <remarks>
/// The same shape as <see cref="IndexedParameterFamilyInfo"/> and deliberately not shared with it: an
/// element is a <see cref="PropertyInfo"/> here and a <see cref="ParameterInfo"/> there, and the two
/// carry different things — a property has an availability and a reporting unit, a parameter has an
/// omission policy and a range. A common base holding only the pattern and the bounds would save four
/// lines and cost the reader the one distinction that matters.
/// </remarks>
public sealed record IndexedPropertyFamilyInfo
{
    private readonly string? _keyPattern;

    /// <summary>Gets the canonical pattern with one <c>{index}</c> placeholder, as a reference writes it.</summary>
    /// <value><c>layer[{index}].t</c>, <c>in[{index}].t</c>, or <c>out[{index}].t</c>.</value>
    public required string Pattern { get; init; }

    /// <summary>Gets the pattern of the key a member's value is published under.</summary>
    /// <value><see cref="Pattern"/> unless <c>D-120</c> respelled the surface: <c>t{index}</c>, <c>in{index}_t</c>.</value>
    public string KeyPattern
    {
        get => _keyPattern ?? Pattern;
        init => _keyPattern = value;
    }

    /// <summary>Gets the pattern a reference wrote before <c>D-120</c>, read for one language major with <c>FS1536</c>.</summary>
    /// <value><see langword="null"/> for a family that never changed spelling.</value>
    public string? LegacyPattern { get; init; }

    /// <summary>Gets the lowest index the family accepts.</summary>
    public required int MinIndex { get; init; }

    /// <summary>Gets the fixed maximum index.</summary>
    /// <value><see langword="null"/> when <see cref="MaxIndexParameter"/> supplies it instead.</value>
    public int? MaxIndex { get; init; }

    /// <summary>Gets the canonical integer parameter controlling the maximum, such as <c>layers</c>.</summary>
    /// <value><see langword="null"/> when <see cref="MaxIndex"/> is fixed.</value>
    public string? MaxIndexParameter { get; init; }

    /// <summary>Gets the shape of one member of the family.</summary>
    public required PropertyInfo Element { get; init; }
}
