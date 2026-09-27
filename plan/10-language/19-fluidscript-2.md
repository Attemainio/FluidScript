---
id: 19-fluidscript-2
title: Language design
tier: 10-language
status: draft
owns: [language design, port inference by flow direction, cases and drivers, controller declaration, run block, reading of the syntax tree into the binder]
depends_on: [01-vision-and-scope, 06-decision-log, 11-language-overview, 12-grammar, 13-type-and-unit-system, 14-expressions-and-references, 15-semantic-model, 16-diagnostics, 17-formatting-and-round-trip, 18-script-compatibility]
traces_to: [R-01, R-02, R-03, R-04, R-05, R-06, R-12, R-13, R-46, R-49]
open_questions: 6
last_review_pass: 0
---

# Language design

## Purpose

What the language's statements mean, why the language is shaped as it is, and how the binder reads it.
The syntax — tokens, lines, blocks, every statement's form and the grammar — is
[`12`](12-grammar.md); this document is the reasoning behind it and the rules that turn a syntax tree into
what the binder binds.

The language was designed as a whole (2026-09-25, `D-165`–`D-169`), after a review of every `.fluid` file
in the repository by five reviewers, around what makes a plant file readable: one meaning per word, a
control loop in one place, operating cases named rather than positional, and a file whose solve modes,
drawing and study settings each have one home. Its shape, in one line each:

- A file is **model, then study**: a `project` block, drivers, curves, circuits, runs.
- A statement is **one line or a block**: a head ending in `:` and indented `name = value` lines.
- One statement per line, `NAME kind` with the tag first, named parameters, and omission meaning "size it"
  (`D-02`).
- `name = value` takes **spaces freely**, and there are **no commas** between settings.
- A circuit is a **block with a quoted title** and its own `fluid` and `number`; its components and the
  lines that connect them share its body, in any order.
- **Ports are inferred from flow direction**, and written only to be explicit.
- A pipe's length and DN sit **at the end of its link**, with no `=`.
- **Cases** are named; a value that varies per case is a **driver** (`let`) or a bracket list, and a curve
  names its driver. Sizing covers every case.
- A **controller is one declaration** with its type, its binding and its tuning.
- A **run** is a block that says where it starts, how long it runs and what happens when.
- Names are globally unique (`D-41`), units are typed with temperature and temperature difference apart,
  the printer is lossless, and `#` starts a comment.

## Responsibilities

**Owns.** The language's design and the reasoning for each rule; the port-inference rule; cases, drivers
and how a curve composes with them; what a controller declaration's settings mean; what a run block's
settings, overrides and events mean; the reading of a syntax tree into the records the binder binds; the
`FS18xx` codes.

**Does not own.** The syntax of any statement, the lexical rules and the grammar (`12`); the unit table
and dimensional algebra (`13`); expression evaluation (`14`); binding and inference (`15`); the codes of
every other range (`16`); the printer's invariants (`17`); version selection (`18`); what a controller does
at run time (`34`); what a run integrates (`33`).

## Contracts

### The reference script

Every rule below is measured against this file. It is a district-heating substation: an open primary
through a pressure-control valve and a plate exchanger, and a closed heating secondary with a mixing
valve held on a weather-compensated supply temperature. It has two cases and one run.

