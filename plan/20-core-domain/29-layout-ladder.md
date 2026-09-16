---
id: 29-layout-ladder
title: Layout ladder
tier: 20-core-domain
status: draft
owns: [the step-by-step derivation of the layout rules from the user's corrections, the ladder scripts and their pictures, the planned steps and the rule each is meant to force, which step established which rule of 28 part C]
depends_on: [28-layout-solver, 27-component-catalog, 26-model-contract]
traces_to: [R-22, R-27, R-44, R-45, R-46, R-47, R-48]
open_questions: 0
last_review_pass: 0
---

# Layout ladder

The layout engine is rebuilt one rule at a time (`D-107`), and this document is the record of how.
Each **step** adds to the previous step's script the smallest thing that forces one new rule --
usually one component -- the engine draws it, the user corrects the picture, and the correction
becomes a numbered rule in [`28`](28-layout-solver.md) part C. This document is the step log: what
was drawn, what the user said, which rule came of it. The rules themselves live in `28` and nowhere
else.

**How a step is run.** The scripts live in `tests/FluidScript.Core.Tests/Layout/Ladder/step-NN-<name>.fluid`.
`LayoutLadderTests` draws each to `diagnostics/layout-ladder/step-NN-<name>.svg` and writes the
`28` A10 text beside it, *before* it asserts that every component is placed and the audit finds no
hard finding. The SVG shows everything the engine reasons on: the margin area (outer box) as a
yellow field, the symbol area (inner box) as a red field, every node -- boundary, two-port or
junction -- as its own box with its name, every port as a red dot with an arrow along its flow
vector, every pipe as a blue line. **The text is what a session diagnoses from and the picture is
what the user judges** (`D-108` item 4): a session reads the `.txt`, including its raster, and
never renders or opens the `.svg`.

**A rule may arrive before its step.** The user stated four rules on 2026-09-16 that no step had
yet drawn (`D-108`); they are in `28` C marked *stated*, and the step that first draws each marks
it *exercised*. The ladder does not wait to rediscover what the user has already said.

**What the engine does with what no rule covers yet.** Nothing clever: the component goes in a
column below everything placed (`group fallback` in the text), and its connections are drawn as the
plainest L between the two anchors, without the router. A picture therefore shows exactly how far
the rules reach and nothing invented past them. The seven layout samples and `header-200` run
through the engine this way; their layout assertions are skipped until the ladder reaches them, and
`LayoutLadderTests` is the gate meanwhile.

**The engines before the ladder.** P5.1d-1's cell planner and P5.1d-2's first build (hanging, the
four-side loop search, headers) are parked outside the repository at
`~/fluidscript-attic/2026-09-16-engine/` with their pictures. Nothing of them is in the tree; what
comes back comes back as a rule a step called for, rewritten against `28` part A.

## The planned steps

Written in the future tense and not edited to match what happened; the log below is what happened.
Each step is chosen so that exactly one new question is put to the user, and the sequence is the
shortest that reaches every layout sample. Ten steps, not twenty: a step adds one component where
one component forces the rule, and a small assembly where nothing smaller does (steps 5–7), with
the reason in the row.

| Step | Script | The one question it asks | Rules expected | Reaches |
|---|---|---|---|---|
| 1 | `PU1 pump` | Where does the first component sit, and what is a boundary node? | C1, C4 | -- |
| 2 | + `HE1 heat_exchanger`, `PU1 - HE1` | How does a level chain enter a standing exchanger, and what does an inferred node cost? | C3, C4 exercised; sequential placement (D); `28` open question 1 | -- |
| 3 | + `LOAD heat_exchanger power=-30`, `HE1 - LOAD - PU1` | How is a loop closed? Clockwise, source left, consumer right, the pump on the bottom or on the vertical | C2 exercised; the loop search (D) reduced to `28` open question 2 | -- |
| 4 | + `CV1 valve`, `P1 pipe`, the nodes written out | Where do free-turning members and an inline pipe sit on the return? | the bottom side's order; A5 | `m2-simple-loop` |
| 5 | + the substation primary: `NPS supply`, `PCV`, `PP`, `HX1`'s second side, `NPR return` | Which flank does the losing side take, and where do an open chain's boundaries sit? | H10 exercised; a boundary node's side | `m2-substation` (its loop has a pipe on each rail; the sample's gate comes off skip when its text is clean) |
| 6 | the cooling loop: a junction, a three-way valve, its recirculation, supply and return | How is a junction laid out, and does the valve's straight run decide the side? | junction sides (`D-105` item 2); the injection loop as `28`'s worked example | `m2-cooling-loop` |
| 7 | a ring with one branch: `HS1`, the header nodes, one injection branch | Is a ring a loop with rails top and bottom, and does a branch hang between them? | the ring as C2's loop; branches (D) in the closed form | -- |
| 8 | + the second branch | Do two branches built alike draw alike, and in what order? | congruence (B priority 5); script order | `m2-distribution-header` |
| 9 | a tank between two supplies and two returns | Is a tank upright with charging ports left and discharging ports right? | the `upright` class; H10 for an open fan | `m4-storage-header` |
| 10 | a sensor and a controller on step 4's loop | Where do instruments and signal lines go? | instruments beside their anchors | `m1-syntax-tour` |
| -- | `header-200` | Is the engine inside `07`'s 30 ms? | none; timing | `LayoutTimingTests` off skip |

