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
| C-20 | [`22`](22-component-model.md) | **Every port list is written down twice**, and the two copies disagreed within an hour | `22` says a kind's registry entry is "built by each component's static registration" so the list exists once. The registry shipped in `P2.6`, a phase before any component existed, so `P3.3`'s classes declare their ports again — and the binder binds an unqualified connection against one copy while the solver indexes `SolveContext.Ports` against the other. A disagreement wires a script's second connection to the wrong port and produces confidently wrong numbers with no exception and no diagnostic. A cross-check test was written and **immediately caught one**: the two-way `valve` was given bidirectional ports, over-generalised from §4's three-way paragraph, where the registry correctly has inlet/outlet. Inverting the dependency so the component feeds the registry is a change to how the binder is fed and is not `P3.3`'s; the test is the seam until then. |
| C-19 | [`62`](../60-docs-and-devex/62-testing-strategy.md), [`22`](22-component-model.md) | **`62`'s worked example cannot evaluate the relation it claims to.** It builds `SolveContext.ForSingleComponent(FakeWater.Instance, massFlow: 0.2391)` — a substance and a flow, no port states — and calls `EvaluateResiduals` on a duty exchanger | `22`'s energy relation is `Q̇ = ṁ(h_out − h_in)` over the *solved* port enthalpies, and a context with no port states has no enthalpies to difference. The example's own constructor, `HeatExchanger(power:, inlet:, outlet:)`, implies the other reading: a duty from stated terminal temperatures and a `cp`. That is a real relation and it is the `FS2101` one — what three stated values imply about the fourth — so it ships as `HeatExchanger.ImpliedFlow`, a reported property rather than a residual. The test asserts both, and asserts they agree. `SolveContext.ForSingleComponent`, added on the strength of the example, was removed the same day: nothing could use it. |
| C-18 | [`22`](22-component-model.md) | **A closed equal-percentage valve is not shut**, and nothing says so | `φ = R^(x−1)` with R = 50 gives `φ(0) = 0.02`, so a valve at position 0 still passes 2 % of its rated Kv. That is what the characteristic *means* — a real valve's shut-off comes from its seat, which is a leakage class and not part of the curve — so forcing `φ(0) = 0` would be inventing a seat. But `22` states the characteristic and never states the consequence, and the consequence is that a bypass closed by an equal-percentage valve keeps flowing. No code change; asserted in a test named for it, so the next reader meets it deliberately rather than while debugging a bypass. |
| C-16 | [`22`](22-component-model.md) | **`EvaluateResiduals` cannot call `ISubstance` and also allocate nothing**, and `22` asks for both | The signature's own remarks say it "must not call the property backend more than necessary", which reads as a budget. It is not one: `FluidState` is a `sealed record`, so *any* state fix inside a residual evaluation allocates, N+1 times per Newton iteration. The two requirements are only compatible if the answer is **none**. Resolved by giving `SolveContext` per-port properties that are already evaluated — pressure, enthalpy, temperature, density, specific heat — so a residual is arithmetic by construction rather than by discipline. This is also what `21`'s per-solve cache was always implying. |
| C-17 | [`22`](22-component-model.md), [`31`](../30-solver/31-solver-architecture.md), [`62`](../60-docs-and-devex/62-testing-strategy.md) | **`SolveContext` is named by three documents and defined by none** | `22` declares `EvaluateResiduals(in SolveContext, Span<double>)` and `62`'s worked example calls `SolveContext.ForSingleComponent(...)`, while `31` — which owns the unknown and equation registry, and writes out `UnknownDeclaration`, `EquationDeclaration`, `StateVector` and `ScalingVector` in full — stops short of this one. It cannot be deferred to `P3.6`, because `08` builds the components first and they cannot be written without it. Given to `22` on the same reasoning as `NodeObservation` in `P3.0`: it describes what a component *reads*, and the component interface is `22`'s. |
| C-14 | [`22`](22-component-model.md), `D-61` | **What a flow sensor reads was never defined at a junction.** §7 says only "a sensor reads the node it is attached to" — which names one number on a two-branch node and two or three on a tee | Unnoticed because the ambiguous case does not arise until something has to return a value: a temperature or a pressure sensor has no such problem, since a node carries one of each. Settled as the **sum of the flows entering the node**, which equals the through-flow wherever that exists and is well defined everywhere else. The alternatives are not academic — at a mixing junction they differ by a factor of two, and every one of them looks like a plausible meter reading. `22` §7 and [`flow_sensor`](../../docs/functions/flow-sensor.md) now say so. |
| C-15 | [`23`](23-topology-and-graph.md) | **The lowering document never mentions observers**, and its step 1 says "each `ComponentSymbol` becomes an `IComponent`" | `D-61` added a component family that must be kept *out* of `CircuitGraph`, and the document that owns lowering was not updated — so `P3.4` would have read it as instructions to instantiate sensors into the graph, which is exactly the hundred-identity-equations outcome `D-61` exists to prevent. Same shape as `L-42`: the amendment reached the specifying document and not the presupposing one. `23` gains a lowering section, and invariant 9 — adding observers leaves the graph byte-identical — which is the property a test can actually hold. |
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

