---
id: 42-rest-contract
title: REST contract
tier: 40-api
status: draft
owns: [REST endpoints, request and response shapes, status codes, REST path versioning]
depends_on: [26-model-contract, 41-api-architecture]
traces_to: [R-18, R-20, R-21, R-25, R-37, R-39, R-40, R-45]
open_questions: 0
last_review_pass: 0
---

# REST contract

## Purpose

`D-06`'s request/response half: compile, validate, solve, edit, and metadata. Small on purpose — five endpoints,
one of which carries almost all the traffic.

## Responsibilities

**Owns.** Endpoint paths, request and response shapes, status codes, and REST versioning.

**Explicitly does not own.** The model shape ([`26-model-contract`](../20-core-domain/26-model-contract.md)),
transient streaming ([`43-realtime-contract`](43-realtime-contract.md)), hosting
([`41-api-architecture`](41-api-architecture.md)).

## Endpoints

| Method | Path | Purpose | Typical caller |
|---|---|---|---|
| `POST` | `/api/v1/compile` | Parse, bind, lower, size, solve. **The debounce path.** | Editor, once per debounce interval |
| `POST` | `/api/v1/validate` | Parse and bind only — diagnostics, no physics | Editor, for fast feedback on large scripts |
| `POST` | `/api/v1/solve` | Explicit solve, stricter than compile | The Solve button |
| `POST` | `/api/v1/edit` | Apply a canvas edit, returning text edits | Canvas write-back (`R-25`) — lands with P7.1's mutation API, not P5.2 |
| `POST` | `/api/v1/format` | The canonical layout of a script, as text edits (`17`) | The editor's Format command (`52`), P5.5 |
| `GET` | `/api/v1/metadata` | Component kinds, parameters, units, diagnostic codes | Editor completion; agents (`R-29`) |

### `POST /api/v1/compile`

```jsonc
// request
{ "sessionId": "b3f1…", "script": "circuit coolingLoop\n…", "solve": true }

// response — 200, always, unless the request itself is malformed
{ "model": { /* 26-model-contract, including its one diagnostics collection */ },
  "timings": { "parseMs": 1, "bindMs": 2, "sizeMs": 3, "solveMs": 6, "totalMs": 14 } }
```

`solve: false` stops after lowering — topology without physics, for a first render while a large script
is still being typed.

`timings` is five integers of milliseconds: `parseMs`, `bindMs`, `sizeMs` (lowering and sizing),
`solveMs` and `totalMs`. A stage that did not run reports `0`.

**A script this build cannot read is the one shape where the envelope carries diagnostics.** A
`fluidscript` line naming a major outside `18`'s supported set returns `model: null` and the
compatibility diagnostics beside it as `diagnostics`; there is no model to carry them. On every
other response the property is absent — the model's collection is the only one (invariant 10).

**Over a limit is a diagnostic, not a status.** A script over `07`'s declaration, token or unknown
ceiling returns 200 with `FS4601` as an error in the model's diagnostics and the solver skipped;
the unknown count is known only after lowering, so the declaration and token checks run after the
parse and the unknown check after `Prepare`, and the model carries the topology either way. Only the
byte ceiling is a status, `413`, because it is checked before the script is read.

**`timings` ships in the response, not only in logs.** The frontend surfaces it in a status line, which
turns "it feels slow" into "sizing took 400 ms" without a profiler. It costs four integers.

### `POST /api/v1/solve` — stricter than compile

Same shape, different strictness: warnings that `compile` tolerates become errors here. Specifically
`FS1507` (unconnected component) and `FS1511` (disconnected graph), as fixed in
[`15-semantic-model`](../10-language/15-semantic-model.md).

The reasoning: while typing, a half-written script must still render. When the user presses Solve they
are asking for an answer, and an answer computed from a circuit with a disconnected pump is misleading.

The escalation happens before the solver decides whether to run, so a `solve` whose only fault is a
floating pump never reaches the solver: `circuits[].solved` is `false`, `model.solve` is absent and
the escalated `FS1507` is why.

### `POST /api/v1/validate` — diagnostics only

```jsonc
// request
{ "script": "circuit coolingLoop\n…" }

// response — 200
{ "contractVersion": "2.0", "languageMajor": 1,
  "diagnostics": [ /* 44's records, ordered by severity then offset */ ],
  "timings": { "parseMs": 1, "bindMs": 2, "sizeMs": 0, "solveMs": 0, "totalMs": 4 } }
```

No `sessionId`, because nothing is cached and nothing warm-started; no model, because the model is
what this endpoint exists not to build. `languageMajor` is the major the script declared, or `null`
when the compatibility gate (`18`) refused it. `contractVersion` says which diagnostic record shape
the list is in (invariant 2), since the diagnostic record is the model contract's.

### `POST /api/v1/edit` — canvas write-back

