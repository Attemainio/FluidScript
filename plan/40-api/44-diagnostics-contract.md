---
id: 44-diagnostics-contract
title: Diagnostics contract
tier: 40-api
status: reviewed
owns: [diagnostic wire shape, span-to-position mapping, severity mapping, suggestion transport]
depends_on: [16-diagnostics, 26-model-contract, 42-rest-contract]
traces_to: [R-20, R-24, R-29]
open_questions: 0
last_review_pass: 2
---

# Diagnostics contract

## Purpose

How a `Diagnostic` becomes an editor squiggle, a component badge, a log line, and something an LLM
agent can act on (`R-20`, `R-24`, `R-29`). The shape is small; the problems are the offset-to-position
mapping, which is easy to get wrong in a way that misplaces every squiggle, and making the same
diagnostic serve four consumers without a per-consumer field.

## Responsibilities

**Owns.** The wire shape, offset-to-line/column mapping, severity mapping, and suggestion transport.

**Explicitly does not own.** Codes and messages ([`16-diagnostics`](../10-language/16-diagnostics.md)),
rendering ([`52-editor`](../50-frontend/52-editor.md),
[`56-console-log`](../50-frontend/56-console-log.md)).

## Wire shape

```jsonc
{
  "code": "FS1302",
  "severity": "error",                 // "error" | "warning" | "info"
  "message": "Cannot add two temperatures. To offset by a difference, write '20C + 30 dK'.",

  "range": {                           // null for a diagnostic with no source location
    "start": { "line": 3, "character": 37 },
    "end":   { "line": 3, "character": 44 },
    "offset": 137, "length": 7
  },

  "component": "HE1",                  // null when it is not about a component

  "suggestion": {
    "title": "Change 'powr' to 'power'",
    "range": { "start": {...}, "end": {...}, "offset": 120, "length": 4 },
    "newText": "power"
  },

  "related": [
    { "message": "First declared here.",
      "range": { "start": {...}, "end": {...}, "offset": 40, "length": 3 } }
  ]
}
```

### Both position forms, deliberately

`line`/`character` for the editor — every editor component (CodeMirror, Monaco) works in line/column,
and having each client convert means each client's off-by-one. `offset`/`length` for anything applying
edits to raw text, which is what the write-back path does.

The server computes both from one authoritative line index. Two representations from one source cannot
disagree; two computations of the same representation can.

**`character` is a UTF-16 code-unit offset**, matching JavaScript's string indexing and the Language
Server Protocol. Scripts are near-always ASCII, but a comment containing an emoji would otherwise
misplace every squiggle after it on that line — a bug that appears once, in a user's file, and is
mystifying.

## Severity mapping

| Core | Wire | Editor | Canvas | Log |
|---|---|---|---|---|
| `Error` | `error` | Red squiggle | Red badge | Shown |
| `Warning` | `warning` | Amber squiggle | Amber badge | Shown |
| `Info` | `info` | None | None | Collapsed by default |

**Info produces no visual mark.** Inference diagnostics (`FS1510`, one per inferred component) would
otherwise cover a large script in squiggles for things that are working correctly.

## Grouping for the log

Diagnostics arrive as a flat list; the console log ([`56-console-log`](../50-frontend/56-console-log.md))
groups them. Grouping and deduplication are entirely the **client's** job, using `code`, severity, and
component. The server sends one entry per occurrence, including that component's fully formatted
message, so an expanded group can show per-component values without inventing fields absent from the
wire contract.

```jsonc
{ "code": "FS4001", "severity": "warning",
  "message": "N1 is approaching freezing point: 2.1 C.",
  "component": "N1", "range": null }
```

## For agents

`R-29` needs a diagnostic to be enough for an agent to fix its own generated script, without reading
prose:

| Field | What the agent does with it |
|---|---|
| `code` | Looks up the full explanation in `/docs/functions/diagnostics.md` |
| `range.offset/length` | Locates the exact text |
| `suggestion` | Applies a known-correct fix directly |
| `message` | Reads the human explanation when there is no suggestion |
| `component` | Knows which part of its design is wrong |

**`suggestion` is what makes the loop closed.** An agent that can apply suggestions fixes typos,
unknown parameters, and reserved-word collisions without a round trip to a human — and
[`16-diagnostics`](../10-language/16-diagnostics.md)'s invariant 5 guarantees an applied suggestion
always parses.

## Invariants

1. `line`/`character` and `offset`/`length` describe the same span, computed from one line index.
2. `character` counts UTF-16 code units.
3. Every `code` exists in the diagnostic registry.
4. `suggestion.newText` applied to `suggestion.range` produces a parseable script.
5. `occurrences` equals the length of `components` when both are present.
6. Diagnostics are ordered by severity, then by offset — so the first is the most important.
7. `range` is null exactly when the diagnostic concerns no source text.

## Error cases

None of its own. This contract carries diagnostics; a failure to compute a range is a bug (`FS9003`),
not a diagnostic.

## Worked example

`HE1 heat_exchanger powr=30 in=20 out=20C+30C` on line 4 (0-based line 3), starting at offset 100:

```jsonc
[
  { "code": "FS1503", "severity": "error",
    "message": "A heat_exchanger has no 'powr'. It accepts: power, in, out, dt, dp, flow.",
    "range": { "start": { "line": 3, "character": 19 },
               "end":   { "line": 3, "character": 23 },
               "offset": 119, "length": 4 },
    "component": "HE1",
    "suggestion": { "title": "Change 'powr' to 'power'",
                    "range": { "start": { "line": 3, "character": 19 },
                               "end":   { "line": 3, "character": 23 },
                               "offset": 119, "length": 4 },
                    "newText": "power" },
    "related": [] },

  { "code": "FS1302", "severity": "error",
    "message": "Cannot add two temperatures. To offset by a difference, write '20C + 30 dK'.",
    "range": { "start": { "line": 3, "character": 37 },
               "end":   { "line": 3, "character": 44 },
               "offset": 137, "length": 7 },
    "component": "HE1", "suggestion": null, "related": [] }
]
```

Ordered by severity then offset (invariant 6): both are errors, so `FS1503` at offset 119 precedes
`FS1302` at offset 137. Emitting them in production order — units before binding, so `FS1302` first —
is the easy mistake, and it is why the ordering has a test rather than a convention: the two stages
that produce these diagnostics run in the opposite order from the one the user reads them in.

What each consumer does with this: the editor draws two red squiggles and offers one quick-fix; the
canvas badges `HE1` red; the log shows two lines; an agent applies the `FS1503` suggestion, recompiles,
and is left with one error it must reason about.

## Acceptance criteria

- [ ] `line`/`character` and `offset`/`length` agree for every diagnostic over the sample corpus.
- [ ] A script containing a multi-byte character in a comment produces correct positions after it.
- [ ] Applying every suggestion in the broken-script corpus yields parseable scripts.
- [ ] A systemic warning on 40 components produces one entry with `occurrences: 40`.
- [ ] Diagnostics are ordered by severity then offset — asserted, since the worked example shows how
      easily it is got wrong.
- [ ] Every emitted `code` resolves in `/api/v1/metadata`.

## Open questions

None. The editor may call `/validate` for early parse/bind feedback and replaces it with `/compile`'s
full canonical diagnostics; a compile does not stream SSE. Transient frames carry stable diagnostic
occurrence ids with `started`/`cleared` events, and the log retains the resulting simulation-time
interval instead of erasing history (`D-30`, `43`, `56`).
