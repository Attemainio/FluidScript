# Units

FluidScript stores every number in SI internally, but you do not have to write SI. **A bare number
means the usual unit for whatever you are describing**, and you can always write the unit out if you
would rather be explicit.

```fluidscript
fluidscript 2

circuit "Plant":
  fluid = water

  HE1  heat_exchanger  power = 30  in.t = 20  out.t = 50
```

That is 30 kilowatts between 20 °C and 50 °C. Writing `power = 30 kW` means exactly the same thing.

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
| Temperature delta | `K` | `K` | `K` |
| Pressure | `Pa` | `kPa` ** | `kPa` |
| Pressure delta | `Pa` | `kPa` ** | `kPa` |
| Power | `W` | `kW` ** | `kW` |
| Energy | `J` | `J` | `kWh` |
| Mass flow | `kg/s` | `kg/s` | `kg/s` |
| Volume flow | `m3/s` | `l/s` ** | `l/s` |
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
| Heat transfer coefficient | `W/(m2*K)` | `W/(m2*K)` | `W/(m2*K)` |
| Thermal resistance | `m2*K/W` | `m2*K/W` | `m2*K/W` |
| Acceleration | `m/s2` | `m/s2` | `m/s2` |
<!-- END GENERATED: unit-dimensions -->

## Writing the unit out

A unit may touch the number or be separated by a space: `30K` and `30 K` are the same. Case matters
where it matters — `mm` and `Mm` are a billion apart — but common names like `bar` and `psi` are
accepted in any case.

<!-- BEGIN GENERATED: unit-symbols -->
| Quantity | You can write |
|---|---|
| Dimensionless | `%` |
| Length | `m`, `mm`, `cm`, `dm`, `km`, `ft` |
| Temperature | `C`, `°C`, `F`, `°F` |
| Temperature delta | `K`, `dK`, `dC` |
| Pressure | `Pa`, `kPa`, `MPa`, `bar`, `mbar`, `psi`, `mH2O`, `mmH2O`, `kPag`, `barg`, `Paa`, `kPaa`, `MPaa`, `bara`, `mbara`, `psia` |
| Pressure delta | `Pa`, `kPa`, `MPa`, `bar`, `mbar`, `psi`, `mH2O`, `mmH2O`, `dPa`, `dkPa`, `dbar` |
| Power | `W`, `kW`, `MW`, `hp` |
| Energy | `J`, `kJ`, `MJ`, `Wh`, `kWh`, `MWh` |
| Mass flow | `kg/s`, `kg/h`, `t/h` |
| Volume flow | `m3/s`, `m3/h`, `l/s`, `l/min`, `l/h` |
| Mass | `kg`, `g` |
| Time | `s`, `ms`, `min`, `h`, `d` |
| Velocity | `m/s`, `km/h` |
| Density | `kg/m3` |
| Specific heat | `J/(kg*K)`, `kJ/(kg*K)` |
| Enthalpy | `J/kg`, `kJ/kg` |
| Area | `m2`, `mm2`, `cm2` |
| Volume | `m3`, `dm3`, `l`, `ml` |
| Kv | *a bare number only* |
| Head | a length: `m`, `mm`, `cm`, `dm`, `km`, `ft` |
| Nominal diameter | *a bare number only* |
| Pixels | `px` |
| Heat transfer coefficient | `W/(m2*K)`, `kW/(m2*K)` |
| Thermal resistance | `m2*K/W` |
| Acceleration | `m/s2` |
<!-- END GENERATED: unit-symbols -->

## Temperatures and temperature differences are not the same thing

A temperature is a reading: `20 C`. A temperature *difference* is written in `K`: `30 K`, as engineers
write a rise or a band. `dK` and `dC` are differences too.

This matters because adding two readings has no meaning. `20 C + 30 K` is 50 °C and is accepted;
`20 C + 30 C` is refused, because there is no sensible answer to it. Subtracting works the way you
would expect in one direction only:

| You write | You get |
|---|---|
| `70 C - 20 K` | 50 °C — a reading |
| `70 C - 20 C` | 50 K — a difference |
| `20 K - 70 C` | refused |

An absolute temperature in kelvin cannot be written: `300 K` on a temperature is a difference where a
reading belongs, and the fix is to write it in °C. A compound unit that contains a `K`, such as
`kJ/(kg*K)`, is its own unit and is not affected.

A pressure difference has the same spellings: `dPa`, `dkPa`, `dbar`. `300 kPa - 10 dkPa` is 290 kPa,
a reading; `300 kPa - 290 kPa` is 10 kPa, a difference, which a `dp` accepts and a node's `p` does not.
Pressures behave this way because a gauge pressure is also a reading rather than an amount.

## Gauge and absolute pressure

Pressures are **gauge** unless you say otherwise — that is what a gauge on the pipe shows, and it is
what circuits are specified in. `p = 300`, `p = 300 kPa` and `p = 3 bar` are the same pressure. Add an
`a` for absolute: `p = 401.325 kPaa` is that same pressure, measured from vacuum instead of from the
weather.

## Pump head is a height of the pumped fluid

`head = 15` is 15 metres **of the fluid being pumped**, and so is `head = 15 m` or
`head = 15000 mm`: a head takes a length, and reads it as that height of whatever the pump moves. A
pressure is refused — `head = 150 kPa` is an error. Metres of water column is a pressure, not a head,
and the two are only equal when the fluid is water, so a glycol circuit that let you write one for
the other would be wrong by the density ratio and would look entirely reasonable on the diagram.

Any length is accepted, so `head = P1.length` is a head of the pipe's length. That is legal and is
almost never what you meant; the script is taken at its word.

Valve `kv` and pipe `dn` take bare numbers only. A `dn` is a name rather than a
measurement, so DN25 pipe does not have a 25 mm bore.
