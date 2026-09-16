---
id: 28-layout-solver-source
title: Layout solver, the source specification
tier: 20-core-domain
status: draft
owns: []
depends_on: [28-layout-solver]
traces_to: []
open_questions: 0
last_review_pass: 0
---

# Layout solver: the source specification (2026-09-16)

The specification as the user wrote it, kept unchanged. [`28-layout-solver`](28-layout-solver.md) is
its restatement in the project's terms and is the document that is maintained; when the two disagree,
`28` has been revised deliberately and this file records where it started.

---

Yes. The core of your idea is a **rule-based orthogonal layout engine**, not a generic geometry optimizer. Most placements should follow directly from topology, port vectors, margins, and previous placement. Search is mainly needed when closing loops, arranging branches, or resolving collisions.

One ambiguity should be removed before implementation: I recommend that every port vector means **fluid-flow direction**, not boundary normal. Therefore a pump pointing upward has inlet and outlet flow vectors `(0,1)`. Whether the outer anchor lies before or after the inner anchor depends on whether the port is an inlet or outlet.

Here is how I would specify it to another LLM.

# Orthogonal layout solver

The thermodynamic topology is already correct. Do not change the thermodynamic graph to improve visual layout.

The layout solver converts the thermodynamic graph into a deterministic 2D orthogonal representation.

The solver is **not** a generic 2D packing solver.

The fundamental principle is:

> Sequential component placement should be deterministic and require essentially no optimization. Search is introduced only for loops, branches, group placement, and collision resolution.

The solver operates hierarchically:

```text
Thermodynamic graph
    ↓
Detect topology structures
    ↓
Sequential groups / branches / loops
    ↓
Solve internal geometry of each group
    ↓
Treat solved groups as rigid layout objects
    ↓
Place groups
    ↓
Generate pipe routes
    ↓
Validate collisions
    ↓
Produce diagnostics
```

---

# 1. Coordinate system

The layout exists in a two-dimensional Cartesian plane.

Coordinates are:

```text
(x, y)
```

The only valid direction vectors are:

```text
Right  = ( 1,  0)
Up     = ( 0,  1)
Left   = (-1,  0)
Down   = ( 0, -1)
```

No diagonal or zero vectors are allowed.

These are invalid:

```text
(1, 1)
(-1, -1)
(0, 0)
```

All component rotations must therefore be multiples of 90 degrees:

```text
0°
90°
180°
270°
```

Mirroring is a separate transformation and must not be treated as rotation.

---

# 2. Port vector semantics

A port vector means:

> The direction in which fluid flows at that port.

It does **not** mean the outward normal of the component bounding box.

For example, a horizontal pump carrying flow left-to-right is:

```text
        flow →

──────▶ [ PUMP ] ──────▶

inlet.flow  = (1, 0)
outlet.flow = (1, 0)
```

If the same pump is rotated so that flow travels upward:

```text
           ↑
           │
       [ PUMP ]
           │
           ↑
```

then:

```text
inlet.flow  = (0, 1)
outlet.flow = (0, 1)
```

A component may change the flow direction.

For example:

```text
inlet.flow  = (1, 0)
outlet.flow = (0, 1)
```

means:

```text
flow enters from the left
component redirects flow upward
```

This direction change is a property of the component orientation/variant.

---

# 3. Component geometry

Every component instance has:

```text
Component
    type
    rotation
    mirror

    innerBounds
    outerBounds

    ports[]
        role
        innerAnchor
        outerAnchor
        flowDirection
```

## 3.1 Inner bounding box

The inner bounding box describes the actual component symbol.

Example:

```text
inner = [(0,0), (2,2)]
```

means:

```text
width  = 2
height = 2
```

The inner bounding box is hard geometry.

Another component, unrelated pipe, or another hard piece of geometry must never pass through it.

---

# 4. Margin and outer bounding box

Each component has margin `m`.

For:

```text
inner width  = w
inner height = h
margin       = m
```

the outer bounds are:

```text
outer width  = w + 2m
outer height = h + 2m
```

Example:

```text
inner = [(0,0), (2,2)]
m = 0.5

outer = [(-0.5,-0.5), (2.5,2.5)]
```

