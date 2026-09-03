---
id: 27-component-catalog
title: Component catalog and standard dimensions
tier: 20-core-domain
status: reviewed
owns: [nominal pipe dimension tables, Kv catalogue, data provenance, sourcing policy, catalog versioning]
depends_on: [22-component-model]
traces_to: [R-02, R-28, R-30, R-32, R-35]
open_questions: 0
last_review_pass: 2
---

# Component catalog and standard dimensions

## Purpose

Auto-sizing ([`24-auto-sizing`](24-auto-sizing.md)) selects from catalogues — nominal pipe diameters,
valve Kv values, pump curves — and those catalogues have to come from somewhere. The standards that
define them (EN 10220, EN 10255, SFS equivalents, ISO 4200) are **copyrighted, paywalled documents**,
so this document is as much about lawful sourcing and provenance as it is about data structure.

## Responsibilities

**Owns.** The catalog data model, the pipe-dimension tables, the Kv catalogue, the sourcing policy,
provenance per row, and catalog versioning.

**Explicitly does not own.** How a size is chosen ([`24-auto-sizing`](24-auto-sizing.md)), component
equations ([`22-component-model`](22-component-model.md)), how the catalog is presented in the UI.

## The legal position, stated plainly

**Do not redistribute standard documents, and do not scrape paywalled standards portals.** SFS Online,
CEN, and ISO sell these documents under licences that forbid redistribution, and a project that ships
a scraped copy of EN 10220's tables is redistributing them regardless of how the data was formatted.

**What is not restricted:** the *dimensions themselves*. That a DN25 steel pipe has a 33.7 mm outside
diameter is a fact about a physical object, and facts are not copyrightable in either the EU or the US.
What is protected is a particular *expression* — the standard's table layout, its selection and
arrangement, its annotations.

So the rule this project follows:

| Do | Do not |
|---|---|
| Read dimensions from manufacturers' freely published catalogues and datasheets | Copy a standard's table wholesale, including its arrangement |
| Cite the standard by number as the *authority* a row conforms to | Reproduce the standard's own text, notes, or footnotes |
| Cross-check a value against two or more independent public sources | Take a single source on trust |
| Record the URL and retrieval date for every source | Scrape a paywalled or login-gated portal |
| Ship a curated, hand-verified data file | Fetch data at runtime from a third party |

**Curated and shipped, never fetched at runtime.** A runtime dependency on someone else's website makes
sizing non-deterministic, breaks offline use, breaks reproducibility of a saved design, and puts a
third party in the path of every solve. The catalog is a versioned data file in the repository.

## Sourcing

Manufacturers publish dimensional data freely because they want it used. Suitable public sources:

| Category | Examples of what to use | What it supplies |
|---|---|---|
| Pipe manufacturers | Public product catalogues and technical datasheets (PDF/HTML) | OD, wall thickness, ID, mass per metre, by DN and series |
| Valve manufacturers | Public Kv/authority selection tables | Kv values per DN and characteristic |
| Pump manufacturers | Public curve data and selection tools | Head/flow curve points, efficiency |
| Standards bodies' free abstracts | Scope pages listing the DN range covered | Confirms which standard governs a range |
| Open engineering references | Public reference tables that themselves cite their source | Cross-check only, never a sole source |

**Every row needs two independent sources agreeing.** A single manufacturer's typo becomes a silent
sizing error affecting every circuit that lands on that diameter, and a wrong ID propagates into
velocity, Reynolds number, friction factor, and pump head — a plausible-looking result that is wrong.

**Retrieval is a one-time, human-reviewed task**, not an automated pipeline. The data changes on the
timescale of standards revisions, i.e. years.

## The standards landscape

Which standard governs which pipe is not obvious, and picking the wrong one gives dimensions that are
plausible and wrong. This is the map, so a future catalogue package starts from a named standard rather
than from a search. It is scope, not a commitment: v1 ships one series.

