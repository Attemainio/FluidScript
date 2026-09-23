namespace FluidScript.Core.Solvers;

/// <summary>A previous solution offered as the first iterate of the next solve (<c>41</c>).</summary>
/// <param name="Solution">The converged iterate of an earlier run, in SI, laid out by that run's <see cref="SystemLayout"/>.</param>
/// <param name="TopologyHash">
/// <see cref="OuterLoopResult.TopologyHash"/> of the run that produced <paramref name="Solution"/>. The
/// outer loop uses the solution only when the hash of the graph it is about to solve is the same:
/// the iterate is a vector indexed by the unknown layout, and a layout that has gained or lost an
/// unknown, or reordered one, makes every position mean something else.
/// </param>
/// <remarks>
/// A warm start is a cache, never a source of truth: a solve seeded from one converges to the same
/// answer as a cold one, in fewer iterations, or it is thrown away and the solve starts cold. That is
/// what lets a host keep the last solution per session and offer it on every request without
/// checking anything but the hash.
/// </remarks>
public sealed record WarmStart(StateVector Solution, string TopologyHash);
