# Units

FluidScript stores every number in SI internally, but you do not have to write SI. **A bare number
means the usual unit for whatever you are describing**, and you can always write the unit out if you
would rather be explicit.

```fluidscript
HE1 heat_exchanger power=30 in=20 out=50
```

That is 30 kilowatts between 20 °C and 50 °C. Writing `power=30 kW` means exactly the same thing.

## What a bare number means

Four units are chosen for readability rather than strict SI, marked **, because nobody specifies a
heat exchanger in watts or a design temperature in kelvin. Everything else takes the SI unit, so a
number you have not seen before still reads the way you would guess.

<!-- BEGIN GENERATED: unit-dimensions -->
| Quantity | Stored as | A bare number means | Shown as |
|---|---|---|---|
| Dimensionless | — | the value itself | — |
| Length | `m` | `m` | `m` |
| Temperature | `K` | `°C` ** | `°C` |
| Temperature delta | `K` | `dK` | `K` |
| Pressure | `Pa` | `kPa` ** | `kPa` |
| Pressure delta | `Pa` | `kPa` ** | `kPa` |
| Power | `W` | `kW` ** | `kW` |
| Energy | `J` | `J` | `kWh` |
| Mass flow | `kg/s` | `kg/s` | `kg/s` |
| Volume flow | `m3/s` | `m3/s` | `l/s` |
| Mass | `kg` | `kg` | `kg` |
| Time | `s` | `s` | `s` |
| Velocity | `m/s` | `m/s` | `m/s` |
| Density | `kg/m3` | `kg/m3` | `kg/m3` |
| Specific heat | `J/(kg*K)` | `J/(kg*K)` | `kJ/(kg*K)` |
| Enthalpy | `J/kg` | `J/kg` | `kJ/kg` |
| Area | `m2` | `m2` | `m2` |
| Volume | `m3` | `dm3` ** | `l` |
| Kv | `m3/h` | `m3/h` | — |
| Head | `m` | `m` | `m` |
| Nominal diameter | — | the value itself | `DN` |
| Pixels | `px` | `px` | `px` |
<!-- END GENERATED: unit-dimensions -->

## Writing the unit out

A unit may touch the number or be separated by a space: `30K` and `30 K` are the same. Case matters
where it matters — `mm` and `Mm` are a billion apart — but common names like `bar` and `psi` are
accepted in any case.

<!-- BEGIN GENERATED: unit-symbols -->
| Quantity | You can write |
|---|---|
| Dimensionless | `%` |
| Length | `m`, `mm`, `cm`, `dm`, `km`, `in`, `ft` |
| Temperature | `C`, `°C`, `K`, `F`, `°F` |
| Temperature delta | `dK`, `dC` |
| Pressure | `Pa`, `kPa`, `MPa`, `bar`, `mbar`, `psi`, `mH2O`, `mmH2O`, `kPag`, `barg`, `Paa`, `kPaa`, `MPaa`, `bara`, `mbara`, `psia` |
| Pressure delta | `Pa`, `kPa`, `MPa`, `bar`, `mbar`, `psi`, `mH2O`, `mmH2O` |
| Power | `W`, `kW`, `MW`, `hp` |
| Energy | `J`, `kJ`, `MJ`, `Wh`, `kWh`, `MWh` |
| Mass flow | `kg/s`, `kg/h`, `t/h` |
| Volume flow | `m3/s`, `m3/h`, `l/s`, `l/min`, `l/h` |
| Mass | `kg`, `g`, `t` |
| Time | `s`, `ms`, `min`, `h`, `d` |
| Velocity | `m/s`, `km/h` |
| Density | `kg/m3` |
| Specific heat | `J/(kg*K)`, `kJ/(kg*K)` |
| Enthalpy | `J/kg`, `kJ/kg` |
| Area | `m2`, `mm2`, `cm2` |
| Volume | `m3`, `dm3`, `l`, `ml` |
| Kv | *a bare number only* |
| Head | *a bare number only* |
| Nominal diameter | *a bare number only* |
| Pixels | `px` |
<!-- END GENERATED: unit-symbols -->

## Temperatures and temperature differences are not the same thing

A temperature is a reading: `20C`. A temperature *difference* is written `dK` or `dC`: `30 dK`.

This matters because adding two readings has no meaning. `20C + 30 dK` is 50 °C and is accepted;
`20C + 30C` is refused, because there is no sensible answer to it. Subtracting works the way you
would expect in one direction only:

| You write | You get |
|---|---|
| `70C - 20 dK` | 50 °C — a reading |
| `70C - 20C` | 50 dK — a difference |
| `20 dK - 70C` | refused |

Pressures behave the same way, because a gauge pressure is also a reading rather than an amount.

## Gauge and absolute pressure

Pressures are **gauge** unless you say otherwise — that is what a gauge on the pipe shows, and it is
what circuits are specified in. `p=300`, `p=300 kPa` and `p=3 bar` are the same pressure. Add an `a`
for absolute: `p=401.325 kPaa` is that same pressure, measured from vacuum instead of from the
weather.

## Pump head has no unit to write

`head=15` is 15 metres **of the fluid being pumped**, and there is deliberately no way to write a
unit after it. Metres of water column is a pressure, not a head, and the two are only equal when the
fluid is water — so a glycol circuit that let you write one for the other would be wrong by the
density ratio, and would look entirely reasonable on the diagram.

Valve `kv` and pipe `dn` are the same: both are bare numbers. A `dn` is a name rather than a
measurement, so DN25 pipe does not have a 25 mm bore.
