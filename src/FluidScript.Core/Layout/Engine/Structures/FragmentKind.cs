namespace FluidScript.Core.Layout.Engine.Structures;

/// <summary>How a fragment is read (<c>28</c> E2), in the order the forms are tried.</summary>
internal enum FragmentKind
{
    /// <summary>A ring through its heat source (C2): the body runs from the source's outlet round to its inlet.</summary>
    Sourced,

    /// <summary>A member joined to itself (C20).</summary>
    SelfLoop,

    /// <summary>From an inlet boundary to an outlet (C19): the body is everything on the paths between them.</summary>
    Open,

    /// <summary>A ring with no heat source, cut at its consumer (C18).</summary>
    Unsourced,

    /// <summary>No ring and no open path: the chain rules grow everything from the head (C1, C5).</summary>
    Chain,
}
