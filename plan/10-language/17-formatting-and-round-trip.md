---
id: 17-formatting-and-round-trip
title: Formatting, printing, and round-trip
tier: 10-language
status: reviewed
owns: [printer, trivia preservation, script mutation API, formatter, write-back edit primitives]
depends_on: [12-grammar, 15-semantic-model]
traces_to: [R-25, R-05, R-45, R-46, R-47]
open_questions: 0
last_review_pass: 6
---

# Formatting, printing, and round-trip

## Purpose

The script is the source of truth (principle P5), so every canvas edit must become a text edit that
changes exactly what the user asked for and nothing else (`R-25`). That requires a printer that is a
perfect inverse of the parser, and a mutation API that edits the tree without reformatting the parts it
did not touch. This is the least glamorous document in tier 10 and the one whose failure is most
visible: a user who changes a valve's Kv and watches their comments get reflowed will not trust the
tool again.

## Responsibilities

**Owns.** The printer, the trivia model's preservation rules, the script-mutation API and its edit
primitives, and the optional formatter.

**Explicitly does not own.** The syntax tree's shape ([`12-grammar`](12-grammar.md)), what the canvas
does with the API ([`54-interaction-and-writeback`](../50-frontend/54-interaction-and-writeback.md)),
undo/redo in the editor ([`52-editor`](../50-frontend/52-editor.md)).

## Two operations, deliberately separate

| | Printer | Formatter |
|---|---|---|
| Guarantee | `Print(Parse(x)) == x`, byte for byte | Produces canonical layout |
| When | Every time text is regenerated | Only when the user asks |
| Changes whitespace | Never | Yes |
| Changes comments | Never | Only their column, and only in aligned regions |

Conflating them is the standard mistake. A printer that "tidies while it prints" makes every write-back
a reformat, and the diff of a one-character change becomes forty lines. The formatter exists because
users will want alignment; it is a command, never a side effect.

## Trivia

Every token carries its leading and trailing trivia, including `D-13`'s `#` comments. The attachment rules are what make round-tripping
deterministic:

1. **Trailing trivia** runs from the end of a token to the end of its line, including a line comment.
2. **Leading trivia** is everything from the previous line's newline up to the token — indentation and
   any full-line comments above it.
3. **A blank line belongs to the following token's leading trivia**, so inserting a statement before a
   blank-line-separated group keeps the blank line where the reader expects it.
4. **Trivia at end of file** attaches to a synthetic end token, so a file ending in comments round-trips.

The `| comment` in the brief's example is trailing trivia of the last token on its line. Column
alignment is therefore preserved automatically as long as the tokens before it do not change width —
and when they do, it is the formatter's job to realign, not the printer's.

## The mutation API

Write-back needs a small set of operations, each of which is a text edit computed from the tree. The
API returns edits rather than new text, so the editor can apply them as a single undoable unit
(`R-25`, and an M5 exit criterion).

