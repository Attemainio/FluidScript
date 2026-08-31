---
id: 43-realtime-contract
title: Realtime contract
tier: 40-api
status: draft
owns: [WebSocket protocol, frame messages, backpressure, reconnection, run lifecycle]
depends_on: [26-model-contract, 33-transient-time-domain, 41-api-architecture]
traces_to: [R-19, R-12, R-40, R-41, R-45, R-50]
open_questions: 0
last_review_pass: 0
---

# Realtime contract

## Purpose

`D-06`'s streaming half. A transient run produces frames over seconds to minutes and playback must
begin before it finishes (`R-19`). This document is the protocol; its hardest question is what a frame
contains, because the obvious answer — the whole model — does not survive contact with a frame rate.

## Responsibilities

**Owns.** The WebSocket protocol, message shapes, backpressure, reconnection, and the run lifecycle.

**Explicitly does not own.** The transient solver
([`33-transient-time-domain`](../30-solver/33-transient-time-domain.md)), the model shape
([`26-model-contract`](../20-core-domain/26-model-contract.md)), playback UI
([`51-frontend-architecture`](../50-frontend/51-frontend-architecture.md)).

## Endpoint

`GET /ws/v1/transient` — upgraded to WebSocket. Same origin as the API in both environments
([`41-api-architecture`](41-api-architecture.md)), so no cross-origin socket handling.

**One run per connection.** A second `start` on a live connection is an error, not a queue. Multiple
runs means multiplexing, correlation ids, and per-run backpressure — none of which a single-user tool
needs.

## Messages

Every message is JSON with a `type` discriminator.

### Client → server

```jsonc
{ "type": "start", "sessionId": "b3f1…", "script": "…",
  "settings": { "horizon": 600, "frameInterval": 1.0 } }

{ "type": "stop" }

{ "type": "pause" }          // stop producing; keep the run alive
{ "type": "resume" }
```

`pause` exists because the alternative is unbounded client buffering: a user who pauses playback while
frames keep arriving accumulates state until the tab dies. Pausing the *producer* is the correct fix
and costs one flag.

### Server → client

```jsonc
// 1 — sent once, immediately after start
{ "type": "base", "model": { /* immutable full model contract at t=0 */ },
  "runId": "r-7f2a", "snapshotId": "sha256:…", "sourceHash": "sha256:…",
  "frameContractVersion": "1.0", "sequence": 0,
  "stateChecksum": "sha256:…", "estimatedFrames": 600 }

// 2 — the stream. Deltas against `base`.
{ "type": "frame", "runId": "r-7f2a", "snapshotId": "sha256:…",
  "sequence": 61, "baseSequence": 60, "t": 61.0,
  "state": { "N2":       { "t": 20.04, "p": 301.2 },
             "HE1":      { "tOut": 65.0, "power": 45.0 },
             "3WV":      { "position": 0.418 },
             "T1":       { "layers": { "2": { "t": 30.20 } } } },
  "diagnosticEvents": [
    { "occurrenceId": "FS4001:N2:1", "action": "started",
      "diagnostic": { "code": "FS4001", "severity": "warning",
                      "message": "…", "component": "N2" } }
  ],
  "stateChecksum": "sha256:…" }

// 3 — terminal
{ "type": "end", "runId": "r-7f2a", "snapshotId": "sha256:…",
  "reason": "completed", "frames": 600, "elapsedMs": 4210 }
{ "type": "error", "runId": "r-7f2a", "snapshotId": "sha256:…",
  "code": "FS3102", "message": "The simulation cannot advance past 184 s…" }
```

### Frames are deltas, and only of what changed

Realtime deliberately uses validated deltas rather than repeating the static model contract.

| Approach | Size per frame, 50 components | Verdict |
|---|---|---|
| Full model | ~40 kB | 24 MB over a 600-frame run. Unusable. |
| Full state, all components | ~6 kB | 3.6 MB. Workable but wasteful — most components are static most of the time. |
| **Delta: changed values only** | ~0.4 kB typical | **Chosen.** 240 kB per run. |
| Binary | ~0.1 kB | Fastest, opaque, hard to debug. Not worth it at these sizes. |

"Changed" means differing from the **last transmitted/reconstructed value** by more than display
precision, so sub-threshold changes accumulate and eventually cross the threshold. Display
precision is already declared per parameter
([`22-component-model`](../20-core-domain/22-component-model.md)), so the threshold is free.

**The frontend Web Worker reconstructs by applying deltas to `base`.** Before mutation it verifies
run/snapshot ids and `baseSequence`; afterwards it verifies `stateChecksum`. A mismatch stops the run
and preserves the last verified frame. It never guesses, skips a delta, or applies one to another
revision (`D-22`). The worker also performs history compaction, colour-scale calculation, and SVG
attribute preparation; only its bounded commit list reaches the UI thread.

`diagnosticEvents` is also delta state. An occurrence id is stable for one continuous episode and is
`{code}:{component-or-global}:{ordinal}`; `started` includes the full diagnostic, while `cleared`
contains only id/action. Duplicate starts or clearing an inactive id is `FS4507`. The worker records
`[startedAt, clearedAt?)` in simulation time so scrubbing reconstructs both active warnings and their
history (`D-30`).

