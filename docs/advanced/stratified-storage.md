# Stratified storage

A tank is the one component whose whole purpose is to hold energy until later, so it is also the one
whose behaviour in a run is different in kind from its behaviour in a static solve.

In a static solve a tank is a mixing junction. Everything that flows in mixes, everything that flows
out leaves at one temperature, and the layer count changes nothing. In a run it is a stack of
equal-volume, perfectly mixed layers, numbered from the bottom, and each one is a state the solver
integrates.

## Ports pick layers, and that is the whole feature

A port's `level` is a fraction of the vessel, not a height in metres. It decides which layer the port
talks to:

```fluidscript
T1 tank volume=300 dm3 layers=5 in.level=90% out.level=90% in[2].level=30% out[2].level=30%
```

The rule is `min(floor(level × layers) + 1, layers)`, so 0 is the bottom layer, 30 % of a five-layer
tank is layer 2, and 1 is the top. A draw at 90 % takes the top layer's water and a draw at 30 %
takes layer 2's, which is what lets one vessel serve a radiator circuit at 60 °C and a ventilation
coil at 30 °C at the same time. In a static solve both would get the same mixed temperature, and the
point of the store would be invisible.

A level forms no pressure. The vessel has one `elevation`, which is where it sits in the plant, and
the layers share one pressure between them.

## The initial profile

By default every layer starts at the mixed temperature the design solve found. State a profile and
the run starts somewhere else:

```fluidscript
T1 tank volume=300 dm3 layers=5 layer[1].t=25 layer[2].t=30 layer[3].t=40 layer[4].t=50 layer[5].t=60
```

Either write `t` once for a uniform start, or write every layer. Half a profile is an error, because
the layers you left out have no sensible default once the others are stated.

**A profile is a disturbance.** A run with no schedule normally sits exactly where it started, and
that is the strongest check the solver has. A stated profile is the exception: the vessel begins away
from equilibrium on purpose, so it evolves from the first step with nothing in the schedule at all.

## Layers cannot float on heavier water

A stack can integrate itself into an arrangement no real vessel holds: a warm inflow into a low layer
leaves light water under heavy water. In a real tank convection turns that over in seconds, far faster
than the solver's step, so it is applied as a correction after each step rather than modelled as a
flow.

Only the smallest violating block moves. Adjacent layers that are out of order are pooled to their
combined temperature, and the check repeats downwards until the stack is stable. A single inversion in
the middle leaves the layers above and below it exactly where they were. The pooling conserves mass
and energy exactly, so it never shows up as drift.

**It compares density, not temperature.** Water is densest at about 4 °C, so below that colder water
is *lighter* and a chilled store that is warm at the bottom is the one resting correctly. A rule
written as "hotter floats" would be right for a heating buffer and would stir an ice store that was
doing nothing wrong.

## Choosing the layer count

More layers give a sharper picture of a charge front moving through the vessel and cost more states.
`layers=1` is a fully mixed vessel with a closed-form response, useful as a check and as a model of a
tank with no real stratification, such as one with a strong internal circulation pump.

The step limit applies here as it does to a pipe: the shortest residence time among the layers bounds
the step, and a layer with no flow through it imposes no limit at all. A tank drawn at a low rate
therefore costs nothing in step size, which is the opposite of a finely divided pipe.

Doubling the layer count should change the drawn temperature by less each time. When it stops
changing, the vessel is resolved.

## What is not modelled

No heat loss to the room, no conduction through the vessel wall, and no mixing from the momentum of
an incoming jet. A stored temperature therefore holds indefinitely unless something draws from it. Any
of those would make a store decay for a reason the script never mentions, and the first question a
user asks about an unexpected number should have an answer in their own file.

## See also

[`tank`](../functions/tank.md) · [Discretized pipes and the run in time](discretized-pipes.md) ·
[`schedule`](../functions/schedule.md)
