# Pawn Diary — Quality Wave Implementation Plan

Status: APPROVED 2026-07-22 · Owner: coding agent · Scope: 10 features (A5, B1, B2, B6, C4, H1, H2, H3, H5, H6)

Companion docs: `AGENTS.md` (rules), `DOCUMENTATION.md` (architecture), `skills/pawndiary-engineering/SKILL.md` (workflow).

## How to use this document

- Implement **phase by phase, in order**. Each phase ends with the Done checklist (§10). Do not start the next phase until the current one builds, tests green, docs updated.
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

## 1. Global rules for every change

- **Architecture barrier:** impure capture/freeze (signals, `DiaryGameComponent.*`, `DiaryContextBuilder`) → plain DTO/save strings → pure policy (`Source/Pipeline/**`, no Verse/Rand/DefDatabase/Translate) → impure transport/UI. New cross-layer data = extend the typed contract (`PromptValues`, `DiaryPovPayload`, `PovSlot`), never pass live `Pawn`/`Def` into pure code.
- **Template XML is append-only.** Existing `fields` row order in `1.6/Defs/DiaryPromptTemplateDefs.xml` is frozen (DefInjected labels are indexed `fields.N.label`). New fields go at the END of each template's list.
- **Localization:** every new player-facing/prompt string lands in `Languages/English/Keyed/PawnDiary.xml` AND `Languages/Russian (Русский)/Keyed/PawnDiary.xml`; Def text via DefInjected in both languages. Structured schema tokens (`key=` in gameContext, `mood=`, `none`/`n/a`) stay English (carve-out, DOCUMENTATION.md §12). `.Translate()` main-thread only.
- **Tunables in XML** (`DiaryTuningDef.xml` or the feature's Def) with code fallbacks.
- **DLC-safety:** string defNames + `GetNamedSilentFail` only; no DLC types referenced.
- **Save schema:** new saved fields are additive with normalization that treats missing as empty/zero; old saves must load unchanged.
- **After EACH phase:** `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`; stage rebuilt `1.6/Assemblies/PawnDiary.dll`; run touched pure tests (`dotnet run --project tests/DiaryPipelineTests/...` etc.); XML-parse edited Defs; update `DOCUMENTATION.md` + dated `CHANGELOG.md` line.

## 2. Phase map

| Phase | Features | Touches |
|---|---|---|
| 1 | B1, C4 | prompt pipeline contracts; UI style Def |
| 2 | A5, B2 | one save-schema batch (`PovSlot`), context builder, templates |
| 3 | H3, H5-prompt | day/quadrum reflection collectors |
| 4 | H5-UI | diary tab divider |
| 5 | H1, H6 | battle beats; art patch |
| 6 | H2 | anniversary/records scanner |
| 7 | B6 | digest routing + soft cap |

---

## 3. Phase 1

### 3.1 B1 — Output-language directive

**Goal:** every LLM request ends its system prompt with one localized line naming the output language; remove model language-guessing (Russian build failure mode).

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
if (lang == null || lang.LegacyFolderName == "English") return "";
name = lang.FriendlyNameNative;  // VERIFY member name on LoadedLanguage in 1.6; fallback FriendlyNameEnglish
return "PawnDiary.Prompt.OutputLanguage".Translate(name).Resolve();
```
Prompts are built on the main thread (`BuildPromptRequest` from the generation queue path + prompt preview) — confirm no background path builds them; the directive must never be resolved inside `LlmClient`.

**Russian labels:** extend `Languages/Russian (Русский)/DefInjected/PawnDiary.DiaryPromptTemplateDef/DiaryPromptTemplateDefs.xml` with `fields.N.label` rows for every field appended in phases 2/5 (indices = final positions after appends). Do NOT translate `key=` schema tokens.

**Tests (DiaryPipelineTests):** directive appended once when supplied; empty directive → unchanged system prompt; Title request also carries it. RimTest: see §12.

### 3.2 C4 — Per-cue page tints + header rules

**Goal:** replace the 3-cue special-casing with per-cue XML color specs for all ~15 cues.

**Files:** `Source/Defs/DiaryUiStyleDef.cs`, `1.6/Defs/DiaryUiStyleDef.xml`.

**Code:**
- `DiaryUiCueColor` gains `public DiaryUiColorSpec pageTint;` and `public DiaryUiColorSpec headerRule;` (null = inherit default).
- In the C# fallback `cueColors` initializer, attach the existing combat/socialFight/mentalBreak tint+rule values as `pageTint`/`headerRule` on those three entries (values = today's `combatPageTintColor` etc., so no-XML behavior is byte-identical).
- `PageTintForCue(cue)`: find cue in `cueColors`; if `entry.pageTint != null` → `entry.pageTint.ToColor(PageTintColor)`; else `PageTintColor`. Same shape for `HeaderRuleForCue`. Delete the hardcoded if-chains. Keep `combatPageTintColor` etc. fields for save/XML compat (now used only as the seeded values).

**XML (`DiaryUiStyleDef.xml` `cueColors`):** one row per cue with accent + pageTint + headerRule. Proposed (tune visually, keep tint α 0.05–0.12, rule α 0.35–0.65):
- combat: tint ember red (0.70,0.10,0.07,0.18), rule (0.95,0.18,0.12,0.65) [existing]
- socialFight: orange [existing]; mentalBreak: muted green [existing]
- danger: tint (0.55,0.08,0.05,0.14), rule (0.80,0.15,0.10,0.55)
- extremeDark: tint (0.25,0.02,0.05,0.22), rule (0.45,0.05,0.10,0.60)
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

## 4. Phase 2 — A5 + B2 (one save-schema batch)

Both add additive `PovSlot` string fields — do them in one change so normalization/scribe changes land once.

**Shared payload chain (per field `X`):**
1. `Source/Models/DiaryEvent.cs` — `PovSlot.X` field; scribe alongside `memoryContext` (same struct field pattern); `SetX(role, value)` + `XForRole(role)`; save-normalization: null → empty, cap length (600 chars, like memoryContext).
2. `Source/Pipeline/DiaryPipelineContracts.cs` — `DiaryPovPayload.X`.
3. `Source/Generation/DiaryPipelineAdapters.cs` — `PovFor` copies it.
4. `Source/Generation/PromptAssembler.cs` — `PromptValues.X` + `ResolveSource` token.
5. `1.6/Defs/DiaryPromptTemplateDefs.xml` — append `<li><label>…</label><source>…</source></li>` at END of listed templates; RU DefInjected `fields.N.label` rows.

### 4.1 A5 — Identity block

- **Pure:** new `Source/Pipeline/IdentitySummaryPolicy.cs`:
  - `IdentityRow { string name; string relationLabel; int opinion; bool hasRelation; }`
  - `Select(IReadOnlyList<IdentityRow> rows, int max)` → order: hasRelation desc, |opinion| desc, name ordinal (OrdinalIgnoreCase); take `max`; format each `name (relationLabel, +N)` or `name (+N)`; join `"; "`; prefix `closest=`. Returns "" for empty.
- **Impure:** `DiaryContextBuilder.BuildIdentitySummary(Pawn pov, Pawn partner)`: iterate free colonists (exclude pov + partner); opinion via the fail-soft `DiaryGameComponent.TryReadOpinion`; relation label via `PawnRelationUtility.GetMostImportantRelation` → `GetGenderSpecificLabelCap` → `ExternalText`. Feed `IdentitySummaryPolicy.Select` with `DiaryTuning.Current.identitySummaryMaxRelations` (XML, default 2).
- **Freeze:** `AddPairwiseEventCore` only (recipient != null), per POV, next to continuity. Birth-captured path: leave empty (no captured equivalent).
- **Templates:** PairDefault, PairBatched, PairImportant — `<li><label>closest bonds</label><source>Identity</source></li>`.
- **Tests:** policy ordering (relation beats raw opinion), cap, deterministic tie-break, formatting, empty input. RimTest: see §12.

### 4.2 B2 — Mood snapshot

- **Pure:** new `Source/Pipeline/MoodSnapshotPolicy.cs`:
  - `BandChance(int moodPercent, IReadOnlyList<MoodSnapshotChanceRule> rules, float fallback)` — rules ordered by `maxPercent` asc; first band with `moodPercent <= maxPercent` wins.
  - `ShouldInclude(float roll, float applyChance, float bandChance)` → `roll < applyChance * bandChance`.
  - `IsEligibleContext(string gameContext)` — `DiaryContextFields.HasMarker` for `mood_event=`, `thought=`, `inspiration=`, `work=`, `hediff=`, `batch=` (mirror `DiaryPromptPlanner.TemplateKeyFor`; both call this one helper to prevent drift).
- **XML (`DiaryTuningDef`):** `moodSnapshotApplyChance` (default 0.6); `moodSnapshotChances` list of `{ maxPercent, chance }`: 15→0.95, 35→0.5, 65→0.12, 85→0.3, 101→0.55.
- **Impure:** `DiaryContextBuilder.BuildMoodSnapshot(Pawn)` → `mood=<BuildMoodSummary>` + `"; thoughts=<first top thought>"` when present.
- **Freeze:** in `AddSoloEventCore`/`AddPairwiseEventCore`: if `IsEligibleContext(gameContext)` → roll `Rand.Value` (consumed only for eligible events; note RNG ordering in comment) → freeze snapshot or empty. Not eligible → empty (no RNG consumed).
- **Templates:** SoloInternalState, SoloBatched, PairBatched — `<li><label>you</label><source>MoodSnapshot</source></li>`.
- **Tests:** band boundaries (0/15/35/65/85/100), product roll, marker eligibility set, slim format. RimTest: see §12.

---

## 5. Phase 3 — H3 + H5-prompt

### 5.1 H3 — Colony news in reflections

- `Source/Defs/PromptArchitectureDefs.cs` — add `DiaryContextReactions` key const `ColonyNews = "colony_news"` (verify the key plumbing: `ForKey`/`ScanBack`/`TimeoutTicks`/`LetterDefAllowed` are generic).
- `1.6/Defs/DiaryContextReactionDefs.xml` — row: enabled, scanBack ~40, timeoutTicks = day window, `requireHomeMap` false, allowed letter defs list (threat/quest/neutral-positive defs; copy the threat-letter list shape).
- `Source/Core/DiaryGameComponent.DaySummary.cs` — `CollectNewsSignals(pawn, dayStartTick, candidates)`: scan `Find.Archive.ArchivablesListForReading` newest-first (bounded); reuse the staleness/tick logic from `ThreatLetterIsStale` (READ that helper first for the exact archivable tick member); newest allowed `Letter` inside [dayStart, now] → one candidate: line `"PawnDiary.Event.DayReflectionNews".Translate(SafeArchivedLabel(letter))`, kind `DayReflectionEventData.SignalKindNews` (new const "news"), weight `tuning.daySummaryWeightNews` (XML, 0.3), not important by default (still XML-overridable via `daySummaryImportantSignalKinds`).
- Quadrum: same helper over [quadrumStart, evidenceEnd] → one `QuadrumReflectionSignal`.
- Keyed EN `"the colony was told: {0}"` / RU equivalent.
- **Tests:** capture-policy format test for the new signal kind tag (DiaryCapturePolicyTests pattern); manual in-game check. RimTest: see §12.

### 5.2 H5 prompt — last-year same-quadrum snippet

- `DiaryTuningDef`: `onThisDayQuadrumCallbackEnabled` (default true).
- `DaySummary.cs` `TryFlushQuadrumReflectionForPawn`: after current-quadrum collection, if enabled && `quadrum >= 4`: `CollectQuadrumReflectionSignals(pawnId, QuadrumStartDay(quadrum-4), QuadrumStartDay(quadrum-4) + daysPerQuadrum - 1, lastYear)`; take max-weight line; add candidate with weight `daySummaryWeightMajorEvent * 0.5f`, tag kind `memory` (new kind const), line wrapped `"PawnDiary.Event.QuadrumReflectionLastYear".Translate(line)` = "a year ago, same season: {0}".
- Known limitation (document): scans hot events only; last-year evidence archived away is silently absent. Optional follow-up: archive scan via `archive.EntriesForPawn`.
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
  - `TryBuildBeats(Pawn pov, string raiderFactionDefName, int raidTick, DiaryTuningDef tuning, out string beats)`.
  - Scan `Find.BattleLog.Battles` newest-first, max `battleBeatsScanBackBattles` (8): candidate = battle with `creationTick >= raidTick - 600 && <= now` whose entries include a pawn of faction def == raiderFactionDefName and any player colonist. (VERIFY 1.6 `Battle` API: entries list + `GetConcernFactions()`; fall back to scanning entries' pawn factions.)
  - From the matched battle: entries where `initiator` or `recipient` is `pov`; score kill/down > hit > near-miss/other; take top `battleBeatsMaxCount` (2) in chronological order; `entry.ToGameStringFromPOV(pov)` inside per-entry try/catch; `DiaryLineCleaner.CleanLine`; join `" | "`; total cap ~240 chars.
- Hook: `Source/Core/DiaryGameComponent.Generation.cs` `EnsureGenerationQueued` — before queueing, if `DiaryContextFields.HasMarker(gameContext, "raid=")` (VERIFY actual leading marker in `RaidEventData.BuildGameContext`) and no `battle_beats=` yet and event not already mined this session (session `HashSet<string>` unscribed): build beats from the saved faction defName in gameContext + `livePawnsById[initiatorPawnId]`; append `"; battle_beats=" + beats` to the saved gameContext (only when beats found).
- Tuning XML: `battleBeatsEnabled=true`, `battleBeatsMaxCount=2`, `battleBeatsScanBackBattles=8`, `battleBeatsMaxAgeTicks=30000`.
- Templates: append `<li><label>combat beats</label><source>GameContext</source><contextKey>battle_beats</contextKey></li>` to SoloImportant + PairCombat; RU labels.
- Acceptance: after a raid delay matures, generated raid pages reference a real hit/miss; pruned/absent log → field omitted, no errors. RimTest: see §12.

### 7.2 H6 — Art immortalization

- VERIFY `CompArt.InitializeArt` 1.6 signature (expected `public void InitializeArt(ArtGenerationContext context)`) and that `CompArt.taleRef`/`Title` members exist as expected.
- New `Source/Patches/DiaryArtPatches.cs` — postfix on `InitializeArt`; via `DiaryPatchSafety.Run`; direct `[HarmonyPatch]` if the method is public/stable, else `DiaryPatchRegistrar`.
- Logic: `__instance.taleRef?.Tale` non-null; find first eligible free colonist with `tale.Concerns(pawn)` (VERIFY `Tale.Concerns(Thing)` in 1.6; fallback: compare dominantPawn/args); roll `Rand.Chance(DiaryTuning.Current.artImmortalizedChance)` (XML, 0.5); submit new `ArtImmortalizedSignal(pawn, sculpture, tale)`.
- New `Source/Ingestion/Sources/ArtImmortalizedSignal.cs` + minimal `Capture/Events/ArtImmortalizedEventData.cs` + spec (new `DiaryEventType.ArtImmortalized`; mirror `MoodEventData`/`MoodEventSpec` shape). Dedup key `"art|" + sculpture.ThingID`, long window. gameContext: `art=<thingDefName>; label=<clean title>; sculptor=<name>; tale=<taleDefName>`.
- XML: interaction group `artImmortalized` (GroupDomain.Interaction, matchDefNames `PawnDiary_ArtImmortalized`, important=false, colorCue white); `DiaryEventPromptDef` row (prompt+enhancement); Keyed label `"immortalized in art"` + text `"{0}'s deed was immortalized in the sculpture \"{1}\"."`; RU mirrors.
- Acceptance: crafting a sculpture about a colonist's tale yields ≤1 quiet page at 50% chance; trader art with unrelated tales → nothing. RimTest: see §12.

## 8. Phase 6 — H2 anniversaries & records

- New `Source/Core/DiaryGameComponent.Anniversaries.cs`: `ScanAnniversariesForDiaryEvents()` — gated in `GameComponentTickInner` by elapsed-time `nextAnniversaryScanTick` (interval XML `anniversaryScanIntervalTicks`, default 15000; scheduling per lore: elapsed time, never modulo).
- **State (additive on `PawnProgressionState`, normalization-safe):** `lastObservedBiologicalAgeYears` (int, -1 = unset), `lastArrivalAnniversaryYear` (int), `bondedDeathAnniversaryYears` (`Dictionary<string,int>` victimId→year, cap 16), `recordMilestoneValues` (`Dictionary<string,float>` defName→last value).
- **Baseline rule:** first scan per pawn stores values, emits nothing (same as skill milestones).
- **Birthday:** `pawn.ageTracker.AgeBiologicalYears > lastObserved` → emit; context reuses existing `birthday_age` template key.
- **Arrival anniversary:** arrival tick = pawn's arrival-description event (hot) else `archive.FirstArrivalTickForPawn`; `years = (nowAbs - arrivalAbs) / GenDate.TicksPerYear`; `years >= 1 && years > lastArrivalAnniversaryYear` → emit; context `anniversary_year=`.
- **Bonded-death anniversary:** bounded newest-first scan of `events.AllEvents` (cap ~400) for Death-domain events; victim id from saved gameContext (VERIFY key, e.g. `death_victim_id=`); victim in `pawn.relations.DirectRelations` (dead relations persist) or bond relation; whole years since death > stored → emit with deceased name/relation label/years.
- **Records:** XML `recordMilestones` in `DiaryTuningDef`: rows `{ record = "MealsCooked"; milestones = [100,500,1000,5000] }` (defaults: MealsCooked, Kills, plus one more verified 1.6 RecordDef). Def lookup `DefDatabase<RecordDef>.GetNamedSilentFail` (cached). Emit only the highest newly crossed threshold per record per scan; store current value.
- **Emission:** existing `DispatchProgression` with `ProgressionEventData` kinds `birthday` / `arrival_anniversary` / `death_anniversary` / `record`, unique dedup keys (`kind|subject|year` or `record|defName|threshold`). Groups: 4 `matchDefNames` rows in `DiaryInteractionGroupDefs.xml` (birthday/arrival/record: white cue, important=false; bonded death: white cue, important=true, somber tone). `DiaryEventPromptDef` rows EN+RU. Keyed labels/texts EN+RU.
- **Pure:** `Source/Pipeline/AnniversaryPolicy.cs` — `YearsBetween(long a, long b, long ticksPerYear)`, `CrossedThresholds(float previous, float current, IReadOnlyList<float> sorted)`, emit-decision helpers. Tests: boundary years, baseline silence, threshold crossings (single/multi), map caps. RimTest: see §12.

## 9. Phase 7 — B6 digest routing + soft cap

**Part 1 — digest lines:**
- `DiaryGameComponent.DaySummary.cs`: `pendingDayDigest` (`Dictionary<string dayKey, List<string>>`, persistence mirrors `pendingDayHediffs` — check whether that is scribed and match it).
- `DiarySignal`: `public virtual string BuildDigestLine() => null;` — implement compact one-liners for `InteractionSignal`, `ThoughtSignal`, `WorkSignal` (reuse their text builders, single line, cleaned).
- `CollectDigestSignals(pawnId, day, candidates)`: each stored line → candidate, kind `digest` (new const), weight `daySummaryWeightDigest` (XML, 0.25). Cleared by the day flush (extend `ConsumePawnDayFiller`).

**Part 2 — soft cap:**
- `DiaryTuningDef.lowSalienceDailySoftCap` (default 2; 0 = disabled).
- `DiarySignal`: `public virtual bool IsLowSalience => false;` — true from interaction/thought/work/ambient signals whose classified group is `!important && !combat` (compute in the signal, it owns its group).
- `Dispatch` (both solo and fanout-child paths), after non-Drop decision: if `IsLowSalience` && cap enabled && `lowSalienceCount(pawnId|day) >= cap` → `line = signal.BuildDigestLine()`; if line non-null → stash to `pendingDayDigest`, run normal dedup marking, return true (counts as handled, no page). Else ordinary `Emit`; on successful emit of an `IsLowSalience` signal, increment the counter. Counter: session `Dictionary<string,int>` keyed `pawnId|day` (unscribed by design; document).
- **Tests:** pure cap arithmetic helper (`DigestPacingPolicy`): under/at/over cap, important bypass, day rollover. RimTest: see §12.

## 10. Done checklist (every phase)

- [ ] MSBuild Debug green; `1.6/Assemblies/PawnDiary.dll` rebuilt + staged.
- [ ] Pure tests added/updated and run (report pass counts).
- [ ] RimTest rows added for new behavior (§12); suite run, pass counts reported.
- [ ] XML parsed for every touched Def; append-only template rule honored.
- [ ] EN + RU Keyed/DefInjected parity for new strings.
- [ ] `DOCUMENTATION.md` section(s) updated; dated `CHANGELOG.md` entry.
- [ ] No-DLC profile: feature inert, no errors.
- [ ] Old-save load: new fields normalize empty; no retroactive pages.

## 11. Cross-cutting risks

- **Verify-before-patch list:** `LoadedLanguage.FriendlyNameNative`, `CompArt.InitializeArt` signature + `taleRef`/`Title`, `Tale.Concerns`, `Battle`/`Find.BattleLog` API, `IArchivable` tick member (read `ThreatLetterIsStale` first), `RecordDef` default names.
- **RNG discipline:** new `Rand` draws only on eligible paths, after dedup (Ability pattern); seeded picks via `PromptVariants.HashSeed` where determinism across reload is required.
- **Save schema:** all new fields additive; normalization treats absence as empty; never repurpose existing keys.
- **Performance:** all scans cadence-gated and bounded (scan-back caps, newest-first early-out); no per-tick pawn scans.

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
   Register new rows in `TEST_COVERAGE_PLAN.md` per its matrix convention. The suite builds via
   the LoadFolders RimTest gate; report pass counts in the phase changelog entry.
3. **Hands-on rows** — only for what automation cannot see (visual tint checks, LLM prose
   quality). Append explicit rows to `tests/SAVE_COMPATIBILITY_SMOKETEST.md`; never claim them
   as covered.

RimTest additions per feature (minimum set; extend when a seam allows more):

- **B1**: fixture — active-language directive reaches the built plan's system prompt; English
  language → absent; toggle off → absent; Title request carries it.
- **C4**: fixture — `PageTintForCue`/`HeaderRuleForCue` resolve per-cue specs from the loaded
  `DiaryUiStyleDef`; unknown cue falls back; XML-absent fallback list keeps legacy 3-cue values.
- **A5**: flow — pair interaction freezes non-empty `identitySummary` on both POV slots; solo
  event leaves it empty; single-colonist edge → empty field; prompt render includes the
  "closest bonds" field. Fixture — save/load round-trip of the new slot field; old-save
  normalization empty.
- **B2**: flow — internal-state event (thought) freezes `moodSnapshot` under forced-favorable
  roll (band chance 1.0 via dev tuning override) and omits it under 0.0; batched event same;
  ordinary social event never rolls. Fixture — slot round-trip; marker-eligibility matches
  TemplateKeyFor routing for all six markers.
- **H3**: flow — day reflection emitted after an allowed letter arrives contains the news
  candidate tag in gameContext signal tags; letter older than window → absent.
- **H5 prompt**: flow — quadrum reflection with a same-quadrum-last-year important entry
  includes the `memory`-kind candidate; year < 4 quadrums → absent.
- **H5 UI**: fixture — divider placement helper: entry tick on current day-of-year in a past
  year matches; current year and dev-mock undated entries never match.
- **H1**: flow — raid capture → delay matures → event gameContext gains `battle_beats=` after a
  real fight involving the POV pawn; peaceful raid (no battle) → no marker, no error. (If the
  loaded Battle API proves untestable in harness, degrade to fixture-level matcher tests with a
  synthetic setup + hands-on row.)
- **H6**: flow — completing a sculpture whose tale concerns a colonist emits ≤1
  `PawnDiary_ArtImmortalized` event (chance forced 1.0 via dev tuning override); second
  sculpture same ID never double-emits; chance 0.0 → silent.
- **H2**: flow per sub-feature — birthday emit on age increase (dev-age the pawn); arrival
  anniversary at +1 year (dev time-skip); bonded-death anniversary at +1 year with a relation
  death page present; record milestone on crossing a threshold (dev-set record value). Baseline
  first scan emits nothing. Fixture — progression-state round-trip of all four new state fields;
  old save baselines silently.
- **B6**: flow — under cap: low-salience pages emit normally; at cap: third low-salience capture
  routes to digest and the day reflection shows its line; important event always emits past the
  cap; day rollover resets. Fixture — digest store survives the reflection flush (consumed) and
  never double-feeds.

Rules for RimTest authors:
- Never call a live LLM: tests assert saved payloads/prompt fields, not generated prose.
- Force randomness through dev tuning overrides (chance 1.0/0.0), never by seeding Verse.Rand.
- Clean up spawned pawns/sculptures; fixtures must not write into a developer's real colonists
  (reuse the existing `PawnDiaryRimTestScope` pattern).
- New fields must round-trip through Scribe in a fixture before a phase is called done.
