# connections

Opens a circuit's topology. Every line after it, until a [`schedule`](schedule.md) line or the next
[`circuit`](circuit.md), joins components.

```fluidscript
fluidscript 1
circuit coolingLoop
connections
N1 - HE1 - N2 - PU1 - N1
```

## Rules

- `A - B` joins two components. `A - B - C` is one line and two connections, and it stays one line in
  your file.
- A port is named with a dot: `TV1.b - N2`. Without one, connections bind to the component's ports in
  order.
- **A component declaration is legal here too.** Boundary conditions usually read better below the
  topology than above it:

```fluidscript
fluidscript 1
circuit coolingLoop
connections
N1 - HE1 - N2

N1 node t=6 p=300
```

- Connecting two components directly creates the node between them that their states need.
- One `connections` line per circuit. A second is ignored, with a warning.

## See also

[`circuit`](circuit.md) · [`schedule`](schedule.md) · [`node`](node.md)