```fluidscript
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

`band = 20 K` reads `K` as a temperature difference (`D-172`).

### Lines, blocks and names

The rules — block heads, indentation, what may stand where, how a line is classified — are
[`12`](12-grammar.md) §Lines and blocks. Why they are what they are:

**A setting belongs to the thing it sets**, so a block holds it: a circuit's fluid is indented under the
circuit, a declaration's parameters under the declaration, a run's events under the run. A line's place is
then visible where it is written rather than decided by the lines before it, and a line in the wrong place
is one message (`FS1802`) about that line.

**A block's lines agree**, because an indentation that differs by one space is either a typing slip or a
level the user meant, and the parser cannot tell which; reporting it (`FS1801`) and reading the line at the
nearer level keeps the rest of the block bound. A curve's rows are exempt because they are a table whose
columns the user aligns.

**Classifying a line reads further than one token** — a qualified name, then what follows it — and is
bounded the same way any lookahead here is: by one line, never by the lines around it. The enclosing block
narrows it further: a declaration block's body holds only parameter lines.

**Statement words are reserved by position, not by the lexer**, so a word such as `curve` stays available
as a setting's name before an `=` (a controller's `curve = heating`), and the list of what a component may
not be called is six words long.

**Names the registry owns are case-insensitive** — kinds, parameters, properties: `Kp`, `KP` and `kp` bind
alike, and the printer keeps what was written. That is `D-15`'s first stage, which `D-170` keeps. A property
named in a reference (`N2.T`) is not yet measured. Component names stay exact, case included: `PU1` and
`pu1` are two components. **A name binds only by its exact spelling** (`D-170`): case and underscores are
normalised as `D-15`'s first stage does, curated aliases resolve as its second stage does, and a merely
similar spelling feeds the error's suggestion and never binds.

### Values, units, lists and ranges

The forms are [`12`](12-grammar.md) §Values and §Units and quantities. Why:

**`in` (inch) and `t` (tonne) are not unit symbols a script can write.** They collide with the names a
script writes most — a port and a temperature — and nobody sizes a heating plant in inches or tonnes. The
rule that a spaced unit is never recognised before the next parameter's `=`, `[` or `.` **looks past
spaces**, because `=` may stand apart: `h` is an hour and an enthalpy, so in `flow = 5 h = 2000` the `h` is
the next parameter.

A bare number takes the canonical unit of the dimension it lands in (`D-14`, `13`).

**`K` is a temperature difference** (`D-172`): `band = 20 K`, `dt = 5 K`, as engineers write them. `C` and
`°C` are absolute temperatures. An absolute temperature in kelvin is not writable; `300 K` on a temperature
is a dimension error whose fix is `°C`. A compound unit that contains a `K` (`kJ/(kg*K)`) is its own
spelling and is unchanged. The unit table reads it so (`D-179`), so a unit means the same thing wherever it
stands (`D-26`'s property, kept).

**A list's unit belongs to the items that state none** — a bare number, a negated one (`[-26, 5] C` is a
−26 °C design day), and a bare name, as `heating kW` is — and is applied when the item is evaluated
(`D-179`); an item that states its own unit, or is an expression such as `a + 5`, is read as written. A
range's trailing unit applies to both ends: `30..40 min`, `85..75 C`.

**A date is unquoted**, because no one means `2026-01-15` as 2026 − 1 − 15. A clock time, `06:30`, is valid
in a run's events once the run states `start`.

### Statements

Every statement's form, and where it may stand, is [`12`](12-grammar.md) §Statements and §What may appear
where. What each means is below.

### The project block

The title is quoted. `cases` names the operating cases every list is read against; a file with no `cases`
has one case, and no list. `catalog` pins the pipe catalogue; `ScriptCompatibility` reads it from the text
before parsing ([`18`](18-script-compatibility.md)). The project block also holds the presentation (below,
`D-171`). Nothing else goes in it: the solve mode belongs to a run (`D-169`), a circuit's fluid to the
circuit (`D-165`).

### Presentation

Presentation is declared once, **in the project block**, and a circuit may override it (`D-171`):

```fluidscript
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

A circuit's `style:` overrides only the keys it states; the rest come from the project's. A style applies to
the block that holds it and is never applied by position. There are no named styles and no per-component
style.

### Drivers and cases

**A driver is a `let` whose value varies per case** (`D-167`):

```fluidscript
let outdoor = [-26, 5] C
```

It is an ordinary named value of any dimension — an outdoor temperature, a production rate, a network
pressure. Nothing about `outdoor` is built in; a plant driven by nothing has no driver. The one built-in
driver is `time`, the run's clock.

**A curve names its driver in its header**, and its rows are bare numbers:

```fluidscript
let outdoor = [-26, 5] C

curve heat_demand: outdoor
  -26   150
   18     0
```

The first column is read in the **driver's** unit (−26 means −26 °C because `outdoor` is a
temperature); the second takes the unit of **the parameter that uses the curve** (`D-57`: a curve has no
dimension of its own). The driver's unit is the one written on the `let`: `let production = [2, 3] m3/h`
puts the rows in m³/h, not in the l/s a bare flow means. A `let` that writes no single unit
(`let supply = outdoor + 2 K`) is read in its dimension's canonical unit (`13`). This project's reasoning:
the rows sit beside the `let` and are read against what it says. A test pins it
(`ADriversRowsAreInTheUnitItIsWrittenIn`). `extrapolated` and `format="…"` follow the driver (`D-60`);
without `extrapolated` a driver outside the rows clamps to the end row, and the report says so.

