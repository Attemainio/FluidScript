---
id: 73-equipment-list
title: The equipment list
tier: 70-future
status: draft
owns: [equipment list rows and columns, column provenance, declared equipment fields, equipment-list CSV, its promotion gate]
depends_on: [01-vision-and-scope, 22-component-model, 24-auto-sizing, 26-model-contract, 51-frontend-architecture, 54-interaction-and-writeback, 71-export-formats]
traces_to: [R-28, R-43, R-47, R-52]
open_questions: 3
last_review_pass: 0
---

# The equipment list

## Purpose

Nobody procures from a P&I diagram. They procure from a table: every device in the plant, one row
each, with the numbers it was designed for. The **equipment list** is that table, grouped by circuit,
derived from the design-point solve and exported for a contractor.

This document specifies it and gates it, so that "add an equipment list" does not later become an
invented spreadsheet whose numbers nobody can trace back to a solve. The failure mode is specific and
expensive: a diagram that is 12 % out is a drawing somebody argues about, a **list** that is 12 % out
is a pump somebody bought.

## Responsibilities

**Owns.** The row and column contract, the provenance rule for every column, the declared equipment
fields and their write-back, the grouping and ordering rule, the CSV form, and the promotion gate for
all of it.

**Explicitly does not own.**

| Concern | Owner |
|---|---|
| The tag a row is keyed by | `D-34`, [`22-component-model`](../20-core-domain/22-component-model.md) |
| What "design" means | `D-58`, and [`24-auto-sizing`](../20-core-domain/24-auto-sizing.md) for what sizing chose |
| The per-component `parameters`/`state` blocks the rows project | [`26-model-contract`](../20-core-domain/26-model-contract.md) |
| Side-1/side-2 semantics on a heat exchanger | [`22-component-model`](../20-core-domain/22-component-model.md) |
| Writing a panel edit back into the script | [`54-interaction-and-writeback`](../50-frontend/54-interaction-and-writeback.md) |
| The panel's visual design | [`55-design-system`](../50-frontend/55-design-system.md) |
| Formats other than CSV | [`71-export-formats`](71-export-formats.md) |
| Whether this is promoted at all | [`72-roadmap`](72-roadmap.md) |

## The promotion gate

[`72-roadmap`](72-roadmap.md)'s four criteria, applied today:

| Criterion | Verdict |
|---|---|
| 1 · A user has asked for it | **✓** Requested with a full column layout and a worked row |
| 2 · Its foundation is stable | **✗** There is no solver yet, so there is no design point to tabulate. Separately, the air-side columns need the air physics `D-28` defers to M7 |
| 3 · It does not reopen a `D-` decision | **~** The list itself reopens nothing. The *electrical* column group does: it needs a motor efficiency the pump model does not have (below), and that is a decision-log entry before it is a parameter |
| 4 · Its `/docs` cost is affordable | **✓** One Advanced Workflows page, plus parameter rows on existing kind pages for the declared fields |

Criterion 2 is what defers it, and it defers cleanly — nothing here asks for a decision to be taken
early. **Promote when** a design-point solve exists and reproduces the water-side columns for the
substation reference circuit unaided, *and* the electrical question below is answered.

**The air columns promote separately**, with `D-28`'s air-side work. A water-only list is a complete
artefact that a contractor can buy from; an air-only one is not, and a list carrying an air column
computed from physics the tool does not claim would be exactly the "alias disguising a missing
equation" `D-28` refuses.

## What the list is made of

**Almost nothing new.** [`26-model-contract`](../20-core-domain/26-model-contract.md) already carries,
per component, the `tag` (`D-34`), the owning `circuit` (`D-36`), a `parameters` block whose every
value has a `source` of `stated`/`sized`/`default` with a `basis`, and a solved `state` block. A row is
a **projection** of those.

Three things are genuinely new:

1. **The column mapping** — which parameter or state name fills which column, per kind. That is domain
   knowledge, not formatting, so it is Core's under `D-03`.