**Designation, not dimension.** These define the labels everything else uses.

| Standard | Defines |
|---|---|
| **EN ISO 6708** | `DN` — nominal size. The designation this project's glossary insists is not a diameter |
| **EN 1333** | `PN` — nominal pressure designation |

**Dimensional series.** The tables a catalogue row is actually read from.

| Standard | Covers |
|---|---|
| **EN 10220** | Preferred OD, wall thickness and mass for steel tubes — the Series 1 diameters (21.3, 26.9, 33.7 …) |
| **EN 10255** | Non-alloy steel tube for **welding and threading**; DN8–DN150. Specifies OD as a *range*, not a single value (`C-35`) |
| **EN 10216 / EN 10217** | Seamless and welded steel tube for pressure purposes |
| **EN ISO 1127** | Stainless tube dimensions, tolerances and masses |
| **EN 10312** | Welded stainless pipe for water and aqueous liquids |
| **EN 1057** | Copper pipe for water, heating and gas |
| **EN 12735-1 / -2** | Copper tube for refrigeration and air conditioning |
| **ISO 4200** | International steel OD/thickness series |

**Plastics**, each material with its own standard: PP `EN ISO 15874`, PE-X `EN ISO 15875`, PB
`EN ISO 15876`, PVC-C `EN ISO 15877`, PE-RT `EN ISO 22391`, multilayer `ISO 21003`, PE pressure pipe
`EN 12201`, PVC-U pressure pipe `EN ISO 1452`.

**District heating**, which is a Finnish-context necessity rather than a nicety: `EN 253`
(pre-insulated bonded pipe), `EN 448` (fittings), `EN 488` (valves), `EN 489-1` (joint casings),
`EN 15632` (flexible pre-insulated systems).

**Fittings, flanges and threads**, wanted by the fitting catalogue this document defers to post-v1:
`EN 10253` (butt-weld), `EN 10241` (steel threaded), `EN 10242` (malleable iron threaded), `EN 1254`
(copper), `EN 1092` (flanges), `EN 1514` (gaskets), `EN 10226`/`ISO 7-1` (pressure-tight threads),
`ISO 228-1` (non-pressure-tight G threads).

**US counterparts**, for reading a datasheet that cites the other family: EN 10220 ↔ ASME B36.10M,
EN ISO 1127 ↔ ASME B36.19M, EN 1092 ↔ ASME B16.5, EN 10253 ↔ ASME B16.9, EN 1057 ↔ ASTM B88,
EN 12735 ↔ ASTM B280, PE-X ↔ ASTM F876/F877, EN 13480 ↔ ASME B31.1/B31.3.

**Acquisition order**, if catalogues are added one at a time and the goal is Finnish HVAC coverage:
EN ISO 6708 → EN 10220 → EN 10255 → EN 10216/10217 → EN ISO 1127 → EN 10312 → EN 1057 → EN 12735 →
EN ISO 15874 → EN ISO 15875 → EN ISO 22391 → ISO 21003 → EN 12201 → EN 1092 → EN 10253 → EN 1254 →
EN 13480.

**None of these is acquired, and none needs to be.** They are named as the *authority* a row conforms
to. The dimensions come from manufacturers' freely published data, which is what makes the sourcing
policy above lawful and what `Catalogs/SOURCES.md` records.

## Data model

