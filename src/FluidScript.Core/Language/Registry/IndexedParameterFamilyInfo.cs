namespace FluidScript.Core.Language.Registry;

/// <summary>A family of indexed parameters, such as a tank's per-layer temperatures.</summary>
public sealed record IndexedParameterFamilyInfo
{
    private readonly string? _keyPattern;

    /// <summary>Gets the canonical pattern with one <c>{index}</c> placeholder, as a script writes it.</summary>
    /// <value><c>layer[{index}].t</c>, <c>in[{index}].level</c>, or <c>out[{index}].level</c>.</value>
    public required string Pattern { get; init; }

    /// <summary>Gets the pattern of the key a member is stored under.</summary>
    /// <value><see cref="Pattern"/> unless <c>D-120</c> respelled the surface: <c>t{index}</c>, <c>in{index}_level</c>.</value>
    public string KeyPattern
    {
        get => _keyPattern ?? Pattern;
        init => _keyPattern = value;
    }

    /// <summary>Gets the pattern a script wrote before <c>D-120</c>, read for one language major with <c>FS1536</c>.</summary>
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
    public required ParameterInfo Element { get; init; }
}
