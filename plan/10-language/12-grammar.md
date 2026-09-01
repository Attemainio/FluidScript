---
id: 12-grammar
title: Grammar
tier: 10-language
status: reviewed
owns: [lexical grammar, syntactic grammar, reserved words, sections, AST node shapes, trivia model, schedule and catalog syntax, circuit numbering and attachment syntax, global directives, control-binding syntax]
depends_on: [02-glossary, 06-decision-log, 11-language-overview, 13-type-and-unit-system]
traces_to: [R-01, R-03, R-04, R-05, R-39, R-46, R-48, R-49]
open_questions: 0
last_review_pass: 6
---

# Grammar

## Purpose

The concrete syntax of FluidScript's declarative language (`D-01`): what the lexer produces, what the parser accepts, and what the
syntax tree looks like. This is the document an implementer types from, and the one the round-trip
printer ([`17-formatting-and-round-trip`](17-formatting-and-round-trip.md)) is the inverse of.

## Responsibilities

**Owns.** Lexical rules, the token set, the reserved-word list, the syntactic grammar, AST node
shapes, and the trivia model.

**Explicitly does not own.** Unit symbols and their meanings
([`13-type-and-unit-system`](13-type-and-unit-system.md)), expression evaluation
([`14-expressions-and-references`](14-expressions-and-references.md)), name resolution
([`15-semantic-model`](15-semantic-model.md)), diagnostic codes
([`16-diagnostics`](16-diagnostics.md) — this document names them, that one defines them).

## Shape of the language

**Line-oriented.** One statement per line. No statement terminator and no line continuation in v1.
Indentation is trivia and carries no meaning — this is deliberate: significant indentation would make
canvas write-back (`R-25`) responsible for preserving semantic whitespace, which is the class of bug
that makes round-tripping unreliable.

**Three sections.** A declaration section (everything before the `connections` keyword), a connection
section, and an optional `schedule` section for transient disturbances. Each header is a section
marker, not a block: there are no braces and no `end`.

## Lexical grammar

```ebnf
token          = keyword | identifier | quantity | number | string
               | "=" | "-" | "." | ".." | "," | "(" | ")" | "+" | "*" | "/" | "@" ;

trivia         = whitespace | line-comment | newline ;
line-comment   = "#" , { any-char - newline } ;
whitespace     = { " " | "\t" } ;

digit          = "0".."9" ;
letter         = "A".."Z" | "a".."z" ;
word-char      = letter | digit | "_" ;

word           = word-char , { word-char } ;      (* classified below *)
number         = digit , { digit } , [ "." , digit , { digit } ] , [ ("e"|"E") , ["+"|"-"] , digit , {digit} ] ;
quantity       = number , [ whitespace ] , unit-symbol ;   (* see the whitespace rule below *)
unit-symbol    = ? longest matching entry of the unit-symbol table ? ;   (* see below *)
string         = '"' , { any-char - '"' } , '"' ;
```

### `%` is a unit, never an operator, and a `.` needs a digit after it

Two spellings were each doing two jobs, and `D-51` gave each one job.

`%` is a unit symbol for `Dimensionless` and is **not** in the token set above:
[`14-expressions-and-references`](14-expressions-and-references.md) has no modulo operator. A unit
symbol is recognised after a number, so `10 % 3` would otherwise lex as the quantity `10 %` followed
by a stranded `3` — `D-50`'s `-` collision with the sides reversed, and separating the readings needs
lookahead past the following token, which invariant 5 forbids.

**A `.` joins a number only when a digit follows it.** `30.5` is one number; `30.` is the number `30`
followed by a `.` token; `30..60` is `30`, `..`, `60`. Without the restriction, maximal munch takes
`30.` and leaves `.60`, so the range production sees one dot where it needs two and an unspaced
`over 30..60` does not parse. Every range written in this specification happens to space its `..`,
which is why the two productions contradicted each other for six review passes.

### Comments

`#` begins a comment that runs to end of line (`D-13`). `#` is therefore **not** available as an
operator, and a hex colour in a `style` directive is written as a quoted string — `style "#2f6f9f"` —
because `#2f6f9f` would otherwise comment out the rest of the line. `string` is already a legal
`style-token`, so that costs no new syntax.

**This replaces the brief's `|`.** `|` reads well as a margin rule when comments are column-aligned,
which is why the brief used it, and it is genuinely awkward to type on most keyboard layouts —
`AltGr`+a key on the Nordic and German layouts this project's author uses, a chorded key on many
others. `#` is unshifted or single-shifted nearly everywhere, and it is what every reader already
recognises as a comment from shell, Python, YAML, and TOML. `D-13` records the trade in full.

`|` is now free and is left unallocated: it is not an operator, not a comment, and produces `FS1002`.
Reclaiming it later for a table or pipeline form is then a non-breaking addition.

### Word classification — the sharp edge

A `word` may start with a digit: `3WV` is a legal identifier (`R-01`, and the brief requires it). This
collides with quantity literals, since `30kW` is also a word by the rule above. Classification:

1. If the word is a **reserved word**, it is a keyword.
2. Otherwise, if the word matches `number` exactly, it is a number literal — **unless** the *next*
   word is a known unit symbol not immediately followed by `=`, in which case the two combine into one
   quantity literal (rule 5).
3. Otherwise, if the word matches `number , unit-symbol` where the suffix is a **known unit symbol**
   from [`13-type-and-unit-system`](13-type-and-unit-system.md), it is a quantity literal.
4. Otherwise it is an identifier.
5. **A unit symbol may be separated from its number by horizontal whitespace**, provided it is not
   immediately followed by `=`. `30 K` is one quantity; `30 in=20` is a number followed by a parameter
   named `in`.

