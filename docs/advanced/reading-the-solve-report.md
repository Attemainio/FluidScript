# Reading the solve report

When a circuit does not solve, the diagnostic codes tell you *what* went wrong in one sentence each.
The **solve report** is the long form: every unknown the solver is looking for, every equation it has
to find them with, every value a sizing rule chose, and how close the matrix is to being invertible.

You will not need it often. You need it the day a circuit reports **"no unique solution"** and none of
the usual causes apply, because at that point the only honest next step is to count.

The report is one block of plain text, produced from a finished run — or from a circuit that never got
as far as running. Every section is described below, in the order it appears, using the tutorial's
simple loop and the distribution header from `samples/`.

## Getting one

Every solved model carries its report — it is not something you assemble. A finished run renders it
directly, and a run that was refused before the solver renders the same sections from the circuit
alone, with the parts that need a solve saying so:

```csharp
var run = await loop.RunAsync(graph, fluid, name, cancellationToken);

// Whether or not the run produced a result.
var report = SolveExplanation.Render(run, graph, name);

// Or, from anything already holding a result — including a debugger watch window.
var same = run.Value.ToString();
```

The format is the same for every model, in every state. That is the point: a section is never dropped
because a particular circuit did not get far enough to fill it in, so two reports can be read side by
side.

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
| **constraints** | The equations your stated values add — `in.t=50` on a heat exchanger is one |
| **datums** | The equation that pins the arbitrary pressure zero. Exactly one per hydraulically connected part, and zero when an `inlet` or `outlet` already states a pressure |
| **less enthalpy levels** | Balances *removed*. A closed loop's energy balances are one equation short of independent — the temperatures are only fixed relative to each other until something states an absolute level — so one is dropped |

The negative terms are where a hand count usually goes wrong. If your own count comes out one over,
the missing subtraction is almost always here.

## The hydraulic partition

```
--- hydraulic partition
    [0] closed, 15 nodes, 11 branches, 29 elements
        datum N1 (stated), 0 boundaries, 1 stated pressures, unknown flux False
        one energy balance dropped as its level; coupled elements: none
```

One block per hydraulically separate part of the model — a substation has two, a single loop has one.
This is where the counting table's *negative* terms come from, so when a subtraction you expected did
not happen, the reason is here.

**Closed means no mass crosses the boundary at all**, and it decides two subtractions at once. A closed
circuit's mass balances are one short of independent, because the last node's is implied by all the
others. Its *energy* balances are one short too, for a different reason: adding the same enthalpy
offset to every node satisfies every balance and every duty relation unchanged, so one of them says
nothing new. Both redundancies have to be removed or the matrix is singular by construction.

Two things suppress the energy subtraction, and the block names both. A **coupled element** — a real
two-sided exchanger whose second side is wired into another part — reads absolute temperatures on both
sides, so the offset no longer cancels and no balance is redundant. And an **open** part takes its
level from the enthalpy arriving with the incoming mass.

`0 boundaries` with `unknown flux False` on a part reported *open* is a contradiction worth chasing:
something is being read as an opening that admits no mass. A stated pressure on an interior node is a
datum — an expansion vessel connection passes no water — and reading it as a boundary is exactly what
this line exists to make visible.

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

**When the pump's head is stated, the same line names a valve instead.** `PU1 pump head=15` takes
the head off the table, and the constraint falls to the first valve on the branch whose `kv` you
did not state:

```
    FixedFlow    on HE1        -> solved for as CV1.kv
```

That valve is then a solved value, not a sized one: it appears in the unknowns table, seeded at the
catalogue's largest Kv and solved down to what absorbs the surplus, and it appears in no sizing
basis — no rule chose it and none reports an authority for it. A loop with no such valve is
over-specified, and the report says so.

**A constraint with no promotion is not automatically a defect.** It can be paid for by a dropped
enthalpy level instead, which is exactly what happens above: `MixedInlet on HE1` is `in.t=50`, and the
loop's redundant energy balance is what it consumes. The footer states the arithmetic so you can
check it. Unanswered constraints *beyond* the levels dropped are the ones with nothing behind them,
and that is over-specification.

