# Pawn Memory System — Associative Memory Fragments

Status: settled design, 2026-07-18. Standalone document — supersedes the Wave C2 memory design
in `CORE_IMPROVEMENT_PLAN.md` Appendix D (`MemoryTag` flags, severity scale, `PawnMind` facade,
forget-on-query fuzz), none of which carries over. The `PawnMind` facade refactor is explicitly
out of scope here and is not a prerequisite. All named seams and `file:line` references were
verified against the working tree on 2026-07-18 — re-verify before coding.

## 1. Goals

1. Each pawn accumulates **arbitrary text memory fragments** — small self-contained pieces of
   remembered experience.
2. Fragments carry **tags** (classification), **keywords** (association handles), and an
   **importance weight**.
3. Retrieval is **associative**: memories surface by similarity to the current moment, not by
   direct query — including memories *not directly related* to the current situation, reached
   through one hop of spreading activation (a shared person, place, or theme bridges two
   otherwise unrelated memories).
4. The store **auto-evicts** items that stop being relevant, so it stays small, bounded, and
   save-friendly over a colony's whole lifetime.

## 2. Principles and constraints

- **No LLM calls, no embeddings.** All extraction, scoring, association, and eviction is
  deterministic code. Where randomness is wanted (recall gates), it is seeded from stable ids
  so behavior is reproducible and testable.
- **All persisted state lives on `DiaryGameComponent`.** No pawn comps, no map comps — the
  mod's standing save-safety rule.
- **Strict pure/impure boundary.** Everything decision-shaped (extraction, scoring, spread,
  eviction planning, rendering) is pure code in a new `Source/Pipeline/Memory/` folder,
  mirrored by a pure test project. Impure code only snapshots inputs and applies results.
- **Context is frozen at capture time.** Recall runs when the event is captured and the result
  is frozen onto the `DiaryEvent` POV slot — exactly how `narrativeContext` works today. The
  generation stage only formats already-frozen fields.
- **One mechanism everywhere.** The same extraction function produces a fragment's tags and
  keywords at deposit time and the query's tags and keywords at recall time. There is no second
  vocabulary and no query language.
- **Additive and invisible when off.** One new optional prompt field, gated by templates;
  empty values cost zero tokens. A single player-facing checkbox disables the whole layer.

## 3. Architecture overview

```
capture (impure, main thread)                      pure (Source/Pipeline/Memory/)
─────────────────────────────                      ──────────────────────────────
AddPairwiseEventCore / AddSoloEventCore
  │  (event registered, fields frozen)
  ├─► ApplyMemoryContextForEvent ──── query ─────► MemoryExtraction.Extract
  │        │                                       MemoryRecallSelector.Recall
  │        │◄─────── MemoryRecallResult ───────────  (direct score + 1-hop spread + render)
  │        └─ freeze slot.memoryContext; bump lastRecalledTick/recallCount
  │
  └─► DepositMemoryFragments ──────── facts ─────► MemoryExtraction.Extract
           │◄────── tags/keywords/importance ──────
           └─ PawnMemoryRepository.Register (idempotent, capped)

save / load / periodic tick
  └─► ApplyMemoryEviction ─────── snapshots ─────► MemoryEvictionPlanner.Plan / PlanGlobalCap
           └─ RemoveByIds / RemoveOwner
```

## 4. Data model — `MemoryFragment`

New file `Source/Models/MemoryFragment.cs`:

```csharp
public class MemoryFragment : IExposable
{
    public string memoryId;        // GUID "N" format, stable across saves
    public string pawnId;          // owner's Pawn.GetUniqueLoadID()
    public string sourceEventId;   // DiaryEvent.eventId that deposited it (idempotency + self-recall guard)
    public string text;            // fragment prose, <= fragmentTextMaxChars (200)
    public List<string> tags = new List<string>();      // closed vocabulary, lowercase tokens
    public List<string> keywords = new List<string>();  // normalized free strings, <= 8
    public float importance;       // 0..1
    public int createdTick;        // event tick (may be historical for staged pages)
    public int lastRecalledTick;   // init = createdTick; refreshed on every recall
    public int recallCount;        // diagnostics + eviction tie-break

    public void ExposeData()
    {
        Scribe_Values.Look(ref memoryId, "id");
        Scribe_Values.Look(ref pawnId, "pawnId");
        Scribe_Values.Look(ref sourceEventId, "sourceEventId");
        Scribe_Values.Look(ref text, "text");
        Scribe_Collections.Look(ref tags, "tags", LookMode.Value);
        Scribe_Collections.Look(ref keywords, "keywords", LookMode.Value);
        Scribe_Values.Look(ref importance, "importance", 0.3f);
        Scribe_Values.Look(ref createdTick, "createdTick", 0);
        Scribe_Values.Look(ref lastRecalledTick, "lastRecalledTick", 0);
        Scribe_Values.Look(ref recallCount, "recallCount", 0);
        // PostLoadInit: null lists -> empty; clamp importance to [0,1];
        // lastRecalledTick < createdTick -> createdTick; trim/normalize text.
    }
}
```

