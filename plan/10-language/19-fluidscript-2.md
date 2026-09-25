---
id: 19-fluidscript-2
title: FluidScript 2
tier: 10-language
status: draft
owns: [language 2 grammar, language 2 statement set, block structure, port inference by flow direction, cases and drivers, controller declaration, run block, translation from language 2 to the binder]
depends_on: [01-vision-and-scope, 06-decision-log, 11-language-overview, 12-grammar, 13-type-and-unit-system, 14-expressions-and-references, 15-semantic-model, 16-diagnostics, 17-formatting-and-round-trip, 18-script-compatibility]
traces_to: [R-01, R-02, R-03, R-04, R-05, R-06, R-12, R-13, R-46, R-49]
open_questions: 6
last_review_pass: 0
---

# FluidScript 2

## Purpose

Language 1 grew statement by statement, each one decided on its own, and a review of the whole
(2026-09-25, five reviewers over every `.fluid` file in the repository) found the result hard to read
as a whole: one word with several meanings chosen by other lines, a control loop spread over four
statements and an invented node, positional scenario lists, and a file whose solve mode, drawing and
study settings are scattered through its model. The user asked what the language would be if it were
written from scratch, and settled it over one conversation. This document is that language.

Language 2 is a **second major beside the first, not a replacement of it** (`D-164`). The version
line selects it before anything parses (`18`, `D-27`); language 1 files keep their own parser and
their own meaning, and language 1 stays the current major until language 2 is complete. The two
share everything after binding: the registry, sizing, the solver, the layout, the model contract.

What language 2 keeps from language 1, deliberately: one statement per line, `NAME kind` with the tag
first, named parameters, omission meaning "size it" (`D-02`), chains `A - B - C`, globally unique
names (`D-41`), typed units and the temperature/temperature-difference split, the lossless printer,
`#` comments.

What it changes, in one line each (the decisions are `D-165`–`D-169`):

- A file is **model, then study**: a `project` block, drivers, curves, circuits, runs.
- A statement may be **one line or a block**: a head ending in `:` and indented `name = value` lines.
- `name = value` takes **spaces freely**; there are **no commas**.
- A circuit is a **block with a quoted title** and its own `fluid` and `number`.
- There is **no `connections` section**: components and links share the circuit's body.
- **Ports are inferred from flow direction**, and written only to be explicit.
- A pipe's length and DN sit **at the end of its link**, with no `=`.
- **Cases** replace scenarios; a value that varies per case is a **driver** (`let`) or a bracket list;
  a curve names its driver; `design` is gone.
- A **controller is one declaration** with its type, its binding and its tuning.
- A **run** is a block that says where it starts, how long it runs and what happens when.

## Responsibilities

**Owns.** The language 2 grammar and statement set; blocks and their indentation; the port-inference
rule; cases, drivers and how a curve composes with them; the controller declaration's syntax; the run
block's syntax; the translation from a language 2 syntax tree to the statements the binder reads; the
`FS18xx` codes.

**Does not own.** The unit table and dimensional algebra (`13`, changed only where this document says
so); expression evaluation (`14`); binding and inference (`15`, which gains the rules listed in
*Translation* below); the codes of every other range (`16`); the printer's invariants, which apply to
language 2 unchanged (`17`); version selection and migration (`18`); what a controller does at run time
(`34`); what a run integrates (`33`).

## Contracts

### The reference script

Every rule below is measured against this file. It is a district-heating substation: an open primary
through a pressure-control valve and a plate exchanger, and a closed heating secondary with a mixing
valve held on a weather-compensated supply temperature. It has two cases and one run.

