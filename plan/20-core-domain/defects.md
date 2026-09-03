---
id: 20-core-domain-defects
title: What implementing against the core domain found
tier: 20-core-domain
owns: [defect and observation record for documents 21-27]
---

# What implementing against the core domain found

Defects, deferrals and observations from implementing against `21`–`27`. The rule and its reasoning
are in [`08-implementation-sequence`](../00-foundation/08-implementation-sequence.md).

`22` and `23` have been implemented against in depth, and `27` as of `P3.5`. `24`, `25` and `26` are
still unread at implementation depth, so their absence from this file means nothing has looked, not
that nothing is wrong.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| C-1 | [`22`](22-component-model.md) | `pipe.insulation` and `pump.curve` are documented but not registered | Both are placeholders in `22`'s tables with no dimension and no range — heat loss is post-v1, and named curves arrive with the catalogue in P3.5. Registering them would make `FS1503` accept a name nothing reads, so a user would get silence where they expect an effect. The registry-comparison test carries both as named exceptions with these reasons, so they cannot be forgotten or silently added. |
| C-4 | [`22`](22-component-model.md) | `heat_exchanger` mode selection is unimplemented | Duty, Rated and Coupled are computed at lowering from connections and stated properties. The registry marks `in2`/`out2` optional, which is all the binder needs; the rest is P3.3/P4.1. |
| C-7 | [`23`](23-topology-and-graph.md), [`22`](22-component-model.md) | Nothing says what boundary condition an I3-inferred node carries, and `FS2107` now depends on the answer | The binder exempts an I3 node from `FS2107` because [`15`](../10-language/15-semantic-model.md) says the node *is* the boundary that rule created — which is what makes `samples/m1-syntax-reference.fluid` produce two dead-end warnings rather than four. `23`'s table gives conditions for a *declared* degree-one node and says nothing about an inferred one. If lowering decides an I3 node carries nothing, the exemption is wrong. P3.3. |
| C-23 | [`22`](22-component-model.md) | **Eight of the sixteen `FS21xx` codes still cannot fire**, and each is blocked on a different stage | `P3.3` raises the ones decided by counting and comparing stated values: `FS2101`, `FS2103`, `FS2105`, `FS2108`, `FS2113`, `FS2114`, `FS2115`. The rest need something the binder does not have. `FS2102` (under-determined after sizing) needs the sizing loop, `P3.7`. `FS2104` (`head` against `dp`), `FS2111` (a duty beyond what the inlets allow) and `FS2116` (a tank substance outside the model) each need a resolved substance, which is `P3.4`'s. `FS2106` (discretization above the cap) must be raised by whatever actually clamps, and nothing subdivides a pipe until `P3.4`; raising it now would report a substitution that had not happened. `FS2109`, `FS2110` and `FS2112` are Rated and Coupled mode, `P4.1`. Listed rather than left implicit because a code with a descriptor and no emit site is invisible: the registry's own coverage test only checks that a *raised* code has a page. |
| C-38 | [`27`](27-component-catalog.md) | **EN 1057 permits several walls per outside diameter, and the shipped copper rows pick one without a source** | Copper was materially harder to source than steel and the reason is structural rather than bad luck: EN 1057 defines Y, X and Z wall series, more than one is on the market, and a 15 mm tube is 15 × 0.7 in the UK Table X range and 15 × 1.0 in several continental ranges — 13.6 mm of bore against 13.0, about 9 % in flow area. Six searches and eight fetches produced no two independent public tables covering a whole series: the ones that do are copies of the standard, which this project does not use. The rows ship as Table X, unverified, and the loader refuses them. Whoever closes it must also decide **which series a Finnish HVAC default should be**, which is a market question rather than a sourcing one. |
| C-36 | [`27`](27-component-catalog.md) | **EN 10220 and EN 10255 give DN150 different diameters, and nothing in the plan says which series a circuit is drawn in** | Two independent sources give EN 10220's Series 1 as … 114.3, 139.7, **168.3**, 219.1 …, with no 165.1 in it; EN 10255's threadable 6″ tube is **165.1**. So DN150 is a different pipe depending on which standard the script means, and `27` names `pipes-steel-en10255` and `pipes-steel-en10220` as separate catalogues without saying that they disagree at a size both contain. **The DN150 half is settled**: `D-67` takes 165.1 for `steel_en10255`, decided by the published 19.7 kg/m, which 165.1/5.0 reproduces as 19.74 and 168.3/5.0 does not. What stays open is that `27` names both catalogues without saying they disagree, and that no script can yet say which series it means. This is also the likeliest explanation for `C-32`: `ReferenceBores` is labelled EN 10220 and the shipped table is EN 10255, so their DN32 and DN40 bores were never meant to match — which nothing in either file says. |
| C-32 | [`27`](27-component-catalog.md) | **The lowering fixture and `27`'s worked table disagree about DN32 and DN40** | `ReferenceBores` gives DN32 a 35.9 mm bore and DN40 41.8 mm; EN 10255 medium gives 42.4 − 2×3.2 = **36.0** and 48.3 − 2×3.2 = **41.9**, and `27`'s own gradient table says 36.0. The fixture is labelled EN 10220, so the difference may be deliberate — but two tables in one repository disagreeing about DN32's bore is how the real one gets doubted, and neither says which series a reference circuit is drawn in. Not resolved by `P3.5`: migrating the topology tests onto the shipped catalogue has to wait until its rows are verified, and changing the fixture's numbers now would move assertions in tests that are about something else entirely. |
| C-25 | [`23`](23-topology-and-graph.md) | **Nothing says what a branch's `Path` order means**, and two readings are both defensible | The document tabulates the cooling loop's branch 2 as "`PU1`, `PU1__HE1`, `HE1`, `HE1__3WV`" — source order, reading down the connection list — and a decomposition walks from whichever end it starts at, so it produces that list or its reverse depending on which junction it reached first. Neither is wrong and the solver does not care, but a golden test comparing a rendered branch table does, and so will write-back. `P3.4a` documents `Path` as "in the order the walk crosses them" and asserts branch *endpoints* rather than their direction. A canonical orientation — lowest-numbered end first, say — is cheap and should be decided before anything downstream depends on the current one. |
| C-28 | [`23`](23-topology-and-graph.md) | **`FS2210`'s message template has no room for the advice the promotion section promises** | The promotion rules say that a duty fixing a branch flow with no valve on that branch should report *"nothing on the branch through RAD1 can change its flow; add a valve"*. The template is `This circuit is over-specified by {n}. Remove one of: {list}.` — there is nowhere for a suggestion to add something, and `{list}` reads as a list of statements to delete. P3.4b emits the template as written and puts the unmatched constraints in `{list}`. Either the template grows a second sentence or the advice needs its own code. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| C-37 | [`27`](27-component-catalog.md), [`24`](24-auto-sizing.md) | **Absolute roughness has no provenance, is in no pipe standard, and the shipped value is for pipe that is new** | `PipeSpec.Roughness` sits beside the diameters and is verified by the same flag, and it is not the same kind of number: 0.045 mm is a textbook figure for *new commercial steel*, appears nowhere in EN 10255 or EN 10220, and no manufacturer's dimension table carries it. `27` already draws exactly this distinction for the plate Nusselt constants — a dimension is a fact about an object, a fitted constant carries an author and a validity range — and roughness belongs on the second side of it without being treated that way. **The magnitude is not small.** A scaled or corroded steel heating pipe is nearer 0.15–0.5 mm; at DN25 and Re 15 450, ε = 0.3 mm moves the friction factor from 0.0305 to about 0.0425, so the pressure gradient and the pump head that follows it rise about 40 %. Nothing in `24` offers a design margin on roughness the way `pump.margin` does on head, so a plant sized on new-pipe roughness has no stated allowance for the condition it will spend its life in. **Resolved by `D-68`.** `MaterialRoughness` carries the value, the material, the condition, a citation and its sources, and needs one source rather than two — the two-source rule catches a transcription error in a number read off an object, and there is no object here. v1 sizes on **new** pipe and says so; a script wanting aged pipe writes `roughness=0.3 mm`. The arithmetic also reframed the problem: the ±50 % published tolerance is worth about 4.5 % on the gradient at DN25, while new-versus-aged is worth about 39 %, so the condition matters far more than which table the value came from. |
| C-35 | [`27`](27-component-catalog.md) | **EN 10255 specifies an outside-diameter *range*, and `27` picks a single value without saying which** | Every public supplier table found lists DN15 at 21.7 mm and DN25 at 34.2 mm; the 21.3 and 33.7 the catalogue ships, and that `27`'s worked example computes from, are EN 10220's Series 1 *preferred* diameters. Threadable tube is specified as a range because the thread has to be cuttable, so both are defensible readings of "the outside diameter of DN25 EN 10255 tube". The gap is **about 2 % in bore, 5 % in flow area and roughly 10 % in pressure gradient** — inside the range that changes a pump selection. **Resolved by `D-67`: the nominal preferred diameter.** The maximum is the pipe the standard permits rather than the pipe anyone makes, and the error runs one way — every circuit sized slightly optimistic with nothing looking wrong. `27`'s 94.1 Pa/m is unchanged, because the worked example was already computed on the nominal value. The rows are now verified. |
| C-24 | [`23`](23-topology-and-graph.md), [`08`](../00-foundation/08-implementation-sequence.md) | **Lowering's step 1 presumes geometry the catalogue owns, and the catalogue is a package later** | "Each `ComponentSymbol` that carries flow becomes an `IComponent` via the registry, with its stated parameters converted to SI" reads as self-contained and is not: a `pipe` needs an inside diameter, a script states `dn`, and turning a designation into a bore is [`27`](27-component-catalog.md)'s — `P3.5`, after `P3.4`. Every other kind converts cleanly. `P3.4a` put an `IBoreLookup` seam in front of it and a six-row test fixture behind that, with the rows carrying real bores rather than the designation, because the designation *is* the trap this project names first. **Closed by `P3.5`:** `CatalogBoreLookup` reads a `PipeSpec`'s derived bore, exactly and never by nearest match, since `dn=27` is a script naming a size that does not exist. The general shape is worth keeping: sizing (`P3.7`) will hit the same wall from the other side, since an unstated `dn` has no bore either until the outer loop has chosen one, and the same seam is what makes lowering re-runnable per outer iteration. |
| C-31 | [`27`](27-component-catalog.md) | **`27` promised DN15–DN300 from a standard that stops at DN150** | The open-questions section committed v1 to `steel_en10255` over DN15–DN300. EN 10255 covers DN6–DN150; anything above it is a different series with its own dimensions and its own two sources per row. The promise was unsatisfiable as written and would have been discovered either by shipping eleven rows and calling the range done, or by inventing DN200 dimensions from the wrong series. `27` now states DN15–DN150 and says what the top end would cost. |
| C-33 | [`27`](27-component-catalog.md) | **`PipeSpec` was specified with arithmetic `Quantity` does not have** | `27` wrote every dimension as `Quantity` and derived the bore as `OutsideDiameter - 2 * WallThickness`, which does not compile: `Quantity` exposes `TryAdd`/`TrySubtract` and no operators, deliberately, because unit arithmetic can fail and a silent operator would hide the failure. The fields are metres as `double`, which is also what `Pipe` takes — it is the only consumer of a bore. |
| C-34 | [`27`](27-component-catalog.md) | **`ICatalog`'s contract and `27`'s own error table disagreed about what happens when nothing fits** | The contract said `SmallestSatisfying` returns "a failure naming the largest available when nothing fits"; the error table says `FS2601` is a *warning* reading "using {max}". The table is right — a design needing more than DN150 still gets a number and a warning, where a refusal blanks the diagram over a circuit that solves. Selection now returns a `CatalogFit` and the sizer builds the diagnostic, because the catalogue does not know which component asked. |
| C-2 | [`22`](22-component-model.md) | Invariants 2, 4, 6 and 7 are asserted; 3 and 5 are not, and one of them is not this tier's | `P3.3` wrote the zero-allocation test, the equation-count agreement, the sign convention and the continuity checks, which is what `08` asked for. Invariant 3 — deterministic and side-effect free — has no test, and it is cheap: evaluate one component's residuals twice from the same context and compare. Invariant 5, residual scaling, is *not* assertable here at all: it is a property of the assembled system against a convergence test, and [`36`](../30-solver/36-numerics-and-convergence.md) is where the scaling happens. Recorded as closed for `P3.3` with invariant 3 moved to `P3.4b`, which assembles a system, and invariant 5 to tier 30, which owns it. |
| C-8 | [`22`](22-component-model.md) | **The tank's per-layer and per-port properties were unregistered**, so `T1.t3` resolved to nothing | The gap was structural rather than three missing rows. `ComponentKindInfo` had `IndexedParameterFamilies` and no property equivalent, so `t1`…`tN` could be *stated* and never read, and `inN_t`/`outN_t` — which have no parameter behind them at all — could not be named in any way. `IndexedPropertyFamilies` is the mirror, deliberately not shared with the parameter one: an element is a `PropertyInfo` on one side and a `ParameterInfo` on the other, and the two carry different things. The lookup went onto `ComponentKindInfo.ResolveProperty` rather than into the binder, because the binder is not the only reader — the model contract reports `T1.t3` too — and the index-pattern rule, which had been the binder's private helper, moved with it to `IndexedName`. `FS1406` now lists `t{index}` among the alternatives, and the generated properties page carries one row per family instead of omitting them. |
| C-22 | [`22`](22-component-model.md) | **`FS2101`'s message shape fits one of the two relations it is specified to cover**, and quotes a value the binder cannot compute | The table gives `{name}: power, in, out and flow cannot all be set — any three fix the fourth. With the other three, flow would be {value}.` Two paragraphs of the same document's acceptance criteria then say "`ua`, `area` and `u` all stated produces `FS2101`", for which that sentence is simply false — it is three parameters and two freedoms, and there is no flow in it. The message is now written over the group: `'{name}': {parameters} cannot all be set. Any {count} of them fix the rest.` The implied value came out with it, and that is the sharper half of the finding: `u = ua/area` is arithmetic, but the implied *flow* is `Q / (cp · dT)`, and a c_p needs a substance. The binder holds a fluid's name and nothing else — the substance behind it is resolved at lowering — so a message naming the fourth value is `P3.4`'s to add, not something `P3.3` chose to skip. |
| C-21 | [`22`](22-component-model.md) | **`FS2105` and `FS2108` name no component** | `Valve position must be between 0 and 1.` and `Efficiency must be between 0 and 1.` are the only two messages in the table with no `{name}`, and a script has more than one valve in it. Both now open with `'{name}': `, which also lets one check site render them: the range and the code that reports it are registry data on the parameter, so `FS2105`, `FS2108`, `FS2114` and `FS2115` are four rows and one method rather than four branches in the binder. |
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
| C-26 | [`23`](23-topology-and-graph.md) | **The counting table gave a flux unknown to a stated `p` but counted a stated `flow` as an equation with no unknown**, over-specifying every terminal that states one | Fixed the other way round from the report's guess. A stated `flow` is not an equation at all: it *names* the flux, so the unknown never appears. `23`'s `X`<sub>f</sub> equation row is gone and its unknown row now reads "one per node that admits a flux and does not state its `flow`". The two readings give the same total on every circuit, and this one is the one that reads correctly — a table with both rows says the circuit had to work to meet a number that was simply given. `HasUnknownFlux` follows the same rule, which is what makes the storage header's fourth mass balance redundant. |
| C-27 | [`23`](23-topology-and-graph.md) | **The pressure datum and the redundant mass balance were described as one mechanism and are two** | `23` now says so, with the two circuits that separate them: a pressure stated mid-branch has a datum and no redundancy, and the storage header — every boundary stating a flow — has a redundancy and an auto-picked datum. The implementation had already separated them in `P3.4b`; the document had not. |
| C-30 | [`23`](23-topology-and-graph.md), [`06`](../00-foundation/06-decision-log.md) | **The counting argument had no enthalpy datum, so every closed circuit that states a temperature was reported over-specified by one** | Every energy relation in the model is a difference — `h_out = h_in + Q̇/ṁ` — so the energy block of a closed, steady, uncoupled circuit is rank-deficient by exactly one and its temperature field is fixed only up to a constant. `23`'s table had N enthalpies against N energy balances, which cancel, so the deficiency was invisible. `D-65` adds the row and `23` explains it beside the pressure datum it mirrors; the difference is that the graph must **not** pick this one, because no temperature it could invent leaves the answer unchanged. Found by `F-15`: adding the sink the simple loop was missing left it still over-specified, naming the one statement that was actually load-bearing. It also gave `S-8` its first script-reachable `FS2211` — a closed circuit that states no temperature anywhere. **The route to it is worth keeping.** `P3.4b` checked the count against `23`'s worked example, which balanced at 20 = 20 on the first run against a hand-tabulated table — on a circuit that cannot exercise this, because the cooling loop is open. It took a user asking for a *different* circuit to be fixed, and the fix not working. |
| C-31 | [`22`](22-component-model.md) | **A boundary and an unfinished stub were the same declaration**, and the count could not tell them apart | `D-64` adds `supply` and `return` as kinds, with `22` gaining the required-parameter policy (`FS2117`) and the group minimum (`FS2118`) that a boundary needs. The registry check that a reserved word can never name a kind had to be relaxed for a kind's own keyword — position disambiguates, and always did — while staying in force for aliases. `12` records the grammar half. |
| C-29 | [`22`](22-component-model.md) | Invariant 3 — residuals are deterministic and side-effect free — had no test | `C-2` moved it to `P3.4b` on the reasoning that it needed an assembled system. It did not: evaluating one component twice at the same iterate and comparing bit for bit is enough, and evaluating every *other* component in between is what catches shared mutable state. Asserted over the components a real lowering produces rather than over hand-built ones, so a kind added to the registry is covered without anyone remembering. Invariant 5, residual scaling, genuinely does need an assembly and stays in tier 30 as `S-3`. |

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

