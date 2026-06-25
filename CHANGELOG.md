# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state.

## 2026-06-25

- **Event store extracted from `DiaryGameComponent`.** The saved `diaryEvents` list and the O(1)
  id->event lookup index that mirrors it now live in a dedicated `Core/DiaryEventRepository.cs`,
  giving the event store one clear owner. The repository owns `FindEvent`, `Register`, `RemoveEvent`/
  `RemoveEvents`, `RebuildIndex`, `EnsureIndexReady`, a never-null `AllEvents` view, and the
  `"diaryEvents"` Scribe key (via `ExposeEvents`); `DiaryGameComponent` constructs it, delegates
  serialization from `ExposeData`, and rebuilds the index from its own `PostLoadInit`, so it stays the
  only RimWorld lifecycle/save owner. All partials now go through the repository (`events.FindEvent`,
  `events.Register`, `events.AllEvents`, `events.RemoveEvents`, `events.ContainsEvent`), replacing the
  old `diaryEvents`/`eventsById` field reads and the private `FindEvent`/`RegisterDiaryEvent`/
  `RebuildEventIndex` methods. The dead `if (diaryEvents == null)` guards (the store is now guaranteed
  non-null across new game, load, and `PostLoadInit`) were dropped. The Scribe key is unchanged, so
  existing saves load unchanged; behavior is identical. The Debug DLL was rebuilt; all five pure test
  projects pass (622 assertions).

- **`DiaryGameComponent.Generation.cs` split by pipeline stage.** The largest partial (~1200 lines,
  which mixed queue orchestration, lane selection, LLM dispatch, result application, and
  eligibility/rule resolution) is now four cohesive files plus a small relocation.
  `DiaryGameComponent.Generation.cs` keeps the queue orchestration (deciding what to (re)queue:
  `QueueAllPendingGenerations`, `QueuePendingGenerationsForPawn`, `EnsureGenerationQueued`, orphan
  recovery, raid-delay gating, death/arrival/pairwise/single routing). New
  `DiaryGameComponent.GenerationDispatch.cs` owns the `QueuePrompt` choke point, `ApplyLlmResult`,
  title follow-up, and prompt-test-mode detection (`TitleMaxTokens`/`PromptTestEndpointLabel` moved
  here). New `DiaryGameComponent.ApiLanes.cs` owns API lane selection, failover, and the English lane
  debug logging. New `DiaryGameComponent.GenerationEligibility.cs` owns the generation gates
  (`DiaryGenerationEnabledFor`, Consciousness-floor incapacitation skips), persona/enchantment/humor
  rule resolution, and the live-pawn snapshot/lookup helpers. Two helpers that only serve
  interaction recording (`InteractionInstruction`, `IsInteractionSignificant`) moved to their natural
  home in `DiaryGameComponent.Interactions.cs`. Pure move of `partial class` members — no call site,
  behavior, or save-data change. Behavior and save data are unchanged; the Debug DLL was rebuilt; all
  five pure test projects pass (622 assertions).

- **Persona and override state extracted from `PawnDiarySettings`.** The save DTO no longer owns
  writing-style catalog mutation or the per-key override-dictionary plumbing. Writing-style (persona)
  CRUD, normalization, and theme policy moved to a new `Settings/PersonaPresetStore.cs`
  (`PersonaPresetConfig` moved with it); the duplicated lookup/set/reset/normalize code behind the
  event-prompt and event-enhancement override maps collapsed into one reusable
  `Settings/PromptOverrideDictionary.cs`. `PawnDiarySettings` constructs one of each
  (`personaPresets`, `eventPromptOverrides`, `eventEnhancementOverrides`) and delegates save/load to
  them; each store serializes under its original Scribe key (`personaPresets`, `eventPromptOverrides`,
  `eventEnhancementOverrides`), so existing saves keep loading unchanged. Callers now go through the
  stores directly (e.g. `settings.personaPresets.OverrideFor(...)`,
  `settings.eventPromptOverrides.Effective(...)`); only the two operations spanning both event maps
  (`ResetAllEventPromptOverrides`, `CustomizedEventPromptCount`) stay on `PawnDiarySettings`. System-
  prompt overrides, connection, and generation settings are unchanged. Behavior and save data are
  unchanged; the Debug DLL was rebuilt; all five pure test projects pass (622 assertions).