```fluidscript lang=2
fluidscript 2

project "Substation 12":
  cases   = [winter, mild]
  catalog = steel_en10255@2026.1
  show    = temperature
  style:
    colour = "#2f6f9f"
    width  = 2

let outdoor = [-26, 5] C

curve district_supply: outdoor
  -26   85
   18   65

curve heat_demand: outdoor
  -26   150
   18     0

curve supply_temp: outdoor
  -26   60
   18   30

curve weather_jan: time
  2026-01-15 06:00   -18
  2026-01-15 12:00    -9

circuit "District primary":
  fluid  = water
  number = 100
  style:
    colour = crimson

  NPS  inlet      t = district_supply   p = 600 kPa
  NPR  outlet     p = 350 kPa
  PCV  valve
  HX1  exchanger:
    primary.out.t   = 45 C              # the network's required return
    secondary.in.t  = 40 C
    secondary.out.t = 60 C

  NPS - PCV
  PCV - HX1                  12 m  DN25
  HX1 - NPR

circuit "Heating":
  fluid  = water
  number = 200

  SP   pump
  TV1  valve3      stroke = 90 s
  TE1  temperature_sensor
  RAD  radiator    power = heat_demand
  TC1  controller:
    type     = PI
    moves    = TV1
    reads    = TE1
    setpoint = supply_temp
    band     = 20 K
    ti       = 120 s

  HX1 - TV1                  30 m  DN32
  TV1 - SP - TE1 - RAD - NR
  NR - TV1
  NR - HX1                   30 m  DN32

run "Cold morning":
  from     = winter
  start    = 2026-01-15 06:00
  duration = 2 h
  frame    = 10 s

  outdoor  = weather_jan
  at   10 min       RAD.power    = 100 kW
  over 30..40 min   NPS.t        = 85..75 C
  at   1 h          TC1.setpoint = 55 C
```

`band = 20 K` reads `K` as a temperature difference (`D-172`); language 1 writes it `20 dK`.

### Lines, blocks and names

