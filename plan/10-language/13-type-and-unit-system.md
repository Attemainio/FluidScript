---
id: 13-type-and-unit-system
title: Type and unit system
tier: 10-language
status: draft
owns: [dimensions, unit symbol table, canonical script units, display units, coercion rules, Quantity type]
depends_on: [02-glossary, 06-decision-log, 11-language-overview]
traces_to: [R-04, R-45, R-48]
open_questions: 0
last_review_pass: 0
---

# Type and unit system

## Purpose

Implements `D-07` as amended by `D-14`: a bare number in the script means the **SI unit of its
dimension**, save for four stated exceptions; an explicit unit is accepted and converted; and Core
computes in SI throughout. This document owns the tables that make that unambiguous. Get it wrong and
every number in the system is wrong by a constant factor, which is the hardest class of bug to see,
because the results still look plausible.

## Responsibilities

**Owns.** The dimension set, the unit-symbol table, the canonical script unit per parameter kind, the
coercion and conversion rules, and the `Quantity` representation.

**Explicitly does not own.** Which parameters each component has
([`22-component-model`](../20-core-domain/22-component-model.md)), how expressions combine quantities
([`14-expressions-and-references`](14-expressions-and-references.md)), how units are displayed in the
UI ([`55-design-system`](../50-frontend/55-design-system.md)).

## Dimensions

The closed set. Adding one is a language change requiring a decision-log entry.

Three columns, and they are three different things — conflating any two is how a factor of 1000 gets in:

- **SI base** — what `Quantity.SiValue` holds. Never varies, never negotiable.
- **Canonical script unit** — what a *bare number* in the script means (`D-07`, `D-14`). Equal to the
  SI base unit except on the four rows marked **exception**.
- **Display unit** — what the UI, hover, and `/docs` print by default
  ([`55-design-system`](../50-frontend/55-design-system.md) owns the formatting; this table owns the
  choice). Independent of the canonical script unit: the wire may carry J/kg while the tooltip says
  kJ/kg, because a reader and a parser want different things.

| Dimension | SI base | Canonical script unit | Display unit | Notes |
|---|---|---|---|---|
| `Dimensionless` | — | — | — | Ratios, efficiencies, counts. Percent is a unit of this dimension. |
| `Length` | m | **m** | mm below 1 m, else m | `length=45` is 45 metres. Write `dn=50` or `45 mm` for millimetres. |
| `Temperature` | K | **°C** — *exception* | °C | Nobody writes `in=293.15`. **See the offset rule below.** |
| `TemperatureDelta` | K | dK | K | A *separate dimension* from `Temperature`; explicit syntax is `dK` or `dC` (`D-26`). |
| `Pressure` | Pa | **kPa gauge** — *exception* | kPa gauge | Bare, `kPa`, `kPag`, `bar`, and `barg` are gauge; `kPaa`/`bara` are absolute (`D-26`). |
| `PressureDelta` | Pa | **kPa** — *exception* | kPa | Same exception, same reason. Separate dimension, as for temperature. |
| `Power` | W | **kW** — *exception* | kW | `power=30` is the brief's own example and `R-04` states it. |
| `Energy` | J | J | kWh | |
| `MassFlow` | kg/s | kg/s | kg/s | |
| `VolumeFlow` | m³/s | m³/s | l/s | Litres per second is the working *display* unit in hydronics; no parameter takes a bare volume flow, so the canonical unit is rarely exercised. |
| `Mass` | kg | kg | kg | |
| `Time` | s | s | s | |
| `Velocity` | m/s | m/s | m/s | |
| `Density` | kg/m³ | kg/m³ | kg/m³ | |
| `SpecificHeat` | J/(kg·K) | J/(kg·K) | kJ/(kg·K) | |
| `Enthalpy` | J/kg | J/kg | kJ/kg | |
| `Area` | m² | m² | m² | |
| `Volume` | m³ | **dm³** — *exception* | l | `volume=300` is 300 litres for hydronic storage (`D-32`). |
| `Kv` | m³/h @ 1 bar | — | — | A defined coefficient, not a derived unit. Its own dimension so it cannot be added to a volume flow. |
| `Head` | m | m | m | Metres **of the pumped fluid**. Not interchangeable with pressure without ρ and g. |
| `NominalDiameter` | — | — | DN | A dimensionless **designation**, not a length ([`02-glossary`](../00-foundation/02-glossary.md)). Its own dimension so it cannot be assigned to or from a `Length`. |
| `Pixels` | px | px | px | Presentation only. Never crosses into physics. |

