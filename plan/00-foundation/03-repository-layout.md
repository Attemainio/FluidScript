---
id: 03-repository-layout
title: Repository layout
tier: 00-foundation
status: reviewed
owns: [directory tree, project boundaries, solution files, build props, migration off the flat csproj]
depends_on: [01-vision-and-scope]
traces_to: [R-16, R-17, R-30]
open_questions: 0
last_review_pass: 2
---

# Repository layout

## Purpose

Fixes the monorepo shape selected by `D-09` before any code exists, so that every later document can
name a real path. It also specifies the migration away from the flat `FluidScript.csproj` currently
at the root — that file and its `Class1.cs` are scaffolding from `dotnet new`, not a starting point.

## Responsibilities

**Owns.** The directory tree, project names and their dependency direction, solution files, the
central build and package-version property files, `.editorconfig`, `.gitattributes`, `.gitignore`.

**Explicitly does not own.** What goes *inside* the projects (the tier 10–50 documents), the coding
standards themselves ([`04-engineering-standards`](04-engineering-standards.md)), CI
([`63-ci-and-repo-hygiene`](../60-docs-and-devex/63-ci-and-repo-hygiene.md)).

## Target tree

```
FluidScript/
├── FluidScript.slnx                 solution: backend projects only
├── global.json                      the SDK feature band this repository builds with
├── Directory.Build.props            shared MSBuild properties for every project
├── Directory.Packages.props         central package management — versions live here, not in csproj
├── .editorconfig                    analyzer + formatting rules
├── .gitattributes                   LF enforcement on .cs/.csproj/.sh/.fluid
├── .gitignore
├── README.md                        R-30: newcomer to a rendered diagram
├── LICENSE
├── CLAUDE.md                        agent operating contract
│
├── src/
│   ├── FluidScript.Core/            the language + physics library. No UI, no ASP.NET.
│   │   ├── Language/                lexer, parser, AST, binder, printer      (tier 10)
│   │   ├── Units/                   dimensions, quantities, unit parsing     (tier 10)
│   │   ├── Diagnostics/             codes, severities, spans                 (tier 10)
│   │   ├── Fluids/                  ISubstance and the SharpProp adapter     (tier 20)
│   │   ├── Components/              Node, Pipe, HeatExchanger, Valve, Pump   (tier 20)
│   │   ├── Topology/                graph construction and validation        (tier 20)
│   │   ├── Catalogs/                shipped dimension tables and provenance  (tier 20)
│   │   ├── Sizing/                  auto-sizing rules and constraints        (tier 20)
│   │   ├── Solvers/                 ISolver, Newton, transient, controllers  (tier 30)
│   │   └── Model/                   the serialized model contract            (tier 20)
│   │
│   ├── FluidScript.Api/             ASP.NET Core host. References Core.      (tier 40)
│   │   ├── Endpoints/
│   │   ├── Realtime/
│   │   └── Contracts/               wire DTOs — never Core types on the wire
│   │
│   └── FluidScript.Export/          M6 DXF/model writers using posted placements + Core symbols; not created before an approved exporter
│
├── tests/
│   ├── FluidScript.Core.Tests/      mirrors src/FluidScript.Core one folder per folder
│   ├── FluidScript.Api.Tests/       endpoint and contract tests
│   └── FluidScript.Fixtures/        shared sample scripts and expected outputs
│
├── frontend/                        React + TypeScript + Vite               (tier 50)
│   ├── package.json
│   ├── vite.config.ts
│   ├── index.html
│   └── src/
│       ├── features/                editor/, canvas/, files/, log/, playback/, theme/
│       ├── api/                     generated client + WebSocket transport
│       ├── workers/                 layout/routing and transient frame preparation
│       ├── design/                  tokens, themes, primitives
│       └── main.tsx
│
├── docs/                            R-28: user-facing documentation
│   ├── tutorial/
│   ├── advanced/
│   └── functions/
│
├── plan/                            this tree
├── samples/                         .fluid scripts used by tests, docs, and the demo
└── .claude/                         skills, agents, review state
```

