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
endpoints, sessions with warm start and supersession, the limits, metadata and OpenAPI. `P5.5` added
`format` to `42` and committed the metadata document and the editor's lexicon as goldens the same
day. **`43`, the
realtime contract, has not been looked at**; its absence below means nothing has looked, not that
nothing is wrong. The `edit` endpoint of `42` is deferred whole to `P7.1` and is not a defect.

**Next id: `A-7`.** The columns, their vocabularies, and the rule for filing, reopening and closing
are in [`08`](../08-implementation-sequence.md) under *Every package writes down what it found*.

## Open

| # | Filed | Effort | Risk | Basis | Document | What | Why it is still open |
|---|---|---|---|---|---|---|---|
| A-2 | 2026-09-18 | tiny | low | measured | [`42`](42-rest-contract.md) | **`metadata.docsIndex` is a repository-relative path, not a URI** | `42` says `docsIndex` is "a URI to the matching generated function index". `61` ships the docs as plain Markdown under `/docs` and nothing serves that directory over HTTP yet, so there is no URI to give, and the value is the configured path `docs/functions/index.md`. An agent following it today gets nothing. Closes when the docs are served, by making the option a URL. |
| A-6 | 2026-09-18 | medium | low | measured | [`42`](42-rest-contract.md), [`26`](../20-core-domain/26-model-contract.md), [`54`](../50-frontend/54-interaction-and-writeback.md) | **The wire has no unit factors and no pipe velocity, so two hovers `52` and `54` specify cannot be drawn** | `52` has the quantity hover show a value "in SI and in alternative units"; `54` has the connection hover show velocity and Reynolds number. `metadata.dimensions` lists unit symbols with no conversion factors, and a connection's state is its mass flow alone; velocity needs the bore, which Core has from the catalogue and the wire does not carry. P5.8 shows what exists (dimension and canonical unit; flow and the pipe's Δp) and files this. What closes it: a factor per unit in `metadata` (one number, `13`'s table is Core's), and `velocity` and `reynolds` on the pipe's state in `26`, computed where the bore is. |

## Traps

What a session working against these documents gets wrong first. Promoted from *Observations*
or from a closed entry when a session hits it a second time; read before working in this tier.

_None promoted yet._

## Closed

| # | Filed | Closed | Effort | Document | What was wrong | What changed |
|---|---|---|---|---|---|---|
| A-4 | 2026-09-18 | 2026-09-19 | — | [`41`](41-api-architecture.md), [`32`](../30-solver/32-steady-state-newton.md) | **The warm start's saving does not show in `solve.iterations`** | Changed 2026-09-19 (sweep tier 2): `OuterLoopResult.Iterations` and the wire's `solve.iterations` are the Newton iterations summed over every sizing pass, retries included -- the run's work, which is where a warm start's saving lands. On the cooling loop cold the count is 8 where the last-pass figure was 2. `26` and `docs/functions/model-contract.md` say which it is; `OuterLoopTests` asserts the total is at least the pass count and at least the last pass's figure. |
| A-3 | 2026-09-18 | 2026-09-19 | — | [`42`](42-rest-contract.md), [`13`](../10-language/13-type-and-unit-system.md) | **Two exchanger quantities go on the wire in spelled-out SI base units** | Closed with `L-54` / `D-119` (2026-09-19): the two dimensions are named and the wire reads `W/(m2*K)` and `m2*K/W`; `metadata.json`'s golden shows the two new dimension rows and their units. |
| A-1 | 2026-09-18 | 2026-09-19 | — | [`41`](41-api-architecture.md), [`32`](../30-solver/32-steady-state-newton.md) | **A solved request prepares the equation system twice** | Changed 2026-09-19 (sweep tier 2): `OuterLoop.RunAsync(PreparedModel, SemanticModel, ISubstance, WarmStart?, name, ct)` runs from a layout the caller prepared; the model/substance overload prepares and delegates to it. `ScriptPipeline` prepares once, reads the unknown count, and hands the same `PreparedModel` to the run. The saving is `Prepare`'s cost per solved request, ~14 ms on the 200-component header. |
| A-5 | 2026-09-18 | 2026-09-18 | — | [`42`](42-rest-contract.md) | **The metadata document's parameter and property order was process-dependent, so its ETag was too** | `42` makes `metadata` cacheable with an ETag, which only pays if the same registry gives the same bytes. `MetadataBuilder` enumerated each kind's parameters and properties from a dictionary, and the order a dictionary enumerates in depends on hash seeds that differ per process. Found 2026-09-18 when P5.5 committed the document as a golden for the completion tests and the second run rewrote it. In the same change the parameter ranges, which `42`'s example shows in the parameter's unit, were found on the wire in SI (a temperature's `typically −50…300` read as kelvin), so a completion detail would have shown `223…573`. | Parameters and properties are ordered by name; a range is converted to the parameter's canonical unit (`RangeIn`) before it goes on the wire, the way the value is. The golden is `Api.Tests/Contracts/Goldens/metadata.json` and the test regenerates it in place and fails once, like the schemas. |

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

**The schema exporter inlines every record where it occurs, and the TypeScript generator sees
each occurrence as a type.** `JsonSchemaExporter` writes no `$defs`; a repeated record is copied,
or, for the second occurrence at the same path, a `$ref` to the first occurrence's path. Fed to
`json-schema-to-typescript` as is, the cooling loop's `Quantity` came out as `Quantity` through
`Quantity15`. P5.4's generator (`frontend/scripts/wireTypes.ts`) expands the exporter's pointers,
hoists every titled object node into `$defs` under its title, and refers to it through a one-element
`allOf` (a `$ref` with a sibling `description` is a new anonymous type to that generator). One
interface per title; a title whose shape differs between two occurrences keeps a numbered second,
so a real divergence shows rather than hides. Titles exist because `SchemaDocumentation` adds them;
without a title nothing here works, which is why it is not optional.

**The `edit` endpoint is P7.1's.** `42` lists it and `08` puts the mutation API it fronts
(`IScriptEditor`, `17`) in P7.1. Decided 2026-09-18 with the user: no stub. A route that answers
501 is a promise the frontend would have to code around, and the endpoint's whole contract — the
revision echo, edits not text — is the mutation API's shape, which is better written once with it.
`42`'s two `edit` criteria stay unticked with that note.

**The metadata's port family gained a `pattern`; nothing else on the wire moved** (P5.13a, `D-120`,
2026-09-20). `PortFamilyWire` now carries `pattern` (`in[{index}]`) beside `prefix` (`in`), and its
`minIndex` is 2: the first member is the fixed port `in` under `ports`, and the family proper starts
above it, which is how the registry has it. `levelParameterSuffix` stays the *key* suffix (`_level`);
the script spelling of that parameter is under `indexedParameters` as `in[{index}].level`. Component
port ids (`in2`), `ComponentStateWire` fields (`flow2`, `tIn2`) and the `solved`/`sizes` maps are
unchanged by decision -- they are keys, and a client that showed them before shows them still. What a
client cannot yet do is turn a key back into the spelling the script uses (`L-56`).
