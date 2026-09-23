namespace FluidScript.Core.Model.Contract;

/// <summary>What the solve did.</summary>
public sealed record SolveWire
{
    /// <summary>Whether the last pass converged.</summary>
    public required bool Converged { get; init; }

    /// <summary>Newton iterations over every sizing pass, retries included: the run's work, where a warm start's saving shows (<c>A-4</c>).</summary>
    public required int Iterations { get; init; }

    /// <summary>The scaled residual norm at the end.</summary>
    public required double ResidualNorm { get; init; }

    /// <summary>Wall time, or <see langword="null"/> when the caller did not time it.</summary>
    public required int? ElapsedMs { get; init; }

    /// <summary>Outer-loop passes.</summary>
    public required int SizingPasses { get; init; }
}
