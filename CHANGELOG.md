# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state.

## 2026-06-25

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
  writing dots; tab mode now draws a short underline on the visible Diary tab button when diary
  activity is present: steady for newly finished pages, softly pulsing while writing is in progress.
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

- **Beta release + publishing flow prepared** (Workshop metadata, preview art, publish script,
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
