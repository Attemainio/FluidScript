---
id: 40-api-defects
title: What implementing against the API tier found
tier: 40-api
owns: [defect and observation record for documents 41-44]
---

# What implementing against the API tier found

Defects, deferrals and observations from implementing against `41`–`44`. The rule and its reasoning
are in [`08-implementation-sequence`](../08-implementation-sequence.md).

`41`, `42` and `44` were implemented against by `P5.2` (2026-09-18): the host, the four REST
endpoints, sessions with warm start and supersession, the limits, metadata and OpenAPI. **`43`, the
realtime contract, has not been looked at**; its absence below means nothing has looked, not that
nothing is wrong. The `edit` endpoint of `42` is deferred whole to `P7.1` and is not a defect.

## Open

| # | Document | What | Why it is still open |
|---|---|---|---|
| A-1 | [`41`](41-api-architecture.md), [`32`](../30-solver/32-steady-state-newton.md) | **A solved request prepares the equation system twice** | What the pipeline needs: the unknown count before it commits to a solve, so that `07`'s unknown ceiling can refuse a script with `FS4601` before any Newton step. What it does: it calls `OuterLoop.Prepare` to read the count, then `RunAsync`, which prepares again internally because it was written for a caller that never prepared. On the 200-component header `Prepare` is about 14 ms, paid twice per solved request. Not wrong, only wasteful, and invisible at the sample sizes. What would close it: `RunAsync` taking the prepared layout, or `Prepare` memoised on the loop for one model. Filed 2026-09-18 with P5.2. |
| A-2 | [`42`](42-rest-contract.md) | **`metadata.docsIndex` is a repository-relative path, not a URI** | `42` says `docsIndex` is "a URI to the matching generated function index". `61` ships the docs as plain Markdown under `/docs` and nothing serves that directory over HTTP yet, so there is no URI to give, and the value is the configured path `docs/functions/index.md`. An agent following it today gets nothing. Closes when the docs are served, by making the option a URL. |
| A-3 | [`42`](42-rest-contract.md), [`13`](../10-language/13-type-and-unit-system.md) | **Two exchanger quantities go on the wire in spelled-out SI base units** | `metadata` names the unit a parameter is reported in, and the model contract carries the same unit on every value. For dimensions with a canonical unit that is the unit table's symbol; for the exchanger's `u` (W/(m²·K)) and `fouling` (m²·K/W) the table has no symbol at all, so the fallback is `Dimension.ToSiUnitString` and the wire reads `kg/(s³·K)` and `s³·K/kg`. Dimensionally right, and no engineer writes it. The gap is the unit table's (`L-54`); this row is so the wire's reader knows where the spelling comes from. Before P5.2 these were worse — dimensionless, `C-99` — which is how the metadata test found both. |

## Closed

| # | Document | What was wrong | What changed |
|---|---|---|---|

## Observations

**A superseded request is answered, a disconnected one is not.** `42`'s error table had one row for
499, "client disconnected (logged, nothing sent)", and `41`'s supersession rule said the previous
draft is cancelled but not what its caller sees. They are two cases: a client that has gone cannot be
answered, but a client whose newer request overtook its older one is still there and waiting on both.
P5.2 answers the older one with 499 problem details so the caller can tell "abandoned, cheaply" from a
fault, and the row in `42` says so.

**The pipeline throws after the solve, not the solver.** `NewtonSolver` honours cancellation by
returning a `Cancelled` termination rather than throwing, which is right for Core (`07`, no stage
throws on user input; a cancelled solve is a result). At the endpoint that result would have been a
200 with an unsolved model, indistinguishable from a script the solver gave up on. `ScriptPipeline`
calls `ThrowIfCancellationRequested` after the solve returns, so the endpoint's `catch` sees the
cancellation and answers 499. The Core rule stands; the translation is the host's.

**Malformed JSON is a 500 in Development unless something says otherwise.** ASP.NET's minimal APIs
set `ThrowOnBadRequest` in the Development environment so that a bad body surfaces as an exception,
and the exception handler saw it as an internal fault. `InternalFaultHandler` maps
`BadHttpRequestException` to its own status as problem details before the `FS9001` branch. Worth
knowing because a test host is Development by default and the production host is not, so the two
would otherwise disagree on a 400.

**`Microsoft.AspNetCore.OpenApi` documents the routes from their handlers.** The document at
`/openapi/v1.json` is generated at request time from the endpoint metadata; there is no hand-written
description to drift. What the contract tests pin is the wire shape through the JSON schemas
(`D-46` step 2, `SchemaTests`), and the OpenAPI test only asserts every route is present. A later
package that wants the schemas *inside* the OpenAPI document has both halves to join.

**The `edit` endpoint is P7.1's.** `42` lists it and `08` puts the mutation API it fronts
(`IScriptEditor`, `17`) in P7.1. Decided 2026-09-18 with the user: no stub. A route that answers
501 is a promise the frontend would have to code around, and the endpoint's whole contract — the
revision echo, edits not text — is the mutation API's shape, which is better written once with it.
`42`'s two `edit` criteria stay unticked with that note.