- **`PawnDiaryMod` settings UI split.** The RimWorld `Mod` entry point now stays small while the
  settings window is split into focused partial files for the top-level layout, API lanes, Prompt
  Studio, Persona Studio, and shared widget helpers. The settings-window model fetch and connection
  test state moved into `ApiConnectionController`, which owns pending async results, stale-row
  matching, and cancellation while still applying translated status text and settings mutations on the
  main draw thread. Behavior and save data are unchanged; the Debug DLL was rebuilt.
- **`DiaryContextBuilder` split by concern.** The one static builder that mixed pure formatting,
  impure Pawn/Map collection, and GameCondition mood-impact policy is now separated so each concern
  has its own home and the pure piece is testable without the game. `DiaryContextBuilder` keeps only
  the impure context collectors (pawn profile, surroundings, continuity, opinions). Its pure
  one-line text cleaner (`CleanLine`) moved to a new Verse-free
  `Generation/DiaryLineCleaner.cs`, now covered by a focused pure test (tag stripping, newline
  collapsing, trimming, null/whitespace). Its localized mood/pain/opinion/age/beauty/bleed/effect
  band tokens moved to a new `Generation/DiaryBuckets.cs` (`.Translate()`-bound, so main-thread —
  not Verse-free). The per-pawn GameCondition mood-direction policy (`DetermineMoodImpact` +
  `GetMoodOffsetFromConditionThoughts` and their helpers) moved to a new
  `Generation/MoodImpactClassifier.cs`. `AgeBucket` now takes a plain `int years` (the caller
  snapshots `AgeBiologicalYears`), removing its `Pawn` dependency. All 89 `CleanLine` call sites
  across capture/core/generation/pipeline and the two mood-method call sites in
  `DiaryGameComponent.MoodEvents.cs` were updated. Behavior is byte-for-byte unchanged; no
  save-format, DLC, or localization change. The Debug DLL was rebuilt; all five pure test projects
  pass (622 assertions).
- **Hardcoded tunables moved to XML.** Three pockets of feature policy that lived in C# are now
  XML-owned with the original values as code fallbacks, so they retune without a recompile and
  modders/DLCs can extend them by string without code: (1) the first-person Consciousness gate
  (`DiaryTuningDef.minimumConsciousnessForFirstPersonGeneration`, default 0.11); (2) the 0..4
  display-staggering intensity thresholds for low Consciousness and intoxication severity; (3) the
  positive/negative mood-impact `GameCondition` defName families. Capture-time intoxication
  detection (`PawnFactCapture.IsIntoxicatingHediff`) no longer keeps its own keyword list — it
  reuses the same `Diary_TextDecorations` `StaggeredWordSizes` rule list as render-time decoration,
  via a new pure helper `DiaryTextDecorations.HediffMatchesStaggeredRules` (with a focused test for
  exact/substring/hidden/non-stagger cases). The decoration rule's label list gains `hangover` to
  preserve alcohol-withdrawal coverage. Behavior is unchanged for the shipped values; no save-format,
  DLC, or localization change.
- **Localized colony name moved off the saved model.** `DiaryEvent.NameForRole` — which reached
  `.Translate()` to render the neutral/colony POV name — is removed. Its sole caller, the adapter's
  pair direct-speech instruction builder (`DiaryPipelineAdapters.DirectSpeechInstructionFor`), now
  picks the saved initiator/recipient name itself and falls back to the localized
  `PawnDiary.Prompt.Colony` label for neutral, matching the colony-name handling already used
  elsewhere in the adapter. Direct-speech prompt text is byte-for-byte unchanged; the persisted
  model no longer touches localization, restoring the pure/impure boundary.
- **Prompt instruction resolution moved off the settings save DTO.** The `InstructionFor*` family
  (classify a Def/signal into its group, then roll one `instructions` variant at capture) no longer
  lives on `PawnDiarySettings` — it is now static on `InteractionGroups`, beside `Classify*`. These
  methods read no settings state (instructions are XML-only — no saved overrides remain), so the
  move restores the pure/impure barrier: the save DTO keeps persisted fields only, and classification
  + RNG sit with classification. All 14 capture call sites in `DiaryGameComponent.*.cs` now call
  `InteractionGroups.InstructionFor*(...)`. Behavior is unchanged: same classification, same
  capture-time roll frozen into `diaryEvent.instruction`. No save-format, DLC, or localization change.
