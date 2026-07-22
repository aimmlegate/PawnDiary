# Lore Memory Seed Plan

**Date:** 2026-07-22 · **Status:** APPROVED PLAN — nothing implemented yet.
**Lore source:** `design/RIMWORLD_LORE_COLLECTION.md` (the dense wiki-derived corpus; §ref below
always means a section of that file).
**Host subsystem spec:** `design/MEMORY_SYSTEM_DESIGN.md` + `design/MEMORY_WIRING_PLAN.md` — the
memory layer is already live-wired. This plan adds an authored content source plus the smallest
recall/prompt changes needed to keep retrieval truthful, bounded, and setting-aware.

---

## 1. Goal, scope, and non-goals

Give diary entries RimWorld-lore texture through authored pawn memories and a compact world-model
primer. The game must never call an additional LLM to prepare, select, rewrite, summarize, or format
prompt context. The normal diary-generation request may weave, paraphrase, or ignore the supplied
memory; every step that prepares that request is deterministic local code.

The approved mechanism has three layers:

1. **Initial lore memories** — static first-person sentences selected once for a pawn and deposited
   into `PawnMemoryRepository`. Default target count: four per pawn.
2. **Lore primer** — compact static system-prompt clauses selected from the effective
   Compact/Balanced/Full context-detail level, including per-lane overrides.
3. **Progression lore memories (deferred L5)** — at most one authored memory attached to a
   successfully registered identity-changing event, with strict per-pawn lifetime limits.

L1-L4 form the initial releasable feature. L5 remains in this same plan so its persistence and Def
contracts are not designed into a corner, but it is explicitly deprioritized and may ship later.

Out of scope: a lore source for prompt enchantments; LLM-based context preparation; unbounded
free-text extraction; automatic rewriting of already established pawn histories; lore rows in the
player-facing diary/archive UI.

## 2. Locked behavior summary

| Decision | Locked behavior |
|---|---|
| Initial count | `maxInitialLoreSeedsPerPawn` is a lifetime deposit ceiling, default 4. Lowering it later never deletes or suppresses existing memories. |
| Stable retries | Persist the initially selected Def names. Retries deposit missing targets only; they never resample replacements. A removed Def is skipped without replacement. |
| Specific seed | Reserve one target for a positively constrained pawn-specific seed. Prefer an eligible core seed, then any eligible specific seed. This is guaranteed for supported base-game human pawn fixtures and best-effort for unknown modded pawns. |
| Narrative age | Store real `createdTick`/`lastRecalledTick` at deposit and a separate `narrativeAgeOffsetTicks`. The offset affects the rendered age band and minimum-age guard only. |
| Retention | Ordinary lore: importance 0.35, non-core. Core identity lore: importance 0.85, core retention, maximum two ever allocated per pawn. |
| Core frequency | Core lore uses a 20-day XML-tunable recall cooldown after it is surfaced; persistence and recall frequency remain separate concerns. |
| Duplicate authored facts | A pawn can receive a given lore-seed Def at most once across initial and progression deposits. |
| Localization | Tags, keywords, provenance, and importance are frozen. Displayed prompt prose resolves the current DefInjected text by provenance, with saved prose as fallback. |
| Disable semantics | Disabling lore memories suppresses deposit and recall without deleting rows. Disabled lore is excluded from normal memory-cap eviction; dead-owner cleanup may still remove it. |
| Primer | Independent toggle. System-prompt suffix, not a user-message field. All 11 first-person templates receive it; neutral death/arrival and title templates do not. |
| Memory prompt budget | Universal hard ceiling of two whole lines and 500 characters. `MemoryContext` is required in every context-detail preset when non-empty; no preset-specific trimming. |
| Progression | At most one owner-only seed after a successfully registered progression event; lifetime cap 4; synthetic non-colliding deposit ID; exact event Def tokens only. |

## 3. Memory fragment contract and provenance

Lore seeds remain ordinary `MemoryFragment` rows. Do not introduce a fragment subclass or a second
repository. Add these optional fields to `MemoryFragment`, its save exposure, and
`MemoryFragmentSnapshot`:

