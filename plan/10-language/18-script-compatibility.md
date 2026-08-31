---
id: 18-script-compatibility
title: Script compatibility and migration
tier: 10-language
status: draft
owns: [language version directive, catalogue pin syntax, compatibility policy, migration contract]
depends_on: [01-vision-and-scope, 06-decision-log, 12-grammar, 16-diagnostics, 17-formatting-and-round-trip]
traces_to: [R-01, R-05, R-30, R-38, R-39, R-45]
open_questions: 0
last_review_pass: 0
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
renaming a kind/parameter without an alias, or changing inference semantics requires a new language
major and migration. Diagnostic wording and editor completion do not.

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
| `FS1705` | Version/catalogue directive is misplaced or duplicated | Error at directive; recover remaining syntax without guessing precedence |

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