2. **The two-stream split** for a coupled heat exchanger, which needs flow groups (`D-63`) the
   frontend does not model.
3. **The declared fields** — the columns no solve can produce.

So Core emits an `equipmentList` block alongside `components`, deliberately denormalized, and the
frontend renders and formats it. That is the same division the rest of the frontend already works
under: the frontend computes geometry and formats numbers, nothing else.

**Why a projection rather than letting the frontend assemble one.** The panel is the *second* consumer,
not the first. CSV export is a third, and [`72-roadmap`](72-roadmap.md)'s report generation — "the
diagram, the equipment list, and the warnings" — is a fourth. A mapping living in the panel is a
mapping every other consumer re-derives, differently.

## Contracts

One row, in canonical script units per `D-14`, every dimensioned field carrying its `unit`:

```json
{
  "tag": "101HC01",                      // D-34; the row's key. null when the kind has no tag code
  "name": "LP35",                        // the identifier the user wrote — D-34: the tag is not identity
  "kind": "heat_exchanger",              // script keyword
  "equipment": "Heating coil",           // the kind's display name, localizable

  "primary": {                           // side 1 — see "Primary is a display name" below
    "fluid": "air",
    "volumeFlow":   { "value": 755,   "unit": "l/s", "at": "inlet", "density": 1.341 },
    "massFlow":     { "value": 1.013, "unit": "kg/s" },
    "tIn":          { "value": -10.0, "unit": "C" },
    "tOut":         { "value": 16.5,  "unit": "C" },
    "rhIn":         { "value": 0.90,  "unit": null },   // air only; absent for a liquid
    "deltaH":       { "value": 26.7,  "unit": "kJ/kg", "basis": "per kg dry air" },
    "pressureDrop": { "value": null,  "unit": "Pa" }
  },
  "secondary": { },                      // side 2, same shape; absent for a single-stream component

  "power": { "value": 27.0, "unit": "kW" },   // duty transferred, positive when side 1 gains heat

  "electrical": {
    "shaft":   { "value": null,  "unit": "kW" },     // derived, where the kind has one
    "planned": { "value": 13.0,  "unit": "kW" },     // declared
    "actual":  { "value": null,  "unit": "kW" },     // declared
    "supply":  "230 V"                                // declared, free text
  },

  "model": "…",                          // declared: the selected product
  "note":  "…",                          // declared: free text

  "provenance": {                        // one entry per column, mirroring 26's `source`
    "power":               { "source": "solved" },
    "primary.volumeFlow":  { "source": "stated" },
    "secondary.massFlow":  { "source": "sized",   "basis": "0.65 l/s to carry 27 kW at Δt=10 K" },
    "electrical.planned":  { "source": "declared" }
  }
}
```

Rows are grouped:

```json
"equipmentList": [
  { "circuit": { "number": 101, "name": "Radiator network" }, "rows": [ /* … */ ] }
]
```

### Where every column comes from

`26`'s three `source` values become four here, because a list has one class its contract does not:

| Class | Meaning | Rendered as |
|---|---|---|
| **solved** | The design-point solve produced it | Plain |
| **stated** | The engineer wrote it in the script; the solve honoured it | Plain, distinguishable from solved |
| **sized** | Sizing chose it, and `basis` says why (`R-43`) | Visually distinct, with the basis available |
| **declared** | Not a model quantity at all — the engineer typed it into this list | Visually distinct, and editable |

A contractor must be able to tell a number the tool chose from a number an engineer chose. The canvas
already makes that distinction under `R-02`; the list carries the same one, for the same reason.

| Column group | Class | Notes |
|---|---|---|
| Tag, Name, Equipment | derived | `D-34`, and the kind registry |
| Primary/secondary fluid | derived | The fluid the circuit declares |
| Volume flow, mass flow, T_in, T_out, Δh, pressure drop | solved / stated / sized | Empty before a successful solve |
| RH_in | solved | Humid air only; absent for a liquid |
| Design power | solved / stated | |
| Shaft power | solved | Only where the kind has one. **Not electrical power** — see below |
| Planned and actual electrical power, supply | declared | |
| Model, note | declared | |