### The canonical unit is a property of the dimension, not of the parameter

One dimension, one canonical script unit, everywhere. A component may not declare that *its* `length`
means metres while another's means millimetres — the reader would have to know the parameter to read
the number, which defeats the point of a canonical unit
([`22-component-model`](../20-core-domain/22-component-model.md)'s parameter tables state the
dimension and inherit the unit from here; they never restate or override it).

The four exceptions are exceptions to **SI**, not to that rule: `°C`, `kPa`, `kW`, and `dm³` each apply to
every parameter of their dimension, uniformly. Each is listed because a stated exception a reader can
memorise in one line is cheaper than an unstated one they discover from a wrong answer.

**Why these four and not others.** `°C` because absolute kelvin in a design script is unusable and
`13`'s own `20 °C + 30 dK` rule already makes temperature affine and special. `kW` because `R-04` states
it and the brief's flagship line is `power=30`. `kPa` because hydronic circuits are specified in kPa
and bar, and a node boundary written `p=300000` is the kind of number a reader stops trusting. `dm³`
because the promoted tank requirement explicitly defines `volume=300` as a 300-litre vessel (`D-32`),
and the same dimension-wide rule must also govern readable pipe-volume properties.
Everything else takes the SI unit, because a bare number's meaning should be guessable by someone who
has never read this table, and "the SI unit" is the only rule with that property.

### Temperature and TemperatureDelta are different dimensions

This is the single most important entry in the document. `20 °C + 30 dK = 50 °C` is correct;
`20 °C + 30 °C` is meaningless. Modelling both as one dimension makes the second expression compile and
produce 293.15 + 303.15 = 596.3 K, silently.

Rules:

| Expression | Result | Rule |
|---|---|---|
| `Temperature + TemperatureDelta` | `Temperature` | Offset applied once |
| `Temperature − TemperatureDelta` | `Temperature` | The same rule mirrored. `let Tret = Tflow - dTdesign` is the line a designer writes first, and omitting it from this table made it an error. |
| `Temperature − Temperature` | `TemperatureDelta` | The only way to produce a delta from two absolutes |
| `TemperatureDelta − Temperature` | **error `FS1302`** | An absolute subtracted from a difference has no meaning |
| `Temperature + Temperature` | **error `FS1302`** | |
| `TemperatureDelta ± TemperatureDelta` | `TemperatureDelta` | |
| `TemperatureDelta × Dimensionless` | `TemperatureDelta` | |
| `TemperatureDelta ÷ Dimensionless` | `TemperatureDelta` | |
| `−TemperatureDelta` (unary) | `TemperatureDelta` | |
| `−Temperature` (unary) | **error `FS1302`** | Negating an absolute temperature is never meant |

**Both binary `Temperature` rows are asymmetric on purpose.** `Temperature − TemperatureDelta` is a
temperature and `TemperatureDelta − Temperature` is an error, because subtraction is not commutative
and a type system that pretended otherwise would accept `30 K - 20 C` and produce something. The same
asymmetry applies to `Pressure` / `PressureDelta`.

`K`, `C`, and `°C` are absolute temperatures. A temperature difference is written `dK` or `dC`;
`let dT = 30 dK` therefore has a type without inspecting where `dT` is later used. A difference
written as `30 K` is an error with a fix to `30 dK`, not a context-dependent interpretation (`D-26`).