**A parameter's usual range turned out to be its declaration of sign**, and that is what made `FS1307`
implementable without a second table. `dt` is `0.1…200`, so it cannot take a negative; `power` is
`-100…100 kW`, so it can, and its sign is the whole of what makes an exchanger a cooler. No new
registry field was needed. The exemption that *was* needed is absolute temperature: a temperature
parameter is written in °C and held in K, so `t=-50` is 223.15 and nothing is negative in SI at all —
a value that did reach below zero would be below absolute zero, where "t cannot be negative" is the
wrong sentence. That case stays `FS1306`, and there is a test for each half.

**`FS2113` failed an existing test the first time it ran, and the test was wrong.**
`AnIndexedParameterBindsAgainstItsFamily` bound `T1 tank layers=3 t2=60` — one layer of three — through
a helper that asserts a clean bind. It had been passing since `P2.6` because nothing checked profile
completeness, and it is the only script in the suite that states a partial one. Worth recording as the
shape rather than the instance: a new validation's first failure is likelier to be an old fixture that
was quietly illegal than a bug in the validation.

**Ordering the three range checks was not obvious and matters more than it looks.** A `position=1.4`
is outside the hard bound *and* outside the usual range, and `dt=-20` is negative *and* out of range,
so the naive implementation reports two diagnostics for one mistake — an error and then a warning
restating it. `CheckRange` now runs validity, then sign, then plausibility, returning at the first hit.
The same rule made `FS2113` skip a tank whose `layers` is itself invalid: `layers=2.5` already has
`FS2114`, and "your profile does not have 2.5 entries" is the same error counted twice.

