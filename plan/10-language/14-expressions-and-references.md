---
id: 14-expressions-and-references
title: Expressions and references
tier: 10-language
status: reviewed
owns: [expression grammar, operator set and precedence, let bindings, member references, dependency graph, evaluation order]
depends_on: [12-grammar, 13-type-and-unit-system]
traces_to: [R-03, R-02]
open_questions: 0
last_review_pass: 2
---

# Expressions and references

## Purpose

The "light expressions" half of `D-01`. Small enough that the whole evaluator fits in a few hundred
lines, expressive enough that a designer never has to hand-compute a derived value and paste it in.
The hard part is not arithmetic — it is `HE1.dp`, a reference to a value that does not exist until the
circuit has been sized and solved, which turns evaluation into a dependency problem.

## Responsibilities

**Owns.** Expression syntax, the operator set and precedence, `let` semantics, member-reference
resolution, the dependency graph, evaluation order, and cycle detection.

**Explicitly does not own.** Dimensions and units ([`13-type-and-unit-system`](13-type-and-unit-system.md)),
which properties a component exposes ([`22-component-model`](../20-core-domain/22-component-model.md)),
the solve itself (tier 30).

## Expression grammar

```ebnf
expression   = additive ;
additive     = multiplicative , { ("+" | "-") , multiplicative } ;
multiplicative = unary , { ("*" | "/" | "%") , unary } ;
unary        = [ "-" ] , primary ;
primary      = quantity | number | reference | "(" , expression , ")" | call ;
reference    = identifier , { "." , identifier } ;
call         = identifier , "(" , [ expression , { "," , expression } ] , ")" ;
```

Precedence, tightest first: unary minus, then `* / %`, then `+ -`. Left-associative. Parentheses group.
No exponentiation operator — `pow(x, 2)` is a call, because `^` and `**` both have a constituency and
picking one violates P6 for no gain.

**No boolean operators, no comparisons, no ternary.** There is nothing to branch on (`D-01`).

### The `-` ambiguity

`-` is both subtraction and the connection operator. There is no conflict, because they never occur in
the same position: connections live only in the connection section and contain no expressions, and
expressions occur only after `=` in a parameter or `let`. A `-` at the start of a connection-section
line that is not between two endpoints produces `FS1104`.

### Functions

A fixed, closed set. Not user-extensible (`D-01`).

| Function | Signature | Notes |
|---|---|---|
| `min(a, b, …)` | same dimension in, same out | |
| `max(a, b, …)` | same dimension in, same out | |
| `abs(a)` | same dimension | |
| `round(a, n?)` | same dimension | `n` = decimals, default 0 |
| `pow(a, n)` | dimensionless `a` only | A dimensioned base with a runtime exponent gives an exponent vector that is not known until evaluation, so nothing can be type-checked ahead of it |
| `sqrt(a)` | dimensionless only | Halves every exponent, so it produces fractional exponents — legal arithmetic, unnameable dimensions, and no way to report a useful `FS1304` |

Restricting `pow` and `sqrt` to dimensionless arguments is a real limitation — `sqrt` of a pressure
appears in orifice equations — and it is deliberate: those equations live in Core, in C#, where the
dimensional bookkeeping is done once and tested, not in user scripts. It is a narrower restriction than
the general algebra in [`13-type-and-unit-system`](13-type-and-unit-system.md) needs, and deliberately
so: `*` and `/` keep exponents integral, and these two would not.

## `let` bindings

```fluidscript
let dT      = 30 dK                  # context-free TemperatureDelta under D-26
let Q       = 30 kW
let mdot    = Q / (4.18 kJ/(kg*K) * dT)
```

- **Bind once.** A second `let` of the same name is `FS1401`. There is no assignment and no shadowing.
- **Order-independent.** A `let` may reference a later `let`. The dependency graph decides evaluation
  order, not source position. This matters because canvas write-back (`R-25`) inserts lines and must
  not have to reason about where.
- **Scope is the whole script.** No block scoping in v1, because there are no blocks.
- **A `let` may be dimensioned or dimensionless.** Its dimension is inferred from its expression;
  there is no type annotation.