## Unit symbol table

Case-sensitive where SI is (`K` vs `k`, `mm` vs `Mm`), case-insensitive for multi-letter non-SI names.

| Dimension | Accepted symbols |
|---|---|
| Length | `m`, `mm`, `cm`, `dm`, `km`, `in`, `ft` |
| Temperature | `C`, `°C`, `F`, `°F`, `K` |
| TemperatureDelta | `dK`, `dC` |
| Pressure | `Pa`, `kPa`, `kPag`, `MPa`, `bar`, `barg`, `mbar`, `psi`, `mH2O`, `mmH2O` (gauge); `Paa`, `kPaa`, `MPaa`, `bara`, `mbara`, `psia` (absolute) |
| PressureDelta | `Pa`, `kPa`, `MPa`, `bar`, `mbar`, `psi`, `mH2O`, `mmH2O` |
| Power | `W`, `kW`, `MW`, `hp` |
| Energy | `J`, `kJ`, `MJ`, `Wh`, `kWh`, `MWh` |
| MassFlow | `kg/s`, `kg/h`, `t/h` |
| VolumeFlow | `m3/s`, `m3/h`, `l/s`, `l/min`, `l/h` |
| Time | `s`, `min`, `h`, `d`, `ms` |
| Velocity | `m/s`, `km/h` |
| Mass | `kg`, `g`, `t` |
| Density | `kg/m3` |
| SpecificHeat | `J/(kg*K)`, `kJ/(kg*K)` |
| Enthalpy | `J/kg`, `kJ/kg` |
| Area | `m2`, `mm2`, `cm2` |
| Volume | `m3`, `l`, `dm3`, `ml` |
| Head | *(none: `head=15` is a bare number)* — see below (`D-50`) |
| Kv | *(none: `kv=1.6` is a bare number)* |
| Dimensionless | `%` (`-` is not a symbol — `D-50`) |
| Pixels | `px` |

**`Head` has no bare `m` symbol, and that is a correction to an earlier draft of this table.** Head and
Length are separate dimensions with the same SI base unit, so letting both accept `m` would violate
invariant 3 twice over — one symbol, two dimensions, with no positional rule to separate them, and
`length=25` on a pipe next to `head=15` on a pump would be indistinguishable to the lexer. A bare
number in a `head=` parameter means metres of the pumped fluid, from the parameter's declared dimension
(`D-07`). Length keeps `m`.

**`Head` takes no symbol at all** (`D-50`). An earlier draft gave it `mH2O`, which `Pressure` and
`PressureDelta` already own, and that is the same violation one row further along: head is metres of
the *pumped fluid* while `mH2O` is metres of *water column*, a pressure of 9806.65 Pa per metre. The
two coincide only for water, so `head=15 mH2O` in a glycol circuit is wrong by the density ratio and
entirely plausible on the diagram. `Head` is therefore bare-only, as `Kv` is.

**`Dimensionless` keeps `%` and does not accept `-`** (`D-50`), and `%` is a unit symbol only —
the language has no modulo operator (`D-51`). A bare number is already
dimensionless, so the symbol carried nothing, and it collided with subtraction: under the
whitespace rule below, the `-` in `let x = 5 - 3` follows a number and is not followed by `=`, so it
would lex as a unit and strand the `3`.

### A unit symbol may be separated from its number by a space

`30K` and `30 K` are the same quantity. This is stated here because three places in the tree
previously disagreed: [`12-grammar`](12-grammar.md)'s EBNF allowed the space, its word-classification
rules could not see across one, and this document asserted that a space-separated `C` was an
identifier — while every `let` example in the tree writes `30 K` and `4.18 kJ/(kg*K)` with a space.

**The rule, in full:** a known unit symbol is recognised when it follows a number token, optionally
separated by horizontal whitespace, **and is not immediately followed by `=`**.

