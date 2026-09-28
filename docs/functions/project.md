# project

The project block: the file's title, the operating cases the plant is sized for, the catalogue it
sizes from, and how it is drawn.

```fluidscript
fluidscript 2

project "Substation 12":
  cases   = [winter, mild]
  catalog = steel_en10255@2026.1
  show    = temperature
  spacing = 1.2
  style:
    colour = "#2f6f9f"
    width  = 2
```

The title is quoted and is only a name; `project:` with no title is fine. Every setting is optional,
and a file with no project block at all has one case, the shipped catalogue and the default drawing.
A file has one project block: a second one is an error, [`FS1818`](diagnostics.md), and its settings are
not read.

## Settings

| Setting | What it says | Page |
|---|---|---|
| `cases` | The operating cases every list in the file is read against, in order | [below](#cases) |
| `catalog` | The pipe catalogue sizing chooses from, and its version | [`catalog`](catalog.md) |
| `show` | The property the diagram colours by, one or a list | [`show`](show.md) |
| `scale` | The colour scale's range for the first property shown | [`show`](show.md) |
| `spacing` | How far apart components are drawn | [`spacing`](spacing.md) |
| `style:` | The drawing's defaults: colour, width, corner and line | [`style`](style.md) |

Anything else is [`FS1503`](diagnostics.md), listing these six. How the file is solved is not a
project setting: the design solve is always steady, and solving in time is a [`run`](run.md)'s.

## Cases

A plant almost never has one duty. A substation heats in January and makes hot water in July; an
air-handling coil is a heater at −20 °C outside and a cooler at +30 °C. Size it for one of those and
it is wrong in the other, usually in a direction nobody checks.

`cases` names the operating cases. Every case is solved, every component is sized to cover **all** of
them, and the plant you get is one plant — not one per case.

```fluidscript
fluidscript 2

project "Changeover":
  cases = [winter, summer]

circuit "distribution":
  fluid = water
  role  = distribution

  HE1   heat_exchanger  power = [50, -40]  in.t = [35, 12]  out.t = [45, 7]
  LOAD  heat_exchanger  power = [-50, 40]  dp = 0
  PU1   pump                            # no list: one pump serving both cases
  P1    pipe  length = 20

  N1 - PU1 - N2 - HE1 - N3 - LOAD - N4 - P1 - N1
```

Any value may then be a list, one value per case, **in the order `cases` names them**: `HE1` puts in
50 kW in `winter` and takes out 40 kW in `summer`. A value that varies per case can also be named once
with a [`let`](let.md#one-value-per-case) and read wherever it is needed.

- **A plain value is not a short list.** `PU1 pump` and `power = 30` mean the same thing in every
  case.
- **The order is the contract.** A list is read against `cases` by position and by nothing else, so
  swapping two names there reassigns every list in the file. Nothing is matched by name and nothing
  is padded: two cases and three values is [`FS1540`](diagnostics.md), not a guess, and a list in a
  file that declares no cases is [`FS1541`](diagnostics.md).
- **Each item is an ordinary value**, so units, expressions and names all work inside one. A unit
  after the closing bracket applies to every item that states none: `t = [85, 70] C`.

**The canvas draws one case at a time, on the plant that covers them all.** Every case is solved and
every size covers all of them whichever case you look at; what changes between cases is the state —
temperatures, flows, pressures, valve positions — and the values that case states. The first case is
the operating one and is drawn by default; a **Case** picker at the canvas's top right draws another
without editing the script, and a static export carries the case on the canvas and names it. A run
starts from the first case unless it says otherwise with `from`.

**A case that does not solve stops the sizing**, because a plant that covers every case cannot be
sized without each of them: [`FS2315`](diagnostics.md) names the case, and that case's own diagnostics
follow it. A closed loop whose source follows a curve down in a mild case while its load stays fixed
is the usual cause — the heat has nowhere to go.

**Every case is checked, not only the first.** A case that contradicts itself is reported on its
line. `HX1 heat_exchanger power = [50, 40] in.t = [35, 12] out.t = [45, 7]` heats the water from 35 to
45 °C in `winter`, which is fine. In `summer` it puts 40 kW into water that cools from 12 to 7 °C,
and you get one [`FS2119`](diagnostics.md) that quotes summer's numbers and ends *"…say the water
cools in summer."* When more than one case fails it is still one message, naming the first case's
numbers and then the rest: *"…cools in winter, and likewise in summer."* A duty whose sign turns with
its temperatures, such as `power = [50, -40]` above, is consistent in both cases and reports nothing.
A check on a single number, such as a negative capacity on a `load` ([`FS1308`](diagnostics.md)), goes
on that number inside the brackets.

## What the plant is sized for

Every case. Not by picking a winning case and taking its whole component — that would be wrong the
moment two cases disagree about different parts, and they usually do. Each size is taken across the
cases on its own:

| | Sized by | Also checked in |
|---|---|---|
| Pipe | the largest flow | — |
| Heat exchanger | the largest UA | — |
| Pump | the case needing the most head at its flow | every case: is it on the curve? |
| Control valve | Kv from the largest-flow case | every case: its authority, and whether the lightest case is within its turn-down |

The reason is the flow trap. A chilled side running 7/12 °C carries 1.91 kg/s for 40 kW; a heating
side at 45/35 °C carries 1.20 kg/s for 50 kW. **The smaller duty has the larger flow.** Size the pipe
from the exchanger's winning case and it is 60 % short in summer.

The sizing report says which case decided each size, and what the pass cost:

```text
=== cases  2 cases: winter, summer — operating winter
    merge      2 rounds, settled

    size                            value  governed by
    HE1.flow                1.90641 kg/s  summer
    LOAD.flow               1.90641 kg/s  summer
    P1.dn                             65  summer

    case            iterations  passes  settled     ms
    winter                   2       1      yes    1.6
    summer                   2       1      yes    1.5
```

That is the flow trap caught in the act. **Summer governs everything** — the 40 kW case, not the
50 kW one — because 40 kW over a 5 K program carries 1.91 kg/s where 50 kW over a 10 K program
carries 1.20. Size that pipe from the bigger duty and it is a size short in the other half of the
year. The report's first line names the first case as `design`.

"merge — 2 rounds" is the pipeline going round twice. Sizes are coupled: a larger pipe drops less
pressure, so the pump needs less head, so the valve sees a different authority. The second round
re-sizes every case against the merged plant and confirmed the first; if a plant is still changing
after four rounds you get the last merge and a note saying so.

### What a list is not

- **Not a time series.** Cases are unordered and nothing interpolates between them. A duty that
  changes through a day is an event in a [`run`](run.md).
- **Not a solve mode.** Each case is an ordinary steady solve. A run starts from one case's steady
  state, the first unless its `from` names another.
- **Not a search.** A case only exists if you wrote it — see below.

### The case you did not write

This is the honest limit. A list checks what someone thought to name.

Take a plant with 50 kW of heating at the cold end, 40 kW of cooling at the warm end, and a recovery
exchanger passing whatever the two have in common:

| Case | Heating | Cooling | Recovery |
|---|---|---|---|
| `winter` | 50 kW | 0 | 0 |
| *between them* | 5 kW | 5 kW | **5 kW** |
| `summer` | 0 | 40 kW | 0 |

Write only `winter` and `summer` and that exchanger is sized at zero in both. It does not come out
small — it disappears, and the case that governs it is the one in the middle that nobody named.

**FluidScript tells you when this happens.** A component that carries no duty in *any* case gets
[`FS2314`](diagnostics.md):

```text
FS2314  'REC' carries no duty and no flow in any of the 2 cases (winter, summer), so nothing
        sizes it. If it exists to serve two demands that peak in different cases, the case where
        both are on is not in the list.
```

It reports the shape, not a diagnosis — it cannot know *which* case you are missing, because that
depends on how one load relates to another and the model does not carry that. It is a warning rather
than an error, because a plant may legitimately carry a standby component no stated case uses.

So: name a shoulder case whenever two loads can be on at once.

### How well a valve controls

A control valve gets its Kv like any other size — the largest any case asked for. Its **authority**
is different: it is not a size but a reading of how well that valve will control, the share of the
branch's pressure drop the valve takes when fully open. So it is read off the merged plant, in every
case, and the report shows the **lowest**, naming the case:

```text
    CV1.authority               0.692107  lowest in summer
    CV1.kv                       10 m3/h  summer
```

On one plant the cases normally agree to within a percent: both the valve's drop and the rest of the
branch's grow with the square of the flow, so a lighter case lowers both by the same factor. They
disagree when something you stated differs between cases. With `dp = [5, 60]` on the exchanger,
winter's light branch wants the larger valve (Kv 16) and gets it; in summer that same valve sits in a
branch dropping 60 kPa and reads 0.23 — and the sizing notes say so:

```text
CV1 has authority 0.23 in summer, below 0.25. It will behave as a switch rather than a control
valve: the branch's own resistance dominates until the valve is nearly shut.
```

**Turn-down.** A valve also has a smallest flow it can still control: its rangeability — 50:1 for
equal percentage, 33:1 linear, 20:1 quick opening — reduced to `R·√a` once installed, because the
valve sees more of the drop as it closes. When the lightest case asks for less than that, you get
[`FS4013`](diagnostics.md):

```text
FS4013  'CV1' must pass 0.024 kg/s in winter and 1.906 kg/s in summer, 1.3 % of its heaviest flow.
        An equal-percentage valve at authority 0.66 controls down to about 2.5 % (50:1 × √0.66), so
        in winter it will open and shut rather than modulate. Give the light case a smaller valve in
        parallel, or split the duty.
```

The `√a` part is FluidScript's own reasoning from the Kv law, not a figure from a standard; the
rangeabilities are manufacturers'. A case in which the valve carries no flow at all is left out of
both checks — a shut valve is not controlling.

## A component's own sizing point

A bivalent plant is a heat pump sized part-way up the heating curve and a boiler for the rest. The
heat pump is not sized for the coldest case — that would mean a machine that cycles all winter — so
it says where it *is* sized, with `sized_at.` and the name of the [`let`](let.md) it is sized
against:

```fluidscript
fluidscript 2

project "Bivalent plant":
  cases = [winter, mild]

let outdoor = [-26, 5] C

curve heating: outdoor
  -26   50
   20    0

circuit "Heating":
  fluid = water

  HP1   heater  power = heating  sized_at.outdoor = -5 C
  BL1   heater
  LOAD  load    power = heating  in.t = 70 C  out.t = 40 C
  PU1   pump

  PU1 - HP1 - BL1 - LOAD
  LOAD - PU1   20 m  DN32
```

`sized_at.outdoor = -5 C` gives each of `HP1`'s parameters its value with `outdoor` taken as −5 °C:
`HP1.power` is the curve at −5, 27.2 kW, and that is the heat pump's capacity. The load reads the
same curve at −26 and asks 50 kW, and the boiler, which wrote nothing, is sized to the 22.8 kW that
remains. Two components read one curve at two points; the curve itself does not change.

The report tells you what fraction of the first case that was:

```text
HP1.power   27.174 kW at outdoor=-5, 0.54 of the 50 kW the design day asks
```

You choose the point, not the percentage — that is how a bivalent system is specified, and the
percentage is what you check afterwards. A bivalence point near −5 °C typically leaves the backup a
few percent of the year's heat; one at 0 °C hands it a third.

**The value at the point is a capacity, not a fixed output.** Wherever the plant asks more — the
coldest case, a cold morning in a run — the heat pump gives its 27.2 kW and the boiler the rest.
Wherever it asks less, the heat pump gives all of it and the boiler nothing, as a heat pump above its
bivalence point turns down:

| | winter, −26 °C | mild, 5 °C |
|---|---|---|
| The curve asks | 50 kW | 16.3 kW |
| `HP1` gives | 27.2 kW, its capacity | 16.3 kW |
| `BL1` gives | 22.8 kW | 0 kW |

The limit applies to the size of the value, so a chiller sized at 28 °C on a 32 °C cooling curve is
held the same way.

**The point names any `let`, of any quantity**, by its exact spelling, and the parameter may read it
directly, through another `let`, or through curves. A plant whose cases state the heat demand itself
sizes the heat pump the same way:

```fluidscript
fluidscript 2

project "Bivalent plant":
  cases = [peak, part]

let demand = [50, 16.3] kW

circuit "Heating":
  fluid = water

  HP1   heater  power = demand  sized_at.demand = 27.2 kW
  BL1   heater
  LOAD  load    power = demand  in.t = 70 C  out.t = 40 C
  PU1   pump

  PU1 - HP1 - BL1 - LOAD
  LOAD - PU1   20 m  DN32
```

That gives `HP1` 27.2 kW in the first case and all 16.3 kW in the second.

- The value is read in the `let`'s unit, so a bare `-5` is −5 °C against `let outdoor = [-26, 5] C`,
  and it is reported in that unit too.
- A point in another dimension than its `let`, `sized_at.outdoor = 3 bar`, is
  [`FS1304`](diagnostics.md).
- A point that none of the component's parameters read changes nothing, and
  [`FS1549`](diagnostics.md) says so — a misspelt name is the usual cause.
- Name more than one `let` with more than one `sized_at.` setting if the component's parameters read
  several.

## See also

[`let`](let.md) · [`circuit`](circuit.md) · [`run`](run.md) · [`catalog`](catalog.md) ·
[`show`](show.md) · [`style`](style.md)
