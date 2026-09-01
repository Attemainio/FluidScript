---
id: 56-console-log
title: Console log
tier: 50-frontend
status: reviewed
owns: [log panel, message grouping, filtering, phrasing rules, log lifecycle]
depends_on: [16-diagnostics, 44-diagnostics-contract, 55-design-system]
traces_to: [R-24, R-27]
open_questions: 0
last_review_pass: 2
---

# Console log

## Purpose

The brief asks for a "command line style pleasant looking log about system warnings — 'approaching
freezing point', 'pressure is too low'". It is the surface where the tool tells the designer what it
noticed, and it is the difference between a diagram that is silently wrong and one that says why.
"Pleasant looking" is a real constraint: a log that reads like a compiler dump gets collapsed on day
one and never opened again.

## Responsibilities

**Owns.** The log panel, grouping, filtering, phrasing rules, and the log's lifecycle.

**Explicitly does not own.** Diagnostic content ([`16-diagnostics`](../10-language/16-diagnostics.md)),
the wire shape ([`44-diagnostics-contract`](../40-api/44-diagnostics-contract.md)), editor squiggles
([`52-editor`](52-editor.md)), colours ([`55-design-system`](55-design-system.md)).

## Shape

A collapsible bottom panel, ~120 px open, showing the most recent entries with the newest at the
bottom — terminal convention, and it means the interesting line is where the eye already is.

```
┌──────────────────────────────────────────────────────────────────────────┐
│  ● all   ○ warnings   ○ errors            solved in 14 ms      ⌄        │
├──────────────────────────────────────────────────────────────────────────┤
│  ·  N2      Using 'N2' as the pressure datum.                            │
│  ▲  3WV     Authority is 0.19 — below 0.25. Control will be poor.        │
│  ▲  N1      Approaching freezing point: 2.1 °C, 2.1 K above freezing.    │
│  ✕  PU1     'PU1' is not connected to anything.                          │
│  ✓          Solved · 4 iterations · 14 ms                                │
└──────────────────────────────────────────────────────────────────────────┘
```

| Column | Content |
|---|---|
| Glyph | `✓` ok · `·` info · `▲` warning · `✕` error, coloured from `--status-*` |
| Component | The component name, clickable — selects it on canvas and in the editor |
| Message | The diagnostic text |

**No timestamps, no severity words, no codes by default.** A timestamp on a log that regenerates once per
debounce interval is noise. The code appears on hover and in the expanded view, where an agent or a support
conversation needs it (`R-29`).

**The component name is the primary affordance.** Clicking it selects the component in both panes
([`54-interaction-and-writeback`](54-interaction-and-writeback.md)), which turns the log from a list of
complaints into navigation.

## Lifecycle — the hard part

The log's content is regenerated on every compile, i.e. once per debounce interval while typing. Treating it as an
append-only terminal produces thousands of duplicate entries; clearing and refilling it makes it flash
on every keystroke and lose scroll position.

**The log is a *view of the current state*, not a history.**

- Entries are keyed by `code` + `component` and **reconciled**, not replaced.
- An entry present in both the old and new set stays put — same position, no re-render, no flash.
- A new entry animates in; a resolved one animates out over 150 ms.
- Scroll position is preserved across updates.
- The panel does not auto-scroll unless already at the bottom — the terminal convention, and it stops
  the log yanking itself away while the user reads.

This is why the static log is a reconciled list rather than a stream. During a transient, stable
occurrence ids reconcile the current state while retaining each started/cleared simulation-time
interval; clearing a warning closes its interval but does not erase it (`43`, `44`, `D-30`).

## Grouping

`FS4001` firing on forty nodes must be one line
([`44-diagnostics-contract`](../40-api/44-diagnostics-contract.md)'s deduplication):

```
▲  40 nodes   Approaching freezing point.                              ⌄
   └─ N1 2.1 °C · N2 2.4 °C · N3 2.0 °C · … 37 more
```

Collapsed by default, expandable. Without this, one systemic condition buries everything else.
The deterministic threshold is **three** occurrences with the same code and severity. One or two stay
as individual lines (so the two `FS2107` dead ends in the syntax example remain distinct); three or
more collapse into one group. The expanded rows use each flat wire diagnostic's `component` and
already formatted `message`, which carries values such as 2.1 °C—there is no separate value array.

## Filtering

Three states — all / warnings and errors / errors only — plus a text filter. Default is **warnings and
errors**: info-level entries are mostly inference notices (`FS1510`, one per inferred component), and
on the syntax example that is six lines about things working correctly.

Info is one click away, and the count is always visible (`3 info`) so nothing is hidden without a trace.

## Phrasing

[`16-diagnostics`](../10-language/16-diagnostics.md) owns the message text; this document owns how the
log presents it.

1. **The component name is a column, not part of the sentence.** The message says "Authority is 0.19",
   not "3WV: authority is 0.19" — the name is already in its own column, and repeating it doubles the
   line's length.
2. **Values in the message use the script's canonical units** ([`16`](../10-language/16-diagnostics.md)'s
   rule 8). A user who wrote `power=30` should never read about 30 000 W.