- `loreSeedDefName` — empty for lived memories; the authoritative provenance for a lore seed.
- `narrativeAgeOffsetTicks` — zero for lived and progression memories; default two-year offset for
  initial lore, clamped at deposit to the pawn's biological age.

Initial seed deposit identity:

```text
sourceEventId = "loreseed:" + seedDefName
```

Deferred progression identity:

```text
sourceEventId = "loreseed-progression:" + eventId + ":" + seedDefName
```

Real event IDs are GUID `N` strings, so neither sentinel collides with a lived event. Centralize the
prefixes and construction/parsing helpers; never scatter string-prefix logic through adapters.

Every lore fragment also receives the closed token `MemoryTagTokens.Lore`. Queries never emit the
token, so it adds no score; it exists for filtering, diagnostics, tests, and policy application.

### 3.1 Real time versus narrative age

For an initial seed deposited at `depositTick`:

```text
createdTick = depositTick
lastRecalledTick = depositTick
narrativeAgeOffsetTicks = min(policy offset, pawn biological age in ticks)
recallCount = 0
```

The effective narrative age is:

```text
(currentTick - createdTick) + narrativeAgeOffsetTicks
```

Use effective narrative age only for:

- the renderer's age label/band; and
- `minRecallAgeTicks`, allowing lore to be eligible on the first prompt.

Use real stored ticks, with no offset, for:

- recency decay;
- normal/core cooldowns and repetition penalties;
- stale-eviction age;
- ordering and save repair.

This separation lets a pawn describe an old memory immediately without making a newly inserted row
look stale to eviction or permanently depressing its first recall score. Custom offset settings can
never make a pawn remember a time before their biological life.

### 3.2 Live localized prose with saved fallback

The saved fragment text is the fallback that makes old saves self-contained. When building the
current recall snapshot:

1. If `loreSeedDefName` resolves, use the current language's DefInjected `DiaryLoreSeedDef.text`.
2. If the Def was removed, renamed, or cannot resolve, use the saved fragment text.
3. Never recalculate tags, keywords, importance, retention tier, or selection from a changed Def.

Only prose is live-localized. A language change must not alter which memories match or how strongly
they score.

## 4. Retention and recall policy

`DiaryLoreSeedDef` declares an explicit `retentionTier` of `ordinary` or `core`; authors do not set
an arbitrary per-row importance.

