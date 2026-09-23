using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One join between two components' ports.</summary>
/// <param name="From">The endpoint on the left of the dash.</param>
/// <param name="To">The endpoint on the right.</param>
/// <param name="SourceSpan">
/// The line the connection was written on. A chain of three endpoints produces two connections that
/// share one span, which is what makes a diagnostic point at the line the user can see.
/// </param>
public sealed record ConnectionSymbol(EndpointSymbol From, EndpointSymbol To, TextSpan SourceSpan);
