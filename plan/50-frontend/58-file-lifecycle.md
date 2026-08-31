---
id: 58-file-lifecycle
title: File lifecycle and recovery
tier: 50-frontend
status: reviewed
owns: [new/open/save/save-as/download, dirty state, local recovery, file conflicts, the open-document set and tab switching]
depends_on: [01-vision-and-scope, 06-decision-log, 17-formatting-and-round-trip, 18-script-compatibility, 51-frontend-architecture, 52-editor]
traces_to: [R-21, R-25, R-30, R-38, R-39, R-42, R-50]
open_questions: 0
last_review_pass: 6
---

# File lifecycle and recovery

## Purpose

Implements `D-27`: defines how a design survives beyond one browser session without confusing recovery data with a saved
file. v1 is local-first and single-user; it does not require accounts or server storage.

## Responsibilities

**Owns.** New, Open, Save, Save As, download fallback, dirty/conflict states, crash recovery, and the
user-visible errors for them.

**Explicitly does not own.** Source compatibility (`18`), editor text model (`52`), API persistence
(none in v1), static export (`59`), or future interchange (`71`).

## Contracts

```ts
type DocumentState = {
  documentId: string;
  displayName: string;
  source: string;
  savedHash?: string;
  currentHash: string;
  fileHandle?: FileSystemFileHandle;
  lastKnownFileModified?: number;
  status: "clean" | "dirty" | "saving" | "conflict" | "error";
  recoveryStatus: "none" | "pending" | "written" | "failed";
  runId?: string;              // an active transient owned by this document (D-39)
};

type WorkspaceState = {
  documents: DocumentState[];  // tab order; at most 8 (07)
  activeDocumentId: string;    // exactly one, always present
};
```

**Every document owns its own everything** — source, hashes, file handle, dirty state, recovery entry
and run. Nothing is shared between tabs but the workspace itself. That is what makes a tab switch a
pure change of `activeDocumentId` rather than a save-and-restore of eight separate pieces of state,
and it is why the recovery rules below need no per-tab special cases: they already operate on one
`DocumentState`.

### Tab switching

Switching sets `activeDocumentId`. It does **not** touch any other document's source, dirty state,
recovery, or run.

| Concern | On switching away |
|---|---|
| Source and dirty state | Retained untouched; no save, no prompt |
| Recovery | Continues on its existing idle timer — an unsaved background tab is still protected |
| Compile / debounce | Cancelled. The draft pipeline serves the visible editor and nothing else |
| Layout and rendering | Detached. No layout, no render preparation, no DOM |
| **An active transient run** | **Continues**, and keeps receiving and reconstructing frames — only presentation stops (`D-39`, `D-42`) |

**The run continuing is the one people expect to go the other way**, and it is the point of `D-39`.
`D-22` and `R-41` already establish that a run owns an immutable snapshot precisely so activity in the
editor cannot destroy it; a tab switch is a cheaper and more frequent gesture than an edit, so having
it silently discard a 600-frame run would defeat the isolation those exist to provide. The computation
is already off the UI thread, so only the *rendering* has to stop. What "detached" means precisely for
the frame pipeline is [`43-realtime-contract`](../40-api/43-realtime-contract.md)'s.

A run ends only on the user's Stop, on closing its document, or on leaving the application.

**Closing a document with an active run offers Stop or Cancel, not Stop or Keep**, and the difference
matters. "Keep" would mean a run outliving the document that owns it — a detached job — and v1 has
none: the run's snapshot, its frames and its checkpoints all live in that document's stores, and
`43`'s socket is bound to it. Keeping it would require somewhere for it to live, which is the
post-v1 server-job boundary this document already draws.

So the choice is: **Stop** ends the run and closes, or **Cancel** abandons the close and leaves both
the run and the document exactly as they were. A user who wants the run to finish cancels, waits, and
closes afterwards. The unsaved-text choice is presented separately, because losing edits and losing a
run are two independent things and one prompt cannot ask about both.

`07-quality-attributes` caps the workspace at 8 documents and 2 concurrent runs. A third run is
refused with a diagnostic naming the two already running, never queued silently: a queued run that
starts minutes later, against a snapshot the user has forgotten, produces results nobody can place.

The primary path uses the browser File System Access API where available. Capability detection, not
browser naming, selects it. The fallback opens with `<input type=file>` and saves by download; because
the fallback cannot overwrite in place, the UI labels the action **Download .fluid**, not Save.

### State transitions

- **New** creates current-version template text, no handle, and `dirty` state.
- **Open** reads bytes, runs compatibility inspection, and replaces the current document only after
  dirty-change confirmation. Unsupported files open read-only.
- **Save** is available only with a handle and compatible durable source. It writes to a temporary
  writable stream, closes successfully, then updates `savedHash`; failure leaves `dirty` state.
- **Save As** obtains a new handle and never changes the old file. Cancelling changes nothing.
- Before overwrite, the app compares the file's last-modified time and content hash with the last
  known values. A mismatch enters `conflict`; choices are Reload, Save As, or Compare. No force
  overwrite exists behind an ambiguous confirmation.
