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

## `steel_en10255` — **verified 2026-09-03**

**Status: verified on the nominal preferred diameters (`D-67`).** Five public sources, retrieved
2026-09-03. `Validate()` is clean and `PipeCatalogs.Resolve` returns the catalogue.

What this attestation covers: the **outside diameter** and **wall thickness** of every row. What it
does **not** cover is `Roughness` — 0.045 mm is a textbook value for new commercial steel, is in no
pipe standard, and is `C-37`.

### The sources

| # | Publisher | URL | Retrieved | What it supports |
|---|---|---|---|---|
| 1 | Botop Steel Pipes | <https://www.botopsteelpipes.com/steel-pipe-weight-chart-en-10220/> | 2026-09-03 | EN 10220 Series 1 preferred outside diameters |
| 2 | Eastern Steel Manufacturing Co., Ltd | <https://www.eastern-steels.com/newsdetail/din-en10220-seamless-steel-pipes.html> | 2026-09-03 | The same sequence, independently |
| 3 | Durgapur Tubes Pvt Ltd | <https://durgapurtubes.com/en-10255.html> | 2026-09-03 | EN 10255 medium wall thicknesses; the inch column that pins the DN mapping |
| 4 | Union Steel Industry Co., Ltd | <https://www.union-steels.com/standards/en-10255.html> | 2026-09-03 | The same wall thicknesses, independently |
| 5 | Integraflow | <https://www.integraflow.co.uk/shop/cs-pip-150-med-gal-pln-dn150-6-nb-medium-wt-en10255-plain-end-galvanised-pipe-10199> | 2026-09-03 | DN150's diameter, via the published mass per metre |

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

### The diameter basis — decided

**The rows carry the nominal preferred diameter**, which is `D-67`. Every supplier table found lists
DN15 at 21.7 mm and DN25 at 34.2 mm; those are EN 10255's *upper tolerance limits*, because a threadable
tube is specified as a range. Sizing on them adds about 2 % to the bore, 5 % to the flow area and 10 %
to the pressure gradient, always in the direction that flatters the design.

The 21.3 / 26.9 / 33.7 series shipped here is EN 10220's Series 1 preferred diameter, which is what a
manufacturer aims at and what `27`'s worked example already computed its 27.3 mm bore and 94.1 Pa/m
from — so the decision changes no published figure.

### DN150 — settled by arithmetic

The one row where the sources disagreed. EN 10220's series runs 139.7 → **168.3** with no 165.1 in it;
EN 10255's 6″ tube is **165.1**; and source 5's product page states *both* diameters for the same
product.

Its published mass does not equivocate. At 7850 kg/m³:

```
165.1 / 5.0  ->  pi/4 * (165.1^2 - 155.1^2) * 7850e-6  =  19.74 kg/m     <- stated: 19.7
168.3 / 5.0  ->  pi/4 * (168.3^2 - 158.3^2) * 7850e-6  =  20.14 kg/m
```

**A mass per metre constrains a diameter and a wall together**, which is what makes it a third source
rather than a restatement of either. `CatalogTests.Dn150ReproducesItsPublishedMassPerMetre` keeps the
check, because the next series added will face the same disagreement.

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

### What roughness still needs — `C-37`

`Roughness = 0.045 mm` is shipped and **is not covered by the verification above**. It is not a
dimension, appears in no pipe standard, and no manufacturer's table carries it; it is a textbook figure
for pipe that is *new*. A scaled steel heating pipe is nearer 0.15–0.5 mm, and at DN25 that moves the
friction factor from 0.0305 to about 0.0425 — roughly 40 % on the pressure gradient and on the pump
head that follows it. It needs a literature citation per material and a decision on whether v1 sizes
for new or aged pipe.

### Range

EN 10255 covers DN8–DN150. `27` previously promised DN15–DN300 from this series, which it cannot
supply; DN200 and above are a different series with their own sources (`C-31`).