Decisions and rationale:

- **Tags are strings from a closed vocabulary of constants** (`MemoryTagTokens`, mirroring the
  `colorCue` string-constant convention and the narrative token classes in
  `Source/Pipeline/Narrative/NarrativeContracts.cs`), not a `[Flags]` enum: string tokens are
  the established save-safe, append-only pattern in this codebase, and unknown tags loaded from
  a newer mod version degrade to "no match" instead of corrupting a bitfield.
- **Keywords are free-form normalized strings, max 8 per fragment**: lowercase-invariant and
  alphanumerics only. Prose/context tokens use length ≥ 3 plus a light stopword filter; pawn-name
  identity tokens deliberately bypass both filters so valid names such as `Will`, `Bo`, or a
  one-character CJK name remain usable association handles. **Persons and places live in keywords,
  not tags** — this is what lets a shared person bridge two otherwise unrelated memories in the
  1-hop spread (§8).
- **Importance is a float 0..1**: it multiplies directly into the retrieval and retention
  formulas, matching how the mod treats continuous weights (weather chances, humor multipliers).
- **`recallCount` is kept** (one int): free eviction tie-breaks and dev diagnostics.

## 5. Tag vocabulary — `MemoryTagTokens`

In new `Source/Pipeline/Memory/MemoryContracts.cs`. Eighteen lowercase constants, append-only:

`combat`, `danger`, `conflict` (social fights), `breakdown` (mental breaks), `dread`
(Anomaly/extremeDark), `body` (body-part cues), `psychic`, `royalty`, `ritual`, `work`,
`family`, `romance`, `death`, `arrival`, `illness`, `joy`, `sorrow`, `social`.

## 6. Storage — `PawnMemoryRepository`

New file `Source/Core/PawnMemoryRepository.cs`, `internal sealed`, following the exact mold of
`DiaryEventRepository` (`Source/Core/DiaryEventRepository.cs`) and `DiaryArchiveRepository`
(saved master list + non-serialized indexes rebuilt on load):

```csharp
internal sealed class PawnMemoryRepository
{
    private List<MemoryFragment> fragments = new List<MemoryFragment>();   // saved master list, insertion order
    // NOT saved — rebuilt in RebuildIndex():
    private readonly Dictionary<string, List<MemoryFragment>> fragmentsByPawnId
        = new Dictionary<string, List<MemoryFragment>>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> depositKeys                            // "pawnId|sourceEventId"
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int Count { get; }
    public IReadOnlyList<MemoryFragment> ForPawn(string pawnId);            // empty list when unknown
    public List<string> OwnerPawnIds();                                     // for eviction/dead-owner scans
    public bool HasDeposit(string pawnId, string sourceEventId);
    public void Register(MemoryFragment fragment);                          // no-op on null/duplicate deposit key
    public int RemoveByIds(HashSet<string> memoryIds);                      // returns removed count
    public int RemoveOwner(string pawnId);                                  // dead-owner cleanup
    public void RebuildIndex();                                             // PostLoadInit; drops null/blank-id rows
    public void EnsureIndexReady();
    public void ExposeMemories(string label);                               // Scribe_Collections Deep + PostLoadInit null guard
}
```

Wiring on `DiaryGameComponent`, in a new partial file `Source/Core/DiaryGameComponent.Memory.cs`:

- Field: `private readonly PawnMemoryRepository memories = new PawnMemoryRepository();`
- In `ExposeData` (`Source/Core/DiaryGameComponent.cs:343`), after `archive.ExposeArchive(...)`:
  `memories.ExposeMemories("pawnMemoryFragments");` plus one dictionary
  `Scribe_Collections.Look(ref memoryOwnerAbsentSinceTick, "memoryOwnerAbsentSinceTick",
  LookMode.Value, LookMode.Value, ...)` — the dead-owner grace clock (§10), same pattern as the
  existing observed-condition cooldown map. All Scribe keys are additive: old saves load with an
  empty store and no errors.
- PostLoadInit try-block: `memories.RebuildIndex(); ApplyMemoryEviction();`
- Pre-save try-block (beside `ApplyDiaryEventLimits`): `ApplyMemoryEviction();`

## 7. Deposit — the write path

### 7.1 Hook point

`AddPairwiseEventCore` / `AddSoloEventCore` (`Source/Core/DiaryGameComponent.EventFactory.cs:78`
/ `:224`) — the single funnel every Record\* path already passes through (interactions, tales,
mental breaks, raids, quests, staged/historical pages, external-API events). Immediately after
event registration / `AddEventRef(...)`:

```csharp
ApplyMemoryContextForEvent(diaryEvent);   // recall + freeze memoryContext per first-person POV (§8)
DepositMemoryFragments(diaryEvent);       // create fragments per first-person POV
```

Order is deliberate — **recall first, deposit second** — so an event can never recall the
fragment it is about to create. Each call is wrapped in `try/catch` + `Log.ErrorOnce` so a
memory failure can never cancel a page (the `NarrativeContextBuilder` failure-isolation
convention).

### 7.2 Fragments come from event facts, not generated diary text

The generated entry text is nondeterministic, per-POV, frequently absent (generation disabled,
prompt-only mode, failures, skipped POVs), and long. Event facts are always present at capture,
deterministic, and compact — and the LLM re-voices the memory in the pawn's current style at
recall anyway. Fragment `text` is therefore a deterministic excerpt of the POV's raw event text
(`initiatorText`/`recipientText` for the role) produced by the existing pure
`DiarySentenceExcerpt` (`Source/Pipeline/DiarySentenceExcerpt.cs`), capped at
`fragmentTextMaxChars` (200). This is localization-safe (raw texts are already localized) and
needs no new prose templates.

### 7.3 Extraction — pure `MemoryExtraction.Extract`

New `Source/Pipeline/Memory/MemoryExtraction.cs`:

```csharp
internal sealed class MemoryExtractionInput
{
    public string povName;            // writer's short name (EXCLUDED from keywords — see below)
    public string otherName;          // other participant's short name (may be empty)
    public string interactionLabel;   // DiaryEvent.interactionLabel (Models/DiaryEvent.cs:127)
    public string colorCue;           // DiaryEvent.colorCue
    public string moodImpact;         // "positive"/"negative"/"neutral"/""
    public bool importantGroup;       // DiaryInteractionGroupDef.important
    public string gameContext;        // DiaryEvent.gameContext key=value blob (parse via DiaryContextFields)
    public string rawText;            // POV raw event text
}

internal sealed class MemoryExtractionResult
{
    public List<string> tags;         // deduped, closed vocabulary
    public List<string> keywords;     // <= maxKeywordsPerFragment (8)
    public float importance;          // 0..1
    public string fragmentText;       // excerpt, <= fragmentTextMaxChars
}

internal static class MemoryExtraction
{
    public static MemoryExtractionResult Extract(MemoryExtractionInput input, MemoryPolicySnapshot policy);
}
```

The impure caller builds the input from the just-constructed `DiaryEvent` — every field is
already a frozen string; no `Pawn` or Def access is needed.

**Tags** come from policy-owned mapping tables (defaults in code, XML-overridable, §11):

- colorCue → tags: `combat`→{combat} · `danger`→{combat, danger} · `socialFight`→{conflict,
  social} · `mentalBreak`→{breakdown} · `extremeDark`→{dread} · `strangeChat`→{dread, social} ·
  `bodyPart*`→{body, illness} · `psychic`→{psychic} · `royalty`→{royalty} · `white`→{joy} ·
  `daze`→{breakdown} · `quiet`→{}
- moodImpact: negative→{sorrow}, positive→{joy}
- gameContext markers (parsed with the pure `DiaryContextFields`,
  `Source/Generation/DiaryContextFields.cs`): ritual fields→{ritual}, `royal_title`→{royalty} —
  a small fixed marker→tag list, policy-owned
- Pairwise (non-solo) events always add `social`

**Keywords** — normalization: lowercase invariant → strip non-alphanumerics → dedupe ordinal →
cap at 8. Prose/context/label tokens additionally drop length < 3 and a ~60-word embedded English
stopword list. Pawn names are identity tokens and bypass those prose-only filters. Priority order:

1. `otherName` — the single most valuable association key. The writer's own name is **excluded**:
   it would appear on every one of their fragments and match everything.
2. Values of whitelisted `gameContext` keys (policy list; defaults: `weapon`, `royal_title`,
   `ideological_role`, `quest_name`, `animal_name`, place/room-style keys where present).
3. Tokens of `interactionLabel`.
4. Remaining slots: first tokens of the normalized `rawText` stream (no POS tagging).

**Importance** — deterministic policy table. Base by colorCue: `extremeDark` 0.9 ·
`bodyPartLost` 0.85 · `danger` 0.8 · `combat` 0.75 · `mentalBreak` 0.7 · `bodyPartAnomalous`
0.7 · `psychic` 0.65 · `bodyPartArtificial` 0.6 · `royalty` 0.6 · `socialFight` 0.55 · `white`
0.5 · `daze` 0.5 · `strangeChat` 0.35 · `quiet` 0.2 · empty/unknown 0.3. Bonuses: +0.15 if
`importantGroup`; +0.10 if moodImpact negative, +0.05 if positive (negativity bias is
deliberate — bad memories stick). Clamp to [0.05, 1.0].

