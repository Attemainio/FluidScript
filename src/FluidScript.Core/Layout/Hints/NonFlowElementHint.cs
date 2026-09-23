namespace FluidScript.Core.Layout.Hints;

/// <summary>Where a rendered non-flow element sits and what it reads and drives.</summary>
/// <remarks>
/// A controller is placed beside the component whose parameter it actuates and routes an observer line
/// to what it measures. A placed instrument (<c>D-61</c>) sits on the node it reads and actuates
/// nothing, so <see cref="ActuationTargetId"/> is <see langword="null"/> for it.
/// </remarks>
public sealed record NonFlowElementHint
{
    /// <summary>The element's stable id.</summary>
    public required string ComponentId { get; init; }

    /// <summary>The graph component the element is drawn beside.</summary>
    public required string PlacementAnchorId { get; init; }

    /// <summary>The graph component whose property the element reads.</summary>
    public required string MeasurementTargetId { get; init; }

    /// <summary>The graph component whose parameter the element drives, or <see langword="null"/> for an instrument.</summary>
    public string? ActuationTargetId { get; init; }

    /// <summary>Position in one keyboard tab order over flow components and these elements together.</summary>
    /// <remarks>
    /// A flow component's position is its <see cref="LayoutHints.Order"/> index plus the number of
    /// elements anchored before it; each element follows its anchor immediately. Unique across the scene.
    /// </remarks>
    public required int NavigationOrder { get; init; }
}
