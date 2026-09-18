# Files and recovery

A script lives in a `.fluid` file on your disk, and FluidScript treats that file as the only copy
that counts. Everything else -- the text in the editor, the draft the browser keeps in case of a
crash, a download -- is a working copy, and the interface never calls one of those "saved".

## The File menu

**File** in the toolbar holds the commands; each has a shortcut where the browser allows one.

| Command | What it does | Shortcut |
|---|---|---|
| New | A new tab with the template: the version line and an empty circuit | -- |
| Open… | Picks one or more `.fluid` files; each opens in its own tab | `Ctrl+O` |
| Save | Writes the file it came from, in place | `Ctrl+S` |
| Save As… | Writes a new file and switches the tab to it; the old file is not touched | `Ctrl+Shift+S` |
| Download .fluid | Hands the text to the browser as a file to save wherever it puts downloads | -- |
| Recovered drafts… | Lists drafts the browser kept of tabs that are no longer open | -- |

Which of these you see depends on the browser. Chromium browsers (Chrome, Edge) can write a file
in place, so they have Save and Save As. Firefox and Safari cannot: there, the menu offers
**Download .fluid** in their place, on `Ctrl+S`, and the document keeps its unsaved marker after
a download, because the file the browser wrote is a copy the application cannot see again.

## Unsaved work

A tab shows a dot after its name while its text differs from what was last saved. A new document
has that dot from the start -- nothing of it is on disk yet -- and an edit you undo back to the
saved text takes the dot away again, since what decides it is the text, not the history of
keystrokes.

**A draft that states no language version cannot be saved.** The first line of a file is
`fluidscript 1`, and a file without it could not be read with certainty years from now, so Save
offers to add the line rather than write the file without it. The editor's quick fix on the first
line does the same.

**Closing a tab with unsaved text asks first**: Save (or Download), Discard, or Cancel. Closing
one that is running a simulation asks separately whether to stop the run; there is no way to keep
a run whose document is gone. An untouched new document closes without a question. Leaving the
page with unsaved text asks the browser's own question.

Up to eight tabs can be open; a ninth is refused with a message rather than opened.

## Recovery

One second after you stop typing -- and while you keep typing, no more often than every five
seconds -- the browser stores a copy of every unsaved document in its own storage. That copy is
never written into your file. It exists for the case where the tab crashes, the browser closes, or
you reload:

- **A document that was never a file** comes back as it was, still marked unsaved.
- **A document that came from a file** comes back with a notice: *Reopen file* reads the file
  again (the browser asks whether the page may see it -- that is the browser's rule, not ours), and
  where the kept draft is newer than the file you are asked which to use: **Restore draft**, **Use
  file**, or **Compare** to see both side by side. Nothing replaces anything until you choose.
- **A tab you had closed** is not brought back, but its draft, if one was kept, is listed under
  **Recovered drafts…**, where you can restore it into a new tab, download it, or discard it.
  Drafts older than thirty days are marked as such and still not deleted until you say so.

If the browser's storage is unavailable -- some private modes, a full disk -- a warning stays on
until the page reloads and asks you to download what you want to keep.

## When the file changed under you

Before Save overwrites a file, it checks that the file is still the one it read. If another program
has changed it since, Save stops with a notice and three choices: **Reload from disk** takes the
file's text and drops yours from the editor (the draft copy stays in recovery until the next
change); **Save As…** writes yours to a new file and leaves the changed one alone; **Compare**
shows both. There is no button that overwrites the other program's work without saying so.

## Files this version cannot edit

A file whose first line names a FluidScript version this build does not understand opens
read-only: you can see it and download it, its bytes stay exactly as they were, and Save is
refused. Migration to the current version is offered as an explicit step when a newer version
exists; none does yet.