So `3WV` → rule 4 (`WV` is not a unit). `2px` → rule 3 (`px` is a unit). `30` → rule 2. `20C` → rule 3.
`45 mm` → rule 5. `30 in=20` → rule 2, because `in` is followed by `=`.

### Rule 5 exists because every `let` in the tree depends on it

`let cp = 4.18 kJ/(kg*K)` and `let dT = 30 dK` are written with spaces everywhere in this
specification under `D-26`, and without rule 5 they are syntax errors. The EBNF above always permitted the space;
rules 1–4 could not see across one, and
[`13-type-and-unit-system`](13-type-and-unit-system.md) previously asserted the opposite. Rule 5 is
that contradiction resolved — the permissive reading, because `4.18kJ/(kg*K)` is materially harder to
read than `4.18 kJ/(kg*K)`.

**The `=` clause is what makes the permissive reading safe.** `in` is the inch symbol and a parameter
name in the brief's own line:

```fluidscript
HE1 heat_exchanger power=30 in=20 out=50
```

Without the clause, `30 in` is thirty inches and that line means nothing like what it says. One token
of lookahead is enough to tell them apart, and it is the only lookahead in the lexer.

**The consequence, stated plainly:** a component may not be named such that it parses as a quantity.
`3K` is three kelvin, not a component named `3K`. This is checked at declaration and produces `FS1003`
with the suggestion to rename (`K3` works). It is a genuine wart. The alternative — requiring a space
before every unit — costs the brief's `2px` and `20C` forms, which is worse. The permissive rule and
identifier collision diagnostic are therefore fixed for v1.

### Rules 1--4 classify a *word*, and a number is not always one

`word-char` is `letter | digit | "_"`, so `4.18` and `1e+5` are not words: they hold characters a name
cannot. The classifier therefore scans the number first, maximally, and only then asks what follows:

> If word characters follow the number and no unit symbol matches, the number and that run are **one
> identifier** — but only when the number is itself spellable inside a name. A number holding a decimal
> point or an exponent sign is not, so it stands alone and the word after it lexes separately.

`3WV` and `3E1x` are one identifier each; `1.5x` is the number `1.5` followed by the name `x`, because
no name can contain a `.`. Both readings are errors further up, and the rule exists so that they are
the *same* error every time rather than depending on which of rules 1--4 was consulted first.

### `unit-symbol` is a table lookup, not a sub-grammar

The accepted symbols are exactly the entries of
[`13-type-and-unit-system`](13-type-and-unit-system.md)'s table, matched **longest-first (maximal
munch)** against the text at that position. Compound symbols such as `kg/s`, `m3/h`, `l/min` and
`kJ/(kg*K)` are **single atomic entries** in that table; the lexer never parses their internal `/`, `*`
or parentheses as tokens.

That is what resolves the one genuine ambiguity in the language:

```fluidscript
let cp   = 4.18 kJ/(kg*K)
let mdot = Q / (cp * dT)
```

On the first line, `kJ/(kg*K)` matches a table entry at the position immediately following a number, so
it lexes as one `unit-symbol` and the line is a single quantity literal. On the second, nothing numeric
precedes the `/`, so no unit-symbol match is attempted and `/`, `(`, `*`, `)` lex as operators.
**A unit symbol is recognised only immediately after a number**; everywhere else the same characters
are operators. Without that rule the two lines are indistinguishable, and the two readings of the first
differ by a factor of 4180.

Maximal munch is load-bearing rather than a tidiness preference: `kJ/kg` must not win over `kJ/(kg*K)`
where the longer entry matches, and `m` must not win over `m3/h`.

Rule 3 is **table-driven on the unit set**, which means adding a unit symbol can reclassify an existing
identifier. That is a breaking change to the language and must be treated as one: the unit table is
append-only-with-review, and a test asserts that no sample script's identifiers collide.

### Reserved words

`fluidscript` · `project` · `circuit` · `fluid` · `dynamic` · `static` · `spacing` · `style` · `show` · `let` ·
`catalog` · `connections` · `schedule` · `supply` · `return` · `control`

Five words were added for `D-33`, `D-37` and `D-40`: `project`, `spacing`, `supply`, `return` and `control`.
Each introduces a statement, so each must be recognisable from the first token — the same standard the
original eleven meet.

**Adding a reserved word is a breaking language change**, because a word that was a legal identifier
stops being one ([`18-script-compatibility`](18-script-compatibility.md)). These five ship inside
major 1 under `18`'s pre-release exemption — no durable v1 file exists yet — and not because a sweep
of this repository's samples found no collision. That sweep was run and found none, but it is evidence
about files we wrote, not about files users write. After v1 ships, a sixth reserved word needs a new
major and a renaming migration.

Reserved words may not be used as identifiers. The list is deliberately tiny (P6): component *kinds*
like `heat_exchanger` are **not** reserved — they are looked up in the component registry at bind
time, so adding a component kind never breaks an existing script that used the name as an identifier.

**`node` and `pipe` are no longer reserved**, and that is a correction rather than a relaxation. They
were reserved on the grounds that "the ambiguity would be real", but no ambiguity exists: neither word
introduces a directive, so neither can start a statement, and both occur only in `kind-name` position
— where `kind-name = identifier` cannot match a keyword token. Reserving them made `P1 pipe length=45`
unparseable, which is a line in both reference circuits. Every kind now resolves the same way, through
the registry ([`15-semantic-model`](15-semantic-model.md)).

### Hyphens are not part of a name

`word-char` is `letter | digit | "_"`, so `-` never joins two words: `3-way-valve` lexes as
`3`, `-`, `way`, `-`, `valve`, and `three-way-valve` as three words with two dashes between them.

