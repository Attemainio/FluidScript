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
| Code | Severity | About | Message |
|---|---|---|---|
| `FS1001` | Error | Lexer | Unterminated string; add a closing quote. |
| `FS1002` | Error | Lexer | '{ch}' is not valid here. |
| `FS1003` | Error | Lexer | '{name}' reads as a quantity ({value} {unit}), not a name. Try '{suggestion}'. |
| `FS1004` | Error | Lexer | '{word}' is reserved. Choose another name. |
| `FS1101` | Warning | Parser | Only the first '{section}' section is used. |
| `FS1102` | Error | Parser | Connections must come after the 'connections' line. |
| `FS1103` | Error | Parser | A {statement} cannot appear after the '{section}' line. |
| `FS1104` | Error | Parser | Cannot read this line. Expected a component declaration or a connection. |
| `FS1105` | Error | Parser | '{token}' looks like a parameter but has no value. Write '{token}=…'. |
| `FS1106` | Error | Parser | Put this under a 'schedule' line. |
| `FS1108` | Error | Parser | '{text}' — a name cannot contain '-'. Write '{underscored}'. |
| `FS1109` | Error | Parser | '{word}' is not an attachment. Write 'supply {node}' or 'return {node}'. |
| `FS1110` | Error | Parser | '{word}' needs one node of the parent circuit, and may appear once per circuit. |
| `FS1111` | Error | Parser | A 'control' line needs named arguments, such as 'control actuate=V1.position measure=N2.t by=PID1'. |
| `FS1112` | Error | Parser | '{word}' applies to the whole file and must come before the first 'circuit' line. |
| `FS1113` | Error | Parser | Spacing is in world units, so write 'spacing {n}' with no unit. |
| `FS1114` | Error | Parser | '{extra}' is more than this line can hold. |
| `FS1203` | Warning | Style directive | '#' starts a comment; the rest of this line was ignored. Write the colour as "{hex}". |
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