The outer bounding box represents preferred component clearance.

Outer bounding boxes are **not hard obstacles**.

They may interfere with each other.

The important rule is instead the distance between actual inner component symbols.

For components A and B:

```text
requiredClearance = max(A.Margin, B.Margin)
```

Their inner bounding boxes should remain at least this far apart.

Thus:

```text
A.margin = 0.5
B.margin = 1.0

required inner-box clearance = 1.0
```

This is a hard component-placement constraint.

---

# 5. Port anchors

Every port has two points:

```text
innerAnchor
outerAnchor
```

The inner anchor lies exactly on the inner component boundary.

The outer anchor lies exactly on the corresponding outer component boundary.

The outer anchor should normally be derived rather than independently stored.

For an outlet:

```text
outerAnchor =
    innerAnchor + flowDirection * margin
```

For an inlet:

```text
outerAnchor =
    innerAnchor - flowDirection * margin
```

This distinction is important.

For an upward-flowing pump:

```text
inlet.inner  = (1, 0)
inlet.flow   = (0, 1)
inlet.outer  = (1, -0.5)

outlet.inner = (1, 2)
outlet.flow  = (0, 1)
outlet.outer = (1, 2.5)
```

Therefore:

```text
                 outlet.outer
                     (1,2.5)
                        ↑
                 outlet.inner
                     (1,2)
                  ┌────────┐
                  │  PUMP  │
                  └────────┘
                 inlet.inner
                     (1,0)
                        ↑
                 inlet.outer
                    (1,-0.5)
```

This should replace ambiguous definitions where an upward-facing port still contains `(1,0)` or `(-1,0)` vectors.

---

# 6. Component templates and transformations

Component geometry should first be defined in one canonical orientation.

For example:

```text
PumpTemplate
    width = 2
    height = 2

    inlet:
        anchor = (0,1)
        flow = (1,0)

    outlet:
        anchor = (2,1)
        flow = (1,0)
```

The layout solver must generate transformed candidates using:

```text
rotation ∈ { 0, 90, 180, 270 }
mirror   ∈ allowed mirrors
```

Rotation transforms:

1. component geometry;
2. inner bounding box;
3. port anchors;
4. port flow vectors.

A rotated component must therefore never have stale port vectors from its canonical orientation.

Mirroring is only allowed when explicitly supported by the component.

---

# 7. Initial layout direction

The native direction of the entire layout is:

```text
(1,0)
```

meaning left-to-right.

The first process path begins with:

```text
currentFlowDirection = (1,0)
```

This is the default visual convention.

It does not mean every pipe in the complete system travels to the right.

Loops naturally contain:

```text
right
down/up
left
up/down
```

depending on loop orientation.

---

# 8. Sequential placement

Sequential placement is the simplest and most important layout operation.

It should not use numerical optimization.

Assume component A has already been placed.

Its connected outlet has:

```text
A.outlet.flow = d
```

When placing component B:

1. Enumerate allowed rotations and mirrors of B.
2. Keep only candidates for which:

```text
B.inlet.flow == d
```

3. Select the preferred candidate.
4. Place B in direction `d`.
5. Align A's outlet and B's inlet along the same axis.
6. Use the minimum permitted component clearance.

The inner-anchor distance should initially be:

```text
gap = max(A.margin, B.margin)
```

For a horizontal connection:

```text
A ─────▶ B
```

the symbols should therefore be placed as close together as permitted.

The resulting connection should be straight whenever no component changes direction.

Example:

```text
NODE → PUMP → SENSOR → VALVE
```

becomes:

```text
[NODE]──[PUMP]──[SENSOR]──[VALVE]
```

without any routing search.

---

# 9. Direction-changing components

A component may redirect the flow.

Suppose:

```text
component.inlet.flow  = (1,0)
component.outlet.flow = (0,-1)
```

The incoming sequence travels right.

After this component:

```text
currentFlowDirection = (0,-1)
```

Every subsequent sequential component must be transformed so that:

```text
next.inlet.flow == (0,-1)
```

Example:

```text
──────▶ [ HX ]
            │
            ▼
          [TS]
            │
            ▼
          [PU]
```