**One kind of statement is written to pay a level rather than falling into it.** In a closed circuit,
an `out` with no matching `in` cannot pin a flow — `power` and `out` alone are one equation in two
unknowns — but it does fix an absolute temperature, which is what the dropped level is missing. It
shows up as its own kind:

```
    EnthalpyLevel   on HS1      -> no promotion
    NodeTemperature on N3       -> solved for as TV_MAIN.position
```

That is the line to look for when a closed circuit reports **under-specified by one** and every
temperature you have stated is already answering something else. A temperature on an interior node
will not fix it — that is a [setpoint](../functions/node.md), and a setpoint brings its own unknown.
Give the source the outlet it actually holds instead.

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

**Where the seed's flows come from.** Each branch is first given a magnitude: from a duty and two
temperatures where a component states them, from a stated flow where one is stated, and from a nominal
0.1 kg/s where nothing does. Those magnitudes do not add up — nothing made them agree at a tee — so the
circuit is spanned by a tree, the branches outside the tree keep their magnitude, and each remaining
branch is *solved* as whatever closes the balance at its node.

Two things follow that are worth knowing when a seed looks wrong.

**Direction comes from the pumps.** A magnitude says how much, not which way, and a branch's stored
direction is an artefact of the order the connections were read. Where a branch has a pump, the seed
runs it the way the pump pushes; a branch with no pump takes the stored direction, which costs nothing
because nothing else in the branch cares.

**No branch is allowed to start at a standstill.** A branch solved as the leftover can legitimately
come out at zero — and a pipe's resistance law `Δp = R·ṁ|ṁ|` has no slope at `ṁ = 0`, so the solver
cannot move away from it. When that happens the seed spans the circuit again, this time refusing to
make that branch the leftover, and keeps the new tree only if fewer branches stand still. A branch that
is genuinely not flowing — a dead leg with nowhere for its water to go — stands still under every tree,
and the seed accepts it and says so. Standing water has no steady temperature of its own, so the
report shows a dead leg's nodes at the temperature of the node the leg hangs from, the same rule a
switched-off consumer's branch follows; a stub you are still typing does not stop the rest of the loop
from solving.

**Where the seed's pressures come from.** Not from a fixed ladder. The flows are chosen first, so that
every node balances; the pressures are then walked out from a starting point through the circuit,
subtracting what each component's own law says it resists at the flow it is carrying. A pump raises
the running pressure, a pipe and a valve lower it, and a three-way valve's legs come out where its own
Kv relations put them rather than all at one value. So a seed pressure is already a claim the
components agree with — everywhere the walk reaches.

It cannot reach everywhere. The walk is a tree, and a circuit has loops: each independent loop leaves
one connection whose two ends were reached separately, and the disagreement there is real. That is the
error Newton exists to close, and it is normal to see a large residual on exactly one component per
loop in a report taken at the seed. What is *not* normal is a large residual on many of them at once —
that usually means the flows themselves are far from the answer, not the pressures.

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

What a rule had to say on the way is in the notes below this table, in order, and the findings
that matter are also diagnostics with the component's name on them: a pipe stepped up a size for
velocity (`FS2307`) or past the top of its catalogue (`FS2305`), a plate count that overshoots the
duty by more than 2 % (`FS2310`), a pump sized against no flow (`FS2304`) or no resistance
(`FS2312`), and the sizes not settling within the pass cap (`FS2301`, which names what was still
moving between the last two passes — state one of those directly to break the cycle).

A parameter you stated through a curve at a component's own point ([`sized_at`](../functions/design.md#sizing-one-component-somewhere-else-on-the-curve))
is listed here too, because it was arrived at rather than typed:

```
    HP1.power            27.174 kW at tout=-5, 0.54 of the 50 kW the design day asks
```

The fraction is the number to check a bivalent choice by, and it is an outcome of the point you
chose, never something the script states.

This is where to look when a result is *plausible but wrong*. A valve at `Kv 630` where you expected
`6.3` is a rule that declined to size and left a bootstrap value in place, and it will bury every
other residual in the report above.

