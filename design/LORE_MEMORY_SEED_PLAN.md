# Lore Memory Seed Plan

**Date:** 2026-07-22 · **Status:** APPROVED PLAN — nothing implemented yet.
**Lore source:** `design/RIMWORLD_LORE_COLLECTION.md` (the dense wiki-derived corpus; §ref below
always means a section of that file).
**Host subsystem spec:** `design/MEMORY_SYSTEM_DESIGN.md` + `design/MEMORY_WIRING_PLAN.md` — the
memory layer is live-wired (deposit/recall/evict/prompt). This plan adds a *content source* to it;
it changes no scoring, no recall flow, and no prompt plumbing beyond one optional primer line.

---

## 1. Goal and mechanism

Give diary entries RimWorld-lore texture without ever letting an LLM select or format the lore.

Mechanism (owner-confirmed 2026-07-22):

1. **Lore seeds** — pre-written, static, first-person "background memory" sentences — are
   deposited into the existing pawn memory store (`PawnMemoryRepository`) when a pawn's diary
   record is created. From then on the **unmodified** associative recall
   (`MemoryRecallSelector`) decides *when* a seed surfaces, exactly as it does for lived
   memories: tag/keyword overlap, seeded gates, cooldowns, eviction.
2. **Lore primer** — one short static guardrail block (the §1 "Five Rules" compressed) appended
   to first-person prompts so the model never invents FTL, aliens, or galactic empires.

Explicitly **out of scope** (deferred unless play-testing shows gaps): a lore source for the
prompt-enchantment pool, per-event "ambient world-state" lore, and any new selection machinery.

## 2. Requirements → how the design satisfies them

| Requirement (owner) | Mechanism |
|---|---|
| No LLM for selection or formatting | Seed text is authored XML prose; selection is the existing pure seeded-RNG recall; the primer is a constant string. |
| Not on every prompt | Existing gates already enforce this: `recallGateChance` 0.6, `minDirectScore` 0.30, `recallCooldownTicks` + `repetitionPenaltyFactor`, competition against real memories. Seeds add zero new probability mass paths. |
| Lore must sound like pawn knowledge, not an encyclopedia | Seeds are authored in first-person memory register, with backstory-gated variants (tribal vs. offworld phrasing of the same fact). The memory channel itself frames them as "long-standing memories". |
| Deterministic / reproducible | Seed *choice* uses an FNV stable seed (reuse `HumorChancePolicy.StableSeed`) keyed by pawnId; recall is already seeded per event. No `Verse.Rand` in any pure path (standing RNG-purity rule). |
| DLC/mod safe | All eligibility matching is by plain strings (xenotype defNames, backstory spawn categories, hediff defNames) that simply never match when absent — same convention as `hediffDefNames` in `DiaryPromptEnchantmentDef`. |

## 3. Architecture decisions (locked)

1. **Seeds are ordinary `MemoryFragment` rows.** No new fragment subclass, no selector changes.
2. **Provenance** rides two existing fields:
   - `sourceEventId = "loreseed:" + seedDefName` (sentinel). Real event ids are GUID "N"
     strings, so the sentinel can never collide, the self-echo guard is unaffected, and
     `PawnMemoryRepository.HasDeposit(pawnId, sentinel)` gives per-seed idempotency for free.
   - a new closed-vocabulary tag token `lore` (`MemoryTagTokens.Lore`). Queries never emit it,
     so it contributes no match score; it exists for diagnostics, tests, and future tooling.
3. **Seeds are evictable, never "core".** Default importance **0.35** — above the deposit noise
   floor, far below `coreImportanceThreshold` 0.8. A hard frontier life gradually displacing the
   old world's stories is intended fiction. The per-pawn seed count is small (default 4) against
   `maxFragmentsPerPawn` 60, so seeds cannot crowd out lived experience.