**A parameter is pinned to a curve by naming it**: `RAD radiator power = heat_demand`. In each case the
curve is read at that case's driver value, and the result is a stated value in that case — a
constraint, exactly as a number would be (`D-02`). In a run, a driver overridden by a curve of time
moves every curve of that driver with the clock (`D-149`'s composition).

A value that varies per case and follows no curve is a list on the parameter itself:
`secondary.out.t = [60, 50] C`. Anything that reads a driver varies with it — a curve, another `let`
(`let double = rise * 2`), a parameter — and is evaluated once per case. A driver whose list does not
have one value per case is `FS1540` (or `FS1541` with no `cases`), and its first case's value, or the
last it has, stands for every case: binding it to nothing would make every curve of it `FS1528` too.

**How the binder holds it**: the ordinary evaluation is the first case's, with each driver at its first
element. Each other case then re-evaluates, in the same dependency order, only the values that differ
between cases, and a parameter that reads a driver carries its value in every case beside its own, as a
list written on the parameter does — so sizing, the projection onto one case and the solvers read a driver
as they read a list, and nothing past the binder distinguishes them. A mistake made in every case (a
negative `dt`) is reported once.

**No case is marked as the design case.** Sizing already covers every case (`D-143`: each size is taken
from the case that demands most, and every case is checked against it); which case the canvas shows is
chosen in the interface; which case a run starts from is the run's `from`.

### Circuits

The title is quoted and is not a reference; it is the circuit's name in the model, and a circuit written
without one is named `circuit 1`, `circuit 2`, … in file order. Settings: `fluid` (the substance, required
once per circuit unless every circuit shares one), `number` (the tag prefix, `D-34`; resolved when absent),
and `role` (the circuit's role for the drawing, `D-35`, stated because a quoted title is a name for people
and carries no role). Declarations and connection lines follow in any order. Components are named globally
(`D-41`), so a line in one circuit may name a component declared in another; that is how circuits are
joined, and it needs no statement of its own (`D-166`).

### Declarations

`NAME kind`, then parameters on the line or on indented lines under a `:` ([`12`](12-grammar.md)
§Component declarations); the two forms are one grammar, the printer keeps whichever was written, and the
formatter never converts between them.

**Kinds and parameters are the registry's** (`22`, `15`). `valve3` is an alias of `three_way_valve`;
`temperature_sensor`, `pressure_sensor` and `flow_sensor` are aliases of the sensor kinds. A two-sided
exchanger's sides are **`primary` and `secondary`**: `primary.in.t`, `HX1.secondary.out`. `primary` is
side 1 and `secondary` side 2; which side is primary is decided by the circuit (below). The registry
records the spellings (`D-179`): `secondary.in` is the only way to write the second side (its `in[2]` is the
wire's id and never the script's), and side 1 is `in` or `primary.in` alike, because a one-sided `load` or
`heater` has no second side to tell it from. Port families that really are families keep brackets:
`layer[3].t`, `in[2].level` on a tank. The wider vocabulary review — `duty`, `rise`, `kvs`, direction taken
from the kind — is open question 2.

**A component's own sizing point is a setting per driver**, `sized_at.outdoor = -5 C` (`D-175`, `D-176`, amending
`D-94`): the driver is any `let`, of any quantity, named by its exact spelling, and the value is read in the `let`'s
unit (a bare number is taken in it). Each parameter that reads the `let`, directly or through curves, takes its value
there as a **capacity**, and is held to it in magnitude in every case and at every step of a run. A point nothing reads
is `FS1549`. With `let demand = [50, 16.3] kW`, `power = demand  sized_at.demand = 27.2 kW` gives 27.2 and 16.3 kW; a heat pump sized at −5 °C on a 50 kW
heating curve gives 27.2 kW on the −26 °C design day, the boiler the other 22.8, and the whole of a 5 °C day's
16.3 kW with the boiler at nothing. The basis line reports the capacity and its share of the design day.

### Connections

