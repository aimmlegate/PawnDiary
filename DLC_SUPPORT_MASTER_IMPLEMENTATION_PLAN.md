# Pawn Diary — DLC support master implementation order

> **Status:** authoritative execution schedule; no production behavior is changed by this document.
>
> **Purpose:** this document dictates **what is implemented next and in what order**. The individual
> Royalty, Ideology, Biotech, Anomaly, Odyssey, and Narrative Continuity plans remain the technical
> authority for their own contracts, hooks, policies, tests, and acceptance scenarios. They do not
> independently choose release order.
>
> **Conflict rule:** when documents disagree about scheduling, this master order wins. When they
> disagree about how a source-specific feature works, the source-specific plan wins. When they
> disagree about shared evidence, selection, references, prompt context, or reflection coordination,
> `DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md` wins.

## 1. How an implementation agent uses this plan

1. Start at Wave 0 and find the first wave whose exit gate is not proven by current code, tests,
   documentation, and changelog.
2. Complete only that wave's next listed slice. Each subordinate phase is still a separate smallest
   safe change unless this plan explicitly groups an XML-only change.
3. Do not begin a later wave because its hook looks easier or its code is nearby.
4. After every slice: run its focused tests, validate touched XML, update `DOCUMENTATION.md` and
   `CHANGELOG.md`, run the Debug build, and rebuild/stage `1.6/Assemblies/PawnDiary.dll` when C# changed.
5. Record the subordinate phase and master wave in the changelog, for example
   `Master Wave 3 / Biotech Phase 2`.
6. When a wave is already partly implemented, verify every missing exit-gate item; do not assume that
   a class or Def name means the behavior shipped.
7. Stop at a release checkpoint for review/in-game smoke testing before proceeding to the next wave.

Parallel implementation is **off by default**. Two agents may work simultaneously only when their
files, save contracts, XML order ranges, and event ownership do not overlap and both slices depend
only on a completed earlier wave. Merge and validate them one at a time in this plan's order.

## 2. Global rules for every wave

- Source-specific capture owns truth, page authorization, fan-out, and duplicate suppression.
- Shared facets are metadata, never generic page-producing event types.
- New DLC reads stay in guarded `DlcContext` accessors or their source-owned guarded adapter.
- All prompt-visible facts are copied on the main thread at event time before async generation.
- Tunable policy and prompt prose live in XML with safe fallbacks and correct localization.
- Every new pure decision has a standalone pure test.
- Every save change has old-save normalization, no-catch-up behavior, and Scribe round-trip coverage.
- Every DLC slice proves inactive/no-DLC behavior, not merely compilation.
- One gameplay action produces one canonical page.
- No wave may broaden scope to an unverified ending, hidden Anomaly state, or guessed DLC outcome.

## 3. Dependency spine

These dependencies are absolute:

```text
N0 shared contracts
  -> N1 persistence/prompt seam
     -> first source event persistence
        -> source provider/emissions (N2/N3 slices)
           -> linked reflections (N4)
              -> global hardening (N5)
```

Exceptions:

- Anomaly A0.0–A0.1 is XML-only classifier/prompt work and may ship after N1 without an Anomaly
  provider.
- A source's pure-contract phase may be prepared after N0 while N1 is being reviewed, but its live
  persistence or page source cannot land before N1.
- Existing shipped DLC behavior continues unchanged; this order governs the new standalone plans.

## 4. Master wave summary

| Wave | Deliverable | Subordinate phases | Release checkpoint |
|---|---|---|---|
| 0 | Freeze shared architecture | Narrative N0 | No behavior; contract review |
| 1 | Land shared persistence/prompt seam | Narrative N1 | Infrastructure release |
| 2 | Correct Anomaly semantics cheaply | Anomaly A0.0–A0.2 | Prompt-quality release |
| 3 | Biotech family flagship | Biotech 0–4 + Narrative N2-B | B1 release |
| 4 | Odyssey journey flagship | Odyssey O1.0–O1.5 + Narrative N2-O | O1 release |
| 5 | Royalty persona/title flagship | Royalty 0–4 + Narrative N3-R | R1 release |
| 6 | Biotech identity/mechanitor depth | Biotech 5–6 + Narrative N3-B | B2 core release |
| 7 | Anomaly knowledge/reveal depth | Anomaly A1.0–A2.2 + Narrative N3-A | A1/A2 release |
| 8 | Odyssey environmental depth | Odyssey O2 + Narrative N3-O | O2 release |
| 9 | Royalty succession/permits/ascent | Royalty 5–7 + Narrative N3-R extension | R2/R3 release |
| 10 | Ideology stance/conversion foundation | Ideology 0–3 + Narrative N3-I | Belief enrichment release |
| 11 | Secondary bonds and terminal chapters | Biotech 7–8, Anomaly A3.0, Odyssey O3 | Feature-complete DLC sources |
| 12 | Unified reflection system | Narrative N4 + Ideology 4–5 | Reflection release |
| 13 | Full compatibility and hardening | Royalty 8, Ideology 6, Narrative N5 | Final integrated release |

