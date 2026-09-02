# fluid

What a circuit carries, and how it is solved.

```fluidscript
fluidscript 1
circuit coolingLoop
fluid water
```

```fluidscript
fluidscript 1
circuit storage
fluid dynamic water
```

## Rules

- `fluid <substance>` names the working fluid for the circuit it appears in.
- `fluid dynamic` solves in time; `fluid static` solves as a steady state. Either overrides the
  default [`project`](project.md) set.
- Arguments after the substance describe it — a glycol fraction, for instance — and are read in the
  order the substance defines.
- Air-side substances are deliberately not accepted in this version. A fan-and-duct model needs
  humidity balance, condensation, leakage and fan curves; naming an air fluid without them would
  produce a hydronic answer wearing air-side names.

## See also

[`project`](project.md) · [`circuit`](circuit.md) · [`schedule`](schedule.md)
