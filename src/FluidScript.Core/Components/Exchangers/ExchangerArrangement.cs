namespace FluidScript.Core.Components.Exchangers;

/// <summary>How the two streams of an exchanger run relative to each other.</summary>
/// <remarks>
/// The script's <c>arrangement</c> symbol, resolved. It selects the ε-NTU relation and nothing else;
/// the hydraulics of either side do not depend on it.
/// </remarks>
public enum ExchangerArrangement
{
    /// <summary>The streams run opposite ways: the default, and the one that reaches ε = 1.</summary>
    Counter,

    /// <summary>The streams run the same way; ε is capped at <c>1/(1 + Cr)</c>.</summary>
    Parallel,

    /// <summary>The streams cross, both unmixed.</summary>
    Crossflow,
}