- **Ordinary:** `loreSeedOrdinaryImportance = 0.35`; non-core; normal recency, cooldown, and eviction.
- **Core:** `loreSeedCoreImportance = 0.85`; resolved importance must be at least the active
  `coreImportanceThreshold` (clamped to the selector's legal range); protected by existing core
  stale-eviction behavior.

Core lore is for high-confidence identity/origin facts, not merely interesting trivia. Authoring
may mark a seed core only when eligibility proves the claim through an exact backstory Def or
equally explicit state such as a xenotype. Broad categories such as `Offworld`, `Tribal`, or
`Imperial` are insufficient by themselves for lifelong core status.

Persist a bounded `coreLoreSeedDefNamesEverDeposited` history. Its default maximum is two across
initial and progression lore. The cap is lifetime allocation, not current repository occupancy: if
a core row is ever removed, the system does not rotate in a new identity fact.

After a core lore row is actually selected and delivered to a prompt, apply
`coreLoreRecallCooldownTicks = 1,200,000` (20 RimWorld days). Ordinary lore keeps the normal memory
cooldown. After the core cooldown, the seed is merely eligible again; normal topical scoring still
decides whether it surfaces.

## 5. New Def contract — `DiaryLoreSeedDef`

Implement `Source/Defs/DiaryLoreSeedDef.cs` and `1.6/Defs/DiaryLoreSeedDefs.xml`.

```xml
<PawnDiary.DiaryLoreSeedDef>
  <defName>LoreSeed_Mechanoid_Tribal</defName>
  <label>old machines (tribal)</label>
  <text>The elders said the old machines sleep under the mountains, and waking them ended the ancestors' world.</text>

  <!-- initial | progression | both -->
  <usage>initial</usage>
  <!-- ordinary | core -->
  <retentionTier>ordinary</retentionTier>
  <weight>1.0</weight>
  <mutexGroup>mechanoid_origin</mutexGroup>

  <!-- `lore` is implied. All tokens come from MemoryTagTokens. -->
  <tags><li>combat</li><li>danger</li><li>dread</li></tags>
  <!-- Stable, language-neutral memory-query values only. -->
  <keywords><li>Mechanoid</li><li>MechanoidCluster</li></keywords>

  <!-- Any-of within a list; different positive list types are all required when populated. -->
  <backstoryCategories><li>Tribal</li></backstoryCategories>
  <excludeBackstoryCategories />
  <backstoryDefNames />
  <excludeBackstoryDefNames />
  <xenotypeDefNames />
  <hediffDefNames />

  <!-- Existing registered progression event defName tokens; never model-facing progressionKind text. -->
  <progressionEventDefNames />
</PawnDiary.DiaryLoreSeedDef>
```

Validation in the Def config-error path:

- non-empty DefInjected text, at most the fragment text limit (200 characters);
- valid `usage` and `retentionTier` closed tokens;
- positive finite weight;
- tags contained in `MemoryTagTokens` (unknown tags are dropped and reported);
- at most eight keywords, normalized by the exact `MemoryExtraction` helper rather than a copy;
- at least one `progressionEventDefNames` entry when usage is `progression` or `both`;
- no localized label, title, description, or prose in any matcher field;
- core authoring has at least one high-confidence positive constraint.

`text` and `label` are DefInjected-translatable. English and Russian text are authored independently;
Russian is native prose, not a mechanical calque. Conditional prose must describe remembered history
rather than assert that a xenotype, faction, title, or health condition is still currently true.
Eligibility is evaluated at deposit time only. Later state changes never retroactively erase the old
memory; a later progression memory may describe the change.

## 6. Persisted per-pawn planning state

Add a small additive save contract beside the pawn's memory/diary state:

- `initialLoreSeedTargetDefNames` — the target roster chosen on the first initial plan, maximum four
  by default; an empty list means planning has not happened.
- `progressionLoreSeedDefNamesEverDeposited` — exact progression Defs actually deposited, bounded by
  the progression lifetime cap.
- `coreLoreSeedDefNamesEverDeposited` — exact core Defs actually deposited, bounded by the core
  lifetime cap.

The initial roster is persisted before attempting deposits. Every later retry checks only those
Def names and deposits missing rows. Catalog additions, weight changes, localization changes, or a
save/reload cannot reshuffle a pawn's established target set. If a target Def disappears, leave the
name in the roster and skip it; do not silently replace it.

The initial maximum is a deposit-time ceiling. Lowering it later does not delete, hide, or suppress
seeds already received. A roster created under the lower value stays at that value; increasing the
setting later does not append new targets.

A Def name may occur at most once for a pawn across both histories. `PlanProgression` excludes every
initial target name as well as the lifetime progression history, even if the original row was later
evicted. This favors narrative stability over opportunistic replacement.

## 7. Pure planning — `LoreSeedPlanner`

`Source/Pipeline/Memory/LoreSeedPlanner.cs` is pure and Verse-free. Expose two intention-revealing
entry points, not a mode flag:

```text
PlanInitial(candidates, pawnFacts, policy, deterministicSeed) -> List<LoreSeedPick>
PlanProgression(candidates, pawnFacts, progressionFacts, policy, deterministicSeed) -> LoreSeedPick?
```

Shared private helpers may handle eligibility, normalization, weights, and tier caps.

Suggested DTO shape in `MemoryContracts.cs`:

```text
LoreSeedCandidate {
  seedDefName, fallbackText, tags, keywords, usage, retentionTier, weight, mutexGroup,
  backstoryCategories, excludeBackstoryCategories,
  backstoryDefNames, excludeBackstoryDefNames,
  xenotypeDefNames, hediffDefNames, progressionEventDefNames
}

LoreSeedPawnFacts {
  pawnId, biologicalAgeTicks,
  backstoryCategories, backstoryDefNames,
  xenotypeDefName, hediffDefNames,
  initialTargetDefNames, progressionDefNamesEverDeposited,
  coreDefNamesEverDeposited
}

LoreSeedProgressionFacts { eventId, eventDefName }

LoreSeedPolicy {
  enabled, maxInitialSeeds, minSpecificInitialSeeds,
  maxProgressionSeedsLifetime, maxCoreSeedsLifetime,
  ordinaryImportance, coreImportance, narrativeAgeOffsetTicks
}
```

### 7.1 `PlanInitial`

1. Filter to `initial`/`both` candidates satisfying every populated constraint.
2. Enforce the remaining lifetime initial and core capacities.
3. Reserve `minSpecificInitialSeeds = 1` slot when a positively constrained candidate is eligible.
   A positive backstory category/Def, xenotype, or hediff constraint makes a candidate specific.
   Prefer a core-specific candidate when core capacity remains, otherwise any specific candidate.
4. Fill remaining slots by deterministic weighted sampling without replacement.
5. Honor `mutexGroup` only inside this one target-set construction: selecting a Def removes its
   siblings from the current pool.
6. Return the complete target roster. The impure adapter persists it before deposit attempts.

The base catalog and reachability tests must make the specific reservation succeed for supported
base-game human pawn fixtures. Arbitrary modded pawns cannot be guaranteed an authored match. When
none exists, fill from eligible generic seeds and emit one bounded diagnostic; never invent an origin
or reject diary generation merely to satisfy the quota.

Retries do not call the sampling path again. They resolve the persisted target roster and attempt
only missing deposits. `mutexGroup` has no cross-retry or lifetime semantics.

### 7.2 `PlanProgression` (deferred L5)

1. Filter to `progression`/`both` candidates whose `progressionEventDefNames` contains the exact
   registered event Def token and whose pawn constraints match.
2. Exclude every initial target and every progression Def used previously.
3. Enforce the remaining progression lifetime cap (default four) and core lifetime cap (default two).
4. Deterministically choose at most one candidate for this event.

One event can produce only one seed, so `mutexGroup` has no cross-event history. The lifetime history
blocks exact Def reuse only.

Use stable `System.Random` seeds derived from stable primitive inputs (world seed, pawn ID, and for
progression the event ID/Def token). Never use `Verse.Rand` in the pure pipeline.

## 8. Impure lifecycle wiring

Centralize live pawn reads, Def resolution, settings access, ticks, repository mutation, Scribe state,
and logging in `DiaryGameComponent.Memory.cs` or a narrow adjacent adapter. The pure planner sees only
the DTOs above.

### 8.1 Initial and old-save path

Do not run a bulk save-load migration. Immediately before a pawn's first eligible event is prepared:

1. Gate on the memory system, `settings.enableLoreSeeds`, and XML policy.
2. If no initial roster exists, collect pawn facts, call `PlanInitial`, and persist the returned Def
   names before registering fragments.
3. Resolve every persisted target and deposit only missing `sourceEventId` sentinels.
4. Complete this check before memory recall so a seed can surface on the same first prompt.

Subsequent eligible events cheaply re-run the idempotent missing-target check. This gives old saves
lazy migration without a startup hitch and gives partial/faulted deposits a safe retry. Pawns that
never enter generation do no work.

Collect exact childhood/adulthood backstory Def names as well as the union of their spawn categories.
Use `DlcContext`-style guarded access for xenotype state, and plain Def-name strings for optional DLC
or mod content. Never match localized backstory titles/descriptions and never require a paid DLC.

### 8.2 Toggle and eviction semantics

`enableLoreSeeds = false` has three effects:

- no new initial or progression deposits;
- filter lore-tagged/provenance rows out before recall;
- exclude lore rows from normal per-pawn and global cap planning while disabled.

Do not delete the rows or histories. Re-enabling makes them active again and immediately subject to
ordinary caps/eviction. Dead-owner cleanup may remove disabled lore because the owner no longer has a
future recall path.

The recall/eviction filters must inspect stable provenance/tag data, not localized text.

### 8.3 Deferred progression attachment point

L5 attaches in the centralized `DepositMemoryFragments` flow, immediately after the ordinary lived
memory is successfully deposited for a registered progression event:

- trigger only for the progression owner, never witnesses;
- trigger only when the event was actually registered and survived its family's existing gates;
- use the event's exact Def token (`XenotypeChanged`, `GeneIdentityChanged`, `PsylinkLevel`,
  `BiotechMechlinkInstalled`, `RoyalTitleGained`, `RoyalTitlePromoted`, and audited equivalents);
