# design

The conditions the plant is sized for.

```fluidscript
design tout=-26
```

Everything that is sized automatically is sized for this. A radiator circuit sized at −26 °C outside
is a different circuit from one sized at −10 °C, and `design` is where you say which.

## With scenarios, it names one of them

If the file has a [`scenarios`](scenarios.md) line, `design` takes a case name instead of driver
values, and its job is smaller:

```fluidscript
scenarios winter summer
design winter
```

It then chooses **which case you are looking at** — the canvas, the numbers on it, a static export,
the starting state of a run — and **nothing about size**. Every case was sized for. There is no
default, so declaring scenarios without a `design` line is [`FS1543`](diagnostics.md).

The rest of this page is the driver form, for a file with no scenarios.

## What it does

**It sizes.** Pumps, pipes and valves with no stated size are chosen for the design condition,
whatever else the file goes on to do.

**It also operates, when there is no clock.** In a circuit solved as a steady state
(`fluid static`), every [`curve`](curve.md) is read once at the design condition and holds that
value — so a heating curve at `design tout=-26` gives its −26 row and the model is fixed numbers
again.

In a circuit solved in time (`fluid dynamic`), curves follow the clock and `design` only sizes. This
is the ordinary way to work: size for the design day, then simulate the year.

## Naming more than one condition

One per driver, on one line or several:

```fluidscript
design tout=-26
design tground=8
```

## Units

The bare number takes the driver's usual unit, so `tout=-26` is −26 °C. Write the unit if you prefer:

```fluidscript
design tout=-26 C
```

## Sizing one component somewhere else on the curve

A bivalent plant is a heat pump sized part-way up the heating curve and a boiler for the rest. The
heat pump is not sized at the design day — that would mean a machine that cycles all winter — so it
says where it *is* sized:

```fluidscript
design tout=-26
curve heating tout
-26  50
 20   0

circuit heating
fluid water

HP1  heater power=heating sized_at tout=-5
BL1  heater
LOAD load power=heating in.t=70 out.t=40
PU1  pump
P1   pipe length=20 dn=32

connections
N1 - PU1 - HP1 - N2 - BL1 - N3 - LOAD - P1 - N1
```

`sized_at` reads every curve on that line at its own point instead of the file's. `HP1.power` is the
curve at −5 °C, 27.2 kW, and that is the heat pump's capacity. The load reads the same curve at −26
and asks 50 kW, and the boiler, which wrote nothing, is sized to the 22.8 kW that remains. Two
components can read one curve at two points; the curve itself does not change.

The report tells you what fraction of the design day that was:

```
HP1.power   27.174 kW at tout=-5, 0.54 of the 50 kW the design day asks
```

You choose the point, not the percentage — that is how a bivalent system is specified, and the
percentage is what you check afterwards. A bivalence point near −5 °C typically leaves the backup a
few percent of the year's heat; one at 0 °C hands it a third.

Everything about `design` applies to `sized_at`: the same driver names, the same units, the same
check that `tout=3 bar` is not a temperature. Name more than one driver on the clause if the curves
need them. A component's own point is enough for its own curves even when the file states no
`design` at all.

## When you need it

Whenever a static circuit reads a curve. FluidScript will tell you which driver has no value rather
than guessing one — a plant sized against a number nobody chose is worse than one that refuses to
size.

## See also

[`curve`](curve.md) · [`project`](project.md) · [`fluid`](fluid.md)
