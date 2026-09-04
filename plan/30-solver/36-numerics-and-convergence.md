---
id: 36-numerics-and-convergence
title: Numerics, tolerances, and convergence
tier: 30-solver
status: reviewed
owns: [the tolerance table, scaling policy, regularization thresholds, failure taxonomy, floating-point rules]
depends_on: [31-solver-architecture]
traces_to: [R-11, R-12]
open_questions: 0
last_review_pass: 2
---

# Numerics, tolerances, and convergence

## Purpose

Every other document says "within tolerance" and points here. This is the single table of numerical
constants and the reasoning behind each, plus the rules — scaling, regularization, comparison — that
make those numbers mean anything. Scattering these across the codebase is how a project ends up with
four different definitions of "converged" that disagree by three orders of magnitude.

## Responsibilities

**Owns.** Every numerical tolerance and threshold, the scaling policy, regularization thresholds, the
failure taxonomy, and floating-point comparison rules.

**Explicitly does not own.** The algorithms that use them (`32`, `33`, `35`), test-assertion tolerances
([`62-testing-strategy`](../60-docs-and-devex/62-testing-strategy.md), which cites this document).

## Scaling — the prerequisite

Nothing below means anything without it.

The unknowns span six orders of magnitude: pressure ~1e5 Pa, mass flow ~1e-1 kg/s, enthalpy ~1e5 J/kg.
An unscaled residual norm is the pressure residual and nothing else; a convergence test on it declares
success while the mass balance is off by 10 %.

**Every unknown and every equation is divided by a reference magnitude before the solver sees it.**