### 7.4 Deposit policy — impure `DepositMemoryFragments`

Per first-person POV (initiator always; recipient on pairwise; never the neutral slot — the
same knowledge boundary narrative continuity uses):

1. Skip if `!settings.enableMemorySystem` or the policy snapshot is disabled.
2. Skip if `memories.HasDeposit(pawnId, eventId)` — idempotent against double emission and
   staged replays.
3. Run `MemoryExtraction.Extract`; skip if `importance < minDepositImportance` (0.3) — the
   noise gate that keeps ambient chat and quiet pages out of the store.
4. Skip if `fragmentText` is blank. The pure selector also rejects blank loaded rows defensively,
   but the live deposit seam should never spend save/cap space on an unrenderable memory.
5. `Register` a fragment with `createdTick = lastRecalledTick = diaryEvent.tick`.
6. If the pawn's list now exceeds `maxFragmentsPerPawn`, run the eviction planner for that pawn
   immediately (mirrors `ApplyDiaryEventLimits()` on registration).

## 8. Retrieval — similarity scoring + 1-hop spreading activation

All pure, in new `Source/Pipeline/Memory/MemoryRecallSelector.cs`. Contracts in
`MemoryContracts.cs`:

```csharp
internal sealed class MemoryFragmentSnapshot      // plain copy, no save-model reference
{
    public string memoryId; public string sourceEventId;
    public List<string> tags; public List<string> keywords;
    public float importance; public int createdTick; public int lastRecalledTick;
    public string text;
}

internal sealed class MemoryRecallQuery
{
    public List<string> tags; public List<string> keywords;   // same extraction as deposit
    public string currentEventId;                              // self-recall guard
    public int currentTick;                                    // = diaryEvent.tick
    public int seed;                                           // FNV-1a of "eventId|pawnId"
}

internal sealed class MemoryRecallPick { public string memoryId; public string kind; /* "direct"|"associative" */ public float score; }

internal sealed class MemoryRecallResult
{
    public List<MemoryRecallPick> picks = new List<MemoryRecallPick>();    // 0..2
    public string memoryContext = string.Empty;                            // rendered field value
    public List<string> diagnostics = new List<string>();                  // reason tokens, dev-only
}

internal static class MemoryRecallSelector
{
    public static MemoryRecallResult Recall(
        MemoryRecallQuery query, List<MemoryFragmentSnapshot> fragments, MemoryPolicySnapshot policy);
}
```

The seed follows the `HumorChancePolicy.StableSeed` pattern
(`Source/Pipeline/HumorChancePolicy.cs`): deterministic per event+pawn, so tests can pin it.

### 8.1 Gates before scoring

- `fragments.Count < minFragmentsForRecall` (4) → empty result (first-entry / young-colony
  behavior; nothing downstream is special-cased).
- Seeded roll: `new System.Random(query.seed).NextDouble() >= recallGateChance` (0.6) → empty
  result. Memories must not flavor literally every page.

### 8.2 Direct scoring

For each fragment `f` (skip when `f.sourceEventId == query.currentEventId`; early-out score 0
when both overlaps are 0):

```
tagOverlap  = |tags(f) ∩ tags(Q)|             (ordinal)
kwOverlap   = |keywords(f) ∩ keywords(Q)|     (OrdinalIgnoreCase)

tagScore    = min(1, tagOverlap / tagSaturationCount)        // default 2: two shared tags saturate
kwScore     = min(1, kwOverlap  / keywordSaturationCount)    // default 3: three shared keywords saturate
base        = tagWeight * tagScore + keywordWeight * kwScore // defaults 0.4 / 0.6

salience    = 0.5 + importance(f)                            // 0.5 .. 1.5
age         = max(0, currentTick - createdTick(f))
decay       = max(recencyFloor, 0.5 ^ (age / recencyHalfLifeTicks))
              // half-life 1,800,000 ticks = 30 days = half a RimWorld year; floor 0.25
              // old memories fade but never vanish from retrieval — resurfacing them is the point

score       = base * salience * decay

if age < minRecallAgeTicks (60,000 = 1 day)                  -> score = 0   // never echo this morning
if lastRecalledTick > createdTick
   and currentTick - lastRecalledTick < recallCooldownTicks (300,000 = 5 days)
                                                             -> score *= repetitionPenaltyFactor (0.25)
```

Anti-repetition needs **no extra recently-used list**: `lastRecalledTick` is already persisted
for eviction, so a recently recalled fragment simply scores at quarter strength — strictly
simpler than the `PawnArcScheduleState.recentlyUsedEventIds` capped-list pattern.

**Direct pick:** the single highest scorer with `score >= minDirectScore` (0.30). Deterministic
ordering throughout: score desc → `createdTick` desc → `memoryId` ordinal (mirrors
`NarrativeContextSelector.CompareScoredCandidates`).

