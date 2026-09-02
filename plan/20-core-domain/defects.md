---
id: 20-core-domain-defects
title: What implementing against the core domain found
tier: 20-core-domain
owns: [defect and observation record for documents 21-27]
---

# What implementing against the core domain found

Defects, deferrals and observations from implementing against `21`–`27`. The rule and its reasoning
are in [`08-implementation-sequence`](../00-foundation/08-implementation-sequence.md).

**Only [`22-component-model`](22-component-model.md) has been implemented against so far**, and only
its metadata half: P2.6 built the component registry the binder reads, and P2.8 the port and tag
derivation that reads it. No physics, no residuals, no catalogue. `21`, `23`, `24`, `25`, `26` and `27` are unread at implementation depth, so their absence
from this file means nothing has looked, not that nothing is wrong.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| C-1 | [`22`](22-component-model.md) | `pipe.insulation` and `pump.curve` are documented but not registered | Both are placeholders in `22`'s tables with no dimension and no range — heat loss is post-v1, and named curves arrive with the catalogue in P3.5. Registering them would make `FS1503` accept a name nothing reads, so a user would get silence where they expect an effect. The registry-comparison test carries both as named exceptions with these reasons, so they cannot be forgotten or silently added. |
| C-2 | [`22`](22-component-model.md) | Every invariant except the parameter-set one is unasserted | Invariants 2–7 are about `EvaluateResiduals` — zero allocation, determinism, fixed length, scaled residuals, continuity — and no component exists to assert them on. **They must be written in P3.3, with the components**, not retrofitted across six kinds afterwards; `08` says the same. |
| C-4 | [`22`](22-component-model.md) | `heat_exchanger` mode selection is unimplemented | Duty, Rated and Coupled are computed at lowering from connections and stated properties. The registry marks `in2`/`out2` optional, which is all the binder needs; the rest is P3.3/P4.1. |
| C-7 | [`23`](23-topology-and-graph.md), [`22`](22-component-model.md) | Nothing says what boundary condition an I3-inferred node carries, and `FS2107` now depends on the answer | The binder exempts an I3 node from `FS2107` because [`15`](../10-language/15-semantic-model.md) says the node *is* the boundary that rule created — which is what makes `samples/m1-syntax-reference.fluid` produce two dead-end warnings rather than four. `23`'s table gives conditions for a *declared* degree-one node and says nothing about an inferred one. If lowering decides an I3 node carries nothing, the exemption is wrong. P3.3. |
| C-8 | [`22`](22-component-model.md) | The tank's per-layer and per-port **properties** are still not registered | P2.8 materializes the *ports* — `in3` exists when a qualified endpoint or an `in3_elevation` evidences it — but `t1`…`tN`, `inN_t` and `outN_t` are readable properties, and nothing resolves `T1.t3`. The families are in the registry; a property lookup does not consult them. P3.3, with the tank that computes them. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| C-13 | [`07`](../00-foundation/07-quality-attributes.md), [`21`](21-fluid-and-state.md) | The humid-air row bounds the **state** at 0–50 °C, and is read as bounding every property on it — but a derived property leaves that box while the state stays inside it | Measured: air at 5 °C and 30 % RH is a perfectly ordinary winter state and its dew point is −9.9 °C, nine degrees below the validated minimum. Nothing is wrong with the number; what was wrong is a claim that appeared to cover it. No code change — `HumidAirState` carries the dew point as a derived property and never re-fixes a state at it, so no diagnostic is owed — and `FS2003` correctly refuses only a caller who *does* re-fix there. [`fluid`](../../docs/functions/fluid.md) now says so with this example. Found by a validation test that cooled a state to its own dew point to check saturation. |
| C-10 | [`21`](21-fluid-and-state.md) | `ISubstance`'s pressure parameter is named `absolutePressure`, which contradicts the same document's "the single adapter adds the model's recorded atmosphere" and [`13`](../10-language/13-type-and-unit-system.md)'s definition of `Dimension.Pressure` as **gauge** | If the interface took absolute, the caller would have converted and "exactly one adapter converts" would be false. Renamed `gaugePressure`: every pressure the model carries is gauge, and `SubstanceBase.Absolute` adds the atmosphere once, immediately before a measurement. |
| C-11 | [`21`](21-fluid-and-state.md) | `FS2002` was recorded as unraisable — "both pairs shipped here are always independent for a single-phase liquid" | False, and measured: on the boiling line pressure and temperature are one constraint, and the backend refuses with "Saturation pressure [101325 Pa] corresponding to T [373.124 K] is within 1e-4 % of given p". Water's own validated domain contains that line, so the pair a script is most likely to write is the one that fails. Registered and raised. |
| C-12 | [`21`](21-fluid-and-state.md) | `FluidState`'s derived properties are specified as "computed on demand and cached" | Computed once when the state is built instead, which is stronger — no first access slower than the rest, and no cache to invalidate. Measured why: fixing a state costs 321–388 µs depending on the pair and reading a property off the result costs 0.003 µs, so there is nothing worth deferring. The same measurement makes `21`'s per-solve cache a requirement rather than an optimisation. |
| C-5 | [`22`](22-component-model.md) | Convention 5 requires every parameter to declare a display precision, and **no table carries one** | `ParameterInfo.DisplayPrecision` is the authority, and `22` now says so. A column of precisions in the document would be a second place to keep in step, for a formatting decision with no bearing on the physics. |
| C-6 | [`22`](22-component-model.md) | The ranges are written in the "bare number means" unit, and nothing said so | Stated, with the failure it prevents: transcribing a temperature range of −50 … 300 as SI by hand gives −50 K, and every plausible temperature then falls outside its own range. The registry converts at build time instead. |
| C-9 | [`22`](22-component-model.md) | The optional flag on a port had never been exercised | It is what keeps a heat exchanger with no secondary side from growing an inferred circuit: I3 terminates `in`/`out` and leaves `in2`/`out2` alone, and the three-way valve's `c` the same way. `22` marked them optional before anything read the flag; P2.8 is the first thing that did, and the reference circuit's inferred-component count is the assertion. |