The downstream components are rotated automatically.

No special routing rule is needed.

The topology and component direction vectors already determine the arrangement.

---

# 10. Sequential groups

A sequential group is a maximal ordered sequence where placement can be derived directly by propagating flow direction.

Example:

```text
PUMP → SENSOR → VALVE → LOAD
```

The solver places the first component and repeatedly applies the sequential placement rule.

After the sequence has been solved, calculate its group bounds:

```text
group.innerBounds
group.outerBounds
```

The complete sequence can then be treated as one object by a parent layout operation.

This is important for hierarchical layout.

---

# 11. Detect topology before solving geometry

Before placing components, classify the graph.

At minimum identify:

```text
1. Sequential paths
2. Split/branch points
3. Merge points
4. Simple loops
5. Complex cyclic regions
```

Cycle detection should happen before ordinary sequential placement.

A practical implementation is:

```text
Tarjan strongly connected components
```

or an equivalent SCC algorithm.

An SCC containing:

```text
more than one component
```

is potentially cyclic.

A simple loop can initially be defined as a cyclic region where every member has exactly:

```text
one internal predecessor
one internal successor
```

More complicated SCCs should later be decomposed further.

For the first implementation, simple loops should be solved extremely well before supporting arbitrary multi-loop SCCs.

---

# 12. Simple loop model

A simple loop has an ordered sequence:

```text
C0 → C1 → C2 → ... → Cn → C0
```

The thermodynamic order must never be changed.

The layout solver is only allowed to decide:

```text
which side of the loop each component belongs to;
component rotation;
component mirroring;
where pipe corners occur;
loop width;
loop height.
```

The preferred simple loop is orthogonal and rectangular.

Conceptually:

```text
        TOP
 ┌──────────────────┐
 │                  │
 │                  │
LEFT              RIGHT
 │                  │
 │                  │
 └──────────────────┘
       BOTTOM
```

The loop therefore contains four directional runs.

For clockwise flow, one possible direction sequence is:

```text
right
down
left
up
```

For counter-clockwise:

```text
right
up
left
down
```

Both orientations should be searched.

---

# 13. Loop edge assignment

The component order is fixed.

The solver searches how that ordered sequence should be divided between the four loop sides.

Example:

```text
C0 C1 C2 | C3 C4 | C5 C6 C7 | C8
```

could mean:

```text
TOP:
C0 C1 C2

RIGHT:
C3 C4

BOTTOM:
C5 C6 C7

LEFT:
C8
```

Empty sides are permitted.

The order around the thermodynamic loop never changes.

Only the location of the three/four side transitions changes.

This is a much smaller search space than arbitrary 2D placement.

---

# 14. Component candidates inside a loop

For every component and every possible loop side, calculate valid transforms.

For example, if the top side travels right:

```text
required inlet direction = (1,0)
```

A normal inline component should preferably have:

```text
inlet.flow  = (1,0)
outlet.flow = (1,0)
```

A component capable of turning the corner may instead have:

```text
inlet.flow  = (1,0)
outlet.flow = (0,-1)
```

Such a component can occupy the top-right corner.

This is preferable to:

```text
component
pipe bend
```

because the pipe requires no additional bend.

Therefore component transformations and loop-side assignment must be solved together.

---

# 15. Loop corners

Direction changes may occur in two ways.

## Case A — component performs the turn

Example:

```text
────────▶ [HX]
             │
             ▼
```

Then the component itself occupies the corner.

No pipe bend is needed.

## Case B — pipe performs the turn

Example:

```text
────────▶ [TS] ──┐
                 │
                 ▼
```

The router inserts one 90-degree corner.

Component turns are preferred when they naturally follow the component's valid geometry.

Otherwise one pipe bend is acceptable.

Avoid multiple bends where one bend is sufficient.

---

# 16. Loop candidate construction

For each candidate loop arrangement:

1. Choose clockwise or counter-clockwise orientation.
2. Choose the component sequence starting position.
3. Partition the ordered cycle into four contiguous side sequences.
4. Select a valid transform for every component.
5. Place components sequentially along each side using minimum margins.
6. Determine required width and height.
7. Align opposite sides.
8. Connect the four sides.
9. Simplify all resulting polylines.
10. Validate geometry.
11. Score the candidate.