### 8.3 1-hop spread

Runs only when a direct pick `F1` exists:

1. **Gate:** second draw from the same seeded `Random`; proceed only if `< spreadGateChance` (0.5).
2. **Expanded query:** `Qx.tags = Q.tags ∪ F1.tags`, `Qx.keywords = Q.keywords ∪ F1.keywords`.
3. **Candidates:** every fragment except `F1`, except fragments sharing `F1.sourceEventId` or
   `query.currentEventId`, and — the associative filter — **except any fragment whose direct
   score against the original Q was ≥ `minDirectScore`** (those are just more direct matches;
   the hop exists to surface the *unrelated*).
4. **Score:** `spreadScore(f) = DirectScoreFormula(f, Qx) * spreadDamping` (0.5).
5. **Pick:** at most 1 candidate with `spreadScore >= minSpreadScore` (0.15), same tie-breaks.

Worked example: today's raid produces query tags {combat, danger}. Direct pick: "the raid where
Yorick fell" (tags {combat, danger, death}, keyword `yorick`). Expanded query now carries
`yorick`, so "Yorick teaching me to cook" (keywords {yorick, cooking}, tags {social, joy} —
zero overlap with combat) can surface as the associative pick. The diary entry becomes
"raiders again… I found myself thinking of Yorick's cooking, of all things." Damping plus the
coin-flip gate keep hop picks occasional seasoning, never dominant.

### 8.4 Rendering

Inside the selector (like `NarrativeContextSelector.Format`): each pick renders as
`- (<AgeLabel>) <text>`, joined with `\n`. `AgeLabel` maps age through a policy-owned ordered
band list (labels XML-editable/translatable): ≤ 300,000 "a few days ago" · ≤ 900,000 (one
quadrum) "a couple of weeks ago" · ≤ 3,600,000 (one year) "a few quadrums ago" · else "a long
time ago". If the joined result exceeds `memoryContextMaxChars` (500), drop the associative
pick first, then the direct pick — never truncate a fragment mid-text (the
`FitsCharacterBudget` convention).

### 8.5 Impure seam — `ApplyMemoryContextForEvent`

In `DiaryGameComponent.Memory.cs`, per first-person POV:

1. Snapshot policy: `MemoryPolicySnapshot policy = DiaryMemoryPolicy.Snapshot();` (main-thread
   Def access).
2. Build the query via the same `MemoryExtraction` used at deposit.
3. Copy `memories.ForPawn(pawnId)` into `List<MemoryFragmentSnapshot>`.
4. `MemoryRecallResult result = MemoryRecallSelector.Recall(query, snapshots, policy);`
5. `diaryEvent.ApplyMemoryContext(role, result);` — freeze on the slot (§9).
6. For each pick: `fragment.lastRecalledTick = diaryEvent.tick; fragment.recallCount++;`

Step 6 is the **only** write the recall path performs, and it happens in the impure applier —
the selector never mutates its inputs.

## 9. Prompt integration — the `narrativeContext` precedent, step for step

1. `DiaryEvent.PovSlot` (`Source/Models/DiaryEvent.cs:74`): add `internal string memoryContext;`.
   Scribed beside the narrative fields under per-role keys (`initiatorMemoryContext` etc.,
   following the narrative slot-key convention); normalized in the save-normalization pass
   (cf. `DiaryEvent.cs:453`): trim, collapse whitespace, cap 600 chars.
2. `DiaryEvent`: `internal void ApplyMemoryContext(string povRole, MemoryRecallResult result)`
   (first-person slots only, like `ApplyNarrativeContext` at `:1218`) and
   `public string MemoryContextForRole(string povRole)` (like `:1249`).
3. `DiaryPovPayload` (`Source/Pipeline/DiaryPipelineContracts.cs:46`): add
   `public string memoryContext;`.
4. `DiaryPipelineAdapters.PovFor` (`Source/Generation/DiaryPipelineAdapters.cs`, beside `:213`):
   `memoryContext = diaryEvent.MemoryContextForRole(role),`.
5. `PromptValues` (`Source/Generation/PromptAssembler.cs:43`): add `public string memoryContext;`.
   `ResolveSource` (`:181`): `if (Eq(source, MemoryContextPrompt.Source)) return v.memoryContext;`.
6. New `MemoryContextPrompt` (`Source/Pipeline/Memory/MemoryContextPrompt.cs`, mirror of
   `Source/Pipeline/Narrative/NarrativeContextPrompt.cs`, token at `:14`):
   `public const string Source = "MemoryContext";` plus
   `public static string Compose(string memoryContext, string instruction)` — empty in, empty out.
