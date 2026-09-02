# fluidscript

The version line every script opens with.

```fluidscript
fluidscript 1
```

It states which major version of the language the file is written in, and it is the first line of a
saved file. A file without it cannot be saved durably: the version is what lets a script written today
still mean the same thing when the language has moved on.

## Rules

- One number, the language major. There is no minor.
- It comes before everything except comments and blank lines.
- A file is read under the major it declares. Current behaviour is never applied to an older file
  behind your back.

## What happens when the version does not match

FluidScript reads this line before it reads anything else, and what it finds decides what you are
allowed to do with the file. It never guesses.

| What the file says | What you can do |
|---|---|
| The current version | Everything. |
| An older version FluidScript still supports | Edit, solve and save. The file is read under *its* rules, not today's, and opening it changes nothing. You are offered a migration; nothing is migrated until you accept it. |
| A version newer than this FluidScript | Read it as text and save a copy. It is not compiled, not solved, and never overwritten. |
| An older version FluidScript has dropped | The same. |
| Nothing at all | Edit and solve as a draft, with `FS1701`. **Save is disabled** until you add the line — one click, and FluidScript inserts the current version for you. |

Two `fluidscript` lines naming *different* versions is `FS1705`. There is no rule about which one
wins, deliberately: a file that contradicts itself about what it means is one FluidScript refuses to
interpret rather than one it guesses at.

## Why saving is blocked without it

A draft with no version line means exactly what it would mean with one — nothing about how it is read
changes. What changes is later. A saved file is read again, sometimes years later, by a FluidScript
that has moved on; the version line is what lets it choose the rules the file was written under
instead of the rules of the day. A file saved without one has no such answer, and the only moment
that can be fixed is before it is written.

## See also

[`catalog`](catalog.md) · [`project`](project.md) · [`circuit`](circuit.md)
