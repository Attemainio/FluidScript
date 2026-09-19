# node

A point in the circuit with a state and no extent. The node is the junction: it is the only component
that accepts any number of connections, and **every state in a circuit lives on one**. Pipes, valves
and pumps have states at their ports, but those ports attach to nodes.

```fluidscript
N1 node t=6 p=300
N2 node
```

Most nodes are never written. Connecting two components directly creates one between them, and a
branch that meets another branch gets one where they meet — you will see them in the diagram and in
the log, named after what they join.

## Ports

Unlimited, unnamed and bidirectional. `N1.a` is an error: a node has no named ports, so connections to
it are always written plain.

## Parameters

Every one of them is a boundary condition. A node with none is solved.

| Parameter | A bare number means | Meaning |
|---|---|---|
| `t` | °C | Fixes the temperature. On an interior node it is a **setpoint** — see below |
| `p` | kPa | Fixes the pressure. The first stated `p` in a circuit is also its pressure datum |
| `flow` | kg/s | Terminal flow. Positive follows the nominal connection: an upstream terminal injects, a downstream one extracts |
| `elevation` | m | Height above the project datum — see [Height](#height) |

Omitting any of them means the solver works it out, which is the normal case — except `elevation`,
which is never worked out: a height is where the plant is, not a number the tool may choose.

## Height

Every piece of equipment sits at one height, and `elevation` says which, in metres above wherever
you call zero — usually the plant room floor:

```fluidscript
HE_AHU load power=24 in=50 out=30 elevation=32
N4     node elevation=32
```

You need not write it on everything. **A height spreads to whatever is wired to it without a pipe
in between**: the valve next to the AHU, the pump beside it, the nodes joining them are all on the
roof once the AHU says so. Only a [`pipe`](pipe.md) — or a bare `A - B` between two nodes — runs
between two heights, and it takes the difference as its rise. Where nothing states a height,
everything sits at 0, so a script with no `elevation` in it means exactly what it did before.

Two stated heights joined directly, with no pipe between them, is a missing riser and
[`FS2219`](diagnostics.md) says so rather than choosing one: the difference is 10 kPa per metre, and a
pressure nothing wrote is the one mistake this feature exists to make unwritable.

**Mind the fill pressure.** Water at the top of a 32 m riser is 313 kPa below the bottom. With no
`p=` anywhere the datum is picked at 0 kPa, the top of the building would sit 213 kPa below
atmospheric — below no pressure at all — and [`FS2220`](diagnostics.md) says so before anything is
solved, naming the node, the height, and the pressure to state:

```
FS2220  'N4' is 32 m above 'N1', which puts it 213 kPa below the lowest pressure water can be at.
        State a pressure on 'N1' of at least 370 kPa.
```

The number is what practice sets: the static head plus half a bar — an expansion vessel's pre-charge
at the static height plus 0.2 bar, and the fill pressure 0.3 bar above that. `N1 node p=370` in the
plant room and the roof is fine.

A plant that is merely *under vacuum* somewhere solves — liquid water exists well below atmospheric —
and is told so afterwards. Every pressure in a loop with no `p=` is relative to the picked datum, and
the solve can put a node below it: a second pump on the ring, say, whose suction sits its own head under
the first pump's. [`FS2221`](diagnostics.md) then names the lowest node and the same practical number:

```
FS2221  'N6' is 29 kPa below atmospheric pressure. State a pressure on 'N1' of at least 80 kPa.
```

It is a warning, not an error: the figures are right relative to one another, and the plant as written
would draw air at `N6` unless it is filled to that pressure.

## A temperature on an interior node is a setpoint

On a node at the edge of the model, `t` is a boundary condition: it says what temperature the fluid
arrives at. On a node *inside* the circuit nothing arrives from outside, so the same word means
something stronger — **hold this node at this temperature**, and the circuit is solved into whatever
valve position does it.

```fluidscript
fluidscript 1
TV_MAIN three_way_valve
N3 node t=60
connections
TV_MAIN.ab - N3
```

`TV_MAIN` blends hot source water with cool return; `N3` is the supply header, held at 60 °C.

The mixing valve feeding `N3` is what holds it, so `TV_MAIN.position` stops being a number you state
and becomes one the solve reports. You will see it in the solve report under *constraints, and what
answers each*:

```
NodeTemperature on N3         -> solved for as TV_MAIN.position
```

This is the same trade a heat exchanger makes when it states an inlet temperature, and it follows the
same rule: **a setpoint brings its own unknown.** One equation says the node is at 60; one unknown is
the position that gets it there. State a setpoint with no free mixing valve to hold it and the circuit
is over-specified, and says so rather than quietly ignoring you.

A setpoint and a pressure on the *same* node do not combine — a node stating `p` is a datum or a
boundary, and a temperature there is read the same way. Put the setpoint on the node you want held
and the pressure where you want the datum.

The dynamic counterpart is a controller: [`control`](control.md) drives the same valve toward the same
setpoint over time, where this solves for the position that holds it at steady state. Use one or the
other on a given actuator, never both — they are two answers to one question.

**A node at the edge of the model is better written as a boundary.** `S1 inlet t=60 flow=0.12` says
fluid enters there and `R1 outlet` says it leaves, which a bare node with the same parameters cannot:
a stub nobody finished wiring looks exactly like a deliberate dead end. See
[`inlet` and `outlet`](inlet-outlet.md).

## Properties

Readable as `N1.t` and so on, once the circuit has been solved.

| Property | Unit | Available |
|---|---|---|
| `t` | °C | after the solve |
| `p` | kPa | after the solve |
| `h` | kJ/kg | after the solve |
| `flow` | kg/s | after the solve |
| `rho` | kg/m³ | after the solve |

## Also written as

`point`, `junction`.

## See also

[`pipe`](pipe.md) · [`inlet` and `outlet`](inlet-outlet.md) · [`connections`](connections.md) · [Units](units.md)