**This is deliberate and it is not free.** `-` is the connection operator (`N1 - N2`) and the
subtraction operator (`Tflow - dTdesign`), and both are unspaced-legal today. Admitting `-` into
words would make `N1-N2` a single identifier that inference rule I1 would silently create a node
for — a wrong answer that compiles, which is the worst outcome available. The dash stays an operator.

Because hyphenated kind names are what a user coming from HTML, CSS or `/docs` filenames will
naturally type, the parser recognises the shape and says so rather than failing obscurely. A
`kind-name` position followed by `-` and a further word produces `FS1108` with the underscored form as
a suggested fix:

> *`'3-way-valve'` — a name cannot contain `-`. Write `3_way_valve`.*

Underscores, spacing and case are all normalised away during kind resolution
([`15-semantic-model`](15-semantic-model.md), `D-15`), so `3_way_valve`, `3WayValve` and `threewayvalve` all
reach `three_way_valve` without the user learning a canonical spelling. Only the hyphen is lexically
impossible, and only the hyphen needs a diagnostic.

## Syntactic grammar

```ebnf
script          = version-directive , { statement } ;
statement       = project-directive | spacing-directive
                | circuit-header | attachment | fluid-directive | catalog-directive | style-directive
                | show-directive | let-binding | component-decl | control-binding
                | connections-header | connection
                | schedule-header | disturbance ;

version-directive   = "fluidscript" , unsigned-integer ;
project-directive   = "project" , [ "dynamic" | "static" ] , identifier ;
spacing-directive   = "spacing" , number ;
circuit-header      = "circuit" , identifier , [ unsigned-integer ] ;
attachment          = ( "supply" | "return" ) , endpoint ;
control-binding     = "control" , parameter , { parameter } ;
fluid-directive     = "fluid" , [ "dynamic" | "static" ] , identifier ;
catalog-directive   = "catalog" , identifier , [ "@" , catalog-version ] ;
catalog-version     = unsigned-integer , "." , unsigned-integer ;
style-directive     = "style" , { style-token } ;
show-directive      = "show" , property-name , { property-name } , [ range ] ;
property-name       = identifier ;   (* resolved against the property registry — see 57 *)
let-binding         = "let" , identifier , "=" , expression ;
connections-header  = "connections" ;
schedule-header     = "schedule" ;

component-decl      = identifier , kind-name , { parameter } ;
kind-name           = identifier ;                  (* resolved against the registry at bind time *)
parameter           = identifier , "=" , parameter-value ;
parameter-value     = expression | reference | symbol ;   (* by the parameter's declared kind — see 15 *)
symbol              = identifier ;                  (* e.g. equal_percentage; bound, not evaluated *)

connection          = endpoint , "-" , endpoint , { "-" , endpoint } ;
endpoint            = identifier , [ "." , identifier ] ;   (* component[.port] *)

disturbance         = ( "at" , expression | "over" , range ) , target , "=" , ( expression | range ) ;
target              = identifier , "." , identifier ;       (* component.parameter *)
range               = expression , ".." , expression ;

expression          = (* see 14-expressions-and-references *) ;
style-token         = identifier | quantity | number | string | "-" | "--" | ".." | "-." ;
unsigned-integer    = digit , { digit } ;
```

The version directive must be the first non-trivia line. For an unsaved editor draft only, a missing
directive recovers as current v1 with `FS1701`; [`18-script-compatibility`](18-script-compatibility.md)
owns durable-file and migration behavior.

### Sections

Three section markers, each introducing the statements that follow it: none (the declaration
section), `connections`, and `schedule`. There are no braces and no `end`.

| Statement | Declaration section | After `connections` | After `schedule` |
|---|---|---|---|
| Directives, `let` | ✓ | `FS1103` | `FS1103` |
| `project`, `spacing` | ✓ — **before the first `circuit`** (`FS1112`) | `FS1103` | `FS1103` |
| `circuit-header` | ✓ | ✓ — ends the section (`D-52`) | ✓ — ends the section (`D-52`) |
| `attachment` (`supply`/`return`) | ✓ | ✓ | `FS1103` |
| `component-decl` | ✓ | ✓ | `FS1103` |
| `control-binding` | ✓ | ✓ | `FS1103` |
| `connection` | `FS1102` | ✓ | `FS1102` |
| `disturbance` | `FS1106` | `FS1106` | ✓ |

`attachment` and `control-binding` follow `component-decl` in being legal in both the declaration and
connection sections, for the same reason: both name components, and a user writing topology naturally
writes what attaches to what next to the connections it attaches through.

**The three sections are scoped to a circuit, not to the file** (`D-52`). A `circuit` header is legal
in any of the three columns: it ends whatever section the previous circuit was in and opens the new
circuit's declaration section. So each circuit may carry its own `connections` and its own `schedule`,
and a file-wide count of either is not a rule the parser has. This is what the header being "a section
marker in effect" means, and it is what lets the distribution-header reference circuit write three
circuits as three readable blocks rather than one declaration wall followed by one topology wall.

**A component declaration is legal after the `connections` header, and that is the change that makes
the reference circuits parse.** Both write their boundary conditions below the topology:

```fluidscript
connections
N1 - N2
...

N1 node t=6 p=300         # primary-side boundary
N3 node p=280
```

`N1 node t=6 p=300` is an ordinary declaration of kind `node` — no new statement form, no new
production, P6 intact. It was previously impossible for two independent reasons: `node` was reserved,
so it could not appear in `kind-name` position, and declarations were forbidden below the
`connections` header. Both are now lifted. Declaring the boundary node explicitly also means
inference rule I1 does not fire for it, which is the honest outcome — a node the user wrote is a node
the user wrote.

Writing boundaries below the connections is a convention, not a requirement; the same lines are legal
in the declaration section, and the printer preserves whichever the user chose.

### Statement disambiguation

