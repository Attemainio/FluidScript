# catalog

Which catalogue sizing chooses from: a setting of the [`project`](project.md) block.

```fluidscript
fluidscript 2

project "Plant room":
  catalog = steel_en10255@2026.1
```

## Rules

- One catalogue per script. A pipe that belongs to another series says so itself:
  `P2 pipe dn = 15 material = copper_en1057` reads its `dn` in copper while the rest of the script
  stays on the project's catalogue ([`pipe`](pipe.md)).
- The `@` and the version pin it, so a script sizes to the same pipe next year as it did today. A
  catalogue that changes underneath a saved script would change its answers silently.
- With no `catalog` setting the shipped default applies, and [`FS2606`](diagnostics.md) says which
  and how to pin it. A name no catalogue has is [`FS2603`](diagnostics.md), listing the ones that ship.
- The catalogue holds dimensions — bores, roughnesses, standard sizes — with a public source for every
  row. It is not a copy of a standard.

## What ships

| Id | What | State |
|---|---|---|
| `steel_en10255` | Medium-series steel tube, DN15–DN150. The default | Verified |
| `steel_en10220` | Welded steel tube on EN 10220's Series 1 diameters, DN15–DN300, at the walls Finnish wholesalers stock for heating mains (EN 10217-1 P235TR1). Its DN150 is 168.3 mm where `steel_en10255`'s is 165.1 | Verified |
| `copper_en1057` | Copper tube, the Finnish type-approved range: 12–54 mm at 1.0–1.5 mm wall, 88.9 and 108 mm | Verified |

## `dn` does not mean the same thing in both

This is the one thing to know before switching catalogues.

```fluidscript
fluidscript 2

project "Plant room":
  catalog = steel_en10255

circuit "Loop":
  fluid = water

  N1 - N2   10 m  DN15
```

That pipe has a **16.1 mm** bore. The same `DN15` under `catalog = copper_en1057`, or with
`material = copper_en1057` on the pipe, is a **13.0 mm** bore — a 19 % difference in bore and
roughly double the pressure drop. Copper's one non-integer size, 88.9 mm, is `DN89`, as Finnish
listings print it.

Neither is a mistake. Steel is designated by *nominal size*, a label whose bore is larger than the
number; copper is designated by its *outside diameter*, whose bore is smaller. Nothing about `DN15`
says which, so the `catalog` setting is what settles it.

## Roughness

Every shipped roughness is for **new** pipe — 0.045 mm for commercial steel, 0.0015 mm for drawn
copper. Real pipe roughens: a scaled steel heating pipe is nearer 0.15–0.5 mm, which at DN25 is about
40 % more pressure drop and a correspondingly larger pump.

FluidScript does not guess at that, because it depends on the water and the years. If you are sizing
for a system that will age, say so:

```fluidscript
fluidscript 2

circuit "Loop":
  fluid = water

  N1 - N2   10 m  DN25  roughness = 0.3 mm
```

## See also

[`project`](project.md) · [`pipe`](pipe.md)