| Group | Scale | Derivation |
|---|---|---|
| Pressure | 1e5 Pa | ≈ 1 bar, the natural magnitude of a hydronic circuit |
| Mass flow | **per branch**: max(1e-3, that branch's estimated flow) | From sizing's estimate; the floor stops a zero-flow branch dividing by zero. Per branch, not per circuit — see below |
| Enthalpy | 1e5 J/kg | Typical liquid-water enthalpy span over the working range |
| Temperature (where solved directly) | 1e1 K | A typical ΔT |

Equation scales mirror their unknowns: a mass balance by the flow scale, a pressure-drop equation by
the pressure scale, an energy balance by (flow scale × enthalpy scale).

**Scales are computed once per solve from the sizing estimate, then held fixed.** Rescaling mid-solve
changes what "converged" means between iterations and can make the residual norm appear to jump.

**Mass flow is scaled per branch, not per circuit.** One circuit-wide flow scale is only adequate when
every branch carries a comparable flow. A primary loop at 10 kg/s beside a bypass at 0.05 kg/s shares
one scale, so the bypass's mass balance is divided by 200× its own magnitude and the convergence test
declares success while that balance is off by 10 % of the flow through it. Because ‖·‖∞ is already a
maximum over individually scaled residuals, per-branch scaling needs no change to the convergence test
— only to how the scale vector is built.

## The tolerance table

| Key | Value | Applies to | Reasoning |
|---|---|---|---|
| `newton.residual_tol` | 1e-8 | Scaled ‖F‖∞ | Eight digits on a scaled residual is ~1e-3 Pa and ~2e-9 kg/s at the worked example's scales — far below anything physical, and reachable in 4–5 iterations from a warm start |
| `newton.step_tol` | 1e-10 | Scaled ‖Δx‖∞ | Detects a stall: a step this small changes nothing |
| `newton.max_iterations` | 50 | | 10× a normal solve. Reaching it means something is wrong, not slow |
| `newton.divergence_factor` | 10 | ‖F_k‖ / ‖F_{k−1}‖ | A tenfold increase is divergence, not a line-search excursion |
| `newton.line_search_min` | 1/64 | α | Six halvings; beyond that the step is not the problem |
| `newton.fd_step` | √ε ≈ 1.49e-8 | Relative | Standard forward-difference optimum: balances truncation against round-off |
| `jacobian.singular_tol` | 1e-12 | pivot / ‖J‖∞ | Twelve orders below the matrix norm is numerically zero |
| `outer.max_passes` | 10 | Sizing + deferred-expression loop | Typical convergence is 3 |
| `outer.relative_tol` | 5e-3 | Change in any sized or deferred value | 0.5 % — below the accuracy of the correlations themselves; tightening it buys nothing real |
| `transient.cfl_safety` | 0.9 | Δt / τ_min | Standard margin below the stability limit |
| `transient.local_error_tol` | 1e-4 | Scaled per-step error | Relative to the state scales above |
| `transient.max_step` | 10 s | | Keeps frames responsive even when stability allows more |
| `transient.min_step` | 1e-4 s | | Below this, give up and report `FS3102` |
| `transient.energy_drift_tol` | 1e-2 | Relative, over a run | 1 % triggers `FS3106` |
| `valve.dp_regularization` | 100 Pa | Below this, the √ law is blended | See below — the blend is quadratic, not linear |
| `upwind.smoothing_band` | 1e-3 kg/s | Signed flow, for node enthalpy upwinding | The band over which `h_upstream` blends between the two sides ([`22`](../20-core-domain/22-component-model.md)). Wider than `flow.zero_tol` deliberately: zero-flow *detection* wants a tight threshold, derivative *smoothing* wants a band the Newton step can resolve |
| `flow.zero_tol` | 1e-6 kg/s | Below this, a branch is "no flow" | Roughly 1 µl/s of water; below any physical relevance |
| `quantity.compare_rel_tol` | 1e-9 | `Quantity` equality | Comparison, not convergence |
| `fixed_point.rel_tol` | 5e-3 | Deferred expressions | Same as `outer.relative_tol` — one loop, one tolerance ([`31`](31-solver-architecture.md)) |
| `optimizer.cache_round` | 1e-4 relative | Continuous values in the cache key | [`35`](35-evolutionary-sizing.md) |
| `optimizer.population_size` | 50 | Individuals per generation | Matches `35`'s interactive cost budget |
| `optimizer.generation_cap` | 200 | Maximum generations | 10 000 evaluations at the default population |
| `optimizer.stagnation_generations` | 25 | Consecutive generations | Stop when the best feasible objective has not improved materially |
| `optimizer.stagnation_rel_tol` | 1e-4 | Relative best-objective improvement | Unit-independent across kWh, currency, and kPa objectives |
| `transient.energy_drift_fail` | 5e-2 | Relative, over a run | 5 % stops with `FS3107`; 1 % remains the warning threshold |
| `scale.pressure` | 1e5 | Pa, every node pressure | ≈ 1 bar, the natural magnitude of a hydronic circuit |
| `scale.enthalpy` | 1e5 | J/kg, every node enthalpy | A typical liquid-water enthalpy over the working range |
| `scale.flow_floor` | 1e-3 | kg/s, the floor under a per-branch flow scale | Stops a zero-flow branch dividing by zero, and is where `flow.zero_tol` sits three orders below |
| `scale.temperature` | 1e1 | K, where a temperature is solved directly | A typical ΔT |

**These are defaults, not user-facing settings.** A user who needs to change a solver tolerance has hit
a bug in this table.

## Regularization

Two places where the physics is not differentiable and Newton needs help.

### Valve at zero pressure drop

`Q = C√Δp` (with `C = Kv/√ρ_r`) has infinite derivative at Δp = 0, which is exactly where a closed
valve sits. Below `valve.dp_regularization` (`a` = 100 Pa) the law is replaced by a blend matched in
**both value and slope** at the threshold:

```
Q = (3C / 2√a)·Δp  −  (C / 2a^1.5)·Δp²          for 0 ≤ Δp < a
Q = C·√Δp                                       for Δp ≥ a
```

extended oddly as `sign(Δp)·Q(|Δp|)`.

**It has to be curved, and the reason is worth stating** because the obvious choice is wrong. A
straight line through the origin, `Q = C·Δp/√a`, matches the value at the join but has slope `C/√a`,
where the √-law's slope there is `C/(2√a)` — off by exactly a factor of two. Value and slope cannot
both be matched by a line through the origin; the quadratic above is the lowest-order curve that
satisfies `Q(0) = 0`, `Q(a) = C√a`, and `Q′(a) = C/(2√a)`, and it is monotone on `[0, a]`
(`Q′` runs from `3C/2√a` down to `C/2√a`, both positive).

C¹-continuous at the join by construction. 100 Pa is 0.001 bar — three orders below any meaningful
valve drop, so the regularized region is never the operating point of a real design, only a place the
solver passes through.

**Sign is preserved** across both segments, so reverse flow works and the function is odd.
Forgetting this gives a valve that only passes flow one way, which presents as a mysterious
non-convergence in any circuit with a bypass.

### Node enthalpy upwinding at zero flow

A node's energy balance takes `h_upstream` for an inflow and `h_node` for an outflow
([`22-component-model`](../20-core-domain/22-component-model.md)), which is a step in the residual at
`ṁ = 0`. Over `±upwind.smoothing_band` the two are blended with the same smoothstep used below, so the
balance is C¹ through a flow reversal. Without it, any circuit containing a bypass that closes — which
is every mixing circuit at one end of its valve travel — has a discontinuous Jacobian at exactly the
state the solver is walking toward.

### Laminar–turbulent transition

The friction factor switches from `64/Re` to Colebrook at Re ≈ 2300. A step there is a step in the
Jacobian. Between Re 2300 and 4000 the two are blended with a smoothstep in log Re, C¹ at both ends.
This costs a few lines and removes a class of convergence failure that occurs in exactly the low-flow
regime an oversized circuit sits in.

## Failure taxonomy

Every failure maps to one termination reason, one code, and one message. The mapping is here so that a
new failure mode is classified rather than given a new ad-hoc message.

| Termination | Code | Meaning | What the user should do |
|---|---|---|---|
| `Converged` | — | ‖F‖ below tolerance | — |
| `IterationLimit` | `FS3001` | Cap reached, still improving | Simplify, or state more values |
| `Singular` | `FS3002` | No unique solution | Fix the topology — usually a missing reference |
| `Diverged` | `FS3003` | Residual grew past the factor | Check for a contradictory constraint |
| `Stalled` | `FS3004` | Steps tiny, residual large | Conflicting requirements |
| `Rejected` | `FS3005` | Solver refused the system | Wrong solver for the model |
| `Cancelled` | `FS3006` | | — |
| `NonFinite` | `FS3007` | NaN or ∞ during evaluation | A component produced an impossible state |
| `StepCollapse` | `FS3102` | Transient step below minimum | Something is changing faster than the model can follow |

**Every non-converged result carries `WorstResiduals`** ([`31`](31-solver-architecture.md)) naming the
component and the physical amount it is off by. "Pressure balance at PU1 is off by 4.2 kPa" is the
difference between a message and a fix.

## Floating-point rules

1. **Never `==` on a computed double.** Comparison is relative with an absolute floor per dimension.
2. **Never compare temperatures for equality** — a 1e-15 K difference is not a difference.
3. **Accumulate in a consistent order.** Residual sums iterate a fixed-order collection, never a
   dictionary or a parallel reduction, so results are bit-reproducible
   ([`32`](32-steady-state-newton.md)'s invariant 6).
4. **Check for NaN at stage boundaries, not everywhere.** A NaN check per arithmetic operation costs
   more than it catches; one per residual evaluation localises the fault to a component.
5. **No `Math.Pow(x, 2)`** in a hot path — `x * x`. Measurable inside a Jacobian assembly.
6. **Guard every division** whose denominator can reach zero: density (never zero physically, but a
   failed property call can return zero), flow (zero constantly), pressure difference (zero at
   equilibrium).

## Invariants

1. Every tolerance used anywhere in Core comes from this table; no numeric literal tolerance appears
   in solver code. Enforced by review and by a grep-style test over the solver namespace.
2. The system is scaled before solving and unscaled after; the raw system is never solved.
3. Scales are computed once per solve and held fixed.
4. Every regularized function is C¹ at its join, asserted by a finite-difference test on both sides.
5. Every termination maps to exactly one code and one message.
6. Bit-reproducible results across runs on the same machine and build.

## Worked example

Why scaling is not optional. The M2 demo's residual vector at the sizing seed, unscaled:

| Equation | Residual | Magnitude |
|---|---|---|
| Mass balance at N2 | 3.1e-4 kg/s | 1e-4 |
| Pressure drop, branch 2 | 4.2e3 Pa | 1e3 |
| Energy balance at N2 | 1.8e2 W | 1e2 |

Unscaled ‖F‖∞ = **4.2e3**, dominated entirely by the pressure equation. A tolerance of 1e-8 on this is
unreachable — no double-precision computation drives a residual measured in pascals to 1e-8 — so the
solver would run to its iteration cap on a perfectly good circuit.

Worse, if the tolerance were relaxed to something reachable, say 1e-3 absolute: the pressure equation
would satisfy it at 1e-3 Pa, while the mass balance at 1e-3 kg/s is **off by 0.4 % of the loop flow** —
a real error, silently accepted.

Scaled (pressure by 1e5, flow by 2.4e-1, energy by 2.4e4):

| Equation | Scaled residual |
|---|---|
| Mass balance at N2 | 1.29e-3 |
| Pressure drop, branch 2 | 4.20e-2 |
| Energy balance at N2 | 7.50e-3 |

‖F‖∞ = **4.2e-2**, all three within two orders of each other, and 1e-8 is both reachable and meaningful
for every one of them. It corresponds to 1e-3 Pa, 2.4e-9 kg/s, and 2.4e-4 W — all far below anything
physical.

That is the entire argument, and it is why scaling appears as invariant 1 of
[`31-solver-architecture`](31-solver-architecture.md) rather than as an optimization.

## Acceptance criteria

- [ ] A test asserts no numeric tolerance literal appears in the solver namespace outside this table's
      implementation.
- [ ] Scaled residuals for the M2 demo are within two orders of magnitude of each other at the seed.
- [ ] The valve law is C¹ at 100 Pa, verified by finite differences either side — the test must
      distinguish the correct blend from a straight line through the origin, which agrees in value
      and differs in slope by exactly 2×.
- [ ] The friction blend is C¹ at Re 2300 and 4000.
- [ ] Node enthalpy upwinding is C¹ through a flow reversal at `ṁ = 0`.
- [ ] The valve law is odd: `Q(−Δp) == −Q(Δp)`.
- [ ] Every `SolveTermination` value has a test producing it.
- [ ] 100 identical runs produce bit-identical results.
- [ ] Every non-converged result names at least one component in `WorstResiduals`.

## Open questions

None. Scaling is per branch, and every solve checks the scaled residual before assembling its first
Jacobian so a converged warm start exits without creating a second definition of convergence.