```csharp
/// <summary>One selectable size from a catalogue.</summary>
public sealed record CatalogEntry<TSpec>
{
    /// <summary>Designation as an engineer would write it: "DN25", "Kv 1.6".</summary>
    public required string Designation { get; init; }

    /// <summary>The dimensional data.</summary>
    public required TSpec Spec { get; init; }

    /// <summary>Where this row came from and when.</summary>
    /// <remarks>
    /// Never null. A catalogue row without provenance cannot be defended when a user asks why
    /// their pipe is 27.2 mm, and cannot be audited when a value turns out wrong.
    /// </remarks>
    public required Provenance Provenance { get; init; }
}

/// <summary>Where a catalogue value came from.</summary>
public sealed record Provenance
{
    /// <summary>The standard this row conforms to, cited by number only.</summary>
    /// <value>"EN 10220", "EN 10255", "ISO 4200", or null for a manufacturer-specific value.</value>
    public string? Standard { get; init; }

    /// <summary>Public sources consulted, at least two.</summary>
    public required ImmutableArray<SourceReference> Sources { get; init; }

    /// <summary>Whether a human verified this row against its sources.</summary>
    public required bool Verified { get; init; }
}

public sealed record SourceReference(string Publisher, string Url, DateOnly Retrieved);

/// <summary>Dimensions of one nominal pipe size in one series.</summary>
public sealed record PipeSpec
{
    /// <summary>Nominal diameter designation, dimensionless by definition.</summary>
    /// <value>
    /// The DN number: 25 for DN25. <b>Not a length</b> — DN25 pipe is 33.7 mm outside and
    /// 27.3 mm bore. Nothing hydraulic may read this field; read
    /// <see cref="InsideDiameter"/> instead.
    /// </value>
    public required int NominalDiameter { get; init; }

    /// <summary>Outside diameter.</summary><value>m.</value>
    public required double OutsideDiameter { get; init; }

    /// <summary>Wall thickness.</summary><value>m.</value>
    public required double WallThickness { get; init; }

    /// <summary>Inside diameter — the hydraulically relevant one.</summary>
    /// <value>m. Derived as OD − 2·wall, not stored independently, so the three can never disagree.</value>
    public double InsideDiameter => OutsideDiameter - 2 * WallThickness;

    /// <summary>Absolute roughness for this material.</summary><value>m.</value>
    public required double Roughness { get; init; }

    /// <summary>Material and series, e.g. "steel, EN 10255 medium".</summary>
    public required string Series { get; init; }
}
```

**Metres as `double`, not `Quantity`.** These were written as `Quantity` and the derivation above does
not compile: `Quantity` has no arithmetic operators, only `TryAdd`/`TrySubtract`, because unit
arithmetic can fail and a silent operator would hide it. Plain metres also matches `Pipe`, which is the
only consumer of a bore (`C-33`).

**`InsideDiameter` is computed, never stored.** A catalogue with all three as independent fields will,
sooner or later, contain a row where they do not agree, and the resulting error is invisible.

## Plate heat exchangers

`D-17` makes the exchanger rated, which means the catalogue now has to carry plate models as well as
pipe dimensions. The data model mirrors `PipeSpec`: physical facts with two public sources each.

```csharp
/// <summary>One plate model from one manufacturer's range.</summary>
public sealed record PlateSpec
{
    /// <summary>Manufacturer's model designation, e.g. "B3-014".</summary>
    public required string Model { get; init; }

    /// <summary>Effective heat transfer area of one plate.</summary><value>m².</value>
    public required Quantity PlateArea { get; init; }

    /// <summary>Lamella between adjacent plates.</summary><value>m.</value>
    public required Quantity Lamella { get; init; }

    /// <summary>Effective channel width, for the per-channel velocity.</summary><value>m.</value>
    public required Quantity PlateWidth { get; init; }

    /// <summary>Plate thickness and conductivity, for the wall resistance.</summary>
    public required Quantity PlateThickness { get; init; }
    public required Quantity PlateConductivity { get; init; }

    /// <summary>Nusselt correlation constants for this plate's chevron pattern.</summary>
    /// <remarks>
    /// <c>Nu = C · Re^m · Pr^(1/3)</c>. C and m depend on the chevron angle and are the one part
    /// of this record that is a <i>correlation fit</i> rather than a measured dimension — see the
    /// provenance rule below, which treats them differently for that reason.
    /// </remarks>
    public required double NusseltC { get; init; }
    public required double NusseltM { get; init; }

    /// <summary>Available total plate counts, ascending.</summary>
    /// <remarks>Usually even and in steps of two, keeping the channel split between the sides equal.</remarks>
    public required ImmutableArray<int> PlateCounts { get; init; }
}
```