- **Live-pawn fact capture extracted from the saved model.** `DiaryEvent` no longer reads live
  `Pawn` state. Its two capture methods (hediff/trait text-decoration facts and the 0..4
  staggered-handwriting intensity) became pure value setters (`SetTextDecorationFacts` /
  `SetStaggeredIntensity`); the live health/hediff/capacity/trait reads moved to a new guarded
  collector, `Generation/PawnFactCapture.cs`, alongside `DlcContext`. Event-record call sites now
  snapshot plain `int`/`string` values and hand them to the model. Restores the pure/impure barrier,
  keeps saved fields and Scribe keys identical (old saves load unchanged), and adds no DLC
  dependencies (base-game accessors only).
- **API lane identity centralized.** Gate/cooldown keys, failover duplicate checks, successful-lane
  pinning, settings fetch/test stale-result checks, and sanitized lane labels now go through shared
  pure helpers with tests for each equality mode.
- **Public release metadata cleaned.** Workshop-facing metadata and README copy now present the mod
  as a public release. The publish script reports the DLL that remains in the uploadable payload and
  preserves dependency package IDs while cleaning the mod's own published package ID.
- **Translation readiness tightened.** English DefInjected stubs now cover prompt final
  instructions, new quest/raid interaction groups, and interaction tone variant pools, so
  translators do not have to chase raw Def XML fallbacks for model-facing prose.
- **Hidden per-entry humor cues added.** Eligible first-person diary entries now occasionally carry
  one subtle structural writing cue appended to the system prompt — never a "be funny" instruction,
  always a single sentence-shape license (a flat understatement coda, a dry inventory, a clerical
  tally of loss). Flavor matches event stakes: **Light** (dry/absurdist) for mundane events and
  **Gallows** (dark/deadpan) for high-stakes events (important, Raid domain, or combat/social-fight/
  mental-break). The feature is always-on and completely invisible: no settings field, no UI toggle.
  The base rate (~10% of eligible entries) is XML-tunable via `DiaryTuningDef.humorChance`; cue text
  and weights live in `DiaryHumorCueDefs.xml`. Cues ride inside the persona voice block, so neutral
  death/arrival/title prompts stay humor-free automatically.
- **Raid generation timing retuned.** Ordinary raids now record at spawn but delay LLM generation by
  `raidGenerationDelayTicks`, so entries lean into warning, positioning, and fight anticipation
  instead of instant battle aftermath. Drop-pod raids and infestations bypass the delay, carry
  arrival/strategy context, and use dedicated prompt instructions.
- **Diary entry access can use the pawn tab again.** A new setting swaps the selected-pawn **Diary**
  command back to the normal pawn inspect tab. Command mode still uses a subtle underline plus
  writing dots; tab mode leaves the normal Diary tab plain, with no tab-strip status indicator.
  Opening that pawn's Diary acknowledges the finished-page marker without top-left messages.

## 2026-06-24

- **Native Ollama API mode removed.** API lanes now speak only OpenAI-compatible Chat Completions and
  OpenAI Responses; model fetch uses OpenAI-style `/models`, and the native Ollama thinking toggle
  was removed from settings. (Local Ollama still works fine over its OpenAI-compatible endpoint.)
- **Harmony declared as a mod dependency.** RimWorld 1.6 no longer ships `0Harmony.dll` in its
  `Managed` folder, so `About/About.xml` now declares the standalone Harmony mod
  (`brrainz.harmony`) under `<modDependencies>` and `<loadAfter>`. The bundled
  `1.6/Assemblies/0Harmony.dll` stays as a run-from-clone fallback; RimWorld's loader dedupes it.
- **API lanes hardened for free-tier pools.** Global routing mode (Balanced / Prefer top rows /
  Failover only), reorder arrows, per-row auth (Bearer, No auth, or editable custom API-key header
  defaulting to `x-goog-api-key`; legacy `api-key`/`x-api-key` rows auto-migrate), Chat-compatible
  Reasoning selector sending `reasoning_effort` (Gemini thinking models), and automatic per-lane
  cooldowns with exponential backoff (10s→300s) cleared by any success. Failover snapshots lane
  readiness once per request; settings-save prunes stale cooldowns; HTTP responses are byte-capped
  before parsing. Prompt-lab gained matching `--auth-mode`.
- **Pawn ability-use events added.** Successful `Ability.Activate` calls become cooldown-weighted
  solo entries for the caster (rare long-cooldown abilities kept more often), with ability name,
  category, target, and cooldown context.
