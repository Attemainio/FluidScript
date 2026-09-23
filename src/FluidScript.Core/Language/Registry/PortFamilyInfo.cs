using System.Globalization;

namespace FluidScript.Core.Language.Registry;

/// <summary>A family of indexed ports, such as a tank's inlets.</summary>
public sealed record PortFamilyInfo
{
    /// <summary>Gets the family's name, such as <c>in</c>: the word before the index.</summary>
    /// <remarks>
    /// A member is written <c>in[n]</c>, with the bare word standing for <c>in[1]</c> (<c>D-120</c>);
    /// it is stored as <c>in{n}</c>, the key the model has always used; and before <c>D-120</c> it was
    /// written as that key. <see cref="Name"/>, <see cref="Key"/> and <see cref="LegacyName"/> are the
    /// three spellings of one port.
    /// </remarks>
    public required string Prefix { get; init; }

    /// <summary>Gets the script spelling of a member with one <c>{index}</c> placeholder: <c>in[{index}]</c>.</summary>
    /// <remarks>The same shape an indexed parameter family's <see cref="IndexedParameterFamilyInfo.Pattern"/> has, so a reader of either can spell a member one way.</remarks>
    public string Pattern => Prefix + "[{index}]";

    /// <summary>Gets the script spelling of one member.</summary>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in</c> for index 1, else <c>in[n]</c>.</returns>
    public string Name(int index) =>
        index == 1 ? Prefix : string.Create(CultureInfo.InvariantCulture, $"{Prefix}[{index}]");

    /// <summary>Gets the model's identifier for one member.</summary>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in1</c>, <c>in2</c>, …</returns>
    public string Key(int index) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{index}");

    /// <summary>Gets the spelling a script used before <c>D-120</c>, which is the key.</summary>
    /// <param name="index">The member's index.</param>
    /// <returns><c>in1</c>, <c>in2</c>, …</returns>
    public string LegacyName(int index) => Key(index);

    /// <summary>Gets the lowest index that exists.</summary>
    public required int MinIndex { get; init; }

    /// <summary>Gets the highest index that may be materialized.</summary>
    public required int MaxIndex { get; init; }

    /// <summary>Gets the nominal direction of flow through ports of this family.</summary>
    public required PortRole Role { get; init; }

    /// <summary>Gets the associated normalized-height parameter suffix.</summary>
    /// <value><c>_level</c> for a tank; <see langword="null"/> where a family has no height.</value>
    public required string? LevelParameterSuffix { get; init; }
}