**An index above a family's bound resolves to nothing rather than to `FS1516`.** The parameter path
reports that code because the user *assigned* something and the fix is to change the index; a property
reference that names no property is `FS1406`, which lists what is available. The asymmetry is
deliberate and is the kind of thing that reads as an oversight later.

**A family bounded by a parameter has no ceiling the registry can check.** `t{index}` is bounded by
`layers`, which is per component, so `T1.t9` on a five-layer tank resolves at bind time. That is
correct — the registry does not know the tank — but it means the check has to exist somewhere the
layer count is known, and nothing owns it yet. Recorded rather than left to be discovered by a script
reading a layer that is not there.

**The cooling loop matched `23`'s table on the first run, and that had not happened for a structural
pass before.** Six nodes with the right origins, four junction elements including both terminals, four
branches joining the tabulated pairs, one loop. The reason is worth naming: `23` does not merely state
the result, it states the *wrong* answer beside it — "counting only the two degree-≥3 elements gives
`Loops = 4 − 2 + 1 = 3`, which is wrong" — so the terminal rule was written from a worked
counter-example rather than from a definition. That is the same pattern `P3.3` recorded from the other
direction: the sections of `22` that stop to explain why the obvious implementation is wrong produced
no defects.

**"Not exactly two ports in a flow group" is one rule where the document reads as two.** `23` says a
junction element is "a terminal, or a component with at least one flow group containing three or more
ports". Implemented literally that is two tests joined by an `or`, and the terminal half needs a degree
the component does not know. Written as *any group whose size is not two* it is one test on data the
component already declares: a group of one is a terminal and a group of three is a split, and a node
gets both cases for free because its group is simply all of its ports.

