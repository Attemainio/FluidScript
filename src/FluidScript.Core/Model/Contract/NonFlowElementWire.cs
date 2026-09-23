namespace FluidScript.Core.Model.Contract;

/// <summary>An instrument or controller.</summary>
/// <param name="ComponentId">Its id.</param>
/// <param name="PlacementAnchorId">The component it is drawn beside.</param>
/// <param name="MeasurementTargetId">What it reads.</param>
/// <param name="ActuationTargetId">What it drives, or <see langword="null"/> for an instrument.</param>
/// <param name="NavigationOrder">Its position in the tab order.</param>
public sealed record NonFlowElementWire(
    string ComponentId, string PlacementAnchorId, string MeasurementTargetId, string? ActuationTargetId, int NavigationOrder);
