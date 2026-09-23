namespace FluidScript.Core.Model.Contract;

/// <summary>One circuit.</summary>
public sealed record CircuitWire
{
    /// <summary>The name as written.</summary>
    public required string Name { get; init; }

    /// <summary>The number, stated or resolved.</summary>
    public required int Number { get; init; }

    /// <summary>Whether the script wrote the number; the printer needs this.</summary>
    public required bool NumberIsExplicit { get; init; }

    /// <summary>The fluid keyword.</summary>
    public required string Substance { get; init; }

    /// <summary>The solve mode: <c>steady</c> or <c>transient</c>.</summary>
    public required string Mode { get; init; }

    /// <summary>The resolved role's canonical name, or <see langword="null"/> for a name the registry does not know (<c>D-35</c>).</summary>
    public required string? Role { get; init; }

    /// <summary>The parent circuit, or <see langword="null"/> when this one stands alone (<c>D-33</c>).</summary>
    public required string? ParentCircuit { get; init; }

    /// <summary>The parent component this circuit takes flow from.</summary>
    public required string? InletAnchorId { get; init; }

    /// <summary>The parent component this circuit returns flow to.</summary>
    public required string? OutletAnchorId { get; init; }

    /// <summary>Whether every component in this circuit has a state (invariant 7).</summary>
    public required bool Solved { get; init; }

    /// <summary>True only alongside <c>FS2502</c>.</summary>
    public required bool StatesOmitted { get; init; }
}
