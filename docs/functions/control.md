# control

Wires a controller to what it drives and what it measures.

```fluidscript
fluidscript 1
circuit heating
TC1 controller
control TV1 with TE1 by TC1 setpoint=20
```

Read it as a sentence: control the valve, using what the sensor reads, by that controller, to 20 °C.

## The short form

`control <what moves> with <what reads> by <which controller> setpoint=<target>`

Each of the first three is a component name. FluidScript knows what to move and what to read because
each kind names one of each: a valve's `position`, a pump's `speed`, a temperature sensor's reading.
Where a kind names none, you are asked to write it out, and the message says what to write.

`with` and `by` are ordinary words, not reserved ones — a component of yours may still be called
either.

## The named form

Every part written out. Use it when you want a parameter other than the usual one, or when the line
reads better spelled out:

```fluidscript
control actuate=TV1.position measure=N2.t by=TC1 setpoint=20
```

The two forms mix: `control TV1.position with TE1.t by TC1 setpoint=20` is the short shape with both
halves qualified, and it is fine.

**In the named form the arguments are named, not positional, and that is deliberate.**
`control 3WV.position N2.t TC1` and `control N2.t 3WV.position TC1` are both plausible-looking,
exactly one is right, and the wrong one would bind, solve, and drive the valve the wrong way. The
short form is safe for a different reason: a sensor cannot be actuated and a valve cannot be read, so
transposing them is caught rather than obeyed.

## Rules

- The setpoint lives here rather than on the controller, so one tuning can serve several loops.
- The setpoint is the loop's **design point**. When the actuator is left unstated, the design solve holds the
  measured temperature with it: a valve at whatever position does it, and a **pump through its head** -- a
  circulator holding its return at 20 °C gets the head that moves the design flow, and the run then moves its
  speed, starting at 1. A pump that states `head`, `dp`, `flow` or `vflow` has no head left to hold it with, so the
  setpoint is not a constraint and the loop may start off it ([`FS3210`](diagnostics.md)), as with any stated
  actuator.
- A setpoint may be a [`curve`](curve.md): `setpoint=supplyTemp` gives a compensated loop, where the
  target follows the outdoor temperature.
- A controller named by no `control` line drives nothing, and is reported. Two `control` lines naming
  one controller is an error: a controller holds one integral term and drives one actuator.
- A controller always reads through a **sensor**. Measure one you placed (`measure=TE1.t`), or write
  the node (`measure=NS.t`) and FluidScript puts a sensor there for you -- `NS__TE` for a temperature,
  `NS__PE` for a pressure, `NS__FE` for a flow -- with an info message saying so. Either way the
  diagram draws the sensor on its pipe and the controller's line comes from it. Write its name as a
  declaration (`NS__TE t_sensor at NS`) to make it yours; see [`t_sensor`](t-sensor.md).
- Measure **one stream**. A node read directly, like a sensor, must have one or two connections: at a
  junction where three pipes meet there is no single stream to read, and the line is
  [`FS1548`](diagnostics.md). Put a node on the pipe you mean.

## See also

[`controller`](controller.md) · [`t_sensor`](t-sensor.md) · [`curve`](curve.md) · [`schedule`](schedule.md)
