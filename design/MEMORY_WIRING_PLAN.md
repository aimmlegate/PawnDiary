# Memory → Diary Wiring Plan

**Date:** 2026-07-21 · **Status:** APPROVED PLAN — nothing implemented yet.
**Authoritative spec:** `design/MEMORY_SYSTEM_DESIGN.md` (§6–§14). This document is the
*delta/handoff*: what already exists, what remains, in what order, and the confirmed
requirements. Implement from the design doc; do not re-derive the architecture.

---

## 1. Context

The associative-memory subsystem was built inert at `a19d31fa` and adversarially reviewed
2026-07-19 (0 crit/0 major; the MINOR blank-text-shadow and LOW CreateDefault↔XML findings
are both **fixed** — see `MemoryRecallSelector.UsableFragments` and
`tests/PawnMemoryTests/Program.cs` `TestPolicyXmlParity`). Nothing in `Source/` constructs
or calls it. This plan wires it into the diary pipeline as a prompt enhancement.

## 2. Requirements (confirmed by owner, 2026-07-21) → design mapping

| Requirement | How the existing design satisfies it |
|---|---|
| **Centralized — one point of usage** | All impure wiring lives in ONE new partial, `Source/Core/DiaryGameComponent.Memory.cs`. Read+write both hook the single event-registration funnel (`AddPairwiseEventCore`/`AddSoloEventCore`, `Source/Core/DiaryGameComponent.EventFactory.cs:78`/`:224`) — two call sites, one applier pair. Prompt side flows through the single pure choke point `DiaryPromptPlanner.Build`. |
| **Not every item — only related or important** | Write gate: `minDepositImportance` 0.3 drops ambient chat / quiet pages (quiet cue = 0.2 base). Read gates: `minFragmentsForRecall` 4, relatedness threshold `minDirectScore` 0.30 (tag/keyword overlap must clear it), and the `<source>MemoryContext</source>` line is added **only to the 8 first-person templates** — reflections, death, arrival, title excluded (§9.8). |
| **Weighted random checks** | Seeded gates already in `MemoryRecallSelector.Recall`: `recallGateChance` 0.6 per-event roll, `spreadGateChance` 0.5 for the 1-hop association. Seed = FNV-1a of `"eventId\|pawnId"` on `System.Random` — deterministic per event, **never `Verse.Rand`** (RNG-purity rule, see §6 gotchas below). Scoring itself is weighted: `tagWeight`/`keywordWeight` blend × importance salience × recency decay. |
| **Store short signal source, not full diary text** | Fragments store tags (closed 18-token vocab) + ≤8 normalized keywords + a ≤200-char excerpt of the **raw event facts** — never generated diary prose (rationale: design §7.2; prose is nondeterministic, per-POV, often absent). |
| **Keyword comparison** | `MemoryRecallSelector.DirectScoreFormula` — saturated tag overlap + keyword overlap; the same `MemoryExtraction.Extract` builds both the deposit fragment and the recall query, so vocabularies always match. |

## 3. Current state — done vs pending

**DONE (inert, tested):**
- Pure layer: `Source/Pipeline/Memory/{MemoryContracts,MemoryExtraction,MemoryRecallSelector,MemoryEvictionPlanner,MemoryContextPrompt}.cs`
- Persistence classes: `Source/Models/MemoryFragment.cs`, `Source/Core/PawnMemoryRepository.cs`
- Policy: `Source/Defs/DiaryMemoryTuningDef.cs` + `1.6/Defs/DiaryMemoryTuningDef.xml` + `DiaryMemoryPolicy.Snapshot()`
- Tests: `tests/PawnMemoryTests` (~264 assertions incl. XML-parity guard) + RimTest repository round-trip fixtures (`tests/PawnDiary.RimTest/PawnDiaryRepositoryRebuildFixtureTests.cs`)

**PENDING (this plan; = design §14 steps 4–7 plus the unfinished half of step 2):**
- `DiaryGameComponent` has no `memories` field; `ExposeData`/`PostLoadInit` don't touch the repository; no tombstone dictionary
- No `DiaryGameComponent.Memory.cs`; `AddPairwiseEventCore`/`AddSoloEventCore` have no hook calls
- No `DiaryEvent.PovSlot.memoryContext`; prompt plumbing (§9 steps 1–5, 7–8) absent (`MemoryContextPrompt` itself — step 6 — exists)
- No `enableMemorySystem` settings checkbox; no eviction tick scan; no DefInjected labels; no end-to-end RimTests

## 4. Phased work plan (for implementing agents)

Each phase is independently shippable and leaves the mod green. Follow design-doc sections
verbatim; only deltas are noted here.

### Phase W1 — Persistence wiring + capture hooks (design §6, §7, §8.5)
The centralization phase: everything lands in the new partial `Source/Core/DiaryGameComponent.Memory.cs`.
1. `memories` field + `memories.ExposeMemories("pawnMemoryFragments")` after `archive.ExposeArchive(...)` in `DiaryGameComponent.ExposeData` (`Source/Core/DiaryGameComponent.cs:377` area); `memoryOwnerAbsentSinceTick` tombstone dict via the existing ref-keys/ref-values idiom.
2. `PostLoadInit`: `memories.RebuildIndex()` (+ `ApplyMemoryEviction()` once W3 lands; stub as RebuildIndex-only here).
3. `ApplyMemoryContextForEvent(diaryEvent)` (recall, §8.5) and `DepositMemoryFragments(diaryEvent)` (deposit, §7.4), called in that order — **recall before deposit** so an event can't recall its own fragment — from both EventFactory funnels immediately after registration. Each wrapped in `try/catch` + `Log.ErrorOnce` (NarrativeContextBuilder failure-isolation convention).
4. Recall result frozen onto the event requires the `PovSlot.memoryContext` slot — so W1 includes §9 steps 1–2 (`DiaryEvent` slot + `ApplyMemoryContext`/`MemoryContextForRole`, scribed per-role keys, save-normalization cap 600).
5. Gate everything behind `settings.enableMemorySystem` (add the checkbox now, default true, `Source/Settings/PawnDiarySettings.cs`) — pulled forward from §14.6 because deposit must be gateable from day one.

