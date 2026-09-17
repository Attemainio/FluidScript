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
| 7 | a ring with one branch: `HS1`, the header nodes, one injection branch | Is a ring a loop with rails top and bottom, and does a branch hang between them? | the ring as C2's loop; branches (D) in the closed form; C11 the column | -- |
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
port `ab`, so it diverts: in at `ab`, out at `a` to the recirculation and at `b` to the return
(the letters do not decide the drawing, `D-112` below).

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

**User's corrections (2026-09-17):** three. (1) A three-way valve's same-role ports are always
adjacent -- `(in, in, out)` or `(out, out, in)`, never alternating -- and the inlets are usually
drawn filled; the symbol already keeps `a` and `b` (the pair sharing a role) on adjacent sides,
so this is a note for the symbol's rendering (P5.3), not a layout change. (2) The valve belongs at
the loop's *top-right corner*: it redirects the flow from left to down, its remaining opening
flows right to the return, and the "T" stands up; that removes a bend. (3) With the valve in the
corner, `N2` should be the bottom-right corner, removing another bend, and `N1 → N2` directed left
puts `N1` under `N3`. "Test first to fix the three way valve, and check whether N2 / N1 resolve
itself."

**Drawn again (2026-09-17):** C9 (a consumer that can turn the corner takes it), C10 (a junction
beside the consumer takes the bottom-right corner), C8 widened (a corner junction's free port goes
level) and C7 widened (a loop is one root, so open ends hanging off different members pair). On
the script as written the valve **cannot** take the corner: `3WV - N2` binds `a`, so the loop
leaves the valve by the straight run, and a straight run cannot turn; the valve stays on the right
side (rotation 180, `ab` above, `a` below, `b` right), `N2` takes the bottom-right corner under
`3WV.a` at `[(2.65, −1.2), (2.85, −1)]`, `N1` hangs right of it and C7 moves it out under `N3`:
both at `x = 3.85`, three bends, length 7.6, hard 0, soft 0. A variant `step-06b-cooling-straight-return.fluid`
binds `3WV.b - N2` and `3WV.a - P1` -- same physics, the recirculation on the angle port -- and
there the valve turns the corner: rotation 90 mirrored, `ab` from the left at `(1.85, 1)`, `a` to
the right into `P1` and `N3`, `b` down into `N2` at the bottom-right corner, "T" up, two bends,
length 6.6, `N1` and `N3` both at `x = 3.45`, hard 0, soft 0. So N2 and N1 did resolve
themselves once the corner rules existed; the valve's corner is decided by the binding, and
whether the sample changes its binding is the user's call (open below).

**Second correction (2026-09-17):** the user accepted the valve, `N2` and `N1`, but the loop had
come unpacked: the valve and `N2` stood a unit clear of `PU1` where before they interfered with
it normally. The cause was C10's order of work: the junction was placed on the rail beside `PU1`
first, the consumer's column then cleared the junction's own box and the rail stub after it, and
only then did the junction slide right under the outlet -- so the column was packed against a box
that was about to move. Now the column packs against the junction's rail position itself (the
outlet at or beyond it) with the junction's box out of the clearance search, and the junction
lands where it already was: the pictures above are the packed ones.

**Rules established:** C2 amended (the consumer fallback); C8 *(step 6, provisional)*: a
junction on a loop rail takes its two loop ports along the rail and its free port on the side
facing away from the loop, level at a corner; C9 and C10 *(step 6, provisional)*: a member that
can turn a corner takes it, and a junction beside the consumer takes the bottom-right corner; C7
widened to pair open ends by loop. The valve's body decides the side: its straight run `a`–`ab`
is the loop's path when `a` is on the loop, and then the loop can only pass through it, not turn.