- never infer from model-facing `progression_kind` text or add secret behavior for disabled/dropped
  event families;
- set real created/last-recalled ticks and zero narrative-age offset;
- record progression/core lifetime history only after repository registration succeeds.

## 9. Recall projectability and prompt memory limits

The current pipeline can select and bump a memory before a later template/context decision omits it.
This plan closes that accounting hole before adding more recallable content.

1. Add one central projectability decision based on the finally chosen prompt template.
2. Recall only when that template declares a `MemoryContext` projection.
3. Add `MemoryContext` to the day, quadrum, and arc reflection templates. Together with the existing
   eight templates, all 11 first-person shapes become projectable.
4. Keep neutral death, neutral arrival, and title templates non-projectable.
5. Render/fit whole selected memories, attach the final non-empty string, then bump only the picks
   that survived fitting and are guaranteed to reach the prompt.

`PromptContextSelector` must treat a non-empty `MemoryContext` as required in Full, Balanced, and
Compact. It may exceed the preset's soft context budget, but it remains bounded by one universal XML
policy:

```text
memoryContextMaxLines = 2
memoryContextMaxChars = 500
```

No line is cut mid-memory. If both selected whole lines do not fit, drop the associative line first,
then the direct line only if even it cannot fit. The surviving-pick list must exactly match the lines
delivered and therefore the rows whose recall metadata is bumped.

