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

    /// <summary>A valve's bench rangeability by trim: the largest over the smallest flow it controls at constant drop.</summary>
    /// <param name="characteristic">The trim.</param>
    /// <returns>Dimensionless: 50 equal percentage, 33 linear, 20 quick opening.</returns>
    /// <remarks>
    /// <para>
    /// Looked up 2026-09-22 (<c>24</c>): manufacturers quote 50:1 for equal percentage, 33:1 for linear
    /// and 20:1 for quick opening — Flo Control, <em>Rangeability and Turndown Ratio</em>. Equal
    /// percentage's 50 is also the <c>R</c> <see cref="Components.ValveLaw"/> already runs its curve on,
    /// so the check and the solver agree about the same valve.
    /// </para>
    /// <para>
    /// A bench figure, measured at constant drop across the valve. Installed, the valve's share of the
    /// drop rises as it closes, so the turn-down it achieves is <c>R·√a</c> — which is this project's
    /// reasoning rather than a standard's, and the part of the check most worth testing (<c>C-121</c>).
    /// </para>
    /// </remarks>
    public static double ValveRangeability(Components.ValveCharacteristic characteristic) => characteristic switch
    {
        Components.ValveCharacteristic.EqualPercentage => 50,
        Components.ValveCharacteristic.QuickOpen => 20,
        _ => 33,
    };

    /// <summary>The most a three-way valve is sized to drop at its common-port flow, fully open.</summary>
    /// <value>Pa. 15 kPa: the top of the band a mixing valve is selected in (<c>24</c>'s <c>three_way.dp_max</c>).</value>
    /// <remarks>
    /// <para>
    /// <strong>A three-way valve in a mixing circuit is sized to a drop band, not to an authority</strong>
    /// (<c>D-122</c>, <c>C-104</c>). ESBE's rotary mixing valves (series VRG130, VRG140, 3F): <em>"Start
    /// with the heat demand in kW and move vertically to the chosen Δt. Move horizontally to the shaded
    /// field (pressure drop of 3–15 kPa) and select the smaller Kvs-value."</em> The flow is the heat
    /// demand at the mixed circuit's Δt -- the flow through the common port -- and the Kvs is the
    /// smallest whose drop at that flow is inside the band. The authority rule sized the switched leg
    /// fully open for authority 0.5 against the variable circuit, and a valve that mixes half and half
    /// at design sits mid-travel where an equal-percentage leg passes 14 % of that: the ladder's series
    /// header was sized to Kv 1.6 and asked 15 bar of its pump.
    /// </para>
    /// <para>
    /// The same band is what the two-way rule's remarks call a balancing valve's job, 3–10 kPa, and
    /// ESBE's upper figure is 15. Its lower edge is <see cref="ThreeWayDropMinimum"/>.
    /// </para>
    /// </remarks>
    public const double ThreeWayDropMaximum = 15_000;

    /// <summary>The least a three-way valve is meant to drop at its common-port flow, fully open.</summary>
    /// <value>Pa. 3 kPa, the bottom of ESBE's band. Below it the smallest catalogue row is still too large for the flow, and the rule says so.</value>
    public const double ThreeWayDropMinimum = 3_000;

    /// <summary>The drop a balancing valve is set to before anything is known about the circuit it balances, Pa.</summary>
    /// <value>3 kPa.</value>
    /// <remarks>
    /// A balancing valve is set to a measured drop, and the makers' balancing guidance puts a floor
    /// under that drop so the measurement is accurate: IMI TA's STAD guidance sizes to a measurable
    /// drop and its handbooks give 3 kPa as the least worth measuring (<c>24</c>). It is the bootstrap
    /// setting only: the first solved pass replaces it with the drop that levels the three-way valve's
    /// legs. It is also what keeps the valve out of the Kv law's regularised band, where a
    /// fully-open provisional sits (measured: Kv 630 at 0.239 kg/s drops 0.19 Pa, below
    /// <see cref="Components.ValveLaw.RegularizationDrop"/>, and the first solve creeps to its cap on that row).
    /// </remarks>
    public const double BalancingDropMinimum = 3_000;

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

    /// <summary>The pressed gap between two plates, which is the fluid depth over the plate's face.</summary>
    /// <value>m. <c>D-145</c>'s hold-up rule: volume per side is heat-transfer area times this, halved.</value>
    /// <remarks>
    /// <para>
    /// A plate pack's fluid volume is its wetted area times the channel gap, and the gap is the one
    /// dimension a brazed plate range barely varies: Alfa Laval publish 0.040 dm³ per channel for the
    /// AC18 (plate 73.5 × 278 mm, 0.0204 m²) and 0.103 dm³ for the CB60 (113 × 466 mm, 0.0527 m²), which
    /// is 1.96 mm on both across a 2.6× change in plate size. That is the 1 dm³/m² the trade quotes.
    /// </para>
    /// <para>
    /// <strong>Half, because a pack has two sides.</strong> The channels alternate, so roughly half the
    /// gaps carry each stream, and the heat-transfer area the sizer reports is counted once for the pair.
    /// A stated <c>volume</c> replaces the estimate as a constraint (<c>D-02</c>), and the estimate is
    /// this project's reasoning from two published units, not a manufacturer's rule — <c>D-145</c>.
    /// </para>
    /// </remarks>
    public const double ExchangerChannelGap = 1.96e-3;
}
