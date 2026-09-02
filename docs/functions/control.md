# control

Wires a controller to what it drives and what it measures.

```fluidscript
fluidscript 1
circuit heating
TC1 controller
control actuate=TV1.position measure=N2.t by=TC1 setpoint=20
```

## Rules

- Four named arguments: `actuate` (what it drives), `measure` (what it reads), `by` (which controller),
  `setpoint` (the target).
- **The arguments are named, not positional, and that is deliberate.** `control 3WV.position N2.t TC1`
  and `control N2.t 3WV.position TC1` are both plausible-looking, exactly one is right, and the wrong
  one would bind, solve, and drive the valve the wrong way. Four extra words buy an error instead of a
  plausible wrong answer.
- `actuate` must name a *parameter*, not a component: `actuate=TV1` is rejected, because a valve has
  more than one thing you could move.
- The setpoint lives here rather than on the controller, so one tuning can serve several loops.
- A controller named by no `control` line drives nothing, and is reported. Two `control` lines naming
  one controller is an error: a controller holds one integral term and drives one actuator.

## See also

[`controller`](controller.md) · [`three_way_valve`](three-way-valve.md) · [`schedule`](schedule.md)