### Project dependency direction

```
FluidScript.Api ──► FluidScript.Core ◄── FluidScript.Export
                            ▲
                            └── FluidScript.Core.Tests
```

**Core references nothing of ours.** It has no ASP.NET, no serializer anywhere in its own code — not
merely off its public surface, which is what `D-47` settled and what the architecture test enforces —
and no knowledge that a frontend exists (`R-16`). The wire DTOs live in `FluidScript.Api/Contracts` and
are mapped from Core's model types, so a change to a Core record cannot silently reshape the API.

The resolved export boundary is the deliberate exception: M3 SVG/PNG uses frontend placements, while
a future `FluidScript.Export` receives placements explicitly and resolves the same Core-owned symbols;
it does not compute geometry or add a second layout engine (`D-03`, `D-20`, `D-29`).

### File extension

Scripts use **`.fluid`** (`D-10`). `.fs` collides with F#, `.fsc` with several existing tools.

## Build configuration

### `global.json`

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestPatch" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

**`rollForward` is load-bearing, and `latestPatch` is a deliberate narrowing of `latestFeature`.** It
means: any 10.0.**1**xx SDK, and nothing from another feature band. Two things depend on it.

The first is ordinary. `setup-dotnet` installs from this file, so CI resolves the same band a
developer does rather than whatever the runner image happens to carry.

The second is not obvious and is why the value is narrow. **Anything that loads this repository's
projects outside `dotnet build` picks its own SDK, and if it picks a different one the two fight over
one NuGet assets cache.** The Roslyn-backed tooling a session uses is exactly that: it registers an
MSBuild, restores with it, and reads `obj/` — so with two SDKs installed and a permissive
`rollForward`, the tool and the shell can each satisfy the pin with a different band, and whichever
restored last leaves a cache the other cannot open. The symptom is a workspace that loads with
missing references and answers questions as though nothing were wrong. Naming one band removes the
ambiguity at the source instead of pinning anyone's `PATH`.

The cost is stated plainly: a machine with only a 10.0.3xx SDK cannot build this repository until it
installs a 10.0.1xx one, and gets `SDK not found` rather than a silent roll-forward. That is the
intended trade — a refusal at the first command beats a degraded answer an hour later.

### `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

`TreatWarningsAsErrors` plus `GenerateDocumentationFile` means a missing XML doc comment on a public
member **fails the build** (CS1591). That is intentional and is how `R-28`'s spirit is enforced on the
C# side without anyone policing it.

### `Directory.Packages.props`

Central package management: `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`,
every version pinned exactly once. Initial set:

| Package | Project | Why |
|---|---|---|
| `SharpProp` | Core | `R-07` — fluid properties |
| `UnitsNet` | Core | Dimensioned quantities; SharpProp already returns UnitsNet types, so adopting it avoids a second conversion vocabulary at the adapter boundary. |
| `xunit.v3` | tests | `R-17` |
| `Microsoft.NET.Test.Sdk` | tests | test host |
| `Verify.Xunit` | tests | golden-file tests for parser, printer, and model output |

Nothing else without a stated reason in a review. A dependency added to Core is a dependency every
consumer inherits.

### `.editorconfig`

Near-empty by design, with a header stating why: analyzer defaults are the standard, and every
suppression must carry its reasoning in the same edit that adds it. A file full of silenced rules is
a standard nobody follows.

### `.gitattributes`

Force `LF` on `*.cs`, `*.csproj`, `*.props`, `*.sh`, `*.fluid`, `*.md`. Windows tooling CRLF-ing a
`.cs` file breaks raw string literals in golden-file tests, which fail in a way that looks like a
logic bug. `*.fluid` matters doubly: script tests compare text spans by offset.

## Migration off the current scaffold

The repository root currently holds `FluidScript.csproj`, `Class1.cs`, `bin/`, `obj/`, and a `.vs/`
folder. All of it is `dotnet new classlib` output.

