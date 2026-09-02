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
| L-1 | [`16`](16-diagnostics.md) | `FS1107`, `FS1201`, `FS1202` are registered nowhere | All three need a bound model. `FS1107` fires on a `schedule` under a circuit solved as a steady state, and which mode a circuit ends up in is `D-37`'s resolution of the circuit's directive against the project's; `FS1201`/`FS1202` classify a `style` token as a colour or a corner treatment, which needs registries that do not exist. **They land with the binder in P2.7/P2.8.** Absent deliberately, not forgotten. |
| L-2 | [`15`](15-semantic-model.md) | `FS1503`'s parameter suggestion is unimplemented | The scoring engine exists (`NameResolution`, shared by kinds, parameters, properties and symbol values) but nothing calls it for parameters yet. P2.7. |
| L-3 | [`17`](17-formatting-and-round-trip.md) | The mutation API and the formatter are unimplemented | By design: `IScriptEditor` is P7.1 and the formatter is P5.5 (see `08`). P2.5 delivered the printer only. Invariants 2–8 and 10 of `17` are therefore unasserted. |
| L-4 | [`13`](13-type-and-unit-system.md) | `Head`'s canonical unit is metres *of the pumped fluid*, and nothing converts it yet | The registry declares `pump.head` as `Head`, but turning metres into pressure needs a density, which needs a fluid. P3.1. |
| L-5 | [`11`](11-language-overview.md) | The nine-diagnostic count on `samples/m1-syntax-reference.fluid` is not asserted | `08` says P2 closes with it. It needs the binder, since most of the nine are binder diagnostics. P2.8. |

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

**`Print` reconstructs from tokens and must never slice the source.** A slice over `FullSpan` would
reproduce the file no matter what the tree had lost, which makes the round-trip test pass while
proving nothing.