- **Anomaly psychic ritual events added.** Captured from `LordToil_PsychicRitual.RitualCompleted`
  (not graph end) and fanned out to invoker/target/participants/spectators; ritual quality is passed
  as XML-tuned plain words; invoker prompts require one unsettling `[[speech]]...[[/speech]]` block;
  a darker Ritual-domain group ships.
- **Ideology ritual events added.** Finished, non-canceled `LordJob_Ritual` outcomes fan out to
  author/target/participants/spectators with role/title/status context. Ritual policy includes
  DLC-safe XML edge groups for Royalty throne/anima, Biotech childbirth, and Odyssey gravship.
- **Important-event status context added.** Prompt enchantments can now add one weighted DLC-safe
  status cue (Royalty title or Ideology role) for important events; the two are mutually exclusive
  per prompt through the shared one-candidate picker.
- **Personas reworked into writing styles.** Player-facing settings, picker text, prompt defaults,
  and presets now describe writing styles (sentence shape, rhythm, detail choice) rather than chat
  personas; the system wrapper tells models not to roleplay, add catchphrases, or invent dialogue.
  The stock catalog was retuned for small-model separability. Internal `DiaryPersonaDef`/save keys
  are unchanged for compatibility.
- **Per-event-type prompt variation.** Interaction groups can carry `instructions` and `tones`
  variant pools; one wording is chosen per entry (instruction rolls at capture and persists; tone
  picks deterministically by event id). Pure `PromptVariants` helper + tests; every tone-bearing
  group ships two variants; prompt-lab `--all-variants` crosses instruction/tone/enchantment pools
  with a coverage check.
- **Diary UI tweaks.** `autoExpandedEntryCount` defaults to 3 so older cards start collapsed;
  save-time sanitizer strips echoed schema punctuation (`;` `=` `:` `|`) while preserving prose.
- **Reliability fixes.** Harmony startup hook restored for 1.6 (resolves
  `ToGameStringFromPOV_Worker` with an old-name fallback so a rename can't abort `PatchAll` before
  later hooks register); cooldown-failover snapshot, No-auth lane identity, query-key URL-fragment
  preservation, and non-negative prompt-variant seeds all fixed.
- **Docs.** Live RimBridge/GABS hook-validation workflow and a phase-by-phase live auto-test scenario
  were recorded in DOCUMENTATION.md; a GABS live-smoke Lua fixture was added under `scripts/gabs/`.
- **Save compatibility documented** in player metadata and persistence docs: the mod records only
  self-contained diary history and attaches no gameplay defs/components to pawns or maps.

## 2026-06-23

- **Settings window compacted.** Request tuning moved inside the connection section; Prompt Studio
  is collapsible with system and event prompts sharing one editor; writing-style presets edit in one
  block; the nonworking generated-speech Social-log injection toggle/path is hidden and forced off.
- **Diary entry point moved to pawn selection.** The Diary inspector tab-strip button is hidden;
  selecting one eligible colonist (or colonist corpse) shows a **Diary** command button with a
  journal-and-pen icon that opens/closes the same tab.
- **Mod icon added** (`About/ModIcon.png`, texture-backed `modIconPath`); publish script now carries
  the mod icon and runtime command-icon textures into `dist`.
- **Save-time tag sanitizer** preserves valid `[[speech]]...[[/speech]]` blocks while repairing
  malformed closers and stripping/flattening hallucinated bracket tags from small models.
- **Prompt-lab coverage** now renders static arrival/death `DiaryEventPromptDef` fields and generated
  Romance/Raid/Quest contexts, and `npm test` checks all XML template/event prompt types.
- **Thinking-model response parsing fixed** — typed visible output beats flattened reasoning, more
  reasoning part types are skipped, and blank Ollama messages fall back to root `response`.
- **Prompt Studio can edit event-source guidance** — per-event `prompt`/`enhancement` overrides over
  the XML catalog.
- **Review hardening pass:** day-reflection dedup on save/load, endpoint editing, capped HTTP reads,
  deferred PlayLog grammar rendering until eligible, one-time reflection warnings, null-tolerant
  diary lookups, throttled name-highlight rebuilds, centralized Anomaly creepjoiner reads in
  `DlcContext`, no direct Royalty suppression handling, one English connector removed from death
  context.
- **Day reflections require an XML-controlled important signal.** End-of-day summaries drop
  filler-only days by default (`daySummaryImportantSignalKinds` = `event`, `opinion`, `hediff`);
  adding `filler` in XML re-enables quiet summaries.

## 2026-06-22