**An underivable column is empty, never guessed.** Absence, never null (`D-02`).

## Primary is a display name, and side 1 is the model

[`22-component-model`](../20-core-domain/22-component-model.md) numbers an exchanger's sides rather
than naming them, deliberately: which side is hot is a solved outcome, not a declaration, so `hot`/
`cold` and `primary`/`secondary` are all refused as model vocabulary.

The list still has to print a word a contractor reads. The mapping is fixed and needs no new decision:

> **Primary is side 1**, which is the side whose properties are unsuffixed (`in`, `out`, `flow`, `dp`)
> and which belongs to the circuit the component's tag was assigned to under `D-36`. **Secondary is
> side 2** (`in2`, `out2`, `flow2`, `dp2`).

This is a display mapping in the equipment list and nowhere else. Renaming `flow2` to `secondaryFlow`
anywhere in Core or on the wire is the mistake this paragraph exists to prevent.

Similarly, the columns say **volume flow** and **mass flow**, never "flow" — the glossary bans the
unqualified word because the two differ by a density that changes with temperature, which is precisely
the error the worked example below is about.

## A coupled component appears once

A two-sided exchanger belongs to two circuits. It gets **one row**, grouped under the circuit its tag
was assigned to, with the other circuit's stream in the secondary columns.

This is not a new judgement. `D-34` already rejected a compound tag on the ground that it "doubles
every equipment list row", and `FS2216` already warns that the circuit assignment was arbitrary — so
the warning a reader needs when the grouping surprises them is already emitted, at the right moment,
by machinery that exists.

## Grouping and order

- **Group** = circuit number, ascending. The group heading is `<number> <circuit name>` — `101 Radiator
  network`.
- **Row order within a group** = tag ordinal, which is declaration order (`D-34`).
- **Nothing the solver produces may influence order.** Sorting by power or by flow makes rows jump
  between two solves of the same plant, and a list whose rows move is a list nobody can review a diff
  of.

`D-36` already guarantees that an unrelated edit does not renumber a tag, so "updates automatically
when a component is added or removed" needs no machinery beyond the existing debounce: the list is a
projection of a contract that is already recomputed.

## The declared fields

The equipment model, the notes, and the electrical supply data are written **in the script**, on the
component, and edited from the panel through `R-25`'s existing write-back:

```fluidscript
PU_RAD pump head=4.5 model="Grundfos Magna3 32-100" power_supply="230 V" p_el=0.18
```

One source of truth, so the data survives save and reopen, reaches static export and the report
generator, and diffs in version control beside the design it describes. Storing it beside the file
instead would put half the deliverable outside the deliverable.

Two things about that line are already settled by the language, and one of them is a trap:

- **A string-valued parameter needs no grammar work.** `string` is a token and `StringLiteralSyntax`
  is an expression ([`12-grammar`](../10-language/12-grammar.md)), and `model="Grundfos Magna3
  32-100"` parses today. The free-text columns cost nothing at the language level.
- **The parameter cannot be called `supply`.** `D-64` made `supply` a component *kind* keyword, and a
  keyword is not a parameter name — `supply="230 V"` is `FS1114` at the second token. The obvious
  name for the obvious column is taken, and `power_supply` above is the placeholder. Final names are
  allocated when the fields are, and they are checked against the keyword set first.

The alternative — declaring nothing and typing into a panel — was rejected for that reason. It is also
why the panel is *editable* rather than read-only: an edit is a script edit, which is the machinery
M5 builds anyway.

## Electrical power is where this gets a number wrong

`pump.efficiency` is documented as **hydraulic** efficiency, default 0.7. Hydraulic power divided by
hydraulic efficiency is **shaft** power. Motor efficiency and drive losses are not modelled anywhere in
FluidScript.