## Backpressure

The dedicated solver worker writes verified frames to a bounded per-run channel of capacity two. The
WebSocket task drains that channel. If the client cannot keep up, the channel fills and the solver
worker waits at a frame boundary; neither the socket continuation nor the browser UI thread performs
calculation, and memory does not grow.

**That is correct behaviour**, and worth stating because the instinct is to buffer. Slowing down is
better than growing an unbounded queue, and the simulation has no real-time obligation. What must not
happen is silent frame dropping: a client that receives frames 1, 2, 5, 6 cannot reconstruct state from
deltas.

**The concrete lagging trigger is the same bounded channel, not a second socket-buffer threshold.**
When a write finds both channel slots occupied, the solver blocks and marks the run lagging. After the
socket drains one slot, the next outbound message is `{ "type": "lagging" }` before the queued frame;
it is emitted once per continuous full-channel episode. No platform-specific socket-buffer byte count
participates in correctness.

## A detached run keeps streaming

When the user switches to another tab, that document's run **continues and its socket stays open**
(`D-39`). Detaching removes work from the pipeline's tail, not its head:

| Stage | Attached | Detached |
|---|---|---|
| Solver produces frames | ✓ | ✓ |
| Socket delivers them | ✓ | ✓ |
| Worker decodes, applies deltas, verifies checksums | ✓ | ✓ |
| Worker retains checkpoints for scrubbing | ✓ | ✓ |
| Worker computes colour scales and geometry | ✓ | **skipped** |
| UI thread commits DOM | ✓ | **skipped** |

**Decode and delta application must not be skipped, and that is the whole subtlety.** Frames are
deltas against the previous frame (invariant 8), so a client that stops applying them cannot resume:
it would hold state at frame 200 and a socket delivering frame 640, with no way to bridge the gap
short of a reconnection and a fresh `base`. Skipping only the presentation stages keeps the state
exact and makes reattaching a matter of preparing one render from state already in hand.

The cost is honest and bounded: a detached run pays decode and delta application for frames nobody is
watching. That is roughly the cheap half of the pipeline, it is already off the UI thread, and
`07-quality-attributes` caps concurrent runs at two precisely so the total stays inside the retention
budget. The alternative — dropping frames while detached — breaks invariant 7 and produces a run that
cannot be scrubbed backwards through the gap.

**Detaching is not backpressure and must not trigger the lagging path.** The worker keeps draining the
socket at full rate; the bounded channel never fills for this reason. A detached run that reported
itself lagging would slow the solver for a presentation stage that is deliberately switched off.

Reattaching prepares a render from current state — no reconnection, no fresh `base`, no
re-verification. The run's `sequence` continues unbroken across the switch, which is the acceptance
criterion.

## Lifecycle

```
      connect
         │
    ┌────▼────┐  start   ┌─────────┐  frame…  ┌─────────┐
    │  Idle   ├─────────►│ Running ├─────────►│ Running │
    └────┬────┘          └────┬────┘          └────┬────┘
         │                    │ pause              │ end / error
         │               ┌────▼────┐          ┌────▼────┐
         │               │ Paused  │          │  Idle   │
         │               └────┬────┘          └─────────┘
         │                    │ resume → Running
         │  disconnect ─────────────────► cleanup (no terminal message can be delivered)
         │  stop ───────────────────────► end(reason="stopped") ─► cleanup
```

**Disconnect begins cancellation immediately and joins the worker within one step** ([`33`](../30-solver/33-transient-time-domain.md)'s
invariant 7). A run with no consumer is waste, and an abandoned run holding a solver is a leak that
compounds across a session.

## Reconnection

The client reconnects and sends `start` again with the same script. The run **restarts from t = 0**;
it does not resume.

Resuming would need server-side run state — the state vector, the controller integrals, the step
history — held for a client that may never return, which is exactly the authoritative server state
[`41-api-architecture`](41-api-architecture.md)'s session design refuses. A transient of a v1
circuit takes seconds; restarting is cheaper than the machinery to avoid it.

## Invariants

1. One run per connection; a second `start` while running is an error.
2. `base` precedes every `frame`, on the initial connection and on every reconnection.
3. Frames arrive with contiguous increasing `sequence`, increasing `t`, matching run/snapshot ids,
   and a `baseSequence` equal to the last verified sequence.
4. A frame contains only values that changed by more than their display precision.
5. Disconnect or `stop` cancels within one integration step and leaves no background work.
6. Every connected run, including explicit `stop`, ends with exactly one `end` or one `error`.
   Disconnect cancels and cleans up without a deliverable terminal message.
7. Frames are never dropped; the producer slows instead.
8. Applying every frame in order to `base` reproduces the state the server computed and its checksum.
9. Solver integration runs only on the dedicated backend worker; frontend decode, delta application,
   scale and render preparation run only in the Web Worker; DOM commit alone runs on the UI thread.