**Dimensions and correlations need different provenance, and conflating them is the trap.** A plate's
area, gap, and thickness are *facts about a physical object* and fall under this document's "facts are
not copyrightable" rule exactly as pipe bores do. `NusseltC` and `NusseltM` are **fitted constants from
published research**, and a fit is an expression: it carries an author, a validity range, and a stated
uncertainty. Every correlation row therefore records its literature citation, the Re and Pr range it
was fitted over, and its reported scatter — and a solve outside that range is `FS2607` (warning), not a
silent extrapolation.

A default correlation ships for a generic 60° chevron so that a plate model with no published fit is
still usable, labelled `default` in the same way `hx.dp_default` is. It is the weakest number in the
exchanger path and `/docs` must say so.

## How the data is held

**A compiled C# table, not a data file (`D-66`).** `D-47` forbids any source file in Core from
reaching a serializer, and P3.5 is the first package that needed to read structured data past it.
Nothing the JSON was for is lost — the rows are versioned in git, human-readable, curated and shipped
rather than fetched — and a malformed row becomes a build error rather than an `FS2604` at run time.
The shape below is retained as the *logical* model: `Provenance` is a record on each entry rather than
a `sourceIndex` string, and `SOURCES.md` is unchanged.

```
src/FluidScript.Core/Catalogs/
├── pipes-steel-en10255.json
├── pipes-steel-en10220.json
├── pipes-copper-en1057.json
├── valves-kv.json
├── plates-generic.json
├── pumps-generic.json
└── SOURCES.md              human-readable provenance summary, per file
```

Millimetres in the table and metres in `PipeSpec`: these are the numbers a manufacturer's catalogue
prints, and transcribing them in the unit they were read in is what makes a row checkable against its
source by eye. Committed to git so a design is reproducible against a known catalog version.

```jsonc
{
  "catalog": "pipes-steel-en10255",
  "version": "1.0.0",
  "standard": "EN 10255",
  "description": "Non-alloy steel tubes suitable for welding and threading — medium series",
  "defaultRoughnessMm": 0.045,
  "entries": [
    { "dn": 15, "odMm": 21.3, "wallMm": 2.6,
      "sources": ["mfr-a-2026-08", "mfr-b-2026-08"], "verified": true },
    { "dn": 20, "odMm": 26.9, "wallMm": 2.6,
      "sources": ["mfr-a-2026-08", "mfr-b-2026-08"], "verified": true },
    { "dn": 25, "odMm": 33.7, "wallMm": 3.2,
      "sources": ["mfr-a-2026-08", "mfr-b-2026-08"], "verified": true }
  ],
  "sourceIndex": {
    "mfr-a-2026-08": { "publisher": "…", "url": "https://…", "retrieved": "2026-08-29" },
    "mfr-b-2026-08": { "publisher": "…", "url": "https://…", "retrieved": "2026-08-29" }
  }
}
```

The `sourceIndex` indirection keeps rows short while making every row's provenance explicit and
auditable. `SOURCES.md` restates it for a human reader and is what a licensing question gets answered
from.

## Catalog selection

A circuit picks catalogues from the `fluid`/`style` context or from defaults:

| Catalogue | Default | Why |
|---|---|---|
| Pipes | `pipes-steel-en10255` | The common European hydronic default |
| Valves | `valves-kv` | Generic, manufacturer-neutral |
| Pumps | `pumps-generic` | The quadratic default curve ([`22`](22-component-model.md)) |

Every durable script pins the exact catalogue with `catalog <id>@<version>` as defined by
[`12-grammar`](../10-language/12-grammar.md) and [`18-script-compatibility`](../10-language/18-script-compatibility.md).
Defaults may seed a new draft, but a saved file never relies on an unversioned deployment default.

