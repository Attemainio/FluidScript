# schedule

Opens the list of what changes, and when, during a run.

```fluidscript
fluidscript 1
circuit demand
connections
N1 - HE1 - N2

schedule
at 60 s              HE1.power = 45
over 60 s .. 120 s   HE1.power = 30 .. 45
```

## Rules

- It comes after the circuit's [`connections`](connections.md), which is where a reader looks for it.
- `at <time> <target> = <value>` steps a value at an instant.
- `over <from> .. <to> <target> = <from> .. <to>` ramps one between two instants.
- The target is a component's parameter, written `Name.parameter`.
- A schedule needs a circuit solved in time. Under [`fluid static`](fluid.md) there is no time for it
  to act in, and that is reported.
- `at` and `over` are ordinary words, not reserved: they are read as scheduling only inside a
  schedule, so a component called `at` is still legal elsewhere.

## See also

[`fluid`](fluid.md) · [`connections`](connections.md) · [`control`](control.md)
