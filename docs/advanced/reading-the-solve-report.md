# Reading the solve report

When a circuit does not solve, the diagnostic codes tell you *what* went wrong in one sentence each.
The **solve report** is the long form: every unknown the solver is looking for, every equation it has
to find them with, every value a sizing rule chose, and how close the matrix is to being invertible.

You will not need it often. You need it the day a circuit reports **"no unique solution"** and none of
the usual causes apply, because at that point the only honest next step is to count.

The report is one block of plain text, produced from a finished run — or from a circuit that never got
as far as running. Every section is described below, in the order it appears, using the tutorial's
simple loop and the distribution header from `samples/`.

## The header

```
=== m2-simple-loop.fluid
    counting     12 unknowns, 12 equations — square
    solve        Converged after 0 iterations, scaled residual 3.331E-09
    sizing       4 pass(es)
    FS2201       Using 'N1' as the pressure datum. Pressures are relative to it.
```

Four lines, and between them they answer "did this work?".

- **counting** — the pre-solve check from
  [Why a circuit has one answer](why-a-circuit-has-one-answer.md). `square` means the count balanced.
  A circuit that is over- or under-specified says so here and the solver never ran.
- **solve** — how the Newton iteration ended, and the **scaled** residual it ended at. Scaled: each
  residual is divided by the magnitude the equation is expected to have, so a pressure equation in
  pascals and an energy balance in watts are comparable. An unscaled norm is a report on the pressure
  equations and nothing else.
- **sizing** — how many times the outer loop re-ran the sizing rules. Sizing and solving alternate: a
  rule picks a valve `Kv`, the solve says what flow that produces, the rule reconsiders. More passes
  means the two took longer to agree.
- Then every diagnostic the run produced, code and message.

"Converged after 0 iterations" is not a mistake. The seed already satisfied every equation to
tolerance, which is what a good seed on a simple circuit looks like.

## The counting table

```
--- counting table
    unknowns   branch flows 1, node pressures 5, node enthalpies 5, component-owned 0,
               external fluxes 0, promotions 1
    equations  pressure relations 5, mass balances 0, energy balances 5, control volumes 0,
               stated pressures 0, constraints 2, datums 1, less enthalpy levels 1
```

This is the count from the pre-solve check, itemised. The two rows must total the same number.

Most of it is structural — one flow per branch, a pressure and an enthalpy per node, a pressure
relation per component — and you never influence it directly. The terms worth knowing:

| Term | What it is |
|---|---|
| **promotions** | Values you did not state that the solver is finding anyway, because a constraint you *did* state has to be paid for. A pump with no `head` on a circuit whose flow is fixed by a duty is the usual one |
| **constraints** | The equations your stated values add — `in=50` on a heat exchanger is one |
| **datums** | The equation that pins the arbitrary pressure zero. Exactly one per hydraulically connected part, and zero when a `supply` or `return` already states a pressure |
| **less enthalpy levels** | Balances *removed*. A closed loop's energy balances are one equation short of independent — the temperatures are only fixed relative to each other until something states an absolute level — so one is dropped |

The negative terms are where a hand count usually goes wrong. If your own count comes out one over,
the missing subtraction is almost always here.

## Constraints, and what answers each

```
--- constraints, and what answers each
    MixedInlet   on HE1        -> no promotion
    FixedFlow    on HE1        -> solved for as PU1.head
    1 with no promotion, against 1 enthalpy level(s) dropped. A level pays for one; anything
    beyond that is over-specification.
```

Every constraint your script created, and the unknown that pays for it. `FixedFlow on HE1` — the duty
and both temperatures fix the flow through `HE1` — is paid for by promoting `PU1.head`: the pump's
head becomes a number the solver finds, because *something* has to be free for the flow to come out
where the duty demands.

**A constraint with no promotion is not automatically a defect.** It can be paid for by a dropped
enthalpy level instead, which is exactly what happens above: `MixedInlet on HE1` is `in=50`, and the
loop's redundant energy balance is what it consumes. The footer states the arithmetic so you can
check it. Unanswered constraints *beyond* the levels dropped are the ones with nothing behind them,
and that is over-specification.

On the distribution header, all four are answered:

```
    MixedInlet   on HE_AHU     -> solved for as TV_AHU.position
    FixedFlow    on HE_AHU     -> solved for as PU_AHU.head
    MixedInlet   on HE_RAD     -> solved for as TV_RAD.position
    FixedFlow    on HE_RAD     -> solved for as PU_RAD.head
```

Each consumer states a duty and both its temperatures, so each gets two promotions: the mixing valve's
position, which sets the blend, and its pump's head, which sets the flow.

## Unknowns, seeded and solved

```
      # kind             owner        name                            seed        solved
      0 BranchFlow       N1->N1       branch 0 flow                   0.239213      0.239254 kg/s
      1 NodePressure     N1           N1.p                              100000   1.69406E-16 Pa
     ...
     11 Parameter        PU1          PU1.head                               0       5.26353 m
```

Every number the solver is looking for, where it started, and where it ended. In SI, always — this is
the solver's own vector, not the display layer.