## Contracts

```csharp
/// <summary>Standard sizes available to the sizer.</summary>
public interface ICatalog<TSpec>
{
    string Name { get; }
    string? Standard { get; }

    /// <summary>Every entry, in ascending designation order.</summary>
    IReadOnlyList<CatalogEntry<TSpec>> Entries { get; }

    /// <summary>The smallest entry satisfying a predicate.</summary>
    /// <returns>The entry and how well it fitted; the largest when nothing satisfies it.</returns>
    CatalogSelection<TSpec> SmallestSatisfying(Func<TSpec, bool> predicate);

    /// <summary>The nearest entry at or below a target — the safe direction for valve Kv
    /// ([`24-auto-sizing`](24-auto-sizing.md)).</summary>
    CatalogSelection<TSpec> NearestBelow(double target, Func<TSpec, double> selector);

    /// <summary>Everything wrong with this catalogue, or empty when it is fit to size against.</summary>
    ImmutableArray<ResultError> Validate();
}
```

**Selection clamps and reports the fit; it does not fail.** These returns were written as a `Result`
failing when nothing fits, which contradicts this document's own error table — `FS2601` is a *warning*
that says "using {max}". The table is right: a design needing more than DN150 still gets a number and
a clear warning, where a refusal would blank the diagram over a circuit that solves. `CatalogFit` is
`Exact`, `ClampedToLargest` or `ClampedToSmallest`, and the sizer turns the two clamps into `FS2601`
and `FS2602` (`C-34`).

## Invariants

1. Every entry has provenance with at least two sources and `verified: true`.
2. No standard's text, table arrangement, or annotation is reproduced in this repository.
3. Inside diameter is derived, never stored.
4. Entries are ordered by designation and contain no duplicates.
5. The catalog is loaded from the shipped data file; no network access occurs at any point.
6. A catalog version change is a versioned, reviewed commit — a sizing result is reproducible given a
   script and a catalog version.
7. Every dimension is physically plausible: OD > 2 × wall, roughness > 0, monotonically increasing OD.