Both `circuit coolingLoop` and `HE1 heat_exchanger` are two identifiers in a row, and inside the
connection section so are `N1 node t=6` and `N1 - N2`. The rule:

> If the first token of a line is a reserved word, the statement is the directive, section header, or
> statement that word introduces. Otherwise, if the **second** token is `-`, the statement is a
> connection; otherwise it is a component declaration. In the `schedule` section every non-reserved
> line is a disturbance.

Every statement added by `D-33`, `D-37` and `D-40` is introduced by a reserved word, so all five fall
into the rule's first clause and none of them costs a second token of lookahead. That was a
constraint on their design, not a happy accident: a new statement form that could only be recognised
by looking further ahead would break invariant 5, and the `in N3` shape rejected above is exactly
what such a form looks like.

**One token of lookahead, and no more.** The earlier rule used section position alone, which cannot
separate a declaration from a connection once both are legal in the same section. One token keeps the
parser single-pass and keeps invariant 5 meaningful; unbounded lookahead is still forbidden.

A connection written above the `connections` header still produces `FS1102` rather than a confusing
"unknown component kind", because the second token is `-` and the section is wrong.

### Circuit header and numbering

`circuit groundSource 400` names a circuit and designates it 400. The number is optional: an omitted
one is resolved by the binder, not the parser ([`15-semantic-model`](15-semantic-model.md)), as the
lowest unused multiple of 100 in declaration order. A single-circuit script never writes a number and
means exactly what it meant before this production existed (`D-33`).

**A script may hold several circuit headers.** Each one begins a circuit; every declaration and
connection that follows belongs to it until the next header. That makes the header a section marker
in effect while remaining a directive in form — no braces, no `end`, consistent with the three
existing section markers. It follows that a header also *ends* the section the previous circuit was
in, wherever that header appears (`D-52`), which is why `connections` and `schedule` are counted per
circuit rather than per file.

The name doubles as the circuit's **role**, resolved through a registry rather than a keyword
(`D-35`). `circuit AHU 101` is not a reserved word `AHU`; it is an identifier the binder looks up.
The parser neither knows nor cares which roles exist, which is what lets the role set grow without a
language version.

### Subcircuit attachment

```fluidscript
circuit AHU 101
HE1 duty in=50 out=30 power=24 kW
TV1 three_way_valve
PU1 pump

supply N3        # takes flow from the parent circuit at N3
return N5        # returns it to the parent at N5
```

Two statements, each a keyword and an endpoint, declaring where this circuit meets its parent
(`D-33`). Both are optional; a circuit with neither stands alone. A circuit with exactly one produces
a diagnostic, because a subcircuit that takes flow and never returns it is not a topology anyone
means.

**`in` and `out` were the obvious spelling and are lexically impossible.** `in N3` is a first token
that is not reserved followed by a second token that is not `-`, so the disambiguation rule below
classifies it as a component declaration: a component named `in`, of kind `N3`. It parses, it binds,
and it is silently not what the user wrote. Reserving `in` and `out` would fix the parse and break
`HE1 heat_exchanger in=20 out=50`, where the same two words are parameter names for inlet and outlet
temperature — one word, two meanings, one document. `supply` and `return` collide with nothing and
say what they mean, so the parser recognises the `in`/`out` shape only to reject it with `FS1109`.

### Project and spacing directives

```fluidscript
fluidscript 1
project dynamic plant_01
spacing 20
```

`project [dynamic|static] <name>` names the project and sets the **default** solve mode for every
circuit in the file (`D-37`). A circuit's own `fluid dynamic|static` still wins locally; the binder
warns when the two disagree rather than picking silently, because both readings are defensible and
the user should know which one they got.

