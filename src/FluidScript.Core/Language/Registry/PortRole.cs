namespace FluidScript.Core.Language.Registry;

/// <summary>Which way flow nominally runs through a port.</summary>
/// <remarks>
/// Nominal, not solved: a negative solved flow is a legal answer (<c>22</c>'s convention 2), and a
/// three-way valve's ports are all bidirectional precisely because mixing and diverting arrangements
/// are both real.
/// </remarks>
public enum PortRole
{
    /// <summary>Flow nominally enters here.</summary>
    Inlet = 1,

    /// <summary>Flow nominally leaves here.</summary>
    Outlet,

    /// <summary>Either, decided by the solve.</summary>
    Bidirectional,
}