- **Quest event source added (rich context).** `Quest.Accept` + a defensive
  `MainTabWindow_Quests` accept-action patch + a `Quest.EverAccepted` scan + `Quest.End` record the
  full lifecycle. Only accepted quests are recorded; `Success`→"completed", `Fail`→"failed"; rich
  context (description cap 600, issuer faction, `QuestPart_DropPods` rewards cap 300) embedded in
  event text and `quest=` marker. Saved quest entries keep the real `QuestScriptDef` and recover
  their group from the saved `signal=` field.
- **Raid event source added (minimal realization).** `IncidentWorker.TryExecute` (filtered to
  `IncidentWorker_Raid`) fans out one solo entry per eligible colonist on the target map; payload is
  incident/faction defName + raid points; colony-level dedup by incident/map/faction/points.
- **Prompt structure split.** Shared system prompts shortened to global essentials; event-source
  guidance moved into `DiaryEventPromptDef` rows (`event prompt:` / `event enhancement:`) before
  per-group `instruction:`/`tone:` flavor.
- **Diary prose nudged toward immediacy** — one sensory detail, one emotional beat, one implied
  consequence/tension from supplied facts; event-type enhancements sharpened.
- **Diary tab made taller** (650→800, tunable via `tabHeight`); dev-only copy button on entry cards
  (copies prompt for prompt-only cards, else generated text); **Prompt suite** reworked to a
  data-driven dropdown that captures one plain prompt-only card per category, plus a "Clear test
  prompts" button.

## 2026-06-21

- **Formatting system matured.** Dev-mode previews for prose/markdown/speech/combat/social-fight/
  mental-break/dark/linked-card/animations/death/strange-chat; stronger staggered speech; distinct
  conflict and mental-break page washes; dark vs. strange (Zalgo) speech split; live pawn names
  highlighted in prose with status-aware colors.
- **Event Catalog completed** for current live sources (Thought, Inspiration, MoodEvent,
  MentalState, Tale, Hediff, Interaction, Romance, Arrival, Death fallback, Work,
  ThoughtProgression, DayReflection). `CaptureDecision` models solo/pair/batch/ambient/day-reflection
  and neutral arrival/death routes.
- **Romance relation events added** (Lover/Spouse/ExLover/ExSpouse pairwise via a Romance domain).
- **Hardening:** Work sampling no longer consumes cooldown/RNG before eligibility gates; catalog
  dispatch tests exercise every Spec; synthetic crafted/relic events removed (vanilla Tales cover
  them); recorders no-op until in play; first-person generation respects minimum biological age;
  save/load and parser reliability improved across prompt metadata, caches, MiniJson, and providers.

## 2026-06-19 — 2026-06-20

- **Generated speech Social-log injection** (initiator entries can inject one generated direct-speech
  row into the Social log). **Known issue:** rows are added to `Find.PlayLog` and get `LogID`s but
  may not appear in RimWorld's Social tab UI; injection is currently disabled/hidden and forced off
  in settings. Direct-speech cleanup hardened (preserve closed speech blocks, prune stale PlayLog
  rows).

## 2026-06-17 — 2026-06-18

- **Workshop release + publishing flow prepared** (Workshop metadata, preview art, publish script,
  package-id cleanup, verification hooks).
- **LLM compatibility broadened:** OpenAI-compatible Chat Completions, OpenAI Responses, and native
  Ollama Chat; reasoning/thinking output cleanup; debug raw-response views. (Native Ollama was later
  removed — see 2026-06-24.)
- **Pipeline extracted into pure contracts** (prompt planning, response postprocessing/parsing, text
  decorations, domain recovery) with tests. Diary architecture split into focused partials/helpers.
- **Health/combat/social capture expanded** (Hediff groups + progression, combat tale batching,
  insult batching, direct-speech POV rules, important health/capacity cues).

## 2026-06-16

- **Core experience matured:** work entries, prompt-lab support, title generation, UI readability,
  social/work generation weights, pending-recovery, LLM retries/failover, batching, day reflections,
  and save/load robustness.

## 2026-06-14 — 2026-06-15

- **Diary gameplay coverage broadened:** arrival and death chronicles, writing personas, DLC-safe
  context patterns, broader event routing, and localization coverage. Established the pawn Diary tab,
  context builders, and early event display/generation flow as the main UI surface.

## 2026-06-13

- **Initial diary system:** base diary event model, generation path, UI surface, and RimWorld
  integration.
