using System.Collections.Immutable;

using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

/// <summary>One quantity of a fluid state, with every spelling the language accepts for it.</summary>
/// <param name="Symbol">The short canonical spelling a port's state is written in: <c>t</c>, <c>rho</c>.</param>
/// <param name="Name">The long spelling, which is what the wire and a colour scale carry: <c>temperature</c>.</param>
/// <param name="Display">The heading a scale shows.</param>
/// <param name="Dimension">The dimension of the value.</param>
/// <param name="Diverging">Whether a scale of it is centred on zero, which a drop is and a temperature is not (<c>57</c>).</param>
/// <param name="Aliases">Further accepted spellings, never printed: <c>mdot</c> for <c>flow</c>.</param>
/// <param name="Of">For a change (<c>dp</c>, <c>dt</c>, <c>dh</c>), the symbol of the quantity it is a change of; <see langword="null"/> for a state quantity (<c>D-123</c>).</param>
/// <param name="Drop">For a change, whether it is read inlet minus outlet (a drop) rather than outlet minus inlet (a rise).</param>
public sealed record PropertyEntry(
    string Symbol,
    string Name,
    string Display,
    Dimension Dimension,
    bool Diverging,
    ImmutableArray<string> Aliases,
    string? Of = null,
    bool Drop = false);
