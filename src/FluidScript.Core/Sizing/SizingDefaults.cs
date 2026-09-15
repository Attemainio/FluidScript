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

    /// <summary>The share of its branch's drop a control valve is sized to take.</summary>
    /// <value>Dimensionless, 0 to 1. <c>24</c>'s <c>valve.authority_target</c>.</value>
    /// <remarks>
    /// <para>
    /// <strong>Authority is what makes a valve's travel mean something.</strong> A valve taking half
    /// the branch's drop keeps roughly its intended characteristic; one taking a tenth is a switch with
    /// a handle, because the branch's own resistance dominates until the valve is nearly shut and then
    /// the flow collapses over the last few percent of travel.
    /// </para>
    /// <para>
    /// It is a <em>control</em>-valve criterion and does not transfer. A balancing valve is sized for a
    /// measurable drop at design flow, typically 3-10 kPa, and asking it for authority sizes it for a
    /// job it does not have (<c>C-49</c>).
    /// </para>
    /// </remarks>
    public const double ValveAuthorityTarget = 0.5;

    /// <summary>The authority below which a sized valve is reported as controlling poorly.</summary>
    /// <value>Dimensionless. <c>24</c>'s <c>valve.authority_min</c>, the threshold for <c>FS4006</c>.</value>
    /// <remarks>
    /// Reported rather than corrected. Raising a valve's authority means shrinking it, and a valve small
    /// enough to control a low-resistance branch may be smaller than the branch can pass -- so the fix
    /// is usually to the branch rather than to the valve, and the rule is not the thing that can decide.
    /// </remarks>
    public const double ValveAuthorityMinimum = 0.25;

    /// <summary>The closest approach an extended exchanger is sized to without a stated <c>approach</c>.</summary>
    /// <value>K. <c>24</c>'s <c>hx.approach_min</c>, the threshold for <c>FS4008</c>.</value>
    /// <remarks>
    /// Below roughly 3 K a water/water plate exchanger's area grows faster than any plate count can
    /// follow, and the selection stops being a selection: approach → 0 is NTU → ∞ (<c>22</c>). A stated
    /// <c>approach</c> replaces it, as a constraint (<c>D-02</c>).
    /// </remarks>
    public const double ExchangerApproachMinimum = 3;

    /// <summary>The surplus duty above which a discrete plate count's overshoot is reported.</summary>
    /// <value>Dimensionless. <c>24</c>'s <c>hx.overshoot_report</c>, 2 %.</value>
    /// <remarks>
    /// Rounding a plate count up always delivers more than was asked; below this it is rounding, above
    /// it the designer should know the unit is larger than the duty needs.
    /// </remarks>
    public const double ExchangerOvershootReport = 0.02;
}