**Two documents presupposed a decision that had been reversed, and neither was found by review.**
`L-42` and `C-15` are the same defect in two tiers: `D-61` was applied everywhere sensors are
*specified* and nowhere they are merely *assumed*, and six months of `plan-review` passes did not
notice, because a paragraph reading "the sensors `D-23` defers" is internally consistent and only
wrong against a decision made elsewhere. A grep for the *superseded* decision's number is the check
that would have found all four in seconds; the decision log records what amends what, so the sweep is
mechanical. Worth doing whenever a `D-` entry amends another.

**The observer family cost about a tenth of what a flow component will.** `IComponent`, `IObserver`,
`IController`, `NodeObservation`, one `PlacedSensor` and the model step come to roughly 250 lines and
14 tests, because an observer has no ports, no residuals, no sizing and no state — its `Read` is a
three-case projection. That asymmetry is the argument `08` makes for building it first, and it holds:
nothing here needed a decision the solver has not made yet, so nothing here has to be revisited when
it does.

**One class serves all three instruments, and `D-61` does not forbid it.** The decision rejected one
`sensor` keyword with `measures=t` — a statement about the *script*, where three keywords buy three
tag codes for free. The C# reading is registry-driven (`ComponentKindInfo.MeasuredProperty`), so three
near-identical classes would have bought nothing. A test asserts every observer kind in the registry
has a reading, which is what keeps the shortcut honest when a fourth instrument appears.

**The fakes are about a thousand times faster than the backend, not ten.** First run of
`StateTimingDiagnostics` on this machine (Debug, WSL, 32 cores): `Water` fixes a state from `(p, T)`
in a median 204 µs, `ConstantPropertyWater` and `LinearPropertyWater` in 0.22 µs each. That ratio is
the whole argument for `ISubstance`, and it is three orders of magnitude rather than the one an
earlier note implied — a component suite making a few thousand property calls is the difference
between a second and a millisecond. The two fakes are indistinguishable from each other, which is
worth knowing: the linear one costs nothing extra, so there is no speed reason to reach for the
constant one when the non-constant one is the stronger check.

**CoolProp's documented pair ordering shows up cleanly once the JIT is out of the way.** Water
`(p, h)` is 425 µs against `(p, T)`'s 204 — the "much slower" the documentation promises for a pair
where neither T nor ρ is given, at almost exactly 2×. `SaturationPressure` at 266 µs is a surprise
worth carrying into `P3.6`: it costs as much as a full state fix, so a cavitation check per iteration
is not the free guard it looks like.

**Cold calls are 5–10× the steady-state cost, and they are not the same number twice.** Saturation's
first call was 2245 µs and `(p, h)`'s 1400 µs against steady-state 266 and 425. That is the cost the
first keystroke after an edit pays, which is why the report keeps it in its own column rather than
warming it away.

**Water `(p, T)` is the one row whose mean should not be quoted.** Median 204 µs, minimum 182,
maximum 304, standard deviation 51 — and raising the warm-up from 20 to 200 calls, past tiered
compilation's promotion threshold, did not narrow it. Whatever the spread is, it is not the JIT. Every
other row on the same run has a deviation under 5 % of its median, so it is specific to that pair
rather than to the machine being noisy.

