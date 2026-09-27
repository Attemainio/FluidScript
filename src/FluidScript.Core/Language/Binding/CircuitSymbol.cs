using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast;

namespace FluidScript.Core.Language.Binding;

/// <summary>One circuit and everything settled about it before topology.</summary>
public sealed record CircuitSymbol
{
    /// <summary>Gets the title written in the header, <c>circuit "Heating":</c>, or the document's name for a file with no header.</summary>
    /// <remarks>Free text: the role is the <c>role</c> setting's, never the name's.</remarks>
    public required string Name { get; init; }

    /// <summary>Gets the circuit's designation, the leading part of every tag it owns.</summary>
    /// <value>
    /// As its <c>number</c> setting states it, or resolved as the lowest unused multiple of 100 in declaration
    /// order when the circuit states none (<c>D-33</c>).
    /// </value>
    public required int Number { get; init; }

    /// <summary>Gets whether the number was written or resolved.</summary>
    /// <remarks>
    /// A write-back needs this to reproduce the source byte for byte: printing a resolved number would add
    /// <c>number = 100</c> to a circuit that never stated one the first time anything touched the file.
    /// </remarks>
    public required bool NumberIsExplicit { get; init; }

    /// <summary>Gets the working fluid's name as written, or <see langword="null"/> when unstated.</summary>
    public string? Substance { get; init; }

    /// <summary>Gets whether this circuit is solved as an equilibrium or in time.</summary>
    /// <value>
    /// Static as bound: the design solve is an equilibrium. A run projects the model and makes every circuit
    /// dynamic except those its <c>steady</c> list names (<c>D-169</c>, <see cref="RunProjection"/>).
    /// </value>
    public required FluidMode Mode { get; init; }

    /// <summary>Gets the circuit's role, resolved from its <c>role</c> setting (<c>D-35</c>).</summary>
    /// <value>Neutral when none is written, or the one written matches no role exactly (<c>FS1519</c>, <c>D-170</c>) — never an error.</value>
    public required CircuitRole Role { get; init; }

    /// <summary>Gets where the header sits in the source.</summary>
    /// <value>The whole file's span for the implicit circuit a headerless script gets.</value>
    public required TextSpan DeclarationSpan { get; init; }
}
