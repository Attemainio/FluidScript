namespace FluidScript.Core.Layout.Hints;

/// <summary>One circuit's structural facts (<c>D-33</c>, <c>D-35</c>).</summary>
public sealed record CircuitHint
{
    /// <summary>The circuit's name.</summary>
    public required string Name { get; init; }

    /// <summary>The circuit's number, stated or resolved; the leading part of every tag it owns.</summary>
    public required int Number { get; init; }

    /// <summary>The resolved role, or <see langword="null"/> when the name matched no registry entry (<c>D-35</c>).</summary>
    public CircuitRoleHint? Role { get; init; }

    /// <summary>The parent circuit's name, or <see langword="null"/> when the circuit stands alone.</summary>
    public string? ParentCircuit { get; init; }

    /// <summary>The parent's component this circuit takes flow from, when attached.</summary>
    public string? InletAnchorId { get; init; }

    /// <summary>The parent's component this circuit returns flow to, when attached.</summary>
    public string? OutletAnchorId { get; init; }
}
