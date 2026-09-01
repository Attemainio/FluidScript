---
id: 31-solver-architecture
title: Solver architecture
tier: 30-solver
status: reviewed
owns: [ISolver seam, system assembly, unknown and equation registry, the outer fixed-point loop, solver selection]
depends_on: [22-component-model, 23-topology-and-graph, 24-auto-sizing]
traces_to: [R-11, R-12, R-15, R-16]
open_questions: 0
last_review_pass: 2
---

# Solver architecture

## Purpose

The seam every solver sits behind, how a `CircuitGraph` becomes a system of equations, and where the
outer loop that reconciles sizing and deferred expressions lives. The brief asks for three solver
families — Newton-type, time-domain, and evolutionary — and they solve genuinely different problems.
This document is what stops them being three unrelated subsystems that each re-derive the assembly.

## Responsibilities

**Owns.** `ISolver` and its result types, system assembly (unknowns and equations), the outer
fixed-point loop, and solver selection.

**Explicitly does not own.** The numerical methods themselves (`32`, `33`, `35`), tolerances and
failure taxonomy ([`36-numerics-and-convergence`](36-numerics-and-convergence.md)), component
equations ([`22-component-model`](../20-core-domain/22-component-model.md)).

## Three solvers, three problems

Naming what each actually solves, because the brief's framing ("newton rhapson, bayesian, gradient
based solver type and purpose") invites picking one, and they are not alternatives:

| Solver | Problem | Shape | When |
|---|---|---|---|
| **Newton** (`32`) | Find the state where all residuals are zero | Square nonlinear system, `F(x) = 0` | Every steady-state solve. The default. |
| **Time-domain** (`33`) | Follow the state as boundary conditions change | Initial-value problem, `dx/dt = f(x, t)` | `fluid dynamic …` |
| **Evolutionary** (`35`) | Choose parameters minimising a cost subject to constraints | Optimization over a search space, each evaluation being a full solve | On request, M6 |

**Bayesian optimization and gradient descent are not circuit solvers.** They are optimizers, and they
belong in the same box as the evolutionary solver — alternatives *to each other* for the sizing
problem, not to Newton for the equilibrium problem. Using gradient descent to find a circuit
equilibrium would work and would be roughly a hundred times slower than Newton, because Newton exploits
the fact that the system is square and its Jacobian is available. This is worth stating plainly since
the brief lists them as peers.

## The seam

```csharp
/// <summary>Solves a circuit. Implementations differ in the question they answer, not in how
/// they are invoked.</summary>
public interface ISolver
{
    string Name { get; }

    /// <summary>Whether this solver can handle the given system.</summary>
    /// <returns>
    /// A reason when it cannot — a steady solver refuses a system with time derivatives, an
    /// explicit transient solver refuses one whose stiffness exceeds its step limit. Checked
    /// before solving so the user gets a sentence rather than a divergence.
    /// </returns>
    Result<Unit> CanSolve(EquationSystem system);

    /// <summary>Solves, reporting progress and honouring cancellation.</summary>
    /// <param name="system">The assembled system.</param>
    /// <param name="initialGuess">Starting iterate; from sizing on a first solve, from the
    /// previous solution on a re-solve — which is what makes editing feel instant.</param>
    /// <param name="progress">Per-iteration progress, or null. Never called after the method returns.</param>
    /// <param name="cancellationToken">Honoured between iterations, never inside a residual evaluation.</param>
    Task<SolveResult> SolveAsync(EquationSystem system, StateVector initialGuess,
                                 IProgress<SolveProgress>? progress,
                                 CancellationToken cancellationToken);
}

public sealed record SolveResult
{
    public required bool Converged { get; init; }
    public required StateVector Solution { get; init; }
    public required int Iterations { get; init; }
    public required double ResidualNorm { get; init; }

    /// <summary>Why it stopped. Converged, iteration cap, stalled, diverged, singular, cancelled.</summary>
    public required SolveTermination Termination { get; init; }

    /// <summary>The worst-offending equations when it did not converge.</summary>
    /// <remarks>
    /// Named by component and equation, not by row index. "Pressure balance at PU1 is off by
    /// 4.2 kPa" is actionable; "residual[17] = 4200" is not — and the mapping from row to
    /// component is only available here, so if this layer does not do it nobody can.
    /// </remarks>
    public required ImmutableArray<ResidualReport> WorstResiduals { get; init; }
}

public enum UnknownKind { BranchFlow, NodePressure, NodeEnthalpy, ExternalMassFlux, Parameter }
public enum EquationKind { Pressure, Mass, Energy, Boundary, ComponentConstraint }

public sealed record UnknownDeclaration(
    int Index,
    UnknownKind Kind,
    string OwnerComponentId,
    string Name,
    string SiUnit);

public sealed record EquationDeclaration(
    int Index,
    EquationKind Kind,
    string OwnerComponentId,
    string Name,
    string ResidualSiUnit);

public sealed record StateVector(ImmutableArray<double> Values);

public sealed record ScalingVector(
    ImmutableArray<double> UnknownScales,
    ImmutableArray<double> ResidualScales);

public sealed record ResidualReport(
    string OwnerComponentId,
    string EquationName,
    double Residual,
    string ResidualSiUnit,
    double ScaledResidual);
```

