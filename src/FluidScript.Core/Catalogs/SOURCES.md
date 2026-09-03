# Catalogue sources

Every row in every shipped catalogue needs **two independent public sources that agree**, and a
person's attestation that they were checked. This file is what a licensing or provenance question is
answered from, and `27-component-catalog.md` is the policy it implements.

## The rule, restated

- Read dimensions from manufacturers' freely published catalogues and datasheets.
- Cite the standard by number as the *authority* a row conforms to — never reproduce its text, table
  arrangement, notes, or footnotes.
- Record publisher, URL, and retrieval date for every source.
- Never scrape a paywalled or login-gated standards portal.
- Never fetch at run time. The catalogue is compiled into the build (`D-66`).

Dimensions are facts about physical objects and are not copyrightable. A standard's particular
expression of them is. Nothing in this repository reproduces the latter.

## `steel_en10255` — **unverified, and refused by the loader**

**Status: awaiting the one-time human retrieval.** The rows in `SteelEn10255.cs` were authored from
engineering knowledge of the medium series. They carry `Verified = false` and no sources, so
`Catalog.Validate()` reports `FS2605` and `PipeCatalogs.Resolve` refuses them. That is the designed
behaviour: an unverified dimension produces a wrong design silently, and the cost of catching it late
is far higher than the cost of a refused build.

`CatalogTests.TheShippedPipeCatalogueIsNotVerifiedYet` asserts the refusal. **That test is meant to be
deleted** by whoever completes the table below — it is what stops this state from becoming permanent.

### What needs checking

Outside diameter and wall thickness, in millimetres, against two independent public sources. The bore
is derived, so it needs no separate check; the two numbers below are the whole of it.

| Designation | OD (mm) | Wall (mm) | Source 1 | Source 2 | Verified by |
|---|---|---|---|---|---|
| DN15 | 21.3 | 2.6 | | | |
| DN20 | 26.9 | 2.6 | | | |
| DN25 | 33.7 | 3.2 | | | |
| DN32 | 42.4 | 3.2 | | | |
| DN40 | 48.3 | 3.2 | | | |
| DN50 | 60.3 | 3.6 | | | |
| DN65 | 76.1 | 3.6 | | | |
| DN80 | 88.9 | 4.0 | | | |
| DN100 | 114.3 | 4.5 | | | |
| DN125 | 139.7 | 5.0 | | | |
| DN150 | 165.1 | 5.0 | | | |

DN25's 33.7 mm OD and 3.2 mm wall give the 27.3 mm bore that `27`'s worked example and every reference
circuit depend on. **One wrong wall thickness moves the pump head by several percent** with nothing
looking wrong, which is why two sources rather than one.

### When the table is filled

1. Add each source to the row's `Provenance.Sources` with publisher, URL, and retrieval date.
2. Set `Verified = true` on the rows that were actually checked, and only those.
3. Delete `TheShippedPipeCatalogueIsNotVerifiedYet` and its explanation.
4. Commit as one reviewed change. The review *is* the attestation.

### Range

EN 10255 covers DN6–DN150. `27`'s open-questions section promises DN15–DN300 from this series, which
that standard cannot supply; anything above DN150 is a second series with its own sources. Recorded as
`C-31`.
