using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast;

namespace FluidScript.Core.Language.Binding;

/// <summary>One circuit and everything settled about it before topology.</summary>
public sealed record CircuitSymbol
{
    /// <summary>Gets the identifier written in the header, which is also the source of the role.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the circuit's designation, the leading part of every tag it owns.</summary>
    /// <value>
    /// As written, or resolved as the lowest unused multiple of 100 in declaration order when the
    /// header omitted it (<c>D-33</c>).
    /// </value>
    public required int Number { get; init; }

    /// <summary>Gets whether the number was written or resolved.</summary>
    /// <remarks>
    /// The printer needs this to reproduce the source byte for byte: printing a resolved number would
    /// rewrite <c>circuit coolingLoop</c> as <c>circuit coolingLoop 100</c> the first time anything
    /// touched the file.
    /// </remarks>
    public required bool NumberIsExplicit { get; init; }

    /// <summary>Gets the working fluid's name as written, or <see langword="null"/> when unstated.</summary>
    public string? Substance { get; init; }

    /// <summary>Gets whether this circuit is solved as an equilibrium or in time.</summary>
    /// <value>
    /// The circuit's own <c>fluid dynamic|static</c> wins; otherwise the project default; otherwise
    /// static (<c>D-37</c>). A circuit contradicting the project gets <c>FS1517</c> and keeps its own.
    /// </value>
    public required FluidMode Mode { get; init; }

    /// <summary>Gets the circuit's role, resolved from its name (<c>D-35</c>).</summary>
    /// <value>Neutral when the name matches no role — never an error.</value>
    public required CircuitRole Role { get; init; }

    /// <summary>Gets the circuit both attachments resolve into, or <see langword="null"/> when this one stands alone.</summary>
    /// <remarks>
    /// Derived, not written: it is the circuit owning <see cref="Supply"/>'s and <see cref="Return"/>'s
    /// resolved components, which must be the same one (<c>FS1526</c>).
    /// </remarks>
    public string? ParentCircuit { get; init; }

    /// <summary>Gets where this circuit takes flow from its parent (<c>D-33</c>).</summary>
    public AttachmentSymbol? Supply { get; init; }

    /// <summary>Gets where this circuit returns that flow.</summary>
    public AttachmentSymbol? Return { get; init; }

    /// <summary>Gets where the header sits in the source.</summary>
    /// <value>The whole file's span for the implicit circuit a headerless script gets.</value>
    public required TextSpan DeclarationSpan { get; init; }
}
