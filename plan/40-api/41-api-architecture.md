---
id: 41-api-architecture
title: API architecture
tier: 40-api
status: reviewed
owns: [hosting, DI, session and model lifetime, threading, cancellation, static hosting]
depends_on: [26-model-contract, 31-solver-architecture, 03-repository-layout]
traces_to: [R-16, R-18, R-19, R-21, R-40, R-41]
open_questions: 0
last_review_pass: 2
---

# API architecture

## Purpose

`FluidScript.Api` is a thin host: it accepts script text, runs Core's pipeline, and returns the model
contract. Its design problems are not about HTTP — they are about lifetime and cancellation. A user
typing produces a request every 300 ms, each superseding the last, while a transient run may be
streaming in the background. Getting that wrong burns CPU on results nobody will see.

## Responsibilities

**Owns.** Hosting, DI registration, session and model lifetime, the threading model, cancellation, and
how the frontend is served.

**Explicitly does not own.** Endpoint shapes ([`42-rest-contract`](42-rest-contract.md)), the WebSocket
protocol ([`43-realtime-contract`](43-realtime-contract.md)), any physics.

## Shape

ASP.NET Core minimal APIs. No controllers, no MVC — there are five endpoints and one socket, and
a controller hierarchy for that is ceremony.

```
FluidScript.Api/
├── Program.cs               composition root: DI, middleware, endpoint mapping
├── Endpoints/               one file per endpoint group
├── Realtime/                WebSocket handler and frame serialization
├── Contracts/               wire DTOs — never a Core type on the wire
├── Sessions/                session store and lifetime
└── wwwroot/                 built frontend, production only
```

**Wire DTOs are separate types from Core's model, mapped explicitly.** The mapping is boilerplate, and
it is what stops a rename inside Core silently reshaping the API — the exact failure the model
contract's versioning ([`26-model-contract`](../20-core-domain/26-model-contract.md)) exists to prevent,
and which auto-mapping from Core types would reintroduce.

## Sessions

The frontend holds one script; the backend needs somewhere to keep the previous solution, since warm
starting is what makes re-solves fast ([`31-solver-architecture`](../30-solver/31-solver-architecture.md)).

```csharp
/// <summary>Per-client state: the last compiled model and its solution, used to warm-start.</summary>
/// <remarks>
/// A session is a cache, never a source of truth. The script is the source of truth (P5), and it
/// arrives with every request. Losing a session costs one cold solve, nothing more.
/// </remarks>
public interface ISessionStore
{
    /// <summary>Gets or creates the session for an id.</summary>
    Session GetOrCreate(SessionId id);

    /// <summary>Drops sessions untouched for longer than the idle timeout.</summary>
    void Evict(TimeSpan idleFor);
}

public sealed class Session
{
    public SessionId Id { get; }

    /// <summary>The last solution, for warm starting. Null on a first request.</summary>
    public StateVector? LastSolution { get; set; }

    /// <summary>Hash of the script the last solution belongs to.</summary>
    /// <remarks>A warm start is valid only when the topology is unchanged. Comparing a hash of the
    /// bound model's structure — not the raw text — lets a whitespace edit still warm-start.</remarks>
    public int? LastTopologyHash { get; set; }

    /// <summary>Cancellation for the current draft compile/solve only.</summary>
    public CancellationTokenSource? InFlightDraft { get; set; }

    /// <summary>The active immutable transient snapshot and its independent cancellation.</summary>
    public ActiveRun? Transient { get; set; }
}
```

**"A session is a cache, never a source of truth"** is the rule that keeps this simple. No server-side
document model, no synchronisation, no conflict resolution. The script arrives with every request; the
session only makes the response faster. It also means the API is horizontally scalable by accident and
that a server restart is invisible to the user.

## Cancellation — the load-bearing part

The debounce path produces a request every 300 ms while the user types, and each supersedes the last.

**On every compile/solve request:**

1. Cancel the session's in-flight solve, if any.
2. Register the new request's `CancellationTokenSource` as in-flight.
3. Run the pipeline with the linked token (request abort ∪ supersession).
4. Clear the registration.

Without step 1, a user typing for ten seconds queues thirty solves, twenty-nine of which produce
results nobody sees, and the last one starts after all of them. With it, at most one solve runs per
session.