## Observations

**The parameter tables are now machine-read.** `22`'s own parameter-registry section asks for a test
comparing the registry against its tables, and there is one — it parses the tables out of the document
and compares in both directions. Two things it needs to keep working:

- A component section is bounded by the *next* second-level heading, not the next numbered one.
  Without that the tank's section swallows `## Parameter registry` and `## Error cases`, and every
  `FS21xx` code becomes a parameter of `tank`.
- It reads **only the table headed `| Parameter |` or `| Parameter pattern |`**. A component section
  holds other tables whose first cell is a backticked name — the exchanger's ε-NTU arrangement
  formulae are one — and reading those as parameters made `counter` a parameter of `heat_exchanger`.

Both are the kind of thing that will be re-derived painfully if this note is not here.

**The controller's parameters are not in `22` at all.** `kp`, `ki` and `kd` are specified in
[`34-controllers`](../30-solver/34-controllers.md), because `22` describes six *flow-component
families* and a controller carries no flow. The registry-comparison test therefore skips any kind
`22` does not document, which is correct but means the controller's parameter set has no
document-drift guard. It will need one when `34` is implemented against.

**A tag code is checked by lexing the tag it produces, not against the unit table.** `22` says no tag
may lex as a quantity literal; the check that matters is whether `400PU01` comes back as one
identifier, since it is the whole tag that must not read as a number and a unit.

**`node` and `pipe` carry no tag code deliberately**, and the registry test asserts exactly that pair.
A future kind added without a code should have to justify itself against this, not inherit silence.

**CoolProp's native pair is (T, ρ), not (p, T), and no pair is free.** Its documentation says so —
"the equations of state are based on T and ρ as state variables, so T, ρ will always be the fastest
inputs", and "P,T will be a bit slower (3-10 times), followed by input pairs where neither T nor ρ are
specified, like P,H; these will be much slower." Measured through SharpProp on a debug build, sharing
one instance: (T, ρ) 321 µs, (p, T) 336 µs, (p, h) 388 µs. The ratios CoolProp describes are about the
flash and are nearly hidden here by the ~320 µs SharpProp charges per `WithState` whatever the pair —
so the lever for `P3.6` is the number of calls, not the choice of pair.

**(T, h) is not a supported pair at all.** `This pair of inputs [HmassT_INPUTS] is not yet supported`.
Worth knowing before a component is written that would want it; `(p, h)` is what `21` chose and what
exists.

**Constructing a `Fluid` per call cost more than the measurement.** 535 µs on a fresh instance against
336 µs on a shared one — the constructor was 37 % of the call. The M0 spike had already proved
`WithState` on a shared instance is thread-safe, so one static instance is both correct and the
cheaper half of the two options.

**The architecture test wanted the type renamed, and was right.** It searches `src/` for the string
`SharpProp`, so a wrapper called `SharpPropBackend` put the package's name into every file that called
it and tripped the one-file rule it was built to satisfy. `PropertyBackend` says what it does rather
than what it wraps; if one file is meant to own a dependency, no other file should have reason to name
it.

**CoolProp offers an IF97 water backend.** "If you are only interested in Water properties, you can
look into using the IF97 (industrial formulation) backend", alongside a tabular one. Neither is used;
recorded here because `P3.6` is where the property call count becomes a budget and this is the first
lever to reach for after caching.

**Only one of the four water properties has a numeric oracle across the range.** `62`'s rule 3 forbids
production-backend output as expected data, and of density, specific heat, viscosity and thermal
conductivity, only density has a published closed form simple enough to transcribe — Kell's 1975
equation, which pins six states to 0.1 %. The other three are checked at `21`'s single published state
and otherwise only *behaviourally*: viscosity falls with temperature, conductivity rises, specific
heat has its minimum near 35 °C. That is a real check — it fails against a constant-property or a
transposed table — and it is weaker than density's, so `V4` should not be read as four properties
validated alike. Closing the gap needs a second tabulated source, which is a sourcing task rather than
a testing one.

**The property-accuracy tier found no defect in the code it was written to check.** Every failure in
its first run was either the test asking for a state on a phase boundary (`F-14`), an arithmetic slip
in the test's own gauge-to-absolute conversion, or a derived property outside the validated box
(`C-13`). Worth recording because it is the first tier where that has been true: `P2.x` found defects
in the *documents* at roughly the rate it wrote tests, and `P3.1`'s own suite passed on the first run
too. The property layer is small, has one external dependency and no user input, which is the profile
of code that a validation tier confirms rather than corrects.
