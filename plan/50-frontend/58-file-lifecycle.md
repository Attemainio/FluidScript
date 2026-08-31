---
id: 58-file-lifecycle
title: File lifecycle and recovery
tier: 50-frontend
status: reviewed
owns: [new/open/save/save-as/download, dirty state, local recovery, file conflicts]
depends_on: [01-vision-and-scope, 06-decision-log, 17-formatting-and-round-trip, 18-script-compatibility, 51-frontend-architecture, 52-editor]
traces_to: [R-21, R-25, R-30, R-38, R-39, R-42]
open_questions: 0
last_review_pass: 2
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
};
```

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
active run. Closing the document presents separate choices for unsaved text and active-run Stop/Keep;
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

## Error cases

| Key | Condition | Required result |
|---|---|---|
| `FILE001` | Permission denied or handle revoked | Remain dirty, retain recovery, offer Save As/Download |
| `FILE002` | Read/write fails or storage is full | Remain dirty; state exact failed action and retain in-memory text |
| `FILE003` | External file change detected | Enter conflict; Reload, Save As, Compare |
| `FILE004` | Recovery store unavailable | Continue editing; persistent warning and Download action |
| `FILE005` | Unsupported language/catalogue | Read-only view; preserve bytes; route to compatibility actions |
| `FILE006` | User cancels picker or confirmation | No state change and no error toast |

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

## Open questions

None.
