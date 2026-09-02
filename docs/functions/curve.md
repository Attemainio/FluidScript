# curve

A named table of values, interpolated between its rows.

```fluidscript
curve heating tout
-26  50
-10  40
 20   0
```

Read this as: at −26 outside, 50; at −10, 40; at 20, nothing. Between the rows the value moves in a
straight line — at −18 it is 45.

A curve is what an engineer draws as a heating curve or a compensation curve, and it is how a plant
says "when it is cold outside, run hotter" without anyone writing that rule out.

## The three words

```fluidscript
curve heating tout extrapolated
```

- `curve` — the keyword.
- `heating` — the name. You use it wherever a value goes.
- `tout` — **what it depends on**. Another curve, a known driver such as `tout` (outdoor
  temperature), or `time`.

Anything after those is a setting, and there is only one: `extrapolated`.

The two names are not interchangeable, which is what protects you from writing them the wrong way
round. The second must be **new**; the third must be something that already exists. So
`curve outdoor heating` when you meant `curve heating outdoor` is reported rather than bound.

## Known drivers

A driver name is matched the same forgiving way a component kind is: `tout`, `t_out`, `outdoor` and
`outdoorTemperature` are all the outdoor temperature, and all reach the same `design tout=-26`.

The drivers FluidScript knows are `tout`, `troom`, `tground`, `humidity`, `wind`, `solar` and
`demand`. Knowing one buys two things: the spellings above, and a check on the design value — `design
tout=3 bar` is caught, because the outdoor temperature is a temperature.

You are not limited to them. Any name works as long as something gives it a number:

```fluidscript
design flueTemp=180
curve recovery flueTemp
100   5
200  20
```

A driver that names no curve, no known driver, no `design` line and is not `time` is an error — not
because the name is unregistered, but because nothing anywhere can say what it is.

## Beyond the ends

By default a curve **holds** at its ends. The table above returns 50 for anything colder than −26,
and 0 for anything warmer than 20.

`extrapolated` continues the slope of the last two rows instead. Use it when the trend is real
outside the table; leave it off when you do not know. Holding is the default because it cannot invent
a number: two rows are not evidence about a temperature twenty degrees past them.

## What a curve's numbers mean

Nothing, on their own. A curve is just numbers, and whatever reads it decides the unit:

```fluidscript
HX1 heat_exchanger power=heating       # 50 becomes 50 kW
TV1 valve position=opening             # 0.3 becomes 0.3, a fraction
```

That is why one curve can drive a power, a percentage or a temperature.

## Curves of time

Give `time` as the driver and the rows are timestamps:

```fluidscript
curve outdoor time
2026-01-01T00:00:00  -1
2026-01-01T01:00:00  -3
```

Timestamps are ISO 8601, or plain seconds. For anything else, say the format:

```fluidscript
curve outdoor time format="dd/MM/yyyy HH:mm:ss"
01/01/2026 00:00:00  -1
```

**Capitals matter in that string.** `MM` is the month and `mm` is the minute; `HH` is the 24-hour
clock and `hh` is the 12-hour. `dd/mm/yyyy` is day, *minute*, year — which is why FluidScript will
not guess a format from your data or from your computer's region: the same file has to mean the same
thing everywhere.

## Where curves go

Before the first `circuit`, with the other whole-file lines. A curve is shared by every circuit that
names it.

A curve's rows run until the next `curve` or the first `circuit`. Nothing else ends one.

## Curves and how the file is solved

A curve is a value that changes, so it needs something to change with.

- **Solving in time** (`fluid dynamic`) — a curve of time follows the clock, and everything reading
  it follows along.
- **Solving a steady state** (`fluid static`) — there is no clock, so say the condition instead with
  [`design`](design.md). Every curve is then read once at that condition and stays there.

Without either, a curve has no value and FluidScript says so rather than picking one.

## See also

[`design`](design.md) · [`schedule`](schedule.md) · [`fluid`](fluid.md)