The `N2-B`, `N2-O`, and `N3-*` labels are provider-specific delivery slices of the broader Narrative
Continuity phases. They do not create separate public phase names or duplicate shared infrastructure.

## 5. Wave 0 — freeze shared architecture

### Implement

Complete Narrative Continuity **Phase N0**:

1. Plain evidence, reference, candidate, selection, and reflection contracts.
2. Stable facet, phase, subject, category, salience, knowledge, and arc-key tokens.
3. `DiaryNarrativeContinuityDef` schema and safe XML fallbacks.
4. Pure selector/reference/reflection policy shells.
5. Standalone `NarrativeContinuityTests` project.
6. Save keys and old-save empty semantics on paper; no save fields yet.

### Do not implement yet

- No `DiaryEvent`/archive fields.
- No prompt-template field.
- No live DLC provider or hook.
- No source-specific page.

### Exit gate

- N0 pure tests pass without RimWorld assemblies.
- XML policy parses.
- Every individual DLC plan's required evidence maps to the contracts without adding a fifth facet.
- Review freezes token spelling and arc-key grammar.

## 6. Wave 1 — land shared persistence and prompt seam

> **Implementation status (2026-07-15): complete.** Narrative N1 now provides only shared save,
> archive-index, and optional prompt infrastructure; it does not add a real DLC provider, hook, or
> source-owned page. Wave 2 now consumes that seam only for exact source-owned monolith evidence.

### Implement

Complete Narrative Continuity **Phase N1**:

1. Per-POV evidence, references, selected keys, and `narrativeContext` persistence.
2. Archive copy/index/rebuild and retention behavior.
3. Pipeline payload and optional first-person prompt field.
4. Full/Balanced/Compact budgets.
5. `NarrativeContextBuilder` with no real DLC provider.
6. Synthetic/core fixture proving zero, one, and two selected lenses.
7. Old-save, archive, prompt-capture, and no-DLC RimTests.

### Exit gate

- Empty shared context produces unchanged prompt-field omission.
- Save/load and hot-to-archive compaction preserve bounded references.
- No real DLC behavior changes.
- Debug build and complete existing tests pass.

### Release checkpoint 1

Ship/review the shared infrastructure alone. Do not hide foundational save/prompt regressions inside
the first DLC feature diff.

## 7. Wave 2 — Anomaly semantic precision

> **Implementation status (2026-07-15): complete.** A0.0 reconfirmed all 16 installed psychic
> ritual keys and the four relevant monolith levels against local RimWorld 1.6 data; A0.1 added six
> exact package-gated ritual families plus the retained generic fallback; A0.2 split Stirring,
> Waking, and Void Awakened into mutually exclusive windows and attached visible source-owned N1
> chapter references. No provider, new Harmony hook, hidden Anomaly state, or extra page source was
> added. Wave 3 subsequently advanced through Biotech Phase 3 and Narrative N2-B; Phase 4 hardening is next.

### Implement in this exact order

1. **Anomaly A0.0** — reconfirm installed keys, classifier ownership, group orders, and package gates.
2. **Anomaly A0.1** — add the six exact psychic-ritual XML groups and DefInjected text.
3. **Anomaly A0.2** — split monolith discovery/activation chapters using existing verified routes.
4. Attach shared evidence to A0.2 only where N1's event-time truth is available; A0.1 remains XML
   classification rather than a new provider.

### Exit gate

- Ritual families classify exactly; generic fallback remains last.
- No settings rows appear without Anomaly.
- Monolith chapters deduplicate existing routes.
- No hidden entity, host, downside, or terminal outcome enters evidence.

### Release checkpoint 2

This is the low-cost prompt-quality release. Do not continue into A1 study/containment yet.

## 8. Wave 3 — Biotech family flagship

