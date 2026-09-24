---
id: 28-layout-solver
title: Layout solver
tier: 20-core-domain
status: draft
owns: [the layout model (coordinates, flow vectors, anchors, margins, transform classes, inline elements, nodes), the layout standard the engine is held to, the layout rules as the ladder establishes them, the candidate algorithms, the layout diagnostic text, determinism]
depends_on: [23-topology-and-graph, 25-layout-hints, 26-model-contract, 27-component-catalog]
traces_to: [R-22, R-27, R-44, R-45, R-46, R-47, R-48]
open_questions: 1
last_review_pass: 0
---

# Layout solver

Core computes where every component sits and where every pipe runs (`D-103`), in world units, and
hands the frontend a finished drawing. This document is the specification of how, in four parts:

- **A. The model** -- what a drawing is made of. Settled.
- **B. The standard** -- what every drawing is held to. Settled; it is the acceptance test.
- **C. The rules** -- how a drawing is produced. Established one at a time by the ladder
  ([`29`](29-layout-ladder.md)): the user corrects a picture, the correction becomes a rule, the
  rule is numbered here and in the code. A rule the user states ahead of a step enters here at once,
  marked *stated*, and becomes *exercised* at the first step that draws it.
- **D. The candidates** -- the algorithms the user's specification
  ([`28-layout-solver.source`](28-layout-solver.source.md), verbatim) proposes for the parts that
  need a search. Each is admitted into C when a ladder step proves it, and deleted if none does.

Nothing in D is used by the engine until it is numbered in C. A picture the engine produces
therefore shows exactly how far the rules reach: what no rule covers is put in a fallback column
below everything placed and left to the router, never guessed.

## A. The model

### A1. Coordinates

World units: a pump is 1 × 1. `y` grows **upward**; `(0, 1)` is up, a box's `Y` is its bottom edge.
The frontend flips once, where it maps units to pixels. Every direction the engine reasons with is
one of the four axis vectors `(1,0) (0,1) (-1,0) (0,-1)`; every rotation is a multiple of 90°;
mirroring is a separate transformation. Every coordinate that leaves the engine is rounded to a
millionth of a unit, so a point reached two ways is one point.

### A2. Components: two boxes

Every component has an **inner box** -- its symbol, after the arrangement and transform chosen for
it -- and an **outer box**, the inner grown by the margin *m* on every side. *m* is `0.5` unless
the script's `spacing` says otherwise. The inner box is hard geometry: no other symbol and no pipe
that does not serve it ever enters it. The outer box is soft: two outer boxes may overlap and a
pipe may cross a margin; that is counted, never rejected.

**Clearance:** two components are placed correctly when their inner boxes are at least `max(m_A,
m_B)` apart, which is the same as "neither inner box enters the other's outer box".

### A3. Ports: inner anchor, outer anchor, flow vector

Every port has an **inner anchor** on the inner box's edge, an **outward direction** (the unit
normal of that edge, carried by the catalogue and turned with the box), and an **outer anchor** one
margin along the outward direction, on the outer box's edge. The stub between them is a straight
pipe a whole margin long; a pipe never bends inside a margin.

Every port also has a **flow vector**: the direction the fluid moves at the port. It is *not* the
outward direction. For an outlet the flow is outward; for an inlet it is inward; a supply boundary
sends, a return boundary receives; a port that says nothing (a junction's side, a `Bidirectional`
port) takes its flow from the port it is joined to, and failing that from the way the connection was
written. A pump pumping left to right has `inlet.flow = (1,0)` and `outlet.flow = (1,0)`; a
standing exchanger fed at the top has `in.flow = (0,-1)`. A component that turns the flow is one
whose inlet and outlet flow vectors differ.

The **flow-oriented graph** is the graph with every connection directed by these vectors. It is
what B's H9 and H10 are measured on and what D's classification runs over. It is nominal --
roles and propagation, never the solved flow -- so the compile and the solved drawings are one
drawing (`D-105`).

### A4. Symbols, transforms and the transform class

The catalogue (`27`) gives each kind one symbol: a box, strokes, and named anchors with outward
directions in symbol space, y up. A symbol may offer **alternative arrangements** of the same ports
on the same box; the exchanger has `u` (each side in and out on its own flank) beside its
through-pass default; a three-way valve's `swapped` arrangement puts `b` on the straight run and
`a` on the angle, because the two switched ports are interchangeable on paper (`D-112`) and the
renderer labels them. An instance is drawn by a **transform**: arrangement × mirror × quarter turn.

**Which transforms a kind admits is a fact about the kind, and hard** (`D-107`, `D-108`). The
catalogue carries it as the kind's **transform class**:

| Class | Transforms | Kinds | Why |
|---|---|---|---|
| `free` | four quarter turns, each mirrored or not: 8 | `valve`, `three_way_valve`, `pipe`, `node` | Nothing about the glyph is up or down |
| `level` | the same 8, the quarter turns admitted last | `pump` | A pump pumps left or right (`D-113`); it stands vertical only where nothing level fits, and a pipe turns level into it first (C3) |
| `standing` | no quarter turn; identity, left-right mirror, up-down mirror, both: 4 | `heat_exchanger` (every spelling: `load`, `boiler`, `chiller`, …), `heat_pump` when M4 adds it | An exchanger is drawn upright on every P&I diagram; its flanks and its flow sense are chosen by mirroring |
| `upright` | identity and the left-right mirror: 2 | `tank` | The layers and the port elevations are a vertical order; an up-down mirror would put the hot layer at the bottom |

Nothing in A prefers one admitted transform over another; C decides. On the wire (`26`) an up-down
mirror is written as `rotation: 180, mirrored: true`, which is the same transform; the diagnostic
text names the mirrors (`mirror-x`, `mirror-y`, `mirror-xy`) for a standing or upright kind and the
turn for a free one.

### A5. Inline elements

A declared `pipe`, a pipe-expansion child, and **a node with exactly two connections, declared
or inferred** (`D-114`; a datum node on a rail is the case) are **inline**: they have no box and
no clearance of their own. The chain of
connections through them is one **run** between the two elements that do have boxes; the run is
one polyline; the inline elements are points on it, spread evenly along the run's longest segment
(`D-105`). Since `D-110` a connection line may carry the pipe's properties itself, and the implicit
pipe it lowers to is inline the same way. The drawing shows only a pipe's label and, in the ladder picture, a hollow dot
with the node's name -- the line *is* the pipe. (The user's step 2 correction: `PU1 - HE1` puts a
node between the two that the script never wrote, and the pump and the exchanger must sit at the
clearance from each other, not from it.)

### A6. Nodes

Every node that is not inline (A5) is laid out as an element (`D-108`): the junction's inner box
(0.2 across), an outer boundary of one margin, a place of its own, its name in the picture and in
the text. That holds for a junction (three or more connections, declared or not) and for a
boundary node the binder added to terminate an open port (`23`, rule I3); `PU1 pump` alone is one
pump and two nodes, drawn so. A declared node with two connections is inline (A5, `D-114`). What the *canvas* draws for a node is `53`'s: a junction (three or more
connections) is a dot with at most one pipe per side, a two-port node draws nothing.

### A7. Pipes

A pipe is an orthogonal polyline from inner anchor to inner anchor, normalised: no duplicate points,
no collinear interior points, no backtracking, so `bends = points − 2`. Where a later pipe crosses
an earlier one it carries a **hop** at the crossing. A pipe's **envelope** is the union of the
rectangles one margin around each segment; it is what the standard's clearance tests measure.

### A8. Groups

Once a set of components has been laid out together it is a **group** with bounds, ports and
allowed transforms, and its parent treats it as one object: translated, turned, mirrored where
allowed, never re-laid inside. A change elsewhere in the system cannot disturb a correct group. A
group's kind and members are on the wire (`layout.groups`).

