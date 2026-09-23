using FluidScript.Core.Model.Contract;

namespace FluidScript.Api.Contracts;

/// <summary>One text replacement (<c>17</c>).</summary>
/// <param name="Span">The span to replace, in UTF-16 code units of the script sent.</param>
/// <param name="NewText">What replaces it.</param>
public sealed record TextEditWire(SpanWire Span, string NewText);
