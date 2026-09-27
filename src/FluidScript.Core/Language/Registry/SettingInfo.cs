using System.Collections.Immutable;

using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

/// <summary>One setting a block takes: <c>fluid</c> in a circuit, <c>duration</c> in a run (<c>19</c>).</summary>
/// <param name="Name">The setting as a script writes it before its <c>=</c>.</param>
/// <param name="Meaning">What it says, in <c>19</c>'s words; an editor shows it beside the name.</param>
/// <param name="Kind">What its value is.</param>
public sealed record SettingInfo(string Name, string Meaning, SettingValueKind Kind)
{
    /// <summary>Gets the other spellings the reader accepts: <c>color</c> for <c>colour</c>.</summary>
    public ImmutableArray<string> Aliases { get; init; } = [];

    /// <summary>Gets the words a <see cref="SettingValueKind.Word"/> value is one of, in the order <c>19</c> lists them.</summary>
    public ImmutableArray<string> Values { get; init; } = [];

    /// <summary>Gets the value's dimension when a quantity has one fixed dimension, or <see langword="null"/>.</summary>
    /// <value>
    /// <see langword="null"/> for a quantity read in what something else is -- a controller's <c>band</c> is in the
    /// measurement's dimension -- and for every value that is not a quantity.
    /// </value>
    public Dimension? Dimension { get; init; }

    /// <summary>Gets the controller types that take the setting; empty when every type does (<c>19</c> §Controllers).</summary>
    public ImmutableArray<string> Types { get; init; } = [];

    /// <summary>Whether a written name is this setting, by its name or an alias, case and underscores aside (<c>D-15</c>'s first stage).</summary>
    /// <param name="written">The name as written.</param>
    /// <returns><see langword="true"/> when it spells this setting.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="written"/> is <see langword="null"/>.</exception>
    public bool Matches(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        var normalized = NameResolution.Normalize(written);
        return NameResolution.Normalize(Name) == normalized
            || Aliases.Any(alias => NameResolution.Normalize(alias) == normalized);
    }
}