> **Implementation status (2026-07-16): in progress.** Biotech Phases 0–3 and Narrative N2-B are complete. Phase 4's
> automated hardening is also implemented: bounded B1 prompt projection/previews and context-detail
> tests, no-DLC metadata checks, ownership-preserving pending-state admission limits, and SpeakUp/RimTalk logic/build smoke
> all pass. New RimTest source additionally covers the real vanilla growth-letter configure/choice boundary,
> delayed live-child naming flush, loaded B1 prompts at every detail preset, and pre-cap Scribe/admission
> recovery. The shared DLC compatibility fixture now also covers null/base-only final-summary omission,
> real installed xenotype/title/ideoligion/precept/eligible-role/creepjoiner state, the exact official
> group/window/settings package matrix, fragile DLC hook signatures, and optional-adapter fail-open
> readiness. The first all-DLC runner pass completed 190/193; the second reached 191/193 and isolated
> the final contracts: vanilla CreepJoiner identity is tracker-backed rather than race-backed, and
> canonical birth context needs `tale=BiotechFamilyBirth` so prompt recovery selects the important B1
> template instead of PairDefault. Both now have focused regression assertions. The duplicate core
> package remains an independent environment error; a clean single-copy rerun and
> the remaining loaded-game acceptance matrix are required before B1 can close. Stable B1
> contracts/policy/settings now feed live, atomic birthday/growth-letter ownership with saved postponed
> rows, auto/choice diffs, mature ordinary fallback, progression consumption, saved family continuity,
> exact lesson/play observations, truthful child/supporter pages, and source-owned N1 identity evidence.
> N2-B now supplies fixed-order provider orchestration, empty future-provider stubs, and guarded exact
> family/current visible identity candidates on canonical growth pages. The exact birth owner now stages
> mature Tale/Thought signals around `ApplyBirthOutcome`, reserves the matching later ritual owner, attaches exact outcome/method facts to the
> family arc, selects at most two adult writers, preserves pending naming across save/load, rejects replay,
> and fails open to the mature routes. A user-directed scheduling exception recorded below permits
> Biotech Phase 5 work while Phase 4's manual B1 acceptance matrix remains open.
>
> **Acceptance checkpoint update (2026-07-17):** existing automated suites are reported passing, and
> new compiled RimTests cover the behavioral contracts in rows 3, 4, 7, 9, and 10. Their Biotech,
> RimTalk, and base-only runner passes remain TODO. Manual-only work is now the visual letter/preview
> checks, one real RimTalk UI read, and the three-launch Biotech on → off → on transition; therefore
> Biotech Phase 4 and Wave 3 remain open. At the user's direction, Odyssey O1.0
> proceeded as a documentation-only scheduling exception; it did not add runtime behavior or claim
> the Wave 3 release gate.
>
> **Biotech Phase 5 scheduling exception (2026-07-17):** at the user's direction, the five deferred
> Phase 4 manual rows no longer block starting Biotech 5. They remain mandatory for closing B1/Wave 3.
> Biotech 5 is implemented: plain gene identity/mutation contracts, an XML-driven pure salience and
> fallback policy, guarded live projection, nested versioned baseline persistence, exact xenogerm
> hooks, same-call Ability arbitration, bounded prompt context, canonical page ownership, and automated
> coverage. Live no-DLC, exact-hook, fallback, and save/reload acceptance remains a manual TODO.

### Implement in this exact order

1. **Biotech 0** — freeze growth/family DTOs, policy, event IDs, settings migration, and shared arc keys.
2. **Biotech 1** — observe growth birthdays and preserve the ordinary fallback.
3. **Biotech 2** — persist family arcs and select truthful supporter/parent POVs.
4. **Narrative N2-B** — add the Biotech family/identity provider and required evidence emissions.
5. **Biotech 3** — canonical birth/naming ownership and duplicate suppression.
6. **Biotech 4** — B1 compatibility, no-DLC, save/load, prompt, and release hardening.

### Required acceptance story

A growth moment produces one composite page, connects to the correct saved family arc, and can carry
one relevant shared lens. It never reveals predicted genes/xenotype or invents a parent's feelings.

### Exit gate

- Growth choice + birthday + trait/passion changes merge into one page.
- Birth/labor/naming ownership is exact.
- Family arc IDs survive save/load and archive compaction.
- Biotech inactive is silent.

### Release checkpoint 3 — B1

Release family/growth before genes, mechanitors, pollution, or secondary Biotech stories.

## 9. Wave 4 — Odyssey journey flagship

> **Implementation status (2026-07-17): O1.0-O1.5 and Narrative N2-O implemented; focused O1
> runtime/save acceptance complete.**
> Installed signatures and stable contracts are frozen; Odyssey policy passes 158 assembly-free
> assertions and shared Narrative Continuity passes 104. XML policy, guarded location/mobile-home
> capture, bounded journey/history Scribe state, silent old-save baselining, exact lifecycle hooks,
> the exact-POV journey/home provider, departure/landing reference factories, and focused RimTests are
> now present. Base-only startup skipped all five lifecycle fixtures cleanly; real takeoff cancellation,
> cross-layer travel/landing, and the Phase A/B/C save/reload sequence passed with exactly-once output,
> no lifecycle resurrection, and both reserved saves removed. O1.5 adds prompt projection/preset
> guarantees, localized fixtures, and routine/duplicate-owner hardening. Its broader loaded
> component/prompt-flow rerun remains separate, O2/O3 are not started, and the five deferred Biotech
> manual rows remain tracked separately.

### Implement in this exact order

