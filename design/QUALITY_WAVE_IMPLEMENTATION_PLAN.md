# Pawn Diary — Quality Wave Implementation Plan

Status: APPROVED 2026-07-22 · Quality review confirmed 2026-07-22 · Owner: coding agent · Scope: 10 features (A5, B1, B2, B6, C4, H1, H2, H3, H5, H6)

Companion docs: `AGENTS.md` (rules), `repowiki/README.md` (architecture), `skills/pawndiary-engineering/SKILL.md` (workflow).

## How to use this document

- Implement **phase by phase, in order**, but treat phases as scheduling containers rather than atomic changes. Each feature is its own internal release: complete the Done checklist (§10), commit it separately, then start the next feature. The shared `PovSlot` contract migration in Phase 2 is also its own internal release and commit.
- Internal releases are not public versions or tags. The user will make one public `1.0` release when the complete wave is ready and tested; there is no separate formal final gate beyond the per-feature Done checks and the user's release decision.
- Every feature spec below lists: goal, locked decisions, files, data/save changes, algorithm, XML, templates, tests, acceptance, risks.
- When a spec says **verify against 1.6 assemblies**, check the real RimWorld type/member before writing code (decompile or object browser); if a patch target is fragile, register via `DiaryPatchRegistrar` with signature check + one warning, never bare `PatchAll`.

## 0. Locked decisions (from user, 2026-07-22)

1. B1: ship the directive **and** Russian DefInjected labels for all newly appended template fields.
2. A5: everyday templates = **PairDefault + PairBatched + PairImportant**.
3. B2: two-stage roll — master apply-chance **×** mood-band chance.
4. B6: digest-lines phase first, soft cap (=2) second.
5. H2: all four sub-features (birthdays, arrival anniversaries, bonded-death anniversaries, records).
6. H1: raid-only slice.
7. H6: enabled by default, 50% mention chance.
8. Phases define order only. Every feature (plus the shared Phase-2 `PovSlot` migration) gets an isolated internal release and commit.
9. B1 names **every** active output language, including English.
10. C4 preserves the three legacy cue values exactly; the `0.05–0.12` page-tint alpha limit applies to newly added cue tints.
11. A5 is **key relationships**, not only positive bonds: one named-relation slot is reserved, remaining slots rank by emotional intensity, and prompt output uses localized sentiment bands rather than numeric opinion.
12. B2 samples independently per POV with a deterministic hash roll. Batched pages retain the most emotionally extreme contributing event-time snapshot; flush-time mood is never presented as event-time mood.
13. H3 is colony-wide after the pawn's arrival boundary, but a directly owned same-category/day story suppresses letter news conservatively. Never render both versions.
14. H5 prompt callbacks search both hot and archived history.
15. H1 waits for POV battle evidence and then a combat-log quiet interval, bounded by a hard deadline.
16. H6 uses deterministic sampling, deterministic single-protagonist selection, an eligible-sculptor memorial fallback, and once-per-stable-tale ownership.
17. New Quality Wave code never calls `Rand.Chance`. This rule does not expand into cleanup of existing code; B2/H2/H6 sampling added by this wave is deterministic and does not consume gameplay RNG.
18. H2 uses decaying deterministic bonded-death recall after year 3, milestone arrival years, monotonic record high-water marks, deterministic 16-memory retention, and at most one combined bonded-death page per pawn/day.
19. B6 state survives save/load. Digest content is opportunistic, never displaces an important highlight, retains four unique newest lines per pawn/day, and suppresses a pair page only when both POVs are capped.

## 1. Global rules for every change

