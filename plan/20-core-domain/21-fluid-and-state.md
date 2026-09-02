---
id: 21-fluid-and-state
title: Fluids and thermodynamic state
tier: 20-core-domain
status: reviewed
owns: [ISubstance abstraction, FluidState, SharpProp adapter, property caching, humid air]
depends_on: [13-type-and-unit-system, 16-diagnostics]
traces_to: [R-07, R-08, R-16, R-40, R-43]
open_questions: 0
last_review_pass: 2
---

# Fluids and thermodynamic state

## Purpose

Everything downstream — every component equation, every solver residual, every warning about freezing
— reduces to "what are this fluid's properties at this state". This document owns that boundary. It
also owns the decision to put an abstraction between Core and SharpProp, which is the difference
between a solver that can be unit-tested in milliseconds and one that cannot be tested without
CoolProp's property tables loaded.

## Responsibilities

**Owns.** `ISubstance`, `FluidState`, the SharpProp adapter, the property cache, the humid-air
surface, and the substance registry that maps a script name to an implementation.

**Explicitly does not own.** Units ([`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md)),
components ([`22-component-model`](22-component-model.md)), where states live in the graph
([`23-topology-and-graph`](23-topology-and-graph.md)).

## The SharpProp boundary

**Core depends on `ISubstance`; exactly one class depends on SharpProp.** Three reasons, in order of
how much they matter:

1. **Test speed.** Property calls dominate every solver iteration. A test double returning
   incompressible-water constants makes component and solver tests run in microseconds; the real
   backend is exercised by a dedicated property-accuracy suite that runs once, not in every test.
2. **Substitutability.** Humid air, incompressible approximations, and future refrigerant mixtures all
   satisfy the same interface. A component asking for enthalpy should not know which is behind it.
3. **Package risk.** SharpProp's exact API and native packaging are M0 spike gates (`05`). One adapter
   class is a contained blast radius; property calls scattered through twenty components is not.

### What the adapter is expected to wrap

Expected from SharpProp's published surface; the M0 spike must compile, publish, and exercise these
operations on every supported OS before M1 starts:

| Expected SharpProp type | Used for |
|---|---|
| `Fluid` + `FluidsList` | Pure fluids: water, refrigerants |
| `Mixture` | Glycol solutions |
| `HumidAir` | Psychrometrics (`R-08`) |
| `Input` / `InputHumidAir` | Specifying the two independent properties that fix a state |

SharpProp returns UnitsNet quantities. If confirmed, `Directory.Packages.props` takes UnitsNet as a
direct dependency and `Quantity` ([`13-type-and-unit-system`](../10-language/13-type-and-unit-system.md)) converts at
the adapter boundary rather than Core inventing a parallel unit system.

## `FluidState`

A state is **two independent intensive properties plus a substance identity**. Everything else is
derived. Storing derived properties as fields invites the classic bug where temperature is updated and
enthalpy is not.

```csharp
/// <summary>A fully determined thermodynamic point.</summary>
/// <remarks>
/// Immutable. Fixed by exactly two independent properties; every other property is computed on
/// demand and cached. Two states are equal when their substance and their two fixing properties
/// are equal — never compare derived properties for equality.
/// </remarks>
public sealed class FluidState
{
    /// <summary>The substance this state belongs to.</summary>
    public ISubstance Substance { get; }

    /// <summary>Absolute pressure.</summary>
    /// <value>
    /// Pa absolute, always. Script values use explicit gauge or absolute pressure units; gauge values
    /// are converted once using the script's declared atmosphere at the language boundary (`D-26`).
    /// </value>
    public Quantity Pressure { get; }

    /// <summary>Temperature.</summary>
    /// <value>K.</value>
    public Quantity Temperature { get; }

    /// <summary>Specific enthalpy.</summary>
    /// <value>J/kg. Datum is the substance's own reference state; only differences are meaningful.</value>
    public Quantity Enthalpy { get; }

    /// <summary>Density.</summary><value>kg/m³.</value>
    public Quantity Density { get; }

    /// <summary>Dynamic viscosity.</summary><value>Pa·s.</value>
    public Quantity DynamicViscosity { get; }

    /// <summary>Specific heat at constant pressure.</summary><value>J/(kg·K).</value>
    public Quantity SpecificHeat { get; }

    /// <summary>Thermal conductivity.</summary><value>W/(m·K).</value>
    public Quantity ThermalConductivity { get; }

    /// <summary>Phase at this state.</summary>
    public Phase Phase { get; }
}
```

**"Only differences are meaningful" on enthalpy is not pedantry.** CoolProp's reference state for water
differs from IAPWS steam-table convention; a test asserting an absolute enthalpy value against a
textbook will fail for a correct implementation.

## `ISubstance`

```csharp
/// <summary>A substance whose thermodynamic properties can be evaluated.</summary>
public interface ISubstance
{
    /// <summary>Script name, e.g. <c>water</c>.</summary>
    string Name { get; }

    /// <summary>Fixes a state from pressure and temperature.</summary>
    /// <returns>The state, or a failure describing why the pair is invalid or out of range.</returns>
    Result<FluidState> FromPressureTemperature(Quantity gaugePressure, Quantity temperature);

    /// <summary>Fixes a state from pressure and specific enthalpy.</summary>
    /// <remarks>
    /// The pair the solver uses. Energy balances produce enthalpy, and going back through
    /// temperature would require inverting cp — which is exactly what the property backend does
    /// correctly and a hand-rolled inversion does not.
    /// </remarks>
    Result<FluidState> FromPressureEnthalpy(Quantity gaugePressure, Quantity enthalpy);

    /// <summary>Freezing point at the given pressure.</summary>
    /// <value>K. Backs the FS4001 warning.</value>
    Quantity FreezingPoint(Quantity pressure);

    /// <summary>Saturation (boiling) pressure at the given temperature.</summary>
    /// <value>Pa absolute. Backs FS4002 and the FS4003 cavitation check.</value>
    Quantity SaturationPressure(Quantity temperature);

    /// <summary>The pressure and temperature range this substance's data is valid over.</summary>
    /// <remarks>Outside it, results are extrapolation. Checked before every call so the user gets
    /// FS2003 rather than a silently wrong number.</remarks>
    StateRange ValidRange { get; }
}
```

**`Result<T>`, not exceptions.** A state request outside the valid range is ordinary — a solver
overshoots during iteration and asks for something impossible, then backtracks. Exceptions in that path
would be both slow and wrong (`error-handling.md`'s rule; principle P4 extended into physics).

## Humid air

`R-08` makes psychrometrics a first-class validated property capability. Humid air needs **three** independent properties, not two —
pressure, one temperature-like property, and one humidity-like property — so it cannot satisfy
`ISubstance` as written.

```csharp
/// <summary>Humid air: a substance requiring three independent properties.</summary>
public interface IHumidAir : ISubstance
{
    /// <param name="humidityRatio">kg water per kg dry air.</param>
    Result<HumidAirState> FromPressureTemperatureHumidity(
        Quantity pressure, Quantity dryBulb, Quantity humidityRatio);

    Result<HumidAirState> FromPressureTemperatureRelativeHumidity(
        Quantity pressure, Quantity dryBulb, Quantity relativeHumidity);

    Result<HumidAirState> FromPressureEnthalpyHumidity(
        Quantity pressure, Quantity enthalpy, Quantity humidityRatio);
}

/// <summary>A humid-air state, adding the psychrometric properties to the common set.</summary>
public sealed class HumidAirState : IThermodynamicState
{
    /// <summary>Humidity ratio.</summary><value>kg water / kg dry air.</value>
    public Quantity HumidityRatio { get; }

    /// <summary>Relative humidity.</summary><value>Dimensionless, 0 to 1.</value>
    public Quantity RelativeHumidity { get; }

    /// <summary>Wet-bulb temperature.</summary><value>K.</value>
    public Quantity WetBulb { get; }

    /// <summary>Dew-point temperature.</summary><value>K. Backs condensation warnings.</value>
    public Quantity DewPoint { get; }

    /// <summary>Enthalpy per kg of DRY air, not per kg of mixture.</summary>
    /// <value>J/kg dry air. The psychrometric convention; mixing the two bases is a
    /// several-percent error that looks like a plausible result.</value>
    public Quantity DryAirBasisEnthalpy { get; }
}
```

**The enthalpy basis is the trap.** Psychrometric enthalpy is per kilogram of *dry air*; every other
substance's is per kilogram of *fluid*. An air-side energy balance written as if they were the same is
wrong by the humidity ratio — a few percent, which is small enough to look like a modelling choice
rather than a bug. The separate property name and shared `IThermodynamicState` interface prevent a
caller from reading fluid-basis enthalpy through a base-class reference. The shared interface exposes
only basis-independent temperature, pressure, density, viscosity, conductivity, and phase.

### Pressure reference boundary

Hydraulic state is stored and displayed as gauge pressure. Immediately before any property call, the
single SharpProp adapter adds the model's recorded atmosphere (101.325 kPa in v1); explicit `kPaa` or
`bara` input is normalized to the equivalent gauge state during binding. No other type performs this
conversion. Property results and diagnostics state whether pressure is gauge or absolute (`D-26`).

## Property caching

A steady-state solve of a modest circuit makes tens of thousands of property calls, most of them
repeats at states the solver has already visited within its iteration.

- **`FluidState` caches its own derived properties**, computed on first access. States are immutable,
  so the cache needs no invalidation — the single largest source of correctness bugs in this area
  disappears by construction.
- **A per-solve state cache** keyed by the exact IEEE-754 bit patterns of `(substance, p, h)`.
  Approximate or rounded cache keys are forbidden: Newton's finite-difference perturbation is
  `sqrt(machine epsilon)` relative, and coarser rounding can turn a real property derivative into a
  zero Jacobian column. The cache is bounded by count and cleared per solve; it is never static/global,
  which would leak across models and make results depend on what ran before.
- **Measure before optimising further.** The cache design above is the cheap part. Anything more —
  interpolation tables, incompressible fast paths — needs a benchmark first, and
  [`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md) owns whether the solver
  is actually property-bound.

## Substance registry

```csharp
public interface ISubstanceRegistry
{
    /// <summary>Resolves a script name to a substance.</summary>
    /// <returns>The substance, or a failure listing the available names.</returns>
    Result<ISubstance> Resolve(string name, IReadOnlyList<Quantity> arguments);
}
```

v1 registry names are `water` for solved hydronic circuits and `air` for metadata/property validation.
`air` cannot lower to a v1 circuit (`D-28`). Glycol mixtures are post-v1: accepting a concentration
before the real backend and freezing-basis behavior are validated would overstate supported physics.

## Invariants

1. Exactly one type in Core references the SharpProp namespace, verified by an architecture test.
2. `FluidState` is immutable; every derived property is a pure function of the two fixing properties.
3. Every `ISubstance` method returns `Result<T>`; none throws for an out-of-range request.
4. Every state request outside `ValidRange` fails, and none is silently extrapolated. A failure during
   solver iteration is a `Result` failure and **emits no diagnostic** — the solver rejects the trial
   point and backtracks ([`32-steady-state-newton`](../30-solver/32-steady-state-newton.md)'s domain
   guarding), and a run that reaches an answer this way is a normal run. `FS2003` is emitted only for
   a state in the *converged* solution or in a stated boundary condition. Emitting it per rejected
   trial point would put hundreds of errors in the log for a circuit that solved correctly.
5. `FromPressureEnthalpy` then reading `Temperature` then `FromPressureTemperature` returns an
   equivalent state within round-trip tolerance.
6. Humid-air enthalpy is per kg dry air everywhere it appears, including in the model contract and the
   UI.
7. No property cache outlives a solve.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS2001` | Unknown substance name | Error | `There is no fluid called '{name}'. Available: {list}.` |
| `FS2002` | Property pair does not determine a state | Error | `Cannot fix a state from {a} and {b} — they are not independent here.` |
| `FS2003` | A **converged or stated** state outside the substance's valid range, *including a state above its boiling line* | Error | `{name} data covers {lo} to {hi}; this state is at {value}.` |
| `FS2004` | Property backend returned a non-finite value | Error | `Could not evaluate {property} for {name} at {state}.` |
| `FS2005` | Glycol concentration outside 0–60 % | Error | `Glycol concentration must be between 0 and 60 %.` |
| `FS2006` | Relative humidity outside 0–100 % | Error | `Relative humidity must be between 0 and 100 %.` |

## Worked example

Water at 20 °C and 2.013 25 bar **absolute** (1 bar gauge under `D-26`),
against CoolProp reference values:

| Property | Expected | Tolerance | Source |
|---|---|---|---|
| Density | 998.3 kg/m³ | 0.1 % | IAPWS-95 |
| Specific heat | 4184 J/(kg·K) | 0.1 % | |
| Dynamic viscosity | 1.002 × 10⁻³ Pa·s | 0.5 % | |
| Thermal conductivity | 0.598 W/(m·K) | 0.5 % | |
| Freezing point | 273.15 K | 0.05 K | |

And humid air at 25 °C dry bulb, 50 % RH, 101 325 Pa:

| Property | Expected | Tolerance |
|---|---|---|
| Humidity ratio | 0.009 93 kg/kg dry air | 0.5 % |
| Wet bulb | 17.9 °C | 0.1 K |
| Dew point | 13.9 °C | 0.1 K |
| Enthalpy | 49.93 kJ/kg **dry air** | 0.5 % |
| Density of the moist air | 1.177 kg/m³ | 0.5 % |

These are the M2 exit criteria made concrete. Two rows are traps.

**Enthalpy is the row that was measured rather than derived, and it is worth saying why.** Earlier
drafts stated 50.3 kJ/kg, which is what the ASHRAE ideal-gas relation
`h = 1.006·t + W·(2501 + 1.86·t)` gives. CoolProp returns **49.93**, because it treats the vapour term
with the real-gas formulation and an enhancement factor rather than with those two constants. The
M0 spike (P1.1) settled which is which: at 0 % RH the two agree to 0.002 kJ/kg, so this is not a
reference-state offset — the gap scales with humidity, reaching 1.55 kJ/kg at saturation. The same
correction applies to the humidity ratio, 0.009 93 against the ideal-gas 0.009 88.

**The basis trap is still real and is now sharper, not softer.** Per kg of *mixture* the same state
gives ≈ 49.8 kJ/kg, which is only 0.3 % from the correct per-dry-air 49.93. A basis error that used
to look like a 1 % discrepancy now hides inside the tolerance of the right answer, so it cannot be
caught by eye at all — only by V14, which asserts the basis through the adapter and the model
contract rather than inspecting a number.

**Density** catches a different one. 1.177 kg/m³ is the mass of *moist air* per m³, from
`(p − p_w)/(R_a T) + p_w/(R_v T)`. The number a reference table is far more likely to hand you is
**1.184 kg/m³**, which is *dry* air at the same temperature and total pressure — moist air is lighter,
because water vapour is lighter than the air it displaces. The gap is 0.6 %, which is outside this
row's own tolerance and inside the range a reader would accept as rounding.

## Acceptance criteria

- [ ] Both tables above pass as tests against the real SharpProp backend.
- [ ] An architecture test asserts SharpProp is referenced from exactly one file.
- [ ] The test double satisfies `ISubstance` and the component test suite runs against it with no
      SharpProp load.
- [ ] A state fixed by (p, h) and re-fixed by (p, T) round-trips within 1e-6 relative.
- [ ] Requesting water at 500 °C / 1 bar returns a failure, not an extrapolated number.
- [ ] A solve whose iterates leave `ValidRange` and recover emits **no** `FS2003`; one whose converged
      answer is outside it emits exactly one.
- [ ] The humid-air density row is asserted against the moist-air value, and a test comments that
      1.184 kg/m³ is dry air at the same state and must not be used.
- [ ] A solve of the M2 demo circuit makes fewer property calls with the cache than without, measured.
- [ ] Two states separated only by the finite-difference perturbation occupy distinct cache entries
      and produce the same numerical property derivative with caching enabled and disabled.
- [ ] The M0 SharpProp spike records package version, real signatures, supported input pairs,
      humid-air basis, valid ranges, exception/threading behavior, and reference-table results. M1/M2
      contracts cannot be frozen until it passes; a mismatch updates this plan before production code.
- [ ] Exactly one adapter converts gauge to absolute pressure, and `p=100 kPa` reaches the backend as
      201.325 kPa absolute.

## Open questions

None. The M0 spike is a pass/fail prerequisite rather than an implementation assumption; humid air
uses a non-hiding state interface; glycol is deferred until a separately validated mixture contract.