```csharp
/// <summary>Produces text edits that change a script without disturbing anything else.</summary>
/// <remarks>
/// Every method returns the edits to apply; none mutates the tree. Applying the returned edits to
/// the source text and re-parsing yields a tree equal to the one the operation described.
/// </remarks>
public interface IScriptEditor
{
    /// <summary>The language major whose syntax and preservation rules this editor applies.</summary>
    LanguageMajor LanguageMajor { get; }

    /// <summary>Sets a parameter on a declared component, adding it if absent.</summary>
    /// <param name="component">The component's identifier as written.</param>
    /// <param name="parameter">Canonical parameter name or a registered script alias.</param>
    /// <param name="value">The value, formatted per the parameter's canonical unit.</param>
    /// <returns>
    /// One edit replacing the existing value, or one inserting <c>name=value</c> at the end of
    /// the declaration's parameter list. Fails for an inferred component, which has no
    /// declaration to edit.
    /// </returns>
    EditResult SetParameter(string component, string parameter, ScriptValue value);

    /// <summary>Removes a parameter, returning it to auto-sized (D-02).</summary>
    EditResult RemoveParameter(string component, string parameter);

    /// <summary>Adds a component declaration.</summary>
    /// <param name="afterComponent">
    /// Insert after this component's declaration; <see langword="null"/> appends to the end of the
    /// declaration section.
    /// </param>
    EditResult AddComponent(string name, string kind, IReadOnlyDictionary<string, ScriptValue> parameters,
                            string? afterComponent);

    /// <summary>Adds a connection line to the connections section.</summary>
    EditResult AddConnection(Endpoint from, Endpoint to);

    /// <summary>Removes a connection. Fails when the connection is part of a chain — see below.</summary>
    EditResult RemoveConnection(Endpoint from, Endpoint to);

    /// <summary>Renames a component and every reference to it.</summary>
    /// <returns>One edit per occurrence, including references inside expressions.</returns>
    EditResult Rename(string oldName, string newName);

    /// <summary>Rewrites derived equipment tags into the script as identifiers (`D-34`).</summary>
    /// <remarks>
    /// The result must still satisfy `D-41`: identifiers are unique across the whole file. Tags
    /// already are, because a tag embeds its circuit number — `101PU01` and `102PU01` cannot collide —
    /// so applying tags to a valid script always yields a valid one. That is a property worth naming
    /// rather than relying on: it is why this operation can rewrite every circuit at once.
    /// </remarks>
    /// <param name="scope">One circuit's name, or null for every circuit in the file.</param>
    /// <remarks>
    /// Component parameters on this interface are unqualified names throughout, and stay that way
    /// under `D-41`: a name identifies one component in the model, so no method needs a circuit
    /// argument.
    /// </remarks>
    /// <returns>
    /// One <see cref="Rename"/>-equivalent edit set per tagged component, with the same old-id/new-id
    /// mapping, so the frontend migrates selection and focus exactly as it does for a typed rename.
    /// Components whose kind has no <c>TagCode</c> are untouched, as are inferred components.
    /// </returns>
    /// <remarks>
    /// <b>Explicit and user-invoked, never automatic.</b> Tags are metadata until someone asks for
    /// this; a binder that wrote them back would renumber identifiers under the cursor on every
    /// insertion, which is the churn `D-34` exists to prevent.
    /// <para>
    /// The operation is not idempotent in the way it first appears: applying it, inserting a pump,
    /// and applying it again renames the components after the insertion point. That is correct, it is
    /// why this is a command rather than a formatting pass, and it is exactly the case the atomic
    /// rename set below exists to permit — the second application's targets are names the set itself
    /// currently holds.
    /// </para>
    /// <para>
    /// <b>The rename set is atomic and self-aware.</b> A target identifier occupied by another member
    /// of the same rename set is legal — that is the ordinary case, since inserting a pump shifts
    /// every later pump's tag onto its neighbour's current name. The operation fails, editing nothing,
    /// only when a target is occupied by a component <i>outside</i> the set.
    /// <para>
    /// Chains and cycles within the set are resolved by computing every replacement against the
    /// original text and applying them simultaneously, never sequentially. Applied one at a time,
    /// renaming <c>PU1</c> to <c>100PU02</c> while <c>100PU02</c> still exists would collide or
    /// silently merge; computed against the original and applied at once, the set permutes cleanly.
    /// Failure to satisfy the outside-the-set condition is <c>FS1604</c>.
    /// </para>
    /// </remarks>
    EditResult ApplyTags(string? scope);

    /// <summary>Promotes an inferred component (I1/I2/I3) into a written declaration.</summary>
    /// <remarks>
    /// The migration path for a user who wants to configure a node the language created for them.
    /// Inserts a declaration using the inferred name, so no connection line changes.
    /// </remarks>
    EditResult Materialize(string inferredName);
}

public sealed record EditResult(ImmutableArray<TextEdit> Edits, ImmutableArray<Diagnostic> Diagnostics);

public sealed record TextEdit(TextSpan Span, string NewText);

public readonly record struct Endpoint(string Component, string? Port);

public abstract record ScriptValue
{
    public sealed record QuantityValue(Quantity Value) : ScriptValue;
    public sealed record SymbolValue(string Symbol) : ScriptValue;
    public sealed record ReferenceValue(string Component, string Property) : ScriptValue;
}

/// <summary>Creates a mutation surface under one supported language major.</summary>
public interface IScriptEditorFactory
{
    IScriptEditor Create(LanguageMajor languageMajor);
}
```

