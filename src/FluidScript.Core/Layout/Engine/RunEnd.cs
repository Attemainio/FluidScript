namespace FluidScript.Core.Layout.Engine;

/// <summary>One end of a run: a boxed element's port.</summary>
/// <param name="Component">The graph index of the boxed element.</param>
/// <param name="Port">Its port index.</param>
internal readonly record struct RunEnd(int Component, int Port);
