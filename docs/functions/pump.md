# pump

What makes the fluid move. A pump is the only component that adds head to a circuit.

```fluidscript
PU1 pump
PU2 pump head=15
```

A pump with no parameters is sized: its head comes out equal to the loop's total pressure drop, which
is the number you would otherwise have computed by hand to choose one.

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

Head is in metres **of the fluid being pumped**, which is what a pump curve is drawn in. It has no
unit symbol of its own; see [Units](units.md).

## Properties

`head`, `dp`, `flow`, `power`, `speed`, `efficiency`.

## Also written as

`circulator`.

## Tag

`PU` — a pump in circuit 400 is tagged `400PU01`.

## See also

[`pipe`](pipe.md) · [`valve`](valve.md) · [Units](units.md)
