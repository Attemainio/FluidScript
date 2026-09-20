# dotnet-toolkit: observations from the 2026-09-19 defect sweep

Written after Tier 2 of the defect sweep, a session that put roughly thirty `validate_patch` calls
through the toolkit -- from one-line edits to a 20-file signature change (`C-39`) -- plus the
usual `get_symbol` / `get_references` navigation. The request was to note anything in the tool
responses that is unnecessary. Everything below is from responses actually received this session;
nothing is inferred from the plugin's source, which was not read.

## 1. Defects (things that produced wrong output)

### 1.1 `newText` ending in a newline inserts a stray blank line

Every edit whose `newText` ended in `\n` left an extra empty line after the replaced range. A
one-line replacement of `lines: "175-175"` with `"    foo,\n"` produced `    foo,` followed by a
blank line. The 20-file `C-39` patch left 33 of them -- inside argument lists, between a method's
signature and its `{`, before a closing `}`, and as double blanks between members. The compile
ladder and 481 analyzers passed, so nothing reported it; it was found by noticing the draft's
`lineCount` growing by one per edit and then diffing.

Replacing `N-(N+1)` with the next line's text *without* a trailing newline removed them cleanly, so
the fix on the caller's side is "never end `newText` with a newline". On the tool's side, either
strip one trailing newline from `newText` (an edit is line-based; the range's own line ending is
kept anyway) or state the rule in the parameter description.

### 1.2 Adding edits to an existing `draftId` mis-anchors them

After a first batch was held as a draft (`unheld_symbol`), a second call with the same `draftId`
and new edits at original-file line numbers inserted the new text *next to* the old lines rather
than replacing them: `PipelineTimingDiagnostics.cs` gained `private void MeasureStep(string path,
ResolvedCatalog<PipeSpec> catalog)` directly under the original `ICatalog<PipeSpec>` signature and
failed parse with `CS1513 } expected`. It is unclear whether line numbers in a follow-up are meant
to be against the original file or the draft; the response does not say, and neither reading
produced a correct result. Rebuilding the whole patch from scratch in one submission worked. If
follow-up edits on a draft are supported, the contract for their line numbers needs stating; if
not, the call should be refused rather than applied wrongly.

### 1.3 Knowledge cache corruption is chronic

`.claude/dotnet-toolkit/cache/` holds 14 `knowledge.db.corrupt-*` files (2026-09-18 and -19), nine
of them from this one session, 20-24 MB each, in a directory whose `.gitignore` is `*` so nothing
prunes them. Each corruption presumably costs a reindex; the stale-cache symptom
(`symbol_not_found` for a fresh id, `stale_base` after an apply) was seen earlier in the session.
Whatever is corrupting the SQLite file -- concurrent writers, the hook process and the MCP server
sharing it, WSL on a `/mnt/c` path -- the corrupt copies should at least be capped or deleted after
the next successful rebuild.

## 2. Round trips that could be one call

### 2.1 `unheld_symbol` is a guaranteed second call

Every patch that touches a method body returns `unheld_symbol` with the current versions and asks
for the same `draftId` back with those versions in `baseVersions` and `edits: []`. The tool already
has the versions; the caller has nothing to add but a copy of them. For the `C-39` patch this took
three rounds (32 symbols, then +5, then +2) because each resubmission pulled in symbols the previous
one had not listed. A patch whose edits are being submitted for the first time cannot be stale
against anything, so an `acceptCurrentVersions: true` (or treating an absent `baseVersions` as
"current") would remove the round trip without losing the protection for a caller who *does* hold
old versions.

### 2.2 The diagnostic locations are capped where the inspection list is not

`CS1503: 25 occurrence(s)` showed `locations[3]` and `suggestedInspection[24]`. The three locations
were what I needed (file:line of each caller to fix); I had to `grep` for the other 22. The
inspection list was the enclosing symbols' display strings, which are one indirection away from a
line number. Inverting the caps -- all locations, a short inspection list -- would let a dependent
break be fixed from the response alone.

## 3. Response content that is noise on a successful call

A successful `validate_patch` on the 20-file patch returned ~230 lines. What was used from it:
`succeeded`, `applied`, and (on failure) `diagnostics.rootCauses[].locations`. What was not:

