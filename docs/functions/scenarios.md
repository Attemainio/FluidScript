# scenarios

The named operating cases the plant must work in — all of them.

```fluidscript
scenarios winter summer
design winter
```

A plant almost never has one duty. A substation heats in January and makes hot water in July; an
air-handling coil is a heater at −20 °C outside and a cooler at +30 °C. Size it for one of those and
it is wrong in the other, usually in a direction nobody checks.

`scenarios` names the cases. Every case is solved, every component is sized to cover **all** of them,
and the plant you get is one plant — not one per case.

## Writing the cases

Name them once, at the top of the file with the other whole-file lines:

```fluidscript
scenarios winter summer
```

Then any parameter may state one value per case, in brackets, **in that order**:

```fluidscript
HX1 heat_exchanger power=[30, 10]
N3  node t=[30, 40]
PU1 pump                            # no list: one pump serving both cases
```

`HX1` is 30 kW in `winter` and 10 kW in `summer`. `N3` is 30 °C then 40 °C.

**A plain value is not a short list.** `PU1 pump` and `power=30` mean the same thing in every case,
which is what they already meant — so nothing you have written today changes, and a file with no
`scenarios` line behaves exactly as it always has.

**The order is the contract.** A list binds to the `scenarios` line by position and by nothing else,
so swapping two names there silently reassigns every list in the file. Nothing is matched by name and
nothing is padded: if you declare two cases and write three values, that is an error
([`FS1540`](diagnostics.md)), not a guess.

Each element is an ordinary value, so units, expressions and curve references all work inside one:

```fluidscript
HX1 heat_exchanger power=[30 kW, 10 kW] in.t=[70, 55]
```

## Which case the drawing shows

`design` picks it:

```fluidscript
scenarios winter summer shoulder
design summer
```

The canvas, the numbers on it, a static export and the starting state of a run are all `summer`'s.
**`design` chooses nothing about size** — every case was sized for, whichever one you are looking at.
There is no default, because a first column is a position and not a decision: declare scenarios
without a `design` line and you get [`FS1543`](diagnostics.md).

## How a component is sized across cases

Not by picking a winning case and taking its whole component. That would be wrong the moment two
cases disagree about different parts — and they usually do. Each size is taken across the cases on
its own:

| | Sized by | Also checked in |
|---|---|---|
| Pipe | the largest flow | — |
| Heat exchanger | the largest UA | — |
| Pump | the case needing the most head at its flow | every case: is it on the curve? |
| Control valve | Kv from the largest-flow case | every case: its authority, and whether the lightest case is within its turn-down |

The reason is the flow trap. A chilled side running 7/12 °C carries 1.91 kg/s for 40 kW; a heating
side at 45/35 °C carries 1.20 kg/s for 50 kW. **The smaller duty has the larger flow.** Size the pipe
from the exchanger's winning case and it is 60 % short in summer.

Every sized value says which case decided it, so a report reads `PU1.head — sized at winter, 4.2 m`.

## What you get back

The sizing report says which case decided each size, and what the pass cost:

```
=== scenarios  2 cases: winter, summer — design winter
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
year.

"merge — 2 rounds" is the pipeline going round twice. Sizes are coupled: a larger pipe drops less
pressure, so the pump needs less head, so the valve sees a different authority. The second round
re-sizes every case against the merged plant and confirmed the first; if a plant is still changing
after four rounds you get the last merge and a note saying so.

## What a list is not

- **Not a time series.** Cases are unordered and nothing interpolates between them. A duty that
  changes through a day is a [`schedule`](schedule.md).
- **Not a solve mode.** With `fluid static` you get an ordinary steady solve, N times. With
  `fluid dynamic` the run starts from `design`'s case.
- **Not a search.** A case only exists if you wrote it — see below.

## The case you did not write

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

```
FS2314  'REC' carries no duty and no flow in any of the 2 scenarios (winter, summer), so nothing
        sizes it. If it exists to serve two demands that peak in different cases, the case where
        both are on is not in the list.
```

It reports the shape, not a diagnosis — it cannot know *which* case you are missing, because that
depends on how one load relates to another and the model does not carry that. It is a warning rather
than an error, because a plant may legitimately carry a standby component no stated case uses.

So: name a shoulder case whenever two loads can be on at once.

## How well a valve controls

A control valve gets its Kv like any other size — the largest any case asked for. Its **authority**
is different: it is not a size but a reading of how well that valve will control, the share of the
branch's pressure drop the valve takes when fully open. So it is read off the merged plant, in every
case, and the report shows the **lowest**, naming the case:

```
    CV1.authority               0.692107  lowest in summer
    CV1.kv                       10 m3/h  summer
```

On one plant the cases normally agree to within a percent: both the valve's drop and the rest of the
branch's grow with the square of the flow, so a lighter case lowers both by the same factor. They
disagree when something you stated differs between cases. With `dp=[5, 60]` on the exchanger, winter's
light branch wants the larger valve (Kv 16) and gets it; in summer that same valve sits in a branch
dropping 60 kPa and reads 0.23 — and you get [`FS4006`](diagnostics.md):

```
note  CV1 has authority 0.23 in summer, below 0.25. It will behave as a switch rather than a
      control valve: the branch's own resistance dominates until the valve is nearly shut.
```

**Turn-down.** A valve also has a smallest flow it can still control: its rangeability — 50:1 for
equal percentage, 33:1 linear, 20:1 quick opening — reduced to `R·√a` once installed, because the
valve sees more of the drop as it closes. When the lightest case asks for less than that, you get
[`FS4013`](diagnostics.md):

```
FS4013  'CV1' must pass 0.024 kg/s in winter and 1.906 kg/s in summer, 1.3 % of its heaviest flow.
        An equal-percentage valve at authority 0.66 controls down to about 2.5 % (50:1 × √0.66), so
        in winter it will open and shut rather than modulate. Give the light case a smaller valve in
        parallel, or split the duty.
```

The `√a` part is FluidScript's own reasoning from the Kv law, not a figure from a standard; the
rangeabilities are manufacturers'. A case in which the valve carries no flow at all is left out of
both checks — a shut valve is not controlling.

## See also

- [`design`](design.md) — which case the drawing shows
- [`schedule`](schedule.md) — a duty that changes with the clock, in a run
- [`curve`](curve.md) — a value that follows a driver
