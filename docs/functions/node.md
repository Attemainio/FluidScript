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
| `t` | °C | Fixes the temperature |
| `p` | kPa | Fixes the pressure. The first stated `p` in a circuit is also its pressure datum |
| `flow` | kg/s | Terminal flow. Positive follows the nominal connection: an upstream terminal injects, a downstream one extracts |

Omitting any of them means the solver works it out, which is the normal case.

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