Do not add a separate player-facing memory-budget option and do not apply different memory caps to
Compact/Balanced/Full. Effective detail is selected later and per-lane; late preset trimming would
reintroduce phantom recall bumps. The hard two-line/500-character ceiling bounds the allowed soft
budget overrun.

## 10. Stable keyword alignment

Tags give broad emotional association; stable topical keywords make lore appear for the right event.
Seed matching must never depend on the active language.

Audit event capture and `MemoryQueryContext` production for each intended topical family. Add missing
language-neutral identifiers—faction Def names, entity/pawn-kind Def names, hediff Def names, or
closed category tokens—to an internal memory-query contract. These identifiers are not new
model-facing prompt fields. Author seed keywords only from values the audited query path can actually
produce.

Rules:

- never use localized labels, free prose, pawn-facing descriptions, or translated backstory text;
- reuse the single `MemoryExtraction` normalization helper;
- order/cap extraction so stable identifiers cannot be crowded out by lower-value values;
- document the final closed topical vocabulary at the top of `DiaryLoreSeedDefs.xml`;
- keep seed tags/keywords frozen after deposit even when localized prose changes.

The implementing audit starts with raid, anomaly, quest, hediff, royal, xenotype/gene, mechlink,
psylink, and reflection paths, then expands only when a catalog family requires it.

## 11. Initial catalog — one release gate, four passes (L3)

Ship a substantial initial catalog of roughly 30–40 Defs. Do not reduce it to a small proof catalog.
Build it in four separately reviewable passes; intermediate green commits are useful but the catalog
is not release-ready until all four pass gates are complete.

1. **Stable vocabulary pass** — finish the keyword/query audit and freeze the supported matcher
   vocabulary.
2. **English catalog pass** — author the full 30–40 Def set with constraints, usage, tier, weight,
   mutex, tags, and reachable keywords.
3. **Native Russian pass** — author every DefInjected Russian text/label independently.
4. **Systematic QA pass** — reachability, contradictions, length, localization parity, no-DLC safety,
   topical recall fixtures, and prompt smoke tests.

Mapping guide:

| Corpus section | mutexGroup | Registers / eligibility |
|---|---|---|
| §1 no-FTL / distance is grief (§15) | `distance` | offworld, imperial, spacer; exclude unsupported origin claims |
| §2 cryptosleep / time | `cryptosleep` | offworld; exact ancient-soldier backstories |
| §4 glitterworld heaven-myth | `glitterworld` | generic myth; exact glitterworld-origin backstory variants |
| §4 urbworld / midworld origin | `homeworld` | exact backstory Defs and supported categories |
| §5 archotech awe/dread | `archotech` | tribal machine-god register; educated register |
| §6 psychic weather | `psychic` | generic; explicit psylink/psycaster-state variant |
| §7 mechanoid old war | `mechanoid_origin` | tribal, educated, mechanitor (`MechlinkImplant`) |
| §8 insectoids | `insectoid` | educated/book-knowledge register |
| §9 own-xenotype lore | `xeno_identity` | explicit xenotype Def-name variants |
| §10 Empire | `empire` | imperial backstories; outsider register |
| §11 void rumor | `void` | generic low-weight rumor; no unsupported proper-name certainty |
| §14 luciferium / persona weapons / thrumbo | `legends` | generic and explicitly constrained variants |

Authoring rules:

- at most 200 characters, first-person remembered-past register;
- no encyclopedia voice, game mechanics, DLC names, or meta terminology;
- broad-category facts stay ordinary; core claims require exact high-confidence evidence;
- constraints describe why the pawn could hold the memory, not necessarily objective truth;
- origin-specific statements never arise from localized title substring matching;
- changed current state does not invalidate an honestly historical sentence.

## 12. Lore primer (L4, independent of seed memories)

`enableLorePrimer` is a separate default-true setting. It works even when lore memories are disabled,
and lore memories work when the primer is disabled.

The primer is deterministic localized text appended to the **system prompt**, never a user-message
field. Compose in this order:

```text
template/base system prompt
+ persona/writing-style system text
+ lore primer (last)
```

Every tier begins with the semantic override:

> Unless supplied facts explicitly establish otherwise …

This makes mod-provided facts authoritative over default RimWorld canon. The effective
`PromptContextDetailLevel`, including any generation-lane override, chooses compact authored clauses:

- **Compact — 3 clauses:** no faster-than-light travel or communication (combined); no aliens;
  mixed technology levels are normal.
- **Balanced — 4 clauses:** Compact plus settled worlds are politically isolated.
- **Full — 5 clauses:** separate no-FTL travel and no-FTL communication; no aliens; mixed technology
  levels; political isolation and its fragmented-civilization consequence.

Use Keyed EN/RU strings (or an equivalent localization-safe XML contract) for all model-facing primer
prose. Do not hardcode English in C#. Apply the primer to the 11 first-person prompt templates:

- `PairDefault`, `PairImportant`, `PairCombat`, `PairBatched`;
- `SoloDefault`, `SoloImportant`, `SoloInternalState`, `SoloBatched`;
- `SoloDayReflection`, `SoloQuadrumReflection`, `SoloArcReflection`.

Explicitly exclude `DeathDescription`, `ArrivalDescription`, and `Title`.

## 13. Settings and XML tuning

Player-facing settings (both default true, independent):

- `enableLoreSeeds` — deposit and recall authored pawn lore memories;
- `enableLorePrimer` — append world-model clauses to first-person system prompts.

Old saves inherit both true defaults. Tooltips and the changelog for the implementation must state
that existing saves acquire seeds lazily and the primer applies automatically unless opted out.

XML policy additions/defaults:

```text
loreSeedsEnabled = true
maxInitialLoreSeedsPerPawn = 4
minSpecificInitialLoreSeedsPerPawn = 1
loreSeedOrdinaryImportance = 0.35
loreSeedCoreImportance = 0.85
loreSeedNarrativeAgeOffsetTicks = 7,200,000
maxCoreLoreSeedsPerPawnLifetime = 2
coreLoreRecallCooldownTicks = 1,200,000
maxProgressionLoreSeedsPerPawnLifetime = 4
memoryContextMaxLines = 2
memoryContextMaxChars = 500  (existing hard character ceiling; retain it)
```

Mirror tuning values in `MemoryPolicySnapshot.CreateDefault` and extend the XML-parity test. The
memory line/character values remain XML policy, not extra UI controls. Prompt context detail remains
the only player-facing context-size choice.

## 14. Test and validation contract

### 14.1 Pure tests

