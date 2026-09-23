namespace FluidScript.Core.Model.Contract;

/// <summary>One port.</summary>
public sealed record PortWire
{
    /// <summary>The port id: <c>in</c>, <c>out</c>, <c>in2</c>, <c>a</c>. The id is the model's key, not the script's spelling -- a script writes the second side <c>in[2]</c> (<c>D-120</c>) and the wire carries <c>in2</c>, so the ids never changed.</summary>
    public required string Name { get; init; }

    /// <summary><c>inlet</c>, <c>outlet</c> or <c>bidirectional</c>.</summary>
    public required string Role { get; init; }

    /// <summary>The component the port is wired to, or <see langword="null"/> when open.</summary>
    public required string? ConnectedTo { get; init; }

    /// <summary>A tank port's normalized elevation; absent otherwise.</summary>
    [AbsentWhenNull]
    public double? Elevation { get; init; }

    /// <summary>The tank layer the port meets; absent otherwise.</summary>
    [AbsentWhenNull]
    public int? Layer { get; init; }
}