**Third correction (2026-09-17, `D-112`):** the session first had the sample state its ports
(`3WV.a - P1`, `3WV.b - N2`) so the loop would leave by the angle port, and the user withdrew that
reasoning: *from the layout's point of view `a` and `b` are the same port*; `PU1 - TV3.a` /
`N3 - TV3.b` and `PU1 - TV3.b` / `N3 - TV3.a` must draw the same arrangement. So the symbol offers
a `swapped` arrangement (`b` straight, `a` angle) and C9 admits any arrangement at the corner,
default first. Measured: the sample as committed (bare, recirculation on `a`) and the stated
variant draw byte-identical geometry -- every box and pipe the same, valve `rotation 90 mirrored
swapped` in the one and `rotation 90 mirrored` in the other, two bends, length 6.6, hard 0, soft
0. The sample swap was reverted before it was committed; the `6b` variant is gone. The step's final
picture is the second one above with the sample's own letters.

### Step 7 · a ring with one injection branch

`step-07-ring-one-branch.fluid`: the header's source `HS1 heat_exchanger out=60` (the power sized by
the closed-circuit balance, one load), the return datum `N1 node p=250`, and the AHU circuit of
`m2-distribution-header` verbatim -- `N3 - PA1 - TV_AHU.a`, `NM_AHU - TV_AHU.b`,
`TV_AHU.ab - PU_AHU - HE_AHU - NM_AHU`, `NM_AHU - PA2 - N5`, `N5 - N1` -- without the second branch and
its taps `N4`, `N6`. With one branch the header is not a ring on its own: the circuit is the outer
loop `HS1 → N3 → PA1 → TV → PU → HE → NM → PA2 → N5 → N1 → HS1` with the recirculation
`NM → TV.b` as an inner loop through the pump and the load.

**Drawn (2026-09-17):** the first draw laid the valve and the pump along the top rail, the load on
the right side, and left the recirculation to the router, which ran it back along the return rail
for half a unit -- `pipes-overlap`, hard. The rule that fixes it is the plan's worked example: C11,
an inner loop through the consumer stands as a column. `Column` finds the cycle through the
consumer that avoids the source by the same depth-first search as the loop, with the source marked
visited; its members on the outer loop -- `TV`, `PU`, `HE`, `NM`, every non-source member here --
become the column, and the ports the inner loop leaves the bottom member by and re-enters the top
member by become the return. The column stacks under the top rail's corner, each inlet one margin
below the previous outlet: `TV_AHU` identity at `[(1.25, −0.5), (2.25, 0.5)]` with `a` up, `ab`
down, `b` west; `PU_AHU` rotation 90 at `[(1.25, −2), (2.25, −1)]`; `HE_AHU` identity slid so its
inlet sits on the column, `[(1.65, −3.5), (2.15, −2.5)]`; `NM_AHU` at `[(1.65, −4.2), (1.85, −4)]`.
The column slid right from `x = 0.65` to `x = 1.75` -- first for the valve to clear `HS1`'s margin,
then for the return line at `x = 0.75` to clear it too. The return runs
`[(1.65, −4.1), (0.75, −4.1), (0.75, 0), (1.25, 0)]`, two bends, length 5.5, exactly the worked
example's numbers with the column at its `x`. `N1` is declared, so it has a box (A6) and takes the
bottom-right corner under `NM` (C10); `PA2` and `N5` are inline on the half-unit between them,
and the return rail runs from `N1` left to `HS1`'s inlet. Loop `[(0.15, −4.8), (1.75, 1)]`, five
bends, length 15.9, hard 0, soft 0. Steps 1–6 unchanged.

**User's corrections (2026-09-17):** "technically correct but not the intended layout", three
notes. (1) `HS1` sat at the top of a long left side; a component that can move along its
direction of flow is aligned to the middle. (2) Flow is left or right: a pump is always level;
vertical only where no other solution exists. (3) The column is the cooling loop mirrored: lay the
loop out first as a block by the same rules, then let the header resolve its routing around the
blocks -- and two subcircuits of the same shape (AHU, RAD) must then draw alike.

