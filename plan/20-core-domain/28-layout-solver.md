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
- every finding, one per line.

The text is `SceneText` in Core since 2026-09-18 (`C-89` closed; it was in Core.Tests before). The
ladder and the sample gates write it to `diagnostics/`; nothing on the wire carries it yet.

A test writes the text *before* it asserts anything, so the file on disk is always the layout that
failed. The SVG beside it shows the same facts -- margin areas, symbol areas, every node, every
port's flow arrow, every pipe -- and is for the user; a session that renders or reads a picture to
check a layout is doing the wrong thing.

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
| H10 | **Heat progresses left to right**: a two-sided exchanger's losing side is its left flank and its gaining side its right flank (`D-36`'s edge decides which is which); a fragment's first process path starts at its heat source -- a supply boundary, a tank's charging ports, or the member with the largest positive stated duty -- and flows right |

H9 and H10 together fix, for a loop with a standing source and a standing consumer: the source on
the left side flowing up, the consumer on the right side flowing down, supply along the top to the
right, return along the bottom to the left. For a closed distribution ring the same pair puts the
supply header along the top and the return header along the bottom with the branches hanging
between them, which is `R-48`'s picture re-derived from topology; for an open supply-to-return
path with no ring, `D-107`'s stacked branches remain the candidate (D).

**Soft -- counted, and the fewer the better.** A pipe through a margin; a pipe running beside another
closer than a margin; two pipes crossing; two outer boxes overlapping; a signal line running along a
pipe (a signal crosses pipes freely: C16 hops it).

**Priorities, in strict order, when a choice remains.**

1. The hard constraints.
2. The fewest bends. A straight pipe beats a shorter pipe with corners on every drawing an engineer
   has seen; four short bends never beat one slightly longer one.
3. The fewest crossings, then the fewest soft findings.
4. What stands, stands; a free-turning member keeps its drawn default where the pipe leaves it open.
5. Alignment: equivalent assemblies -- the same kinds in the same order -- are drawn congruently.
6. Compactness -- least pipe, least area -- **last**.

**Edit stability.** Adding an instrument moves no process symbol; adding a component to one branch
moves only that branch and what it pushes along; an edit inside one group leaves every other group
identical up to translation; a change to a value moves nothing.

**The audit.** `SceneAudit` measures every hard constraint and every soft class on every scene; the
tests assert hard = 0 on every ladder step and, when the ladder reaches them, on every sample. Since
the `C-88` package (2026-09-17) that is all ten: H1–H3 and H6 as box and pipe tests, H4 and H5 on the
route's ends (H5 followed through inline points, since a node on a straight line is not a bend), H7 as
collinear overlap, H9 as the signed area of every simple directed cycle of the flow-oriented graph,
H10 as the losing side's flank where a duty is stated. A hard constraint nobody measures would be a
preference; none is left. The `C-95` package (2026-09-18) closed the three gaps the tour's faulty
picture had shown: a connection's sense is read from the flow along the route's first segment, so a
cycle through inline nodes -- where both anchors sit on one point -- is enumerated; a pipe is excused
from its own two components' clearance but never from their bodies; and a signal line is measured
against every inner box but its two ends' (hard) and along every pipe (soft). `SceneAuditTests`
holds each on a scene bent to break it: the loop samples mirrored, a pipe bent back through its
own component, a signal through an exchanger and along a pipe.

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
  -- leaves the port along its straight margin, turns towards that flank's side and continues
  level: the substation's primary arrives from the left into `in2` at the top and its return
  leaves `out2` at the bottom back to the left. Off a chain's exchanger (step 2) a port's
  continuation hangs straight.
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
- **C13** *(step 7, `D-113`)* -- **A pump is level.** It pumps left or right; a quarter turn is
  admitted only where nothing level fits, and a vertical pipe turns level into a pump (C3's turn,
  rightwards) before the pump is turned to meet it. This answers open question 2 for pumps: they
  never leave a rail for a vertical.
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
  bottom member is not a junction, and a boxed member on a branch before its first block.
- **C15** *(step 10, corrected once, provisional)* -- **Instruments stand off their anchors, and a
  controller reads through its sensor.** A sensor stands one margin off the node it observes --
  above a level rail -- with its signal a straight drop to the node's point. A controller stands one
  margin off the component it actuates, on that component's centre line (above first, then below,
  left, right), and its actuation signal runs straight into the component's facing edge, the valve's
  stem side. Its measurement signal comes from the sensor on the node it reads, not from the node:
  it leaves the sensor level, by the side facing the controller -- never by the side the sensor's own
  line leaves by -- turns once and enters the controller's facing edge. When that level stub would
  be shorter than a margin, the sensor and its node slide along the rail to make room: the node is
  inline (`D-114`) and free along its run, the valve is fixed by its loop, and the user left the
  choice between moving either. A signal may cross a pipe (C16); it never runs along one. *Step
  11d (`C-94`):* the one-bend line is kept only while it passes through no placed box and runs
  along no drawn line; otherwise the router draws it round every placed box and instrument, one
  margin off every line, crossing pipes freely -- it may leave the instrument by any edge and
  reach the target by any edge. Not built: a controller directly under its sensor, through the
  node; a sensor on a boxed junction; a controller placed on its sensor's side of a rail when the
  side facing the sensor is within a margin of another circuit (the signal goes round instead).
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
- **C18** *(step 11c, provisional)* -- **A loop with no heat source is a ring with a bare left
  side.** The tour's transient loop and its radiator loop are a pump, a load and a valve on a
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
  bare corner should turn when the valve is the last member before the consumer.
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
  layout.

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
| A5, A6, C | `Layout/LayoutEngine.cs` |
| A10, B | `Layout/SceneAudit.cs`; the text is `SceneText` in Core.Tests today and belongs in Core (`C-89`); `SceneSvg`, `LayoutLadderTests` in Core.Tests |
| D (router) | `Layout/OrthogonalRouter.cs` |
| the classification the engine starts from | `Layout/LayoutHints.cs` ([`25`](25-layout-hints.md)) |