**`initialGuess` from the previous solution is what makes the editor feel alive.** A one-character
edit changes one coefficient; starting Newton from the previous solution converges in two or three
iterations instead of eight. This is the difference between the debounce path feeling instant and
feeling sluggish, and it is free. It also buys headroom for a shorter debounce, since `D-49` sets
that from measured compile time.

## System assembly

`CircuitGraph` → `EquationSystem`: a flat vector of unknowns, a residual function, and the mapping back
to components that makes diagnostics readable.

```csharp
/// <summary>A square nonlinear system assembled from a circuit.</summary>
public sealed class EquationSystem
{
    /// <summary>Unknowns in solve order. Each names its owner and physical meaning.</summary>
    public IReadOnlyList<UnknownDeclaration> Unknowns { get; }

    /// <summary>Equations in residual order, each naming the component that contributes it.</summary>
    public IReadOnlyList<EquationDeclaration> Equations { get; }

    /// <summary>Evaluates every residual at the given iterate.</summary>
    /// <remarks>
    /// The hot path: called once per Newton iteration plus once per column for a numerical
    /// Jacobian, so an N-unknown system evaluates it N+1 times per iteration. Allocation-free
    /// and deterministic (22's invariants 2 and 3).
    /// </remarks>
    public void EvaluateResiduals(ReadOnlySpan<double> x, Span<double> residuals);

    /// <summary>Per-unknown and per-equation scale factors.</summary>
    /// <remarks>
    /// Pressures are ~1e5 Pa, mass flows ~1e-1 kg/s, enthalpies ~1e5 J/kg. An unscaled system
    /// spans six orders of magnitude, its Jacobian's condition number is dominated by units
    /// rather than physics, and a convergence test on the raw norm measures the pressure
    /// residual alone. Scaling is not an optimization here; it is a correctness requirement.
    /// </remarks>
    public ScalingVector Scaling { get; }
}
```

### Unknown ordering

Grouped by kind — all branch flows, then all node pressures, then all node enthalpies — with each group
in the graph's deterministic component order.

Grouping by kind, rather than interleaving per node, gives the Jacobian a block structure: the
flow/pressure block is the hydraulic problem and the enthalpy block is the thermal one, and they couple
only weakly through density. That structure is what a later block-decomposed or segregated solver would
exploit if measurement later justifies block decomposition (`36`), and it costs nothing to establish now.

## The outer loop

Three things iterate, and they must be **one loop**, not three nested ones:

1. **Sizing** needs flows; flows need sizes ([`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)).
2. **Deferred expressions** need solved values; solved values need the parameters those expressions set
   ([`14-expressions-and-references`](../10-language/14-expressions-and-references.md)).
3. **The solve** needs both.

```
CompileAndSolve(script):
    model  ← bind(parse(script))
    graph  ← lower(model)
    x      ← seedFromStatedDuties(graph)

    for pass in 1..MaxOuterPasses:            # default 10
        evaluateDeferredExpressions(graph, x)  # 14
        applySizing(graph, x)                  # 24
        result ← solver.SolveAsync(assemble(graph), x)
        if not result.Converged: return failure(result)
        if nothing changed by more than tolerance: return success(result)
        x ← result.Solution                    # warm start the next pass

    return warning(FS2301 / FS1405, last result)
```

**Nesting them would be wrong**, not merely slower: a sized value feeding a deferred expression feeding
another sized value would converge in an inner loop against a stale outer value, and the two loops
could oscillate against each other with neither detecting it. One loop, one convergence test, one
iteration cap.

The loop is **the same code path for steady and transient**. A transient run does one outer pass to
establish the initial condition, then steps in time with sizes held fixed
([`24`](../20-core-domain/24-auto-sizing.md)'s fixed-snapshot rule — sizing is a design-point property
and must not re-run per frame).

## Solver selection

Automatic, from the model. Not a user choice in v1 — a `solver newton` directive would be a
question the user cannot answer better than the tool can.

| Model | Solver |
|---|---|
| `fluid water` (static) | Newton |
| `fluid dynamic water` | Time-domain, with Newton establishing t = 0 |
| An explicit optimization request (M6) | Evolutionary, wrapping Newton per evaluation |

`CanSolve` is checked before the run so an unsuitable pairing produces a sentence rather than a
divergence.

## Invariants

1. `Unknowns.Count == Equations.Count` for every assembled system (guaranteed by
   [`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)'s counting check, which
   includes boundary conditions and promoted parameters — a count over branches and nodes alone
   balances identically and checks nothing).
2. `EvaluateResiduals` allocates nothing and is deterministic.
3. Every unknown and every equation names its owning component.
4. Cancellation is honoured between iterations; a cancelled solve leaves no background work.
5. The outer loop terminates within `MaxOuterPasses` with convergence or a diagnostic.
6. A solve is a pure function of (graph, initial guess, settings) — no global state, no ambient cache.
7. Two solvers given the same converged system agree within the convergence tolerance.