**Drawn again (2026-09-17):** the column became a block (C11 rewritten). `Block` lays the inner
loop out as its own clockwise ring at a provisional origin: `HE_AHU` the consumer on the right,
`TV_AHU` the first member after it that turns upward flow to rightward with `a` facing out to the
left -- rotation 270, `b` from below, `ab` to the right, stem up -- at the top-left corner, `PU_AHU`
level on the top rail, `NM_AHU` at the bottom-right corner (C10), the recirculation left along the
block's bottom and up into `b`. The outer ring then treats the block as one member: `HS1` at the
origin, the supply rail `y = 1` running straight into `TV_AHU.a` at `(0.75, 1)`, the block slid right
until the valve cleared `HS1`'s margin, `NM_AHU`'s free port to the right into `PA2`, `N5` and the
declared datum `N1`, which takes the bottom-right corner and slides along its rail until it clears
the block (C10 widened), the return rail at `y = −1.7` back to `HS1`. `HS1` moved down to
`(0, −0.35)`, the middle of a side 0.7 longer than itself (C12). The pump became a `level` kind
(C13, `D-113`): quarter turns admitted last, and a vertical pipe turns level into it before it is
turned. Five bends, length 13.9, hard 0, soft 0; steps 1–6 byte-identical. Two blemishes for the
user: the inline `N3`, `PA1` and the inferred node sit on `HS1`'s outlet stub, the run's longest
segment, rather than on the supply rail; and `N1`, being declared, is a box at the corner with
`PA2` and `N5` on the short stub before it.

**Second correction (2026-09-17):** `HS1`'s position and the pump's direction were right, but the
block's return left to the right, where the cooling loop -- "supply from right and return to
right" -- says a subcircuit's inlet and outlet face the same way. The user's take: solve the
subcircuits internally, treat each as one grouped component with one inlet and one outlet, and
let the source arrange its supply and return to them; and detect loops recursively, whatever the
script's circuit declarations say, each group one block.

**Drawn a third time (2026-09-17):** the block's split junction `NM_AHU` takes the *bottom-left*
corner (C10 mirrored, `Corners`): in from the block's bottom rail, out up the left side into `b`,
its free port the block's outlet facing left beside the inlet. `Bottom` puts the parent's return
rail level with a left-facing outlet, so `NM_AHU`'s outlet at `(1.85, −1)` lies exactly on
`HS1`'s inlet-corner height and the datum `N1` sits on that rail as an ordinary member at
`[(0.75, −1.1), (0.95, −0.9)]`, `PA2` and `N5` inline on the rail between them. `UnitOf` finds a
unit recursively -- `Column` with an `avoid` set of the enclosing rings' sources and corner
members -- so a block's consumer may itself be a block. The picture: `HS1` at the origin (no
slack this time: the block's outlet lands on the inlet corner's height), the supply rail straight
into `TV_AHU.a` at `(1.45, 1)`, the block `[(1.45, −1.1), (4.95, 1.5)]`, the return rail straight
back. Four bends, length 9.8, hard 0, soft 0; steps 1–6 byte-identical. The two blemishes of the
second draw are gone with the rule.

**Accepted (2026-09-17):** "Yes! That looks very neat now!" Two follow-ups in the same message.
(1) The rules must be general: no layout has rules of its own, groups are detected recursively
everywhere, and the cooling loop -- one inlet, one outlet -- is one group by itself. They are:
one `Loop` → `UnitOf` → `Block` path runs for every script; the ladder scripts are inputs, not
cases. The two shapes the code knows are one rule from two sides -- a standing member takes a
vertical side (the source on the left, the consumer on the right) and a free-turning member that
can turn the flow takes the corner beside its rail -- so the cooling loop is a sourced ring with
the valve at the top-right corner, and the AHU block an unsourced ring with the valve at the
top-left. (2) Groups: every ring and block is now a `LayoutGroup` (A8 as built), listed in the
diagnostic text with members and bounds -- the cooling loop `loop-1 [HE1, 3WV, N2, PU1]`, step 7
`loop-1 [HS1, TV_AHU, PU_AHU, HE_AHU, NM_AHU, N1]` holding `loop-2 [HE_AHU, NM_AHU, TV_AHU,
PU_AHU]` -- and drawn in the picture as dashed frames, wider for a group that holds another;
each placement names its innermost group. The user then narrowed it: the cooling loop is a group
because it has one input and one output, but step 7's outer ring is a closed circuit and is not.
So a ring is a group only when exactly one connection enters it and one leaves it; step 7 now
lists the AHU block alone as `loop-1`, the simple loop lists none, and the substation's secondary
`[HX1, LOAD, SP]` counts as one because its exchanger's primary ports are its one inlet and one
outlet. A first cut counted the distribution header's ring as a group too, since one connection
leaves it into the AHU branch and one comes back; for the ring that holds the source, a crossing
whose flow returns to the ring is a tap, not an inlet, so the header's ring is the drawing and
only its branches are groups.