**A node's port count is not in the semantic model.** A node's ports are unnamed and positional, so
nothing but the connection list says how many it has — and the count decides both the port count and
whether the node carries a mass balance. Lowering therefore counts degrees before it constructs any
node, which reverses `23`'s step order for that one kind. Flow components still instantiate first, as
step 1 says.

**Expansion had to be a rewrite-then-prune rather than a substitution.** `nodes=n` replaces one pipe
with 2n+1 elements, and the natural implementation removes the pipe as it goes — which invalidates
every element index the link list holds, silently, since the indices stay in range. The pipe is left
in place while its links are rewired and dropped afterwards in one pass that remaps every index.

**`23`'s worked example reproduced on the first run, term for term.** Six pressure relations, four
mass balances, two external fluxes, two constraints and two promotions — including *which* parameter
each constraint promotes, which the document derives from physics and the implementation derives from
a candidate list. That is the strongest evidence available that the counting scheme in the document
and the one in the code are the same scheme, and it is worth saying because three of the other five
reference circuits needed the count corrected before they balanced.

**Junction-ness and connectedness are different questions, and one function cannot answer both.**
`IsJunctionElement` reads the component's declared flow groups: a duty exchanger has two groups of two
and is never a vertex, whether or not its second side is wired. The *pressure relation* count reads
what is actually connected: that same exchanger contributes one relation with one side wired and two
with both. Using declared groups for the second gives the cooling loop seven relations instead of six;
using connected ports for the first makes every duty exchanger a junction element and destroys the
branch decomposition.

