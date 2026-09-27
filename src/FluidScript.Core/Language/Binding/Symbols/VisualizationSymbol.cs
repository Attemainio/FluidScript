using System.Collections.Immutable;
using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One <c>show</c> line as written: the properties the canvas colours by, and the range it states (<c>57</c>).</summary>
/// <param name="Properties">The properties in the order written, by any of <c>57</c>'s names, each with where it is written.</param>
/// <param name="Scale">
/// The range stated for the first property, as the two numbers written, in that property's canonical unit (a
/// temperature in °C); <see langword="null"/> when none is stated or either end is not a plain number.
/// </param>
/// <param name="Span">Where the line's keyword is written, which a second <c>show</c> is reported against.</param>
/// <remarks>
/// Carried rather than resolved: the contract builder reads the properties against <c>PropertyTable</c> and
/// raises <c>FS1210</c>, <c>FS1213</c> and <c>FS1214</c> (<c>L-50</c>). On the model so that a stage after the
/// binder never reads the tree (<c>D-177</c>).
/// </remarks>
public sealed record VisualizationSymbol(
    ImmutableArray<(string Name, TextSpan Span)> Properties,
    (double From, double To)? Scale,
    TextSpan Span);
