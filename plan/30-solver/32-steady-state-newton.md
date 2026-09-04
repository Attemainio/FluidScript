---
id: 32-steady-state-newton
title: Steady-state Newton solver
tier: 30-solver
status: reviewed
owns: [Newton-Raphson iteration, Jacobian assembly, damping and line search, linear solve, singular handling]
depends_on: [31-solver-architecture, 36-numerics-and-convergence]
traces_to: [R-11]
open_questions: 0
last_review_pass: 2
---

# Steady-state Newton solver

## Purpose

The default solver: finds the state where every residual is zero. Newton is the right method here for
a specific reason — the system is square, its Jacobian is cheap relative to a property evaluation, and
the physics is smooth almost everywhere. The word "almost" is where the engineering is, and most of
this document is about the exceptions.

## Responsibilities

**Owns.** The Newton iteration, Jacobian assembly, damping and line search, the linear solve, and
singular-Jacobian handling.

**Explicitly does not own.** Tolerances and the failure taxonomy
([`36-numerics-and-convergence`](36-numerics-and-convergence.md)), assembly
([`31-solver-architecture`](31-solver-architecture.md)), component residuals
([`22-component-model`](../20-core-domain/22-component-model.md)).

## The iteration

```
x ← initialGuess
for k in 1..maxIterations:
    F ← residuals(x)                         # scaled
    if ‖F‖∞ < tolerance: return converged

    J ← jacobian(x)                          # scaled
    solve J·Δ = −F                           # LU with partial pivoting
    if singular: return FS3002 with the diagnosis below

    α ← lineSearch(x, Δ, ‖F‖)                # α ∈ (0, 1]
    x ← x + α·Δ

    if ‖α·Δ‖ < stepTolerance and ‖F‖ > tolerance: return FS3004 stalled
    if ‖F‖ > ‖F_previous‖ · divergenceFactor: return FS3003 diverging

return FS3001 iteration cap
```

Nine lines of control flow. Everything below is about making each line behave when the physics does not
cooperate.

## Jacobian

**Numerical, by forward differences, in v1.**

```
J[i,j] = (F_i(x + h_j e_j) − F_i(x)) / h_j        h_j = √ε · max(|x_j|, scale_j)
```