9a. Detaching a run skips colour-scale, geometry and DOM stages only. Decode, delta application and
   checksum verification continue, so `sequence` stays contiguous across a detach/reattach and
   invariants 3, 7 and 8 hold unchanged (`D-39`).
9b. Detaching never triggers `lagging`, never changes the solver's rate, and never closes the socket.
10. Every diagnostic occurrence starts once and clears at most once; applying events through frame N
    reproduces the server's active diagnostic set and complete intervals through that frame.
11. Tank layer deltas key by immutable one-based layer index (`D-32`). A frame may update layer values but may
    not add/remove/reorder layers or change port-to-layer mapping; that is `FS4507` shape mismatch.

Invariant 8 is testable by having the server also compute the reconstructed state and assert equality
in a test build. It is the invariant that catches a delta-threshold bug, which otherwise presents as a
value that quietly stops updating on the canvas.

## Error cases

| Code | Trigger | Action |
|---|---|---|
| `FS4501` | `start` while a run is active | `error` message; the existing run continues |
| `FS4502` | Script does not compile | `error` with the compile diagnostics; no run starts |
| `FS4503` | Script is not a transient model (`fluid` without `dynamic`) | `error` suggesting `fluid dynamic …` |
| `FS4504` | Solver failure mid-run | `error` with the inner code; frames already sent stay valid |
| `FS4505` | Client lagging beyond the buffer threshold | `lagging`, production paused |
| `FS4506` | Protocol violation (unknown type, malformed JSON) | `error` and close |
| `FS4507` | Snapshot, sequence, base, shape, or checksum mismatch | Stop/close; retain last verified frame and report integrity failure |
| `FS4508` | Solver worker crashed, exceeded its watchdog/resource limit, or failed cancellation | Stop/close after cleanup; retain verified frames |

`FS4504`'s "frames already sent stay valid" matters: a run that fails at t = 184 s has produced 184
seconds of correct simulation, and the UI should keep them rather than clearing the chart.

## Worked example

M4's `D-16` demo — the **demand-step loop** ([`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md)),
its 30 → 45 kW step — over 600 s at 1 s frames.

```
→ start { horizon: 600, frameInterval: 1 }
← base  { model: <full, t=0>, runId: "r-7f2a", estimatedFrames: 600 }      38 kB

← frame { t: 1,  state: {} }                                                24 B   nothing changed
← frame { t: 2,  state: {} }                                                24 B
  … 58 near-empty frames — the system is at steady state …
← frame { t: 61, state: { "HE1": { "power": 45.0, "tOut": 65.0 } } }        86 B   step applied
← frame { t: 62, state: { "HE1": {...}, "PB__1": { "t": 51.5 } } }         118 B   front enters
  … 40 frames as the front travels, growing to ~30 components changing …
← frame { t: 101, state: { …12 components… } }                             410 B
  … settling; changes shrink back …
← frame { t: 420, state: { "N2": { "t": 20.00 } } }                         52 B
  … 180 near-empty frames …
← end   { reason: "completed", frames: 600, elapsedMs: 4210 }
```

**Total ≈ 38 kB base + ~95 kB of frames = 133 kB** for a 600-second simulation. Full-model frames
would have been 23 MB. The empty frames — 240 of the 600 — cost 24 bytes each and are worth keeping
rather than skipping: they advance the clock, they prove the run is alive, and dropping them would
break invariant 3's no-gaps rule for no meaningful saving.

## Acceptance criteria

- [ ] The M4 demo streams in under 200 kB total.
- [ ] Frames reconstruct exactly to the server's computed state (invariant 8).
- [ ] Playback starts before the run finishes.
- [ ] Disconnecting mid-run stops the solver within one step.
- [ ] A second `start` produces `FS4501` without disturbing the running simulation.
- [ ] A slow client triggers `lagging` and drops no frames.
- [ ] Every run terminates with exactly one `end` or `error`.
- [ ] A non-transient script produces `FS4503` with the suggested fix.
- [ ] Wrong snapshot id, sequence, base id, state shape, non-finite value, and checksum each stop the
      run in a fault-injection test without applying the bad frame.
- [ ] Thread tracing proves the backend worker, frontend Web Worker, and UI DOM commit boundaries.
- [ ] A warning starting at 90 s and clearing at 140 s reconstructs that exact interval during live
      playback and backward scrubbing; duplicate/missing occurrence events stop with `FS4507`.
- [ ] Storage-header deltas update only changed tank layers and reconstruct all five exactly; an index
      0/6 or changed layer count stops the run as a shape mismatch.
- [ ] Detaching a run mid-stream and reattaching 200 frames later yields contiguous `sequence` with no
      gap, no reconnection, and no second `base`.
- [ ] A detached run never emits `lagging`, and its frame rate is unchanged — measured against the
      same run left attached.
- [ ] Scrubbing backwards across the detached interval replays correctly from checkpoints.
- [ ] A counting spy shows zero colour-scale and zero geometry calls while detached, and non-zero
      decode and delta-application calls over the same interval.

## Open questions

None. v1 uses raw `System.Net.WebSockets`, the browser worker retains a full checkpoint every 60
frames within `07`'s retention budget, and `frameContractVersion` evolves independently of the model
contract.