*As built by step 7 (2026-09-17):* a ring the engine lays out is a group of kind `loop`,
orientation `cw`, **when it is one component to the rest of the system: exactly one connection
enters it and one leaves it** (the user's definition). The cooling loop is one group -- its supply
enters at the mixing junction, its return leaves at the valve; an injection branch is one group
inside the header; the closed circuit of a whole drawing is not a group. For the ring that holds
the heat source, a tap whose flow comes back to the ring is a branch of the closed circuit, not
an inlet or an outlet: the header's ring with its branches crosses nothing that leaves the
system, so it is the drawing, while each branch is a group inside it. Groups nest, outer listed before inner,
`loop-1`, `loop-2`, … in that order. A group's members are its boxed components; a nested block's
members are also its parent's. Its bounds are the union of the members' inner boxes and the
routes between them. The diagnostic text lists the groups (A10) and a placement names the
innermost group that placed it; the picture draws each group's bounds as a dashed frame with its
id. The scene's groups are not on the wire yet -- P5.1d-3, with the layout report.

### A9. Determinism

Identical input gives identical output. Never dictionary or hash order, timing, randomness, or a
floating-point near-tie without a rule. Ties break by: script order, lower component id, the default
arrangement, the **smaller clockwise turn, then unmirrored before mirrored** (`D-109`: a symbol
reversing on a line is mirrored, so a valve's stem and a pump's badge stay on top), lower x, lower y.

### A10. The diagnostic text

**The layout is read from its text, not from its picture** (`D-106` item 10, `D-108` item 4). The
text is a Core output -- the layout report `D-100` asked for and `62` describes, one text and not
two -- and for every scene it lists:

- each component with its group, transform (the class's vocabulary, A4), arrangement, inner and
  outer boxes, and each port's inner anchor, outer anchor and flow vector;
- each connection with its points, length, bends, crossings, hops and envelope;
- the groups, with kind, orientation, members and bounds;
- **a character raster of the arrangement** *(shipped 2026-09-18, P5.1d-3)*: the scene's extent
  at four cells per unit, first row at the top; outer boxes `.`, inner boxes `#`, a node `o`,
  pipe `-` and `|`, a signal `:`, a route's own bend `*`, a crossing `+`, a port's flow `<>^v` at
  its anchor, and each component's name inside its box where it fits, else just above it -- so a
  session sees the arrangement a picture would show;
- the audit's verdict: every hard constraint of B with its count, every soft class with its
  count, totals of bends and length, and the metrics `62` trends rather than gates: the pipes'
  length over their ends' Manhattan distance, the symbol area over the extent's, the extent's
  aspect;
- every finding, one per line, with the rules that last placed or laid the two things it names (`D-155`);
- **who placed what** *(`D-155`, 2026-09-24)*: each component's block says the rule that last placed it and why,
  each pipe's the rule that laid it; the verdict names each fragment's form with the forms that declined before it,
  and the three pipes that run furthest past the distance between their ends;
- **the placement trace** *(shipped 2026-09-20, `C-107`)*: every decision the engine made, in the
  order it made them -- each fragment's declared members; which member is its head and by which of
  C1's fallbacks (the largest positive duty, else the first inlet, else the first member with
  nothing upstream, else the first declared); each form tried in order (C2 sourced loop, C20 ring
  of one, C19 open supply-to-return, C18 unsourced ring, then the C1 chain) as *drawn* or *declined*
  with the reason it declined; and every placement as `rule -- member: reason; centre, transform`
  (C2 the source, C9 the corner, C11 a unit and its slide, C14 a hanger's junction, C3/C4/C5 the
  sequential rules, C7 an aligned open end, C15 an instrument's side on its host, A5 an inline cut, `fallback`). The
  sections before it are the result; this is the reasoning. The cooling loop's up/down picture
  (`C-105`) took the engine's source to explain twice in one day; the trace answers it in five lines:
  head `N1` by the inlet fallback, C2 declined (no source), C20 declined (a boxed member besides the
  head), C19 declined (no unit on the ring path), C18 drawn with `3WV` as the left turner.

The text is `SceneText` in Core since 2026-09-18 (`C-89` closed; it was in Core.Tests before). The
ladder and the sample gates write it to `diagnostics/`; nothing on the wire carries it yet.

A test writes the text *before* it asserts anything, so the file on disk is always the layout that
failed. The SVG beside it shows the same facts -- margin areas, symbol areas, every node, every
port's flow arrow, every pipe -- and is for the user; a session that renders or reads a picture to
check a layout is doing the wrong thing.

### A11. Labels *(shipped 2026-09-22, `C-84`)*

A label is a box, not a point. Every placement carries `LabelBox`, the rectangle its text reserves,
and `LabelAt` is that box's centre. The box is `advance × characters × size` from a metric Core
declares and the wire carries (`LayoutWire.LabelMetric`: size 11/60 world unit, the canvas's 11 px
at 60 px per unit; advance 0.62 em), never from measuring rendered text (`D-73`, [`53`'s label
geometry](../50-frontend/53-canvas-renderer.md)). The text is the component's tag where it has one,
else its id (`D-34`), so the box is sized for what the canvas draws.

Labels are laid out **last**, against everything else in place: symbols, pipes, signals. Each
label starts just outside its owner's inner box on the side the symbol's label anchor names, and
when that box collides -- enters another inner box, another label already placed, or is crossed by
a route segment -- it slides along that edge in quarter-unit steps up to a unit either way, then
tries the opposite side, then the other two, in the same steps. The first clear position wins; when
none is clear the least-collided one is taken and `LabelClear` is `false`, which is the renderer's
cue to draw a leader from the label to its owner rather than drop the tag. Labels are placed in
scene order, so the result is deterministic (A9) and the earlier label of a pair keeps its first
position. An inline element's label and an instrument's tag inside its bubble are boxed where they
stand and not moved. The extent takes every placed label's box in.

Three soft findings measure the result: `label-in-inner`, `label-in-label` and `line-in-label`
(`53` invariant 3a). Every ladder step and every sample is clear -- no finding and no leader --
which is what `LabelLayoutTests` holds on the busiest steps; the text (A10) prints each label's box
and `leader` when one was needed.

## B. The standard

What every drawing is held to, whatever rules produce it. The notation references are ISO 10628,
ISO 14617 and ANSI/ISA-5.1, which fix how symbols and instruments are drawn and say nothing about
placement; the placement standard is this project's reasoning, and H9–H10 are the user's convention
stated as constraints (`D-108`).

**Hard -- a drawing that breaks one is wrong, not worse.**

| # | Constraint |
|---|---|
| H1 | No inner box enters another inner box |
| H2 | No inner box enters another component's outer box (the clearance, A2) |
| H3 | No pipe, and no signal line, passes through an inner box it does not serve |
| H4 | A pipe starts and ends on the two ports its connection names |
| H5 | A pipe leaves a port along the port's outward direction for a whole margin |
| H6 | A pipe turns on bare pipe: no inline element sits on a corner |
| H7 | Supply and return never share a pipe segment |
| H8 | Every component and every connection is drawn; nothing is dropped |
| H9 | **Every flow loop runs clockwise**: each simple directed cycle of the flow-oriented graph (A3), walked in flow order through its members' centres, encloses negative signed area (y up) |
| H10 | **Heat progresses left to right**: a two-sided exchanger's losing side is its left flank and its gaining side its right flank (`D-36`'s edge decides which is which); a fragment's first process path starts at its heat source -- a supply boundary, a tank's charging ports, or the member with the largest positive stated duty -- and flows right (a tank shared by two loops takes a loop per flank, `D-157`) |
| H11 | **Two pipes never run side by side closer than a margin**, except two runs of one symbol within that symbol's clearance, where the port pitch decides (`C-96`). *Stated 2026-09-23 (`D-153`); the audit enforces it when the new engine takes over (part E), since the old one never kept it -- until then it is the soft `pipe-beside-pipe`* |
| H12 | **Every pipe and every signal line is an orthogonal polyline** (A7): each segment level or plumb. *Measured since `D-155` (2026-09-24); a run a rule lays skew is refused and left to the router (E4)* |
| H13 | **A junction takes one pipe per side** (A6, `53`): no two pipes leave a junction's dot in the same direction. *Measured since `D-155`* |

H9 and H10 together fix, for a loop with a standing source and a standing consumer: the source on
the left side flowing up, the consumer on the right side flowing down, supply along the top to the
right, return along the bottom to the left. For a closed distribution ring the same pair puts the
supply header along the top and the return header along the bottom with the branches hanging
between them, which is `R-48`'s picture re-derived from topology; for an open supply-to-return
path with no ring, `D-107`'s stacked branches remain the candidate (D).

**Soft -- counted, and the fewer the better.** A pipe through a margin, *or exactly along its edge*
-- the clearance is `≥ m` between inner boxes, and a pipe one margin from a box it does not serve
satisfied a strict interior test and drew as a line brushing the clearance (`C-87`); a sensor's
clearance touching the pipe it measures is C15's line one margin long and not a finding; a *terminal node's*
clearance yields to a sibling run of the same symbol -- the tank's second supply at the symbol's
0.96 port pitch under a margin of 1.0 (`C-96`), a node being a point and its outer box a
convention, the same allowance the beside test makes for two runs of one symbol; a pipe running beside
another closer than a margin (hard as H11 once the new engine takes over, `D-153`); two pipes crossing; a pipe running through another run's inline point, where a sensor on the point would read as
measuring either pipe (`D-155`); two outer boxes overlapping; a signal line running
along a pipe (a signal crosses pipes freely: C16 hops it); and, since labels are boxes (A11), a
label entering another inner box, two labels intersecting, or a line crossing a label -- soft
because the layout keeps the label with a leader rather than dropping it, and the count says how
busy the picture got.

**Priorities, in strict order, when a choice remains.**

1. The hard constraints.
2. The fewest bends. A straight pipe beats a shorter pipe with corners on every drawing an engineer
   has seen; four short bends never beat one slightly longer one.
3. The fewest crossings, then the fewest soft findings.
4. What stands, stands; a free-turning member keeps its drawn default where the pipe leaves it open.
5. Alignment: equivalent assemblies -- the same kinds in the same order -- are drawn congruently.
6. Compactness -- least pipe, least area -- **last**.

**Edit stability.** Adding a component to one branch moves only that branch and what it pushes along;
an edit inside one group leaves every other group identical up to translation; a change to a value
moves nothing. Adding an instrument may move what stood where its bubble needs to be (`D-151`: it is
part of its host's footprint, and the layout makes room for it).

**The audit.** `SceneAudit` measures every hard constraint and every soft class on every scene; the
tests assert hard = 0 on every ladder step and, when the ladder reaches them, on every sample. Since
the `C-88` package (2026-09-17) that is all ten: H1–H3 and H6 as box and pipe tests, H4 and H5 on the
route's ends (H5 followed through inline points, since a node on a straight line is not a bend), H7 as
collinear overlap, H9 as the signed area of every simple directed cycle of the flow-oriented graph,
H10 as the losing side's flank where a duty is stated. A hard constraint nobody measures would be a
preference -- and two were: H6 and H8 had no check until `D-155` (2026-09-24), which measures them (H6 as the
directions the two pipes at an inline point leave it by, H8 as a pipe per connection and a placement per
component, a pipe with cells drawn as its cells), H10's tank clause (the connected charging ports left of the
discharging ones), and the drawing rules A6 and A7 as H12 and H13. The kinds live in one table,
`SceneAudit.HardKinds` / `SoftKinds`, which the report prints from. The `C-95` package (2026-09-18) closed the three gaps the tour's faulty
picture had shown: a connection's sense is read from the flow along the route's first segment, so a
cycle through inline nodes -- where both anchors sit on one point -- is enumerated; a pipe is excused
from its own two components' clearance but never from their bodies; and a signal line is measured
against every inner box but its two ends' (hard) and along every pipe (soft). `SceneAuditTests`
holds each on a scene bent to break it: the loop samples mirrored, a pipe bent back through its
own component, a signal through an exchanger and along a pipe, and a pipe laid exactly along an
unrelated box's outer edge. **Since the edge counts (2026-09-20), three pictures that were soft 0
are not**: the parallel header's consumers pack at exactly one margin (C11's slide, C14's hang),
so the next branch's supply drop and return run along the previous coil's outer edge for half a
unit -- `08d` soft 6, `08e` soft 1, `header-200` soft 48, every one of them this shape. The audit is
right and the packing is now an open question (`C-108`): a margin plus one raster step between
consumers, decided when a picture is judged.

## C. The rules

Numbered in the order they were established; each names its step and is marked in `LayoutEngine`
with the same number. *Stated* means the user gave the rule ahead of the step that draws it;
*exercised* names the step that did; *provisional* means drawn but not yet corrected.

- **C1** *(step 1, corrected 2026-09-16; amended by step 3)* -- **The first component placed sits
  at the origin in its drawn default**: identity transform, the default arrangement, flowing left
  to right -- unless it is a loop's source, whose transform is C2's (outlet up). Which
  component is first is H10's: the fragment's heat source, else the first component of `Order`
  (`25`), never an inferred one. (Step 1 drew it as "the first the script declares", which is the
  same component on one pump and the wrong one on a loop whose source is declared second.)
- **C2** *(stated 2026-09-16, `D-108`; exercised by step 3, provisional)* -- **A flow loop is
  laid out clockwise, its source member on the left side flowing up and its consumer member on the
  right side flowing down.** H9 and H10 as construction: the two standing members take the
  verticals; the members between them in flow order take the top (source → consumer) and the
  bottom (consumer → source). As built: the source sits at the origin in the first admitted
  transform of its default arrangement that sends its loop outlet up and takes its loop inlet from
  below; the rails lie one margin outside those two ports (the bottom rail lower if the consumer is
  taller); the top members are placed rightwards from the outlet corner by C5, the bottom members
  rightwards from the inlet corner *against* the flow, each by its outlet facing the source; the
  consumer's column stands at the longer rail's end, its inlet corner on the top rail, slid right
  until H2 holds; every run's inline nodes sit at the midpoints of their longest segments. Which
  free-turning members leave the bottom for a vertical: a pump never, unless nothing level fits
  (`D-113`, C13); the rest is open (below). *Step 6*: a loop with no
  standing consumer takes as its right side the first member in flow order the loop's fluid
  leaves by -- a diverting valve, a junction with an outlet off the loop; the loop is found by a
  depth-first walk over leaving ports that passes through junctions.
- **C3** *(stated 2026-09-16, `D-108`; exercised by step 2)* -- **A standing kind is never
  turned.** Its transform is chosen among the class's mirrors (A4) so that the port the pipe
  arrives at faces the pipe: a chain reaching an exchanger from the left enters its left flank's
  top port, turning once at the port's outer anchor; the exchanger does not lie down and the chain
  does not turn vertical. A two-sided exchanger's left-right mirror is H10's (losing side left).
  *Exercised by step 2*: the pipe turns at the node's outer anchor and drops into `in`.
- **C4** *(stated 2026-09-16, `D-108`; exercised and narrowed by step 2)* -- **Every node that
  is not inline is placed with its boundaries** (A6): a boundary node sits one clearance past the
  port it terminates, on the port's axis. A two-connection node, declared or inferred, is a point
  on its run (A5, `D-114`) and the far element is placed as if piped directly.
- **C5** *(step 2, provisional; widened by step 11b)* -- **Sequential placement.** From every placed port, the element
  at the other end of its connection is placed along the port's axis, one clearance out or as far
  as H2 needs (slack goes into the pipe, in tenths of a unit): a node on the axis; a component in
  the first admitted transform of its *default* arrangement whose port faces the pipe -- among
  those, the one that sends a port the flow leaves by to the right (H10, step 11b: a three-way
  valve fed from below takes its second inlet from the left and discharges to the right, mirrored
  or turned the other way as that needs); a standing
  component that cannot face a level pipe by C3's turn; and only then an alternative arrangement,
  because an arrangement that faces the pipe by sending the outlet back the way the pipe came
  reverses the path (H10) -- B's bend count does not get to buy that.
- **C6** *(step 5, provisional)* -- **What hangs from a loop member's flank port runs level, away
  from the loop.** A standing exchanger on a loop has its loop side on one flank (C2) and its
  other side on the outer flank; whatever is piped to the outer flank -- a chain, a boundary node
  -- leaves the port along **two** straight margins, turns towards that flank's side and continues
  level: the substation's primary arrives from the left into `in2` at the top and its return
  leaves `out2` at the bottom back to the left. Two margins, not one, since `C-86` (2026-09-20):
  the loop's own rail on that side runs along the outer anchors' line, and a corner on it put the
  primary's approach and the secondary's rail on one line 0.3 apart -- a reader saw a pipe crossing
  the exchanger's top with a gap. One margin further out, the two lines separate. Off a chain's
  exchanger (step 2) a port's continuation hangs straight.
- **C7** *(step 5, provisional; widened by steps 6 and 11b)* -- **An open end aligns with its supply.** Where a supply and a
  return boundary -- or any two open ends, the terminating nodes the language infers for open
  ports as much as declared boundaries (step 11b) -- hang level off the same loop on the same side
  (a loop is one root: its members are not told apart, and a two-port chain between the boundary
  and the loop is walked through), the nearer
  one is moved out to the farther one's line when no placed box or margin lies in the way, and
  its run is laid again over the longer pipe; the two then read as one pair of terminals, the
  return under the supply. Where something is in the way, each stays where its own rule put it.
  Declared boundaries of one fragment share one root whatever they hang off (step 11d's
  correction): the tour's `SB1` off the mixing valve and `NB1`, `NB2` off the junctions read as
  one column of terminals, the nearer ones moved out to the farthest.
- **C8** *(step 6, provisional)* -- **A junction on a loop rail.** Its two loop ports lie along
  the rail; its free port takes a side no port uses, the one facing away from the loop's centre
  first -- vertical on a level rail, level on a vertical side, and level at a corner (C10) so that
  an open end there lines up with the loop's other open ends (C7) -- and what hangs from it is
  placed by C5 from there.
- **C9** *(step 6, provisional)* -- **A member that can turn the corner takes it.** A loop's
  consumer position is the top-right corner, not the right side, when the consumer has an admitted
  transform whose loop inlet faces left and whose loop outlet faces down: the
  top rail runs straight into its inlet, the right side descends from its outlet, and the loop has
  one bend fewer. Only when no such transform exists does the consumer stand on the right side, in
  from above and out below. The corner may use any arrangement the symbol offers, the default
  first (A9): a three-way valve's two switched ports are interchangeable on the drawing (`D-112`),
  its symbol offers `a` straight or `b` straight, and a loop leaving by either letter turns the
  same corner. What the layout may not do is move a connection to a different port (H4).
- **C10** *(step 6, provisional)* -- **A junction beside the consumer takes the bottom-right
  corner.** When the first member after the consumer in flow order is a junction, it does not sit
  on the bottom rail with a bend beside it: it slides right along the rail to stand under the
  consumer's outlet, the descent lands on it from above, and the rail leaves it leftwards. The rail
  is set low enough beforehand for the junction to fit one margin under the outlet. The
  source-side corners are the same rule mirrored, not yet built because no step has needed them.
- **C11** *(step 7, corrected twice the same day; widened by step 8; provisional)* -- **An inner loop is a block,
  laid out first, presenting one inlet and one outlet to its parent.** When a cycle through the
  consumer avoids the enclosing rings' sources -- an injection branch: the valve whose common port
  feeds a pump, a load and the junction that returns to the valve's other port -- that cycle is
  laid out on its own by these same rules as a clockwise ring: its consumer's unit on the right;
  the first member after the consumer that can turn the flow from upward to rightward with the
  branch's inlet facing out to the left takes the top-left corner (the three-way valve: `b` from
  below, `ab` to the right, `a` from the left); the members between them lie on the bottom rail,
  and the last of them, a junction, takes the *bottom-left* corner -- in from the rail, out up the
  left side, its free port the block's outlet facing out to the left beside the inlet (C10
  mirrored); the rest lie on the top rail. The block is the cooling loop mirrored: there the
  supply came from the right into the mixing junction at the bottom-right corner and the return
  left to the right from the valve at the top-right; here the supply comes from the left into the
  valve at the top-left and the return leaves to the left from the junction at the bottom-left.
  The parent ring then treats the block as one member with one inlet and one outlet (A8): the
  supply rail runs straight into the inlet, the return rail leaves the outlet level, the block is
  slid right until every member clears what is placed, and the parent's source arranges its
  supply and return to them (C2, C12). Units nest: the consumer of a block is itself found by the
  same search, with the enclosing rings' sources and corner members avoided, so a loop within a
  loop within a ring is three blocks. Two blocks built from the same script shape draw the same:
  step 8's radiator and AHU blocks are 3.5 × 2.6 each. *Widened by step 8:* every inner loop along
  a ring is a block, not only the one through the consumer. The last in flow order is the ring's
  right side with its outlet facing back; each earlier one stands on the top rail as a member with
  its outlet facing *on* -- its split junction at the bottom-right corner (C10) with the free port
  to the right -- and the rail continues level from that outlet into the next member, so a chain of
  blocks steps down from outlet to inlet (the series header). A block is laid out on a clean canvas:
  nothing placed before it is an obstacle to its own arrangement, and it is slid into its parent
  afterwards, jumping past each obstacle by whole tenths. A load whose power is sized from stated
  inlet and outlet temperatures is a consumer of nominal duty, so a series branch's second load is
  found. Not built yet: a block none of whose members can take the corner (a pump and a load
  alone), and an inner member that is neither on the outer loop nor inline.
