---
id: 20-core-domain-defects
title: What implementing against the core domain found
tier: 20-core-domain
owns: [defect and observation record for documents 21-27]
---

# What implementing against the core domain found

Defects, deferrals and observations from implementing against `21`–`27`. The rule and its reasoning
are in [`08-implementation-sequence`](../08-implementation-sequence.md).

`22` and `23` have been implemented against in depth, and `27` as of `P3.5`. `24`, `25` and `26` are
still unread at implementation depth, so their absence from this file means nothing has looked, not
that nothing is wrong.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| C-79 | [`25`](25-layout-hints.md), [`23`](23-topology-and-graph.md), [`53`](../50-frontend/53-canvas-renderer.md) | **`Loops` hands the renderer the solver's cycle basis, and on a header that is not the set of rectangles anyone would draw** | `25` says a loop is drawn as a rectangle and `Rank` excludes its members. The cycle basis `Lowering` builds is a spanning-forest basis: correct for the solver, which needs any set of independent cycles, but on `m2-distribution-header` it holds four loops of which two run through *both* consumers' mixing legs and the header (`[N3, N4, PR1, …, PA1]`, 22 members), so every component on the sample is a loop member, `Rank` is empty, and a renderer drawing one rectangle per entry would draw the AHU coil three times. What a diagram wants is the *minimum* cycle basis — the two recirculation loops and the two header rings — or, more honestly, no `Loops` on an attached branch at all, because `DistributionGroups` and `BranchShapes` already say how to draw it. P5.1a shipped the basis as is, with each walk rotated to start where `Order` starts so it is at least stable; P5.3 decides whether Core computes a minimum basis (Horton's algorithm is cheap at this size) or the renderer ignores `Loops` inside a distribution group. Filed 2026-09-15. |
| C-78 | [`24`](24-auto-sizing.md), [`22`](22-component-model.md), [`27`](27-component-catalog.md), `D-99` | **The plate exchanger's metal is only half specified: no sourced `U`, no plate step, no `lamella` correlation, and `24`'s plate-step criterion contradicts `01`'s plate count** | Found closing `P4.1`. The thermal size is solid --- `ua` from the design point by both routes, `area` from a stated `u`, `plates` from a stated `plate_area`, each pinned by `ThermalSizerTests` --- and everything past it leans on a catalogue that does not exist. `24` carried `hx.u_default = 3000 W/(m²·K)` with no source and `D-99` withdrew it; `lamella` is read and unused because the chevron correlation that turns geometry into `u` and channel `dp` needs plate dimensions to apply to; and `hx.plate_step = 2` would round the substation to 40 plates where `01` and `22` both say 39, so the rule rounds to the next whole plate and the step criterion is left unticked. In the same family: `24`'s `FS2311` (an incomplete Rated profile) has no descriptor, and the sizer reports the case in a note instead. All of it resolves the same way --- `27`'s plate catalogue, with a cited coefficient and a manufacturer's plate step --- and none of it blocks a script that states `u` or `plate_area` itself. |
| C-74 | [`24`](24-auto-sizing.md), [`16`](../10-language/16-diagnostics.md) | **`24` specifies thirteen sizing diagnostics, `FS2301`–`FS2313`, and none is registered; sizing speaks only through notes** | `24`'s table gives each rule a code: the loop not settling (`FS2301`), conflicting stated flows (`FS2302`), a stated head the loop cannot meet (`FS2303`), nothing to size against (`FS2304`), a size outside the catalogue (`FS2305`), a plausibility bound (`FS2306`), a velocity step-up (`FS2307`), a parallel branch with nothing to balance (`FS2308`), the index branch (`FS2309`), the discrete exchanger overshoot (`FS2310`), and the pump with no modelled resistance (`FS2312`). `DiagnosticArea` reserves the range and `CodeRangeOwnershipTests` would hold it to `24`, but no `FS23xx` code exists in `Diagnostics/`: every one of those conditions that the sizers do detect (`C-57`'s zero-resistance pump, `C-66`'s valve on a bare branch, the loop cap in `OuterLoop`) is reported as a free-text entry in `OuterLoopResult.Notes` --- "what sizing wanted to say" --- which the API does not carry as a diagnostic, the editor cannot anchor to a component, and no test can name as a code. M2a's `minor_loss` criterion asked for `FS2312` by number and got a note. | Open. Not a physics defect, so not an `M2a` blocker, but it is `16`'s contract broken for a whole range. Register the codes the sizers already detect (`FS2301`, `FS2304`, `FS2305`, `FS2308`, `FS2310`, `FS2312`) with a test naming each as a literal, route `OuterLoop`'s notes through them with a `ComponentName` anchor, and leave the rest until the rule that would raise them exists. `P4`'s first package is the natural place, since the model contract (`P5.1`) is what carries diagnostics to the editor. |
| C-73 | [`24`](24-auto-sizing.md), `D-58`, `D-94` | **A DHW circuit's design condition is a draw profile, and nothing reads one** | The half of `C-51` that `D-94` did not answer. `design tout=-26` and `sized_at tout=-5` both position a *curve of an outdoor temperature*, and every rule sizes against the value that gives. A domestic hot-water substation is not sized at any outdoor temperature: its design condition is a draw profile --- a peak draw over a duration, or a tapping cycle such as EN 13203-2's, against a store and a coil --- and the language has the words for none of it. `design draw=...` is already grammatical under `D-59`'s permissive registry and binds to nothing. | Open, deferred to P4's substation package, which is where a DHW circuit is first modelled well enough to have a design condition to read. Needs the draw-profile shape decided with two public sources (EN 13203-2's tapping cycles, DHW peak-demand tables of the kind ASHRAE and CIBSE Guide G publish), a registry role for it, and a rule in `24` that sizes a store or a coil from it. Not an `M2a` blocker: no M2a script has a DHW circuit. |
| C-72 | [`22`](22-component-model.md), [`15`](../10-language/15-semantic-model.md), [`24`](24-auto-sizing.md) | **Nothing in the language distinguishes a modelling fiction from a physical load, and the two size differently** | What survives `C-59` once its arithmetic complaint is answered. A closed circuit's duties must sum to zero, so a script that states a source states a sink --- and that sink is often a *device*, written only to make the equations solvable. A fiction should not resist flow; a physical load absolutely should, and `heat_exchanger` has a `dp` default that applies to both. Measured on `24`'s own worked loop: with every exchanger carrying the decided 20 kPa the ring holds **40 kPa** of exchanger drop, the valve is sized against twice the branch, and the head reaches **11.93 m** against the document's **5.28**. Neither number is wrong on its own --- which is the problem. The workaround is `dp=0`, and it works: `24`'s example states it and reproduces the document exactly. | Open. It is a **language** gap rather than a sizing one, and the cost of leaving it is that the default is wrong for whichever of the two kinds the user meant, silently, in a quantity (pump head) that more than doubles. The candidates are a distinct kind for an ideal block, a `dp=0` default that a physical load must override, or a required `dp` on any exchanger in a closed ring. All three change what an existing script means, so this needs a decision rather than an edit. The registry's rationale string ("write `dp=0` for an ideal block") and `24`'s worked example are the only two places that say any of it, and neither is somewhere a user looks before writing their first load. |
| C-71 | [`22`](22-component-model.md), [`36`](../30-solver/36-numerics-and-convergence.md) | **The cooling loop converges on a 2 % leak that is an artefact of the characteristic, not a modelled seat — and that artefact is the only thing keeping a shut bypass solvable** | Found by trying to ship `C-69`'s characteristic half and measuring why it failed. `m2-cooling-loop` converges with **`3WV.position` = 1**: the valve is fully open to its control leg and the bypass is passing the secondary's recirculation through equal percentage's floor, **φ(0) = R⁻¹ = 0.02**. `ValveLaw.Opening` documents that floor as deliberate and notes it "matters for a bypass that is supposed to be shut (`C-18`)" — what was not noticed is that a shipped sample is *relying* on it. Substituting the linear bypass manufacturers actually build gives **φ_b(1) = 0** exactly; `ValveLaw.MassFlow`'s coefficient is proportional to the effective Kv, so it returns 0 at every Δp, the residual `−ṁ_b − 0 = 0` pins the recirculation flow at zero, and a constant-flow secondary that still needs it drives the pressures out of the property domain. Measured: **9 corpus tests fail and `m2-cooling-loop` goes `NonFinite`**. Naming the valve's ports so the letters match the physical roles makes it **11**, which is what rules out labelling as the cause and leaves the pairing itself. Implemented, measured, reverted. **The physical gap is a leakage class.** A real valve's shut-off comes from its seat and is a rated leakage — Belimo's characterised valves are tight-shutoff — so the honest model of a closed leg is `φ = max(φ(x), leakage)` with the leakage a property of the *body*, not of the characteristic. `ValveLaw.Opening`'s remarks record that a floor was "tried and rejected, because it changes φ(0) on a characteristic whose φ(0) = 0 is deliberate and asserted" — that reasoning is right about the *characteristic* and does not settle the *valve*, which is a different object with a catalogue property the characteristic does not carry. | Open, and it **blocks `C-69`'s characteristic half**: linear on the bypass cannot ship until a closed leg has somewhere to pass its rated leakage. It also puts a question over an answer already shipped — the cooling loop's `position` = 1 is the solver saying *shut the bypass*, and whether the plant it describes actually works is decided by a leakage number the model does not have. Needs a `leakage` property on the valve catalogue with two public sources, a decision on the default (`C-18` is the entry that first asked), and a re-measurement of `m2-cooling-loop` against it. Not an `M2a` blocker: today's behaviour is unchanged and the corpus is green; what is wrong is that the reason it is green is not the reason the report gives. |
| C-69 | [`22`](22-component-model.md), [`24`](24-auto-sizing.md) | **A three-way valve has one `kv` and one `characteristic` where the hardware has two of each, and the pair we collapse them onto is the pair that matters** | `ThreeWayValve.EvaluateResiduals` gives the control path `Kv · φ(position)` and the bypass `Kv · φ(1 − position)` — one coefficient, one characteristic, complementary openings. Belimo's project-planning notes for 2-way and 3-way characterised control valves state the arrangement they actually build: the bypass **B–AB is 70 % of the Kvs of the control path A–AB**, A–AB is **equal percentage** and B–AB is **linear**. Danfoss's 3-way datasheets carry the same split. **The characteristic half is not cosmetic.** With equal percentage on both legs our combined coefficient is `50^(x−1) + 50^(−x)`: **1.02 at either end of travel and 0.283 at mid-travel**, a **3.6× collapse** in the capacity of the common port. A three-port valve exists to hold the primary flow roughly constant, and ours throttles the primary hardest exactly where it should be doing nothing to it. Belimo's pairing gives 1.02 / 0.641 / 1.00 over the same travel — about 1.6× rather than 3.6×. Every mid-position solve on a mixing circuit is running against this. **The 70 % is a different animal and should not be copied as physics.** It matches the two *paths'* resistance, not the valve's own capacity, since the bypass skips the coil — it is a manufacturer's built-in partial substitute for the bypass balancing valve `docs/functions/three-way-valve.md` already tells the user to add, and the same page already admits one coefficient cannot do that job because the two legs carry different flows. | Open. Three separable pieces, in increasing cost. (1) **Per-leg characteristic**, defaulting to equal-percentage on `a` and linear on `b`: corrects real physics, needs a `D-` entry and re-baselines every mixing solve in the corpus. **Tried, measured and reverted — it is blocked on `C-71`**, because linear reaches φ(0) = 0 exactly and a shipped sample is relying on equal percentage's 0.02 floor to keep a shut bypass solvable. (2) **A statable `kv_b`**, so a user can model the bypass balancing valve properly; costs a registry parameter, printer round-trip and a second coefficient through sizing. (3) **Defaulting `kv_b` to 0.70·`kv`** — rejected as written, because it bakes one vendor's product decision into the equations for every valve, globe valves included. Two further details from the same source are recorded and **not** acted on: A–AB is equal percentage with a curve factor n(gl) = 3.2 that runs **linear over the lower 0–30 % of travel**, where our `Rangeability = 50` pure exponential does not; and on Belimo's R-series **ball** bodies the bypass is already shut at 70° of the 90° stroke, so `1 − position` overstates how long it stays open. Both are body-specific rather than universal. Not an `M2a` blocker: the valve solves and sizes, and the error is in how faithfully mid-travel behaves. |
| C-68 | [`21`](21-fluid-and-state.md), [`07`](../00-foundation/07-quality-attributes.md) | **The finite-difference Jacobian spends 99.98 % of a Newton step in the property backend, and the per-solve cache `21` already requires does not exist** | Measured, `diagnostics/pipeline-timings.md`. One `EvaluateResiduals` against constant properties costs **0.003--0.024 ms**; against real water it costs **1.4--7.6 ms**, 300--400x more. A finite-difference Jacobian needs `N+1` of them, so one iteration on `m2-distribution-header` (N=45) is **233 ms of Jacobian against 0.054 ms of dense LU**. The linear algebra is not the cost and never was: LU stays under 0.15 ms even at N=65. Whole-solve figures follow from it --- `m2-cooling-loop` converges in one iteration and three sizing passes, 1.8 ms on constant properties and **122 ms** on water. `fluid-state-timings.md` gives the unit: one `Water` (p, T) state is **2.23 ms**, so a residual is a handful of state fixes and a Jacobian column is one more. | Open. `21` states a per-solve property cache as a requirement and nothing implements one, which is why this is a defect rather than an observation. The structure is favourable: across a Jacobian's `N+1` evaluations only one unknown moves at a time, so almost every state fixed is the one fixed in the base evaluation --- a cache keyed on the (p, h) pair would hit on nearly every column. **The alternative is an analytic Jacobian**, which is a much larger change and would still leave the base residual on the backend. Cache first, and measure again before considering it. Not an `M2a` blocker --- nothing in `05` budgets a steady solve, and `07`'s interactive gates are about *compile*, which is measured at 0.16 ms for ten declarations and 1.0 ms for forty against a 150 ms budget. It becomes one the moment a transient run wants 10 frames/s. |
| C-65 | [`22`](22-component-model.md) | **A three-way valve body is built for mixing or for diverting, and the model treats that as an outcome rather than a fact about the equipment** | The ports are bidirectional and which arrangement a valve is gets read from the topology, which is right for the *solve*: `m_ab = m_a + m_b` with signed flows covers both, and a negative solved flow is a legal answer. It is wrong about the *specification*. Manufacturer guidance is explicit that "a mixing valve must not be used for diverting service, or vice versa" --- the plug geometry differs and the wrong one defeats the fail-safe position --- so the arrangement is a purchase decision the script is making silently. A user who writes a mixing arrangement and buys a diverting valve gets a correct model of a plant they cannot build. | Open. The fix is a declaration the script can make and the topology can be checked against, and the `mixing_valve` / `diverting_valve` spellings already exist --- they are **aliases today**, resolving to the same component with no effect, which is the cheapest possible place to put the meaning. What it costs is a new diagnostic and a decision about the default: a bare `three_way_valve` must stay unconstrained, or every existing script acquires a claim it did not make. **Not to be confused with the reverted attempt** (`22`): that typed *every* three-way valve inlet/outlet/outlet, hard-coding diverting, and broke mixing circuits. Making the role follow the *declared kind* is a different design and that failure does not condemn it. Not an `M2a` blocker --- `D-85` renamed the ports, which was the half that actively misled. |
| C-64 | [`24`](24-auto-sizing.md) | **A three-way valve's sized drop is not the drop it runs at, and on this topology the two differ by 28x** | Measured on `m2-cooling-loop` after `C-63` closed: the rule sizes `3WV` for a **2.18 kPa** drop and the converged solve puts **61.2 kPa** across it. Both numbers are right and the gap is structural. The rule measures the variable leg's own resistance --- `P1`'s 1.10 kPa --- which does not depend on the pump at all. The operating drop is set somewhere the rule never looks: the valve's two legs share one coefficient, so the position satisfying the controlled leg also fixes the **bypass** leg, and the bypass leg is what sets the head `PU1` must develop to drive the constant-flow secondary. The reported authority moves the same way --- **0.66** designed against roughly **0.98** achieved. | Open, and **not** blocking: the direction is right, so the answer is usable. Measured across the series with `kv` stated, head falls monotonically as the valve opens --- 33.6 m at Kv 1.6, 6.4 at Kv 4, 3.4 at Kv 6.3, 2.4 at Kv 10 --- so sizing generously against the leg lands on a plant that works. What it costs is that one R5 step is a **factor of about two in pump head**, which is a larger consequence than a valve selection usually carries, and the rule has no way to see it. Closing this means either sizing the valve against the head its bypass leg implies --- a fixed point the outer loop could close, since it already re-sizes from solved flows --- or reporting the achieved drop beside the design one so the user can see the difference. The first changes a rule and needs a source; the second is reporting and does not. Neither is `M2a`. |
| C-54 | [`24`](24-auto-sizing.md) | **The pipe rule's velocity ceiling never fires at the default gradient target, so the step it describes is untested by any real case** | `24` step 3 reads as a check that acts -- "if exceeded, step up one nominal size and re-check" -- and on EN 10255 at 150 Pa/m it never does. The reason is dimensional: at fixed flow the gradient falls roughly as D^-5 and the velocity as D^-2, so a size meeting a gradient target is already far under its noise limit. The worked example is the illustration -- DN25 at 0.411 m/s against a 1.0 m/s ceiling, a factor of 2.4 of margin -- and a sweep from 0.05 to 12 kg/s never triggers a step-up at that target. It becomes reachable only when a script states a much looser target, or at the very top of the series where DN150 meets 150 Pa/m at about 1.65 m/s against a 1.5 m/s limit, and there the step cannot be taken because nothing is larger. `PipeSizer` implements the rule as written and `TheVelocityCeilingIsHardAndSaysWhenItMoved` exercises it at a stated target rather than pretending the default reaches it. Left open because it is a question for `24` rather than a defect in the code: a bound that never binds is either a wrong bound, a wrong target, or documentation that should say it is a guard. **Found the same way `C-48` was** -- by running the rule rather than reading it. Its companion is now fixed: clamping at the top of the series reported the gradient miss and said nothing about the velocity ceiling it also breached. |
| C-50 | [`15`](../10-language/15-semantic-model.md), [`24`](24-auto-sizing.md) | **`radiator` aliases to `heat_exchanger`, so an emitter's surface requirement is unmodellable and its absence is silent** | `15`'s alias table sends `radiator`, `load` and `boiler` to `heat_exchanger`, whose Duty mode has one hydraulic side and a `dp` default. So `RAD1 radiator power=20k in=45 out=35` sizes exactly as happily as the same duty at `in=70 out=40` -- bigger pipe, bigger pump, no complaint -- while the physical radiator needed is **2.00x** the surface. EN 442 nominal is 75/65/20 (LMTD 49.83 K) with exponent 1.30; at 70/40 into a 21 C room the LMTD is 31.67 K and the output factor `(31.67/49.83)^1.30` = 0.555, so 20 kW needs **36.1 kW nominal**; at 45/35 the LMTD is 18.55 K, the factor 0.277, and the requirement **72.3 kW nominal**. The design that will not fit on the wall is indistinguishable from the one that will. Needs an emitter kind whose second side is a room rather than a stream -- a Rated exchanger with `Cr = 0`, which is the same degenerate branch `D-80`'s condensing and boiling zones use. |
| C-49 | [`24`](24-auto-sizing.md) | **Rounding Kv to a catalogue destroys the proportional balancing the same document requires** | Invariant 5 says every sized Kv is a catalogue value and the valve rule rounds **down**; the parallel-branch acceptance criterion says every branch carries its design flow after the solve. Run both against `24`'s own worked two-branch example on an R5 valve series: required Kv 8.39 and 5.99 round to 6.3 and **4.0**, giving valve drops of 23.6 and **30.0 kPa** against branch totals of 43.6 and 50.0 kPa. Branch 2 now carries `sqrt(33.3/50.0)` = **18 % less than design flow**, and the two branches no longer agree on which is the index. The R5 step is 1.6x in Kv and 2.56x in dp, so no tolerance swallows it. Real balancing valves are presettable in ~0.1-turn increments over roughly 10-100 % of Kvs, which is exactly why balancing works in practice: the **body** is discrete and the **setting** is continuous. Invariant 5 has to split by valve role -- and `15`'s alias table already carries `balancing_valve`, so the role reached the binder and was discarded. Authority is a control-valve criterion; a balancing valve is sized for a measurable dp at design flow, typically 3-10 kPa. **Still open after `P3.7b`'s valve rule**, which deliberately scoped itself to a single control valve: `ValveSizer` rounds down to R5 and reports the achieved authority, which is correct for a control valve and is the wrong job for a balancing one. Two things have to land before the rest can: the role split, so `balancing_valve` stops being an alias the binder discards, and a home for set balancing -- which is **not** a rule, because `ISizer` cannot see a sibling branch and `24`'s procedure needs every one of them to find the index. The decision taken is that it becomes a pass in `OuterLoop` above the rules, leaving `SizingContext` narrow. |
| C-47 | [`24`](24-auto-sizing.md), [`22`](22-component-model.md) | **`pump.efficiency = 0.7` is being asked to be two different quantities and is wrong as both, and `Pump` writes no energy term at all** | Settled by `D-82`. For the energy *balance* you need the fraction of work that heats the fluid, `(1 - eta_hydraulic)`, plus every watt of motor loss **if the pump is wet-rotor** and none of it if dry-rotor. For the energy *cost* you need wire-to-water `eta_hyd x eta_motor`, which for a small circulator is 0.3-0.5, not 0.7. Independently, `Pump.EvaluateResiduals` writes only a pressure relation: the simple loop's 51.7 kPa at eta 0.7 dissipates 22.2 J/kg and adds 17.7 W to a 30 kW circuit -- 0.06 %, negligible in size and systematic in sign, on an architecture whose claim is that every energy balance is exact. `D-69`'s flux member is its home, the same slot `S-18` named for ambient loss. Open until the catalogue splits and the term lands. |
| C-44 | [`24`](24-auto-sizing.md) | **Step 2's propagation cannot run on magnitudes, and `24` writes it as though it can** | The pipeline says "push stated constraints along branches" to a fixed point, with `FS2302` on a conflict. A branch already carries one flow along its whole length, and a junction element is degree one or degree three and up — never two (`23`) — so there is no series step to take: the only propagation left is *across* a junction, and that is a mass balance, which needs signed flows. Signs are not available before the field is oriented, and orienting it is the seed's job, which runs after. So the exact rule is either a small linear solve over the branch graph or a second pass after the seed, and `24` says which of those it is nowhere. `P3.7a` shipped the weaker rule the seed actually needs — spread the largest determined estimate across a junction's other branches — and left `FS2302`/`FS2304` unregistered, because a wrong *diagnostic* is a sentence a user acts on while a wrong *estimate* only costs Newton iterations. The exact propagation is due with the sizers that report those codes. |
| C-23 | [`22`](22-component-model.md) | **Eight of the sixteen `FS21xx` codes still cannot fire**, and each is blocked on a different stage | `P3.3` raises the ones decided by counting and comparing stated values: `FS2101`, `FS2103`, `FS2105`, `FS2108`, `FS2113`, `FS2114`, `FS2115`. The rest need something the binder does not have. `FS2102` (under-determined after sizing) needs the sizing loop, `P3.7`. `FS2104` (`head` against `dp`), `FS2111` (a duty beyond what the inlets allow) and `FS2116` (a tank substance outside the model) each need a resolved substance, which is `P3.4`'s. `FS2106` (discretization above the cap) must be raised by whatever actually clamps, and nothing subdivides a pipe until `P3.4`; raising it now would report a substitution that had not happened. `FS2109`, `FS2110` and `FS2112` are Rated and Coupled mode, `P4.1`. Listed rather than left implicit because a code with a descriptor and no emit site is invisible: the registry's own coverage test only checks that a *raised* code has a page. **Re-measured after `P3.7`: still exactly eight** --- `FS2102`, `FS2104`, `FS2106`, `FS2109`, `FS2110`, `FS2111`, `FS2112`, `FS2116` have a descriptor and no emit site. `FS2102`'s stated blocker is discharged, though: the sizing loop exists, so "under-determined after sizing" is now a gap rather than a sequencing note, and it is the one to raise first. **Re-measured after `P4.1` (2026-09-15): four.** `FS2109`, `FS2110` and `FS2112` fire from `BindingRun.ReviewExchangerModes`, `FS2111` from `ThermalSizer` (carried to the solve's diagnostics by `OuterLoop.Apply`), and `FS4008` --- allocated in M1 --- is live in the new `DesignDiagnostics` area. `FS2102`, `FS2104`, `FS2106`, `FS2116` remain; `DiagnosticCoverageTests` now names every one of the new codes. |
| C-38 | [`27`](27-component-catalog.md) | **EN 1057 permits several walls per outside diameter, and the shipped copper rows pick one without a source** | Copper was materially harder to source than steel and the reason is structural rather than bad luck: EN 1057 defines Y, X and Z wall series, more than one is on the market, and a 15 mm tube is 15 × 0.7 in the UK Table X range and 15 × 1.0 in several continental ranges — 13.6 mm of bore against 13.0, about 9 % in flow area. Six searches and eight fetches produced no two independent public tables covering a whole series: the ones that do are copies of the standard, which this project does not use. The rows ship as Table X, unverified, and the loader refuses them. Whoever closes it must also decide **which series a Finnish HVAC default should be**, which is a market question rather than a sourcing one. |
| C-39 | [`27`](27-component-catalog.md) | **`CatalogBoreLookup` reads an unverified catalogue without asking, so `FS2605` is bypassable** | `Validate` and `Resolve` enforce provenance, and `FS2605` says an unverified row must never reach a user — but `new CatalogBoreLookup(CopperEn1057.Instance)` returns bores from rows nobody has checked, silently. The migration in `C-32` routes the topology tests through `Resolve` so the enforced path is the one under test, and that is a convention rather than a constraint: nothing stops the next caller constructing the lookup directly. Not closed here because the obvious fix — restricting the constructor to a resolved catalogue — would also break the test that asserts what `dn=15` means in each series, which legitimately reads an unverified copper row for its designation rather than to size anything. The distinction between *reading* a catalogue and *sizing against* one is real and this document does not draw it. |
| C-36 | [`27`](27-component-catalog.md) | **EN 10220 and EN 10255 give DN150 different diameters, and nothing in the plan says which series a circuit is drawn in** | Two independent sources give EN 10220's Series 1 as … 114.3, 139.7, **168.3**, 219.1 …, with no 165.1 in it; EN 10255's threadable 6″ tube is **165.1**. So DN150 is a different pipe depending on which standard the script means, and `27` names `pipes-steel-en10255` and `pipes-steel-en10220` as separate catalogues without saying that they disagree at a size both contain. **The DN150 half is settled**: `D-67` takes 165.1 for `steel_en10255`, decided by the published 19.7 kg/m, which 165.1/5.0 reproduces as 19.74 and 168.3/5.0 does not. What stays open is that `27` names both catalogues without saying they disagree, and that no script can yet say which series it means. This is also the likeliest explanation for `C-32`: `ReferenceBores` is labelled EN 10220 and the shipped table is EN 10255, so their DN32 and DN40 bores were never meant to match — which nothing in either file says. |
| C-28 | [`23`](23-topology-and-graph.md) | **`FS2210`'s message template has no room for the advice the promotion section promises** | The promotion rules say that a duty fixing a branch flow with no valve on that branch should report *"nothing on the branch through RAD1 can change its flow; add a valve"*. The template is `This circuit is over-specified by {n}. Remove one of: {list}.` — there is nowhere for a suggestion to add something, and `{list}` reads as a list of statements to delete. P3.4b emits the template as written and puts the unmatched constraints in `{list}`. Either the template grows a second sentence or the advice needs its own code. |


## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| C-67 | [`22`](22-component-model.md), `D-91` | **A neutral heat exchanger can state `in=50 out=30 power=+24` --- fluid cooling, duty claiming heating --- and nothing says so** | `power` is positive when side 1 gains heat. `in > out` means the fluid leaves colder than it arrived, so its explicitly signed duty must be negative; the two statements contradict and the component's own energy balance cannot satisfy both. `m2-distribution-header` carried the shape on both consumers and the contradiction surfaced only as a convergence residual; `D-91` gave the role words their sign so the ordinary spelling could not write it, and left the neutral spellings unchecked. | **Closed 2026-09-15 with `FS2119`.** `BindingRun.ReviewDutyDirection` compares `sign(power)` with `sign(out − in)` on side 1 and `sign(in2 − out2)` on side 2 whenever all three are stated on a neutral spelling; role words are skipped because the word decided the direction and the magnitude has nothing to contradict; a stated `dt` is a magnitude and does not participate. The `m1-*` audit this entry asked for found four contradictory lines in `m1-syntax-tour` (`HE2`–`HE5`, all `in=50 out=30` with a positive duty) and seventeen more in the binder's own test fixtures --- every one a cooling load written as a positive neutral duty, the exact shape the header sample had --- now all `load`. `ComponentDiagnosticsTests.FS2119_*` (six). |
| C-4 | [`22`](22-component-model.md) | `heat_exchanger` mode selection is unimplemented | `HeatExchanger.Mode` returned `Duty` unconditionally, so the property reported `duty` for a Rated or Coupled exchanger and the enum's other two members had no producer; `WellPosedness.IsCoupled` decided coupling separately for counting, so the two could disagree without anything noticing. | **Closed by `P4.1`, 2026-09-15.** `HeatExchanger.ResolvedMode` is `Coupled` when both secondary ports are wired, else the rating's mode (`Rated` when the factory built one from a second-side profile), else `Duty`; `Mode` renders it. `IsCoupled` still reads the partition, and cannot disagree: both derive from the same wiring the factory saw. `ExchangerModeTests` pins all three. Noticed closed in the pre-`P5.1` audit rather than at `P4.1`'s close. |
| C-77 | [`21`](21-fluid-and-state.md), [`23`](23-topology-and-graph.md), [`24`](24-auto-sizing.md), [`27`](27-component-catalog.md) | **What the post-`C-76` review of Core found in this tier: one walk with no bound, one sizer with no guard its sibling has, one sizer with no test, and three places the code's own documentation told the reader the opposite of what the code does** | The 2026-09-14 review (`.claude/dotnet-toolkit/review/2026-09-14-202157-Core.md`, four instances over disjoint folders) was asked for the `C-76` class --- what no test sees and no diagnostic reports --- and found no second `C-76`: the residual paths allocate nothing as documented, `SolveContext` is a `readonly ref struct` so nothing can hold a solver buffer across iterates, the backend is the only native state, and its thread-static fix is sound. What it did find, none of it a wrong number today: **`PortMap.Trace`** replayed a branch with `while (true)` and no step cap, the only walk in Solvers without one --- correct while lowering's adjacency is an involution, and an infinite loop on a request worker the day it is not, which is worse than a throw. **`PipeSizer.Provisional`** read `catalog.Entries[0]` where `ValveSizer` guards the same read; an empty catalogue is refused earlier, so unreachable, but two sizers with two rules for one read is how the next refactor breaks one. **`ExchangerSizer`** was the only sizer with no direct test, and the one whose remarks cite `C-59` in exactly its arithmetic. **`ReportCompetingDatums`** flood-filled the whole graph once per pair of stated pressures --- O(S²·N²) --- beside a union-find in `HydraulicComponent` that answers the same question in one pass; and `Lowering.Build.IsNode` scanned every component per endpoint. **The docs**: `SteelEn10255` still said its rows were unverified and would size nothing, eleven days after the attestation that made it the shipped default; `HeatExchanger.ImpliedFlow` said `FS2101` reports the value it computes, which `22` explicitly says it does not (`C-21`) and which nothing in the pipeline calls; and `PropertyBackend` said *never* `WithState` two paragraphs above a `WithState` on humid air, with no word on why that one is exempt. The review also flagged `Lowering.Build.Connect` as silently last-write-wins on a port named twice; that one was wrong --- the binder refuses it as `FS1506` with a test, and the connection never reaches lowering --- and `Connect` now says so. | **Closed 2026-09-14, all of it in one change.** `Trace` is bounded by the graph's port count and stops rather than loops, leaving the rest of a broken branch unconnected for the assembler to report. `PipeSizer` guards the empty catalogue as `ValveSizer` does. `ExchangerSizerTests` holds the rule to eight cases including the zero-flow and reversed-flow ones. Equipotentials are labelled once per check; `IsNode` is a lookup. The three documents say what the code does, and `21`'s exemption for humid air is stated with its measurement: `HAPropsSI` is stateless, a `HumidAir` owns no native state, and `NativeMemoryTests` shows two thousand reads grow nothing. `ImpliedFlow` returns NaN rather than dividing by a zero rise. What the review did not assess, because the standards that would have were not triggered: error handling in Binding and Syntax, resource management outside Fluids and Solvers, test coverage outside Components and Solvers, and architecture anywhere. The language-tier findings --- two stack overflows and a binder crash, the ones that actually took a process down --- are `L-48` and `L-49`. |
| C-76 | [`21`](21-fluid-and-state.md), [`07`](../00-foundation/07-quality-attributes.md) | **Every property read cloned a native CoolProp state of ~540 KB and nothing ever freed it, so a solve's memory grew with its evaluation count until the kernel killed the process** | What the code meant: one `Fluid` shared for the whole process, because constructing one per call cost 37 % of a measurement, and `WithState` on it because the M0 spike had shown that call to be thread-safe --- it returns a new instance rather than mutating the receiver. What that new instance is: a SharpProp wrapper around its *own* native `AbstractState`, about 540 KB of CoolProp working memory the managed heap knows nothing about, and neither the wrapper's `Dispose` nor the GC ever returned it --- measured in isolation, 20 000 `WithState` calls cost **+10.8 GB** of working set, disposed or not, against **+0 MB** for 20 000 `Update` calls on one instance. Why it stayed hidden: the managed side of each leak is a few dozen bytes, so the GC never felt pressure and never ran the finalizers that would have freed the native side; and the suite runs almost everything on `ConstantPropertyWater`. Why it surfaced as something else: `SolverScaleDiagnostics` reads a real water state at every node on every one of Newton's N+1 residual sweeps, so the 30-consumer header reached 31.5 GB resident in seconds (`dmesg`: `Killed process (FluidScript.Cor) anon-rss:31511772kB`), the OOM killer took the largest process tree, and what the user saw was Claude Code dying --- recorded on 2026-09-14 as an environment trap and an agent-memory rule against running the test, both wrong (`60`). Found the same day by probing `n = 2` under `DOTNET_GCHeapHardLimit`: 18 MB allocated on the managed heap, 2.5 GB working set. | **Closed 2026-09-14.** `PropertyBackend` keeps one `Fluid` per thread (`[ThreadStatic]`, since `Update` is a mutation and the API solves concurrently) and updates it in place; SharpProp clears its lazy cache on `Update`, and a rejected state leaves the instance ready for the next --- both verified before the change. Same residuals, same iteration counts, working set at `n = 2` **2 501 → 147 MB**, at `n = 61` **165 MB**; the ladder to 861 unknowns runs in 50 s and the whole suite with it in 59 s. The clone was also most of the property cost: water (p, T) **1 949 → 15.9 µs** median, (p, h) 2 429 → 133 µs, saturation 2 363 → 3.4 µs (`fluid-state-timings.md`), and every solve in the corpus is 4× faster. `F-19` and `C-68` are re-read in that light; `21`'s per-solve cache is still worth having, at a quarter of the stakes. The harness reads `FLUIDSCRIPT_SCALE_SIZES` and reports allocation and working set, so the next leak is one row, not one kernel log. `NativeMemoryTests` guards the backend in the unit tier (1 059 MB on the old code, under 10 on the fix) and `MemoryFootprintDiagnostics` reports what every sample's solve keeps (`62`, *Memory*). The test project cloned the same way --- the M0 spike's thread-safety test alone held 8.6 GB for the rest of every suite run, and `BackendPairDiagnostics` thousands more --- and now updates in place too; the suite process went from 8.9 GB to 229 MB, and the pair matrix gained two HEOS-mixture cells that flash cold and refuse warm from their own previous state, which in-place updating exposes and cloning hid. |
| C-75 | [`23`](23-topology-and-graph.md), [`24`](24-auto-sizing.md), `D-02`, `C-58` | **The balancing-valve `kv` promotion is dead code, so a pump with a stated head is refused instead of the valve closing on the surplus** | What the rule meant: when a stated value leaves a closed loop with one constraint too many --- `PU1 pump head=15` on the simple loop, whose ring needs 5.28 m --- `23`'s promotion looks along the loop for a valve whose `kv` nothing has decided and solves for it, which is what a balancing valve is *for*: it closes until the pump's 15 m are all spent. What happens: the count reports `FS2210`, over-specified by one, and suggests removing `HE1.in` or `HE1.out` --- the exchanger's own duty temperatures --- which no engineer would do. Why: `C-58`'s fix moved the valve's bootstrap Kv from a literal into `ValveSizer.Provisional`, which the factory writes into `SizedParameters` before the first count so that the valve exists at all; `WellPosedness.IsFree(CV1, "kv")` reads all three maps (rightly, or a parameter is sized and promoted at once) and so is false from the first pass on, and the `kv` branch of the promotion rule has not fired since `P3.7b`. `WellPosednessTests.AStatedPumpHeadConstrainsRatherThanSeedsAndTheMismatchIsReported` pins today's behaviour and says which assertions change. | **Closed by `D-96`, 2026-09-14.** A provisional is a fourth state in the overlay rather than a fourth map: `SizingOverlay.Provisional` flags what the bootstrap wrote, `IComponentFactory.Provisional` carries the set into `CircuitGraph.ProvisionalParameters`, and `WellPosedness.IsFree` reads a flagged sized value as free. `OuterLoop.Apply` skips a rule whole when any of its parameters is promoted, so no authority is reported for a Kv the solver chose. Measured on the case above: square at 12/12, `FixedFlow on HE1 -> solved for as CV1.kv`, converged in 6 iterations at 15 m with `CV1.kv` 0.773 --- 146.8 kPa from the pump, 20 kPa in the exchanger, 2.5 kPa in DN25 over 25 m, 124.3 kPa across the valve, which the Kv law gives as 0.861 m³/h over √1.24 bar. No sample in the corpus changes; the simple loop's unconstrained pass still takes the pump head, because the pump row is offered before the valve row and a provisional only matters where nothing earlier is free. Found and fixed alongside it: `FS3008` fired for the Kv 0 the iterate passed through on its way down from 630 (`S-61`). Left for later, and taken up by `P4.1`: a promoted Kv seeded at the bootstrap's 630 and took six iterations where a seed from the Kv law at the seed flow takes fewer --- `SolutionSeed.PromotedKv` now offers `ValveLaw.RequiredKv(flow, drop/2, 1000)` from the branch's stated end pressures, or the pump's stated head, and the substation's `PCV.kv` starts at 2.88 for a solved 2.13 (`SolutionSeedTests.APromotedValveKvIsSeededFromTheDropItHasToSpend`). Still open: the solved Kv appears in the unknowns table only, with no basis line saying what it absorbed. `WellPosednessTests.AStatedPumpHeadConstrainsRatherThanSeedsAndTheBalancingValveAbsorbsIt`, `.AProvisionalIsFreeAndARuleSizedValueIsNot`, `OuterLoopTests.AStatedPumpHeadIsSpentByTheBalancingValveAndNothingSizesTheValve`. |
| C-51 | [`24`](24-auto-sizing.md), `D-58`, `D-94` | **No sizing rule reads a design driver other than `tout`, and the fraction-of-peak rule has nowhere to live** | **Closed by `D-94`, and `P3.8` found the entry had the shape of the fix wrong.** It expected a sizing rule in `24` reading `ProjectSettings.Design`; measured, `design` already sizes end to end, because the binder folds every curve to its design-point value before lowering and the rules never see the difference between `power=heating` and `power=50`. What was missing was not a reader of the design point but a way for *one component* to be read somewhere else on the curve, which is what a bivalent heat pump is: its capacity is the heating curve at the bivalence point, and the boiler is the remainder. That is now `HP1 heater power=heating sized_at tout=-5` --- the same machinery as `design` with a narrower scope --- and the closed-circuit closure sizes the backup. The fraction of peak the entry wanted a rule for is the *outcome* of choosing the point (CIBSE's bivalent guidance puts the heat-pump share at 50--75 % of peak for a point near −5 °C) and is reported as the parameter's basis, never stated. **The other half of the entry stands as written and is not closed here**: a DHW circuit's design condition is a draw profile, and nothing reads `design draw=...`; that is `C-73`, deferred to P4's substation work. `OuterLoopTests.ABivalentPairSplitsTheDesignDayAtTheHeatPumpsOwnPoint` pins the pair: 27.17 kW at −5, 22.83 kW closed on the boiler, 0.398 kg/s, 56.3 °C between them. |
| C-62 | [`24`](24-auto-sizing.md), `D-89` | **The two-way valve rule rounds `kv` down unconditionally, which is only safe on a pump-driven circuit** | **Closed by `D-89`, and the audit found the fix was smaller than the entry thought.** `ValveSizer` already carried both directions and `CanSize` already answered for `Valve` as well as `ThreeWayValve`; what selected between them, `SizingContext.AvailableDrop`, was filled in **exactly one place** --- `OuterLoop.ThreeWay`. A two-way valve was therefore never told the circuit was bounded, the `bounded` branch could not run for one, and it rounded down exactly as filed. The arithmetic was there and only the context was missing. `OuterLoop.Context` now fills it from a new `Driven(graph, component)` --- is there a pump with an unstated or promoted head on a **loop through this component** --- paired with the existing `Offered(graph)`. `ThreeWay` keeps its own `driven` test and a comment saying why: a three-way valve with its bypass connected is a junction element, so it sits in no branch's `Path` and the shared helper would call every one of them bounded. **Nothing in the corpus moved**, which is the expected result and also the risk --- `m2-simple-loop`'s `CV1` is on a pump-driven ring and `m2-cooling-loop`'s `3WV` on a free-pump secondary, so both still round down at Kv 1.6 and Kv 4. A new branch no sample exercises is `C-54`'s shape, so `ATwoWayValveOnABoundedPathIsToldWhatTheBoundariesOfferAndRoundsUp` runs a valve between `supply p=300` and `return p=280` with no pump at all and asserts the basis says the boundaries determined the drop. **The reporting moved too**: the "chosen or determined" sentence was `OuterLoop.ThreeWay`'s, so a two-way valve's basis had never said which shape applied. `ValveSizer` writes it now, since `bounded` is the rule's own variable, and both passes get it. `24`'s Provenance note and its acceptance criterion are updated; the criterion had been **false for a three-way on a bounded circuit since the split first shipped**. |
| C-59 | [`24`](24-auto-sizing.md) | **`24`'s worked example counts one exchanger drop on a circuit that has two** | **Closed --- `24` now says it in the document rather than leaving it to a registry rationale string.** The worked example carries the paragraph this entry asked for: "`LOAD` changes none of the arithmetic below, and the circuit has no steady state without it... It states its duty and nothing else: no unknown, no demand, and no stated `dp`, so it adds no pressure drop to size against and the head below is unchanged." So the arithmetic is no longer unexplained, and the 5.28 m head is right for the circuit as written. **The question underneath it is real and is not what the title says**, so it is restated as its own entry rather than kept alive here: nothing in the *language* distinguishes a modelling fiction from a physical load, and `dp=0` is a workaround the user has to know to reach for. See `C-72`. |
| C-48 | [`24`](24-auto-sizing.md) | **`pipe.velocity_min` is a catalogue row no rule reads, and at a 100 Pa/m target the sizer would select a pipe it knows trips its own validator** | **Closed, implemented as this entry prescribed.** It asked for "an explicit precedence: `velocity_max` hard, gradient a target, `velocity_min` a soft bound that steps down and reports", and that is `PipeSizer`'s three steps in order --- step 1 walks up to the gradient target and reports running out of series, step 2 steps up while the velocity exceeds `Ceiling`, step 3 steps *down* while the velocity is under `SizingDefaults.VelocityMinimum` **and the smaller size would not breach the ceiling**, reporting each step. The soft bound can therefore never override the hard one, which is the part that had to be got right. `SizingDefaults.VelocityMinimum`'s own remarks record the before state: "a catalogue row no rule read until `C-48`, which meant the sizer could select a pipe it knew would make its own validator emit `FS4005`". **The residue survives and is not this entry**: the criterion that actually governs is the index circuit's total head budget, which no per-pipe rule can see. `SizingDefaults.PipeGradientTarget`'s remarks say so and `C-54` is the entry that holds it. |
| C-1 | [`22`](22-component-model.md) | `pipe.insulation` and `pump.curve` are documented but not registered | **Closed as a deliberate deferral with a guard, which is a resolved state and was being counted as an unresolved one.** `pipe.insulation` and `pump.curve` stay unregistered: heat loss is post-v1 and named curves arrive with the catalogue, and registering them early would make `FS1503` accept a name nothing reads --- silence where a user expects an effect, which is worse than an unknown-parameter diagnostic. The registry-comparison test carries both as **named exceptions with these reasons**, so neither can be forgotten nor silently added. Nothing is pending here: the decision is made, the reasoning is recorded, and a test enforces it. Re-open it when the feature lands, as its own entry. |
| C-7 | [`23`](23-topology-and-graph.md), [`22`](22-component-model.md) | Nothing says what boundary condition an I3-inferred node carries, and `FS2107` now depends on the answer | **Closed; `23` already answered it and the entry outlived the answer.** The section *I3's boundary nodes* gives the condition for all three cases, and its first two rows are the **inferred** ones --- an open port on a valve's bypass, and an open port on anything else --- against the third, which is the declared degree-one node this entry thought was the only one covered. An I3 node carries **zero flow** and is a boundary in its own right, which is exactly what makes the binder's `FS2107` exemption correct rather than a guess. `23` now says that consequence out loud beside the table, and notes that `FS2202` is what reports the stub. |
| C-25 | [`23`](23-topology-and-graph.md) | **Nothing says what a branch's `Path` order means**, and two readings are both defensible | **Closed by measurement, not by choice.** `23` now states the canonical orientation --- `From` is the end whose element comes first in `CircuitGraph.Components`, `To` is the other, and `Path` runs between them in that direction. It was going to be a decision until the corpus was measured: **every branch of every sample already ran ascending, 20 of 20**, so the rule fixes what the decomposition already produces and changes nothing. `BranchOrientationTests` pins it across five samples. `23` also now warns off the reading that would otherwise creep in: `Path` order is not flow direction --- flow direction is the sign of the solved mass flow, and a branch running against its written orientation is legal and common. |
| C-43 | [`22`](22-component-model.md) | **`IFlowComponent.DeclareUnknowns` had no consumer, and the one kind that used it was modelled by nothing** | `22` puts `DeclareUnknowns` on the interface and says `Index` is "filled in by assembly"; four packages later no assembly existed. Only the tank declared anything — one enthalpy unknown alongside one energy balance — and the counting table counted neither, because `EnergyBalances` is `Nodes.Length` and there was no term for a component-owned scalar. The two omissions cancelled exactly, so `m4-storage-header` reported square and was not. | `D-74`. The member now has a consumer on both sides: the table names the unknowns and counts the balances, `SystemLayout` allocates them, and `EquationSystem` hands each component the slice of the iterate holding its own state. What forced it was not the count but the crash — the tank reads `context.Unknowns[EnthalpyIndex]` at its first line, and against an empty span a shipped sample threw. Tracked from the solver side as `S-16`. |
| C-66 | [`24`](24-auto-sizing.md) | **Nothing sized a three-way valve on a header, for two reasons that looked like one** | `OuterLoop.Reaches` asked whether the element at the far end of a leg states a pressure. On `m2-cooling-loop` that works, because the controlled leg runs straight into `N3 return p=280`. A header states its pressure at the plant, three or more hops from any valve leg, so **every** three-way valve on one declined and kept bootstrap Kv 630 --- a Kv law demanding about 78 kg/s on a plant that moves 0.93, whose residual buries every other equation and made five source arrangements look identical. | **Closed.** Both valves on the header now size at Kv 6.3 and the residual norm falls from **95 to 1.96**; `m2-cooling-loop` is unchanged at Kv 4, and `m2-simple-loop` and `m4-storage-header` are untouched. The fix is `ValveLegs`, and the second reason it needed is the one the search nearly missed. **The common leg was identified by largest flow, and on the first pass the flows are the seed's.** Measured on the header's seed the `a` leg carried 0.5736 kg/s against 0.2868 on the other two --- mass balance holds with `a` as the sum, because branch orientation is the decomposition's choice --- so "largest flow" named the wrong leg, after which the two remaining legs shared a far end, tied on distance, and the valve declined anyway. `D-85` settles `ab` as the common port and the header names its ports outright, so the **port name is believed first** and the flow test is the fallback for scripts that do not name them. On top of that, the variable leg is now the one that does *not* close the valve's own loop: the bypass leg is whichever gets back to the common leg's far end in fewer components without passing through the valve. **The distance comparison is this project's reasoning, not an inherited convention** --- the case it would read backwards is a short tap off a header feeding a long secondary, and `ValveLegsTests` pins the criterion as an inequality so that case fails loudly. A stronger signal exists and was not needed: on a header the two legs land in different *circuits*. Closing this exposed the next thing rather than fixing the header, which is what it was for --- a tap branch written as a bare node-to-node connection drops 0 kPa, so the rule declines for an honest reason until the script gives it pipework, and underneath that sits `S-38`. |
| C-61 | [`24`](24-auto-sizing.md) | **Nothing sizes a three-way valve wired as a three-way** (the two-way-wired case is closed --- see below) | **Closed, and superseded rather than solved as written.** The two-way-wired half closed with `C-63`; the genuine three-way is sized by `OuterLoop.ThreeWay` and now works on a header too (`C-66`). The parts of this entry that survived are worth keeping in mind and are recorded in their own entries: the drop is *chosen* on a pump-driven circuit and *determined* on a pressure-bounded one, and they round in opposite directions (`C-62`); the sized drop is not the drop the valve runs at (`C-64`); and identifying the legs needed two corrections, not one (`C-66`). The prediction this entry made that did **not** hold is that the port letters are useless because binding is positional -- `D-85` named the ports and scripts write them, and believing `ab` is now what makes the common leg identifiable at all. |
| C-63 | [`24`](24-auto-sizing.md) | **A three-way valve's two legs are coupled, and no rule sized it at all** | Both legs share one `kv` and one `position` with complementary coefficients, so a drop chosen for one leg fixes the other's. `24` sized from one leg and said nothing about the other, and no implementation of it as written could be right on a mixing circuit. Underneath that, nothing sized a `three_way_valve`'s `kv` **at all**: `Bootstrap` hands provisionals out by kind while `ValveSizer.CanSize` selected by type, so `3WV` wore the largest row in the series --- **Kv 630**, chosen to behave like an open port for one pass --- for the life of every run (`C-60`). At Kv 630 the valve restricts nothing, `position` runs to its bound trying to compensate, and `m2-cooling-loop` could not converge. | **`m2-cooling-loop` converges**, on `01`'s own figures: mixing node **19.99 &deg;C** against 20, return **49.94** against 50, recirculation **0.0763 kg/s**, and `PU1` asked for a plausible **6.4 m**. An `OuterLoop` pass sizes the valve, because choosing *which* leg to size against is a comparison across sibling branches that an `ISizer` handed one branch cannot make; once the context exists, `ValveSizer` is the rule unchanged. Both legs are identified from the circuit rather than from port letters, which bind positionally and would have sized the wrong leg on any script written the other way round: the **common** leg is the one carrying what the other two split, and the **variable** leg is the one reaching a stated pressure. **The document's worked example was wrong and the measurement is what settled it.** `24` called this circuit pressure-bounded, which gives Kv 1.6 --- and Kv 1.6 asks `PU1` for **33.6 m** on a loop whose exchanger drops 5, which is the absurdity this entry originally recorded. The path from `N1` to `N3` runs *through* `PU1`, whose head is promoted, so the circuit is pump-driven and the boundary pair never bounded the valve. The bypass-balancing half of the rule stands as written and is **not** what unblocked this: measured, all four leg topologies --- empty, a `valve`, a `pipe`, a balancer in the other leg --- now reach the same answer, so `m2-cooling-loop` keeps its empty bypass and the fixture is unchanged. What remains is `C-64`. |
| C-60 | [`24`](24-auto-sizing.md), [`22`](22-component-model.md) | **A bootstrap provisional survived as if it were a design, because provisionals are handed out by kind and rules apply to types** | `OuterLoop.Bootstrap` applies every `ISizer.Provisional` to any component whose *kind* declares that parameter sizable; `ISizer.CanSize` then tests a *type*. `ValveSizer.CanSize` is `component is Valve`, so a `three_way_valve` is given a Kv and no rule ever replaces it -- against `Bootstrap`'s own promise that "the first real pass replaces it". The value it keeps is the **largest row in the series, Kv 630**, chosen deliberately to behave like an open port for one pass. It stayed one: on `m2-cooling-loop` `3WV` drops nothing, the primary's 300/280 kPa boundary over-drives the loop by about 14 kPa, the pump is asked for negative head to absorb it and is pinned at 0, and the Kv law cannot close -- 1.59 kg/s out, which is the residual that stops that sample. It was reported as a **sized** value with no basis at all, so a user asking why had nowhere to look. | `OuterLoop.Unsized` now writes a basis and a note for any overlay value no sizer both `CanSize`s and lists. The test is **static** rather than "did a basis get written this pass", so a parameter sized on an earlier pass and skipped on a later one is not slandered as a leftover. Reported rather than corrected on purpose: see `C-61`. |
| C-58 | [`24`](24-auto-sizing.md), `D-02`, `D-32` | **A valve's `Kv` was a magic `1` that no parameter map recorded** | `ComponentFactory` built every valve with `Value(symbol, kind, "kv") ?? 1`, and the fallback reached the component without reaching `StatedParameters`, `SizedParameters` or `DefaultParameters`. On `m2-simple-loop` `CV1` reported **all three maps empty**, so `IsFree` called `kv` free and promotable while lowering had already chosen it. `D-02` allows a parameter to be sized or to carry a *visible* decided default and allows nothing else. The cost was measurable: at `Kv = 1` the valve dropped **74.3 kPa**, and the promoted `PU1.head` came out **7.84 m**. | The fallback is gone and both valve kinds follow the pipe's rule -- no bore, no pipe; no Kv, no valve. `ValveSizer.Provisional` supplies the largest catalogue row so a valve still builds on the bootstrap pass, and the rule replaces it on the first real one. `CV1` now resolves to **Kv 4 at 4.6 kPa** and the head to **0.729 m**. Neither is `24`'s 5.28 m, and the whole remainder is the heat exchanger's missing pressure drop. |
| C-57 | [`24`](24-auto-sizing.md) | **A pump on no circuit and a circuit with no resistance were the same number, so the rule misdiagnosed both places it fired** | `SizingContext.LoopDrop` was a `double` and `OuterLoop.Circuit` returned `0` both when a cycle through the component resisted nothing *and* when no cycle contained it at all. The pump rule read the zero and said "its circuit contains no modelled resistance" either way. Every firing in the corpus was the second case -- `m1-syntax-reference` and `m1-syntax-tour` both declare `PU1` and never connect it, which the first sample states in its own header -- so the rule was wrong in 2 of 2 live cases, telling a reader to add a pipe when what was missing was a connection. | `LoopDrop` became `double?`, `Circuit` returns `null` for "no cycle contains it", and the rule now separates three causes that all reach zero head: no closed circuit (a missing connection), a drop of zero at zero flow (a missing duty -- at zero flow every loss law returns zero, so a ring full of real pipes reports no resistance), and a drop of zero at a real flow (a missing loss, the original message). The basis string says `no closed circuit` instead of `loop drop 0.0 kPa`. |
| C-56 | [`23`](23-topology-and-graph.md), [`24`](24-auto-sizing.md) | **A sized parameter was still promotable, so one value could have been chosen by a rule and solved for as an unknown at once** | `ComponentFactory.Defaults` states the rule in its own remarks -- "well-posedness looks for a parameter no map claims" -- and `WellPosedness.IsFree` read `StatedParameters` and `DefaultParameters` and not `SizedParameters`. Latent for four packages because nothing ever filled the third map; live the moment `P3.7b`'s loop did. The failure would have been silent in the worst way: a pump head chosen by a sizing rule *and* carried as a Newton column, converging to two different numbers with the counting table calling the system square throughout. | `IsFree` reads all three. The loop skips promoted parameters when sizing and `ASizedParameterIsNeverAlsoPromoted` asserts the disjointness on the corpus, so the two halves of the division of labour are each checked against the other rather than both against the same assumption. |
| C-55 | [`24`](24-auto-sizing.md), [`62`](../60-docs-and-devex/62-testing-strategy.md) | **The test fixture lowered a script differently from the way a solve does, and the difference was invisible while every sample stated its own sizes** | `GraphFixture.Lower` called `Lowering.Lower` directly with a bare `ComponentFactory`. That is only equivalent to what a solve builds while nothing needs sizing: a pipe with no chosen `dn` has no bore, so it is **not built at all** and lowering reports it under `Unresolved` rather than failing. Removing `dn=25` from `m2-simple-loop` -- which its own header had asked for since `P3.5` -- made three tests fail and four skip, every one of them reporting the pipe as dropped. The fixture was testing a model nobody runs. | `GraphFixture.Lower` and `WellPosednessTests.Excess` both go through `OuterLoop.Prepare`, which is public for exactly this reason. Two consequences fell out immediately: `m2-simple-loop` counts square with nothing stating its diameter, and `m1-syntax-tour`'s `PB1` resolves for the first time, taking the corpus skip count from 7 to 3. |
| C-46 | [`24`](24-auto-sizing.md), `D-58` | **`24` never cited the decision that owns its sizing point, and said a transient freezes sizes "at t = 0"** | `D-58` makes `design` the sizing point in every mode and explicitly *not* the operating point in a dynamic solve, so freezing sizes at t = 0 sizes the plant for whatever the weather is at midnight on 1 January. The document referenced `D-22` for the freeze and `D-58` nowhere at all, which is how the two drifted. | `24`'s pipeline now states that sizing runs once at the `design` point and holds for the whole run, and cites `D-58` where it says so. The remaining gap -- that no rule reads any driver but `tout` -- is `C-51`, deferred to `P3.8`. |
| C-45 | [`21`](21-fluid-and-state.md) | **The interpolation-table invitation was gated on a benchmark and carried no correctness constraint** | "Anything more -- interpolation tables, incompressible fast paths -- needs a benchmark first" asks only whether the solver is property-bound. A future session arriving with a benchmark would build a `(P,T)` table, correct for subcooled hydronics and silently wrong the day a refrigerant partition reached it through `ISubstance`. Inside the dome `P` and `T` are not independent, so a `(P,T)` grid does not interpolate badly across the two-phase region -- **it has no cells covering it at all**. | `D-79`, written into `21` beside the invitation: tables are gridded on `(P,h)` or `(P,s)`, the saturation line is a hard table boundary, and no property is ever obtained by blending two evaluated states. The existing per-solve cache already satisfies all three -- it is keyed on exact IEEE-754 bit patterns and returns a *computed* state, never a blend. |
| C-41 | [`22`](22-component-model.md), `ComponentRegistry` | **An omitted pipe elevation was a sizing candidate, so the sizer could have invented building geometry** | `elevation` was registered with `OmissionBehavior = Size`, entitling the sizing loop to choose a pipe's height difference to satisfy a constraint, while `docs/functions/pipe.md` promised "0 m, no elevation stated". The page was right: a height is where the plant is, not something equipment selection decides. Now `Defaulted("0 m")` per `D-70`. **Fixed immediately rather than with the elevation package**, and the whole suite passed unchanged — which is the point: an omission policy is unobservable until a sizer exists, and `P3.7` is the package that would have started acting on it. A defect that costs nothing today and a wrong answer next package is one to take now. |
| C-40 | [`22`](22-component-model.md), [`23`](23-topology-and-graph.md) | **A heat exchanger and the node it discharges into both claimed the same enthalpy relation** | The exchanger declared `Q = ṁ(h_out − h_in)` and the downstream node's balance reduced to `h_own = h_arriving` — the same relation with `Q` missing — so the assembled system was over-specified by one row per exchanger while the counting table counted neither the duty row nor the conflict. Settled by `D-69`: energy is a flux a component contributes to its neighbours' balances, not a row it owns. Node rows stay unconditional at `Nodes.Length`, `HeatExchanger.EquationCount` goes 2 → 1, and `23`'s counting argument is unchanged — it was right and the component was wrong. The first fix tried, dropping the downstream node's row, is direction-dependent in correctness and is written out in the decision so nobody re-derives it. |
| C-32 | [`27`](27-component-catalog.md) | **The lowering fixture and `27`'s worked table disagree about DN32 and DN40** | `ReferenceBores` gives DN32 a 35.9 mm bore and DN40 41.8 mm; EN 10255 medium gives 42.4 − 2×3.2 = **36.0** and 48.3 − 2×3.2 = **41.9**, and `27`'s own gradient table says 36.0. The fixture is labelled EN 10220, so the difference may be deliberate — but two tables in one repository disagreeing about DN32's bore is how the real one gets doubted, and neither says which series a reference circuit is drawn in. **Closed once the rows were verified (`D-67`).** `GraphFixture.Bores()` now resolves the shipped catalogue through `PipeCatalogs.Resolve` and `ReferenceBores` is deleted, so one table answers the question. Routing through `Resolve` rather than reading the instance is deliberate: it is the path a solve takes, and every test in the folder now fails if the shipped rows ever stop being verified. **Nothing moved.** DN32's bore changed 35.9 → 36.0 and the whole suite stayed green, which says the divergence was never load-bearing — and that is exactly why it could have survived indefinitely. |
| C-37 | [`27`](27-component-catalog.md), [`24`](24-auto-sizing.md) | **Absolute roughness has no provenance, is in no pipe standard, and the shipped value is for pipe that is new** | `PipeSpec.Roughness` sits beside the diameters and is verified by the same flag, and it is not the same kind of number: 0.045 mm is a textbook figure for *new commercial steel*, appears nowhere in EN 10255 or EN 10220, and no manufacturer's dimension table carries it. `27` already draws exactly this distinction for the plate Nusselt constants — a dimension is a fact about an object, a fitted constant carries an author and a validity range — and roughness belongs on the second side of it without being treated that way. **The magnitude is not small.** A scaled or corroded steel heating pipe is nearer 0.15–0.5 mm; at DN25 and Re 15 450, ε = 0.3 mm moves the friction factor from 0.0305 to about 0.0425, so the pressure gradient and the pump head that follows it rise about 40 %. Nothing in `24` offers a design margin on roughness the way `pump.margin` does on head, so a plant sized on new-pipe roughness has no stated allowance for the condition it will spend its life in. **Resolved by `D-68`.** `MaterialRoughness` carries the value, the material, the condition, a citation and its sources, and needs one source rather than two — the two-source rule catches a transcription error in a number read off an object, and there is no object here. v1 sizes on **new** pipe and says so; a script wanting aged pipe writes `roughness=0.3 mm`. The arithmetic also reframed the problem: the ±50 % published tolerance is worth about 4.5 % on the gradient at DN25, while new-versus-aged is worth about 39 %, so the condition matters far more than which table the value came from. |
| C-35 | [`27`](27-component-catalog.md) | **EN 10255 specifies an outside-diameter *range*, and `27` picks a single value without saying which** | Every public supplier table found lists DN15 at 21.7 mm and DN25 at 34.2 mm; the 21.3 and 33.7 the catalogue ships, and that `27`'s worked example computes from, are EN 10220's Series 1 *preferred* diameters. Threadable tube is specified as a range because the thread has to be cuttable, so both are defensible readings of "the outside diameter of DN25 EN 10255 tube". The gap is **about 2 % in bore, 5 % in flow area and roughly 10 % in pressure gradient** — inside the range that changes a pump selection. **Resolved by `D-67`: the nominal preferred diameter.** The maximum is the pipe the standard permits rather than the pipe anyone makes, and the error runs one way — every circuit sized slightly optimistic with nothing looking wrong. `27`'s 94.1 Pa/m is unchanged, because the worked example was already computed on the nominal value. The rows are now verified. |
| C-24 | [`23`](23-topology-and-graph.md), [`08`](../08-implementation-sequence.md) | **Lowering's step 1 presumes geometry the catalogue owns, and the catalogue is a package later** | "Each `ComponentSymbol` that carries flow becomes an `IComponent` via the registry, with its stated parameters converted to SI" reads as self-contained and is not: a `pipe` needs an inside diameter, a script states `dn`, and turning a designation into a bore is [`27`](27-component-catalog.md)'s — `P3.5`, after `P3.4`. Every other kind converts cleanly. `P3.4a` put an `IBoreLookup` seam in front of it and a six-row test fixture behind that, with the rows carrying real bores rather than the designation, because the designation *is* the trap this project names first. **Closed by `P3.5`:** `CatalogBoreLookup` reads a `PipeSpec`'s derived bore, exactly and never by nearest match, since `dn=27` is a script naming a size that does not exist. The general shape is worth keeping: sizing (`P3.7`) will hit the same wall from the other side, since an unstated `dn` has no bore either until the outer loop has chosen one, and the same seam is what makes lowering re-runnable per outer iteration. |
| C-42 | [`27`](27-component-catalog.md) | **`27` promised DN15–DN300 from a standard that stops at DN150** | The open-questions section committed v1 to `steel_en10255` over DN15–DN300. EN 10255 covers DN6–DN150; anything above it is a different series with its own dimensions and its own two sources per row. The promise was unsatisfiable as written and would have been discovered either by shipping eleven rows and calling the range done, or by inventing DN200 dimensions from the wrong series. `27` now states DN15–DN150 and says what the top end would cost.  **Renumbered from `C-31`, which `P3.4c` had already allocated to a different defect**; a register number that means two things is the same failure as a reused diagnostic code, so the later allocation moved. `P3.5`'s commit message still says `C-31`. |
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
| C-29 | [`22`](22-component-model.md) | Invariant 3 — residuals are deterministic and side-effect free — had no test | `C-2` moved it to `P3.4b` on the reasoning that it needed an assembled system. It did not: evaluating one component twice at the same iterate and comparing bit for bit is enough, and evaluating every *other* component in between is what catches shared mutable state. Asserted over the components a real lowering produces rather than over hand-built ones, so a kind added to the registry is covered without anyone remembering. Invariant 5, residual scaling, genuinely does need an assembly and stays in tier 30 as `S-3`. || C-70 | [`24`](24-auto-sizing.md), [`23`](23-topology-and-graph.md) | **The sizer and the equations named a three-way valve's legs by different criteria, so an injection circuit could not be sized at all** | **Closed by `D-88`.** `ThreeWayValve.EvaluateResiduals` gives port `a` the opening `position` and port `b` the complement — the manufacturers' A–AB control path and B–AB bypass, cast into the equations. `ValveLegs.Variable` ignored that and re-derived the control leg by walking the graph, returning “cannot tell” when the two legs tied. **An injection circuit ties**: the source leg and the return leg both land on the same header, so `TV_MAIN` declined and kept the bootstrap Kv 630, and the circuit did not solve. It matters because authority is measured against the resistance *behind* the leg, and a recirculation leg has almost none — sizing the wrong one asks for a large Kv and returns a valve with no authority over the path it controls. **Reading the port letter unconditionally was tried first and is wrong**, which is worth keeping: `Lowering.Build.End` records `component.Ports[port].Name` whether or not the script named a port, and `BindingRun.Unqualified` hands ports out in connection order. On `m2-cooling-loop`, wired `HE1 - 3WV` / `3WV - N2` / `3WV - P1`, that puts the inferred `a` on the **recirculation** leg and `b` on the control leg — exactly backwards. Implemented, measured (five failures including the sample's own convergence), reverted. The fix carries the distinction instead: `EndpointSymbol.PortStated` through `CircuitGraph.StatedPorts`, so a written letter decides and an inferred one is not consulted. Measured: `m2-cooling-loop` unchanged at Kv 4 / authority 0.66 (walk), `m2-distribution-header` unchanged (name and walk agree), injection circuit **Kv 25, authority 0.51 at 0.656 l/s, 0.9 kPa** where it previously declined. `Common`'s `ab` test still believes an inferred name deliberately — `ab` is port 0, so positional binding gives it to the first connection written, which is the common leg for both arrangements. What this exposed rather than fixed is `C-69`. |


## Observations

**`C-52` and `C-53` are findings, not outstanding work, and were filed as open by habit.** Both
record a mistake about refrigerant states that has already been corrected in code, and what is left
of each is a deferral `07` had already taken.

*`C-53` --- a refrigerant's valid range is bounded by its data, not by its critical point.* Ammonia
and propane were first given ranges stopping at their critical temperatures, which refused the
reference cycle: a −7/40 °C ammonia cycle at η_is = 0.7 **discharges at 152.5 °C**, twenty degrees
above the 132.25 °C critical temperature — superheated vapour well below the critical *pressure*,
an ordinary state. Worse, the bound moves with the compressor: the same cycle at η_is = 0.5
discharges at 205 °C, so a range wide enough for a good machine refuses a bad one, and the identity
tests that sweep η walk straight into it. `Refrigerant` now runs ammonia to 250 °C and propane to
165 °C, each with the discharge figure that set it in its own remarks. Worth keeping because the
error is silent in the other direction too: a range set from a fluid's headline numbers rather than
from the states a cycle visits will refuse real designs and look principled doing it.

*`C-52` --- inside the two-phase dome the backend answers with the vapour phase's transport
properties under labels that say the state's.* Measured on ammonia at −7 °C, quality about 0.2, the
backend returned viscosity 8.15e-6 Pa·s and conductivity 0.0252 W/(m·K) — the **vapour's**, against
liquid ammonia's 1.8e-4 and 0.55, both a factor of about 22 out — and a specific heat of 12 715
J/(kg·K) where the constant-pressure value is *infinite*, because heat added there moves quality and
not temperature. Three plausible numbers, none the quantity its field is documented as: the shape of
`S-16`, `S-22` and `S-24`. `Refrigerant.Build` blanks all three for a two-phase state, which costs
nothing today because nothing reads them and makes the failure loud the day something does. The real
answer is a `Quality` on the state with saturated-liquid and saturated-vapour lookups, which a
two-phase pressure-drop model will need and which `07` keeps out of v1 — a deferral already taken,
not a defect outstanding. Re-open it as its own entry when that model lands.


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
`WithState` on a shared instance is thread-safe, so one static instance was taken as both correct and
the cheaper half of the two options. **It was neither** (`C-76`): `WithState` clones a native state
that is never freed, and the ~320 µs it "charges whatever the pair" was mostly that clone. One
instance per thread updated in place measures 16 µs for (p, T) and leaks nothing.

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

**A note that names the package which will make it obsolete should name the right one.** Both M2
samples carried "`dn` is stated because the catalogue lands in P3.5 -- remove it when the catalogue
lands". P3.5 landed and the notes could not be honoured: the catalogue turns a designation into a
bore, and *choosing* the designation is sizing (`P3.7`). Lowering still drops a pipe with no `dn`.
Corrected to name P3.7. Worth recording because the note was written by the package that was blocked,
which is the package least able to know which later one unblocks it.

**The pump rule has no converging circuit in the corpus, and the reason is that promotion gets there
first.** Every sample that solves either states its pump's head or has it *promoted* to a solver
unknown, because a duty is a spare constraint and promotion is what spends one. The rule therefore
fired exactly twice across seven samples, both times on a pump connected to nothing. That is not an
argument against the rule -- where a head is genuinely free and a flow is genuinely fixed, the loop
drop is the answer, and `PumpSizerTests` holds it against `24`'s worked example directly. It is an
argument about coverage: `Circuit` and `Resistance` return a real non-zero drop from a real graph
(2.5 kPa on a two-pump ring, measured), and no *converging* sample exercises that path. The circuit
that would is two pumps in series, which is `S-29` and does not converge for unrelated reasons. Worth
knowing before the same shape is assumed for the valve rule, where the promotion/sizing split lands in
the same place.

**Promotion and sizing compute the same pump head by different routes, and promotion is the better
one.** On the simple loop `PU1.head` is promoted and the solver finds the head that delivers the
duty-fixed flow -- which is, by construction, the loop drop at that flow, `24`'s 5.28 m. The rule
computes the identical quantity from the outside with a `margin` multiplier and one pass of lag. Where
both are available the solver's answer is exact and needs no heuristic, so the rule should be read as
the fallback for circuits promotion cannot reach, not as the primary path. `24` presents it as the
primary path; that framing is worth revisiting when the valve rule forces the same question.

**A worked example computed at a loop mean does not match a rule that sizes at each inlet, and both
numbers are right.** `24` sizes the simple loop's `P1` at the 35 C loop mean and reads 94.1 Pa/m; the
implementation sizes every component at its own inlet, which for `P1` is the 20 C leaving `LOAD`, and
reads 99.8 Pa/m. Six percent, and the same DN25 either way, so nothing was wrong -- but the sample's
header comment had transcribed 94 as what the tool reports, which it does not. `24` already flags
inlet-versus-mean as a trap for the *pump's* head and does not notice that its own pipe example has
it. The sample now states both numbers and why they differ. Any future worked example needs to say
which state it is worked at, because the two agree closely enough to look like rounding and diverge
with the loop's temperature spread.

**Sizing runs against the seed before it runs against a solution, and the two differ enough to
notice.** `OuterLoop.Prepare` sizes from the bootstrap's flow estimate: `P1` reads 104.2 Pa/m there
and 99.8 Pa/m at convergence, a 4 % gap from the seed's cruder flow. Both choose DN25, which is the
discreteness that makes the outer loop settle in one pass. Recorded because `Prepare`'s bases are
tempting to assert on in a test -- they are not the run's answer, and a continuous parameter would
show the gap plainly.

**The simple loop is 7.84 m of head where `24` says 5.28, and both missing pieces are named rules.**
Measured against the converged field rather than argued: node pressures run 0 -> 76.79 kPa across the
pump, flat through both exchangers, 76.79 -> 2.49 across the valve, and 2.49 -> 0 across the pipe.
So the pipe contributes 2.49 kPa (25 m at the 99.8 Pa/m it was sized to, which agrees), the valve
contributes **74.3 kPa** because its `Kv` is the hidden `1` of `C-58`, and both exchangers contribute
**zero** because the heat-exchanger `dp` rule does not exist yet. `24` assumes Kv 1.6 and 20 kPa of
exchanger drop and totals 51.67 kPa. The two gaps account for the whole difference, which is worth
recording because it means the worked example is not evidence of anything until both rules land -- and
that `PumpSizerTests` had to hold the conversion against a hand-supplied 51.7 kPa rather than against
the corpus.

**`ISizer` cannot express parallel-set balancing, and `24`'s valve rule requires it.** Step 3 of the
`kv` rule says to take the larger of the authority requirement and "the balancing requirement", and
steps 1-5 of the parallel-set procedure need every *sibling* branch's drop to find the index. A rule
sees `State`, `MassFlow`, `BranchDrop` and `LoopDrop` and has no way to reach a sibling -- deliberately,
since the narrowness is what makes a rule testable without a graph and what `24`'s invariant 3 leans
on for determinism. So set balancing is not a rule at all in this architecture: it is a pass over a
branch set that happens to set a parameter on one component of each branch. Either `SizingContext`
grows a view of the set, or the balancing pass lives in the outer loop above the rules. This is worth
settling before the valve rule is written rather than after, because the answer decides whether
`PumpSizer`'s shape generalises or was only ever right for a single component.

**`24`'s worked example is computed at the loop mean throughout, and the implementation is computed at
each component's own inlet. That is now three numbers.** The pipe gradient reads 99.8 Pa/m against the
document's 94.1; the required Kv reads 1.823 against 1.833; the achieved valve authority reads 0.565
against 0.57. All three are the same 0.4 % density difference — 998.2 kg/m³ at 20 °C against 993.9 at
the 35 °C mean — and the first two only choose a catalogue row, which is identical either way. The
third is different in kind: **an achieved authority is reported to the user**, so the document and the
tool now print different numbers for the same circuit. The implementation is the defensible one and
`24` says so itself for the pump's head, where it flags inlet-versus-mean as a trap; it then works its
own example at the mean. `24` needs a stated basis per line, not a correction — both numbers are right
about different states, and the reason a reader cannot tell is that nothing says which.

**Nothing in the corpus exercised a valve, and the reason was that nothing had to.** `CV1` carried a
hidden `Kv = 1` (`C-58`) that satisfied lowering, satisfied the counting table and produced a
converging circuit, so every test that ran the simple loop passed while the valve's coefficient was an
undocumented literal. What surfaced it was not a test but a probe printing the three parameter maps
per component — all three empty on a component that had clearly resolved something. Worth repeating on
the next kind added: a component that builds is not a component whose parameters are accounted for,
and the maps are the only place that distinction is visible.

**The R5 step is coarse enough to hide a disagreement, which is why the valve rule needs both numbers
reported.** Required Kv 1.823 and 1.833 select the same Kv 1.6, so the density question was invisible
until the *achieved authority* was computed from the chosen row — 0.565 against 0.57. A rule that
reported only its selection would have agreed with `24` while disagreeing about the physics. This is
the argument for `24`'s invariant that the achieved value is reported rather than the target, and it
is worth carrying to every rule that rounds: report what the chosen row does, not what was asked for.

**`24`'s worked example now reproduces end to end, and the last difference left is the density
basis.** With the exchanger's drop in place, the pipeline produces DN25 at 99.8 Pa/m, Kv 1.6 dropping
29.0 kPa, a 51.5 kPa ring and 5.264 m of head, against the document's DN25, Kv 1.6, 29.32 kPa,
51.67 kPa and 5.28 m. Every one of those is now computed by a rule reading the solved field rather
than supplied by hand to a unit test. The residual is 0.4 % and it is the inlet-versus-loop-mean
question recorded three times already -- which is the argument for settling it in `24` rather than
widening a tolerance: it is the only thing left between the document and the tool on this circuit, and
that is a good state to keep.

**Three rules chained cost two extra passes, and the loop still settles.** The simple loop went from
one pass to four when the exchanger's drop landed, because the chain is real: the exchanger's design
flow comes from the solve, its drop changes the branch, the valve's Kv is sized against that branch,
and the valve's drop changes the flow again. `24` says two passes for this circuit, written when there
was one rule. Four of a cap of ten, `Settled` is true, and discreteness is still what ends it -- DN25
stays DN25 and Kv 1.6 stays Kv 1.6 while the continuous values under them are still moving. Worth
watching rather than fixing: the count grows with the number of *coupled* rules, not with circuit
size, and the next rule to read a branch drop will add another pass.

**A parameter can be half a law, and the registry cannot say so.** `dp` alone is not a resistance --
`Δp = dp·(ṁ/ṁ_design)²` needs the flow it was measured at, and a script never writes that. So `dp` is
a decided default and `flow` is *sized*, and the two only mean something together. The registry
describes parameters one at a time and has no way to express "this one is meaningless without that
one"; what caught it here was the component's constructor, which throws when given a positive drop and
a zero flow. That contract is doing the work a registry relation should. Recorded because the same
shape is coming for `ua`/`area`/`u` and for `dp2`/`flow2`, and the second of those is already live:
side 2 resists only when a script states `flow2`, and nothing tells a reader that stating `dp2` alone
achieves nothing.

**Handing out defaults by kind and choosing them by type is a mismatch that hides.** `Bootstrap` reads
the registry, which knows kinds; `CanSize` reads the object, which has a type. Wherever a kind is
sizable and its type is not covered by a rule, a value crosses the whole pipeline unexamined and is
reported as a size. Nothing failed, no test went red, and the number was the largest in the catalogue --
which is exactly the value least likely to look wrong, because an oversized valve just quietly stops
resisting. This is `C-58` one level up: there the literal `?? 1` reached a component and no parameter
map, here a real map entry reached the answer with no rule behind it. The general form: **a default's
provenance has to be checked at the same granularity it was applied.**

**The largest catalogue row is the right provisional and the worst thing to leave behind.** Its remark
argues, correctly, that a bootstrap must disturb the first pass as little as possible, so the most open
valve in the series is the right choice. That reasoning holds only for one pass. Surviving, it becomes
the single most misleading value available -- a component that is present, connected, counted, and
hydraulically absent. Worth pairing every "safe placeholder" with the check that it was replaced.

**Checking a derived rule against published guidance changed two of its three claims.** The core of the
three-way rule --- that authority is measured against the *variable-flow* circuit rather than the whole
loop --- turned out to be the published rule almost verbatim, which is reassuring but was not knowable
without looking. The worked example's gloss was wrong: it presented an authority of 0.92 as simply
right for a primary-side valve, where Spirax Sarco says explicitly not to exceed 0.5 and FluidFlow
calls anything above it excellent for control but wasteful of pumping energy. And the rounding
direction, the most consequential novel claim, is stated by **no** source found --- it stands on its
own physics and is now marked as this document's reasoning rather than as inherited practice. The
general form: a rule derived from first principles is worth checking against the literature *before*
it is implemented, because the parts that turn out to be conventional and the parts that turn out to be
novel are not the parts one expects, and only the novel parts need defending in tests.

**A high authority is a finding about the circuit, not an error in the valve.** The reflex is to treat
0.92 as a sizing failure. It is not reachable any other way: a circuit offering 20 kPa against 1.12 kPa
of pipe forces almost the whole drop onto whatever limits the flow. And pressure-independent control
valves are a product category built to deliver 100 % authority deliberately, so the number is not
pathological in itself --- it reports that the available differential greatly exceeds what the circuit
needs, which is the condition a differential-pressure controller absorbs. `FS4006` therefore fires on a
low authority and not on a high one.

**The port letters are positional, so neither switched port is reliably the controlled path.** `22`
named the ports `a` common, `b` controlled and `c` bypass at the time --- since renamed `ab`, `a`, `b`
by `D-85` --- and the three-way sizing rule was written on the assumption that the "controlled" one
carries the variable flow. Measured on `m2-cooling-loop`, it does not: the first switched port binds to
`N2` and carries the **recirculation** at 0.076 kg/s while the second binds toward `N3` and carries the
**primary draw** at 0.163 kg/s. The binding follows the order the connections were written --- `3WV - N2` before
`3WV - P1` --- so which port is nominally "controlled" is decided by the user's typing order rather than
by the hydraulics. The rule's *numbers* were right because they were derived from the duty and the
boundary pressures, but its *identification* of the path was not, and an implementation that reached
for port `b` would size the wrong leg on half the scripts that could be written. The variable path has
to be found topologically --- the one reaching the stated-pressure boundary rather than closing back
into the valve's own loop --- and `24` must say so before the `OuterLoop` pass is written.

**A converged answer that is physically silly is worse than one that does not converge.** The bounded
variant of the three-way rule hit every number `01` states for `m2-cooling-loop` and would have passed
the acceptance criteria written for it, while giving a small cooling loop a 33.6 m pump. Nothing in the
corpus would have caught it: the criteria check temperatures and flows, and the head is not among them.
It was caught only because the number looked wrong to read. Worth a criterion of its own --- a sized
pump head that exceeds what the loop's own resistances can explain is a finding --- and worth
remembering that "the sample converges" is not the same as "the sample is right".

**Measuring twice in opposite directions located the real defect faster than reasoning would have.**
Bounded gave too small a valve and a huge head; pump-driven gave too large a valve and no convergence.
Neither result alone says much; together they say the drop cannot be chosen from one leg at all,
because the two legs share a `kv` and a `position` and their coefficients are complementary. That is
`C-63`, and it is a gap in the rule rather than in its implementation --- which is the more useful thing
to have learned, and the reason the code was reverted rather than patched toward whichever answer looked
closer.

**The valve was never the problem; the circuit was missing a component.** Three sessions' worth of work
on `m2-cooling-loop` treated its last residual as a sizing gap --- nothing sizes a three-way valve ---
and the sizing rule, once written and measured, turned out to be unimplementable as specified because
one `kv` cannot serve two legs. Twenty minutes of looking up how the arrangement is actually built gave
the answer immediately: real installations put a balancing valve in the bypass, and this fixture's
bypass branch is literally empty. The lesson is not that the code was wrong but that **a model missing a
component that every real installation has will present as an unsolvable rule**, and the search that
finds it is cheaper than any amount of reasoning about the rule.

**A rule can be right and its worked example wrong, and only the example is testable.** `24`'s
three-way rule states the pump-driven/pressure-bounded split correctly, and then works its one example
on the wrong side of it --- calling `m2-cooling-loop` bounded because `N1` and `N3` both state a
pressure, when the path between them runs through a pump whose head is promoted. The prose was general
and true; the example was specific and false, and the example is what an implementer copies. Measuring
both answers is what settled it: the bounded reading selects Kv 1.6 and asks for **33.6 m** of head on
a loop whose exchanger drops 5, the pump-driven reading selects Kv 4 and asks for **6.4**. Neither
number appears in the rule, and no amount of reasoning about the rule would have produced them.

**Widening a `CanSize` silently widened a reporting check that reads it.** `Unsized` asks a
deliberately *static* question --- does any sizer both `CanSize` this component and list this parameter
--- so a value sized on one pass is not slandered as a bootstrap leftover on the next. Letting
`ValveSizer.CanSize` accept every three-way valve made that answer yes for valves the new pass can
still decline, which would have reported them with **no basis at all** --- `D-02`'s "absence, never
null" read backwards, and precisely the `C-60` defect the check exists to prevent. The pass that
declines is the only thing that knows why, so it now says so itself. Worth recording because nothing
about the change looked like it touched reporting.

**A deliberate deferral with a guard is a resolved state, and counting it as open is what makes a
register feel unmanageable.** `C-1` sat open for the whole project describing a decision that was made,
reasoned, and enforced by a test that carries both parameters as named exceptions. Nothing about it was
pending. The rule this suggests: an entry stays open while *someone still has to decide or do
something*; a decision plus a guard closes, and the feature landing later opens a new entry rather than
reviving the old one.

**Two of the four closed here were already answered in the document they pointed at.** `C-7` asked what
boundary condition an I3-inferred node carries; `23`'s *I3's boundary nodes* table answered it, and the
entry had read the table as covering only the declared case. `C-43`'s resolution column was fully
written and the row was never moved. Between this sweep and the two before it, **five entries across
three tiers were closed in fact and open on paper** --- which is a larger share than any of the genuine
engineering.

**`C-25` was going to be a decision and measurement made it a description.** The entry proposed choosing
a canonical branch orientation "before anything downstream depends on the current one". Measuring first
showed every branch of every sample already ran ascending, so the rule cost nothing to adopt and broke
nothing --- where choosing without measuring would have had an even chance of mandating a change to
working code. **Measure the current behaviour before deciding what it should be**; where they agree, the
decision is free.
