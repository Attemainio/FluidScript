# The editor

The script pane is a code editor that knows FluidScript: it colours the text as you type, underlines
what the compiler objects to, offers the names you are about to need, and lays a script out on
request. None of it stops you typing. When the server is away, the colours and the shortcuts keep
working and the underlines simply stop updating until it is back.

## Colours

The editor colours every token by what it is on its line, not by what it means to the solver, so the
colour never waits for a compile:

| You see | It is |
|---|---|
| `circuit`, `let`, `connections` in the keyword colour | A reserved word starting a statement |
| `HE1` emphasised at the start of a line | A name being introduced |
| `heat_exchanger` in the type colour | The kind of the component |
| `power` muted, before an `=` | A parameter |
| `30` bright and `kW` dimmed | A number and its unit; the unit is dimmed so a column of numbers scans |
| `# ...` italic | A comment, to the end of the line |
| `PU1.dp` with `dp` in the reference colour | A property read through a dot |
| `HE1.out` after a `-`, with `out` in the port colour | A port in a connection |

The palette is the familiar code-editor one rather than the diagram's colours, in both themes
([Themes](themes.md)). A keyword that is not blue reads as wrong before it reads as different.

The editor cuts words the way the compiler does, so a name like `3WV` is a name and `3 kW` is a
quantity, and a unit written with a space, `30 kW`, is still one quantity. A test runs the editor's
tokenizer and the compiler's lexer over every sample script and fails if they disagree about one
token.

## Underlines

A short while after you stop typing the script is compiled and the result comes back as underlines:
red for an error, amber for a warning. Notes are not underlined; they are in the log below the
editor. Hover an underline to read the message, its code and, where the compiler pointed at a
second place, that place with its line number.

The underlines from the last compile stay until the next one replaces them, so they do not flicker
off and on with every keystroke. What you may notice is one underline lagging a character or two
behind an edit for a moment; that is the gap before the next compile, not a stale result.

Where the compiler knows the fix, the hover has an **Apply fix** button: an alias spelled out to its
canonical kind, a unit corrected, a parameter renamed. The fix is one edit, so one Undo puts the text
back. If you have changed the text since the compile that proposed it, the fix is skipped rather than
applied to the wrong place, and the next compile offers it again.

## Completion

Completion opens as you type and knows where you are on the line:

| Where the cursor is | What is offered |
|---|---|
| Start of a declaration line | Nothing; you are naming the component |
| After the name | Every component kind, with its description; an alias is matched too and shown as `via 'radiator'` |
| After the kind, or after a parameter | The parameters that kind still accepts, each with its dimension, unit and typical range |
| After `param=` | `let` names, `Component.property` references and unit symbols that have that parameter's dimension |
| Start of a connection line, or after `-` | Every component, including the ones the compiler inferred, marked as such |
| After a `.` | The component's ports in a connection, its properties in an expression |
| Inside `schedule` | The parameters a schedule may set |

**Tab or Enter commits the canonical spelling.** Type `heat_ex`, press Tab, and the text reads
`heat_exchanger`; type `rad` and Tab gives `heat_exchanger` as well, because `radiator` is one of its
aliases and the list said so. Anything you type without accepting a completion stays exactly as you
typed it. The compiler accepts the same aliases and misspellings the list does, so nothing the list
offers is refused later, and nothing the compiler would accept is missing from the list.

**Values are filtered by dimension.** After `power=` you see the `let`s and properties that are a
power and the power units, `kW` and `W` among them; a temperature `let` is not there. After `out=` a
`let` holding `70 C` is offered and one holding `20 dK` is not, because a temperature and a
temperature difference are different things and the filter says so before the compiler has to. A
`let` whose value waits for the solve is offered with its dimension and no value. Where the kind did
not resolve, so no dimension is known, everything is offered rather than nothing.

For a tank, `T1.` offers the ports already in use plus the pattern `in{1..16}`, and `T1 tank` offers
`volume`, `layers`, `t` and the layer temperatures the resolved layer count allows.

## Hover

Rest the pointer on a component's name and the same card the canvas shows appears: its parameters
and where each came from, its solved state, its warnings. On a `let` name it shows the binding's
value and dimension, or that the value waits for the solve. On a quantity such as `30 kW` it names
the dimension and the unit a bare number would mean there.

## Formatting

**Shift+Alt+F** lays the whole script out in the canonical form and nothing else:

- leading indentation is removed;
- a parameter's `=` has no spaces around it, a `let`'s has one on each side;
- brackets hug their contents and a comma is followed by one space;
- between an operator and its operand your spacing stands, collapsed to one space at most, so
  `Q/(cp*dT)` and `Q / (cp * dT)` are both left as they are;
- within a run of lines the trailing comments share one column, two spaces past the longest line,
  and consecutive `let`s pad their names so the `=` signs line up;
- blank lines, full-line comments and the rows of a `curve` are left exactly as written.

A blank line or a full-line comment ends a run, so one long line aligns the comments of its own
paragraph and no further. Formatting twice changes nothing, it changes no token and no comment, and
the whole thing is one edit: one Undo restores your layout.

## Shortcuts

| Keys | Does |
|---|---|
| `Tab`, `Enter` | Accept the selected completion |
| `Esc` | Close the completion list |
| `Ctrl+/` | Comment or uncomment the line, or the selected lines |
| `Shift+Alt+F` | Format the script |
| `Ctrl+Shift+Enter` | Solve now, without waiting for the pause |
| `Ctrl+Click` on a component name | Jump to its declaration; nothing happens for an inferred one |
| `Ctrl+Z`, `Ctrl+Y` | Undo, redo |

On a Mac, `Cmd` stands in for `Ctrl`.

## What is not there yet

Renaming a component with `F2`, everywhere it is referenced, arrives with the
editing API; `Ctrl+Enter` to run a transient waits for the dynamic solver; opening, saving and the
recovery copy that survives a reload arrive with the file commands.

## See also

[Working in tabs](working-in-tabs.md) · [Themes](themes.md) · [Syntax](../functions/syntax.md)
