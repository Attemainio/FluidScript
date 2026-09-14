# pump

What makes the fluid move. A pump is the only component that adds head to a circuit.

```fluidscript
PU1 pump
PU2 pump head=15
```

A pump with no parameters is sized: its head comes out equal to the loop's total pressure drop, which
is the number you would otherwise have computed by hand to choose one.

## How the head is chosen

The head is the resistance of the circuit the pump drives, at the flow the circuit's **duty** fixes —
a heat exchanger with a power and two temperatures fixes it through an energy balance, and the pump is
then sized to it. Everything on the loop states its own drop, so the number includes your pipes,
valves and exchangers and nothing you did not write down.

**A loop with a second pump on it is that pump's to drive, not this one's.** Two pumps in series on
one loop have one head between them, and the loop equation alone does not say how it divides. The
convention is primary–secondary practice: the primary pump is sized for the primary circuit, each
secondary pump for its own. So when every loop through a pump carries another pump — two boilers
with their own pumps feeding consumers with their own — the source pump is sized to its own branch,
the header sits at no differential, and each consumer's pump develops its own circuit's drop. A pump
that shares no loop is sized to the worst of the loops through it, as before.

Pressure becomes metres at the density **entering the pump**, not at the loop's average. On the simple
loop that is 51.7 kPa at 998 kg/m³, or 5.28 m; the same drop at the loop's 35 °C mean would read
5.30 m. The gap is small on one ring and grows with the temperature spread, so the reported head and a
hand check will only agree if the hand check uses the inlet too.

`margin` multiplies the result and is reported when you set it, so a head of 5.81 m says
`margin 1.1` beside it. It is a design allowance, not a stand-in for fittings nobody modelled — those
belong in a pipe's `minor_loss`.

### When the head comes out zero

Zero is a real answer, and three different omissions produce it. The reported reason says which:

| What you are told | What is missing |
|---|---|
| `on no closed circuit` | The pump is declared but never connected into a loop — check your `connections`. |
| `no flow was estimated` | Nothing sets a flow. Give the circuit a duty: an exchanger with a power and two temperatures, or a stated flow. |
| `no modelled resistance` | The loop has flow but nothing that resists it. Add the pipe, valve or exchanger drop you meant to write. |

## Ports

`in` and `out`.

## Parameters

| Parameter | A bare number means | Meaning | If you omit it |
|---|---|---|---|
| `head` | m | Head at the duty point | Sized from the circuit |
| `dp` | kPa | Pressure rise, an alternative to `head` | Sized |
| `flow` | kg/s | Duty flow | Sized |
| `speed` | — | Relative speed, for variable-speed control | Sized, or driven by a controller |
| `efficiency` | — | Hydraulic efficiency | 0.7, a typical wet-rotor circulator |
| `margin` | — | Head multiplier applied only when auto-sizing | 1.0 — size to the computed duty, with no spare |
| `elevation` | m | Height above the project datum; see [`node`](node.md#height) | Wherever it is wired to, else 0 m |

Head is in metres **of the fluid being pumped**, which is what a pump curve is drawn in. It has no
unit symbol of its own; see [Units](units.md).

A pump's `elevation` changes the pipes attached to it, never the pump's own equation: the water a
pump lifts up a riser comes back down the return, and round a closed loop the two cancel, so a sized
head is friction alone whatever the building's height.

### What is checked

An `efficiency` below 0 or above 1 is [`FS2108`](diagnostics.md). Between 0 and 0.1, or above 0.95,
it binds and produces [`FS1306`](diagnostics.md) instead: unusual for a circulator, but a number a
real machine could have.

## Properties

`head`, `dp`, `flow`, `power`, `speed`, `efficiency`.

## Also written as

`circulator`.

## Tag

`PU` — a pump in circuit 400 is tagged `400PU01`.

## See also

[`pipe`](pipe.md) · [`valve`](valve.md) · [Units](units.md)
