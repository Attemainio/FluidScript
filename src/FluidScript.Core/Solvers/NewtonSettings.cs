namespace FluidScript.Core.Solvers;

/// <summary>Tuning for <see cref="NewtonSolver"/>.</summary>
/// <remarks>
/// <para>
/// Every default is <see cref="Tolerances"/>'s, which is <c>36</c>'s table made into a type. None of
/// these is user-facing: a script cannot set them, and a user who needed to would be working around a
/// defect rather than configuring a tool.
/// </para>
/// <para>
/// <strong>There is no retry-from-seed setting, and <c>32</c> is wrong to place one here.</strong> The
/// rule it describes — after a failure, retry once from the sizing seed rather than from the diverged
/// iterate — is right and worth having. But this solver is handed exactly one starting vector, so it
/// has no second one to retry from: the warm start <em>is</em> its <c>initialGuess</c>. Retrying needs
/// both seeds at once, and the only layer holding both is <c>31</c>'s outer loop (<c>S-20</c>).
/// </para>
/// </remarks>
public sealed record NewtonSettings
{
    /// <summary>Gets the most iterations to take before giving up.</summary>
    /// <value>Ten times a normal solve. Reaching it means something is wrong, not slow.</value>
    public int MaxIterations { get; init; } = Tolerances.NewtonMaxIterations;

    /// <summary>Gets the scaled residual infinity norm that counts as solved.</summary>
    public double ResidualTolerance { get; init; } = Tolerances.NewtonResidual;

    /// <summary>Gets the scaled step infinity norm below which the solve has stopped moving.</summary>
    public double StepTolerance { get; init; } = Tolerances.NewtonStep;

    /// <summary>Gets the residual growth ratio that counts as divergence.</summary>
    public double DivergenceFactor { get; init; } = Tolerances.NewtonDivergenceFactor;

    /// <summary>Gets the smallest line-search factor worth trying.</summary>
    /// <value>Six halvings; beyond that the step length is not the problem.</value>
    public double MinLineSearchStep { get; init; } = Tolerances.NewtonLineSearchMin;
}
