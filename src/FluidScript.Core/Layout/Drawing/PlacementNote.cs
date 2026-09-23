namespace FluidScript.Core.Layout.Drawing;

/// <summary>One decision the layout made, for the layout report (<c>C-107</c>).</summary>
/// <param name="Subject">What was decided about: a component's name, or <c>fragment N</c> for a form.</param>
/// <param name="Rule">The rule of <c>28</c> that decided it -- <c>C2</c>, <c>C9</c>, <c>C18</c> -- or <c>form</c>, <c>head</c>, <c>fallback</c>.</param>
/// <param name="Reason">Why, in a sentence: what the rule saw, or why a form declined.</param>
/// <remarks>
/// The report listed boxes, ports, routes and the audit -- the result -- and nothing said which form
/// drew a fragment, why the others declined, or which rule put a member where it is. Every such
/// question was answered by reading the engine (<c>C-105</c> twice in one day). The trace answers it in
/// the order the engine decided.
/// </remarks>
public sealed record PlacementNote(string Subject, string Rule, string Reason);
