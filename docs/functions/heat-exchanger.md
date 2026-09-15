# heat_exchanger

Heat source, heat consumer, or a real two-sided exchanger — one physics model covers all three. Use
a role word when you want to write a positive capacity, or `heat_exchanger` when you want an
explicitly signed heat flow.

```fluidscript
RAD1 load power=30 in=50 out=30
BLR1 heater power=30 in=30 out=50
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
you must connect the other — one open is [`FS2112`](diagnostics.md), naming the open port.

**Side 1 is the side the unsuffixed parameters describe.** The sides are numbered rather than named
hot and cold, because which side is hot is a solved outcome and a script that says `hot_in=40` when
the solve makes it the cold side is worse than one that says nothing.

## Parameters

| Parameter | A bare number means | Meaning |
|---|---|---|
| `power` | kW | Capacity on a role spelling; signed side-1 heat flow on `heat_exchanger`, `exchanger`, or `hx` |
| `in`, `out` | °C | Side-1 inlet and outlet temperature |
| `in2`, `out2` | °C | Side-2 inlet and outlet temperature |
| `dt`, `dt2` | dK | Temperature change across that side. Always positive; the sign follows `power` |
| `dp`, `dp2` | kPa | Pressure drop at design flow, per side. Defaults to 20 kPa; write `dp=0` for an ideal block |
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
| `elevation` | m | Height above the project datum, both sides' ports at it; see [`node`](node.md#height). Never sized: wherever it is wired to, else 0 m |

Everything except `arrangement`, `fouling` and `u` is sized when you omit it. `u` is never invented:
no published coefficient is in the catalogue yet, so without a `u` you get a `ua` and a note saying
that no area follows from it. `lamella` is read but not yet used — deriving `u` from plate geometry
waits for the plate catalogue.

**`ua`, `area` and `u` are related by UA = U·A, so any two fix the third.** Stating all three is an
error, and so is stating `power`, `in`, `out` and `flow` together — any three of those fix the fourth.
Plate geometry is a fourth route to the same pair: it derives both `area` and `u`.

## The thermal size

In Rated and Coupled mode the four temperatures and the duty are a **design point**, not demands on
the solved circuit, and the thermal size is what follows from them. The district-heating substation
— 150 kW, 85/45 primary, 40/60 secondary — is the worked example:

```fluidscript
HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 u=3300
```

| Step | Number |
|---|---|
| Capacity rates, from the duty over each side's rise | C₁ = 150 / 20 = 7.5 kW/K, C₂ = 150 / 40 = 3.75 kW/K |
| The most any exchanger could move | Cmin · (85 − 40) = 168.75 kW — 150 kW is feasible |
| Effectiveness required | ε = 150 / 168.75 = 0.8889 at Cr = 0.5 |
| NTU, counterflow | ln((1 − ε·Cr) / (1 − ε)) / (1 − Cr) = 3.219 |
| **`ua`** | 3.219 · 3.75 = **12.07 kW/K** |
| **`area`**, from the stated `u` | 12 071 / 3300 = **3.66 m²** |
| `plates`, if you state `plate_area=0.1` | 3.66 / 0.1 rounded up, plus the two end plates: **39** |

The report shows each of these on the sized value's basis line, and, at the solution, the same
exchanger rated by the log-mean route — `UA 12.071 kW/K rated, 12.071 kW/K by LMTD 12.427 K`. The two
share no code, which is what makes their agreement a check on the physics rather than a restatement.

A plate count rounds **up**: more area means a closer approach and a little more duty, never a
shortfall. When the surplus passes 2 % the report says how much the built exchanger delivers against
what was asked.

**What pins the flow.** A design point says what each side runs at, so on a side whose circuit has
no other flow constraint — no `flow` on a node, no `dt` on a load — the exchanger's own
`power` with `in`/`out` (or `in2`/`out2`, or a `dt`) pins it. The substation's primary runs at
150 kW / (cp · 40 K) = 0.895 kg/s because `HX1` says 85/45, and its secondary at 1.79 kg/s because
`LOAD` says `dt=20`; where both would pin the same side, the load's wins and the exchanger's is
treated as design information only.

**What fixes the temperature.** In the extended modes the duty is `ε · Cmin · (T_in2 − T_in1)`, read
from the temperatures the circuit actually delivers. Warm the inlet and less crosses; cool it and
more does. That is what lets a closed loop find its own temperature level: a loop with a rated
exchanger and a `dt` load settles where the exchanger delivers exactly what the load takes, and for
the substation that is 60 °C out and 40 °C back.

### What is checked

| If you write | You get |
|---|---|
| All four of `power`, `in`, `out`, `flow` | [`FS2101`](diagnostics.md) |
| All three of `ua`, `area`, `u` | [`FS2101`](diagnostics.md) |
| `in`, `out`, `in2`, `out2`, `power` **and** a thermal size (`ua`, or `u` with `area`) | [`FS2109`](diagnostics.md) — the four temperatures and the duty already fix the size |
| `ua`, `area`, `u`, `approach`, `plates` or `plate_area` with no second side at all | [`FS2110`](diagnostics.md) — a warning; the parameter is inert in Duty mode |
| A duty above what the two inlet temperatures allow, `Cmin · (T_hot,in − T_cold,in)` | [`FS2111`](diagnostics.md), naming the maximum — checked before anything is sized |
| One of `in2`/`out2` connected without the other | [`FS2112`](diagnostics.md) |
| A design whose approach comes out under 3 K, or under your stated `approach` | [`FS4008`](diagnostics.md) — the size is still reported; the design is what is questioned |
| A negative `dt` or `dt2` | [`FS1307`](diagnostics.md) |
| A negative `power` on `load`, `cooler`, `radiator`, `chiller`, `heater` or `boiler` | [`FS1308`](diagnostics.md) — the word carries the sign; the magnitude is taken |
| Water running through it from `out` to `in` at a converged solve | [`FS3013`](diagnostics.md) — a warning naming any stated terminal now on the wrong end |

**The sign of `power` is the device's, never the water's.** Which way heat crosses the wall is the
role word's job (or the sign on the neutral spelling); which way the water runs is solved from the
circuit, and the connection order is only what you *intended*. If the solve runs a component
backwards it still does its duty on the node it actually discharges into, and `FS3013` tells you —
because `in=50` stays bound to the port you called `in`, exactly as a temperature sensor stays
mounted where the installer put it.

**`dt` is never negative.** It says how far the temperature moves, not which way. The component word
supplies the direction when you use a role: `RAD1 load power=70 dt=20` removes 70 kW and leaves the
outlet 20 K below the inlet; `BLR1 heater power=70 dt=20` adds 70 kW and raises it by 20 K. With the
neutral spelling, direction stays explicit: `RAD1 heat_exchanger power=-70 dt=20` means the same
consumer. `dt=-20` is rejected because it would encode direction twice.

## Pressure drop

Every exchanger resists flow, so `dp` carries a decided default of **20 kPa** — a plate exchanger at
its design flow. You will see it in the pressure the pump has to develop, and in the valve sizes on
the same branch, because both are chosen from what the branch actually drops.

A pressure drop is only half a law: `Δp = dp · (ṁ/ṁ_design)²` needs the flow the drop was measured at,
and you never write that down. It is the flow the circuit runs at, so it is worked out for you and
reported beside the drop:

```
HE1  flow  0.2392   sized   0.2392 kg/s — the flow HE1's 20 kPa is measured at, 0.24 l/s
```

### When to write `dp=0`

When the block is a modelling device rather than a piece of plant. A `LOAD` that exists only to make
the duties balance is not a physical exchanger and should not resist anything, and nothing in the
script can tell the two apart — so say so:

```fluidscript
LOAD load power=30 dp=0
```

Leaving it out is a common way to end up with twice the exchanger drop you meant and a pump sized to
match.

### The second side

`dp2` behaves the same way, but its design flow is **not** worked out for you: state `flow2` alongside
it if you want the secondary side to resist. Until you do, side 2 is ideal — it still carries the
relation that its two connections are at the same pressure, which is what makes a two-sided exchanger
solvable at all.

**The duty crosses.** When you connect a second side, whatever `power` puts into one stream comes out
of the other: 150 kW arriving on the secondary is 150 kW leaving the primary, and the primary's return
temperature drops accordingly. Each side reads its own flow direction, so a counter-current
arrangement — the usual one — puts the heat on the right port of each stream without your having to
say which way round they run.

The second side's **flow** follows from its design point the same way side 1's does: `power` with
`in2` and `out2` (or `dt2`) implies the flow that carries it, and where nothing else in that circuit
pins a flow, that is what the circuit runs at. State `flow2` instead when you know the flow and want
the temperatures solved.

**Rated is Coupled with side 2 written down.** Leave `in2`/`out2` unconnected and state them, and the
exchanger behaves exactly as it would against a real stream at those conditions — same size, same
duty relation, same answer for the side you did wire — with nothing to draw for the side you did not.

## Properties

`power`, `ua`, `area`, `u`, `ntu`, `effectiveness`, `lmtd`, `approach`, `plates`, `dp`, `dp2`, `dt`,
`dt2`, `flow`, `flow2`, `t_in`, `t_out`, `t_in2`, `t_out2`.

`lmtd` is reported, never solved: it is formed from the terminal temperatures the solve produced, and
the conductance it implies is printed beside the rated one so the two can be compared.

## Also written as

`exchanger`, `hx`, `heater`, `cooler`, `radiator`, `load`, `boiler`, `chiller`.

Those lower to one physics kind. `load`, `cooler`, `radiator`, and `chiller` treat `power` as a
positive cooling/heating-load capacity and pass a negative heat flow to side 1. `heater` and `boiler`
pass a positive heat flow. `heat_exchanger`, `exchanger`, and `hx` keep the number signed for models
where a neutral transfer block is the honest description.

## Tag

`HE` — a heat exchanger in circuit 400 is tagged `400HE01`.

## See also

[`pump`](pump.md) · [`node`](node.md) · [Units](units.md)