- category and exact-backstory inclusion/exclusion matrices;
- xenotype/hediff and mixed-constraint eligibility;
- deterministic weighted selection and different stable seeds;
- reserved specific slot, core-first preference, and unsupported-modded fallback;
- mutex behavior only within one initial plan;
- persisted roster retries deposit missing targets without resampling;
- removed target gets no replacement;
- one Def at most once across initial/progression histories;
- initial, progression, and core lifetime limits, including after repository eviction;
- narrative-age clamp to biological age;
- narrative offset affects age label/minimum age but not recency, cooldown, or eviction;
- ordinary versus core importance and 20-day core cooldown;
- a newly deposited core seed is immediately eligible; the core cooldown starts only after its
  first successful prompt projection;
- disabled-lore filtering and cap-planning exclusion;
- two-line/500-character whole-pick fitting and exact surviving-pick metadata;
- `MemoryContext` required under Full/Balanced/Compact when non-empty;
- projectability prevents selection/bump for neutral/title templates;
- policy XML/default parity.

### 14.2 Catalog tests

- every initial seed is reachable by at least one supported default-policy pawn fixture;
- every progression seed is reachable by a matching exact event token and pawn fixture;
- each distinct topical keyword family has an extraction-to-recall fixture;
- every EN/RU Def has text and label parity and respects the length limit;
- unknown tags, unknown closed tokens, localized matcher values, and invalid core constraints fail QA;
- base-game/no-DLC fixtures load and simply leave DLC string matchers inert.

### 14.3 Integration and prompt tests

- save/load round-trip of target lists, provenance, narrative offset, and lifetime histories;
- no duplicate deposit after reload or partial retry;
- disabling preserves rows, suppresses recall/eviction accounting, and re-enabling restores them;
- a selected memory is bumped only when its final whole line is actually projected;
- reflection templates project bounded memory; neutral/title templates never do;
- primer appears last in system composition for all 11 first-person shapes and nowhere else;
- Compact/Balanced/Full primer fixtures assert 3/4/5 clause semantics and explicit-fact override;
- retain 2–3 full end-to-end prompt smoke fixtures for representative matching seeds rather than
  attempting a fragile snapshot for every catalog row.

Each implementation phase runs relevant pure tests, RimTest coverage where applicable, XML parsing,
the Debug MSBuild build, and the repository verification hook. Each behavior/structure phase updates
`DOCUMENTATION.md` and adds a dated `CHANGELOG.md` entry.

## 15. Phased implementation plan

Each phase must finish green. L3's internal passes may land separately but L3 is one release gate.

### L1 — contracts, truthfulness, and bounded prompt projection

- Add optional lore provenance/narrative-age fields and additive save exposure.
- Extend effective-age calculation without changing real recency/cooldown/eviction time.
- Add explicit projectability, reflection `MemoryContext` fields, required context-detail policy,
  and the two-line/500-character whole-pick contract.
- Ensure recall metadata is bumped only for final projected picks.
- Add tuning/default/parity and focused pure/prompt tests.

No lore Def constructs or deposits a seed yet.

### L2 — initial planner, persistence, settings, and wiring

- Add `DiaryLoreSeedDef`, DTOs, `LoreSeedPlanner.PlanInitial`, and Def validation.
- Add persisted initial/core/progression histories (progression history is inert until L5).
- Implement deterministic target selection, specific reservation, exact Def idempotency, tier rules,
  live localized snapshot text, and lazy pre-recall deposit.
- Implement independent seed toggle and disabled recall/eviction semantics.
- Add pure and RimTest save/load/retry/toggle coverage.

### L3 — full EN/RU catalog and keyword alignment

Complete the four passes from §11: stable vocabulary, full English catalog, native Russian catalog,
then systematic QA. L3 is not complete with a miniature catalog or untranslated placeholders.

### L4 — context-detail lore primer

- Add independent primer setting/localization.
- Compose it last in the system prompt.
- Cover all 11 first-person templates and three effective detail levels.
- Update deliberate prompt fixtures once.

After L4, the initial feature is releasable.

### L5 — deferred progression lore