**Observation (2026-09-17, `D-114`):** "Why is the supply heat exchanger not closer to the loop?"
The datum `N1 node p=250`, declared and so boxed (`D-108` item 3), sat on the return rail between
`HS1` and the block with its 0.2 box and two margins, and the block's outlet stub had to start
beyond them: 1.2 units of gap over the supply rail's one margin. The user chose that a declared
node with two connections is inline like an inferred one; `N1` is now a labelled point on the
return rail and the block sits one margin from `HS1`.

**Rules established:** C11 (a block, laid out first, one inlet and one outlet to its parent,
nesting), C12 (middle of a side with slack), C13 (pumps level) -- all *(step 7, provisional)*;
C10 widened both ways (a corner junction under a right-facing outlet slides along its rail until
it clears the unit; in a block the junction nearest the left side takes the bottom-left corner).

### Step 8 · the header with two branches, in parallel and in series

`step-08a-header-parallel.fluid`: `m2-distribution-header`'s circuits verbatim -- `HS1 heat_exchanger
power=54 kW out=60`, the header `N1 - HS1 - N3`, `N3 - N4`, `N6 - N5`, `N5 - N1`, the datum
`N1 node p=250`, step 7's AHU branch between `N3` and `N5`, and the radiator branch `N4 - PR1 -
TV_RAD.a`, `NM_RAD - TV_RAD.b`, `TV_RAD.ab - PU_RAD - HE_RAD - NM_RAD`, `NM_RAD - PR2 - N6` at
30 kW. `step-08b-header-series.fluid`: the same two branches in series, as the user specified it --
the radiators first at 50/40 and 20 kW, the AHU cooling what is left from 40 to the 30 °C return
with its duty sized, `HS1` stating the total 30 kW; `N4` joins the radiators' return to the AHU's
supply and, with two connections, is inline (`D-114`). The user asked that the scripts pass before
the layouts were built.

**The solve gate (2026-09-17):** the ladder got a second theory, `EveryStepSolvesAndSettles`: every
step runs through the outer loop -- the catalogue, Newton, the bore lookup -- and writes
`step-NN.solve.txt`, its `SolveExplanation`, beside the picture. A script beginning `# fragment` is
skipped (steps 1 and 2 are not circuits); one beginning `# does not settle: S-nn` is expected to
stall until that defect closes; every other step must settle. It found two things before a line was
laid. Step 7's lone valve was sized to Kv 1 against the ring's whole head and the solve ran
non-finite: `C-91`, and the script states `kv=6.3`, the header's own figure, which converges in one
iteration. The series script converges in none of the forms tried -- the AHU's duty stated or
sized, the AHU first or second: `S-63`, marked, and drawn all the same since the layout does not
read the solution. Step 4's valve states its Kv too, citing `C-66`: a Kv on a bare loop has nothing
to size against. The parallel script converges in one iteration.

**Drawn (2026-09-17):** both scripts fell to the fallback at first, for two reasons of the
machinery. The ring search found each inner loop starting from its valve rather than from its
consumer, so no member could take the block's corner; `Column` now rotates the path it finds to the
consumer. And a `load` whose power is sized (`in=35 out=30`, the series AHU) reaches the layout
with power 0 and was no consumer at all; a load with stated inlet above outlet and no power is a
consumer of nominal duty. Then the two pictures drew, and two rules were needed to make them right.