No continuous numerical solver is required.

The search is combinatorial and finite.

---

# 17. Opposite-side sizing

Suppose the top sequence requires width:

```text
12
```

and the bottom sequence requires:

```text
9
```

The loop width must be at least:

```text
12
```

The bottom side has three units of free pipe length available.

Do not unnecessarily increase component spacing.

Prefer adding required slack to pipe-only sections.

Likewise:

```text
height =
max(requiredLeftHeight, requiredRightHeight)
```

This naturally produces compact rectangular loops.

---

# 18. Loop objective

Candidate arrangements should not use one vague weighted cost initially.

Use lexicographic comparison.

An arrangement with a better higher-priority property always wins regardless of lower-priority properties.

Recommended comparison order:

```text
1. Hard geometry validity
2. Number of unnecessary pipe bends
3. Number of pipe crossings
4. Number of route/component clearance violations
5. Total number of pipe vertices
6. Total pipe length
7. Loop area
8. Margin interference
9. Number of mirrors
10. Rotation preference / deterministic tie-break
```

Hard invalid geometry is rejected completely.

For valid geometry, fewer bends should normally dominate shorter pipe length.

This prevents:

```text
4 short bends
```

from beating:

```text
1 slightly longer bend
```

which is visually incorrect.

---

# 19. Minimum-bend principle

Before accepting any route, simplify its points.

For:

```text
A → B → C
```

if A, B and C lie on the same horizontal or vertical line, remove B.

Example:

```text
[(0,0), (2,0), (5,0)]
```

becomes:

```text
[(0,0), (5,0)]
```

Duplicate points must also be removed.

The number of bends is therefore:

```text
route.Points.Count - 2
```

after simplification, assuming every intermediate point changes direction.

A connection that can be represented as a straight line must never contain extra bends.

---

# 20. Pipe representation

A connection contains:

```text
source.innerAnchor
source.outerAnchor
routing points
target.outerAnchor
target.innerAnchor
```

Conceptually:

```text
source inner
    ↓
source outer
    ↓
orthogonal route
    ↓
target outer
    ↓
target inner
```

However, margins are permitted to overlap.

Therefore source and target port stubs may occasionally geometrically overlap.

That is valid.

After route generation, normalize the complete polyline by:

```text
1. removing duplicates;
2. removing collinear intermediate vertices;
3. removing backtracking when the same straight segment overlaps.
```

The final SVG/frontend polyline should represent the simplest equivalent path.

The diagnostic output may still show the logical inner and outer anchors separately.

---

# 21. Pipe clearance envelope

Every pipe route has a clearance width.

Example:

```text
pipeClearance = 0.3
```

The route must therefore generate a 2D clearance envelope around its segments.

The ends remain flat.

For orthogonal routes, do not use a generic polygon offset library unless necessary.

Represent the envelope as the union of axis-aligned rectangles around each segment.

For a horizontal segment:

```text
(x1,y) → (x2,y)
```

with clearance `c`:

```text
[min(x1,x2), y-c]
[max(x1,x2), y+c]
```

Similar logic applies to vertical segments.

Corner rectangles join adjacent envelopes.

This makes collision detection trivial.

---

# 22. Collision rules

Distinguish hard and soft collisions.

## Hard

These invalidate a layout:

```text
inner component geometry intersects another inner component;
required component clearance is violated;
pipe clearance envelope intersects unrelated inner component geometry;
port anchor is not on its expected boundary;
port vector is invalid;
connected port directions cannot be reconciled.
```

## Soft

These are permitted but should increase the candidate cost:

```text
outer component margins overlap;
pipe passes through component margin;
pipe clearance envelopes cross;
two routes cross;
group margins overlap.
```

Pipe crossings may later be rendered using crossing hops.

They should still be avoided whenever an equally valid non-crossing arrangement exists.

---

# 23. Rectangle clearance test

Because components are axis-aligned after 90-degree rotations, generic polygon point-inside tests are unnecessary for most component collisions.