**A ring of pass-throughs has no vertex, and P3.4a produced no branches for it at all.** Every node on
`N1 - PU1 - N2 - HE1 - N3 - CV1 - N4 - P1 - N1` has degree two, so nothing is a junction element, so
the branch graph has no vertices and therefore no edges — and the loop the user wrote vanished
silently, with no flow unknown and nothing for `FS2214` to name. It is cut at its lowest-indexed node.
Where the cut falls changes no unknown and no equation, so it only has to be deterministic. The cut has
to be found **per flow group** rather than per component: seeding every port of a coupled exchanger
enters both its sides at once and merges the two circuits it separates.

**A catalogue row's two failure modes need two different checks, and neither substitutes.** A wall
thickness transcribed as 32 rather than 3.2 leaves no bore and is caught by arithmetic. One
transcribed as 3.6 rather than 3.2 gives a perfectly plausible bore, is caught by nothing except a
second source, and moves the pump head by several percent with every intermediate number looking
reasonable. `27` states both rules; what is worth adding is that they are not redundant and the cheap
one cannot stand in for the expensive one.

**The ordering invariant is load-bearing, not tidiness.** `SmallestSatisfying` returns the first row
matching the predicate and calls it the smallest. On a table that stops ascending it does not fail —
it quietly answers with the wrong pipe. That is why invariant 7's monotonicity check is enforced in
`Validate` rather than left to review.

