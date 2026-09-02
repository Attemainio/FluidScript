using System.Collections.Immutable;

using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;

namespace FluidScript.Core.Fluids;

/// <summary>The substances a script's <c>fluid</c> line can name.</summary>
/// <remarks>
/// <para>
/// Two in v1 (<c>D-28</c>): <c>water</c> for solved hydronic circuits and <c>air</c> for property
/// validation and metadata. Glycol is post-v1 — accepting a concentration before the mixture contract
/// and the freezing basis are separately validated would overstate the supported physics, which is a
/// worse failure than refusing a name.
/// </para>
/// <para>
/// <strong>Names resolve exactly, with no similarity stage</strong>, and that is the difference from
/// <see cref="ComponentRegistry"/>. A kind resolved by similarity draws a slightly wrong symbol; a
/// working fluid resolved by similarity silently changes every density, viscosity and specific heat in
/// the model, and the results stay plausible. Case and separators are normalised — <c>Water</c> and
/// <c>WATER</c> resolve — because that is spelling rather than a different word.
/// </para>
/// </remarks>
public sealed class SubstanceRegistry : ISubstanceRegistry
{
    private readonly ImmutableDictionary<string, ISubstance> _byName;

    private SubstanceRegistry(ImmutableArray<ISubstance> substances)
    {
        Substances = substances;
        Names = [.. substances.Select(static substance => substance.Name)];

        _byName = substances.ToImmutableDictionary(
            static substance => NameResolution.Normalize(substance.Name),
            static substance => substance,
            StringComparer.Ordinal);
    }

    /// <summary>Gets the registry a model uses unless a test supplies another.</summary>
    /// <value>The real backend: measured water and measured humid air.</value>
    public static SubstanceRegistry Default { get; } =
        new([Water.Instance, HumidAirSubstance.Instance]);

    /// <summary>Gets a registry whose water is the constant-property double.</summary>
    /// <value>
    /// For component and solver tests, which are not about properties and should not pay for them. The
    /// name it answers to is still <c>water</c>, so a fixture reads identically either way.
    /// </value>
    public static SubstanceRegistry Constant { get; } =
        new([ConstantPropertyWater.Instance, HumidAirSubstance.Instance]);

    /// <summary>Gets a registry whose water varies linearly with temperature.</summary>
    /// <value>
    /// The other half of the pair. A component that only works with constant properties passes against
    /// <see cref="Constant"/> and fails here, which is the whole reason there are two.
    /// </value>
    public static SubstanceRegistry Linear { get; } =
        new([LinearPropertyWater.Instance, HumidAirSubstance.Instance]);

    /// <summary>Gets every registered substance, in order.</summary>
    public ImmutableArray<ISubstance> Substances { get; }

    /// <inheritdoc/>
    public ImmutableArray<string> Names { get; }

    /// <inheritdoc/>
    public Result<ISubstance> Resolve(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _byName.TryGetValue(NameResolution.Normalize(name), out var substance)
            ? Result.Success(substance)
            : Result.Failure<ISubstance>(ResultError.From(
                FluidDiagnostics.UnknownSubstance,
                ("name", name),
                ("list", string.Join(", ", Names))));
    }
}
