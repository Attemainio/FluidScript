using System.Collections.Immutable;

using FluidScript.Core.Components.Declarations;

namespace FluidScript.Core.Components.Valves;

/// <summary>A control valve: a rated Kv, an opening, and the characteristic between them.</summary>
/// <remarks>
/// <para>
/// <strong>This names the set of control valves, which nothing did before</strong> (<c>D-147</c>). The
/// two-way and the three-way valve share their rating, their opening and the order their resolvable
/// parameters are read in, and every rule that asks "is this a control valve" had to list the pair.
/// </para>
/// <para>
/// The opening is <see cref="Position"/> for both. For a three-way valve it is the opening between the
/// common port and <c>a</c>, and the bypass leg reads <c>1 − position</c> from the same number.
/// </para>
/// </remarks>
public abstract class ValveComponentBase : ComponentBase
{
    /// <summary>The index of <c>kv</c> among a control valve's resolvable parameters.</summary>
    public const int KvIndex = 0;

    /// <summary>The index of <c>position</c> among a control valve's resolvable parameters.</summary>
    public const int PositionIndex = 1;

    /// <summary>Initializes the parts every control valve shares.</summary>
    /// <param name="name">The user's identifier.</param>
    /// <param name="kv">The rated flow coefficient, m³/h at 1 bar.</param>
    /// <param name="position">The opening, 0 to 1.</param>
    /// <param name="characteristic">Which characteristic the controlled path follows.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kv"/> is not positive.</exception>
    protected ValveComponentBase(string name, double kv, double position, ValveCharacteristic characteristic)
        : base(name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kv);

        Kv = kv;
        Position = position;
        Characteristic = characteristic;
    }

    /// <summary>Gets the rated flow coefficient.</summary>
    /// <value>m³/h of water at 1 bar differential.</value>
    public double Kv { get; }

    /// <summary>Gets the opening.</summary>
    /// <value>
    /// 0 to 1; 1 is fully open — for a three-way valve, fully open between <c>ab</c> and <c>a</c>,
    /// whichever way the fluid moves. The one parameter a controller may move (<c>D-61</c>).
    /// </value>
    public double Position { get; init; }

    /// <summary>Gets which characteristic the controlled path follows.</summary>
    public ValveCharacteristic Characteristic { get; }

    /// <inheritdoc/>
    /// <value>
    /// <c>kv</c>, which sizing chooses, and <c>position</c>, which a controller sets and which promotion
    /// moves when a stated temperature can only be met by throttling or by the split (<c>23</c>).
    /// </value>
    public override ImmutableArray<ResolvedParameter> Resolvable =>
    [
        new ResolvedParameter("kv", Kv, "m3/h", Minimum: 0),

        // Bounded on both sides, and a three-way valve's bypass is why the upper one matters as much
        // as the lower: the bypass leg reads `1 - position`, so a split above 1 is a bypass opening
        // past fully shut.
        new ResolvedParameter("position", Position, "1", Minimum: 0, Maximum: 1),
    ];
}
