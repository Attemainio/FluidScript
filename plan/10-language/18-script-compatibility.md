---
id: 18-script-compatibility
title: Script compatibility and migration
tier: 10-language
status: reviewed
owns: [language version directive, catalogue pin syntax, compatibility policy, migration contract]
depends_on: [01-vision-and-scope, 06-decision-log, 12-grammar, 16-diagnostics, 17-formatting-and-round-trip]
traces_to: [R-01, R-05, R-30, R-38, R-39, R-45, R-46, R-49]
open_questions: 0
last_review_pass: 6
---

# Script compatibility and migration

## Purpose

Implements `D-27`'s durable-versioned-file rule. Opening a file selects known semantics before
binding; it never silently interprets old text as the newest language.

## Responsibilities

**Owns.** The `fluidscript` directive, catalogue pin, supported-version policy, and explicit migration.

**Explicitly does not own.** General grammar (`12`), byte-preserving edits (`17`), catalogue contents
(`27`), file-picker lifecycle (`58`), or model/API schema versions (`26`, `42`, `43`).

## Contracts

The first non-trivia line of every durable `.fluid` file is:

```fluidscript
fluidscript 1
catalog steel_en10255@2026.1
```

`fluidscript` is followed by one unsigned decimal major. `catalog` is optional and followed by one
ASCII catalogue id and an optional `@major.minor` exact version. An unversioned named catalogue uses
the application's shipped version and records it in provenance; adding a second id is an error, not a
preference list. Neither directive accepts expressions. A BOM, blank lines, and comments may precede
the version directive; no model statement may precede it.

```csharp
public readonly record struct LanguageMajor(int Value);
public readonly record struct MigrationId(string Value);
public readonly record struct SourceHash(string Value);
public sealed record CatalogPin(string Id, string? Version);

public enum CompatibilityDisposition
{
    Current, SupportedOld, UnsupportedNewer, UnsupportedOld, UnversionedDraft
}

public enum CompatibilityAction { Compile, Solve, Save, SaveAsBytes, PreviewMigration }

public sealed record CompatibilityResult(
    LanguageMajor? DetectedMajor,
    CatalogPin? Catalog,
    CompatibilityDisposition Disposition,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<CompatibilityAction> AllowedActions);

public sealed record SemanticChangeNote(string Code, string Message, TextSpan? SourceSpan);

public sealed record MigrationPreview(
    MigrationId MigrationId,
    SourceHash SourceHash,
    LanguageMajor SourceMajor,
    LanguageMajor TargetMajor,
    ImmutableArray<TextEdit> Edits,
    ImmutableArray<SemanticChangeNote> SemanticChanges,
    ImmutableArray<Diagnostic> DiagnosticsBefore,
    ImmutableArray<Diagnostic> DiagnosticsAfter,
    bool RequiresUserReview);

CompatibilityResult Inspect(SourceText source, SupportedVersions supported);
MigrationPreview PreviewMigration(SourceText source, LanguageMajor target);
SourceText ApplyMigration(SourceText source, MigrationId id, SourceHash expectedHash);
```

**`Inspect` reads the text, not a syntax tree.** Invariant 2 puts version selection before parse and
bind, so it cannot ask the parser which major to parse under without asking the question it exists to
answer. It scans past a BOM, blank lines and comments to the first line that says anything, and
matches `fluidscript` followed by one unsigned decimal — a prefix that is fixed across majors by
construction, since a major that changed how its own version line is spelled could not be detected by
any application that did not already know its version.

`MigrationId` is created by `PreviewMigration` and returned as `MigrationPreview.MigrationId`; it is
valid only for that preview's source hash and target major. `ApplyMigration` rejects an unknown id or
an `expectedHash` different from the preview and never searches for a compatible preview implicitly.

### Policy (`D-27`)

- New and saved files use the current major; v1 is `fluidscript 1`.
- Unsaved editor text without a directive is a recoverable `unversioned-draft`: parse with current
  semantics and show `FS1701`. Save is disabled until the user accepts insertion of the directive.
- A supported older major is parsed under that major's grammar and binding semantics. It is not
  rewritten on open.
- A newer or unsupported older major is viewable as text but cannot compile, solve, overwrite, or
  migrate. Save As may preserve the bytes unchanged.
- Migration is a named, deterministic transformation. The UI shows the diff and semantic notes; the
  user explicitly applies it. Apply rejects when the source hash changed since preview.
- Language, model-contract, API, application, and catalogue versions are independent identifiers.
- A missing pinned catalogue uses the application's declared default and records its exact version in
  solved/exported metadata. Reopening may warn that the default changed; it never changes an active
  run snapshot.

