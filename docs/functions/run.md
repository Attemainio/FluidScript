# run

What happens to the plant in time. The rest of the file says what the plant is; a run says how it is
disturbed, starting from one of its cases, and the interface plays the run you choose.

```fluidscript
fluidscript 2

project:
  cases = [winter, mild]

let outdoor = [-26, 5] C

curve weather_jan: time
  2026-01-15 06:00   -18
  2026-01-15 12:00    -9

circuit "Heating":
  fluid = water
  SP   pump
  RAD  radiator  power = 150
  SP - RAD - SP

run "Cold morning":
  from     = winter
  start    = 2026-01-15 06:00
  duration = 2 h
  frame    = 10 s

  outdoor  = weather_jan
  at   10 min       RAD.power = 100 kW
  over 30..40 min   RAD.power = 100..80 kW
```

A file may hold several runs, each with its title. None of them changes the design solve.

## Settings

| Setting | Meaning | When it is left out |
|---|---|---|
| `from` | The case whose steady state the run starts from; a name the project's `cases` does not list is [`FS1542`](diagnostics.md) | The first case |
| `start` | Where the clock sits on curves of time, as an unquoted date with an optional time, `2026-01-15 06:00`; one that is not a time is [`FS1545`](diagnostics.md) | A run that follows a curve of time needs it ([`FS1546`](diagnostics.md)) |
| `duration` | How much time is simulated | 10 min |
| `frame` | The simulated time between kept states | 1 s |
| `steady` | Circuits held quasi-steady in this run, by title: `steady = ["District primary"]` | Every circuit is solved in time |

## Overrides

Any other `name = value` in a run is an **override**: it holds from the start of this run and changes
nothing outside it.

- `RAD.power = 20 kW` sets a parameter.
- `outdoor = -10 C` sets a driver to a value; everything that reads it follows.
- `outdoor = weather_jan` makes a driver follow a curve of time, and every curve of that driver moves
  with the clock.

The run then begins by moving from the case's steady state to the new condition, which is what the
plant would do.

## Events

An event starts with `at` or `over`, and only a run has them.

- `at T  target = value` is a step at `T`.
- `over T1..T2  target = v1..v2` is a ramp, and **both ends are written**. A single value after `over`
  is [`FS1807`](diagnostics.md): for a step, write `at`.

A time is a duration from the start of the run (`10 min`, `1 h`), or, when the run states `start`, a
clock time (`07:30`) — the next one at or after the start. A clock time without `start` is
[`FS1816`](diagnostics.md), and an event that starts after the run's `duration` never happens and is
[`FS1817`](diagnostics.md).

An event replaces whatever drove its target: after `at 10 min RAD.power = 100 kW` the duty no longer
follows its curve. A controller's `setpoint` is a target too (`TC1.setpoint = 55 C`), read in what the
controller measures.

- The target is a component's parameter, `Name.parameter`. A run can move what the solver resolves
  at solve time — an exchanger's `power`, a valve's `position` or `kv`, a pump's `head`. A size, and
  for now a boundary's state (an inlet's `t`), is refused with [`FS3105`](diagnostics.md) when the run
  starts, and a parameter a [`controller`](controller.md) moves with [`FS3109`](diagnostics.md).
- The run lands a step boundary on every event's instant: the frame at the instant shows the plant
  after the change ([Discretized pipes and the run in time](../advanced/discretized-pipes.md)).

## See also

[`project`](project.md) · [`curve`](curve.md) · [`let`](let.md) · [`controller`](controller.md) ·
[Discretized pipes and the run in time](../advanced/discretized-pipes.md)