*Parallel (8a):* the ring is `HS1 → N3 → N4 → PR1 → TV_RAD → PU_RAD → HE_RAD → NM_RAD → PR2 → N6 →
N5 → N1`; the AHU branch is off the ring, from `N3`'s free port to `N5`. The radiator block, the
last inner loop on the ring, is the ring's right side exactly as step 7's block was --
`TV_RAD` at `[(5.55, 0.5), (6.55, 1.5)]` on the supply rail, `HE_RAD` at `[(8.55, −0.5), (9.05, 0.5)]`,
`NM_RAD` at `(6.05, −1)` with its outlet facing left. The AHU branch hangs between the rails (C14):
`Hang` finds, for a top-rail junction's free port, the path to a bottom-rail member with the ring
marked visited, lays the path's inner loop out as a block by C11 with the ring avoided, and slides
the block under the junction -- its top one margin under the junction's box, its inlet one margin to
the junction's right, and the junction moved along its rail to stand over that inlet so the drop
from its free port is one bend: `N3` at `(0.85, 1)`, `TV_AHU` at `[(1.35, −0.6), (2.35, 0.4)]`,
`HE_AHU` at `[(4.35, −1.6), (4.85, −0.6)]`, `NM_AHU` at `(1.85, −2.1)`. The bottom rail goes under
the hanging block -- a margin, a junction's half and a fifth more, `y = −2.8` -- and the junction the
branch returns to stands one margin left of the block's outlet, so the return is one bend too: `N5`
at `(1.25, −2.8)`. The first draw had the radiator block's descent inside `HE_AHU`'s margin (two
`pipe-in-outer`, one `pipe-beside-pipe`, the sample's own gate failing where the ladder's hard gate
passed): the ring's right unit is slid until its descent clears every box as well (`Free`). Ten
bends, length 30, hard 0, soft 0; groups `loop-1 [HE_RAD, NM_RAD, TV_RAD, PU_RAD]` and
`loop-2 [HE_AHU, NM_AHU, TV_AHU, PU_AHU]`, congruent at 3.5 × 2.6. `HS1` sits at the middle of a
side 1.7 longer than itself (C12).

*Series (8b):* the ring is `HS1 → N3 → PR1 → TV_RAD … NM_RAD → PR2 → N4 → PA1 → TV_AHU … NM_AHU →
PA2 → N5 → N1`, both inner loops on it. The last, the AHU, is the right side; the radiator block
stands on the top rail as a member with its outlet facing *on* (C11 widened): its split junction
`NM_RAD` takes the bottom-right corner under `HE_RAD` (C10) with its free port to the right, and the
rail continues level from there through `PR2`, `N4` and `PA1` into `TV_AHU.a`, so the chain steps
down from one block's outlet to the next block's inlet. `TV_RAD` at `[(0.75, 0.5), (1.75, 1.5)]`,
`HE_RAD` at `[(3.75, −0.5), (4.25, 0.5)]`, `NM_RAD` at `(3.85, −1.1)`, `TV_AHU` at
`[(4.75, −1.6), (5.75, −0.6)]`, `HE_AHU` at `[(7.75, −2.6), (8.25, −1.6)]`, `NM_AHU` at
`(5.25, −3.1)`, the return rail at `y = −3.1` back to `HS1` at the middle of a 2.5-unit side. Six
bends, length 21.7, hard 0, soft 0; `loop-1` the AHU, `loop-2` the radiators.

*What the pictures taught about the machinery.* The series radiator pump first landed three units
from its valve: the block was laid out at its provisional origin while the AHU block, built before
it, still stood there as a phantom. `Block` now lays out on a clean canvas -- every placement
suppressed, restored on return -- and every unit comes back unplaced, to be slid in by its parent.
And sliding by tenths past a row of blocks is quadratic: `Slide` jumps past each obstacle by whole
tenths, the same lattice as before, so steps 1–7 are byte-identical.

*The 200-component header:* with hanging branches in the rules, `header-200` -- eighteen injection
branches on one ring -- draws through them rather than the fallback: hard 0, soft 0, 74 bends,
length 289, in 56 ms best-of-five in a Debug build. `LayoutTimingTests` is off skip and the
distribution header's sample gates are live (`Reached`). 56 ms is over `07`'s 30 ms line: `C-92`.

**User's reading (2026-09-17):** "quite impressed how these layouts solved." The series picture
accepted without comment. One correction to the parallel one: `N3` and `N5` -- the junctions the
AHU branch hangs from and returns to -- must be aligned vertically, "the same idea as aligning
supply and return nodes vertically in a single loop". They were 0.4 apart because each stood one
margin left of its own end of the block, and the block's outlet (the split junction under the
valve's `b` port) lies half a unit right of its inlet (the valve's `a` port on its left face).

