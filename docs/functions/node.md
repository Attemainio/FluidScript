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

Omitting any of them means the solver works it out, which is the normal case.

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

**A node at the edge of the model is better written as a boundary.** `S1 supply t=60 flow=0.12` says
fluid enters there and `R1 return` says it leaves, which a bare node with the same parameters cannot:
a stub nobody finished wiring looks exactly like a deliberate dead end. See
[`supply` and `return`](supply-return.md).

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

[`pipe`](pipe.md) · [`supply` and `return`](supply-return.md) · [`connections`](connections.md) · [Units](units.md)
