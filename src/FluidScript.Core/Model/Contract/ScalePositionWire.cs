namespace FluidScript.Core.Model.Contract;

/// <summary>An element's place on one colour scale, each 0 to 1 or <see langword="null"/> where the element has no such value (<c>57</c> invariant 5: neutral, never the low end).</summary>
/// <param name="At">The representative value: a node's own, a component's outlet (<c>D-30</c>).</param>
/// <param name="From">Where a gradient starts: a component's inlet, a route's first end.</param>
/// <param name="To">Where it ends: a component's outlet, a route's last end.</param>
public sealed record ScalePositionWire(double? At, double? From, double? To);