7. `DiaryPromptPlanner` (beside `Source/Pipeline/DiaryPromptPlanner.cs:204`):
   `memoryContext = MemoryContextPrompt.Compose(pov?.memoryContext, request.policy?.memoryContextInstruction),`;
   `DiaryPolicySnapshot` gains `memoryContextInstruction`, copied from XML in the adapters'
   policy assembly.
8. Template XML (`1.6/Defs/DiaryPromptTemplateDefs.xml`): append
   `<li><label>…</label><source>MemoryContext</source></li>` to the **eight first-person
   templates** (verified defNames): `DiaryPromptTemplate_PairDefault`, `_PairImportant`,
   `_PairCombat`, `_PairBatched`, `_SoloDefault`, `_SoloImportant`, `_SoloInternalState`,
   `_SoloBatched`. **Excluded:** `_SoloDayReflection` / `_SoloQuadrumReflection` /
   `_SoloArcReflection` (reflections already have their own memory machinery via
   `ArcReflectionMemorySelector`) and `_DeathDescription` / `_ArrivalDescription` / `_Title`.
   Empty values are dropped by `PromptAssembler` — the field costs zero tokens when nothing was
   recalled.

Default instruction text (XML, translatable): *"Long-standing memories that drift into mind
while writing. Weave in at most one, briefly and only if it feels natural; these are the past,
not today's events."*

## 10. Auto-eviction

### 10.1 Pure planner — `MemoryEvictionPlanner`

New `Source/Pipeline/Memory/MemoryEvictionPlanner.cs`:

```csharp
internal static class MemoryEvictionPlanner
{
    // one pawn's snapshots -> memoryIds to evict; never mutates input
    public static List<string> Plan(List<MemoryFragmentSnapshot> fragments, int currentTick, MemoryPolicySnapshot policy);
    // global safety pass (snapshots carry pawnId)
    public static List<string> PlanGlobalCap(List<MemoryFragmentSnapshot> all, int currentTick, MemoryPolicySnapshot policy);
}
```

Retention score — the relevance a fragment must defend; **being recalled keeps a memory alive**:

```
freshness = max(createdTick, lastRecalledTick)
retention = importance * 0.5 ^ ((currentTick - freshness) / retentionHalfLifeTicks)   // half-life 1,800,000
```

Per-pawn plan, in order:

1. **Stale rule:** evict any fragment with `importance < coreImportanceThreshold` (0.8) AND
   `currentTick - freshness > staleEvictTicks` (7,200,000 = 2 in-game years) — un-recalled
   ordinary memories fade out even when under cap.
2. **Core exemption + core cap:** fragments with `importance >= 0.8` are exempt from score
   eviction but have their own cap `maxCoreFragmentsPerPawn` (15); overflow evicts the core
   fragment with the oldest freshness.
3. **Per-pawn cap:** if survivors exceed `maxFragmentsPerPawn` (60), evict non-core fragments
   lowest-retention-first. Tie-breaks: older `createdTick` → lower `recallCount` → `memoryId`
   ordinal.

**Global safety cap:** if the whole store exceeds `maxTotalFragments` (3000), evict
lowest-retention fragments colony-wide (core included — importance already dominates the score)
until under. Worst case ≈ 500 bytes/fragment × 3000 ≈ 1.5 MB save weight, hard-bounded.

### 10.2 Impure applier + scheduling

`ApplyMemoryEviction()` in `DiaryGameComponent.Memory.cs`: per-owner `Plan` → `RemoveByIds` →
global pass → dead-owner cleanup. Runs:

- at save time inside the existing pre-save try in `ExposeData` (beside `ApplyDiaryEventLimits`),
- in PostLoadInit after `RebuildIndex`,
- periodically in `GameComponentTickInner` (`Source/Core/DiaryGameComponent.cs:608`) behind a
  new `nextMemoryEvictionScanTick` with interval `memoryEvictionScanIntervalTicks` (150,000 =
  2.5 days) — the deadline-gated `nextXxxScanTick` mold,
- per-pawn immediately on deposit overflow (§7.4).

**Dead/absent-owner cleanup:** during the periodic scan, resolve each owner id via the existing
pawn-lookup path. Alive and resolvable → clear any tombstone in `memoryOwnerAbsentSinceTick`.
Dead or unresolvable → stamp `memoryOwnerAbsentSinceTick[pawnId] = now` on first sight; once
`now - stamp > deadOwnerGraceTicks` (3,600,000 = 1 year), `memories.RemoveOwner(pawnId)` and
clear the tombstone. Kidnapped/exiled colonists remain resolvable world pawns → their stores
survive indefinitely (they can return); only the dead and truly destroyed are cleaned, and the
grace year means a colonist who dies mid-story doesn't lose their store before any postmortem
feature might want it. **Other pawns' fragments about the dead are untouched — the dead
resurfacing in survivors' entries is a feature.**

## 11. Tuning surface

