using System.Globalization;

namespace FluidScript.Core.Catalogs;

/// <summary>The rated flow coefficient of one valve size in one series.</summary>
/// <remarks>
/// <para>
/// <strong>Kv is not an SI quantity and this record does not pretend otherwise.</strong> It is defined
/// as m³/h of water at 1 bar differential, and <see cref="Components.ValveLaw"/> holds the conversion
/// into the kg/s a residual needs. Storing a "converted" Kv here would put that conversion in two
/// places, and the factor it carries is a √10⁵ — an error of two and a half orders of magnitude that
/// looks entirely plausible at every step.
/// </para>
/// <para>
/// One field, because a valve's Kvs is the only thing sizing selects on. Body size, connection and
/// leakage class are real properties of a real valve and none of them changes which row the rule
/// picks, so they are absent rather than stubbed.
/// </para>
/// </remarks>
public sealed record ValveSpec
{
    /// <summary>The rated flow coefficient at full travel.</summary>
    /// <value>
    /// m³/h of water at 1 bar differential — Kv's own definition, not SI. The rated value, so a rule
    /// that sizes at part travel divides by <see cref="Components.ValveLaw.Opening"/>.
    /// </value>
    public required double Kvs { get; init; }

    /// <summary>Material and series, for a basis string a user can read.</summary>
    /// <value><c>"R5 preferred numbers"</c>.</value>
    public required string Series { get; init; }

    /// <summary>What is wrong with one row, or <see langword="null"/>.</summary>
    /// <param name="spec">The row to check.</param>
    /// <returns>The fault, phrased to complete "'Kv 1.6' …".</returns>
    public static string? Fault(ValveSpec spec)
    {
        // Absence is a fault the caller reports, as `PipeSpec.Fault` reports it: nothing in a catalogue
        // check throws.
        if (spec is null)
        {
            return "is absent";
        }

        if (!double.IsFinite(spec.Kvs) || spec.Kvs <= 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"has a Kvs of {spec.Kvs}, which is not a size");
        }

        // selected and then refused by the binder -- a size the rule offers and the language rejects.
        return spec.Kvs is < 0.01 or > 10_000
            ? string.Create(CultureInfo.InvariantCulture, $"has a Kvs of {spec.Kvs}, outside the range `kv` binds over")
            : null;
    }
}
