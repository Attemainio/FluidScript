namespace FluidScript.Core.Language.Syntax.Ast;

/// <summary>Whether a fluid or a project is solved as an equilibrium or in time.</summary>
public enum FluidMode
{
    /// <summary>Solved as a steady state.</summary>
    Static = 1,

    /// <summary>Solved in time.</summary>
    Dynamic,
}