```jsonc
// request
{ "sessionId": "b3f1…", "documentRevision": 184, "script": "…",
  "operation": { "kind": "setParameter", "component": "3WV", "parameter": "kv",
                 "value": 12.4, "unit": "m3/h" } }

// response
{ "documentRevision": 184,
  "edits": [ { "span": { "start": 96, "length": 0 }, "newText": " kv=12.4" } ],
  "model": { /* the re-solved model */ } }
```

**The server returns text edits, not new text** ([`17-formatting-and-round-trip`](../10-language/17-formatting-and-round-trip.md)).
The client applies them to its own buffer as one undoable unit, preserving cursor position and undo
history. Returning whole text would blow away both, which is the thing that makes an editor feel
broken.

`documentRevision` is an opaque, monotonically increasing editor revision supplied by the client and
echoed unchanged. The client applies edits only when the response revision still equals its current
buffer revision; otherwise it discards the response and re-sends the operation with current text.

Operations mirror `IScriptEditor`: `setParameter`, `removeParameter`, `addComponent`, `addConnection`,
`removeConnection`, `rename`, `materialize`.

### `POST /api/v1/format` — the formatter, as edits

```jsonc
// request
{ "script": "HE1   heat_exchanger power = 30\nPU1 pump\n" }

// response
{ "edits": [ { "span": { "start": 0, "length": 31 }, "newText": "HE1 heat_exchanger power=30" } ] }
```

Added by P5.5 (2026-09-18). The formatter lives in Core (`Formatter`, `17`) because it reads the
lexer's tokens and trivia, and the editor asks for it the way it will ask for a canvas edit: one
edit per changed line, spans in UTF-16 code units of the script sent, never overlapping, so the
client applies them as one transaction and its cursor and undo history survive. No session and no
revision: formatting is stateless and the client checks its own buffer is still the text it sent
before applying. The same size limit and the same 413 as `compile`. A formatted script answers
with an empty array.

### `GET /api/v1/metadata`

The static description of the language: every component kind with parameters, aliases, units, ranges,
fixed ports and indexed port/parameter-family patterns (`D-32`),
`SymbolId` and `SymbolDefinition` delivered under `D-24`; every unit and diagnostic; current/supported language and contract
versions; exact catalogue/property versions; quality/input/concurrency limits; and `docsIndex`, a URI
to the matching generated function index. Cacheable with an ETag. Parameters and properties are
listed by name and a parameter's range is in its canonical unit (P5.5, `A-5`), and the document is
committed as `Api.Tests/Contracts/Goldens/metadata.json` so the editor's completion tests run against
the real registry without a host. The editor's lexicon -- the reserved words, the unit symbols and
`D-15`'s thresholds -- is not part of this document but a committed schema-side file,
`Contracts/Schemas/language.json`, generated from Core like the schemas and consumed at the
frontend's build (`52`).

**This is the endpoint an LLM agent reads first** (`R-29`). It is also what drives editor completion,
so the two consumers keep each other honest — a gap in the metadata shows up immediately as missing
autocomplete.

## Conventions

| Rule | Reason |
|---|---|
| Version in the path (`/api/v1/`) | Visible, cacheable, trivially routable |
| `camelCase` JSON | Matches the model contract |
| Values in canonical script units | Same rule as the model contract; one convention everywhere |
| Errors as RFC 9457 problem details | Only for request-level failures, never for script diagnostics |
| No `PUT`/`DELETE` | Nothing is stored; every call is a pure function of its body plus a cache |

**Every endpoint is idempotent and stateless apart from the warm-start cache.** The same body always
produces the same model. That makes the API testable with recorded request/response pairs and makes
`compile` safe to retry.

### REST-major coexistence

Each supported REST major has a separate `/api/v{major}` route set and generated schema. A new major
coexists with the preceding major for at least one application major release; removal is announced in
release notes and never changes an existing route's meaning. Session-cache keys are
`(apiMajor, sessionId)`, so warm starts never cross majors. `contractVersion` identifies the model
payload independently: two REST majors may carry the same model contract, and a REST major may add
backward-compatible endpoints without changing that contract version.

## Invariants

1. A script with diagnostics returns 200; only a malformed *request* is 4xx.
2. Every response containing values states its `contractVersion`.
3. `compile` and `solve` differ only in strictness, never in the shape they return.
4. `edit` returns text edits, never whole text.
5. `metadata` is a pure function of the deployed version.
6. Every endpoint honours cancellation.
7. No endpoint mutates server state that another endpoint reads, except the session cache.

## Error cases

| Status | When |
|---|---|
| 200 | Anything about the script, including errors |
| 400 | Malformed JSON, missing required field (the problem names it as `field`), unknown operation kind (`edit`, P7.1) |
| 404 | Unknown path |
| 413 | Script beyond the size limit; the problem carries `bytes` and `limit` |
| 499 | The request was superseded by a newer one on the same session (`41`), or the client disconnected — the first is answered, the second is logged with nothing sent |
| 500 | Internal fault: `FS9001`, with `code` and `correlationId` on the problem |