`spacing <number>` is a bare number in world units. It is presentation, not physics and not layout
structure: the binder puts it in style settings and Core never reads it (`D-37`,
[`25-layout-hints`](../20-core-domain/25-layout-hints.md)'s invariant 1). It takes a plain `number`
rather than a `quantity` deliberately — world units are not metres, and admitting `20 mm` here would
invite the reader to believe the canvas has a physical scale.

**Both must precede the first `circuit` header**, because both are file-global and a global statement
appearing after the thing it governs reads as though it applied only from that point. `project` must
also follow the version directive, which stays first so an unsupported file can be rejected before it
is parsed ([`18-script-compatibility`](18-script-compatibility.md)).

### Control binding

```fluidscript
PID1 pid kp=3
control actuate=TV1.position measure=N2.t by=PID1 setpoint=20
```

The controller *definition* is an ordinary component declaration and needs no production of its own —
`pid`, `pi` and `p` are registered aliases of the kind `controller` and resolve through the registry
like any other spelling (`D-15`). The **binding** is a new
statement: the keyword `control` followed by named parameters, reusing the `parameter` production
already defined for declarations (`D-40`).

**Named, never positional.** `control TV1 N2.t PID1` would be shorter and is rejected: there is no
memorable order for actuator, measurement and controller, and transposing two of them produces a
model that binds, solves, and drives the wrong way. The four recognised names — `actuate`, `measure`,
`by`, `setpoint` — are named, and `actuate` takes a qualified `component.parameter` reference
(`D-43`). Which are required is
[`15-semantic-model`](15-semantic-model.md)'s to define; this document owns only the shape.

### Style directive

`style blue 2px fillet --` is a **set of positional tokens, order-independent**, each classified by
what it is rather than where it sits:

| Token shape | Interpreted as | Examples |
|---|---|---|
| A known colour name, or a string holding `#rrggbb` | stroke colour | `blue`, `"#2f6f9f"` |
| A quantity with a length/pixel unit | stroke width | `2px`, `1.5px` |
| A known corner keyword | corner treatment | `fillet`, `sharp`, `round` |
| A dash pattern token | line pattern | `-` solid, `--` dashed, `..` dotted, `-.` dash-dot |

**A hex colour must be quoted** (`D-13`). `#` now begins a comment, so a bare `#2f6f9f` comments out
the rest of the line — silently, since the result is still a valid `style` directive with one fewer
token. Quoting it is one pair of characters and the `string` token already existed for exactly this
class of value. Named colours are unaffected and remain the common case.

Order-independence is a P1 decision: the user should not have to remember whether width comes before
colour. The cost is that an unrecognised token cannot be attributed to a position, so `FS1201` says
what it could not classify and lists the categories.

### Show directive

`show temperature pressure` selects which fluid property drives the canvas colour scale. The grammar is
trivial — a keyword and a list of identifiers — but the property names, their aliases, and what happens
when one is unknown belong to
[`57-state-visualization`](../50-frontend/57-state-visualization.md), which owns the whole feature.
`FS121x` codes are allocated there, in this document's `FS12xx` range, because the directive is parsed
here and interpreted there.

**Pattern tokens are recombined by the style parser, not by the lexer.** `--` lexes as two `-` tokens,
`..` as two `.` tokens, and `-.` as a `-` followed by a `.`. Inside a `style` directive the parser joins
any run of adjacent `-` and `.` tokens with no intervening whitespace into one pattern token, then
matches that against the four patterns above. All three multi-character patterns need this, not only
`--`. It is the one place the grammar is not context-free, and it is contained to one production
deliberately.

### Catalog directive

`catalog steel_en10255` selects the one catalogue auto-sizing draws from
([`27-component-catalog`](../20-core-domain/27-component-catalog.md));
`catalog steel_en10255@2026.1` pins its exact version.

**`2026.1` reaches the parser as one number token, not as `2026`, `.`, `1`.** The lexer's number rule
consumes a `.` followed by a digit, and it has no context to do otherwise — recognising a version only
after an `@` would make the lexer position-sensitive, which invariant 5 exists to prevent. The parser
therefore splits `CatalogVersionSyntax`'s major and minor out of the number's *source text* rather than
its value, which is also the only way `@2026.10` stays distinguishable from `@2026.1`. The version is optional so a draft may track
the shipped version of a named catalogue, but a durable reproducible design should pin it. v1 has no
catalogue preference list: a second identifier is `FS1101`. With no directive, the shipped default
applies and `FS2606` reports its exact id and version.

It is a directive rather than a per-pipe `series=` parameter because the series is a property of the
installation, not of one pipe, and repeating it on every pipe is the kind of noise `P1` exists to
prevent. `27` owns the series names and their provenance; this document owns only the syntax.

### Schedule section

`schedule` introduces the disturbances a transient run applies
([`33-transient-time-domain`](../30-solver/33-transient-time-domain.md)). It is meaningful only under
`fluid dynamic`; under `fluid static` the section parses and produces `FS1107` (warning).

```fluidscript
schedule
at 60 s              HE1.power   = 45          # step
over 60 s .. 120 s   HE1.power   = 30 .. 45    # linear ramp
at 300 s             3WV.position = 0.3        # any settable parameter
```

Two forms, one production. `at` takes an instant and a value; `over` takes a range of time and either
a single value (step at the end of the ramp) or a range (linear interpolation between the two). The
target is always `component.parameter` — a `reference` shape the expression grammar already parses.

**`at` and `over` are not reserved words.** They are the first token of a statement in the `schedule`
section, and section position classifies them, exactly as it does for connections. Reserving two
common English words to buy nothing is the trade P6 exists to refuse.

**`..` is a range token here and a dotted line pattern in a `style` directive.** The same characters,
separated by context, as `-` already is (connection, subtraction, solid pattern). Both contexts are
closed and neither can occur in the other, so no lookahead is needed to tell them apart.

### Connections

`A - B - C` desugars to `A - B` and `B - C` (rule I6). Port qualification uses `.`: `3WV.b - N3`.
An unqualified endpoint means "the next free port, in the component's declared port order", which is
what makes the brief's example work without any port names at all.

## AST shapes

Every node carries a `TextSpan` and its leading/trailing trivia. Nodes are records; the tree is
immutable.

```csharp
public abstract record SyntaxNode
{
    /// <summary>Span in the source text, excluding trivia.</summary>
    public required TextSpan Span { get; init; }

    /// <summary>Trivia attached before this node, in source order.</summary>
    public required ImmutableArray<Trivia> LeadingTrivia { get; init; }

    /// <summary>Trivia attached after this node up to the next newline, in source order.</summary>
    public required ImmutableArray<Trivia> TrailingTrivia { get; init; }
}

public sealed record ScriptSyntax(ImmutableArray<StatementSyntax> Statements) : SyntaxNode;

/// <summary>A bare identifier: a component name, a kind name, a parameter name, a circuit name.</summary>
/// <remarks><see cref="Text"/> is the spelling exactly as written. Normalisation for kind and
/// parameter resolution happens at bind time (`D-15`) and never rewrites this.</remarks>
public sealed record IdentifierSyntax(string Text) : SyntaxNode;

/// <summary>A number with no unit symbol.</summary>
/// <remarks><see cref="Text"/> retains the source spelling — <c>1.50</c>, <c>1.5</c> and <c>15e-1</c>
/// are one value and three different strings, and the printer must reproduce the one written
/// (`R-25`).</remarks>
public sealed record NumberLiteralSyntax(double Value, string Text) : ExpressionSyntax;

/// <summary>A number immediately or whitespace-separated from a unit symbol.</summary>
/// <remarks><see cref="Unit"/> is the matched symbol as written, not its canonical spelling; the
/// dimension it denotes is <see href="13-type-and-unit-system.md">13</see>'s.</remarks>
public sealed record QuantityLiteralSyntax(double Value, string Text, string Unit) : ExpressionSyntax;

/// <summary>A double-quoted string. Cannot span a newline (invariant 6).</summary>
public sealed record StringLiteralSyntax(string Value) : ExpressionSyntax;

/// <summary>Base of the expression hierarchy.</summary>
/// <remarks>
/// The literal leaves above are declared here because the statement productions in this document
/// consume them directly. Operator, reference and range nodes belong to
/// <see href="14-expressions-and-references.md">14</see>, which owns evaluation.
/// </remarks>
public abstract record ExpressionSyntax : SyntaxNode;

/// <summary>Whether a fluid or a project is solved as an equilibrium or in time.</summary>
public enum FluidMode { Static, Dynamic }

/// <summary>Which side of a subcircuit's attachment a statement declares (`D-33`).</summary>
public enum AttachmentDirection { Supply, Return }

public abstract record StatementSyntax : SyntaxNode;

/// <summary>Begins a circuit. Every statement until the next header belongs to it.</summary>
/// <remarks>
/// <paramref name="Number"/> is the circuit designation as written; null when omitted, in which case
/// the binder resolves one (`D-33`). The parser never invents a number — an absent number must stay
/// distinguishable from a written one so the printer can reproduce the source byte for byte.
/// <paramref name="Name"/> also carries the circuit's role, resolved against the role registry at
/// bind time (`D-35`); the parser does not classify it.
/// </remarks>
public sealed record CircuitHeaderSyntax(
    IdentifierSyntax Name,
    NumberLiteralSyntax? Number) : StatementSyntax;

/// <summary>Names the project and sets the file-wide default solve mode (`D-37`).</summary>
/// <remarks><paramref name="Mode"/> is null when neither <c>dynamic</c> nor <c>static</c> was
/// written, which leaves every circuit's own directive to decide.</remarks>
public sealed record ProjectDirectiveSyntax(
    FluidMode? Mode,
    IdentifierSyntax Name) : StatementSyntax;

/// <summary>Component spacing on the canvas, in world units (`D-37`).</summary>
/// <remarks>A bare number, never a quantity: world units have no physical dimension, and accepting
/// <c>20 mm</c> would imply the canvas has a scale it does not have.</remarks>
public sealed record SpacingDirectiveSyntax(NumberLiteralSyntax Value) : StatementSyntax;

/// <summary>Where a subcircuit meets its parent (`D-33`).</summary>
/// <remarks>
/// <c>Supply</c> takes flow from the parent, <c>Return</c> gives it back. The endpoint names a node
/// in the parent circuit; whether it exists is a binder question, not a parser one.
/// </remarks>
public sealed record AttachmentSyntax(
    AttachmentDirection Direction,        // Supply | Return
    EndpointSyntax Endpoint) : StatementSyntax;

/// <summary>Binds a declared controller to what it actuates and what it measures (`D-40`).</summary>
/// <remarks>
/// Arguments are named and order-independent, reusing <see cref="ParameterSyntax"/>. The parser
/// accepts any set of names and any count; which names are recognised, which are required, and what
/// each must resolve to are <see href="15-semantic-model.md">the binder's</see>.
/// </remarks>
public sealed record ControlBindingSyntax(
    ImmutableArray<ParameterSyntax> Arguments) : StatementSyntax;

public sealed record FluidDirectiveSyntax(
    FluidMode Mode,                       // Static (default) | Dynamic
    IdentifierSyntax Substance,
    ImmutableArray<ExpressionSyntax> Arguments) : StatementSyntax;

public sealed record CatalogDirectiveSyntax(
    IdentifierSyntax CatalogId,
    CatalogVersionSyntax? Version) : StatementSyntax;

public sealed record CatalogVersionSyntax(int Major, int Minor) : SyntaxNode;

public sealed record StyleDirectiveSyntax(ImmutableArray<StyleTokenSyntax> Tokens) : StatementSyntax;

public sealed record LetBindingSyntax(IdentifierSyntax Name, ExpressionSyntax Value) : StatementSyntax;

public sealed record ScheduleHeaderSyntax : StatementSyntax;

/// <summary>One entry of the schedule section: a change applied at a time or over an interval.</summary>
/// <remarks>
/// <paramref name="When"/> is a point for the <c>at</c> form and a range for the <c>over</c> form;
/// <paramref name="Value"/> is a point for a step and a range for a ramp. The four combinations are
/// all legal — <c>over</c> with a single value ramps nothing and steps at the end, which is
/// occasionally what a user means and is cheaper to allow than to diagnose.
/// </remarks>
public sealed record DisturbanceSyntax(
    RangeOrPointSyntax When,
    EndpointSyntax Target,                // component.parameter
    RangeOrPointSyntax Value) : StatementSyntax;

public abstract record RangeOrPointSyntax : SyntaxNode
{
    public sealed record Point(ExpressionSyntax Value) : RangeOrPointSyntax;
    public sealed record Range(ExpressionSyntax From, ExpressionSyntax To) : RangeOrPointSyntax;
}

public sealed record ComponentDeclarationSyntax(
    IdentifierSyntax Name,
    IdentifierSyntax Kind,
    ImmutableArray<ParameterSyntax> Parameters) : StatementSyntax;

public sealed record ParameterSyntax(IdentifierSyntax Name, ExpressionSyntax Value) : SyntaxNode;

public sealed record ConnectionsHeaderSyntax : StatementSyntax;

public sealed record ConnectionSyntax(ImmutableArray<EndpointSyntax> Endpoints) : StatementSyntax;

public sealed record EndpointSyntax(IdentifierSyntax Component, IdentifierSyntax? Port) : SyntaxNode;

/// <summary>A statement the parser could not classify. Carries its raw text so the printer
/// can reproduce it and the renderer can skip it without losing the rest of the script.</summary>
public sealed record MalformedStatementSyntax(string RawText) : StatementSyntax;
```

`MalformedStatementSyntax` is what makes P4 and `R-05` real. Recovery is line-granular: a line that
cannot be parsed becomes one of these, the parser resumes at the next newline, and every other line is
unaffected. Line granularity is chosen over token-level recovery because the language is
line-oriented — there is no construct spanning lines to resynchronise into.

## Parser API

```csharp
public static class FluidScriptParser
{
    /// <summary>Parses source text into a syntax tree. Never throws on any input.</summary>
    /// <param name="text">The script source.</param>
    /// <returns>
    /// The tree, always non-null, together with every diagnostic produced. A tree containing
    /// <see cref="MalformedStatementSyntax"/> nodes is a normal result, not a failure.
    /// </returns>
    public static ParseResult Parse(SourceText text);
}

public sealed record ParseResult(ScriptSyntax Root, ImmutableArray<Diagnostic> Diagnostics);
```

## Invariants

1. **Lossless.** Concatenating every token and trivium in source order reproduces the input byte for
   byte. This is asserted by a test over the whole `samples/` corpus.
2. `Parse` never throws, for any byte sequence, including invalid UTF-8 and a 100 MB single line.
3. Every `SyntaxNode.Span` is within the source bounds, and a parent's span contains every child's.
4. At most one `ConnectionsHeaderSyntax` and at most one `ScheduleHeaderSyntax` **per circuit**
   (`D-52`); a second of either within the same circuit produces `FS1101` and is treated as trivia.
   A file with several circuits therefore holds several of each, and a circuit with no topology of
   its own holds neither.
5. A statement is classified by its first token, its section, and **at most one token of lookahead**.
   Unbounded lookahead is never used.
6. No token spans a newline. (Strings therefore cannot contain newlines; accepted, since the only
   current use is a label.)
7. `#` appears in exactly one role: the start of a line comment. It is never an operator, so a `#`
   makes the remainder of its line trivia — **except inside a string literal**, which is what makes
   `D-13`'s quoted hex colour, `style "#2f6f9f"`, a colour rather than a comment. A string is scanned
   as one token from its opening quote, so a `#` between quotes was never at a token boundary.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1001` | Unterminated string literal | Error | `Unterminated string; add a closing quote.` |
| `FS1002` | Unrecognised character | Error | `'{ch}' is not valid here.` |
| `FS1003` | Identifier that parses as a quantity | Error | `'{name}' reads as a quantity ({value} {unit}), not a name. Try '{suggestion}'.` |
| `FS1004` | Reserved word used as an identifier | Error | `'{word}' is reserved. Choose another name.` |
| `FS1101` | Second `connections` or `schedule` header in one circuit (`D-52`) | Warning | `Only the first '{section}' section is used.` |
| `FS1102` | Connection outside the `connections` section | Error | `Connections must come after the 'connections' line.` |
| `FS1103` | Directive, `let`, or declaration in a section that does not accept it | Error | `A {statement} cannot appear after the '{section}' line.` |
| `FS1104` | Statement cannot be classified | Error | `Cannot read this line. Expected a component declaration or a connection.` |
| `FS1105` | Parameter with no `=` | Error | `'{token}' looks like a parameter but has no value. Write '{token}=…'.` |
| `FS1106` | Disturbance outside the `schedule` section | Error | `Put this under a 'schedule' line.` |
| `FS1107` | `schedule` section under `fluid static` | Warning | `This circuit is solved as a steady state, so the schedule is ignored. Write 'fluid dynamic {substance}' to run it in time.` |
| `FS1108` | Hyphen inside a name or kind name | Error | `'{text}' — a name cannot contain '-'. Write '{underscored}'.` |
| `FS1109` | `in` or `out` used where an attachment was meant | Error | `'{word}' is not an attachment. Write 'supply {node}' or 'return {node}'.` |
| `FS1110` | `supply` or `return` with no endpoint, or a second one of the same direction in one circuit | Error | `'{word}' needs one node of the parent circuit, and may appear once per circuit.` |
| `FS1111` | `control` binding with no arguments, or an argument with no `=` | Error | `A 'control' line needs named arguments, such as 'control actuate=V1.position measure=N2.t by=PID1'.` |
| `FS1112` | `project` or `spacing` after the first `circuit` header, or a second of either | Error | `'{word}' applies to the whole file and must come before the first 'circuit' line.` |
| `FS1113` | `spacing` given a quantity rather than a bare number | Error | `Spacing is in world units, so write 'spacing {n}' with no unit.` |
| `FS1201` | Unclassifiable style token | Warning | `Ignoring style '{token}'. Expected a colour, a width, a corner style, or a line pattern.` |
| `FS1202` | Two style tokens of the same category | Warning | `'{a}' overrides the earlier '{b}'.` |
| `FS1203` | Bare `#rrggbb` in a `style` directive | Warning | `'#' starts a comment; the rest of this line was ignored. Write the colour as "{hex}".` |

**`FS1103` was previously "Declare components before the 'connections' line" and is redefined here
rather than retired**, because its trigger has widened rather than changed meaning: it still fires on
a statement that is in the wrong section, and it no longer fires on a declaration below `connections`,
which is now legal. No script that was accepted before is rejected now.

**`FS1109` exists because the failure it prevents is silent.** `in N3` is a legal component
declaration — a component named `in` of kind `N3` — so without this code the user gets an unknown-kind
diagnostic pointing at `N3`, or, if a kind named `N3` ever existed, no diagnostic at all and a
subcircuit that never attaches. The parser therefore recognises `in`/`out` followed by a single
identifier at statement position and rejects it by name rather than letting the general rule classify
it. This is the only place the parser looks at an identifier's spelling, and it is worth the
exception: a wrong answer that compiles is the outcome `P3` exists to refuse.

**`FS1203` is a warning about a comment, which sounds odd until you see the failure.** `style #2f6f9f
2px fillet` under `D-13` comments out everything from `#`, leaving a `style` directive with no tokens
at all — legal, silent, and the diagram renders in the default colour. The lexer cannot know the user
meant a colour, but the *style parser* can: a directive whose entire token list was consumed by a
comment beginning with a hex-shaped run is worth one warning.

Message wording rules — sentence case, no jargon, and a suggested fix wherever one exists — are owned
by [`16-diagnostics`](16-diagnostics.md).

## Worked example

Lexing `HE1 heat_exchanger power=30 in=20 out=50    # heat exchanger with power of 30 kW`:

| # | Token | Kind | Span | Why |
|---|---|---|---|---|
| 1 | `HE1` | Identifier | 0–3 | word, not reserved, not a quantity (`E1` is not a unit) |
| 2 | `heat_exchanger` | Identifier | 4–18 | word; kind resolution happens at bind time |
| 3 | `power` | Identifier | 19–24 | |
| 4 | `=` | Equals | 24–25 | |
| 5 | `30` | Number | 25–27 | matches `number` exactly (rule 2) |
| 6 | `in` | Identifier | 28–30 | **rule 5's `=` clause**: `in` is a unit symbol, but it is followed by `=`, so it is a parameter name and token 5 stays a bare number |
| 7 | `=` | Equals | 30–31 | |
| 8 | `20` | Number | 31–33 | |
| 9 | `out` | Identifier | 34–37 | |
| 10 | `=` | Equals | 37–38 | |
| 11 | `50` | Number | 38–40 | |
| — | `    # heat exchanger…` | Trivia | 40–end | whitespace + line comment, trailing trivia of token 11 |

**Token 6 is the whole safety of rule 5 in one row.** Drop the `=` clause and tokens 5–6 merge into a
quantity of thirty inches, `power` loses its value, and the line still parses.

The bare `30` is a *number*, not a quantity — it acquires kW when bound to the `power` parameter, whose
dimension declares that canonical unit (`D-07`, `D-14`). The lexer does not know about parameters, and
must not.

Parsing yields one `ComponentDeclarationSyntax` with `Name = HE1`, `Kind = heat_exchanger`, and three
`ParameterSyntax` children. Its `TrailingTrivia` holds the whitespace and comment, so the printer can
put the comment back in the same column.

**A second line, in the connection section**, exercising the lookahead rule and the boundary form:

```fluidscript
connections
N1 - N2                   # second token is '-'  → connection
N1 node t=6 p=300         # second token is not  → component declaration, kind 'node'
```

Both start with `N1`, both sit below `connections`, and one token of lookahead separates them. Under
the previous rules the second line was `FS1104` and the first reference circuit did not parse.

## Acceptance criteria

- [ ] **Every script in `samples/`, and every `fluidscript` block in `plan/` and `/docs`, parses with
      zero unexpected diagnostics** — extracted and run in CI. Both reference circuits previously
      failed to parse under this document while every acceptance criterion here passed, which is what
      a corpus test over the specification's own examples exists to catch.
- [ ] Round-trip test: for every file in `samples/`, `Print(Parse(text)) == text` byte for byte.
- [ ] `3WV` lexes as an identifier; `2px` as a quantity; `30` as a number; `3K` as a quantity that
      produces `FS1003` when used as a component name.
- [ ] `P1 pipe length=45` and `N1 node t=6 p=300` both parse as component declarations — `node` and
      `pipe` reach `kind-name` position, which reserving them made impossible.
- [ ] `N1 - N2` and `N1 node t=6` in the same connection section classify differently, by one token of
      lookahead.
- [ ] `4.18 kJ/(kg*K)` lexes as one quantity token, and `Q / (cp * dT)` lexes with no unit-symbol among
      its tokens — the same characters, classified by whether a number precedes them.
- [ ] `power=30 in=20` lexes as two parameters and never as thirty inches (rule 5's `=` clause).
- [ ] Maximal munch: with both `kJ/kg` and `kJ/(kg*K)` in the table, the longer wins where it fits.
- [ ] `50 %` is one quantity and `10 % 3` is a quantity followed by a number — `%` never lexes as an
      operator, because there is no modulo operator to lex it as (`D-51`).
- [ ] `30.5` is one number, `30.` is a number and a `.`, and `30..60` is `30`, `..`, `60` (`D-51`).
- [ ] `--`, `..` and `-.` each recombine to exactly one style pattern token, and `..` in a `schedule`
      line lexes as a range token instead.
- [ ] `#` anywhere on a line makes the remainder trivia, including inside a `style` directive, and
      `style #2f6f9f 2px` produces `FS1203`.
- [ ] `3-way-valve` in kind position produces `FS1108` suggesting `3_way_valve`, and never a node
      named `3-way-valve`.
- [ ] A `schedule` section under `fluid static` parses and produces exactly one `FS1107`.
- [ ] The distribution-header reference circuit parses: three `circuit` headers, the second and
      third below a `connections` section, each opening its own declaration section (`D-52`).
- [ ] Deleting each character of each sample in turn never throws and always yields a tree.
- [ ] A fuzz corpus of 10 000 random mutations produces no exception and no span outside bounds.
- [ ] Every code `FS1xxx` above has a test that triggers exactly it.

## Open questions

None. Quantity-shaped identifiers such as `3K` remain unavailable and get `FS1003`; v1 `fluid` has no
arguments because mixtures are deferred; schedule values may use only statically evaluable expressions.