Printing shaft power in a column headed "electricity" overstates nothing and understates the real draw
by roughly 10–25 %, silently, on the document somebody sizes a supply and a breaker from. Three honest
resolutions, and the list must take one before the column exists:

| Option | Cost |
|---|---|
| Add a motor efficiency to the pump model | A `D-` entry and a parameter on every kind that draws power. The most correct, and the most work |
| Label the column **shaft power** and leave electrical entirely declared | Free, honest, and less useful — the contractor still types the number |
| Emit nothing derived; both electrical columns declared | Free, honest, least useful |

This is open question 1.

## Export

**CSV, RFC 4180, comma-delimited, `.` decimal separator, UTF-8 with a BOM.** One dialect, chosen once,
because an exporter that switches on locale produces files that silently differ between two engineers'
machines — and the difference only surfaces when the two spreadsheets disagree.

The cost is real and worth stating rather than hiding: a European Excel with a comma decimal separator
opens this file into one column until the user picks the import dialect. The BOM at least fixes the
encoding half of that. A semicolon/comma-decimal variant is a second dialect, which makes it a format
question — [`71-export-formats`](71-export-formats.md)'s admission gate, with a named consumer. This is
open question 3.

**`.xlsx` goes through that same gate.** It is a library dependency, a second compatibility surface,
and a versioned format — exactly what `71` exists to stop "export" quietly becoming. CSV passes `71`'s
consumer test trivially; xlsx has to name one.

The header is three rows — group, column, unit — matching the printed convention. The unit row is not
decoration: it is the only thing that makes a bare `755` mean something, and its absence is what the
worked example is about.

## Invariants

1. Every row's key is a `tag`; a kind with no tag code produces no row.
2. A component appears in exactly one row, in exactly one group.
3. Every derived value in a row is traceable to one `parameters` or `state` entry of the same component
   in the same model contract. The list computes no physics of its own.
4. Every dimensioned column carries a unit, and every volumetric column additionally carries the state
   its density was evaluated at.
5. Every value carries one of the four provenance classes. There is no fifth, and no unclassified value.
6. A value that cannot be derived is absent. The list never substitutes a typical figure.
7. Row order is a function of the script alone — never of a solve result.
8. The panel and the CSV render the same rows from the same block; neither derives a value the other
   does not have.
9. An air-side enthalpy is per kilogram of **dry air**, and the column says so.

## Error cases

