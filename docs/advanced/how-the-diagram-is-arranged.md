# How the diagram is arranged

The diagram beside your script is drawn from the circuit, not from anything you typed about
position. There is nothing to type: no coordinates, no "place this left of that". What you *can* do
is understand what the layout reads off the circuit, so that the picture you get is the one you
expect, and so that an edit moves only what it should.

This page is what the layout is told, and then what it decides. The placing and the routing happen
in FluidScript itself, not in the browser: the diagram arrives as a finished drawing -- a box for every
component, a polyline for every pipe -- and the canvas only draws inside the boxes it is given. Where
a component sits is not something the script can steer today; pinning one is planned.

## How components are placed

**Two boxes.** Every component has an **inner** box -- its symbol, turned and, for an exchanger,
in one of two arrangements: through-pass (in at one end, out at the other) or U-pass (each side in
and out on its own flank); for a three-way valve, with either switched port on the straight run,
since `a` and `b` are the same port to the drawing and the labels say which is which -- and an
**outer** box, the inner grown by a margin on every side: 0.5
symbol units unless [`spacing`](../functions/spacing.md) says otherwise. The inner box is hard:
no other symbol and no pipe that does not serve it ever enters it. The outer box is soft: two
margins may overlap, a pipe may cross one, and the layout counts that against a drawing without
forbidding it. Two components are placed correctly when neither's symbol is inside the other's
margin, which is the same as saying their symbols are at least a margin apart.

**Ports.** Every port has a point on the symbol's edge, a direction straight out of that edge, and
a point one margin out along it, on the edge of the outer box. The pipe between those two points
is always straight: a pipe leaves a component straight for a whole margin and only then may turn.
Every port also knows which way its fluid moves -- out of an outlet, into an inlet, out of a
supply boundary, into a return boundary -- and the drawing's flow arrows are those directions,
read from the ports and not from the solved numbers, so a transient run never rearranges anything.

**Heat flows left to right, and every loop runs clockwise.** Where heat enters the picture is on
the left, where it changes hands is in the middle, where it is used is on the right: a two-sided
exchanger always has the side that gives heat away on its left flank and the side that receives it
on its right. A closed loop is drawn as a rectangle with the flow going round it clockwise -- up
the left side through the source, right along the top, down the right side through the consumer,
left along the bottom -- so supply is always the top of a loop and return the bottom, and a
district-heating substation reads as the primary arriving from the left, dropping through the
exchanger, and the secondary circulating clockwise on its right. Neither is a preference the layout
might trade for a shorter pipe: a drawing that breaks either is wrong.

**Some symbols are never turned, only mirrored.** An exchanger is always drawn upright: which flank
each side takes and whether its flow runs up or down are chosen by mirroring it left-to-right or
top-to-bottom, never by laying it on its side. A tank is upright too, and it is only ever mirrored
left-to-right, because its layers are a vertical order -- the hot water stays at the top. A valve
turns freely. A pump pumps left or right: it is turned to vertical only where nothing level fits,
and a vertical pipe turns level into it first. A component that can slide along its own direction
of flow -- an exchanger on the short side of a loop -- sits at the middle of that side. This is a fact about the kind, not a preference: where a level pipe reaches
a standing exchanger, the pipe turns into it; the exchanger does not lie down and the pipe upstream
does not tip over to meet it.

**Pipes.** A pipe is a line of straight pieces, horizontal or vertical, from one port to the
other. It never passes through a symbol it does not serve; where a run changes direction there is
bare pipe, never a component; supply and return never share a piece of pipe; and where two pipes
must cross, the one drawn later hops the other with a small arc.

**What a good drawing is, in order.** When more than one drawing obeys all of that, the layout
prefers, in this strict order: the fewest bends (a straight pipe beats a shorter pipe with corners,
and four short bends never beat one slightly longer one); then the fewest crossings and the fewest
overlapping margins; then the reading conventions -- the first path flows left to right, supply
sits above return, what stands stands; then congruence, so that two branches built the same way
are drawn the same way; and only last, compactness. An edit moves only what it must: adding an
instrument moves no process symbol, adding a component to one branch moves only that branch, and
changing a value moves nothing.

**A pipe is a line, not a box.** A declared `pipe` and the pieces a pipe is split into have no box:
they are points on the run between the two elements either side of them, and the line through them
*is* the pipe; a pipe shows only its name beside the line. A node where three or more pipes meet is
drawn as a small dot, with at most one pipe on each of its four sides.

**Every node has a place of its own.** The language terminates every port you left unconnected
with a boundary node so the circuit is complete ([how a script becomes a
circuit](how-a-script-becomes-a-circuit.md)), and every node -- the ones you declared, the ones
inferred between two components, and those boundary nodes -- is laid out with a box and a margin
like any other element, so the arrangement can be read box by box. What the canvas *draws* for a
node is a separate matter: a junction where three or more pipes meet is a small dot, and a node
with only two connections draws nothing, because there is nothing to see at a joint between two
pipes. `PU1 pump` on its own is therefore laid out as a pump between two nodes, and drawn as a pump
with two short pipe ends.

