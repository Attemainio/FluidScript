---
id: 09-project-state
title: Project state
tier: plan
status: living
owns: [which phase the project is in, which work packages have shipped and in what order, which defect records each phase closed, what the next package is]
depends_on: [08-implementation-sequence]
traces_to: []
open_questions: 0
last_review_pass: 0
---

# Project state

## Purpose

[`08-implementation-sequence`](08-implementation-sequence.md) says what the work is and in what order
it must be done. It is written entirely in the future tense and deliberately never changes as work
lands, because it is a plan and a plan that edits itself to match what happened stops being one.

This file is the other half: **what has actually happened, in what order, and where that leaves the
next session.** A session that reads `08` alone knows the whole map and not its own position on it.

It exists because context does not survive. This project has been built across many sessions, several
of them compacted, and everything a session learns that is not written into `plan/` or `/docs` is gone
when it ends. The commit log records *changes*; it does not record *state*, and reconstructing "which
phase are we in" from 141 commits costs a session's attention before any work starts.

## What this file is not

**It is not a defect list.** Every open question lives in the `defects.md` of the tier that owns it,
and that file is the only place it is described. This one carries the *count* and the *pointer*, so
that a number here going stale is visible rather than a description here disagreeing with the one that
matters.

**It does not restate closed defects either.** When a package completes it records which ids that
package closed, and nothing about them. The reasoning is in the tier's Closed table, written from the
beginning as `CLAUDE.md` requires, and copying a summary here would produce a second account that
drifts from the first.

**Closure attribution starts from this file, and is not reconstructed backwards.** P0 through P3.7
shipped before this record existed, and inferring which package closed which id from commit subjects
would put guesses in the one place a future session trusts. Those closures are attributed where they
already are: most Closed entries name their package in their own text — "fixed in `P3.4c`", "P2.8
closed all five" — and that is the record for everything before `0f8985e`.

**It is not a changelog.** A commit per line would be the log again. What is recorded is the package —
one branch, one merge, one closed verification — and the defect ids it moved.

**It does not follow [`_template.md`](_template.md).** Every other document in `plan/` is a contract
with Purpose · Responsibilities · Contracts · Invariants · Error cases · Worked example · Acceptance
criteria · Open questions. This is a record of work, which has none of those; the template's shape
would be filled with nothing.

## Where the project stands