| Situation | Required result |
|---|---|
| The model has not solved | Rows render with identity and stated columns; every solved column is empty. The list is never blanked — a half-written script is the normal state |
| The solve failed to converge | The same, plus the failure surfaced on the panel. Last-converged values are never presented as design values |
| The model is transient (`fluid dynamic`) | The list shows the **design point** (`D-58`), labelled, never the current frame |
| A coupled exchanger's tag circuit is ambiguous | One row under the assigned circuit, and `FS2216` already says the assignment was arbitrary |
| A declared field is edited in the panel and the script cannot accept it | The edit is refused and the cell reverts; no edit path produces a script that does not parse (M5's criterion) |
| An air-side row is requested before air physics exists | The air columns are absent, not zero, and the list says the capability is not present |
| Export is requested while the model is unsolved | Export succeeds with empty derived columns, and the file records that it is unsolved. A file that looks complete and is not is the worst outcome available |

## Worked example

The requested layout, applied to an air heating coil. The water side is given as `0.65 l/s, 40 → 30 °C`
and the air side as `755 l/s, −10 → 20 °C, 27 kW`.

**The water side is self-consistent.** At a mean 35 °C, ρ = 993.9 kg/m³, so 0.65 l/s is 0.6460 kg/s.
With c_p = 4.178 kJ/(kg·K) over Δt = 10 K:

```
Q = 0.6460 × 4.178 × 10 = 27.0 kW
```

**The air side is consistent with that only at a density nobody wrote down.** Dry air at the coil's
own inlet — −10 °C, 101.325 kPa — has ρ = 101 325 / (287.05 × 263.15) = **1.341 kg/m³**. At that
density:

```
ṁ   = 0.755 × 1.341            = 1.013 kg/s
Δh  = 1.006 × (20 − (−10))     = 30.2 kJ/kg
Q   = 1.013 × 30.2             = 30.6 kW
```

30.6 kW, not 27. The written row balances only at ρ = 1.2 kg/m³ — air at about 20 °C — where
ṁ = 0.906 kg/s and Q = 27.3 kW. **The two readings are 12 % apart, and both are defensible.** They are
different coils:

| Reading | Air mass flow | Leaves at | Duty |
|---|---|---|---|
| 755 l/s at inlet density (1.341) | 1.013 kg/s | **16.5 °C** | 27.0 kW |
| 755 l/s at 1.2 kg/m³ | 0.906 kg/s | 20.0 °C | 27.3 kW |
| To leave at 20 °C at inlet density | 0.894 kg/s = **667 l/s** | 20.0 °C | 27.0 kW |

What is *not* defensible is a column headed `l/s` that does not say which. Hence invariant 4, and hence
the `at`/`density` fields on every volumetric column.

Two further corrections the same row surfaces:

- **Δh was given as `12,5 kJ/(kg·K)`.** That unit is a specific heat; a specific enthalpy difference is
  kJ/kg, and the value here is ≈ 30, which is exactly what reproduces the stated 27 kW. The wrong unit
  is what let the wrong number pass unnoticed.
- **Δh is redundant for water** — it is c_p·Δt, which the two temperature columns already give. It
  earns its column only on the air side, where it carries the latent part, and it is the reason the
  RH column sits beside it. On humid air it is per kg of **dry air**, which is a few percent and looks
  like a modelling choice rather than an error.

That is the argument for this feature in one row: the inconsistency is invisible in a hand-written
table and unavoidable in a generated one, because both sides come from a single solve.

## Acceptance criteria

- [ ] Every row is keyed by a `D-34` tag, grouped by circuit number, ordered by tag ordinal, and its
      order is unchanged by re-solving.
- [ ] Every derived value in every row is traceable to a `parameters` or `state` entry of the same
      component; a test asserts the list computes no physics.
- [ ] Every value carries one of the four provenance classes, and sized values carry a basis.
- [ ] A coupled heat exchanger produces exactly one row, with both streams on it.
- [ ] Every volumetric column names the state its density was taken at; the worked example's coil
      reports 16.5 °C or 667 l/s, and never 755 l/s with 20 °C and 27 kW together.
- [ ] Air-side enthalpy is labelled per kg dry air.
- [ ] An unsolved or failed model produces a list with empty derived columns and no substituted values.
- [ ] A transient model's list shows the design point, labelled as such.
- [ ] The declared fields round-trip through save and reopen, and a panel edit writes into the script
      without disturbing another byte of it.
- [ ] The CSV and the panel render identical rows, and the CSV states its dialect.
- [ ] No electrical column is presented as electrical power until open question 1 is answered.

## Open questions

1. **What fills the electrical columns.** `pump.efficiency` is hydraulic, so the derivable quantity is
   shaft power, not electrical power — 10–25 % apart on the document a supply is sized from. Resolved
   by choosing one of the three options above; adding a motor efficiency requires a `D-` entry first.
   Blocks the electrical column group, and nothing else.
2. **Which state a volumetric flow is reported at.** The component's own inlet state is the recommended
   default — it is the state the solve actually has, and it needs no convention. A design convention
   referred to a fixed density is the alternative, and is what ventilation practice often assumes.
   Resolved by a user decision, not a measurement. Blocks the air columns; liquid columns are
   insensitive enough at plant temperatures that the recommended default is safe either way.
3. **Whether a locale CSV dialect ships.** One dialect is correct and mildly inconvenient for a
   European Excel. Resolved under `71`'s admission gate if a named consumer asks; not before.
