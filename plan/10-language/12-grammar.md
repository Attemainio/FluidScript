---
id: 12-grammar
title: Grammar
tier: 10-language
status: draft
owns: [lexical grammar, syntactic grammar, token set, statement words, block and indentation rules, statement set, AST node shapes, trivia model]
depends_on: [02-glossary, 06-decision-log, 11-language-overview, 13-type-and-unit-system]
traces_to: [R-01, R-03, R-04, R-05, R-39, R-46, R-48, R-49]
open_questions: 0
last_review_pass: 0
---

# Grammar

## Purpose

The concrete syntax of FluidScript's declarative language (`D-01`): what the lexer produces, which lines
and blocks the parser accepts, and what the syntax tree looks like. This is the document an implementer
types from, and the one the round-trip printer ([`17-formatting-and-round-trip`](17-formatting-and-round-trip.md))
is the inverse of. Why the language is shaped as it is — blocks, cases, port inference, runs — and how the
binder reads the tree is [`19`](19-fluidscript-2.md).

## Responsibilities

**Owns.** The lexical rules and the token set; the statement words; lines, blocks and indentation; the
syntax of every statement and value; the grammar; the syntax tree's node shapes and its trivia model; the
codes of the `FS10xx`, `FS11xx` and `FS12xx` ranges (the `FS121x` codes of `show` are allocated to
[`57`](../50-frontend/57-state-visualization.md), which interprets it).

**Explicitly does not own.** Unit symbols and their meanings
([`13-type-and-unit-system`](13-type-and-unit-system.md)), expression evaluation
([`14-expressions-and-references`](14-expressions-and-references.md)), name resolution and binding
([`15-semantic-model`](15-semantic-model.md)), what a statement means once read — ports, cases,
controllers, runs ([`19`](19-fluidscript-2.md)), the version line's policy and the catalogue pin
([`18-script-compatibility`](18-script-compatibility.md)), and the rules every diagnostic message follows
([`16-diagnostics`](16-diagnostics.md) — this document names its codes, that one defines how they read).

## Shape of the language

