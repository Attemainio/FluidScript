# The log

The panel under the editor is where FluidScript tells you what it noticed: a valve with poor
authority, a node near freezing, a pump wired to nothing. It is not a history that scrolls away.
It is a picture of the current script, redrawn every time the script is compiled, so a line stays
exactly where it is until the problem it describes is gone.

## Reading a line

```
▲  3WV     Authority is 0.19 — below 0.25. Control will be poor.
```

| Column | Meaning |
|---|---|
| `✕` `▲` `·` `✓` | Error, warning, note, or the success line |
| `3WV` | The component concerned. Click it and the component is selected on the diagram and its line highlighted in the script |
| The message | What was found, with the numbers in the units you wrote |

Hover a line to see its code, `FS4006` and the like; the code is what the reference pages are
listed by. A note is something the compiler did for you, such as adding a junction node, and is
worth reading once and ignoring afterwards.

## What shows

The three buttons at the left choose what the panel lists: everything, warnings and errors, or
errors only. The default is warnings and errors. Notes are hidden, and a count beside the buttons,
`6 info`, says how many there are, so nothing disappears without a trace. The filter box beside them
narrows the list to lines containing what you type, component names and codes included.

**Three of a kind fold into one line.** When the same condition fires on three or more components,
the panel shows one line, `40 components · Approaching freezing point`, with a control to open it
and see each component's own message and value. Two of a kind stay as two lines.

**A clean solve ends in a green line**, `✓ Solved · 4 iterations · 14 ms`. An empty panel never
means "fine"; it means nothing has run. A solve that gives up ends in a red line saying so.

## The header

The right side of the header says in one phrase what the solver did:

| Shows | Meaning |
|---|---|
| `solved in 14 ms` | The last compile solved cleanly |
| `solving…` | A compile is in progress |
| `3 errors` | The script has errors; the last good model is still on the canvas |
| `not solved — 1 error` | Nothing was solved: the error stops it before the solver |
| `did not converge` | The solver ran and gave up |
| `offline` | The server cannot be reached; the panel keeps its last lines |

The button beside it copies the visible lines as text, one per line with code, severity, component
and message, ready for a message or a bug report.

## How it behaves while you type

Every compile replaces the panel's contents, but you will not see it flicker: a line that is still
true keeps its place, a new one fades in, a resolved one fades out. The scroll position stays where
you left it, and the panel follows new lines only when you were already at the bottom, the way a
terminal does.

The panel makes no requests of its own. It shows what the last compile returned, and nothing else.

## See also

[The editor](the-editor.md) · [The canvas](the-canvas.md) · [Working in tabs](working-in-tabs.md)