**A chain reads in the direction of flow.** `A - B - C`: water leaves `A`, passes `B` and enters `C`.
An endpoint no declaration names is a node (`15`'s rule I1).

**Ports are inferred from flow roles, and written only to be explicit** (`D-166`). After every line of
the file is read:

1. **A two-port component** (pump, valve, pipe, one-sided exchanger): the connection flowing in is its
   inlet, the one flowing out its outlet.
2. **A three-way valve** counts its connections. Two in and one out is a **mixing** valve: the out is
   `ab`, and the ins are `a` and `b`. One in and two out is **diverting**: the in is `ab`, the outs `a` and
   `b`. **`a` is the control path and `b` the bypass, read from the plant** (`D-175`): the bypass is the
   switched leg that gets back to the common leg's far end crossing the fewest components, the valve barred —
   the leg that closes the valve's own loop, and the test sizing already applies (`ValveLegs.Variable`). Two
   legs equally far, an injection circuit's landing on one header, take the order written, the first `a`;
   a script that means otherwise names the port. `mixing_valve` and
   `diverting_valve` remain as kinds that **assert** the function; a connection count that disagrees with
   the asserted function is `FS1805`.
3. **A two-sided exchanger**: the side wired inside the circuit that declares it is **primary**; the side
   wired from any other circuit is **secondary**. When both sides are wired in one circuit, the first
   pass written is primary. A pass `S - HX1 - R` pairs its inlet and outlet; passes on separate lines pair
   in the order written. A side with one port written explicitly takes the pass that needs its other port:
   `HX1.secondary.out - TV1` with `NR - HX1` is one secondary side.
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

**What the binder receives is an explicit port, so an inferred port counts as stated** (`D-88`'s
`PortStated`). The rule is the meaning — rule 2 makes the control path `a`, the leg the controller moves —
so there is nothing left for the binder to revise. A chain reaches the binder as one connection per link,
because a component in the middle of `A - B - C` takes a different port on each side.

**A sensor may sit in a chain**: `TV1 - SP - TE1 - RAD`. The binder lowers it to a node between `SP` and
`RAD` with the sensor observing it (`D-166`, amending `D-61`, whose objection — the sensor's identity
equations in the flow path — does not arise once it is lowered to a node). A sensor may still be placed
on a named node with `at`; placed both ways it is `FS1814`, and the chain's placement is kept. The node is named
after the sensor, `TE1_node` (with a number appended if that name is taken), and is what the drawing shows.

**A pipe's length and DN sit at the end of its link, with no `=`** (`PCV - HX1  12 m  DN25`). Each is
recognised by its own form — a length has a length unit, `DN25` is a designation — so neither needs a name,
and any other pipe parameter follows as `name = value` (`roughness = 0.05 mm`, `nodes = 4`). **Pipe
properties are allowed only on a line with one link**; on a longer chain they are `FS1803`, which asks
which link is meant. Applying them to every link would turn `A - B - C  25 m` into 50 m of pipe, and
choosing one link would be a guess.

### Controllers

A controller is **one declaration** holding its type, what it moves, what it reads, its setpoint and its
tuning (`D-168`):

```fluidscript
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

**What the binder receives.** Two records: the declaration, which keeps what belongs to the controller
whatever it is wired to — `type`, `kp`, `ti`, `td`, `action` — and the loop (`D-61`), which names what is
moved (a bare component meaning its one actuated parameter) and what is read, and carries `setpoint`,
`band`, `differential`, `output` and `curve`, because each is read in the units of what is measured or
moved. So:

- `band` and `differential` are differences in the measurement's dimension (`20 K` on a temperature; `20 C` is
  `FS1304`), `output` is a range in the actuated parameter's (`10..100 %` of a position, and outside the
  parameter's valid range is its own code, `FS2105` for a position), and a setpoint in the wrong dimension is
  `FS1304`.
- A setpoint that reads a driver is evaluated in every case, and each case's design solve holds its own:
  38.9 °C in the reference script's mild case.
- **`band` is not converted to `kp` here.** `kp = (Ymax − Ymin) / band` (`D-168`) needs the output range the
  controller settles when it is built, so `34` owns the conversion; the binding carries the band.
- `FS1808`, `FS1809` and `FS1810` are the reader's: the setting is dropped (`FS1808`; `kp` under `FS1809`),
  so the controller binds as the type it states. A setting no type has is `FS1503`, listing the twelve.
- A controller missing `moves` or `reads` is `FS1521`, "A controller needs moves, reads." A `curve` controller
  reading a driver or `time` binds as its declaration only: its loop has nothing in the plant to read, and
  `FS1810` already says it is not run. P6.3 gives it a binding.

### Runs

A run says what happens to the plant in time; the model says what the plant is (`D-169`). A file may hold
several; the interface plays the one chosen.

```fluidscript
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
- `over T1..T2  target = v1..v2` — a ramp; **both ends are written**, so a ramp never has to guess where it
  starts. A single value after `over` is `FS1807` ("a ramp needs both ends; for a step, write `at`").

An event replaces whatever drove its target: after `at 10 min RAD.power = 100 kW` the duty stops
following `heat_demand`. A time is a duration from t = 0 (`10 min`, `1 h`) or, with `start` stated, a
clock time (`06:30`). What an event may target is `33`'s business, and some targets need solver work
before they run — a boundary's state is `S-77`, a pump switched off is `S-76`; the syntax accepts them
from the start and the solver's refusal is its own diagnostic.

What a run does with a stated value is `33`'s table: sizes are frozen, inputs are held until an override or
event moves them, design targets are released to their controllers.

Solver numerics — step size, tolerances — are not written in a script.

**What the binder receives.** A run reaches the binder as written, its values in the form the evaluator
reads, and binds to a `RunSymbol` in `SemanticModel.Runs`; nothing in the model changes. `RunProjection`
turns the model and the run the interface plays into the model that run solves: the `from` case's values,
every circuit dynamic but those named in `steady` (by title), the run's events as the schedule, its `start`
as the clock's, and its duration and frame as the transient's settings. So:

- An override of a parameter (`RAD.power = 50 kW`) is a step at t = 0. An override of a `let` by a value
  (`outdoor = -10 C`) is evaluated by the binder, in the run's starting case, into a step at t = 0 on every
  parameter that reads it and changes. An override of a driver by a curve of time (`outdoor = weather_jan`)
  re-points every curve of the driver at that curve, and each parameter that reads one follows the clock.
  For that, every parameter that reads a curve is held for the clock with its value.
- A clock time is the next one at or after `start`: `06:30` in a run starting at 06:00 is 30 min in, and in one
  starting at 22:00 it is 8.5 h in. This project's reasoning; with no `start` it is `FS1816`.
- A run that follows a curve of time with no `start` is `FS1546`, once on the run's head.
- An event that starts after the run's `duration` never happens, and says so (`FS1817`, a warning): `at 2 h` in a
  1 h run is a typo or a duration too short, and neither is worth refusing the file over.
- `TC1.setpoint` is an event target, read in what `TC1` measures.
- An event replaces what drove its target from its start: the transient writes the clock and then the schedule
  (`33`).

### Reading the tree into the binder

The syntax tree is the printer's and the editor's, and the binder reads it through a front end of its own,
`ScriptReader` (`D-177`, `D-178`). It hands the binder records rather than statements — a circuit, a link,
a control loop, the cases — and every span in them points into the script's text, so every diagnostic lands
on what the user wrote.

| Written | The binder receives |
|---|---|
| `circuit "T":` with `fluid`, `number`, `role` | A circuit's head, with its number and role, and its fluid |
| A declaration, either form | The declaration with its settings gathered onto it |
| `primary.*`, `secondary.*` | The registry's spellings of its ports `in`/`out` and `in2`/`out2`, resolved by the lookup (`D-179`) |
| A chain with inferred ports | One connection per link, every port settled |
| A sensor in a chain | A node in the chain and the sensor placed on it |
| `12 m DN25` at a link's end | `length` and `dn` on that link's pipe |
| A controller block | The controller's declaration and its loop |
| `cases` | The cases, with the first as the one the file operates at |
| A run | The run, its values in the form the evaluator reads |
| `show`, `scale`, `spacing`, `style:` | The show settings, the spacing, and a style per circuit with the project's keys under the circuit's |

Port inference is the reader's: it sees every line before the binder files any, and hands the binder
explicit ports. A script has no schedule roles: a driver is a `let` (`D-177`).

### The frozen corpus

The binder is held to a frozen corpus (`D-178`): every script the project owned when the language was
proven, committed as its text beside a golden of what it binds to — the model's shape, the `let`s, the
deferred expressions, the `show` settings, and every diagnostic with its code, span and message. The direct
binder must bind every item to its golden, and a golden changes only with its reason stated in the commit
(`FrozenCorpusTests`).

### Diagnostics

The range **`FS18xx`** is owned by this document. Every message is in the language's own words, from every
stage: the stage that raises a message writes the spelling a script uses — an exchanger's port as
`secondary.in`, a whole `K` as a difference (`D-179`) — so nothing respells a diagnostic on its way out, and
each code has one template (`D-174`). `FS2112` and `FS2202` name the port by its spelling, `FS2119` says
"primary side" or "secondary side", `FS2210`/`FS2211` list `HX1.secondary.out.t`, and `FS1503`/`FS1505` list
what the kind accepts. `ScriptDiagnosticsTests` holds a corpus of mistakes to it.

| Code | Severity | When | Message |
|---|---|---|---|
| `FS1801` | Error | A block body's lines are indented unlike each other, or mix tabs and spaces | `This line is indented unlike the rest of its block. Indent it as the line above it is.` |
| `FS1802` | Error | A statement outside the block it belongs in: `fluid = water` at the top level, an event outside a run | `{statement} belongs {place}.` |
| `FS1803` | Error | Pipe properties on a line with more than one link | `A pipe's length and size describe one link, and this line has {links}. Put the pipe on a line of its own: '{first} - {second} {properties}'.` |
| `FS1804` | Error | A port the inference rule cannot settle; the message lists the ports to write | `'{component}' cannot take this connection: {reason}. Name the port, such as '{example}'.` |
| `FS1805` | Error | A `mixing_valve` or `diverting_valve` whose connections say the other function | `'{component}' is written as a {asserted} valve, and its connections make it {actual}: {inflows} in and {outflows} out.` |
| `FS1806` | Error | A line in a shape the language does not have, with the form to write ([`12`](12-grammar.md) §Lines the language does not have). Each shape is exact, so a line that merely starts with the same word is not caught; the price is that a component may not be declared under one of these words in that shape (`design valve`) — this project's reasoning: that reading is far more likely to be meant | `'{word}' does not start a line this way; {instead}.` |
| `FS1807` | Error | A ramp missing an end: of its time (`over 30 min`) or of its value (`= 75 C`); the message says which | `A ramp needs both ends of {half}, such as '{example}'. For a step, write 'at'.` |
| `FS1808` | Error | A controller parameter that its stated type does not have | `'{controller}' is a {type} controller, which has no '{parameter}'. A {type} controller takes: {available}.` |
| `FS1809` | Error | Both `band` and `kp` stated | `'{controller}' states both band and kp, and each says the other. State one.` |
| `FS1810` | Warning | A controller type the solver does not run yet | `'{controller}' is a {type} controller, which the solver does not run yet.` |
| `FS1811` | Error | A curve whose driver is neither a `let` nor `time` | `'{curve}' is driven by '{driver}', which is not a let. Write 'let {driver} = [...]' with one value per case, or drive it by time.` |
| `FS1812` | Error | A block head without its `:` | `A {head} line opens a block and ends with ':'.` |
| `FS1813` | Error | A word after a pipe's link that is not a DN designation (`12 m NPS1`); the pipe keeps its length and is sized | `'{text}' is not a pipe size. Write a DN designation such as DN25, or name the property: 'roughness = 0.05 mm'.` |
| `FS1814` | Error | A sensor that sits in a chain and is also placed `at` a node; the chain's placement is kept | `'{sensor}' sits in a chain and is also placed at '{node}'. Keep one: in a chain it reads the point where it sits.` |
| `FS1815` | Info | How the rule wired a component where it chose between ports: a three-way valve, an exchanger with two sides, a tank side with more than one stream | `'{component}' is wired as {wiring}.` |
| `FS1816` | Error | A clock time in a run that states no `start` | `'{time}' is a clock time, and '{run}' states no start. Write 'start = 2026-01-15 06:00' in the run, or a duration such as '30 min'.` |
| `FS1817` | Warning | An event that starts after its run ends, and so never happens (`L-67`) | `This event starts at {time}, after '{run}' ends at {duration}, so it never happens.` |

## Invariants

1. **`Print(Parse(x)) == x` for every input**, malformed ones included (`17`'s invariant 1, under the same
   fuzz test).
2. **One bad line damages at most its block.** A malformed line inside a block is a malformed statement
   in that block; the block and every line after it still bind.
3. **The parser does not read the component registry.** Kinds and parameters are names; a new kind or
   parameter changes no grammar.
4. **Every choice between ports is reported once (`FS1815`) and drawn labelled.** No such choice is
   silent; a two-port's inlet and outlet are not a choice.
5. **Every span the binder sees points into the script's text.**

## Error cases

The `FS18xx` table above, the codes `12` owns for the lines themselves, and the codes of every other range
that the statements here raise. No stage throws on any input.

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
| `HX1 - TV1` (from another circuit) | `HX1.secondary.out`; `TV1`'s inflow from the exchanger, the control path → `a` |
| `TV1 - SP - TE1 - RAD - NR` | `TV1`'s one outflow → `ab`, so `TV1` mixes; `TE1` becomes a node between `SP` and `RAD` with `TE1` placed at it |
| `NR - TV1` | `TV1`'s inflow from the return, which closes its own loop through `RAD` → `b`, the bypass |
| `NR - HX1` | `HX1.secondary.in` |

**What the binder receives for the heating circuit** — spans in the script's text:

| Record | What it holds |
|---|---|
| Circuit | `Heating`, number 200, fluid `water` |
| Declarations | `SP` pump; `TV1` three-way valve, `stroke` 90 s; `TE1` temperature sensor at `TE1_node`; `RAD` radiator, `power = heat_demand`; `TC1` controller, `type` PI, `ti` 120 s |
| Links | `HX1.secondary.out → TV1.a` (a pipe, 30 m, DN32); `TV1.ab → SP.in`; `SP.out → TE1_node`; `TE1_node → RAD.in`; `RAD.out → NR`; `NR → TV1.b`; `NR → HX1.secondary.in` (a pipe, 30 m, DN32) |
| Loop | `TC1` moves `TV1.position`, reads `TE1.t`, setpoint `supply_temp`, band 20 K |

## Acceptance criteria

- [x] The reference script parses, prints back byte for byte, and binds with no error, its run included.
      Its design solve does not settle, which is `S-86`, not a fault of the language.
- [x] The printer fuzz test runs on the language's input (`FluidScriptParserTests`: every one-character
      deletion of the reference script and 3 000 random edits).
