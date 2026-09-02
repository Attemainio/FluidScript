# supply and return

Where a subcircuit takes flow from its parent, and where it gives it back.

```fluidscript
fluidscript 1
circuit branch1 200
supply N3
return N5
```

`supply` names the parent node the subcircuit draws from; `return` names the one it feeds. Together
they are what makes several circuits on one distribution header a model rather than three unrelated
drawings.

## Rules

- One `supply` and one `return` per circuit. A second of either is an error.
- Each names a node in the parent circuit.
- Writing `in N3` instead is an error that names `supply` — it is never read as a component called
  `in` of kind `N3`.

## See also

[`circuit`](circuit.md) · [`node`](node.md) · [`connections`](connections.md)