- Closing, reloading, opening another file, or navigating away with dirty text prompts the user.

### Recovery

After 1 s idle and at most every 5 s while dirty, the app stores source bytes, current hash, display
name, language version disposition, timestamp, and optional original-file fingerprint in IndexedDB.
Recovery is never written into the named file and never changes `savedHash`. On launch, newer recovery
content is offered with Preview, Restore as dirty, Download, and Discard. Recovery entries older than
30 days are listed for explicit cleanup; the app does not silently delete the only copy of a draft.

After an explicit **Open**, the app matches recovery first by persisted file-handle identity when
available, otherwise by `(displayName, savedHash)` from the recovery entry and the bytes just read.
If a matching recovery is newer and its `currentHash` differs from the opened bytes, the file opens in
preview and offers Restore recovery as dirty, Use file, Compare, or Cancel; neither candidate silently
replaces the other. A recovery with only the same display name but a different saved hash is not a
match and remains listed separately.

Transient run snapshots reference a source hash only. Saving, opening, or editing does not mutate an
active run. Closing the document presents separate choices for unsaved text and active-run Stop/Cancel;
leaving the application stops the run because v1 has no detached server jobs.

### Accessible workflow

New/Open/Save/Save As/Download are reachable by menus and documented shortcuts. Filename, dirty,
read-only, saving, recovery, and conflict state are exposed as text and polite live-region updates,
never colour alone. Dialog focus is trapped and restored; every action works without canvas use.

## Invariants

1. Only a completed named-file write changes `savedHash` or shows `clean`.
2. Recovery and download are not described as overwriting a file.
3. Open, migration, recovery preview, and conflict comparison are non-mutating.
4. Source bytes remain authoritative; no separate project database owns hidden model state.
5. File operations cannot mutate or implicitly cancel an active transient snapshot.
6. Exactly one `activeDocumentId` exists and names a document in `documents`.
7. Switching tabs changes no document's `source`, `currentHash`, `savedHash`, `status`,
   `recoveryStatus` or `runId` — asserted by comparing the whole workspace across a switch and back,
   field by field (`D-39`).
8. A run ends only through Stop, closing its document, or application exit. No navigation, selection
   or editing gesture ends one, and no run outlives its document.
9. Recovery is per document and keyed by `documentId`; two dirty tabs produce two recovery entries and
   restoring one never touches the other.

## Error cases

| Key | Condition | Required result |
|---|---|---|
| `FILE001` | Permission denied or handle revoked | Remain dirty, retain recovery, offer Save As/Download |
| `FILE002` | Read/write fails or storage is full | Remain dirty; state exact failed action and retain in-memory text |
| `FILE003` | External file change detected | Enter conflict; Reload, Save As, Compare |
| `FILE004` | Recovery store unavailable | Continue editing; persistent warning and Download action |
| `FILE005` | Unsupported language/catalogue | Read-only view; preserve bytes; route to compatibility actions |
| `FILE006` | User cancels picker or confirmation | No state change and no error toast |
| `FILE007` | Opening a ninth document | Refuse with a message naming the limit; no tab is closed to make room |
| `FILE008` | Starting a third concurrent run | Refuse, naming the two running documents; never queued |
| `FILE009` | Closing a document with an active run | Two separate choices — unsaved text, and Stop/Cancel for the run. No option keeps a run whose document is gone |

## Worked example

`loop.fluid` is clean at hash A. The user edits to B while a transient made from A continues. Recovery
writes B. Another program changes the disk file to C; Save detects C and enters conflict. Choosing
Save As writes B to a new file, marks that new file clean, and leaves both the original C and the run
from A untouched.

## Acceptance criteria

- [ ] File-System-Access and upload/download paths pass the same New/Open/dirty/recovery scenarios.
- [ ] Failed, cancelled, and conflicting saves never display clean or lose the last recoverable text.
- [ ] Unsupported-version files remain byte-identical unless an explicit migration is applied.
- [ ] A crash/reload restores a newer dirty draft without overwriting its named file.
- [ ] Explicit Open matches recovery by handle identity or `(displayName, savedHash)`, offers the
      newer divergent draft before replacement, and never matches on filename alone.
- [ ] Keyboard and screen-reader tests cover every file action and state.
- [ ] File operations during a transient do not change its snapshot id, frames, or worker lifetime.
- [ ] Two documents open with independent sources, dirty states, file handles and recovery entries;
      saving one leaves the other's status untouched.
- [ ] Switching away from a tab with an active transient and back leaves the run's frame sequence
      unbroken, and a field-by-field workspace comparison across the switch shows no change.
- [ ] Editing tab A while a transient runs in tab B affects neither B's run nor its source.
- [ ] Two dirty tabs produce two recovery entries; restoring one leaves the other intact.
- [ ] A ninth document and a third run are both refused with messages naming the limit.

## Open questions

None.