A step whose picture the user accepts unchanged still establishes its rule as *exercised*; a step
whose correction changes an earlier rule rewrites that rule in `28` and notes the step here.

## Steps

### Step 1 · one pump

`step-01-pump.fluid`: `PU1 pump`, nothing connected. The binder terminates both ports with the
boundary nodes `PU1__in` and `PU1__out` (`23`, rule I3), so the graph has three components and two
connections.

**Drawn (2026-09-16):** the pump at the origin in its drawn default, flow left to right; each
port's pipe running straight from the inner anchor to the outer anchor, one margin out, where a
tick marked the boundary node. In the text: `PU1__in node stub at (-1, 0)`, `PU1` inner
`[(-0.5, -0.5), (0.5, 0.5)]`, `PU1__out node stub at (1, 0)`; two connections of length `0.5`
and no bends; validation hard 0, soft 0.

**User's corrections (2026-09-16):** four, recorded as `D-108`. (1) Heat flows left to right and
loops circulate clockwise -- `28` H9, H10, C2. (2) Exchangers, tanks and heat pumps are never
rotated, only mirrored -- `28` A4's transform classes, C3. (3) Lay out the whole outer boundary of
every component, *including every node*, so the arrangement is visible -- `28` A6 rewritten: the
boundary nodes this step had reduced to ticks are nodes with boxes and boundaries, and the session's
reasoning that "a user who declared one component saw three" is withdrawn. (4) Check the layout
from its text, not the SVG -- `28` A10 tightened, the raster added.

**Rules established:** `28` C1 (corrected: the first component is the fragment's heat source,
which on one pump is the pump); C4 (stated: every node placed with its boundaries). C2 and C3 are
stated here and exercised by steps 3 and 2.

**Redrawn (2026-09-16), after `D-108`:** `PlaceStubs` and the stub flag are gone; a boundary node
goes through the same rule as a declared one-connection node (C4). In the text: `PU1__in node`,
inner `[(-1.2, -0.1), (-1, 0.1)]`, outer `[(-1.7, -0.6), (-0.5, 0.6)]`, its one port at `(-1, 0)`
with flow `(1, 0)`; `PU1` unchanged; `PU1__out node` inner `[(1, -0.1), (1.2, 0.1)]`; extent
`[(-1.7, -1), (1.7, 1)]`; the two connections still `0.5` long, no bends; hard 0, soft 0. Each
node's inner box is exactly one clearance from the pump's, so its outer boundary *touches* the
pump's inner box without entering it, which H2 permits. The two stubs of one connection -- the
pump port's and the node port's -- are the same 0.5 segment, which the source (§20) allows.
Awaiting the user's view of the redraw and of `28` open question 1.

### Step 2 · a pump feeding an exchanger