- [ ] A malformed line inside a block leaves the rest of the block and the file bound (invariant 2).
- [x] Port inference: each rule has a test, including a mixing and a diverting `valve3`, an exchanger
      wired across two circuits and within one, a tank, and each `FS1804` shape — a valve with one stream
      each way, a valve with three inflows, a third pass through an exchanger and a second inflow into a
      pump (`ScriptReaderTests`).
- [ ] `FS1803`: `A - B - C 12 m DN25` is refused, `A - B 12 m DN25` binds as one 12 m pipe.
- [x] The worked example's per-case values (70.9 °C, 44.3 kW, 38.9 °C) are asserted (`ScriptReaderTests`).
- [x] A controller of each type binds; each `FS1808` and `FS1809` shape is refused; `FS1810` is raised
      for every type the solver does not run.
- [x] A run binds to the settings and events `33` reads; a one-valued `over` is `FS1807`; the weather loop
      is played in time (`ScriptRunTests`).
- [x] Each line shape of `FS1806`'s row is `FS1806` with the form to write (`FluidScriptParserTests`, the
      fifteen shapes).
- [x] Every diagnostic a script receives is in the language's words, from every stage, and each code has
      one template (`ScriptDiagnosticsTests`; `ScriptReaderTests`'
      `AMessageAboutTheSecondSideWritesItsSpelling`).