### Phase W2 — Prompt plumbing (design §9 steps 3–5, 7–8)
1. `DiaryPovPayload.memoryContext` (`Source/Pipeline/DiaryPipelineContracts.cs:46`) ← `DiaryPipelineAdapters.PovFor` (`:213` area).
2. `PromptValues.memoryContext` + `ResolveSource` case (`Source/Generation/PromptAssembler.cs:43`/`:181`).
3. `DiaryPromptPlanner.Build`/`ProjectValues` (`Source/Pipeline/DiaryPromptPlanner.cs:204` area): `memoryContext = MemoryContextPrompt.Compose(pov?.memoryContext, request.policy?.memoryContextInstruction)`; `DiaryPolicySnapshot` gains `memoryContextInstruction` copied from the tuning XML in the adapters' policy assembly.
4. Template XML: append the `MemoryContext` source line to exactly the 8 first-person templates named in §9.8 (`1.6/Defs/DiaryPromptTemplateDefs.xml`). Do NOT touch reflection/death/arrival/title templates.
5. DefInjected labels EN **and** RU (RU authored natively, never calqued — standing rule).

### Phase W3 — Eviction + lifecycle (design §10)
`ApplyMemoryEviction()` in the same Memory partial: per-owner `Plan` → `RemoveByIds` → `PlanGlobalCap` → dead-owner grace cleanup. Runs pre-save (beside `ApplyDiaryEventLimits`), in PostLoadInit, on deposit overflow, and behind a `nextMemoryEvictionScanTick` deadline gate in `GameComponentTickInner` (`Source/Core/DiaryGameComponent.cs:608` area, interval `memoryEvictionScanIntervalTicks`).

### Phase W4 — End-to-end verification (design §12 RimTest items 2–5)
RimTests: two related events a day apart → second slot has frozen `memoryContext` and the prompt renders the field; unrelated event → field absent; signal replay deposits once; dead-owner grace; old-save load (no memory keys) → empty store, no errors. Plus an optional dev-panel fragment list on the existing Dev tab.

## 5. Explicitly out of scope

- **No prose-based extraction.** Fragments come from frozen event facts at capture, not from
  generated entries. If prose extraction is ever wanted, the only post-generation choke point
  holding both final text and source event is `DiaryGameComponent.ApplyLlmResult`
  (`Source/Core/DiaryGameComponent.GenerationDispatch.cs:273`) — noted for the future, not used.
- **No interaction with the external-API override slots.** `memoryContext` is an independent
  user-prompt field beside `narrativeContext`; it does not arbitrate against
  `Set*Override`/`ExternalOverrideArbitration` (those feed the persona voice block).
- **No new facade** (2026-07-20 verdict stands).

## 6. Gotchas for the implementing agent

- **RNG purity:** all memory randomness stays on seeded `System.Random` inside the pure
  selector. Never draw `Verse.Rand` in the capture path — `DiaryGameComponent.Dispatch.cs:18-23`
  documents the check-before-decide ordering that keeps dropped events from perturbing the
  global RNG stream; the memory hooks run after registration and must not add draws.
- **Budget behavior:** the rendered field is pre-fitted to `memoryContextMaxChars` 500 by
  `RenderAndFitBudget` (drops whole picks, never truncates). Decide the field's
  `required`/score in `PromptContextSelector` terms so Compact/Balanced budgets can drop it
  (it is an enhancement, not required).
- **Scribe keys are additive only**; old saves load to an empty store. Never rename
  namespaces of scribed types.
- **`memoryContextInstruction` is intentionally XML-only** (not in `CreateDefault()`); the
  parity test asserts this exception — keep it that way.
- **EN DefInjected drift overrides Def XML** — keep EN DefInjected in sync when adding labels.
- **Committed DLL** must be rebuilt with VS MSBuild (hook's Find-MSBuild), not `dotnet msbuild`,
  or the pre-commit freshness check fails.
- **Skipped POVs:** deposit still happens (the pawn experienced it); recall is skipped — per
  design §13; don't "optimize" this away.

## 7. Verification checklist (per phase)

- Pure suites re-run green: `tests/PawnMemoryTests` (and the other pure suites untouched).
- RimTest fixtures compile and are run in-game (don't leave them unrun-green — recurring
  review debt).
- Build 0 warnings/0 errors; committed DLL freshness check passes.
- In-game smoke (W2+): with a loaded model, force two related events (same other-pawn or
  weapon keyword) a day apart, confirm the second prompt contains the MemoryContext field
  via the prompt-capture seam; confirm quiet/ambient events deposit nothing.
- `CHANGELOG.md` + `DOCUMENTATION.md` updated per standing rules.
