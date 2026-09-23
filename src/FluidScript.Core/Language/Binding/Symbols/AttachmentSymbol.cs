using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Language.Binding.Symbols;

/// <summary>One end of a subcircuit's attachment to its parent (<c>D-33</c>).</summary>
/// <param name="ParentComponentName">The name the script wrote.</param>
/// <param name="ParentComponent">
/// The component it resolved to, or <see langword="null"/> while no such component exists — which is
/// <c>FS1518</c>, not an exception.
/// </param>
/// <param name="Span">Where the line sits in the source.</param>
public sealed record AttachmentSymbol(
    string ParentComponentName,
    ComponentSymbol? ParentComponent,
    TextSpan Span);
