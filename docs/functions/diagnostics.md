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
| `FS1107` | Warning | Parser | '{circuit}' is solved as a steady state, so its schedule does not run. Write 'fluid dynamic' to solve it in time. |
| `FS1108` | Error | Parser | '{text}' — a name cannot contain '-'. Write '{underscored}'. |
| `FS1109` | Error | Parser | '{word}' is not an attachment. Write 'supply {node}' or 'return {node}'. |
| `FS1110` | Error | Parser | '{word}' needs one node of the parent circuit, and may appear once per circuit. |
| `FS1111` | Error | Parser | A 'control' line needs named arguments, such as 'control actuate=V1.position measure=N2.t by=PID1'. |
| `FS1112` | Error | Parser | '{word}' applies to the whole file and must come before the first 'circuit' line. |
| `FS1113` | Error | Parser | Spacing is in world units, so write 'spacing {n}' with no unit. |
| `FS1114` | Error | Parser | '{extra}' is more than this line can hold. |
| `FS1115` | Error | Parser | Put this pair under a 'curve' line. |
| `FS1116` | Error | Parser | 'curve {name}' needs what it depends on, such as 'curve {name} tout'. |
| `FS1117` | Error | Parser | A curve row is one x and one y, such as '-26 50'. |
| `FS1118` | Error | Parser | A 'design' line needs named values, such as 'design tout=-26'. |
| `FS1203` | Warning | Style directive | '#' starts a comment; the rest of this line was ignored. Write the colour as "{hex}". |
| `FS1302` | Error | Units | Cannot add two {dimension}s. To offset by a difference, write '{example}'. |
| `FS1304` | Error | Units | '{parameter}' is a {expected}; '{value}' is a {actual}. |
| `FS1305` | Error | Units | Cannot {operation} a {left} and a {right}. |
| `FS1306` | Warning | Units | {parameter} = {value} is outside the usual range ({low}–{high}). Check the unit. |
| `FS1401` | Error | Expressions | '{name}' is already defined at line {line}. |
| `FS1402` | Error | Expressions | '{name}' depends on itself: {cycle}. |
| `FS1403` | Error | Expressions | Dividing by zero here. '{expression}' is zero. |
| `FS1404` | Error | Expressions | Nothing named '{name}'. |
| `FS1406` | Error | Expressions | A {kind} has no '{property}'. It has: {available}. |
| `FS1408` | Error | Expressions | No function '{name}'. Available: {available}. |
| `FS1409` | Error | Expressions | '{function}' takes {expected} arguments. |
| `FS1501` | Error | Binder | '{name}' is already declared at line {line}. Names are unique across the whole file; tags are what distinguish circuits. |
| `FS1502` | Error | Binder | There is no '{kind}'. |
| `FS1503` | Error | Binder | A {kind} has no '{parameter}'. It accepts: {available}. |
| `FS1504` | Error | Binder | '{name}' is a value, not a component. |
| `FS1505` | Error | Binder | A {kind} has no port '{port}'. Ports: {available}. |
| `FS1506` | Error | Binder | Port '{port}' of '{name}' is already connected at line {line}. |
| `FS1507` | Warning | Binder | '{name}' is not connected to anything. |
| `FS1508` | Warning | Binder | No circuit name; using '{name}'. |
| `FS1510` | Info | Binder | Added {kind} '{name}' ({rule}). |
| `FS1511` | Warning | Binder | '{name}' and {count} others are not connected to the rest of the circuit. |
| `FS1512` | Info | Binder | Read '{written}' as '{canonical}'. |
| `FS1513` | Error | Binder | '{written}' could be '{first}' or '{second}'. Write one of them. |
| `FS1514` | Error | Binder | '{parameter}' accepts {available}; '{written}' is none of them. |
| `FS1515` | Error | Binder | '{parameter}' names a component property, like 'N2.t'. |
| `FS1516` | Error | Binder | '{written}' is outside {kind}'s supported {min}…{max} range. |
| `FS1517` | Warning | Binder | '{circuit}' is {circuitMode} while the project is {projectMode}; the circuit's own setting is used. |
| `FS1518` | Error | Binder | '{name}' is not declared anywhere. A subcircuit attaches to a node of another circuit. |
| `FS1519` | Info | Binder | '{name}' is not a known circuit role, so it is placed neutrally. Known roles: {available}. |
| `FS1520` | Warning | Binder | '{circuit}' declares '{present} {node}' and no '{other}'. A subcircuit attaches with both. |
| `FS1521` | Error | Binder | A 'control' line needs {list}. Missing: {missing}. |
| `FS1522` | Error | Binder | '{param}' of '{component}' cannot be controlled. |
| `FS1523` | Error | Binder | '{name}' is a {kind}, not a controller. |
| `FS1524` | Error | Binder | Circuit {number} is already '{owner}'. Every circuit's number is its own. |
| `FS1525` | Error | Binder | '{name}' is already a circuit at line {line}. |
| `FS1526` | Error | Binder | '{circuit}' takes flow from '{a}' and returns it to '{b}'. A subcircuit attaches to one parent; write the second link as a connection. |
| `FS1527` | Error | Binder | '{driver}' is not something '{curve}' can depend on. Name a curve, a known driver, or 'time'. |
| `FS1528` | Error | Binder | '{curve}' depends on '{driver}', which has no value here. Add 'design {driver}=...' or solve in time. |
| `FS1529` | Info | Binder | '{curve}' has two rows at {x}; the later one is used. |
| `FS1530` | Error | Binder | '{curve}' needs at least two rows to interpolate between. |
| `FS1531` | Error | Binder | A {kind} has no single {role} to use here. Write it out, such as '{example}'. |
| `FS1532` | Error | Binder | '{name}' is a {kind}, which is not placed with 'at'. Connect it with '-' instead. |
| `FS1533` | Warning | Binder | '{name}' observes nothing. Place it with 'at' and the name of a node. |
| `FS1701` | Info | Compatibility | This draft states no language version. Add 'fluidscript {major}' as its first line to save it. |
| `FS1702` | Error | Compatibility | This file is FluidScript {major}, which this version cannot read. It understands {supported}. |
| `FS1705` | Error | Compatibility | This file says it is FluidScript {first} and also {second}. Delete the line that is wrong. |
| `FS2001` | Error | Substances | There is no fluid called '{name}'. Available: {list}. |
| `FS2002` | Error | Substances | Cannot fix a state from {a} and {b}; they are not independent here. |
| `FS2003` | Error | Substances | {name} data covers {lo} to {hi}; this state is at {value}. |
| `FS2004` | Error | Substances | Could not evaluate {property} for {name} at {state}. |
| `FS2006` | Error | Substances | Relative humidity must be between 0 and 100 %. |
| `FS2107` | Warning | Components | '{name}' is a dead end. Set t, p or flow to make it a boundary. |
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