## Rank and conditioning

```
--- rank and conditioning
    evaluated at the seed, 10 equations x 11 unknowns
    pivots       largest 4.388, smallest 0.7509, ratio 0.1711
    rank         10: 1 unknown(s) nothing determines, 0 equation(s) the others imply
```

That one is a closed loop — a source, a load, a valve, a pump and a pipe — with **no temperature
stated anywhere**. It counts under-specified by one, and the section below it says which one.

The section that answers "is this system actually solvable, and by how much is it not?".

The matrix is the **scaled** Jacobian — the one the solver factors — eliminated with full pivoting.
It is measured whether or not the count balanced, because a circuit the count refused is exactly the
one whose rank is worth knowing: the counting table can tell you the shortfall is one, and only the
matrix can tell you *which* unknown nothing determines.

The two numbers after the rank are different failures and it matters which you have:

- **unknowns nothing determines** — the system has more freedoms than it has relations. Something has
  to be stated, or a value the tool is currently solving for has to be worked out by a rule instead.
- **equations the others imply** — a relation in the system says nothing new. Stating something else
  will not help; one of those equations has to go.

A square system can have both at once, one of each, which is what "singular" used to be reported as.
Knowing it is **one** rather than "some" is the difference between looking for a single missing
relation and concluding the model is wrong in a structural way.

The `pivots` ratio is a rough condition number. Around `1e-14` and below, the deficiency is real
rather than arithmetic noise.

When the rank is short, up to two lists follow, and **they answer different questions**:

```
    unknowns nothing separates (the column direction — where pivoting landed):
               1  N4.h
               1  N2.h
               1  N5.h
               1  N1.h
               1  N3.h

    equations that are not independent (the row direction — the redundancy):
               1  N1 energy balance
               1  N2 energy balance
               1  N3 energy balance
           0.911  TV_RAD mass balance
           ...
```

The **column** direction is the set of unknowns that move together: change them all in that
proportion and no equation notices. Here it is every node enthalpy at weight 1 — add the same amount
to all five and every energy balance still holds, which is exactly what a circuit with no stated
temperature leaves free. It tells you what is undetermined, and stating a value for any one of them
determines the rest. It does **not** in general name the cause — the particular unknowns listed depend
on where the elimination's pivoting happened to land, and on a larger circuit the list will be a
pump head and a valve position that are merely where it stopped.

The **row** direction is the defect. These equations are not independent; one of them is already
implied by the others, so the circuit constrains one thing fewer than it appears to. Stating something
elsewhere will not help. Weights are relative magnitudes, so the entries at `1` are the ones carrying
the relation.

Reading only the column list is the classic mistake, and it costs hours: it sends you to whichever
pump the pivoting stopped at, when the answer is a redundant balance three sections up.

The row excerpt is from an older run of the distribution header and is worth keeping, because it is
how a real defect was found. The row direction was almost
entirely **energy** balances at high weight, while the counting table for the same circuit said `less
enthalpy levels 0` — no energy balance had been dropped. Those two lines together name the cause with
no further searching: the circuit's energy relations were one short of independent and nothing removed
the redundancy. The reason sat one section up, in the hydraulic partition, which was calling a closed
circuit open because it carried a stated pressure on its datum node.

Neither list alone would have said that. The column direction pointed at two pumps, which is where the
elimination stopped rather than where the problem was.

On a system that is not square, one of the two lists may be withheld and the report says so:

```
    1 equation(s) are dependent and the row direction is withheld: squaring a 44x45 system
    adds rows, which are dependent by construction
```

Reading a null direction needs a square matrix, so a rectangular system is padded to reach one — and
the padding is only harmless in one direction at a time. More unknowns than equations pads with rows
of zeros: a zero row constrains nothing, so the *column* answer is exact, but it is dependent on
everything, so it would be named ahead of any real redundancy. More equations than unknowns pads with
columns of zeros and the two guarantees swap. Whichever answer the padding could have invented is
withheld rather than printed.

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