**The rules are being established one component at a time.** How the components are arranged --
which sits where, how a loop is laid out, how branches stack -- is decided rule by rule from
drawings the author corrects, starting with a single pump and adding one component per step. A
component that no rule covers yet is not guessed at: it is set aside in a column below everything
placed, with its pipes drawn as plainly as possible, so the picture shows exactly how far the rules
reach. What the rules build so far: the first path starts at the heat source and flows left to
right with each component straight after the one before; a level pipe reaching an exchanger turns
into it; a closed loop is laid out once as a clockwise rectangle -- the source on the left, the
consumer on the right, pumps level -- and then kept rigid, so a change elsewhere never rearranges
it; an injection branch (a three-way valve, its pump, its load and the junction that recirculates)
is laid out first as a block of its own, its inlet and outlet side by side facing its header, and
the header treats the block as one component; a distribution ring has its supply header along the
top, its return along the bottom, and its branches hanging between them in the order they are
declared, each under the junction that feeds it and over the one it returns to; branches in series
step down from one block's outlet to the next block's inlet. A sensor stands just off the node it
reads and a controller just off the component it drives, on its centre line; the controller's
signal comes from the sensor, level and then down, and its own line goes straight into the
actuator. Where lines cross, the one in front runs through and the one behind is broken around it:
signal lines run behind pipes, and return pipes behind supply pipes. Where a component may sit is
not something the script can steer today; pinning one is planned.

## What is read off the circuit

**Order.** Components are walked from the pressure datum outward, following each component's ports in
the order they are declared. That walk is the left-to-right, top-to-bottom reading order of the
diagram, and it is also the order the keyboard tabs through. Reordering the declarations in your
script does not change it; rewiring does.

**Flow direction.** Which way a pipe carries its fluid is read from the ports it joins: a pump's
outlet sends, an exchanger's inlet receives, a supply boundary sends and a return boundary receives,
a three-way valve's common port `ab` decides its `a` and `b`, and a junction passes a direction on
to the one pipe at it that nothing else has decided. A pipe between two ports that say nothing runs
the way you wrote the connection. Nothing here is read from the solved flow: the drawing does not
flip when a transient reverses a bypass.

**Thermal stages.** Across the whole picture, heat progresses left to right: where it comes in, where
it changes hands, where it is stored, where it is used. Each component is assigned one of those
bands. The band is reported with the drawing and orders parts of the picture that are not piped to
each other; it never moves a component off its run:

| Band | What puts a component there |
|---|---|
| Source | It feeds the losing side of an exchanger or the charging ports of a tank |
| Conversion | It is a two-sided exchanger, or one stated with `ua`, `area` or `u` |
| Storage | It is a `tank` |
| Consumer | It is fed by the gaining side of an exchanger or the discharging ports of a tank |
| Neutral | None of the above |

A circuit with no exchanger and no tank between it and anything else is one neutral band. That is
the tutorial's cooling loop.

Where the topology does not decide, the circuit's name does. `circuit radiators` says consumer,
`circuit district` says source, and a component in such a circuit takes that band when nothing
physical placed it already. The registered names and what they say are listed under
[`circuit`](../functions/circuit.md). A name that matches nothing is neutral, and that is not an
error.

**The name is evidence, not an order.** If a circuit is called `radiators` and every exchanger in it
is stated with a positive `power` — giving heat *to* the water — it is a source whatever it is
called, and the diagram places it on the left. You are told:

```
FS2403  'radiators' is named as a radiator circuit but its stated duties make it a
        source; the duties decide where it is drawn.
```

Fix the name, or the sign, whichever was wrong.

**Circuits and their branches.** A subcircuit that joins its parent through `supply` and `return`, or
through two connections to the parent's nodes, is a branch off that parent. Several branches on one
parent are a distribution group, reported with the drawing so that branches built the same way --
the same kinds in the same sequence -- can be drawn the same way. Renaming every component in one
branch changes nothing; inserting a valve in one of them does, and only that branch widens.

**Instruments and controllers.** A sensor is drawn beside the node it is `at`. A controller is drawn
beside the component it actuates, with a signal line back to what it measures, resolved through the
sensor: `measure=TE1.t` draws the line to `TE1`'s node, as [`control`](../functions/control.md)
explains.

**What you wrote and what was inferred.** The nodes the language adds to close a loop have two
connections each and are therefore not drawn, so the picture reads as the circuit you have in mind and
not as the ten-element graph it lowers to; they still exist for hover and for the solver. [How a script becomes a
circuit](how-a-script-becomes-a-circuit.md) lists what gets inferred.

## What is *not* read off the circuit

Colours, line weights and patterns come from [`style`](../functions/style.md), the clearance from
[`spacing`](../functions/spacing.md), and the fill of each symbol from [`show`](../functions/show.md).
Nothing about the layout depends on a solved number: the flow arrows are read from the ports, so a
transient run animates the state colours without rearranging the diagram, and changing a duty or a
setpoint moves nothing.

## The messages

| Code | When | What to do |
|---|---|---|
| `FS2401` | The circuit closes on itself, so there is no first component; the walk starts at the pressure datum | Nothing. Every loop says this, and it is hidden by default |
| `FS2402` | A pipe with more than ten internal nodes, or a scene past five hundred elements, starts folded | Expand it on the canvas when you want to see inside |
| `FS2403` | A circuit's name and its duties disagree about which way heat flows | Fix whichever is wrong — the diagram followed the duties |
