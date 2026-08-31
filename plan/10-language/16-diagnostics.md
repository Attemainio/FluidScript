---
id: 16-diagnostics
title: Diagnostics
tier: 10-language
status: reviewed
owns: [diagnostic code registry, severity model, span model, message style rules, suggestion mechanism]
depends_on: [12-grammar, 13-type-and-unit-system, 14-expressions-and-references, 15-semantic-model]
traces_to: [R-05, R-20, R-24, R-29]
open_questions: 0
last_review_pass: 2
---

# Diagnostics

## Purpose

Every message FluidScript shows a user comes from here: parse errors, unit mismatches, unconnected
components, and physical warnings like "approaching freezing point" (`R-24`). One model, one code
space, one style. Diagnostics are also the machine-readable surface an LLM agent uses to correct a
generated script (`R-29`), which is why codes are stable and spans are exact.

## Responsibilities

**Owns.** The `Diagnostic` type, the severity model, the code-range allocation, the message style
rules, and the suggestion mechanism.

**Explicitly does not own.** The individual codes — each is defined in the document that produces it,
and this document owns only the *ranges* and the registry that collects them. How diagnostics are
displayed ([`52-editor`](../50-frontend/52-editor.md),
[`56-console-log`](../50-frontend/56-console-log.md)), how they cross the wire
([`44-diagnostics-contract`](../40-api/44-diagnostics-contract.md)).

## Code ranges

Codes are `FS` + four digits. The range says which stage produced it, which is the first thing anyone
debugging wants to know.

