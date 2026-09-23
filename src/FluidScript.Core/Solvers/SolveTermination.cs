namespace FluidScript.Core.Solvers;

/// <summary>Why a solve stopped.</summary>
public enum SolveTermination
{
    /// <summary>Every scaled residual is inside the tolerance.</summary>
    Converged = 1,

    /// <summary>The iteration cap was reached with the residual still above tolerance (<c>FS3001</c>).</summary>
    IterationCap,

    /// <summary>The Jacobian had no unique solution (<c>FS3002</c>).</summary>
    Singular,

    /// <summary>The residual grew away from a balance (<c>FS3003</c>).</summary>
    Diverging,

    /// <summary>Steps fell below tolerance while the residual stayed above it (<c>FS3004</c>).</summary>
    Stalled,

    /// <summary>The caller cancelled between iterations (<c>FS3006</c>).</summary>
    Cancelled,

    /// <summary>A residual or an iterate was not a finite number (<c>FS3007</c>).</summary>
    NonFinite,
}