All knobs live in a new **`DiaryMemoryTuningDef : Def`** (single instance, defName
`Diary_Memory`; new `Source/Defs/DiaryMemoryTuningDef.cs` + `1.6/Defs/DiaryMemoryTuningDef.xml`),
read through `DiaryMemoryPolicy.Snapshot()` into the pure `MemoryPolicySnapshot`, whose
`CreateDefault()` matches all shipped values — a missing Def changes nothing. Precedent:
`DiaryNarrativeContinuityDefs.xml` → policy snapshot. Player-facing surface is a single
`enableMemorySystem` checkbox (default true) in `PawnDiarySettings`; everything else is XML-only.

| Knob | Default | Meaning |
|---|---|---|
| `enabled` | true | master switch (XML side) |
| `minDepositImportance` | 0.3 | deposit noise gate |
| `fragmentTextMaxChars` | 200 | fragment excerpt cap |
| `maxKeywordsPerFragment` | 8 | keyword cap |
| `cueImportance` / `cueTags` / `contextMarkerTags` / `contextKeywordKeys` | tables in §7.3 | colorCue→importance, colorCue→tags, gameContext marker→tags, whitelisted keyword keys |
| `importantGroupBonus` / `negativeMoodBonus` / `positiveMoodBonus` | 0.15 / 0.10 / 0.05 | importance boosts |
| `minFragmentsForRecall` | 4 | store-size gate |
| `recallGateChance` | 0.6 | seeded per-event recall roll |
| `tagWeight` / `keywordWeight` | 0.4 / 0.6 | score blend |
| `tagSaturationCount` / `keywordSaturationCount` | 2 / 3 | overlap saturation |
| `recencyHalfLifeTicks` | 1,800,000 | retrieval decay half-life (half a year) |
| `recencyFloor` | 0.25 | old memories never fully fade from retrieval |
| `minRecallAgeTicks` | 60,000 | never recall same-day fragments |
| `minDirectScore` | 0.30 | direct pick threshold |
| `spreadGateChance` | 0.5 | seeded hop gate |
| `spreadDamping` | 0.5 | hop score multiplier |
| `minSpreadScore` | 0.15 | hop threshold (post-damping) |
| `recallCooldownTicks` | 300,000 | anti-repetition window (5 days) |
| `repetitionPenaltyFactor` | 0.25 | score multiplier inside cooldown |
| `memoryContextMaxChars` | 500 | rendered field budget |
| `ageBands` | §8.4 | age→label list (translatable) |
| `memoryContextInstruction` | §9 | prompt guidance line |
| `maxFragmentsPerPawn` | 60 | per-pawn cap |
| `coreImportanceThreshold` | 0.8 | core-memory exemption line |
| `maxCoreFragmentsPerPawn` | 15 | core cap |
| `retentionHalfLifeTicks` | 1,800,000 | eviction decay half-life |
| `staleEvictTicks` | 7,200,000 | un-recalled non-core expiry (2 years) |
| `maxTotalFragments` | 3000 | global save-size safety cap |
| `deadOwnerGraceTicks` | 3,600,000 | dead-owner store grace (1 year) |
| `memoryEvictionScanIntervalTicks` | 150,000 | periodic scan cadence |

## 12. Testing strategy

**New pure test project `tests/PawnMemoryTests`** (mirror of `tests/NarrativeContinuityTests`,
no RimWorld reference), covering `Source/Pipeline/Memory/*`:

- Extraction: normalization (case, punctuation, prose stopwords/length, identity-name exceptions),
  keyword priority order and cap, tag mapping tables, importance table + clamps — golden expectations.
- Direct scoring: formula math, saturation, decay at age 0 / one half-life / floor, min-age
  zeroing, cooldown penalty, zero-overlap early-out.
- Determinism: identical query+fragments+policy → byte-identical `memoryContext` and identical
  picks; seed changes flip the gates predictably.
- Spread: gate roll boundaries, expansion union, direct-match exclusion from hop candidates,
  damping/threshold, and the "shared person bridges unrelated memories" scenario (raid query
  recalls a combat memory keyworded `yorick`; the hop surfaces a cooking memory sharing only
  `yorick`).
- Rendering: age bands, char budget dropping associative-then-direct, blank fragments excluded
  before scoring/store-size gating, empty result → empty string.
- Eviction planner: stale rule, core exemption + core cap, per-pawn cap ordering, tie-breaks,
  global cap, plan never mutates input.

**RimTest (`tests/PawnDiary.RimTest`):**

- Save round-trip: deposit fragments → save → load → fragments identical, indexes rebuilt,
  malformed rows dropped.
- End-to-end: submit two related events a day apart → second event's slot has a frozen
  `memoryContext`; the prompt contains the field; an unrelated event → field absent.