`step-02-exchanger.fluid`: `PU1 pump`, `HE1 heat_exchanger`, `PU1 - HE1`. The binder puts the
node `PU1__HE1` between them and terminates `PU1.in` and `HE1.out`; `in2`/`out2` are optional and
stay open. Five graph components, four connections.

**Drawn (2026-09-16):** `PU1` at the origin (C1: no source, no supply, the head of the chain).
`PU1__HE1` on the pump's axis one clearance out, inner `[(1, −0.1), (1.2, 0.1)]` (C4). `HE1`
standing in its default arrangement (C3): the level pipe leaves the node's east port, turns at the
node's outer anchor `(1.7, 0)` and drops into `HE1.in` at `(1.7, −0.6)`, the exchanger slid down
by 0.1 past the minimal drop so its inner box `[(1.6, −1.6), (2.1, −0.6)]` clears the node's
outer boundary (H2). `HE1.out` flows down and `HE1__out` hangs below it at `(1.7, −2.2)`. Audit:
hard 0, soft 0, one bend, length 2.6.

**Two corrections the text forced before the user saw it.** (1) The first draw gave `HE1` its
`u` arrangement, because `u`'s `in` faces a level pipe with no bend and B ranks bends first; but
`u` sends `out` back to the left, reversing the path, which H10 forbids. The chain now tries the
default arrangement straight, then C3's turn, and only then an alternative. (2) A hung node took
no clearance check and `HE1__out` landed inside `PU1__HE1`'s margin; every sequential placement
now slides along its axis by tenths until H2 holds, so slack goes into pipe.

**User's correction (2026-09-16):** "why do we keep a node between pump and heat exchanger? Pump
and heat exchanger should interfere." The node `PU1__HE1` is binder plumbing the script never
wrote; it takes no place. `28` A5 now counts an inferred two-connection node as inline, C4 is
narrowed to the nodes that are not, and open question 1 is closed.

**Redrawn:** `PU1` at the origin; the pipe leaves `out` at `(0.5, 0)`, turns at the pump's outer
anchor `(1, 0)` and drops to `HE1.in` at `(1, −1)`, the exchanger `[(0.9, −2), (1.4, −1)]` slid
down until its box clears the pump's outer boundary at `y = −1` -- a drop of one unit, twice the
margin, because H2 is measured against the pump now. `PU1__HE1` sits inline at `(1, −0.5)`, the
midpoint of the run's longest segment, drawn as a hollow dot. `HE1__out` hangs below at
`(1, −2.6)`. Hard 0, soft 0, one bend, length 2.5. Extent `[(−1.7, −3.2), (1.9, 1)]` against
`[(−1.7, −2.8), (2.6, 1)]` before: narrower by the node's 1.2 and the exchanger's slide, deeper by
0.4.

**Still for the user:** whether the exchanger should stand higher with the corner further out
(the drop is now a whole unit), and whether a source exchanger in a chain should flow up.

**Rules established:** C3 *exercised*; C4 *exercised and narrowed* (A5's inline node); C5 (sequential placement, `28` D's first candidate)
*provisional*: from every placed port the next element goes along the port's axis, one clearance
out or as far as H2 needs, in the first admitted transform of its default arrangement whose port
faces the pipe.

### Step 3 · the loop closed with a load

`step-03-loop.fluid`: `PU1 pump`, `HE1 heat_exchanger power=30 in=20 out=50`, `LOAD
heat_exchanger power=-30`; `PU1 - HE1`, `HE1 - LOAD`, `LOAD - PU1`. Three members, three inferred
nodes, no open port.