| Range | Stage | Owner |
|---|---|---|
| `FS10xx` | Lexer | [`12-grammar`](12-grammar.md) |
| `FS11xx` | Parser | [`12-grammar`](12-grammar.md) |
| `FS12xx` | Style directive | [`12-grammar`](12-grammar.md) |
| `FS13xx` | Units and dimensions | [`13-type-and-unit-system`](13-type-and-unit-system.md) |
| `FS14xx` | Expressions and references | [`14-expressions-and-references`](14-expressions-and-references.md) |
| `FS15xx` | Binder and inference | [`15-semantic-model`](15-semantic-model.md) |
| `FS16xx` | Printer and write-back | [`17-formatting-and-round-trip`](17-formatting-and-round-trip.md) |
| `FS17xx` | File compatibility and migration | [`18-script-compatibility`](18-script-compatibility.md) |
| `FS20xx` | Substances and properties | [`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md) |
| `FS21xx` | Components | [`22-component-model`](../20-core-domain/22-component-model.md) |
| `FS22xx` | Topology | [`23-topology-and-graph`](../20-core-domain/23-topology-and-graph.md) |
| `FS23xx` | Sizing | [`24-auto-sizing`](../20-core-domain/24-auto-sizing.md) |
| `FS24xx` | Layout hints | [`25-layout-hints`](../20-core-domain/25-layout-hints.md) |
| `FS25xx` | Model contract serialization | [`26-model-contract`](../20-core-domain/26-model-contract.md) |
| `FS26xx` | Catalogue loading and selection | [`27-component-catalog`](../20-core-domain/27-component-catalog.md) |
| `FS30xx` | Solver | tier 30 |
| `FS31xx` | Transient | [`33-transient-time-domain`](../30-solver/33-transient-time-domain.md) |
| `FS32xx` | Controllers | [`34-controllers`](../30-solver/34-controllers.md) |
| `FS35xx` | Optimization | [`35-evolutionary-sizing`](../30-solver/35-evolutionary-sizing.md) |
| `FS40xx` | Physical plausibility warnings | this document — see below |
| `FS45xx` | Realtime protocol | [`43-realtime-contract`](../40-api/43-realtime-contract.md) |
| `FS50xx` | Frontend layout/rendering | [`53-canvas-renderer`](../50-frontend/53-canvas-renderer.md) |
| `FS90xx` | Internal errors | this document |

**The `FS4xxx` block is not all physical warnings**, and the realtime codes were originally allocated
inside it as `FS43xx`, which reads as one. They are `FS45xx` instead, leaving `FS40xx`–`FS44xx` to the
design-warning family. A reader who sees `FS4` should be able to assume "something about the design"
without checking.

**Codes are permanent.** A code is never reused for a different meaning and never renumbered, because
scripts, tests, `/docs` pages, and agent prompts all reference them. A retired code is marked retired
in the registry and left unallocated.

## Severity

| Severity | Meaning | Effect on the pipeline | UI |
|---|---|---|---|
| `Error` | The affected element cannot be processed | That element is skipped; everything else continues (P4) | Red squiggle, listed in the log |
| `Warning` | Processed, but probably not what was meant | Nothing | Amber squiggle, listed |
| `Info` | Something was decided for the user | Nothing | No squiggle; log only, collapsed by default |

**There is no `Fatal`.** No single diagnostic stops the pipeline — that is principle P4, and it is what
lets the canvas keep rendering while the user types (`R-05`). The only thing that stops the pipeline is
an internal error (`FS90xx`), which is a bug report, not a diagnostic about the script.

### Physical warnings — the `FS40xx` range

`R-24`'s "approaching freezing point" class. These are unusual: they are produced by tier 20/30 code
but they are *about the design*, not about the script, and they are the ones the console log
([`56-console-log`](../50-frontend/56-console-log.md)) exists to show.

| Code | Condition | Severity | Note |
|---|---|---|---|
| `FS4001` | A node's temperature is within 5 K of the fluid's freezing point | Warning | |
| `FS4002` | A node's temperature is above the fluid's boiling point at that pressure | Error |
| `FS4003` | Absolute pressure at a node is below the fluid's saturation pressure — cavitation risk | Warning |
| `FS4004` | Velocity in a pipe exceeds the noise threshold for its diameter | Warning |
| `FS4005` | Velocity below the self-cleaning minimum — sedimentation risk | Info |
| `FS4006` | Control-valve authority below 0.25 | Warning |
| `FS4007` | Pump operating outside its curve's usable range | Warning |
| `FS4008` | An extended heat exchanger's temperature approach is below the configured minimum | Error | **Live in M2b** for Rated and Coupled modes (`D-19`). Duty mode has no approach. |
| `FS4009` | Reverse flow where the design assumed forward | Info |
| `FS4010` | A branch carries no flow — a dead leg | Warning |

Thresholds (5 K, 0.25 authority, the velocity limits) are engineering conventions, not physics. They
belong in one configurable table, not scattered through the components that check them, and the table
must be citable in `/docs` so a user can see what the tool considers "approaching".

**Every `FS40xx` diagnostic carries the component it is about**, not only a span — a physical warning
about an inferred component has no span at all, and the canvas needs to badge the component regardless
(`R-24`).

## The type

```csharp
/// <summary>One message about a script or the design it describes.</summary>
public sealed record Diagnostic
{
    /// <summary>Stable code, e.g. <c>FS1302</c>. Never reused for another meaning.</summary>
    public required string Code { get; init; }

    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>The rendered message, already formatted with its arguments.</summary>
    public required string Message { get; init; }

    /// <summary>Where in the source this is about.</summary>
    /// <value><see langword="null"/> for a diagnostic about an inferred component or the design
    /// as a whole, which has no source text.</value>
    public TextSpan? Span { get; init; }

    /// <summary>Name of the component this concerns, when it concerns one.</summary>
    /// <value><see langword="null"/> for diagnostics about the script rather than a component.</value>
    public string? ComponentName { get; init; }

    /// <summary>An offered fix, when one is unambiguous.</summary>
    public Suggestion? Suggestion { get; init; }

    /// <summary>Related locations — the earlier declaration for a duplicate, every member of a
    /// dependency cycle.</summary>
    public ImmutableArray<RelatedLocation> Related { get; init; }
}

/// <summary>A concrete edit that resolves a diagnostic.</summary>
/// <remarks>
/// Applied verbatim by the editor's quick-fix and, where the fix is unambiguous, offered to an
/// agent (R-29). A suggestion that is a guess is worse than none — populate it only when the
/// replacement is certainly correct.
/// </remarks>
public sealed record Suggestion(string Title, TextSpan Span, string Replacement);
```

`Related` matters more than it looks. A dependency cycle (`FS1402`) with four participants is
unactionable when the diagnostic points at one of them; with all four as related locations the editor
can highlight the whole loop.

## Message style rules

These are the rules that make a hundred diagnostics feel like one product. They apply to every `FSxxxx`
message in every document.

1. **Sentence case, ending in a period.** Not `Unexpected token`, not `UNEXPECTED TOKEN`.
2. **Say what is wrong, then what to do.** `Cannot add two temperatures. To offset by a difference,
   write '20C + 30 dK'.` — the second sentence is the value.
3. **Name the thing.** Quote the user's identifier, unit, or value. `A heat_exchanger has no 'flow'`
   beats `Unknown property`.
4. **List the alternatives when the set is small.** `It accepts: power, in, out, area.` Costs one
   string join; saves a documentation lookup.
5. **Suggest by edit distance** when a name is close: `Did you mean 'power'?` Only when the distance is
   1 or 2 and the match is unique — a wrong suggestion is worse than none.
6. **No jargon the script does not use.** Never "token", "AST", "binder", "residual", "Jacobian". The
   user wrote a line of text; the message is about that line. Internal vocabulary belongs in `FS90xx`.
7. **No blame and no exclamation.** Not "You forgot", not "Invalid!".
8. **Units in the message match the script's canonical units**, not SI. Telling a user their 30 kW heat
   exchanger has a problem at 30 000 W is telling them about a number they never wrote.
9. **One diagnostic per problem.** A single unknown identifier must not produce a parse error, a bind
   error, and a reference error. Later stages skip elements already reported.

Rule 9 is the one that requires design rather than discipline: each stage checks whether the element it
is about already carries an error before adding another. The `SemanticModel` therefore tracks which
symbols are already-reported.

## The registry

Every code in the tables above is collected into one machine-readable registry, generated from the
plan documents and shipped in Core:

```csharp
public static class DiagnosticRegistry
{
    /// <summary>Every defined code, with its severity, owning stage, and message template.</summary>
    public static ImmutableDictionary<string, DiagnosticDescriptor> All { get; }
}
```

Two things depend on this existing rather than being implied by scattered string literals: `/docs` has
a generated page listing every diagnostic (`R-29` — an agent that can look up `FS1302` can correct its
own output), and a test asserts that every code emitted anywhere in Core appears in the registry, which
is how a code invented in an ad-hoc `throw` gets caught.

## Invariants

1. Every emitted code exists in `DiagnosticRegistry`, and every registry entry is emitted by some code
   path (both directions tested).
2. No code is emitted with two different severities.
3. A `Span`, when present, lies within the source bounds.
4. At most one diagnostic per source element per stage (rule 9).
5. `Suggestion.Replacement` applied to `Suggestion.Span` always produces a script that parses.
6. Message text contains no term from the banned-jargon list (rule 6), asserted by a test over the
   registry.
7. Codes are never reused or renumbered.

Invariant 5 is testable and worth the effort: an editor quick-fix that produces a broken script is
worse than having no quick-fix.

## Error cases

The `FS90xx` range, for when the tool itself fails:

| Code | Trigger | Severity |
|---|---|---|
| `FS9001` | An unexpected exception escaped a pipeline stage | Error |
| `FS9002` | The property backend (SharpProp) failed for a valid state | Error |
| `FS9003` | An internal invariant was violated | Error |

These say so plainly — *"Something went wrong inside FluidScript (FS9001). This is a bug; the script is
probably fine."* — rather than dressing an internal fault as a user error. That distinction is worth
the three codes.

## Worked example

A user writes `HE1 heat_exchanger pwor=30 in=20 out=20C+30C`. Two problems on one line. `pwor`
scores below `D-15`'s 0.70 silent-resolution threshold, so it remains an unknown parameter while the
nearest name is still useful as a suggested edit:

```
FS1503  Error   line 4, col 20-24
        A heat_exchanger has no 'pwor'. It accepts: power, in, out, dt, dp, flow.
        Suggestion: "Change 'pwor' to 'power'"  → replace [20,24) with "power"

FS1302  Error   line 4, col 37-44
        Cannot add two temperatures. To offset by a difference, write '20C + 30 dK'.
```

What the rules produced: `FS1503` names the thing (3), lists alternatives (4), and suggests by edit
distance (5 — `pwor`→`power` is the unique nearest name but below the binding threshold). `FS1302`
says what is wrong then what to do
(2), using the user's own values (3) and the script's units (8). Neither mentions a token or a binder
(6). The unknown parameter produced one diagnostic, not also a downstream "no value for power" (9).

Applying the `FS1503` suggestion yields `power=30`, which parses (invariant 5). A closer typo such as
`pwer` resolves silently to `power` with `FS1512` under `D-15`; it does not produce `FS1503`. The
`FS1302` error has
no suggestion, because `20C + 30 dK` and `20C + 30C - 273.15K` are both plausible readings and guessing
would be rule 5's failure mode.

## Acceptance criteria

- [ ] `DiagnosticRegistry.All` contains every code defined across all plan documents.
- [ ] A test asserts every code emitted in Core is in the registry, and vice versa.
- [ ] Applying every suggestion in a corpus of deliberately broken scripts yields parseable scripts.
- [ ] No message in the registry contains a banned jargon term.
- [ ] The syntax reference produces exactly the diagnostic set enumerated in
      [`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md) — `FS1507`, `FS2107` twice, and
      one `FS1510` per inferred component. "Exactly one diagnostic" was wrong: `FS1510` is emitted for
      every inference, and the syntax reference makes six of them.
- [ ] A generated `/docs/functions/diagnostics.md` lists every code with its meaning and an example.

## Open questions

None. One versioned Core table owns fixed v1 physical-warning thresholds and generates metadata/docs.
Per-script or project overrides are post-v1 and require a compatibility decision (`D-30`).
