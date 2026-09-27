# curve

A named table of values, read against its driver and interpolated between its rows.

```fluidscript
fluidscript 2

project "Heating plant":
  cases = [winter, mild]

let outdoor = [-26, 5] C

curve heating: outdoor
  -26   50
  -10   40
   20    0
```

Read this as: at −26 outside, 50; at −10, 40; at 20, nothing. Between the rows the value moves in a
straight line — at −18 it is 45.

A curve is what an engineer draws as a heating curve or a compensation curve, and it is how a plant
says "when it is cold outside, run hotter" without anyone writing that rule out.

## The header

```fluidscript
fluidscript 2

let outdoor = -26 C

curve heating: outdoor extrapolated
  -26   50
   20    0
```

- `curve` — the statement word, at the top level of the file.
- `heating` — the name. You use it wherever a value goes.
- `outdoor` — after the colon, **what it depends on**: a [`let`](let.md), or `time`.

Anything after those is a setting: `extrapolated`, and for a curve of time, `format="…"`.

The two names are not interchangeable, which is what protects you from writing them the wrong way
round. The name before the colon is new; the one after it must already be a `let`. So
`curve outdoor: heating` when you meant `curve heating: outdoor` is [`FS1811`](diagnostics.md), not a
curve bound backwards. A curve cannot be driven by another curve: give the value it follows a `let`.

## The rows

Each row is indented under the header and holds two bare numbers, and the rows end at the first line
that is not indented under it. A row needs only to sit deeper than the header, so align the columns
as you like.

- **The first column is in the driver's unit.** −26 means −26 °C because `outdoor` is written in
  °C. `let production = [2, 3] m3/h` puts the rows in m³/h, and a `let` that writes no single unit,
  such as `let supply = outdoor + 2 K`, is read in its dimension's usual unit ([Units](units.md)).
- **The second column is in the unit of whatever reads the curve** — see below.
- A row that is not two numbers is [`FS1117`](diagnostics.md), and a curve needs at least two rows
  ([`FS1530`](diagnostics.md)).

## Reading a curve

A parameter reads a curve by naming it, and in each case the curve is read at that case's value of
its driver:

```fluidscript
fluidscript 2

project "Heating plant":
  cases = [winter, mild]

let outdoor = [-26, 5] C

curve heating: outdoor
  -26   50
   20    0

circuit "Heating":
  fluid = water

  HX1  heat_exchanger  power = heating       # 50 kW in winter, 16.3 kW in mild
```

The result is a stated value in each case, a constraint exactly as a number would be.

**A curve's numbers mean nothing on their own.** Whatever reads it decides the unit: `power = heating`
makes 50 into 50 kW, and `position = opening` would make 0.3 a fraction. That is why one curve can
drive a power, a percentage or a temperature.

When the table is not in the parameter's own unit, write the unit after the name, the way you would
after a number:

```fluidscript
fluidscript 2

let outdoor = -26 C

curve heating: outdoor
  -26   50
   20    0

circuit "Heating":
  fluid = water

  HX1  heat_exchanger  power = heating W     # 50 becomes 50 W
```

The unit is read exactly as it would be on a literal, so `power = heating kPa` is refused as a
pressure handed to a power ([`FS1304`](diagnostics.md)). A unit after a value that already has one,
such as `dp = HE1.dp kW`, is refused the same way; `dp = HE1.dp kPa` merely agrees and changes nothing.

A component can take its capacity from another point on the curve than its cases give it, with
`sized_at.outdoor = -5 C` on its declaration — see
[A component's own sizing point](project.md#a-components-own-sizing-point).

## Beyond the ends

By default a curve **holds** at its ends. The table above returns 50 for anything colder than −26,
and 0 for anything warmer than 20.

`extrapolated` continues the slope of the last two rows instead. Use it when the trend is real
outside the table; leave it off when you do not know. Holding is the default because it cannot invent
a number: two rows are not evidence about a temperature twenty degrees past them.

## Curves of time

Give `time` as the driver and the first column is a date:

```fluidscript
fluidscript 2

curve weather_jan: time
  2026-01-15 06:00   -18
  2026-01-15 12:00    -9
```

A date is written unquoted — `2026-01-15`, `2026-01-15 06:00` or `2026-01-15 06:00:30` — or as plain
Unix seconds. No time zone is written or read, in a row or in a run's `start`, so the two always
agree. For anything else, say the format:

```fluidscript
fluidscript 2

curve weather_jan: time format="dd/MM/yyyy HH:mm:ss"
  15/01/2026 06:00:00   -18
  15/01/2026 12:00:00    -9
```

**Capitals matter in that string.** `MM` is the month and `mm` is the minute; `HH` is the 24-hour
clock and `hh` is the 12-hour. `dd/mm/yyyy` is day, *minute*, year — which is why FluidScript will
not guess a format from your data or from your computer's region: the same file has to mean the same
thing everywhere.

A format that cannot read a date is reported once, on the header ([`FS1534`](diagnostics.md)): not a
quoted string, no day (`d`), or no month (`M`) — `dd/mm/yyyy` is the usual one. The rows are then left
alone, because they are not the mistake. When the header is fine and rows still do not read, the
first five are marked where they are and the rest are counted on the header
([`FS1535`](diagnostics.md)), so a year of hourly data with one wrong column layout is one message,
not thousands.

**Only a run has a clock**, so a curve of time is read through a run. The run hands it to a driver —
`outdoor = weather_jan` — and from then on every curve of that driver moves with the clock, and
everything that reads one follows along:

```fluidscript
fluidscript 2

project "Heating plant":
  cases = [winter, mild]

let outdoor = [-26, 5] C

curve heating: outdoor
  -26   50
   20    0

curve weather_jan: time
  2026-01-15 06:00   -18
  2026-01-15 12:00    -9

circuit "Heating":
  fluid = water

  HX1  heat_exchanger  power = heating

run "Cold morning":
  start    = 2026-01-15 06:00
  duration = 2 h
  outdoor  = weather_jan
```

The run's clock starts where its `start` says, so this run reads the 06:00 row first, and its steps
land on every row of the time curve, where the line between rows changes slope. A parameter that
reads a curve of time directly, with no run to hand it a clock, is [`FS1528`](diagnostics.md); a run
that follows one with no `start` is [`FS1546`](diagnostics.md). See [`run`](run.md).

## See also

[`let`](let.md) · [`run`](run.md) · [`project`](project.md) · [`controller`](controller.md)