**Drawn (2026-09-16):** `HE1` is the source (C1: the largest positive duty) and takes the left side
flowing up, rotation 180 so its through-pass faces the loop: `in` at `(0.15, −0.5)`, `out` at
`(0.15, 0.5)`. The rails sit one margin outside its ports, `y = 1` and `y = −1`. Nothing lies
between source and load in flow order, so the top rail is bare pipe. `PU1` is the one member on
the return and sits on the bottom rail pumping left (rotation 180), its box pushed to
`[(0.75, −1.5), (1.75, −0.5)]` by the clearance from `HE1`. `LOAD` is the consumer (the most
negative duty) and takes the right side flowing down, identity, its inlet corner on the top rail
at `x = 2.35`: as far right as the bottom rail's pump needs plus a slide of 0.1 to clear the pump's
margin. The loop is the rectangle `[(0.15, −1), (2.35, 1)]`, walked clockwise: down the right,
left along the bottom, up the left, right along the top (H9). Four bends, one per corner; length
5.4; hard 0, soft 0. The three inferred nodes sit at the midpoints of their runs' longest segments.

**User's corrections:** *(awaiting)*. `28` open question 2 is in front of the user here: the pump
on the bottom rail, or on the left vertical under the source as the earlier sketch had it.

**Rules established:** C2 *exercised*, as built: the source at the origin in the transform that
sends its outlet up, the rails one margin outside its ports, the top members placed rightwards
from the outlet corner and the bottom members rightwards from the inlet corner against the flow,
the consumer's column at the longer rail's end. C1 amended: for a loop's source the transform is
C2's, not the drawn default.

### Step 4 · a valve on the return

`step-04-valve.fluid`: step 3 with `CV1 valve` between the load and the pump: `LOAD - CV1`,
`CV1 - PU1`. Four members, four inferred nodes. No engine change.

**Drawn (2026-09-16):** the return now carries two members, placed rightwards from the source's
inlet corner against the flow: `PU1` first at `[(0.75, −1.5), (1.75, −0.5)]` as before, then `CV1`
one clearance on at `[(2.25, −1.3), (3.25, −0.7)]`, rotation 180 so its outlet faces the pump. The
load's column moved right to `x = 3.85` to stand past the valve. Still four bends, one per corner;
length 7.4; hard 0, soft 0. Every run on the rails is straight; the valve's two runs are 0.25
long each, the clearance split by the inline node between valve and pump.

**User's corrections:** *(awaiting)*.

**Rules established:** none new; C2's bottom rail *exercised* with more than one member, in
flow order read from the right.

### Step 5 · the primary on the exchanger's second side

`step-05-primary.fluid`: step 4 with `NPS supply t=85 p=600`, `PCV valve`, `PP pipe length=12
dn=25`, `NPR return p=350`; `NPS - PCV - PP - HE1.in2`, `HE1.out2 - NPR`; `HE1` now states
`in2=85 out2=45`. The plan's row allowed an assembly here because nothing smaller puts a chain on
a flank.

**Drawn (2026-09-16):** the loop of step 4 unchanged, `HE1` still rotation 180, so its second
side is the left flank (H10: the losing side left) with `in2` on top and `out2` below. The
primary hangs from those two ports by C6: `in2`'s pipe rises its straight margin to `(−0.15, 1)`
and turns left, `PCV` stands on that line at `[(−1.75, 0.7), (−0.75, 1.3)]` facing right, slid
left until its box clears the exchanger's margin, `NPS` one clearance beyond it at `(−2.35, 1)`;
`PP` and the two inferred nodes are inline, spread evenly along the run's longest segment at
`x = −0.6, −0.45, −0.3`. `out2`'s pipe drops its margin to `(−0.15, −1)`, turns left, and `NPR`
sits at `(−0.85, −1)`. Six bends, length 10.1, hard 0, soft 0.

**Three corrections the text forced before the user saw it.** (1) The first draw gave `PP` a box:
the inline pass-through followed one inline element and placed whatever came next, and here node,
pipe, node come in a row. The walk now follows any chain of inline elements to the next boxed one
and spreads them along the run's longest segment (`D-105`). (2) `NPR` hung straight below `out2`
and the loop's return pipe ran through its margin (soft, `pipe-in-outer` twice). C6 is scoped to
a *loop member's* flank ports and applies to boundary nodes too, so the return turns back left;
step 2's chain exchanger still hangs its continuation straight. (3) The loop walk left the
source by its first leaving port, which with the second side connected could have been `out2`;
it now tries each.