3. **State the margin, not only the condition.** "Approaching freezing point: 2.1 °C, 2.1 K above
   freezing" tells the designer how much room they have. "Approaching freezing point" alone does not.
4. **One line by default.** Longer explanations expand.
5. **Success is stated.** A run that produced no warnings shows `✓ Solved · 4 iterations · 14 ms`, not
   an empty panel. An empty panel is ambiguous between "fine" and "nothing happened".

Rule 5 is small and matters: the log's default state should be a green tick, which makes a warning
appearing an event rather than the norm.

## Status line

The right side of the header always shows the current state:

| State | Shows |
|---|---|
| Idle, solved | `solved in 14 ms` |
| Compiling | `solving…` with a subtle indicator |
| Errors | `3 errors` in `--status-error` |
| Not solved | `not solved — 1 error` |
| Transient running | `t = 184 s / 600 s` |
| Disconnected | `offline` |

This is the one always-visible piece of feedback, and it is where `timings`
([`42-rest-contract`](../40-api/42-rest-contract.md)) surfaces. A user who says "it feels slow" can
read the number.

## Invariants

1. The log is a reconciled view of the current diagnostics, never an append-only history (except during
   a transient run).
2. An unchanged entry does not move or re-render between compiles.
3. Scroll position survives an update.
4. Every entry with a component links to it in both panes.
5. Duplicate `code`+`component` pairs are impossible — grouping is applied before rendering.
6. A clean solve shows a success line, never an empty panel.
7. The log makes no network requests.
8. Every code shown resolves in `/api/v1/metadata`.

## Error cases

| Situation | Behaviour |
|---|---|
| Hundreds of diagnostics | Virtualised list; grouping keeps the visible count small |
| Compile failed entirely | `✕ Could not compile` plus the parse errors |
| Server unreachable | `offline`; the last entries stay, dimmed |
| A code with no metadata entry | Show the message without the code link; log a console warning — a bug in the registry |
| Transient produced no frames | `✕ Simulation did not start` plus the reason |

## Worked example

The brief's example, unmodified, on first compile:

```
┌──────────────────────────────────────────────────────────────────────────┐
│  ○ all   ● warnings   ○ errors               not solved — 1 error   ⌄    │
├──────────────────────────────────────────────────────────────────────────┤
│  ✕  PU1     'PU1' is not connected to anything.                          │
│  ▲  N1      'N1' is a dead end. Set t, p or flow to make it a boundary.  │
│  ▲  N3      'N3' is a dead end. Set t, p or flow to make it a boundary.  │
│                                                     6 info               │
└──────────────────────────────────────────────────────────────────────────┘
```

Three lines out of nine diagnostics. Clicking `PU1` selects it on canvas — where it is visibly floating
with no arrows — and highlights line 6 in the editor.

**The counts come from [`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md), which owns
this script's diagnostic set**, and no document may state a different one: one `FS1507`, two `FS2107`,
and **six** `FS1510` inference notices — three from I1, one from I2, two from I3 (the disconnected
pump's ports). The six are
suppressed here, one click away. An earlier draft of this panel showed four info entries and a
`FS2202` port warning that is not in the set at all, which is the kind of drift that only shows up when
two documents are read together.

After the user connects the pump and adds boundaries — arriving at the cooling loop — the same panel
becomes:

```
│  ✓          Solved · 4 iterations · 14 ms                                │
│                                                     4 info               │
```

Four info now rather than six: the cooling loop declares `N1` and `N3` as boundary nodes, so I1 fires
only for `N2` and I3 not at all.

One green line. That transition — from two problems to a tick — is the log's whole job, and it is why
rule 5 exists: without the success line the user would see an empty box and not know whether the tool
had run.

During the M4 transient, the same panel:

```
│  ▲  N1      Approaching freezing point: 2.1 °C.        t = 92–147 s      │
│  ✓          Running · t = 184 s / 600 s                                  │
```

The time range comes from [`44-diagnostics-contract`](../40-api/44-diagnostics-contract.md)'s
occurrence lifecycle: a warning that appeared at 92 s and cleared at 147 s remains real history even
when it is no longer active.

## Acceptance criteria

- [ ] An unchanged entry does not move or re-render across ten consecutive compiles.
- [ ] Scroll position survives an update.
- [ ] Forty instances of one code render as one grouped, expandable line.
- [ ] Two same-code occurrences remain separate; the third groups them, and expanded rows reproduce
      each occurrence's component-specific formatted message.
- [ ] Clicking a component name selects it on canvas and in the editor.
- [ ] A transient occurrence remains listed with its exact start/end time after clearing and
      reconstructs correctly while scrubbing.
- [ ] Copy as text includes code, severity, component, message, and transient interval without canvas
      or colour dependence.
- [ ] A clean solve shows the success line with iterations and elapsed time.
- [ ] The default filter hides info and shows its count.
- [ ] 500 diagnostics render without dropping frames.
- [ ] The log makes no network requests, asserted.
- [ ] The brief's example produces exactly the two visible lines above.

## Open questions

None. Static diagnostics reconcile current state; transient occurrences retain time intervals. v1
provides accessible copy-as-text only. A formatted report is a future product feature, not an exporter
side effect (`D-30`).