**Drawn again (2026-09-17):** the return junction stands directly under the feeding junction, and
never nearer the block's outlet than a margin: `N5` at `(0.85, −2.8)` under `N3` at `(0.85, 1)`,
the return `PA2 → (0.85, −2.1) → N5` one bend with a 0.9 stub. Ten bends, length 30.4, hard 0,
soft 0; the series picture, steps 1–7 and `header-200` unchanged.

**Margin 1 (2026-09-17):** the user, having found nothing visually wrong in steps 1–8 or the
reached samples, asked for every chart at `spacing 1` to see whether the rules break. None did:
every step and every reached sample drew hard 0, soft 0, with the bend count it has at 0.5 --
lengths 2, 4.5, 9.4, 12.4, 19.6, 11.6, 14.6, 49.7 and 34.7 for steps 1–8b. The rules are stated
in margins, not in units, and the pictures scale with the margin. The scripts were not changed;
`spacing 1` was inserted for the run and removed.

**Four in series (2026-09-17):** the user asked for four loops in series like the AHU and the
radiators: `step-08c-header-series-four.fluid`, the radiators at 50/40 and 20 kW, then AHU, floor
and DHW loads cooling the ring 40 → 36 → 33 → 30 with sized duties, `HS1` at 30 kW, `N4`–`N6`
inline between the branches (marked `S-63` like 8b). A first draw had the two middle blocks a unit
taller with their split junctions beside rather than under their exchangers: the script had written
those loads with inlet *below* outlet, so they were heaters, no consumer was found in their loops
and the junction stood in for it -- the script's error, and a reminder that a block's shape follows
from what the engine takes for its consumer. Corrected, the four blocks are congruent, 3.5 × 2.7
(2.6 for the last), each stepping down from the previous block's outlet: `TV_RAD` on the supply
rail at `y = 1`, `TV_AHU` at `−1.1`, `TV_FLR` at `−3.2`, `TV_DHW` at `−5.3`, the return rail at
`−7.4`, `HS1` centred on a 6.3-unit side. Ten bends, length 47.9, hard 0, soft 0. The question
the picture puts to the user: is a series chain a staircase -- each block level with the outlet
that feeds it, which is C11 as built -- or a row of blocks on one rail, with the rail climbing back
between them?

**Four in parallel (2026-09-17):** `step-08d-header-parallel-four.fluid`, 8a's shape with two more
taps on each rail: supply `N3 → N6`, return `N10 → N7`, every load at 50/30 (30, 24, 18, 12 kW),
`HS1` their sum. It converges in two iterations, and draws without a rule being touched: three
branches hang between the rails under `N3`, `N4`, `N5` at `x = 0.85, 4.85, 8.85` and over `N7`,
`N8`, `N9` at the same `x`, each block 3.5 × 2.6 and slid right until it clears the one before it;
the DHW branch, last on the ring, is the ring's right side. Eighteen bends, length 63.6, hard 0,
soft 0. The one asymmetry: the last branch stands on the supply rail with its valve level with
the header, one margin higher than the three that hang, because the ring's right unit is placed by
C11 and the hangers by C14.

**Rules established:** C14 (a branch between a top-rail junction and a bottom-rail member hangs
between the rails, the return junction directly under the feeding one) *(step 8, corrected once,
provisional)*; C11 widened (every inner loop on a ring is a block; a block on the top rail presents
its outlet facing on and the chain steps down; a sized load is a consumer; a block is laid out on a
clean canvas); C10 exercised for a block on a rail.

**Accepted (2026-09-17):** the corrected parallel picture, the four in series and the four in
parallel: "It works as intended, really good." The staircase reading of a series chain stands as
the rule until a picture says otherwise.

