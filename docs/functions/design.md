# design

The conditions the plant is sized for.

```fluidscript
design tout=-26
```

Everything that is sized automatically is sized for this. A radiator circuit sized at −26 °C outside
is a different circuit from one sized at −10 °C, and `design` is where you say which.

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

## When you need it

Whenever a static circuit reads a curve. FluidScript will tell you which driver has no value rather
than guessing one — a plant sized against a number nobody chose is worse than one that refuses to
size.

## See also

[`curve`](curve.md) · [`project`](project.md) · [`fluid`](fluid.md)
