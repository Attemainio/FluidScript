using FluidScript.Core.Language.Registry;

namespace FluidScript.Core.Components;

/// <summary>An attachment point carrying a fluid state and a flow.</summary>
public sealed record Port
{
    /// <summary>Gets the port's name, as a qualified connection writes it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what this port does in the nominal flow direction.</summary>
    public required PortRole Role { get; init; }

    /// <summary>Gets whether inference rule I3 may leave this port unconnected.</summary>
    public required bool IsOptional { get; init; }

    /// <summary>Gets the normalized bottom-to-top height of a tank port.</summary>
    /// <value>0 at the bottom and 1 at the top; <see langword="null"/> for every other kind.</value>
    /// <remarks>
    /// It never enters a hydraulic pressure equation (<c>22</c> invariant): it selects which layer a
    /// port talks to, and nothing else. A hydrostatic term would be a different model.
    /// </remarks>
    public double? NormalizedLevel { get; init; }
}
