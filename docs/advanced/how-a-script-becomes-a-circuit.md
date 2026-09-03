# How a script becomes a circuit

Between the text you write and the numbers you get back there is a graph. You never write it and you
cannot edit it, but almost every confusing result is easier to understand once you know what it looks
like — so this page walks the whole of it, with the cooling loop from the tutorial as the example.

```
circuit cooling 100
fluid water

N1 node t=6 p=300
N3 node p=280
PU1 pump head=6 flow=0.24
HE1 heat_exchanger power=30
3WV three_way_valve kv=6.3
P1 pipe length=10 dn=25

connections
N1 - N2
N2 - PU1
PU1 - HE1
HE1 - 3WV
3WV - N2
3WV - P1
P1 - N3
```

## Nodes: where state lives

**Every pressure and every temperature in a result belongs to a node**, and there are more nodes than
you wrote. The script above declares two — `N1` and `N3` — and the graph has six:

| Node | Where it came from |
|---|---|
| `N1`, `N3` | You wrote them. They carry the boundary conditions |
| `N2` | You named it in `connections` without declaring it, so it was created |
| `PU1__HE1`, `HE1__3WV`, `3WV__P1` | Two components were connected directly, and a node was put between them |

The last three exist because **components do not connect to each other; they connect to nodes.**
Without that rule there would be nowhere for the temperature between the pump and the exchanger to
live, and that temperature is the one you usually want to see. The generated names use `__` so they
are recognisable, and the diagram draws them smaller than the ones you named.

A pipe with `nodes=n` adds more: `nodes=4` becomes five sub-pipes with four nodes between them, each
carrying a quarter of the pipe's water. That is how travel time along a long run shows up in a
transient — and it is off by default, because most pipes are not long enough for it to matter.

## Branches: where flow lives

**Flow does not belong to a component. It belongs to a branch** — a run between two places where flow
divides. Everything along a branch carries the same flow, because there is nowhere for it to go.

The cooling loop has four:

| Branch | From → to | What is along it |
|---|---|---|
| 1 | `N1` → `N2` | nothing: a bare connection is a perfect, lossless link |
| 2 | `N2` → `3WV.a` | `PU1`, `HE1`, and the two nodes between them |
| 3 | `3WV.b` → `N2` | nothing — the recirculation leg |
| 4 | `3WV.c` → `N3` | `P1` |

This is why a chain of five pipes in series solves as fast as one: it is one flow, not five.

### Where a branch ends

A branch ends where flow can divide, or where the circuit does:

- **A node with three or more connections.** `N2` has three, so branches end there.
- **A three-way valve.** All three of its ports carry different flows.
- **A tank with three or more ports**, for the same reason.
- **A terminal** — a node with one connection. A branch has to end somewhere.

A node with exactly *two* connections is not one of these. It still carries a temperature and a
pressure; it simply sits in the middle of a branch.

**A heat exchanger with all four ports connected is not one either**, which surprises people. It has
four ports, but they are two independent pairs: nothing flows from the hot side to the cold side. So
it sits in the middle of a branch on each side, and appears in two of them. If it split flow the way
a three-way valve does, the model would be asserting that water crosses between the two circuits.

## Loops

Count the branches, count the places they end, and the difference plus one is the number of
independent loops. The cooling loop has four branches and four ends, so it has one: the circulation
`N2 → PU1 → HE1 → 3WV → N2`.

Loops are not extra equations — they fall out of the pressures already being solved. They matter for
two other things: the diagram is laid out around them, and **a loop with nothing to drive flow around
it is worth telling you about**, because it will carry none and every temperature downstream of it
will be wrong in a way that still looks like a solved circuit.

## What is not in the graph

- **Instruments.** A `t_sensor` reads a node; it does not sit in the flow path. Adding a hundred of
  them changes nothing about the circuit and nothing about how long it takes to solve.
- **Controllers.** Same reason: a controller moves a valve, it does not carry water.
- **Circuit boundaries.** Several circuits in one file are one graph. A circuit is a name and a
  grouping, not a wall — attaching a subcircuit to its parent joins them hydraulically, and they
  share one pressure reference from then on.

## See also

[`connections`](../functions/connections.md) · [`node`](../functions/node.md) ·
[`pipe`](../functions/pipe.md) · [`three-way valve`](../functions/three-way-valve.md)