4. **Backdating.** `createdTick = depositTick - loreSeedBackdateTicks` (default **7,200,000** =
   2 in-game years) so the renderer's age band reads "a long time ago" and recency decay sits at
   its floor (0.25) — folklore is faint by design. `lastRecalledTick` MUST be set to the
   **deposit tick**, not `createdTick` (see gotcha G1).
5. **One text per def.** Register variants (tribal vs. spacer phrasing of one fact) are separate
   defs whose eligibility filters make them mutually exclusive. No template interpolation, no
   grammar — static strings only.
6. **Centralization** (repo convention): all impure wiring lands in the existing partial
   `Source/Core/DiaryGameComponent.Memory.cs`; all decision logic is pure and Verse-free under
   `Source/Pipeline/Memory/`.

## 4. New def type — `DiaryLoreSeedDef`

`Source/Defs/DiaryLoreSeedDef.cs` + `1.6/Defs/DiaryLoreSeedDefs.xml`.

```xml
<PawnDiary.DiaryLoreSeedDef>
  <defName>LoreSeed_Mechanoid_Tribal</defName>
  <label>old machines (tribal)</label>
  <!-- First-person memory prose, <= fragmentTextMaxChars (200). Authored, never generated. -->
  <text>The elders always said the old machines sleep under the mountains, and that waking them is how the ancestors' world ended.</text>
  <!-- Closed MemoryTagTokens vocabulary. "lore" is implied and added by code; do not list it. -->
  <tags><li>combat</li><li>danger</li><li>dread</li></tags>
  <!-- Topical association keywords, <= maxKeywordsPerFragment (8), normalized like extraction. -->
  <keywords><li>mechanoid</li><li>machine</li><li>raid</li><li>ruin</li></keywords>
  <importance>0.35</importance>
  <weight>1.0</weight>                      <!-- selection weight among eligible seeds -->
  <!-- Eligibility. Empty list = no constraint. All matching is case-insensitive strings. -->
  <backstoryCategories><li>Tribal</li></backstoryCategories>          <!-- any-of, spawn categories -->
  <excludeBackstoryCategories />                                       <!-- none may match -->
  <xenotypeDefNames />                                                 <!-- any-of, e.g. Hussar -->
  <hediffDefNames />                                                   <!-- any-of, e.g. MechlinkImplant -->
  <mutexGroup>mechanoid_origin</mutexGroup> <!-- at most ONE seed per group per pawn -->
</PawnDiary.DiaryLoreSeedDef>
```

Def-load validation (in the Def class `ResolveReferences`/config-error pass): text non-empty and
≤ 200 chars; tags ⊆ `MemoryTagTokens` (unknown → drop + config error, mirroring the
extraction-time rule that a typo cannot invent a tag); keywords ≤ 8, normalized with the SAME
normalizer `MemoryExtraction` uses (lowercase/trim — reuse its helper, do not re-implement).

`<text>` and `<label>` are DefInjected-translatable. **Russian prose is authored natively, never
calqued** (standing localization rule).

## 5. Pure planner — `LoreSeedPlanner`

`Source/Pipeline/Memory/LoreSeedPlanner.cs` — pure, Verse-free (links into the pure test
project). Input DTOs, defined in `MemoryContracts.cs`:

```
LoreSeedCandidate   { seedDefName, text, tags, keywords, importance, weight, mutexGroup,
                      backstoryCategories, excludeBackstoryCategories, xenotypeDefNames, hediffDefNames }
LoreSeedPawnFacts   { pawnId, backstoryCategories (union of childhood+adulthood spawn categories),
                      xenotypeDefName, hediffDefNames, alreadySeededDefNames }
LoreSeedPolicy      { enabled, maxSeedsPerPawn, defaultImportance, backdateTicks }
```

Algorithm (`Plan(candidates, pawnFacts, policy, seed) → List<LoreSeedPick>`):

1. Filter to eligible candidates (all eligibility lists satisfied; `alreadySeededDefNames`
   excluded — idempotent re-runs pick nothing new).
