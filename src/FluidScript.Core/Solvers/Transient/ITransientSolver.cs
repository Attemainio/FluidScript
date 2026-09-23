namespace FluidScript.Core.Solvers.Transient;

/// <summary>Runs a transient, yielding frames as they are computed (<c>33</c> §Contracts).</summary>
/// <remarks>
/// Not an <see cref="ISolver"/>: it owns one. The t = 0 state is the design solve the snapshot holds
/// (<c>D-141</c>); every step's algebraic solve is the steady Newton on the pinned system (<c>D-139</c>).
/// </remarks>
public interface ITransientSolver
{
    /// <summary>Runs a transient, yielding frames as they are computed.</summary>
    /// <param name="snapshot">
    /// The immutable model and initial state. Dynamic storage states may be explicitly non-equilibrium;
    /// hydraulics and algebraic states are balanced at t = 0. It includes the fixed sizes and the
    /// schedule.
    /// </param>
    /// <param name="cancellationToken">Stops the run at the next integration boundary.</param>
    /// <returns>
    /// Frames in increasing time order, at the configured interval. The enumeration ends at the
    /// horizon, on cancellation, or on a step failure; the last frame's diagnostics say which.
    /// </returns>
    IAsyncEnumerable<TransientFrame> RunAsync(RunSnapshot snapshot, CancellationToken cancellationToken);
}