**One statement per line, or a block.** A block is a head line ending in `:` and the lines indented deeper
than it. There is no statement terminator and no line continuation: a statement never spans two lines, and
a block is several statements, each on its own line. That is what makes recovery line-granular — a line
that cannot be read costs that line and nothing else ([Invariants](#invariants)).

**A file is model, then study** (`D-165`): a version line, a `project` block, `let` lines and curves, the
circuits, then the runs. The order is a convention the printer keeps and nothing enforces; what is
enforced is where each statement may stand ([What may appear where](#what-may-appear-where)).

A complete script, which the sections below refer to:

```fluidscript
fluidscript 2

project "Boiler house":
  cases   = [cold, mild]
  catalog = steel_en10255@2026.1
  show    = temperature
  style:
    colour = "#2f6f9f"
    width  = 2

let outdoor = [-20, 5] C
let rise    = 20 K

curve demand: outdoor extrapolated
  -20   60
   18    0

curve weather: time
  2026-01-15 06:00   -15
  2026-01-15 09:00    -8

circuit "Boiler loop":
  fluid  = water
  number = 100

  BLR  boiler:
    out.t = 75 C
    dp    = 15 kPa
  PU1  pump
  MV1  valve3      stroke = 90 s
  TE1  temperature_sensor
  RAD  radiator    power = demand
  TC1  controller:
    type     = PI
    moves    = MV1
    reads    = TE1
    setpoint = 60 C
    band     = rise
    ti       = 120 s

  BLR - MV1                  15 m  DN32
  MV1 - PU1 - TE1 - RAD - NR
  NR - MV1
  NR - BLR                   15 m  DN32

run "Cold start":
  from     = cold
  start    = 2026-01-15 06:00
  duration = 3 h

  outdoor = weather
  at   20 min       RAD.power    = 40 kW
  over 1..2 h       TC1.setpoint = 60..55 C
  at   08:30        TC1.setpoint = 50 C
```

## Lexical structure

### Characters, whitespace and lines

The script is read character by character into tokens and trivia, and every character lands in exactly
one of them ([invariant 1](#invariants)). The language's characters are ASCII:

| Characters | Role |
|---|---|
| `A`–`Z`, `a`–`z`, `_`, `0`–`9` | Names and numbers |
| `"` | Opens and closes a string |
| `#` | Starts a comment |
| space, tab | Whitespace: separates tokens, and at a line's start is its indentation |
| `\n`, `\r\n`, `\r` | End a line |
| `=` `-` `+` `*` `/` `.` `..` `,` `(` `)` `[` `]` `@` `:` | Operators and punctuation ([below](#operators-and-punctuation)) |
| a byte-order mark before the first character | Whitespace: some editors save one, and it is not indentation |

Any other character outside a string or a comment — `|`, `;`, a non-ASCII letter — is an `Unknown` token and
`FS1002`, kept in the tree so the printer loses nothing. `%` and `°` are characters of unit symbols and are
recognised only as part of one, after a number ([Units and quantities](#units-and-quantities)); anywhere else
they are `FS1002` too.

**No token spans a line** ([invariant 6](#invariants)). A string, a comment and a unit all end at the line
break, which is what keeps one bad line from taking the lines after it.

### Comments

`#` begins a comment that runs to the end of the line (`D-13`), anywhere outside a string. `#` is therefore
not available as an operator, and a hex colour is written quoted — `colour = "#2f6f9f"` — because a bare
`#2f6f9f` would comment out the rest of the line. A setting whose value is missing because a hex-shaped
comment took it, `colour = #2f6f9f`, is `FS1203`, which says what happened: the line is legal and silent
otherwise, and the diagram would render in the default colour.

`#` was chosen over the `|` of the original brief because it is unshifted or single-shifted on nearly every
keyboard layout and is what readers already know from shell, Python, YAML and TOML; `|` needs `AltGr` on the
Nordic and German layouts. `D-13` records the trade. `|` is left unallocated — not an operator, not a
comment, `FS1002` — so reclaiming it later for a table or pipeline form is a non-breaking addition.

### Names

A **name** is letters, digits and underscores: `PU1`, `heat_demand`, `secondary`. It may start with a
digit — `3WV` is a legal component name (`R-01`) — which collides with numbers and quantities, since
`30kW` is also letters and digits. The lexer settles it by scanning the number first and asking what
follows:

1. A number followed by a **unit symbol** that ends before a non-word character is a quantity: `20C`,
   `2px`, `30kW`.
2. A number followed by other word characters is **one name**, when the number is itself spellable inside
   a name: `3WV` (`W` is a unit, `WV` is not, and a unit is matched whole). A number holding a decimal
   point or an exponent sign is not spellable, so `1.5x` is the number `1.5` followed by the name `x` —
   both readings are errors further up, and the rule makes them the *same* error every time.
3. A number followed by spaces and then a unit symbol is a quantity, under the conditions of
   [Units and quantities](#units-and-quantities).
4. Otherwise a number is a number.

**The consequence, stated plainly:** a component may not be named so that it reads as a quantity. `3K` is
three kelvin, and written where a name belongs it is `FS1003`, whose fix swaps the parts (`K3`). It is a
wart; the alternative — a space before every unit — costs `2px` and `20C`, which is worse.

**Hyphens are not part of a name.** `-` is the connection operator (`N1 - N2`) and the subtraction operator
(`t - rise`), both legal unspaced, so it never joins two words: `HX-1` is `HX`, `-`, `1`. Admitting `-` into
names would make `N1-N2` one name that the binder silently creates a node for (`15`'s rule I1) — a wrong
answer that compiles. Because a hyphenated name is what a user coming from HTML, CSS or a `/docs` file name
will type, the parser recognises the shape and says so — for a component's name (`HX-1 pump`), a name in
a chain (`N1 - HX-1 - N2`) and a kind (`V1 3-way-valve`, `V1 three-way-valve`): `FS1108`,
*'three-way-valve' — a name cannot contain '-'. Write 'three_way_valve'.* The dash must touch both words: `V1 valve - N2`
is not read as one. (A kind starting with a letter was `FS1114`, "cannot read this line", until `L-89`.) Case and underscores are
normalised when a kind or parameter is resolved (`D-15`'s first stage, which `D-170` keeps), so only the
hyphen needs a diagnostic.

A name binds **only by its exact spelling** (`D-170`); how kinds, parameters and properties resolve, and
that component names are case-sensitive, is [`15`](15-semantic-model.md)'s and [`19`](19-fluidscript-2.md)'s.

### Numbers

`30`, `1.5`, `4.18`, `1e5`, `2.5E-3`. Every number is non-negative: `-26` is a minus and a number, and a
negative value is a unary minus in an expression.

**A `.` joins a number only when a digit follows it** (`D-51`). `30.5` is one number; `30.` is `30` and a
`.`; `30..60` is `30`, `..`, `60`. Without the restriction maximal munch takes `30.` and leaves `.60`, and an
unspaced range does not parse. **An exponent is one only when digits follow** its `e` and sign, so
`1exchanger` is a name, not `1e` followed by `xchanger`.

**`%` is a unit, never an operator** (`D-51`). There is no modulo operator
([`14`](14-expressions-and-references.md)), and `%` is the unit of a dimensionless fraction: `output =
10..100 %`. A unit symbol is recognised after a number, so `10 % 3` would lex as the quantity `10 %` and a
stranded `3`; telling that apart from a remainder needs lookahead past the following token, which
[invariant 5](#invariants) forbids. `D-50` met the same collision for `-` and resolved it the other way,
by dropping the unit.

### Units and quantities

A **quantity** is a number and the unit symbol after it, with or without spaces between: `600 kPa`,
`12 m`, `30kW`, `4.18 kJ/(kg*K)`. The symbols are exactly the entries of
[`13`](13-type-and-unit-system.md)'s table, and the lexer matches them as a table lookup, not a
sub-grammar:

- **Longest entry first** (maximal munch). `kJ/(kg*K)` and `m3/h` are single entries, and the lexer never
  reads their `/`, `*` or parentheses as tokens; `kJ/kg` must not win where `kJ/(kg*K)` fits, nor `m` where
  `m3/h` does.
- **Only immediately after a number.** Everywhere else the same characters are operators. That is what
  resolves the one genuine ambiguity in the language:

  ```text
  let cp   = 4.18 kJ/(kg*K)      -> one quantity
  let mdot = Q / (cp * rise)     -> a division and a product
  ```

  On the first line `kJ/(kg*K)` matches an entry at the position after a number; on the second nothing
  numeric precedes the `/`. Without the rule the two lines are indistinguishable, and the two readings of
  the first differ by a factor of 4180.
- **A symbol ends at a non-word character**, so `30kWx` is a name, not thirty kilowatts and an `x`.
- **Two symbols are never recognised**: `in` (the inch) and `t` (the tonne). They are the names a script
  writes most — a port and a temperature — and the table keeps them only so a conversion stays defined.

**A unit may stand apart from its number** (`D-26`), because `4.18 kJ/(kg*K)` is materially easier to
read than `4.18kJ/(kg*K)`, and every `let` in this specification is written that way. **A spaced unit is
refused when what follows it starts the next parameter**: an `=` (after spaces too), a `[`, or a `.`
before a word. `h` is an hour and an enthalpy, so in `flow = 5 h = 2000` the `h` is the next parameter and
`5` is a bare number; in `power = 30 in.t = 20` — were `in` a unit — the `.t` would do the same (`D-120`
widened the clause to `[` and `.` without adding lookahead). A range's `..` is not a `.` before a word, so
`50 m..60 m` still ends a unit. Only the spaced form asks: `30C` is thirty degrees whatever follows it.

**The unit table is part of the grammar.** Rule 1 of [Names](#names) is table-driven, so adding a unit
symbol can reclassify a name a script already uses — `3WV` would become a quantity were `WV` added. The
table is therefore append-only with review, and a test asserts that no sample's names collide with it.

**`K` is a temperature difference** (`D-172`, `D-179`): `band = 20 K`, `rise = 5 K`. `C` and `°C` are
absolute temperatures. Which symbols exist and what dimension each carries is [`13`](13-type-and-unit-system.md)'s;
the lexer knows only the spellings.

A bare number carries no unit: `30` in `power = 30` acquires kilowatts when it is bound to `power`, whose
dimension declares that canonical unit (`D-07`, `D-14`). The lexer does not know about parameters, and
must not.

### Dates and clock times

A **date** is written unquoted: `2026-01-15`, `2026-01-15 06:00`, `2026-01-15 06:00:30`, or with a `T`,
`2026-01-15T06:00`. A **clock time** is `06:30` or `06:30:15`. Each is one token, lexed by its fixed shape —
four digits, a hyphen, two, a hyphen, two, then optionally spaces or a `T` and a clock time — and neither
may run on into a word character, so `2026-01-15x` is not a date. No one means `2026-01-15` as 2026 − 1 −
15, and a clock time is the only place two digits meet a colon, so the shape costs no expression anything.

A date or a clock time may stand wherever a value may: a run's `start`, an event's time, the first column
of a curve of time. What a clock time means is [`19`](19-fluidscript-2.md) §Runs.

### Strings and titles

A **string** is `"…"` on one line, with no escapes. It holds a block's **title** — `circuit "Boiler loop":` —
a quoted hex colour, a curve's `format`, and the circuit titles a run's `steady` lists. A title is never a
reference: `"Boiler loop"` names the circuit for people and for the model, and no line refers to it by
that string except `steady`. A string with no closing quote before the end of its line is `FS1001`, and ends
at the line break.

### Operators and punctuation

| Token | Meaning |
|---|---|
| `=` | Separates a name from its value |
| `-` | The link of a connection line; subtraction; unary minus |
| `+` `*` `/` | Addition, multiplication, division ([`14`](14-expressions-and-references.md)) |
| `.` | Qualifies a name: `HX1.primary.out.t`, `sized_at.outdoor` |
| `..` | A range: `30..40 min`, `60..55 C` |
| `,` | Separates a list's items and a call's arguments, and nothing else |
| `(` `)` | Group an expression; enclose a call's arguments |
| `[` `]` | Enclose a list, `[winter, mild]`; or an index touching a name, `in[2]`, `layer[3]` (`D-120`) |
| `@` | Pins a catalogue's version: `steel_en10255@2026.1` |
| `:` | Ends a block's head; separates a curve's name from its driver; separates a clock time's fields |

**Spaces are free and there are no commas between settings.** `name = value` takes spaces on either side
of the `=`, and several pairs may share a line: `t = 85 C   p = 600 kPa`. A comma appears only inside
brackets and parentheses.

### Statement words and event words

The words that open a statement are recognised **by their position at a line's start**, not by the lexer,
which reserves nothing:

| Word | Opens |
|---|---|
| `fluidscript` | The version line |
| `project` | The project block |
| `let` | A named value |
| `curve` | A curve |
| `circuit` | A circuit block |
| `run` | A run block |

`style` opens a block too, but only as `style:` inside a project or a circuit, and `at` and `over` open an
event only inside a run and only before a time. Neither is a statement word: `at` is an ordinary name
elsewhere, and places a sensor on a node in a declaration (`TE1 temperature_sensor at N2`).

**A component may not be named one of the six**: `run pump` is a run head that fails, and a statement word
written where a name belongs is `FS1004`. **Before an `=` a statement word is a setting's name**, which no
statement starts with — a controller's `curve = heating`. The statement words, the event words and every
block's settings are one Core table, `SettingRegistry`, which the parser, the reader, the editor's lexicon
and the metadata all read (`L-86`, `A-9`).

**Kinds are not reserved.** `pump`, `radiator` and `controller` are names the binder looks up in the
component registry ([`15`](15-semantic-model.md)), so adding a kind or a parameter never breaks a script
that used the word as a name, and never changes this grammar. The one constraint runs the other way: no
kind and no alias may be spelled as a statement word. `ComponentRegistry.Verify` refuses one, and reports it
as a defect in this project's tables rather than as a script diagnostic, since no user's file can cause it.
`time` is the one built-in name: a curve's driver when the curve is of time.

**Adding a statement word is a breaking language change**, because a word that was a legal name stops being
one ([`18-script-compatibility`](18-script-compatibility.md)). The list is kept to the six for that reason.

### The lexical grammar

```ebnf
token        = name | number | quantity | date | string
             | "=" | "-" | "+" | "*" | "/" | "." | ".." | "," | "(" | ")"
             | "[" | "]" | "@" | ":" ;
trivia       = whitespace | comment | line-break ;
whitespace   = ( " " | "\t" ) , { " " | "\t" } ;    (* and a byte-order mark before the first character *)
comment      = "#" , { any-char - line-break } ;
line-break   = "\r\n" | "\n" | "\r" ;

digit        = "0".."9" ;
letter       = "A".."Z" | "a".."z" ;
word-char    = letter | digit | "_" ;
digits       = digit , { digit } ;

name         = ( letter | "_" ) , { word-char }
             | digits , word-char , { word-char } ;  (* only when no unit symbol matches: Names, rule 2 *)
number       = digits , [ "." , digits ] , [ ( "e" | "E" ) , [ "+" | "-" ] , digits ] ;
quantity     = number , [ whitespace ] , unit-symbol ;       (* the spaced form: Units and quantities *)
unit-symbol  = ? the longest entry of 13's table ending before a non-word character, not "in" or "t" ? ;
date         = digit , digit , digit , digit , "-" , digit , digit , "-" , digit , digit ,
               [ ( "T" | [ whitespace ] ) , clock ] ;
clock        = digit , digit , ":" , digit , digit , [ ":" , digit , digit ] ;
date-literal = date | clock ;                          (* one token; never followed by a word-char *)
string       = '"' , { any-char - ( '"' | line-break ) } , '"' ;
```

## Lines and blocks

### Lines

A line is the tokens between two line breaks. A blank line and a line holding only a comment produce no
tokens — their text rides as trivia on the next token — so **neither ever ends a block**. A line's
**indentation** is the whitespace before its first token, compared as text: a tab and a space are
different indentation.

### Blocks

A **block head** is one of:

- a `project`, `circuit` or `run` line, with an optional quoted title, ending in `:`;
- `style:` inside a project or a circuit;
- a curve header, `curve NAME: DRIVER` — its `:` separates the name from the driver, and every curve has
  rows, so it needs no second one;
- a component declaration whose last token is `:`.

Every following line indented **deeper than the head** belongs to its block, its **body**; the first line
at the head's indentation or less ends it, and a line shallower still ends every block it is shallower
than. Blocks nest — a `style:` or a declaration block inside a circuit — and the depth is relative, so two
levels is the most any script needs. A circuit's statements are scoped to the circuit (`D-52`): they are its
block's body, and the next line back at the top level ends it.

**A block's lines agree.** The first line of a body sets its indentation, and every other line of that
body repeats it exactly. A line indented between two levels, or with tabs where the body has spaces, is
`FS1801`, reported on that line, and the line is read in the block it is indented under — deeper than the
head, so inside it, which is the nearer level. **A curve's rows are the exception**: they are a table whose
columns the user aligns (`  -26   85` over `   18   65`), so a row needs only to be deeper than its header.
Nothing else about whitespace means anything. The formatter indents a body two spaces and leaves a curve's
rows as written; the printer keeps what was typed ([`17`](17-formatting-and-round-trip.md)).

**A head without its `:`** is `FS1812`, and the block opens anyway, so the lines under it are read as its
body rather than each reported for standing where nothing allows them. A declaration written without its
colon and followed by deeper lines becomes their head the same way, with `FS1812` reported once on the
declaration. A `:` followed by more text on the head line is `FS1114`.

### What may appear where

| Line | Top level | `project` | `circuit` | `run` | `style:` | a declaration | a curve |
|---|---|---|---|---|---|---|---|
| The version line | ✓ | | | | | | |
| A `project`, `circuit` or `run` head | ✓ | | | | | | |
| `let` | ✓ | | | | | | |
| A curve header | ✓ | | | | | | |
| `style:` | | ✓ | ✓ | | | | |
| A setting line, `name = value` | | ✓ | ✓ | ✓ | ✓ | ✓ | |
| A component declaration | | | ✓ | | | | |
| A connection line | | | ✓ | | | | |
| An event, `at` or `over` | | | | ✓ | | | |
| A curve row | | | | | | | ✓ |

A statement outside the block it belongs in — `fluid = water` at the top level, an event outside a run, a
declaration in a run — is `FS1802`, whose message names the block it belongs in. It is still parsed as what
it is, so the binder sees it and the one misplaced line costs one message. Every line of a curve's body is a
row; a row anywhere else is `FS1115`.

A setting line means what its block makes it: a block's own setting in a project, circuit, run or style,
a parameter in a declaration's body, and in a run any name that is not a run setting is an override
([Runs](#runs)).

### Classifying a line

A line is classified by its first tokens, read in this order:

1. **In a curve's body**, the line is a row.
2. **A quantity followed by a name** is a declaration whose name reads as a quantity (`3K pump`): `FS1003`.
3. **A number, a quantity, a `-` or a date** starts a curve row, and nothing else. Outside a curve it is
   `FS1115`, which says what the line is. An identifier may *start* with a digit, but the lexer has
   already made `3WV` a name, so this costs no lookahead.
4. **A line in a shape the language does not have** is `FS1806`, and the message says what to write
   instead ([below](#lines-the-language-does-not-have)).
5. **A statement word**: `fluidscript` and a number is the version line; `project`, `circuit` or `run`
   followed by a title or `:` is a block head; `let` and a name is a `let`; `curve` and a name is a curve
   header, and `curve =` a setting; `style` alone or `style:` is a style head. A statement word followed by
   anything else is `FS1004`.
6. **`at` or `over` followed by what can start a time** — a number, a quantity, a date or clock time, `-`
   or `(` — is an event.
7. **Otherwise the line starts with a qualified name**, and what follows it decides: `=` makes a setting
   line, `-` a connection line, anything else a declaration.

The lookahead is bounded by the line and never by the lines around it ([invariant 5](#invariants)); the
enclosing block then says whether the statement may stand there. A qualified name is several tokens to the
lexer and one unit to the grammar, so step 7 reads to the end of it: `HX1.secondary.out - TV1` is a
connection, `in[2].level = 0.8` a setting, and `3WV.b - N3` a connection whose first endpoint names a port
(`D-56`).

A line nothing above can read is `FS1104`, which lists the three shapes a line inside a block usually has.

### Lines the language does not have

A few words start a line only in shapes the language does not have, and a user may still type them. The
parser recognises each shape exactly and answers with `FS1806` and the form to write, rather than letting
the general rule read it as a declaration of an unknown kind:

| Line | What to write |
|---|---|
| `connections` or `schedule` alone | Connection lines anywhere in a circuit's block; events in a `run` block |
| `control`, `scenarios` or `design` and a name | A controller declaration; `cases = [...]` in the project block; nothing — sizing covers every case |
| `project`, `circuit` or `style` and a name | `project "Title":`, `circuit "Title":` and `style:`, with settings indented below |
| `curve NAME DRIVER` without the colon | `curve NAME: DRIVER` |
| `fluid`, `show`, `spacing` or `catalog` and anything but `=`, `-` or `.` | The setting, `fluid = water`, in its block |
| `inlet NAME` or `outlet NAME` alone | A connection line in either circuit: circuits join through a component both name |

Each shape is exact, so a line that merely starts with the same word is not caught: `control - N1` is a
connection and `fluid = water` a setting. The price is that a component may not be declared under one of
these words in these shapes — `design valve` reads as the shape above. This project's reasoning: that
reading is far more likely to be meant.

## Statements

### The version line

`fluidscript 2`, the first non-trivia line of the file, at the top level. A byte-order mark may precede it.
A file with no version line is read as the current language (`D-174`); what a file that states another
major gets — shown as text, never compiled — and the codes for a missing or misplaced line (`FS1701`,
`FS1705`) are [`18`](18-script-compatibility.md)'s. The parser records the line and judges nothing: it is
a statement in the file's list rather than a field of its own (`D-54`), so a line that is missing,
duplicated or not first still prints back as written. `fluidscript` followed by anything but a number is
`FS1004`.

### The project block

```text
project "Title":
  cases   = [winter, mild]
  catalog = steel_en10255@2026.1
  show    = temperature
  scale   = 20..90 C
  spacing = 1.2
  style:
    ...
```

The title is optional (`project:`). A file has one project block: a second is `FS1818` and is not read, so the first
is the project (`L-88`). The settings:

| Setting | Value |
|---|---|
| `cases` | A list of names, `[winter, mild]` |
| `catalog` | A catalogue name, optionally pinned to a version: `steel_en10255@2026.1` |
| `show` | A property name, or a list of them |
| `scale` | A range, `20..90 C` |
| `spacing` | A bare number (`D-37`) |
| `style:` | A [style block](#style-blocks) |

What each means is [`19`](19-fluidscript-2.md) §The project block and §Presentation; which properties
`show` knows is [`57`](../50-frontend/57-state-visualization.md)'s. **A catalogue's version reaches the
parser as one number token**, `2026.1`, because the number rule consumes a `.` followed by a digit and has
no context to do otherwise — recognising a version only after an `@` would make the lexer position-sensitive.
The `@` and the number touch the name, and the major and minor are split from the number's *source text*
rather than its value, which is the only way `@2026.10` stays distinct from `@2026.1`. The pin itself, and
reading it before the file is parsed, are [`18`](18-script-compatibility.md)'s.

### `let`

`let NAME = value`, at the top level. The value is any [value](#values): a quantity, an expression reading
other `let`s, or a list with one item per case, which makes the `let` a **driver** (`D-167`):

```text
let outdoor = [-20, 5] C
let rise    = 20 K
let supply  = outdoor + 2 K
```

`let` followed by anything but a name is `FS1004`. What a driver does is [`19`](19-fluidscript-2.md)
§Drivers and cases.

### Curves

```text
curve NAME: DRIVER [extrapolated] [format = "…"]
  x   y
  x   y
```

The header names the curve and, after a colon, its **driver**: a `let` or `time` (`D-57`, `D-167`).
`extrapolated` and `format = "…"` follow the driver in either order (`D-60`). A header with no driver —
`curve heating` or `curve heating:` — is `FS1116`.

Each **row** is two whitespace-separated parts, an `x` and a `y`. The `x` of a curve of time is a date and a
clock time (`2026-01-15 06:00`), or a timestamp in the header's `format`; otherwise both are bare numbers,
and `-26` is one part. The parser keeps a row's tokens whole and the binder splits them, because a
timestamp in a `format` of its own is not one token; a row with fewer than two parts is `FS1117`. Columns
may be aligned freely ([Blocks](#blocks)). A curve's rows end at the first line back at the header's
indentation.

### Circuits

`circuit "Title":`, with the title optional — a circuit written without one is named `circuit 1`,
`circuit 2`, … in file order. Its body holds, in any order:

| Line | Example |
|---|---|
| The settings `fluid`, `number`, `role` | `fluid = water`, `number = 100`, `role = district` |
| A `style:` block | overriding the project's style |
| Component declarations | `PU1 pump` |
| Connection lines | `BLR - MV1  15 m  DN32` |

`number` is the tag prefix (`D-34`); left out, the binder resolves the lowest unused multiple of 100 in
declaration order (`D-33`). `role` is resolved against the circuit-role registry (`D-35`), so the parser
neither knows nor cares which roles exist, and the set can grow without a grammar change.

### Component declarations

```text
NAME kind [at NODE] [name = value ...] [:]
```

The name comes first and the kind second, then the parameters as `name = value` pairs. The declaration
takes either of two forms, which are one grammar:

- **One line**: `RAD radiator power = demand`, with as many pairs as the line holds.
- **A block**: the line ends in `:`, and its parameters follow on indented lines, one or several pairs to a
  line: `BLR boiler:` then `out.t = 75 C`. Pairs may also stand on the head line before the `:`.

The printer keeps whichever form was written, and the formatter never converts between them.

A **parameter name** is a qualified name: `power`, `primary.out.t`, `in[2].level`, `sized_at.outdoor`. Each
part may carry an index that touches it — `in[2]`, never `in [2]` or `in[ 2 ]`, which are `FS1119` (`D-120`).
`sized_at.outdoor = -5 C` is an ordinary parameter whose name is qualified by the driver it names (`D-175`,
`D-176`, amending `D-94`). A name followed by no `=` is `FS1105`.

**The kind is any name**, resolved against the component registry at bind time ([`15`](15-semantic-model.md)),
so a new kind never changes this grammar. **`at NODE`** after the kind places an observer — a sensor — on a
node (`D-61`). A declaration with no kind is `FS1104`; a hyphenated kind is `FS1108` ([Names](#names)).

**A controller is a declaration** of kind `controller` (`D-40`, `D-168`), usually in block form:

```text
TC1 controller:
  type     = PI
  moves    = MV1
  reads    = TE1
  setpoint = 60 C
  band     = 10 K
  ti       = 120 s
```

Its settings — `type`, `moves`, `reads`, `setpoint`, `band`, `kp`, `ti`, `td`, `output`, `action`,
`differential`, `curve` — are named, never positional: there is no memorable order for an actuator, a
measurement and a setpoint, and transposing two of them would give a model that binds, solves, and drives
the wrong way. `moves` takes a component or a qualified parameter, `BLR.power` (`D-43`). Which settings each
type takes, and what they mean, is [`19`](19-fluidscript-2.md) §Controllers.

### Connection lines

```text
A - B - C
A.port - B            [length] [DN] [name = value ...]
```

A connection line is two or more **endpoints** joined by `-`, read in the direction of flow. An endpoint is
a component or node name, optionally followed by a port: `HX1.secondary.out`, `T1.in[2]`, `MV1.a`. A name no
declaration names is a node ([`15`](15-semantic-model.md)'s rule I1). `N1-N2` written without spaces is a
connection too.

**A pipe on the link** is written at the end of a line with one link, with no `=` for its two commonest
properties (`D-110`, `D-166`), each recognised by its form:

| Part | Read as | Example |
|---|---|---|
| A quantity | The pipe's length | `15 m` |
| A bare name | Its DN designation, checked against the catalogue | `DN32` |
| `name = value` | Any other pipe property | `roughness = 0.05 mm`, `nodes = 4` |

The parts come in any order after the last endpoint. On a line with more than one link they are
`FS1803`, which asks which link is meant; the line is kept, so the chain still binds. A bare word that is
not a DN designation is `FS1813`. How ports are inferred, and what the binder makes of the pipe, is
[`19`](19-fluidscript-2.md) §Connections.

A name with a hyphen in a chain — `N1 - HX-1 - N2` — is `FS1108`: the chain fails at `1`, and the parser
reads back to name `HX-1`.

### Style blocks

```text
style:
  colour = "#2f6f9f"
  width  = 2
  corner = fillet
  line   = dashed
```

`style:` stands inside the project block or a circuit's. Its settings are `colour` (also `color`: a CSS
colour name, or a quoted `#rrggbb` or `#rgb`), `width` (pixels, a bare number or `px`), `corner` (`sharp`,
`fillet`) and `line` (`solid`, `dashed`, `dotted`, `dashdot`). A setting stated twice in one block is
`FS1202`, and the later wins. A style applies to the block that holds it and is never a name: there are no
named styles and no component style (`D-171`; `D-104`'s named styles are not part of the language). What a
circuit's style overrides is [`19`](19-fluidscript-2.md) §Presentation.

### Runs

```text
run "Title":
  from     = CASE
  start    = DATE
  duration = TIME
  frame    = TIME
  steady   = ["Circuit title", ...]

  NAME = value                         # an override
  at   T        target = value         # a step
  over T1..T2   target = v1..v2        # a ramp
```

The title is optional. The five settings are `from` (a case name), `start` (a date and time), `duration`
and `frame` (times), and `steady` (a list of circuit titles). **Any other setting line is an override**,
held from the run's start: a qualified parameter, `RAD.power = 20 kW`, or a `let`, `outdoor = weather`.

**An event** is a step or a ramp. Its time is a duration from the run's start — `20 min`, `1 h` — or a clock
time, `08:30`; its target is an endpoint with a qualified name, `TC1.setpoint`, `HX1.secondary.out.t`.
`over` takes a range for its time and one for its value, **both ends of each**: a single value on either
side is `FS1807`, whose message says which is missing and shows the step form. The line is kept, so the
target still resolves. `at` and `over` open an event only when a time follows them
([Statement words](#statement-words-and-event-words)); inside anything but a run the event is `FS1802`.

What a run's settings, overrides and events do is [`19`](19-fluidscript-2.md) §Runs, and what may be a
target is [`33`](../30-solver/33-transient-time-domain.md)'s.

### Values

A value is what follows an `=`:

| Form | Example | Notes |
|---|---|---|
| An expression | `600 kPa`, `outdoor + 2 K`, `max(rise, 15 K) * 3 / 2`, `TE1`, `demand kW` | Numbers, quantities, dates, strings, references, calls, `+ - * /`, unary minus, parentheses ([`14`](14-expressions-and-references.md)) |
| A list | `[60, 50] C`, `[winter, mild]` | One item per case, in the order `cases` names them (`D-143`) |
| A range | `30..40 min`, `60..55 C`, `20..90 C` | Two expressions joined by `..` |
| A catalogue | `steel_en10255@2026.1` | `catalog` only |

**A list** is `[a, b, …]`: items separated by commas, each an expression. A unit written after the
closing bracket — a symbol of `13`'s table, or a compound written as three touching tokens, `kg/s` —
belongs to every item that states none: a bare number, a negated one (`[-20, 5] C` is a −20 °C design
day), and a bare name. An item that states its own unit, or is an expression such as `a + 5`, is read as
written (`D-179`). `[]`, `[30,]`, `[30 10]` and an unclosed `[30` are `FS1121`; a list with the wrong number
of items for the cases is the binder's (`FS1540`, `FS1541`). A list does not nest.

**A range** is `a..b`. A unit on its upper end applies to both: `30..40 min` is thirty to forty minutes and
`60..55 C` is 60 to 55 °C. The `..` may be spaced or not.

**A reference may carry a unit** after it, `power = demand kW`, which gives a curve's bare numbers one
(`L-35`, `D-57`). Only a spelling the unit table holds is taken, so a name after a name is never read as a
unit, and the next-parameter rule of [Units and quantities](#units-and-quantities) applies.

**A value is one expression, list or range, and the next pair starts where it ends.** That is what lets
pairs share a line without commas: in `power = Q * 1.1   rise = 20 K`, the expression `Q * 1.1` ends at
`rise`, which no operator joins to it, and `rise =` starts the next pair.

An expression may nest 64 levels deep — parentheses, call arguments, unary minus — before the line is read
as malformed. That is far beyond any expression a script states and far below the parser's stack; without
it one line of a few thousand `(` would overflow the stack, which no parser can catch.

## The syntactic grammar

A body is written `indented(x)`: one or more lines of `x`, each indented deeper than the head and alike
([Blocks](#blocks)). Which statements may stand in which body is also [What may appear
where](#what-may-appear-where); a statement in the wrong one parses as itself and is `FS1802`.

```ebnf
script          = { top-line } ;
top-line        = version-line | project-block | let-line | curve-block | circuit-block | run-block ;

version-line    = "fluidscript" , number ;

project-block   = "project" , [ string ] , ":" , indented( setting-line | style-block ) ;
circuit-block   = "circuit" , [ string ] , ":" ,
                  indented( setting-line | style-block | declaration | connection-line ) ;
run-block       = "run" , [ string ] , ":" , indented( setting-line | event ) ;
style-block     = "style" , ":" , indented( setting-line ) ;

let-line        = "let" , name , "=" , value ;

curve-block     = "curve" , name , ":" , name , { curve-modifier } , indented( curve-row ) ;
                  (* the driver is a let or "time"; rows need only be deeper than the header *)
curve-modifier  = name | pair ;                         (* extrapolated ; format = "…" -- D-60 *)
curve-row       = row-part , row-part , { row-part } ;  (* whitespace-separated; the binder splits them *)
row-part        = ? a run of tokens with no whitespace between them ? ;

declaration     = name , name , [ "at" , name ] , { pair } , [ ":" , indented( setting-line ) ] ;
                  (* the name, the kind, an observer's node -- D-61 *)

connection-line = endpoint , "-" , endpoint , { "-" , endpoint } , { pipe-part } ;
                  (* pipe parts only on a line with one link: FS1803 *)
endpoint        = name , [ "." , qualified-name ] ;
pipe-part       = quantity | name | pair ;              (* a length, a DN designation, any other property *)

setting-line    = pair , { pair } ;
pair            = qualified-name , "=" , value ;
qualified-name  = indexed-name , { "." , indexed-name } ;    (* the dots touch the names *)
indexed-name    = name , [ "[" , digits , "]" ] ;            (* the brackets touch the name -- D-120 *)

event           = ( "at" | "over" ) , point-or-range , endpoint , "=" , point-or-range ;
                  (* "over" needs a range on both sides: FS1807 *)
point-or-range  = expression , [ ".." , expression ] ;

value           = list , [ unit ] | expression , [ ".." , expression ] ;
list            = "[" , expression , { "," , expression } , "]" ;
unit            = name | name , "/" , name ;             (* a spelling of 13's table; the three touch *)

expression      = term , { ( "+" | "-" ) , term } ;      (* evaluation: 14 *)
term            = factor , { ( "*" | "/" ) , factor } ;
factor          = "-" , factor | primary ;
primary         = number | quantity | date-literal | string
                | "(" , expression , ")"
                | name , "(" , [ expression , { "," , expression } ] , ")"
                | reference , [ unit ]
                | name , "@" , number ;                  (* a catalogue; the three touch *)
reference       = name , { "." , indexed-name } ;
```

## Syntax tree

Nodes are immutable records. **A node holds the tokens it consumes** — its keywords and punctuation, not
only its structural children — and trivia hangs off those tokens (`D-55`), which is the model
[`17`](17-formatting-and-round-trip.md) states and the only one that round-trips: no node is a `let`, an
`=`, a `-` or a `(`, so a tree that dropped them would drop every space around them too. A node's span and
trivia are therefore **derived** from its tokens, never supplied, which is what makes
[invariant 3](#invariants) hold by construction.

**The trivia model.** A token's **leading trivia** holds everything between the previous line's end and the
token: line breaks, blank lines, whole-line comments and the line's indentation. Its **trailing trivia**
holds the spaces and the comment up to its own line's end. Indentation is therefore the first token's
leading trivia; the parser reads it to place the line in a block, and the printer writes it back as it was.

| Line or value | Node |
|---|---|
| The whole file | `ScriptSyntax(Statements, EndOfFile)` — one ordered list, the version line among the statements (`D-54`) |
| A block | `BlockSyntax(Head, Colon, Body)`: the head statement, its `:` when written, and the body's statements |
| The version line | `VersionDirectiveSyntax(Keyword, Major)` |
| A `project`, `circuit` or `run` head | `ProjectHeadSyntax`, `CircuitHeadSyntax`, `RunHeadSyntax` (`Keyword`, `Title`) |
| `style:` | `StyleHeadSyntax(Keyword)` |
| `let` | `LetBindingSyntax(Keyword, Name, Equals, Value)` |
| A curve header | `DriverCurveHeadSyntax(Keyword, Name, Colon, Driver, Modifiers)` |
| A curve row | `CurveRowSyntax(Parts)`, the row's tokens whole |
| A setting line | `SettingLineSyntax(Assignments)`, each a `ParameterSyntax(Name, Equals, Value)` |
| A declaration | `ComponentDeclarationSyntax(Name, Kind, AtKeyword, AttachedTo, Parameters, …)` |
| A connection line | `ConnectionSyntax(First, Links)`, each link a `-` and an endpoint; with a pipe, `PipedConnectionSyntax(Connection, Properties)` |
| An event | `DisturbanceSyntax(Keyword, When, Target, Equals, Value)`, `When` and `Value` each a `PointSyntax` or `RangeSyntax` |
| A line that cannot be read | `MalformedStatementSyntax(Parts)`, its tokens whole |
| Values | `NumberLiteralSyntax`, `QuantityLiteralSyntax`, `DateLiteralSyntax`, `StringLiteralSyntax`, `ReferenceSyntax`, `QuantityReferenceSyntax`, `CallSyntax`, `UnaryExpressionSyntax`, `BinaryExpressionSyntax`, `ParenthesizedExpressionSyntax`, `ScenarioListSyntax`, `UnitListSyntax`, `RangeExpressionSyntax`, `CatalogReferenceSyntax` |

Three choices in the shapes are deliberate:

- **A literal keeps its source spelling.** `1.50`, `1.5` and `15e-1` are one value and three strings, and
  the printer must reproduce the one written (`R-25`).
- **Parentheses are a node** (`D-54`). `(a + b) * c` and `a + b * c` differ, and a redundant grouping in an
  engineering formula is usually deliberate; reconstructing parentheses from precedence prints a correct
  expression rather than the user's.
- **A block is a statement**, so every token appears once in the tree, in source order, and the printer
  walks one sequence.

`MalformedStatementSyntax` is what makes `P4` and `R-05` real. A line that cannot be read becomes one of
these, the parser resumes at the next line, and every other line is unaffected; a malformed head still
opens its block, so its body is read as its body.

## Parser API

```csharp
public static class FluidScriptParser
{
    /// <summary>Parses source text into a syntax tree. Never throws on any input.</summary>
    /// <param name="source">The script source. Any characters at all; may be empty.</param>
    /// <returns>
    /// The tree, always non-null, together with every diagnostic the lexer and the parser produced. A tree
    /// containing <see cref="MalformedStatementSyntax"/> nodes is a normal result, not a failure.
    /// </returns>
    public static ParseResult Parse(SourceText source);
}
```

`ParseResult` carries the source, the `ScriptSyntax` root and the diagnostics. **The parser does not read
the component registry**: kinds and parameters are names, and the only table it reads is
`SettingRegistry`'s statement words.

## Invariants

1. **Lossless.** Concatenating every token and trivium in source order reproduces the input byte for byte,
   and so does concatenating the tree's tokens with their trivia. Asserted over `samples/` and every
   `fluidscript` block in `plan/` and `docs/`.
2. `Parse` never throws, for any byte sequence, including invalid UTF-8, a 100 MB single line and a line of
   a few thousand `(`.
3. Every `SyntaxNode.Span` is within the source bounds, and a parent's span contains every child's.
4. **Every line belongs to exactly one block**, decided by its indentation and the head lines above it. A
   misindented line is reported (`FS1801`) and read at the nearer level; no line is dropped.
5. **Lookahead is bounded by the line.** The lexer decides a unit by what follows it on the same line, and
   the parser classifies a line from its own tokens, never from the lines around it; indentation only
   places it in a block. Unbounded lookahead is never used.
6. No token spans a line break. Strings therefore cannot hold one; accepted, since a string is a title or a
   colour.
7. `#` has exactly one role: the start of a comment. It is never an operator, so a `#` makes the rest of its
   line trivia — **except inside a string**, which is what makes a quoted hex colour a colour rather than a
   comment. A string is scanned as one token from its opening quote, so a `#` between quotes is never at a
   token boundary.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1001` | Unterminated string literal | Error | `Unterminated string; add a closing quote.` |
| `FS1002` | A character the language does not use | Error | `'{ch}' is not valid here.` |
| `FS1003` | A name that reads as a quantity, where a name belongs | Error | `'{name}' reads as a quantity ({value} {unit}), not a name. Try '{suggestion}'.` |
| `FS1004` | A statement word where a name belongs, or not followed by what its statement needs | Error | `'{word}' is reserved. Choose another name.` |
| `FS1101` | *(retired)* | — | A second 'connections' or 'schedule' section in one circuit. Retired (D-174), not reused: a circuit has no sections. |
| `FS1102` | *(retired)* | — | A connection above a circuit's 'connections' line. Retired (D-174), not reused: connection lines go anywhere in the circuit block. |
| `FS1103` | *(retired)* | — | A statement in the wrong section of a circuit. Retired (D-174), not reused: a statement belongs to a block, and one in the wrong block is FS1802. |
| `FS1104` | A line that cannot be classified | Error | `Cannot read this line. Expected a declaration such as 'PU1 pump', a connection such as 'A - B', or a setting such as 'name = value'.` |
| `FS1105` | A parameter name with no `=` | Error | `'{token}' looks like a parameter but has no value. Write '{token} = …'.` |
| `FS1106` | *(retired)* | — | A step or ramp outside a 'schedule' section. Retired (D-169, D-174), not reused: an event belongs to a run, and one outside it is FS1802. |
| `FS1107` | *(retired)* | — | A schedule in a circuit with no time to run in. Retired (D-169, D-174), not reused: events belong to a run, which has its duration. |
| `FS1108` | A hyphen inside a name or a kind | Error | `'{text}' — a name cannot contain '-'. Write '{underscored}'.` |
| `FS1109` | *(retired)* | — | 'in' or 'out' where an 'inlet'/'outlet' attachment line was meant. Retired (D-174), not reused: circuits join through a component both name. |
| `FS1110` | *(retired)* | — | A malformed 'inlet'/'outlet' attachment line. Retired (D-174), not reused: there are no attachment lines. |
| `FS1111` | *(retired)* | — | A malformed 'control' line. Retired (D-168, D-174), not reused: a loop is one controller declaration. |
| `FS1112` | *(retired)* | — | A 'project' or 'spacing' line after the first circuit. Retired (D-174), not reused: both are settings of the project block. |
| `FS1113` | *(retired)* | — | A 'spacing' line given a quantity. Retired (D-174), not reused: spacing is a project setting, and a unit on it is FS1514. |
| `FS1114` | Text after a statement that is already complete, or after a head's `:` | Error | `'{extra}' is more than this line can hold.` |
| `FS1115` | A curve row outside a curve | Error | `Put this pair under a 'curve' line.` |
| `FS1116` | A curve header with no driver | Error | `'curve {name}' needs what it depends on after a colon, such as 'curve {name}: outdoor'.` |
| `FS1117` | A curve row that is not two values | Error | `A curve row is one x and one y, such as '-26 50'.` |
| `FS1118` | *(retired)* | — | A 'design' line with no values. Retired (D-174), not reused: there is no design line; the first case is the operating one. |
| `FS1119` | An index that is not a whole number touching its name: `in[a]`, `in[ 2 ]`, `in[]` (`D-120`) | Error | `An index is a whole number in brackets right after the name, such as 'in[2]'.` |
| `FS1120` | *(retired)* | — | A 'scenarios' line with no names. Retired (D-174), not reused: the project block names its cases, 'cases = [...]'. |
| `FS1121` | A list that is not comma-separated values: `[]`, `[30,]`, `[30 10]`, `[30` (`D-143`) | Error | `A list is one value per case, separated by commas, such as '[30, 10]'.` |
| `FS1201` | *(retired)* | — | A token of a one-line style that was no style. Retired (L-77, D-174), not reused: each style setting is checked against its key, which is FS1514. |
| `FS1202` | A style block that states one setting twice; the later wins | Warning | `'{a}' overrides the earlier '{b}'.` |
| `FS1203` | A value written as a bare `#rrggbb`, which the comment took | Warning | `'#' starts a comment; the rest of this line was ignored. Write the colour as "{hex}".` |
| `FS1204` | *(retired)* | — | A named style used and never defined. Retired (D-174), not reused: there are no named styles and no component style (19). |
| `FS1205` | *(retired)* | — | A named style defined twice. Retired (D-174), not reused: there are no named styles (19). |
| `FS1210` | `show` names a property the colour scale does not know (`57`) | Warning | `Nothing to show called '{name}'. Available: {list}.` |
| `FS1213` | The same property twice in one `show` (`57`) | Info | `'{name}' listed twice.` |
| `FS1214` | A second `show` setting; only the first is read (`57`) | Warning | `Only the first 'show' is used.` |

The retired codes stay listed so that none is reused and an old code can still be looked up (`D-180`).
The block-structure codes — a misindented line (`FS1801`), a statement outside its block (`FS1802`), a head
without its `:` (`FS1812`), a pipe on a chain (`FS1803`), a line in a shape the language does not have
(`FS1806`), a one-valued ramp (`FS1807`) — are [`19`](19-fluidscript-2.md)'s range, raised by this parser.

**`FS1203` is a warning about a comment, which sounds odd until you see the failure.** `colour = #2f6f9f`
comments out everything from `#`, leaving a setting with no value — and a style line with nothing in it
would otherwise render in the default colour, silently. The lexer cannot know a colour was meant, but the
parser can: a line that ends in `=` whose comment begins with three or six hex digits is worth one warning,
in place of the general `FS1104`.

Message wording rules — sentence case, no jargon, and a suggested fix wherever one exists — are owned by
[`16-diagnostics`](16-diagnostics.md).

## Worked example

Lexing and classifying two lines of the complete script, each inside the circuit's block:

```text
  RAD  radiator    power = demand
  BLR - MV1                  15 m  DN32
```

| # | Token | Kind | Why |
|---|---|---|---|
| — | `  ` | Trivia | the line's indentation: leading trivia of `RAD`, and what places the line in the circuit's body |
| 1 | `RAD` | Name | letters and digits; not a statement word |
| 2 | `radiator` | Name | a kind is a name; the registry resolves it at bind time |
| 3 | `power` | Name | |
| 4 | `=` | Equals | |
| 5 | `demand` | Name | a reference; no unit follows it |
| — | `  ` … | Trivia | the next line's break and indentation, leading trivia of token 6 |
| 6 | `BLR` | Name | |
| 7 | `-` | Minus | |
| 8 | `MV1` | Name | |
| 9 | `15 m` | Quantity | a number, spaces, and `m`, which is followed by spaces and `DN32` — not `=`, `[` or `.` — so the unit stays with its number |
| 10 | `DN32` | Name | a word; it is the parser, not the lexer, that reads it as a designation |

**The first line** starts with a name followed by a name, so it is a declaration (step 7 of
[Classifying a line](#classifying-a-line)): `ComponentDeclarationSyntax` with `Name = RAD`, `Kind = radiator`
and one `ParameterSyntax`. **The second** starts with a name followed by `-`, so it is a connection line
with one link; tokens 9 and 10 follow its last endpoint, so it is a `PipedConnectionSyntax` whose pipe is
15 m of DN32.

**The spaced unit's clause in one line.** In `flow = 5 h = 2000`, `5` and `h` would join as five hours —
but `h` is followed, past a space, by `=`, so `5` stays a bare number and `h = 2000` is the next pair. Drop
the clause and `flow` takes five hours, `2000` is stranded, and the line still parses.

## Acceptance criteria

- [x] **Every sample in `samples/`, and every `fluidscript` block in `plan/` and `docs/`, parses with
      exactly the codes its fence declares** — a block meant to be wrong declares them, as
      [`61`](../60-docs-and-devex/61-documentation-plan.md) specifies (`FluidScriptParserTests`). A corpus
      test over the specification's own examples is what catches a grammar that passes its own criteria
      while its examples do not parse.
- [ ] Round trip: for every file in `samples/`, `Print(Parse(text)) == text` byte for byte, and
      concatenating the tree's tokens with their trivia reproduces the source (`D-55`).
- [ ] `3WV` lexes as a name; `2px` as a quantity; `30` as a number; `3K pump` is `FS1003`.
- [ ] `4.18 kJ/(kg*K)` lexes as one quantity, and `Q / (cp * rise)` with no unit symbol among its tokens —
      the same characters, classified by whether a number precedes them.
- [ ] `flow = 5 h = 2000` lexes as two pairs and never as five hours; `in.t = 20` and `t = 85 C` hold no
      unit symbol before their `=`.
- [ ] Maximal munch: with both `kJ/kg` and `kJ/(kg*K)` in the table, the longer wins where it fits.
- [ ] `50 %` is one quantity and `10 % 3` a quantity followed by a number (`D-51`).
- [ ] `30.5` is one number, `30.` a number and a `.`, and `30..60` is `30`, `..`, `60` (`D-51`).
- [ ] `2026-01-15 06:00` and `08:30` are one token each; `2026-01-15x` is not a date.
- [ ] `#` anywhere outside a string makes the rest of the line trivia, and `colour = #2f6f9f` is `FS1203`.
- [ ] `HX-1` as a name, in a declaration or a chain, is `FS1108` suggesting `HX_1`, and never a node.
- [ ] A body line indented unlike its block is `FS1801` and stays in the block; a curve's rows may be
      aligned freely.
- [ ] A declaration written without its `:` and followed by deeper lines is `FS1812` once, and the lines
      bind as its parameters.
- [ ] Each statement in the wrong block is `FS1802` and still parses as itself.
- [ ] `run pump` is `FS1004`; a controller's `curve = heating` is a setting.
- [ ] `HX1.secondary.out - TV1` classifies as a connection and `in[2].level = 0.8` as a setting.
- [ ] `(a + b) * c` prints back with its parentheses, and so does `(a) + b` (`D-54`).
- [ ] `let   x = 1` and `HE1  heat_exchanger` keep every run of whitespace, including the interior runs
      that no structural child owns (`D-55`).
- [ ] Deleting each character of each sample in turn never throws and always yields a lossless tree, and
      a fuzz corpus of random mutations produces no exception and no span outside bounds.
- [ ] Every live code of `FS10xx`, `FS11xx` and `FS12xx` above has a test that triggers exactly it.

## Open questions

None. Quantity-shaped names such as `3K` remain unavailable and get `FS1003`; a curve row's parts are
split by the binder, not the grammar.
