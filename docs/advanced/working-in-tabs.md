# Working in tabs

FluidScript opens several scripts at once, one per tab, and keeps them apart: each tab has its own
text, its own diagram, its own log and its own unsaved-work marker. Switching is a click, and
nothing about the tab you leave changes because you left it.

## The workspace

The strip under the toolbar lists the open documents in the order you opened them. **+** opens a new
one from the template; **×** closes the active one, asking first when it has unsaved text or a
running simulation ([Files and recovery](files-and-recovery.md)). A tab shows its name and a `●` when it has
edits you have not saved. Up to eight documents can be open at once; a ninth is refused rather than
opened, because a workspace wider than that is one nobody can keep track of.

The tabs, their order and which one is active are remembered by the browser, so a reload brings
back the same strip, and each tab's text comes back from its file or from the draft the browser
kept -- see [Files and recovery](files-and-recovery.md) for what is asked when the two differ.

## What happens when you switch

The pane, the diagram, the log and the status line all describe the active tab and only it. When you
switch away from a tab:

- **Its text stays exactly as it was.** No save, no prompt, no change.
- **A compile in progress for it is dropped.** The pipeline serves the tab you are looking at and
  nothing else, so the outgoing tab's half-finished compile is cancelled and the incoming tab's
  starts. Switching back compiles the returning tab again from its current text.
- **A running simulation keeps running.** A transient run belongs to its document and continues off
  screen; the tab shows `▸` while it does. Switching back resumes the playback from the frames it
  produced while you were away. A run stops only when you press Stop, close its document, or leave
  the application.

The last point is the one people expect to go the other way, and it is deliberate. A run is started
from a snapshot of the script at that moment, and editing does not disturb it either; a tab switch
is a smaller gesture than an edit, so it cannot be the thing that throws a run away.

## The status line

The line at the bottom always says what the solver is doing and for which document:

| Shown | Meaning |
|---|---|
| `◐ Converging · draft compile · plant_01` | A compile is in progress |
| `● Converged · steady solve · plant_01` | The last solve finished within tolerance |
| `▲ Did not converge · steady solve · plant_01` | The solver stopped: iteration cap, a stop condition, or a non-finite state |
| `○ Not solved · steady solve · plant_01` | The script is parsed but nothing has been solved, or an error stopped it |

The glyph and the word carry the state; the colour only repeats it, so the line reads the same in
greyscale. It names the document because two tabs can be doing two different things: one converged,
one with a syntax error. A background run's state is reachable from its tab's `▸`, not from this
line, which describes the tab you can see.

## The log

The panel above the status line lists the diagnostics of the active tab, one per line: a glyph for
the severity (`✕` error, `▲` warning, `·` note), the component concerned, and the message. Its header
counts the entries and the errors and shows how long the last solve took. `⌄` folds it away; the
fold is remembered.

## When the host is away

If the server cannot be reached, the status line says `Not connected`, the editor keeps working, and
the diagram keeps showing the last model it had. Nothing you type is lost; the next compile that gets
through catches up.

## See also

[Using the API](using-the-api.md) · [Themes](themes.md)