**Mixed (2026-09-17):** before step 9 the user asked for a parallel header with one branch in
series: `step-08e-header-mixed.fluid`, the AHU and the DHW at 50/30 on the outer taps, the middle
branch the radiators at 50/40 and 20 kW followed by the floor cooling that stream 40 → 30 with its
duty sized, `N11` inline between them (marked `S-63`: the series pair stalls the solve as 8b does).
The first draw left the floor loop to the fallback, hard 17: a hanging branch was one block, and
this branch holds two. C14 now reads a branch as a rail does (C11): `Ranges` finds every inner
loop along it, the first block hangs under the junction as before, each further block steps on from
the previous block's outlet through `Top`, and the last faces back to the left. The picture: AHU
hanging under `N3` at `x = 0.85`, the radiator block under `N4` at `4.85` with its split junction at
the bottom-right facing on, the floor block stepped down from it at `[(9.35, −4.3), (12.85, −1.7)]`
with its outlet facing left, the return running left under the radiator block to `N8` directly
under `N4` on the return rail at `y = −4.9`, `N7` under `N3`, the DHW block the ring's right side.
Sixteen bends, length 72.3, hard 0, soft 0; steps 1–8d and `header-200` unchanged. Accepted:
"it looks fine."

### Step 9 · a tank between two supplies and two returns

`step-09-tank.fluid`: `m4-storage-header` verbatim -- `S1 supply t=60 flow=0.12`, `S2 supply t=45
flow=0.08`, `T1 tank … in1_level=90% in2_level=30% out1_level=90% out2_level=30%`, `RAD_NETWORK
return flow=0.12`, `AHU_NETWORK return flow=0.08`, the four connections one per port. Nothing in
it is a loop; it is the ladder's first open fan and its first `upright` kind.

**Drawn (2026-09-17):** with no rule added. `S1` is the head (H10: a supply boundary) at the
origin; C5 places `T1` from `S1`'s port level, identity, its `in1` at 90 % of its height on the
west flank meeting the pipe: `[(0.6, −1.44), (1.6, 0.16)]`, `in1` at `(0.6, 0)`, `in2` at
`(0.6, −0.96)`; the other three boundaries are placed from the tank's ports by C5, each one margin
out along its port and level with it -- `S2` at `(0, −0.96)`, `RAD_NETWORK` at `(2.2, 0)`,
`AHU_NETWORK` at `(2.2, −0.96)`. The picture is the plan's row: the tank upright, charging ports
on the left, discharging ports on the right, every pipe straight. Zero bends, length 2, hard 0,
soft 0; the solve converges in one iteration. The `upright` class is *exercised* by its identity
member only: nothing here asks for the left-right mirror, and no step yet says when a tank would
take it (a tank whose charging ports are declared `out` and drawn from the right, perhaps). The
storage header's sample gates are live (`Reached`). Steps 1–8e unchanged. **Accepted (2026-09-17):**
"Looks correct."

### Step 10 · a sensor and a controller on step 4's loop

`step-10-instruments.fluid`: step 4 with `N1` written out between `HE1` and `LOAD` (inline,
`D-114`), `TE1 t_sensor at N1`, `PID1 pid kp=2`, and `control CV1 with TE1 by PID1 setpoint=50`
-- the syntax tour's short control form. It converges in two iterations.

**Drawn (2026-09-17):** the loop is step 4's, byte for byte, with `N1` a point on the supply rail
at `(2, 1)`. The instruments are placed by the rule P5.1d-1 left in `PlaceInstruments`: each
non-flow element stands one margin off its anchor's box, above first, then below, left, right,
and shifts right past another instrument. `TE1` stands above `N1` at `[(1.7, 1.5), (2.3, 2.1)]`
with its signal dropping 0.8 to the node; `PID1` stands above its actuated valve, inside the loop,
at `[(2.45, −0.2), (3.05, 0.4)]`, its actuation signal dropping 1.1 to the valve's centre and its
measurement signal rising to `(2.75, 1)` and running 0.75 *along the supply rail* to `N1`. Process
pipes hard 0, soft 0, four bends, length 7.4 -- the audit measures pipes and boxes, not signals. Two
things the text shows that the audit does not: the measurement signal is drawn from the controller
to the node rather than from the sensor, and it lies on a pipe; and the actuation signal ends at
the valve's body rather than its actuator. Put to the user with the picture.