1. **O1.0** — reconfirm signatures, ownership, IDs, journey arc grammar, and old-save semantics.
2. **O1.1** — pure location, novelty, history, cooldown, writer, and context policy.
3. **O1.2** — guarded location capture, journey/history persistence, mobile-home context.
4. **O1.3** — state-only takeoff/travel/landing lifecycle hooks; no pages yet.
5. **Narrative N2-O** — Odyssey journey/home provider and departure/landing references.
6. **O1.4** — landing page plus corrected launch truth and deduplication.
7. **O1.5** — hardening and delivery.

### Required cross-DLC acceptance story

With Biotech active, a family event aboard a committed gravship may select exact mobile-home context,
and a meaningful landing may select an exact family/home lens. Either DLC alone remains complete.

### Exit gate

- Ritual owns ceremony; travel commits state; landing owns arrival.
- Routine hops update history without pages.
- Pilot/copilot/crew selection is bounded.
- Quest-site landing never claims quest completion.
- Save/load cannot create a false first journey or landing.

### Release checkpoint 4 — O1

Do not add seasonal flooding, life support, or Mechhive choices in this wave.

## 10. Wave 5 — Royalty persona and progression flagship

> **Implementation status (2026-07-19): Royalty Phases 0–4 and Narrative N3-R core implemented;
> Wave 5 remains in progress.** By
> explicit user scheduling exception, Phase 0 began while the five Biotech B1 manual acceptance rows
> remain open; those earlier gates are neither passed nor removed. Phase 0 added plain
> persona/title/psylink snapshots, the shared `royalty-persona|<weaponThingId>|<bondEpoch>` arc
> grammar, XML-owned policy with safe fallbacks, pure deterministic lifecycle/trait/milestone/title/
> mutation ownership decisions. Phase 1 adds guarded live projections, versioned plain Scribe state,
> silent old-save persona/title/psylink baselining, deterministic normalization, and resettable
> transient correlation shells. N3-R replaces the empty Royalty provider slot with exact persona-arc
> and title-identity candidates, adds source-owned persona/title evidence factories, and snapshots
> current guarded Royalty facts using DefInjected prose. Phase 2 adds defensive persona coding,
> equipment, destruction, map-removal, and cleanup hooks; exact formation/transfer ownership;
> elapsed reconciliation for meaningful separation/recovery; late-visible silent adoption; live
> provider validation; package-gated groups, prompts, localization, prompt fixtures, and N3-R evidence
> attachment. Vanilla bonded persona thoughts are situational. Phase 3 instead verifies exact
> persona-trait `killThought` memories, gives the first qualifying Tale one canonical solo killer page,
> consumes first-kill truth independently from page acceptance, owns exact same-call ordinary combat
> Tales without stealing victim death, and enriches the existing wielder-death page from a pre-`UnCode`
> snapshot even when the live coded weapon is non-primary. Kill-signal buffers are bounded, one-shot,
> and fail open; save normalization preserves the observed/recorded distinction. The pure suites pass
> 245 Royalty, 125 Narrative Continuity, 665 capture, and 2,290 pipeline assertions. The Phase-2
> automated loaded suite is user-confirmed green; its manual matrix remains open. The first Phase-3
> loaded run reached 240/241 and exposed a pre-dead Tale timing gate. The expanded rerun reached
> 242/244; its only failures were fixture queries that treated the `talecombat` group key as a flushed
> event Def instead of reading the saved Tale-batch context. After those queries were fixed, the
> focused loaded rerun passed 244/244 on 2026-07-19. Phase 3 automated loaded coverage is green, but
> its hands-on matrix remains open. Phase 4 now adds defensive exact faction-title/bestowing/anima/
> neuroformer registration, immediate per-faction observations, loss-aware fallback scanning, bounded
> ritual/progression ownership, exact title-memory arbitration, XML-owned prompt context, and focused
> loaded fixtures. The initial Phase-4 suite ultimately passed 252/252. Consolidated adversarial
> hardening then added immediate DLC-off load invalidation, master/group-consistent consumption,
> per-route combined fallback selection, missing-pawn expiry pruning, retry-safe hook completion, and
> stronger pure/loaded fixtures. Pure suites pass 316 Royalty, 2,437 pipeline, 665 capture-policy, and
> 125 Narrative Continuity assertions, and both assemblies build. The user-confirmed expanded loaded
> suite passes 256/256; all Phase-2/3/4 hands-on matrices remain open, so the R1 release
> gate and Wave 5 completion are not claimed.

### Implement in this exact order