> **M2a exited 2026-09-14; M2b exited 2026-09-15** — P4.1 (`c275bd9`), P4.2 (`bdf78f6`), P4.3
> (`17fbe3a`) — on the user's call, with one criterion deferred rather than ticked: `400HP01` needs
> a `heat_pump` kind, which is `D-80`'s and M4's; the ownership rule it tests is built and checked
> on a `chiller`. The audit of open defects before `P5.1` closed `C-4` (already met by P4.1),
> `L-36` (a `13` correction) and `C-67` — the last with `FS2119`, which found seventeen test
> fixtures and four syntax-tour lines writing a cooling load as a positive neutral duty.
> **P5 — M3, the usable static product — is in progress: P5.1a–c (layout hints, the model contract,
> symbols and the payload baseline) and P5.1d-1 (the layout solver in Core, `D-103`, with named
> styles, `D-104`) shipped 2026-09-15 and was taken to the user's pictures on 2026-09-16 (`D-105`,
> the router, the audit); P5.1d-2 built the rule-based engine of `D-106` whole on 2026-09-16, and
> the same day the user judged its pictures, the plan was re-evaluated and the engine restarted
> from an empty rule set against a ladder of scripts (`D-107`, `28` rewritten in four parts, `29`
> the step log). Step 1 -- one pump -- was drawn and corrected the same day: the user's four
> directions are `D-108` (heat left to right and loops clockwise as hard constraints H9/H10, a
> transform class per kind with exchangers and tanks mirrored never turned, every node laid out
> with its boundaries, the layout checked from its text), written into `28` as C1–C4 and A4/A6/A10
> and into `29` as a ten-step plan that reaches every sample; `C-88`–`C-90` filed for what the
> code lacks (the audit's five unmeasured constraints, the text in tests not Core, no transform
> class in the catalogue). Step 1 is redrawn with two nodes instead of ticks (hard 0); step 2 -- a
> pump feeding an exchanger -- is drawn (C3 and C4 exercised, C5 sequential placement provisional,
> hard 0, one bend); the user's first correction to it -- the inferred node between two
> components takes no place -- is in `28` A5 and closed its open question 1. Step 3 -- the loop
> closed with a load -- is drawn by C2 (source left flowing up, load right flowing down, the pump on
> the bottom rail; four bends, hard 0), step 4 put a valve on the return with no engine change and
> passed, and step 5 hung the primary off the exchanger's second side (C6: a loop member's flank
> chain runs level away from the loop; a declared pipe and a chain of inline elements spread along
> their run; hard 0, soft 0). The user's step 5 corrections: `D-109` (a symbol reversing on a
> line is mirrored, not half-turned) is in; pipe properties on a connection line is decided,
> `D-110` (option A: an implicit pipe per connection carrying properties, bare connections
> unchanged), as package P5.1e in `08`; C7 aligns a return under its supply. Step 5 stands.
> P5.1d-3 and P5.1e shipped 2026-09-18: the layout report lives in Core and the predicate sweep runs
> on every fixture; the samples, `01`'s reference circuits and the ladder write pipe properties on
> the connection line (rule I7). **P5.2 shipped the same day:** the REST host with `compile`,
> `solve`, `validate` and `metadata`, sessions with warm start and supersession (`41`), `07`'s
> limits as `413` and `FS4601`, the committed JSON schemas (`D-46` step 2) and OpenAPI; `edit` is
> deferred whole to P7.1 with the mutation API. `C-99` found and closed on the way (the exchanger's
> `u`, `ua` and `fouling` were dimensionless); `A-1`–`A-3`, `L-53` and `L-54` opened. Committed
> as `d1a1c08`. **P5.3, the design system, shipped the same day:** the tokens, the two themes as
> JSON with the cascade generated from them, custom theme files, eight primitives, and the first
> frontend tests -- contrast, palette, the literal scan -- on Vitest; `U-1`, `U-2` opened, `U-3`
> closed, in the new `50-frontend/defects.md`. Committed as `8439e2f`. **P5.4, the app shell,
> shipped the same day:** `51`'s shell around a text area and a component list, the four stores,
> the typed client with the wire types generated from the Api's schemas (now titled and documented,
> `D-46` step 4), and the debounce pipeline with its validate phase, all of `51`'s request-stream
> criteria asserted on a hand-driven clock; `U-4` (the debounce is unmeasured until P5.5) and
> `A-4` (the warm start's saving is invisible in `solve.iterations`) opened. Committed as `4b45c20`.
> **P5.5, the editor, shipped the same day:** CodeMirror around a stream tokenizer that mirrors
> the lexer and is fed from the host's committed lexicon, diagnostics as squiggles with one-step
> quick fixes, `52`'s completion table from the committed metadata golden, the formatter in Core
> behind a new `format` endpoint, and the `D-48` benchmark harness, built but not run because no
> browser can launch on this machine; `A-5` closed (the metadata's order was process-dependent),
> `U-5` and `U-6` opened, `U-4` open with the benchmark waiting on a browser. The first screenshot
> of the product was taken the same day (headless Edge on the Windows side, since nothing launches
> in WSL) and judged: technical, and intimidating to a non-programmer. Decided with the user:
> P5.6 first, then a new look-and-feel package (`08`), tuned on screenshots with the diagram in
> place. **P5.6, the canvas, shipped the same day:** the prepared scene as a pure function of the
> wire, one SVG component drawing symbols, routes with hops and fillets, arrows, tags, badges and
> the sized mark, `57`'s flat fill pulled forward, the CAD viewport with axes, grid and level of
> detail, four SVG goldens from the Api samples, `docs/advanced/the-canvas.md`; `U-7` (nobody folds)
> opened; the worker deferred to M4 in `53`. The four samples were drawn and looked at through the
> Edge route. The user then moved the look-and-feel package to the end of the phase as **P5.12**:
> P5 is built to a working prototype first and the interface adjusted once against the whole. The
> P5.7, the renderer's consumption of the prepared scene, was done by P5.6 (the scene is the wire's
> layout since `D-103`, and P5.6 draws all of it); its row in `08` says so. The user's first
> minutes with the canvas (2026-09-18) filed `S-65` (a well-posedness failure reaches the wire as
> one `FS2004` carrying another diagnostic's text, no component, no range) and `C-100`, closed the
> same day with ladder step 3b: a loop with no known duty is still drawn as a ring; the user also
> asked for an unsolved plant to draw red, which `57` records. **P5.8 shipped the same day:** the
> hover card (canvas and editor, one implementation), selection from the canvas, the caret and the
> log with the editor highlighting the declaration, and `56`'s log -- reconciled, grouped at three,
> filtered with the info count, the success line, copy as text, the header's phrase. Building it
> found that nearly every diagnostic reached the wire with no component (`L-55`); the model
> contract now attributes one raised on a declaration to that component. `A-6` opened (no unit
> factors, no pipe velocity on the wire). The user's next script, `PU1 - PU1`, filed `C-101`: a
> component connected to itself was routed through its own symbol, seven hard breaches the audit
> would have failed -- had it run over anything but the ladder's fixtures. Closed the same day, at
> the user's "this should be fixed immediately": `28` C20 (a ring of one on its own outer box, walked
> clockwise; ladder step 3c, pump and exchanger, hard 0), and every layout now audited with each hard
> finding an `FS5002` warning in the log, so a breach is reported rather than handed out as correct.
> Probing the fix found `C-102`, opened: a sourceless, consumerless ring of two still falls to the
> chain rule and breaches -- reported now, not yet drawn right. **P5.9 shipped the same day:** the
> file lifecycle as `58` specifies it, both browser paths behind one interface with every scenario
> run against both, `D-116` for the two things `58`'s one-document draft did not say (Open into a
> new tab; dirty by hashes), `U-8` and `U-9` opened for what only a Chromium session with a human
> can show. **P5.10 shipped the same day.** Building it found `57` in conflict with `D-103`: the
> wire carried the active property's positions only, so the switcher would have needed a request or
> a second mapper; `D-117` puts every available scale on the wire. Two Core gaps the package fixed
> first, at the user's word: `57`'s `show` diagnostics were specified and never raised, and
> `show enthalpy`/`density` were documented and drew nothing. Looking at the coloured cooling loop
> found `C-103` -- a port read its node, so the three-way valve discharging into the mixing node
> drew at the mixed temperature -- closed in the same package: a solved outlet port is now
> reconstructed from the balance's own term, `h_in + injection/ṁ`, and the valve draws hot with its
> recirculation line fading into the node. **P5.11 shipped 2026-09-19:** the SVG and PNG export as
> `59` specifies it, from the one `SceneView` the canvas mounts, with the canvas's own stylesheet
> resolved to literal colours (`D-118`: no font is embedded, the stack ends in a generic family);
> the accessibility pass over `53` -- the diagram as a table built from the hover's cards, every
> control on the keyboard, axe clean under jsdom, two real findings fixed; and the M3 render
> baseline measured where Node can measure it. `U-10` holds what only a browser can verify. **The
> user closed P5 there (2026-09-19: "no need for P5.12; the first phase of the frontend is
> complete")**; look and feel waits for the product to be tested, as the layout does. What follows
> is not a package: a **defect sweep** across every tier's register, quick fixes first, the solver's
> harder ones after, each tier of it reported and approved before the next. Tier 1 (2026-09-19):
> `L-1`, `L-3` and `L-21` were bookkeeping -- already shipped, half shipped, or folded into `C-74`.
> Tier 2 (2026-09-19, the quick fixes): `S-65` (a refused solve reports the posedness check's own
> diagnostics, codes and components intact, through `ResultError.Diagnostics`/`Report`), `L-53`
> (`FS1503`/`FS1512` span the name), `L-54` and `A-3` (`D-119`: `HeatTransferCoefficient` and
> `ThermalResistance` named, `W/(m2*K)` and `m2*K/W` on the wire), `C-28` (`FS2210` says "or add a
> valve" when nothing on the branch can throttle), `C-97` (an implicit pipe carries its connection
> line as `sourceSpan`), `A-1` (`RunAsync` from a `PreparedModel`; the pipeline prepares once),
> `A-4` (`solve.iterations` is the total over passes), `C-39` (`CatalogBoreLookup` takes a resolved
> catalogue only), `U-1` (the literal scan reads TSX sizes; the axes, grid and export type sizes
> moved to tokens). Found on the way: `C-103`'s `tOut` wording had been written into the generated
> page rather than the wire record's XML doc, so the docs gate would have failed on its next run;
> moved. Ten closed, none opened; 67 open.
> Tier 3 (2026-09-19, the medium ones): `S-20` (the outer loop's retry from the sizing seed was
> already there; `FS3012` now registered and raised on it), `L-40` (`FS1534` validates a time curve's
> `format=` on the header per `D-60`; `FS1535` caps `FS1117`'s cascade at five marked rows), `L-55`
> (the binder attributes a diagnostic to the component its `name`/`node`/`component` argument names,
> resolved at the end of the bind; the span fallback is declared-only again), `C-74` (`FS2301`,
> `FS2304`, `FS2305`, `FS2307`, `FS2310`, `FS2312` registered and raised beside their notes with a
> component; `FS2301` names what moved -- `L-21`'s event under its one code), `S-52` (`FS2211`'s
> thermal candidates read from the constraint list per `D-90`, so a matched temperature no longer
> silences the missing level). `L-50` narrowed: three of its five codes live since P5.10; the binder
> home and `FS1211`/`FS1212` stay open. `C-87` not taken: a parked layout entry, the ladder's to
> reproduce. Five closed, none opened; 62 open.
> Tier 4 (2026-09-19, the solver experiments), first item: **`S-29` closed by `D-121`.** Two pumps
> in series never were a Newton or pump-curve defect: the ring's datum is the first pump's suction
> at 0 gauge (`D-98`), the second pump discharges into it, so the second suction sits its own head
> below the datum -- 72 kPa absolute, liquid water -- and `Water.ValidRange` began at 100 kPa
> absolute, so the correct solution lay outside the property table and the line search halved
> against the wall. The floor is now the triple point (`F-25`), the boiling line bounds the liquid
> by phase, and `FS2221` says after the solve what the relative figures cannot: the fill pressure
> that keeps the plant out of vacuum. Found on the way and closed: `S-67` (a step under the step
> tolerance reported the residual of the point it left; the exit now measures where it landed).
> Found and opened: `S-66` (the seed's pressure walk does not see a promoted Kv, so the substation's
> seed is nearly singular in the Kv column and converges by the accident of the 100 kPa datum
> residual -- which is why seeding the datum at 0 was tried and withdrawn). No corpus value moved.
> Two closed, one opened; 62 open.
> **`S-63` closed** (the same day): two injection blocks in series never converged because of two
> seed defects the series shape alone exposes -- a mixing valve's hot-leg fraction read the graph's
> hottest source instead of what feeds its `a` port (the AHU behind the radiators sees 40 °C, not
> 60, so its stream was seeded at a sixth of its circulation instead of half), and the pressure
> walk's anchor read only branch endpoints, so a datum on an inline node started the walk 150 kPa
> away and the closure error fell on a valve's `√Δp` leg. The ladder's 8b now settles in three
> passes with its Kv stated; left to the authority rule, the second pass sizes the radiator valve to
> Kv 1.6, which at mid-travel asks 15 bar of its pump -- `S-58`'s observation as a divergence, filed
> as **`C-104`** (a sizing convention to look up, not derive). The ladder's 8c and 8e, marked `S-63`
> by analogy, fail at the seed for a third reason -- the flow field overrides rated coils with
> nominal ones and starves the source -- filed as **`S-68`**, which is the seed family's next item.
> One closed, two opened; 63 open.
> **`S-68` closed** (the same day): the seed's spanning forest chose its chords blind to what each
> estimate rested on, so a rated coil could be a tree edge and come out of continuity at a twentieth
> of its duty while three nominal coils kept theirs; the forest is now a minimum spanning tree over
> the estimates' basis (`Nominal` < `Propagated` < `Partitioned` < `Duty` < `Stated`), and the
> best-founded flows are the ones the closure never touches. Underneath it, `Prepare` sized pumps
> and valves once, on the bootstrap graph where an unrated coil is ideal, so a pump no constraint
> claimed got zero head; the rules now run twice before the first solve, and the simple loop settles
> in two passes instead of three. The ladder's 8e settles (with a real mixing point for its floor
> and its Kv stated, `C-104`); 8c's first form asked three valves to sit exactly on their open
> bounds, is corrected, and still leaves the domain in a step for reasons that are not the seed's
> flow field -- **`S-69`**: with three unrated loads every `out` promotes a power, every pump but one
> is a rule's choice, and the first pass's answer puts the whole ring on the one promoted pump. The
> header's residual norm moved in its third digit; nothing else in the corpus. One closed, one
> opened; 63 open.
> **`C-104` closed, `D-122` (the same day):** the three-way valve had been sized as a two-way
> control valve -- equal-percentage legs, fully open, authority 0.5 -- and a mixing point that sits
> mid-travel by design then asked fifty times the chosen drop. Looked up: ESBE sizes a mixing valve on
> the flow through it to a 3–15 kPa band and never mentions authority; Johnson Controls shows two
> equal-percentage plugs passing 28 % at mid-stroke; Siemens' and Belimo's mixing valves are linear.
> Now: `three_way_valve` defaults linear, is sized on its common-port flow to the band (`authority=`
> stated still asks for the control-valve rule), its linear legs keep 2 % at the stop so the position
> column survives the other leg opening fully, and the seed's pressure walk crosses a promoted pump's
> branch last so a loop's closure error falls across the pump and not a valve leg. The rule reproduces
> the Kv 6.3 three ladder scripts had stated by hand; the header's pumps fall from 6.5/8.3 to 4.7/5.2
> m and the cooling loop's from 6.4 to 2.5; no flow moved; every three-way step in the ladder and the
> corpus converges. Goldens re-recorded. One closed; 62 open.
> **Two small things the same day.** The log pane's top edge is draggable and its height persists
> (`56` *Height*, the user could not see a dozen warnings in four lines). Found on the way: the
> frontend's whole log feature had never been committed -- the Visual Studio `.gitignore`'s `[Ll]og/`
> swallowed `frontend/src/features/log/` since `P5.4`, so a clean clone did not build the frontend --
> negated and committed, `T-5` closed in the same change, the trap recorded in `63`.
> **`D-120` (2026-09-19):** the user's reading of the language after writing scripts by hand --
> one scheme for a port's state, `in[n].quantity`, brackets for every indexed port, one property
> table with symbol and name, the node kept one-state, `D-115`'s boundaries kept -- is decided and
> is **P5.13**, to run before P6 so M4 is not specified in a spelling about to change.
> **P5.13a shipped 2026-09-20**, ahead of the sweep's `S-53`/`S-55`/`S-56` at the user's request
> (the sweep would otherwise have written its tests in `in=`). The lexer has `[` and `]` and its
> rule-5 clause reads through `in.t=` and `in[2]`; the parser reads a parameter's name, an endpoint's
> port and a schedule target as a qualified name (`FS1119` for a malformed index); the registry's
> rows carry a script `Name` and a model `Key` with the pre-`D-120` spelling as a legacy of the key,
> and its dictionary is keyed by the key; `PropertyTable` owns the quantities and `show` reads it
> (`L-50` narrowed); the binder folds `[1]` and the long quantity names, binds the old spellings with
> `FS1536` and a name-span suggestion, refuses a port state on a node with `FS1537`, and quotes
> `kind.ParameterName(key)` in every message; the tank's port families start at 2 behind the fixed
> `in`/`out`, and the metadata's port family carries a `pattern`. Samples, the ladder scripts, the
> fenced blocks in `plan/` and `docs/`, `22`'s tables and the seven docs pages are in the new form;
> the frontend tokenizer and completion read a name back from its `=` and offer a port's quantities
> after `in[2].`. Port ids, `ComponentStateWire` fields and the `solved`/`sizes` maps are keys by
> decision. **P5.13b** holds what the package deliberately left: `in.p` on a component binding to
> the touching node, `vflow` as a constraint, and the report's and the wire's spelling of a keyed
> parameter (`L-56`). Goldens re-recorded; no corpus value moved. One narrowed, one opened; 64 open.
> **The product's first test by the user (the same day), three findings.** `C-105`: the cooling
> loop with its sign flipped drew as a chain -- C18, the unsourced ring, had no corner-taker and its
> corner junction's free port went up because the loop centre was never set; both fixed, ladder step
> 6c pins the mirror of step 6, nothing else moved. `C-106`: `FS2107` and `FS2210` still spoke
> pre-`D-115` -- a dead-end `node p=` was told to state a parameter and then refused with an empty
> list; both now say "write `N3 outlet`", and the dead-end check reads the kind. `S-70`: a two-circuit
> substation's refusal names the wrong circuit twice and the wrong pump. Discussed and left as
> decided: inferring a boundary from a one-connection node (`D-115`, the vessel stub), and whether
> the layout may read `power`'s sign at all (`D-108`; the user's principle is structure only -- his
> call, recorded under `C-105`). Also fixed: the editor's completion list was white on white in the
> dark theme (CodeMirror's light base rule outranked the stylesheet; the colours are a view theme now).
> Two closed, one opened; 63 open.
> **`S-53` closed** (the same day): its four fixes had landed under `S-58`, `S-63` and `S-68`; the
> acceptance is now measured and pinned on the entry's own no-bypass header (`HeaderSeedTests`:
> coils 0.2871/0.3589 kg/s, source 0.2581, valve legs a partition at 0.4/0.6, two Newton
> iterations; 39/38 and a thermal-first `FS2211` without the level). One closed; 62 open.
> **`S-55` closed** (the same day): the driver check and the valve sizer asked one fundamental cycle
> whether a pump lies on it; the answer belongs to the cycle's biconnected block, where a pump anywhere
> reaches every branch. `HydraulicBlocks` labels branches by block; `FS2214` reads it with the
> boundaries grounded, the sizer without (so a bounded primary beside a pumped secondary still rounds
> up, `D-89`). The pump-free mixing header solves 45/45 in one iteration, `TV_MAIN` sizes to Kv 6.3
> instead of the bootstrap 630, heads 5.05/5.51 m; the tripwire test written with the entry runs and
> passes. No corpus value moved. One closed; 61 open.
> **`S-56` closed** (the same day, the user choosing option b): `power=0` is an operating state. A
> stated `in` on an off coil adds no row and promotes nothing; `out` with `in` pins the branch at
> exactly zero; the pump promoted to hold it may solve negative (the bound lifted for that column)
> and is reported either way -- `FS3015` dead-headed, `FS3014` cannot hold. The measurement found
> three more: the stopped branch's nodes were `S-23`'s dead leg (rank 36/38) and now close against
> the header node the branch hangs from; the exchanger sizer took 1.8e-27 kg/s as a design flow
> (resistance 1e44) and an off coil is now not sized; and a mixed inlet's candidate order handed the
> radiator's inlet the AHU's free valve, so the coil's own split comes first. `HeaderSeedTests` pins
> AHU-off, RAD-off and the main-pumped forward-push case. `S-23` narrowed to the dead leg proper.
> One closed; 60 open.
> **`S-71` filed and closed** (the same day): the solve report carries the Newton trajectory, every
> node in °C/kPa and every branch against its written order, the heat balance per hydraulic, the
> seed basis beside each branch flow, and pump/valve operating points. 60 open.
> **`C-107` filed and closed** (the same day): the layout report carries a `PLACEMENT` trace --
> each fragment's head and why, every form tried with its decline reason, every placement with its
> rule, and every slide, alignment and nudge -- in the order the engine decided. 60 open.
> **P5.13b, item 2 -- `vflow`** (the same day, taken first as the physics item): measuring it found
> that a stated `flow` was not a constraint at all (`S-72`, filed and closed): `HE1 flow=0.3` and
> `PU1 flow=0.3` set a design flow and a duty point and the loop solved to 0.086 kg/s. Both pin the
> branch now; `vflow` pins it through the inlet node's solved density. `L-57` opened on the bare
> unit (`vflow=0.3` is 300 l/s). Items 1 (`in.p` on a component) and 3 (`L-56`) remain. 61 open.
> **`D-123`** (the same day, the user's observation): a `d` prefix on a state quantity is its
> change across the component, with a direction word per row (`dp` a drop, `dt`/`dh` a rise), and
> the property table is the reserved quantity list, linked from `02`. Rows `cp`, `dt`, `dh` added;
> `show dt`/`show dh` draw; `L-58` holds the six symbols reserved ahead of their dimensions. 62 open.
> Committed 2026-09-17 with the Api goldens regenerated to the ladder engine's sample layouts.
> `C-88` and `C-90` closed the same day: the audit measures all ten hard constraints and the
> transform class is on the wire. Step 6, the cooling loop, is drawn (the loop walk through
> junctions, the consumer fallback, C8 junctions on a rail; hard 0) and awaits corrections. The
> simple loop, the substation and the cooling loop samples are reached: their routing, corner and
> audit gates are live and pass. The
> seven layout samples run through the engine's fallback and their layout gates are skipped until
> the ladder reaches them.**
> The substation converges on `01`'s figures —
> UA 12.071 kW/K by ε-NTU and by LMTD at the solved state, 3.658 m², 0.895 / 1.793 kg/s — after
> four changes that were one defect from the outside (`S-32`): the exchanger's duty is
> `ε·Cmin·(T_in2 − T_in1)` from the port states, its design point pins a side's flow where nothing
> else does (`D-97`), the picked datum is the pump suction so the seed stays inside the property
> domain (`D-98`, `S-62`), and the promoted Kv seeds from the Kv law. `FS2109`–`FS2112` and `FS4008`
> are live; `hx.u_default` is withdrawn until it has a source (`D-99`, `C-78`).
> P3.0 through P3.9 shipped and every M2a criterion in `05` is ticked with the test that holds it.
> The last one, the solver-scale baseline, found `C-76`: every real-water property read leaked a
> 540 KB native CoolProp state, which is what had been killing the machine and the agent sessions
> with it. Fixed the same day; 861 unknowns now solve in 5.4 s at 165 MB, and every solve in the
> corpus is 4× faster. The R-17 coverage row is `62`'s governing-equation table.
> `C-75` closed with `D-96` the same day: a bootstrap provisional counts as free, so `head=15` is
> absorbed by the balancing valve (Kv 0.77) instead of refused. `S-61` (`FS3008` on a bound the
> path merely crossed) was found and closed with it.
> **All three M2a demo scripts converge**, as of 2026-09-14, and the header lands on `01`'s figures.
> `S-58` was the last blocker: a junction mixed its inlets by a plain average, so no valve position
> could move a mixed temperature. `D-91` (positive role capacities) and `D-92` (fixed flow as a flow
> residual) are in; `S-53`, `S-55`–`S-57` record what measuring them found and stay open.

M2a asks for three demo scripts to solve. All three do:

| Sample | State | Note |
|---|---|---|
| `m2-simple-loop.fluid` | **Converged** | `24`'s worked example reached rather than transcribed — pump head 5.28 m from nothing but the loop |
| `m2-cooling-loop.fluid` | **Converged** | Mixing node 19.99 °C against 20, return 49.94 against 50, 0.0763 kg/s recirculating |
| `m2-distribution-header.fluid` | **Converged** | One Newton iteration, three sizing passes: 0.1914 / 0.2392 kg/s drawn from the 60 °C header, 0.4307 through the source against `01`'s 0.4306, valves at 0.63 / 0.62 of travel. `S-58` |
| `m2-substation.fluid` | **Converged** | Two Newton iterations, two sizing passes: `HX1.ua` 12.071 kW/K, `HX1.area` 3.658 m², primary 0.895 kg/s at 85/45, secondary 1.793 kg/s at 60/40, `PCV.kv` 2.13, `SP.head` 10.2 m. `S-32`, `S-62`, `D-97`, `D-98` |
| `m4-storage-header.fluid` | **Converged** | Solves in one pass; nothing in it needs sizing |
| `m4-demand-step.fluid` | **Converged** | One iteration on `01`'s figures once the setpoint constrains `N2` (`S-75`, `D-141`): `N2` 20.00 °C, 0.2393 kg/s secondary, 0.0763 recirculating, the valve at 0.501, `PU1` 2.55 m; the cells keep DN20 (`C-113`) |

The recorded status of every sample is asserted by `CorpusStatusTests.EachSampleStandsWhereItStood`,
which is the durable form of this table: a sample that starts solving without anyone noticing fails
that test rather than quietly improving.

## The phase ladder

| Phase | Milestone | Packages | State | Closed |
|---|---|---|---|---|
| P0 | pre-M0 | 3 | **Complete** | 2026-09-01 |
| P1 | M0 | 4 | **Complete** | 2026-09-01 |
| P2 | M1 | 10 | **Complete** | 2026-09-02 |
| P3 | M2a | 10 | **Complete** — every package shipped and every `05` criterion ticked | 2026-09-14 |
| P4 | M2b | 3 | **Complete** — every `05` criterion ticked but the heat-pump tag, whose kind does not exist until M4; M2b exited on that basis | 2026-09-15 |
| P5 | M3 | 13 | **Closed by the user 2026-09-19** — P5.1–P5.11 shipped, P5.12 dropped, P5.13a shipped 2026-09-20 and P5.13b 2026-09-21, the spelling M4 will be specified in | 2026-09-19 |
| P6 | M4 | 9 | **In progress** — P6.0, P6.1, P6.2 and `C-114` shipped 2026-09-22; P6.8a and P6.8b 2026-09-22/23 (the scenario language and its sizing pipeline), `C-121` 2026-09-23 (valve authority and turn-down across scenarios). P6.9 2026-09-23 (the source structure, `D-147`/`D-148`, S0–S5), `C-120` 2026-09-23 (binder checks read every scenario). `S-79` 2026-09-23 (`D-149`, a run's start time). `D-150` 2026-09-23 (no measurement at a junction), `C-124` 2026-09-23 (a pipe with `nodes=` drawn as its chain), `D-151` 2026-09-23 (instruments on their hosts, I8), `D-152` 2026-09-23 (signals cross the drawing). Next: P6.10, the layout engine rebuilt (`D-153`), then P6.3 | — |
| P7 | M5 | 2 | Not started | — |
| P8 | M6 | — | Evidence-gated; not decomposed | — |

`08` lists P2 as nine packages and P3 as nine. Both grew by one during execution and `08` records why
in each case: **P2.10** took the language half of `D-57`–`D-62` out of P3, and **P3.9** was created by
`D-70` when elevation turned out to be a parameter on every kind rather than a line in P3.6.

## What each phase delivered

### P0 — prerequisites · complete 2026-09-01 · `bcfd8e9`

All three packages in one commit, because none of them produces code: sequencing the implementation
(`D-45`, `D-46`) and independently reproducing every asserted reference number.

**P0.3 paid for itself twice**, and `08` keeps both accounts: an input figure was wrong (`h(6 °C)`
stated 124 J/kg off CoolProp), and later a derived figure reproduced its own arithmetic while
describing the wrong circuit.

### P1 — M0 scaffold · complete 2026-09-01 · `9fbb4cf`

The SharpProp spike, the repository skeleton, CI with the architecture tests and the docs gate, and
the five test-trait harnesses with no baselines behind them.

**P1.1 was the one package able to invalidate a tier**, and it did not — but it moved figures in
[`21-fluid-and-state`](20-core-domain/21-fluid-and-state.md) that the whole property tier rests on,
which is the outcome the gate exists to produce cheaply.

### P2 — M1, the language spine · complete 2026-09-02

| # | Package | Commit |
|---|---|---|
| P2.1 | Diagnostics, spans, the code registry | `386ea5b` |
| P2.2 | Dimensions, units, `Quantity` | `557ef39` |
| P2.3 | Lexer with trivia attached | `4e884d4` |
| P2.4 | Parser, AST, error recovery | `fe27ce3` |
| P2.5 | Printer and the round-trip fuzz | `491948b` |
| P2.6 | Component registry, kind resolution | `cbb2adc` |
| P2.7 | Binder steps 0–5, expressions | `d391b46` |
| P2.8 | Binder steps 6–11 | `643c261` |
| P2.9 | Version detection and the compatibility gate | `d7055a5` |
| P2.10 | The language half of `D-57`–`D-62` | `2fcc3c5` |

**M1's exit criterion is asserted, not claimed**: `01`'s nine-diagnostic count runs against
`samples/m1-syntax-reference.fluid`, and `Print(Parse(x)) == x` is a standing corpus-mutation fuzz from
P2.5 onward rather than a milestone check.

P2 is where most of tier 10 was found and closed — 35 of its 42 entries are in the Closed table, and
each names the package that closed it. Seven remain open; see
[`10-language/defects.md`](10-language/defects.md).

### P3 — M2a, the hydraulic core · complete 2026-09-14

| # | Package | Commit(s) | State |
|---|---|---|---|
| P3.1a | The three M2a reference circuits, transcribed | `5a16eff` | Shipped |
| P3.1 | `ISubstance`, `FluidState`, the SharpProp adapter, both fakes | `450df19` | Shipped |
| P3.2 | Property accuracy — V4, V5, V13, V14 | `d5e1a20` | Shipped |
| P3.0 | Sensors as solved observers | `13a764e` | Shipped |
| P3.3 | Component model, six kinds in duty mode | `3b38804`…`79f80b7` (7) | Shipped |
| P3.4a | Lowering, `CircuitGraph`, the cycle basis | `1a09e54` | Shipped |
| P3.4b | The counting argument, promotion, the `FS22xx` codes | `f244d98` | Shipped |
| P3.4c | Boundaries that declare themselves, the enthalpy datum | `50a0a5b` | Shipped |
| P3.5 | The catalogue, compiled and refusing its own rows | `b96673a` + sourcing | Shipped |
| P3.6a | Scaling, the state vector, the equation rows | `4dc5bfa`…`16e5637` (4) | Shipped |
| P3.6b | Newton, and the seed it cannot start without | `b5f5539` | Shipped |
| P3.7a | The seed that closes every mass balance | `1fe14ce` | Shipped |
| P3.7b | Sizing rules and the single outer loop | `91ac4fc`…`0689589` (5) | Shipped |
| P3.8 | The design point as the sizing point — `sized_at` (`D-94`) | `b079ec6` | Shipped 2026-09-14 |
| P3.9 | Elevation as an absolute height (`D-70`, `D-95`) | `38bfd61` | Shipped 2026-09-14 |

**P3.1a and P3.4c were not in the plan.** P3.1a transcribed the reference circuits before anything
could solve them and found two defects in the documents that define them. P3.4c began as a change to
what a boundary declaration means and turned into two corrections to the counting argument itself —
`08` keeps the account of why, because the shape of it ("the package that finds a defect is the one
that tries to *use* the thing") is a planning lesson rather than a state fact.

**P3.8 was smaller than planned, and half of it moved.** Measured before anything was written,
`design` already sized end to end: the binder folds every curve to its design-point value before
lowering, so no sizing rule needed to read `ProjectSettings.Design` and `C-51`'s "fraction-of-peak
rule" turned out to be the wrong shape. What shipped is `D-94`'s `sized_at` clause — one component
reading the curve at its own bivalent point, the closed-circuit closure sizing its backup, and the
fraction reported as the parameter's basis. The live-curve half (a curve as a function of time in a
transient run) moved to P6.1, where a clock first exists; `08` records the re-scope.

**P3.9 shipped as `D-70` with one amendment and one deferral.** Measured first: the relative rise
already carried the right physics (313 kPa and 314 J/kg over a 32 m riser, +0.0125 K of friction
heating on an open one), so the package was the language and the propagation, not the equations.
`elevation` is now a height on every single-height kind; a pipe has none and its rise is derived;
a bare node-to-node link carries `ρgΔz` in the assembler, in the arriving enthalpy and in sizing's
loop walk (the last found by measurement: 45.8 m of head before it counted the link); the tank's
port fraction is `in1_level`. The amendment is `D-95` — an omitted height is inherited from the
neighbourhood, not 0, or a roof would need `elevation=32` on every line. The deferral is the tank's
per-port `z_tank + f·H`, which waits for P6.2 to give the tank a height; its ports take the vessel's
one height meanwhile, recorded in `22` and `23`. `S-60` was filed on the way: a tall loop with no
`p=` fails at the seed with `FS3007` and nothing names the fill pressure; with heights on every node
it became `FS2220` the same day, arithmetic before the seed.

P3 is where tiers 20 and 30 were largely written and largely corrected: 49 of tier 20's 68 entries and
41 of tier 30's 56 are closed. Both Closed tables carry the attribution.

### P4 — M2b, coupled thermal rating · in progress

| # | Package | Commit(s) | State |
|---|---|---|---|
| P4.1 | The rated two-sided exchanger: ε-NTU as the residual route, LMTD as the reported one (`D-97`, `D-98`, `D-99`) | `c275bd9` | Shipped 2026-09-15 |
| P4.2 | Two coupled hydraulic graphs, two pressure datums | (with P4.1's follow-up) | **Shipped 2026-09-15, as a verification.** The partition, the two datums and `D-17`'s `FS2213` exemption were built in P3.4 and P4.1 solved through them; what remained was `23`'s three unticked substation criteria, one of which had no test — a coupled exchanger on two `Branch.Path`s, no junction, no mass balance. Added and ticked. |
| P4.3 | `D-36` circuit ownership from the enthalpy-losing side | (this commit) | Shipped 2026-09-15 |

**P4.3 is a binder step, not a graph one.** `D-36` and `25` describe ownership as read off the
heat-transfer edge the layout hints build, but a tag is a binder product computed on every keystroke,
before anything lowers. `BindingRun.ResolveOwnership` runs after `Validate` and before `AssignTags`:
it walks each side's ports through inferred nodes to the first declared component and takes its
circuit, reads the losing side from the duty's sign (role words carry it, `D-91`) or from whichever
side's stated terminals drop, and rewrites `ComponentSymbol.CircuitName` — so the tag, the graph's
`CircuitOf` and the layout hints all agree without a second traversal. The substation as two blocks
tags `HX1` `400HE01` from either block in either order; `LOAD` stays `100HE01`; a `chiller` between
the same circuits is `100HE01`; two circuits with no readable direction fall back to the lower number
with `FS2216` anchored on the declaration. `WellPosedness.ReportOwnership`, which raised `FS2216`
for a fallback it never applied and against the wrong fallback order (both sides in one circuit is
not ambiguous), is gone. `RatedExchangerSolveTests.TheSolvedStateIsIdenticalWhicheverCircuitDeclaresTheExchanger`
is `23`'s "ownership never reaches the physics" test. Nine new tests, 1604/0/4.

**P4.1 was four changes wearing one defect.** `S-32` was filed as "nothing computes the side-2
flow", a registry group and an `ImpliedFlow` away. Measured, that would have seeded the right flow
and constrained nothing: the substation's primary has stated pressures at both ends and a promoted
valve, and its flow was whatever Kv 630 passed. What closed it: (1) `ExchangerRating` on the lowered
component and `HeatExchanger.Duty` — `ε(UA/Cmin, Cr)·Cmin·(T_in2 − T_in1)` from the port states,
allocation-free, with `Effectiveness` (counterflow blended C¹ across `Cr → 1`, parallel, crossflow by
bisection) and `LogMeanTemperatureDifference` sharing no code; (2) `D-97`, the design point as a
flow pin on a side nothing else pins, the same rule giving Rated mode its pin; (3) `D-98`, the
picked datum at the pump suction, found by a scratch experiment after everything else was right
and the seed still died at 80 kPa absolute (`S-62`); (4) `SolutionSeed.PromotedKv` and per-side
duty shifts in the seed's temperature levels, so `PCV.kv` starts at 2.88 for a solved 2.13.
`ThermalSizer` sizes `ua` (and `area` from a stated `u`, `plates` from a stated `plate_area`) from
the design point alone, checks feasibility before inverting (`FS2111`), holds the approach to
`hx.approach_min` (`FS4008`, the first `FS4xxx` code to fire), and returns diagnostics the outer loop
merges into the solve's. The solve report gained *exchanger ratings at the solution*, printing
`UA … rated, … by LMTD` side by side. `BranchFlows.Duty` learned `dt`/`dt2` so a rated loop stated
as `in`+`dt` seeds at its design flow rather than 0.1 kg/s. What was deliberately not done: no `U`
is invented (`D-99`), `lamella` is unused, side-2 `dp` stays the duty-mode default, and the plate
step is not applied — all `C-78`, all waiting on `27`'s plate catalogue. 50 new tests; every sample
in the corpus converged or unchanged; only the `FS2201` text moved on closed loops.

### P5 — M3, the usable static product · in progress

| # | Package | Commit(s) | State |
|---|---|---|---|
| P5.1a | `LayoutHints` per `25`, with `BranchShapes` (`D-100`) and `FS2401`–`FS2403` | (this commit) | Shipped 2026-09-15 |
| P5.1b | `ModelContract` per `26`: wire records in Core, the serializer in the Api, goldens | (this commit) | Shipped 2026-09-15 |
| P5.1c | Symbol strokes per `D-24` and `53`'s inventory; the 200-component payload baseline | `969db66` | Shipped 2026-09-15 |
| P5.1d-1 | The layout solver in Core (`D-103`): placements with inner and outer boxes, stub-and-join routes, named styles (`D-104`), inline elements and alignment (`D-105`) | (with P5.1d-2's first commit, 2026-09-17) | Shipped 2026-09-16 |
| P5.1d-2 | The layout engine built rule by rule against the ladder ([`28`](20-core-domain/28-layout-solver.md) parts A–D, [`29`](20-core-domain/29-layout-ladder.md); `D-106`, `D-107`, `D-108`, `D-109`, `D-110`, `D-112`, `D-113`, `D-114`) | (this commit, 2026-09-17, with P5.1d-1's engine work) | Steps 1 to 10 accepted 2026-09-17; step 11a (two independent loops, stacked) accepted; the syntax tour's circuits follow one at a time |
| P5.1d-3 | The layout report (`D-100`) and `62`'s predicate gates: `SceneText` in Core with its raster, the audit's three gaps (`C-95`), `LayoutPredicateTests` | (this commit, 2026-09-18) | Shipped 2026-09-18 |
| P5.1e | Pipe properties on a connection line (`D-110`): the grammar's trailing property list, the printer round trip, the implicit `pipe` per connection (rule I7) with `length` defaulting to zero, the samples, `01`'s reference circuits and the ladder scripts rewritten to it, `docs/functions/pipe.md` | `44e30fa` | Shipped 2026-09-18; `L-51` closed; `C-97` (the implicit pipe's source span) opened |
| P5.2 | REST and diagnostics contracts, host, sessions, cancellation ([`42`](40-api/42-rest-contract.md), [`44`](40-api/44-diagnostics-contract.md), [`41`](40-api/41-api-architecture.md)): `compile`/`solve`/`validate`/`metadata`, sessions with warm start and supersession, `07`'s limits, the committed JSON schemas, OpenAPI, `docs/advanced/using-the-api.md` | `d1a1c08` | Shipped 2026-09-18; `edit` deferred to P7.1; `C-99` closed; `A-1`–`A-3`, `L-53`, `L-54` opened |
| P5.3 | Design tokens and themes ([`55`](50-frontend/55-design-system.md)): `tokens.ts`, the two themes as JSON and the generated cascade, custom theme files, the UI store's `theme`, eight primitives, Vitest with the design tests, `docs/advanced/themes.md` | `8439e2f` | Shipped 2026-09-18; `F-3` closed; `F-1`, `F-2` opened; `Tooltip`, `Slider`, `NumericInput` land with their first consumers |
| P5.4 | App shell, the four state domains, the debounce pipeline ([`51`](50-frontend/51-frontend-architecture.md)): the shell with `SplitPane`, tabs, log slot and status line; `draftStore`, `runStore`, `workspaceStore`, `uiStore`; the typed client and `types.generated.ts` from the schemas (titles and descriptions added on the Api side); `CompilePipeline` with `LatencyTracker`; `docs/advanced/working-in-tabs.md` | `4b45c20` | Shipped 2026-09-18; `F-4`, `A-4` opened; the editor is a text area until P5.5, the canvas a list until P5.6 |
| P5.5 | The editor ([`52`](50-frontend/52-editor.md)): CodeMirror with a stream tokenizer generated from the host's `language.json`, inline diagnostics with quick fixes, alias-aware and dimension-filtered completion from the committed metadata golden, the formatter in Core (`17`) behind `POST /api/v1/format` (`42`) and `Shift+Alt+F`, go to definition, toggle comment, Solve; `TokenGoldenTests` on both sides; the `D-48` benchmark harness; `docs/advanced/the-editor.md` | `0f112c8` | Shipped 2026-09-18; `A-5` closed; `F-5`, `F-6` opened; `F-4` stays open, the benchmark is built and unrun (no launchable browser here); hover for components, lets and quantities → P5.8, Rename → P7.1, Run → M4, Save/Open → P5.9 |
| P5.6 | The canvas ([`53`](50-frontend/53-canvas-renderer.md)): `prepareScene`, `SceneView`, the viewport, `CanvasPane` with axes, grid and level of detail; `57`'s flat fill; SVG goldens per Api sample; `docs/advanced/the-canvas.md` | `a33af65` | Shipped 2026-09-18; `F-7` opened; the worker deferred to M4; the frame budget unmeasured (`F-4`'s browser) |
| P5.8 | Hover, selection, the console log, the status header ([`54`](50-frontend/54-interaction-and-writeback.md), [`56`](50-frontend/56-console-log.md)): the hover card with one implementation in the canvas and the editor, the selection store and its three sources, the reconciled and grouped log with filters, the success line and copy as text, `56`'s header; diagnostics attributed to their declaration on the wire; `docs/advanced/the-log.md` | `3a3ac8f` | Shipped 2026-09-18; `L-55`, `A-6` opened; editing and write-back are P7.2's |
| P5.9 | The file lifecycle ([`58`](50-frontend/58-file-lifecycle.md), `D-116`): New, Open into a new tab, Save in place through the File System Access API, Save As, Download in browsers that cannot overwrite, `dirty` by hashes, the conflict check before an overwrite with Reload / Save As / Compare, recovery to IndexedDB on the idle timer and its return after a reload, `FS1701` withholding Save with the version line offered (Core now attaches its suggestion), read-only unsupported files, the close questions for text and run, the eight-document limit; the File menu, the notice bar, the dialog; `docs/advanced/files-and-recovery.md` | `19c7584` | Shipped 2026-09-18; `F-8` (the native path unverified in a real browser), `F-9` (re-permission after a reload) opened |
| P5.10 | State visualization ([`57`](50-frontend/57-state-visualization.md), `D-117`): every available scale on the wire with every element's place on each (`ColourScales`), enthalpy and density mapped, `FS1210`/`FS1213`/`FS1214` raised; pipe gradients between the outlet left and the inlet entered, the exchanger gradient across its body, the legend with unit, 1-2-5 ticks, the switcher (no request), the band hover, degenerate and unavailable notes, stale desaturation, `Home` resetting the switch; `docs/advanced/the-canvas.md` and `show.md` | `f43774d` | Shipped 2026-09-18; `C-103` found and closed (a port read its node; an outlet now reads its own stream, `22`); not built: tank layer bands, the run-wide transient domain (M4), `57`'s wider property table |
| P5.11 | Static export and the accessibility pass ([`59`](50-frontend/59-static-export.md), `D-118`, `D-45`): `renderExportSvg`/`exportSvg`/`exportPng` from the canvas's `SceneView` with `scene.css` resolved to the theme's literal colours (Oklab for the mixes), strokes in world units, the legend band, the value under each symbol, the provenance `<desc>`; the Export dialog and `Ctrl+E`; export goldens per sample; `SceneTable`, keyboard pan/zoom/select/card, axe-core under jsdom, the status line's landmark and the editor's accessible name fixed; `baseline.test.tsx`; `docs/advanced/exporting.md`, the canvas page's keyboard table | `d3b7323` | Shipped 2026-09-19; `F-10` opened (the browser-only checks: PNG pixels, four viewers, axe with layout, zoom, screen reader, the panning budget); the render budget is not met by extrapolation (`53`) |
| P5.13c | `14`'s Phase B (`L-59`): `DeferredEvaluation` in the outer loop -- each pass evaluates the deferred expressions against the solved state and writes them in as stated values with a basis, pass 0 against the seed with the script's own anchors only, the settle test over sizes and values, `FS1405` at the cap, `FS1410` for a line no pass could evaluate; `docs/functions/let.md`, the report guide; the S-58 trickle paragraph in `three-way-valve.md` | *(this commit)* | Shipped 2026-09-21. `L-59` closed; `L-60`–`L-63` and `C-109` opened |
| P5.13b | The rest of `D-120`: `vflow` and `flow` as constraints (`S-72`), keyed spellings in the report (`L-56`), and a port's pressure as the touching node's (`D-124`: generated `port.p` rows, `PropagatePortPressures`, `FS1538`, `FS1539`, `FS2210` naming the pressures, `PressureStatedAs`); `docs/functions/{syntax,heat-exchanger}.md`, `reading-the-solve-report.md` | *(this commit)* | Shipped 2026-09-21. `L-56` closed; `L-59` opened (a deferred reference to a solved value is never evaluated); `in.p`→`in.t` near miss and `FS2210`'s level-paid candidate fixed with it |
| P5.13a | Port-indexed state syntax (`D-120`): `in[n].quantity` on declarations, references, endpoints and schedule targets; `FS1119`, `FS1536`, `FS1537`; `PropertyTable`; registry rows keyed by model key with a script name; the corpus, `22` and `docs/functions/{syntax,heat-exchanger,tank,show,properties}.md` respelled; frontend tokenizer and completion | *(uncommitted, 2026-09-20)* | Shipped 2026-09-20. `L-56` opened for P5.13b (`in.p` on a component, `vflow` as a constraint, keyed spellings in the report and on the wire) |
| Sweep 1–3 | The defect sweep after P5 (tiers 1–3, 2026-09-19): 15 entries closed across five registers -- `L-1`, `L-3`, `L-21`, `S-65`, `L-53`, `L-54`, `A-3`, `C-28`, `C-97`, `A-1`, `A-4`, `C-39`, `F-1`, `S-20`, `L-40`, `L-55`, `C-74`, `S-52` -- with `D-119` (named exchanger dimensions), `D-120` (port-indexed state syntax, P5.13 to implement), `SizingDiagnostics`, `FS1534`/`FS1535`/`FS3012`, and the dotnet-toolkit observations report | `20fccb9` | Recorded above under P5.11 |

**P5.1a is `LayoutHintsDerivation.Derive(graph, model, branchFlows)`**, a pure function of the
lowered graph, its model and the solved branch flows, returning the hints and its three
informational codes. Every one of `25`'s worked examples is a test: the cooling loop's `Order`,
`Rank`, one loop walk `[N2, PU1, PU1__HE1, HE1, HE1__3WV, 3WV]`, four inferred of ten and one
Neutral stage; the storage header's `Source [S1, S2] · Storage [T1] · Consumer [RAD_NETWORK,
AHU_NETWORK]`; the distribution header's one group of two, equal `BranchShapes`
(`[pipe, three_way_valve, pump, heat_exchanger, pipe]`) that survive renaming and differ on an
inserted valve; the substation's source side before `HX1` and its heating side after, stable under
block swap. Three things `25` had to be made precise about while implementing, all written into it:
loops are *banded* rather than collapsed (the header's cycle basis holds four loops, two of them
through both consumers); classification is *relative to a pivot* (an extended exchanger or a tank),
which is why an open loop with no pivot is one Neutral stage rather than a source and a consumer
either side of nothing; and a registered Neutral role classifies nothing, so `FS2403` fires only for
a `Source`/`Consumer` role contradicted by its members' duty sign. The header sample's parent ring
therefore shares rank 0 with its branches — `25`'s example said `Source [heating's boundary]` for a
sample that has none and is corrected — and whether it should be a Source band on its duty sign
alone is `25`'s one open question, left for the canvas package (P5.6) to answer with a diagram in front of it. A
subcircuit written as connections (`F-16`'s mixing branch) gets its parent and anchors read off the
graph — node contacts with exactly one other circuit, not mutual — because the binder binds them
only from `supply`/`return` lines. `NavigationOrder` became one tab order over flow components and
instruments together. Docs: `advanced/how-the-diagram-is-arranged.md` and a role table on
`circuit.md`. Nineteen tests, 1629/0/4. The user's two addenda to `D-100` — the layout report is
columnar text like the solve report, not JSON; layout reasons on a bounding box and port anchors,
strokes are for drawing — are recorded in `62` and `53`. Filed `C-79`: the cycle basis is the solver's, not the drawing's, and on the header every component is a loop member.

**P5.1b is `ModelContractBuilder.Build(input)` in `FluidScript.Core.Model` and
`ModelContractJson` in `FluidScript.Api/Contracts`.** The builder projects the bound model, the
lowered graph and the outer-loop result into `26`'s records -- every number in the script's canonical
unit beside its unit, six significant digits, `stated`/`sized`/`default` with a basis, the solved
operating point per component and connection read back through the new `SolvedStates`, `25`'s hints
field for field, `44`'s diagnostics in both position forms, provenance, and the `show` directive
resolved to a scale. The serializer is the Api's because `D-47` says Core names no serializer and
the architecture tests enforce it: the first draft had it in Core and three tests said so, which is
what they are for; `D-101` records the split and reads `41`'s invariant 1 as *no domain type on the
wire*. Eight golden files (four samples, compile-only and solved) are checked in under
`FluidScript.Api.Tests/Contracts/Goldens` and regenerate only with `FLUIDSCRIPT_UPDATE_GOLDENS=1`;
the round trip is byte-identical in both forms. `26` records ten precisions the shape needed, the
one that matters most being that a promoted head or Kv is `sized` on the wire with the solver as its
basis. `L-50` filed: the binder does not bind `show`, so `FS1210`–`FS1214` are unregistered and the
contract reads the directive off the syntax. Docs: `functions/model-contract.md`, generated from the
records' own XML docs by the docs gate, and `show.md`'s resolution table. Core 1648/0/4; Api 15/0.

**P5.1c is the strokes inside `SymbolCatalog` and the payload baseline.** Every symbol `53`'s
inventory lists now carries its primitives on the wire -- with `fill: "state"` marking the slot the
colour scale paints, `fill: "stroke"` a solid mark, and `dashed` on the controller's bubble -- and
`docs/functions/model-contract.md` gains a symbols section whose table the docs gate generates from
the catalogue. `53`'s inventory gained the sensor row it had been missing since `D-61`. The
200-component reference model is generated, not checked in: `ReferenceModels.DistributionHeader(18)`
in `FluidScript.Fixtures`, `01`'s header with eighteen pumped consumers, exactly 200 components and
19 circuits. Measured: 189.8 KiB compile, 237.6 KiB solved with every state, ~1.2 ms warm
serialization, 1.1--1.5 s to solve; `05`'s M3 payload criterion is ticked on the server side and
`26` holds the numbers. `D-102`, asked for by the user mid-package: every anchor carries its outward
direction, a symbol may offer alternative arrangements of its ports (the exchanger's `u` beside its
through-pass default), `25`'s `PortSides` is read off the default arrangement, and `53` states the
Manhattan-plus-bends cost the renderer picks arrangement and rotation by. Core 1656/0/4; Api 18/0.

**P5.1d-1 is `LayoutSolver.Solve(graph, model, hints, margin)` in `FluidScript.Core.Layout`**, and
it exists because the user stopped P5.1c's plan mid-sentence: the frontend was about to own
placement, and *all solving and calculation should be made in Core* -- the frontend is a renderer.
`D-103` records that; the `CLAUDE.md` non-negotiable now says geometry never moves to the frontend
either. The solver is three passes over the hints. *Cells*: a distribution group's parent becomes two
rails and each child branch a column between them (the longest simple path through the branch, so a
bypass edge cannot shortcut it), a loop a ring with bare corners (`D-44`), a chain hangs outward,
and a model with neither is placed by thermal stage. *Orientation*: every symbol's arrangement
(`D-102`), quarter turn and mirror are chosen by Manhattan stub-to-target length plus a bend
penalty, two passes so later neighbours can move earlier choices. *Coordinates and routes*: each
column and row is sized to its widest and tallest symbol plus twice the margin, which is what makes
`D-103`'s invariant -- no inner box inside another's outer box -- a property of the grid rather than
a check; a route leaves each anchor for `margin / 2` along its direction and the two stubs are
joined by the cleanest of the simple orthogonal joins (no box crossed first, then fewest bends, then
shortest). The wire carries it as `layout.margin`, `extent`, `placements[]` and `routes[]` (`26`).
`D-104` puts the styles beside it: `style name = tokens` defines, `style name` on a circuit and
`style=name` on a component apply, `fill=` is keyed and `show` overrides only the fill; Core
resolves named colours and the wire carries `#rrggbb`. `spacing` is the margin now, 0.5 by default,
and the samples' `spacing 20` -- twenty pumps -- became `0.75` (`55` records why). Measured: the
solver is 24 ms on the 200-component header after `D-105`, under `07`'s 30 ms; the payload grew
to 278.5 / 325.7 KiB.
Seven layout SVGs under `diagnostics/layout/` are what a session looks at, since it cannot see a
canvas, each with a `.txt` beside it listing every placement and route in world units.
**The user reviewed the first pictures on 2026-09-15 and named five things wrong with them**, which
became `D-105` (2026-09-16): a declared pipe was a box, an inferred node a circle as big as a
junction, an exchanger's off-centre pass met the pump with a jog, ties fell to enumeration order,
and the three-way valve's body was not one a manufacturer builds. Now a pipe and a two-port node are
points on their run (no cell, no box, nothing drawn for the node, a label for the pipe), a junction
is a 0.2 dot with one pipe per side, a symbol slides in its cell so its run anchor sits on the line,
a designer's orientation is a cost below a bend, routes are penalised for running along a drawn pipe,
labels stay upright beside the placed box, and `three_way_valve` has `a`–`ab` straight with `b` the
angle port (Belimo, Siemens VXG -- cited in `53`). **Then the user pointed at `53` itself**, and
the cells were re-planned to its shape: every branch and every loop is one U (`ShapeU`/`PlaceU`) --
supply run along the top, the last exchanger down the far side with its neighbouring valves, return
along the bottom, the bypass junction under its valve, members that lead out of a loop climbing the
near side, the entry junction in the top-left corner, all four corners reserved so no chain lands on
one -- and a loop runs the way the plant is piped, read off port roles rather than the solved
orientation hint so the compile and the solved drawings are the same drawing. `53`'s worked example
was updated for the valve body (the valve climbs the left vertical instead of sitting on the corner)
and three of its acceptance boxes are ticked; filed `C-83` (rail ends, where a chain hangs off a U, bypass-leg components). The 200-component header solves in 24 ms, under `07`'s
30 ms, because two hundred fewer cells are routed. Filed `C-80` (bypass legs hug the branch line), `C-81` (orientation ties fall to enumeration
order), `C-82` (nested headers are laid out as chains); closed `L-1`. Core 1695 total/0 failed/4 skipped, twice; Api 18/0.

**P5.1d-1 continued on 2026-09-16 with the router and the audit, and ended with `D-106`.**
`OrthogonalRouter` replaced the simple joins: a Hanan grid over every outer box and every laid pipe,
Dijkstra with bends and pipe-following penalised and no doubling back, a straight join when two
ports face each other with nothing between, hops recorded where a route crosses an earlier one,
each port's stub reserved as a lane so no later pipe wraps it, and the stub a whole margin long from
the inner boundary to the outer one, turning only from there. `SceneAudit` (Core) is the layout's
validator: inner box inside an outer box, a pipe through an outer box, a pipe beside a pipe closer
than the margin, with the run-ownership and port-pitch exemptions; `SceneText` (tests) writes the
scene as text per sample -- rotation, inner and outer boxes, each port at both boundaries with its
vector, every route with its band, the audit's findings -- because the user diagnoses text faster than
SVG. Then the user reviewed the four pictures against sketches and named the drift: the substation
and the simple loop were laid out by a cost and a router rediscovering what one rule states (the
exchanger takes the flow down, so the pump is below it), and four bends appeared where there is
one. Their specification of the rule-based engine is `28` (kept verbatim beside it) and `D-106`; the
pictures were brought to the sketches by hand-tuned hints (`_approach`, `FlankStep`, `Jog` lanes) to
prove the audit and the text, and those hints are what `28`'s stages replace. Measured after the
router: `header-200` 21 ms (`07`'s 30 ms). Core 1702/0/4 twice; Layout 55/55; Api 18/0 with goldens
regenerated. Filed `C-84` (labels are placed, not laid out) and `C-85` (the thermal-stage fallback
stacks); `C-80`, `C-81`, `C-83` are answered by `28`'s stages rather than fixed in P5.1d-1's engine.

**P5.1d-2 built the engine whole the same day, then started again.** The first build implemented
all six stages of the `D-106` list: y up everywhere in Core, link directions by roles and
propagation, inline elements contracted into runs, a header laid per `28`'s branch rule, simple
loops by Tarjan SCC solved by a four-side partition search into rigid groups, everything else hung
from placed ports, the router last. Seven samples audit-clean (hard 0 / soft 0), `header-200`
23.5 ms, Layout 55/55. The user judged the pictures wrong in the same way the P5.1d-1 pictures were
wrong -- exchangers lying down, a chain turning vertical, flow reading top to bottom -- and asked
for the plan to be re-evaluated and the engine restarted one component at a time. The evaluation
kept `D-103`, `D-100`'s standard and `D-106`'s principle; dropped `D-38`'s picture, `D-31`'s bands,
and orientation-as-cost (`D-102`, `D-105` item 4); and found the P5.1a hints `Rank`, `PortSides`,
`Loops`, `LoopOrientations` and `BranchShapes` to be the old engines in data form. `D-107` records
all of it. The state now: `28` rewritten as model / standard / rules / candidates; `29` the ladder;
`25` reduced to the nine hints that survive, the wire and goldens with it; `53` a renderer document;
a fresh `LayoutEngine` with `28` A6 (boundary stubs) and C1 (the first component at the origin);
the old engines parked in `~/fluidscript-attic/2026-09-16-engine/`. Step 1 is drawn: one pump,
two stub ticks, hard 0. `LayoutSolverTests`' routing and audit gates and `LayoutTimingTests` are
skipped until the ladder reaches the samples; `LayoutLadderTests` is the gate. `C-86` and `C-87`
were filed against the first build and stay open as observations for the ladder to answer.

**Step 1's correction came as four standing rules, not one picture note (`D-108`, 2026-09-16).**
Heat flows left to right and every flow loop runs clockwise -- promoted from `28` B's fourth
priority to hard constraints H9 and H10, which together fix a loop's source on the left flowing up,
its consumer on the right flowing down, and re-derive `R-48`'s rails for a closed ring that
`D-107` had withdrawn with `D-38`; an exchanger, a tank and a heat pump are mirrored and never
turned, as a transform class the catalogue carries (`free`, `standing`, `upright` -- the tank's
layers are why the last is not the second); every node, boundary nodes included, is laid out with
its box and outer boundary, withdrawing the stub-and-tick `28` A6 a session had decided; and a
layout is checked from its text, which moves into Core and gains a raster. `28` C now holds C1
(corrected: the first component is the heat source) and C2–C4 *stated*; `29` gained the planned
ten steps, each asking the user one question, ending at every sample. Two questions are left for
the pictures: whether an inferred node's boundary takes clearance (step 2) and which free-turning
members leave a loop's bottom for a vertical (step 3). Filed `C-88` (the audit measures four of ten
hard constraints), `C-89` (`SceneText` in tests, `62`'s second text withdrawn), `C-90` (no
transform class in the catalogue; `Transform.All` offers every kind eight).

**Step 6 was accepted on 2026-09-17 after three corrections and one decision (`D-112`).** The
cooling loop's first drawing put the three-way valve on the loop's right side with the loop passing
through it; the user wanted it at the top-right corner turning the flow, the junction at the
bottom-right corner, and the supply under the return. That became `28` C9 (a consumer that can turn
the corner takes it), C10 (a junction beside the consumer takes the bottom-right corner), and C7 and
C8 widened (a loop is one root for pairing open ends; a corner junction's free port goes level). A
second correction repacked the loop: C10 had cleared the consumer against a junction box that was
about to move. The session then had the sample state its ports so the loop would leave by the angle
port, and the user withdrew the reasoning: the switched ports are interchangeable on the drawing.
`D-112`: the symbol offers a `swapped` arrangement and C9 admits it at the corner, so the sample as
committed and the stated variant draw byte-identical geometry; the sample swap was reverted before
commit. Steps 1–5, the simple loop and the substation are byte-identical to what the user accepted;
the cooling loop draws with two bends. Open from the ladder: `28` open question 2,
`C-89`, P5.1e, the source-side corners of C10 when a step needs them.

**Step 7 (2026-09-17) drew the ring with one injection branch twice.** The first draw was the
plan's worked example, a vertical column with the pump turned to vertical; the user called it
technically correct and not the intended layout, and gave three rules (`D-113`): a pump is level,
a member on a side with slack sits at its middle, and an inner loop is laid out first as a block
by the same rules as the cooling loop, the header routing around the blocks afterwards. `28` C11
is now the block -- the first rigid group (A8) the engine builds, laid out at a provisional
origin and slid into the ring with its runs -- with C12 (centring) and C13 (level pumps, the
`level` transform class on the wire) beside it, and open question 2 answered for pumps. The
user then had the block present its inlet and outlet together to its parent -- the split
junction at the bottom-left corner, the return rail level from it -- and asked for loops to be
found recursively into nested blocks, which `UnitOf` now does. The third draw is audit-clean,
four bends, with steps 1–6 byte-identical, and the user accepted it. Every ring and block is
now a layout group when it is one component to the rest of the system -- one inlet, one outlet,
a tap that returns to the source's ring not counting (A8 as built: `loop-n`, nested, in the text
and as frames in the picture; not on the wire until P5.1d-3). `D-114`: a node with two
connections is inline whether declared or inferred, which closed the gap the datum node had put
between the source and the block. Step 8, the second branch, is where two blocks of one shape
must draw alike.

**Step 8 (2026-09-17) drew the header with two branches, in parallel and in series.** The user
asked for both scripts and that they pass before being drawn, so the ladder got a solve gate
(`EveryStepSolvesAndSettles`, `62`), which found `C-91` (a lone injection valve sized against the
whole ring) and `S-63` (two branches in series never converge) before a line was laid. The parallel
header is `m2-distribution-header` verbatim: the radiator block takes the ring's right side as
step 7's did, and the AHU branch hangs between the rails under `N3` and over `N5` -- `28` C14,
`D-108`'s branch rule in its closed form -- one bend to feed it and one to return it. The series
header puts the radiator block on the top rail with its outlet facing on and steps down into the
AHU block (C11 widened). Both are audit-clean with no soft findings, the two blocks congruent,
steps 1–7 byte-identical. With branches in the rules the 200-component header draws through them,
hard 0 and soft 0, so `LayoutTimingTests` is off skip at 56 ms best-of-five -- over `07`'s 30 ms
line, `C-92` -- and the distribution header's sample gates are live. The user accepted the series
picture and corrected the parallel one once: the junction a branch returns to stands directly under
the one that feeds it, as a loop's supply and return nodes align (C14 as it stands). Four branches
in series (8c, a staircase of blocks) and four in parallel (8d, three hanging and the last on the
rail) then drew with no rule touched, and the user accepted all of step 8; a parallel header with
one branch in series (8e) needed C14 to read a branch as a chain of blocks, and draws clean. Step 9,
the tank between two supplies and two returns, drew with no rule added -- the open fan is C5 from
the head, the tank upright, every pipe straight -- and the storage header's sample gates are live.
Step 10, a sensor and a controller on step 4's loop, drew with the first engine's instrument rule
and the user corrected it twice over: a signal leaves a sensor by a side other than its own
connection and reaches the controller in one bend with a whole stub, the sensor sliding along its
inline node's rail to make room (`28` C15); and at a crossing the route in front runs through while
the one behind breaks -- signals behind return behind supply, the hotter circuit in front when two
overlap (C16, the temperature rank open as `28` question 3). Every route carries a `layer` on the
wire now. The user accepted the corrected picture: every planned rung is climbed.

**Step 11 (2026-09-17) began the syntax tour.** Five independent circuits in one file drew as one
fragment and a fallback column (hard 71); the user's rule is that independent circuits go under
each other in script order, never side by side, and to approach the tour two loops at a time.
`28` C17: the graph's fragments, each laid out on its own canvas by the existing rules and stacked
one margin apart, left edges aligned, in the order the script declares them. Two loops draw clean
(11a, with the user) and the tour itself falls to hard 4 untouched. The solve gate found `C-93`:
the binder refuses two independent circuits in one project as one disconnected circuit
(`FS2213`), which the syntax tour, documented as not meant to be solved, had hidden. Step 11b
drew the tour's first two circuits and, through three readings with the user, made the code match
`D-114` (a boundary is never inline), widened C7 to every open end, and turned a valve placed from
a pipe so that the flow leaves it to the right; the tour's expressions circuit was rewired to read
as plant (`L-52` filed from the user's remark on supply nodes). Step 11c took the tour's two loops
with no heat source: `28` C18 lays a sourceless loop out as a ring from its consumer with a bare
left corner, and C1 reads a component's role from its written kind, so a load whose power is a
curve is a consumer before any curve is read. Both loops draw clean; the tour stands at hard 2,
all in the open supply-to-return form no rule draws yet (11d). `C-94` filed: a controller's
signal runs through the valve it drives when sensor and controller land on opposite sides of a
rail; `S-64` filed: wiring the tour's `ahu` between two stated pressures makes the seed run the
parallel load backwards, and the sample carries `# does not seed: S-64` (`62`). Step 11d, the same
day: the open supply-to-return form has its rule (`28` C19 -- the supply and the return are the
two left ends of a ring's rails, the chain under the supply, the looped path on the right), `C-94`
closed (a signal that would cross a box or run along a line is routed round them), and the syntax
tour draws hard 0, soft 0 and joins `Reached`; `C-95` filed for the three audit gaps the faulty
picture exposed. Every sample now draws audit-clean. **`D-115`, the same day, from the user's
reading of that picture:** the boundary kinds are `inlet` and `outlet` (`supply` and `return` are
a circuit's pipes), a boundary has exactly one connection (`FS2205`, an error), and a closed
circuit without a stated pressure is warned (`FS2201`). The rename runs through the keyword table,
`BoundaryRole`, the attachment statements, the wire's `inletAnchorId`/`outletAnchorId`, every
sample and ladder script, the docs page `inlet-outlet.md` and `22`'s tables; the tour and step 11b
write junctions after the inlet and before the outlet, C19 is keyed to them, and `S-64` and `L-52`
close. Core 1732/0/4, Api 18/0 with goldens regenerated. The tour's reading after that
(2026-09-17): a chain taller than the open form's block rebuilds the block deeper so the return
runs straight (C12), and a fragment's declared boundaries share one root so they align (C7); step
11b's one-path form is the chain alone with the outlet at its foot (C19). The whole ladder was
then drawn at `spacing 1`: every step and sample hard 0, soft 0 but the tank form, `C-96` filed.
Core 1734/0/4. P5.1d-3 on 2026-09-18 (the report in Core with its raster, `C-89`; the audit's three
gaps, `C-95`; the predicate sweep): Core 1796/0/4, Api contracts 16/0. P5.1e the same day: a
connection line ends in `name=value` pipe properties, held on the `ConnectionSyntax` and printed as
written; the binder declares one `pipe` per connection on the line (I7, `{A}__{B}`, created in step 1
so its parameters bind and evaluate like a declaration's) and wires the connection through it, I2's
nodes beside it named `{pipe}__in` / `__out`; the factory gives an implicit pipe with no `length` a
decided default of zero, and `Pipe` accepts it. Every sample, `01`'s three reference circuits and the
24 ladder scripts write their pipes on the line (49 pipes); the tour keeps `PA3` declared because its
far end is the `outlet` attachment's, not a connection's. The rule number: `11` already had I4 (flow
direction) and I6 (chains), so the implicit pipe is I7. Core 1805/0/4 (two rows more: the pipe page's new example block is parsed as a sample), Api 18/0, goldens regenerated; `C-98` filed for the test process's intermittent CoolProp crash at exit.

**P5.2 is the host in `FluidScript.Api` and one pipeline behind four routes** (2026-09-18).
`ScriptPipeline` is the whole of a request: the compatibility gate (`18`), parse, `07`'s
declaration and token limits, bind, the `solve` escalation of `FS1507`/`FS1511` to errors, the
catalogue pin, the substance, `OuterLoop.Prepare` for the unknown limit, the solve, then
`ModelContractJson`; `validate` returns after the bind, `solve: false` before the solver, and the
three share every stage above the one they stop at (`42` invariant 3). Over a limit is `FS4601` in
the model's diagnostics with the solver skipped, and only the byte ceiling is a status (`413`).
Sessions (`41`) are `(apiMajor, sessionId)` in a `SessionStore` with a thirty-minute idle eviction:
a new request on a session cancels the draft in flight, which is answered `499`, and a converged
solve is remembered as a `WarmStart` — the solution with a topology hash, sixteen hex of SHA-256
over the unknowns' `Kind:Owner:Name` — that `OuterLoop.RunAsync` takes as its seed when the next
script's hash matches, retrying cold when the warm start fails to converge. Measured on the cooling
loop: the warm compile takes the first solution as its Newton guess and converges in fewer
iterations; a whitespace edit warm-starts, a changed topology is cold. `metadata` is a lazily built
document with an ETag (a matching `If-None-Match` is `304`), covering every registry kind with its
parameters, aliases, families and units, every dimension, every diagnostic code live and retired,
the symbols, the catalogues, the property backend and the limits; `/openapi/v1.json` is generated
from the handlers. Request-level failures are RFC 9457 problem details: `400` naming the missing
`field`, `413` with `bytes` and `limit`, `500` with `FS9001` and a `correlationId`. The JSON schemas
of the model contract, the compile response and metadata are exported from the C# records and
committed under `Api/Contracts/Schemas` (`D-46` step 2), gated like the goldens. `edit` is P7.1's,
with the mutation API it fronts — decided with the user, no stub. The metadata test found `C-99`:
the exchanger's `u`, `ua` and `fouling` had been dimensionless in every model since their rows
were added, a static-initialisation-order slip, now closed, and the Api goldens moved on fourteen
`unit` lines. Filed: `A-1` (the equation system is prepared twice per solved request), `A-2`
(`docsIndex` is a path until the docs are served), `A-3`/`L-54` (base-unit spelling on the wire
for the two dimensions the unit table cannot name), `L-53` (`FS1503` spans the value with the
name). `docs/advanced/using-the-api.md` is the page. Core 1805/0/4, Api 55/0.

**P5.3 is `frontend/src/design` and the first frontend tests** (2026-09-18). `tokens.ts` names
every colour token in `55`'s order (with `--syn-function`, `--editor-bg` and `--editor-fg`, which
the list had left out, `U-3`), the theme-independent scales, the durations, the syntax opacities,
`D-73`'s advance widths and the contrast pairs. The two built-in themes are `themes/light.json` and
`themes/dark.json` in the public custom-theme format, and `themeCss.ts` renders `55`'s cascade from
them -- `:root` light, `[data-theme='dark']`, the same under `prefers-color-scheme` when nothing
chose, every duration zeroed under reduced motion -- into a checked-in stylesheet gated by a test
that regenerates it. `theme.ts` reads a custom file without throwing, fills its gaps per token from
the selected built-in naming them, measures every declared pair at WCAG AA and loads a failing theme
with the pair named; `applyTheme` is an attribute for a built-in and inline properties under
`custom`, so switching re-mounts nothing. The UI store of `51` exists with its `theme` field,
Zustand persisted to localStorage, and a rendered test rehydrates it. The primitives are `Button`,
`IconButton`, `Panel`, `Card`, `Badge`, `StatusDot`, `Toolbar`, `Tabs`; the four with drag or
positioning behaviour wait for a consumer. The scaffold's hero, logos and accent-purple CSS are
gone; `App.tsx` is the provider, a toolbar with the theme control, and a preview page for judging
a theme until P5.4's shell. The values `55` did not fix -- surfaces, text, borders, canvas -- were
chosen to clear the contrast test and have no other basis (the defects file says so). Vitest and
jsdom are the test runner (`62`, `63`); `npm test` is 25/0, `tsc -b` and oxlint clean, Prettier
clean. `docs/advanced/themes.md` is the page. Core and Api unchanged.

**P5.4 is `51` built around a text area** (2026-09-18). The shell is `features/shell`: `51`'s
toolbar (Solve live, Run, Stop and Export disabled until their packages), the tab strip from the
workspace store with the dirty and running markers, the editor-left canvas-right `SplitPane` whose
ratio persists, the log's slot listing the active document's diagnostics with `56`'s glyphs, and
the status line -- `● Converged · steady solve · plant_01` -- with a distinct glyph and word per
state. The four stores are `51`'s table: `draftStore` keyed by document with the last successful
model, the diagnostics, the revision each came from and the solve status, guarding against a stale
response by revision; `runStore` with a run's id, snapshot hash and status and no model object;
`workspaceStore` persisted as ids and names, capped at eight; `uiStore` with theme, split and the
log's fold. The text of every document lives in `features/editor/documents.ts`, a map outside
React state (`51` invariant 1), read by the pipeline when it sends. `api/client.ts` is the typed
`compile`/`solve`/`validate`/`metadata` over `fetch` with problem details as `ApiError`; the
types are `api/types.generated.ts`, produced by `npm run types` from the Api's three schemas and
gated by a test. For that to name anything, the Api's exporter now adds every object's `title` and
every documented member's `description` from the XML documentation files (`SchemaDocumentation`,
`D-46` step 4; the schemas regenerated, 781 lines added), and the frontend's generator hoists the
inlined records into `$defs` so one interface comes out per title. `CompilePipeline` is the
debounce: every edit restarts the timer, the request in flight is aborted when the next fires, a
stale answer is dropped, a failed compile keeps the model, a tab switch cancels the outgoing
document's work; `LatencyTracker` switches the 100 ms `validate` phase on above a 100 ms p95 and
off after fifty compiles under 75 ms, and its diagnostics yield to the compile's for the same
revision. Every one of `51`'s request-stream criteria is a test on a hand-driven clock; the shell
test types into the text area, watches the status line, switches tabs and reloads the split. The
debounce is 300 ms and unmeasured until P5.5's benchmark (`U-4`). Smoke-tested against the live
host: the cooling loop cold 906 ms / warm 24 ms over HTTP, 413 with its problem details, metadata
13 kinds and 133 codes -- and the warm run's `iterations` was 3 against the cold run's 2, filed as
`A-4`. `docs/advanced/working-in-tabs.md` is the page. Frontend 45/0, Api 55/0 with the schemas
regenerated, Core unchanged.

**P5.5 is `52` on CodeMirror 6** (2026-09-18). The grammar is a `StreamLanguage` over
`features/editor/language/tokenizer.ts`, a line-at-a-time port of the lexer's rules with the section
as state and roles by position; `52` says why it is not a Lezer grammar. Its lexicon -- reserved
words, unit symbols longest-first, `D-15`'s thresholds -- is `LexiconWire`, exported by the Api's
schema test to `Contracts/Schemas/language.json` and generated into `lexicon.generated.ts` by
`npm run types`, gated on both sides. The agreement test is `TokenGoldenTests` in Core, one golden
per sample with every token's offset, length and kind, replayed by `tokenizer.test.ts`. Diagnostics
are `@codemirror/lint`'s set wholesale per compile, infos skipped, the hover carrying the code and
the related places, a `suggestion` as an **Apply fix** action that is one transaction; a fix the
document moved from under is skipped. Completion is `completion/completion.ts`, a pure function of
the line, the metadata and the model: the `D-15` scorer ported from `NameResolution` and asserted
against its figures, kinds alias-aware with Tab and Enter inserting the canonical keyword, parameters
with `dimension · unit · typically a…b`, values filtered by the parameter's dimension over lets,
`Component.property` references and unit symbols, tank families with materialized ports and the next
template, all twenty-two of `52`'s cases against `Api.Tests/Contracts/Goldens/metadata.json`. For
that the wire's `bindings` gained `dimension` and `siUnit` (`26`), the metadata orders by name and
converts ranges to the canonical unit (`A-5`), and `sensr` is a real ambiguous pair (`p_sensor`,
`t_sensor`; both listed, first preselected, `U-6`). The formatter is `Formatter` in Core (`17`
records the layout as this project's reasoning: no indentation, zero-or-one spacing kept, `=` tight
for a parameter and spaced for a `let`, trailing comments and `let` names aligned within a run,
blank lines, full-line comments and curve rows untouched), idempotent and token-preserving over the
corpus, served by `POST /api/v1/format` as `TextEdit`s and applied by `Shift+Alt+F` as one
transaction. Go to definition is `Ctrl+Click` on the model's `sourceSpan`; `Ctrl+/` toggles `#`;
`Ctrl+Shift+Enter` flushes the pipeline as a solve. `D-48`'s benchmark is `frontend/e2e/latency.bench.ts`
on Playwright, driving a dev-only `window.fluidscript` hook and writing `diagnostics/keystroke-latency.md`;
Chromium's headless shell needs `libnspr4`/`libnss3` that this machine cannot install without
root, so it has not run and the debounce stays provisional (`U-4`). `docs/advanced/the-editor.md`
is the page. Frontend 88/0, Api 58/0, Core 1820/0/4 (the model-contract page regenerated for the
two binding fields).

**P5.6 is `53` drawn from the wire and nothing else** (2026-09-18). `features/canvas/scene.ts`
turns `layout` and `symbols` into the prepared scene: a placement with a box is a symbol with the
Core instrument's transform, an inline one (`D-105`, a zero box) is a mark or nothing, routes are
sorted from the back and cut around their hops, arrows come from `layout.flow`, labels are the tag
or the id, badges from the diagnostics addressed to a component and a hollow square for a sized or
defaulted value. `SceneView` is the one drawing implementation: the pane renders it under a root
`translate scale(k -k)` and the golden test renders it to static markup, one SVG per Api sample
under `canvas/goldens`, regenerated in place and failed once like every gate. `viewport.ts` is the
CAD arithmetic (fit with 5 %, zoom about the cursor, reset, the grid step), `CanvasPane` the pane
with `R-22`'s axes, the grid and `53`'s four detail levels. The `state` fill slot is `57`'s flat
fill through `fluidFill` in `tokens.ts`, pulled forward with the user's agreement so the first
pictures read as a plant. The four samples were drawn through the Edge route and looked at: the
loops, rails, mirrored exchangers and inferred nodes come through as the ladder's pictures; the
one thing seen and filed as an observation is the exchanger label sitting on its top edge, which
is the catalogue's anchor, not the renderer's. Not done: the worker (`53` says why, M4), collapse
(`U-7`), the frame budget (no browser). Frontend 115/0, Core and Api unchanged.

**P5.8 is `56` and the reading half of `54`** (2026-09-18). The log is `features/log`: `logModel.ts`
is pure -- entries keyed by code and component, grouped at three of one code and severity with the
members' own messages under the line, ordered errors, warnings, infos, closed by `✓ Solved · n
iterations · t ms` or the failure line; the filter (warnings and errors by default, the info count
shown), the text filter, the header's phrase per `56`'s table and copy as text -- and `LogPane`
renders it as a list whose keys are the entries' keys, so an unchanged entry keeps its element
across compiles (asserted by identity), a resolved one fades for 150 ms, and the list follows the
bottom only when it was there. Hover is `features/hover`: `card.ts` builds a component's card
(every parameter with `stated`/`sized`/`default` and the basis under a chosen one, the state rows,
the warnings, the inferred note), a connection's (flow, and the pipe behind the line with its Δp)
and a `let`'s; `HoverCard` is the one body, mounted by the canvas in the new `Tooltip` primitive
after 150 ms and by the editor through CodeMirror's hover tooltip, where a quantity shows its
dimension and canonical unit. Selection is `state/selectionStore.ts` with the origin of the last
change: canvas click and Shift+click, Escape, the caret on a declaration, the log's component
column; the editor marks the declaration's line and scrolls when the change was not its own. The
wire needed one change: `ModelContractBuilder.Diagnostics` attributes a diagnostic that names no
component to the declared component whose span holds it, because only six producers set the name
(`L-55`); the badge, the card and the log's column all read it. `docs/advanced/the-log.md` is the
page; the canvas and editor pages gained hover and selection. Frontend 134/0, Core 1822/0/4, Api
58/0.

> **2026-09-20 — the planning process itself.** After a segment that closed `S-53`/`S-55`/`S-56`,
> built the solver's replay (`S-71`) and the layout's provenance (`C-107`), the user asked what in
> the planning system works and what does not. Answered by measuring it: the registers gained
> Effort / Risk / Basis on open rows and actual Effort on closed ones, a `Next id` line, and a
> `Traps` section between Open and Closed for what a session gets wrong twice; the decision log
> gained a generated index; `08` owns the columns and the grep-before-filing / re-read-before-
> reopening / measure-before-closing rule; `check.py` verifies all of it — and turned out to have
> been crashing since `08` and `09` moved to the plan root, which is `F-27` with the 38-citation
> backlog it had hidden. `C-64` was found stale under `D-122` and marked so rather than reopened.
> Nothing in the code changed except one comment. The procedure itself — reads, commands, the
> filing / reopening / closing steps, package and phase close — is now `65-working-the-plan`, which
> `CLAUDE.md` points at; the same runbook went into `agent-planner` as a template.

> **2026-09-20 — sweep tier 4b, the rows the columns called fixable.** Seven closed by the runbook,
> each on its own script with the whole report read, solver first: `S-70` (a coupled exchanger's two
> rings named by their own elements, each stated flow by its own pump, the report's direction read
> off the port map -- eleven rows across the corpus had been labelled *reversed* for pointing against
> the walk); `S-23` (a dead leg's nodes take the live end's enthalpy, the closure `S-56` built; the
> simple loop with a stub converges instead of going singular at `N9.h`); `S-66` (the seed's walk
> reads the promoted Kv and head -- substation seed pivot ratio 3e-6 → 1.4e-2, `head15` 6 → 4
> iterations; the position deliberately not, measured to cost 1–2 first-pass iterations on every
> three-way circuit; the datum-at-0 re-seed measured again and withdrawn again for a different
> reason). The report gained the seed's conditioning beside the solution's and the iterations per
> sizing pass. Layout: `C-87` (a pipe along an unrelated box's edge is soft; a sensor's clearance
> excused), which surfaced `C-108` (header consumers pack at exactly one margin -- `08d` soft 6,
> `header-200` soft 48 -- a spacing for a picture judgement); `C-86` (the flank lead is two margins,
> so a primary's approach and the secondary's rail no longer share a line across the exchanger);
> `C-96` (a terminal node's clearance yields to a sibling run of the same symbol; `LayoutDiagnostics`
> now draws every `diagnostics/scratch/*.fluid`, the layout half of the circuit harness); `C-102`
> (C18 seats any boxed member but the head, ladder step 3d, the `FS5002` guardrail moved to
> `N1 - N2 - N1`). Six rows re-triaged rather than fixed: `C-91`, `C-54`, `C-38`, `C-36` are the
> user's decisions, `C-47` waits on `D-82`, `S-42` got harder by its own account. Core 1936/0/3,
> Api 59/0. Next: **P5.13b** items 1 and 3.
> **P5.13b items 1 and 3 shipped 2026-09-21.** `L-56`: `ComponentConstraint` carries the key and
> the spelling, the report spells its constraints, equations, sizes and branch ends, the wire keeps
> its keys. Item 1 measured first what a stated interior `p` does (datum when alone in a closed loop;
> `FS2210` naming the wrong row when two; over-specified on an open primary with the valve already
> promoted) and found `HX1 in.p=300` bound as `in.t=300` under `FS1512`; then the user's rule became
> `D-124`: a port's `p` is the touching node's, `V1 valve out.p=100` *is* `N1 node p=100`, generated
> for every port, copied onto the node by the binder, `FS1539` when stated twice, `FS1538` for a
> quantity the port lacks and never a near miss, `FS2210` naming `PU1 in.p, PU1 out.p`. Probing
> `D-120`'s example read found `L-59`: `SemanticModel.Deferred` has no consumer, so
> `head=1.2*HE1.dp` sizes the head as if absent and says nothing. One closed, one opened; 58 open.
> Core 1948/0/3, Api 59/0, frontend 223/0.
> **`L-57` closed** (the same day, the user's call): `D-125`, a bare `vflow` is l/s, the sixth
> exception row. 57 open.
> **P5.13c -- `L-59` closed** (the same day, the user's call to build the fixed point now): each
> pass evaluates `SemanticModel.Deferred` and writes the results in as stated values; `HE2
> in[2].t=HE1.out[2].t` is 67.146 °C at pass 1 and settles at pass 2. Building it found that the
> flagship `head=1.2*HE1.dp` cannot type (`L-60`), that no pressure-delta literal exists (`L-61`),
> that a target the seed cannot supply leaves pass 1 without it so the walk to `FS1405` has no
> script yet (`L-62`), that a connection-line node is not a name to an expression (`L-63`), and that
> a valve's stated `dp` is read by nothing (`C-109`). `S-58`'s valve observation went to the docs as
> a paragraph, no row. One closed, five opened; 61 open. Core 1955/0/3, Api 59/0, frontend 223/0.
> **`L-60` closed by `D-126`** (the same day, the user's call over the stated risk): a `head`
> parameter accepts a length, `pi` and `g` are reserved constants (`FS1411`), `Acceleration` and
> `m/s2` exist, and `FS1304` on a head spells `dp / (rho * g)`. `14`'s worked example finally runs:
> 2.4522 m from the seed, the valve promoted to Kv 7.01, one pass. The seed now supplies a kind's
> decided default too. `C-109` widened to the pump's `dp` (stated, written in from an expression,
> and read by nothing). 60 open. Core 1963/0/3, Api 59/0, frontend 223/0.
> **`C-109`, `L-63`, `L-61` closed** (the same day): a pump's stated `dp` is the rise its equation
> holds, with the head reported at the solved inlet density and the seed reading the rise; a
> valve's stated `dp` chooses the next larger Kv row at the design flow (Belimo's rule, cited in
> `24`); rule I1 runs with the declarations so `N3.t` reads a connection-line node; `dPa`, `dkPa`,
> `dbar` spell a pressure difference (`D-127`). 57 open. Core 1971/0/3, Api 59/0, frontend 223/0.
> **`C-91`, `C-54`, `C-38`, `C-36` decided and closed** (the same day, the user's four calls):
> `C-91` was already `D-122`'s -- the one-branch ring converges sized (Kv 4, 4.71 m) and the ladder
> script drops its stated Kv; `C-54`'s velocity ceiling is documented as a guard; `D-129` ships
> copper as the Finnish type-approved range, ten rows with two Finnish listings each, and
> `SOURCES.md` is rebuilt (it had been committed at 169 404 lines); `D-128` gives a pipe
> `material=` to read its `dn` in another shipped series. `C-110` filed for the unsourced
> `steel_en10220`. Four closed, one opened; 54 open.

> **`70`, the Core refactoring, R0–R5 shipped** (the same day, the user's call to execute the plan
> as the session decided it; `D-130`, `D-131`): the architecture review found one question --
> who owns a parameter -- answered at eleven sites, a pump's head derived three ways, the ring forms'
> rollback written three ways and each restoring a different subset, and `ρ g h` at twelve sites.
> `70` records the diagnosis; the packages landed in nine commits, each byte-identical on the corpus,
> the ladder and the goldens except R2's four pump-head digits, which closed a disagreement the
> cooling-loop golden already carried (2.48434 sized against 2.48435 solved). Restoring the register
> on the way found five closed rows the day's earlier closures had dropped. `C-111` stays open on the
> language decision; R6 waits for a feature to open the binder. Nothing closed, nothing opened; 54 open.

> **`C-93` closed by `D-132`** (the same day, advancing on the session's recommendation): two
> circuits joined by nothing are two systems. Measured before deciding: with `FS2213` silenced the
> ladder's two-loops script counted 30/30 and converged in 4 iterations with a datum and a level per
> hydraulic, so the error was the only barrier. `FS2213` is information now; step 11a solves and its
> marker is gone. One closed; 53 open.

> **`L-62` closed** (the same day): a deferred line the seed cannot supply is absent from pass 1, and
> when that pass fails the report now says so -- `FS1412`, the line and the pass -- instead of
> `FS1410`'s "not published by any pass", which was false with no pass completed. The refusal exit
> carries it too; it had carried nothing. One closed; 52 open.

> **`S-45` closed by `D-133`** (the same day, the user's call: options B and D): a promotion's
> candidate lists now hold only what can move the quantity -- a split for the temperatures on the
> stream it mixes, walked along nominal flow to the next exchanger; a pump for the flows on a cycle
> through it; a leg split for its leg's flow -- and `D-130`'s greedy pass is followed by an augmenting
> matching (Kuhn) that reassigns only to rescue a constraint the pass left, reporting what stays
> unmatched as its Hall group. The injection header with `HS1 in.t=40 out.t=80` now names the four
> statements that share three actuators instead of blaming the radiator that came last; a parallel
> pair with one valve on the first-declared branch counts square instead of asking for a valve on the
> index branch. Corpus and ladder otherwise unchanged (`m1-syntax-tour` over by 4, not 3: the
> promotion it lost was the reach). `FS2218` is information. Opened `S-73`: that square parallel pair
> does not solve from the bootstrap seed. 52 open.

> **`D-134`, dates on the registers** (the same day, the user's question: when did each defect
> emerge?): every open row carries `Filed`, every closed row `Filed` and `Closed`, ISO dates written
> once and never edited; no *modified* column, because a field every edit must touch drifts and git
> holds the row's change history exactly. All 284 rows across the seven registers were backfilled
> from the registers' 191 commits (first commit carrying the id; last commit moving it to Closed).
> `08`, `65` and `check.py` carry the rule; the agent-planner templates the convention came from were
> changed to match in their own repository. The solver register's three rows that sat between the
> Closed table's header and its separator were moved below it while there.

> **The property-backend measuring package** (the same day, the user's call: measure before designing):
> `PipelineTimingDiagnostics` now times the Jacobian as `NewtonSolver` builds it, one
> `TryEvaluateScaledAt` per column, and keeps the old N+1 extrapolation as a *naive* column beside it;
> `StateTimingDiagnostics` splits a SharpProp water fix into the update and each property read. Run on
> release and debug. `C-68`'s 99.98 % was the extrapolation: the header's real Jacobian is 2.7 ms
> against 47.8 naive, a Newton iteration about 4 ms, and the whole water solve 52 ms against 5.4 on
> constant properties, so two thirds of the property cost sits outside the Newton step, where the
> per-solve cache `21` specifies would catch the repeats. Below `ISubstance` the flash is the cost --
> `(p, h)` 138 µs, `(p, T)` 9 µs, the seven reads about 10 µs. `F-19`'s 705 µs from earlier in the day
> was measured with the test suite running. Both rows re-measured and re-triaged to medium; neither
> closed. The same harness then measured the other CoolProp backends behind SharpProp on the
> pair a solve uses: `IF97::Water` fixes (p, h) in 5.8 µs against HEOS's 142, within 1e-4 of it on
> enthalpy and 5e-4 on cp; the (p, h)-gridded tables in under a microsecond but 0.8 % off on
> viscosity. Which backend `Water` measures through is a decision the user has not yet taken. The
> refrigerants pay 23 to 77 µs per (p, h) on HEOS with no IF97 to move to; the glycol brine 9.7 µs on
> INCOMP; humid air 423 µs through `HAPropsSI` with no alternative at all.

> **`S-73` closed, `S-69` advanced** (the same day, the user's call to make it work before making it
> fast): the parallel pair's failure was a `dt` row reading a load's power as written (+20 kW) rather
> than as carried (-20 kW), so it demanded the load heat its stream -- only the substation's explicit
> `power=-150` had ever met the row -- and then a promoted balancing Kv with no sibling to seed from.
> Fixed both; the pair converges in two iterations to Kv 4.1 and 10 kPa. The series ring's seed now
> partitions each unrated coil from the ring flow the closed field found and seeds the promoted powers
> from the stated temperatures (residual 1.52 to 0.185 at the seed); its solve still fails in the sizing
> passes, which `S-69` now names as what is left. Corpus byte-identical. 52 open.

> **`C-111`, first part** (2026-09-21): `FS4011` after every converged solve, on a three-way valve
> whose two legs sit further apart than its own full-open drop, naming the balancing valve practice
> puts on the easy leg with its drop and Kv at the solved flow. Measured on the series header: 32.0 kPa
> against 7.6 on `TV_RAD`, Kv 1.53 between `NM_RAD` and `TV_RAD.b`; the AHU's valve silent. The row's
> guess that the pump would fall to 3 m was wrong and is corrected there: the balancing valve buys
> travel, not head. The line is project reasoning, marked in `24`. Fires on every bare bypass in the
> corpus. **Closed the same day with the second part**, the user's choice: a `valve` with no `kv`
> on a three-way valve's switched leg is set each pass to level the legs, unrounded (a balancing
> valve is set, not selected; TA STAD cited in `24`). One-branch ring: Kv 0.75, `TV_AHU` from 0.79 to
> 0.674, both legs 6.5 kPa; parallel header both blocks; cooling loop's diverting valve at its split.
> The series header's cold first pass creeps to its cap with the valve in place, and the stated-Kv
> sweep (5 iterations at 2.95, 46 at 2.94) puts that on the solver: `S-74`. 52 open.

> **`S-74` closed** (2026-09-21): the Jacobian's pressure columns were perturbed by 4 mPa, inside the
> (p,h) flash's iteration noise, so every temperature row's pressure entry was noise ten times its
> value; `FlashNoiseDiagnostics` measured it and `newton.fd_step_state` (1e-5) clears it, with the Kv
> laws kept at √ε because their √Δp may sit under a pascal. The series header's sweep converges in 5-6
> everywhere, the balancing valve settles on it (Kv 1.53, `TV_RAD` 0.501), nothing regressed and the
> substation lost an iteration. Changing the last digits of every solved value flipped the colour
> scales' nice bounds on two goldens (a -200 kPa pressure floor from a -7e-10 kPa value): `C-112`,
> filed, goldens regenerated. 52 open.

> **`C-71` closed by `D-135`, `C-69` closed as superseded** (2026-09-21): `leakage` on
> `three_way_valve`, 2 % by default (Belimo's B-AB class I), `0.05%` for a rotary body, floored at
> class IV because a stopped branch at zero leakage has no temperature (measured singular). The
> stopped AHU at 0.05 % passes 0.0002 kg/s and converges in 3 passes. `C-69`'s three pieces each
> went to a decision already taken: `D-122` (linear legs), `C-111` (a valve on the leg), and the
> rejected default. 50 open.

> **`C-65` closed by `D-136`** (2026-09-21): `mixing_valve` and `diverting_valve` now declare the
> body's service, `FS4012` names a valve the solve runs the other way (the cooling loop written as a
> mixing valve: "runs it diverting: 0.239 kg/s enters at ab and leaves 0.076 by a and 0.163 by b"),
> and a bare `three_way_valve` claims nothing. 49 open.

> **`C-64` closed by re-measurement** (2026-09-21): under the band rule the cooling loop's valve is
> sized for 12 kPa and drops 4.3 / 23.2 kPa across its legs bare, 11.5 / 11.5 with `C-111`'s valve on
> the return leg -- the 28x was the authority rule's, the 2x is the bare bypass and `FS4011` reports
> it beside the design drop. The programme continues: `S-47`. 48 open.

> **`S-47` closed by measurement** (2026-09-21): the seed's per-valve cap is `seed.valve_excursion`,
> 15 kPa, the top of ESBE's selection band, held equal to `three_way.dp_max` by a test; swept at
> 10 / 15 / 40 / 100 / 300 kPa and at the pump's shut-off head, only 10 and 15 are green, and the
> circuit-derived bound the row proposed stalls the switched-off consumer behind a main pump. The
> clamp no longer reaches sizing's branch-drop totals, which read solved flows. The user's three-way
> programme is complete: `S-74`, `C-71`, `C-69`, `C-65`, `C-64`, `S-47`. 47 open.

> **`C-112` closed** (2026-09-21): a colour scale's ends are settled to the legend's precision before
> they are niced (`ScaleDomain.Settle`): six significant digits of the larger end, and zero under the
> solve's own resolution for pressure and flow. The storage header reads 45 to 60 °C instead of 45 to
> 65 and `all 0 kPa` instead of `all 6.55e-10 kPa`; the substation's floor no longer depends on the
> sign of a nanopascal. 46 open.

> **`D-137`: water on IF97** (2026-09-22, the user's call): `PropertyBackend` builds water on CoolProp's
> IF97 backend, refines its backward T(p, h) with one forward step, and computes the saturation line
> in closed form (`If97Saturation`); the refrigerants stay on HEOS. The header's water solve went
> from 73 to 17 ms in debug. Every golden moved in its fifth digit, and the ladder's series header
> slid a metre along a valley: `S-37` is now measured (pivot ratio 2e-5; the two blocks' pumps share
> a pumpless ring's head), the ring tests pin flows and Kvs and band the rest, and `C-68` and `F-19`
> carry the new numbers. **`C-110` closed** the same day: `steel_en10220` ships verified, DN15--DN300
> at the walls Dahl and Onninen stock. 45 open.
>
> **`C-84` closed 2026-09-22 (`28` A11):** a label is a box from a declared metric, laid out last
> against symbols, lines and the labels before it, sliding along its owner's edge and around its
> sides until clear; when nothing is clear it keeps its place with a leader (`LabelClear`). The wire
> is `2.1` (`labelBox`, `labelClear`, `layout.labelMetric`), the canvas's font table is held equal
> to the metric, the audit counts three soft label findings and the ladder and samples are all
> clear. Estimated big, took medium. 44 open.
>
> **`S-42` closed 2026-09-22:** the square-and-singular fixture exists. `PU2 pump flow=0.239` beside
> `HE1`'s duty pair on the simple loop with two pumps in series promotes both heads, counts square
> at 15 and is rank 14; `FS3009` and `FS3010` fire from the script with the heads and the two flow
> statements named, and `NullDirectionTests` holds both messages. The probe also measured `S-37`'s
> valley on the smallest ring there is: one head promoted and one sized never settles. 43 open.
>
> **`L-35` closed 2026-09-22:** a reference may carry a unit -- `power=heating W`, `flow=demand kg/s`
> -- taken under the lexer's own rule for a number and applied to a bare value only. 42 open.
>
> **`U-6` and `T-2` closed 2026-09-22:** an ambiguous completion list opens on the typed text
> itself, which inserts nothing, and the properties page is held to the resolver -- every spelling
> the registry offers a reference must resolve and have a row. 40 open.
>
> **`A-6` closed 2026-09-22:** unit factors and offsets travel in the metadata, so the quantity hover
> converts; a pipe's state carries the velocity and Reynolds number its drop was computed at, from
> the mean properties the residual used, and the connection card shows them. Contract `2.2`. 39 open.
>
> **`U-5` closed 2026-09-22:** the binder types a deferred `let` from the units and properties its
> expression names, the wire carries the dimension, and completion filters it. 38 open.
>
> **P6 readied 2026-09-22** (`.claude/plan-review/findings/2026-09-22-P6-transient-readiness.md`,
> pass 7): the transient's design was sound and its assembly unspecified — `33` integrated node
> enthalpies the built graph gives no volume, nothing said what a stated `out.t` means at t > 0, the
> setpoint played no part at t = 0, two `IController`s shared one name, the tank's run-time residual
> set and the `ISolver`/`ITransientSolver` seam were unstated, and the reference circuit did not exist
> as a sample. Measured: `01`'s demand-step loop does not solve as written (valve at 1, pump to 62 m,
> non-finite) and does on the cooling loop's figures once the setpoint constrains `N2` (`S-75`); a
> discretized pipe loses its stated `dn` (`C-113`). The user's calls became `D-138` (sizing sweeps a
> driver range, the envelope per kind — a plant with a heating end and a cooling end, and an internal
> exchanger that peaks between them), `D-139` (partition by volume, each step the pinned steady
> system), `D-140` (a stated value is a design point; following it takes a `control` line) and `D-141`
> (a run starts from its design state; an unstated actuator's setpoint is its constraint; the cold
> start is `S-76`, an opt-in later). `08` gained P6.0 before P6.1 and P6.8 after P6.7; `33`, `34`,
> `31`, `43`, `36`, `05`, `22`, `24`, `15`, `12` and `01` carry the rest; `samples/m4-demand-step.fluid`
> is committed with its token golden and counted square (it entered `CorpusStatusTests` with P6.0).
> `C-114` (exchanger hold-up) and `C-115` (storage from a profile) are the user's two further points,
> filed as hunches. P7 (M5, the mutation API) was checked and is implementable as written. 43 open.
>
> **`S-37` closed 2026-09-22 (`D-142`):** the pumpless series ring is a valley, not a point, and the
> solver says so. Two outside analyses the user brought answered the parallel manifold, where the
> built code is already full rank; the series ring is the shape both call undetermined. `FS3016`
> names the sizes sharing the Jacobian's weakest direction at a converged answer under
> `jacobian.valley_tol`, the report prints the direction, and stating one head was measured to move
> the valley to a balancing valve rather than close it. No new language. 42 open.

### P6 — M4, the transient · in progress

| # | Package | Commit(s) | State |
|---|---|---|---|
| P6.0 | The transient assembly on the steady system ([`33`](30-solver/33-transient-time-domain.md), [`34`](30-solver/34-controllers.md)): `SystemLayout.Differential` partitioned by volume and `EquationSystem.Pin`/`Freeze`/`Release` (`D-139`, `D-140`); a `control` line's setpoint as a constraint source on an unstated actuator, `CircuitGraph.Setpoints`, `FS3210`/`FS3211` (`D-141`, `S-75`); `FS3016` on the way in (`S-37`, `D-142`); `C-113`; `CircuitGraph.Schedule` in SI, `TransientSettings`, `RunSnapshot.Create` with the design state, the differential and promotion initial vectors and the tank profile, `SnapshotId`; `FS3109`; `samples/m4-demand-step.fluid` in `CorpusStatusTests` | `cb2b959`, `2bc5a4c`, (this commit) | Shipped 2026-09-22; `S-75`, `C-113` closed; `S-76`, `C-114`, `C-115` stay open; not built: `FS3110` (no per-circuit modes on the graph), `RunLimits`, `ControlBinding`, the `SnapshotId` struct — each named with its package in `33` |

> **P6.0 shipped 2026-09-22** in three commits. What it meant to do: give the integrator a system to
> pin, a state to start from and a snapshot to own, without integrating anything. What it found:
> the pinned view at rest is the equilibrium (a design state stays put in 0 iterations with every
> cell pinned and every promotion frozen — V9 with the integrator stubbed to zero); the demand-step
> loop carries two promotions at t = 0, not one (`PU1.head` chosen by `HE1.out.t` as well as the
> valve position chosen by the setpoint), and both freeze; the storage header's five layers start
> at their stated 25–60 °C while the design solve holds `T1.h` at their mix, which is `33`'s
> invariant 3 rather than a contradiction. Fixed on the way, not filed: the binder filled both ends
> of a single scheduled value over a span with that value, which is a hold and not the step at the
> span's end `12` describes — a step now carries no start value. 42 open.

| P6.1 | Transport delay and time integration on a fixed graph ([`33`](30-solver/33-transient-time-domain.md)): `TransientSolver : ITransientSolver`, Heun with the adaptive controller, the CFL limit from reference masses, landing on every scheduled and frame time; `EquationSystem.TryEvaluateRates` (the balance the pin overwrote, divided by the mass), `.Schedule` (a stated parameter's table or `Freeze`), `NodeDensity`; `RunSnapshot.ReferenceMasses`; `TransientFrame` with `Differential`, `Steps`, `Settled`, `EnergyDrift`; `TransientSolver.SteadyAt` for invariant 8; the tank layer balance with interface flows (no remix); `FS3101`–`FS3107` less `FS3108`; `docs/advanced/discretized-pipes.md`, `pipe.md`, `schedule.md`; `TransientRunFixture` writing `diagnostics/transient/*.run.txt` | (this commit) | Shipped 2026-09-22; `S-77` (a boundary state cannot be scheduled; `FS3105` refuses it), `S-78` (three exits unprovoked), `S-79` (the live-curve half: where t = 0 sits) opened; `FS3110` still waits for per-circuit modes |

> **P6.1 shipped 2026-09-22.** What it meant to do: integrate the states P6.0 partitioned, on the
> pinned system, landing on every scheduled and frame time, and measure `33`'s worked example. What
> it found, in the order it was found: `33` said frames were interpolated and said no step straddles
> a frame time, and only the second can be true — landing caps the step at the 1 s frame interval,
> so the demo is 750 steps rather than 70 and 1.8 s of Debug wall time, which is fine; the derivative
> is the energy balance the pin overwrites, captured before the overwrite, so the integrator and the
> algebraic rows are one evaluation and the drift accumulator reads 2e-11 on every run; `33`'s
> closed-form table holds for three rows and then the recirculation feedback lifts the source — the
> exchanger's outlet climbs from 65.1 to 72.2 °C as the warmed front returns to `N2`, so the rows
> past 90 s are above the table on a correct run and the settled state is the post-step steady
> solve, held by V8 to 1e-3 scaled; a schedule on `N1.t` bound, lowered, and would have been silently
> ignored, now `FS3105` (`S-77`); and the live-curve half P3.8 moved here has no t = 0 anchor
> (`S-79`), a user's call. Not built: the tank's remix (P6.2), `FS3108`, `FS3110`. 45 open.
>
> **`D-143` accepted 2026-09-22, superseding `D-138`:** a plant is sized for a stated list of named
> scenarios, not for a swept driver range. The user's premise is that duties are inputs — occupancy,
> lighting, equipment, structure — and not functions of outdoor temperature, so the sizer must never
> derive one from a driver nobody wrote. `scenarios winter summer` declares the cases, a parameter
> states one value per case as an array, a scalar means all of them, any other length is `FS1540`
> with nothing padded, and `design <name>` names the operating scenario and sizes nothing. The
> pipeline is four steps, not three: solve each scenario sizing freely, merge parameter by parameter
> under the kind's rule, **re-solve every scenario against the merged sizes frozen** — none of the
> first solves is a state of the merged plant, since the merged sizes exceed what any of them used —
> then draw the merged plant with `design`'s numbers. What it gives up is the interior peak: a file
> naming only winter and summer sizes a recovery exchanger to zero in both, which is the case this
> line of work started from, and `24` records the two candidate mitigations. `C-116` and `C-117`
> closed; `12`, `15`, `24` and `08`'s P6.8 row carry the detail. 43 open.

| P6.8a | **The scenario language** (`D-143`, [`12`](10-language/12-grammar.md), [`15`](10-language/15-semantic-model.md)): `ReservedWord.Scenarios` and the `scenarios` directive; `ScenarioListSyntax` as a `parameter-value` admitted only after `=`; `design <name>` beside `D-58`'s `driver=value`; `FS1120`/`FS1121` in the parser and `FS1540`–`FS1544` in the binder; `ProjectSettings.Scenarios`/`DesignScenario`, `ParameterValue.Scenarios`, `ValueId.ScenarioParameter`; `ScenarioProjection.Project`; `docs/functions/scenarios.md` | `aa93675`, `99794e2` | Shipped 2026-09-22; `L-64` closed in the same change. Nothing consumes the list yet — that is P6.8b |
| P6.8b | **The scenario sizing pipeline** (`D-143`, [`24`](20-core-domain/24-auto-sizing.md) §Sizing over scenarios): `OuterLoop.Freeze` and `PreparedModel.Frozen`, `Prepare(from:)`; `ScenarioEnvelope` with a closed per-parameter rule set; `ScenarioSizing.SizeAsync` — project, size, merge, re-size against the merge until it settles, then solve every case frozen; `ScenarioExplanation` and `diagnostics/scenario-sizing.md`; `samples/m5-scenarios.fluid` | `dbd53a0`, (this commit) | Shipped 2026-09-23; `C-120` and `C-121` opened. Not built: the valve turn-down check and the zero-envelope diagnostic, both waiting on `C-121` |
| `C-121` | **Valve authority and turn-down across scenarios** ([`24`](20-core-domain/24-auto-sizing.md) §Sizing over scenarios): the frozen solve reads every control valve (`OuterLoop.Readings`, `ThreeWayContext` shared with the sizing pass, `ValveSizer.Achieved`) onto `OuterLoopResult.Valves`; `ScenarioSizing.Controllability` reports the lowest authority across cases with the case named, `FS4006` on it, and `FS4013` for a lightest case below `1/(R·√a)`; `SizingDefaults.ValveRangeability`; the scenarios and valve pages | (this commit) | Shipped 2026-09-23; `C-122` opened (a stated `dp` that differs by case is taken at the merged design flow) |
| P6.9 S0 | **The source structure, decided and written down** (`D-147`, [`71`](71-source-structure.md)): PandaAI's `folder-structure-guidelines-core.md` and tree read as the model; the four choices the user made — namespaces mirror folders, a class folder at three partials, `…Base` suffix with family-noun derived types, a builder rather than a base for the pipe catalogues; Framework Design Guidelines looked up for the naming; the target tree for every current folder; the line budget measured; `04`'s rules, `03`'s tree, `08`'s P6.9 row | (this commit) | Written 2026-09-23; S1–S5 not started |
| P6.9 S1 | **Core moved into nine domain folders** (`D-148`): 203 source and 119 test files by `git mv` in a commit that compiles untouched; namespaces set to the folder, a class folder (`LayoutEngine/`, `BindingRun/`) transparent; `using`s rebuilt from a type map and 149 unused ones removed; `EveryCoreNamespaceIsItsFolder`; the token goldens moved with their test | `8f7c71e`, (this commit) | Shipped 2026-09-23. Core 2199/0/3, Api 60/0, no golden moved. Found the class-folder / namespace collision → `D-148` |
| P6.9 S2 | **One type per file**: 279 types out of 69 files in Core and Api, each file's `using`s pruned to its own (312 removed); the syntax tree's nodes under `Ast/Statements/` and `Ast/Expressions/`, the equation and unknown declarations under `Components/Declarations/` | (this commit) | Shipped 2026-09-23. Core 204 → 446 files, 58 549 → 59 245 lines (+696, against `71`'s +1 500 estimate). Core 2199/0/3, Api 60/0, no golden moved. `C-98` recurred once — the segfault at shutdown after the summary printed — and two reruns were clean |
| P6.9 S3 | **Concern partials**: every Core file over 600 lines split by concern with members moved whole — 22 classes, 63 new files, class folders for `OuterLoop`, `EquationSystem`, `WellPosedness`, `LineParser`, `SolutionSeed`, `SolveExplanation`, `ComponentRegistry`, `ModelContractBuilder`, `LayoutHintsDerivation` and `Lowering`; nested builders split as nested partials; unused `using`s pruned. `BinderDiagnostics` (933) stays whole, a descriptor table whose `All` cannot cross files — [`71`](71-source-structure.md) invariant 4. `70`'s R6 not taken | (this commit) | Shipped 2026-09-23. Core 446 → 509 files, 59 245 → 59 923 lines (+678, against `71`'s +400 estimate); largest class file now 591 lines. Core 2199/0/3, Api 60/0, no golden moved |
| P6.9 S4 | **`ComponentBase` and `ValveComponentBase`**: the seven flow components on one base and the two control valves on a second; the `…Component` renames by `rename_symbol` (`CircuitNode` → `NodeComponent`), files moved to match; the living spec and docs follow the new names, the registers keep the names they were written with. Three departures from `71`'s sketch, recorded in its S4 row | (this commit) | Shipped 2026-09-23. Core −98 lines. Solver-scale at 861 unknowns 5.50/5.63 s before, 5.58/5.52 s after, allocations identical — inside run-to-run noise, so S4 ships. Core 2199/0/3, Api 60/0, no golden moved |
| P6.9 S5 | **`SizerBase<TComponent>` and `PipeCatalogBuilder`**: five sizers on the typed base, refusal wording kept per sizer; the two control-valve rules name `ValveComponentBase`; three pipe catalogues build through one builder. The frontend's token-golden test had read S1's old folder since S1 — fixed, with `03`'s stale example paths | (this commit) | Shipped 2026-09-23; **P6.9 closed**. Core +53 lines; the whole restructure +1 491 (Core 58 387 → 59 878, 204 → 513 files). Core 2199/0/3, Api 60/0, frontend 230/230, no golden moved |
| P6.2 | Stratified tank in time ([`33`](30-solver/33-transient-time-domain.md) §Stratified tank, `D-32`): `Stratification.Remix` as one pool-adjacent-violators pass on the backend's density, `EquationSystem.Remix` and `SetLayerMasses`, the run calling it after each accepted step; `FS3108` on a profile outside the property domain; V15, V16 and V17; `docs/advanced/stratified-storage.md` and `tank.md` | (this commit) | Shipped 2026-09-22; `S-80` (the interface-flow formula's zero branch is the only one exercised) and `S-81` (V17 has no independent reference table) opened |

> **P6.2 shipped 2026-09-22.** What it meant to do: stop a tank from holding light water under heavy
> water, and close `62`'s three tank cases. What it found: the density anomaly is real on this
> backend and worth asserting rather than assuming — IF97 peaks at 999.9754 kg/m³ at 4 °C, so a 6 °C
> layer under a 1 °C layer is the stable arrangement and a rule written on temperature would stir a
> chilled store that was resting correctly; `FS3108` was registered by P6.0 and unreachable, because
> the layer's own failure passed the backend's range message through instead of being wrapped, which
> is why a code can exist, be named by a test and still never fire. Measured: V15 within 0.03 K of
> `T(t) = 54 − 34e^(−t/1495)` over 1800 s; V17's 25/55/40/45/60 °C profile pools layers 2–4 to
> 46.61 °C on the first step and leaves 25 and 60 alone; drift 3e-12. Two gaps filed rather than
> papered over: the interface-flow formula's non-zero branches have no fixture (`S-80`), and V17's
> refinement half compares the solver against itself because `62`'s independent table was never
> written (`S-81`). 47 open.
>
> **`D-144` accepted 2026-09-22, no code:** a heat exchanger's hold-up is plate area times the channel
> gap. The obvious rule was litres per kW and the search does not support one — manufacturers publish
> hold-up per channel, and the published litres-per-kW figures (11, 17–18, 25–27, 30) are *system*
> water content for buffer sizing, wrong here by two orders of magnitude. What the search does support
> is the gap: 0.040 l per channel behind a 73.5 × 278 mm plate on an AC18 and 0.103 l behind a
> 113 × 466 mm plate on a CB60 both give **1.96 mm**, two models an order of magnitude apart in duty.
> This project's reasoning, and the part to test: that gap is measured against projected plate area
> while a quoted heat transfer area is the developed area, larger by an enlargement factor of order
> 1.15–1.25, so the rule reads high by that much. A sized `area` deliberately does not feed it —
> sizing runs after lowering, so the hold-up would appear between passes and change the counting table
> mid-solve — which is the same shape as `D-99` stopping at `ua` with no default `U`. `C-114` rewritten
> with the build that remains, `C-118` opened for the profile. 49 open.
>
> **`D-145` accepted 2026-09-22, amending `D-144` the same day, no code:** checked against the two
> reference exchangers, `D-144`'s ordering fired on neither — the demand-step loop states no `u` and
> so has no area at all, and the substation's 3.658 m² is sized rather than stated, which `D-144`
> excluded. So stated `volume`/`volume2` per side becomes the primary route and the area rule the
> fallback, now reading a sized area too; the stability worry that exclusion existed for is solved by
> declaring the hold-up unknown for every exchanger in a **dynamic** circuit whatever its volume,
> since a zero hold-up is algebraically today's outlet and a static circuit declares nothing extra.
> The user's evidence added a second manufacturer: Danfoss publishes `V₁ = 0.027·N/2` and
> `V₂ = 0.027·(N−2)/2`, which sum to the `N−1` channels of an `N`-plate stack and give the exact
> one-channel asymmetry between sides, with per-channel volumes of 0.017–0.239 l across a range
> bracketing the 0.040 and 0.103 l measured on Alfa Laval. `C-119` opened for the plate metal,
> measured at 15–20 % of the fluid's capacitance on water/water and dominant on a refrigerant or air
> side. 50 open.
>
> **`C-114` shipped 2026-09-22, and `D-146` records why it was small.** What it meant to do: give an
> exchanger the water it holds, so a duty step leaves it as a curve rather than a cliff. `D-145`
> specified the mechanism as an unknown and a balance row per connected side, with the duty moved off
> the downstream node and half of `D-69` reversed. **What the build found is that a third mechanism
> exists and neither `D-144` nor `D-145` considered it**: the node the side discharges into is already
> a control volume whenever it carries a thermal volume, and has been since the pipe cells landed in
> P6.1. At forward flow `D-69` already puts the whole duty on that node and the fluid arriving is the
> inlet's, so its balance is already the row `D-145` was going to write — giving the node a mass turns
> it into `m·dh/dt = ṁ(h_in − h) + Q̇`. Sixty lines in `Lowering`, no column, no row, nothing in a
> static solve (`SystemLayout` builds differential states only in `SolveMode.Transient`). What it
> costs, stated rather than discovered later: the volume sits at the nominal outlet, so a reversed
> side holds its water at the wrong end, and a shared node mixes the hold-up one junction upstream of
> true. Measured on the demand-step loop with `HE1` stating `volume=0.5`: a fifth differential state
> at 0.494 kg; the outlet leaves 50.06 °C at the step instead of jumping to 65.09, and approaches it
> as a first-order lag with `τ = m/ṁ = 2.065 s` — 55.82 °C at 1 s against a predicted 55.82, 59.37 at
> 2 s against 59.37. Settled state unchanged at 72.18 °C, V8 still agrees, 2120 green. The sizer owns
> `volume`/`volume[2]` and the substation's 3.658 m² gives 3.585 dm³ a side with nothing written.
> `33`'s worked example, its CFL row (8.6 s → 1.86 s, the hold-up now the smallest volume) and one
> acceptance criterion moved with it. 49 open.
>
> **P6.8a shipped 2026-09-22**, in two commits. What it meant to do: teach the language `D-143`'s
> scenarios, with nothing consuming them yet. **The design decision worth recording is where the list
> is not**: `ParameterValue.Value` stays a *scalar* whether or not a list was written, holding the
> design case's number, and the other cases sit beside it in `ParameterValue.Scenarios`. Thirty-three
> sites read `.Value` — the component factory, the contract builder, deferred evaluation, six binder
> passes, the scene audit — and not one changed. `ScenarioProjection.Project(model, i)` rewrites that
> one field and nothing else, so P6.8b's N solves run today's code N times. The alternative, a model
> carrying lists all the way down with a selector at each read, is the same feature with thirty-three
> places to get it wrong, every one on a path that files *without* scenarios also take.
> What it found: `scenarios` was missing from `Binder`'s list of statements step 0b reads for itself,
> so a file-wide line before the first `circuit` header built an implicit circuit — reported obliquely
> as `FS1508` "no circuit name" rather than as anything about the directive (`L-64`, closed in the
> same change). The three `expects=FS1104,FS1105` fences in `plan/` came off, which is `15`'s own
> mechanism working: the corpus test failed because three blocks had become *too* correct. 49 open.
>
> **P6.8b shipped 2026-09-23.** What it meant to do: build `24`'s four steps. What it found, in the
> order it mattered.
>
> **`OuterLoop` had no way to solve without sizing**, which step 3 needs — all three `RunAsync`
> overloads run the rules and `Prepare` does lowering and sizing in one method. `Freeze` adds it. The
> shortcut worth recording as refused: writing the merged sizes back as *stated* parameters would
> freeze them for free, since a sizer never sees a stated one — but a stated value is a **constraint**
> under `D-02`, so an exchanger merged with both `ua` and `area` would raise `FS2101` on a well-posed
> plant. Freezing is a tier below stating.
>
> **The flow trap is real, and measured on `m5-scenarios`.** A changeover loop, 50 kW over 35/45 °C
> in winter against 40 kW over 7/12 °C in summer: the *smaller* duty governs every flow-driven size,
> because a 5 K program carries 1.906 kg/s where a 10 K one carries 1.20. `HE1.flow` and `P1`'s DN65
> are both governed by summer. A plant merged from the duty's winner is a pipe size short, which is
> why the envelope is per parameter and not per component — the same rule structural engineering
> applies per result component per member.
>
> **The merge settles in two rounds**, the second confirming the first — `08`'s first open
> measurement, answered on one plant. Rounds exist because sizes are coupled: a larger pipe lowers
> the head a pump asks for. `MaxRounds` is 4 and a plant still moving at the cap reports its last
> merge with a note, the same shape as the outer loop's `FS2301`. One plant is not a proof, which is
> why the cap is real.
>
> **Two gaps filed rather than papered over.** `C-121`: a merged plant reports no valve `authority`,
> because it is `Δp_valve/Δp_circuit` at the case's own flow and the merged valve's is a different
> number in every case — so `FS4006` silently stops applying the moment a file gains a `scenarios`
> line, and the turn-down check `24` now specifies cannot run. `C-120`: every binder *review* reads
> `ParameterValue.Value`, which holds the design case alone, so a physically contradictory non-design
> case binds clean and fails two layers down as a non-finite residual — found because the first
> fixture I wrote had exactly that mistake in its summer element and `FS2119` stayed silent. That is
> the standing cost of the scalar-`Value` design, worth paying for the thirty-three readers it left
> untouched, but a review is not a reader. 51 open.
>
> **`FS2314` added 2026-09-23**, the user's call on `24`'s two candidate mitigations for the case
> nobody writes. It names any duty-carrying component every declared case leaves inert — `24`'s
> recovery exchanger, zero in winter and zero in summer and governed by a shoulder case that is not
> in the list. It reports the *pattern* and claims no diagnosis: knowing which case is missing needs
> the dependency between one component's duty and another's, which the model does not carry. Scoped
> to duty-carrying components deliberately, since a pipe with no flow still sizes to the smallest row
> and conflating the two would fire on every standby leg. The range sugar is rejected — one candidate
> set with two spellings, the second of which is `D-138`'s sweep returning under another name.
>
> **`C-121`'s reasoning was corrected the same day it was filed.** The first entry said authority
> differs per case because it is taken at that case's own flow. It is not:
> `achieved = achievedDrop / (rest + achievedDrop)` is a ratio of two drops at *one* flow and both
> scale as `ṁ²`, so the flow cancels — what is left is the friction factor's Reynolds dependence and
> the density's, a percent or two. **Authority is a property of the built geometry, not of the
> operating point.** No case's figure survives the merge because the merge changes that geometry
> twice: a Kv that is not the one that case reported against, and pipes that are not that case's
> either. The row is smaller than filed — one authority per valve, computed once from the merged
> sizes — and the fix is unchanged.
>
> **`C-121` closed the same day**, with the minimum across cases (the user's call). Measured on
> `Seasons` with a valve: the merged plant reads 0.693 in winter and 0.692 in summer, equal to
> summer's single run — the cancellation confirmed at 0.13 % for a flow 37 % lower. The floor the
> register recorded that morning (merged ≥ the governing case's) holds only for the case that chose
> the Kv: with `dp=[5, 60]` winter governs the Kv and reads 0.76 while summer reads 0.23, which is
> `FS4006` in a case that before this fix reported nothing. That is why the report takes the lowest
> reading and not the governing case's. The turn-down check shipped with it as `FS4013` — 1 kW
> against 40 kW asks for 1.3 % where an equal-percentage valve at 0.66 reaches 2.5 %. Measuring the
> per-case drop found `C-122`: winter's stated 5 kPa is taken at the merged 1.91 kg/s, so winter's
> exchanger drops about 2 kPa on the merged plant, silently.

### R — Core refactoring ([`70`](70-core-refactoring.md)) · R0–R5 shipped 2026-09-21, R6 deferred

Behaviour-preserving packages in `70`'s order; each row states what was measured and what moved.

| # | Package | Commit(s) | State |
|---|---|---|---|
| R0 | Seams and dead code: `LayoutEngine` into seven partials along its own section markers (`Solve`, `Rings`, `Blocks`, `Anchors`, `Connections`, `Instruments`, `Scene`; the file's order gave seven seams, not `70`'s four), `SolutionSeed.Field` to a partial file, `SceneAudit.Inside` deleted, `ThermalStages` as eight phase methods over a `StageGraph`, the two `Tank` casts merged | *(this commit)* | Shipped 2026-09-21. Measured: Core 1975/0/3, Api 59/0, no golden under `diagnostics/` or `tests/` moved |
| R1 | Small shared helpers: `Hydrostatic.Pressure/Head/Lift/Power` at the twelve `StandardGravity` sites (now referenced from `Units/` only), `SizingContext.VolumeFlow`/`LitresPerSecond` at the six sizer sites, `BindingRun.Register` at the three symbol-table registrations, `LoopGroup` at the four `("loop", …)` tuples. Not done: the factory's six parameter-map initializer blocks -- a C# object initializer cannot be splatted from a record, so a helper would not remove them | *(this commit)* | Shipped 2026-09-21. Measured: Core 1975/0/3, Api 59/0, circuit harness report unchanged, no golden moved |
| R2 | The solved view: `SolvedStates.Pump` (flow, rise, head over the mean of inlet and outlet density, the residual's convention, with a `PumpHeadBasis`) read by both the report and the contract; `SolvedStates.Resolved` replaces the report's private promoted map and local lookup | *(this commit)* | Shipped 2026-09-21. Measured: Core 1975/0/3 with every report golden unchanged; three contract goldens re-baselined on four `state.head` values only (cooling loop 2.48435→2.48434, now equal to the sized parameter it had disagreed with; header 4.71031→4.71025, 5.18505→5.18498; substation 10.2325→10.2322), the inlet-versus-mean density difference being water's compressibility over the rise |
| R3 | Parameter ownership and promotion (`D-130`): `ParameterState`/`Ownership.Of` read by `IsFree`, `Claimed` and `IsProvisional`, `Ownership.Key` at the fourteen label sites; `Reach.Feeding/Reaching/Local` for the three branch walks; `Candidates` a per-kind switch over `Splits` and `FlowActuators` in `D-130`'s order, kinds matched by type; `Constraints` split into `TerminalConstraints`, `StatedFlowConstraints`, `DesignPointConstraints` in the first-come order. Tests: `OwnershipTests` (each state, the factory's maps), `TheFirstPinnedFlowTakesThePumpAndTheSecondFallsToItsOwnValve`, `PipeSizer.CanSize` direct. `C-111`'s balancing valve is not the resolver's to size (it is not in the script); the row stays open on the language decision | *(this commit)* | Shipped 2026-09-21. Measured: Core 1980/0/3 (1975 unchanged + 5 new), Api 59/0, circuit harness unchanged, no counting line or golden moved |
| R4 | The outer loop and the seed: `Refused` for the four refusals (one error shape, the check's diagnostics travelling with it), `Annotated` for the diagnostics chain both exits build (one order), `ProvisionalBasis` for the line `Declined` and `Unsized` each wrote; `SolutionSeed.PromotedColumns` for the three walks over the promoted columns (`Parameters`, `Promoted`, `PromotesHead`). Left as they are, on reading: `Integrate` (its three local functions are already the named phases; a `WalkState` class would move text and remove nothing) and `NewtonSolver.Solve` (nine exits already go through `Stop`/`OutOfDomain`, each with its own diagnostic) | *(this commit)* | Shipped 2026-09-21. Measured: Core 1980/0/3, Api 59/0, harness and goldens unchanged; pass counts and residual norms unmoved |
| R5 | The layout ring forms: `Attempt` snapshots the groups, the placed, loop and inline flags, the sides and the loop centre when a form starts and restores them when it declines, replacing three hand-written rollbacks that each restored a different subset (`Loop`, tried first, restored only its groups); `RailItems`, `Unplace` and `AssignRuns` for what the three tails shared verbatim; `Segments.Crossing/Shared/Collinear` for the arithmetic the engine and the audit each wrote, taking the caller's tolerance because the audit's is 1e-6 and the engine's 1e-9; `Open.Finish`'s up-then-left order documented against `FreeSide`'s. Not done: a wholesale `AssembleRing` -- the three forms differ in what feeds `Top` and `Close` (source anchors, corner anchors, supply and return with a chain), so one tail would carry ten parameters and read worse than three | *(this commit)* | Shipped 2026-09-21. Measured: Core 1980/0/3, Api 59/0, every ladder step, audit count and contract golden unchanged -- so `70`'s open question 2 is answered: the `Loop` rollback gap changed no picture |

### After P3.7b — the convergence work · 2026-09-07 to 2026-09-09 · 60 commits

**This is state no phase table shows, and it is most of the last three days.** P3.7b closed with the
outer loop built and `24`'s worked example reached. What followed belongs to no package: it is the
difference between a solver that runs and demo scripts that converge, and it was driven by the defect
register rather than by `08`.

The load-bearing ones, by what they settled:

| Area | Entries | Outcome |
|---|---|---|
| Seeding | `S-26`, `S-30`, `S-35`, `S-46`, `S-49`, `S-50`, `S-51` | The seed went from "a number per unknown" to a construction with stated properties — mass-consistent, inside the property domain, off every bound, oriented by the pumps, and with no branch at rest |
| Mixing | `S-58` | A junction's arriving enthalpy is the mass-weighted mix of its inlets, not their average — the one-line defect under `S-48` and `S-51`, found by stating a position and reading the converged number |
| The fourth plant | `S-59`, `D-93` | Two pumped sources, two mixing consumers, one direct — built as a scratch experiment on 2026-09-14 and kept as `OuterLoopTests.TwoPumpedSourcesShareALoadTheirConsumersSetAndEveryPumpKeepsItsOwnLoop`. It found the closure running after the count and a source pump sized to a loop through a consumer's pump; converges in two iterations on hand figures |
| Three-way valves | `C-60`, `C-61`, `C-63`, `C-66`, `D-85`, `D-88` | Ports named `ab`/`a`/`b` as manufacturers label them, sized by authority, and identified by the port name the script *wrote* rather than by walking the graph |
| Counting and rank | `S-33`, `S-36`, `S-39`, `S-41`, `S-43`, `D-86`, `D-90` | A singular system now names the equation its other rows imply, instead of naming a component to blame |
| Pressure boundaries | `S-38`, `S-44`, `D-86`, `D-87` | A stated pressure is a boundary only on a boundary; a temperature on an interior node is a setpoint that promotes the split holding it |
| Rounding direction | `C-62`, `D-89` | A valve rounds down against a free pump and up against bounded pressures, and two-way valves now get the context that decides which |
| Diagnostics | — | One solve report answers every question the ad-hoc probes were asking; `diagnostics/` carries the timings |

**Not everything here was progress.** `be69f5c` audited all 49 open defects against the code and found
four already solved and two that were never work — which is the cost of a register that only ever
grows, and the reason a periodic audit is now part of the workflow below.

## Open questions, by tier

Counts only. Every description lives in the file named.

| Tier | Open | File |
|---|---|---|
| 00 · Foundation | 2 | [`00-foundation/defects.md`](00-foundation/defects.md) |
| 10 · Language | 6 | [`10-language/defects.md`](10-language/defects.md) |
| 20 · Core domain | 28 | [`20-core-domain/defects.md`](20-core-domain/defects.md) |
| 30 · Solver | 11 | [`30-solver/defects.md`](30-solver/defects.md) |
| 40 · API | 2 | [`40-api/defects.md`](40-api/defects.md) |
| 50 · Frontend | 8 | [`50-frontend/defects.md`](50-frontend/defects.md) |
| 60 · Docs and dev-ex | 2 | [`60-docs-and-devex/defects.md`](60-docs-and-devex/defects.md) |
| | **57** | |

Counted from the files on 2026-09-20 after the sweep's tier 4b (seven closed, `C-108` opened, six
re-triaged). Of the 58: **35 measured, 23 hunch**; 3 tiny, 12 small, 30 medium, 9 big, 4 large.
**Fixable now** (tiny or small, measured): `L-57`, `L-35`, `C-108`, `C-54`, `C-38`, `C-36`, `A-2`,
`U-6` -- and five of those eight wait on a decision of the user's (`L-57` the bare `vflow` unit,
`C-54` whether `24`'s velocity bound is a guard, `C-38`/`C-36` which pipe series, `C-108` a spacing
judged on a picture), so the sweep's quick list is three rows long. **Largest** (big or large, high
risk): `F-19`, `C-73`, `C-68` (large); `C-98`, `C-72`, `S-37`, `S-27` (big). The estimates are the
filer's and are meant to be checked against the Closed tables' actual efforts as rows close: of the
seven closed today, five took what was estimated, `S-66` took medium against small (the instruments
were most of it), and `C-87` tiny as estimated.

Counted from the files on 2026-09-19 after sweep tier 4's `C-104` (one closed; 63 after `S-68`, 63 after `S-63`, 62 after `S-29`, 62 after tier 3, 67 after tier 2, 77 before it); the table had not been recounted since P3 and read
48. Tier 70 has no defect record because nothing has implemented against it yet. Its absence means
nothing has looked, not that nothing is wrong — the same caveat each existing file carries about
its own unread documents.

**Nothing open blocks the three demo scripts any more.** The header's remaining entries were each
measured on a *variant* and stay open on their own merits: `S-53` (the seed doubles a three-way
valve's inlet legs when the source outlet is omitted), `S-55` (driver analysis misses distribution
pumps once a source valve is added), `S-56` (a zero-duty consumer inherits its sibling's flow) --
all three closed 2026-09-20 -- `S-52` (`FS2211` sends the user to the balanced half) and `S-45`'s residue, which `FS2218` now
makes visible without deciding. `L-47` (closed) records the sign discussion of 2026-09-14: `power`
never carries flow direction, terminals stay port-bound, and `FS3013`/`FS1308` say so. Nothing stands between here and M2a's exit any more; it exited 2026-09-14 with `C-76` closed. P3.8 closed `C-51` with `D-94` on 2026-09-14; the M2a
sweep of 2026-09-14 filed `C-74` (no `FS23xx` code is registered; sizing speaks in notes) and `C-75`,
and closed `F-24` by moving the model-contract payload criterion to M3. A four-instance code review
of Core the same day, asked for the `C-76` class, found no second leak and three ways a script could
take the process down --- two stack overflows and a binder crash, `L-48`/`L-49` --- plus the
tier-20 sweep recorded as `C-77`; all closed before P4.1 starts. The review's untriggered aspects
(error handling in Binding/Syntax, resource management outside Fluids/Solvers, architecture) are
unassessed, not clean.

## What is next

**P6.8a and P6.8b shipped 2026-09-22 and 2026-09-23.** A plant is sized for every case it declares,
the merge is measured to settle in two rounds, and `diagnostics/scenario-sizing.md` names the case
that governed each size. What P6.8 still owes, and what comes after:

- ~~`C-121`~~ **closed 2026-09-23**: a merged plant reports its lowest authority across cases,
  `FS4006` applies to scenario files again, and the turn-down check is live as `FS4013`. The `√a`
  half of that check is still **this project's reasoning**, not a standard.
- ~~P6.9~~ **shipped 2026-09-23**, the source structure ([`71`](71-source-structure.md),
  `D-147`, `D-148`): nine domain folders, one type per file, concern partials, the component, valve
  and sizer bases and the pipe-catalogue builder. +1 491 lines against +1 700 estimated. `70`'s R6
  was not taken and is still deferred.
- ~~`C-120`~~ **closed 2026-09-23**: a check that reads values reads every scenario, and names
  the cases it fails in, once per finding.
- **`S-82`**: the user's verdict (2026-09-23) is that the failure was the right answer. The heat-pump
  file stated the machine's duty and the load equal, and a shut three-way body leaks, so no steady
  state holds the supply. The saturation-release prototype is shelved, not merged
  (`diagnostics/scratch/s82-saturation-wip.patch`); what stays open is a diagnostic that names the
  over-statement instead of `Singular`. A heat pump is `D-78`'s refrigerant circuit, which waits on
  `D-77`.
- ~~`S-83`~~ **closed 2026-09-23**: a machine that holds its leaving temperature (`heater out.t=45`,
  duty free) now seeds the load it feeds and solves; the propane heat-pump priority cases converge. It
  opened `S-84` (the report's heat balance omits a solved duty) and `C-123` (a stated `out.t` wired
  straight to a junction constrains the mixed node).
- **`C-122`** waits on a user's call, not on work: refuse a scenario list on `dp`, or hold each
  case's stated drop at its own design flow.
- ~~The zero-envelope diagnostic~~ **built 2026-09-23** as `FS2314`, the user's call between `24`'s
  two candidate mitigations. The range sugar is rejected: it would spell one candidate set two ways,
  and the second spelling is `D-138`'s driver sweep under another name.
- ~~`S-79`~~ **closed 2026-09-23 by `D-149`**: `start=` on the project line places t = 0 on the
  time axis, and a run follows every curve of time from there. Open in `33`: whether t = 0 is the
  design state (`D-141`, today) or the plant settled at the start's own conditions -- the user's call.
- **`D-150` built 2026-09-23**: a sensor, or a `measure=` naming a node, reads a node with at most two
  connections; a junction is `FS1548`. `01`'s demand-step loop reads `NS` on the mixed pipe past `N2`
  (figures unchanged, measured). The layout half of the user's instrument redesign -- a sensor merged
  with its node and a controller with its actuator as one layout box, on the first free perpendicular
  side, breaking `D-100`'s instrument edit-stability rule -- is the next package and takes its own
  `D-` (the user's call: a short signal line, one margin, between host and bubble). Mapping it found
  ~~`C-124`~~, closed the same day: a pipe with `nodes=` is now drawn as its chain.
- **`D-151` built 2026-09-23**: an instrument is drawn on its host as one footprint -- a sensor on its
  node, a controller on its device, on the host's first free side (stem, up, down, left, right), joined
  by a line one margin long -- and the layout makes room for it (`Reserve` on a sensor's run, bubbles
  and stubs in the forms' clearance). `D-100`'s instrument edit-stability clause is retired, L19's
  instrument test replaced by the `C15` predicates. The user's refinement: a controller that measures a
  node reads it through a sensor the binder infers there (I8, `NS__TE`). Every reached sample and step
  hard 0, soft 0; `m4-demand-step` hard 0, its 5 soft filed as `C-125`. ~~`C-94`~~ closed.
- **`D-152` built 2026-09-23**: a signal line crosses the drawing -- only an inner box stops it, a
  margin is a cost, it never runs along a pipe and crosses one clear of the pipe's ends, fewest bends
  then shortest (the router's signal mode). m4's `TC1` line 7.8 → 5.5, two bends. A three-zone plant
  drawn for the user the same day filed `C-126` (a plain parallel branch has no rule), `C-127` (the
  fallback's bare L breaks H5) and `C-128` (room for a bubble made on three run paths of nine); the
  user asked for a rewrite of the layout engine (`D-153`).
- **Next: P6.10**, the layout engine rebuilt beside the old one (`08`, `28` part E), R0 first: the
  parity harness. Decided with the user 2026-09-23: built beside the old engine, changed pictures shown
  before commit, H11 (no pipe beside a pipe within a margin) hard at the switch.
  - **R0 built 2026-09-23**: `LayoutSolver.Solve(…, LayoutEngineKind)` selects the engine (the ladder's
    by default); `Layout/Engine/ComposedEngine` is the skeleton; `LayoutParityTests` draws every ladder
    step, the pending scripts (`Layout/Parity/*.fluid`: the three zones, `C-126`, and the zones with
    their instruments, `C-128`) and the samples with both engines, writing `diagnostics/layout-parity/`
    -- a report per case and `summary.txt`. The ladder engine on the pending scripts: hard 4 and hard 11.
  - **R1 built 2026-09-23**: `Layout/Engine/CircuitView` -- ports and peers in one lookup, every port's
    flow once, the runs between boxed elements (`Run`, `RunEnd`, `InlinePoint`) oriented with the flow,
    the fragments; a cycle of inline elements alone keeps its first element boxed. `GraphLinks` is the one
    link builder, the ladder engine's too. The composed engine traces fragments and runs (`E1`);
    `CircuitViewTests` read them. Core 2272/0.
  - **R2 built 2026-09-23**: `Layout/Engine/Decomposition` reads each fragment before any geometry --
    its head (C1) and form (sourced ring, ring of one, open form, unsourced ring, chain, tried in the
    ladder engine's order), its body (the biconnected block between the two terminals) as series,
    headers (paths that flow the same way) and loops (paths that flow opposite ways), and its pendants.
    `D-154`: a header's spine is its branch declared last (the most-junctions rule of `D-153` failed on
    `08e`). Every ladder step, pending script and sample reads with nothing loose and every pendant a
    tree; step 6c reads as an open form whose body is one loop (the ring C18 draws). The plan is in the
    trace (`E2`), `DecompositionTests` read it; header-200 decomposes in 3.7 ms against the ladder
    engine's 43.9 ms for its whole solve (`LayoutTimingTests`, now timing both).
  - **R3/R4 built 2026-09-23**: `Sheet` (placement state, the one clearance test `Clashes`/`Clear`/
    `Shift`/`ClearAt`, instruments as footprint, `RunLength` for every run), `Composer` (C17 stacking,
    the chain rules C3–C6, C7, stranded members stood under their fragment), `Painter` (every unlaid run
    routed with both stubs, C15/C16, groups, labels, scene). The ring forms (C2, C18, C19, C20), blocks
    (C11) and hanging branches (C14) read a ring's members, loops and branches from the decomposition
    (`Composer.Paths`) instead of searching the graph, over the ladder engine's rail geometry. **Parity:
    every ladder step and sample -- instruments, signals and header-200 included -- is drawn identically
    by both engines**, and `LayoutParityTests` now asserts it for every case but the pending ones. New
    (`28` E3): a header's plain branch hangs as a column under its split, its merge straight under it; the
    pending three-zone scripts draw hard 0 soft 0 (the ladder engine: hard 4 soft 7, hard 11 soft 12) --
    `ComposerTests`. The user accepted the zones pictures (2026-09-23; ladder steps 12 and 12b at the
    switch). Drawn next at the user's request, step 8a with its controls (`Parity/step-08a-header-parallel-controls.fluid`): a hanging block now keeps its bubbles a margin under its rail, a block's merge is
    held under its split by the re-lay pass, and a sliding unit clears pipes as well as boxes -- hard 0 soft
    0 (the ladder engine: hard 5 soft 4); every accepted picture unchanged. Step 8b with its controls
    (`Parity/step-08b-header-series-controls.fluid`) draws clean and identically in both engines.
  - **Every step with and without its controls (2026-09-23, the user's call):** each working circuit gets a
    controlled twin as a pending script, `Layout/Parity/step-XX-...-controls.fluid` -- 03, 05, 06, 06c, 07,
    8a-8e, 09, 11a and the zones (`step-12-zones`, `-controls`); 04's twin is the existing step 10. They
    move into the ladder at the switch. A twin is a working control scheme: its setpoint *replaces* the
    stated temperature it holds (with both, the controller is a passenger, `FS3210` -- as step 10's still
    is). Pending scripts are held to a step's gates now (`LayoutLadderTests`: drawn by the composed engine
    with no hard finding, solves and settles). All pass; 03, 05, 8c and 11a carry `# does not settle:`
    markers naming `S-87` (a pump named to hold a temperature makes the design solve singular) and `S-86`
    (the flow seed never reads a setpoint), and both zone scripts `S-85` (a plain split seeds every branch
    with the whole flow, running zone 1 backwards). Layout found on the way: a node's instruments now count
    in its footprint (09's terminal sensors), and a unit's pipes, its descent and its headroom keep clear of
    the bubbles estimated on the sensor points of pipes already drawn (8e); every accepted picture unchanged.
    Measured the same day (`C-92`): `header-200` 3.7 ms Release / 16.2 ms Debug (ladder engine 9.7 / 50.6),
    the composed engine ~n^2 against the ladder engine's ~n^3 -- 234 ms against 13.3 s at 1762 components.
    Code: the composed engine is 3550 code lines against the ladder engine's 2811 (+26%); the rail geometry
    (`Composer.Rails`, 693) is the old engine's, ported for parity.
  - **Two placement fixes (2026-09-24, accepted by the user):** (A) a unit's sensor room is counted from
    the far end of its own run -- the inlet run's from the top rail's end, the outlet run's from the bottom
    rail's -- not both from the bottom rail's; (B) the first settle pass lays the bottom rail as if no
    column hung over it (four passes now), so a pump pushed past an early column no longer leaves a gap
    once the splits are held. Step 10 draws as `29` records it (`LOAD` 1 m left, back on step 4's loop);
    the ladder engine had drifted from that picture and the parity gate lists it as such. Zones: `NA1`
    4.65 → 2.25, `LD3` 8.3 → 5.9; zones with controls `NA1` 5.25 → 3.35, `LD3` 10.0 → 8.1; hard 0 soft 0.
    Core 2353/0.
  - **A stress plant (2026-09-24, the user's request):** `Layout/Stress/plant.fluid`, 61 components --
    boilers in parallel, a buffer tank, duty/standby pumps, two headers in series, injection circuits, a
    two-sided DHW exchanger with its tank -- and three pieces of it. Neither engine draws it (composed hard
    28, ladder hard 39). Filed `C-129`, `C-130`, `C-131`, `C-132`; each is planned as a ladder step, `C-129`
    recommended first since it sends the whole plant to the fallback. No gate reads the folder.
  - **Diagnostics first (2026-09-24, the user's call; `D-155`):** the audit now measures what `28` states and
    did not check -- H6, H8, H10's tank clause, and A7/A6 as hard H12 (orthogonal) and H13 (one pipe per junction
    side), plus a soft `pipe-through-point`; the report names the rule behind every component and every pipe,
    each fragment's form and what declined, the worst detours, and the rules behind each finding. Every run a
    rule lays is named with its connections' ids; a skew one is refused for the router, a stale one taken back.
    Every accepted picture unchanged (parity, hard 0); the stress plant's diagonals are gone, its two skew
    producers filed as `C-133`, and the new checks' findings there added to `C-129` and `C-132`. Core 2360/0.
  - **Fixes the diagnostics pointed at (2026-09-24, the user's go-ahead):** a unit's descent lands where the
    bottom rail ends (`C-133` part 2); a ring's consumer is read by the side the ring passes, so a heat source's
    second side is one (`C-132`, its filed mechanism corrected); a branch that rejoins its rail -- the duty/standby
    pair -- runs as a row under it (`C-130`, built, awaiting the picture). Each compared on all 50 scripts: no
    accepted picture moved. Core 2361/0.
  - **The two design questions, answered from mock-ups (2026-09-24): stacked, and one loop per side.** Headers in
    series are stacked bands (`D-156`, `C-131`: piece B hard 15 → 0). A tank shared by two loops takes a loop per
    flank, the second loop read as an attached ring and laid by C2 with the tank as its fixed head (`D-157`,
    `C-129`'s first step: ladder step 13a hard 0 soft 0, and it solves). Piece B with its controls
    (`plant-distribution-controls.fluid`) draws hard 0 once the right side's descent crosses no laid pipe and its
    box keeps clear of the sensor bubbles still to come (`28` E3). No accepted picture moved. Core 2367/0.
    Next: the rest of `C-129` -- parallel boilers on the source ring (piece A), the attached ring through the bands
    (the whole plant), a loop per side of a two-sided exchanger (piece C's DHW) -- then R5 (pipe bands in the
    occupancy across forms, H11). The pictures of 13a and the controls version await the user.
  - **The user's review of the controls picture (2026-09-24, `D-158`):** the pump pair's row stands over the top
    rail, square with the spine; a header's spine on the right side is a column like its siblings (step 12's zone 3,
    the stacked plant's RAD3 and DHW); a sensor takes its controller's side of its pipe; a unit slides by the one
    clearance test. Piece B with controls hard 0 soft 3, step 12 hard 0 soft 0; no ladder picture or sample moved.
- **Then P6.3**, controllers, actuator limits and anti-windup. Its blocker is gone; P6.10 runs first (the user's call).

P6.3 follows; `S-79` is closed (`D-149`), so a setpoint following a curve now reads it at
`start + t`. `C-118`
(one mixed volume over-damps) and `C-119` (the plates store heat, **not scheduled** — the user's
call) share a structure, the three-node exchanger, so design them together when either is built.
Read `33` whole before any transient package; its worked example now carries the measured column
beside the closed form and says where the two part.

0. **The Core refactoring (`70`)** shipped R0–R5 on 2026-09-21 (`D-130` for the actuator order); R6
   (the binder's phase records, `EquationSystem`'s builder) waits for the next feature that opens
   those files. `C-111`'s second half stays the user's language decision.
1. **`01`'s header listing has drifted from the sample that meets its figures**, in three recorded
   ways: attachment replaced by hand wiring (`F-16`/`F-17`), `PU_MAIN` removed because the consumer
   pumps drive the whole loop (`S-55`'s subject), and `load` in place of `heat_exchanger` (`D-91`).
   The figures and the tag table are unchanged. Updating the listing to the sample verbatim is a
   spec edit and the user's call; until then the sample is the reference and `01` the intent.
2. **Whether the fourth plant becomes a fourth reference circuit.** It converges on hand figures as
   a test fixture; promoting it to `samples/` and `01` is a spec addition (`D-11`) and the user's
   call. Its natural next variants — one source off (`S-56`, closed: the pump dead-heads and `FS3015` says a
   check valve is what an off pump would need), a source-side mixing valve (`S-55`) — are the open entries it points at.
3. **The sweep's tier 4 resumes where P5.13a interrupted it**: `S-56` is closed (option b) and tier 4b
   (2026-09-20) closed the seven rows the columns called fixable; P5.13b shipped 2026-09-21 (`D-124`,
   `L-56` closed, `L-59` opened -- a deferred read of a solved value is never evaluated, which is what
   makes every `Name.port.quantity` read inert until `31`'s step exists); P5.13c built that step
   the same day (`L-59` closed, `L-60`–`L-63` and `C-109` opened), and `L-60` closed by `D-126`
   (a head accepts a length; `pi`, `g`); `C-109`, `L-61`, `L-63`, then `C-91`, `C-54`, `C-38`
   (`D-129`) and `C-36` (`D-128`) closed after it. `S-45` closed by `D-133` (2026-09-21, the reach and the matching), which opened `S-73`, the seed of a promoted `kv` on a parallel branch; `S-73` closed and `C-111`'s warning shipped the same day. `C-111` closed in full (`FS4011`, and a bypass `valve` set to level the legs), opening `S-74`, the series ring's erratic cold first pass, closed the same day (the Jacobian's pressure step was inside the flash's noise). `C-71` closed by `D-135` and `C-69` as superseded the same day. `C-65` closed by `D-136`, `C-64` by re-measurement. `S-47` closed by measurement the same day, completing the user's three-way programme, and `C-112` (colour-scale ends settled before nicing) after it; `D-137` (water on IF97, 2026-09-22) and `C-110` (the EN 10220 steel catalogue) followed. `S-37` closed by `D-142` (the valley is named by `FS3016`, no new language). `C-84` closed 2026-09-22 (labels laid out as boxes, `28` A11) and `S-42` the same day (the square-and-singular fixture). Next: `S-69`'s sizing passes, the sweep of open rows whose fix is known, then
   `D-108` and `C-108`, which wait on the product being testable. Two report packages the
   user asked for on 2026-09-20 -- layout provenance (`C-107`) and the solver report's iteration
   history, engineering-unit state, heat balance and seed bases (`S-71`) -- are both done.
4. **P5 — M3, the usable static product** (`08`); `P5.1`, the model contract and layout hints, is
   Core-side and closed by golden files before a pixel exists. `D-100` (2026-09-15) triaged a
   proposed layout standard before P5.1 started: hard constraints as a named class, an explicit
   priority order, equivalent assemblies drawn congruently (P5.1a's `BranchShapes`, withdrawn by
   `D-107`), edit stability as an invariant, three spacing tiers (dropped by `D-107`), and a text layout report
   (`LayoutExplanation`, P5.1d-3's since `D-103`) so a session can read a placement it cannot see. Temperature
   ordering of branches and barycentre reordering are deferred with reasons.
5. **The layout ladder has reached every sample** (`29`, 2026-09-17): all 24 layouts draw hard 0,
   soft 0 at the default spacing, and the rules are keyed to shapes, never to a circuit. What it has
   not reached is `29`'s closing list (the open form with more than two paths, a branch off the
   bottom rail, a block with no corner-taker, the valve residue of the loop search, a declared
   pipe off a ring); each waits for a script that needs it. The user closed P5.1d-2 there
   (2026-09-18: "the layout is at a sufficient level now; adjustments when the product is
   testable"), which is the sign-off `08`'s exit asked a person for. **P5.1d-3 shipped the same
   day:** the layout report is `SceneText` in Core with `28` A10's raster and `62`'s metrics
   (`C-89`); the audit measures cycles through inline nodes, a pipe through its own component and
   signal lines against boxes and pipes (`C-95`, three tests on bent scenes); and the predicate
   sweep is `LayoutPredicateTests` -- determinism, the port-for-port bijection, normalised routes,
   the transform class, spacing as presentation, congruent branches and three edit-stability
   fixtures -- over thirteen fixtures, with `62`'s L1--L19 table reconciled to the `D-107` engine
   (L13 and L16 withdrawn, L4, L11 and L15 the renderer's). `08`'s other exit, the reflow's
   monotonicity argument, is met vacuously: there is no reflow; a form that cannot finish backtracks
   whole. Still open from the package: `C-92` (48 ms against the 30 ms line, unprofiled), corpus
   mutation over the samples (`62`), and the report on the wire. Next: **P5.1e** (`D-110`, pipe
   properties on a connection line), then P5.2.
   What `P4` left behind, none of it blocking: `C-78` (the plate catalogue's shopping list — a cited
   `U`, a plate step, the `lamella` correlation, `FS2311`), `22`'s unticked crossover criterion (a
   solve driven across `C₁ = C₂`, not just the duty relation stepped over it), `C-75`'s last
   follow-on (a solved Kv has no basis line saying what it absorbed), and the `400HP01` criterion
   that waits on M4's `heat_pump` kind. `F-19`'s budget re-derivation has real numbers to work
   from (16 µs per water state, not 63).

## Standing baselines

Numbers a session can check in one command, so that "did I break something" has an answer that is not
a judgement.

| Baseline | Value | Where |
|---|---|---|
| Core test suite | **2199 total, 0 failed, 3 skipped** (2026-09-23), ~71 s with the `Diagnostic` classes and the transient runs; the `Unit` slice in ~4.5 s | `FluidScript.Core.Tests` |
| API test suite | **60 passed, 0 failed**, ~4 s | `FluidScript.Api.Tests` |
| Frontend tests | **230 passed, 0 failed**, ~12 s | `cd frontend && npm test` |
| Debounce | **300 ms, provisional** (`D-49`; the benchmark is built, `npm run bench`, and has not run for want of a browser, `U-4`) | `frontend/src/features/pipeline/debounce.ts` |
| Frontend checks | `tsc -b`, `npm run lint`, `npm run format:check` all clean | `frontend/` |
| Render baseline (M3, `D-45`) | **4.8 ms** to prepare and render the 24-placement header to static markup in Node; the browser numbers (`07`: 50 fps p95 panning 200 components, 8 ms per commit) are unmeasured, `F-10` | `frontend/src/features/canvas/baseline.test.tsx` |
| Build | **0 warnings** (`TreatWarningsAsErrors`) | `dotnet build` |
| Unit tier | under 2 s | `--filter-trait Category=Unit` |

`dotnet test` discovers zero tests in this environment; the binaries under
`~/.dotnet-artifacts/bin/<project>/debug/` are run directly. That and the Visual Studio `obj/`
collision that produces hundreds of spurious `CS0246`s are written up in
[`60-docs-and-devex/defects.md`](60-docs-and-devex/defects.md) under Observations, which is the
authoritative account of both. **Environment traps are the one thing that also belongs in an agent's
own memory**, because their whole value is firing before the mistake rather than after someone goes
looking. Nothing about the project's state does: that is this file's job.

## Updating this file

A phase or package is not complete until this file says so. The workflow — what to read before
starting, what to write when a package closes, and when a defect gets an entry — is stated once in
`CLAUDE.md` and pointed at from `AGENTS.md`. It is not repeated here, because a workflow described in
two places is a workflow that disagrees with itself.

What this file needs when a package closes:

- Its row in the phase table, with the commit and the date.
- The defect ids that package **closed** — ids only, never their reasoning.
- Any open id it **created**, if that id changes what the next session should do.
- The standing baselines, if they moved.
- The "Where the project stands" block, if the position moved.
