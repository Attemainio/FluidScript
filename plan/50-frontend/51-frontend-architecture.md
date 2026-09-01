---
id: 51-frontend-architecture
title: Frontend architecture
tier: 50-frontend
status: reviewed
owns: [React/Vite structure, state management, the debounce pipeline, API layer, app shell layout]
depends_on: [26-model-contract, 42-rest-contract, 43-realtime-contract]
traces_to: [R-19, R-21, R-27, R-38, R-41, R-42, R-50, R-51]
open_questions: 0
last_review_pass: 2
---

# Frontend architecture

## Purpose

The shape of the React application: how the script gets to the server, how the model gets back, and
where each piece of state lives. The organising constraint is `R-21`'s debounce loop — everything the
user sees is downstream of one pipeline that runs once per debounce interval, and the architecture is mostly about
making that pipeline correct under interruption.

## Responsibilities

**Owns.** Project structure, state management, the debounce pipeline, the API layer, and the app shell.

**Explicitly does not own.** The editor ([`52-editor`](52-editor.md)), the canvas
([`53-canvas-renderer`](53-canvas-renderer.md)), write-back
([`54-interaction-and-writeback`](54-interaction-and-writeback.md)), visual design
([`55-design-system`](55-design-system.md)), the log ([`56-console-log`](56-console-log.md)).

## Structure

Feature folders, not type folders. A feature owns its components, hooks, state, and styles together.

```
frontend/src/
├── main.tsx
├── App.tsx                     the shell: layout, theme provider, error boundary
├── features/
│   ├── editor/                 CodeMirror wrapper, highlighting, completion, squiggles
│   ├── canvas/                 renderer, layout engine, symbols, interaction
│   │   ├── layout/             placement and routing — the only place Math lives (04)
│   │   └── symbols/            generic interpreter for Core SymbolDefinition primitives
│   ├── log/                    console-style warning stream
│   ├── playback/               transient controls: run, pause, scrub, timeline
│   ├── files/                  file lifecycle, conflicts, recovery (58)
│   └── theme/                  theme switching and persistence
├── api/
│   ├── client.ts               REST calls, typed
│   ├── transient.ts            WebSocket client, frame reconstruction
│   ├── transient.worker.ts     delta/checksum/history/render preparation
│   ├── layout.worker.ts        layout and routing
│   └── types.ts                wire types generated from the shared JSON Schemas (`42`)
├── state/
│   ├── draftStore.ts           last-good draft model, diagnostics, source revision
│   ├── runStore.ts             immutable snapshot id, verified playback state, status
│   └── uiStore.ts              theme, panel sizes, canvas viewport
└── design/                     tokens, primitives, icons (55-design-system)
```

## State — four domains, deliberately separated

| Store | Holds | Lifetime | Persisted |
|---|---|---|---|
| **Script** | The source text | The editor owns it | IndexedDB recovery through `58`, never as a named save |
| **Draft model** | Last successful static model, diagnostics, solve status, source hash | Replaced per draft response | No |
| **Run** | Snapshot/source ids, reconstructed verified state, checkpoints, terminal reason | One explicit run | No |
| **UI** | Theme, panel sizes, viewport | Session | localStorage |
| **Workspace** | Open documents, tab order, `activeDocumentId` | Application | localStorage (ids and names only) |

**The script lives in the editor, not in a global store.** CodeMirror owns its document, its undo
history, and its cursor; mirroring that into React state means two sources of truth and a class of bug
where an undo restores text the store does not know about. React reads the document when it needs to
send it, and never writes it except through an explicit edit transaction
([`54-interaction-and-writeback`](54-interaction-and-writeback.md)).

**The model store keeps the last *successful* model.** When a compile returns errors and no model, the
canvas keeps drawing the previous one (`R-05`). Blanking the diagram on every transient syntax error —
which is most keystrokes — makes the tool feel broken.

**Script, Draft model and Run are per document; UI and Workspace are global** (`D-39`). The first
three are keyed by `documentId`, which is what lets a background tab keep a running transient while
the visible one is recompiled. Viewport is deliberately in the *global* UI store rather than per
document: a user switching tabs to compare two circuits wants the same view of both, and restoring a
per-tab pan/zoom makes the comparison impossible.