The `=` clause is the whole reason the rule can be permissive. `in` is a unit (inch) *and* a parameter
name in the brief's own line:

```fluidscript
HE1 heat_exchanger power=30 in=20 out=50
```

Without the clause, `30 in` lexes as thirty inches and the brief's flagship example silently becomes
nonsense. With it, `in` is followed by `=` so it is a parameter name, and `30` stays a bare number that
takes `power`'s canonical unit. The same protects `t` (tonne / a node's temperature) in `flow=5 t=6`.

One token of lookahead, which [`12-grammar`](12-grammar.md)'s invariant 5 permits (it forbids
*unbounded* lookahead). The alternative — forbidding the space entirely — is simpler to lex and costs
`4.18 kJ/(kg*K)`, `45 mm` and `30 K`, which is most of what makes a `let` block readable.

**`C` for Celsius without the degree sign** is required — nobody types `°`. `20C` and `20 C` are both
quantities; `C` alone, with no number before it, is an identifier.

### World units are dimensionless, and `spacing` is the reason to say so

`spacing 20` (`D-37`) takes a bare `number`, never a `quantity`. World units are the canvas
coordinate system ([`02-glossary`](../00-foundation/02-glossary.md)); they are not metres, not
millimetres, and not pixels, so no symbol in the table above denotes one and `spacing 20 mm` is
`FS1113`.

**The temptation is to accept `mm` and treat the canvas as a drawing at some scale**, and it must be
refused. A P&I diagram is a schematic: the distance between a pump and a valve on the page has no
relationship to the pipe length between them, and a unit that implied otherwise would invite a user
to read dimensions off the diagram. It also keeps `spacing` clear of the dimensional algebra
entirely — it is a presentation value carried through Core untouched, and giving it a dimension would
put it into an expression system that has no business evaluating it.

This is the same reasoning that makes `DN` its own dimension rather than a `Length`: a number that
looks dimensional and is not causes a specific, silent class of error, and the type system is where
that gets stopped.

## Canonical parameter units

`D-07`'s core table: what a bare number means. Every parameter of every component maps to a dimension,
and the dimension's canonical script unit from the table above is what a bare number is interpreted as.
[`22-component-model`](../20-core-domain/22-component-model.md) declares the mapping per parameter;
this document owns the dimension → canonical-unit half, and a parameter table that restates or
overrides a canonical unit is a review finding.

The brief's line, plus a pipe, resolves as:

| Written | Parameter's dimension | Canonical unit | Value | Stored (SI) |
|---|---|---|---|---|
| `power=30` | Power | kW *(exception)* | 30 kW | 30 000 W |
| `in=20` | Temperature | °C *(exception)* | 20 °C | 293.15 K |
| `out=50` | Temperature | °C *(exception)* | 50 °C | 323.15 K |
| `p=300` | Pressure | kPa *(exception)* | 300 kPa | 300 000 Pa |
| `length=45` | Length | m | 45 m | 45 |
| `length=45 mm` | Length | m | 45 mm | 0.045 |
| `roughness=0.045 mm` | Length | m | 0.045 mm | 4.5 × 10⁻⁵ |
| `elevation=-2.5` | Length | m | −2.5 m | −2.5 |
| `dn=50` | NominalDiameter | — | DN50 | 50 (a designation) |
| `2px` | Pixels | px | 2 px | 2 px |

**`length=45` is 45 metres, and `roughness` needs its unit written.** Under `D-14` the canonical unit
follows the dimension, so every `Length` parameter reads in metres. Pipe roughness is the one place
that costs a token — `0.045 mm` rather than `0.045` — and it is worth it: the alternative, letting one
parameter mean millimetres while its neighbour means metres, is precisely the trap that made
`length=25` ambiguous in the first place. The default is 0.045 mm and most scripts never write it.

## Dimensional algebra