## Member references

`HE1.dp` reads a resolved property of another component. This is the feature that makes `D-02`'s
"explicit values are constraints" usable — `PU1 pump head=1.2*HE1.dp` states a real design intent.

### What is referenceable

| Category | Available | Example |
|---|---|---|
| **Declared parameters** | Always, immediately | `HE1.power` — what the user wrote |
| **Sized parameters** | After sizing | `PU1.head` when the pump was auto-sized |
| **Solved state** | After the solve | `N2.t`, `N2.p`, `HE1.dp`, `PU1.flow` |
| **Derived geometry** | After sizing | `P1.diameter` |

The property names are declared per component in
[`22-component-model`](../20-core-domain/22-component-model.md) and must be short — `dp`, `t`, `p`,
`flow` — because they appear inline in a language that trades on density.

### The circularity that matters

`PU1 pump head=1.2*HE1.dp` needs `HE1.dp`, which is a *solved* value, and the solve needs the pump's
head. That is a genuine circular dependency between the script's evaluation and the physics, not a
mistake by the user, and it is the central design problem of this document.

**Resolution: a two-phase evaluation with a fixed point.**

```
Phase A — static evaluation
  Evaluate every expression whose dependencies are literals, lets, and declared parameters.
  Anything depending on a solved or sized value is left as a deferred expression.

Phase B — sizing and solving, iterated
  1. Size and solve with deferred expressions held at their current estimate
     (first pass: the component's own default sizing, ignoring the deferred constraint).
  2. Re-evaluate every deferred expression against the new solution.
  3. If any changed by more than the fixed-point tolerance, go to 1.
  4. Converged, or FS1405 after the iteration cap.
```