**Cancellation is checked between solver iterations, never inside a residual evaluation**
([`32-steady-state-newton`](../30-solver/32-steady-state-newton.md)'s invariant 7). Granularity is one
iteration, which is a millisecond or two — fine.

**A transient run is never registered in `InFlightDraft`.** Starting it copies all required data into
an immutable `RunSnapshot`; draft requests can cancel only other draft requests. A run ends only for
the stop conditions in `D-22`/`07-quality-attributes`, including explicit Stop, disconnect, worker or
solver invariant failure, and corrupt/incompatible frames.

## Threading

- **Short draft solves** run on a request-pool worker and remain cancellable between iterations.
- **Every transient runs on a bounded dedicated worker thread**, fed through a bounded channel. The
  WebSocket handler only validates messages and asynchronously drains verified frames; it never calls
  the integrator or property backend. Worker count defaults to logical processors minus one, minimum
  one, and server-wide queue/run limits are returned by metadata.
- **Per session, one draft solve and one transient may run concurrently.** A newer draft cancels only
  the older draft. A second Run command explicitly stops/replaces the prior run after cleanup; a
  passive edit does neither.
- **No shared mutable state across requests** except the session store, which is a `ConcurrentDictionary`
  of independent sessions.

The property adapter is worker/solve-scoped unless the M0 spike proves both thread safety and safe
concurrent calls for the pinned version. It is never promoted to a shared singleton by assumption.

There is no locking anywhere in Core, and there must not be: a solve is a pure function of its inputs
([`31`](../30-solver/31-solver-architecture.md)'s invariant 6). All concurrency lives here.

## Serving the frontend

| Environment | Arrangement |
|---|---|
| Development | Vite dev server on 5173 with HMR; API on 5000; Vite proxies `/api` and `/ws` to it |
| Production | `npm run build` → `wwwroot`; the API serves static files with SPA fallback; one origin, one port |

Development uses a proxy rather than CORS so the two environments differ in as few ways as possible —
same-origin in both, so no cookie, WebSocket-origin, or preflight behaviour changes between them. CORS
is a source of "works locally, fails deployed" that a proxy removes entirely.

## DI registration

| Service | Lifetime | Reason |
|---|---|---|
| `IComponentRegistry` | Singleton | Immutable metadata |
| `ISubstanceRegistry` | Singleton | Immutable |
| `IBinder`, parser | Singleton | Stateless |
| `ISolver` implementations | Transient | Hold per-solve state |
| `ISessionStore` | Singleton | The shared cache |
| `IModelSerializer` | Singleton | Stateless |
| Property backend factory/pool | Singleton | Produces worker-scoped adapters; concurrency behavior is pinned by the M0 spike |
| Transient worker pool | Singleton | Bounded queue and dedicated CPU workers; owns run cleanup/watchdogs |

The last row is a real risk: a property backend that is not thread-safe and is registered as a
singleton fails intermittently under concurrent requests, which is the hardest kind of bug to
reproduce. It must be verified during the SharpProp spike
([`21-fluid-and-state`](../20-core-domain/21-fluid-and-state.md)'s M0 packaging gate), not assumed.

## Invariants

1. No Core type appears on the wire; every response is a `Contracts/` DTO.
2. A session holds no authoritative state — deleting every session changes no result.
3. At most one solve per session runs at a time.
4. Every request's cancellation token reaches the solver.
5. `Program.cs` contains no business logic.
6. The API adds no physics: every number in a response came from Core.
7. Static file serving is production-only; development never serves a stale `wwwroot`.

Invariant 7 prevents the failure where a developer sees an old build because `wwwroot` was populated
once and the dev server is bypassed.

## Error cases

[`42-rest-contract`](42-rest-contract.md) is the sole authority for HTTP status codes. This host owns
only the lifecycle handling that precedes a response:

| Situation | Host handling |
|---|---|
| Script has errors | Return the normal model DTO with diagnostics; `42` maps it to HTTP |
| Malformed request or input limit exceeded | Return typed request failure for `42` to map |
| Solve cancelled by supersession | Abandon the response; the client already sent a newer request |
| Solve cancelled by client disconnect | Cancel/join work; record 499 in server access logs, with no response body |
| Internal exception | Log `FS9001` with correlation id; return typed internal failure |
| Session not found | Create it implicitly — sessions are a cache |

**A script with errors returning 200 is deliberate** and worth defending, since it looks wrong. The
request succeeded: the API was asked to compile a script and it did, reporting what it found. 4xx would
force the frontend to parse error responses on the path it hits most often — every keystroke on an
incomplete script — and would conflate "your script has a typo" with "your request was malformed".

## Worked example

The debounce path, one keystroke:

```
t=0ms     User types 'v' in "kv=12.4"
t=300ms   Debounce fires. POST /api/compile { sessionId, script }
t=301ms   Handler cancels the in-flight solve from the previous keystroke.
t=302ms   Parse → bind → lower.  Topology hash matches the session's → warm start available.
t=305ms   Sizing: 1 pass (sizes barely moved).
t=308ms   Newton from LastSolution: 2 iterations (vs 5 cold).
t=312ms   Serialize to the model contract.
t=314ms   200 with the model. Session updated.
```

**14 ms total, of which the solve is 3.** The warm start saved roughly 8 ms and the cancellation saved
an entire wasted solve. Neither is visible in a profile of a single request, and both are what makes
the editor feel live rather than laggy — which is the whole of `R-21`.

## Acceptance criteria

- [ ] A superseding request cancels the previous solve within one iteration, asserted by a counting
      fake solver.
- [ ] A script with errors returns 200 with diagnostics and a topology-only model.
- [ ] Warm start reduces iteration count on an unchanged topology, measured.
- [ ] Deleting all sessions changes no response body.
- [ ] An architecture test asserts no Core type is reachable from a `Contracts/` type.
- [ ] Development runs against the Vite proxy with no CORS configuration present.
- [ ] 100 concurrent compiles of different scripts produce correct, independent results — the property
      backend thread-safety check.
- [ ] Client disconnect mid-solve leaves no running work after one iteration.
- [ ] Fault injection proves draft edits neither cancel nor mutate an active run, while every run stop
      condition cancels and joins its dedicated worker.
- [ ] Backend thread tracing proves transient integration never runs on the WebSocket handler.
- [ ] The frontend worker integration criterion in `51` proves frame decode and render preparation do
      not run on the browser UI thread; this backend document does not claim to observe browser threads.

## Open questions

None. The client supplies an untrusted UUID used only as a cache key; adapters are worker-scoped unless
the pinned spike proves safe sharing; byte/token/declaration/unknown limits come from `07` and metadata.
