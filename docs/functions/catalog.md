# catalog

Which catalogue sizing chooses from.

```fluidscript
fluidscript 1
catalog steel_en10255@2026.1
```

## Rules

- One catalogue per script, named before the components that draw on it.
- The `@` and the version pin it, so a script sizes to the same pipe next year as it did today. A
  catalogue that changes underneath a saved script would change its answers silently.
- With no `catalog` line the shipped default applies, and the log says which.
- The catalogue holds dimensions — bores, roughnesses, standard sizes — with a public source for every
  row. It is not a copy of a standard.

## What ships

| Id | What | State |
|---|---|---|
| `steel_en10255` | Medium-series steel tube, DN15–DN150. The default | Verified |
| `copper_en1057` | Copper tube, table X, 15–108 mm | **Not yet verified — it will refuse to size** |

## `dn` does not mean the same thing in both

This is the one thing to know before switching catalogues.

```fluidscript
fluidscript 1
catalog steel_en10255
P1 pipe dn=15 length=10
connections
N1 - P1 - N2
```

That pipe has a **16.1 mm** bore. The same `dn=15` under `catalog copper_en1057` is a **13.6 mm** bore
— a 24 % difference, and roughly double the pressure drop.

Neither is a mistake. Steel is designated by *nominal size*, a label whose bore is larger than the
number; copper is designated by its *outside diameter*, whose bore is smaller. Nothing about `dn=15`
says which, so the `catalog` line is what settles it.

## Roughness

Every shipped roughness is for **new** pipe — 0.045 mm for commercial steel, 0.0015 mm for drawn
copper. Real pipe roughens: a scaled steel heating pipe is nearer 0.15–0.5 mm, which at DN25 is about
40 % more pressure drop and a correspondingly larger pump.

FluidScript does not guess at that, because it depends on the water and the years. If you are sizing
for a system that will age, say so:

```fluidscript
P1 pipe dn=25 length=10 roughness=0.3 mm
```

## See also

[`pipe`](pipe.md) · [Catalogues](catalogs.md)