1. **Royalty 0 (implemented; contract-only)** — pure persona/title/psylink contracts and policy.
2. **Royalty 1 (implemented; structural, page-silent)** — guarded collection, persistence, and silent baseline.
3. **Narrative N3-R core (implemented; provider/evidence only)** — persona/title provider plus required bond/identity evidence.
4. **Royalty 2 (implemented; automated green, manual acceptance pending)** — persona lifecycle pages.
5. **Royalty 3 (implemented; automated loaded green, manual acceptance pending)** — first consequential kill and death integration through existing owners.
6. **Royalty 4 (implemented; pure/build/expanded loaded green, manual acceptance pending)** — immediate title/psylink cause correctness and scanner fallback.
7. **Royalty 5 (implemented; pure/build/automated loaded green at 267/267, manual acceptance pending)** — exact inheritance correlation, explicit heir appointment, and succession evidence.

### Exit gate

- Persona bond epoch and weapon identity are stable.
- Formation/separation/recovery/end pages do not duplicate combat/death pages.
- Title changes preserve faction, before/after, and cause.
- Bestowing/anima/neuroformer paths do not double-write progression.
- Royalty inactive is silent.

### Release checkpoint 5 — R1

Do not include succession, permits, or Royal Ascent yet.

## 11. Wave 6 — Biotech identity and mechanitor depth

> **Implementation status (2026-07-18): Biotech 5, Biotech 6, and the first N3-B identity extension
> are implemented by explicit scheduling exception; the N3-B loaded acceptance passed and Phase-6
> live acceptance is pending.** Pure policy, guarded
> persistence, exact/fallback ownership, bounded prompt context, one exact salient-gene identity
> candidate, and persisted-key repetition policy are present. This does not claim the Wave 3/B1
> manual release gate.

### Implement in this exact order

1. **Biotech 5 (implemented; live acceptance pending)** — pure contracts/XML selector, guarded live
   projection, versioned baseline, exact mutation ownership, fallback emission, and prompt context.
2. **N3-B gene identity (implemented; loaded acceptance passed)** — exact-subject salient-gene
   candidate, stable persisted selection key, and the shared repetition penalty.
3. **Biotech 6 (implemented; live acceptance pending)** — mechlink, first controlled mech, first
   consequential mech combat, significant loss, and boss-tier mechanitor lifecycle.
4. Extend **N3-B** with mechanitor bond/chapter references.

### Exit gate

- Prompts receive themes, not gene dumps.
- Custom xenotypes work without hardcoded vanilla catalogs.
- Routine mech creation/bandwidth churn stays silent.
- Boss and meaningful-loss ownership is exact and bounded.

### Release checkpoint 6 — B2 core

Pollution, psychic bonds, and interrupted deathrest remain deferred to Wave 11.

## 12. Wave 7 — Anomaly knowledge, containment, and reveal

