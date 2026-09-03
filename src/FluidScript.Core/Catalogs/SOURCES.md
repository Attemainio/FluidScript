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

## `steel_en10255` — sourced, **still unverified**, and refused by the loader

**Status: four public sources recorded; one hydraulic decision outstanding.** The retrieval was done
on **2026-09-03**. `Verified` remains `false`, so `Validate()` reports `FS2605` and
`PipeCatalogs.Resolve` refuses the catalogue — see "What is still open" below, which is a question for
a person rather than a missing signature.

`CatalogTests.TheShippedPipeCatalogueIsNotVerifiedYet` asserts the refusal. **That test is meant to be
deleted** by whoever closes the question.

### The sources

| # | Publisher | URL | Retrieved | What it supports |
|---|---|---|---|---|
| 1 | Botop Steel Pipes | <https://www.botopsteelpipes.com/steel-pipe-weight-chart-en-10220/> | 2026-09-03 | EN 10220 Series 1 preferred outside diameters |
| 2 | Eastern Steel Manufacturing Co., Ltd | <https://www.eastern-steels.com/newsdetail/din-en10220-seamless-steel-pipes.html> | 2026-09-03 | The same sequence, independently |
| 3 | Durgapur Tubes Pvt Ltd | <https://durgapurtubes.com/en-10255.html> | 2026-09-03 | EN 10255 medium wall thicknesses; the inch column that pins the DN mapping |
| 4 | Union Steel Industry Co., Ltd | <https://www.union-steels.com/standards/en-10255.html> | 2026-09-03 | The same wall thicknesses, independently |

**No single source covered both halves correctly**, which is the two-source rule doing exactly what it
is for rather than a shortcoming of the search.

### What the sources settled

- **The wall thicknesses are confirmed.** Sources 3 and 4 agree with each other and with every value
  shipped: 2.6, 2.6, 3.2, 3.2, 3.2, 3.6, 3.6, 4.0, 4.5, 5.0, 5.0 for DN15 through DN150.
- **The DN mapping is confirmed.** Source 3 carries the inch column: ½″ is DN15, 1″ is DN25, 2″ is
  DN50, 6″ is DN150.
- **The range is confirmed.** EN 10255 covers DN8–DN150 and no further (`C-31`).
- **DN150 is 165.1 mm, not 168.3.** Sources 1 and 2 both give the EN 10220 Series 1 sequence as
  … 114.3, 139.7, **168.3**, 219.1 … with no 165.1 in it. EN 10255's threadable 6″ tube is 165.1 and
  EN 10220's DN150 is 168.3 — a real divergence between two standards at one size (`C-36`).

### What is still open — `C-35`, and it must be decided before `Verified`

**Every supplier table found lists DN15 at 21.7 mm and DN25 at 34.2 mm**, not 21.3 and 33.7. Those are
EN 10255's *upper tolerance limits*: threadable tube is specified as an outside-diameter range, because
the thread has to be cuttable. The 21.3 / 26.9 / 33.7 series shipped here is EN 10220's Series 1
preferred diameter, which is also what `27`'s worked example computes its 27.3 mm bore and 94.1 Pa/m
from.

The difference is **about 2 % in bore and 5 % in flow area**, which is roughly 10 % in pressure
gradient — well inside the range that changes a pump selection. So the question is not clerical:

> Which outside diameter should a hydraulic catalogue carry for a tube whose standard specifies a
> range: the preferred/nominal value, the mid-tolerance value, or the maximum?

Until that is answered, `Verified` stays false. Answering it may also change `27`'s worked example,
which is the reason to answer it before anything is sized rather than after.

### A source that was found and deliberately not used

The search surfaced a PDF of BS EN 10255:2004 itself, hosted publicly by a fittings vendor. **It was
not opened and is not cited.** The standard is a copyrighted document whatever site it sits on, and
this project's rule is that dimensions are read from manufacturers' own published data and the
standard is cited by number as the authority. That is the rule costing something rather than being
free, which is when it is worth having.

### A source that was found and rejected on the evidence

One manufacturer's chart (PandaPipe) carries the correct outside-diameter *values* against **DN labels
shifted by one size**. It omits DN8 from the head of the series, so everything below it moves up a
row: DN15 is listed as 17.2 mm, DN20 as 21.3, DN25 as 26.9, and the table ends at DN150 = 139.7 with
no 165.1 at all.

Read alone it would have produced a catalogue where every pipe is one size too small and every number
still looks entirely reasonable. DN25's bore would come out at 26.9 − 2×2.6 = **21.7 mm** instead of
27.3 — a 37 % error in flow area, and nothing in the result would look wrong.

**This is the failure the two-source rule exists to catch, caught on the first attempt.** It is worth
keeping in mind that a single source was not merely thin here — it was actively wrong, from a
manufacturer, in a way no plausibility check would have flagged.

### When the table is verified

1. Decide `C-35` and set the outside diameters accordingly.
2. Set `Verified = true`. The review of that commit *is* the attestation.
3. Delete `TheShippedPipeCatalogueIsNotVerifiedYet` and this section's status line.

### Range

EN 10255 covers DN8–DN150. `27` previously promised DN15–DN300 from this series, which it cannot
supply; DN200 and above are a different series with their own sources (`C-31`).