**One statement per line, or a block.** A **block head** is a `project`, `circuit` or `run` line or a
declaration whose last token is `:`, a `style:` line inside a project or circuit, or a `curve` header
(whose `:` separates the curve's name from its driver). Every following line indented deeper than the head belongs to it; the first non-blank,
non-comment line at the head's indentation or less ends it. Blank lines and comment lines never end a
block. Blocks nest (a component block inside a circuit block); the depth is relative, so two levels is
the most any script needs.

**Indentation is counted in characters, and a block's lines agree.** Every line of one block body is
indented alike; a body mixing tabs and spaces, or a line indented between two levels, is `FS1801`
reported on that line, and the line is read as belonging to the nearer level. **A curve's rows are the
exception**: they are a table whose columns the user aligns (`  -26   85` over `   18   65`, as in the
reference script), so a row needs only to be deeper than its header. Nothing else about
whitespace means anything. The formatter indents a body two spaces; the printer keeps what was typed
(`17`).

**`name = value`, spaces free, no commas.** A value runs to the next `name =` on the same line or to
the end of the line, so a value may contain spaces and operators: `power = Q * 1.1   dt = 20 K`. Several
pairs may share a line inside a block. Commas separate list items (`[winter, mild]`) and nothing else.

**Names** are letters, digits and underscores, and may start with a digit (`3WV`), as in language 1.
A quoted string is a title (`"District primary"`) and is never a reference.

**Comments** start with `#` outside quotes, as in language 1.

**Statement words** open statements and are recognised by their position at a line's start, not by the
lexer: `fluidscript`, `project`, `let`, `curve`, `circuit`, `run`; inside a run body, `at` and `over`. A
component may therefore not be named one of the six — `run pump` is a run head that fails — and that is
`FS1004`, as a reserved word used as a name is in language 1. The lexer reserves nothing, which keeps
language 1's reserved-word table, and everything generated from it, unchanged. `time` is the one
built-in driver name. Kinds and parameters are names the binder checks against the registry, so a new
kind or parameter needs no grammar change. A name binds **only by its exact spelling** (`D-170`): case
and underscores are normalised as `D-15`'s first stage does, curated aliases resolve as its second stage
does, and a merely similar spelling feeds the error's suggestion and never binds.

**Classifying a line** reads its first token and, when that is a name, the qualified name it starts:
`=` after it makes a setting or a parameter line, `-` a connection, another name a declaration. That is
more than language 1's one token of lookahead (`11` invariant 7) and bounded the same way — by one
line, never by the lines around it — and the enclosing block narrows it further: a declaration block's
body holds only parameter lines.

### Values, units, lists and ranges

A unit follows its number, with or without a space: `600 kPa`, `12 m`, `30kW`. Units are a closed
vocabulary (`13`), recognised only immediately after a number. Language 2 removes the two symbols that
collide with names a script writes most: **`in`** (inch) and **`t`** (tonne). `13`'s rule that a unit
is never recognised before `=`, `[` or a `.` that starts a word **stays**, and looks past spaces,
because language 2 lets `=` stand apart: `h` is an hour and an enthalpy, so in `flow = 5 h = 2000` the
`h` is the next parameter. Language 1 reads `p = 300 t = 6` as three hundred tonnes (the review measured
it); language 2 does not.

A bare number takes the canonical unit of the dimension it lands in, as in language 1 (`D-14`, `13`).

**`K` is a temperature difference** (`D-172`): `band = 20 K`, `dt = 5 K`, as engineers write them. `C` and `°C`
are absolute temperatures, and `dK` and `dC` remain accepted as differences. An absolute temperature in kelvin is
not writable; `300 K` on a temperature is a dimension error whose fix is `°C`. A compound unit that contains a `K`
(`kJ/(kg*K)`) is its own spelling and is unchanged. The reading is fixed per language by the version line, so a
unit still means the same thing wherever it stands (`D-26`'s property, kept).

**Names are case-insensitive** where the registry owns them — kinds, parameters, properties: `Kp`, `KP` and `kp`
bind alike, and the printer keeps what was written. That is `D-15`'s first stage, which `D-170` keeps. **Corrected 2026-09-25 (package 3a):** this
paragraph first said the binder already did so for parameters, from reading `NameResolution.Match`, which does
normalise; running it showed that `ResolveParameter` discarded an exact normalised hit, so `HEAD=5` was `FS1503`
("a pump has no 'HEAD'") in language 1. Fixed for both languages, as `D-15` states it. Kinds were already
case-insensitive; a property named in a reference (`N2.T`) is not yet measured. Component names stay exact, case
included: `PU1` and `pu1` are two components.

**A list** is `[a, b, …]`, one value per case in the order `cases` names them; a unit after the closing
bracket applies to every item: `t = [85, 70] C`. **A range** is `a..b`, and a trailing unit applies to
both ends: `30..40 min`, `85..75 C`.

**A date** is written unquoted, `2026-01-15` or `2026-01-15 06:00` or `2026-01-15 06:00:30`: four
digits, a hyphen, two, a hyphen, two, optionally a time. Language 1 needs quotes because `2026-01-15`
is arithmetic there; no one means 2026 − 1 − 15, so language 2 lexes the shape as a date. A clock time
`06:30` is valid in a run's events once the run states `start`.

### Statements

| First token | Statement | Block? |
|---|---|---|
| `fluidscript` | The version line, first non-trivia line (`18`) | no |
| `project` | The project block: title and study settings | yes |
| `let` | A named value, or a driver when its value is a list | no |
| `curve` | A curve: header, then rows | rows indented |
| `style` | A style, inside a project or circuit block | yes |
| `circuit` | A circuit block | yes |
| `run` | A run block | yes |
| a name, then a kind | A component declaration | optional |
| a name, then `-` | A connection line | no |
| a name, then `=` | A setting of the enclosing block, or an override in a run | no |
| `at`, `over` | An event, inside a run only | no |

A statement that belongs inside a block written outside it — `fluid = water` at the top level, an event
outside a run — is `FS1802`, with the block it belongs in.

### The project block

```fluidscript lang=2
project "Substation 12":
  cases   = [winter, mild]
  catalog = steel_en10255@2026.1
```

The title is quoted. `cases` names the operating cases every list is read against; a file with no
`cases` has one case, and no list. `catalog` pins the pipe catalogue as language 1's `catalog` line does
(`18`); `ScriptCompatibility` reads it from the text before parsing, so its pattern is extended to this
form. The project block also holds the presentation (below, `D-171`). Nothing else goes in it: the solve
mode belongs to a run (`D-169`), a circuit's fluid to the circuit (`D-165`).

### Presentation

Presentation is declared once, **in the project block**, and a circuit may override it (`D-171`):

```fluidscript lang=2
project "Substation 12":
  show    = temperature
  scale   = 20..90 C
  spacing = 1.2
  style:
    colour = "#2f6f9f"
    width  = 2
    corner = fillet
    line   = solid

circuit "District primary":
  style:
    colour = crimson
```

| Setting | Where | Meaning |
|---|---|---|
| `show` | project | The property the diagram colours by, one or a list (`57`) |
| `scale` | project | The colour scale's range for the first property shown |
| `spacing` | project | The layout's spacing factor (`D-37`, `28`) |
| `style:` | project, circuit | `colour` (a colour name or a quoted hex, since `#` starts a comment), `width` (pixels), `corner` (`sharp`, `fillet`), `line` (`solid`, `dashed`, `dotted`, `dashdot`) |

A circuit's `style:` overrides only the keys it states; the rest come from the project's. A style is
never applied by position, as language 1's `style` line is (to what follows it). Named styles and a
component's own style are not in language 2's first version.

### Drivers and cases

**A driver is a `let` whose value varies per case** (`D-167`):

```fluidscript lang=2
let outdoor = [-26, 5] C
```

It is an ordinary named value of any dimension — an outdoor temperature, a production rate, a network
pressure. Nothing about `outdoor` is built in; a plant driven by nothing has no driver. The one built-in
driver is `time`, the run's clock.

**A curve names its driver in its header**, and its rows are bare numbers:

```fluidscript lang=2
curve heat_demand: outdoor
  -26   150
   18     0
```

The first column is read in the **driver's** unit (−26 means −26 °C because `outdoor` is a
temperature); the second takes the unit of **the parameter that uses the curve** (`D-57`, unchanged: a
curve has no dimension of its own). `extrapolated` and `format="…"` follow the driver as in language 1
(`D-60`); without `extrapolated` a driver outside the rows clamps to the end row, and the report says
so.

**A parameter is pinned to a curve by naming it**: `RAD radiator power = heat_demand`. In each case the
curve is read at that case's driver value, and the result is a stated value in that case — a
constraint, exactly as a number would be (`D-02`). In a run, a driver overridden by a curve of time
moves every curve of that driver with the clock (`D-149`'s composition, unchanged).

A value that varies per case and follows no curve is a list on the parameter itself:
`secondary.out.t = [60, 50] C`.

**There is no `design`.** Sizing already covers every case (`D-143`: each size is taken from the case
that demands most, and every case is checked against it); which case the canvas shows is chosen in the
interface; which case a run starts from is the run's `from`.

### Circuits

```fluidscript lang=2
circuit "Heating":
  fluid  = water
  number = 200

  SP   pump
  # … the other declarations
  TV1 - SP - TE1 - RAD - NR
```

The title is quoted and is not a reference; it is the circuit's name in the model, and a circuit written
without one is named `circuit 1`, `circuit 2`, … in file order. Settings: `fluid` (the substance, required once per circuit
unless every circuit shares one), `number` (the tag prefix, `D-34`; resolved automatically when absent,
as in language 1), and `role` (the circuit's role for the drawing, `D-35`; language 1 derives it from the
circuit's name, which a quoted title cannot carry). Declarations and connection lines follow in any
order. Components are named globally (`D-41`), so a line in one circuit may name a component declared
in another; that is how circuits are joined, and there is no attachment statement (`D-166`).

### Declarations

```fluidscript lang=2
circuit "Heating":
  RAD  radiator  power = heat_demand            # one line

  HX1  exchanger:                               # a block
    primary.out.t   = 45 C
    secondary.in.t  = 40 C
    secondary.out.t = 60 C
```

`NAME kind`, then parameters on the line, or `:` and parameters on indented lines. The two forms are one
grammar; the printer keeps whichever was written, and the formatter never converts between them.

**Kinds and parameters are the registry's** (`22`, `15`), shared with language 1, with these additions:
`valve3` is an alias of `three_way_valve`; `temperature_sensor`, `pressure_sensor` and `flow_sensor` are
aliases of the sensor kinds. A two-sided exchanger's sides are **`primary` and `secondary`**:
`primary.in.t`, `HX1.secondary.out`. `primary` is side 1 and `secondary` side 2 of language 1's
`in`/`in[2]`; which side is primary is decided by the circuit (below). Port families that really are
families keep brackets: `layer[3].t`, `in[2].level` on a tank. The wider vocabulary review — `duty`,
`rise`, `kvs`, direction taken from the kind — is open question 2.

### Connections

**A chain reads in the direction of flow.** `A - B - C`: water leaves `A`, passes `B` and enters `C`.
An endpoint no declaration names is a node (`15`'s rule I1, unchanged).

**Ports are inferred from flow roles, and written only to be explicit** (`D-166`). After every line of
the file is read:

1. **A two-port component** (pump, valve, pipe, one-sided exchanger): the connection flowing in is its
   inlet, the one flowing out its outlet.
2. **A three-way valve** counts its connections. Two in and one out is a **mixing** valve: the out is
   `ab`, and the ins are `a` and `b` in the order written — the first written is `a`, the control path.
   One in and two out is **diverting**: the in is `ab`, the outs `a` and `b` in order. `mixing_valve` and
   `diverting_valve` remain as kinds that **assert** the function; a connection count that disagrees with
   the asserted function is `FS1805`.
3. **A two-sided exchanger**: the side wired inside the circuit that declares it is **primary**; the side
   wired from any other circuit is **secondary**. When both sides are wired in one circuit, the first
   pass written is primary. A pass `S - HX1 - R` pairs its inlet and outlet; passes on separate lines pair
   in the order written.
4. **A tank's** inflows take `in`, `in[2]`, … and its outflows `out`, `out[2]`, … in the order written.
5. **An explicit port always wins**: `HX1.secondary.out - TV1.a`.
6. **What the rule cannot settle is an error, never a guess**: a valve with three inflows, a side with two
   inlets — `FS1804`, naming the ports to write.

Where the rule chose between ports it says so once, as information (`FS1815`: *'TV1' is wired as a mixing
valve: a from HX1, ab to SP, b from NR.*). That covers every three-way valve, an exchanger with both sides
wired by the rule, and a tank with more than one stream on one side. A two-port's inflow and outflow have
one reading, and reporting them would put a line under every pump. The canvas labels the ports. When the
canvas writes a connection to a component with more than two ports, it writes the port explicitly:
inference is for what people type.

**What the binder receives is an explicit port, so an inferred port counts as stated.** In language 1 an
unwritten port is a guess the binder may revise (`D-88`'s `PortStated`); in language 2 the rule is the
meaning (rule 2 makes the first inflow `a`, the path the controller moves), so there is nothing left to
revise. A chain reaches the binder as one connection per link, because a component in the middle of
`A - B - C` takes a different port on each side and one language 1 endpoint cannot name two.

**A sensor may sit in a chain**: `TV1 - SP - TE1 - RAD`. The binder lowers it to a node between `SP` and
`RAD` with the sensor observing it (`D-166`, amending `D-61`, whose objection — the sensor's identity
equations in the flow path — does not arise once it is lowered to a node). A sensor may still be placed
on a named node with `at`; placed both ways it is `FS1814`, and the chain's placement is kept. The node is named
after the sensor, `TE1_node` (with a number appended if that name is taken), and is what the drawing shows.

**A pipe's length and DN sit at the end of its link, with no `=`**:

```fluidscript lang=2
circuit "District primary":
  PCV - HX1                  12 m  DN25
```

Each is recognised by its own form — a length has a length unit, `DN25` is a designation — so neither
needs a name. Any other pipe parameter follows as `name = value` (`roughness = 0.05 mm`, `nodes = 4`).
**Pipe properties are allowed only on a line with one link**; on a longer chain they are `FS1803`, which
asks which link is meant. Language 1 applies them to every link of the chain, so `A - B - C length=25` is
50 m of pipe there.

### Controllers

A controller is **one declaration** holding its type, what it moves, what it reads, its setpoint and its
tuning (`D-168`):

```fluidscript lang=2
circuit "Heating":
  TC1 controller:
    type     = PI
    moves    = TV1
    reads    = TE1
    setpoint = supply_temp
    band     = 20 K
    ti       = 120 s
    output   = 10..100 %
```

| Parameter | Meaning |
|---|---|
| `type` | `P`, `PI`, `PID`, `onoff` or `curve`. Absent means `PI`. |
| `moves` | The actuator: a component, meaning its kind's one actuated parameter (`D-61`: position for a valve, speed for a pump), or a qualified parameter (`BLR.power`) |
| `reads` | What is measured: a sensor, a node's property, a driver, or `time` (a `curve` controller only) |
| `setpoint` | The target, in the measurement's unit: a number, a curve, a driver or a list |
| `band` | The proportional band, in the measurement's unit: the error over which the output travels its whole range. `kp = range / band`. |
| `kp` | The gain, as an alternative to `band`; stating both is `FS1809` |
| `ti`, `td` | Integral and derivative times |
| `output` | The output limits as a range in the actuator's unit; absent, the actuator's full range |
| `action` | `direct` or `reverse`, stated only as a check: the direction is measured from the plant, and a stated action that contradicts it is an error at solve time (`34`) |
| `differential` | The switching differential of an `onoff` controller |
| `curve` | The characteristic of a `curve` controller: output as a function of the reading |

| `type` | Needs | In the design solve | In a run |
|---|---|---|---|
| `P` | `band` or `kp` | Holds the setpoint | Settles with an offset; the report states it |
| `PI` | as `P`, and `ti` | Holds the setpoint | Drives the actuator (`34`) |
| `PID` | as `PI`, and `td` | Holds the setpoint | As `PI`, derivative on the measurement |
| `onoff` | `differential` | Holds nothing; the actuator sits at its "on" value | Cycles between the ends of `output` |
| `curve` | `curve` | The actuator takes the curve's value at the case's reading | Follows the reading, open loop |

A parameter that does not belong to the stated type — `td` on a `PI`, `differential` on a `PI`, `band` on
an `onoff` — is `FS1808`. Tuning left out is estimated when a run starts (`34`; the estimation rule is
open question 5). Until P6.3 builds them, the types other than `PI` bind and are reported `FS1810` —
"not yet run by the solver" — and are never run as `PI` in silence.

**An actuator's speed is the actuator's**: `TV1 valve3 stroke = 90 s`. It limits the valve however it is
moved, by a controller or by an event.

### Runs

A run says what happens to the plant in time; the model says what the plant is (`D-169`). A file may hold
several; the interface plays the one chosen.

```fluidscript lang=2
run "Cold morning":
  from     = winter
  start    = 2026-01-15 06:00
  duration = 2 h
  frame    = 10 s

  outdoor  = weather_jan
  at   10 min       RAD.power    = 100 kW
  over 30..40 min   NPS.t        = 85..75 C
  at   1 h          TC1.setpoint = 55 C
```

| Setting | Meaning | Absent |
|---|---|---|
| `from` | The case whose steady solve is the starting state (`D-141`) | The first case |
| `start` | Where the clock sits on curves of time (`D-149`) | A run that reads a curve of time needs it (`FS1546`) |
| `duration` | Simulated time | 10 min (`33`'s horizon default) |
| `frame` | Simulated time between kept states | 1 s (`33`) |
| `steady` | Circuits held quasi-steady in this run: `steady = ["District primary"]` | Every circuit is dynamic |

**An override** is `name = value` with no `at`: it holds from t = 0 for this run only and changes nothing
outside it. Overriding an input at t = 0 means the run begins by moving from the case's steady state to
the new condition, which is what the plant would do.

**An event** is a step or a ramp:

- `at T  target = value` — a step at T.
- `over T1..T2  target = v1..v2` — a ramp; **both ends are written**. A single value after `over` is
  `FS1807` ("a ramp needs both ends; for a step, write `at`"). Language 1 reads it as a step at the span's
  end.

An event replaces whatever drove its target: after `at 10 min RAD.power = 100 kW` the duty stops
following `heat_demand`. A time is a duration from t = 0 (`10 min`, `1 h`) or, with `start` stated, a
clock time (`06:30`). What an event may target is `33`'s business, and some targets need solver work
before they run — a boundary's state is `S-77`, a pump switched off is `S-76`; the syntax accepts them
from the start and the solver's refusal is its own diagnostic.

What a run does with a stated value is `33`'s table, unchanged: sizes are frozen, inputs are held until an
override or event moves them, design targets are released to their controllers.

Solver numerics — step size, tolerances — are not written in a script.

### Translation to the binder

Language 2 has its own syntax tree, which the printer prints and the editor reads. **The binder reads
the statements it already reads**: a translation step turns the language 2 tree into them, and every
span in them points into the language 2 text, so every diagnostic lands on what the user wrote.

| Language 2 | The binder receives |
|---|---|
| `circuit "T":` with `fluid`, `number`, `role` | A circuit header with number and role, and a fluid line |
| A declaration, either form | A component declaration with its parameters |
| `primary.*`, `secondary.*` | `in`/`out` and `in[2]`/`out[2]` |
| A chain with inferred ports | One connection per link, every port explicit |
| A sensor in a chain | A node in the chain and the sensor placed `at` it |
| `12 m DN25` at a link's end | `length=12 dn=25` on that link |
| A controller block | A controller declaration and a control binding |
| `cases` | The scenario list, with the first case as the operating case `design` names |
| A run | The run settings, the per-circuit mode, the start, and the events |
| `show`, `scale`, `spacing`, `style:` | The show directive, the spacing, and a style per circuit with the project's keys under the circuit's |

**What the binder must newly learn**, because language 1 cannot say it:

1. A `let` whose value is a list, varying per case (language 1: `FS1104`).
2. A curve whose driver is a `let`, read at each case's driver value — what `D-143` promised and
   language 1 cannot write.
3. Controller types other than `PI`, and `band`, `ti`, `td`, `output`, `action`, `differential`, `curve`
   on a controller.
4. A driver overridden by a curve of time for one run.
5. A clock-time event.

Port inference is done by the translation, not by the binder: the translation sees every line and hands
the binder explicit ports, so language 1's order-based assignment is untouched.

### Diagnostics

Codes carry over where their meaning holds. A message that quotes language 1 syntax gets a language 2
wording (`FS1531` names `moves =`, not `control … with`). A code whose cause cannot be written in
language 2 is never raised there. The full audit of all 186 codes is package 4 of `P6.11`. The new
range is **`FS18xx`**, owned by this document:

| Code | Severity | When |
|---|---|---|
| `FS1801` | Error | A block body's lines are indented unlike each other, or mix tabs and spaces |
| `FS1802` | Error | A statement outside the block it belongs in: `fluid = water` at the top level, an event outside a run |
| `FS1803` | Error | Pipe properties on a line with more than one link |
| `FS1804` | Error | A port the inference rule cannot settle; the message lists the ports to write |
| `FS1805` | Error | A `mixing_valve` or `diverting_valve` whose connections say the other function |
| `FS1806` | Error | A language 1 statement in a language 2 file, with the language 2 form as its fix. Recognised by language 1's shape, so a language 2 line starting with the same word is not caught: `connections` or `schedule` alone; `control`, `scenarios`, `design`, `project`, `circuit` or `style` followed by a name (`project dynamic`, `circuit heating`); `curve NAME DRIVER` without the colon; `fluid`, `show`, `spacing` or `catalog` followed by anything but `=`, `-` or `.`; `inlet NAME` or `outlet NAME` alone. The price is that a component may not be declared under one of these words (`design valve` reads as language 1's `design`) — this project's reasoning: that reading is far more likely to be meant |
| `FS1807` | Error | A ramp with one value |
| `FS1808` | Error | A controller parameter that its stated type does not have |
| `FS1809` | Error | Both `band` and `kp` stated |
| `FS1810` | Warning | A controller type the solver does not run yet |
| `FS1811` | Error | A curve whose driver is neither a `let` nor `time` |
| `FS1812` | Error | A block head without its `:` |
| `FS1813` | Error | A word after a pipe's link that is not a DN designation (`12 m NPS1`); the pipe keeps its length and is sized |
| `FS1814` | Error | A sensor that sits in a chain and is also placed `at` a node; the chain's placement is kept |
| `FS1815` | Info | How the rule wired a component where it chose between ports: a three-way valve, an exchanger with two sides, a tank side with more than one stream |

## Invariants

1. **`Print(Parse(x)) == x` for every language 2 input**, malformed ones included (`17`'s invariant 1,
   under the same fuzz test).
2. **One bad line damages at most its block.** A malformed line inside a block is a malformed statement
   in that block; the block and every line after it still bind.
3. **The parser does not read the registry.** Kinds and parameters are names; a new kind or parameter
   changes no grammar.
4. **Every choice between ports is reported once (`FS1815`) and drawn labelled.** No such choice is
   silent; a two-port's inlet and outlet are not a choice.
5. **Every span the binder sees points into the language 2 text.**
6. **A language 2 file and its language 1 twin bind to the same model** wherever both can say it: the
   same components, parameters, connections and ports, the same solve.
7. **Language 1 is untouched.** Its parser, binder rules and every one of its goldens stay as they are
   until a decision retires them.

## Error cases

The `FS18xx` table above, plus every language 1 code whose meaning carries over. No stage throws on
language 2 input.

## Worked example

**Per case, from the reference script's curves** (linear between rows):

| | winter (outdoor −26 °C) | mild (outdoor 5 °C) | Unit from |
|---|---|---|---|
| `NPS.t = district_supply` | 85 °C | 85 − 31 × 20/44 = **70.9 °C** | `t` |
| `RAD.power = heat_demand` | 150 kW | 150 − 31 × 150/44 = **44.3 kW** | `power` |
| `TC1.setpoint = supply_temp` | 60 °C | 60 − 31 × 30/44 = **38.9 °C** | what `TE1` reads |

**Ports inferred:**

| Line | Inferred |
|---|---|
| `PCV - HX1` (in `HX1`'s own circuit) | `HX1.primary.in` |
| `HX1 - NPR` | `HX1.primary.out` |
| `HX1 - TV1` (from another circuit) | `HX1.secondary.out`; `TV1`'s first inflow → `a` |
| `TV1 - SP - TE1 - RAD - NR` | `TV1`'s one outflow → `ab`, so `TV1` mixes; `TE1` becomes a node between `SP` and `RAD` with `TE1` placed at it |
| `NR - TV1` | `TV1`'s second inflow → `b`, the bypass |
| `NR - HX1` | `HX1.secondary.in` |

**What the binder receives for the heating circuit** (language 1 spellings, spans in the language 2
text):

```
circuit heating 200
fluid water
SP  pump
TV1 three_way_valve stroke=90 s
TE1 t_sensor at TE1_node
RAD radiator power=heat_demand
TC1 controller band=20 dK ti=120 s
connections
HX1.out[2] - TV1.a length=30 dn=32
TV1.ab - SP.in
SP.out - TE1_node
TE1_node - RAD.in
RAD.out - NR
NR - TV1.b
NR - HX1.in[2] length=30 dn=32
control actuate=TV1.position measure=TE1.t by=TC1 setpoint=supply_temp
```

`stroke` and `band` are not language 1 parameters yet; they are registry additions package 3 makes, and
the translation emits them into the records the binder reads, not as language 1 text. The listing only
shows what the records say.

## Acceptance criteria

- [ ] The reference script parses, prints back byte for byte, and binds with no error.
- [x] The printer fuzz test runs on language 2 input as it does on language 1 (package 2:
      `FluidScript2ParserTests`, every one-character deletion of the reference script and 3 000 random edits).
- [ ] A malformed line inside a block leaves the rest of the block and the file bound (invariant 2).
- [ ] Every sample in `samples/` has a language 2 twin that binds to the same model and the same solve
      (invariant 6), checked by a test that compares the two model contracts.
- [x] Port inference: each rule has a test, including a mixing and a diverting `valve3`, an exchanger
      wired across two circuits and within one, a tank, and each `FS1804` shape (slice 3b:
      `Language2TranslatorTests`; the shapes are a valve with one stream each way, a valve with three
      inflows, a third pass through an exchanger and a second inflow into a pump).
- [ ] `FS1803`: `A - B - C 12 m DN25` is refused, `A - B 12 m DN25` binds as one 12 m pipe.
- [ ] The worked example's per-case values (70.9 °C, 44.3 kW, 38.9 °C) are asserted.
- [ ] A controller of each type binds; each `FS1808` and `FS1809` shape is refused; `FS1810` is raised
      for every type the solver does not run.
- [ ] A run binds to the settings and events `33` reads; a one-valued `over` is `FS1807`.
- [ ] Every language 1 statement in a language 2 file is `FS1806` with its language 2 form.
- [ ] A file with no version line is language 1 until `P6.11`'s switch-over (`D-164`).
- [ ] The editor highlights and completes language 2 in a file whose version line says 2, and language
      1 otherwise.
- [ ] The tutorial and a reference page per language 2 statement exist, and the documentation gate
      checks them.

## Open questions

1. **Tanks.** A tank's ports and layers inside a block (`layer[3].t = 45 C`, `in[2].level = 0.8`) and
   rule 4 of port inference are written above; nothing else about tanks has been discussed.
2. **The vocabulary pass**: `duty` for heat and `power` for shaft or electrical power, `rise` on a pump
   and `dp` only ever a drop, `kvs`, and each thermal kind taking its direction from what it is (a boiler
   heats its water, a radiator cools it) instead of from a sign convention on an alias (`D-91`).
   Recommendation: a package of its own after language 2 lands, since the registry is shared by both
   languages.
3. **An `onoff` controller's switching points**: symmetric (setpoint ± differential/2) or below the
   setpoint (on at setpoint − differential, off at the setpoint). Both appear in practice and no
   standard was found; the choice is this project's and the documentation must state it.
4. **A sensor's `lag`** (a thermowell's time constant), which the derivative default below needs.
   Recommendation: add it.
5. **Default tuning.** Proposal for `34`: a bump test in the compiled model at run start (a 10 % step of
   the actuator, a first-order-plus-dead-time fit by Smith's two-point method), then Skogestad's SIMC
   rules with τc = θ — `band` from Kc = (1/k)·τ/(τc + θ), `ti` = min(τ, 8θ) but never shorter than the
   actuator's stroke time, `td` = the sensor's lag; for `onoff`, the differential that keeps the cycle at
   20 minutes or longer (3 starts an hour, the conservative end of compressor makers' 3–6); a setpoint
   left out defaults to the design solve's value at the sensor; a valve's stroke defaults to 90 s (Belimo's
   globe-valve default). This replaces `34`'s current rule, whose "half the ultimate gain" is taken from a
   steady gain and has no source. It is `34`'s to decide, with P6.3.
6. **Check-only cases** (`check = [extreme]`: solved and reported, never sized for), and a labelled list
    form (`[winter: 85, mild: 70]`) for files with many cases. Recommendation: later, neither is needed
    for the first version.
