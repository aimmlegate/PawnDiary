# RimTalk Bridge — context-extension plan (global context + pair shared memory)

> Status: **planned, not implemented** (2026-07-09). This plan extends the already-shipped RimTalk
> bridge with the two remaining features from the 2026-07-09 research handoff. It is written to be
> executed by a coding agent on a machine that can build against RimWorld + RimTalk (this repo's
> `.csproj` files reference Windows Steam paths; the plan author could not compile or playtest).
> Companion docs: `RIMTALK_BRIDGE_PLAN.md` (the original, shipped, steps 0–8), `../repowiki/README.md`
> (shipped behavior), `../EXTERNAL_API.md` / `../INTEGRATIONS.md` (public API contract).

Follow steps in order. Do not improvise beyond the marked decision points. Read `AGENTS.md` and
`skills/pawndiary-engineering/SKILL.md` first — they are binding, including the **RimTalk-type
isolation** rule and the **pure-code / main-thread** rules that the existing bridge already follows.

---

## 0. Audit — what already exists (reconciles handoff §2)

The handoff's "nothing implemented yet" is **stale**. As of this branch a full bridge is merged into
`main` under `integrations/PawnDiary.RimTalkBridge/` (plan `RIMTALK_BRIDGE_PLAN.md`, steps 0–8 done).

| Handoff feature | State | Where |
|---|---|---|
| #2 Pawn diary entries → `{{pawn.diary}}` | **Shipped** | `Source/DiaryContextInjector.cs` — `InjectPawnSection(Pawn.Thoughts, After)` + `RegisterPawnVariable("diary")`, per-pawn cache, status-listener invalidation. |
| #4 Weighted/importance selection | **Partly shipped** | `Source/Pure/ImportancePolicy.cs` (conversation → entry importance) + `ThrottlePolicy.cs`. No *selection weighting for reads* yet — this plan adds it. |
| Persona sync (not in handoff) | **Shipped** | `Source/PersonaSync.cs` — Tier A `chat_persona=` context provider, Tier B persona-led psychotype override. |
| Level-2 conversation capture (chat → diary) | **Shipped** | `Source/ConversationTracker.cs`, `Source/Pure/ConversationAssembly.cs`, engine mode in `RimTalkEngineClient.cs`. |
| #1 Global context → `{{colony_events}}` | **MISSING** | This plan, Step 3. |
| #3 Pair shared memory → `{{diary_shared}}` | **MISSING** | This plan, Step 4. |

Both missing features are **bridge-only**: they need no new Pawn Diary core C# or API members
(confirmed below). Everything lands in `integrations/PawnDiary.RimTalkBridge/`.

## 1. Verified facts — do NOT re-research the basics, but DO re-verify the ⚠️ items

Ground truth for shipped work is the installed DLL `cj.rimtalk` **v1.0.13** (net48, workshop
3551203752). The bridge already pins it (`Source/PawnDiaryRimTalkBridge.csproj` `RimTalkAssembly`;
`About/About.xml` `modDependencies`). The API facts below were re-verified 2026-07-09 against
`jlibrary/RimTalk` **`main`** source — v1.0.13 may lag, so treat new-to-us methods as ⚠️.

**RimTalk API (namespace `RimTalk.API`).** The shipped bridge uses the low-level
`ContextHookRegistry` directly (not the `RimTalkPromptAPI` façade) — **stay consistent with that.**
Note the two layers order their parameters differently:

- `ContextHookRegistry` — **name/section first, then modId** (this is what `DiaryContextInjector`
  already calls):
  - `RegisterEnvironmentVariable(string variableName, string modId, Func<Map,string> provider, string description = null, int priority = 100)`
  - `RegisterContextVariable(string variableName, string modId, Delegate provider, string description = null, int priority = 100)` — pass a `Func<PromptContext,string>`; the param is typed `Delegate`.
  - `InjectEnvironmentSection(string sectionName, string modId, ContextCategory anchor, InjectPosition position, Func<Map,string> provider, int priority = 100)`
  - `UnregisterMod(string modId)` for cleanup (no per-variable remove needed; registrations are process-global & idempotent, exactly like the shipped `InjectPawnSection`/`RegisterPawnVariable`).