**User's corrections (2026-09-17):** two. (1) *"PCV or CV1 valve should be mirrored instead of
rotated, because it preserves the T on top. Same for the pump... later we would like to add an
inverter symbol on top."* `D-109`: ties break by the smaller turn first, then unmirrored; the
return's pump and valve are now `rotation 0 mirrored`, and the picture is otherwise unchanged
(hard 0, soft 0, six bends). (2) *"The polylines from component to another already represent the
pipe... we should find a way in the syntax to overwrite the pipe DN"* -- `PU1 - HE1 - LOAD - PU1
DN25`. A language decision, filed as `L-51` with options; the user chose option A the same day,
`D-110`, package `08` P5.1e; the ladder keeps declared pipes until it lands. (3) *"Could NPS and
NPR be aligned vertically... if the pipe is open ended and it has a supply and it has no obstacles
to align with supply, do it"* -- C7: `NPR` moved from `(−0.85, −1)` to `(−2.35, −1)` under `NPS`,
its pipe now `(−0.15, −0.5) → (−0.15, −1) → (−2.25, −1)`, length 11.6 in all, still six bends,
hard 0, soft 0. The pair is found by the loop member both chains hang from, followed through
two-port components (`NPS` touches the valve, `NPR` the exchanger); the nearer end moves out to
the farther one's line when no placed box or margin lies on the way.

**Rules established:** C6 *(step 5, provisional)*: whatever hangs from a loop member's flank
port runs level, away from the loop on that flank's side, after the port's straight margin. C7
*(step 5, provisional)*: an open end aligns with its supply. A5 *exercised* for a declared pipe
and for a chain of inline elements. `D-109` (mirror before half turn) and `D-110` (pipe
properties on a connection line) came out of this step.

### Step 6 · the cooling loop

`step-06-cooling.fluid`: the `m2-cooling-loop` sample verbatim -- `HE1` (power 30), `3WV`,
`PU1`, `P1`, the junction `N2`, `N1 supply`, `N3 return`; `HE1 - 3WV` binds the valve's common
port `ab`, so it diverts: in at `ab`, out at `a` to the recirculation and at `b` to the return.

**Drawn (2026-09-17):** three engine changes first, none of them a new picture rule. The loop
walk is a depth-first search over the ports the fluid leaves by, through junctions as well as
inline elements, because this loop runs `HE1 → 3WV → N2 → PU1 → HE1` through a junction and
the valve's first leaving port is not the one on the loop. The loop has no standing consumer, so
C2 takes as its right side the first member the flow *leaves the loop by* -- the valve, whose `b`
goes to the return. A junction sits on a rail like a component (`OnRail`) and its free port takes
the side facing away from the loop's centre (`FreeSide`). The picture: `HE1` left flowing up,
rotation 180; `3WV` right flowing down, rotation 180 so `ab` is on top, `a` below and `b` to
the right; `N2` on the bottom rail at `[(2.25, −1.1), (2.45, −0.9)]`, `PU1` mirrored pumping
left; `N1` hangs below `N2` at `(2.35, −1.7)`; `N3` hangs right of `3WV.b` at `(4.55, 0)` with
`P1` inline between. Loop rectangle `[(0.15, −1), (3.45, 1)]`, four bends, length 8.4, hard 0,
soft 0. The `m2-cooling-loop` sample draws identically.

**User's corrections:** *(awaiting)*. The supply below the junction and the return to the right
of the valve do not align (C7 pairs only level approaches on one side); whether the primary
should read as one pair of terminals is the question this step puts.

**Rules established:** C2 amended (the consumer fallback); C8 *(step 6, provisional)*: a
junction on a loop rail takes its two loop ports along the rail and its free port on the side
facing away from the loop. The valve's body did decide the side: its straight run `a`–`ab`
had to be vertical for the loop to pass through it, which is what put it on a vertical side.

## What the ladder has not reached

Everything in `28` part D past a chain and one loop: direction-changing free-turning components, the loop search's residue (open question 2), rigid groups, branches, the router as a last resort. Inline
elements (`28` A5) are specified but no step has needed one yet; until one does, a declared pipe is
drawn as the fallback draws it.
