# fluid

What a circuit carries, and how it is solved.

```fluidscript
fluidscript 1
circuit coolingLoop
fluid water
```

```fluidscript
fluidscript 1
circuit storage
fluid dynamic water
```

## Rules

- `fluid <substance>` names the working fluid for the circuit it appears in.
- `fluid dynamic` solves in time; `fluid static` solves as a steady state. Either overrides the
  default [`project`](project.md) set.
- Arguments after the substance describe it — a glycol fraction, for instance — and are read in the
  order the substance defines.
- Air-side substances are deliberately not accepted in this version. A fan-and-duct model needs
  humidity balance, condensation, leakage and fan curves; naming an air fluid without them would
  produce a hydronic answer wearing air-side names.

## Where the properties are trusted

`water` is validated from 0 to 120 °C at 1 to 10 bar absolute, as liquid water. Inside that box its
density is within 0.1 % of an independent published correlation, and its viscosity and thermal
conductivity within 0.5 %.

Outside it you get [`FS2003`](diagnostics.md) naming the bound you crossed, rather than a number
nobody has checked. Two states *on* the boundary are refused differently, with
[`FS2002`](diagnostics.md):

- **the boiling line** — at 100 kPa absolute that is 99.61 °C, well inside the box
- **0 °C**, the melting line, which is the lower endpoint of the range above

At either one, a pressure and a temperature are the *same* constraint rather than two, so they do not
say whether you meant liquid or the phase on the other side. Move the state off the line — 0.01 °C
instead of 0 — or fix it from a pressure and an enthalpy, which stays unambiguous there.

`air` is validated from 0 to 50 °C at 80 to 110 kPa absolute and 10 to 90 % relative humidity, within
0.5 %. **It cannot be solved** — it is there for psychrometric properties and for metadata, and a
derived property may leave the box even when the state does not: air at 5 °C and 30 % RH has a dew
point of −9.9 °C.

**Its enthalpy is per kg of dry air**, unlike every other substance here. At a typical room state the
other basis is 0.3 % away, which is small enough to look like rounding and large enough to be wrong.

## See also

[`project`](project.md) · [`circuit`](circuit.md) · [`schedule`](schedule.md)
