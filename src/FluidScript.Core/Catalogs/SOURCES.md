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

### Roughness — provenanced separately, and deliberately

`Roughness` is **not** covered by the diameter attestation above, and `D-68` says why. It is not a
dimension: it appears in no pipe standard, no manufacturer's table carries it, and its published
tolerance is ±30 % to ±50 %. What defends it is a citation and a stated condition, not two suppliers
agreeing — so `MaterialRoughness` carries value, material, condition, citation and sources, and needs
one source rather than two.

| Material | ε | Condition | Citation |
|---|---|---|---|
| commercial steel | 0.045 mm | **new** | Moody (1944); Colebrook (1939); Crane TP-410 |
| drawn copper | 0.0015 mm | **new** | as above |

| # | Publisher | URL | Retrieved |
|---|---|---|---|
| R1 | SimuPipe | <https://simupipe.com/resources/pipe-roughness> | 2026-09-03 |
| R2 | EngineerExcel | <https://engineerexcel.com/pipe-roughness/> | 2026-09-03 |

**The uncertainty that matters is not the one it looks like.** At DN25 and Re 15 450, the ±50 % band on
the textbook value moves the friction factor 0.0305 → 0.0319, about **4.5 %**. Ageing to 0.3 mm moves
it to 0.0425, about **39 %**. Arguing about which table to copy is arguing about the small term; the
condition is the large one, which is why it is a field rather than a comment.

A script that wants aged pipe writes `roughness=0.3 mm`. v1 does not pretend to know that number.

## `steel_en10220` — **verified 2026-09-21** (`C-110`)

**Status: verified on the walls the Finnish market stocks.** Welded tube to EN 10217-1 in P235TR1 on
EN 10220's Series 1 diameters, DN15–DN300. EN 10220 lists a dozen walls per diameter and chooses none,
so the wall is chosen the way `D-129` chose copper's: the item an installer buys. Two wholesalers'
category listings carry every row below as a stocked, painted heating tube, and two dimension tables
carry the DN designation each diameter is sold under.

| # | Publisher | URL | Retrieved | What it supports |
|---|---|---|---|---|
| 1 | Dahl Suomi Oy | <https://www.dahl.fi/tuoteryhma/hitsatut-putket-lv/> | 2026-09-21 | Every diameter and wall, as stocked items (`Teräsputki hitsattu EN 10217-1 … P235TR1 suojamaalattu`) |
| 2 | Onninen Oy | <https://www.onninen.fi/lampo-ja-vesi-seka-prosessiputkistot/hitsatut-hiiliterasputket/c/7> | 2026-09-21 | The same items, independently (four listing pages) |
| 3 | Eastern Steel Manufacturing Co., Ltd | <https://www.eastern-steels.com/newsdetail/din-en10220-seamless-steel-pipes.html> | 2026-09-21 | DN15–DN300 against the Series 1 diameter each carries |
| 4 | Pipe Flow Calculations | <https://www.pipeflowcalculations.com/tables/en10220-DN15.xhtml> | 2026-09-21 | The same mapping, independently |

### What the sources settled

- **The rows.** 21.3 × 2.0, 26.9 × 2.3, 33.7 × 2.6, 42.4 × 2.6, 48.3 × 2.6, 60.3 × 2.9, 76.1 × 2.9,
  88.9 × 3.2, 114.3 × 3.6, 139.7 × 4.0, 168.3 × 4.5, 219.1 × 4.5, 273.0 × 5.0, 323.9 × 5.6 — DN15 to
  DN300. Sources 1 and 2 both list every one.
- **Where both stock two walls, the row takes the painted heating item.** Both carry 168.3 × 4.0 and
  × 4.5, and 219.1 × 4.5 and × 6.3; 4.5 is the wall both sell painted for heating at DN150, and at DN200
  the 6.3 is the process-pipe wall. This is this project's choice, not a standard's.
- **DN250 is 273.0, not 273.1.** Source 4 prints 273.1 for DN250; sources 1, 2 and 3 and the EN 10220
  Series 1 chart already cited for `steel_en10255` (Botop) all print 273.0. Three against one, and the
  wholesalers sell the tube as 273,0. Recorded because a fourth source that disagrees by a tenth of a
  millimetre is exactly the kind of thing a later reader should not have to rediscover.
- **Roughness** is the steel figure and its condition, provenanced under `steel_en10255` above; `C-37`
  still owns the ageing question.

## `copper_en1057` — **verified 2026-09-21** (`D-129`)

