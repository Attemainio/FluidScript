using System.Collections.Immutable;

namespace FluidScript.Api.Contracts;

/// <summary>One diagnostic code.</summary>
/// <param name="Code">The code, <c>FS1302</c>.</param>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
/// <param name="Area">The subject the code belongs to.</param>
/// <param name="Message">The message with its <c>{placeholders}</c> unfilled.</param>
/// <param name="Arguments">The placeholder names, in order.</param>
public sealed record DiagnosticCodeWire(string Code, string Severity, string Area, string Message, ImmutableArray<string> Arguments);