- **`detectedChanges[]` in full.** One entry per touched symbol with `changeKinds`, `oldVersion`,
  `newVersion`, `apiImpact`, `declarationSites{file,startLine,endLine,signatureLine}`. Forty
  entries for `C-39`. Two symbols appeared twice with identical content (`sym_4d250923ce211da9`,
  `sym_f60d5254645b3edc`). A count per `apiImpact` and the list of `breaking-public` symbols would
  carry the same information in five lines; the per-symbol detail could be behind a flag.
- **`apiImpact: breaking-public` on non-breaking edits.** Changing the message template string
  inside `TopologyDiagnostics.OverSpecified`'s initializer was reported as `changeKinds: signature,
  apiImpact: breaking-public`. A property initializer is not a signature. Similarly a test class's
  private helper going from `ICatalog` to `ResolvedCatalog` is `breaking-internal`, which is true
  and irrelevant inside a test project.
- **`notAssessed[]` boilerplate.** The same two sentences on every response: *"analyzers covered N
  changed document(s); analyzer findings in files this patch did not touch are not assessed"* and
  *"N analyzer suggestion(s) sit on lines this patch did not change and are not reported; they are
  pre-existing, not consequences of it"*. Once per session would do; on a per-call basis a
  `preExistingSuggestions: 7` field says it.
- **`checks.levels[]` with `durationMs` and `scope`.** Useful when a level fails or is slow;
  otherwise `completedLevel: dependent_compile` in the ladder block already says all four passed.
- **`ladder.nextAction`** and **`fixHint`** are generic strings (*"Fetch the suggested symbols,
  revise the patch, and resubmit."*, *"Inspect the listed symbols and reconcile them with the
  change."*) that carry no information specific to the failure.
- **`draft.expiresAt`.** Never mattered; a draft was always resubmitted within a minute.
- **`draft.files[]{file,lineCount}`** -- keep this one. `lineCount` is how defect 1.1 was found.

A compact success response could be: `succeeded, applied, files changed (n), symbols changed (n:
k breaking-public, listed), analyzers clean, pre-existing suggestions (n)`. Everything else on
request or on failure.

## 4. What worked and should stay

- The compile ladder caught every dependent break of the `C-39` signature change across four
  projects before anything was written to disk, and the 20-file patch applied atomically.
- Analyzers running on the changed documents only, with the pre-existing count separated out.
- `applyOnSuccess: true` with a held draft on failure: nothing half-applied, ever.
- The `.cs` edit guard. It forced the 33 blank-line removals through `validate_patch` too, which
  was tedious, but a formatting-only bypass would also be a bypass for everything else.

## 5. Session-wide numbers (from the transcript, all context windows)

| Tool | Calls | Response chars | Notes |
|---|---|---|---|
| `validate_patch` | 382 | 588 K | 197 succeeded (52 %); 96 `unheld_symbol` (25 %); 47 failed validation; 19 `stale_base`; 12 `symbol_not_found`; 12 other |
| `get_symbol` | 262 | 693 K | 74 with `source: code`, 28 with `source: full` on symbol lists (one response 28 K chars); 7 `symbol_not_found` |
| `search_index` | 48 | 115 K | |
| `rename_symbol` | 11 | 21 K | 1 `symbol_not_found` |
| `get_references` | 5 | 8 K | |
| `workspace_status` / `reload_workspace` | 14 / 7 | 9 K | the reloads were cache-corruption recoveries |

Three findings follow from the numbers rather than from any single response:

- **A third of `validate_patch` calls carried no edit.** 96 `unheld_symbol` + 19 `stale_base` + 12
  `symbol_not_found` = 127 calls (33 %) that resubmitted a draft or re-fetched a version. Add the 47
  validation failures and fewer than half the calls did what they were sent to do. Successful
  responses averaged 38 lines.
- **`get_symbol` is being used as a line-numbered file reader.** Because an edit is anchored by
  line number, every patch needs the *current* line numbers of its targets, so a `get_symbol`
  (`source: code`) or a `grep -n` precedes it -- and after any apply that shifts lines, again. That
  is where most of the 693 K chars went. A string-anchored edit needs none of it: the anchor is
  the text itself, which the caller has already read.
- **`stale_base` 19 times** in a single-writer session. Nothing else was editing; the version went
  stale because the previous patch in the same session moved it and the caller's copy was from
  before. In a sequential workflow the pin protects against the caller's own last write.

## 6. Is `validate_patch` worth keeping?

Asked directly by the user after this report. The honest answer for this repository is no.

What it buys over `Edit` (string-anchored replacement) followed by `dotnet build` (6.6 s here,
incremental, `TreatWarningsAsErrors` on):

| Property | `validate_patch` | `Edit` + `dotnet build` |
|---|---|---|
| Dependent breaks caught | Before writing | After writing; same errors, *all* locations rather than three |
| Analyzers | Changed documents | Whole solution; already the gate |
| Rollback | Nothing written | `git checkout -- <file>` |
| Multi-file atomicity | Yes | No; the build at the end is the gate either way |
| Stale-version protection | `baseVersions` | None; matters only with two writers on one file |
| New files | Cannot create one | `Write` |
| **Edit anchoring** | **Line numbers** | **Exact strings** |

The last row decides it. A model can see a string; it cannot reliably count lines. Nearly every
failed round this session was anchoring -- a range one line short of the `{`, one line past the
`while`, `371` for `372` -- a class of error `Edit` does not have. On top of that sit 1.1 and 1.2,
the `unheld_symbol` round trip, ~38 lines of response per success and the guard routing even a
blank-line removal through the same path.

The one time the ladder "saved" anything -- `C-39`'s 25 dependent errors -- `dotnet build` would
have produced the identical list, uncapped, thirty seconds later, with the same fix. Checking
"would this compile" without touching disk is a real capability, but git makes disk cheap and the
need never arose.

**Keep:** `search_index`, `get_symbol` (as a symbol reader, not a file reader), `get_references`,
the call and type hierarchies, `get_semantic_diff`, `workspace_status` -- dispatch-aware navigation
is what grep cannot do and is the reason `CLAUDE.md` routes C# through the toolkit. Caveat from this
session: the `C-39` call sites were found with one `grep -n` because a constructor call is textual;
navigation earns its keep on interface and virtual dispatch, not on `new Foo(`. Keep
`rename_symbol` too: a Roslyn rename is something `Edit` cannot do safely.

**Replace with:** let `Edit`/`Write` through on `.cs`; keep a PostToolUse hook that runs
`dotnet build <touched project> --no-restore` after a `.cs` write and surfaces the errors -- the
compile ladder at zero protocol cost. Drop drafts, version pinning and the symbol-change report.
Keep blocking Bash writes (`sed`, heredocs) to `.cs`, so edits stay string-anchored and reviewable;
that guard is the part of the current design that pulls its weight.

**Where the answer flips:** parallel subagents writing C# into one solution. Then the version pin
and the atomic apply do real work, and the right move is to keep `validate_patch`, fix 1.1 and 1.2,
and make `unheld_symbol` accept current versions on a fresh patch. This project runs sequentially.

## 7. Summary for the plugin author

| # | Kind | Item | Cost this session |
|---|---|---|---|
| 1.1 | defect | trailing `\n` in `newText` inserts a blank line | 33 stray lines, one extra 33-edit patch |
| 1.2 | defect | follow-up edits on a `draftId` mis-anchor | one failed round, patch rebuilt from scratch |
| 1.3 | defect | knowledge.db corrupts repeatedly; corrupt copies accumulate | 14 files, ~300 MB, reindexes |
| 2.1 | round trip | `unheld_symbol` on every fresh body edit | 1-3 extra calls per patch |
| 2.2 | round trip | diagnostic locations capped at 3, inspection list at 24 | a grep per dependent break |
| 3 | noise | `detectedChanges` in full, boilerplate `notAssessed`, generic hints, `expiresAt` | ~200 lines per success |
| 5 | structural | a third of `validate_patch` calls carry no edit; `get_symbol` used as a file reader for line numbers | 127 protocol calls, ~700 K chars |
| 6 | design | line-anchored edits are the root of most failures; `Edit` + build hook covers the rest | recommendation: keep the explorer, drop the patcher |

## 8. Addendum from tiers 3–4 (later the same day: `S-29`, `S-63`, `S-68`, `C-104`)

Roughly forty more `validate_patch` calls and a hundred `get_symbol` calls. Everything above held;
what follows is only what is new. Same standard: observed in responses, not inferred from source.

### 8.1 `unleased_body`: a third pre-flight refusal, and it contradicts the tool's own advice

A `get_symbol` without `source` returns `contentVersion: "decl:…"` with no body layer. A patch that
then edits the body is refused with `unleased_body` and told to *"refetch with an include that
serves the body (source, bodyOutline or mechanicalFacts)"*. Two things: the refusal's own response
already carries the full version (`current[]{symbolId, "decl:…|body:…"}`), so the refetch is a copy
of what was just returned -- same shape as 2.1; and `source: bodyOutline` is refused by `get_symbol`
(`invalid_component: … Modes: full (default …) or code`), so one of the three suggested includes does
not exist. Seen four times; each cost one round.

### 8.2 `stale_base` on the containing type after any edit to a sibling member

Adding a member needs the type's version in `baseVersions` (2.1). That version changes whenever any
member of the type is edited -- including by the previous patch in the same session -- so a second
addition to the same type is refused `stale_base` with the new version, and the patch is resent
unchanged. Seen five times (`ValveSizerTests`, `SolutionSeedTests`, `OuterLoopTests`,
`ComponentRegistryTests`, `ValveLaw`). For a fresh addition the type's version proves nothing:
there is no old text being overwritten. The pin should apply to replaced ranges, not to the
anchor of an insertion.

### 8.3 `symbol_not_found` for an id the same response listed

`get_symbol` on `sym_60f164cf64292f8a` -- one of two candidates an `ambiguous_symbol` response had
just returned -- failed `symbol_not_found`; `FluidScript.Core.Sizing.BranchResistance` by full name
also failed while `…BranchResistance.Along` resolved to the ambiguity. This is 1.3's stale cache;
the workaround was reading the file with `sed`. An id returned by the tool should resolve for at
least the life of the response.

### 8.4 `search_index` finds neither members nor strings, and rejects its `kinds` values

- `search_index("LoopDrop")` returned `items[0]` for a property declared in `ISizer.cs` and read in
  three files. `grep` found four hits.
- `search_index("sized to zero head because its circuit contains no modelled resistance")` returned
  `termsWithNoHits` for every word of a string literal in `SizingDiagnostics.cs`. Whether string
  contents are indexed at all is not stated anywhere I found; if not, the response should say so
  rather than report each common English word as unindexed.
- `kinds: ["Method","Property","Field"]` was answered `kindsHint: "… matched no symbol kind"`. The
  parameter's accepted values are not in its description.

### 8.5 Diagnostic locations point at the wrong operand for `CS1620`

`string.Create(culture, $"…{a}…" + $"…{b}…" + "plain literal")` fails `CS1620: Argument 2 must be
passed with the 'ref' keyword` because one operand of the concatenation is not interpolated and the
handler falls back. The reported location was the *interpolated* line, two lines above the literal
that caused it. Not a toolkit defect, but the toolkit is what surfaces it, and `ExchangerSizer.cs`
already carries a comment about the ternary form of the same trap. Worth a `fixHint` that names it
when `CS1620` appears on a `string.Create` argument.

### 8.6 Range edits at a member's end: the closing brace

Eight failed rounds this session were the same mistake: a range that ended one line before the
member's `}` (leaving a duplicate), or that included a blank separator line after it (dropping the
gap). The `@a-b` compact source view and `declarationSites.endLine` both give a line number, but
neither says whether `endLine` *is* the brace or the last statement. For inserting after a member,
the natural anchor is "after this symbol", not a line: an `insertAfter: sym_…` / `insertBefore`
edit form would remove the whole class of error. (§6's string-anchored `Edit` also removes it.)

### 8.7 Numbers for tiers 3–4

| Response | Count | Share |
|---|---|---|
| `validate_patch` succeeded and applied | 31 | 55 % |
| `unheld_symbol` / `unleased_body` / `stale_base` (no edit carried) | 14 | 25 % |
| validation failed (all but two were 8.6 or 8.5) | 11 | 20 % |

`get_symbol` ~100 calls; three `symbol_not_found` (8.3); one `invalid_component` (8.1). The
knowledge cache corrupted once more (`knowledge.db.corrupt-*` now 15 files).