Invariant 7 is a cheap automated guard against transcription errors, which are the realistic failure
mode of hand-curated data.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS2601` | Required size exceeds the largest catalogue entry | Warning | `'{name}' needs more than {max}; using {max}. Consider two parallel runs.` |
| `FS2602` | Required size below the smallest entry | Info | `'{name}' sized to the smallest available, {min}.` |
| `FS2603` | Named catalogue does not exist | Error | `No catalogue '{name}'. Available: {list}.` |
| `FS2604` | Catalogue file failed validation on load | Error | `Catalogue '{name}' is invalid: {reason}.` — a build/startup failure, not a user error |
| `FS2605` | Entry lacks verified provenance | Error | Startup failure. An unverified row must never reach a user. |
| `FS2606` | No `catalog` directive; the shipped default was used | Info | `Using catalogue '{name}'. Write 'catalog {name}' to pin it.` |
| `FS2607` | A correlation was evaluated outside its fitted range | Warning | `'{name}': the {plate} correlation is fitted for Re {lo}–{hi} and this design runs at {re}. The result is an extrapolation.` |

**Three of these are not registered yet, and each waits for a different thing.** `FS2601` and
`FS2602` name the *component* that was clamped, and the catalogue does not know who is asking — so
selection returns a `CatalogFit` and the sizing loop that knows the name builds them (`P3.7`).
`FS2607` needs plates (`P4.1`). `FS2604` shipped with a narrower meaning than the row above states:
there is no file to fail parsing, so it reports invariant 7's structural faults instead (`D-66`).

`FS2605` failing at startup rather than warning is deliberate: an unverified dimension produces a wrong
design silently, and the cost of catching it late is far higher than the cost of a failed build.

## Worked example

Sizing the **simple loop**'s pipe ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md))
at 0.241 l/s, target gradient 150 Pa/m, `pipes-steel-en10255`. Water at the loop's 35 °C mean:
ρ = 994 kg/m³, μ = 0.7225 × 10⁻³ Pa·s, ε = 0.045 mm, Darcy–Weisbach with Colebrook–White.

**This table is the single source for these figures.** [`24-auto-sizing`](24-auto-sizing.md) cites it
rather than restating it — two copies of a pressure-gradient table is how the tree ended up with two
different answers for the same pipe.

| DN | OD (mm) | Wall (mm) | ID (mm) | Velocity (m/s) | Re | f | Gradient (Pa/m) | Verdict |
|---|---|---|---|---|---|---|---|---|
| 15 | 21.3 | 2.6 | 16.1 | 1.183 | 26 200 | 0.0301 | 1299 | ✗ gradient |
| 20 | 26.9 | 2.6 | 21.7 | 0.651 | 19 440 | 0.0301 | 292 | ✗ gradient |
| 25 | 33.7 | 3.2 | 27.3 | 0.411 | 15 450 | 0.0305 | **94.1** | ✓ |
| 32 | 42.4 | 3.2 | 36.0 | 0.237 | 11 720 | 0.0316 | 24.4 | (oversized) |

**DN25 selected**, velocity 0.411 m/s — below the 1.0 m/s limit, so no step-up. Reported as
`"DN25 (EN 10255) — 94 Pa/m, 0.41 m/s"`, and hover links to the provenance.

Two things about this table are worth pinning, because both were got wrong before. The velocities come
from the **inside** diameter, so DN25 gives 0.411 m/s and not the 0.44 m/s that the 25 mm designation
would give. And the gradient is genuinely steep: at DN25 this flow costs ~94 Pa/m, not the ~60 Pa/m
that a reader eyeballing a chart might expect — which matters because the pump head follows directly
from it.

The 27.3 mm inside diameter is what every downstream number depends on: velocity is
Q/(π(0.0273/2)²), Reynolds number is ρvD/μ, friction factor follows from Re and ε/D, and the pump head
follows from that. **One wrong wall thickness moves the pump head by several percent** with nothing
looking wrong — which is why invariant 1 demands two sources.

Note DN25's ID is 27.3 mm, not 25 mm. DN is a designation, not a dimension, and the glossary
([`02-glossary`](../00-foundation/02-glossary.md)) must say so — treating DN as a diameter is a
15 % error in area and a factor-of-two error in pressure gradient.

## Acceptance criteria

- [ ] Every shipped catalogue row has two sources and `verified: true`; startup fails otherwise.
- [ ] `SOURCES.md` lists every source with publisher, URL, and retrieval date.
- [ ] No standard's table text appears anywhere in the repository (reviewed, and stated in `SOURCES.md`).
- [ ] Plausibility validation runs at load and rejects OD ≤ 2×wall or non-monotonic OD.
- [ ] The worked example selects DN25 with ID 27.3 mm, at 94 Pa/m and 0.411 m/s.
- [ ] The gradient table is computed by the same code the sizer uses, not transcribed — a test
      recomputes every row and fails on a discrepancy above 1 %.
- [ ] No network call occurs during any solve, asserted by a test with networking disabled.
- [ ] A sized result names its catalogue and standard in its basis string.
- [ ] `/docs` renders the catalogue from the same data file the sizer reads.

## Open questions

None. v1 ships one verified pipe series (`steel_en10255`, DN15–DN150 — the range EN 10255 covers;
DN200 and above are a second series with their own sources, `C-31`) and one generic discrete Kv
series. `catalog steel_en10255@2026.1` pins exactly one version; `catalog steel_en10255` selects the
shipped version of that named catalogue; absence selects the shipped default. Every resolution records
the exact id and version. M2a cannot exit until two public manufacturer sources support every row and an independent
review checks the generated table. Pump curves remain user-supplied or the documented generic
quadratic; changing manufacturer product data is not bundled as a supposedly durable catalogue.