**Status: verified on the Finnish type-approved range.** Two independent public listings per row,
retrieved 2026-09-21; `Validate()` is clean and `PipeCatalogs.Resolve` returns the catalogue. The
rows are the walls a Finnish wholesaler stocks -- the market's plain tube is Cupori 110 Premium and
every listing below is of it -- which `D-129` chose over the UK Table X range the rows once carried
(15 × 0.7) and over the German range (28 × 1.5, 54 × 2.0). EN 1057 leaves the wall to national type
approval, so the standard settles nothing here and is cited as the authority only.

What this attestation covers: the **outside diameter** and **wall thickness** (or the bore, which a
Finnish listing prints as `OD x ID`) of every row. `Roughness` is `D-68`'s, above.

### The rows and their sources

| Designation | OD (mm) | Wall (mm) | Bore (mm) | Source 1 | Source 2 |
|---|---|---|---|---|---|
| 12 mm | 12 | 1.0 | 10.0 | Taloon.com, `cupori-premium-110-12x3000` | Jukira Oy, straight tubes (12×10) |
| 15 mm | 15 | 1.0 | 13.0 | LVI-Tarvikkeet, `15x3000` (15.0 × 1.0 stated) | Jukira Oy (15×13) |
| 18 mm | 18 | 1.0 | 16.0 | Taloon.com, `18x3000` | Jukira Oy (18×16) |
| 22 mm | 22 | 1.0 | 20.0 | Taloon.com, `22x20-mm-5-m` | Jukira Oy (22×20) |
| 28 mm | 28 | 1.2 | 25.6 | Taloon.com, `28x25-6-mm-5-m` | Jukira Oy (28×25.6) |
| 35 mm | 35 | 1.5 | 32.0 | Taloon.com, `35x32-mm-5-m` | Cronvall, variant 10175 (35×1.5) |
| 42 mm | 42 | 1.5 | 39.0 | Taloon.com, `42x39-mm-5-m` | Onninen, AMM025 (42×39) |
| 54 mm | 54 | 1.5 | 51.0 | Taloon.com, `54x3000` (wall 1.5 stated) | Onninen, AMM026 (54×51) |
| 88.9 mm (`dn=89`) | 88.9 | 2.0 | 84.9 | Taloon.com, `88-9x84-9-mm-5-m` | KME, Product Data Sheet Plumbing Tubes 2017 (88.9 × 2.0) |
| 108 mm | 108 | 2.5 | 103.0 | Onninen, AHB217 (108×103) | KME, Product Data Sheet Plumbing Tubes 2017 (108 × 2.5) |

The URLs are in `CopperEn1057.cs`, one `SourceReference` per cell. Jukira and Taloon print the bore;
LVI-Tarvikkeet and Taloon's 54 mm page print the wall; the two agree on every row both cover.

### What was found and not used

- **K-Rauta's `28x25`** rounds the bore (25.6 → 25), so read alone it says 28 × 1.5. Taloon and
  Jukira both print 25.6, and the row is 1.2. A rounded designation is not a dimension.
- **Onninen's 54 mm page** reads as 2 mm wall in one place and `54x51` in its title; Taloon states
  1.5 explicitly and the row is 1.5, with Onninen's title as the second source.
- **64 mm and 76.1 mm** are in EN 1057 and in KME's German range (64 × 2.0, 76.1 × 2.0) but in no
  Finnish listing found. They are not rows: a size the sizer would choose has to be one the market
  sells, and one source is one source.
- **Cupori's own datasheet** (`Cupori_110_Premium_EN.pdf`) carries no dimension table -- "outer
  diameters, wall thicknesses and other details according to national type approval requirements" --
  which is why the manufacturer is attested through its stockists.
- **Engineering ToolBox's EN 1057 page** reproduces the standard's whole wall matrix and is not cited.

### The designation trap

**`dn` does not mean the same thing here as in a steel catalogue.** Copper is designated by its
outside diameter, so `dn=22` is a 22 mm tube with a 20.0 mm bore. Steel's `dn=25` is a label whose
bore is 27.3 mm — *larger* than the number. So `dn=15` is a 16.1 mm bore in steel and 13.0 mm in
copper: the same script text, 19 % in bore, and the only thing that distinguishes them is which
catalogue the pipe reads -- the script's `catalog` line, or the pipe's own `material=` (`D-128`).
`PipeSpec.DesignationBasis` states it per series. The one non-integer size, 88.9 mm, is `dn=89`, as
Finnish listings print it.