> **Progress (2026-07-21): A1.0–A1.4 and A2.0–A2.2 implemented.** The primitive policy,
> detached DTOs, exact study/containment/visible-creepjoiner/surgical-disclosure/ghoul-infusion hooks, fail-closed catalog route, seven-key
> normalized save baseline, lifecycle-cleared bounded caches/scopes, and five package-gated prompt
> groups are implemented. N3-A now maps only exact visible ghoul/containment/monolith/creepjoiner
> source evidence into bounded identity/chapter/pressure candidates; it adds no source, page, owner,
> save field, or terminal-void path. Deduplicated review hardening shares its exact monolith source gate
> and prevents the slice from changing non-monolith event-window detail budgets. Focused suites pass 639
> Anomaly, 708 catalog, 135 save-normalization, and 289 Narrative assertions; runtime and 354-test RimTest
> assemblies build. The user-confirmed corrected Anomaly-active rerun passed 354/354, including the
> new N3-A evidence/reference/key/context assertions and the repaired deterministic memory fixtures.
> The exact-only classifier and all hooks are Anomaly-gated. Loaded A1.1 state
> coverage and the original nine A1.3 containment fixtures passed in their recorded profiles. A later
> full run passed 314/315, including the new containment scope-state fixture; its sole failure was a
> false-positive visible-label assertion, corrected to exact localized fallback equality. The
> user-confirmed corrected rerun passed all 315 compiled fixtures. That aggregate result corresponds to
> the unchanged 315-fixture assembly whose source inventory contains all ten A1.2 fixtures; no preserved
> per-method run artifact independently enumerates them. The old A1.2 row is closed under acceptance of
> the user-confirmed full-suite result, not from compilation alone. The user also confirms the complete
> automated A1.4 Anomaly-active 316-fixture run green; no preserved per-method log is claimed. The
> first 323-fixture A2.0 active run passed 321 and reported two false-negative creepjoiner assertions:
> each solo event existed, but the fixture incorrectly matched the visible subject as a pairwise
> recipient. The corrected fixtures assert solo writer ownership plus the captured subject ID and
> compile in the rebuilt 323-test assembly. Adversarial hardening now promotes vanilla aggressive
> rejection to truthful hostile state, enforces same-map speaker POV, releases nested visible outcomes
> from letterless rejection, closes unverifiable committed markers as terminal barriers, and adds four
> loaded fixtures. The later user-provided 335-fixture run passed every A2.0 case, closing that embedded
> 327-fixture acceptance debt as aggregate evidence. A2.1 adds an exact composite
> recipe/tracker/Pawn result correlation, nonterminal visible-only disclosure continuity, exact
> surgeon-first/subject-second POVs, and fail-open `DidSurgery` ownership. Eight focused loaded cases plus
> expanded registration/lifecycle assertions compile in the 335-test assembly. Its first in-game run
> passed 333/335: only the two joined-subject pair assertions failed because the shared test setup still
> forced A2.0's one-writer cap. Setup now uses the supported two-writer ceiling. The next user-provided
> run passed 334/335 and confirmed that correction; its sole failure was the live fixture expecting
> shortened role-context keys after it had already found the exact pair page. The fixture now asserts
> the frozen `initiator_witness_role` / `recipient_witness_role` schema. The user-confirmed corrected
> rerun passed 335/335, closing the A2.1 loaded acceptance debt. Review hardening now skips XML policy
> snapshots for unrelated Tales, prevents
> a defensive post-create state mismatch from releasing a duplicate generic surgery page, rejects
> mismatched arc identities, and clears event IDs from merged blank replay barriers. The separate
> Anomaly-inactive profile, disposable missing study/containment-hook compatibility profiles, and real
> process-boundary save/reload remain explicit deferred rows. A2.2 now verifies only a guarded
> non-ghoul → ghoul post-state, owns an exact surgeon-first/subject-second `DidSurgery` in a bounded
> scope that cannot overlap A2.1, emits only exact eligible POVs, and suppresses the generic signal only
> after the dedicated event exists. It adds no save field, hidden-state projection, DLC dependency, or
> N3-A candidate. The first expanded loaded run reached 342/347. Two failures exposed one production
> defect—the completed ghoul no longer passed the event factory's live colonist check, so its exact
> preverified page reference was omitted—and three were fixture assumptions about surgeon-only
> exceptional fallback, delayed `Wounded` batching, and Biotech's policy-selected salient gene. The
> exact-writer reference path and fixtures are corrected; the user-confirmed rerun passed 347/347.
> Follow-up adversarial review gives ghoul replay dedup its own XML knob, centralizes live ghoul reads
> through `DlcContext`, narrows the no-save fixture to prospective A2.2 marker names, and keeps an
> already-created dedicated page authoritative if its reference/generation handoff throws.

### Implement in this exact order

1. **A1.0** — pure contracts, policy Def, knowledge/containment planners, and shared token mapping.
2. **A1.1** — Anomaly catalog route/persistence and **N3-A** provider skeleton.
3. **A1.2** — study/knowledge milestones.
4. **A1.3** — exact containment breach ownership.
5. **A1.4** — A1 hardening/delivery.
6. **A2.0 (implemented; loaded acceptance green within the 335-fixture aggregate run)** — visible creepjoiner state and old-save baseline.
7. **A2.1 (implemented; loaded acceptance green at 335/335)** — surgical visible disclosure/outcome ownership.
8. **A2.2 (implemented; loaded acceptance green at 347/347)** — ghoul transformation.
9. **N3-A (implemented; loaded acceptance green at 354/354)** — visibly authorized chapter, pressure, and identity candidates only.

### Exit gate

- Study/containment/reveal events have exact visible evidence.
- Deferred Tale transactions fail open.
- No hidden state reaches prompt, save metadata, diagnostics, or reflection candidates.
- Anomaly inactive is silent.

### Release checkpoint 7 — A1/A2

The user-confirmed 354/354 Anomaly-active run closes N3-A automated loaded acceptance and permits
Wave 8. The separately tracked Anomaly-inactive compatibility and process-boundary save profiles
remain explicit follow-ups and are not implied by that aggregate run.

Terminal void outcomes remain deferred to the endings wave.

## 13. Wave 8 — Odyssey environmental depth

> **Progress 2026-07-21:** the XML-first O2 slice and Narrative N3-O environmental extension are
> implemented. O2 replaces the stale `Flooding` mood matcher with the Odyssey-gated, prompt-only
> `SeasonalFlood` `ThingPresent` observer, leaves generic visible-condition projection unduplicated,
> keeps `GravNausea` as bounded prompt context, and enriches the one canonical landing from exact
> successful worker outcomes. N3-O reuses that observer's saved exact-map row at event time to propose
> at most one detached, per-POV seasonal-flood pressure lens beside the existing mobile-home lens; it
> adds no rescan, hook, page, save field, setting, or landing consequence. Pure and build validation is
> recorded in the source plans. The life-support feasibility spike and combined in-game Odyssey
> acceptance run remain open.