The factory must reject an unsupported major. Every edit is parsed and printed under the editor's
fixed `LanguageMajor`; current application semantics are never applied implicitly to an older file
(`18`, `D-27`). `ScriptValue` covers quantity-, symbol-, and reference-valued registry parameters, so
write-back can set `characteristic=equal_percentage` as well as `kv=12.4`.

### Formatting a value for insertion

`SetParameter` receives a `Quantity` in SI and must produce script text. The rules:

1. Convert to the parameter's canonical unit (`D-07`).
2. Round to the parameter's display precision, declared alongside its range in the registry.
3. Emit **bare** if the value is in the canonical unit; emit with an explicit unit only if the user's
   existing value had one — matching what they wrote (P5).
4. Never emit more precision than the sizing produced. `kv=12.4` not `kv=12.40000000000001`.

Rule 3 is why `ParameterValue` retains the original `ExpressionSyntax`: the editor reads the old form
to decide the new one's shape.

The same preservation rule applies to registered aliases and `D-32`'s tank spellings. `SetParameter("T1", "volume", ...)` edits
the existing value in `T1 tank v=300`; it does not replace `v` with `volume` or append a second
assignment. If the parameter is absent, insertion uses the canonical name (`volume`). Removal accepts
either spelling and removes the one written assignment. Kind aliases such as `container` are likewise
preserved by unrelated edits; only an explicit migration may canonicalize them. The binder-provided
`ParameterValue.WrittenName` from [`15-semantic-model`](15-semantic-model.md) is authoritative, so
write-back does not rediscover aliases by string heuristics.

### Editing an expression is not supported

`SetParameter` on a parameter whose current value is an expression (`head=1.2*HE1.dp`) **replaces the
whole expression with a literal** and emits `FS1601` (warning) saying so. The alternative — trying to
adjust one operand of an expression the user wrote — has no correct answer. Warning and replacing is
honest; silently replacing is not.

### The chained-connection problem

`A - B - C` is one statement and two connections. `RemoveConnection(A, B)` must rewrite the line to
`B - C`, and `RemoveConnection(B, C)` to `A - B`. Removing the middle of `A - B - C - D` must split
one line into two. This is the one genuinely fiddly case; it is called out here so it is implemented
deliberately rather than discovered.

## Invariants

1. **`Print(Parse(x)) == x` byte for byte**, for every input, including malformed ones.
2. `Parse(Apply(Print(t), edits))` is well formed for every `EditResult` any method returns.
3. An `EditResult` touches only spans belonging to the element named; a test asserts every other byte
   of the file is unchanged.
4. `SetParameter` on an inferred component returns no edits and one diagnostic — it never invents a
   declaration silently.
5. `Rename` updates every reference, including inside expressions and connection endpoints; a test
   asserts the count of occurrences before and after.
6. Applying an `EditResult`'s edits in any order gives the same result — spans within one result never
   overlap.
7. The formatter is idempotent: `Format(Format(x)) == Format(x)`.
8. Editing a parameter through its canonical name or alias preserves the spelling already present;
   insertion uses the canonical name and never creates canonical-plus-alias duplicates.
9. A circuit header whose number was resolved rather than written prints without a number (`D-33`).
   `CircuitSymbol.NumberIsExplicit` is what invariant 1 depends on here: printing a resolved number
   would make `Print(Parse(x)) != x` for every single-circuit script in existence.
10. `ApplyTags` either renames every eligible component in its scope or edits nothing; its
    old-id/new-id mapping covers exactly the components it renamed; and its replacements are computed
    against the original text and applied simultaneously, so a rename set that permutes existing names
    succeeds.

**Invariant 9 is the round trip's newest sharp edge.** The binder assigns every circuit a number, so
the obvious printer reads `CircuitSymbol.Number` and writes it — which quietly rewrites
`circuit coolingLoop` as `circuit coolingLoop 100` the first time anything touches the file. The
printer must therefore print from the *syntax tree*, where the number is absent, not from the bound
model, where `D-33` guarantees it never is. This is the same rule that already keeps aliases and written parameter
spellings intact, applied to a value the binder invents rather than one it normalises.
Invariant 3 is the one users actually feel, and it is the reason to write it as a test that diffs the
whole file rather than asserting on the changed line.

## Error cases

