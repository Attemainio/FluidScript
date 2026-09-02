# project

The project's name, and the default for how the whole file is solved.

```fluidscript
fluidscript 1
project dynamic plant_01
```

## Rules

- `project <name>` names the file. `project dynamic <name>` or `project static <name>` also sets the
  default solve mode for every circuit in it.
- It appears once, before the first `circuit` header. Writing it later is an error rather than a
  silent reordering.
- A circuit that states its own [`fluid dynamic`](fluid.md) or `fluid static` overrides the project
  default locally, and a disagreement between the two is reported — not resolved quietly.

## See also

[`circuit`](circuit.md) · [`fluid`](fluid.md) · [`spacing`](spacing.md)