2. Deterministic weighted sample **without replacement** using `System.Random(seed)` until
   `maxSeedsPerPawn` picks or candidates exhausted, honoring `mutexGroup` (picking one seed
   removes its whole group).
3. Return picks with final fragment field values (tags + implied `lore` token, importance,
   text). The planner never mutates inputs and never touches ticks — backdating math is a pure
   static helper (`BackdatedCreatedTick(depositTick, policy)`) covered by boundary tests.

Seed for step 2: `HumorChancePolicy.StableSeed("loreseed", pawnId)` — stable per pawn, so a
save/reload or a re-run picks identical seeds.

## 6. Impure wiring — `DiaryGameComponent.Memory.cs`

New method `SeedLoreMemoriesIfNeeded(Pawn pawn, PawnDiaryRecord diary)`:

1. Gates: `MemorySystemEnabled()` && `settings.enableLoreSeeds` && policy snapshot `enabled`
   && `!diary.loreSeeded`.
2. Collect `LoreSeedPawnFacts` from the live pawn (this is the only place live pawn state is
   read): backstory spawn categories via `pawn.story?.Childhood/Adulthood`, xenotype defName via
   the same guarded access `Progression`/`GeneIdentityObservationState` already use, visible
   hediff defNames. Wrap in `try/catch` + `Log.ErrorOnce` (failure-isolation convention).
3. Call the pure planner; for each pick, build a `MemoryFragment`:
   `memoryId` fresh GUID; `pawnId`; `sourceEventId` sentinel; `text`; tags (+`lore`); keywords;
   `importance`; `createdTick` backdated; `lastRecalledTick = Find.TickManager.TicksGame`;
   `recallCount = 0`; register via `memories.Register(fragment)` (belt-and-braces:
   skip when `memories.HasDeposit(pawnId, sentinel)`).
4. Set `diary.loreSeeded = true` (additive Scribe bool on `PawnDiaryRecord`, default false —
   old saves seed lazily on next access, which is the intended migration).

Call site: end of the record-creation branch in `FindDiary(pawn, createIfMissing:true)`
(`Source/Core/DiaryGameComponent.Lookup.cs:997` area), after `IndexDiaryRecord(diary)` — plus in
`EnsurePawnDiaryDefaults` behind the `loreSeeded` flag so pre-existing records from old saves get
seeded once. Both funnel into the same guarded method; the flag makes double invocation free.

**No recall-side changes.** Deposit-side (`DepositMemoryForRole`) is also untouched — seeds do
not flow through event deposit.

## 7. Settings + tuning

- `PawnDiarySettings.enableLoreSeeds` (bool, default **true**) + checkbox next to the memory
  system toggle; label/tooltip EN + RU Keyed strings.
- `DiaryMemoryTuningDef` additions (mirror in `MemoryPolicySnapshot.CreateDefault` — keep the
  XML-parity test green, `tests/PawnMemoryTests` `TestPolicyXmlParity` pattern):
  `loreSeedsEnabled` true, `maxLoreSeedsPerPawn` 4, `loreSeedDefaultImportance` 0.35,
  `loreSeedBackdateTicks` 7200000.

## 8. Keyword alignment audit (makes seeds actually fire)

Recall matches on tags (weight 0.4) and keywords (weight 0.6). Seed *tags* reuse the emotional
vocabulary, so a mechanoid seed tagged `combat,danger,dread` is eligible on any violent event;
the *keywords* are what make it fire on the RIGHT violent event. That only works if event-side
extraction actually produces topical keywords like `mechanoid`.

Task for the implementing agent: audit the `gameContext` strings emitted by
`Source/Capture/Events/{RaidEventData,AnomalyEventData,QuestEventData,HediffEventData,RoyalPermitEventData}.cs`
for keys whose values name the topical entity (raid faction kind, anomaly entity kind, quest
name, hediff name), and add the missing keys to `<contextKeywordKeys>` in
`DiaryMemoryTuningDef.xml` (+ snapshot default + parity test). Then author seed keywords ONLY
from values those keys can actually produce. Document the final keyword vocabulary in a comment
block at the top of `DiaryLoreSeedDefs.xml`. Do not add free-text scraping.

