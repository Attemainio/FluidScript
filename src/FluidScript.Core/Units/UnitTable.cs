using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FluidScript.Core.Units;

/// <summary>
/// Every unit spelling the language accepts, and the dimension each denotes.
/// </summary>
/// <remarks>
/// <para>
/// The table is append-only across releases. Removing or repurposing a symbol changes the meaning of
/// scripts that already exist, silently and by a constant factor, which is the hardest class of bug to
/// see because the results stay plausible.
/// </para>
/// <para>
/// One symbol denotes one dimension, with a single documented exception: the pressure spellings shared
/// by <see cref="Dimension.Pressure"/> and <see cref="Dimension.PressureDelta"/>, which have the same
/// SI base unit and the same scale and differ only in whether the value is a reading or a difference.
/// The target parameter supplies that distinction. <see cref="Dimension.Head"/> and
/// <see cref="Dimension.Kv"/> deliberately have no spelling at all.
/// </para>
/// </remarks>
public static class UnitTable
{
    /// <summary>Standard atmospheric pressure, the datum a gauge pressure is measured from.</summary>
    /// <value>Pa absolute.</value>
    public const double StandardAtmosphere = 101_325;

    private const double Fahrenheit = 5.0 / 9.0;
    private const double MetreOfWater = 9806.65;
    private const double PoundPerSquareInch = 6894.757293168361;
    private const double Horsepower = 745.6998715822702;

    private static readonly FrozenDictionary<string, ImmutableArray<UnitSymbol>> ByText;
    private static readonly FrozenDictionary<string, ImmutableArray<UnitSymbol>> ByLowercase;

    private static readonly FrozenDictionary<string, ImmutableArray<UnitSymbol>>
        .AlternateLookup<ReadOnlySpan<char>> ByTextSlice;

    private static readonly FrozenDictionary<string, ImmutableArray<UnitSymbol>>
        .AlternateLookup<ReadOnlySpan<char>> ByLowercaseSlice;

    static UnitTable()
    {
        All = [.. Build()];

        ByText = All
            .GroupBy(static symbol => symbol.Text, StringComparer.Ordinal)
            .ToFrozenDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                StringComparer.Ordinal);

