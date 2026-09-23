namespace FluidScript.Core.Layout.Hints;

/// <summary>Which way a connection carries flow at the operating point.</summary>
public enum FlowDirection
{
    /// <summary>Within the zero-flow tolerance, or unsolved: a dead leg draws no arrow.</summary>
    None = 0,

    /// <summary>As the connection was written.</summary>
    Forward,

    /// <summary>Against the written direction.</summary>
    Reverse,
}