**A single manufacturer's table was not merely thin — it was wrong, and plausibly so.** Sourcing the
pipe rows turned up a manufacturer chart carrying the correct outside diameters against DN labels
shifted one size, having dropped DN8 from the head of the series. Read alone it would have given DN25
a 21.7 mm bore instead of 27.3 — a 37 % error in flow area — and every downstream number would have
looked reasonable: a velocity, a Reynolds number, a friction factor and a pump head, all self-
consistent and all wrong. `27` argues the two-source rule from the risk of a typo. The real case is
worse than a typo and the rule caught it on the first attempt.

**Two sources agreed on the wall thicknesses and two on the diameters, and no single source was right
about both.** Worth recording because it is the shape the rule is usually justified against and
rarely demonstrated in: the second source is not a confirmation of the first, it covers a different
half.

**A public copy of the standard itself was found and deliberately not opened.** A fittings vendor
hosts BS EN 10255:2004 as a PDF. It is a copyrighted document wherever it sits, and the project's rule
is that dimensions come from manufacturers' own published data with the standard cited by number.
That is the policy costing something rather than being free, which is the only time it matters.

**A mass per metre is a third source, and it is the one to reach for when two disagree.** Diameter and
wall were each supported by two sources, and DN150 still had two candidate diameters with a merchant's
page stating both for one product. The published 19.7 kg/m settled it in one line of arithmetic,
because a mass constrains the diameter and the wall *together* rather than restating either. `27`'s
sourcing table lists what to read; it is worth adding that a published mass is a cross-check and not
merely another row to transcribe.

**Sourcing difficulty is not uniform across materials, and the reason is structural.** Steel took four
sources and one arithmetic tiebreak. Copper defeated eight attempts, because EN 1057 permits several
walls per outside diameter and the market ships more than one — so there is no single "EN 1057 22 mm"
row to find two sources for, and the tables that carry a whole series are copies of the standard.
Worth recording before the next catalogue is scheduled: the cost of a series is set by how many
degrees of freedom its standard leaves open, not by how common the material is.

**A `dn` value does not mean the same thing in two catalogues, and nothing in a script says which.**
Steel's `dn=15` is a designation whose bore is *larger* than the number, 16.1 mm; copper's `dn=15` is
the outside diameter itself, whose bore is *smaller*, 13.6 mm. Same script text, 24 % in bore, roughly
a factor of two in pressure gradient, and the only thing that distinguishes them is which catalogue
resolved. `PipeSpec.DesignationBasis` now states it per series, which makes it inspectable but does
not make a script self-explanatory — a reader still cannot tell what `dn=15` means without knowing the
`catalog` line. That is worth a `/docs` sentence at minimum and possibly a diagnostic.
