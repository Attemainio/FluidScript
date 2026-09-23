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

## Where a run starts in time

A file solved in time that reads a [curve of time](curve.md#curves-of-time) needs to say where the
run's clock starts on it:

```fluidscript
fluidscript 1
project dynamic plant_01 start="2026-01-15T06:00:00"
```

The run's first second is 06:00 on 15 January, and every curve of time is read at that instant plus
however long the run has gone. Start the same file at `start="2026-01-15T14:00:00"` and it runs the
afternoon instead, with nothing else changed.

- **Quote it.** `2026-01-15` unquoted is a subtraction. Plain Unix seconds need no quotes:
  `start=1768456800` is the same instant.
- **The same dates a curve reads:** ISO 8601, to the day, minute or second, or Unix seconds. No time
  zone is written or read, in a start or in a curve row, so the two always agree.
- **Without it,** a file that follows a curve of time in a circuit solved in time gets
  [`FS1546`](diagnostics.md) on each parameter that does. The design solve still stands; a run holds
  those parameters at their design values.
- A start that does not read as a time is [`FS1545`](diagnostics.md). A start in a file where every
  circuit is `static` does nothing and says so, [`FS1547`](diagnostics.md).

**The run begins from the design state.** If the curves at your start differ from the design day,
the first thing the run shows is the plant meeting that difference.

## See also

[`circuit`](circuit.md) · [`fluid`](fluid.md) · [`spacing`](spacing.md)
