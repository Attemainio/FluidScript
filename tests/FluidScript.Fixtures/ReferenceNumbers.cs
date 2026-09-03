namespace FluidScript.Fixtures;

/// <summary>
/// The hand-computed figures every milestone gate asserts, reproduced independently of any project
/// code before the code that must produce them was written.
/// </summary>
/// <remarks>
/// <para>
/// These are the numbers in <c>plan/00-foundation/01-vision-and-scope.md</c>,
/// <c>05-milestones-and-acceptance.md</c>, <c>20-core-domain/24-auto-sizing.md</c> and
/// <c>27-component-catalog.md</c>. They are stated here once so that a solver test asserts a figure
/// the plan owns rather than a literal retyped at the assertion site, where a transcription error
/// reads as a solver bug.
/// </para>
/// <para>
/// All 71 were reproduced from the stated inputs during P0.3 of
/// <c>plan/00-foundation/08-implementation-sequence.md</c>. The raw fluid properties they are derived
/// from are <em>not</em> verified here — confirming those against CoolProp is validation case V4/V5,
/// which arrives with the real property backend in P3.2.
/// </para>
/// </remarks>
public static class ReferenceNumbers
{
    /// <summary>Water enthalpies used by every reference circuit, per <c>21-fluid-and-state</c>.</summary>
    /// <remarks>
    /// Specific enthalpy on the IAPWS reference (zero for saturated liquid at the triple point), so
    /// only differences between these values are physically meaningful.
    /// </remarks>
    public static class WaterEnthalpy
    {
        /// <summary>Specific enthalpy of liquid water at 6 °C.</summary>
        /// <value>J/kg.</value>
        public const double At6C = 25_324.0;

        /// <summary>Specific enthalpy of liquid water at 20 °C.</summary>
        /// <value>J/kg.</value>
        public const double At20C = 84_007.0;

        /// <summary>Specific enthalpy of liquid water at 50 °C.</summary>
        /// <value>J/kg.</value>
        public const double At50C = 209_418.0;
    }

    /// <summary>The cooling loop — the topology reference circuit.</summary>
    public static class CoolingLoop
    {
        /// <summary>Mass flow through the secondary loop, driven by <c>HE1</c>'s stated duty.</summary>
        /// <value>kg/s.</value>
        public const double SecondaryFlow = 0.2392;

        /// <summary>Primary-side share of the flow mixing at <c>N2</c>.</summary>
        /// <value>Dimensionless, 0 to 1.</value>
        public const double MixingFraction = 0.681;

        /// <summary>Mass flow entering from the primary boundary at <c>N1</c>.</summary>
        /// <value>kg/s.</value>
        public const double PrimaryFlow = 0.1630;

        /// <summary>Mass flow through the recirculation branch from <c>3WV.b</c> back to <c>N2</c>.</summary>
        /// <value>kg/s, positive in the nominal connection direction.</value>
        public const double RecirculationFlow = 0.0763;

        /// <summary>Pressure gradient in <c>P1</c> once sized to DN20.</summary>
        /// <value>Pa/m.</value>
        public const double PipeGradient = 138.0;
    }

    /// <summary>The simple loop — the sizing and solver-arithmetic reference circuit.</summary>
    public static class SimpleLoop
    {
        /// <summary>The single mass flow around the series loop.</summary>
        /// <value>kg/s.</value>
        public const double Flow = 0.2392;

        /// <summary>Total pressure drop around the loop at the sized component values.</summary>
        /// <value>Pa.</value>
        public const double LoopPressureDrop = 51_674.0;

        /// <summary>Head <c>PU1</c> is sized to.</summary>
        /// <value>
        /// m of the fluid at the pump's own inlet state — 998.2 kg/m³ at 20 °C, not the loop's 35 °C
        /// mean. The basis matters: at 994 kg/m³ the same drop reads 5.30 m.
        /// </value>
        public const double PumpHead = 5.28;

        /// <summary>Catalogue <c>Kv</c> selected for <c>CV1</c>, rounded down from a required 1.833.</summary>
        /// <value>m³/h at 1 bar differential.</value>
        public const double ValveKv = 1.6;

        /// <summary>Valve authority achieved after rounding <c>Kv</c> down.</summary>
        /// <value>
        /// Dimensionless. Above the 0.5 target, which is the safe direction — a value below the target
        /// after rounding down would mean the rounding went the wrong way.
        /// </value>
        public const double AchievedAuthority = 0.57;
    }