The dimension list above is a set of **names**, not a closed algebra, and the difference matters as
soon as an expression multiplies two quantities.
[`14-expressions-and-references`](14-expressions-and-references.md)'s own worked example needs
`Power ÷ (SpecificHeat × TemperatureDelta) → MassFlow`, and `Q / dT` (W/K) has no name in the list at
all. A design that only permits named dimensions rejects the first; one that permits anything loses the
safety the list exists for.

**A `Quantity` carries an exponent vector over the SI base dimensions** — mass, length, time,
temperature — and `*` and `/` add and subtract those vectors. The named `Dimension` is a **label looked
up from the vector**, not the representation:

| Operation | Rule |
|---|---|
| `a * b`, `a / b` | Exponent vectors add / subtract. Always legal. |
| `a + b`, `a − b` | Legal only when the vectors are equal (plus the temperature rules above) |
| Result has a named dimension | Use the name — `W/(J/kg)` resolves to `MassFlow` |
| Result has no named dimension | Legal **inside** an expression, and carried as an unnamed quantity |
| An unnamed quantity reaching a parameter, a `let` that is read by one, or the model contract | `FS1304`, naming the derived unit it arrived with |

So `Q / dT` computes fine and only fails if the user tries to *store* it somewhere that expects a named
dimension — which is the behaviour that lets an intermediate step be un-named without letting a wrong
one through. `FS1305` is then reserved for the genuinely illegal case: adding or comparing mismatched
vectors.

**Temperature stays outside this scheme**, because it is affine rather than linear. `Temperature` and
`TemperatureDelta` share an exponent vector and are still distinct types, resolved by the table above
rather than by dimensional analysis. That exception is the price of getting `20 °C + 30 dK` right, and
it is worth it.

## The `Quantity` type

```csharp
/// <summary>A number with a dimension. The only representation of a dimensioned value that
/// crosses a public boundary in Core.</summary>
/// <remarks>
/// Stored in SI base units always. The unit a value was written in is kept only for round-tripping
/// and display; it never participates in arithmetic.
/// </remarks>
public readonly record struct Quantity
{
    /// <summary>Magnitude in the dimension's SI base unit.</summary>
    /// <value>W for <see cref="Dimension.Power"/>, K for temperature, Pa for pressure, and so on.</value>
    public double SiValue { get; init; }

    /// <summary>The dimension this quantity belongs to.</summary>
    public Dimension Dimension { get; init; }

    /// <summary>The unit the value was written in, for display and round-tripping.</summary>
    /// <value><see langword="null"/> when the source was a bare number.</value>
    public UnitSymbol? SourceUnit { get; init; }
}
```

**`double`, not `decimal`.** Thermodynamic property calls return `double`, correlations are empirical
to three or four significant figures, and the solver needs speed inside its iteration. `decimal` would
buy exactness the underlying physics does not have and cost an order of magnitude of performance.

**`SourceUnit` is presentation state living in a value type.** The alternative — a parallel map from
span to unit — is worse: it must be threaded through every stage and it desynchronises. Two quantities
with the same `SiValue` and `Dimension` but different `SourceUnit` compare **unequal** under the
generated record equality, which is a trap; `Quantity` therefore overrides `Equals` to compare
`SiValue` and `Dimension` only, and exposes `EqualsExactly` for the printer.

## Conversion and comparison

- **Conversion is exact where the factor is exact** (`kW → W` is ×1000) and correctly rounded
  otherwise. No accumulating conversions: parse converts to SI once, display converts from SI once.
- **Comparison uses a relative tolerance of 1e-9** with these SI absolute floors: dimensionless
  `1e-12`, temperature/temperature delta `1e-9 K`, pressure/pressure delta `1e-6 Pa`, mass flow
  `1e-12 kg/s`, and every other dimension `1e-12` in its SI unit. The comparison uses
  `max(relativeTolerance × max(|a|, |b|), absoluteFloor)`, because
  `0.1 + 0.2 != 0.3` reaches user-visible assertions otherwise.