Invariant 7 is a real test, not a platitude: solving a steady system with the time-domain solver run to
steady state must reproduce Newton's answer. It is the strongest cross-check available, since the two
share no numerical code.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS3001` | Iteration cap reached | Error | `Could not solve in {n} steps. Furthest off: {component} by {amount}.` |
| `FS3002` | Singular Jacobian | Error | `The circuit has no unique solution around {component}. Check for a missing pressure datum or a closed loop with no driver.` |
| `FS3003` | Residual grew — diverging | Error | `The solution is moving away from a balance. Last stable point: {values}.` |
| `FS3004` | Stalled — steps below tolerance, residual above | Error | `Stuck at {residual}. {component} may have conflicting requirements.` |
| `FS3005` | A solver refused the system | Error | `{solver} cannot solve this: {reason}.` |
| `FS3006` | Cancelled | Info | `Solve cancelled.` |
| `FS3007` | Non-finite value during evaluation | Error | `{component} produced an impossible value at {state}.` |

Every message names a component. That mapping exists only in `EquationSystem`, which is why
`ResidualReport` is part of the result contract rather than a debugging afterthought.

## Worked example

The **cooling loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)): 6 nodes,
4 branches, assembled per [`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)'s
counting scheme.

**Unknowns (20):**

| Index | Kind | Owner |
|---|---|---|
| 0–3 | Branch flow | branches 1–4 |
| 4–9 | Node pressure | N1, N2, N3, PU1__HE1, HE1__3WV, 3WV__P1 |
| 10–15 | Node enthalpy | the same six nodes |
| 16–17 | External mass flux | N1, N3 — the two nodes with a stated pressure |
| 18 | `PU1.head` | promoted by `HE1 out=50` fixing the flow |
| 19 | `3WV.position` | promoted by `HE1 in=20` fixing the mix |

**Equations (20):**

| Index | Kind | Owner |
|---|---|---|
| 0–5 | Pressure relation | PU1, HE1, P1 (one each), 3WV (a→b and a→c), the N1–N2 ideal link |
| 6–9 | Mass balance | N1, N2, N3, and the 3WV split — not the three interior nodes |
| 10–15 | Energy balance | the six nodes |
| 16–17 | Stated pressure | N1 = 300 kPa, N3 = 280 kPa |
| 18–19 | Component constraint | `HE1.in` = 20 °C, `HE1.out` = 50 °C |

No datum row appears and no mass balance is dropped: `N1` states a pressure, so it supplies the datum,
and the external fluxes make the mass balances independent
([`23`](../20-core-domain/23-topology-and-graph.md)). The two promoted unknowns and the two component
constraints arrive together — that pairing is what keeps the system square, and it is the assembly-level
expression of `D-02`.

**Scaling:** pressures by 1e5, flows by 2.4e-1 (the largest branch flow), enthalpies by 1e5. After scaling every residual is O(1)
and the convergence test means the same thing for all three.

**A run**, warm-started from sizing:

| Iteration | Scaled residual norm | Note |
|---|---|---|
| 0 | 4.2e-2 | sizing's estimate; the same full scaled norm derived in `36` |
| 1 | 2.7e-2 | |
| 2 | 8.3e-4 | |
| 3 | 1.1e-6 | quadratic convergence visible |
| 4 | 3.2e-9 | **converged** (tolerance 1e-8) |

Errors squaring each iteration — 1e-2 → 1e-4 → 1e-6 — is Newton behaving as it should on a
well-scaled system with an accurate Jacobian. A run that converges linearly instead is a signal that
the Jacobian is wrong or the system is badly scaled, and it is worth asserting the *rate* in a test,
not only the endpoint.

## Acceptance criteria

- [ ] The cooling loop assembles to exactly the 20 unknowns and 20 equations tabulated above.
- [ ] Removing `HE1 in=20` removes both that equation and the `3WV.position` unknown, leaving the
      system square at 19 — promotion and constraint always appear and disappear together.
- [ ] Every unknown and equation reports its owning component by name.
- [ ] A steady solve from a warm start converges in fewer iterations than from a cold one, measured.
- [ ] The time-domain solver run to steady state reproduces Newton's answer within tolerance
      (invariant 7).
- [ ] Cancelling mid-solve returns within one iteration and leaves no background task.
- [ ] `EvaluateResiduals` allocates zero bytes.
- [ ] A singular system produces `FS3002` naming a component, not a linear-algebra exception.
- [ ] The outer loop converges on the M2 demo in ≤ 5 passes.

## Open questions

None. Sizing and deferred expressions remain in one outer loop around Newton; catalogue choices never
enter the continuous Jacobian. Both steady and transient solvers expose cancellable `Task` APIs and
run on dedicated bounded backend workers. The async boundary expresses scheduling/cancellation; hot
residual and Newton code remains synchronous within the worker (`D-30`).