## 9. Seed catalog — initial content (Phase L3)

Author ~30–40 defs from `RIMWORLD_LORE_COLLECTION.md`, favoring registers over coverage.
Mapping guide (corpus § → mutexGroup → registers):

| Corpus section | mutexGroup | Registers (eligibility) |
|---|---|---|
| §1 no-FTL / distance is grief (§15) | `distance` | offworld spacer; imperial; (tribals excluded — no off-world past) |
| §2 cryptosleep / time | `cryptosleep` | offworld; ancient-soldier backstories |
| §4 glitterworld heaven-myth | `glitterworld` | universal; glitterworld-born variant |
| §4 urbworld / midworld origin | `homeworld` | urbworld backstories; midworld backstories |
| §5 archotech awe/dread | `archotech` | tribal ("machine gods"); educated |
| §6 psychic weather | `psychic` | universal; psycaster/psylink hediff variant |
| §7 mechanoid old war | `mechanoid_origin` | tribal; educated; mechanitor (hediff `MechlinkImplant`) |
| §8 insectoids | `insectoid` | educated only (Sorne story is book-knowledge) |
| §9 own-xenotype lore | `xeno_identity` | one per xenotype via `xenotypeDefNames` (hussar, genie, waster, yttakin, …) |
| §10 Empire | `empire` | imperial backstories; outsider view variant |
| §11 void rumor | `void` | universal, low weight (rumor register only — no Horax name) |
| §14 luciferium / persona weapons / thrumbo | `legends` | universal, several defs |

