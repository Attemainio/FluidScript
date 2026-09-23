using System.Collections.Immutable;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Language.Registry;

/// <summary>One parameter a component kind accepts.</summary>
public sealed record ParameterInfo
{
    private readonly string? _key;

    /// <summary>Gets the canonical name: the spelling a script writes, <c>/docs</c> teach and a diagnostic quotes.</summary>
    /// <value><c>power</c>, or a port's quantity in <c>D-120</c>'s form — <c>in[2].t</c>.</value>
    public required string Name { get; init; }

    /// <summary>Gets the identifier binding stores the value under, which is what Core reads.</summary>
    /// <value>
    /// <see cref="Name"/> unless <c>D-120</c> respelled the surface: an exchanger's <c>in[2].t</c> is
    /// stored as <c>in2</c>, the key every sizer, seed and residual has read since before the
    /// spelling changed. The split keeps the language free to change its surface without a rename
    /// through the physics.
    /// </value>
    public string Key
    {
        get => _key ?? Name;
        init => _key = value;
    }

    /// <summary>Gets the curated input spellings.</summary>
    /// <value>
    /// Binding stores <see cref="Key"/>; printing preserves whatever the source said, so
    /// <c>T1 tank v=300</c> keeps its <c>v</c> (<c>D-32</c>). An alias is silent: <c>in[1].t</c> for
    /// <c>in.t</c> is one port written two ways, not an old form.
    /// </value>
    public ImmutableArray<string> Aliases { get; init; } = [];

    /// <summary>Gets the spellings a script wrote before <c>D-120</c>, read for one language major.</summary>
    /// <value>
    /// <c>in</c> for <c>in.t</c>, <c>flow2</c> for <c>in[2].flow</c>. Each binds exactly as
    /// <see cref="Name"/> does and raises <c>FS1536</c> with the new spelling as its suggestion, so
    /// the editor's quick fix rewrites the line and the documentation teaches one form (<c>18</c>
    /// retires them at the next major).
    /// </value>
    public ImmutableArray<string> LegacySpellings { get; init; } = [];

    /// <summary>Gets what shape of value this parameter accepts.</summary>
    /// <value>
    /// <see cref="ParameterValueKind.Quantity"/> for everything dimensioned;
    /// <see cref="ParameterValueKind.Symbol"/> for a closed set of names such as a valve's
    /// <c>characteristic</c>; <see cref="ParameterValueKind.Reference"/> for a controller's
    /// <c>measure</c> and <c>actuate</c>, which name another component's property rather than a value.
    /// All three are the same syntax, and only this says how to bind it.
    /// </value>
    public required ParameterValueKind ValueKind { get; init; }

    /// <summary>Gets the dimension, for a <see cref="ParameterValueKind.Quantity"/> parameter.</summary>
    /// <value><see cref="Dimension.Dimensionless"/> for the other kinds.</value>
    public required Dimension Dimension { get; init; }

    /// <summary>Gets the accepted names, for a <see cref="ParameterValueKind.Symbol"/> parameter.</summary>
    /// <value>Empty for the other kinds. Resolved by the same normalisation as a kind name.</value>
    public ImmutableArray<string> AcceptedSymbols { get; init; } = [];

    /// <summary>Gets what omission means: size the value, or apply an explicit visible default.</summary>
    public required ParameterOmissionBehavior OmissionBehavior { get; init; }

    /// <summary>Gets the canonical source literal for a defaulted parameter.</summary>
    /// <value><see langword="null"/> for a sized one. Written as a user would write it, unit included.</value>
    public string? DefaultLiteral { get; init; }

    /// <summary>Gets the user-facing reason for a default.</summary>
    /// <value><see langword="null"/> for a sized parameter.</value>
    public string? DefaultBasis { get; init; }

    /// <summary>Gets the plausibility bounds in SI, used for <c>FS1306</c>.</summary>
    /// <value><see langword="null"/> disables the check.</value>
    public Range<double>? UsualRange { get; init; }

    /// <summary>Gets the bounds outside which a value is an error rather than a doubt.</summary>
    /// <value>
    /// <see langword="null"/> where the parameter has no hard limit, which is most of them.
    /// </value>
    /// <remarks>
    /// Different in kind from <see cref="UsualRange"/> rather than only in severity. A usual range is a
    /// judgement about units — 30 000 W where kW was meant is <em>implausible</em>, not impossible — so
    /// it warns and the value stands. This is the range in which the parameter means anything at all: a
    /// valve 1.4 open, a pump 130 % efficient, or a port above the top of its own tank has no reading a
    /// solve could take, so it is an error and the value is dropped.
    /// </remarks>
    public ParameterValidity? Validity { get; init; }

    /// <summary>Gets the decimal places write-back formats this parameter to.</summary>
    /// <value>
    /// From <c>22</c>'s convention 5: every parameter declares a display precision, so a sized
    /// <c>kv</c> is written back as <c>12.4</c> rather than <c>12.40000000000001</c>.
    /// </value>
    public required int DisplayPrecision { get; init; }
}