- **C12** *(step 7, provisional)* -- **A member on a side with slack sits at the side's middle.** The
  rails' span is set by the taller side; the member on the shorter side -- the source when the
  block is tall, a consumer entered from above when the source is -- moves to the middle of its
  side and its two stubs lengthen equally. The user's words: "if the component can move in its
  direction of flow, it should be aligned middle". In the open form (C19) the block stands on
  both rails, entered level from the top rail and leaving level into the bottom one, so it has no
  stub to lengthen: where the chain is taller than the block, the block is laid again with its own
  bottom rail lower by the difference and its hung unit down by half of it, so its outlet meets
  the rail level and the return runs straight (step 11d's correction).
- **C13** *(step 7, `D-113`; a column's riser since `D-159`)* -- **A pump is level.** It pumps left or right; a quarter turn is
  admitted only where nothing level fits, and a vertical pipe turns level into a pump (C3's turn,
  rightwards) before the pump is turned to meet it. This answers open question 2 for pumps: they
  never leave a rail for a vertical. The one exception is a column: a pump on a branch drawn as a column --
  boilers in parallel, each with its circulator -- stands in the riser (`D-159`).
- **C14** *(step 8, provisional)* -- **A branch hangs between the rails, under the junction that
  feeds it and over the one it returns to.** When a top-rail junction's free port leads, off the
  ring, to a bottom-rail member, the path between them is an injection branch: its inner loop is
  laid out as a block (C11) with its outlet facing left beside its inlet, and the block hangs one
  margin under the junction's box with its inlet one margin to the junction's right. The junction
  moves along its rail to stand over that inlet, so the feed is one drop and one bend; the bottom
  rail is set a margin, a junction's half and a fifth more under the lowest block; and the junction
  the branch returns to stands on the bottom rail *directly under the one that feeds it* -- as a
  loop's supply and return nodes align, the user's correction to the first draw -- and never nearer
  the block's outlet than a margin, so the return is one bend too. Branches hang in script order, each slid right until it clears what hangs
  before it: `D-108`'s branch rule in the closed form. A branch is a chain like a rail (C11): every
  inner loop along it is a block, the first hangs, each next one steps on from the previous block's
  outlet, and the last faces back to the left, so a series pair inside a parallel branch draws as
  the series header does (step 8e). The ring's right unit is slid until its own descent to the
  bottom rail clears every box as well. Not built yet: a branch off the bottom rail, a branch whose
  bottom member is not a junction, and a boxed member on a branch before its first block. A branch
  with no inner loop hangs as a column (E3, P6.10; the ladder engine has no rule for it, `C-126`).
- **C15** *(step 10, corrected once, redrawn by `D-151`)* -- **An instrument is drawn on its host, as
  one footprint.** A sensor stands on the node it reads -- a point on one pipe or a terminal (`D-150`)
  -- and a controller on the device it actuates, each joined to its host by a straight line one margin
  long: from the node's point, or from the middle of the device's edge. Its side is the host's first
  free side, a side no connection leaves by, in the order: the device's actuator stem (a valve's is up
  in its drawn default, a three-way valve's right, opposite its angle port), then up, down, left, right --
  except that a sensor first tries the side its controller takes on its own host, where that side lies
  across the sensor's pipe, so the signal between them need not cross it (`D-158`);
  the first whose bubble keeps the clearance from every box, bubble and drawn pipe of its fragment, else
  the first free side. The layout makes room rather than searching for it: while a form places a
  component, a device's bubbles count in the clearance as its box does, every stub -- the first margin
  of pipe out of a port -- keeps out of every bubble's clearance, and a run carrying a sensor's node
  (`Reserve`) is laid at least long enough that, cut evenly by A5, each bubble on it keeps a margin
  from the boxes at both ends and from the next bubble. A fragment's extent includes its bubbles, so
  the next circuit stacks under them (C17). The only signal routed is a controller's measurement, from
  the sensor it reads through, and it crosses the drawing (`D-152`): only an inner box stops it, a
  margin is a cost, it never runs along a pipe and crosses one only a quarter margin or more from the
  pipe's ends; it takes the fewest bends, then the shortest way, crossings and time in margins breaking
  ties; it leaves a bubble by any edge but its stalk's, on a stub of half a margin. A control line that
  measures a node directly reads it through the sensor the binder puts there (I8), so a controller is
  never joined to a node. Not built: a host
  with no free side (a four-way junction, which `D-150` refuses to measure).
- **C16** *(step 10, provisional)* -- **At a crossing the route in front runs through and the one
  behind breaks.** Every route carries a layer -- `inlet`, `outlet`, `signal` -- and the picture is
  drawn from the back: signals, then return pipes, then supply pipes. Where two routes cross, the
  one drawn behind owns the crossing and is broken for a quarter margin either side of it; between
  two of one layer, the later one. A pipe is supply from a heat source -- a supply boundary or an
  exchanger's gaining outlet -- until the flow passes a losing side, a consumer's first side or a
  source's second; every other pipe is return. The user's full rule ranks two overlapping circuits
  by temperature, the hotter in front; the layout is solved before any temperature is, so the layer
  stands in for it (open question 3).
- **C17** *(step 11, provisional)* -- **Independent circuits stack top to bottom in script order.**
  The graph's connected fragments are found first; each is laid out by these rules on a canvas of
  its own, with nothing of the others placed, and is then moved under the fragment before it -- its
  outer box one margin under the other's, left edges aligned -- the fragments in the order the
  script declares their first components. A component connected to nothing is a fragment of one.
  *Step 11b made the code match `D-114` (A5, C4):* a boundary node is never inline, whatever meets
  it -- it is an end of the plant, and two streams into a return meet at its box, not head-on on
  one line.
  The user's words: "put them under each other", never side by side. Not built yet: the
  fallback column for what no rule places still hangs under the last fragment, and a fragment
  whose head has no loop and no boundary takes its first declared component as the head.
- **C18** *(step 11c, provisional; widened by step 3b, `C-100`, and step 3d, `C-102`)* -- **A loop with no heat source is a ring with a bare left
  side, and so is a loop with no known duty at all: its first exchanger takes the consumer's seat,
  and with no exchanger either, any boxed member but the head -- the first that is not a pump.** The tour's transient loop and its radiator loop are a pump, a load and a valve on a
  closed ring with nothing that gains heat: nothing is a head by C1 and the ring rule (C4) has no
  source to stand on the left. The consumer still sits on the right (`D-108`), so the ring is laid
  out from it: the loop is found through the consumer and the run before it is the top rail, the
  run after it the bottom, rotated so that the member the flow reaches last before the consumer's
  rail stands where the source would -- and where a node that is not a boundary lies on the
  turn, it takes the corner as a boxed junction (C4); where none does, the corner is a bare bend,
  the top rail's pipe turning down into the bottom rail's, drawn as one run. The rails are the
  ring's rails: level, the pump on the top one, the valve wherever the file put it, the loop
  clockwise (H9). Who is a consumer and who a source is read from the file, not from a solved
  duty (C1 widened): a stated negative power or a load's written kind (`load`, `radiator`, `cooler`,
  `chiller`) is a consumer, a stated positive power or a `heater`/`boiler` a source, and a load
  whose power is a curve (`power=heating`) is a consumer by its kind alone, since the layout is
  solved before any curve is read. Not built: a sourceless loop with a branch, and where the
  bare corner should turn when the valve is the last member before the consumer. *Built 2026-09-20
  (`C-105`, ladder step 6c):* the last bottom member that can turn the flow from leftward to upward
  takes the bottom-left corner -- C9 mirrored, placed by its outlet on the left side's line and its
  inlet on the rail -- and the left side rises out of it; a junction there stays C10's. The same fix
  set the loop's centre for C8: this ring is built from its corner at the origin, so the centre had
  stayed there and the corner junction's own free port, at zero distance from it, fell to the default
  order and went up; it is now the ring members' mean, and the corner's open end goes level, where
  C7 columns it with the other. The cooling loop written as a consumer (`load` or `power=-30`)
  draws as the mirror of step 6, hard 0, soft 0, two bends, length 6.6.
- **C19** *(step 11d, re-keyed under `D-115`, provisional)* -- **An inlet whose junction feeds two
  paths to one outlet's junction is the open form of the ring: the inlet's junction is the left end
  of the top rail and the outlet's junction, directly under it, the left end of the bottom rail; the
  inlet and the outlet hang off their junctions' left sides, one margin out, level.** A boundary
  has one connection (`D-115`), so the form is read through it: the inlet's one link leads to the
  junction, and both paths end on the outlet's junction, which is stripped from them. (An inlet
  wired to several paths, an `FS2205` error, is still drawn, the inlet standing as the junction.)
  With one path and no inner loop there are no rails: the chain hangs straight down under the
  junction and the outlet stands at its foot (step 11b, the user's correction). The paths from the
  junction to the outlet's junction are found by the branch search (C14) from each of the
  junction's other connections; the first with no inner loop is a
  chain and hangs straight down under the supply, each member placed from the one before as on a
  vertical rail, into the return's top; the path with an inner loop is laid out as a ring path
  (C11: its last inner loop is the unit on the right, fed level from the supply's right side) and
  its return runs from the unit's outlet down to the bottom rail and left into the return's right
  side. The bottom rail's height is the lower of what the chain needs (the return one margin
  under the chain's last member) and what the unit needs (C12); a chain longer than the unit has
  the unit laid again that much deeper (C12), so its return meets the rail without a step. The
  junction's other connections -- a path to a second outlet, a valve fed off it -- leave by the
  sides the form leaves free, up first, and are placed by the chain rule (C5), which is how
  step 11b's mixing valve keeps its place above the supply. The user's picture: the supply on the
  left feeding rightwards along the top, the return under it collecting from the right along the
  bottom, the loads between. Not built: more than two paths to the return, a chain path whose
  member turns level, a path with a junction on the bottom rail after the unit (the code passes it
  to `Close` untested), and a supply that is not a boundary node.

- **C20** *(step 3c, `C-101`)* -- **A component connected to itself is a ring of one: it stands at the
  origin in its drawn default, and the pipe leaves the outlet by its margin, walks the outer box's
  edge clockwise round to the inlet's stub and enters by its margin, the inline elements between
  cut into that return.** `PU1 - PU1` binds as `PU1.out → PU1__PU1 → PU1.in`, which is what the
  syntax says, and no other rule can seat it: C2 wants a source, C18 a consumer, and a pump is
  neither, so before this rule C5 seated the pump at the origin and its inferred node one row under,
  and the router drew the return from `out` *leftwards along the centreline* through the pump's own
  box -- against A3, against the audit's `pipe-in-inner`, and unreported, because the audit ran over
  fixtures only. Both stubs end one margin out, which is on the outer box's edge (A2), so the return is
  the edge itself: for a pump, out to the right, down, back under the symbol, up into the inlet; for an
  exchanger, whose outlet is at the bottom and inlet at the top, down, left, up its left side and in
  from above. Clockwise is H9; the walk never enters the inner box and never runs shorter than a
  margin, by construction. Only a fragment whose one boxed member is the head is a ring of one; a
  member whose stubs do not end on its outer box -- a port anchored inside the symbol -- is left to
  the rules after it, and the audit says so. The picture is a fact about the wiring, not the physics:
  a pump on itself cannot solve, and the plant is drawn red (`57`).
- **Every layout is audited.** The audit (A10, `SceneAudit`) was the ladder's gate and nothing
  else's; `C-101` was a picture it would have failed, handed out as if correct. Since `C-101`
  `ModelContractBuilder` runs it over every solved layout and reports each hard finding as an
  `FS5002` warning (`53`'s reserved code) naming the rule, the two parts and the geometry, so a
  breach reaches the log rather than the eye alone. Soft findings stay the ladder's business.

## D. The candidates

The user's specification proposes these for the parts of a layout that need a search. Each is a
candidate until a ladder step needs it, at which point it is written into C in the form the step
proved; a candidate no step ever needs is deleted.

- **Sequential placement** (source §7–10): the first process path flows to the right; each next
  component is placed along the current flow vector at the clearance, in a transform whose inlet
  faces the pipe; a component that turns the flow turns everything after it. A chain lays itself out
  with no search. Under C3 a standing component never turns the chain; the pipe turns into it.
- **Topology before geometry** (source §11): Tarjan's strongly connected components classify the
  flow-oriented graph before anything is placed -- sequential paths, splits, merges, simple loops,
  complex cycles. A **simple loop** is a cycle where every member has one internal predecessor and
  one internal successor.
- **The loop search** (source §12–19, narrowed by `D-108`): a simple loop is a rectangle. The
  orientation is clockwise (H9), the standing members' sides are C2's, so what remains to search is
  which contiguous run of the free-turning members between them sits on each side and each member's
  transform; candidates are scored lexicographically -- valid, then bends, crossings, clearance,
  vertices, length, area, interference, mirrors, preference, tie-break. A member may take a corner
  with its own geometry. For the substation's secondary (HX1, SS, LOAD, SR, SP: two standing, one
  pump, two inline pipes) the search is over where the pump sits -- on the bottom pumping left, or
  on the left vertical under the exchanger pumping up -- and nothing else.
- **Rigid groups and hierarchy** (source §24–25): a solved loop, chain or branch set is one object
  to its parent (A8). *Built by step 7 for an inner loop (C11): the block is laid out at a
  provisional origin, measured, and slid into the ring with its runs.*
- **Branches** (source §26, narrowed by `D-108`): a closed ring's branches hang between its top and
  bottom sides in script order (H9 makes the ring a clockwise loop, so the rails are given); an open
  supply-to-return path's branches stack perpendicular to the main flow in script order, the parent
  placing the split and the merge and routing with minimum bends. *Built by step 8 for the closed
  ring (C14); the open form is not yet needed.*
- **The router**: for whatever connection the rules leave, an orthogonal path over the placed boxes
  and pipes, bends before length, crossings dear but not forbidden. It is never asked to discover a
  layout. *Admitted for signals by `D-152`* in a mode of its own: inner boxes block, margins cost, a bend
  is worth 2 units of length, crossings and margins 0.25.

## E. The engine *(D-153, 2026-09-23; being built as package P6.10)*

The engine that draws C's rules, restructured after the first one grew a form, a clearance test and a way
of drawing a pipe per rule (`D-153`). Three stages, each its own type; nothing in a later stage changes
what an earlier one decided.

### E1. The circuit view

Built once per scene, read by everything after it.

- **Ports and peers.** Every link indexed by both ends, so a port's peer is one lookup. Every port's flow
  (A3) is computed once: role, else the joined port's role, else the boundary, else the writing.
- **Runs** (A5). The inline elements -- pipes, pipe cells, two-connection nodes that are not
  boundaries -- are collapsed first: a run joins two boxed elements' ports and carries its links in order
  and its inline elements. Everything after this stage speaks of runs, not links.
- **Fragments** (C17), in script order of their first declared member.

### E2. Decompose

Each fragment becomes a tree of structures before any geometry exists.

- **Terminals.** The fragment's head (C1: the largest positive duty, else the first inlet, else the first
  member with nothing upstream, else the first declared) fixes the two terminals: a source's outlet and
  inlet, or an inlet and the outlet whose paths from it take in the most runs (C19). Without either, the
  consumer cuts the loop (C18), and a member joined to itself is a ring of one (C20). An open form whose
  body is one loop between two terminal runs -- the cooling loop written as a consumer, step 6c -- is a
  ring fed from outside, and is drawn as C18 draws it.
- **The body** is the biconnected block holding a virtual link between the two terminals: exactly the
  elements on some path from one to the other. Everything else hangs off it at one port, as a pendant.
- **Series-parallel reading.** Between the two terminals the boxed graph is read as a series-parallel
  composition. A **parallel group** is a split element and a merge element joined by two or more
  disjoint paths. Its paths' flow (E1) decides what it is:
  - all flow split → merge: a **header** -- the paths are **branches** (C14; C19 in the open form; a
    plain zone, `C-126`);
  - some flow each way: a **loop** -- the ring itself at the top (C2, C18), a **block** below it (C11).
- **The spine** (`D-154`): at a header the ring runs on through the branch declared last -- the one whose
  first declared member comes latest -- and every other branch hangs between the rails in script order
  (C14). A header's taps therefore lie on its rails, and the spine of the last header is the ring's right
  side (C11's unit).
- **Attached rings** (`D-157`). A pendant that is not a tree, touching the body at exactly two ports of one
  element -- one leaving it, one entering -- is a second loop through that element: a buffer tank's distribution
  loop, its boiler loop being the ring. It is decomposed as a ring of its own whose head is that element, and
  printed in the trace under its own heading.
- **Chains.** What hangs off a port and ends in a boundary or an open port is a chain (C4, C5, C6), and
  the open ends of a ring are paired (C7).
- **Instruments** (C15) are attached to their hosts here, so a host's footprint knows its bubbles from
  the start.
- **The remainder.** A piece that is not series-parallel -- a bridge, say -- is a *loose* structure:
  chain rules place it, the router joins it, and the trace names it. Nothing is stacked in a fallback
  column and no pipe is drawn without its stub (H5).

The tree is printed in the trace (A10) before any placement: one line per structure with its kind,
members and parent.

### E3. Compose

Bottom-up: every structure lays itself out on its own canvas, then reports its **footprint** and its
**port anchors** to its parent, which places it as one object (A8).

- **Footprint.** The members' inner boxes, their instruments' bubbles (`D-151`) and the bands of the pipes
  laid inside it (A7). **Occupancy** holds every placed footprint and answers every placement's one
  question -- does this footprint keep the clearance from everything placed -- with one test: inner
  boxes, bubbles and pipe bands against each other's margins, a symbol's own port pitch exempt (H11).
- **Run length.** One calculation for every run: a stub at each boxed end, and room for each inline
  point's bubble (`D-151`'s `L · min(t, c+1−t)/(c+1) ≥ m + s/2` and `L · (t2−t1)/(c+1) ≥ s + m`).
  Every rule that lays a run asks it; none reserves room of its own.
- **Rails.** A ring or a block lays its members along a top rail and a bottom rail from its left
  side's two ports (C2), turns corners with the members that can (C9, C10), hangs its branches between
  the rails under the junctions that feed them and over the ones they return to (C14), and stands its
  right side at the longer rail's end (C11) -- its consumer read by the side the ring passes, so a heat source's
  second side is a consumer (`C-132`). A member on a side with slack sits at its middle (C12). A
  ring's members, its loops and its header branches are read from the decomposition, never searched
  for in the graph: the path runs through each header's spine and each loop's forward way, a loop past
  the ring's start is a block, and every other branch hangs from its split.
- **Plain branches** *(built P6.10 R4)*. A branch with no loop is a vertical chain under its split, each
  member facing down the drop, laid on a canvas of its own and slid right as one until it clears the
  ring's left side and everything placed by the one test (boxes, bubbles, stubs); the split moves along
  its rail to stand over it. Its merge stands on the bottom rail directly under the split, the rail's
  cursor stopped short of it by the run length the merge's onward run needs, and the bottom rail lies a
  whole return's run length under the column (bubbles included). Where the bottom rail still cannot
  put the merge under its column -- a pump on the rail before it, a long reserved run -- the ring is laid
  again from the state it started in with that split held over the merge; four passes at most. The first
  pass lays the bottom rail as if no column hung over it, so the merge it finds is where the rail itself
  wants it: a column hung early would push a tall rail member (a pump) past itself, and a split held over
  the merge that pushed it keeps the gap after the pump has moved back, since a hold only moves right. A merge
  a column returns to never takes C10's corner under the right side's outlet. A block's merge is held the
  same way, a margin short of its outlet.
- **Headers in series** *(built 2026-09-24, `D-156`, `C-131`)*. A header whose branch returns to a merge on the
  ring's path before the ring's right side, with a consumer on its spine, is a band: laid by the same rail rules as
  the ring (`Top`, C11's unit on the band's right side from `ConsumerOf` bounded to the band, `Close` for its return),
  its return ending at a step on its left -- the band split's x less the merge's half-width and the step run's length
  -- from which the next band's rail runs on rightwards, a margin plus the two rails' half-heights lower. The two
  halves of the step's run (the return down to the step, the rail on from it) are joined into one run. The last
  band closes the ring to the source. Only single-member consumers on the bands and no block on the ring's path so
  far; anything else falls back to one band.
- **A branch that rejoins its rail** *(built 2026-09-24, `C-130`; over the rail since `D-158`)*. A plain branch whose
  split and merge both stand on one level rail -- a duty/standby pump pair -- is not hung: it runs as a row parallel
  to the spine. On the ring's top rail the row stands a margin *over* the highest spine box, up from the split, along
  the row, down into the merge, raised until it clears everything placed -- out of the ring, the branch declared first
  highest -- and where its boxed members and the spine's carry the same symbols in order, each stands square over its
  counterpart (`D-158`). On any other rail it runs a margin under the lowest spine box, down from the split, along
  the row, up into the merge, as below. It is
  laid when the rail reaches the merge (the spine is placed by then), on a canvas of its own, then lowered until
  it clears everything placed by the one test; the merge is held right of the row's end by the rise's run length,
  and the bottom rail keeps a margin under the row. Where the rail turns between split and merge the branch is
  left to the chain rules. Pumps side by side on branches between a common suction and a common discharge line is
  how HVAC schematics draw a pump set ([The Engineering Mindset, chilled-water schematics](https://theengineeringmindset.com/chilled-water-schematics/));
  stacking the rows under a level rail is this project's mapping of that, the part most worth the user's eye.
- **Sources in parallel** *(built 2026-09-24, `D-159`, `C-129`)*. Cut at its head, a ring whose head sits on one
  branch of a header of heat sources reads, between the first junction after the head and the last before it, as a
  loop: forward through the consumer, back through the sibling source. That loop is the ring itself. Its forward way
  stays on the ring's path; its way back is a sibling that rises as a column from its split on the bottom rail into
  its merge on the top rail -- laid top down under the merge on a canvas of its own, each member facing up the riser,
  slid right until clear with the merge moved over it, its split then held straight under it by C14's rule for a
  merge. The head's own branch, from the junction before it to the one after, is the left side as a column, top down
  from the top-left corner the same way, so the siblings stand level with it. A pump stands in a riser (C13).
- **The spine as a column** *(built 2026-09-24, `D-158`)*. Where a ring's or a band's right side is a consumer on a
  header's spine with members of its own between the split and it, that stretch is C11's unit, laid as the header's
  hanging columns are (C14): on a canvas of its own, each member facing down the drop, its inlet a split's half-height
  and a run under the rail -- level with the siblings' first members -- the top rail turning down into it at a corner.
  Its return's points are cut on its drop rather than its longest segment (A5), so a sensor there stands where the
  siblings' do, and the drop is at least that run's length; the column is not centred on its side (C12). The unit
  slides by the one test -- boxes, bubbles and stubs -- as every placement does (E3), not box against box.
- **A loop per flank** *(built 2026-09-24, `D-157`, `C-129`)*. Where the element an attached ring shares is a tank,
  the ring's two ports stand on its east flank and the source ring's on its west, at their stated elevations (a
  flank override on the sheet; the symbol's rule by name holds everywhere else). The tank is the source ring's
  consumer unit (C11), its west inlet and outlet facing the ring. Once the ring is drawn, the attached ring is laid by
  C2 with its head fixed where C11 stood it: the top rail from the east outlet, the bottom rail back to the east
  inlet, its consumer on its own right side; C12 does not move the fixed head. A head whose two ports do not both
  face right declines, and the attached ring is left to the chain rules. Built for a tank; a two-sided exchanger
  with a loop per side (the DHW circulation of the stress plant) is the next step of `C-129`.
- **Room for what a unit carries** *(built P6.10 R4)*. A hanging block hangs low enough that its devices'
  bubbles, and a bubble over each sensor point on its own level pipes (the side C15 tries first), clear
  the rail it hangs from by a margin. A unit sliding into place (C11) goes on until its pipes clear every
  placed box and bubble and the pipes the form has laid clear its own -- the first of E3's pipe bands,
  within one form; across forms it is R5's. The run length it needs is counted from the run's own
  far end: the unit's inlet run from the top rail's end, its outlet run from the bottom rail's, so a
  sensor on the rail with more members pushes the unit no further than its own run needs. A sensor on a pipe's point has no side until C15 chooses
  it, so placement keeps clear of where it will stand: a bubble over the point where its run is level
  (the side tried first) or to its left where the run is vertical -- in either case the side its controller takes,
  where that lies across the run (`D-158`); the right side's descent keeps
  clear of those too. A node's instruments count in its footprint as a device's do. The unit's own box keeps a
  margin from those bubbles as from a placed box, and its outlet's descent to the bottom rail -- facing left or down --
  crosses no pipe the form has laid: a block hung between the rails is passed, not cut through (2026-09-24, piece B
  with its controls, where the DHW load stood inside the floor block's width on its sensor: hard 3 → 0).
- **Chains** grow from their port along its axis, a standing member entered by C3's turn, a pump kept
  level (C13), and a chain off a loop member's flank leaving by two margins (C6).
- **Fragments** stack under one another, left edges aligned (C17); then open ends align (C7), which is
  the only move made after a structure is placed, and it moves a terminal along its own run only.

### E4. Draw

- **One run builder.** Every run is drawn from its two anchors through its stubs: straight where the
  anchors face each other, one bend where the rules put the corner, else the router (part D) with both
  stubs fixed. A junction's side is decided when its run is drawn and never defaulted.
- **Every laid run is checked** *(`D-155`)* before anything is routed. A run is laid through one call that names it,
  its connections' ids and the rule that shaped it in the trace; one that is not orthogonal is refused there and
  left to the router. At the draw stage, a laid run whose end is no longer on its port -- a member moved after the
  run was laid: a junction slid along its rail, a unit slid in -- is taken back for the router, and the trace says
  which rule laid it and which end it lost. The rule that laid it skew is still wrong (`C-133`); the check makes
  that visible instead of drawing it.
- **Signals** (C15, `D-152`) through the router's signal mode, then **crossings** (C16), **labels** (A11),
  the **groups** (A8) and the **scene**.

### E5. Parity

Until the switch, both engines run on every ladder step and sample. A report per step gives the audit
counts of each and the geometry difference -- each placement's move and each route's change in points,
length and bends. The new engine takes over when every step and sample is hard 0 with H11 and soft no
worse than the old engine's; a step whose picture changed is shown to the user and judged before its
commit (`D-153`). The old engine is then deleted.

## Worked example

The injection branch of `m2-distribution-header`, under H9, H10, C2–C4 and the catalogue as
shipped; every number is world units with *m* = 0.5. Members in flow order: `TV_AHU.ab → PU_AHU →
HE_AHU → NM_AHU → TV_AHU.b`, with supply arriving at `TV_AHU.a` from above and `NM_AHU` also
leaving downward to the return rail.

The three-way valve's straight run is `a`–`ab` (`D-105`), so wherever the valve sits the flow
through it is straight, and with supply arriving from above that run is vertical and downward.
Downward is the right side of a clockwise loop (H9), so the valve, the pump and the load -- the
load standing, C3 -- are the right side in that order, and the three other sides are the bare
recirculation pipe back into `b`, which faces west:

| Element | Transform | Inner box | Port on the run |
|---|---|---|---|
| `TV_AHU` | identity (`a` up, `ab` down, `b` west) | `[(−0.5, −0.5), (0.5, 0.5)]` | `ab` at `(0, −0.5)`, flow `(0, −1)` |
| `PU_AHU` | rotation 90 (`in` up, `out` down) | `[(−0.5, −2), (0.5, −1)]` | `in` at `(0, −1)`, `out` at `(0, −2)` |
| `HE_AHU` | identity, slid `+0.15` so `in` sits on `x = 0` (`D-105` item 3) | `[(−0.1, −3.5), (0.4, −2.5)]` | `in` at `(0, −2.5)`, `out` at `(0, −3.5)` |
| `NM_AHU` | node | `[(−0.1, −4.2), (0.1, −4.0)]` | west port `(−0.1, −4.1)`, south port on to the return |

Every gap on the column is exactly one clearance (0.5) and every pipe on it is a straight stub:
`TV.ab → PU.in` 0.5, `PU.out → HE.in` 0.5, `HE.out → NM` 0.5, no bends. The recirculation leaves
`NM` west for a whole margin (H5), runs to `x = −1.0` -- one clearance outside the widest inner box
on the column, `x = −0.5` -- climbs to `y = 0` and enters `b` from the west along its own stub:
`[(−0.1, −4.1), (−1.0, −4.1), (−1.0, 0), (−0.5, 0)]`, length 5.5, **two bends**, and the loop is the
rectangle `[(−1.0, −4.1), (0, 0)]`. Walked in flow order (down the right, left along the bottom,
up the left, right along the top) its signed area is negative: clockwise, H9 holds. No search ran:
H9 fixed the orientation, the valve's body fixed which side is vertical, C3 kept the load standing,
and everything else followed. What a designer draws for an injection circuit is exactly this column
with its bypass down the near side, which is `53`'s "bypass junction under its valve" without a rule
for it.

## Open questions

1. *Closed by step 2 (2026-09-16).* Does an inferred node's outer boundary take clearance? No: the
   user saw `PU1 - HE1` drawn with a boxed node between them and said the pump and the exchanger
   should meet directly. A5 and C4 record it. A *declared* node keeps its box; `N1 - PU1 - N2 -
   HE1` is the step 4 question.
2. **Which free-turning members leave a loop's bottom for a vertical?** C2 fixes the verticals'
   standing members and the top; on the simple loop `CV1`, `P1` and `PU1` all fit on the bottom,
   and the user's earlier sketch put the pump on the vertical under the source exchanger. Step 3
   draws the bottom-only form; the correction becomes the rule and the residual search in D.
   *Answered for pumps by step 7 (`D-113`, C13): never, unless nothing level fits. Open for valves.*
3. **Between two pipes of one layer, which is in front?** The user's rule (step 10): the hotter.
   The layout runs at compile, before a temperature is solved, so C16 takes the later route.
   Candidates: rank by the temperatures the script states (`out=60`, `in=50`) where it states
   them; or let the renderer re-rank crossings from the solved state, which moves a layout decision
   into the frontend against `D-103`. No sample crosses two pipes yet, so nothing decides it.

## Mapping to code

| Part | Code |
|---|---|
| A1–A4, A7 | `Layout/Direction.cs`, `Layout/Scene.cs` (`Box`, `Point`, `PlacedAnchor`, `Placement`, `Route`, `LayoutGroup`, `Scene`), `Model/SymbolCatalog.cs` (`SymbolWire.TransformClass`) |
| A5, A6, C | `Layout/LayoutEngine.cs`; the run-time audit and `FS5002` in `Model/ModelContractBuilder.cs` |
| A11 | `Layout/LabelLayout.cs`, called last from `LayoutEngine.ToScene`; `PlacementWire.LabelBox`/`LabelClear` and `LayoutWire.LabelMetric` on the wire; `LabelLayoutTests` |
| A10, B | `Layout/SceneAudit.cs`; the text is `SceneText` in Core (`C-89`), its `PLACEMENT` trace from `Scene.Provenance` (`C-107`); `SceneSvg`, `LayoutLadderTests` in Core.Tests |
| D (router) | `Layout/Routing/OrthogonalRouter.cs` |
| E (being built, P6.10) | `Layout/Engine/` beside `Layout/LayoutEngine/` until the switch ([`71`](../71-source-structure.md)) |
| the classification the engine starts from | `Layout/LayoutHints.cs` ([`25`](25-layout-hints.md)) |