For rectangles A and B with required clearance `g`, they are valid if at least one of these is true:

```text
A.Right  + g <= B.Left
B.Right  + g <= A.Left
A.Top    + g <= B.Bottom
B.Top    + g <= A.Bottom
```

Otherwise their required clearance regions interfere.

This is faster and safer than checking only whether polygon vertices are inside one another.

Checking only polygon vertices is insufficient because two orthogonal shapes can cross without any vertex lying inside the other.

---

# 24. Solved loops become rigid groups

Once a loop has been solved successfully:

```text
LoopGroup
    members
    internalPlacements
    internalRoutes
    bounds
    externalPorts
```

must be created.

The parent layout solver should treat it as a rigid object.

It may:

```text
translate the loop;
rotate the entire loop;
possibly mirror the whole loop if explicitly allowed.
```

It should not independently rearrange components inside a solved loop.

This is critical for layout stability.

A small change elsewhere in the system must not destroy a correct loop arrangement.

---

# 25. Hierarchical layout

Ultimately the entire diagram should consist of layout objects:

```text
LayoutObject
    Component
    SequentialGroup
    LoopGroup
    BranchGroup
```

Every layout object exposes:

```text
bounds
ports
allowed transforms
```

Therefore the parent solver does not need to care whether an object contains:

```text
one valve
```

or:

```text
a complete 30-component heating loop.
```

It places both using the same interface.

This allows recursive layout.

---

# 26. Branches

After simple loops work correctly, branch structures should use the same principles.

For:

```text
             ┌── Branch A ──┐
SUPPLY ──────┤              ├──── RETURN
             └── Branch B ──┘
```

the main direction remains left-to-right.

Child branches are solved independently as groups.

Then the parent branch group:

1. places the split point;
2. stacks the solved children perpendicular to main flow;
3. places the merge point;
4. connects them using minimum-bend routes.

Branch ordering should preferably follow script/topological ordering unless changing the order materially reduces crossings.

---

# 27. Determinism

Identical input must always create identical output.

Never depend on:

```text
dictionary iteration order;
hash ordering;
parallel race timing;
random search;
floating-point near-ties without deterministic tie-breaking.
```

Candidate ties should be resolved using stable rules such as:

```text
1. script order
2. lower component ID
3. no mirror before mirror
4. smaller clockwise rotation
5. lower X
6. lower Y
```

This is essential for tests and diagnostics.

---

# 28. Recommended C# model

A possible implementation is:

```csharp
public readonly record struct Point2(double X, double Y);

public readonly record struct Direction2(int X, int Y)
{
    public static readonly Direction2 Right = new(1, 0);
    public static readonly Direction2 Up    = new(0, 1);
    public static readonly Direction2 Left  = new(-1, 0);
    public static readonly Direction2 Down  = new(0, -1);
}

public readonly record struct Rect2(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY);

public enum PortRole
{
    Inlet,
    Outlet
}

public sealed record PortTemplate(
    string Name,
    PortRole Role,
    Point2 Anchor,
    Direction2 FlowDirection);

public sealed record ComponentTemplate(
    string Kind,
    double Width,
    double Height,
    double Margin,
    IReadOnlyList<PortTemplate> Ports,
    bool AllowMirror);

public readonly record struct ComponentTransform(
    int Rotation,
    bool Mirrored);

public sealed record PlacedPort(
    string Name,
    PortRole Role,
    Point2 InnerAnchor,
    Point2 OuterAnchor,
    Direction2 FlowDirection);

public sealed record ComponentPlacement(
    string ComponentId,
    ComponentTransform Transform,
    Rect2 InnerBounds,
    Rect2 OuterBounds,
    IReadOnlyList<PlacedPort> Ports);

public sealed record Route(
    string ConnectionId,
    IReadOnlyList<Point2> Points,
    IReadOnlyList<Rect2> ClearanceEnvelope);

public sealed record LayoutGroup(
    string Id,
    Rect2 Bounds,
    IReadOnlyList<ComponentPlacement> Components,
    IReadOnlyList<Route> Routes);
```

Validate `Direction2` during construction so only the four orthogonal unit vectors are accepted.

---