- [x] `samples/v2-syntax-tour.fluid` holds every statement and binds with nothing to report. Its solve, like
      the reference script's, is `S-86`'s.
- [x] A file with no version line is read as the current language (`D-174`; `ScriptCompatibilityTests`, and
      the editor's new-file template).
- [x] The binder binds the whole frozen corpus to its goldens (`FrozenCorpusTests`, `D-178`).
- [x] The editor highlights and completes the language (`U-11`).
- [ ] The tutorial and a reference page per statement exist, and the documentation gate checks them.

## Open questions

1. **Tanks.** A tank's ports and layers inside a block (`layer[3].t = 45 C`, `in[2].level = 0.8`) and
   rule 4 of port inference are written above; nothing else about tanks has been discussed.
2. **The vocabulary pass**: `duty` for heat and `power` for shaft or electrical power, `rise` on a pump
   and `dp` only ever a drop, `kvs`, and each thermal kind taking its direction from what it is (a boiler
   heats its water, a radiator cools it) instead of from a sign convention on an alias (`D-91`). Two gaps
   are measured: a side's flow is written at its inlet port (`primary.in.flow`, `secondary.in.flow`), and
   the side itself has no spelling (`primary.flow` is `FS1503`); and the second side's fluid volume is still
   `volume[2]`, the one exchanger spelling left indexed. Recommendation: a package of its own.
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