This is an outer fixed-point loop wrapped around the solver
([`31-solver-architecture`](../30-solver/31-solver-architecture.md) owns the loop's placement).

**When it converges, and when it cannot.** The iteration is `H ← k · dp(H)`, and it contracts exactly
when `|k · ∂dp/∂H| < 1`. That condition is not automatic, and the obvious example fails it: in a
**closed loop** the pump head equals the total loop drop at equilibrium, and every passive drop scales
as ṁ², so a component's drop is a near-constant *fraction* φ of the head. The map becomes `H ← k·φ·H`,
which is linear with no non-zero fixed point — it collapses to zero flow for `kφ < 1` and diverges for
`kφ > 1`. Writing `head=1.2*HE1.dp` on a loop the pump itself drives is therefore not a slow-converging
case; it is a degenerate one.

The cases that do converge are those where the referenced value is **anchored by something other than
the parameter being set** — a duty-constrained flow, a stated boundary, a parallel branch. The worked
example below is one of those, and `FS1405` reports the degenerate cases with their values rather than
picking one.

**The alternative, rejected:** forbid references to solved values, allowing only declared parameters.
That removes the whole problem and most of the feature's value — `1.2*HE1.dp` is exactly the expression
a designer wants to write, and forbidding it sends them back to hand-computing and pasting.

## The dependency graph

Nodes are `let` bindings, component parameters, and component properties. Edges point from a
dependency to its dependent.

```csharp
/// <summary>Resolves evaluation order and detects circularity among script-level values.</summary>
public interface IDependencyGraph
{
    /// <summary>Evaluation order for everything computable without a solve.</summary>
    /// <returns>Topologically sorted, or the cycle when one exists.</returns>
    OrderResult TopologicalOrder();

    /// <summary>Expressions that depend on a sized or solved value.</summary>
    ImmutableArray<DeferredExpression> Deferred { get; }
}

public abstract record OrderResult
{
    public sealed record Ordered(ImmutableArray<ValueId> Order) : OrderResult;

    /// <param name="Cycle">The participating ids in cycle order, first repeated at the end.</param>
    public sealed record Cyclic(ImmutableArray<ValueId> Cycle) : OrderResult;
}

/// <summary>A stable identity for any value that may participate in evaluation.</summary>
public abstract record ValueId
{
    public sealed record Let(string Name) : ValueId;
    public sealed record ComponentParameter(string Component, string Parameter) : ValueId;
    public sealed record ComponentProperty(string Component, string Property) : ValueId;
}

/// <summary>An expression held until sizing or solving supplies all of its inputs.</summary>
public sealed record DeferredExpression(
    ExpressionSyntax Expression,
    ValueId Target,
    Quantity? CurrentEstimate,
    ImmutableHashSet<ValueId> Dependencies);
```

`CurrentEstimate == null` means the first outer pass must obtain the target from normal sizing before
re-evaluation. Dependencies contain every direct `ValueId` read by the expression, including values
that are already static; this makes cycle formatting and invalidation deterministic.

**A static cycle is an error, not a fixed point.** `let a = b + 1` / `let b = a + 1` has no solution
and is `FS1402`, reported with the whole cycle so the user can see which link to break — reporting only
one participant is the standard failure of cycle diagnostics and is useless when the cycle is four
links long.

**A cycle through a solved value is not a static cycle.** `PU1.head → HE1.dp → (solve) → PU1.head` is
the fixed point above, and the graph classifies it as deferred rather than cyclic. Distinguishing them
is what makes the feature usable: rejecting all cycles rejects the useful case, accepting all cycles
hangs.

## Invariants

1. Evaluation of a script with no deferred expressions is a pure function of its source text.
2. Every `let` is evaluated at most once per compilation.
3. A static cycle is reported as `FS1402` and no partial values from that cycle are used.
4. The fixed-point loop is bounded: it terminates in at most `MaxFixedPointIterations` (default 20)
   with either convergence or `FS1405`.
5. Dimensional correctness is checked before evaluation, so no operation is performed on mismatched
   dimensions even in a deferred expression.
6. Evaluation never throws — division by zero is `FS1403`, not `DivideByZeroException`.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1401` | Duplicate `let` | Error | `'{name}' is already defined at line {n}.` |
| `FS1402` | Static dependency cycle | Error | `'{a}' depends on itself: {a} → {b} → … → {a}.` |
| `FS1403` | Division by zero | Error | `Dividing by zero here. '{expr}' is zero.` |
| `FS1404` | Reference to an unknown name | Error | `Nothing named '{name}'. Did you mean '{suggestion}'?` |
| `FS1405` | Fixed point did not converge | Error | `'{expr}' did not settle: {v1} then {v2} then {v3}. Try stating a value directly.` |
| `FS1406` | Reference to a property the component does not have | Error | `A {kind} has no '{prop}'. It has: {list}.` |
| `FS1407` | Reference to a solved value in a context evaluated before the solve | Error | `'{ref}' is only known after solving; it cannot set '{target}'.` |
| `FS1408` | Unknown function | Error | `No function '{name}'. Available: {list}.` |
| `FS1409` | Wrong argument count | Error | `'{fn}' takes {n} arguments.` |

`FS1406` listing the available properties is the difference between a diagnostic and a scavenger hunt,
and it costs one string join.

`FS1407` is deliberately narrow: it fires only where the consumer must be finalized before the outer
sizing/solve loop exists (a language version, catalogue id/version, component kind, port name, symbol
name, schedule time, or fixed visualization range). A quantity-valued component parameter or `let`
that reads a sized/solved property becomes a `DeferredExpression` instead. For example,
`catalog HE1.series` produces `FS1407`; `PU1 pump head=1.2*HE1.dp` defers and does not.

## Worked example

```fluidscript
let dT   = 30 dK
let Q    = 30 kW
let cp   = 4.18 kJ/(kg*K)
let mdot = Q / (cp * dT)

HE1 heat_exchanger power=Q in=20 out=20C+dT
PU1 pump head=1.2*HE1.dp
```

**Dependency graph.**

```
dT ──┐
     ├──► mdot ─── (unused by any component; still evaluated, still reported if it errors)
Q ───┤
cp ──┘
Q ────────► HE1.power
dT ───────► HE1.out
HE1.dp ───► PU1.head        (deferred — dp is solved, not declared)
```

**Phase A**, in topological order:

| Value | Expression | Result |
|---|---|---|
| `dT` | `30 dK` | 30 K (TemperatureDelta) |
| `Q` | `30 kW` | 30 000 W |
| `cp` | `4.18 kJ/(kg*K)` | 4180 J/(kg·K) |
| `mdot` | `Q / (cp * dT)` | 30000 / (4180 × 30) = **0.2392 kg/s** — the *user's* cp, not the property backend's |
| `HE1.power` | `Q` | 30 000 W |
| `HE1.in` | `20` | 293.15 K |
| `HE1.out` | `20C + dT` | 323.15 K |
| `PU1.head` | `1.2 * HE1.dp` | **deferred** |

**Phase B.** The circuit is the **simple loop**
([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)), where `HE1` states `power`, `in`
and `out` — so the flow is pinned by the energy balance at 0.2394 kg/s and does **not** move with the
pump head. `HE1.dp` therefore depends on the head only through the small effect of pressure on density,
which is what makes this reference converge rather than collapse.

| Iteration | Pump head used | Solved `HE1.dp` | `1.2 × dp` as head | Change |
|---|---|---|---|---|
| 1 | auto-sized: 5.28 m | 20.00 kPa | 2.451 m | — |
| 2 | 2.451 m | 20.00 kPa | 2.451 m | < tol → **converged** |

Two passes, and the second only confirms. `∂dp/∂H ≈ 0` here because nothing the head does changes the
flow, so the map is effectively constant — the strongest possible contraction.

The head-to-pressure conversion uses ρ ≈ 998.2 kg/m³ and g = 9.81: 1.2 × 20 000 Pa = 24 000 Pa, ÷
(998.2 × 9.81) = **2.451 m**. That conversion is `Head` ↔ `Pressure`, and it is exactly why the
glossary insists they are different things.

**The result is a worse pump, and the tool must not hide that.** 2.451 m does not deliver the loop's
51.7 kPa, so the circuit cannot run at the stated duty on that head, and `FS2303`
([`24-auto-sizing`](../20-core-domain/24-auto-sizing.md)) reports the shortfall. A converging
fixed point is not the same as a sensible design; the expression did what it was asked, and the sizing
diagnostic is what says the answer is unusable.

**The contrasting case, worth writing a test for.** Change `HE1` to state only `power` and the flow is
free, set by the pump against the system curve. Now `dp` moves with `H` almost proportionally, the map
is `H ← 1.2·φ·H`, and the iteration walks to zero flow instead of settling — hitting `FS1405` at the
cap with its last three values, which is enough for the user to see what happened and state a head
directly.

## Acceptance criteria

- [ ] `let` bindings evaluate in dependency order regardless of source order.
- [ ] A static cycle reports every participant, in cycle order, exactly once.
- [ ] `mdot` in the worked example evaluates to 0.2392 kg/s ± 1e-4. This is deliberately *not* the
      0.2394 kg/s that [`22-component-model`](../20-core-domain/22-component-model.md) computes for the
      same duty: the script states `cp = 4.18 kJ/(kg*K)` and the property backend gives 4178 J/(kg·K).
      An expression uses the number the user wrote.
- [ ] The fixed-point loop converges on the worked example in ≤ 3 iterations.
- [ ] `head=1.2*HE1.dp` on a loop whose flow the pump sets produces `FS1405`, not a converged answer —
      the degenerate case has a test of its own.
- [ ] A deliberately divergent script produces `FS1405` with three values and no partial result.
- [ ] Every function in the table has a test for correct use and for a dimension violation.
- [ ] `HE1.nonsense` produces `FS1406` listing the real properties.
- [ ] A solved-value reference in a quantity component parameter becomes deferred, while the same
      reference used as a catalogue id or schedule time produces `FS1407` naming that pre-solve target.

## Open questions

None. Evaluated `let` values ship in the model contract's `bindings` collection so agents and future
watch UI can explain them. Deferred-expression convergence uses `fixed_point.rel_tol` plus the
dimension-specific absolute scales owned by
[`36-numerics-and-convergence`](../30-solver/36-numerics-and-convergence.md); `FS1405` fires at its
declared pass cap.