- Idempotency: replaying the same signal deposits once.
- Dead-owner grace: kill pawn → store survives; advance past grace → store removed; a
  survivor's fragments naming the dead remain.
- Old-save load (no memory keys) → empty store, no errors.

## 13. Edge cases

- **Empty store / first entries:** `minFragmentsForRecall` returns empty; the prompt field is
  dropped; nothing special-cased downstream.
- **Self-echo:** excluded by the `sourceEventId == currentEventId` guard plus `minRecallAgeTicks`.
- **Skipped POVs (incapacitated):** deposit still happens (the pawn experienced it); recall is
  skipped (no prompt will render).
- **Historical/staged pages:** `createdTick = diaryEvent.tick` (the historical tick), and all
  recall math uses `diaryEvent.tick` as `currentTick`, so decay stays coherent with the
  frozen-context convention.
- **Pawn death:** survivors keep and recall fragments about the dead (feature); the dead pawn's
  own store is cleaned after a one-year grace (§10.2).
- **Kidnapped / leaving colonists:** stores persist while the pawn is resolvable; bounded by the
  per-pawn cap; the global cap and stale rule erode them if the pawn never returns.
- **Save size:** ≈ 500 bytes/fragment, ≤ 60/pawn ≈ 30 KB/pawn, hard-bounded colony-wide at
  3000 fragments ≈ 1.5 MB; caps enforced at deposit, on scan, and at save.
- **Mid-version tag/keyword changes:** unknown string tags load fine and simply stop matching;
  no migration needed; all Scribe keys additive.
- **Determinism under mods:** everything derives from frozen event strings and stable seeds; no
  live-state reads in the pure layer, so modded events flow through the same funnel untouched.

## 14. Implementation sequencing

1. Pure layer: `MemoryContracts` + `MemoryTagTokens` + `MemoryExtraction` +
   `MemoryRecallSelector` + `MemoryEvictionPlanner` + `MemoryContextPrompt`, with
   `tests/PawnMemoryTests` — fully verifiable before touching the game.
2. Persistence: `MemoryFragment`, `PawnMemoryRepository`, `ExposeData` wiring + tombstone
   dictionary; RimTest round-trip.
3. Policy: `DiaryMemoryTuningDef` + XML + `DiaryMemoryPolicy.Snapshot()`.
4. Capture hooks: `DiaryGameComponent.Memory.cs` (deposit + recall appliers) wired into
   `AddPairwiseEventCore`/`AddSoloEventCore`.
5. Prompt plumbing: §9 steps 1–8 + DefInjected labels (English + Russian).
6. Eviction applier + tick scan + dead-owner grace; `enableMemorySystem` settings checkbox.
7. RimTest end-to-end + an optional dev-panel fragment list for the existing Dev tab.

## 15. File inventory

New files:

| File | Contents |
|---|---|
| `Source/Models/MemoryFragment.cs` | persisted fragment model (§4) |
| `Source/Core/PawnMemoryRepository.cs` | store + indexes (§6) |
| `Source/Core/DiaryGameComponent.Memory.cs` | impure appliers: deposit, recall, eviction, dead-owner scan |
| `Source/Pipeline/Memory/MemoryContracts.cs` | `MemoryTagTokens`, snapshots, query/result DTOs, `MemoryPolicySnapshot` |
| `Source/Pipeline/Memory/MemoryExtraction.cs` | pure tags/keywords/importance/excerpt extraction (§7.3) |
| `Source/Pipeline/Memory/MemoryRecallSelector.cs` | pure direct scoring + 1-hop spread + rendering (§8) |
| `Source/Pipeline/Memory/MemoryEvictionPlanner.cs` | pure eviction planning (§10.1) |
| `Source/Pipeline/Memory/MemoryContextPrompt.cs` | source token + Compose (§9.6) |
| `Source/Defs/DiaryMemoryTuningDef.cs` + `1.6/Defs/DiaryMemoryTuningDef.xml` | tuning surface (§11) |
| `tests/PawnMemoryTests/` | pure test project (§12) |

Touched files (small, additive edits): `Source/Core/DiaryGameComponent.cs` (`ExposeData`,
tick scan) · `Source/Core/DiaryGameComponent.EventFactory.cs` (two hook calls) ·
`Source/Models/DiaryEvent.cs` (`PovSlot.memoryContext` + accessors + Scribe/normalization) ·
`Source/Pipeline/DiaryPipelineContracts.cs` · `Source/Generation/DiaryPipelineAdapters.cs` ·
`Source/Generation/PromptAssembler.cs` · `Source/Pipeline/DiaryPromptPlanner.cs` ·
`1.6/Defs/DiaryPromptTemplateDefs.xml` · `Source/Settings/PawnDiarySettings.cs` (checkbox) ·
`Languages/*` (labels). The csproj globs `Source/**/*.cs` — no project-file edits needed.