- **Never compare temperatures for equality.** The API offers `IsCloseTo(other, tolerance)` and no
  `==` on `Temperature`-dimensioned quantities in test helpers.

## Timestamps

A timestamp is a lexical unit, not a quantity, and it exists only inside a `curve` section whose
driver is `time` (`D-60`). It is **not** a dimension: it never takes part in arithmetic, never carries
a unit, and converts to seconds on the SI side like everything else.

Two forms need no declaration — ISO 8601 (`2026-01-01T00:00:00`) and a bare number of Unix seconds.
Anything else is stated on the curve:

```fluidscript
curve outdoor time format="dd/MM/yyyy HH:mm:ss"
```

The format string is .NET's, **and its case carries meaning**: `MM` is the month and `mm` the minute,
`HH` is the 24-hour clock and `hh` the 12-hour. `dd/mm/yyyy hh:mm:ss` — the shape most people write
from memory — is literally day / minute / year, 12-hour : minute : second, and would parse without
complaint. The format is therefore validated when the curve binds: a string naming no month, or no
day, or using `hh` with no designator, is a diagnostic rather than a silent misparse.

Culture-inferred parsing is rejected outright and `D-60` records why with the example that settled it.
A format that depends on the reader's locale means one file means two things on two machines.

## Invariants

1. Every `Quantity` crossing a public Core boundary is in SI base units.
2. `Temperature` and `TemperatureDelta` are distinct `Dimension` values and no implicit conversion
   exists between them.
3. A unit symbol maps to one dimension, except pressure spellings shared by `Pressure` and
   `PressureDelta`, whose target parameter supplies that affine distinction. Temperature has no such
   exception: `K` is absolute and `dK` is a delta. `m` is Length and never Head, and `Head` accepts no
   symbol at all (`D-50`).
4. Converting a value to a unit and back yields the original within 1e-12 relative.
5. No `double` representing a dimensioned value appears on a public Core signature.
6. The unit symbol table is append-only across releases: removing or repurposing a symbol changes the
   meaning of existing scripts silently.
7. **The canonical script unit is a function of the dimension alone.** Two parameters of the same
   dimension interpret a bare number identically, whatever component they belong to.
8. **The canonical script unit equals the SI base unit except on the five rows marked *exception*.**
   Five rows, four distinct units: `Pressure` and `PressureDelta` both take `kPa`. Counting rows and
   counting units gives different answers and an earlier draft of this invariant said "four rows",
   which no implementation could satisfy. `dK` is not among them — it changes type, not scale.
   Adding another exception requires a decision-log entry amending `D-14`/`D-32`.
