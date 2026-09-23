namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>One run as a structure: the line between two vertices.</summary>
/// <param name="From">The vertex the reading starts at.</param>
/// <param name="To">The vertex it ends at.</param>
/// <param name="Run">The run's index in the view.</param>
/// <param name="WithFlow">Whether the run's flow goes from <paramref name="From"/> to <paramref name="To"/>.</param>
internal sealed record RunLeaf(int From, int To, int Run, bool WithFlow) : Structure(From, To);