        ByLowercase = All
            .Where(static symbol => symbol.IsCaseInsensitive)
            .GroupBy(static symbol => symbol.Text.ToLowerInvariant(), StringComparer.Ordinal)
            .ToFrozenDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                StringComparer.Ordinal);

        ByTextSlice = ByText.GetAlternateLookup<ReadOnlySpan<char>>();
        ByLowercaseSlice = ByLowercase.GetAlternateLookup<ReadOnlySpan<char>>();
        LongestSymbolLength = All.Max(static symbol => symbol.Text.Length);
    }

    /// <summary>Gets every accepted spelling, in declaration order.</summary>
    /// <value>
    /// A spelling shared by two dimensions appears once per dimension, so the count exceeds the number
    /// of distinct spellings.
    /// </value>
    public static ImmutableArray<UnitSymbol> All { get; }

    /// <summary>Gets the length of the longest accepted spelling.</summary>
    /// <value>
    /// The bound on how far the lexer must look ahead for a unit symbol. Nine today, for
    /// <c>kJ/(kg*K)</c>. Derived from the table rather than declared, so adding a longer spelling
    /// cannot leave the lexer matching a prefix of it.
    /// </value>
    public static int LongestSymbolLength { get; }

    /// <summary>Determines whether a slice of text is an accepted spelling, exactly.</summary>
    /// <param name="text">The candidate, which is compared whole rather than as a prefix.</param>
    /// <returns><see langword="true"/> when the table holds this spelling.</returns>
    /// <remarks>
    /// The span overload exists for the lexer, which probes every length from
    /// <see cref="LongestSymbolLength"/> downwards at each position that follows a number. Taking a
    /// <see cref="string"/> would allocate one per probe, on the path that runs on every keystroke.
    /// </remarks>
    public static bool IsSymbol(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || text.Length > LongestSymbolLength)
        {
            return false;
        }

        if (ByTextSlice.ContainsKey(text))
        {
            return true;
        }

        // Case folding is a fallback and reaches only the spellings flagged for it, for the reason
        // Candidates gives: 'mm' and 'Mm' differ by a factor of a billion. The length is bounded
        // above, so the buffer is small and the stack is the right place for it.
        Span<char> folded = stackalloc char[LongestSymbolLength];
        var written = text.ToLowerInvariant(folded);
        return written >= 0 && ByLowercaseSlice.ContainsKey(folded[..written]);
    }

    /// <summary>Finds every dimension a spelling could denote.</summary>
    /// <param name="text">The symbol as written in the script.</param>
    /// <returns>
    /// The candidates, empty when the spelling is not a unit. More than one only for the shared
    /// pressure spellings.
    /// </returns>
    public static ImmutableArray<UnitSymbol> Candidates(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (ByText.TryGetValue(text, out var exact))
        {
            return exact;
        }

        // Case folding is a fallback, never the first attempt: 'mm' and 'Mm' differ by a factor of a
        // billion, so an SI spelling must never match one that is merely close.
        return ByLowercase.TryGetValue(text.ToLowerInvariant(), out var folded) ? folded : [];
    }

    /// <summary>Resolves a spelling that denotes exactly one dimension.</summary>
    /// <param name="text">The symbol as written in the script.</param>
    /// <param name="symbol">The resolved unit, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the spelling is a unit and is unambiguous. A shared pressure
    /// spelling returns <see langword="false"/> here and needs the overload taking the expected
    /// dimension.
    /// </returns>
    public static bool TryResolve(string text, [NotNullWhen(true)] out UnitSymbol? symbol)
    {
        var candidates = Candidates(text);
        symbol = candidates.Length == 1 ? candidates[0] : null;
        return symbol is not null;
    }

    /// <summary>Resolves a spelling against the dimension the destination expects.</summary>
    /// <param name="text">The symbol as written in the script.</param>
    /// <param name="expected">The dimension the value is being read into.</param>
    /// <param name="symbol">The resolved unit, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when a candidate denotes <paramref name="expected"/>. This is how
    /// <c>dp=50 kPa</c> and <c>p=50 kPa</c> read the same spelling as a difference and as a reading.
    /// </returns>
    public static bool TryResolve(string text, Dimension expected, [NotNullWhen(true)] out UnitSymbol? symbol)
    {
        symbol = Candidates(text).FirstOrDefault(candidate => candidate.Dimension == expected);
        return symbol is not null;
    }

    /// <summary>Resolves a spelling, preferring the dimension the value is being read into.</summary>
    /// <param name="text">The symbol as written in the script.</param>
    /// <param name="expected">
    /// The dimension the value is being assigned to, or <see langword="null"/> where nothing states
    /// one — inside an expression whose result has no declared destination.
    /// </param>
    /// <returns>The unit, or <see langword="null"/> when the spelling is not a unit at all.</returns>
    /// <remarks>
    /// A shared spelling resolves to <paramref name="expected"/> where it can and to the first
    /// candidate otherwise. That is the difference from the two-argument
    /// <see cref="TryResolve(string, out UnitSymbol?)"/>, which refuses an ambiguous spelling: a
    /// caller with nowhere to go from a refusal ends up treating <c>2 bar</c> as the bare number two,
    /// and returning a candidate instead keeps the mismatch reportable against a real dimension.
    /// </remarks>
    public static UnitSymbol? Resolve(string text, Dimension? expected)
    {
        var candidates = Candidates(text);

        if (candidates.IsEmpty)
        {
            return null;
        }

        if (expected is { } wanted)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Dimension == wanted)
                {
                    return candidate;
                }
            }
        }

        return candidates[0];
    }

    /// <summary>Gets the unit a bare number means for a dimension.</summary>
    /// <param name="dimension">The dimension the destination declares.</param>
    /// <returns>
    /// The canonical spelling, or <see langword="null"/> where a bare number is already the SI value —
    /// a dimensionless ratio, a designation, or <see cref="Dimension.Head"/>.
    /// </returns>
    /// <remarks>
    /// Resolved from the table rather than declared twice, so the canonical spelling and its
    /// conversion cannot disagree.
    /// </remarks>
    public static UnitSymbol? CanonicalUnitFor(Dimension dimension) =>
        dimension.CanonicalUnit is { } canonical
        && TryResolve(canonical, dimension, out var symbol)
            ? symbol
            : null;

    /// <summary>Gets every spelling accepted for one dimension.</summary>
    /// <param name="dimension">The dimension to list.</param>
    /// <returns>The spellings in declaration order, empty for a bare-only dimension.</returns>
    public static ImmutableArray<UnitSymbol> For(Dimension dimension) =>
        [.. All.Where(symbol => symbol.Dimension == dimension)];

    private static IEnumerable<UnitSymbol> Build()
    {
        // Length -- 'm' is Length and never Head.
        yield return new UnitSymbol("m", Dimension.Length, 1);
        yield return new UnitSymbol("mm", Dimension.Length, 0.001);
        yield return new UnitSymbol("cm", Dimension.Length, 0.01);
        yield return new UnitSymbol("dm", Dimension.Length, 0.1);
        yield return new UnitSymbol("km", Dimension.Length, 1000);
        yield return new UnitSymbol("in", Dimension.Length, 0.0254);
        yield return new UnitSymbol("ft", Dimension.Length, 0.3048);

        // Temperature -- absolute. 'C' without the degree sign is required: nobody types the symbol.
        yield return new UnitSymbol("C", Dimension.Temperature, 1, 273.15);
        yield return new UnitSymbol("°C", Dimension.Temperature, 1, 273.15);
        yield return new UnitSymbol("K", Dimension.Temperature, 1);
        yield return new UnitSymbol("F", Dimension.Temperature, Fahrenheit, 273.15 - (32 * Fahrenheit));
        yield return new UnitSymbol("°F", Dimension.Temperature, Fahrenheit, 273.15 - (32 * Fahrenheit));

        // TemperatureDelta -- a difference is spelled differently, so it has a type without context.
        yield return new UnitSymbol("dK", Dimension.TemperatureDelta, 1);
        yield return new UnitSymbol("dC", Dimension.TemperatureDelta, 1);

        // Pressure -- SI value is GAUGE pascals, so the absolute spellings carry the atmosphere offset.
        foreach (var symbol in GaugePressureSpellings(Dimension.Pressure))
        {
            yield return symbol;
        }

        foreach (var symbol in GaugePressureSpellings(Dimension.PressureDelta))
        {
            yield return symbol;
        }

        foreach (var symbol in AbsolutePressureSpellings())
        {
            yield return symbol;
        }

        yield return new UnitSymbol("W", Dimension.Power, 1);
        yield return new UnitSymbol("kW", Dimension.Power, 1000);
        yield return new UnitSymbol("MW", Dimension.Power, 1e6);
        yield return new UnitSymbol("hp", Dimension.Power, Horsepower, IsCaseInsensitive: true);

        yield return new UnitSymbol("J", Dimension.Energy, 1);
        yield return new UnitSymbol("kJ", Dimension.Energy, 1000);
        yield return new UnitSymbol("MJ", Dimension.Energy, 1e6);
        yield return new UnitSymbol("Wh", Dimension.Energy, 3600);
        yield return new UnitSymbol("kWh", Dimension.Energy, 3.6e6);
        yield return new UnitSymbol("MWh", Dimension.Energy, 3.6e9);

        yield return new UnitSymbol("kg/s", Dimension.MassFlow, 1);
        yield return new UnitSymbol("kg/h", Dimension.MassFlow, 1.0 / 3600);
        yield return new UnitSymbol("t/h", Dimension.MassFlow, 1000.0 / 3600);

        yield return new UnitSymbol("m3/s", Dimension.VolumeFlow, 1);
        yield return new UnitSymbol("m3/h", Dimension.VolumeFlow, 1.0 / 3600);
        yield return new UnitSymbol("l/s", Dimension.VolumeFlow, 0.001);
        yield return new UnitSymbol("l/min", Dimension.VolumeFlow, 0.001 / 60);
        yield return new UnitSymbol("l/h", Dimension.VolumeFlow, 0.001 / 3600);

        yield return new UnitSymbol("s", Dimension.Time, 1);
        yield return new UnitSymbol("ms", Dimension.Time, 0.001);
        yield return new UnitSymbol("min", Dimension.Time, 60, IsCaseInsensitive: true);
        yield return new UnitSymbol("h", Dimension.Time, 3600);
        yield return new UnitSymbol("d", Dimension.Time, 86_400);

        yield return new UnitSymbol("m/s", Dimension.Velocity, 1);
        yield return new UnitSymbol("km/h", Dimension.Velocity, 1000.0 / 3600);

        yield return new UnitSymbol("kg", Dimension.Mass, 1);
        yield return new UnitSymbol("g", Dimension.Mass, 0.001);
        yield return new UnitSymbol("t", Dimension.Mass, 1000);

        yield return new UnitSymbol("kg/m3", Dimension.Density, 1);

        yield return new UnitSymbol("J/(kg*K)", Dimension.SpecificHeat, 1);
        yield return new UnitSymbol("kJ/(kg*K)", Dimension.SpecificHeat, 1000);

        yield return new UnitSymbol("J/kg", Dimension.Enthalpy, 1);
        yield return new UnitSymbol("kJ/kg", Dimension.Enthalpy, 1000);

        yield return new UnitSymbol("m2", Dimension.Area, 1);
        yield return new UnitSymbol("mm2", Dimension.Area, 1e-6);
        yield return new UnitSymbol("cm2", Dimension.Area, 1e-4);

        yield return new UnitSymbol("m3", Dimension.Volume, 1);
        yield return new UnitSymbol("dm3", Dimension.Volume, 0.001);
        yield return new UnitSymbol("l", Dimension.Volume, 0.001);
        yield return new UnitSymbol("ml", Dimension.Volume, 1e-6);

        // Dimensionless keeps '%' and does not accept '-', which would collide with subtraction.
        yield return new UnitSymbol("%", Dimension.Dimensionless, 0.01);

        yield return new UnitSymbol("px", Dimension.Pixels, 1);
    }

    private static IEnumerable<UnitSymbol> GaugePressureSpellings(Dimension dimension)
    {
        // No offset: a gauge reading and a difference are both already in the SI representation.
        yield return new UnitSymbol("Pa", dimension, 1);
        yield return new UnitSymbol("kPa", dimension, 1000);
        yield return new UnitSymbol("MPa", dimension, 1e6);
        yield return new UnitSymbol("bar", dimension, 1e5, IsCaseInsensitive: true);
        yield return new UnitSymbol("mbar", dimension, 100, IsCaseInsensitive: true);
        yield return new UnitSymbol("psi", dimension, PoundPerSquareInch, IsCaseInsensitive: true);
        yield return new UnitSymbol("mH2O", dimension, MetreOfWater);
        yield return new UnitSymbol("mmH2O", dimension, MetreOfWater / 1000);

        if (dimension == Dimension.Pressure)
        {
            // The explicit gauge spellings exist only for a reading; a difference has no datum to be
            // gauge or absolute against, which is why kPag has no PressureDelta twin.
            yield return new UnitSymbol("kPag", dimension, 1000);
            yield return new UnitSymbol("barg", dimension, 1e5, IsCaseInsensitive: true);
        }
    }

    private static IEnumerable<UnitSymbol> AbsolutePressureSpellings()
    {
        const double offset = -StandardAtmosphere;
        yield return new UnitSymbol("Paa", Dimension.Pressure, 1, offset);
        yield return new UnitSymbol("kPaa", Dimension.Pressure, 1000, offset);
        yield return new UnitSymbol("MPaa", Dimension.Pressure, 1e6, offset);
        yield return new UnitSymbol("bara", Dimension.Pressure, 1e5, offset, IsCaseInsensitive: true);
        yield return new UnitSymbol("mbara", Dimension.Pressure, 100, offset, IsCaseInsensitive: true);
        yield return new UnitSymbol("psia", Dimension.Pressure, PoundPerSquareInch, offset, IsCaseInsensitive: true);
    }
}