- **Architecture barrier:** impure capture/freeze (signals, `DiaryGameComponent.*`, `DiaryContextBuilder`) → plain DTO/save strings → pure policy (`Source/Pipeline/**`, no Verse/Rand/DefDatabase/Translate) → impure transport/UI. New cross-layer data = extend the typed contract (`PromptValues`, `DiaryPovPayload`, `PovSlot`), never pass live `Pawn`/`Def` into pure code.
- **Template XML is append-only.** Existing `fields` row order in `1.6/Defs/DiaryPromptTemplateDefs.xml` is frozen (DefInjected labels are indexed `fields.N.label`). New fields go at the END of each template's list.
- **Localization:** every new player-facing/prompt string lands in `Languages/English/Keyed/PawnDiary.xml` AND `Languages/Russian (Русский)/Keyed/PawnDiary.xml`; Def text via DefInjected in both languages. Structured schema tokens (`key=` in gameContext, `mood=`, `none`/`n/a`) stay English (carve-out, repowiki/README.md §12). `.Translate()` main-thread only.
- **Tunables in XML** (`DiaryTuningDef.xml` or the feature's Def) with code fallbacks.
- **DLC-safety:** string defNames + `GetNamedSilentFail` only; no DLC types referenced.
- **Save schema:** new saved fields are additive with normalization that treats missing as empty/zero; old saves must load unchanged.
- **Exact ownership/no duplicates:** derived context or anniversary features never repeat a story already owned by a direct diary event. Use stable identity/typed category ownership, never fuzzy prose matching. Purposeful later remembrance (for example a death anniversary) is not a same-event duplicate.
- **New-wave randomness:** do not add `Rand.Chance`. Prefer deterministic hash sampling from saved/stable identities so reloads and repeated callbacks cannot reroll and the mod does not perturb RimWorld's gameplay RNG.
- **After EACH feature/internal release:** `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`; stage rebuilt `1.6/Assemblies/PawnDiary.dll`; run touched pure tests (`dotnet run --project tests/DiaryPipelineTests/...` etc.); XML-parse edited Defs; update `repowiki/README.md` + dated `CHANGELOG.md` line; commit only that verified feature. Apply the same gate to the shared Phase-2 contract migration.

## 2. Phase map

| Phase | Ordered internal releases | Touches |
|---|---|---|
| 1 | B1, then C4 | prompt pipeline contracts; UI style Def |
| 2 | shared `PovSlot` contract migration, then A5, then B2 | save schema, context builder, templates |
| 3 | H3, then H5-prompt | day/quadrum reflection collectors |
| 4 | H5-UI | diary tab divider |
| 5 | H1, then H6 | battle beats; art patch |
| 6 | H2 | anniversary/records scanner |
| 7 | B6 digest-lines foundation, then B6 soft-cap behavior; one B6 feature commit after both ordered slices pass their local checks | digest routing + soft cap |

---

## 3. Phase 1

### 3.1 B1 — Output-language directive

**Goal:** every LLM request with an active language ends its system prompt with one localized line naming the output language, including English; remove model language-guessing (Russian build failure mode).

**Files:**
- `Source/Pipeline/DiaryPipelineContracts.cs` — add `public string outputLanguageDirective;` to `DiaryPromptRequest`.
- `Source/Generation/DiaryPipelineAdapters.cs` — new `private static string OutputLanguageDirective()`, called in `BuildPromptRequest` (main thread).
- `Source/Pipeline/DiaryPromptPlanner.cs` — in `Build`, after `ComposeSystem`: if directive non-empty, `systemPrompt = systemPrompt.TrimEnd() + "\n" + directive`. Apply to ALL template keys including `Title` (titles must match UI language).
- `Source/Defs/DiaryTuningDef.cs` + `1.6/Defs/DiaryTuningDef.xml` — `outputLanguageDirectiveEnabled` (default `true`).
- Keyed EN: `<PawnDiary.Prompt.OutputLanguage>Write the diary entry in {0}.</PawnDiary.Prompt.OutputLanguage>`
- Keyed RU: `<PawnDiary.Prompt.OutputLanguage>Пиши дневниковую запись на этом языке: {0}.</PawnDiary.Prompt.OutputLanguage>`

**Resolution logic (adapter, main thread):**
```
if (!DiaryTuning.Current.outputLanguageDirectiveEnabled) return "";
lang = LanguageDatabase.activeLanguage;
if (lang == null) return "";
name = lang.FriendlyNameNative;  // VERIFY member name on LoadedLanguage in 1.6
if (name is blank) name = lang.FriendlyNameEnglish;
if (name is blank) name = lang.LegacyFolderName;
if (name is blank) return "";
return "PawnDiary.Prompt.OutputLanguage".Translate(name).Resolve();
```
Prompts are built on the main thread (`BuildPromptRequest` from the generation queue path + prompt preview) — confirm no background path builds them; the directive must never be resolved inside `LlmClient`.

**Russian labels:** extend `Languages/Russian (Русский)/DefInjected/PawnDiary.DiaryPromptTemplateDef/DiaryPromptTemplateDefs.xml` with `fields.N.label` rows for every field appended in phases 2/5 (indices = final positions after appends). Do NOT translate `key=` schema tokens.

**Tests (DiaryPipelineTests):** English and non-English directives append exactly once; empty/missing-language directive → unchanged system prompt; toggle off → unchanged; Title request also carries it. RimTest: see §12.

### 3.2 C4 — Per-cue page tints + header rules

**Goal:** replace the 3-cue special-casing with per-cue XML color specs for all ~15 cues.

**Files:** `Source/Defs/DiaryUiStyleDef.cs`, `1.6/Defs/DiaryUiStyleDef.xml`.

**Code:**
- `DiaryUiCueColor` gains `public DiaryUiColorSpec pageTint;` and `public DiaryUiColorSpec headerRule;` (null = inherit default).
- In the C# fallback `cueColors` initializer, attach the existing combat/socialFight/mentalBreak tint+rule values as `pageTint`/`headerRule` on those three entries (values = today's `combatPageTintColor` etc., so no-XML behavior is byte-identical).
- `PageTintForCue(cue)`: find cue in `cueColors`; if `entry.pageTint != null` → `entry.pageTint.ToColor(PageTintColor)`; else `PageTintColor`. Same shape for `HeaderRuleForCue`. Delete the hardcoded if-chains. Keep `combatPageTintColor` etc. fields for save/XML compat (now used only as the seeded values).

**XML (`DiaryUiStyleDef.xml` `cueColors`):** one row per cue with accent + pageTint + headerRule. Preserve the three legacy values exactly even where they exceed the new range; for every newly added tint keep α 0.05–0.12 and rule α 0.35–0.65 unless a visual test proves a stronger value is required:
- combat: tint ember red (0.70,0.10,0.07,0.18), rule (0.95,0.18,0.12,0.65) [existing]
- socialFight: orange [existing]; mentalBreak: muted green [existing]
- danger: tint (0.55,0.08,0.05,0.12), rule (0.80,0.15,0.10,0.55)
- extremeDark: tint (0.25,0.02,0.05,0.12), rule (0.45,0.05,0.10,0.60)
- daze: tint teal (0.10,0.30,0.28,0.08); strangeChat: tint (0.05,0.25,0.10,0.10)
- white (romance/heartfelt): warm rose-paper tint (0.45,0.25,0.20,0.07), rule (0.70,0.45,0.38,0.40)
- psychic: violet tint (0.25,0.10,0.40,0.08), rule (0.55,0.30,0.80,0.45)
- royalty: gold tint (0.40,0.30,0.08,0.08), rule (0.75,0.60,0.20,0.45)
- bodyPartAnomalous/Artificial/Lost: their accents at tint α ~0.08
- eventful (NEW accent+tint): accent warm amber (0.90,0.65,0.25,1), tint (0.35,0.25,0.08,0.06)
- quadrumReflection (NEW): accent soft blue (0.55,0.65,0.85,1), tint (0.15,0.20,0.35,0.07)
- quiet: neutral gray tint (0.30,0.30,0.28,0.05)

Unknown/modded cues → default `pageTintColor`/`headerRuleColor`.

**Acceptance:** XML parses; dev mock entries per cue show distinct tint/rule; no change when XML absent. RimTest: see §12.

---

## 4. Phase 2 — shared contract migration, then A5, then B2

Both add additive `PovSlot` string fields. First land one behavior-free shared contract migration that adds and normalizes both fields through steps 1–4 below. Build/test/commit that migration independently. A5 and B2 then populate their own field and append only their own template rows in separate internal releases.

**Shared payload chain (per field `X`):**
1. `Source/Models/DiaryEvent.cs` — `PovSlot.X` field; scribe alongside `memoryContext` (same struct field pattern); `SetX(role, value)` + `XForRole(role)`; save-normalization: null → empty, cap length (600 chars, like memoryContext).
2. `Source/Pipeline/DiaryPipelineContracts.cs` — `DiaryPovPayload.X`.
3. `Source/Generation/DiaryPipelineAdapters.cs` — `PovFor` copies it.
4. `Source/Generation/PromptAssembler.cs` — `PromptValues.X` + `ResolveSource` token.
5. In the owning feature release, `1.6/Defs/DiaryPromptTemplateDefs.xml` — append `<li><label>…</label><source>…</source></li>` at END of listed templates; RU DefInjected `fields.N.label` rows.

### 4.1 A5 — Identity block

- **Pure:** new `Source/Pipeline/IdentitySummaryPolicy.cs`:
  - `IdentityRow { string name; string relationLabel; string sentimentLabel; int opinion; bool hasRelation; }`
  - `Select(IReadOnlyList<IdentityRow> rows, int max)` reserves at most one slot for the strongest named relation (|opinion| desc, name ordinal-ignore-case tie-break), then fills remaining slots from every unselected row by |opinion| desc and name ordinal-ignore-case. This keeps family identity without hiding a defining friend or rival.
  - Format each row as `name (relationLabel, sentimentLabel)` or `name (sentimentLabel)`; join `"; "`; prefix `relationships=`. Never expose the numeric opinion. Returns `""` for empty.
- **Impure:** `DiaryContextBuilder.BuildIdentitySummary(Pawn pov, Pawn partner)`: iterate free colonists (exclude pov + partner); opinion via the fail-soft `DiaryGameComponent.TryReadOpinion`; relation label via `PawnRelationUtility.GetMostImportantRelation` → `GetGenderSpecificLabelCap` → `ExternalText`; sentiment via the existing XML-tuned/localized `DiaryBuckets.FormatOpinion`. Feed the resulting plain rows to `IdentitySummaryPolicy.Select` with `DiaryTuning.Current.identitySummaryMaxRelations` (XML, default 2).
- **Freeze:** `AddPairwiseEventCore` only (recipient != null), per POV, next to continuity. Birth-captured path: leave empty (no captured equivalent).
- **Templates:** PairDefault, PairBatched, PairImportant — `<li><label>key relationships</label><source>Identity</source></li>`.
- **Tests:** reserved named-relation slot; intense non-relative friend/rival selection; positive and negative sentiments; no numeric opinion leakage; cap; deterministic tie-break; formatting; empty input. RimTest: see §12.

### 4.2 B2 — Mood snapshot

- **Pure:** new `Source/Pipeline/MoodSnapshotPolicy.cs`:
  - `BandChance(int moodPercent, IReadOnlyList<MoodSnapshotChanceRule> rules, float fallback)` — rules ordered by `maxPercent` asc; first band with `moodPercent <= maxPercent` wins.
  - `ShouldInclude(float roll, float applyChance, float bandChance)` → `roll < applyChance * bandChance`.
  - `DeterministicRoll(string eventId, string pawnId, string povRole)` → stable [0,1) hash sample; distinct POV roles produce independent rolls without consuming `Verse.Rand`.
  - `PreferBatchSnapshot(current, candidate)` → keep the candidate with the greatest absolute distance from neutral mood; deterministic tie-break (lower mood, then earlier tick).
  - `IsEligibleContext(string gameContext)` — `DiaryContextFields.HasMarker` for `mood_event=`, `thought=`, `inspiration=`, `work=`, `hediff=`, `batch=` (mirror `DiaryPromptPlanner.TemplateKeyFor`; both call this one helper to prevent drift).
- **XML (`DiaryTuningDef`):** `moodSnapshotApplyChance` (default 0.6); `moodSnapshotChances` list of `{ maxPercent, chance }`: 15→0.95, 35→0.5, 65→0.12, 85→0.3, 101→0.55.
- **Impure:** `DiaryContextBuilder.CaptureMoodSnapshot(Pawn, tick)` returns the plain mood percent plus `mood=<BuildMoodSummary>` and `"; thoughts=<first top thought>"` when present.
- **Freeze (ordinary pages):** after the `DiaryEvent` has its event ID, `AddSoloEventCore`/`AddPairwiseEventCore` evaluates each eligible POV independently with `DeterministicRoll(eventId, pawnId, role)` and freezes the snapshot or empty. Ineligible contexts do no sampling.
- **Freeze (batched pages):** `PendingInteractionBatch` and `PendingAmbientInteractionNote` retain each POV's most emotionally extreme contributing event-time snapshot while the batch is open. At flush, evaluate that retained snapshot once with the final event ID. Never sample live mood at batch flush and describe it as event-time mood. Saving already flushes pending batches, so the selected snapshot reaches the saved `PovSlot` before persistence.
- **Templates:** SoloInternalState, SoloBatched, PairBatched — `<li><label>you</label><source>MoodSnapshot</source></li>`.
- **Tests:** band boundaries (0/15/35/65/85/100), product roll, deterministic and role-distinct sampling, marker eligibility set, extreme-batch selection/ties, event-time rather than flush-time snapshot, slim format. RimTest: see §12.

---

## 5. Phase 3 — H3 + H5-prompt

### 5.1 H3 — Colony news in reflections

- `Source/Defs/PromptArchitectureDefs.cs` — add `DiaryContextReactions` key const `ColonyNews = "colony_news"` (verify the key plumbing: `ForKey`/`ScanBack`/`TimeoutTicks`/`LetterDefAllowed` are generic).
- Extend the reaction policy with XML-owned news-category rules. Each rule declares a stable category token (`threat`, `quest`, `positive`), its allowed letter Def names, and the direct diary domains/markers that own that category. Do not infer ownership from translated labels.
- `1.6/Defs/DiaryContextReactionDefs.xml` — row: enabled, scanBack ~40, timeoutTicks = day window, `requireHomeMap` false, categorized threat/quest/neutral-positive letter rules (copy the threat-letter list shape where applicable).
- `Source/Core/DiaryGameComponent.DaySummary.cs` — `CollectNewsSignals(pawn, dayStartTick, candidates)`: scan `Find.Archive.ArchivablesListForReading` newest-first (bounded); reuse the staleness/tick logic from `ThreatLetterIsStale` (READ that helper first for the exact archivable tick member). Clip the lower bound to the pawn's first arrival tick: colony news is shared with map/caravan colonists, but never attributed to a pawn before they joined.
- Before accepting a letter, query hot + archived diary history for a displayable direct event owned by the same pawn, XML category, and day. If one exists, suppress the letter conservatively even when that also hides a distinct second story in the category. The no-duplicate guarantee is more important than fuzzy recovery.
- The newest remaining allowed `Letter` inside [max(dayStart, arrival), now] produces one candidate: line `"PawnDiary.Event.DayReflectionNews".Translate(SafeArchivedLabel(letter))`, kind `DayReflectionEventData.SignalKindNews` (new const "news"), weight `tuning.daySummaryWeightNews` (XML, 0.3), not important by default (still XML-overridable via `daySummaryImportantSignalKinds`).
- Quadrum: same helper over [max(quadrumStart, arrival), evidenceEnd], with the same direct-owner suppression, → one `QuadrumReflectionSignal`.
- Keyed EN `"the colony was told: {0}"` / RU equivalent.
- **Tests:** capture-policy format test for the new signal kind tag (DiaryCapturePolicyTests pattern); arrival-boundary exclusion; same-category/day direct-owner suppression across hot and archive history; unrelated categories remain eligible; manual in-game check. RimTest: see §12.

### 5.2 H5 prompt — last-year same-quadrum snippet

- `DiaryTuningDef`: `onThisDayQuadrumCallbackEnabled` (default true).
- `DaySummary.cs` `TryFlushQuadrumReflectionForPawn`: after current-quadrum collection, if enabled && `quadrum >= 4`, collect the matching prior-year quadrum from **both** hot events and `archive.EntriesForPawn`. Normalize both sources into one plain candidate shape, deduplicate by stable source/event identity, then take the max-weight line. Add it with weight `daySummaryWeightMajorEvent * 0.5f`, tag kind `memory` (new kind const), line wrapped `"PawnDiary.Event.QuadrumReflectionLastYear".Translate(line)` = "a year ago, same season: {0}".
- Archive support is part of the initial slice, not a follow-up: year-old evidence is exactly the evidence most likely to have left the hot store.
- RimTest: see §12.

## 6. Phase 4 — H5 UI "On this day" divider

- New `Source/UI/DiaryOnThisDayDivider.cs`:
  - `DayOfYear(int absTick)` = `(absTick / GenDate.TicksPerDay) % GenDate.DaysPerYear`.
  - `EntryDayOfYear(DiaryEntryView e)` — convert `e.Tick` (TicksGame) to abs using the same offset math as `DayIndexForGameTick`.
  - `bool Matches(entry, currentDayOfYear)`; `Label(int yearsAgo)` → Keyed `PawnDiary.Tab.OnThisDay` ("On this day · {0} year(s) ago" / RU).
- `Source/UI/ITab_Pawn_Diary.cs` — in the entry-layout pass where quadrum dividers are computed (`BeginEntryLayoutBuild`/`ProcessEntryLayoutSlice`): when the viewed year < current in-game year, detect the first entry whose `EntryDayOfYear` == current day-of-year; reserve one divider row (height `quadrumDividerHeight`) above it; draw via the existing `DrawQuadrumDivider`-style renderer (no season icon).
- Dev-mock caveat: divider is tick-based; mock pages with fake dates simply never match (acceptable, matches the file's existing stance).
- RimTest: see §12.

## 7. Phase 5 — H1 + H6

### 7.1 H1 — BattleLog mining (raid only)

- New `Source/Generation/BattleBeatsBuilder.cs` (impure, main thread):
  - `Inspect(Pawn pov, string raiderFactionDefName, int raidTick, int now, DiaryTuningDef tuning)` returns plain facts: matching battle found, latest relevant entry tick, and bounded candidate beats.
  - Scan `Find.BattleLog.Battles` newest-first, max `battleBeatsScanBackBattles` (8): candidate = battle with `creationTick >= raidTick - 600 && <= now` whose entries include the **POV pawn** and a pawn of faction def == `raiderFactionDefName`. (VERIFY 1.6 `Battle` API: entries list + `GetConcernFactions()`; fall back to scanning entries' pawn factions.) Requiring the POV prevents a concurrent same-faction battle from stealing ownership.
  - From the matched battle: entries where `initiator` or `recipient` is `pov`; score kill/down > hit > near-miss/other; take top `battleBeatsMaxCount` (2) in chronological order; `entry.ToGameStringFromPOV(pov)` inside per-entry try/catch; `DiaryLineCleaner.CleanLine`; join `" | "`; total cap ~240 chars.
- Hook: `Source/Core/DiaryGameComponent.Generation.cs` `EnsureGenerationQueued` — before queueing, if `DiaryContextFields.HasMarker(gameContext, "raid=")` (verified leading marker in `RaidEventData.BuildGameContext`) and `battle_beats_checked=true` is absent, inspect beats from the saved faction defName + `livePawnsById[initiatorPawnId]`.
  - No matching POV battle yet and age < deadline → move the existing per-role generation-ready tick to `now + battleBeatsRetryIntervalTicks`; do not queue.
  - Matching battle found but `now - latestRelevantEntryTick < battleBeatsQuietTicks` and age < deadline → retry after the interval so an early miss cannot hide a later down/kill.
  - Quiet or deadline reached → append cleaned `battle_beats=` when non-empty, always append saved schema marker `battle_beats_checked=true`, then queue normally. The saved marker replaces the proposed unscribed session set and prevents load/retry loops even when no beats were found.
- Tuning XML: `battleBeatsEnabled=true`, `battleBeatsMaxCount=2`, `battleBeatsScanBackBattles=8`, `battleBeatsRetryIntervalTicks` (fallback 250), `battleBeatsQuietTicks` (default 1200), `battleBeatsMaxAgeTicks=30000`.
- Templates: append `<li><label>combat beats</label><source>GameContext</source><contextKey>battle_beats</contextKey></li>` to SoloImportant + PairCombat; RU labels.
- Acceptance: drop-pod/infestation pages do not queue on the incident tick; after POV combat goes quiet they reference the strongest real beat(s); a battle that runs to the deadline uses the best evidence available then; pruned/absent log reaches the deadline, records `battle_beats_checked=true`, omits the prompt field, and logs no errors. RimTest: see §12.

### 7.2 H6 — Art immortalization

- VERIFY `CompArt.InitializeArt` 1.6 signature (expected `public void InitializeArt(ArtGenerationContext context)`) and that `CompArt.taleRef`/`Title` members exist as expected.
- New `Source/Patches/DiaryArtPatches.cs` — postfix on `InitializeArt`; via `DiaryPatchSafety.Run`; direct `[HarmonyPatch]` if the method is public/stable, else `DiaryPatchRegistrar`.
- New pure `ArtImmortalizationPolicy` owns stable protagonist order and sampling:
  - Require an exact stable tale identity; fail closed when it cannot be verified because once-per-tale ownership may not degrade to translated-label matching.
  - Writer order: eligible dominant pawn; otherwise the eligible concerned colonist with the lowest stable load ID; otherwise an eligible sculptor **only** when the tale concerns a pawn with an existing colony diary (hot or archived). Unrelated trader art remains silent.
  - Compute the chance sample deterministically from sculpture stable ID + tale identity and compare with the XML chance. Never call `Rand.Chance` or consume `Verse.Rand`.
- Postfix logic: resolve tale/sculpture/sculptor facts, apply the pure policy, then query hot + archived diary ownership for the exact tale identity. Once any art-immortalization event exists for that tale, every later sculpture skips. If the current artwork passes and the tale is unclaimed, submit `ArtImmortalizedSignal(writer, sculpture, taleIdentity, tale)`; event registration becomes the claim before LLM generation.
- New `Source/Ingestion/Sources/ArtImmortalizedSignal.cs` + minimal `Capture/Events/ArtImmortalizedEventData.cs` + spec (new `DiaryEventType.ArtImmortalized`; mirror `MoodEventData`/`MoodEventSpec` shape). Exact ownership key `"art-tale|" + taleIdentity`; gameContext: `art=<thingDefName>; art_thing_id=<stableId>; label=<clean title>; sculptor=<name>; tale=<taleDefName>; art_tale_id=<stableTaleId>`.
- XML: interaction group `artImmortalized` (GroupDomain.Interaction, matchDefNames `PawnDiary_ArtImmortalized`, important=false, colorCue white); `DiaryEventPromptDef` row (prompt+enhancement); Keyed label `"immortalized in art"` + text `"{0}'s deed was immortalized in the sculpture \"{1}\"."`; RU mirrors.
- Acceptance: crafting sculptures about one stable tale yields ≤1 quiet page across all sculptures; repeated initialization cannot reroll one sculpture; deterministic writer choice survives iteration-order changes; art about a deceased diarist may be written by its eligible sculptor; trader art with unrelated tales → nothing. RimTest: see §12.

## 8. Phase 6 — H2 anniversaries & records

- New `Source/Core/DiaryGameComponent.Anniversaries.cs`: `ScanAnniversariesForDiaryEvents()` — gated in `GameComponentTickInner` by elapsed-time `nextAnniversaryScanTick` (interval XML `anniversaryScanIntervalTicks`, default 15000; scheduling per lore: elapsed time, never modulo).
- **State (additive on `PawnProgressionState`, normalization-safe):**
  - `lastObservedBiologicalAgeYears` (int, -1 = unset);
  - `lastArrivalAnniversaryYear` (int);
  - `bondedDeathMemories` (`List<BondedDeathMemoryState>`, cap from XML, default 16), each with victim ID/name, relation label + stable priority token, death tick, and last processed anniversary year;
  - `lastBondedDeathDiscoveryTick` so hot/archive discovery advances monotonically and an evicted memory is permanently forgotten instead of being rediscovered on every scan;
  - `recordMilestoneHighWater` (`Dictionary<string,float>` defName→highest value ever observed; values never decrease).
- **Baseline rule:** first scan per pawn initializes age, arrival, death discovery/retention, and record high-water state but emits nothing (same as skill milestones). An old save therefore never receives retroactive pages.
- **Birthday:** `pawn.ageTracker.AgeBiologicalYears > lastObserved` → consider the current age once. If hot/archive history already contains a direct birthday/growth page for this pawn and exact `birthday_age`, advance the baseline without emitting; otherwise emit. This makes the existing Biotech growth owner authoritative and enforces no duplicates.
- **Arrival anniversary:** arrival tick = pawn's arrival-description event (hot) else `archive.FirstArrivalTickForPawn`; `years = (nowAbs - arrivalAbs) / GenDate.TicksPerYear`. XML owns milestone years `[1,2,3,5,10]` plus `arrivalAnniversaryRecurringIntervalYears=5` after year 10. Advance `lastArrivalAnniversaryYear` for every evaluated year; emit only when the new whole year is a configured milestone. Context `anniversary_year=`.
- **Bonded-death discovery/retention:** query an indexed hot + archive accessor only for death events newer than `lastBondedDeathDiscoveryTick`; victim ID comes from the verified saved gameContext key. Admit only a direct relation/bond. Retain strongest bonds first (spouse/lover, parent/child, bonded animal), then the most recent deaths, stable victim-ID tie-break. Keep at most `bondedDeathMemoryCap=16`; advance the discovery cursor after every scan so evicted rows stay forgotten.
- **Bonded-death recall:** for each retained memory whose next whole anniversary year is due, compute an independent deterministic hash sample from pawn ID + victim ID + anniversary year. Years 1–3 are guaranteed; year 4 chance = 0.60; each later year multiplies by 0.65; floor = 0.05. All values are XML-tunable. Mark the anniversary year processed even when the sample fails, so save/load cannot reroll it.
- **Coincident bonded deaths:** emit at most one bonded-death anniversary page per pawn/day. Combine up to three sampled memories in strongest-bond order; mark every evaluated memory processed, including qualifying memories beyond the display cap.
- **Records:** XML `recordMilestones` in `DiaryTuningDef`; Def lookup uses cached `DefDatabase<RecordDef>.GetNamedSilentFail`. Defaults verified against base RimWorld 1.6:
  - `Kills`: `[10,25,50,100]`;
  - `MealsCooked`: `[100,500,1000,5000]`;
  - `ThingsConstructed`: `[100,500,1000,5000]`.
  Emit only the highest newly crossed threshold per record per scan. Compare against and update the monotonic high-water mark so a modded record reset cannot produce the same milestone twice.
- **Emission:** existing `DispatchProgression` with `ProgressionEventData` kinds `birthday` / `arrival_anniversary` / `death_anniversary` / `record`, unique dedup keys (`kind|subject|year`, combined death identity/year, or `record|defName|threshold`). Groups: 4 `matchDefNames` rows in `DiaryInteractionGroupDefs.xml` (birthday/arrival/record: white cue, important=false; bonded death: white cue, important=true, somber tone). `DiaryEventPromptDef` rows EN+RU. Keyed labels/texts EN+RU.
- **Pure:** `Source/Pipeline/AnniversaryPolicy.cs` — `YearsBetween`, arrival milestone matching, monotonic high-water/threshold crossing, bond retention ordering, deterministic recall chance/sample, coincident-death aggregation, and emit-decision helpers. Tests: boundary years; old-save baseline silence; direct birthday-owner suppression; arrival years 1/2/3/5/10/15 and non-milestones; death chance schedule/floor and no-reroll processing; retention/eviction ordering; aggregation cap; record single/multi-crossings and value reset. RimTest: see §12.

## 9. Phase 7 — B6 digest routing + soft cap

**Part 1 — digest lines:**
- `DiaryGameComponent.DaySummary.cs`: `pendingDayDigest` (`Dictionary<string dayKey, List<DayDigestRecord>>`, persistence mirrors the verified `pendingDayHediffs` pattern). A record stores tick, source-kind token, and one cleaned line.
- `DiarySignal`: `public virtual string BuildDigestLine() => null;` plus a stable source-kind token — implement compact one-liners for `InteractionSignal`, `ThoughtSignal`, `WorkSignal` (reuse their text builders, single line, cleaned). A pair-cap route supplies the appropriate POV line to each pawn's buffer.
- Buffer policy is pure and deterministic: exact-line deduplication, at most `dayDigestMaxLines=4` unique rows per pawn/day (XML), newest rows replace the oldest when full.
- `CollectDigestSignals(pawnId, day, candidates)`: each stored line → candidate, kind `digest` (new const), weight `daySummaryWeightDigest` (XML, 0.25), always `important=false`. Digest evidence cannot make `DayReflectionEventData.Decide` generate a page by itself.
- Make highlight priority explicit: select important candidates before any non-important pool; only unused slots may select news/filler/digest by their existing weights. Digest content never displaces an important highlight and is allowed to disappear when no important story justifies a reflection.
- Clear the digest rows in the same day flush/consume path as filler + hediff state, including the no-reflection path, so stale low-value evidence cannot leak into another day.

**Part 2 — soft cap:**
- `DiaryTuningDef.lowSalienceDailySoftCap` (default 2; 0 = disabled).
- `DiarySignal`: `public virtual bool IsLowSalience => false;` — true from interaction/thought/work/ambient signals whose classified group is `!important && !combat` (compute in the signal, it owns its group).
- Persist `lowSalienceDailyCounts` as a bounded pawn/day map beside `pendingDayDigest`; missing old-save rows normalize to zero and prior-day rows are discarded during rollover/finalization. Reloading mid-day must not reset pacing.
- **Solo/fanout child:** after a non-Drop decision, if `IsLowSalience` and the pawn is at cap, build/stash the digest line, run normal dedup marking, and return handled without a page. Otherwise emit; increment the persisted count only after successful low-salience emission.
- **Shared pair event:** inspect both POV counts before emission. If either eligible POV is below cap, emit the normal shared pair page and increment both counts (the already-capped side may exceed this deliberately soft limit). Only when **both** POVs are at cap does the signal suppress the shared event and add the appropriate line to both digest buffers. Never create a half-visible pair event.
- **Tests:** pure `DigestPacingPolicy` covers under/at/over cap, important bypass, persisted reload state, old-save empty normalization, day rollover, four-line newest/dedup buffer behavior, digest never satisfying the important gate, important-first highlight selection, and asymmetric/both-capped pair decisions. RimTest: see §12.

## 10. Done checklist (every feature/internal release)

- [ ] MSBuild Debug green; `1.6/Assemblies/PawnDiary.dll` rebuilt + staged.
- [ ] Pure tests added/updated and run (report pass counts).
- [ ] RimTest rows added for new behavior (§12); suite run, pass counts reported.
- [ ] XML parsed for every touched Def; append-only template rule honored.
- [ ] EN + RU Keyed/DefInjected parity for new strings.
- [ ] `repowiki/README.md` section(s) updated; dated `CHANGELOG.md` entry.
- [ ] No-DLC profile: feature inert, no errors.
- [ ] Old-save load: new fields normalize empty; no retroactive pages.
- [ ] Commit contains only this verified feature (or the shared Phase-2 contract migration), including its rebuilt DLL when C# changed; no public tag/version bump.

There is no additional formal `1.0` gate in this plan. After every internal release passes this checklist, the user decides when the combined work is ready for the single public `1.0` release.

## 11. Cross-cutting risks

- **Verify-before-patch list:** `LoadedLanguage.FriendlyNameNative`, `CompArt.InitializeArt` signature + `taleRef`/`Title`, `Tale.Concerns`, `Battle`/`Find.BattleLog` API, `IArchivable` tick member (read `ThreatLetterIsStale` first), `RecordDef` default names.
- **Sampling discipline:** no new Quality Wave code calls `Rand.Chance`. B2, H2 recall, and H6 use pure deterministic hash samples from stable/saved identities, so repeated callbacks and reloads cannot reroll and the mod does not perturb gameplay RNG. Existing unrelated `Rand.Chance`/`Rand.Value` uses are out of scope.
- **Save schema:** all new fields additive; normalization treats absence as empty; never repurpose existing keys.
- **Exact ownership:** H3 direct-story suppression, H2 birthday-owner suppression, and H6 once-per-tale ownership query both hot and archived history using stable tokens/categories. Never use translated prose equality as an ownership key.
- **Delayed truth:** H1 persists `battle_beats_checked=true` and uses retry/quiet/deadline tuning; an unscribed session flag is insufficient because load would restart mining.
- **Batch truth:** B2 stores event-time mood candidates while interaction/ambient batches accumulate. Flush-time live state is not historical evidence.
- **Performance:** all scans cadence-gated and bounded (scan-back caps, indexed hot/archive access, newest-first early-out); no per-tick pawn scans. H1 retry intervals avoid scanning the battle log every tick.

## 12. Testing strategy

Every feature ships three layers of proof, cheapest first:

1. **Pure tests** (`tests/DiaryPipelineTests`, `tests/DiaryCapturePolicyTests`, or a new pure
   project) — mandatory for any new pure policy/selector/formatter. Must compile without
   RimWorld/Verse/Unity. Run with `dotnet run --project tests/<project>` after each phase.
2. **RimTest in-game tests** (`tests/PawnDiary.RimTest/`) — **add everywhere possible**. Two
   established shapes; prefer the one that needs no live LLM:
   - **Flow tests** (`PawnDiary*FlowTests.cs`): drive a real gameplay path (spawn pawns, fire the
     event, advance ticks) and assert the saved DiaryEvent shape: defName, gameContext markers,
     frozen slot fields, dedup behavior, enabled/disabled gating.
   - **Fixture tests** (`PawnDiary*FixtureTests.cs`): assert loaded-game state — save/load
     round-trips of new fields, normalization of old saves (new fields empty), template/Def
     wiring, prompt field projection.
   Register new rows in `design/TEST_COVERAGE_PLAN.md` per its matrix convention. The suite builds via
   the LoadFolders RimTest gate; report pass counts in the phase changelog entry.
3. **Hands-on rows** — only for what automation cannot see (visual tint checks, LLM prose
   quality). Append explicit rows to `tests/SAVE_COMPATIBILITY_SMOKETEST.md`; never claim them
   as covered.

RimTest additions per feature (minimum set; extend when a seam allows more):

- **B1**: fixture — active-language directive reaches the built plan's system prompt for English and
  non-English languages; missing language/toggle off → absent; directive appears exactly once; Title
  request carries it.
- **C4**: fixture — `PageTintForCue`/`HeaderRuleForCue` resolve per-cue specs from the loaded
  `DiaryUiStyleDef`; unknown cue falls back; XML-absent fallback list keeps legacy 3-cue values.
- **A5**: flow — pair interaction freezes non-empty `identitySummary` on both POV slots; solo
  event leaves it empty; single-colonist edge → empty field; prompt render includes the
  "key relationships" field. Fixture — one named relation plus one emotionally intense friend/enemy
  are selected deterministically; output uses localized sentiment and contains no opinion number;
  save/load round-trip of the new slot field; old-save normalization empty.
- **B2**: flow — internal-state event (thought) freezes `moodSnapshot` under deterministic
  forced-favorable policy and omits it under 0.0; POV roles sample independently; a batched event uses
  its most extreme contributing event-time snapshot rather than flush-time mood; ordinary social
  event does not sample. Fixture — slot round-trip; stable hash repeatability; marker-eligibility
  matches TemplateKeyFor routing for all six markers.
- **H3**: flow — day reflection emitted after an allowed post-arrival letter contains the news
  candidate tag; pre-arrival/expired letters are absent; a direct same-category/day hot or archived
  event suppresses the news cue while a different category remains eligible.
- **H5 prompt**: flow — quadrum reflection with a same-quadrum-last-year important entry
  includes the `memory`-kind candidate from hot or archived history without duplicates; year < 4
  quadrums → absent.
- **H5 UI**: fixture — divider placement helper: entry tick on current day-of-year in a past
  year matches; current year and dev-mock undated entries never match.
- **H1**: flow — immediate raid capture does not queue before combat; a real POV fight waits through
  the quiet interval, then event gameContext gains the strongest `battle_beats=` plus
  `battle_beats_checked=true`; active combat at the hard deadline uses the best available evidence;
  peaceful raid reaches the deadline with only the checked marker and no error. Save/load during the
  wait does not restart or bypass ownership. (If the
  loaded Battle API proves untestable in harness, degrade to fixture-level matcher tests with a
  synthetic setup + hands-on row.)
- **H6**: flow — completing a sculpture whose tale concerns a colonist emits ≤1
  `PawnDiary_ArtImmortalized` event under deterministic chance 1.0; repeated initialization and later
  sculptures for the same stable tale never double-emit; deterministic dominant/lowest-ID ownership
  is stable; art about a deceased diarist can use the eligible sculptor fallback; unrelated trader
  art and chance 0.0 stay silent.
- **H2**: flow per sub-feature — birthday emits on age increase unless the exact Biotech growth owner
  already exists; arrival emits at years 1/2/3/5/10/15 but not 4/6; bonded-death years 1–3 are
  guaranteed, later years follow deterministic decay, and coincident qualifying deaths produce one
  page with ≤3 names; record milestone emits on high-water crossing and does not repeat after a value
  reset. Baseline first scan emits nothing. Fixture — hot/archive death discovery, 16-memory
  retention/eviction cursor, processed-year no-reroll state, record high-water, and old-save round-trip.
- **B6**: flow — under cap: low-salience pages emit normally; persisted counts survive reload; both-
  capped pair routes to both digests while an asymmetric pair still emits for both; important events
  always emit and fill reflection slots before digest; a digest-only day creates no reflection; buffer
  retains four unique newest lines; day rollover resets. Fixture — digest/count stores round-trip,
  flush consumption never double-feeds, and old-save fields normalize empty.

Rules for RimTest authors:
- Never call a live LLM: tests assert saved payloads/prompt fields, not generated prose.
- Force randomness through dev tuning overrides (chance 1.0/0.0), never by seeding Verse.Rand.
- Clean up spawned pawns/sculptures; fixtures must not write into a developer's real colonists
  (reuse the existing `PawnDiaryRimTestScope` pattern).
- New fields must round-trip through Scribe in a fixture before a phase is called done.
