using FluidScript.Core.Solvers.Seeding;

namespace FluidScript.Api.Pipeline;

/// <summary>One request to the pipeline.</summary>
/// <param name="Script">The script text.</param>
/// <param name="Mode">How far to go.</param>
/// <param name="Solve">Whether to run the solver at all; <see langword="false"/> stops after lowering.</param>
/// <param name="WarmStart">The session's last solution, offered to the outer loop; <see langword="null"/> for a cold start.</param>
public sealed record PipelineRequest(string Script, PipelineMode Mode, bool Solve, WarmStart? WarmStart);
