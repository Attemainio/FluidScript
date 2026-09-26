using System.Collections.Immutable;

namespace FluidScript.Core.Language.Registry;

/// <summary>One named port of a component kind.</summary>
public sealed record PortInfo
{
    private readonly string? _key;
    private readonly string? _spelling;

    /// <summary>Gets the port's id on the wire and in the layout: <c>in</c>, <c>in[2]</c>, <c>ab</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the port as a script writes it, and as a message about it writes it (<c>D-179</c>).</summary>
    /// <value>
    /// <see cref="Name"/> unless the language spells the port otherwise: an exchanger's second side is <c>in[2]</c> on
    /// the wire and <c>secondary.in</c> in a script, so a lookup resolves this and never the id.
    /// </value>
    public string Spelling
    {
        get => _spelling ?? Name;
        init => _spelling = value;
    }

    /// <summary>Gets the other spellings a script may write the port in: <c>primary.in</c> for an exchanger's <c>in</c>.</summary>
    /// <value>
    /// A two-sided exchanger's first side is <c>primary</c> beside <c>secondary</c>, and a one-sided one -- a <c>load</c>,
    /// a <c>heater</c> -- has no second side to tell it from, so both spellings are the language's (<c>19</c>).
    /// </value>
    public ImmutableArray<string> Aliases { get; init; } = [];

    /// <summary>Gets the identifier the model, the wire and the symbol anchors know the port by.</summary>
    /// <value>
    /// <see cref="Name"/> unless <c>D-120</c> respelled it: <c>in[2]</c> is the port <c>in2</c> to
    /// everything downstream of the binder, which is what keeps the scene's anchors and the frontend
    /// unaware that the script's spelling changed.
    /// </value>
    public string Key
    {
        get => _key ?? Name;
        init => _key = value;
    }

    /// <summary>Gets the nominal direction of flow through the port.</summary>
    public required PortRole Role { get; init; }

    /// <summary>Gets whether the port may be left unconnected without inference rule I3 firing.</summary>
    public required bool IsOptional { get; init; }
}
