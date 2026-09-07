namespace FluidScript.Core.Sizing;

/// <summary>The constants every sizing rule reads, in one place so a user can be shown them.</summary>
/// <remarks>
/// <c>24</c>'s default catalogue. Not a code-organisation preference: a user asking "why 150 Pa/m?"
/// must be able to get an answer, and a table with a source column is that answer. <c>24</c> owns the
/// sources and the docs gate renders them; this type is only the numbers the code reads, so the two
/// cannot disagree about a value while agreeing about a citation.
/// </remarks>
public static class SizingDefaults
{
    /// <summary>The pressure gradient a pipe is sized to.</summary>
    /// <value>Pa/m. Common distribution practice.</value>
    /// <remarks>
    /// <strong>The argument about this number is narrower than it sounds.</strong> For 20 kW of water
    /// at 70/40 °C — 0.160 kg/s — a 150 Pa/m target selects DN20 at 132 Pa/m and 0.438 m/s, and a
    /// 100 Pa/m target selects DN25 at 42 Pa/m and 0.276 m/s. One catalogue step, with nothing in
    /// between, and the second choice trips <see cref="VelocityMinimum"/>. The criterion that actually
    /// governs is the index circuit's total head budget, which a per-pipe rule cannot see (<c>C-48</c>).
    /// </remarks>
    public const double PipeGradientTarget = 150;

    /// <summary>The velocity below which sedimentation and air entrainment are a concern.</summary>
    /// <value>m/s.</value>
    /// <remarks>
    /// A <em>soft</em> bound: it steps a size down and reports, and never overrides
    /// <see cref="VelocityMaximum"/>. It was a catalogue row no rule read until <c>C-48</c>, which meant
    /// the sizer could select a pipe it knew would make its own validator emit <c>FS4005</c>.
    /// </remarks>
    public const double VelocityMinimum = 0.3;

    /// <summary>The velocity ceiling for a nominal diameter, which is a noise limit.</summary>
    /// <param name="nominalDiameter">The DN designation — 25 for DN25, not a length.</param>
    /// <returns>m/s. A <em>hard</em> bound: a size exceeding it is never selected.</returns>
    public static double VelocityMaximum(int nominalDiameter) => nominalDiameter switch
    {
        < 50 => 1.0,
        <= 150 => 1.5,
        _ => 2.0,
    };
}
