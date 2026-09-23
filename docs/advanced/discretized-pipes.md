# Discretized pipes and the run in time

A static solve answers *where the plant settles*. A run answers *how it gets there*: the front of
warmer water leaving an exchanger, crossing a pipe, arriving at the node a controller measures some
seconds later. That delay is the whole reason a run exists, and it is set by one parameter that looks
cosmetic: `nodes=` on a pipe.

This page is about what a run computes, what `nodes=` buys and costs, and how to read a run that is
slow, stopped, or still moving at the end.

## What is dynamic and what is not

Hydraulics are fast and thermals are slow. Pressure equalises across a water circuit in tens of
milliseconds; a temperature front travels at the flow velocity, about a metre a second, and takes
minutes. So a run keeps the flows and pressures **algebraic** — solved fresh at every step exactly as
a static solve does — and integrates only the **enthalpy of the volumes that store fluid**: the cells
of a discretized pipe and the layers of a tank. A node with no volume behind it, such as a mixing
point, a terminal or an exchanger's outlet, has no time constant and takes whatever its inflows bring
it, instantly.

The consequence you will notice first: **a heat exchanger has no volume in this version.** When its
duty steps, its outlet temperature jumps within one step. The lag you see downstream is the pipe's,
not the exchanger's.

## What a stated value means once time moves

The static solve is the design point. In a run, every value the script wrote keeps its meaning at
t = 0 and then one of three things happens to it:

| Written | At t = 0 | At t > 0 |
|---|---|---|
| A boundary condition — `inlet t=6 p=300` | Held | Held, unless the schedule moves it |
| An input — `power=30` | Held | Held, unless the schedule moves it |
| A stated design temperature on equipment — `out.t=50` on an exchanger | Held, and it chose something: a pump head, a valve position | **Released.** What it chose stays where the design solve put it; the temperature follows the flow |
| A sized value — a pipe's `dn`, a pump's head | Sized | Frozen |
| A `control` line's setpoint | Held, by choosing the actuator | The controller drives the actuator (when controllers ship) |

There is no implicit thermostat. `out.t=50` names the flow the exchanger was designed for, not a
promise that the outlet stays at 50 °C when the load doubles. If you want a value followed in time,
write a [`control`](../functions/control.md) line.

## The `nodes=` trade

Write `PB pipe length=8 dn=20 nodes=4` and the 8 m of pipe becomes four cells of 2 m, each holding
0.74 litres of water at the DN20 bore of 21.7 mm. At a flow of 0.076 kg/s each cell has a residence
time of 9.6 s, and a front entering the pipe appears at the far end about 38 s later — the sum of the
four.

What the cell count changes is the **shape** of the arrival, not its mean time:

- `nodes=1` smears the front into a single first-order lag. At one residence time the outlet has
  reached 63 % of the change, not 0 %.
- `nodes=4` gives a recognisable front: 2 % at 10 s, 16 % at 20 s, 38 % at 30 s.
- `nodes=8` is visibly sharper; `nodes=20` sharp; `nodes=100` very sharp at a hundred times the state
  count.

Both are "correct" for their discretization. Doubling `nodes=` should change what you see by less
each time; when it stops changing, you have enough.

**What it costs.** An explicit integrator cannot step further than the smallest cell's residence
time, times a safety margin of 0.9. Four cells of 9.6 s allow 8.6 s; forty cells of 0.2 m allow
0.87 s. Below the one-second frame interval the limit binds and the run reports `FS3101`, naming the
cell that is limiting. That message is the answer to "why is my run slow": the pipe it names has more
cells than the frame rate can use.

**On an air duct the advice inverts.** Air is 800 times less dense, so a duct cell of the same length
holds 800 times less mass and its residence time is a fraction of a second. A high `nodes=` on a duct
buys almost nothing visible — a front crosses 8 m of duct in under two seconds — and costs twenty
times the steps. Keep ducts coarse.

**On the diagram, the pipe is drawn as its cells**, in line along the pipe it replaces: each stretch
between two internal nodes is coloured from their own values, so the profile along the pipe is a
gradient of real states rather than a line interpolated between its ends, and a front travelling down
it during a run is visible as it moves.

## The schedule

A run has to be disturbed. The [`schedule`](../functions/schedule.md) section is where:

```fluidscript
schedule
at 60 s              HE1.power = 45
over 60 s .. 120 s   HE1.power = 30 .. 45
```

The first is a step at an instant; the second a linear ramp. A single value over a span is a step
**at the end** of the span. The integrator lands a step boundary on every scheduled instant, so the
frame at 60 s shows the plant *after* the step — the exchanger's outlet already at its new
temperature — and the frame at 59 s shows it before.

What a schedule can move is what the solver resolves at solve time: an exchanger's `power`, a
valve's `position` or `kv`, a pump's `head`. A boundary temperature, a pipe's diameter or a tank's
volume are fixed at assembly, and scheduling one is refused with `FS3105` rather than silently
ignored. A parameter a `control` line drives cannot also be scheduled (`FS3109`); move the setpoint
instead.

## Reading a run

Every frame carries the whole state at that instant, the number of integration steps it took to get
there, the smallest of those steps, and whatever the run had to say.

**Settled** means every stored enthalpy has moved by less than the integrator can resolve over each
of the last ten frames. A run that reaches its horizon before settling says so with `FS3104`, which is
information, not a fault: extend the horizon. A run that has settled still runs to its horizon,
because the schedule may have a later entry.

**Drift** is the run's self-check. The energy stored in the cells and layers is compared, at every
step, with the heat the exchangers injected and the boundaries carried in, integrated over the run.
On a correct run the two agree to a few parts in a billion. `FS3106` warns at one percent; five
percent is a broken run and stops it with `FS3107` before an unverified frame is shown.

**The settled state is a steady solve of the post-step system.** When the demand-step loop settles
after its load rises from 30 to 45 kW, the exchanger's outlet is at 72 °C, not the 65 °C it jumped to
at the step. The jump held the flow and the inlet at their pre-step values for one instant; then the
warmer recirculation raised the inlet, and the outlet followed. The static solver, given the same
circuit with 45 kW and the valve and pump where the design solve left them, gives the same 72 °C.
That agreement between two solvers that share no time-stepping code is the strongest check a run has,
and it is what the test suite holds.

**A run that stops early** ends on the last frame whose flows and pressures balanced, with an error
saying why: `FS3103` when the circuit could not be balanced at some instant (the usual cause is a
scheduled value outside what the fluid can do), `FS3102` when the step fell below its floor because
something changed faster than the model can follow.

## See also

[`pipe`](../functions/pipe.md) · [`schedule`](../functions/schedule.md) · [`control`](../functions/control.md) ·
[`tank`](../functions/tank.md) · [Reading the solve report](reading-the-solve-report.md)
