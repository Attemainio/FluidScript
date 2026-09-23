using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Registry;

/// <summary>The range a parameter's value must lie in, and the code that says so when it does not.</summary>
/// <remarks>
/// <strong>The descriptor travels with the range because each of these codes reads as its own
/// sentence.</strong> "position must be between 0 and 1" and "layers must be a whole number from 1 to
/// 100" are not one message with a substitution in it, and flattening them into one would produce the
/// generic bounds message every parameter already has in <c>FS1306</c>. One check site renders all of
/// them, so a newly bounded parameter is a registry row and a descriptor rather than another branch in
/// the binder.
/// </remarks>
public sealed record ParameterValidity
{
    /// <summary>Gets the inclusive bounds, in SI.</summary>
    public required Range<double> Range { get; init; }

    /// <summary>Gets the code raised for a value outside the range.</summary>
    /// <value>
    /// Rendered with <c>name</c>, <c>parameter</c>, <c>value</c>, <c>low</c> and <c>high</c> available;
    /// a template uses the ones its sentence needs and the rest are ignored.
    /// </value>
    public required DiagnosticDescriptor Descriptor { get; init; }

    /// <summary>Gets whether a fractional value is an error too.</summary>
    /// <value>
    /// <see langword="true"/> for a tank's <c>layers</c>, which is a count of things and not a size.
    /// </value>
    public bool RequiresWholeNumber { get; init; }
}
