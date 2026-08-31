---
id: 12-grammar
title: Grammar
tier: 10-language
status: reviewed
owns: [lexical grammar, syntactic grammar, reserved words, sections, AST node shapes, trivia model, schedule and catalog syntax]
depends_on: [02-glossary, 06-decision-log, 11-language-overview, 13-type-and-unit-system]
traces_to: [R-01, R-03, R-04, R-05, R-39]
open_questions: 0
last_review_pass: 2
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
               | "=" | "-" | "." | ".." | "," | "(" | ")" | "+" | "*" | "/" | "%" | "@" ;

trivia         = whitespace | line-comment | newline ;
line-comment   = "#" , { any-char - newline } ;
whitespace     = { " " | "\t" } ;

digit          = "0".."9" ;
letter         = "A".."Z" | "a".."z" ;
word-char      = letter | digit | "_" ;

word           = word-char , { word-char } ;      (* classified below *)
number         = digit , { digit } , [ "." , { digit } ] , [ ("e"|"E") , ["+"|"-"] , digit , {digit} ] ;
quantity       = number , [ whitespace ] , unit-symbol ;   (* see the whitespace rule below *)
unit-symbol    = ? longest matching entry of the unit-symbol table ? ;   (* see below *)
string         = '"' , { any-char - '"' } , '"' ;
```

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

`fluidscript` · `circuit` · `fluid` · `dynamic` · `static` · `style` · `show` · `let` · `catalog` · `connections` ·
`schedule`

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
statement       = circuit-header | fluid-directive | catalog-directive | style-directive
                | show-directive | let-binding | component-decl
                | connections-header | connection
                | schedule-header | disturbance ;

version-directive   = "fluidscript" , unsigned-integer ;
circuit-header      = "circuit" , identifier ;
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
| `component-decl` | ✓ | ✓ | `FS1103` |
| `connection` | `FS1102` | ✓ | `FS1102` |
| `disturbance` | `FS1106` | `FS1106` | ✓ |

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

> If the first token of a line is a reserved word, the statement is the directive or section header
> that word introduces. Otherwise, if the **second** token is `-`, the statement is a connection;
> otherwise it is a component declaration. In the `schedule` section every non-reserved line is a
> disturbance.

**One token of lookahead, and no more.** The earlier rule used section position alone, which cannot
separate a declaration from a connection once both are legal in the same section. One token keeps the
parser single-pass and keeps invariant 5 meaningful; unbounded lookahead is still forbidden.

A connection written above the `connections` header still produces `FS1102` rather than a confusing
"unknown component kind", because the second token is `-` and the section is wrong.

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
`catalog steel_en10255@2026.1` pins its exact version. The version is optional so a draft may track
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

public abstract record StatementSyntax : SyntaxNode;

public sealed record CircuitHeaderSyntax(IdentifierSyntax Name) : StatementSyntax;

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
4. Exactly one `ConnectionsHeaderSyntax` and at most one `ScheduleHeaderSyntax` may appear; a second
   of either produces `FS1101` and is treated as trivia.
5. A statement is classified by its first token, its section, and **at most one token of lookahead**.
   Unbounded lookahead is never used.
6. No token spans a newline. (Strings therefore cannot contain newlines; accepted, since the only
   current use is a label.)
7. `#` appears in exactly one role: the start of a line comment. It is never an operator and never
   part of a token, so a `#` anywhere on a line makes the remainder trivia.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1001` | Unterminated string literal | Error | `Unterminated string; add a closing quote.` |
| `FS1002` | Unrecognised character | Error | `'{ch}' is not valid here.` |
| `FS1003` | Identifier that parses as a quantity | Error | `'{name}' reads as a quantity ({value} {unit}), not a name. Try '{suggestion}'.` |
| `FS1004` | Reserved word used as an identifier | Error | `'{word}' is reserved. Choose another name.` |
| `FS1101` | Second `connections` or `schedule` header | Warning | `Only the first '{section}' section is used.` |
| `FS1102` | Connection outside the `connections` section | Error | `Connections must come after the 'connections' line.` |
| `FS1103` | Directive, `let`, or declaration in a section that does not accept it | Error | `A {statement} cannot appear after the '{section}' line.` |
| `FS1104` | Statement cannot be classified | Error | `Cannot read this line. Expected a component declaration or a connection.` |
| `FS1105` | Parameter with no `=` | Error | `'{token}' looks like a parameter but has no value. Write '{token}=…'.` |
| `FS1106` | Disturbance outside the `schedule` section | Error | `Put this under a 'schedule' line.` |
| `FS1107` | `schedule` section under `fluid static` | Warning | `This circuit is solved as a steady state, so the schedule is ignored. Write 'fluid dynamic {substance}' to run it in time.` |
| `FS1108` | Hyphen inside a name or kind name | Error | `'{text}' — a name cannot contain '-'. Write '{underscored}'.` |
| `FS1201` | Unclassifiable style token | Warning | `Ignoring style '{token}'. Expected a colour, a width, a corner style, or a line pattern.` |
| `FS1202` | Two style tokens of the same category | Warning | `'{a}' overrides the earlier '{b}'.` |
| `FS1203` | Bare `#rrggbb` in a `style` directive | Warning | `'#' starts a comment; the rest of this line was ignored. Write the colour as "{hex}".` |

**`FS1103` was previously "Declare components before the 'connections' line" and is redefined here
rather than retired**, because its trigger has widened rather than changed meaning: it still fires on
a statement that is in the wrong section, and it no longer fires on a declaration below `connections`,
which is now legal. No script that was accepted before is rejected now.

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
- [ ] `--`, `..` and `-.` each recombine to exactly one style pattern token, and `..` in a `schedule`
      line lexes as a range token instead.
- [ ] `#` anywhere on a line makes the remainder trivia, including inside a `style` directive, and
      `style #2f6f9f 2px` produces `FS1203`.
- [ ] `3-way-valve` in kind position produces `FS1108` suggesting `3_way_valve`, and never a node
      named `3-way-valve`.
- [ ] A `schedule` section under `fluid static` parses and produces exactly one `FS1107`.
- [ ] Deleting each character of each sample in turn never throws and always yields a tree.
- [ ] A fuzz corpus of 10 000 random mutations produces no exception and no span outside bounds.
- [ ] Every code `FS1xxx` above has a test that triggers exactly it.

## Open questions

None. Quantity-shaped identifiers such as `3K` remain unavailable and get `FS1003`; v1 `fluid` has no
arguments because mixtures are deferred; schedule values may use only statically evaluable expressions.