- Façade `RimTalkPromptAPI` (do **not** use here; documented only so nobody mixes them up) —
  **modId first**: `RegisterContextVariable(string modId, string variableName, Func<PromptContext,string> provider, …)`. Different order → silent bugs if copied. Ignore the façade.
- Enums: `ContextHookRegistry.HookOperation {Append, Prepend, Override}`, `ContextHookRegistry.InjectPosition {Before, After}`.
- Anchors (verified): `ContextCategories.Environment.{Time, Date, Season, Weather, Temperature, Wealth}`; `ContextCategories.Pawn.{…, Thoughts, Social, Personality, Surroundings, …}`.

**⚠️ V1 — `PromptContext` shape.** `RegisterContextVariable`'s provider receives a
`RimTalk.Prompt.PromptContext` (namespace inferred from `RimTalkPromptAPI` usings; **confirm the
exact namespace + fields from source before coding Step 4**:
`https://github.com/jlibrary/RimTalk` → search `class PromptContext`). The handoff lists these
fields (re-verify names/types against v1.0.13): `Pawn CurrentPawn`, `List<Pawn> AllPawns` (alias
`Pawns`), `Map Map`, `TalkRequest TalkRequest`, `bool IsMonologue`, `TalkType TalkType`, string
`DialogueType/Intent/ConversationTopic/DialogueStatus`, `List<(Role,string)> ChatHistory`,
`bool IsPreview`. Step 4 needs only `AllPawns` (or `Pawns`), `IsMonologue`, and `IsPreview`. If
`RegisterContextVariable`/`PromptContext` is absent in the installed v1.0.13 DLL, Step 4's variable
registration must degrade (try/catch `MissingMethodException`/`TypeLoadException`, log one warning)
— the pair cache and Step 4's optional prompt-entry path can still function for template authors on
newer RimTalk.

**Pawn Diary core API (already sufficient — no core changes).** Verified in `Source/Integration/`:

- `DiaryEntryTitleQuery.partnerPawnId` — *"Other pawn id in a paired entry."* Partner ids are stored
  as `partner.GetUniqueLoadID()` (`Source/Ingestion/Sources/ExternalEventSignal.cs:70`), so Feature 3
  filters shared entries with `new DiaryEntryTitleQuery { partnerPawnId = other.GetUniqueLoadID() }`.
- `PawnDiaryApi.GetContextSnapshot(Pawn, int maxEntries, DiaryEntryTitleQuery)` →
  `DiaryContextSnapshot { List<DiaryEntryProseSnapshot> entries }`. `DiaryEntryProseSnapshot` exposes
  `tick` (recency), `title`, `summary`, `date`, `groupLabel`, `domain`, `atmosphereCue`,
  `externallyAuthored`, `externalSourceId`, `archived`. **Main-thread only** (returns empty off
  the main thread) and gated by `PawnDiaryApi.IsExternalApiEnabled`.
- `PawnDiaryApi.RegisterEntryStatusListener(id, Action<DiaryEntryStatusSnapshot>)` — the bridge
  already uses this (`DiaryContextInjector.OnEntryStatus`) to invalidate caches on entry change.
  `DiaryEntryStatusSnapshot.handle.pawnId` gives the changed pawn; a paired entry fires for both.

**Threading rule (the crux, same as shipped Feature 2).** RimTalk invokes variable/section
providers **while assembling a prompt, possibly on a background `Task`**. Providers must therefore be
**cache-read-only**: no `PawnDiaryApi` calls, no `.Translate()`, no map scans. All reads happen on
the **main thread** in the game component's throttled pass and are stored in a cache the provider
reads under a lock. Copy the exact `Gate`/`Cache`/`RefreshFor`/`SectionFor` split from
`DiaryContextInjector.cs`.

## 2. Open-question resolutions (handoff §6)

- **#2 packageId + min version / loading.** `cj.rimtalk`, v1.0.13, net48. Already pinned. Keep the
  **runtime-guard** approach the bridge already uses (`RimTalkActive = ModsConfig.IsActive("cj.rimtalk")`
  + `[MethodImpl(NoInlining)]` on every RimTalk-typed method + About.xml `modDependencies`). **Do not**
  add a `LoadFolders.xml` condition — the guard pattern is proven and already ships. Wrap the two new
  registrations in their own try/catch (like `RegisterAll`/`RegisterContextProvider` in the mod ctor)
  so a `MissingMethodException` on an older RimTalk disables just that feature.
