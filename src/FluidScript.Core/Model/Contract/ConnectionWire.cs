namespace FluidScript.Core.Model.Contract;

/// <summary>One adjacency.</summary>
public sealed record ConnectionWire
{
    /// <summary><c>c{n}</c>, by position in the model's connection list.</summary>
    public required string Id { get; init; }

    /// <summary>Where it starts.</summary>
    public required EndpointWire From { get; init; }

    /// <summary>Where it ends.</summary>
    public required EndpointWire To { get; init; }

    /// <summary><c>forward</c>, <c>reverse</c> or <c>none</c>: the solved direction relative to how it was written.</summary>
    public required string Flow { get; init; }

    /// <summary>The solved flow along it, or <see langword="null"/> when unsolved.</summary>
    public required ConnectionStateWire? State { get; init; }
}