Two things to look for. **A solved column identical to the seed** means the solver never moved: the
first step failed, and the numbers in the `solved` column are the seed dressed up. **A seed that is
wildly wrong** — a pump head of 0, a pressure at atmospheric on a circuit running at 3 bar — explains
a failure that has nothing to do with the model.

`PU1.head` starting at 0 is normal. A promoted parameter with no stated value has nothing better to
start from, and the line search walks it up.

## Equations, and how far each is from satisfied

```
      # kind             owner        name                            residual   unit
      0 Pressure         HE1          HE1 side-1 drop               0.0003331 Pa
      ...
      9 Boundary         N1           N1 pressure datum             1.694E-16 Pa
     10 ComponentConstraint HE1          HE1.in stated                -6.635E-09 K
    dropped as redundant: N1 energy balance (hydraulic 0)
```

The residual is how far that equation is from being satisfied, **in its own units** — pascals, watts,
kelvin, kilograms per second — not scaled. That is deliberate: 30 kPa on a pressure relation is a
number you can reason about physically, and 0.4 in scaled units is not.

On a converged circuit every residual is at the noise floor. On a failed one, sort by magnitude: the
largest is where the model and the physics disagree most, and it is usually a much better lead than
the code the solver stopped with.

The **dropped** lines name every balance removed as redundant, and are worth reading. A closed circuit
has one redundant mass balance — the last node's is implied by all the others — and one redundant
energy balance. If a circuit you believe is closed drops neither, something upstream is treating it as
open, and the count will be one short in a way no line of your script explains.

## Values chosen by a sizing rule

```
    CV1.authority        0.56 achieved against a target of 0.5 — Kv 1.6 drops 29 kPa of the
                         branch's 51.5 kPa
    CV1.kv               Kv 1.6 (R5 preferred numbers) — authority 0.56 at 0.24 l/s, 29 kPa
    P1.dn                DN25 (steel, EN 10255 medium) — 99.8 Pa/m, 0.41 m/s
```

Everything the tool chose because you did not state it, with the basis it chose on. These are not
solver unknowns — they are constants as far as the solve is concerned, recomputed between passes.

This is where to look when a result is *plausible but wrong*. A valve at `Kv 630` where you expected
`6.3` is a rule that declined to size and left a bootstrap value in place, and it will bury every
other residual in the report above.

## Rank and conditioning

```
--- rank and conditioning
    evaluated at the solved iterate, order 45
    pivots       largest 4.8, smallest 9.958E-14, ratio 2.074E-14
    rank         44 of 45, deficient by 1
```

The section that answers "is this system actually solvable, and by how much is it not?".

The matrix is the **scaled** Jacobian — the one the solver factors — eliminated with full pivoting.
`rank 44 of 45` means one equation of forty-five adds nothing the other forty-four did not already
say. **Deficient by 1** is a much more useful fact than "singular": one means look for a single
missing or duplicated relation, and several means the model is wrong in a structural way.

The `pivots` ratio is a rough condition number. Around `1e-14` and below, the deficiency is real
rather than arithmetic noise.

When the rank is short, two lists follow, and **they answer different questions**:

```
    unknowns nothing separates (the column direction — where pivoting landed):
               1  PU_RAD.head
               1  PU_AHU.head
          -0.124  TV_RAD.position
          ...

    equations that are not independent (the row direction — the redundancy):
               1  N1 energy balance
               1  N2 energy balance
               1  N3 energy balance
           0.911  TV_RAD mass balance
           ...
```

The **column** direction is the set of unknowns that move together: change them all in that
proportion and no equation notices. It tells you what is undetermined, and stating a value for any one
of them determines the rest. It does **not** name the cause — the particular unknowns listed depend on
where the elimination's pivoting happened to land.

The **row** direction is the defect. These equations are not independent; one of them is already
implied by the others, so the circuit constrains one thing fewer than it appears to. Stating something
elsewhere will not help. Weights are relative magnitudes, so the entries at `1` are the ones carrying
the relation.

Reading only the column list is the classic mistake, and it costs hours: it sends you to whichever
pump the pivoting stopped at, when the answer is a redundant balance three sections up.

In the excerpt above, the row direction is almost entirely energy balances at high weight, while the
counting table says `less enthalpy levels 0` — no energy balance was dropped. Those two facts
together name the defect: the circuit's energy relations are one short of independent and nothing
removed the redundancy.

## When the circuit never ran

A circuit refused by the pre-solve check has no iterations and no solution, and the report says so
rather than omitting the sections:

```
    solve        not run
    ...
--- rank and conditioning
    evaluated at the seed, order 12
```

The counting table, the constraints and the seed are all still there, and on an over-specified circuit
they are the whole diagnosis.

## See also

- [Why a circuit has one answer](why-a-circuit-has-one-answer.md) — the counting rules, in prose
- [How a script becomes a circuit](how-a-script-becomes-a-circuit.md) — where the unknowns come from
- [Diagnostics](../functions/diagnostics.md) — every code, including `FS3009` and `FS3010`