- **#3 importance → selection weights.** See Step 5 (`SharedMemorySelection`, pure + tested).
- **#4 `{{diary_shared}}` delivery default.** Register the **context variable always** (Level ≥ 1 +
  feature toggle on) for template authors. For zero-config delivery there is **no "inject context
  section"** in RimTalk (only pawn/env section injection exists, and neither provider sees the second
  participant), so zero-config requires a **prompt entry** that references `{{diary_shared}}`.
  Recommendation: ship an `autoInjectSharedMemory` toggle **default ON**, implemented **only if V1
  confirms the prompt-entry API exists in v1.0.13** (`RimTalkPromptAPI.CreatePromptEntry` +
  `AddPromptEntry` + `RemovePromptEntriesByModId`). Register idempotently (remove-by-modId then add)
  and clean up on toggle-off / on missing feature. **⚠️ prompt entries persist in the user's active
  preset** — the cleanup is mandatory. If the prompt-entry API is absent in v1.0.13, default the
  toggle OFF, hide the row, and document the variable for manual template use.
- **#5 license.** RimTalk is CC BY-NC-SA 4.0. The bridge is already a **separate optional mod** that
  only *references* `RimTalk.dll` at compile time and calls public API — no RimTalk source/DLL is
  bundled or redistributed. Keep it that way, credit RimTalk in `About.xml`, and (maintainer action,
  not agent) ping the RimTalk author for a courtesy OK. This plan does not treat license as a blocker;
  flag it in the PR summary.
- **#6 `IsPreview`.** In the `{{diary_shared}}` provider, if `context.IsPreview` is true, return a
  cheap localized **sample** line (no cache lookup, no API) so RimTalk's settings-menu preview is fast
  and safe. Same idea for `{{colony_events}}`: the env provider has no `IsPreview`, so guard it by
  serving cache-or-empty only (never compute in the provider anyway).

## 3. Step A — Feature #1: global colony context → `{{colony_events}}`

**Goal.** A compact, curated line about the colony's *current* situation (recent raids, active
threats/conditions, anomaly state, open quests) injected into RimTalk prompts as an **environment**
variable + optional section. Differentiator vs RimTalk *Event Plus* (live per-event) / *Prompt
Enhance* (colony history): Pawn Diary curates a short *situational* summary incl. Anomaly. **Default
OFF** (overlap risk; opt-in).

**Files:** NEW `Source/Pure/ColonyEventsFormat.cs` (pure, testable); NEW `Source/ColonyContextInjector.cs`
(impure, RimTalk- + RimWorld-typed → `NoInlining` + guard); edit `RimTalkBridgeGameComponent.cs`
(refresh in the pass), `PawnDiaryRimTalkBridgeMod.cs` (register in ctor + settings), Keyed EN/RU.

1. **Pure `ColonyEventsFormat`** — no RimWorld/RimTalk usings (file-link into the test project like
   `ContextFormat`). Input: a `List<ColonyEventLine> { string Text; int Weight; }` plus a translated
   header and a `maxChars` cap; output: `"colony situation:\n- <line>\n- <line>"`, highest-weight
   first, whole lines only, "" when empty. Reuse `ContextFormat.CapAtWord`/`Clean` conventions.
