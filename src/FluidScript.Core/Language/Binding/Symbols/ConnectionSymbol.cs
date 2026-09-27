using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One join between two components' ports.</summary>
/// <param name="From">The endpoint on the left of the dash.</param>
/// <param name="To">The endpoint on the right.</param>
/// <param name="SourceSpan">
/// The line the connection was written on. A chain of three endpoints produces two connections that
/// share one span, which is what makes a diagnostic point at the line the user can see.
/// </param>
public sealed record ConnectionSymbol(EndpointSymbol From, EndpointSymbol To, TextSpan SourceSpan)
{
    /// <summary>Gets the circuit whose block the line is written in.</summary>
    /// <value>
    /// <see langword="null"/> for a connection no line wrote, such as a boundary rule I3 adds. A node rule I2 puts
    /// between two components belongs here, not to the circuit of whichever component is on the left: the secondary
    /// loop's <c>HX1.secondary.out - NSUP</c> starts at an exchanger another circuit declares, and its node is still in
    /// the secondary loop.
    /// </value>
    public string? Circuit { get; init; }
}