Every non-200 body is RFC 9457 problem details. A malformed request never reaches the pipeline, so
the timings that a 200 carries are absent from every one of these.

## Worked example

The Solve button on a script whose pump is unconnected — the brief's original example:

```
POST /api/v1/solve   { sessionId, script: <the brief's example> }

200 {
  "model": { "circuits": [ { "name": "coolingLoop", "number": 100, "solved": false, … } ],
             "components": [ … nine components, states null … ],
             "diagnostics": [
    { "code": "FS1507", "severity": "error",
      "message": "'PU1' is not connected to anything.",
      "span": { "start": 214, "length": 3 }, "component": "PU1" },
    { "code": "FS2107", "severity": "warning",
      "message": "'N1' is a dead end. Declare it 'inlet' or 'outlet' if fluid crosses there; a node's t= or p= only states a level and passes no mass.",
      "component": "N1" },
    { "code": "FS2107", "severity": "warning",
      "message": "'N3' is a dead end. Declare it 'inlet' or 'outlet' if fluid crosses there; a node's t= or p= only states a level and passes no mass.",
      "component": "N3" }
             ] },
  "timings": { "parseMs": 1, "bindMs": 2, "sizeMs": 0, "solveMs": 0, "totalMs": 4 }
}
```

200, with `solved: false`, one error and two warnings — the set
[`01-vision-and-scope`](../00-foundation/01-vision-and-scope.md) enumerates for this script, minus the
six `FS1510` info entries elided here for length. The canvas still renders the topology — the user
sees their circuit with the pump floating unconnected, which is a better explanation of the problem
than any sentence. The same request to `/compile` would return the same body with `FS1507` as a
**warning**, which is what the editor shows while typing.

The span is `{ start: 214, length: 3 }`: `FS1507` points at the identifier `PU1`, not at the whole
declaration. A diagnostic about a name underlines the name.

## Acceptance criteria

- [x] Every endpoint has a contract test pinning request and response shapes
      (`Api.Tests/Endpoints`, P5.2; `edit` excepted, see below).
- [x] A script with errors returns 200 from `compile` and `solve` (P5.2).
- [x] `solve` escalates `FS1507` to error; `compile` does not (P5.2; the escalated solve never
      reaches the solver, asserted with a counting fake).
- [ ] `edit` returns edits that, applied, produce a parseable script. *Deferred with the endpoint
      to P7.1: the mutation API it fronts (`17`, `IScriptEditor`) is that package's, and a stub that
      answers 501 would be a route nothing can call. Decided 2026-09-18 with P5.2.*
- [ ] An `edit` response echoes the request revision; a client-side integration test changes the
      buffer while the request is in flight and proves the stale edits are discarded and re-requested.
      *With `edit`, P7.1.*
- [x] `metadata` covers every registered component kind and every diagnostic code (P5.2; the
      test enumerates `ComponentRegistry.Default` and `DiagnosticRegistry.All`, retired codes too).
- [x] Tank metadata exposes canonical `tank`/`volume`, input aliases `container`/`v`, the 1…16
      inlet/outlet families, indexed temperature/elevation patterns, and dynamic symbol-anchor rule
      (P5.2; the anchor rule is each family's `levelParameterSuffix`, the parameter that places the
      port on the vessel).
- [x] OpenAPI is generated and matches the hand-written contract tests (P5.2: `/openapi/v1.json`
      from `Microsoft.AspNetCore.OpenApi`; the test asserts every route is in it).
- [x] Cancelling a request stops the solve (P5.2: supersession and disconnect both, `41`).
- [x] `format` returns one edit per changed line and none for a formatted script, and the edits
      applied give `Formatter.FormatText`'s output (P5.5; idempotence over the corpus is `17`'s test).
- [x] No response duplicates model diagnostics; `/validate` is the only diagnostics-only response
      (P5.2; the compatibility-refused script's `model: null` envelope is the one exception, above).
- [x] Shared JSON Schemas generate the C# and TypeScript transport DTOs and contract tests reject
      drift, as `D-46` reversed it: the C# records are the source, the schemas under
      `Api/Contracts/Schemas` are exported from them (P5.2, `SchemaTests`) with every object's
      `title` and every documented member's `description` from the XML docs (P5.4,
      `SchemaDocumentation`, `D-46` step 4), and `frontend/src/api/types.generated.ts` is generated
      from those files by `npm run types` and gated by a test that regenerates it (P5.4). A drift on
      either side fails a suite.
- [ ] REST majors coexist and cache independently under the policy above; `contractVersion` remains
      the model payload's version rather than an alias for the route major. *The key is
      `(apiMajor, sessionId)` and `contractVersion` is the model's (P5.2); a second major does not
      exist to prove coexistence with, so the box stays open.*

## Open questions

None. Model/realtime JSON Schemas generate both transport DTO sets. M3 SVG/PNG export is client-side
from the same declarative symbols and placements; a future server-side format adds its own endpoint.
