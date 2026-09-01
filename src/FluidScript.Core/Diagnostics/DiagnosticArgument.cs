namespace FluidScript.Core.Diagnostics;

/// <summary>
/// One named value substituted into a diagnostic's message template.
/// </summary>
/// <param name="Name">
/// The placeholder name as it appears between braces in the template, without the braces.
/// </param>
/// <param name="Value">
/// The already-formatted replacement text. Numbers and quantities are formatted by the caller,
/// because only the caller knows the unit the user actually wrote — a message about a 30 kW heat
/// exchanger must not mention 30 000 W.
/// </param>
/// <remarks>
/// Arguments are matched to placeholders by name rather than by position, so reordering a sentence
/// during a wording change cannot silently swap two values.
/// </remarks>
public readonly record struct DiagnosticArgument(string Name, string Value);