### Implement

Complete **Odyssey O2** and extend **N3-O**:

1. Seasonal Flooding correction through the existing observer model.
2. Audit visible active conditions before adding bespoke observers.
3. Add bounded `GravNausea` context.
4. Add negative landing consequences only after the exact outcome hook is verified.
5. Keep life-support crisis deferred unless its separate spike proves a safe visible seam.
6. **Narrative N3-O (implemented 2026-07-21)** — project only the existing source-owned seasonal-
   flood observer fact through the shared exact-POV provider and ordinary lens budget/history.

### Exit gate

- Environmental pressure is location-scoped and bounded.
- Generic visible-condition context is not duplicated.
- No recurring flood/weather page spam.
- No guessed life-support behavior.

### Release checkpoint 8 — O2

## 14. Wave 9 — Royalty succession, permits, and ascent

### Implement in this exact order

1. **Royalty 5 (implemented; automated loaded green at 267/267, manual acceptance pending)** — exact inheritance correlation and succession evidence.
2. **Royalty 6 (code-complete; pure/runtime/RimTest builds green; loaded suite green at 278/278;
   manual acceptance and a separately recorded Royalty-inactive run pending)**
   — successful dramatic permit families and friendly-raid ownership. Installed RimWorld 1.6 audit
   confirms `FactionPermit.Notify_Used()` as the post-success edge and quick `RaidFriendly` as the
   earlier same-action signal; the implementation uses exact allowlisted families plus lossless
   bounded faction/map stage/claim/expiry ownership. Adversarial hardening distinguishes master-off
   generic ownership from group-off source consumption, makes fallback lookup non-reentrant and
   cap-safe, and hides package-specific Prompt Studio rows without unloading their Defs. The full
   278-test suite is user-confirmed green in a loaded game; that result does not close the manual
   matrix or establish a separate Royalty-inactive profile run.
3. **Royalty 7 (code-complete; pure/runtime/287-test RimTest builds green; corrected loaded rerun and
   manual acceptance pending)** — Royal Ascent start, bounded pressure, and terminal chapter. Installed lifecycle audit
   proves acceptance is commitment only, `Quest.End(Success|Fail)` owns the hosting-quest outcome,
   and later `SentWithExtraColonists`—not Quest success—owns escape/credits. Exact root-first routing,
   one stable witness, append-only bounded quest-instance/arc identity, start-only window, active
   pressure context, and one truthful terminal page are implemented without polling or a paid-DLC
   dependency. The XML master and live DLC gate fail closed before the exact adapter emits anything.
   Pure suites pass 463 Royalty, 2,734 pipeline, and 132 Narrative assertions. The first expanded
   loaded run passed 284/287 and exposed three fixture-only gaps: stale official-DLC group/window
   expectations, an intentionally invalid signal used by the fanout assertion, and a pre-existing
   generic Quest dedup entry on the stable witness. Corrected fixtures build and await a rerun; no
   hands-on row or separate Royalty-inactive profile is recorded as passed.
4. Extend **N3-R** with succession identity, permit authority, Ascent journey, and court-pressure
   candidates.

### Exit gate

- Succession names deceased/heir only from exact vanilla facts.
- Routine permits remain silent.
- Ascent produces beginning, bounded pressure, and one exact ending.
- No arrival/outcome is claimed before its verified signal.

### Release checkpoint 9 — R2/R3

Royalty Phase 8 final compatibility remains in Wave 13 so it covers the completed shared system.

## 15. Wave 10 — Ideology stance and conversion foundation

### Implement in this exact order

1. **Ideology 0** — complete pure correlation/lexical resolver and belief policy.
2. **Ideology 1** — guarded live snapshot and saved event-time belief source.
3. **Narrative N3-I** — adapt high-confidence stance output into the single shared
   `interpretation` candidate category.
4. **Ideology 2** — conversion/crisis/ritual/speech mutation capture and required event integrations.
5. **Ideology 3** — bounded passive certainty/ideology tracking for future reflection evidence.
6. Complete the common N3 selector/provider tests across Royalty, Biotech, Anomaly, and Odyssey.

### Exit gate

- Structural live correlations beat lexical fallback.
- Ambiguous/unrelated beliefs return empty.
- Ordinary enrichment consumes the shared interpretation slot and obeys the global two-lens cap.
- Other DLC combinations still integrate with Ideology inactive.
- No standalone belief reflection yet.

### Release checkpoint 10 — belief enrichment

This deliberately ships useful event-relative belief context before reflection scheduling.

## 16. Wave 11 — secondary bonds and terminal source chapters

Implement these independent source slices in this exact order, with a build/release-quality gate after
each one:

1. **Biotech 7** — pollution pressure/escalation through observed-condition policy.
2. **Biotech 8** — psychic-bond lifecycle and interrupted deathrest.
3. **Anomaly A3.0** — exact terminal void outcome and source-owned reflection evidence only.
4. **Odyssey O3** — verified major exploration/Mechhive source and choice facts only; unresolved hooks
   stay explicitly deferred.
5. Add/extend N3 provider emissions for each verified source slice.

### Exit gate

- Every supported source arc has exact beginning/change/ending references where facts permit.
- Unsupported life-support/Mechhive/optional stories remain absent rather than guessed.
- N3 is complete for every implemented provider.

### Release checkpoint 11 — feature-complete sources

No new immediate DLC page sources are added after this checkpoint until the integrated system ships.

## 17. Wave 12 — unified reflections

### Implement in this exact order

1. **Narrative N4** — pure `ReflectionCoordinator`, opportunity contracts, linked-memory selection,
   archive lookup, cooldowns, and consumption rules.
2. Adapt existing arc/quadrum/day scheduling to the coordinator without changing source truth.
3. **Ideology 4** — belief-reflection page and scheduling as one coordinator opportunity.
4. Connect only already-planned source-owned terminal reflection evidence from Royalty, Biotech,
   Anomaly, and Odyssey.
5. **Ideology 5** — tune automatic coverage and exceptional corrections after real combined prompt
   fixtures exist.

### Exit gate

- One rest opportunity creates at most one reflection.
- Major arc -> linked cross-arc -> belief -> quadrum -> day priority is deterministic.
- Two unrelated DLC memories cannot form a cross-arc reflection.
- Failed dispatch does not consume pending state.
- Disabled groups cannot accumulate a catch-up flood.

### Release checkpoint 12 — reflections

## 18. Wave 13 — global compatibility and final hardening

### Implement in this exact order

1. **Royalty 8** — compatibility and release hardening against the final shared layer.
2. **Ideology 6** — compatibility, documentation, and release hardening.
3. **Narrative N5** — global tuning, performance, save migration, localization, and combination matrix.
4. Re-run every source-specific hardening scenario whose shared prompt/reflection behavior changed.
5. Update `DOCUMENTATION.md`, `TEST_COVERAGE_PLAN.md`, `EVENT_PROMPT_MAP.md` where applicable,
   localization, and `CHANGELOG.md`.
6. Rebuild and stage the committed DLL.

### Required final matrix

- zero DLC;
- each DLC alone;
- Royalty + Biotech;
- Ideology + Biotech;
- Biotech + Odyssey;
- Royalty + Anomaly;
- Ideology + Anomaly;
- Ideology + Odyssey;
- all available DLCs active;
- save/load before generation, after compaction, and with pending reflection;
- Full/Balanced/Compact prompt capture;
- DLC group settings disabled/re-enabled without backlog;
- no hidden Anomaly or unverified ending facts.

### Final exit gate

- All pure projects and RimTest requirements pass.
- All XML parses.
- Debug build succeeds and committed DLL is current.
- No-DLC operation is clean.
- Prompt lens cap, repetition, archive references, and reflection priority are proven.
- Every subordinate plan's definition of done is either met or explicitly marked deferred with its
  unverified source reason.

## 19. Things an agent must not reorder

- Do not land source save fields before N1.
- Do not implement Odyssey O2/O3 before O1 is released.
- Do not implement Royalty succession/permits/ascent before R1 persona/title correctness.
- Do not implement Anomaly A1/A2 before the A0 classifier/chapter baseline.
- Do not implement Biotech gene/mechanitor breadth before B1 family/growth hardening.
- Do not add belief reflections before N4.
- Do not add terminal/cross-arc reflections while immediate source ownership is incomplete.
- Do not run N5 early; every new provider would invalidate its combination matrix.
- Do not let an attractive optional story displace the current wave.

## 20. Blocker and deferral policy

If a wave hits an unverified private hook or missing visible truth:

1. Complete safe pure/XML/state work that does not depend on the hook.
2. Record the exact unresolved signature/state question in the source plan.
3. Defer only that sub-feature; do not invent a fallback claim.
4. Continue within the same wave only when the remaining slice is independently shippable and its
   exit gate can still be truthful.
5. Do not jump to a later master wave unless the current wave's release checkpoint explicitly permits
   the deferred item.

Examples already authorized for deferral are Odyssey life-support, unverified Mechhive choice state,
and any exact player-facing heir-appointment source that cannot be distinguished from automatic
fallback assignment.

## 21. Completion record template

Use this block in the implementation PR/task summary for every slice:

```text
Master wave:
Subordinate phase:
Behavior shipped:
Behavior explicitly deferred:
Save/XML tokens added or changed:
Dedup/page owner:
No-DLC result:
Pure tests:
RimTests:
XML validation:
Debug build:
Documentation/changelog:
Next permitted slice:
```
