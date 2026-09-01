# Diagnostics

Every message FluidScript shows you about a script carries a code, a severity, and — when it is about
something you wrote — the exact piece of text it is about. This page is the complete list of codes,
and what each one means.

## Reading a code

A code is `FS` followed by four digits, for example `FS1302`.

**A code never changes meaning.** Once a code has been given a meaning it keeps it, and it is never
renumbered. That is what makes a code safe to write into a note, a test, or a prompt: `FS1302` will
mean the same thing next year. When a rule changes so much that a code no longer applies, the code is
withdrawn rather than re-used, and it appears under [Withdrawn codes](#withdrawn-codes) below.

The first two digits say which part of FluidScript noticed the problem, which is usually the first
thing worth knowing. The **Reported by** column in the table below spells that out for each code.

## Severities

| Severity | What it means | What happens |
|---|---|---|
| Error | The thing it is about cannot be used | That one component, connection, or line is skipped |
| Warning | It was used, but it probably does not say what was meant | Nothing — the script runs as written |
| Info | Something was decided for you | Nothing |

**Nothing stops at the first problem.** An error is always about one element — one component, one
connection, one line — and everything else in the script still runs. A script with three errors in it
still sizes, still solves, and still draws the parts that are intact. This is deliberate: a script
being edited is incomplete most of the time, and a diagram that blanks on every keystroke is useless.

An error does mean the element it names is left out of the result, so a downstream number that depends
on it will be missing too. Fix errors from the top of the list down; later ones often disappear on
their own.

## Suggestions

Some messages come with a suggested edit — a replacement for an exact piece of your script, which the
editor can apply for you. A suggestion is offered only when there is one certainly correct answer. Two
plausible readings means no suggestion, and a message that explains the choice instead.

## Codes

<!-- BEGIN GENERATED: diagnostic-codes -->
| Code | Severity | Reported by | Message |
|---|---|---|---|
| `FS1001` | Error | Lexer | Unterminated string; add a closing quote. |
| `FS1002` | Error | Lexer | '{ch}' is not valid here. |
<!-- END GENERATED: diagnostic-codes -->

## Withdrawn codes

These codes were used once and never will be again. They are listed so that a code found in an old
note or an old script can still be looked up, and so that it is clear the number has not been quietly
given to something else.

<!-- BEGIN GENERATED: retired-diagnostic-codes -->
| Code | Why it is no longer reported |
|---|---|
| `FS1509` | Meant 'more than one circuit header', which is now legal: a script may declare several numbered circuits. Two circuits claiming one number is a different condition and took a new code rather than inheriting this one. |
<!-- END GENERATED: retired-diagnostic-codes -->
