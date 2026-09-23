using System.Collections.Immutable;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Binding;

/// <summary>The language's built-in constants, readable by name in any expression (<c>D-126</c>).</summary>
/// <remarks>
/// Two, deliberately: <c>pi</c> and standard gravity <c>g</c>, the ones a plant script has a use for.
/// A constant is a reserved name recognised wherever an expression is read, never a binding; a
/// <c>let</c> of its name is <c>FS1411</c>. It is an identifier, so it needs an operator --
/// <c>2 * g</c> -- because <c>2 g</c> is two grams: <c>g</c> is also the mass unit, and a unit
/// symbol is what follows a number.
/// </remarks>
public static class Constants
{
    private static readonly ImmutableDictionary<string, (Quantity Value, string What, string Spelled)> Table =
        ImmutableDictionary.CreateRange(StringComparer.Ordinal,
        [
            KeyValuePair.Create("pi", (Quantity.FromSi(Math.PI, Dimension.Dimensionless), "the circle constant", "3.14159...")),

            // Standard acceleration of gravity, ISO 80000-3:2019 item 3-9.2 and the 3rd CGPM (1901):
            // 9.80665 m/s² exactly, the value pump and pressure conventions are defined against.
            KeyValuePair.Create("g", (Quantity.FromSi(9.80665, Dimension.Acceleration), "the standard acceleration of gravity", "9.80665 m/s2")),
        ]);

    /// <summary>Gets every constant's name, for completion and the lexicon.</summary>
    public static ImmutableArray<string> Names { get; } = [.. Table.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Looks a constant up by name.</summary>
    /// <param name="name">The identifier as written; case-sensitive.</param>
    /// <param name="value">The constant's SI value and dimension.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is a constant.</returns>
    public static bool TryGet(string name, out Quantity value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Table.TryGetValue(name, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Spells a constant's value for a message, as the docs print it.</summary>
    /// <param name="name">A constant's name.</param>
    /// <returns>The spelling, or the name itself when it is not a constant.</returns>
    public static string Spell(string name) => Table.TryGetValue(name, out var entry) ? entry.Spelled : name;

    /// <summary>Says what a constant is, for a message.</summary>
    /// <param name="name">A constant's name.</param>
    /// <returns>A short noun phrase, or the name itself when it is not a constant.</returns>
    public static string Describe(string name) => Table.TryGetValue(name, out var entry) ? entry.What : name;
}