**A mixture costs 550 times what a pure fluid does, and one pair never comes back.** `BackendPairDiagnostics`
over all five fluid families. Water-ethanol 60/40 fixes `(T, p)` in 34.8 ms against pure water's 63 µs;
`(T, d)` takes 786 ms, `(T, s)` 517 ms, `(p, s)` 288 ms and `(p, h)` 267 ms. `(p, d)` is refused
outright — *"DP_flash not ready for mixtures"* — and **`(h, s)` does not return at all**, iterating
without converging and without a limit of its own. Everything above is the argument for `D-28`
deferring mixtures, now with numbers: a Newton iteration that fixed one mixture state per residual
evaluation would take minutes per solve, and one unlucky pair would hang the process.

**`(T, h)` is unsupported on every family, not just on water.** Pure, pseudo-pure, both incompressible
kinds and the mixture all answer `HmassT_INPUTS is not yet supported`. The `P3.1` observation
generalises, and `21`'s choice of `(p, h)` is the only enthalpy pair there is.

**The incompressible backend supports four pairs out of ten, and is ten times faster than HEOS.**
`(T, p)`, `(p, h)`, `(p, s)` and `(p, d)` work at 6–10 µs; `(T, s)`, `(T, d)`, `(h, s)`, `(h, d)` and
`(s, d)` are all refused. Both halves matter for glycol: it would be **cheaper** than water, not dearer
— 48 of SharpProp's 120 INCOMP fluids are solutions taking a concentration — but a component wanting a
state from a temperature and a density could not have one. Worth knowing before `D-28` is revisited.

**Humid air's property *reads* cost more than its state fix, which is the reverse of pure water.**
Fixing `(p, T, RH)` and reading one property is 1.2 µs; `HumidAirSubstance` fixing the same state and
reading ten is ~100 µs. So a humid-air property read is roughly 10 µs, and `C-12`'s "eager is free"
argument — measured on water, where a read was 0.003 µs — does not transfer to it.

**Two measurements of the same thing disagree, and it is not yet resolved.** Water `(T, p)` reading one
property is 63 µs here; through `ISubstance`, building a full `FluidState` with seven properties, it is
204 µs. That implies ~20 µs per read on a pure fluid, against the 0.003 µs the M0 spike recorded and
`PropertyBackend` still documents. One of the two is measuring something other than what it says.
**This is open**, and it should be settled before `P3.6` designs the per-solve cache, because `C-12`
rests on the smaller number.

**A metadata field that looks like a discriminator was not one.** The first version of the family split
used SharpProp's `FractionMin`/`FractionMax`, and produced a census claiming all 305 HEOS fluids and all
120 INCOMP ones take a concentration — pure water included. The range defaults to 0–1 on everything;
`Pure()` is the flag that discriminates. Caught only because the census printed a number obviously
impossible, which is an argument for making a probe report its whole population rather than only the
rows it sampled.

**A node is a state point, and nothing in `22`'s interface let it read its own state.** `SolveContext`
as first written carried port states and flows, which is everything a pipe or a valve needs — but a
node's pressure and enthalpy are its *own* unknowns, and the states on its ports belong to what is
attached to it. Added `Unknowns`, a span of the component's own declared unknowns in declaration
order. Found by writing the node, not by reading the document; `22` describes the node's equations
fully and says nothing about where `h_node` comes from.

**A residual needs viscosity, and the property set was chosen before anything needed one.** `PortState`
started with pressure, enthalpy, temperature, density and specific heat — the five an energy balance
wants. A pipe cannot form a Reynolds number without dynamic viscosity, so the set is now the seven
`FluidState` itself carries. Cheap to fix here and expensive later: the whole point of forbidding a
backend call inside `EvaluateResiduals` is that a component reaching for a missing property has no
other way to get it.

**Serghide's approximation holds to 0.01 % where `22` asks it to.** Checked against the implicit
Colebrook–White equation iterated in the test, which shares no code with it, at seven points spanning
Re = 4×10³ to 10⁸ and ε/D = 0 to 0.05. Worth recording as a *pass*: it is the acceptance criterion most
likely to have been optimistic, and it was not.

**`f = 64/Re` cannot be written literally in a residual.** At rest the factor diverges and the term it
multiplies vanishes, so evaluating it gives `∞ × 0` and a `NaN` that poisons the entire Newton step —
not just the pipe's own row. Substituting Re back gives `32·μ·L·v/D²`, which is linear in velocity,
exactly zero at rest and has a finite derivative there. The two are algebraically identical and only
one of them can be evaluated.