    /// <summary>The substation — the two-sided rated exchanger reference circuit.</summary>
    public static class Substation
    {
        /// <summary>Thermal conductance required to meet the stated duty and four temperatures.</summary>
        /// <value>W/K. The ε-NTU and LMTD routes must both produce this, sharing no code.</value>
        public const double RequiredUa = 12_071.0;

        /// <summary>Log-mean temperature difference for the counterflow design point.</summary>
        /// <value>K.</value>
        public const double Lmtd = 12.427;

        /// <summary>Heat-transfer area required at the stated <c>u</c> of 3300 W/(m²·K).</summary>
        /// <value>m².</value>
        public const double RequiredArea = 3.658;

        /// <summary>Total plate count selected, including the two end plates that transfer nothing.</summary>
        /// <value>Plates. Rounds up: more area means a closer approach, never a duty shortfall.</value>
        public const int TotalPlates = 39;

        /// <summary>Cold-end temperature difference achieved by the selected plate count.</summary>
        /// <value>K. Closer than the 5.0 K design approach because the plate count rounded up.</value>
        public const double AchievedApproach = 4.90;
    }

    /// <summary>The distribution header — the multi-circuit reference.</summary>
    public static class DistributionHeader
    {
        /// <summary>Mass flow round the AHU subcircuit's own loop, from its stated 24 kW at 50/30 °C.</summary>
        /// <value>kg/s. What passes through <c>HE_AHU</c>, which is more than the subcircuit draws.</value>
        public const double AhuBranchFlow = 0.2871;

        /// <summary>Mass flow round the radiator subcircuit's own loop, from its stated 30 kW at 50/30 °C.</summary>
        /// <value>kg/s. What passes through <c>HE_RAD</c>, which is more than the subcircuit draws.</value>
        public const double RadiatorBranchFlow = 0.3589;

        /// <summary>Mass flow the AHU subcircuit draws from the header.</summary>
        /// <value>
        /// kg/s. Its duty over the <em>header's</em> 30 K, not the exchanger's 20 K: the header supplies
        /// 60 °C and the subcircuit returns 30 °C, and the three-way valve makes up the difference by
        /// mixing the loop's own return back in.
        /// </value>
        public const double AhuHeaderFlow = 0.1914;

        /// <summary>Mass flow the radiator subcircuit draws from the header.</summary>
        /// <value>kg/s. Its duty over the same 30 K.</value>
        public const double RadiatorHeaderFlow = 0.2392;

        /// <summary>Mass flow through the source, by continuity at the attachment nodes.</summary>
        /// <value>
        /// kg/s. The sum of what the two subcircuits <em>draw</em>, and equally <c>HS1</c>'s own 54 kW
        /// over its 30 K rise — the two agree, which is the point of the fixture. It is not the sum of
        /// the loop flows: that was <c>F-16</c>, and it required a 40 °C return no arrangement of two
        /// mixing valves and two 30 °C loads can produce.
        /// </value>
        public const double SourceFlow = 0.4306;
    }

    /// <summary>The storage header — the stratified tank and thermal-order reference.</summary>
    public static class StorageHeader
    {
        /// <summary>Mass held by one of the five equal layers of the 300 dm³ tank.</summary>
        /// <value>kg, at the validation fixture's incompressible 1000 kg/m³.</value>
        public const double LayerMass = 60.0;

        /// <summary>Initial rate of temperature change of layer 2, where <c>S2</c> injects.</summary>
        /// <value>K/s. Every other layer's initial derivative is zero.</value>
        public const double InitialLayer2Derivative = 0.020;
    }

    /// <summary>The demand-step loop — the transient, transport and control reference.</summary>
    public static class DemandStepLoop
    {
        /// <summary>Residence time of one 2 m thermal cell in the recirculation pipe <c>PB</c>.</summary>
        /// <value>s, at the recirculation flow and DN20's 21.7 mm bore.</value>
        public const double ThermalCellResidenceTime = 9.58;

        /// <summary>Dead time from a change at <c>HE1</c> to its arrival at the measured node <c>N2</c>.</summary>
        /// <value>s, four thermal cells in series.</value>
        public const double DeadTime = 38.3;

        /// <summary>Largest time step the CFL rule permits, set by the smallest control volume.</summary>
        /// <value>s.</value>
        public const double CflStepLimit = 8.62;
    }
}
