---
id: 57-state-visualization
title: State visualization and colour scales
tier: 50-frontend
status: draft
owns: [the show directive, property selection, colour scales, gradient rendering, the scale legend, domain computation]
depends_on: [12-grammar, 21-fluid-and-state, 26-model-contract, 53-canvas-renderer, 55-design-system]
traces_to: [R-23, R-26, R-27, R-08, R-34, R-45]
open_questions: 0
last_review_pass: 0
---

# State visualization and colour scales

## Purpose

Rendering the solved state *as* the diagram rather than as numbers next to it: every node and every
pipe cell coloured by a chosen property, with a legend that says what the colours mean. This is the
feature that turns the canvas from a schematic into an instrument — a temperature gradient down a loop
communicates in one glance what a table of forty numbers does not.

The `show` directive selects the property. Both halves — the language and the rendering — are owned
here, because a colour scale that the script cannot select is decoration, and a directive with no
scale behind it is dead syntax.

## Responsibilities

**Owns.** The `show` directive and its property aliases, colour-scale selection, domain computation,
gradient rendering per node and per segment, and the legend.

**Explicitly does not own.** Grammar mechanics ([`12-grammar`](../10-language/12-grammar.md) hosts the
production; this document specifies it), the base palette
([`55-design-system`](55-design-system.md)), symbol drawing
([`53-canvas-renderer`](53-canvas-renderer.md)), what properties exist
([`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md),
[`22-component-model`](../20-core-domain/22-component-model.md)).

## The `show` directive

```fluidscript
circuit coolingLoop
fluid dynamic water
show temperature                 # colour everything by temperature
```

```ebnf
show-directive = "show" , property-name , { property-name } ;
property-name  = identifier ;
```

**One directive, one or more properties.** The first is the active scale; the rest are alternatives the
UI offers as a quick switch without recompiling. Writing several is how a user says "these are the
views I care about for this model".

`show` with no properties, or no `show` at all, defaults to `temperature` — the property a designer
looks at first, and the one that makes a hydronic diagram immediately legible.

### Property names and aliases

Both a long name and a short one, because the language trades on density and `show t` is what someone
will type.

| Long | Short | Dimension | Notes |
|---|---|---|---|
| `temperature` | `t` | Temperature | Default |
| `pressure` | `p` | Pressure | Gauge for display; contract/Core retain absolute pressure (`D-26`) |
| `enthalpy` | `h` | Enthalpy | Per kg fluid; per kg **dry air** for humid air |
| `density` | `rho` | Density | |
| `viscosity` | `mu` | — | **Dynamic** viscosity by default; `kinematic_viscosity` / `nu` for the other |
| `specific_heat` | `cp` | SpecificHeat | |
| `conductivity` | `k` | — | Thermal conductivity |
| `flow` | `mdot` | MassFlow | On connections; a node shows its net |
| `volume_flow` | `q` | VolumeFlow | |
| `velocity` | `v` | Velocity | Connections only — a node has no velocity |
| `reynolds` | `re` | Dimensionless | Connections only |
| `pressure_drop` | `dp` | PressureDelta | Connections and components only |

Humid air adds, when the substance is `air`:

| Long | Short | Notes |
|---|---|---|
| `humidity_ratio` | `w` | kg water / kg dry air |
| `relative_humidity` | `rh` | 0…1, displayed as % |
| `wet_bulb` | `twb` | |
| `dew_point` | `tdp` | Condensation risk reads directly off this |

**`viscosity` defaulting to dynamic is stated in the table and in `/docs`, and reported in the legend**
(`μ · dynamic viscosity · mPa·s`). The two differ by density — a factor of ~1000 for water — so a user
who meant kinematic and got dynamic sees numbers that are wrong by three orders of magnitude, which is
at least loud. The legend naming which one it is closes the gap.

**A property that does not apply to an element leaves it un-coloured**, drawn in the neutral symbol
colour. `show velocity` colours the pipes and greys the nodes, which is correct and self-explaining
rather than an error.

## Colour scales

Two families, chosen by whether the property has a meaningful midpoint.

### Sequential — most properties

Low to high across a perceptually uniform ramp. Used for pressure, density, viscosity, flow, velocity,
Reynolds number, humidity.

The ramp is the HVAC palette's cool→warm axis ([`55-design-system`](55-design-system.md)) rather than a
generic viridis: a designer reads blue as cold and red as hot, and adopting a scientific ramp for
temperature would fight that. For non-thermal properties the same ramp reads as low→high without
implying temperature, because the legend labels it.

| Stop | Light | Dark |
|---|---|---|
| 0.00 | `#1B6CA8` | `#4FA3D9` |
| 0.25 | `#3A8FB7` | `#6FBBD9` |
| 0.50 | `#5C7A89` | `#8FA9B5` |
| 0.75 | `#C97B3C` | `#E09E5F` |
| 1.00 | `#B23A2E` | `#E06C5A` |

Interpolated in **Oklab**, not sRGB. sRGB interpolation between blue and orange passes through a muddy
grey-brown and produces visible banding; Oklab keeps the ramp perceptually even, which is what makes a
small temperature difference visible at all.

### Diverging — properties with a meaningful zero

Used for `pressure_drop` (a pump's is negative, per the sign convention) and for any property displayed
as a deviation from a reference. Neutral at zero, diverging both ways, with the domain forced symmetric
so that zero sits at the midpoint — an asymmetric diverging scale puts the neutral colour at a nonzero
value and misleads systematically.

### Domain

The range mapped to the scale's ends.

| Mode | Domain | When |
|---|---|---|
| **Auto** (default) | Min and max of the property across every element in the circuit | Static solve |
| **Run-wide** | Min and max across every frame of a transient | Transient — see below |
| **Fixed** | User-specified | `show temperature 0..80` (`D-30`) |
| **Nice** | Auto, rounded outward to sensible ticks | Always applied on top, for the legend |

**A transient must use a run-wide domain, not a per-frame one.** A domain recomputed each frame makes
the colours mean something different in every frame: a loop warming from 20 °C to 68 °C would look
identical at both ends because each frame re-normalises to itself. Since the run streams and the final
range is unknown at t = 0, the domain expands as frames arrive and the legend updates with it — visibly,
so the user sees why the colours shifted.

**A degenerate domain** — every element at the same value, common before the first solve — collapses the
scale to its midpoint and the legend says `all 20.0 °C`. Dividing by a zero range is the obvious bug.

`null` model-state values are unsolved, not zero. Domain computation excludes them. If at least one
finite value remains, null elements render neutral under invariant 5 and do not affect min/max. If no
finite value remains, the scale is `unavailable` (not degenerate), no numeric ramp or ticks render,
and the legend says `No solved {property} values`; every element is neutral.

`show` is durable script presentation. Choosing another property in the UI creates a session-only
override and does not write back; Reset view returns to the script's first property. `show temperature
0..80` uses the grammar's existing range and fixes the domain. For band queries and legend counts, a
multi-state component contributes its downstream/outlet value; gradients still use every endpoint and
internal state (`D-30`). A tank has several outlets and is the explicit exception: its representative
value is the volume-weighted mean across layers, while the vessel always colours each layer separately
(`D-32`).

## Rendering

### Nodes

Filled with the scale colour for their value. The symbol's stroke stays
`--canvas-symbol` so shape remains readable against any fill.

### Connections and pipe cells

**A connection is a gradient between its endpoint values**, not a flat colour. This is where the
feature earns its place: a pipe run from a 50 °C heat exchanger to a 6 °C node draws as a gradient,
and the eye follows the energy through the circuit.

A discretized pipe (`nodes=n`, [`22`](../20-core-domain/22-component-model.md)) has real intermediate
states, so it draws as a **multi-stop gradient through its internal nodes** — a genuinely accurate
picture of the profile along its length, and the visual payoff for discretizing. During a transient,
watching a front travel down that gradient is what `R-14` looks like on screen.

An undiscretized connection interpolates linearly between its two ends, which is an approximation and
is worth saying in `/docs`: the true profile is not linear, and the picture is a two-point
interpolation, not a computed field.

### Components

A component with an internal profile (a heat exchanger, inlet to outlet) draws as a gradient across its
symbol. Everything else takes a flat fill from its representative state.

A tank draws one discrete horizontal fill band per layer, bottom to top. It does not interpolate away
the interface: the layer boundary is part of the stated finite-volume resolution. A port connection
begins its gradient at the attached layer's value, not at the tank-wide representative mean.

### Stale values

During a drag preview or between compiles, coloured elements desaturate toward `--status-stale`
([`55`](55-design-system.md)) rather than freezing at old colours. A stale gradient that looks live is
worse than one that visibly says "recomputing".

## The legend

Always visible when a scale is active, bottom-right of the canvas, unobtrusive.

```
┌─────────────────────────────────┐
│  Temperature · °C               │
│  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  │
│  10      25      40      55  70 │
│                                 │
│  ⌄ temperature · pressure · flow│
└─────────────────────────────────┘
```

| Element | Rule |
|---|---|
| Title | Property's display name **and unit** — never a bare gradient |
| Ramp | The active scale, ~180 px |
| Ticks | 4–6 "nice" values from the rounded domain |
| Switcher | The other properties from `show`, one click, no recompile |
| Hover | Hovering the ramp highlights every element within that band on the canvas |

**A gradient with no legend is a decoration.** The legend is what makes it data, which is why it is an
invariant rather than an option.

The band-highlight on hover is a small feature with a large payoff: "show me everything above 60 °C" is
a question a designer asks constantly, and it needs no UI beyond a cursor.

## Contracts

The model contract ([`26-model-contract`](../20-core-domain/26-model-contract.md)) gains one block:

```jsonc
"visualization": {
  "active": "temperature",
  "available": ["temperature", "pressure", "flow"],
  "scale": {
    "property": "temperature",
    "displayName": "Temperature",
    "unit": "C",
    "kind": "sequential",              // "sequential" | "diverging"
    "domain": { "min": 10.0, "max": 70.0, "nice": true }, // null when unavailable
    "status": "available",              // "available" | "degenerate" | "unavailable"
    "degenerate": false
  }
}
```

Core computes the domain because it holds every value; the frontend owns the colours. That split is
`D-03` applied exactly: the domain is a fact about the model, the ramp is a presentation choice.

**Per-element values are already in the payload** (`components[].state`, `connections[].state`), so the
gradient needs no extra data — the frontend reads the active property's value per element and maps it.
Adding a redundant per-element colour to the payload would move a presentation decision to the server
and inflate every transient frame.

## Invariants

1. A scale is never rendered without a legend naming the property and its unit.
2. The domain is computed over every element carrying the property, and over the whole run for a
   transient.
3. A degenerate domain renders the midpoint colour and says so; it never divides by zero.
4. Interpolation is in Oklab.
5. An element lacking the active property renders in the neutral symbol colour, never at the scale's
   low end. (Rendering "absent" as "minimum" is a lie the eye believes.)
6. Switching the active property requires no recompile and no network request.
7. Stale values desaturate; they never display a value that is no longer current.
8. `viscosity` means dynamic viscosity, everywhere, and the legend says which.
9. Null/unsolved values never enter a domain calculation; an all-null property is unavailable, never
   a zero-valued degenerate domain.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1210` | `show` names an unknown property | Warning | `Nothing to show called '{name}'. Available: {list}.` |
| `FS1211` | `show` names a property no element has | Warning | `No component has '{name}'; showing '{fallback}'.` |
| `FS1212` | `show` names a psychrometric property for a non-air fluid | Warning | `'{name}' applies to humid air; this circuit is {fluid}.` |
| `FS1213` | Duplicate property in one `show` | Info | `'{name}' listed twice.` |
| `FS1214` | More than one `show` directive | Warning | `Only the first 'show' is used.` |

All warnings, none errors: a bad `show` must never stop a circuit rendering.

## Worked example

The **cooling loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)) with
`show temperature pressure`:

**Domain.** Node temperatures: `N1` 6.0, `N2` 20.0, `PU1__HE1` 20.0, `HE1__3WV` 50.0, `3WV__P1` 50.0,
`N3` 50.0. Raw domain 6.0…50.0; niced outward to **5…50**, ticks at 5, 15, 25, 35, 45, 50.

**Mapping** (dark theme):

| Element | Value | Normalised | Colour |
|---|---|---|---|
| `N1` | 6.0 °C | 0.02 | `#4FA3D9` — the cold end, the primary supply |
| `N2` | 20.0 °C | 0.33 | `#6FBBD9` — after mixing |
| `PU1__HE1` | 20.0 °C | 0.33 | `#6FBBD9` |
| `HE1__3WV` | 50.0 °C | 1.00 | `#E06C5A` — the hot end |
| `3WV__P1`, `N3` | 50.0 °C | 1.00 | `#E06C5A` |

`HE1`'s symbol draws as a gradient from `#6FBBD9` to `#E06C5A` across its body — the duty made visible.
The recirculation branch `3WV.b → N2` carries hot water back into the mixing node, and the jump from 50 °C to 20 °C
across `N2` is where the 6 °C primary enters: the mixing is legible as a colour discontinuity at a
single node, which is exactly what it physically is.

**That discontinuity is the feature earning its place.** A table of six numbers does not show that one
node is where two streams meet; a diagram in which the colour changes abruptly at exactly one point
does, without a legend entry or a label.

**With `nodes=4` on `PB` in the `D-16` demand-step loop**, the recirculation path becomes a four-cell gradient
through its internal-node temperatures. The 30→45 kW step produces an orange front moving through
those cells; each has a 9.6 s residence time at the reference flow
([`33-transient-time-domain`](../30-solver/33-transient-time-domain.md)'s worked example) — the
transport delay, watchable.

**Switching to `pressure`** with one click: the domain spans the stated boundaries and the pump's rise,
280 kPa at `N3` up to the pump discharge, with `N1` at 300. Same model, same frame, no recompile.

## Acceptance criteria

- [ ] `show t` and `show temperature` are equivalent.
- [ ] `show viscosity` resolves to dynamic viscosity and the legend says so.
- [ ] Default with no `show` is temperature.
- [ ] The worked example's domain nices to 5…50 with the tabulated ticks, and includes `N1`'s 6 °C —
      a domain that clips the coldest node is the bug this criterion exists to catch.
- [ ] A discretized pipe renders a multi-stop gradient through its internal node values.
- [ ] A transient uses a run-wide domain; a frame-local domain fails the test that colours are
      comparable between frames.
- [ ] Every element at one value renders the midpoint colour with a `all X` legend.
- [ ] A mixed finite/null model computes its domain from finite values and renders null elements
      neutral; an all-null model renders the unavailable legend with no ramp or ticks.
- [ ] An element lacking the property renders neutral, not at the scale minimum.
- [ ] Switching properties makes no network request.
- [ ] Interpolation is Oklab, asserted against reference midpoint values.
- [ ] `show nonsense` produces `FS1210` and still renders with the default.
- [ ] Hovering a legend band highlights exactly the elements in that range.
- [ ] A UI property override performs no write-back and Reset view restores the script-owned `show`.
- [ ] `show temperature 0..80` fixes both static and transient domains without expansion.
- [ ] A heat exchanger is counted in a legend band by its outlet value while its symbol/route retains
      the inlet-to-outlet gradient.
- [ ] A tank renders one fill band per layer and each connection gradient starts at its mapped layer;
      legend-band queries count the tank once at its volume-weighted mean.

## Open questions

None. The script owns `show`; UI switching is a non-persistent override. The existing `a..b` grammar
fixes a domain. Outlet/downstream state represents a multi-state component in band queries except for
a tank, whose representative is its volume-weighted layer mean (`D-32`). Comparing
multiple designs is post-v1 and uses explicit identical fixed domains rather than implicit shared UI
state (`D-30`).
