---
id: 30-solver-defects
title: What implementing against the solver tier found
tier: 30-solver
owns: [defect and observation record for documents 31-36]
---

# What implementing against the solver tier found

Defects, deferrals and observations from implementing against `31`–`36`. The rule and its reasoning
are in [`08-implementation-sequence`](../00-foundation/08-implementation-sequence.md).

**No solver has been built.** What has happened is that four packages in tier 20 — `P3.0` through
`P3.4a` — were written *against* this tier's contracts without implementing any of them: the component
interface waits on `31` for the shape of `SolveContext`, the residual functions implement `36`'s
smoothing constants, and the graph exists to be assembled by `32`. Everything below was found from
that side. **The absence of an entry about `33`, `34` or `35` means nothing has looked**, not that
nothing is wrong.

This file was started late. `P3.0`–`P3.4a` should each have appended to it as they went, and instead
the findings sat in [`20-core-domain/defects.md`](../20-core-domain/defects.md) where the tier that
owns them would not have seen them. The entries below are that backlog, written up in one pass.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| S-42 | [`32`](32-steady-state-newton.md), [`62`](../60-docs-and-devex/62-testing-strategy.md) | **`FS3009` and `FS3010` have no end-to-end subject left in the corpus** | Both codes were covered by `m2-distribution-header`, the only sample that reached the solver square and singular. `S-41` moved it behind the counting check, so neither code is now produced by any sample. `NullDirectionTests` still pins their *content* at the matrix level, which is where the sign and significance rules live, but nothing exercises the path from a script to the message a user reads. | Open, and cheap to close badly. A sample invented to be singular would be a sample nobody would write, and adding one to `samples/` puts a broken circuit in front of users; a fixture inside the test suite is the right home. The hard part is that a circuit which is **square and singular** is exactly the case counting is meant to catch, so constructing one on purpose means finding a deficiency counting cannot see --- which is the same search `S-33` took two sessions over. Not a blocker; recorded so the gap is not discovered by a regression that fails to fail. |
| S-38 | [`23`](../20-core-domain/23-topology-and-graph.md), [`24`](../20-core-domain/24-auto-sizing.md), [`32`](32-steady-state-newton.md) | **A secondary's flow constraint and the pump head it promotes are one freedom too many, and two secondaries make it visible** | Each consumer states `power`/`in`/`out`, which `WellPosedness` turns into two constraints: a `MixedInlet` that promotes the three-way valve's `position`, and a `FixedFlow` that promotes the pump's `head`. Measured, the promotions are branch-local and correct --- `HE_AHU` claims `PU_AHU`, `HE_RAD` claims `PU_RAD`. With **one** consumer the system is full rank; with **two** it is `Singular` at iteration 0 at every line-search floor from 1/64 to 1e-14. `m2-cooling-loop` carries the identical pair and converges, because its primary's `supply`/`return` fixes the drop its valve works against. | Open, and `M2a`'s blocker. **The fix is measured and the implementation is not done.** A pump head answering a flow constraint should be *sized*, not promoted: `24`'s rule is head = the loop's drop at the design flow, and the design flow is the duty's, which is exactly what the constraint states --- so the pair leaves the counting table together, no unknown and no equation, and `OuterLoop` closes it by re-sizing from the solved field. `PumpSizer` already implements the rule and is simply never reached, because promotion wins over sizing. **Validated by substitution**: with both heads stated and `out=` dropped from both exchangers --- which is what removing the pair does --- the header is no longer `Singular` at all. It still does not converge, because hand-picked heads of 4 m and 5 m are not the sized ones. Where a branch has no pump, `Candidates` never offers a head, nothing is deferred, and the rule is a no-op by construction, so an unpumped consumer keeps today's honest `FS2210`. **Measured after `S-41`, and it moves the entry's own account.** Five variants of the header, counted and ranked:
one consumer with its head promoted is 24 unknowns / 23 equations, rank 23 --- **one free unknown**; the same circuit with `PU_AHU head=6` stated is 23/23, **rank 23, nothing free**;
two consumers with both heads promoted is 45/44, rank 44, one free unknown; with **both** heads stated it is 43/44, rank 43, **one dependent equation**; with one stated it is square 44/44 and still rank 43.
The rule that fits all five: **on a closed circuit exactly one absolute-temperature constraint is paid for by the enthalpy level that is dropped, and every other one needs its own promotion.** One consumer needs *zero* promoted heads; two need one; the code promotes one per consumer, which is always one too many.
Two of this entry's recorded facts do not survive: *"with one consumer the system is full rank"* and the substitution validation were both measured before `S-41`, when no energy balance was being dropped, and neither reproduces now.
**The arithmetic written here is also wrong:** the constraint and the promotion do not leave the table together. Only the *unknown* leaves --- the `FixedFlow` row stays and the level pays for it --- so the change is `-1` unknown and `0` equations, not `-1`/`-1`. That is exactly why the reverted first implementation left every sample over-specified by one.
**With both heads stated the redundant row was named, and it is antisymmetric between the two consumers** ---
`HE_AHU__TV_AHU energy balance` +1 against `HE_RAD__TV_RAD` -1, `TV_AHU__PA2` +1 against `TV_RAD__PR2` -1, `HE_AHU.in stated` +0.408 against `HE_RAD.in stated` -0.409, all the way down.
The `out stated` rows do not appear at all. A redundancy that is the *difference* between two subcircuits is the signature of a symmetry rather than of a physically superfluous equation,
and it says the pair that is one too many is a pair of `in=` statements, not the `out=` statements this entry has always suspected. **One measurement of one artificial variant** --- both heads pinned at the same 6 m --- so it is a lead, not a finding.
**A second defect surfaced with it:** with `PU_AHU.head` stated, `Promote` gave `HE_AHU`'s constraint to `PU_RAD.head` --- outside the constraint's own branch, because its own pump was no longer free. That variant is square and rank-deficient at once, which is the shape a fallback reaching across branches produces.
**The deficiency is a missing header differential pressure, and `FS2211` had been saying so.** With the instrument speaking again (`S-43`), the experiment `FS2211` itself suggests --- *"add a pressure on N3"* --- was finally run.
Stating `N3 node p=310` against `N1 node p=250` (a 60 kPa header differential) **and freeing `PU_MAIN`'s head** gives 45 unknowns, 45 equations, **rank 45, nothing free and nothing dependent**, and the circuit **converges in one iteration at a scaled residual of 2.85e-10**.
That is the right physics and the sample had it backwards: a variable-flow distribution header is controlled to a **differential-pressure setpoint** and the main pump modulates to hold it. Stating the main pump's head instead says the pump runs at fixed speed and leaves the header differential floating with load, which is not what the plant does and is why nothing pinned it.
**The promotion machinery was never the defect.** Two earlier readings are now falsified outright: the antisymmetric redundancy is *not* a consumer symmetry --- giving `HE_RAD` a different `out=` changes the free direction's weights and not its existence --- and "promote one fewer head" fixes one consumer only because one consumer has no header differential to be free about.
**The converged answer is degenerate and must not be shipped as a demo.** `HS1` states no `out`, so the source outlet floats; the solve uses that freedom to land the supply header at **50.2 C**, exactly the consumers' required inlet. Both mixing valves then have nothing to blend --- the AHU recirculation leg carries 1e-4 kg/s --- and the sample's own header comment (*"every consumer runs cooler than the header, so each blends header water with its own return"*) describes something the solution does not do.
Worse, the return legs sit at **37.5 C** and **40.2 C** rather than the exchangers' 30.2 C, and `HE_RAD`'s header draw (0.3926 kg/s) exceeds the flow through its own exchanger (0.3589): **a mixing circuit cannot draw more than it circulates**, so header water is short-circuiting through the recirculation leg back to the return.
**And the setpoint is delicate**: 30 kPa reaches the iteration cap, 60 kPa converges, 70 kPa diverges. A one-iteration convergence flanked by failure on both sides is a seed that happens to land, not a robust solve.
So `S-38`'s structural half is answered and its physical half is not. What remains: give `HS1` a stated outlet or a controller so the header is genuinely hotter than the consumers need, then re-measure --- and only then consider the sample changed. `PU_MAIN.head` was also promoted to answer a *consumer's* flow constraint, which is the cross-branch attribution recorded below and means these numbers may be right for the wrong reason.
**Where it stands after `S-41`:** the header is no longer `Singular` --- it is refused by counting, 45 unknowns against 44 equations, under-specified by exactly 1. That is this entry's deficiency, unchanged, now stated where a user can read it rather than met as a zero pivot. The counting table and the rank measurement finally agree on the number, which is the precondition for testing a fix by counting rather than by solving. **A first implementation was built and reverted**: deferring the constraint in `Promote` and passing the filtered list to `Count` left every sample over-specified by one, with `Constraints.Length` unchanged in the table although the code reads as though it drops. Not diagnosed further; the table's own accounting needs reading before the next attempt. |
| S-37 | [`23`](../20-core-domain/23-topology-and-graph.md), [`24`](../20-core-domain/24-auto-sizing.md) | **Two pumps in series have one head between them and nothing says how it divides** | A booster in the supply header assists the consumers' own pumps: a branch no longer has to develop the whole loop's pressure difference, because the header pump contributes part of it. That is ordinary plant, and it is **undetermined as written** --- only the *sum* appears in the loop's pressure equation, so raising one head and lowering the other by the same amount changes nothing observable. Measured on the corrected header: source loop plus one pump per consumer is **full rank at 44 unknowns**; the same circuit plus a header booster is **rank deficient at 46**. Nothing in the script chooses between "the branch pumps do all the work" and "the header pump does". | Open. The physical answer is that a real installation states one of them --- a differential-pressure setpoint held across the header, a fixed branch-pump duty, or a decoupler that stops the two sharing head at all --- so the language needs a way to say it and the sizing rules need to respect it. `24` sizes a pump from the loop it drives, which is exactly the quantity that stops being well defined here. **`FS3009` names the wrong pump for it**, which is `S-36` again from a different direction: the free combination it reports is `PU_MAIN.head - 0.5 x TV_MAIN.position + ...`, and the pump whose *presence* causes the deficiency, `PU_HDR`, does not appear in it at all. Not an `M2a` blocker --- no demo script has a booster --- but it is the shape a user reaches for first when a branch is short of head. |
| S-32 | [`22`](../20-core-domain/22-component-model.md) | **A stated `in2`/`out2`/`power` triple implies a side-2 flow and nothing computes it** | The third and last of the "side 2 is not modelled" family, after `S-14b` (momentum) and `S-31` (energy). `HX1` states six parameters and the counting table's **only** constraint is `LOAD.dt`: none of its four temperatures becomes one. Side 1 works because `ImpliedFlow` turns `power` + `in` + `out` into a branch flow the seed uses, and `ImpliedFlow` knows only side 1 -- so the substation's primary runs at **2.21 kg/s** where 150 kW over an 85/45 drop implies 0.897. The registry says as much: `ParameterGroups` has `(power, in, out, flow)` with three freedoms and **no side-2 twin**. Unlike the other two this is not a conservation bug and is properly `P4.1`'s: it is the coupled model deciding what a stated secondary profile means. Recorded because the substation reads as nearly-solving and is not, and because the fix is a registry group plus a side-2 `ImpliedFlow` rather than anything deep. **Not an M2a blocker** -- `05`'s demo scripts are the cooling loop, the simple loop and the distribution header. |
| S-27 | [`32`](32-steady-state-newton.md), `D-78` | **A two-phase node's enthalpy column is carried by one equation, and nothing has measured whether it is enough** | `(P, h)` fixes a two-phase state exactly, which is why enthalpy rather than temperature was the right node unknown -- but inside the dome `dT/dh = 0` **exactly**, so every residual that reads a temperature at that node contributes nothing to its enthalpy column. The column is carried by the evaporator's own energy balance alone, whose coefficient in `h` is 1. That is almost certainly sufficient and it is exactly the `S-21`/`S-25` shape this project has now hit three times, each time after arguing it away first. Open until a Jacobian probe on a real refrigerant loop says so; the argument is not the evidence. Note also that a `sqrt(eps)` perturbation (~6 mJ/kg on water at 100 C) cannot itself cross a phase boundary from a state well inside a region -- the hazard is the vanishing derivative, not a chord across the discontinuity. |
| S-4 | [`36`](36-numerics-and-convergence.md), [`07`](../00-foundation/07-quality-attributes.md) | **A property-backend call can never return, and nothing has a cut-off** | Measured, not theorised: `Water + Ethanol 60/40` flashed at `(h, s)` ran past a 600 s timeout with no output and no progress. `36` covers a *solver* that does not converge and says nothing about a *backend* that does not — and a flash is itself an iteration, inside the residual, inside the Newton step. `D-22`'s stop requirement ("any isolation breach stops the run at its last verified frame") has no mechanism here: a hung flash is not a breach, it is a call that never comes back, and cancellation cannot reach a native frame. The performance harness works around it with a background thread and a five-second join, which is a diagnostic's answer and not a solver's. |
| S-23 | [`31`](31-solver-architecture.md), [`36`](36-numerics-and-convergence.md) | **A dead leg's node enthalpy is a zero column, and nothing reports it** | A terminal node with no boundary role admits no external flux (`D-64`), so its branch's mass balance forces the flow to exactly zero — correct, and reported as `FS4010`. The consequence is not reported: with no mass moving, that node's enthalpy appears in its own energy balance multiplied by zero and in every other node's the same way, so its **column is identically zero** and the Jacobian is singular for a reason that has nothing to do with the user's circuit. It is also *physically* right — stagnant fluid has no steady temperature, any value satisfies the equations — which is why the fix is a modelling decision rather than a numerical one: either the checker refuses to solve a graph with a dead leg, or a dead node takes its neighbour's enthalpy by an explicit closure equation. Neither belongs in a seed, and the seed is where it was found: `m1-syntax-reference` and `m1-syntax-tour` both carry one, and `SolutionSeedTests` exempts them by name rather than by silence. Nothing else in the corpus is affected, so it does not block `P3.7`. |
| S-20 | [`32`](32-steady-state-newton.md) | **`NewtonSettings.RetryFromSeedOnFailure` cannot be implemented where `32` puts it** | The rule is right: after a failure, retry once from the sizing seed rather than from the diverged iterate, because a diverged iterate is a worse starting point than a rough estimate, and it rescues a real case — a user edits a value, the warm start lands in the wrong basin, and a cold retry converges immediately. But `ISolver.SolveAsync` is handed exactly **one** starting vector, and on a re-solve that vector *is* the warm start. The solver has no second seed to retry from, so the setting as specified is a flag with nothing behind it. Retrying needs both seeds at once and belongs to `31`'s outer loop, which is the layer that has them. `P3.6b` ships without the setting and `FS3012` stays unregistered, since registering a code no path can produce would put it on the generated documentation page. |
| S-17 | [`33`](33-transient-time-domain.md) | **Quasi-static pressure is justified by the wrong quantity, and the right one nearly fails for air** | `33` argues the split from acoustic transit: 1500 m/s in water, milliseconds across a plant. Sound speed in air is **343 m/s** — only 4.4× slower — so a 50 m duct is 146 ms and transit time would clear air comfortably. It is not the criterion. What decides whether pressure can be treated as instantaneous is **pneumatic capacitance**: from `dp = a²dρ`, `C = V/a²` and `τ = (V/a²)(2Δp/ṁ)` across a square-law resistance. A 1 m³ hydronic loop at 200 kPa and 5 kg/s gives **0.036 s** and the conclusion holds by one to two orders; a 100 m³ AHU and duct system gives **0.34 s** at design flow and **1.7 s** throttled to 0.5 kg/s, which is the same order as the timestep and the frame interval. The physical point is not that pressure signals travel slower in air — it is that **air stores mass and water essentially does not**. The split is still right for v1; the argument for it is not, and the air-side limit is currently unstated and unguarded. |
| S-18 | [`33`](33-transient-time-domain.md), [`12`](../10-language/12-grammar.md) | **A schedule has no ambient to drive, so an outdoor-temperature scenario cannot be written at all** | `schedule` sets `component.parameter`, and there is no ambient temperature in the model to be a target. `33` also states that v1 has no ambient loss and no wall conduction, so even given one, no component would read it. The only path an outdoor condition has into a model today is a boundary node's stated temperature — which then transports downstream correctly, but is a district-supply scenario rather than a weather one. The gap is small and structural rather than deep: an ambient scalar the schedule can reach, plus `−UA(T̄ − T_amb)` contributed by the pipe. That term already has its home — it is `D-69`'s flux member, which is on `IFlowComponent` rather than on the exchanger for exactly this reason — so the component half costs nothing. Recorded because "outdoor temperature drops and the front reaches the air handling unit late" is the first transient a user will try to write, and nothing today says why they cannot. |
| S-19 | [`33`](33-transient-time-domain.md) | **The `nodes=` guidance inverts on the air side, and the CFL limit gets 24× tighter for a shorter delay** | The step limit is the residence time `Vρ/ṁ`, and air is ~800× less dense than water. `33`'s DN20 water cell — 0.0925 l, 988 kg/m³, 0.0763 kg/s — gives τ = 1.20 s and a step of 1.08 s. A Ø315 duct cell of the same 0.25 m length holds 19.5 l at 1.2 kg/m³ and 0.467 kg/s: τ = **0.050 s**, a step of **0.045 s**. Same `nodes=`, **24× more steps** — roughly 13 000 over a 600 s horizon against 1 400, and two Newton solves each. The payoff moves the other way: a front crosses 8 m of duct at 5 m/s in **1.6 s**, so duct transport is barely visible while the air handling unit's thermal mass, which v1 does not model, dominates the response. So the document's advice — that `nodes=` is the single most important modelling decision in a transient run — is true on the water side and close to backwards on the air side, and `FS3101` will fire on every air-side model. |
| S-29 | [`32`](32-steady-state-newton.md) | **Two pumps in series terminate `NonFinite`, and one pump on the same count converges** | Isolated to the second pump and nothing else. The simple loop solves in five iterations at 12 unknowns; adding a node and a second *pipe* solves in five at 14; replacing that pipe with a second **pump** at the same 14 unknowns terminates `NonFinite` after five. `PU2.head` is *stated* in the failing case, so sizing, promotion and the counting table are all out of the picture -- one free head, a flow the duty fixes, a square system. Series pumps are ordinary plant (a primary and a secondary on one ring, a booster on a riser), so this is not an exotic topology. Not diagnosed further; the likely suspects are the pump curve going negative past its shut-off flow during a step and the seed placing both rises on one branch. `TwoPumpsInSeriesDoNotConvergeYet` pins the current behaviour and fails the day it is fixed. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| S-43 | [`36`](36-numerics-and-convergence.md), [`32`](32-steady-state-newton.md) | **The rank instrument and the report that calls it used different rank criteria, so one report contradicted itself two lines apart** | `SolveExplanation` measured a pivot against the **largest pivot** and called the header deficient; `NullDirection.Of` measured it against the **matrix norm** scaled by `Tolerances.JacobianSingular` (1e-12) and returned no direction at all. On `m2-distribution-header` with one pump head stated --- largest pivot 385.6, smallest 2.754e-9 --- the norm-scaled floor lands near 1e-9 and calls that collapsed pivot live. So the report printed `rank 43: 1 unknown(s) nothing determines, 1 equation(s) the others imply` and then `(none found)` under **both** direction headings. The instrument went blind exactly where `S-38` needed it to speak, and the silence looked like "there is no direction" rather than "I used a different rule from the one that said there was". | **Closed by having one criterion instead of two.** `NullDirection.RankTolerance` is public, relative to the largest entry --- which under full pivoting *is* the elimination's first pivot --- and `SolveExplanation` now references it rather than keeping a copy. `DenseLu` keeps `Tolerances.JacobianSingular` and should: it is deciding whether a Newton **step** can be taken, and a pivot near the arithmetic's own noise is what stops it. Rank is a different question. The constant is not delicate --- measured, a circuit that solves runs a smallest-to-largest pivot ratio of 4e-2 to 6e-4 and a deficient one runs 6e-12 to 2e-14, six orders of clear water --- and it sits deliberately between machine epsilon and the forward-difference noise floor near `sqrt(eps)`. All 1426 tests pass unchanged, so the wider criterion reports nothing new on a circuit that was already right. **The disagreement, not either constant, was the defect**: two owners of one rule drift, and the drift is silent. |
| S-41 | [`23`](../20-core-domain/23-topology-and-graph.md), [`36`](36-numerics-and-convergence.md) | **A closed circuit annotated with its own pressure datum was read as open, so the energy redundancy every closed circuit has was never removed** | `D-86` established that a stated pressure on an *interior* node is a datum and admits no mass, and fixed `HasUnknownFlux`, `WellPosedness.Count` and `SolutionSeed.Free` --- three sites. `HydraulicComponent.IsClosed` was a **fourth**, and kept the sentence `D-86` overturned, three lines above the comment that overturns it: *"a stated `p` lets mass in to hold the pressure"*. So `m2-distribution-header`, whose `N1 node p=250` is an expansion vessel connection, reported `open` with **zero boundaries** and `unknown flux False` --- a contradiction on the face of it. `NeedsEnthalpyLevel` returns false at its first guard for an open part, so **no energy balance was dropped**. A closed circuit's energy balances are always one short of independent: add the same enthalpy offset to every node and each balance is satisfied unchanged, because the offsets cancel against the mass balances, and a duty relation `m*(h_out - h_in) = Q` never sees it at all. That undropped redundancy is precisely what `FS3010` had been reporting --- `N1`, `N2`, `N3` energy balances at weight 1, thirteen more behind them, and the two mass balances the offset argument leans on at 0.91 and 0.73. | **Closed by dropping the pressure clause from `IsClosed`.** A `supply` or a `return` is already caught by the boundary clause; a stated `flow` still opens the part, because it genuinely injects mass. The header now reports `closed`, drops one energy balance as its level, and counts **45 unknowns against 44 equations --- under-specified by 1**, refused before the solver with `FS2211` instead of met as a zero pivot at iteration 0. **The deficiency did not change; where it is reported did**, and the counting table now agrees with the rank measurement (44) that had contradicted it. Found by the first run of `SolveExplanation`, from two lines of one report: a row direction dominated by energy balances beside `less enthalpy levels 0`. Fixing it also exposed that the sample's two loads were written `power=+24` and `power=+30` against `in=50 out=30` --- fluid cooling, duty claiming heating --- which `FS2203` caught the moment the circuit was correctly read as closed, at 108 kW. `m2-simple-loop` and `m2-substation` had it right (`power=-30`, `power=-150`). |
| S-33 | [`32`](32-steady-state-newton.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **The distribution header is rank-deficient at the seed, and the counting pass calls it square** | **Closed by `S-41`, and the disagreement this entry recorded was the whole finding.** Counting said square, a full-pivot elimination said one short, and the two contradicted each other in both directions on the same script. The Jacobian was right and the count was wrong: an energy balance that should have been dropped as the closed circuit's level was still in the system. This entry's own strongest clue was recorded and not followed --- *"the row left dependent is an **energy** balance (`N2`), not a pressure one"*, and *"points at `D-65`'s territory (a closed circuit fixes every enthalpy difference and no absolute value)"*. That is the answer, written down two sessions before it was acted on. All three hypotheses it tested and rejected stayed rejected. |
| S-40 | [`32`](32-steady-state-newton.md), [`36`](36-numerics-and-convergence.md) | **`FS3010` reports nothing on a circuit that demonstrably has a row null direction** | The rebuilt `m2-distribution-header` terminates `Singular` and emits `FS3002` and `FS3009`, but **not** `FS3010`. The direction is there: a finite-difference Jacobian taken at the same point names seven rows, mass balances at weight 1, which is exactly what `S-39` was found from. `NullDirection.Redundancy` transposes and delegates to `Of`, and on the transpose the elimination finds nothing above its significance cut. So the user is handed only the column half --- the half `S-36` exists to warn against trusting alone. | **Closed, and not by touching `NullDirection`.** With the header's circuit correctly read as closed and its redundant mass balance dropped (`D-86`), the elimination on the transpose has a different matrix and finds the direction: `FS3010` now reports `N1 energy balance + N2 + N3 + 0.91 x TV_RAD mass balance + 0.78 x N5 energy balance ... and 11 more`. The suspicion recorded here --- that `Of`'s significance cut was calibrated for column scales and wrong for row ones --- was **not** the cause and is not evidence of anything. The instrument was fine; it was being handed a matrix built from a mis-modelled circuit. `ASingularCircuitIsToldWhichEquationTheOthersAlreadyImply` now asserts the presence it was written to assert the absence of. |
| S-39 | [`23`](../20-core-domain/23-topology-and-graph.md), [`36`](36-numerics-and-convergence.md) | **A pressure datum written on a junction silently turns a closed circuit into an open one, and the circuit is then deficient by exactly one** | `HasUnknownFlux` is true when a node *carries a mass balance* and states a pressure. For a `supply` or a `return` that is right --- mass genuinely crosses there. For a plain interior `node p=250` sitting on a three-way junction it is not: nothing enters, the circuit is closed, and its mass balances still sum to an identity. But the partition now looks open, so `EquationLayout.Redundancies` drops **no** balance --- measured, `0 balances dropped` on a 46-unknown header --- and no external-flux unknown appears to take up the slack either. The row null direction says so exactly: `NM_AHU + N1 + N3 mass balance`, every weight 1, same sign, which is the global conservation identity and nothing else. **Measured decisively**: the same fixture with the datum moved from the junction `N1` to the two-connection node `N2` stops being `Singular` at all. The rule is deliberate --- `WellPosedness` documents the carries-a-balance test in a comment written against it --- so this is a decision to revisit, not an oversight. | **Closed by `D-86`.** `HasUnknownFlux` now tests the node's *kind*: only a `supply` or a `return` with no stated `flow` admits unknown mass. An interior node's `p=` is a datum. `m2-distribution-header` keeps its datum on `N1`, the return header's junction, which is where an engineer puts the expansion vessel --- the workaround of moving it to a mid-branch node is gone. **The rule was written in three places and the fix had to change all three**: `HydraulicComponent.HasUnknownFlux` decides the mass-balance drop, `WellPosedness.Count` decides the flux *unknown*, and `SolutionSeed.Free` decides which nodes the seed may push flux into. Changing the first alone left the table an equation short and advising a pressure on a mid-branch node; changing the first two left `m1-syntax-tour`'s `NB2` seeded 0.167 kg/s out of balance, so the seed's divergence-free claim was false. |
| S-36 | [`32`](32-steady-state-newton.md), [`36`](36-numerics-and-convergence.md) | **`FS3009` names the wrong half of a singular system, and three sessions of pump theories came from believing it** | The null direction reports which *columns* move together. On the header it has always named pumps --- `PU_AHU.head - PU_MAIN.head + PU_RAD.head`, then `PU_RAD.head - 0.12 x TV_RAD.position + 0.1 x ...` --- and `S-33` and `S-34` spent four measured pump arrangements on that lead. Transposing the Jacobian and eliminating again gives the **row** null vector, which says which *equation* the others already imply, and on the minimal reproduction it is: `+1.00 NM_RAD mass balance`, `-0.85` on each of the three subcircuit energy balances, `-0.43` on each of the six header ones --- **one mass balance against every energy balance in the circuit**. Not a pump, not a valve. A square system has exactly one of each when it is deficient by one, and the column vector is only where the elimination happened to land; the row vector is the redundancy itself. | **Closed: the instrument is built and the defect it was chasing now has a name.** `NullDirection.Redundancy` transposes and reuses the same full-pivot elimination, and `FS3010` reports the row direction beside `FS3009`. The row vector this entry measured --- *one mass balance against every energy balance in the circuit* --- is exactly `S-39`: the header's pressure datum sat on a junction, so a closed circuit was read as open, no redundant mass balance was dropped, and the global conservation identity showed up as the redundancy. Every cause this entry eliminated stayed eliminated. The remainder splits cleanly: `S-39` owns the defect, `S-40` owns the one place the instrument is still silent, and `S-33` owns the counting-versus-rank disagreement. **This entry's real contribution was the distinction, not the fix** --- a square system deficient by one has a column direction and a row direction, and only the row one names the redundancy. |
| S-1 | [`31`](31-solver-architecture.md) | **`SolveContext` and `UnknownDeclaration` are named as this tier's and defined nowhere** | **Closed by ratification.** `31` now carries `SolveContext`'s shape beside `UnknownDeclaration`'s, and says outright that both are defined in `FluidScript.Core.Components` and adopted here rather than specified here first. That is the honest resolution of the choice this entry posed: six components implement the shape, it has held through promotion, component-owned scalars (`D-74`) and the no-allocation rule, and changing it now would cost six rewrites to buy nothing. `31` also records the three properties that are load-bearing and invisible in the field list --- it is a `readonly ref struct` because `EvaluateResiduals` runs N+1 times per iteration and must allocate nothing; `Flows` is signed and **per port** so a component never learns which way its branch was written (`C-25`); and `Parameter(index, own)` is the only correct read of a promotable value. |
| S-5 | [`36`](36-numerics-and-convergence.md) | **"Continuous in value and first derivative" does not say how to check it, and the obvious check fails on correct code** | **Closed.** `36` gains *How to test C¹, and how not to*, which states the failing form and the working one. The obvious test --- finite differences either side of the join, asserted close --- measures the derivative's *sweep* across the band rather than the property: over the valve's 100 Pa regularisation the true derivative runs from 0 to about 3.75e7, so the test fails on correct code. The working form probes the one-sided derivative at the join with a **shrinking step** from each side and asserts the two sequences reach the same limit, relative to that limit. `36`'s acceptance line now points at it, so the next implementer does not have to write the wrong test first to find out. |
| S-12 | [`36`](36-numerics-and-convergence.md) | **Per-branch flow scaling is right and the reason `36` gives for it does not hold** | **Closed by correcting the reasoning rather than the code.** Per-branch scaling was always right; the justification was not. `36` now says so explicitly: the discarded claim --- that one scale lets the test "declare success while that balance is off by 10 %" --- does not follow from this table's own `newton.residual_tol` of 1e-8, which permits 1e-7 kg/s absolute, two parts per million of the bypass. The two reasons that hold are written in its place: the convergence test stops being a **relative** statement when one scale spans 200x, and the bypass's Jacobian columns come out 200x smaller, which is a conditioning problem that makes `newton.step_tol` meaningless on the small branch. **An argument that reaches a correct conclusion from a false premise is worth fixing** --- the next person to touch scaling would have checked the number and doubted the design. |
| S-30a | [`32`](32-steady-state-newton.md) | **The cooling loop's singularity is the seed, and it is the temperature *walk* rather than the datum** | Proven by substitution: replacing five node enthalpies with `01`'s own figures -- the mixing node at 20 &deg;C between a 6 &deg;C primary and a 50 &deg;C return -- stops the solve going `Singular` and lands it on **20.13 &deg;C** at the mixing node against `01`'s 20, and **0.07626 kg/s** on the recirculation branch against `01`'s 0.0763. The residual left is entirely hydraulic (`3WV`'s Kv law), so the energy side is solved. What the stock seed does instead: `Datum` takes the *first stated node temperature*, which here is the 6 &deg;C primary boundary, and every unstated node is then `datum - NominalRise x steps`. So a secondary loop `HE1` states as 20/50 was seeded at **2 to 6 &deg;C** -- every node on the wrong side of the mixing split, and the node *after* the pump colder than the node before it. | **Closed by `S-30b`**, and two earlier fixes were built, measured and reverted on the way. **(1) Placing a component's stated `in`/`out` on the nodes at its own ports**: correct as far as it goes and not enough alone -- the unplaced neighbours stay near 6 &deg;C, the seed then carries a 46 K jump across one component, and the first Newton step throws `N1` out of the fluid's range (`NonFinite` at 1, where it had been `Singular` at 11). **(2) That, plus a datum taken as the mean of every temperature the script names**: makes `m2-simple-loop` converge **at the seed** -- zero iterations, norm 3.7e-9 -- and still walks `3WV__P1` down to **0.20 &deg;C** and the substation's `SP__HX1` up to **115 &deg;C**, both out of range, failing earlier (7 and 2 iterations) and harder than before. One sample better and two worse is not a trade worth keeping, so both were reverted. The part that survives every attempt is that **`- NominalRise x steps` is the wrong shape**: a monotone walk away from a single global level cannot serve a circuit whose nodes sit at several levels, however well that level is chosen. What it needs is propagation from the anchors -- stated node temperatures and stated port temperatures -- along the branch paths, and the honest reason that is not in this entry as a fix is that the rule for a *mixing* node, adjacent to anchors at 6 and 50, is a modelling choice rather than an obvious one. |
| S-34 | [`23`](../20-core-domain/23-topology-and-graph.md), [`01`](../00-foundation/01-vision-and-scope.md) | **Every pump arrangement of the distribution header is rank-deficient by one, so the pumps are not the cause** | Measured across four variants, each built and eliminated at the seed. As shipped (3 pumps): rank 27 of 28. Plant pump removed, `HS1` still stating `out`: counting **refuses** it, over-specified by 1. Plant pump removed and `HS1.out` dropped (2 pumps, one per branch, which is the arrangement good practice asks for and the one the user identified): still **rank 26 of 27**. Plant pump kept and `HS1.out` dropped, so `PU_RAD` is sized rather than promoted: still rank 26 of 27. Adding a second pressure datum on the isolated side: counting refuses that too. **Four arrangements, four deficiencies of exactly one** --- and the conclusion drawn from that, that "no arrangement counting will accept is one the Jacobian will", is now **falsified**: one is. Every arrangement tested moved the *pumps* around while leaving the source in line with the header, and `F-21` is that the source needs a **loop of its own**. Measured with the consumers written as real mixing circuits, in line is rank deficient and own-loop is **full rank** at 44 unknowns. | **Closed, and it is the entry that took the longest to be wrong in a useful way.** Four pump arrangements, four deficiencies of exactly one -- and the pumps were never the cause, which this entry had already concluded. What it could not yet name, the row null direction later did: the header carried **two** independent deficiencies at once, `S-39` (a pressure datum on a junction, which quietly opens a closed circuit) and `S-38` (two consumers sharing a header differential nothing states). Every measurement taken before those were separated reported one combined direction pointing at neither, which is exactly why four careful variants each came back deficient by one. The blocker this entry names last -- "attachment cannot express a mixing node" -- is discharged: the fixture writes both ends of each tap by hand and `F-16`/`F-17` are closed. What remains is `S-38` alone. |
| S-30 | [`32`](32-steady-state-newton.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **The cooling loop is bounded, physical and still singular at 11 iterations** | **Closed.** `m2-cooling-loop` converges: measured this session at **1 iteration, norm 3.7e-10**, with `3WV` sized to **Kv 4** at authority 0.66. `01`'s figures are met -- mixing node **19.99 &deg;C** against 20, return **49.94** against 50, recirculation **0.0763 kg/s**, and `PU1` asking a plausible **6.4 m**. The diagnosis in this entry was right about the symptom and wrong about the cause: `position` pinned at 1 was not the solver failing to find a reachable path, it was the valve wearing bootstrap **Kv 630**, at which the valve restricts nothing and no position can balance the loop (`C-60`, `C-63`). The seed suspicion was separately real and closed as `S-30b`. Not an `M2a` blocker any more. |
| S-35 | [`32`](32-steady-state-newton.md) | **Putting any component in a three-way valve's bypass leg made a well-posed circuit singular at iteration zero** | `Steps` restarted its walk at each branch, so the *first interior node of every branch* took step 1 and the seed gave it the same pressure as every other branch's first node. Two branches leaving one junction element are adjacent **through** it, and that kind of adjacency is the kind a per-branch walk cannot see. A three-way valve is exactly that shape: with a component in each leg its three ports seeded to one pressure, `sqrt(dp)` went to zero, and the Kv law's derivative with respect to `position` went with it -- a column of zeros, `FS3009`, `Singular` after **0 iterations**. `m2-cooling-loop` escaped it only because its bypass leg is **empty**, which put that side on a branch *end* (`N2`, step 0) instead of an interior node. Measured: a `valve`, a `pipe`, and nothing at all in that leg gave three different outcomes on the same circuit, and the pipe and the valve were bit-identical to each other -- so it was never about Kv, balancing, or the valve law. | Each branch now starts its walk at **its own leg index at the element it leaves** rather than at zero, so the legs of a junction begin one step apart and their first interior nodes cannot agree. Within a branch nothing changes. That alone turned the failure into `NonFinite`, which exposed the second half: the band walked one-sidedly *down* from each level, so `Band` steps is 8 K below it -- harmless at 60 &deg;C and **below freezing at 6**, where the seed ends in a failed property call rather than a bad guess. Chilled water at 6 &deg;C is not exotic and the cooling loop is one, so the band is now **centred** on its level. All four leg topologies now behave identically and reach `01`'s figures -- mixing node **19.98 &deg;C**, return **49.93 &deg;C**, recirculation **0.0763 kg/s** -- with no sample's termination changed. `AComponentInAValvesLegDoesNotCollapseItsPortsOntoOnePressure` and `NoNodeSeedsOutsideTheRangeItsFluidIsDefinedOver` pin both halves. |
| S-30b | [`32`](32-steady-state-newton.md) | **The seed walked every node down from one global level, so a loop rated 20/50 started at 2 &deg;C** | `Datum` returns the first stated *node* temperature and every unstated node was `datum - NominalRise x steps`. On `m2-cooling-loop` that datum is the 6 &deg;C primary boundary, so the secondary -- which `HE1` rates 20/50 -- was seeded at 2 to 6 &deg;C, on the wrong side of the mixing split and with the node *after* the pump colder than the node before it. `Singular` at eleven iterations. | The level is now **propagated downstream from what the script names** rather than walked away from one number. Anchors are stated node temperatures and stated port temperatures; a node with no anchor takes the mean of the anchors reaching it from upstream, which at a mixing node is the two streams it mixes and everywhere else is the one thing feeding it. Branches meet at junction *elements* as well as at nodes, so a three-way valve links the node before it to the nodes after it -- without that the level anchored on an exchanger's outlet never crossed the valve, which was the 46 K jump that made an earlier attempt worse than doing nothing. **`m2-cooling-loop` now reaches `01`'s figures**: the mixing node at **20.13 &deg;C** against 20, the return at **50.07 &deg;C** against 50, the primary at 6.12 against 6. It no longer goes `Singular`, no promoted parameter runs to a bound on the way, and `m2-simple-loop` now converges **at the seed** in zero iterations. |
| S-33a | [`32`](32-steady-state-newton.md) | **A singular system was reported as singular "around" one component, which is usually the wrong one** | `FS3002` named `system.Unknowns[factored.SingularColumn]`, and `DenseLu` uses **partial** pivoting -- so the column it stops at is whichever its row search reaches first, not the one most responsible. On `m2-distribution-header` it named `PU_RAD` and suggested checking for a missing pressure datum, on a script that states one. What is actually undetermined there is `PU_AHU.head - PU_MAIN.head + PU_RAD.head`: no single name can express it. | `NullDirection` eliminates the Jacobian again with **full** pivoting and back-substitutes one null vector, and `FS3009` names the combination alongside `FS3002` rather than replacing it -- one code for why the run stopped, one for what it found. It rebuilds the matrix rather than keeping a copy, because `DenseLu.Factor` overwrites what it factorises and an *n*&sup2; copy per iteration to serve the last one is the wrong trade; N+1 residual evaluations on a run that is over is the right one. **It paid for itself immediately**: `m2-cooling-loop`, whose failure at iteration 11 had resisted diagnosis (`S-30`), reports a direction made of **enthalpies and branch flows** -- `3WV__P1.h + 0.83 N3.h - 0.8 N1.h - 0.47 b1 + 0.47 b2 + 0.47 b3` -- which is the energy side, not the pressure side, and is the first hard evidence about what that defect is. |
| S-31 | [`22`](../20-core-domain/22-component-model.md), [`24`](../20-core-domain/24-auto-sizing.md) | **A coupled exchanger created energy from nothing** | `EvaluateEnergyInjection` put the whole duty into side 1 and zero into side 2, with a remark saying so: "duty mode makes no claim about a second stream, and a coupled exchanger's -Q on the other side arrives with the rated model (P4.1)". But a script that wires `in2`/`out2` **and** states `power` has already made the claim -- 150 kW crossing an exchanger leaves one stream and enters the other. On `m2-substation` the district primary sat at **85 C from end to end** instead of returning at 45, because nothing ever took its heat away. | Side 2 now withdraws the same duty whenever the script wired it, with its **own** `ForwardShare` from its own flow -- the streams are usually counter-current, and a shared share would put the withdrawal on the wrong port exactly when they are. `NPR` moved from 85.04 C to **70.58 C**, which is the correct drop for its flow. What `P4.1` owns is how much duty crosses when the script does *not* say (e-NTU, LMTD, a rated point); it does not own conservation. Blast radius is one sample: nothing else in the corpus wires a second side. |
| S-26b | [`32`](32-steady-state-newton.md), [`22`](../20-core-domain/22-component-model.md) | **Nothing bounded a promoted parameter, so one ran away as soon as its column could move** | `position` is a fraction and `head` is not negative; the solver knew neither. Invisible until `S-26a` was fixed -- a column that cannot move cannot run away -- and then immediately visible: `m2-cooling-loop`'s `3WV.position` walked to **5.465**, a fraction five times fully open, and `PU1.head` to **-0.49**, both reported as an answer. | `ResolvedParameter` gained `Minimum`/`Maximum`, and the component declares them because the component is the only thing that knows (`D-30`) -- the same reason `SystemLayout` reads the unit off that record. `EquationSystem.Project` clamps every promoted column into its range and needed **no new constructor parameter**: `_promoted` already carried the element and slot, so the bounds were already reachable. `NewtonSolver` projects after the step rather than constraining the step, which leaves the line search's arithmetic untouched. `FS3008` names the parameter that ran out of room, once per parameter rather than once per step. Measured: the position is now held at 1 and the head at 0.2, `m2-simple-loop` is unaffected at 5.2635, and the substation is unchanged. **The cooling loop still does not converge** -- it terminates `Singular` at 11 iterations instead of `Diverging` -- which is a separate defect rather than this one half-done, and is recorded as `S-30`. |
| S-26a | [`22`](../20-core-domain/22-component-model.md), [`36`](36-numerics-and-convergence.md) | **A clamped parameter's Jacobian column was dead at either bound, so a promoted position could never step back into range** | `ValveLaw.Opening` opened with `Math.Clamp(position, 0, 1)` and `NewtonSolver.Jacobian` builds columns by **forward** differences. At `position = 1` the perturbation clamped straight back to 1, every entry came out `0 - 0`, and `DenseLu` reported the system singular naming the valve; the same happened anywhere below 0. **A bound is not somewhere the iterate may not go -- it is somewhere the derivative stops existing**, and a step onto it was unrecoverable because the column that would step back off was the one that died. | The clamp is gone. **Equal-percentage needed no repair at all**: `R^(x-1)` is smooth, strictly positive and monotone on the whole real line, so deleting the clamp is the entire fix for the default characteristic and for every valve in the corpus, with values inside `[0, 1]` untouched to the last bit. Linear and quick-open continue linearly above 1 and stay floored at zero below it, because `phi(0) = 0` with `phi'(0) > 0` admits **no** non-negative C1 extension below zero -- a line through the origin with positive slope goes negative, and a negative phi is a negative Kv, which drives flow against its own pressure difference. A leakage floor was tried and reverted: it changed a `phi(0) = 0` that is deliberate and asserted, on characteristics nothing in the corpus uses. `m2-cooling-loop` went `Singular` -> `Diverging`, which is the column coming alive. |
| S-14b | [`22`](../20-core-domain/22-component-model.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **A coupled heat exchanger had no side-2 pressure relation, so the substation could not assemble a square system** | `Relations()` counts one pressure relation per non-node component *per branch it lies on*, so `HX1` -- crossed by both the primary and the secondary -- was credited with two while `HeatExchanger` declared one. `m2-substation` therefore counted 23 for 23 and assembled 22 rows: a circuit the checker called square and no solver could be handed. It had never once reached the Newton iteration. | `EquationCount` became `SecondarySideConnected ? 2 : 1`, and `ComponentFactory` tells the component whether the script wired `in2`/`out2` -- lowering is the only thing that knows (`D-63`). The *resistance* is separate from the *row*: side 2 resists only when a script states `flow2`, because no rule can choose it (a rule sees one branch and this component sits on two, the same limit as `C-49`), and an ideal side still carries `p_in2 = p_out2`. That is a statement where declaring nothing was a hole. The substation now assembles 23/23 and iterates. It terminates `NonFinite` after 9 iterations, which is a different defect and a much smaller one. `RowAllowance` -- the test helper that had been subtracting this exact row -- is deleted, exactly as its own remarks said it would be. |
| S-24 | [`23`](../20-core-domain/23-topology-and-graph.md), [`31`](31-solver-architecture.md) | **A free enthalpy level was counted as an unknown no layout allocates, so a square table assembled a row over** | `CountingTable.EnthalpyLevels` was added to `Unknowns` and matched by the constraint row a stated temperature contributes, and the two cancelled. Nothing allocates a level column, because the offset is a null direction of the node-enthalpy columns already counted — `SystemLayout`'s own test wrote the subtraction down, with a comment saying the shortfall was "in the energy block's rank rather than in its width". The comment was right and the arithmetic was not: `m2-simple-loop` read `Excess = 0` and assembled 13 rows against 12 columns, and Newton refused it as over-determined on a circuit `23`'s own check called square. | `D-75`. The level moves from a term added to `Unknowns` to a term subtracted from `Equations`, and `EquationLayout` drops one energy balance per level component exactly as it already drops one mass balance per closed one — the two are the same argument, since a branch's two ends contribute the same upwind enthalpy times the same flow with opposite signs and cancel through the smoothing band as well. `Excess` is unchanged for every script and `FS2211` still fires. The `EnthalpyLevels != 0` skip came out of `ASquareCircuitAssemblesAsManyRowsAsItHasColumns`, which had been excusing the one sample that was wrong. Third instance of one shape after `S-16` and `S-22`, and all three were found by holding an assembled artefact against the table rather than the table against itself. |
| S-28 | [`31`](31-solver-architecture.md), [`32`](32-steady-state-newton.md) | **The outer loop assembled and factored a system without consulting either verdict, so a malformed script threw `ArgumentException`** | `WellPosednessResult.CanSolve` existed and `RunAsync` never read it. `m1-syntax-reference` is 18 unknowns for 19 equations and says in its own header that it is not a solvable circuit; it reached `DenseLu.Factor`, which threw on a Jacobian that was not square. A pipeline stage throwing on user input, which the contract forbids outright -- and reachable from the API by any script a user is halfway through typing. | Two guards, because there are two ways to be non-square and `CanSolve` sees only one. `CanSolve` is checked before the system is built, which catches the counting table's own verdict and reports the first *error* diagnostic when there is one, so `m1-syntax-reference` now names `PU1, PU1__in, PU1__out` rather than a matrix order. Then `system.Rows != system.Columns` is checked on the built system, because **the counting table is a prediction and the assembly is the system**: `m2-substation` counts 23 for 23 and assembles 22, `S-14b`'s missing side-2 momentum row. That one now reads "assembly gave 22 equations for 23 unknowns, though counting predicted 23 for 23", which names the defect instead of hiding it. `EquationLayoutTests` had been skipping that sample with the gap in its skip message all along. |
| S-25 | [`32`](32-steady-state-newton.md) | **A uniform seed field is singular in every variable that multiplies a difference, not only in flow** | `S-21` established that a zero-flow seed kills `∂(R·ṁ|ṁ|)/∂ṁ`. Two more of the same shape turned up the moment promoted parameters became real columns, and neither is about flow. **Pressure:** a valve's law is `ṁ = Kv·f(x)·√(Δp·ρ)`, so a uniform pressure field makes its derivative in `Kv` *and* in `position` exactly zero — the cooling loop's promoted `3WV.position` was measured as a column of zeros against a system whose every other column was O(1). **Enthalpy:** a node's energy balance carries `ṁ(h_arriving − h_own)`, so a uniform enthalpy field makes its derivative in *flow* zero, and the simple loop came out rank 11 of 12 with the null direction running along `PU1.head` and the branch flow together. Both were argued away before they were measured: "pressure enters linearly so Newton reaches the field in one step" is true of the pressure block and says nothing about the columns that multiply `√Δp`. | `SolutionSeed` steps pressure and temperature along each branch, wrapped into a band of five so a long branch cannot walk out of the fluid's validated range. The property is stated once, in the seed's own terms: **the seed must make every difference a residual reads non-zero, not merely every value.** |
| S-22 | [`31`](31-solver-architecture.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **A stated boundary flow was counted as needing no unknown, and then entered no equation either** | `WellPosedness` leaves a node with a stated `flow` out of `FluxNodes` because it declares no unknown — correct for the count, and its comment says so in as many words. `EquationSystem` then assembled its flux terms from that same list, so the stated value reached no mass balance and no energy balance. The count stayed square throughout, which is why nothing caught it: `m4-storage-header`, whose *every* boundary is a stated flow, had a circuit at rest as an exact solution and would have reported convergence for it. The same shape as `S-16` — an error that cancels inside one number. | Found while writing the seed, because a seed whose whole claim is mass consistency has to know which terms are in the balance. The flux list now carries a column *or* a constant: a `FluxNodes` entry reads its unknown, and a stated flow contributes its own signed magnitude (negative for a `return`, `D-64`) to both balances. `AtRestEveryBalanceIsZeroExceptWhereAComponentAddsEnergy` was documenting the defect and now exempts exactly those nodes, with `AStatedBoundaryFlowReachesTheBalanceItNames` asserting the value it steps over. |
| S-21 | [`32`](32-steady-state-newton.md), [`24`](../20-core-domain/24-auto-sizing.md) | **Newton had no seed to start from, and the obvious hand-made ones were singular rather than merely poor** | `32`'s initial-guess table says the first solve starts from sizing, and sizing was `P3.7`, so `P3.6b` had nothing to call. Both stand-ins produced a **singular Jacobian**: zero flow kills `∂(R·ṁ|ṁ|)/∂ṁ` outright, and one sign for every branch leaves some node with every port an inflow, whose own enthalpy then enters no equation. Measured on the cooling loop, `N2.h` came back with a maximum Jacobian entry of `1e-6` against `1` everywhere else. | `SolutionSeed` builds a field that **satisfies every mass balance outright**, which is the property the two hacks were approximating. The branch graph is spanned by a forest; each chord takes its own flow estimate, each boundary flux is chosen so a hydraulic component's fluxes sum to zero, and the tree branches are then solved leaves-inward, each one being whatever closes its vertex. The last vertex closes identically, which is the construction's whole claim and is asserted on all seven samples. The `S-21` note that the seed must be *roughly* mass-consistent turned out to understate it: making it **exactly** so is easier than approximating it, because the cycle basis `23` already computes is precisely the freedom available. `NewtonSolverTests` dropped its alternating-sign helper for the real seed. |
| S-16 | [`31`](31-solver-architecture.md), [`22`](../20-core-domain/22-component-model.md) | **`DeclareUnknowns` was on the component interface and consumed by nothing, so a control volume's own state was modelled nowhere** | Exactly one kind used it: a tank declares one enthalpy unknown and one energy balance, `EnergyBalances` is `Nodes.Length`, and there was no term for a component-owned scalar — so the table counted neither. They cancelled, `Excess` read zero, and `m4-storage-header` reported square while being a row and a column short. Found by holding the assembled row count against the table rather than the table against itself. | Recorded as deferrable and then forced: `Tank.EvaluateResiduals` reads `context.Unknowns[EnthalpyIndex]` at its first line, so the first attempt to evaluate the storage header threw `IndexOutOfRangeException` — a pipeline stage failing on a shipped sample, which is the one thing none of them may do. `D-74` settles it. `CountingTable` gains `ComponentUnknowns` (named, because the layout must allocate them) and `ControlVolumeBalances`; `SystemLayout` allocates them straight after the node enthalpies so the energy block stays contiguous; `EquationSystem` slices them out of the iterate, which costs nothing because they are adjacent by construction. `RowAllowance.ControlVolumeRows` is deleted — and it failed the moment the fix landed, exactly as its own remarks promised. |
| S-2 | [`31`](31-solver-architecture.md), [`32`](32-steady-state-newton.md) | **The iteration loop had to pre-evaluate every port state and neither document said so** | A residual may not call the property backend at all: one water property is ~63 µs and a seven-property state ~204 µs, and a residual runs N+1 times per Newton iteration, so a 20-unknown circuit with ten components fixing one state each is ~43 ms per iteration — past `07`'s whole interactive budget before any linear algebra. `P3.3` closed the component half by putting evaluated properties in `SolveContext.Ports`; what stayed open was that nothing filled them. | `EquationSystem` fills them, once per iterate, in the one method in the solve that touches the backend. The Jacobian goes further: `TryEvaluateScaledAt` re-fixes only the node a perturbed unknown can change, and a flow, a flux, a promoted parameter or a control volume's own enthalpy changes none at all — so most columns cost no property call whatever. On a 200-component model the naive full refresh per column is ~N² property calls where N of them changed anything, which is roughly a second against roughly fifty milliseconds per iteration. Built in rather than retrofitted, because the shape of the saving decides the shape of the cache. |
| S-3 | [`36`](36-numerics-and-convergence.md) | **Residual scaling was specified and unimplementable where it was asserted** | `22`'s invariant 5 asks every residual to be comparable across component kinds, and a component evaluated alone has nothing to be comparable to — so `P3.3` could not assert it and moved it here (`C-2`). It is the one `36` names as costing a week of "the solver does not converge" when skipped. | `ResidualScales` is `36`'s `DF` diagonal, built from the unit already on each row for the diagnostic's sake — the same fact used twice rather than a coincidence. `NewtonSolver` solves scaled and never solves the raw system. Power is derived rather than tabulated: an energy balance is `Σ ṁh`, so its scale is a flow scale times an enthalpy scale, both already in the table, and a fourth constant would be a fourth thing to keep consistent with three that determine it. |
| S-7 | [`31`](31-solver-architecture.md) | **The residual-row-to-component mapping was built and consumed by nothing** | `31` makes the point that the mapping exists only at the component layer — "HX1 energy balance off by 4.2 kW" is actionable and "residual[17] = 4200" is not — and that if that layer does not carry it, no later one can. `P3.3` implemented `DeclareEquations` and nothing read it. | `EquationLayout` assigns every row its index and owner, `ComponentRows.Row` scatters a component's residuals into them, and `SolveResult.WorstResiduals` reports by component and equation with both the raw miss and its unit. Every `FS30xx` message names a component rather than a row index, which is what the mapping was for. |
| S-9 | [`31`](31-solver-architecture.md) | **Nothing consumed the counting table, so its terms were asserted against a document rather than against an assembly** | The table said the cooling loop has 20 unknowns and 20 equations, checked against `23`'s hand-tabulated table. When the real system is assembled those are the same two numbers computed a second way — and if they disagree, one is wrong with nothing to say which. | Both layouts are built to consume it and hold their totals against it on every sample. It has earned the argument four times over: `S-11` (an exchanger and its node both claiming one relation), `S-14a` (a valve writing a law for an unconnected port), `S-15` (a row the table counted and could not locate), and `S-16` (a term missing from *both* sides, which cancelled and hid). None of the four would have failed a test that checked the table against itself. |
| S-13 | [`36`](36-numerics-and-convergence.md) | **A mass balance belongs to a node while flow is scaled per branch, and nothing said which scale a node's row takes** | The residual is a sum of the flows incident on the node, so its magnitude is set by the largest of them; scaling by the smallest would inflate a residual that can never get that small and the row would never converge. It matters exactly where per-branch scaling was introduced to help — a node joining a 10 kg/s primary to a 0.05 kg/s bypass — so the two rules interact on the one topology they were both written for. | `ResidualScales` takes the largest incident branch scale and `36` now carries the rule as a keyed row rather than leaving it to be rediscovered. |
| S-15 | [`31`](31-solver-architecture.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **The counting table counted the ideal zero-drop links and could not say which nodes they joined** | `PressureRelations` aggregates three things, and two of them are some component's own equation reaching the assembler through `DeclareEquations`. The third is not: `D-25` makes a bare `A - B` between two nodes an ideal link, there is nothing in the branch's path to declare `p_A = p_B`, and the assembler has to write that row itself. A count cannot say between which nodes, so the equation layout would have had to walk the branches a second time — the drift the table's own `FluxNodes` note warns about, one term further down. | `WellPosedness.Relations` now collects the pairs while it counts them, and `CountingTable.IdealLinks` names them beside `FluxNodes`, `PressureNodes`, `DatumComponents` and `LevelComponents`. `EquationLayout` writes one row per link from that array, and `EquationRowReconciliationTests` deleted its own walk in favour of `counting.IdealLinks.Length` — which is the point: the second implementation existed, in a test, and naming the links removed it. |
| S-14a | [`22`](../20-core-domain/22-component-model.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **A three-way valve wired as a two-way still wrote a Kv law for the port nothing was connected to** | The registry has always made the bypass port optional --- spelt `c` then, `b` since `D-85` --- and the page has always said leaving it open is how a two-way valve is written; `ThreeWayValve` declared three equations regardless, and its residual read `Ports[2]` for a port with no node. `m2-distribution-header` and `m1-syntax-tour` hold two each, so both reported square while the system they would assemble was over-specified by two — invisible, because nothing assembled yet. Lowering now counts every component's connections and the qualified port names, and the factory hands the valve its wiring: with the bypass open it is a pass-through with two ports, one flow group, one Kv law and no mass balance of its own, and its mode reads `two_way`. The count is decided by topology and never by a solved value, exactly as an exchanger's mode is. `EquationRowReconciliationTests` now holds the declared pressure rows against `Relations()` on every sample, so the next kind that under-declares fails a test rather than a milestone. |
| S-11 | [`22`](../20-core-domain/22-component-model.md), [`23`](../20-core-domain/23-topology-and-graph.md), [`31`](31-solver-architecture.md) | **The rows the components declared and the rows the counting table counted disagreed, by exactly one per heat exchanger** | Found by `P3.6a` doing what `S-9` asks — comparing the two counts before writing an assembler that would have had to pick one. `WellPosedness.Relations` counts a crossed component once as a *pressure* relation and `EnergyBalances` is `Nodes.Length` unconditionally, so nothing counted the exchanger's duty row, and the excess equalled the number of non-node components declaring an energy row on every sample. Not a miscount but a contradiction about ownership. `D-69` settles it: energy is a flux a component contributes, the counting table is unchanged, and the excess disappears because the exchanger stops owning a row. Recorded from the component side as `C-40`. |
| S-6 | [`36`](36-numerics-and-convergence.md) | **The smoothing constants were `36`'s numbers living in tier 20's code, with no shared source** | `valve.dp_regularization` and `upwind.smoothing_band` had been transcribed by hand into two unrelated component files, and a change to the table reached neither. `P3.6a` made the table itself a type — `Solvers.Tolerances`, all twenty-five rows — and `ValveLaw.RegularizationDrop`, `Smoothing.UpwindBand` and `Quantity.DefaultRelativeTolerance` now read from it. The direction is the point: the table is the source and a component is one of its consumers, so a component reaches into `Core.Solvers` rather than the reverse. `ToleranceTableTests` parses `36`'s own markdown and asserts **both** directions — every constant carries its documented value, and every documented row is implemented — because the failure that happened was the second kind: a value transcribed correctly, and then the document moved on without it. |
| S-10 | [`23`](../20-core-domain/23-topology-and-graph.md), [`31`](31-solver-architecture.md) | **`CircuitGraph` did not carry the port-level adjacency the assembler needs** | Lowering computed `_peerElement`/`_peerPort` to decompose the branches at all, then discarded them. `Branch.Path` records the order a walk crossed the elements and not *which port* faced which — and a two-port pass-through walked from the other end is entered at its outlet, so the direction is not recoverable from the order. A component's residual reads `Ports[i]` as the state at the node port `i` touches, so no `SolveContext` can be built without it. `P3.6a` publishes it as `CircuitGraph.Adjacency`, and a test walks every branch through the table and reproduces `Decompose`'s own `Path` exactly. Recorded rather than done quietly because it is a tier-20 type changed from a tier-30 package. |
| S-8 | [`36`](36-numerics-and-convergence.md), [`23`](../20-core-domain/23-topology-and-graph.md) | **`FS2211` (under-specified) appeared to be unreachable from any script**, so half of the well-posedness count was untested against real input | It was reachable, and the reason nothing reached it was a missing row rather than a dead code path: the count had no **enthalpy datum** (`C-30`, `D-65`). A closed, steady circuit coupled to nothing has one enthalpy its own relations cannot determine, and a script that states no temperature anywhere in such a circuit is genuinely under-specified by exactly one. `P3.4c` counts it, and `Understated` now names a temperature ahead of a pressure — the one candidate the graph could not have picked for itself, since a datum covers the pressure case before it can reach this code. The original entry guessed at the two possibilities correctly and picked neither: the equation set *was* missing a row, and `FS2211` is not dead. |

## Observations

**The two counts had to be compared before either could be trusted, and comparing them is what found
`S-11`.** `S-9` asks the assembler to *consume* the counting table rather than re-derive it, and the
cheapest possible version of that — summing `DeclareEquations` over the graph and holding it against
`CountingTable.Equations` on every sample — cost one throwaway test and found a contradiction that had
survived four packages. Neither number was checkable alone: the table matched `23`'s hand-tabulated
worked example exactly, and the declarations matched `22`'s component list exactly. It is the
disagreement that carries the information, which is the whole argument for building the assembler
against the table instead of beside it.

**The counting table's own note said what to do, one term before it was needed.** `FluxNodes` carries a
paragraph explaining why it is *named* rather than counted — "an assembler has to declare these unknowns
and a count cannot say which nodes get one" — and `S-15` is the identical argument one row down, on the
equation side, found only when an assembler actually tried to write the row. Four terms had already been
converted from counts to named arrays for exactly this reason; the fifth was missed because nothing had
yet needed it. The lesson is cheap to state and was not: **a table meant to be consumed should name every
term whose consumer cannot re-derive it**, and the test of that is not whether a count is enough for the
diagnostic, but whether the assembler can write the row from it.

**A count that is short on both sides is worse than a count that is wrong.** `S-16` is the tank
declaring an unknown and a row the table models on neither side. `Excess` reads zero, `FS2210` and
`FS2211` stay quiet, and every reconciliation that compares two totals agrees — because both totals are
short by one. The reconciliation this session added is what caught it, and only because it holds the
*assembled row count* against the table rather than holding the table against itself: an error that
cancels inside one number is invisible until something outside that number depends on it.

**A defect found this way names both sides and neither.** `S-11` was recorded against `22`, `23` and
`31` together, because the exchanger's row and the node's row are each defensible in isolation and only
the pair is wrong. A finding filed against one document would have been fixed there, and the fix would
have been the direction-dependent one that does not survive a flow reversal.


**The nodal formulation's consequences are now structural, not just stated.** `23` argues that loop
closure is a property of the unknowns rather than an equation, and `P3.4a` built the cycle basis as
data with no equation attached: `CircuitLoop` is read by layout and by `FS2214`, and by nothing in the
system. The failure mode `23` warns about — 21 equations against 20 unknowns on this tree's own
reference circuit — is now not reachable by accident, because there is no code path from a loop to a
row.

**Branch-owned flow is implemented, and the unknown count follows from it.** A branch with three pipes
and a valve in series is one flow unknown and four pressure relations, not four flows and three
identities. `23` calls this the most consequential structural decision in tier 20 for solver
performance; it is now a fact about the graph rather than an intention, and `32` assembles against it.

**Node ordering is part of the contract, not an implementation detail.** `23`'s invariant 6 ties the
renderer's placement memory *and* the solver's variable ordering to lowering's deterministic order, and
`P3.4a` asserts it by lowering the same model twice and comparing a canonical rendering. A solver that
re-sorts variables for its own reasons — by degree, for fill-in — breaks the renderer, which is not an
obvious coupling from inside tier 30.

**The energy block spans what the pressure block does not.** A rated exchanger produces more than one
hydraulic connected component in one graph, and the energy system runs over every node in the model.
`23` states the consequence for tier 30 plainly and it is worth repeating where a solver author will
read it: the hydraulic blocks are genuinely independent and could be factorised separately, the energy
block is not, and a segregated solver that split by circuit would iterate against a stale duty. Any
block-decomposition experiment in `36` has to preserve that coupling.

**A residual that returns `NaN` at zero flow poisons the whole Newton step, and the laminar branch is
where it comes from.** `f = 64/Re` diverges as velocity goes to zero while the term it multiplies goes
to zero with it — literally `∞ × 0`. Substituting Re gives `32·μ·L·v/D²`, which is linear in velocity,
exactly zero at rest and has a finite derivative there. `36`'s "regularised where the physics is not"
covers this in spirit; it does not name the case, and zero flow is the initial guess.

**Every residual function in `P3.3` is allocation-free and was asserted so in the package that wrote
them.** `08` said to write that test with the components rather than retrofit it across six types, and
that was right for a reason worth keeping: the first version of the test measured 21 600 bytes, all of
it the test's own collection expressions inside the measured region. Retrofitted later, that number
would have been read as a real allocation in the component.

**The energy block's rank deficiency is tier 30's to respect, and no synthetic row fixes it.** A
closed, steady, uncoupled circuit's energy equations determine every enthalpy *difference* and no
enthalpy, so the assembled Jacobian is singular by one unless the script states a temperature
(`D-65`). `32`'s singular-handling list now names it beside the missing pressure datum, and the two
are not symmetric in what the solver may do about it: a pressure datum can be invented because every
pressure is relative, and a temperature cannot be, because every property call reads the absolute
value. An assembler that "helpfully" pins an enthalpy would return a plausible answer to a question
the user never asked.

**A worked example is a regression test for the case it was written from.** `23`'s counting table
reproduced term for term on the first run, 20 = 20 with both promotions, which read as strong evidence
that the counting pass was right. It was evidence about one open circuit. Both of the count's real
defects — the stated-`flow` flux (`C-26`) and the missing enthalpy level (`C-30`) — are invisible on
that circuit and were found by running the whole sample corpus and by a user asking for a *different*
circuit to be fixed. The sweep over every sample, recorded as a dictionary of outcomes rather than as
a list of exceptions, is what makes the next one visible.

**Well-posedness runs on the graph alone, and that is what makes it testable.** Nothing in the pass
reaches back into the semantic model; it reads `IComponent.StatedParameters`, which lowering now fills
from the bound symbol. So a solver test can build a graph by hand, ask whether it is square, and get
the same answer the pipeline would — and `23`'s invariant 7 stays true without an exemption.

**Stated-ness is the whole input, and it was being thrown away.** `IComponent.StatedParameters`,
`SizedParameters` and `DefaultParameters` have existed since `P3.3` and were empty on every component
lowering built. Nothing downstream can recover the distinction from the value: a stated `position=1`
and the registry's own default are the same number and mean opposite things to the count. Filling them
was the precondition for promotion existing at all, and it is `D-02` made observable rather than a
convenience.

**A dropped component leaves a branch ending nowhere, and the cycle basis threw on it.** A pipe whose
bore no catalogue resolves is not built and its connections go with it, so `Decompose`'s "the branch
ends where the graph does" path produces an end that is not a vertex — and `CycleBasis` indexed it
directly. `KeyNotFoundException` on a script that is merely incomplete, which no stage may do. Found by
a test written to reach `FS2211`, which is the second time this package a test written for one reason
found something else.

**A deliberately self-deleting allowance worked, and it is worth copying.** `RowAllowance` existed to
subtract the one row `S-14b` left undeclared, and its own remarks said why it was computed from the
graph rather than listed by sample name: "it goes to zero the day the equation lands, and every test
that subtracts it starts failing until this file is removed. That failure is the reminder." That is
exactly what happened -- the side-2 row landed, three reconciliations went from passing to failing in
the same build, and the fix was to delete the file. A hard-coded skip list would have gone on excusing
a defect that no longer existed. Worth doing again wherever a test has to tolerate a known gap.

**The substation's failure mode moved from structural to numerical, which is progress that looks like
none.** Before: "assembly gave 22 equations for 23 unknowns" -- the circuit could not be handed to a
solver in any form. After: 23/23, nine Newton iterations, `NonFinite`. The sample still does not solve
and the corpus still reports it as failing, so a summary counting converged samples sees no change.
What changed is that the remaining problem is now the same *kind* of problem as `m2-cooling-loop`'s
and `m2-distribution-header`'s, rather than a hole in the component model.

**A more correct seed made a failure happen sooner, and that was the useful part.** Seeding promoted
parameters from their components rather than from zero changed `m2-cooling-loop` from `Singular` after
three iterations to `Singular` at iteration zero. By outcome that is no better -- the sample failed
before and fails now. By diagnosis it is much better: the old failure looked like step control losing
its way after a plausible start, and the new one says plainly that the column is dead at the seed and
no step was ever possible. The temptation with a change like this is to judge it by the pass count,
where it scores nothing.

**The seed's own remarks named the defect and nobody read them as a defect.** The line was "a promoted
parameter carries no unit yet and falls to zero, which is honest and is the thing the outer loop
replaces when promotion becomes live." Promotion went live in `P3.7b` and nothing came back to this.
A placeholder that documents itself accurately is still a placeholder, and the sentence that would
have caught it is the one that says *when* it stops being acceptable -- which this one had. Worth a
convention: a comment naming the package that obsoletes it should be greppable, because it is the
package that lands, not the file that changes.

**One symptom, two defects, and the second was invisible until the first was fixed.** `S-26` was
recorded as "a promoted parameter has no bounds" on the evidence of a position reaching 180. That was
half the story. The other half -- a clamp plus forward differences making the column identically zero
-- was the reason the solve stopped *before* the bound could matter, and it made the recorded fix
inapplicable: a box constraint clamps the iterate onto a bound, which is precisely where the
derivative did not exist. Fixing them in the wrong order would have looked like the fix not working.
Worth remembering the shape: when a defect's recorded remedy would land the system exactly at the
place the symptom is measured, suspect a second defect underneath.

**`m2-distribution-header` is still `Singular` at iteration zero, and it is not this defect.** All
three of its pumps report a promoted head of **0**, because a pump built with nothing stated genuinely
holds a shut-off head of zero and `Resolvable` reports it honestly. No head means no pressure
difference, and a valve's `sqrt(dp)` law has a derivative in `position` that vanishes at `dp = 0` --
the seed's own remarks describe exactly this failure (`S-25`), one component over. So the header needs
a pump whose seeded head is non-zero, which is a different fix from either half of `S-26` and is
recorded here so the next person does not read it as the same one.

**A bound that binds is a diagnostic, not a repair, and the two look the same from outside.** `FS3008`
now reports `3WV.position` held at 1 on the cooling loop, which reads as "this circuit is asking for
more than the valve can give". That would be a good answer if it were true, and `01`'s own figures say
it is not -- the duty is reachable. So the projection turned a wrong *number* into a plausible
*explanation*, which is a subtler failure than the one it replaced. Worth stating as a rule for the
rest of the bounds work: a parameter pinned at a bound on a circuit whose answer is known to exist is
a solver defect wearing a user-facing warning, and the warning should not be trusted as evidence that
the model is over-specified until the circuit is known to converge without it.

**The data a fix needs is often already plumbed.** `EquationSystem.Project` needed the bounds of every
promoted column and took no new constructor parameter to get them: `_promoted` already carried
`(element, slot, column)` because the assembler has to write parameter values back before evaluating,
and `slot` indexes the very record the bounds now live on. The instinct on reading `S-26`'s recorded
fix was that this would mean threading bounds through a 17-parameter constructor. It was three lines.
Worth checking what a structure already carries before widening it.

**"Side 2 is not modelled" was three defects, and two of them were conservation bugs wearing a
deferral.** `S-14b` (no momentum row), `S-31` (no energy withdrawal) and `S-32` (no implied flow) all
lived under one deferral to `P4.1`, and each carried a code comment saying the coupled model would
supply it. Two of the three were not features at all: a component two branches cross **must** declare
two pressure relations or nothing can assemble it, and a component that moves heat between two streams
**must** take it out of one, or it invents energy. Only the third -- how much duty crosses when the
script does not say -- is genuinely the coupled model. Worth the general form: a deferral that covers
a conservation law is mis-scoped, and the tell is that the deferred behaviour is not optional in any
model, however simple.

**The substation is not an M2a demo, and thinking it was distorted the priority.** `05`'s exit
criteria name three scripts -- the cooling loop, the simple loop and the distribution header. The
substation is the coupled-thermal sample and belongs to M2b. It was worth the two conservation fixes,
which were real defects reachable from any script that wires a second side, but the remaining work on
it is `P4.1`'s and should not be pulled forward again. **The M2a blockers are `S-30` (the cooling
loop) and the distribution header's zero-head seed**, and neither has anything to do with exchangers.

**A correlation across five samples looked like a cause and was not.** Every sample containing a
three-way valve failed and every sample without one converged, which is as clean as a corpus that size
can be, and both remaining M2a blockers had one. It was worth about twenty minutes to test rather than
act on: the cooling loop is full rank at its seed, and the header's null direction names three pumps
and no valve. The distribution header is also the only multi-circuit script, so the three-way valve was
confounded with circuit attachment from the start -- five samples cannot separate two features that
always appear together. **The corpus is too small for correlation to mean anything**, and the null
vector of a singular Jacobian is a direct measurement that costs one elimination.

**Counting squareness and having full rank are different properties, and only the first is checked.**
`WellPosedness` counts unknowns against equations; nothing checks that the equations are independent.
On `m2-distribution-header` the two disagree in *both* directions -- square by count and deficient by
rank as shipped, over-specified by count and still deficient by rank once a head is stated. A counting
pass cannot see a linear dependence among rows, so a script can pass every pre-solve check and hand the
solver a matrix with no inverse. `S-28`'s guard was built for the case where the two counts differ; this
is the case where they agree and the matrix is still singular, which is the harder half.

**The instrument is now in the repository.** `CorpusStatusTests` pins every sample's termination,
including the failing ones, so no session has to re-measure the corpus before it can prioritise -- and
so a fix that changes a sample's state has to say so in its own diff. Three times this session a stale
claim about which sample was blocked on what was carried forward and acted on before being checked.

**The null direction is a measurement, and it belongs in the product rather than in a probe.** Every
hypothesis rejected while diagnosing `S-33` cost a throwaway test file, a build and a run; the
direction that settled it is four lines of back-substitution the solver can do for itself at the moment
it gives up. Two of the three rejected hypotheses would have been rejected instantly by reading
`FS3009` instead. The general form: when a diagnosis requires instrumenting the solver, the
instrumentation is usually the missing diagnostic.

**Four hypotheses died on this one sample, and each looked better than the last.** The three-way valve
(spurious correlation), the missing recirculation leg wired naively (made it worse), too many free pump
heads (every arrangement is deficient), and the promotion pairing being non-local (it is already local,
and forcing it changed no sample). The pattern in the misses is worth more than any one of them: each
time, a plausible mechanism was inferred from a *partial* measurement -- a correlation, a null vector, a
count -- and the next measurement killed it. What finally held was the variant sweep, because it changed
one thing at a time and every arm came back with the same answer.

**A change that alters no observable behaviour is not a safe change, it is an unjustified one.**
Restricting flow constraints to branch-local pumps is defensible on physical grounds and matches what
`Promote`'s own remark claims. It was still reverted: measured against the whole corpus it changed no
sample's promotions and moved `m1-syntax-tour`'s excess from 4 to 5. A refactor with no demonstrated
defect behind it and one behaviour change against it is a liability, however good the argument for it
reads -- the same call as the `MinimumOpening` leakage floor earlier in this phase.

**The counting pass refuses every arrangement that might work.** Removing the plant pump, and adding a
datum on the far side of the cut, are both refused as over-specified before the solver sees them. So the
two models do not merely disagree on the shipped script (`S-33`): counting actively blocks the search for
an arrangement that satisfies both. Whatever fixes this has to move counting, and no experiment on the
sample can proceed until it does.

**Substituting the answer is the cheapest way to find out whether the seed is the problem.** The cooling
loop had resisted diagnosis through `S-26a`, `S-26b` and `S-30`; five hand-set enthalpies settled it in one
run, and the numbers that came back were `01`'s to three significant figures. It is worth reaching for
earlier: a solver that reaches the right answer from a hand-built start and not from its own has a seed
defect, and no amount of staring at the iteration trace says so as directly.

**Two fixes were built and both were reverted, which is the process working rather than failing.**
`CorpusStatusTests` caught each one within a single run -- the first turned the cooling loop from
`Singular` to `NonFinite`, the second moved two samples' failures earlier while fixing a third. Neither
would have been visible from the sample that improved. The instrument added on the same day it was needed
paid for itself twice.

**A better constant cannot fix a wrong shape.** Both attempts kept `datum - NominalRise x steps` and tried
to improve the `datum`. The measurement that ends that line of attack is `3WV__P1` at 0.20 &deg;C from a
mean of 25.3: the walk's own magnitude carries a node out of range regardless of where it starts. Anchored
propagation is the shape; choosing a global level better is not.

**A rated `in`/`out` is a design condition, not a boundary, and that distinction decides where it may
be used.** The script saying an exchanger is rated 20/50 does not assert that the nodes at its ports
are at 20 and 50: in `m2-cooling-loop` those temperatures are *produced* by the mixing valve
recirculating hot return water, and how far the fluid actually rises depends on the flow the pump
delivers. Using them to place node temperatures is nevertheless right, because **a seed is not a
claim** -- Newton moves off it freely, so a design point cannot make an unreachable design look
reachable. The line to hold is that this informs the seed and never a constraint row, which the
counting pass owns. Getting that backwards in either direction is a real defect: as a constraint it
would assert something the script did not say, and refusing it as a seed throws away the only prior
anyone has about where the circuit is meant to sit.

**Path order is not orientation, and assuming it is laid the rated temperatures on backwards.** The
first implementation read a component's neighbours out of `branch.Path`, which is the order the walk
crossed the branch rather than the component's own direction. Measured, that put 46 &deg;C on the node
before `HE1` and 18 &deg;C on the node after it -- a seed asserting the exchanger cooled the water, and
one that still *looked* plausible because both numbers were in range. The port map has no such
ambiguity: a parameter is named after the port it describes. `ARatedInletLandsOnTheNodeAtTheInletPort`
pins it.

**Three attempts, and only the third was a fix.** Placing the ports alone made the sample worse
(`NonFinite` at 1). Placing them and averaging the global datum made one sample better and two worse.
Propagating from anchors fixed it. The two failures were not wasted: each eliminated a shape, and the
second is what proved a better *constant* could never work, because `3WV__P1` reached 0.20 &deg;C from
a mean of 25.3 -- the walk's own magnitude carries a node out of range regardless of where it starts.

**A fixture can hide a defect by being smaller than a real installation.** `m2-cooling-loop`'s bypass leg
is an empty connection, which is not what anyone builds -- the guidance for this arrangement is unanimous
that the bypass carries a balancing resistance matched to the path it bypasses, or the circuit
short-circuits (`C-63`). The empty leg was not merely unrealistic: it was the one shape in which the seed
worked. Every attempt to add the missing component made the sample *worse*, and it took measuring a plain
`pipe` in that leg -- which cannot be a sizing, authority or Kv problem -- to show that the component was
never the cause. **Two variants that fail identically are worth more than one that fails**, because
whatever they share is the mechanism and whatever they do not share is eliminated.

**The seed's own documentation stated the property it did not have.** `Steps` said in as many words that
"what this exists to guarantee is that adjacent nodes differ", and `Band` said its wrap gives "every pair
of adjacent nodes different values, which is the only property the seed needs from this". Both were
written against `S-25`, both are the right requirement, and neither was true -- because "adjacent" was
read as "consecutive along a branch" when a junction element makes nodes on *different* branches adjacent
too. A doc comment asserting an invariant is not a test of it, and this one had been believed for three
defects running.

**The column null direction answers a question nobody asked.** `FS3009` says which unknowns move
together, which reads like a cause and is not one: in a square system deficient by one there is a
left null vector and a right one, and only the left says which equation is redundant. The right one is
whatever combination of columns the elimination's pivoting happens to leave over, and on the header it
picked pumps every time --- so `S-33` tested a pump arrangement, then `S-34` tested three more, and all
four were deficient by exactly one because the deficiency was never about pumps. The measurement that
ended it took the transpose and cost forty lines in a probe. **A diagnostic that names participants
without naming the equation invites exactly this**, and it did.

**Five things eliminated is worth more than one thing suspected.** The header defect now has a minimal
reproduction and five ruled-out causes, each by a measurement rather than an argument: not the seed
(identical direction at six valve positions), not the enthalpy datum, not the missing resistance, not
the second subcircuit, not the closure. None of that was available while the search was following
`FS3009` toward the pumps.

**Counting is right about the unpumped branch, and it is worth saying so.** Two of the four pump
arrangements measured were *refused* rather than solved, and both refusals are correct engineering.
A consumer with **no pump of its own** --- flow regulated only by its three-way valve, which is a real
convention --- comes back `FS2210`, over-specified by one per unpumped branch: "remove `HE_RAD.out`".
That is the tool saying that without a pump the branch's flow is set by the network rather than by the
branch, so the coil cannot be asked to hold *both* its inlet and its outlet temperature. It is the same
degree of freedom the pump would have supplied. After several entries here about counting disagreeing
with the Jacobian, it is worth recording a case where counting says something true and useful that the
rank test could not.

**Three pumps were never the problem, and a fourth would be.** `S-33` and `S-34` measured four pump
arrangements on the shipped header and found each deficient by exactly one, which read as evidence
against the pumps. It was not: the corrected circuit carries the **same three pumps** and is full rank,
because what it gained was the *source's own loop* (`F-21`) rather than a different pump count. But
adding a **fourth** pump in series, which is what a header booster is, does create a real deficiency
(`S-37`). Both facts are about pumps and they point opposite ways, which is why neither could be
settled by counting them.

**The bootstrap `kv` makes every unsized three-way valve look like a different defect.** A valve no rule
has sized wears `kv = 630`, and a Kv law at 630 demands about **78 kg/s** on a plant that moves 0.93 ---
so its residual is two orders of magnitude above every other equation in the system, and whatever the
circuit's real problem is, the solver never gets near it. Five header arrangements were measured before
this was noticed, all reporting norms around 95 and worst residuals that were *always* the three valve
laws. Once `kv` is stated by hand the norm falls to about 1.2 and the actual defect (`S-38`) becomes
visible underneath. **The bootstrap value is documented as chosen "to disturb the first pass as little
as possible", and on a three-way valve it does the opposite**; `C-63`'s sizing pass was supposed to make
it unreachable, and it declines on every header (`C-66`).

**Two hypotheses died to one measurement each, and both looked stronger than the truth.** A promoted
pump head is seeded at 0, which is *also* its `Minimum`, so `PU_RAD.head` pins there and never leaves ---
`FS3008` says so out loud. That is a real property of the seed and it is not the header's problem:
forcing the head seed to 1, 3, 6 or 12 m changes the outcome not at all, and `m2-simple-loop` and
`m2-cooling-loop` are completely insensitive to it. Likewise a 128x sweep of the three valve `kv` values
converges nowhere. Both were plausible, both were cheap to test, and testing them cost less than the one
round of reasoning that would have argued for either.

**The row direction was right the second time, and the first reading cost a commit.** `S-38` was filed
leading with `FS3009` --- "the pump's head and the valve's position control the same thing" --- in an
entry that cites `S-36`, which exists in this repository *specifically* because the column null direction
names wherever partial pivoting landed rather than the cause. Reading the row direction instead named
mass balances at weight 1, which is the global conservation identity, which led to `S-39` in one step.
The discipline is not hard and it is not new; what makes it fail is that the column reading is always
plausible, because those unknowns really are coupled --- they are simply not *the* coupling.

**Two deficiencies in one circuit look exactly like one deficiency, and each hides the other's evidence.**
The header carried `S-39` (a datum on a junction) and `S-38` (two consumers sharing a differential) at
once. Every measurement taken before they were separated reported one combined direction, and the
combined direction pointed at neither. What separated them was deleting half the plant: the
single-consumer fixture isolates `S-39`, and fixing `S-39` there leaves the rank intact, which is what
proves `S-38` needs two. **Shrinking the fixture was worth more than any diagnostic**, and it should have
come before the jitter sweep rather than after it.

**A jitter sweep is the cheapest way to tell a seed defect from a structural one, and it should be
routine.** `S-25` and `S-35` were both seeds producing singular Jacobians, so the prior was reasonable ---
and wrong here. Evaluating the same Jacobian at 1 %, 5 %, 20 % and 50 % random perturbation of the seed
takes about fifteen lines against `TryEvaluateResiduals` and answers the question outright: a deficiency
that survives 50 % jitter is in the equations, not in where they were evaluated.

**An argument that reaches a correct conclusion from a false premise still has to be fixed.** `S-12`
was the clearest case: per-branch flow scaling was right, implemented, and shipped, and the reason `36`
gave for it was arithmetic that this project's own tolerance table contradicts by three orders. Nothing
was broken and no test could fail. What it costs is the next person to touch scaling, who checks the
number, finds it wrong, and now doubts the design it was defending.

**`S-5` is the same failure one step earlier: a specification that every implementer will satisfy
incorrectly on the first try.** "Continuous in value and first derivative" is a correct requirement and
an untestable instruction --- the obvious test measures the derivative's sweep across the band rather
than the property at the join, and fails on correct code. A document that states a property whose
natural test is wrong should carry the working test, not just the property.

**Closing `S-36` needed `S-39` to exist, and that is the normal shape rather than an exception.** The
entry measured the row null direction, named it exactly (*one mass balance against every energy
balance*), eliminated five causes, and could not say what the fix was. It stayed open through two more
sessions until the datum-on-a-junction rule was found. **An entry that has isolated a defect precisely
and cannot name its cause is doing its job**, and closing it early to shorten the list would have thrown
away the measurement that eventually identified `S-39`.

**One rule, three implementations, and the build is green after changing any one of them.** `D-86`'s
flux rule lives in `HydraulicComponent.HasUnknownFlux` (which decides the mass-balance drop),
`WellPosedness.Count` (which decides the flux unknown) and `SolutionSeed.Free` (which decides where the
seed may push flux). `SolutionSeed`'s copy carries a comment saying it is "restated rather than shared"
with "a test holding the two lists against each other" --- and the inconsistency still got through,
because the guard compares the seed against `WellPosedness` and neither against `HydraulicComponent`.
Each partial change produced a *plausible* failure a long way from the edit: an equation-short counting
table advising a pressure on a mid-branch node, then a seed 0.167 kg/s out of balance at one node of one
sample. **Neither failure named the rule that had been changed.**

**A change proposed as "one clause" touched three predicates, four test fixtures and two samples.** That
gap between estimate and reality is worth recording on its own. The estimate came from reading the
predicate; the reality came from running the suite, which found every site in three iterations. Nothing
about the design was wrong --- `D-86` is still the right decision --- but "one clause" was a claim about
code shape made without looking for duplicates of it.

**`S-40` closed without its own fix, and the cause recorded against it was wrong.** The entry guessed
that `NullDirection.Of`'s significance cut was calibrated for column scales and mis-applied to rows. It
was not: the instrument was being handed a matrix built from a mis-modelled circuit, and it started
working the moment the circuit was modelled correctly. **A guessed cause in an open entry is a liability**
--- the next reader would have spent the afternoon in the elimination.

**`S-38`'s fix was validated by substitution before it was built, and the build still failed.** Stating
both pump heads and dropping both `out=` statements is what removing the constraint/promotion pair does
by hand, and it took the header from `Singular` to full rank --- strong evidence for the design, gathered
in one run. The implementation then did not reproduce it: `Promote` deferred the constraint and `Count`
received a list without it, and `CountingTable.Constraints.Length` came back unchanged anyway. **A
validated design and a working change are different things**, and the gap here is in the counting
table's accounting rather than in the physics. Worth reading `Count` end to end before trying again,
rather than editing `Promote` a third time.

**Twelve throwaway probes were a tooling gap, not twelve investigations.** Every question asked of the
header between `C-66` and `S-38` --- what is the counting table, which promotion answers which
constraint, what does the seed hold, which rows are dependent, what did the sizing rules choose ---
was answered by writing a test, printing something, reading it and deleting it. `SolveExplanation`
answers all of them in one call, and on its first run reported something none of the probes had: the
header's row null direction is dominated by **energy** balances at weight 1 while the counting table
says `less enthalpy levels 0`. That is `S-39`'s story on the energy side --- a redundancy nothing
removed --- and it is the lead `S-38` should be attacked from. **Build the instrument at the second
probe, not the twelfth.**

**The report's own first bug was a confident wrong verdict.** It printed "NOTHING --- this is what
reports the circuit over-specified" for any constraint no promotion answered. `m2-simple-loop` has
exactly that and counts square: its `in=50` is paid for by the enthalpy level the closed loop drops,
not by an unknown it adds. A diagnostic that states a conclusion the counting table contradicts is
worse than one that states nothing, so the section now prints the arithmetic --- unanswered
constraints against levels dropped --- and leaves the verdict to the count.

**The report was built as a function and every caller re-wrote the same three lines.** `Render` had two
overloads --- one for a finished run, one for a graph --- and picking between them was left to whoever
was calling, so every probe repeated the same `run.IsSuccess ? ... : ...` branch, and the interesting
case is the one where that branch goes the *wrong* way: a circuit refused before the solver is exactly
the one whose report is worth reading. The fix is smaller than the habit it breaks --- one overload
that takes the run and the graph, and `ToString()` on `OuterLoopResult` so anything holding a result
can print one, a debugger watch window included. **A diagnostic that each caller has to assemble is a
diagnostic that gets assembled differently each time**, and the format has to be identical across
models or two reports cannot be read side by side.

`EverySolutionCarriesTheSameReport` now enumerates `samples/` rather than listing it, and asserts all
eight sections on every script. A list covers the samples that existed when it was written; the
guarantee wanted here is over the directory.