# 29. Sequential placement implementation

The core operation should be approximately:

```csharp
PlaceNext(
    ComponentPlacement previous,
    ComponentTemplate next,
    string previousOutlet,
    string nextInlet)
```

Algorithm:

```text
d = previous.outlet.FlowDirection

candidates =
    all valid rotation/mirror transformations of next

candidates =
    candidates where transformed next.inlet.FlowDirection == d

for each candidate:
    gap = max(previous.Margin, next.Margin)

    calculate translation such that:
        next inlet is aligned with previous outlet
        next lies gap units away in direction d

    validate clearance

select deterministic best candidate
```

For an ordinary sequence, exactly one natural candidate should normally remain.

---

# 30. Simple-loop implementation strategy

Implement loops in stages.

## Version 1

Support only simple directed cycles.

Detect:

```text
every member has one predecessor and one successor inside the cycle
```

Enumerate:

```text
clockwise / counter-clockwise
starting position
four-side partition
valid component transformations
```

Build each candidate deterministically.

Score it.

Choose the lexicographically best candidate.

This should solve the majority of ordinary HVAC circulation loops.

## Version 2

Support multiple branches inside cyclic SCCs.

Decompose the SCC into nested:

```text
loops
sequential groups
branch groups
```

Do not attempt Version 2 until Version 1 produces excellent and predictable output.

---

# 31. Diagnostic output

The text diagnostic is a first-class Core output.

Do not debug layout primarily from SVG.

Every component should output:

```text
PU1 pump
 group LOOP_1
 rotation 270
 mirror false

 inner [(0,0), (2,2)]
 outer [(-0.5,-0.5), (2.5,2.5)]

 inlet
   inner (1,0)
   outer (1,-0.5)
   flow (0,1)

 outlet
   inner (1,2)
   outer (1,2.5)
   flow (0,1)
```

Every connection should output:

```text
PU1 -> HE1

points
  (1,2)
  (1,2.5)
  (4.3,2.5)
  (4.3,2)

simplified points
  ...

length 4.3
bends 2
crossings 0
```

Every group should output:

```text
GROUP LOOP_1
 type simple_loop
 orientation clockwise

 members
   PU1
   HE1
   LOAD

 bounds [...]
 width ...
 height ...

pipe bends ...
pipe length ...
```

Finally:

```text
VALIDATION

hard errors: 0

component clearance violations: 0
route/component collisions: 0
route crossings: 1
outer-margin interferences: 2
total bends: 8
total pipe length: 42.5
```

For every interference:

```text
INTERFERENCE

objects:
  HE1
  PU1

type:
  outer-margin-overlap

hard:
  false

bounds:
  [...]
```

---

# 32. Important implementation principle

Do not ask the Manhattan router to discover the layout.

That is backwards.

The correct order is:

```text
topology
    ↓
group structure
    ↓
flow directions
    ↓
component transformations
    ↓
component placement
    ↓
simple connections
    ↓
routing only where necessary
```

If:

```text
HE1
LOAD
```

belong on the same vertical loop edge, the loop solver must first place them on that edge with compatible port directions.

The router should then discover that their connection is a straight line.

It should not receive arbitrary HE1 and LOAD positions and try to invent a four-bend pipe between them.

---

# 33. Core philosophy

The final solver should behave according to these principles:

```text
Topology determines order.

Flow vectors determine orientation.

Margins determine minimum spacing.

Sequential placement determines most coordinates.

Groups create hierarchy.

Loops require discrete search.

Routing fills the remaining geometry.

Collision detection validates the result.

Scoring selects between multiple valid loop/group arrangements.

Frontend only renders the finished layout.
```

The important simplification is:

> Most layout geometry is not something that needs to be "solved". It follows directly from component connectivity and orthogonal direction propagation.

Only when topology introduces multiple valid spatial arrangements — especially loops and branches — should the solver search candidate layouts.

The most important addition I made is the **four-side partition model for loops**. It turns “find a nice loop layout” into a finite combinatorial problem: preserve component order, divide that order among four sides, enumerate transformations, construct the rectangle, and compare candidates. That is much more controllable and testable than letting a general router move components around.