- Implement `PlanProgression` using the already persisted contracts.
- Audit exact registered progression event Def tokens.
- Attach one owner-only seed after successful ordinary event deposit.
- Enforce exact-Def uniqueness, four-seed progression lifetime cap, and two-core lifetime cap.
- Add progression save/load, dropped-event, witness-exclusion, and event-family fixtures.

L5 is deliberately deprioritized and does not block the L1-L4 release.

## 16. Gotchas and invariants

- **G1 — never backdate `createdTick`.** Narrative age is separate. Real backdating breaks first
  recall scoring and risks immediate stale eviction.
- **G2 — no pre-life memory.** Clamp initial narrative offset to biological age at deposit.
- **G3 — target roster is history.** Persist once; catalog changes never resample established pawns.
- **G4 — localized prose is not identity.** Match, dedupe, and score using frozen stable fields.
- **G5 — core means exact evidence.** Broad culture/category matchers cannot author lifelong origin
  claims by themselves.
- **G6 — current state may change.** Deposit-time truth stays as historical memory; prose must not
  claim the old condition is still current.
- **G7 — `minFragmentsForRecall = 4`.** Four initial seeds make recall available from the first
  eligible prompt. This is intentional folklore-before-lived-history behavior.
- **G8 — disabled does not mean deleted.** Preserve storage and histories; exclude from active caps
  until re-enabled, except dead-owner cleanup.
- **G9 — whole-line prompt fitting.** Never truncate memory prose and never bump a dropped pick.
- **G10 — primer is system policy.** It is not a user field and supplied mod facts explicitly win.
- **G11 — exact progression tokens.** Use registered event Def names, not translated or model-facing
  `progression_kind` values.
- **G12 — RNG purity.** Only stable `System.Random`; no `Verse.Rand` in the pure planner.
- **G13 — no UI leakage.** Lore fragments remain internal context, never fake diary events.
- **G14 — DLC safety.** String matchers may sit inert; live DLC state reads stay guarded and optional.

## 17. Expected file touch map

This is a planning map, not permission to broaden a phase unnecessarily.

| Area | Expected change |
|---|---|
| `Source/Defs/DiaryLoreSeedDef.cs` | New Def type, closed tokens, validation |
| `1.6/Defs/DiaryLoreSeedDefs.xml` | Full L3 seed catalog and vocabulary header |
| `Source/Pipeline/Memory/MemoryContracts.cs` | Lore tag, provenance/offset snapshots, seed DTOs/policy |
| `Source/Pipeline/Memory/LoreSeedPlanner.cs` | New pure initial/progression planner |
| `Source/Pipeline/Memory/MemoryRecallSelector.cs` | Effective-age/core-cooldown and bounded surviving picks |
| `Source/Pipeline/Memory/MemoryEvictionPlanner.cs` | Disabled-lore exclusion using a plain policy/filter contract |
| `Source/Core/PawnMemoryRepository.cs` | Additive provenance/offset persistence and live-text adapter support |
| `Source/Core/DiaryGameComponent.Memory.cs` | Fact collection, lazy deposit, filters, projectability/bump wiring |
| Pawn diary/memory save model | Initial targets and bounded lifetime histories |
| `Source/Defs/DiaryMemoryTuningDef.cs` + XML | Lore and memory-line tuning fields; stable query vocabulary |
| Prompt context selector/contracts | Required non-empty `MemoryContext` in every detail level |
| `1.6/Defs/DiaryPromptTemplateDefs.xml` | Reflection memory fields; primer eligibility if explicit flag is used |
| `Source/Generation/PromptAssembler.cs` / planner | Primer system composition after persona/style |
| Settings/UI | Two independent default-true toggles |
| `Languages/English`, `Languages/Russian` | Keyed UI/primer text and DefInjected seed prose |
| `tests/PawnMemoryTests` | Pure planner, timing, fitting, parity, reachability tests |
| `tests/DiaryPipelineTests` | Prompt detail/projectability/primer composition tests |
| `tests/PawnDiary.RimTest` | Save/load, lifecycle, toggle, reflection, progression fixtures |
| `DOCUMENTATION.md`, `CHANGELOG.md` | Updated in every implementation phase that changes behavior/structure |
