using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>One setting a language 2 block takes, as an editor offers it (<c>19</c>, <c>52</c>, <c>A-9</c>).</summary>
public sealed record SettingWire
{
    /// <summary>The setting as a script writes it before its <c>=</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Other spellings the reader accepts: <c>color</c> for <c>colour</c>.</summary>
    public required ImmutableArray<string> Aliases { get; init; }

    /// <summary>What the setting says, in <c>19</c>'s words.</summary>
    public required string Meaning { get; init; }

    /// <summary>
    /// What its value is: <c>quantity</c>, <c>word</c>, <c>substance</c>, <c>circuitRole</c>, <c>catalog</c>, <c>case</c>,
    /// <c>names</c>, <c>circuits</c>, <c>date</c>, <c>range</c>, <c>colour</c>, <c>block</c>, <c>actuator</c>,
    /// <c>measurement</c>, <c>value</c> or <c>curve</c>.
    /// </summary>
    public required string ValueKind { get; init; }

    /// <summary>The dimension of a quantity with one fixed dimension, an entry in <see cref="MetadataWire.Dimensions"/>; otherwise <see langword="null"/>.</summary>
    public required string? Dimension { get; init; }

    /// <summary>
    /// The words the value is one of, when that set is closed and known to the build: a <c>word</c>'s values, the
    /// substances, the circuit roles, and each catalogue as <c>id@version</c>. Empty when the value is not chosen
    /// from a list, or the list is the script's own (its cases, its circuits, its components).
    /// </summary>
    public required ImmutableArray<string> Values { get; init; }

    /// <summary>The controller types that take the setting; empty when every type does.</summary>
    public required ImmutableArray<string> Types { get; init; }
}
