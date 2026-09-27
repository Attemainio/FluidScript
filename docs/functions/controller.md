# controller

A control loop, in one declaration: the controller's type, what it moves, what it reads, the target
it holds, and its tuning.

```fluidscript
fluidscript 2

circuit "Heating":
  fluid = water

  BLR  heater    power = 150  out.t = 70 C
  SP   pump
  RAD  radiator  power = 150  out.t = 40 C
  TV1  valve3    stroke = 90 s
  TE1  temperature_sensor
  TC1  controller:
    type     = PI
    moves    = TV1
    reads    = TE1
    setpoint = 60 C
    band     = 20 K
    ti       = 120 s
    output   = 10..100 %

  BLR - TV1 - SP - TE1 - RAD - NR
  NR - TV1
  NR - BLR                   6 m  DN32
```

Read it as a sentence: a PI controller moves the mixing valve to hold what the sensor reads at 60 °C.
The sensor sits in the chain, on the supply between the pump and the radiator.

## Settings

| Setting | Meaning | When it is left out |
|---|---|---|
| `type` | `P`, `PI`, `PID`, `onoff` or `curve` | `PI` |
| `moves` | The actuator: a component, meaning its one actuated parameter, or a qualified parameter (`BLR.power`) | Required ([`FS1521`](diagnostics.md)) |
| `reads` | What is measured: a sensor, or a node's property (`NS.t`); a `curve` controller may read a driver or `time` | Required ([`FS1521`](diagnostics.md)) |
| `setpoint` | The target, in what `reads` measures: a number, a [`curve`](curve.md), a [`let`](let.md) or a list per case | Required ([`FS1521`](diagnostics.md)) |
| `band` | The proportional band, in what `reads` measures: the error over which the output travels its whole range | Estimated when a run starts |
| `kp` | The gain, as an alternative to `band`; stating both is [`FS1809`](diagnostics.md) | Estimated when a run starts |
| `ti` | The integral time | Estimated when a run starts |
| `td` | The derivative time | No derivative action |
| `output` | The output limits, a range in the actuator's unit: `10..100 %` of a valve's position | The actuator's full range |
| `action` | `direct` or `reverse`, stated only as a check: the direction is measured from the plant, and a stated action that contradicts it is an error | Measured from the plant |
| `differential` | The switching differential of an `onoff` controller, in what `reads` measures | — |
| `curve` | The characteristic of a `curve` controller: its output as a function of the reading | — |

The settings may share lines, `TC1 controller  moves = TV1  reads = TE1  setpoint = 60 C`, or sit in
the block as above. Anything that is none of these is [`FS1503`](diagnostics.md), listing them.

**What a component means in `moves`.** Each kind names one parameter an actuator moves: a valve's
`position`, a pump's `speed`. Where a kind names none — an exchanger — you are asked to write the
parameter out ([`FS1531`](diagnostics.md)): `moves = HE1.power`.

**Units follow the loop.** `band` and `differential` are differences in what is measured, so on a
temperature they are written in `K`: `band = 20 C` is [`FS1304`](diagnostics.md). A setpoint in
another dimension than the measurement is `FS1304` too. `output` is in the actuated parameter's unit,
and a limit outside that parameter's range is refused ([`FS2105`](diagnostics.md) for a position).

## Types

| `type` | Needs | In the design solve | In a run |
|---|---|---|---|
| `P` | `band` or `kp` | Holds the setpoint | Settles with an offset; the report states it |
| `PI` | as `P`, and `ti` | Holds the setpoint | Drives the actuator |
| `PID` | as `PI`, and `td` | Holds the setpoint | As `PI`, derivative on the measurement |
| `onoff` | `differential` | Holds nothing; the actuator sits at its "on" value | Cycles between the ends of `output` |
| `curve` | `curve` | The actuator takes the curve's value at the case's reading | Follows the reading, open loop |

A setting that does not belong to the stated type — `td` on a `PI`, `band` on an `onoff` — is
[`FS1808`](diagnostics.md), and the controller binds as the type it states. Only `PI` is run by the
solver today: every other type binds, and [`FS1810`](diagnostics.md) says it is not run yet rather
than running it as a `PI` in silence.

## The setpoint is the design point

In the design solve a controller's setpoint is a constraint: the actuator is left free, and the solve
finds the position that holds it. A valve takes whatever position does it, and a **pump holds it
through its head** -- a circulator holding its return at 20 °C gets the head that moves the design
flow, and a run then moves its speed, starting at 1. A pump that states `head`, `dp`, `flow` or
`vflow` has no head left to hold it with, so the setpoint is not a constraint and the loop may start
off it ([`FS3210`](diagnostics.md)), as with any stated actuator.

- A setpoint may be a [`curve`](curve.md): `setpoint = supply_temp` gives a compensated loop, where the
  target follows the outdoor temperature. A setpoint that reads a driver is evaluated in every case,
  and each case's design solve holds its own.
- In a run, an event may move the setpoint: `at 1 h  TC1.setpoint = 55 C` ([`run`](run.md)).
- An actuator's speed is the actuator's, not the controller's: `TV1 valve3 stroke = 90 s` limits the
  valve however it is moved.

## What it reads

A controller always reads through a **sensor**. Read one you placed (`reads = TE1`), or write the
node's property (`reads = NS.t`) and FluidScript puts a sensor there for you -- `NS__TE` for a
temperature, `NS__PE` for a pressure, `NS__FE` for a flow -- with an info message saying so. Either
way the diagram draws the sensor on its pipe and the controller's line comes from it. Declare that
name yourself (`NS__TE temperature_sensor at NS`) to make it yours; see [`t_sensor`](t-sensor.md).

Read **one stream**. A node read directly, like a sensor, must have one or two connections: at a
junction where three pipes meet there is no single stream to read, and the controller is
[`FS1548`](diagnostics.md). Put a node on the pipe you mean.

## Ports

None. A controller carries no flow and is not part of the hydraulic graph.

## Properties

None yet.

## Also written as

`pi`, `pid`, `p`, `thermostat`. The kind's spelling chooses nothing: `type` does, so `TC1 pid:` with
no `type` is a `PI` controller.

## Tag

`PID` — a controller in circuit 400 is tagged `400PID01`.

## See also

[`t_sensor`](t-sensor.md) · [`curve`](curve.md) · [`run`](run.md) · [`three_way_valve`](three-way-valve.md)