Authoring rules: ≤ 200 chars; first-person past ("I remember…", "Back home…", "The elders
said…"); a pawn may believe things that are *wrong* in detail but right in register; never state
mechanics; never use meta terms (DLC names, "xenotype" as a word tribals wouldn't use).
RU catalog authored natively in the same commit.

## 10. Lore primer (Phase L4 — independent of seeds)

- Keyed string `PawnDiary.Prompt.LorePrimer` (EN + RU): ≈3 sentences from corpus §1 — slow
  stars (no FTL, news decades old), no aliens (only divergent humanity), mixed tech is normal.
- Delivery follows the `MemoryContext` precedent: new `PromptValues` field + `ResolveSource`
  case `LorePrimer` in `Source/Generation/PromptAssembler.cs`, projected in
  `DiaryPromptPlanner.Build`, added as a `<source>LorePrimer</source>` line to the **8
  first-person templates only** (same set as MemoryContext; reflections excluded in v1 to keep
  the diff small — revisit after play-testing). Gated by `settings.enableLorePrimer`
  (default true).

## 11. Phased work plan (each phase ships green)

**L1 — inert core.** `DiaryLoreSeedDef` (+validation), `MemoryTagTokens.Lore`,
`LoreSeedPlanner` + DTOs, tuning fields + snapshot + parity test, pure tests
(eligibility matrix, mutex groups, deterministic sampling, backdate boundaries, idempotent
re-plan). Nothing constructs the planner yet.

**L2 — seeding wiring.** `SeedLoreMemoriesIfNeeded` + both call sites + `loreSeeded` flag +
settings checkbox. RimTest: save/load round-trip of a seeded pawn (fragments persist, flag
persists, no double-seed); disabled-setting path deposits nothing.

**L3 — content.** Keyword-alignment audit (§8) + `DiaryLoreSeedDefs.xml` catalog + RU
DefInjected. Add 2–3 dev PromptTestSuite fixtures proving a seed surfaces in `memoryContext`
for a matching staged event and does NOT surface for a non-matching one (seeded, deterministic).

**L4 — primer.** As §10. Snapshot prompt fixtures updated once, deliberately.

**L5 (stretch) — identity top-ups.** On progression events that create new pawn identity
(`XenotypeChanged`, psylink gain, `MechlinkImplant`, title bestowal — hooks already exist in
`DiaryGameComponent.Progression.cs`), deposit ONE matching seed with the real
`sourceEventId` and no backdating ("what I learned when I became this"). Same planner,
`maxSeedsPerPawn` unaffected (separate cap 1 per progression event).

## 12. Gotchas (verified against current code — do not rediscover)

- **G1 — stale eviction trap.** `MemoryFragment` init sets `lastRecalledTick = createdTick`; a
  backdated seed (2 years) would be instantly eligible for `staleEvictTicks` (7.2M) eviction.
  Seeds MUST set `lastRecalledTick` to deposit tick. The PostLoadInit repair
  (`lastRecalledTick < createdTick` → raise) does not undo this (deposit tick > backdated tick).
- **G2 — early-game negative ticks.** Backdating at colony start yields negative
  `createdTick`. Selector math (age = now − created) tolerates it, but clamp to
  `min(createdTick, 0)`… no: simply allow negatives and add a pure test asserting decay/band
  math behaves; if any consumer assumes non-negative, clamp createdTick at
  `depositTick - backdateTicks` floor `int.MinValue/2` — decide in L1 with a test either way.
- **G3 — closed tag vocab.** Unknown tags in seed XML must fail at def-load (config error), not
  silently at runtime; extraction-time dropping does not run for seeds because seeds bypass
  `MemoryExtraction`.
- **G4 — `minFragmentsForRecall` = 4 side effect.** Four seeds push a fresh pawn over the
  recall threshold from day one, so recall activates earlier than pre-seed behavior. This is
  accepted and intended (folklore before lived history) — note it in the PR description; if it
  proves noisy, raise `minFragmentsForRecall`, don't special-case seeds.
- **G5 — keyword normalization.** Reuse `MemoryExtraction`'s normalizer for seed keywords at
  def-load; a case/whitespace mismatch silently zeroes the 0.6-weight match channel.
- **G6 — RNG purity.** Planner uses `System.Random(seed)` only; `Verse.Rand` is forbidden in
  `Source/Pipeline/` (standing rule; see `MemoryRecallSelector` header comment).
- **G7 — no UI leakage.** Fragments have no player-facing reader; seeds therefore never appear
  as fake "events". Keep it that way — do not add seeds to any archive/diary view.
- **G8 — sentinel ids.** Never parse `sourceEventId` outside the `"loreseed:"` prefix check;
  keep the prefix as a single const on the repository or planner contracts.

## 13. File touch list

| File | Change |
|---|---|
| `Source/Defs/DiaryLoreSeedDef.cs` | NEW def type + validation |
| `1.6/Defs/DiaryLoreSeedDefs.xml` | NEW seed catalog (L3) |
| `Source/Pipeline/Memory/MemoryContracts.cs` | `MemoryTagTokens.Lore`, seed DTOs, policy fields |
| `Source/Pipeline/Memory/LoreSeedPlanner.cs` | NEW pure planner |
| `Source/Defs/DiaryMemoryTuningDef.cs` + `1.6/Defs/DiaryMemoryTuningDef.xml` | 4 tuning fields (+`contextKeywordKeys` rows from audit) |
| `Source/Core/DiaryGameComponent.Memory.cs` | `SeedLoreMemoriesIfNeeded` |
| `Source/Core/DiaryGameComponent.Lookup.cs` | 2 call sites |
| `Source/Models/PawnDiaryRecord.cs` | additive `loreSeeded` bool |
| `Source/Settings/PawnDiarySettings.cs` (+ settings UI file) | 2 checkboxes |
| `Source/Generation/PromptAssembler.cs`, `Source/Pipeline/DiaryPromptPlanner.cs`, `1.6/Defs/DiaryPromptTemplateDefs.xml` | primer source (L4) |
| `Languages/**/DefInjected` + `Keyed` | EN + RU strings |
| `tests/PawnMemoryTests/` | planner + parity + backdate tests |
| `tests/PawnDiary.RimTest/` | seed round-trip fixture |
