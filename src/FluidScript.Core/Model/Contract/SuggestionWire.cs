namespace FluidScript.Core.Model.Contract;

/// <summary>A concrete fix.</summary>
/// <param name="Title">What it does.</param>
/// <param name="Range">What it replaces.</param>
/// <param name="NewText">What it puts there.</param>
public sealed record SuggestionWire(string Title, RangeWire Range, string NewText);
