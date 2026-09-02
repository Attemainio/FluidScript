---
id: 10-language-defects
title: What implementing tier 10 found
tier: 10-language
owns: [defect and observation record for documents 11-18]
---

# What implementing tier 10 found

Every defect, deferral and observation from implementing against `11`–`18`, newest package last. The
rule and its reasoning are in
[`08-implementation-sequence`](../00-foundation/08-implementation-sequence.md).

A closed entry says what changed. This matters more than it looks: a document that has been corrected
reads as though it was always right, so without this file there is no record that
[`15`](15-semantic-model.md)'s ambiguity example was ever wrong, or that `FS1114` exists because
`FS1101` was cited for something it could not cover.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| L-1 | [`16`](16-diagnostics.md) | `FS1201` and `FS1202` are registered nowhere | Both classify a `style` token as a colour or a corner treatment, which needs registries that do not exist yet. Absent deliberately, not forgotten. (`FS1107` was here too and is now raised by the binder, which is the first stage that knows a circuit's mode.) |
| L-20 | [`15`](15-semantic-model.md) | The circuit-role registry has no documented entries | Step 0 resolves a circuit's name through it and `FS1519` lists "known roles", but `D-35` names only four examples and no document enumerates the set or its role→stage mapping. `CircuitRoleRegistry` defines twelve roles as **implementation-defined**, which is recorded there in as many words. It needs a home in `15` or [`25`](../20-core-domain/25-layout-hints.md) before anything depends on the exact list. |
| L-25 | [`15`](15-semantic-model.md) | `ISymbolMap` maps declarations and endpoints, not references inside an expression | `Print(Parse(x))` and hover both work from the spans the binder already holds. A `let` used in `power=Q*2` is recorded as a dependency (`ValueId`), never as a span, so go-to-definition from inside an expression finds nothing. Invariant 6 says "every source position **within a declaration**", which this satisfies; the wider thing an editor will want needs an expression walker, and nothing walks expressions for spans yet. P5.x, with the editor that asks for it. |
| L-26 | [`15`](15-semantic-model.md), [`23`](../20-core-domain/23-topology-and-graph.md) | Nothing says what boundary condition an I3 node carries | `15` says the binder "records only that it is a boundary" and leaves the condition to `23`; `23`'s table gives conditions for a *declared* degree-one node. An I3 node has no parameters at all, so lowering has to decide. It is exempt from `FS2107` here on exactly that promise — if `23` later decides an I3 node carries nothing, the exemption is wrong and the reference circuit's nine becomes eleven. P3.3. |
| L-32 | [`15`](15-semantic-model.md) | `FS1507` skips a kind with no ports at all, and skips `node` | A controller appears in no connection by design — it is bound by a `control` line, not by topology — so warning about it would put an amber squiggle on the one script that uses `D-40` correctly. A declared `node` is skipped for the weaker reason that a subcircuit's attachment target may legitimately be its only mention; that one is a judgement, not a derivation, and `15` states neither. It needs a sentence in step 10 or a decision that a lone declared node *is* worth a warning. |
| L-35 | [`14`](14-expressions-and-references.md), `D-57` | **`power=heating kW` — a curve reference with an explicit unit — is unimplemented** | The bare-number half works: `power=heating` takes the parameter's canonical unit through `D-14`, which is the common case and the one the feature was asked for. Overriding it needs an expression form the grammar does not have — a *reference* followed by a unit symbol, where today only a number may carry one. That is a `14` change and its own AST node, so it is deferred rather than smuggled in; the `curve` page does not promise it. Wanted explicitly in the proposal that produced `D-57`, so it is scheduled, not dropped. |
| L-36 | [`13`](13-type-and-unit-system.md), `D-60` | A timestamp is documented as a lexical unit and implemented as a **line**-level one | `13` says "a timestamp is a lexical unit". It is not, and cannot be: `2026-01-01` is also a valid subtraction, so no context-free lexer can tell them apart. What ships instead is narrower and works — a curve row keeps its raw tokens, and the binder splits the row's *text* at its last run of whitespace. The lexer's only part is a `Colon` token so a clock time does not raise `FS1002`. `13` needs correcting to say that. |
| L-39 | [`15`](15-semantic-model.md), `D-59` | The schedule-role registry has no documented entries | The exact twin of L-20, one document later. `D-59` names `tout` and nothing else, and `FS1527` tells a user to "name a known driver" without a list anywhere. `ScheduleRoleRegistry` defines seven as **implementation-defined**, recorded there in as many words. It needs a home in `15` before a script depends on the exact set. |
| L-40 | [`15`](15-semantic-model.md), `D-60` | A `format=` that is not a quoted string is silently ignored, and every row then fails | `D-60` says the format is validated when the curve is bound and that "a string with no month or no day is a diagnostic rather than a silent misparse". Neither check exists. What happens instead is that each unreadable row reports `FS1117` on its own line — honest, and correct as far as it goes, but a year of hourly data is 8 760 diagnostics for one mistake on the header. It needs a code on the header and a cap on the cascade. |
| L-21 | [`14`](14-expressions-and-references.md) | `FS1405` is unregistered | The fixed point cannot fail to converge before there is a loop to iterate. P3.7. |
| L-3 | [`17`](17-formatting-and-round-trip.md) | The mutation API and the formatter are unimplemented | By design: `IScriptEditor` is P7.1 and the formatter is P5.5 (see `08`). P2.5 delivered the printer only. Invariants 2–8 and 10 of `17` are therefore unasserted. |
| L-4 | [`13`](13-type-and-unit-system.md) | `Head`'s canonical unit is metres *of the pumped fluid*, and nothing converts it yet | The registry declares `pump.head` as `Head`, but turning metres into pressure needs a density, which needs a fluid. P3.1. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|
| L-6 | [`12`](12-grammar.md) | `%` was both a unit symbol and a modulo operator, and `.` had three meanings | `D-51`: `%` is a unit only; `30..60` lexes as three tokens. |
| L-7 | [`12`](12-grammar.md) | Sections were file-wide, so the distribution-header reference circuit did not parse | `D-52`: `connections` and `schedule` are scoped to a circuit. A second of either is `FS1101`. |
| L-8 | [`16`](16-diagnostics.md) | Six code ranges named the stage that emitted a diagnostic rather than its subject | `D-53`. A range that names a stage has to move every time a check moves. |
| L-9 | [`12`](12-grammar.md) | `FS1101` was cited for a catalogue directive holding two ids — a code whose message is about a duplicated section | `FS1114` allocated, "text after a statement that is already complete", which covers the general case: a second catalogue id, a second circuit number, a stray word after a project name. |
| L-10 | [`12`](12-grammar.md), [`17`](17-formatting-and-round-trip.md) | The two documents disagreed about where trivia lives, and under `12`'s reading no node held `let`, `=`, `-` or `(` | `D-55`: trivia lives on tokens, and a node holds the tokens it consumes. The AST could not have round-tripped otherwise. |
| L-11 | [`12`](12-grammar.md) | The AST as specified could not round-trip: `fluid`'s mode is optional and four nodes had nowhere to keep the tokens they were printed from | `D-54`. |
| L-12 | [`12`](12-grammar.md) | The section table omitted its own two markers, which made every `schedule` below a `connections` line `FS1103` — and the schedule section unreachable in any circuit with connections | `D-56`. `12`'s own worked example was the failing case. |
| L-13 | [`12`](12-grammar.md) | One token of lookahead could not classify `3WV.b - N3`, the document's own example of port qualification | `D-56`: a second token of `-` **or** `.` makes a connection. Still one token, and nothing else can put a `.` there. |
| L-14 | [`17`](17-formatting-and-round-trip.md) | `FS1604` was assigned to two different triggers | The `ApplyTags` one became `FS1607`. Two triggers behind one code sends the user to a page describing the other one. |
| L-15 | [`17`](17-formatting-and-round-trip.md) | Trivia rule 1 never said which side of the line break the terminator falls on | Stated: it opens the next token's leading trivia. Deliberately not Roslyn's convention — it is what makes a statement's tokens cover exactly one line, which the line-granular parser and its recovery rest on. |
| L-16 | [`17`](17-formatting-and-round-trip.md) | The first acceptance criterion asked for malformed files in `samples/`, which `12`'s corpus criterion requires to parse clean | Reworded to what is actually enforced: the samples, every fenced block, the adversarial list, and every fuzz mutation. |
| L-17 | [`15`](15-semantic-model.md) | The ambiguity margin's worked example does not follow from the formula above it. `valv` scores 0.80 against `valve` and **0.44** — not 0.78 — against `3wayvalve`; the "normalised prefix" it cited is computed nowhere | Replaced with `4_way_valve`, which is exactly one substitution from both `2_way_valve` and `3_way_valve` and scores 0.889 against each. The margin was right; the example was not. |
| L-18 | [`15`](15-semantic-model.md) | `FS1502`'s message offers a suggestion, and nothing said when a suggestion stops being worth making | Suggestion floor of 0.60, and a second `FS1502` message for having nothing to suggest. |
| L-19 | [`15`](15-semantic-model.md) | `ComponentKindInfo` had no way to say that a node accepts any number of connections | `HasUnlimitedPorts`. Without it the binder has to know that the kind spelled `node` is special, which is the one thing the registry exists to prevent. |
| L-22 | [`15`](15-semantic-model.md), [`14`](14-expressions-and-references.md) | `FS1502` and `FS1404`'s message shapes end in an optional "Did you mean '{suggestion}'?" clause, which [`16`](16-diagnostics.md)'s style rules reject: a template ending in a clause that is sometimes empty is not a sentence | Both messages are now sentences, and the suggestion rides on `Diagnostic.Suggestion` — the structured field that already existed, which an editor can offer as a fix rather than prose to parse back out. `15`'s two-row table for `FS1502` is one row again. |
| L-23 | [`22`](../20-core-domain/22-component-model.md) | `samples/m1-syntax-tour.fluid` declared three components of kind `duty` | There is no such kind: Duty is a heat-exchanger *mode*, computed at lowering from what the script states, and `22` says outright that there is no script `mode=` parameter. The binder is the first stage that could notice, and did, on its first run over the samples. Changed to `heat_exchanger`. |
| L-27 | [`15`](15-semantic-model.md) | **No binding step bound the schedule.** Steps 0–11 never mentioned a disturbance, and the parser produced `DisturbanceSyntax` for nothing to consume | Folded into step 9, which already resolves the cross-circuit references a `control` line needs — a scheduled target resolves exactly the way an actuated one does. Not a twelfth step: renumbering would have invalidated "steps 6–11" in four documents to say the same thing. |
| L-28 | [`15`](15-semantic-model.md) | `FS1520`'s message read "takes flow at '{node}' and never returns it", which is false for half of its own trigger | The trigger is `supply` without `return` **or the reverse**. Rewritten direction-neutral: `'{circuit}' declares '{present} {node}' and no '{other}'.` |
| L-29 | [`15`](15-semantic-model.md) | Nothing distinguished `FS1507` from `FS1511`, and on the syntax reference both would have fired for `PU1` | They partition one mistake: `FS1507` is a component in no connection, `FS1511` a cluster of two or more joined only to each other. Judged on the connections the user wrote — after I3 nothing is unconnected, and neither code could fire again. Written into step 10. |
| L-30 | [`15`](15-semantic-model.md) | `FS2107` would have fired on every I3 boundary node, making the syntax reference's fixed nine eleven | An I3 node *is* the boundary that rule created. Exempted, and stated in step 10 — see L-26 for what still has to hold for that to stay true. |
| L-31 | [`06`](../00-foundation/06-decision-log.md) `D-35`, `CircuitRoleRegistry` | `circuit coolingLoop` — the name in the syntax reference, `12`'s example, and `52`'s — resolved to no role, so the documentation's own flagship script emitted `FS1519` about itself | `cooling_loop` registered as an alias of `cooling`, alongside the `solar_loop` and `district_loop` that were already there. Found by asserting `01`'s nine-diagnostic count, which came back as ten. |
| L-33 | [`18`](18-script-compatibility.md) | `FS1705` and `FS1112` were two codes for one trigger | `FS1705` was "version or catalogue directive is misplaced or duplicated", which is exactly what `FS1112` already says and already raises. A misplaced line is a grammar error, and `D-53` puts a code in the range naming its *subject*. `FS1705` narrowed to what only the gate can judge — a file whose directives name **different majors**, from which no semantics can be selected. Not the redefinition `16`'s invariant 7 forbids: `FS1705` had never been registered, and `18`'s table was its only reference. |
| L-34 | [`18`](18-script-compatibility.md) | Nothing said how `Inspect` finds the directive without parsing, though invariant 2 requires exactly that | It cannot ask the parser which major to parse under without asking the question it exists to answer. Stated: it scans the raw text past a BOM, blank lines and comments, and matches `fluidscript` plus one unsigned decimal — a prefix fixed across majors by construction, since a major that changed its own version line's spelling could not be detected by an application that did not already know its version. |
| L-37 | [`14`](14-expressions-and-references.md), [`13`](13-type-and-unit-system.md) | **A unit spelling shared by two dimensions evaluated as a bare number, so `p=2 bar` bound as two kilopascals** | The evaluator resolved a literal with the overload that *refuses* an ambiguous spelling, and fell through to "bare" on refusal — under a comment asserting that reaching there would be a lexer defect. `kPa` survived by accident, being the canonical spelling of both pressure dimensions; `bar`, `Pa`, `MPa`, `mbar`, `psi` and `mH2O` did not. `UnitTable.Resolve(text, expected)` added, and the evaluator now carries the dimension its result is being assigned to. Found by a design value in the wrong dimension not raising `FS1304`. |
| L-38 | [`13`](13-type-and-unit-system.md) | **A negative temperature could not be written with a unit anywhere in the language** | `13`'s unary table makes `−Temperature` an error, and the evaluator applied it to `-26 C` — so `design tout=-26 C`, which is `D-59`'s own worked example, did not bind, and neither did `NB1 node t=-5 C`. The bare `-26` worked, which is what made it a trap rather than an inconvenience. `D-62`: a sign directly on a quantity literal is part of the literal. `-(26 C)` is still a negation and still an error. |
| L-41 | [`12`](12-grammar.md), [`15`](15-semantic-model.md) | The classification rule meant a malformed curve row is often not a row | `nonsense here` inside a curve section classifies on its first token as a *declaration*, so it is `FS1103` and never reaches the row reader. `FS1117` from the binder covers only a line that starts like a row and fails to read — `12 34 56`, or a timestamp the stated format does not fit. That is the right split and it is not written down anywhere: a test asserting `FS1117` for `nonsense here` was written and failed. |

## Observations

**The corpus test is what finds these.** Nearly every entry above was found by running the
specification's own examples through the implementation, not by reading the document again. `12`
records that both reference circuits once failed to parse while every acceptance criterion in that
document passed. A block that is meant to be wrong declares its codes on its fence
(` ```fluidscript expects=FS1203 `); a block that is not a script at all does not claim to be one.

**Three constants in `15` were chosen, not derived.** The resolve threshold (0.70) and ambiguity
margin (0.05) come from the document; the suggestion floor (0.60) does not, and was set by the `fan`
case. If any of them is ever tuned, the tests that pin them name the input that fixed them.

**`at` and `over` are ordinary identifiers**, classified by position inside a `schedule` section.
Reserving two common English words to buy nothing is the trade `P6` exists to refuse — worth knowing
before someone "fixes" the classifier by reserving them.

**A statement's tokens never include the newline that ends its line.** Anything computing an edit
span over a statement has to add its own line break. See L-15.

**A bare number is reinterpreted at assignment, not at evaluation.** The evaluator reports whether a
unit took part anywhere in an expression; the binder converts a bare result into the parameter's
canonical unit (`D-14`). Doing it the other way — handing the evaluator the target's dimension —
would make the same expression mean different things in different places, and `let x = 30` would have
no meaning at all.

**A stated parameter is readable immediately whatever its property's availability says.** A
`heat_exchanger`'s `power` property is registered as `Sized`, but a user who wrote `power=30 kW` can
read `HE1.power` at once. Availability describes where a value comes from when nobody stated one.

**`Print` reconstructs from tokens and must never slice the source.** A slice over `FullSpan` would
reproduce the file no matter what the tree had lost, which makes the round-trip test pass while
proving nothing.

**A count is the cheapest specification there is.** `01` fixes the syntax reference's diagnostic set at
nine, and asserting it found three separate defects in one run: the `FS1519` on `coolingLoop` (L-31),
the `FS2107` on every I3 node (L-30), and the two ways `FS1507` and `FS1511` could double-report
(L-29). None of them would have been visible from a test that checked "an FS1507 is produced".

**Steps 6–11 needed the component list to have one home.** The binder kept a `ComponentSymbol` in a
name→slot dictionary *and* in an ordered list; step 6 rewrites a component's ports and step 11 its
tag, both by `with`, so the dictionary's copy went stale the moment a port was materialized. The slot
now holds an index. A record's structural equality makes this class of bug silent: the stale copy
compares equal to nothing, and reads perfectly.

**A drift check that parses a document must assert it parsed something.** `CodeRangeOwnershipTests`
reads `16`'s range table with a regex; a pattern that matched nothing would make both of its
assertions pass while checking nothing at all. It asserts a floor on the row count and one known row
first. Every check in this repository that reads the plan has this failure mode, and this is the only
one that currently guards against it.

**A curve is a node of the dependency graph, and that is the whole of step 0b's cost.** Making
`ValueId.Curve` an ordinary id bought the topological order for free, so a curve that drives another
is already evaluated when the second is reached, and a cycle among curves is the same `FS1402` and the
same depth-first sort that already reported one among `let` bindings. The alternative — a second
ordering pass over curves alone — is the shape that drifts.

**The design point is tried before the driver chain, and the order is the feature.** `D-58`'s worked
example only works that way: with `design tout=-26`, `time → outdoor → heating` is not walked at all.
Reversing it would make a file carrying a year of weather data unsolvable statically, which is the
case the design point exists for.

**A driver's role is resolved even when the driver is a curve.** `curve heating outdoor` is driven by
the curve `outdoor`, and `design tout=-26` still reaches it, because the role is the design point's
*key* rather than the driver's kind. Resolving the role only in the `Role` branch is the obvious
implementation and it breaks `D-58`'s example silently — the curve chain is walked instead, and the
number that comes out is plausible.

**An observer needed no exemption from `FS1507`, `FS1511` or `FS2107`.** All three are already
conditioned on ports or on appearing in a connection, and an instrument has neither. The registry's
`IsObserver` earns its place elsewhere — it is what makes `at` on a pump `FS1532` — but the validation
half came out free, which is worth knowing before someone adds a fourth check that hard-codes a list
of instrument kinds.

**`design` values evaluate through the same pending/graph machinery as everything else**, with two
extra fields on `PendingValue` rather than a parallel path. That is what makes `design tout=baseTemp`
work without anything being written for it, and what makes the dimension check reuse `FS1304`.