**User's corrections (2026-09-17):** two. (1) A signal never leaves a sensor by the side the
sensor's own line leaves by: `TE1` connects downward, so its signal goes up, left or right -- here
right, the controller being to the right, then down: one bend, not the two drawn. And the sensor
must be far enough left, or the valve and controller far enough right, that the level stub is not
a scrap; "valve and PID must be aligned always"; which of the two moves was left to the session.
(2) At a crossing the polyline in front is continuous and the one behind shows a slight
discontinuity around it; signal lines are drawn behind, supply lines in front, return behind
supply, and between two circuits the hotter wins. It applies to every crossing, pipes included.

**Drawn again (2026-09-17):** C15 and C16. The sensor moves: its node `N1` is inline and free
along the rail, the valve is a loop member. `TE1` slid 0.05 left to `[(1.65, 1.5), (2.25, 2.1)]`
with `N1` under it at `(1.95, 1)`, so the stub to `PID1`'s column is a whole margin; the
measurement signal is `(2.25, 1.8) → (2.75, 1.8) → (2.75, 0.4)`, sensor to controller, one bend,
crossing the supply rail at `(2.75, 1)` where it is broken a quarter margin either side; the
actuation signal `(2.75, −0.2) → (2.75, −0.7)` ends on the valve's box at its stem side; `TE1`'s
own line `(1.95, 1.5) → (1.95, 1)`. Every route now carries a layer on the wire (`Route.Layer`:
`supply`, `return`, `signal`), the crossing belongs to the route behind, and the diagnostic text
names each pipe's layer -- the loop reads `supply` from `HE1.out` to `LOAD.in` and `return` from
`LOAD.out` round to `HE1.in`, the header's supply rail and every branch's feed `supply`, every
return and every recirculation `return`. The temperature rank between two circuits waits for a
solved state (`28` open question 3). Steps 1–9 unchanged; the Api goldens carry the layer.
**Accepted (2026-09-17):** "Yes, perfect."

### Step 11 · several circuits in one script

The plan's ten rungs are climbed and one layout sample is not reached: the syntax tour, five
independent circuits in one file. As the engine stood it chose one head, laid out its fragment
and dropped everything else into the fallback column beneath: `m1-syntax-tour` hard 71, the
picture the user called "horrific". The rule from the user: independent circuits go under each
other in script order -- `Circuit 1 / Circuit 2 / Circuit 3`, never side by side -- and the tour is
to be approached small, two closed loops first, the rest added gradually.

`step-11a-two-loops.fluid`: step 4's heating loop and step 6's cooling loop as two `circuit`
blocks, renamed apart, nothing joining them.

**Drawn (2026-09-17):** C17. `Fragments` finds the graph's connected fragments and orders them by
the script position of their first declared component (the engine's own order walks from the
pressure datum, which put the cooling loop first on the first try); each fragment is laid out by
the ring and chain rules with every other fragment's placement suppressed, measured -- outer boxes
and routes -- and moved under the one before, one margin below its outer box with left edges
aligned. The heating loop stands where step 4 drew it, `HS_H` at the origin; the cooling loop
sits under it with `HE_C` at `(0, −4.5)`, its rails at `y = −3.5` and `−5.5`, `N1` and `N3` on the
right at `x = 3.45`; each picture is byte for byte its own step's. Six bends, length 14, hard 0,
soft 0. The syntax tour falls from hard 71 to hard 4 with no other change, the four in its
radiator circuit, whose load states `power=heating` from a curve and is not read as a consumer.

**The solve gate refused the script:** `FS2213`, the heating loop's eight elements "are not
connected to the rest of the circuit". The binder takes a project as one hydraulic system and an
unconnected subgraph as an error; the language's separate `circuit` blocks say otherwise. Filed
as `C-93`; the script carries `# does not bind: C-93` on its first line and the gate expects the
refusal until it closes, as it expects `S-63`'s stall. **Accepted (2026-09-17):** "the picture
looks right now."

## What the ladder has not reached

The syntax tour's remaining circuits, added one at a time (step 11b onward); the `upright`
class's mirror. From `28` part D: the loop search's residue (open question 2 for valves), branches in
the open supply-to-return form, a block none of whose members can take a corner, a branch off the
bottom rail, and the router as a last resort. Inline elements (`28` A5) lie on every ring since
step 7; a declared pipe off a ring is still drawn as the fallback draws it.
