namespace FluidScript.Core.Layout.Engine;

/// <summary>An inline element on a run (<c>28</c> A5): a point on the line, with the port that faces the run's start and the port that faces its end.</summary>
/// <param name="Element">The graph index of the pipe, pipe cell or two-connection node.</param>
/// <param name="Near">Its port towards the run's start.</param>
/// <param name="Far">Its port towards the run's end.</param>
internal readonly record struct InlinePoint(int Element, int Near, int Far);
