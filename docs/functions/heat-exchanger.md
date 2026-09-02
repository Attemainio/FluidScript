# heat_exchanger

Heat source, heat consumer, or a real two-sided exchanger — one kind covers all three. A negative
`power` is a consumer.

```fluidscript
HE1 heat_exchanger power=30 in=20 out=50
```

## What it is doing depends on what you wrote

There is no `mode=` parameter. Which of the three it is follows from the script:

| It is | when |
|---|---|
| **Duty** | Nothing describes a second side. A stated duty crosses the model boundary; no area or effectiveness is claimed |
| **Rated** | No second-side connections, but one of `in2`, `out2`, `dt2`, `flow2` is stated. Side 2 is an external profile |
| **Coupled** | Either second-side port is connected. Two real streams, coupled by ε-NTU |

Coupled wins over Rated, so connecting a real second side to an external-profile design has one
predictable meaning.

## Ports

`in` and `out` for side 1; `in2` and `out2` for side 2, both optional. If you connect one of the pair
you must connect the other.

**Side 1 is the side the unsuffixed parameters describe.** The sides are numbered rather than named
hot and cold, because which side is hot is a solved outcome and a script that says `hot_in=40` when
the solve makes it the cold side is worse than one that says nothing.

## Parameters

| Parameter | A bare number means | Meaning |
|---|---|---|
| `power` | kW | Duty transferred. Positive adds heat to side 1 |
| `in`, `out` | °C | Side-1 inlet and outlet temperature |
| `in2`, `out2` | °C | Side-2 inlet and outlet temperature |
| `dt`, `dt2` | dK | Temperature change across that side. Always positive; the sign follows `power` |
| `dp`, `dp2` | kPa | Pressure drop at design flow, per side |
| `flow`, `flow2` | kg/s | Flow constraint, per side |
| `ua` | W/K | Overall conductance — the thermal size, independent of how it is achieved |
| `area` | m² | Heat transfer area |
| `u` | W/(m²·K) | Overall heat transfer coefficient |
| `approach` | dK | **Minimum** temperature difference the design must respect |
| `arrangement` | — | `counter` (the default), `parallel`, or `crossflow` |
| `plates` | — | Total plate count. Effective plates are `plates − 2` |
| `lamella` | m | Gap between adjacent plates, usually written `lamella=2.4 mm` |
| `plate_area` | m² | Effective area of one plate |
| `fouling` | m²·K/W | Combined fouling resistance. Defaults to 1e-5, clean surfaces |

Everything except `arrangement` and `fouling` is sized when you omit it.

**`ua`, `area` and `u` are related by UA = U·A, so any two fix the third.** Stating all three is an
error, and so is stating `power`, `in`, `out` and `flow` together — any three of those fix the fourth.
Plate geometry is a fourth route to the same pair: it derives both `area` and `u`.

## Properties

`power`, `ua`, `area`, `u`, `ntu`, `effectiveness`, `lmtd`, `approach`, `plates`, `dp`, `dp2`, `dt`,
`dt2`, `flow`, `flow2`, `t_in`, `t_out`, `t_in2`, `t_out2`.

## Also written as

`exchanger`, `hx`, `heater`, `cooler`, `radiator`, `load`, `boiler`, `chiller`.

Those are one kind, not eight: a radiator and a boiler differ in the sign of their duty and in nothing
the model needs to know separately.

## Tag

`HE` — a heat exchanger in circuit 400 is tagged `400HE01`.

## See also

[`pump`](pump.md) · [`node`](node.md) · [Units](units.md)