Cost: N+1 residual evaluations per iteration, each of which touches the property backend. For a 15-
unknown circuit that is 16 evaluations per iteration and roughly 60 for a whole solve — negligible.
For a 500-unknown circuit (a large discretized network) it is 500 evaluations per iteration, and the
property cache ([`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md)) is what keeps that
affordable, since a perturbation of one unknown leaves most states unchanged.

**Why not analytic.** Each component would need a hand-derived derivative for every parameter, each
one a place for a sign error that presents as slow convergence rather than a wrong answer — the worst
possible failure mode, because it looks like a tuning problem. Forward differences are exact enough
(`√ε` relative, ≈ 1e-8) for Newton to retain its quadratic rate. Analytic Jacobians are a measured
optimization, available later through an optional interface addition
([`22-component-model`](../20-core-domain/22-component-model.md) says why it is left off the interface).

**The step size matters more than it looks.** `h = √ε · max(|x|, scale)` rather than `√ε · |x|`,
because an unknown that is legitimately zero — a closed valve's branch flow — would otherwise get
`h = 0` and a column of zeros, producing a singular Jacobian at exactly the state a real circuit sits
in.

**Sparsity.** The Jacobian is sparse: a component's residual depends only on its own ports' unknowns.
v1 uses a dense matrix and a dense LU, because at N ≤ 200 dense is faster than any sparse
structure's overhead. The **structural sparsity pattern is still computed and stored**, because it
costs nothing at assembly time and it is what a later sparse solve, and the graph-colouring
optimization that evaluates many Jacobian columns per residual call, both need.

## Line search

Undamped Newton diverges on this class of problem regularly — a full step from a poor initial guess
sends a pressure negative or a flow through a valve to a state where the square-root term has no real
value.

**Backtracking, Armijo condition:**

```
α ← 1
while ‖F(x + α·Δ)‖ > (1 − c·α)·‖F(x)‖  and  α > αmin:
    α ← α/2
```

with `c = 1e-4` and `αmin = 1/64`. If `α` reaches `αmin` without improvement, the step is taken anyway
and the divergence check catches it next iteration — refusing to move is how a solver stalls forever.

**Domain guarding comes before the Armijo test.** A trial point outside the substance's valid range
([`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md)'s `ValidRange`) or with a negative
absolute pressure is rejected and `α` halved without evaluating residuals there. Without this, the
first thing a poor initial guess does is ask CoolProp for water at −3 bar.

## Linear solve

Dense LU with partial pivoting. v1 sizes make this uninteresting: a 200×200 factorisation is
under a millisecond.

**What is interesting is detecting singularity properly.** A pivot below `1e-12 · ‖J‖∞` means the
system has no unique solution, and the useful part is *which* unknown. The zero pivot's row and column
map back to a component and an equation through `EquationSystem`'s declarations, so `FS3002` can say:

> *"The circuit has no unique solution around N3. Check for a missing pressure datum or a closed
> loop with no driver."*

The three causes worth naming in that message, in order of how often they occur:

1. **No pressure datum** in a connected component — every pressure is free by a constant.
2. **No stated temperature in a closed circuit** — every enthalpy is free by a constant, in exactly
   the same way and for exactly the same reason (`D-65`). The energy block of a closed, steady,
   uncoupled circuit is rank-deficient by one, and the assembler must expect that: a stated
   temperature is what makes it full rank, and there is no synthetic row to add in its place, because
   no temperature the solver could invent leaves the answer unchanged.
3. **A loop with no flow driver** — flow is indeterminate; only the trivial zero-flow answer exists.
4. **A duplicated equation** from an over-specified component
   ([`22-component-model`](../20-core-domain/22-component-model.md)'s `FS2101`).

All three are topology problems, which is why
[`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md) checks for them *before* the
solve. A singularity reaching here means the pre-check missed a case, and that is worth logging as
such — it is a bug report about the topology checks, not just a user error.

## Initial guess

Newton's basin of attraction is the practical limit on robustness, and a good guess is worth more than
any amount of damping.

| Situation | Guess |
|---|---|
| First solve | From sizing: flows from stated duties, a stepped pressure and temperature field, enthalpies from stated temperatures — and mass-consistent, which is the part that matters (below) |
| Re-solve after an edit | The previous solution ([`31`](31-solver-architecture.md)) |
| Transient step | The previous frame |
| After a failure | Retry once from the sizing seed rather than the last iterate — a diverged iterate is a worse starting point than a rough estimate |

The retry-from-seed rule is cheap and rescues a real case: a user edits a value, the warm start is now
in the wrong basin, and a cold retry converges immediately. **It cannot live in `NewtonSettings`,
though, and this document put it there (`S-20`).** `SolveAsync` receives exactly one starting vector,
and on a re-solve that vector *is* the warm start — so the solver has no second seed to retry from.
Both seeds exist together only in [`31`](31-solver-architecture.md)'s outer loop, which is where the
retry belongs.

**A seed is a prerequisite for convergence, not a convenience, and the obvious stand-ins are singular
(`S-21`).** Two hand-made guesses look reasonable and both produce `FS3002` on a well-posed circuit:

| Seed | Why it is singular |
|---|---|
| Zero flow everywhere | `Δp = R·ṁ\|ṁ\|` and `H₀ − kṁ²` both have derivative **exactly** zero at `ṁ = 0`, so every quadratic pressure row contributes a zero |
| One sign for every branch flow | A branch's orientation is `Decompose`'s choice rather than a direction, so some node ends up with every port an inflow — and a node nothing leaves is one whose own enthalpy enters no equation it owns |

Measured on the cooling loop, the second case gave `N2.h` a maximum Jacobian entry of `1e-6` against
`1` everywhere else; alternating the seeded signs took the matrix non-singular. So the requirement on a
seed is that it be mass-consistent, which is stronger than "close enough", and it is why
[`24-auto-sizing`](../20-core-domain/24-auto-sizing.md) is a hard dependency of a converging solve
rather than a later refinement.

**"Roughly" was the wrong word, and the exact version is the easier one to build.** A divergence-free
field on a graph is a particular solution plus the cycle space, and [`23`](../20-core-domain/23-topology-and-graph.md)
already computes that cycle space for its own reasons — so choosing freely exactly where the freedom
is, and solving for the rest, costs one spanning forest and reaches every mass-consistent field there
is. `SolutionSeed` does that: it spans the branch graph, gives each chord its own flow estimate, picks
boundary fluxes summing to zero per hydraulic component, then solves the tree leaves-inward, each
branch taking whatever closes its vertex. The final vertex closes identically, and that is the
construction's whole claim. An approximation would have needed a tolerance, a test that argues about
it, and a failure mode where a nearly-consistent seed is singular anyway.

**One case the seed cannot rescue, and should not try to.** A dead leg — a terminal with no boundary
role — carries exactly zero flow, so its node's enthalpy is multiplied by zero in every equation it
appears in and its column is identically zero (`S-23`). That is the physics: stagnant fluid has no
steady temperature. It needs a modelling decision — refuse the graph, or close the node against its
neighbour — and not a different starting point.

**Every difference a residual reads has to be non-zero, and that is the general form of the rule.**
`S-21` is the flow case. Two more turned up the moment promoted parameters became real columns, and
neither is about flow at all:

| Uniform in the seed | What goes singular | Where it was measured |
|---|---|---|
| Pressure | `ṁ = Kv·f(x)·√(Δp·ρ)` has derivative zero in **`Kv`** and in **`position`** at `Δp = 0` | The cooling loop's promoted `3WV.position`, a column of zeros beside columns of O(1) |
| Enthalpy | A node's `ṁ(h_arriving − h_own)` has derivative zero in **flow** at a uniform `h` | The simple loop, rank 11 of 12, null direction along `PU1.head` and the branch flow together |

Both had an argument against them that sounded right and was not. Pressure *does* enter a momentum
relation linearly, so Newton reaches the pressure field in one step from any level — and that says
nothing about the columns which multiply `√Δp`. So the seed steps pressure and temperature along each
branch, wrapped into a band of five steps so that a long branch cannot walk a state out of the fluid's
validated range while adjacent nodes still differ (`S-25`).

Flow estimates come from stated duties and stated flows, spread across junctions by magnitude rather
than by [`24`](../20-core-domain/24-auto-sizing.md)'s exact propagation (`C-44`), and every unstated
branch falls back to a nominal 0.1 kg/s. That one is a genuine weakness and costs iterations rather
than correctness.

## Contracts

```csharp
public sealed class NewtonSolver : ISolver
{
    public string Name => "newton";

    /// <inheritdoc/>
    /// <remarks>Refuses systems declaring time derivatives — those need the transient solver.</remarks>
    public Result<Unit> CanSolve(EquationSystem system);

    /// <inheritdoc/>
    public Task<SolveResult> SolveAsync(EquationSystem system, StateVector initialGuess,
                                        IProgress<SolveProgress>? progress,
                                        CancellationToken cancellationToken);
}

/// <summary>Tuning. Defaults are in 36-numerics-and-convergence; these are not user-facing.</summary>
public sealed record NewtonSettings
{
    public int MaxIterations { get; init; } = 50;
    public double ResidualTolerance { get; init; } = 1e-8;
    public double StepTolerance { get; init; } = 1e-10;
    public double DivergenceFactor { get; init; } = 10.0;
    public double MinLineSearchStep { get; init; } = 1.0 / 64.0;
}
```

## Invariants

1. Every residual and every unknown is scaled before the iteration; the raw system is never solved.
2. The residual norm is non-increasing across accepted steps, except where `α` hit `αmin`.
3. No trial point outside the substance's valid range is evaluated.
4. The Jacobian's finite-difference step is non-zero for every column, including zero-valued unknowns.
5. A singular Jacobian returns `FS3002` naming a component; no linear-algebra exception escapes.
6. The solver is deterministic — same input, same iterate sequence, bit for bit.
7. Cancellation is checked between iterations only; a residual evaluation is never interrupted midway.

Invariant 6 rules out any parallelism inside the iteration that could reorder floating-point
accumulation. Jacobian columns are independent and tempting to parallelise; doing so must preserve
deterministic accumulation order, and if it cannot, it must not be done. A solver that gives slightly
different answers on different runs makes every golden-file test flaky and every user report
unreproducible.

## Error cases

Inherited from [`31-solver-architecture`](31-solver-architecture.md): `FS3001` cap, `FS3002` singular,
`FS3003` diverging, `FS3004` stalled, `FS3007` non-finite. Two specific to this solver:

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS3011` | Line search hit `αmin` without improvement | Info | `Taking a reduced step near {component}; the solution is hard to reach here.` |
| `FS3012` | Retried from the sizing seed after a warm-start failure | Info | `Restarted from the initial estimate.` |

Both are info: they describe recovery, not failure, and a user does not need them — but a support
conversation does, and the console log ([`56-console-log`](../50-frontend/56-console-log.md)) can show
them on demand.

## Worked example

**A minimal abstract system, not a reference circuit** — permitted by `D-11`'s carve-out for
numerical-method illustrations ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)),
because what is being shown is the convergence *rate* and the simple loop's catalogue lookups and
enthalpy calls would bury it. Nothing here is a plant figure and nothing here is cited elsewhere.

A two-node loop: a pump, 10 m of DN25 pipe, water at 20 °C. Pump curve `H = 8 − 2×10⁵·ṁ²` (metres, ṁ in
kg/s), pipe drop `Δp = 4.1×10⁴·ṁ²` Pa. One unknown after eliminating pressures by symmetry: the flow.

Residual: `F(ṁ) = ρg(8 − 2×10⁵ṁ²) − 4.1×10⁴ṁ² = 78 300 − 1.958×10⁹ṁ² − 4.1×10⁴ṁ²`.

Analytically, ṁ² = 78 300 / 1.958×10⁹ ≈ 3.999×10⁻⁵, so ṁ = **6.324×10⁻³ kg/s**... which is an
implausibly small flow for a DN25 pipe, and that is the example doing its job: the pump curve's
coefficient is unrealistic. Taking `H = 8 − 20·ṁ²` instead:

`F(ṁ) = 78 300 − 1.958×10⁵·ṁ² − 4.1×10⁴·ṁ²= 78 300 − 2.368×10⁵·ṁ²` → ṁ = **0.5750 kg/s**, which in
DN25 (27.3 mm bore, **not** 25 mm) is v = **0.98 m/s**. Plausible.

The velocity is worth spelling out because the wrong one is so easy to write: 0.575 kg/s ÷ 998 kg/m³ =
5.76×10⁻⁴ m³/s over the 27.3 mm bore's 5.854×10⁻⁴ m² gives 0.98 m/s. Dividing by the area of a 25 mm
circle instead gives 1.17 m/s — a 20 % error, from a number that is not a diameter
([`02-glossary`](../00-foundation/02-glossary.md)).

Newton from the sizing seed ṁ₀ = 0.4:

| k | ṁ | F(ṁ) | J = dF/dṁ | Δ | α | ‖F‖ |
|---|---|---|---|---|---|---|
| 0 | 0.4000 | +40 412 | −1.894×10⁵ | +0.2133 | 1 | 4.04e4 |
| 1 | 0.6133 | −10 762 | −2.904×10⁵ | −0.03706 | 1 | 1.08e4 |
| 2 | 0.5762 | −325.3 | −2.729×10⁵ | −0.001192 | 1 | 3.25e2 |
| 3 | 0.5750 | −0.336 | −2.723×10⁵ | −1.234e-6 | 1 | 3.4e-1 |
| 4 | 0.5750 | −3.6e-7 | | | | **converged** |

The residual drops 4e4 → 1e4 → 3e2 → 3e-1 → 4e-7: each error roughly the square of the last, once past
the first step. That is the signature to test for. Full steps throughout, because the problem is smooth
and the seed was close — a line search would only engage from a much worse guess, or with a valve near
closed.

Note the scaled view: with a pressure scale of 1e5, those norms are 0.40, 0.11, 3.3e-3, 3.4e-6, 4e-12,
and the 1e-8 tolerance is met at iteration 4. Scaling is what makes one tolerance mean the same thing
for a pressure residual and a mass residual.

## Acceptance criteria

- [ ] The worked example converges in ≤ 5 iterations to 0.5750 kg/s ± 1e-4.
- [ ] Convergence is quadratic: `log‖F_{k+1}‖ ≈ 2·log‖F_k‖ + c` over the last three iterations,
      asserted as a rate test rather than only an endpoint test.
- [ ] A circuit whose datum was never established produces `FS3002` naming a node.
- [ ] A cold start from a guess 10× off still converges, exercising the line search.
- [ ] No trial point outside `ValidRange` reaches the property backend, asserted with a counting fake.
- [ ] The same input produces a bit-identical iterate sequence across 100 runs.
- [ ] A closed valve (zero-flow branch) does not produce a singular Jacobian.
- [ ] A 200-unknown circuit solves within `07`'s draft-compile budget, and within one debounce
      interval (`D-49`) so consecutive compiles cannot overlap.

## Open questions

None. v1 uses dense LU and refuses models above `07`'s 800-unknown hard limit before allocation.
M0/M2 benchmark fixtures at 200 and 800 unknowns must meet their declared interactive/cancellable
budgets; a miss blocks the milestone and triggers a new sparse-solver decision rather than silently
raising the limit. The Jacobian is rebuilt/refactorized every iteration; modified Newton is post-v1
and requires profiling evidence (`D-30`).
