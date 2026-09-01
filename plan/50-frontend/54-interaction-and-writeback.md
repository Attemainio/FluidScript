---
id: 54-interaction-and-writeback
title: Interaction and write-back
tier: 50-frontend
status: reviewed
owns: [hover readout, selection, on-canvas value editing, the write-back loop, undo integration]
depends_on: [17-formatting-and-round-trip, 42-rest-contract, 52-editor, 53-canvas-renderer]
traces_to: [R-23, R-24, R-25]
open_questions: 0
last_review_pass: 2
---

# Interaction and write-back

## Purpose

`R-25`, and the feature that makes FluidScript feel like a tool rather than a compiler: change a valve
on the diagram and the script changes to match. Everything hard about it is a consequence of principle
P5 — the script is the source of truth, so a canvas edit is a *text* edit, and it must be as surgical
as one made by hand.

## Responsibilities

**Owns.** Hover readout, selection, on-canvas property editing, direct-manipulation previews, the
write-back loop, and undo integration. It does not provide free placement or symbol dragging in v1
(`D-29`).

**Explicitly does not own.** The edit primitives
([`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md)), the edit endpoint
([`42-rest-contract`](../40-api/42-rest-contract.md)), rendering
([`53-canvas-renderer`](53-canvas-renderer.md)).

## Hover

`R-23`. Hovering a component shows its resolved state without a click, from the model already in the
store — no network call ([`51-frontend-architecture`](51-frontend-architecture.md)'s invariant 7).

```
┌─ 3WV · three_way_valve ────────────────┐
│                                        │
│  Kv          1.6      sized            │
│              Kv 1.6 — authority 0.57   │
│              at 0.241 l/s              │
│  position    0.412    sized            │
│  authority   0.57     solved           │
│                                        │
│  ── state ─────────────────────────    │
│  flow        0.2392 kg/s               │
│  Δp          29.4 kPa                  │
│  in          50.0 °C                   │
└────────────────────────────────────────┘
```

Design rules:

- **`stated` / `sized` / `default` is shown for every parameter** — the three values of the model
  contract's `source` field, and no others; there is no `solved` source, because a solved *state* is a
  different row in the card from a *parameter* — with the basis for the latter two
  ([`26-model-contract`](../20-core-domain/26-model-contract.md)). This is the field that turns a
  number into a claim the user can evaluate.
- **Warnings appear in the card**, not only in the log (`R-24`).
- **Appears after 150 ms**, disappears immediately. Long enough not to flicker while the cursor
  crosses the canvas, short enough to feel responsive.
- **Follows the cursor, flips at viewport edges**, never covers the hovered component.
- **An inferred component's card says so**, and offers "write this into the script"
  (`IScriptEditor.Materialize`).

Hovering a *connection* shows its flow, velocity, Reynolds number, and pressure drop — the numbers a
designer wants about a pipe run, and the reason connections are hoverable at all.

## Selection

Click selects; the selected component is emphasised, and the editor scrolls to and highlights its
declaration line. Shift-click adds. Escape clears.

**Selection is bidirectional**: placing the caret on a declaration line in the editor selects that
component on the canvas. This is the cheapest possible way to make two panes feel like one document,
and it works from the `SymbolMap` ([`15-semantic-model`](../10-language/15-semantic-model.md)) that
already exists.

## On-canvas editing

Editing a value on the diagram. Two mechanisms, both landing in the same write-back path.

### Inline editing

Double-click a value in the hover card (or on a label at high zoom) to edit it in place. Enter commits,
Escape cancels.

### Drag handles

Direct manipulation for the values that have a natural one:

| Component | Handle | Range |
|---|---|---|
| `valve` | Position slider on the symbol | 0…1 |
| `pump` | Speed slider | 0…1.2 |
| `pipe` | Diameter stepper (± through the catalogue) | Catalogue entries ([`27`](../20-core-domain/27-component-catalog.md)) |
| `heat_exchanger` | Duty drag along an axis | Component's range |

**Dragging previews live, commits on release.** During the drag the canvas shows the value updating
and the affected numbers greying out — they are stale until the next solve. On release, one write-back
and one solve. Solving per drag frame would mean dozens of round trips per gesture.

**A drag over a catalogue value snaps to entries.** A pipe diameter slider that passes through 29.4 mm
is offering something unbuyable.

## The write-back loop

```
User commits an edit on the canvas
   │
   ├─► POST /api/v1/edit { documentRevision, script, operation }
   │
   ├─► server echoes documentRevision + TextEdit[] + the re-solved model
   │     · if echoed revision != current editor revision, discard and re-send from current text
   │
   ├─► apply the edits to the CodeMirror document as ONE transaction
   │     · annotated so the debounce pipeline ignores it (the model is already in hand)
   │     · cursor and selection preserved
   │     · one undo step
   │
   └─► draftStore.set(model)
         · static mode: canvas re-renders the draft
         · active run: simulation canvas remains bound to its immutable RunSnapshot
```

**Three properties make or break this:**

1. **One transaction, one undo step.** `Ctrl+Z` after dragging a valve must restore both the script and
   the diagram in one press. Applying edits individually gives one undo step per edit and a confusing
   history.
2. **The edit does not retrigger the debounce compile.** The response already carries the re-solved
   model. The transaction is annotated as canvas-originated and the debounce listener skips it —
   otherwise every canvas edit costs two solves and the second can arrive out of order.
3. **The cursor does not move.** The user may be typing elsewhere. CodeMirror maps positions through a
   transaction automatically; the requirement is to use one transaction rather than replacing the
   document ([`52-editor`](52-editor.md)'s invariant 6).

When a run is active, a successful edit updates only the draft model and displays “restart to apply.”
It neither blocks, cancels, nor mutates the run. Any draft/run identity leak is an isolation failure:
stop playback at the last verified frame and report the contract error (`D-22`).

## Undo model

| Action | Undo restores |
|---|---|
| Typing | Editor's native undo |
| Canvas value edit | The script text; the model follows on re-solve |
| Quick fix | One text edit |
| Format | One text edit |

## Invariants

1. A canvas edit changes only the intended span; every other byte is unchanged
   ([`17`](../10-language/17-formatting-and-round-trip.md)'s invariant 3).
2. A canvas edit is one undo step.
3. A canvas edit does not move the caret or lose the selection.
4. A canvas edit does not trigger a debounce compile.
5. Hover and selection make no network requests.
6. Editing an inferred component is refused with an explanation and an offer to materialize it.
7. A drag previews without solving; exactly one solve occurs on release.
8. No edit path can produce a script that fails to parse
   ([`17`](../10-language/17-formatting-and-round-trip.md)'s `FS1606`).
9. An edit during an active transient changes draft state only and does not wait for, cancel, or alter
   the run snapshot.
10. Text edits are applied only to the exact document revision from which their spans were computed.

## Error cases

| Situation | Behaviour |
|---|---|
| Edit endpoint fails | Revert the preview, keep the script, show a toast |
| Edit would break the script (`FS1606`) | Refused server-side; nothing applied |
| The document changed while an edit was in flight | Echoed revision differs; discard response without applying model or spans, then re-send with current revision and text |
| Editing an inferred component | Card explains and offers "write into script" |
| Value outside the parameter's range | Clamp during drag; on inline edit, accept and let the diagnostic report it |
| Draft/run identity mismatch after an edit | Stop the run at the last verified frame and report the isolation failure |

The last row is a real asymmetry and is intentional: a drag *cannot* express an out-of-range intent, so
clamping is right; typing a number *can*, and refusing it would prevent a user from stating a value
they know is unusual. Accept, and let `FS1306` say so.

## Worked example

The user drags `3WV`'s position handle from 0.41 to 0.65 and releases.

```
t=0ms      Mouse down on the handle. Drag begins.
           Canvas enters preview: the valve fill animates with the drag; every
           dependent number (flow, Δp, temperatures) greys out as stale.
           No network activity.

t=0-800ms  Drag. The handle follows the cursor, clamped to 0…1.

t=800ms    Release at 0.65.
           POST /api/v1/edit {
             script: "…",
             operation: { kind: "setParameter", component: "3WV",
                          parameter: "position", value: 0.65, unit: null } }

t=812ms    200 {
             edits: [ { span: { start: 100, length: 0 },
                        newText: " position=0.65" } ],
             model: { … re-solved … } }

t=813ms    CodeMirror applies one annotated transaction. The line becomes:
             3WV three_way_valve position=0.65                # auto size
           Caret unmoved. One undo step. The debounce listener skips this transaction.

t=814ms    modelStore.set(model). Canvas un-greys with the new values:
             flow 0.238 → 0.191 kg/s,  Δp 29.5 → 11.2 kPa,  N2.t 20.0 → 21.4 °C

t=814ms    The log gains: "FS4006 3WV authority is now 0.19 — below 0.25."
```

The `FS4006` line appears *because* of the edit: opening the valve from 0.41 to 0.65 cuts its pressure
drop, and with it the valve's share of the branch. At the design position the authority clears 0.25 and
no warning fires — which is why the hover card above shows none.

**The comment stays in its original column**, so the line is now misaligned. That is
[`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md)'s deliberate
behaviour: realigning would change bytes the user did not touch. If they want alignment, `Shift+Alt+F`.

**`position` was previously solved and is now stated** — the parameter's `source` flips from `sized`
to `stated`, the symbol's sized-marker disappears, and the valve is now under the user's control rather
than the tool's. That transition is `D-02`'s whole point, and the canvas showing it is what makes the
model comprehensible.

**Which raises the case this document has to handle and previously did not.** On the cooling loop
`3WV.position` is not merely sized — it is a **promoted solver unknown**, promoted by `HE1 in=20`
([`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md)). Stating it while that
constraint stands is two things setting one unknown, and the circuit comes back over-specified with
`FS2210` naming both.

So a drag on a promoted parameter is not an ordinary edit, and the interaction must say so **before**
the round trip rather than presenting an error afterwards:

| Situation | Behaviour |
|---|---|
| Parameter is sized, nothing promotes it | Drag freely; write back on release, as above |
| Parameter is a **promoted unknown** | The handle is drawn in the promoted style and its tooltip names the constraint driving it: *"set by `HE1 in=20`"*. Dragging offers **"release `HE1 in=20` and set position here"** as a single edit that removes one line and adds another |
| User declines | No edit; the handle springs back |

One edit, one undo step, and the script stays solvable at every intermediate state — which is what
principle P5 requires of a canvas that owns no model of its own. Presenting the drag, accepting it, and
then showing `FS2210` would leave the user holding a broken script they did not knowingly write.

In the M4 demand-step loop, the same edit changes the draft only. The active run continues with its
captured controller and actuator values; the playback bar names the source hash and offers Restart.

## Acceptance criteria

- [ ] The worked example produces exactly the one text edit shown.
- [ ] `Ctrl+Z` after it restores the script and the diagram in one press.
- [ ] The caret does not move during a canvas edit, verified with the caret mid-document.
- [ ] A canvas edit triggers exactly one compile, not two.
- [ ] Dragging produces zero network requests until release.
- [ ] Hover produces zero network requests.
- [ ] Editing an inferred component is refused with the materialize offer.
- [ ] Selecting on canvas highlights the editor line, and placing the caret selects on canvas.
- [ ] A failed edit reverts the preview and leaves the script untouched.
- [ ] During a transient, an edit updates draft diagnostics without blocking or changing playback.
- [ ] A forced draft/run identity leak stops playback at the last verified frame.

## Open questions

None. Inline edits use the server-owned edit primitives so formatting has one implementation. Manual
placement and canvas deletion are post-v1 research: v1 edits properties on the canvas and edits
topology in the script (`D-29`).
