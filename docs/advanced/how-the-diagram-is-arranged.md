# How the diagram is arranged

The diagram beside your script is drawn from the circuit, not from anything you typed about
position. There is nothing to type: no coordinates, no "place this left of that". What you *can* do
is understand what the layout reads off the circuit, so that the picture you get is the one you
expect, and so that an edit moves only what it should.

This page is what the layout is told. How it turns that into pixels is the canvas's business and is
not something the script can steer.

## What is read off the circuit

**Order.** Components are walked from the pressure datum outward, following each component's ports in
the order they are declared. That walk is the left-to-right, top-to-bottom reading order of the
diagram, and it is also the order the keyboard tabs through. Reordering the declarations in your
script does not change it; rewiring does.

**Loops.** A closed loop — a pump, a load, a mixing valve and back — is drawn as a rectangle, with
the supply run along the top and the return along the bottom. Which way round the rectangle runs is
fixed once, by the direction the solved flow leaves the loop's first pump; it does not flip when a
transient reverses a bypass. Components outside the loop hang off it, one column per hop.

**Thermal stages.** Across the whole picture, heat progresses left to right: where it comes in, where
it changes hands, where it is stored, where it is used. Each component is placed in one of those
bands:

| Band | What puts a component there |
|---|---|
| Source | It feeds the losing side of an exchanger or the charging ports of a tank |
| Conversion | It is a two-sided exchanger, or one stated with `ua`, `area` or `u` |
| Storage | It is a `tank` |
| Consumer | It is fed by the gaining side of an exchanger or the discharging ports of a tank |
| Neutral | None of the above |

A circuit with no exchanger and no tank between it and anything else is one neutral band, and its
loop rectangle is the whole picture. That is the tutorial's cooling loop.

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
parent are a distribution group: the canvas stacks them, in the order they are declared, and draws
branches that are built the same way — the same kinds in the same sequence — the same width, with
their columns aligned. Renaming every component in one branch changes nothing; inserting a valve in
one of them does, and only that branch widens.

**Instruments and controllers.** A sensor is drawn beside the node it is `at`. A controller is drawn
beside the component it actuates, with a signal line back to what it measures, resolved through the
sensor: `measure=TE1.t` draws the line to `TE1`'s node, as [`control`](../functions/control.md)
explains.

**What you wrote and what was inferred.** The four nodes the language adds to close a loop are drawn
lighter than the components you declared, so the picture reads as the circuit you have in mind and
not as the ten-element graph it lowers to. [How a script becomes a
circuit](how-a-script-becomes-a-circuit.md) lists what gets inferred.

## What is *not* read off the circuit

Sizes, colours, spacing and the drawing mode are the canvas's, from [`style`](../functions/style.md),
[`spacing`](../functions/spacing.md) and [`show`](../functions/show.md). Nothing about the layout
depends on a solved number except the direction of the flow arrows and which way a loop runs — so a
transient run animates the arrows without rearranging the diagram.

## The messages

| Code | When | What to do |
|---|---|---|
| `FS2401` | The circuit closes on itself, so there is no first component; the walk starts at the pressure datum | Nothing. Every loop says this, and it is hidden by default |
| `FS2402` | A pipe with more than ten internal nodes, or a scene past five hundred elements, starts folded | Expand it on the canvas when you want to see inside |
| `FS2403` | A circuit's name and its duties disagree about which way heat flows | Fix whichever is wrong — the diagram followed the duties |