Backward-compatible additions may ship within major 1. Removing syntax, changing bare-unit meaning,
renaming a kind/parameter without an alias, **adding a reserved word**, or changing inference
semantics requires a new language major and migration. Diagnostic wording and editor completion do not.

**Adding a reserved word is a removal, not an addition**, which is why it belongs in that list rather
than in the sentence above it. A word that was a legal identifier stops being one: a script that named
a component `control` parsed before and fails with `FS1004` after. The addition looks purely additive
from the grammar's side, and that is exactly what makes it easy to ship by mistake.

The pre-release exemption is the same one `D-32` relies on below: **until a v1 file can be saved, the
reserved list may grow freely.** `D-33`, `D-37` and `D-40` added `project`, `spacing`, `supply`,
`return` and `control` under that exemption. Afterwards, growing the list requires a new major and a
migration that renames colliding identifiers — which is mechanical, since the migration knows both the
old and new reserved sets and every identifier's span.

A sweep of this repository's own samples is **not** evidence that an addition is safe. It shows the
change is safe for files we wrote; the files that matter are the ones users wrote, which no sweep can
see. Before v1 ships, that gap does not exist because those files do not exist yet — and that, not the
sweep, is the argument.

`D-32` establishes the initial v1 semantics before release: a bare tank `volume`/`v` value is dm³,
`tank` has the `container` kind alias, and the omitted defaults are 300 dm³ and five layers. Once a
v1 file can be saved, changing that bare unit, either default, alias resolution, indexed-port
materialization, or elevation-to-layer mapping is a semantic breaking change and therefore requires a
new language major and an explicit migration. Adding a new optional tank parameter or a further alias
without changing existing binding remains backward compatible.

## Invariants

1. Inspecting or opening source never mutates it.
2. Semantics are selected before parse/bind and never inferred from application release number.
3. A migration applies only to the exact previewed source hash and is undoable as one editor action.
4. The round-trip invariant applies separately under every supported language major.
5. Saved engineering results record the language major and resolved catalogue version.
6. A supported v1 tank is always rebound with the v1 dm³/default/port/elevation semantics, regardless
   of the current application's newer tank model.

## Error cases

| Code | Condition | Result |
|---|---|---|
| `FS1701` | Unsaved draft has no `fluidscript` directive | Info with quick action to insert current major; no durable save until resolved |
| `FS1702` | Unsupported newer/older major | Error; read-only text, no compile or overwrite |
| `FS1703` | Pinned catalogue is absent or unsupported | Error; no sizing or solve |
| `FS1704` | Source changed after migration preview | Error; discard preview and recompute |
| `FS1705` | The file states more than one language **major** | Error; disposition is unsupported, and only `SaveAsBytes` is allowed |

**`FS1705` was two codes for one trigger, and is now narrower.** It was specified as "version or
catalogue directive is misplaced or duplicated" — which is exactly [`12-grammar`](12-grammar.md)'s
`FS1112`, "a file-wide directive after the first `circuit`, or a second of either", already registered
and already firing. A misplaced line is a *grammar* error: the statement is in the wrong place, and
`D-53` puts a code in the range that names its subject. What only compatibility can judge is a file
whose directives name **different majors** — the parser sees two well-formed statements, and the gate
cannot select semantics from them. That is `FS1705`'s trigger. Two directives naming the *same* major
is an ordinary duplicate and stays `FS1112`.

The narrowing is not a redefinition of the kind [`16-diagnostics`](16-diagnostics.md)'s invariant 7
forbids: `FS1705` had never been registered or raised, and this table was its only reference.

The policy implements `D-27`: version selection precedes parsing, opening never rewrites, and every
migration remains explicit and previewable.

## Worked example

Opening `fluidscript 1` in an application whose current language is 2 parses with v1 semantics and
offers “Preview migration to 2”. The preview changes `pressure=3 bar` only if v2 defines a required
explicit spelling, explains the gauge/absolute effect, and shows before/after diagnostics. Clicking
Cancel leaves every byte unchanged.

## Acceptance criteria

- [ ] Every saved sample begins with `fluidscript 1` and parses under major 1.
- [ ] Each supported major has parser, binder, and byte-round-trip fixtures.
- [ ] Unsupported versions never reach sizing or solve and are never overwritten accidentally.
- [ ] Migration preview/apply is deterministic, hash-guarded, diffable, and one-step undoable.
- [ ] Pinned and unpinned catalogue behavior records the exact resolved version.
- [ ] A v1 fixture containing `T1 container v=300 layers=5` retains the same canonical model and port
      map under every application release that still supports language 1.

## Open questions

None.