**Only the active document is fed by the debounce pipeline.** Switching cancels any in-flight compile
for the outgoing document rather than letting it land — its result would be written to a store nothing
is rendering, and it would race with the incoming document's first compile for the single-flight slot
that invariant 3 guards.

A background document's Run store keeps being updated by its own worker
([`43-realtime-contract`](../40-api/43-realtime-contract.md)'s detached-run rules). That is the one
place where a non-active document does work, and it is deliberate: stopping it would discard the run.

State library: **Zustand**. The state is small, mostly flat, and mutated from outside React
(WebSocket frames). Redux is ceremony at this size; Context re-renders too broadly for a canvas
receiving frames at 1 Hz. Per-document stores are slices keyed by `documentId` in one store rather
than one store per document, so a tab close disposes its slice in a single action and cannot leak a
subscription.

## The debounce pipeline

`R-21`'s idle debounce, and the reason for most of this document. Its value is measured rather than
fixed (`D-49`).

```
keystroke
   │
   ├─► CodeMirror updates its document          (immediate, always)
   │
   └─► debounce (measured, D-49)
          │
          ├─► abort the in-flight request (AbortController)
          ├─► POST /api/v1/compile { sessionId, script }
          │
          ├─► on success:  modelStore.set(model)   → canvas, hover, log re-render
          │                editor.setDiagnostics(diagnostics)
          ├─► on error:    keep the last model, show the diagnostics
          └─► on abort:    do nothing (a newer request is already running)
```

**Client-side aborts matter as much as the server-side cancellation**
([`41-api-architecture`](../40-api/41-api-architecture.md)). Without `AbortController`, responses can
arrive out of order and a stale response overwrites a newer one — the model flickers back to an older
state for no visible reason. This is the classic race in any debounced editor and it is invisible
until it happens on a slow connection.

**The debounce is measured, not chosen** (`D-49`). It lives in one constant, but that constant is set
from a benchmark on the reference environment and recorded as a baseline — it is not a number picked
in advance and left alone. Two bounds fix the range it may take:

- **Floor: typing cadence.** It may never be short enough to fire inside an average typist's inter-key
  interval, about 200 ms at 40 wpm. Below that it is per-keystroke behaviour wearing a debounce's
  name, and it starts reporting half-typed tokens — *a heat_exchanger has no 'pow'* while `power` is
  still being typed. Note what does **not** set this floor: server load, which
  [`41`](../40-api/41-api-architecture.md) already handles by cancelling a superseded compile within
  one solver iteration, and squiggle flicker, which [`52`](52-editor.md) already handles by keeping
  diagnostics across the gap. Both of the obvious objections to a shorter debounce are answered
  elsewhere; the one that survives is about half-written words.
- **Ceiling: whatever `D-48`'s end-to-end gate leaves** once the compile for that script size has been
  measured. At the 14 ms round trip [`41`](../40-api/41-api-architecture.md)'s worked example
  describes, the 250 ms syntax-tour gate leaves well over 200 ms of debounce; at the 150 ms p95
  budgeted for 200 declarations, the 400 ms gate leaves about 250 ms.

**It does not adapt at runtime**, and that was the first design proposed. Latency that moves with
recent load is worse than latency that is consistently longer — a user calibrates to a rhythm, and one
that shifts reads as the tool being erratic rather than as the tool being quick. It also makes a bug
report unreproducible, since the reporter cannot say what the debounce was at the time. The adaptive
`POST /validate` phase below is a different mechanism and is unaffected: it adds a *phase* under
sustained slowness, it does not move the debounce.

### Two-phase feedback

Syntax errors are known in ~2 ms; a solve takes ~15 ms. For a large script the gap grows.

- **Immediate (0 ms):** client-side lexical highlighting — no round trip.
- **Debounced (one debounce interval):** `POST /compile` for diagnostics and the model.

The client enables the 100 ms `POST /validate` phase when the rolling p95 of the last 20 completed
`/compile` requests exceeds 100 ms. It disables the phase after 50 consecutive compiles below 75 ms,
preventing threshold chatter. Validation diagnostics are tagged with the document revision and are
replaced only by a full compile for that same or a newer revision (`44`). This measured two-phase path
does not introduce a streaming protocol.

## Transient runs

Separate from the debounce pipeline, deliberately.

Under `D-06`, editing and steady solve stay on REST while a transient uses WebSocket streaming.

```
User presses Run
   │
   ├─► open WebSocket /ws/v1/transient
   ├─► send { type: "start", script, settings }
   ├─► receive `base` → transfer the immutable baseline to transient.worker
   ├─► receive `frame`… → worker verifies ids/order/checksum, applies deltas and checkpoints
   │                      → worker prepares one coalescible SVG commit
   │                      → playback timeline advances
   └─► receive `end`/`error` → close, keep the buffer for scrubbing
```

**Editing the script during a run does not cancel it** ([`41`](../40-api/41-api-architecture.md)). The
debounce pipeline keeps running and updates the *editor's* diagnostics, but the canvas stays bound to
the running simulation until it ends or the user stops it. Two sources wanting the canvas at once is
resolved by an explicit mode flag in the model store, not by whichever wrote last.

**Checkpoint every 60 frames** for backward scrubbing (`43`): ten snapshots per 600-frame run, each a
full state, so scrubbing to any t replays at most 59 deltas and remains within `07`'s retention budget.

## App shell

```
┌────────────────────────────────────────────────────────────┐
│  toolbar: file · run · stop · theme · export               │
├────────────────────────────────────────────────────────────┤
│  tabs: plant_01 ● │ substation │ ahu-test ▸        (D-39)   │
├──────────────────────┬─────────────────────────────────────┤
│                      │                                     │
│   editor             │   canvas                            │
│   (resizable)        │   (fills)                           │
│                      │                                     │
│                      ├─────────────────────────────────────┤
│                      │   playback (visible during a run)   │
├──────────────────────┴─────────────────────────────────────┤
│  log (collapsible, ~120 px)                                │
├────────────────────────────────────────────────────────────┤
│  status: ● Converged · steady solve · plant_01     (R-51)   │
└────────────────────────────────────────────────────────────┘
```

A tab shows its display name, a dirty marker (`●`), and a running marker (`▸`) when it owns an active
transient. The running marker is what makes `D-39` legible: a user needs to see that a background tab
is still working, or a run continuing after a switch is indistinguishable from a leak.

### Status line

One line, always present, stating what the solver is doing (`R-51`):

| State | Shown | When |
|---|---|---|
| Converging | ◐ *Converging* | A steady solve or a transient run is in progress |
| Converged | ● *Converged* | The last computation finished within tolerance |
| Failed | ▲ *Did not converge* | Iteration cap, a stop condition, or a non-finite state |
| Idle | ○ *Not solved* | A parsed draft with no solve attempted |

**It names which computation it describes**, because there are three sources and they disagree
routinely: a draft compile, a steady solve, and a specific transient run. "Converged" with no subject
is unreadable in a workspace where a background tab is mid-transient and the visible one has a syntax
error. The line reads `Converged · steady solve · plant_01`.

**Shape and text carry the state; colour only reinforces it.** Green, amber and red are the obvious
encoding and are not sufficient on their own under `R-42` — each state has a distinct glyph and a
distinct word, and the automated check asserts the three are distinguishable in greyscale.

Status for a **background** run is reachable from its tab's running marker rather than shown in the
line, which always describes the active document. One line describing an invisible document's run
would be worse than no line.

Script left, drawing right — the brief's "renders natively next to the script". The split is
resizable and its position persists. Mobile is out of scope: this is a design tool for a desktop screen,
and pretending otherwise would compromise the canvas for a use case nobody has.

## Performance

| Concern | Approach |
|---|---|
| Canvas update on every frame | Worker produces keyed attribute commits; one `requestAnimationFrame` UI commit coalesces obsolete display frames |
| Large model diffing | Components are keyed by stable id ([`25-layout-hints`](../20-core-domain/25-layout-hints.md)); React reconciles by key |
| Hover | Local component state, never the global store — a hover must not re-render the tree |
| Frame application | Deltas/checksums/history/colour/geometry run in `transient.worker`; no frame calculation runs on the UI thread |
| Layout recomputation | `layout.worker` runs only when the topology hash changes, not when values change |

The last row is the important one: a transient run changes values 600 times and the topology zero
times. Recomputing layout per frame would make playback unusable, and it is the obvious accidental
implementation.

## Invariants

1. The script's source of truth is the CodeMirror document; React never holds a second copy.
2. A failed compile never clears the canvas.
3. At most one compile request is in flight; older ones are aborted client-side.
4. A stale response never overwrites a newer model.
5. No physics or unit conversion in the frontend beyond display formatting
   ([`04-engineering-standards`](../00-foundation/04-engineering-standards.md)).
6. Layout is recomputed only on a topology change.
7. Canvas interactions (pan, zoom, hover) never trigger a network request.
8. Draft and run stores share identifiers only, never a mutable model object.
8a. Script, draft-model and run state are keyed by `documentId`; no store holds a value belonging to
   more than one document (`D-39`).
8b. A tab switch cancels the outgoing document's in-flight compile and starts no work for it
   afterwards, except its own detached run's frame handling.
8c. The status line always names the computation and document it describes, and its three states are
   distinguishable without colour.
9. Worker failure, snapshot/sequence/shape/checksum mismatch, or non-finite state stops playback and
   prevents the suspect commit; ordinary draft edits do not.

## Error cases

| Situation | Behaviour |
|---|---|
| Network unavailable | Banner "Not connected"; the editor keeps working; the canvas holds the last model |
| Contract major-version mismatch | Refuse to render; tell the user to reload ([`26`](../20-core-domain/26-model-contract.md)) |
| WebSocket drops mid-run | Show "Simulation interrupted"; keep the frames received; offer Run again |
| Compile returns 500 | Show the correlation id; keep the last model |
| IndexedDB/localStorage unavailable | Editing continues; persistent warning and Download action; no false recovery/saved state |
| Model too large (`FS2502`) | Render topology; fetch states on hover instead |

## Worked example

A user types `4` in `power=3` → `power=34`, with a transient run already streaming:

```
t=0ms     CodeMirror inserts '4'. Lexical highlighting updates. No network.
t=0ms     The canvas keeps rendering simulation frames — unaffected.
t=300ms   Debounce fires. Previous request aborted (none in flight). POST /compile.
          (300 ms is the recorded default here, not a fixed constant -- see D-49.)
t=315ms   200 with the new model. modelStore.set().
          Canvas mode is `simulation`, so the canvas ignores it.
          The editor's squiggles update. The log shows the new diagnostics.
t=316ms   A "script changed — restart the simulation to see it" hint appears in playback.
```

**The canvas does not switch to the new static model**, because a running simulation is what the user
is watching. The hint tells them their edit is not reflected, which is honest and non-disruptive. The
alternative — swapping the canvas to the static model mid-run — would look like the simulation crashed.

## Acceptance criteria

- [ ] Typing continuously for 10 s produces at most one in-flight request at any moment.
- [ ] An artificially delayed response never overwrites a newer model.
- [ ] A latency fixture above 100 ms p95 enables the 100 ms validation phase; 50 requests below 75 ms
      disable it, and an older validation result never replaces newer diagnostics.
- [ ] A syntax error leaves the canvas showing the previous model.
- [ ] Layout is not recomputed during a 600-frame transient — asserted by a counting spy.
- [ ] Hover does not re-render the canvas tree.
- [ ] Theme and split position survive a reload.
- [ ] A dropped WebSocket keeps received frames and offers a restart.
- [ ] Frame application sustains 10 fps on a 200-component model.
- [ ] Thread instrumentation proves layout, frame reconstruction, colour/geometry preparation are off
      the UI thread and the SVG commit meets `07`'s budget.
- [ ] A corrupt frame or worker crash stops the run at the last verified frame; an invalid draft edit does not.

## Open questions

None. Zustand owns the small external stores; computed placements are prepared drawing state supplied
to both renderer and client-side export, with no v1 manual overrides. Layout/routing and transient
preparation always use Web Workers because responsiveness is a contract, not a later optimization
(`D-22`, `D-29`).