2. **`ColonyContextInjector`** (mirror `DiaryContextInjector`'s cache/threading exactly):
   - Per-map cache `Dictionary<int /*map.uniqueID*/, CachedSection>` under a `Gate` lock.
   - `RefreshFor(Map map)` (**main thread**, in the pass): build the situation lines from **RimWorld
     map state**, translate labels, cap, store. Sources (all read-only, all main-thread):
     - Recent/active threats: `map.dangerWatcher.DangerRating`; active raids via
       `map.lordManager.lords` hostile lords or `map.attackTargetsCache`; keep it summary-level
       (e.g. "under attack", "recovering from a raid"), not a unit list.
     - Active game conditions: `map.gameConditionManager.ActiveConditions` (toxic fallout, eclipse,
       cold snap, etc.) — label each via its def, cap the count.
     - Anomaly (guard on `ModsConfig.AnomalyActive`): monolith level / active anomaly threats via
       `Find.Anomaly` — this is the curated differentiator; keep it vague/atmospheric, never spoilery.
     - Open quests: `Find.QuestManager.QuestsListForReading` where `quest.State == QuestState.Ongoing`
       and `!quest.hidden`; take the top 1–2 by a stable order, label via `quest.name`.
     - Weather/season come from RimTalk already — **do not** duplicate them here.
   - `SectionFor(Map map)` → **cache read only**; returns "" when Level < 1, feature toggle off,
     `!PawnDiaryApi.IsExternalApiEnabled`, or nothing cached.
   - `RegisterAll()` (`NoInlining`, from the mod ctor, guarded, try/catch):
     - `ContextHookRegistry.RegisterEnvironmentVariable("colony_events", BridgeIds.ModId, SectionFor, "<Keyed desc>", 100)` → usable as `{{colony_events}}`.
     - `ContextHookRegistry.InjectEnvironmentSection("colony_events", BridgeIds.ModId, ContextCategories.Environment.Weather, InjectPosition.After, SectionFor, 100)` for zero-config — ⚠️ same U1 caveat as Feature 2: **verify in-game** that injected environment sections render into the default prompt; if not, fall back to `RegisterEnvironmentHook(ContextCategories.Environment.Weather, Append, …)`.
   - `NeedsRefresh`/`ResetForNewGame`/`Gate` — copy shape from `DiaryContextInjector`. Cache key is
     the map; refresh all maps with colonists on the pass.
3. **Game component:** in `RunPass`, when `LevelAtLeast(1)` and `Settings.injectColonyContext`, call
   `ColonyContextInjector.RefreshFor` for each colonist-bearing map on the same TTL floor
   (`ContextRefreshTtlFloorTicks`). Add `ColonyContextInjector.ResetForNewGame()` to `FinalizeInit`.
4. **BridgeIds:** add `ColonyEventsVariableName = "colony_events"` (frozen token; matches the section
   name).

**Verify (maintainer, in-game):** with the toggle on and a live raid/quest, RimTalk's prompt shows a
`colony situation:` block; toggle off or Level 0 → nothing; no per-frame cost (cache served, refresh
on the 250-tick pass); Anomaly line only appears with Anomaly active and reads atmospherically.

## 4. Step B — Feature #3: pair shared memory → `{{diary_shared}}`

**Goal.** When two colonists talk, inject the diary memories they **share** (entries where one is the
subject and the other the partner) — "previous interactions" — selected weighted-randomly. Delivered
as a **context variable** (sees all participants) + optional auto prompt-entry (§2 #4).

**Files:** NEW `Source/Pure/SharedMemorySelection.cs` (pure, tested — Step 5); NEW
`Source/SharedMemoryInjector.cs` (impure, RimTalk-typed → `NoInlining` + guard); edit game component,
mod ctor, `BridgeIds`, Keyed EN/RU. Reuse `ContextFormat` for the final block text.

1. **Pair key.** Ordered `min|max` of the two `GetUniqueLoadID()` strings (string-compare, stable).
   Put a tiny pure helper `SharedMemorySelection.PairKey(idA, idB)` so it's testable and identical on
   read/write sides.
2. **Pair cache** `Dictionary<string /*pairKey*/, CachedSection>` under a `Gate` (same pattern). But
   pairs are O(n²) and the participants are only known at prompt time, so **do not** precompute all
   pairs. Use a **lazy request queue** (this is the clean fit for the cache-read-only provider):
   - Provider `SharedFor(PromptContext ctx)` (invoked by RimTalk, possibly background):
     - If `ctx.IsPreview` → return the localized sample line (no cache/API). (#6)
     - If Level < 1, feature toggle off, `ctx.IsMonologue`, or `< 2` colonist participants → "".
     - Resolve the current pawn (`ctx.CurrentPawn`) and the "other" (first other colonist in
       `ctx.AllPawns`/`ctx.Pawns`). Compute `pairKey`.
     - Under `Gate`: if cached → return text. Else enqueue `pairKey` (+ the two ids) into a
       thread-safe `ConcurrentQueue<PairRequest>` for the main-thread pass and return "" this time.
       (First conversation for a fresh pair shows nothing until the next pass fills it — acceptable,
       matches the "serve cache, refresh async" pattern; conversations recur.)
   - Pass (main thread, in `RunPass` when `LevelAtLeast(1)` and `Settings.injectSharedMemory`):
     drain the queue; for each unique `pairKey` not fresh, call
     `PawnDiaryApi.GetContextSnapshot(pawnA, N, new DiaryEntryTitleQuery { partnerPawnId = idB })`
     (N = a settings cap, ~8 candidates), map rows to `SharedMemoryCandidate`, run
     `SharedMemorySelection.Select(...)`, build the block with `ContextFormat` (reuse the memory-line
     format; cap 2–4 lines / ~150 chars each — settings), store in the cache. Resolve pawns from the
     queued ids via the same live-pawn resolution `ConversationTracker` uses (or `PawnsFinder`).
   - Invalidate on entry change: extend the existing status listener (or add one) so a changed entry
     for pawn X marks every cached pair containing X stale. Cheap: iterate cache keys, `Contains(Xid)`.
   - `ResetForNewGame()` clears cache + queue; wire into `FinalizeInit`.
3. **Registration** (`NoInlining`, mod ctor, guarded, try/catch — degrade on `MissingMethodException`):
   - `ContextHookRegistry.RegisterContextVariable("diary_shared", BridgeIds.ModId, (Func<PromptContext,string>)SharedFor, "<Keyed desc>", 100)` → `{{diary_shared}}`.
   - If `Settings.autoInjectSharedMemory` (§2 #4) **and** the prompt-entry API exists: idempotently
     register a system prompt entry whose content embeds `{{diary_shared}}` (remove-by-modId then
     add). Clean it up (`RemovePromptEntriesByModId`) when the toggle is off, when the feature is
     disabled, and defensively at registration start. Guard the whole block in try/catch.
4. **BridgeIds:** add `SharedMemoryVariableName = "diary_shared"` and (if used)
   `SharedMemoryPromptEntryName = "PawnDiary shared memory"` — both frozen tokens.

**Verify (maintainer, in-game):** two colonists with a past paired entry (e.g. an argument captured
by Level 2) talk → after one pass, RimTalk's prompt for that conversation shows a shared-memory line
naming the shared moment; a pair with no shared history → ""; monologue/single-participant → "";
preview in settings → sample text; toggling the feature clears the block and (if used) removes the
prompt entry from the active preset.

## 5. Step C — Feature #4: weighted selection (pure, tested)

**File:** NEW `Source/Pure/SharedMemorySelection.cs` (no RimWorld/RimTalk usings). Consumed by Step 4.

```csharp
public sealed class SharedMemoryCandidate
{
    public string Title;        // resolved title or group label
    public string Summary;      // one-line prose
    public string Date;
    public int Tick;            // recency
    public bool HasAtmosphereCue;   // rare cue present → more notable
    public bool IsConversationEntry; // external, bridge conversation source → "previous interaction"
}
```

Weight (map handoff's `f(recency, importance, pair involvement)`; pair involvement is guaranteed by
the partner-filtered query, so it drops out of the per-candidate weight):

- **recency:** rank candidates newest-first by `Tick`; `recencyWeight = 1.0 / (1 + ageRank)`
  (newest = 1.0, next ≈ 0.5, …). Keeps recent shared moments likeliest without ignoring old ones.
- **importance multiplier:** `1.0 + (HasAtmosphereCue ? cueBonus : 0) + (IsConversationEntry ? convoBonus : 0)`
  with `cueBonus`/`convoBonus` small constants (e.g. 1.0 / 0.5) — tune later, keep in code as named
  consts with a comment (parser-style constants, not player settings).
- `weight = recencyWeight * importanceMultiplier`.

`public static List<SharedMemoryCandidate> Select(List<SharedMemoryCandidate> candidates, int maxPick, int seed)`:
weighted-random **without replacement**, using a **`System.Random(seed)`** (NEVER `Verse.Rand`
inside anything reachable from a provider — the handoff and SKILL.md both require this). Seed from a
stable per-pair value passed by the caller (e.g. `PairKey.GetHashCode()` combined with the in-game
day) so a pair's shown memories are stable within a refresh window and the function is deterministic
under test. Return newest-first for display stability after selection.

**Tests** (`tests/RimTalkBridgeLogicTests/Program.cs`, extend the existing assert-based `Program`):
- `PairKey` is order-independent and stable.
- Recency ordering: with equal multipliers and a fixed seed, newer candidates are selected more often
  across many seeds (statistical) and `maxPick` is respected.
- Importance multiplier: a cued/conversation candidate outranks a plain one of equal age.
- `maxPick >= count` returns all; empty input returns empty; `maxPick <= 0` returns empty.
- `ColonyEventsFormat`: weight ordering, header/empty handling, `maxChars` whole-line truncation.

Run: `dotnet run --project tests/RimTalkBridgeLogicTests/RimTalkBridgeLogicTests.csproj` (the pure
project file-links `Source/Pure/*.cs`; add the two new files to its `<Compile Include>` if it lists
them explicitly — check the `.csproj`).

## 6. Step D — settings, localization, docs

1. **Settings** (`PawnDiaryRimTalkBridgeSettings` in `PawnDiaryRimTalkBridgeMod.cs`) — new fields with
   **new frozen Scribe keys** (never reuse existing keys), defaults chosen per §2:
   - `bool injectColonyContext = false;` (`"injectColonyContext"`)
   - `bool injectSharedMemory = true;` (`"injectSharedMemory"`)
   - `bool autoInjectSharedMemory = true;` (`"autoInjectSharedMemory"`) — effective only if the
     prompt-entry API exists (else forced off + row hidden).
   - `int sharedMemoryCount = 3;` (cap 0–4) and `int colonyEventCount = 3;` (cap 0–6) with defensive
     clamps in `ExposeData` like the existing ints.
   Draw them under the Advanced section as checkboxes + `TextFieldNumericLabeled`, gated so the
   shared-memory sub-toggles disable when `injectSharedMemory` is off.
2. **Localization:** add all new Keyed strings to `Languages/English/Keyed/PawnDiaryRimTalkBridge.xml`
   (settings labels/descs, section headers `colony situation:` / shared-memory header, memory-line
   format reuse, the `IsPreview` sample line, variable descriptions). Mirror in
   `Languages/Russian (Русский)/Keyed/PawnDiaryRimTalkBridge.xml` — **author native RU, do not calque**
   (handoff ⚠️ U4); flag in the PR for the maintainer's native pass. Schema keys in prompt text stay
   English (carve-out, comment why); the pawn perspective must never mention "RimTalk"/"AI".
3. **Docs (part of done):** `../repowiki/README.md` integrations section — add the two variables
   (`{{colony_events}}`, `{{diary_shared}}`), their toggles, the pair-cache/threading design, and the
   U1 in-game outcome for `InjectEnvironmentSection`. Dated `../CHANGELOG.md` entry. If
   `../EXTERNAL_API.md` / `../INTEGRATIONS.md` enumerate consumer-facing variables, add rows.
   `About/About.xml` description: add the global-context + shared-memory capabilities.
4. **Deploy for testing:** `scripts/deploy-integrations.ps1`, then run the in-game matrix from each
   step's Verify plus: master `Allow external mod integrations` OFF → both providers return "";
   RimTalk absent → bridge idles, no errors; Level 0 → nothing injected.

## 7. Risks / explicitly out of scope

- **⚠️ V1 (blocking for Step 4 registration):** confirm `RegisterContextVariable` + `PromptContext`
  exist in the **installed v1.0.13** DLL, not just `main`. If absent, ship Step 4's pair cache + the
  documented-variable path but degrade the auto prompt-entry and log one warning; do not fake the API.
- **U1 (verify in-game):** does `InjectEnvironmentSection` render into RimTalk's default prompt?
  Fallback `RegisterEnvironmentHook(Environment.Weather, Append)`. Same open question the shipped
  Feature 2 carries for `InjectPawnSection`.
- **Overlap:** Feature 1 overlaps RimTalk *Event Plus* / *Prompt Enhance* — hence default OFF and a
  curated/atmospheric (esp. Anomaly) angle rather than a live event feed.
- **Prompt entries persist in the user's active preset** — cleanup on toggle-off is mandatory (§2 #4).
- **Do NOT** add core Pawn Diary API members (none are needed), touch RimTalk's save/persona,
  introduce triggered chats, or use `Verse.Rand` anywhere a provider can reach it.
- **Token budget:** RimTalk targets small models; keep both blocks tight (caps are settings; hard
  char caps stay in code like `DiaryContextInjector.MaxSectionChars`).