9. A unit symbol separated from its number by horizontal whitespace lexes as part of the quantity,
   unless the symbol is immediately followed by `=`.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1301` | Unknown unit symbol | Error | `'{sym}' is not a unit. Did you mean '{suggestion}'?` |
| `FS1302` | Adding two absolute temperatures (or pressures) | Error | `Cannot add two temperatures. To offset by a difference, write '{a} + {n} dK'.` |
| `FS1303` | Delta written with an absolute-temperature unit | Error | `'{n} K' is an absolute temperature. Write '{n} dK' for a difference.` |
| `FS1304` | Dimension mismatch in an assignment | Error | `'{param}' is a {expected}; '{value}' is a {actual}.` |
| `FS1305` | Dimension mismatch in an operation | Error | `Cannot {op} a {a} and a {b}.` |
| `FS1306` | Value outside a parameter's physical range | Warning | `{param} = {value} is outside the usual range ({lo}–{hi}). Check the unit.` |
| `FS1307` | Negative value for a strictly positive parameter | Error | `{param} cannot be negative.` |

`FS1306` is the one that catches the real-world failure: a user writing `power=30000` meaning watts
where kW was expected gets a warning that 30 000 kW is a large heat exchanger, rather than a plausible
diagram of a 30 MW plant. Ranges are declared per parameter in
[`22-component-model`](../20-core-domain/22-component-model.md).

## Worked example

`let dT = 30 dK` then `HE1 heat_exchanger in=20 out=20C+dT`:

| Step | Value | Dimension | Note |
|---|---|---|---|
| `30 dK` lexed | quantity, `30`, unit `dK` | TemperatureDelta | Explicit delta syntax |
| `dT` bound | `SiValue = 30`, `Dimension = TemperatureDelta` | | |
| `20` in `in=20` | number, no unit | → Temperature | Bare number takes `in`'s canonical unit, °C |
| `in` stored | `SiValue = 293.15` | Temperature | 20 + 273.15 |
| `20C` in the expression | quantity, `20`, unit `C` | Temperature | `SiValue = 293.15` |
| `20C + dT` | Temperature + TemperatureDelta | Temperature | 293.15 + 30 = **323.15 K** |
| `out` displayed | `50 °C` | | Converted from SI once, for display |

And the failing case, `out=20C+30C`: both operands are `Temperature`, so `FS1302` fires with the
message naming the fix. Nothing is computed. A single-dimension design would have returned 596.3 K and
drawn a diagram.

## Acceptance criteria

- [ ] `power=30`, `power=30 kW`, `power=30kW`, and `power=30000 W` all produce `SiValue == 30000`.
- [ ] `length=45`, `length=45 m` and `length=45m` all produce `SiValue == 45`; `length=45 mm` produces
      `0.045`. A test asserts a bare `Length` is **metres**, since the mm reading is the regression
      `D-14` exists to prevent.
- [ ] Every parameter of the same dimension resolves a bare number identically — asserted by a test
      that walks [`22-component-model`](../20-core-domain/22-component-model.md)'s registry and groups
      by dimension (invariant 7).
- [ ] Exactly five dimensions have a canonical script unit whose *conversion* differs from their SI
      base unit — `Temperature`, `Pressure`, `PressureDelta`, `Power`, `Volume` — spelling the four
      units named here (invariant 8). The test compares factor and offset, not spelling, so the `dK`
      row is correctly excluded: it changes type, not scale.
- [ ] `power=30 in=20` lexes as two parameters, **not** as thirty inches — the `=`-lookahead clause
      has a test of its own, because it is the whole safety of the whitespace rule.
- [ ] `let dT = 30 dK` and `let cp = 4.18 kJ/(kg*K)` both lex as one quantity each.
- [ ] `20C + 30dK` yields 323.15 K; `20C + 30C` yields exactly one `FS1302` and no value.
- [ ] `70C - 20dK` yields 323.15 K; `20dK - 70C` yields `FS1302`.
- [ ] Every symbol in the unit table has a round-trip test to SI and back within 1e-12 relative.
- [ ] `Quantity` equality ignores `SourceUnit`; `EqualsExactly` does not.
- [ ] A test asserts no unit symbol maps to two dimensions except the documented pressure/delta
      spellings — run over the whole table so a later addition cannot add ambiguity.
- [ ] `p=300`, `p=300 kPa`, and `p=3 bar` store 300 kPa gauge; `p=401.325 kPaa` stores the same
      hydraulic gauge value and passes 401.325 kPa absolute to the substance adapter.
- [ ] Every dimension in the dimension table has at least one accepted symbol, or is explicitly
      marked as bare-only.
- [ ] `30 kW / 10 K` evaluates without error and carries an unnamed dimension; assigning it to a
      `power=` parameter produces `FS1304` naming W/K.
- [ ] `FS1306` fires for `power=30000` on a heat exchanger and does not fire for `power=30`.

## Open questions

None. `D-26` fixes gauge/absolute pressure spellings and makes `K` absolute with explicit `dK`/`dC`
temperature differences.