| Code | Trigger | Severity | Message shape |
|---|---|---|---|
| `FS1601` | `SetParameter` replacing an expression with a literal | Warning | `Replacing '{expr}' with {value}.` |
| `FS1604` | `ApplyTags` target identifier is held by a component outside the rename set | Error | `Cannot apply tags: '{tag}' is already the name of '{component}'. Rename it first.` |
| `FS1602` | Editing an inferred component | Error | `'{name}' was added automatically and has no line to edit. Write it into the script first.` |
| `FS1603` | `Rename` to an existing name | Error | `'{new}' is already used at line {n}.` |
| `FS1604` | `Rename` to a name that lexes as a quantity | Error | `'{new}' reads as a quantity. Try '{suggestion}'.` |
| `FS1605` | `RemoveConnection` for a connection that is not present | Error | `There is no connection from '{a}' to '{b}'.` |
| `FS1606` | An edit would produce an unparseable script | Error | `Internal: this edit would break the script. No change made.` |

`FS1606` is a guard, not a user-facing expectation: every method validates its own output by
re-parsing before returning. It costs a parse per edit — microseconds on scripts of this size — and it
makes invariant 2 enforced rather than hoped for.

## Worked example

The user drags `3WV`'s Kv to 12.4 on the canvas. Source before:

```fluidscript
HE1 heat_exchanger power=30 in=20 out=50    # heat exchanger with power of 30 kW
3WV three_way_valve                # auto size
PU1 pump                    # auto size by pressure difference in loop
```

`SetParameter("3WV", "kv", 12.4 m³/h)`:

1. Find `3WV`'s `ComponentDeclarationSyntax`. Its `Parameters` is empty; its `Kind` token ends at
   **offset 100** — line 1 is 80 characters plus a newline, and `3WV three_way_valve` is 19 more —
   and its trailing trivia, `                | auto size`, begins there.
2. `kv` is absent, so this is an insertion at the end of the parameter list: **immediately after the
   kind token**, before the trailing trivia.
3. Format the value: Kv's canonical form has no unit symbol, display precision 1 decimal → `12.4`.
4. Emit one `TextEdit`: insert `" kv=12.4"` at offset 100.

Result:

```fluidscript
HE1 heat_exchanger power=30 in=20 out=50    # heat exchanger with power of 30 kW
3WV three_way_valve kv=12.4                # auto size
PU1 pump                    # auto size by pressure difference in loop
```

Every other byte is identical — including the two comment columns, which are now misaligned. **That is
correct behaviour.** Realigning them would change lines the user did not touch, and the diff would
claim three lines changed when one did. If the user wants alignment they run the formatter, which is
their choice and one undo step.

Then `RemoveParameter("3WV", "kv")` returns the value to auto-sized: one edit deleting `" kv=12.4"`,
restoring the file byte for byte to the original. That symmetry is invariant 3 doing its job, and it is
worth a dedicated test.

## Acceptance criteria

- [ ] Round-trip over the whole `samples/` corpus: `Print(Parse(x)) == x` byte for byte, malformed
      samples included.
- [ ] Idempotence: `Print(Parse(Print(Parse(x)))) == Print(Parse(x))`.
- [ ] The worked example produces exactly one `TextEdit`, of exactly the text shown and at exactly the
      offset shown — the offset is part of the assertion, since an edit at the wrong offset still
      produces plausible-looking output.
- [ ] Set-then-remove of a parameter restores the file byte for byte.
- [ ] Every `IScriptEditor` method has a test asserting the whole file is unchanged apart from the
      intended span.
- [ ] Removing the middle connection of a four-endpoint chain produces two well-formed lines.
- [ ] `Rename` across a script containing the name in a declaration, a connection, and an expression
      updates all three.
- [ ] Formatter idempotence over the corpus.
- [ ] On `T1 container v=300 layers=5`, setting canonical `volume` changes only `300`, removing `v`
      removes that assignment, and setting it again inserts exactly one canonical `volume=` while
      preserving `container`.

## Open questions

None. Format aligns comments/assignments only within consecutive non-blank declaration runs, so one
long line cannot reflow a section. Write-back preserves the existing unit token and scale exactly;
canonical-unit selection applies only when inserting a parameter that has no prior unit (`D-30`).