1. Delete `Class1.cs`, `bin/`, `obj/`, `.vs/`. Confirm `.gitignore` already covers the last three.
2. Delete the root `FluidScript.csproj`. It is replaced, not moved — its only content is the property
   group that `Directory.Build.props` now owns centrally.
3. `dotnet new classlib -o src/FluidScript.Core`, then strip the generated `csproj` down to nothing
   but `<Project Sdk="Microsoft.NET.Sdk" />` plus package references; the properties come from
   `Directory.Build.props`.
4. Repeat for `FluidScript.Api` (`webapi`, minimal, no controllers) and the test projects (`xunit3`).
5. Rewrite `FluidScript.slnx` to reference `src/**` and `tests/**`.
6. `npm create vite@latest frontend -- --template react-ts`.
7. Verify: `dotnet build` and `dotnet test` both succeed from the root with zero warnings.

Step 7 is the acceptance test for this document. Do it before writing a line of FluidScript logic —
a `TreatWarningsAsErrors` repo that already has warnings on day one never gets clean again.

## Invariants

1. `FluidScript.Core` has no `PackageReference` to any ASP.NET or UI package, and no `ProjectReference`
   at all.
2. Every package version appears exactly once in the repository, in `Directory.Packages.props`.
3. The test tree mirrors the source tree folder for folder: `src/FluidScript.Core/Sizing/PumpSizer.cs`
   is tested from `tests/FluidScript.Core.Tests/Sizing/PumpSizerTests.cs`.
4. No `.cs` file lives outside `src/` or `tests/`.
5. `dotnet test` from the repository root runs every backend test with no additional arguments (`R-17`).

## Error cases

| Situation | What happens | Why it matters |
|---|---|---|
| A public member lacks an XML doc | Build fails (CS1591 as error) | Documentation debt cannot accumulate silently |
| A `csproj` pins a version inline | Build fails (NU1008) | Central package management is enforced, not encouraged |
| Core gains a UI-layer reference | Architecture test fails in `dotnet test` | Enforces `R-16` from allowed assembly/project references (`D-30`). |

## Worked example

Where a new feature's files land — adding a `three_way_valve`:

| Artifact | Path |
|---|---|
| Component type | `src/FluidScript.Core/Components/ThreeWayValve.cs` |
| Keyword registration | `src/FluidScript.Core/Language/ComponentRegistry.cs` |
| Sizing rule | `src/FluidScript.Core/Sizing/ThreeWayValveSizer.cs` |
| Unit tests | `tests/FluidScript.Core.Tests/Components/ThreeWayValveTests.cs` |
| Sizing tests | `tests/FluidScript.Core.Tests/Sizing/ThreeWayValveSizerTests.cs` |
| Sample script | `samples/three-way-valve-basic.fluid` |
| Declarative symbol | `src/FluidScript.Core/Model/Symbols/ThreeWayValveSymbol.cs` |
| Generic renderer coverage | `frontend/src/features/canvas/symbols/symbolRenderer.test.tsx` |
| Documentation (`R-28`) | `docs/functions/three-way-valve.md` |

Nine files. That list is the definition of "done" for a component, and
[`61-documentation-plan`](../60-docs-and-devex/61-documentation-plan.md) restates the last row as a
hard gate.

## Acceptance criteria

- [ ] `dotnet build` from the root succeeds with zero warnings on an empty solution.
- [ ] `dotnet test` from the root discovers and runs tests in both test projects.
- [ ] `npm run dev` in `frontend/` serves the Vite app.
- [ ] No `Class1.cs`, no root `FluidScript.csproj`, no committed `bin/`/`obj/`/`.vs/`.
- [ ] `git check-ignore` reports `bin/`, `obj/`, `node_modules/`, `.vs/` as ignored.

## Open questions

None. M3 SVG/PNG is client-side from current placements and Core-owned `SymbolDefinition`s. A future
DXF exporter receives placements explicitly and resolves the same declarative symbols; it never grows
a second layout engine (`D-20`, `D-29`). The export project is created only when that M6 item is approved.