**A zero-allocation test that allocates its own fixture is worse than no test.** The first run reported
21 600 bytes, every one of them from collection expressions in the test's own arguments rather than
from the components. It fails for a reason the code under test cannot fix, which is the shape of an
assertion that gets suppressed rather than investigated. Buffers are built once, outside the measured
region.

**C¹ continuity cannot be checked by comparing neighbouring finite differences.** The first attempt
required successive numerical slopes across the upwinding band to stay close, and they are not meant
to: the true derivative sweeps from 0 to 3.75 × 10⁷ J/kg per kg/s across a band 2 g/s wide, so the
differences are legitimately far apart and the test failed on correct code. What C¹ actually claims is
that the *one-sided* derivative at each join is zero, so the test now probes the edge with a shrinking
step and requires the measured slope to shrink with it. A corner would hold it constant.

**Both of `22`'s trap-shaped acceptance criteria caught nothing, because they were read first.** The
pump's affinity law and the valve's regularisation are the two places the document stops to explain
why the obvious implementation is wrong — `n²H₀ − k(ṁ/n)²` leaves a spare `1/n²` that is *silent at
n = 1*, and a straight line through the origin matches the valve law's value while missing its slope by
exactly a factor of two. Written from the document, both came out right first time and the tests passed
on the first run. Worth recording as evidence for the practice: a criterion that names the wrong answer
is worth more than one that names the right one, and `22` writes several of them that way.

**Both failures in the valve and pump suite were the test's arithmetic again.** A pump's curve reaches
zero head at `√(H₀/k) = √6 ≈ 2.449` times the duty flow, and the expectation said `√1.2` — confusing
the shut-off *head* ratio with a *flow* ratio. And `ρgH` at 0.5 m is 4894.5 Pa, not the 4890 the comment
rounded to. That is now three suites running where every first-run failure has been the test rather
than the code; the pattern is that hand-checked expectations are the least-reviewed line in a test, and
they are the only line that carries a claim.

**The heat exchanger was the smallest of the six, because `08` had already split it.** `P4.1` owns the
rated two-sided exchanger with ε-NTU *and* LMTD as separate routes, on the stated grounds that "two
formulations sharing no code is what makes UA = 12.07 kW/K a validation rather than a regression" — so
building ε-NTU here would have pre-empted the check it exists for. `P3.3`'s exchanger is duty mode:
two equations, nine tests. Worth recording because the instinct on reading `22` §3 is that the
exchanger is the big one; the schedule had already made it the small one, and this is the first package
where checking `08` before writing changed the answer rather than confirming it.

**Nine tests passed on the first run, which had not happened before in this tier.** The three previous
component suites each had a first-run failure and every one of them was the test's own arithmetic. The
difference here is that the exchanger's numbers were taken from the fake's declared constants rather
than typed from a hand calculation — `ImpliedFlow(SpecificHeat, 30)` instead of `0.239006` written out.
The one hand-typed figure in the file, 17 448 W, is a residual off the solution, which no comment could
have got from anywhere else.

**Two `PortRole` enums existed for about twenty minutes, and the compiler was happy with both.**
`Language.PortRole` had shipped with the registry in `P2.6`; `P3.3` declared an identical one in
`Components` because `22` lists `PortRole` beside `Port` in the component-model interface. Different
namespaces, no file using both, so nothing failed — the build stayed clean and the duplicate would have
survived until the first file that needed to convert between them. Deleted in favour of the registry's,
which was also the better-documented one. The general shape is worth naming: a duplicated *type* is
invisible to every check this repository runs, unlike a duplicated *value*, which the registry
cross-check does catch.

**The tank was the quietest of the six.** Fifteen tests, no first-run failures, and nothing in `22` §6
turned out to be wrong or ambiguous — the layer-boundary rule is written as an explicit formula
precisely so two implementations cannot round it differently, and it transcribed without a decision.
Recorded because the pattern across `P3.3` is that the sections which stop to explain *why* the obvious
implementation is wrong (the pump's affinity laws, the valve's regularisation) produced no defects,
while the sections that state a result plainly are where the gaps were — `SolveContext` undefined,
`h_node` unsourced, viscosity missing from the port state.

