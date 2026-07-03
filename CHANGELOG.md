# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state.

## 2026-07-03

- **Settings connection row alignment and localization.** Main-tab API rows now share the same label
  column for reasoning controls and the same action-button columns for model/API-key rows, removing
  the clipped "Reasoning" label and staggered right-side buttons. Russian settings localization now
  includes the missing reasoning capability/tag strings and shorter compact labels.
- **New color cues for psychic and royal events.** `psychic` (bright violet) for psylink gains and
  psycasts, `royalty` (gold) for title gains and royal rituals — previously psylink shared the
  Anomaly dread red (`extremeDark`) and titles shared the generic warm-white cue. Anomaly dread
  groups stay on `extremeDark`. Palette in `DiaryUiStyleDef.xml` with C# fallbacks; `colorCue` is
  saved per-event, so existing entries keep their old color.
- **Dev event panel: hid the non-functional Events section.** Its rail button is no longer drawn,
  saved `events` selections normalize to Diary, and Diary is the default section. `DrawRealEventsSection`
  and the `Trigger*` helpers are retained so the section can be re-enabled once triggers are fixed.
- **Event-coverage review fixes (XML + docs only).** Corrected dead `ThingPresent` observer
  defNames (`ObeliskPresence` → real `WarpedObelisk_*`, `UnnaturalCorpsePresence` →
  `UnnaturalCorpse_Human`, `HarbingerTreePresence` → `Plant_TreeHarbinger`) and added display
  groups for the new page-recording defs so they classify properly instead of hitting the catch-all.
  Documented the event-coverage defs in §5/§5.1.
- **Event-coverage pass: XML-only groups, enchantments, personas, observed conditions, tone windows.**
  Implements Tiers 1-2 of `EVENT_COVERAGE_PLAN.md`, Anomaly focus, no C# changes. Retone groups:
  Anomaly entity raids, weather hardship (ColdSnap/HeatWave/VolcanicWinter/Flashstorm + DLC), and a
  three-way mental-break split. New prompt enchantments (malnutrition, heatstroke/hypothermia,
  anesthetic, psychic shock, carcinoma, mechanites, drugs, deathrest/lungrot, bloodrage, vacuum,
  etc., including memory decay). New drunk writing-style override. New observed
  conditions (weather, Anomaly states, obelisks/pit gate/fleshmass heart, weighted light flavor for
  thrumbo/blight/ambrosia). New event windows (`MechClusterLanded`, `ShortCircuitAftermath`,
  `SelfTameJoined`). All plain-string matchers (DLC-safe); EN + natively-written RU strings;
  `EVENT_PROMPT_MAP.md` refreshed.
- **Event-coverage gap analysis & plan (docs only).** New `EVENT_COVERAGE_PLAN.md`: inventories
  current reactions and proposes a tiered XML-only extension plan. No behavior change.
- **Render-time paragraph reflow for diary prose.** Long single-line entries split into readable
  paragraphs at sentence ends, RimWorld year mentions, semicolons, and em-dashes, with a hard length
  cap. Default atmosphere only; `GeneratedText` is never mutated and both measure/draw passes share
  the helper. Tuning in `DiaryUiStyleDef.xml`; new pure `tests/DiaryParagraphReflowTests`.
- **Review fixes: API settings, arc cadence, localization.** Locked the per-row in-flight guard so
  UI cancellation can't race reasoning-capability refreshes; arc reflections honor
  `arcReflectionMaxEntriesPerYear` across the full UI range (pure tests added); moved dynamic
  Advanced prompt-policy prefixes to Keyed EN/RU; `LlmClient.TestConnection` no longer holds an
  English prompt fallback.
- **Title fallback guard tightened.** Title follow-ups now reject one-line labels, instruction
  echoes, reasoning-style lines, terminal periods, and out-of-contract lengths, falling back to a
  title built from the finished entry.
- **Reasoning capability auto-refreshes across the settings window.** A row's
  capability/model list now fetches on settings-open (uncached rows), URL/key/auth change
  (background), and Test connection (parallel), plus the manual Fetch. A new capability-only
  refresh updates the thread-safe cache without touching single-flight picker state; removed the
  redundant auto-fetch-at-Pick code.

## 2026-07-02

- **Per-lane reasoning-tag picker and capability-aware reasoning effort.** A "Reasoning tag"
  dropdown (default Auto) pins the exact wrapper tag a model emits, stripped alongside the built-in
  broad guess-list. Endpoints advertising `reasoning.supported_efforts`/`default_enabled` now
  constrain the effort dropdown and clamp outgoing `reasoning_effort` (fixes 400s on non-reasoning
  models like Gemma); providers without capability degrade gracefully. New `ModelReasoningCapability`,
  `ModelCapabilityCache`, `ApiEndpointPolicy.NormalizeReasoningTag`, and a tag-parameterized stripper;
  pure tests added.
- **Reasoning config now mostly auto.** The built-in Auto reasoning-tag stripper now covers
  `think`/`thinking`/`reasoning`/`analysis`/`thought`/`reflection`/`scratchpad` (was four tags);
  reasoning capability auto-fetches at model pick when not yet cached.
- **Public mod-integration API v1 (inbound events).** Other mods push moments via the stable
  `PawnDiary.Integration.PawnDiaryApi.SubmitEvent(ExternalEventRequest)` facade — validated,
  crash-isolated, main-thread-guarded, routed through the normal bus as a new `External` source.
  Prompt policy stays in XML (an External-domain group claims the `eventKey`); new
  `externalEventDedupTicks` knob and `enableWhenPackageIdsLoaded` group flag. Debug Action test entry,
  EN/RU strings, `INTEGRATIONS.md` contract, and a buildable adapter template in
  `integrations/PawnDiary.ExampleAdapter/` with a deploy script.
- **Hidden-infection "insurance" tone for post-kill metalhorror.** New `MetalhorrorInfection`
  observed condition (backed by a new `MapHiddenHediff` observer) keeps a softer dread-tone alive
  while any home-map colonist is infected — tone-only, never names the hediff or host. DLC-safe
  (plain defName string).
- **Revealed-metalhorror override lasts the whole threat.** `MetalhorrorEmergence` now ends on the
  live `Metalhorror` ThingDef leaving `ListerThings` (death → `Corpse_Entity`), removing the blunt
  2-day `maxActiveTicks` cap that cut the override off mid-rampage.
- **Per-row Test connection.** Clicking Test on row B while row A runs starts B immediately, each
  with its own status/generation counter and a thread-safe result queue. Fetch-models stays
  single-flight global.
- **Non-reasoning models stopped failing Chat Completions on Reasoning → None.** The request body
  omits `reasoning_effort` when saved effort is `none` (matching `default`), fixing 400 "Thinking
  budget not supported" errors on models like Gemma. Responses mode unchanged.
- **Pre-release performance pass.** Diary tab name-highlight refresh no longer re-measures cards
  unless highlights really changed (new pure `DiaryNameHighlighter.SameHighlights`); the hottest
  Harmony hooks (`Thing.SpawnSetup`, `AddHediff`, `TryGainMemory`) use a closure-free state-passing
  `DiaryPatchSafety.Run` overload; batch scanners allocate keys lazily.
- **No longer bundles Harmony.** Ships only `PawnDiary.dll`; runtime Harmony comes from the
  `brrainz.harmony` dependency. Removed shipped `0Harmony.dll`, publish script no longer copies it,
  and the verify hook fails on a bundled copy. `Source/Libs/0Harmony.dll` stays as a compile-time
  reference only.
- **Project builds from any checkout location.** RimWorld/Unity assembly hint paths resolve via a
  configurable `RimWorldManaged` MSBuild property (`/p:RimWorldManaged=...` or `RIMWORLD_MANAGED`
  env var), replacing the hard-coded relative path.
- **Diary export moved to Debug Actions.** `Pawn Diary > Export all diary pages...` writes hot
  pages, compact archived pages, archive rows without a live record, and backing event records; the
  old settings export button was removed.
- **Single-item interaction batches become standalone entries.** A `PairEvent` batch that collects
  only one moment now emits a normal standalone entry (original defName/label, no batch marker);
  multi-item batches unchanged. Pure test pins `events=1` template selection.
- **Lasting prompt overrides gained a stale-state guard.** Observed-condition prompt bias stops once
  a condition is missing past its end debounce, even if its row is retained for retry; event-window
  rows validate they have a positive timeout or usable end signal. Pure tests cover the boundary.
- **Inspect-tab Diary button no longer shows unread markers.** The bottom command overlay still
  signals unread/writing status; removed obsolete marker style knobs.
- **Prompt-enchantment editor exposes `chance`, not the `frequency` alias.** Legacy `frequency` XML
  is accepted but pruned from saved overrides.
- **Russian localization review.** Filled missing prompt-tuning/Prompt Studio Keyed strings, synced
  DefInjected, fixed a persona speech marker, and replaced code-flavored UI phrases with natural
  RimWorld terminology.
- **Prompt-policy override boxes no longer mirror XML translation defaults.** Key-backed boxes
  (`*Text`, `conditionLabel`, cue/batch/hediff text) stay blank when XML owns the Keyed default.
- **Arrival capture no longer risks an early-worldgen "Could not find player faction" log.** Checks
  `GamePlaying` first and uses `Faction.OfPlayerSilentFail`.
- **Malformed quests filtered from prompts.** New pure `QuestEventData.IsMalformedResolvedQuestDescription`
  rejects quests whose description resolves to `ERR:` placeholder or blank text.
- **Harmony patching is now per-class.** Startup replaced `harmony.PatchAll()` with a per-class
  `PatchAllSafely` sweep so one fragile target can't block later patches.
- **Quest UI accept fallback silent on clean boot.** `QuestUiAcceptPatch` generated-closure fallback
  no longer warns (canonical `Quest.Accept` is the real hook); dev-mode only.
- **UI text-clipping hardening.** Group-label chip is 20px tall; Advanced group title measures
  Medium-font line height instead of a hard-coded 24px.
- **Destructive dev buttons tinted red** with the XML-owned `devDangerButtonColor`.

## 2026-07-01

- **Tuning and prompt settings tabs for XML overrides.** Mod settings reorganized into
  Main/Prompts/Styles/Tuning tabs. Prompts holds shared/event prompts plus a full prompt-policy and
  weights editor; Styles edits writing-style rules/tags; Tuning exposes scalar/list/table knobs from
  `DiaryTuningDef`, `DiarySignalPolicyDef`, and `DiaryContextReactionDef`. Tuning and Prompt policy
  use a two-pane editor with per-field widgets, reset, accent coloring, filtering, and tooltips.
  Overrides persist per player (`TuningOverrideStore`) and apply immediately to live Def fields;
  follow-ups register Def-backed groups lazily, replace translation-key editors with literal-text
  override fields, and make literal overrides win over Keyed at generation time.
- **Pawn arc reflections.** Passion/psylink/xenotype/title progression pages plus rare yearly arc
  reflections from de-duplicated hot/archive memories. XML owns templates/cadence/policy; fixtures,
  pure tests, and `PAWN_ARC_REFLECTION_IMPLEMENTATION.md` cover the flow.
- **Gray-flesh suspicion hands off to emerged metalhorrors.** Observed conditions gained
  `suppressWhenThingDefNames`, age-based decay (`promptDecayTicks`/`promptDecayMinMultiplier`),
  `maxActiveTicks`, and restart cooldowns. `AnomalyGrayFleshEvidence` tracks gray-flesh samples and
  stops once a visible metalhorror exists; `MetalhorrorEmergence` now observes the spawned ThingDef.
  Long-lived style-override hediffs (inhumanization, joywire, etc.) are suppressed from prompt
  enchantments; settings expose the decay/cooldown/suppression knobs.
- **Diary arcs start with arrival and continue from the prior ending.** Starting-colonist arrivals
  flush first on new games, arrival refs insert at the front of the index, and prompts gain an
  XML-owned `PreviousEntryEnding` field. Pure tests cover the new source token.
- **Resurrected pawns keep writing.** A death page stays a historical boundary but no longer ends
  the diary if RimWorld revives the same load ID; capture, generation, rendering, export, and
  retention all ignore the old cutoff while alive.
- **Archived-page purge moved to a direct Debug Action** (`Pawn Diary > Purge archived entries for
  pawn...`): a pawn picker clears only that pawn's compact archive rows, leaving active hot events
  untouched.
- **Diary tab pagination no longer flickers over quiet refreshes.** A year-index build is full
  blocking only when no cached index exists.
- **Dev event panel click split.** Def-backed rows left-click to fire, right-click to open the Def
  selector; button titles mirror the selected menu label.
- **Generated-text sanitizer hardened.** Handles incomplete speech markers, `speach` typos,
  unfinished bracket/reasoning tags, and leaked Unity rich-text tags before save/UI.
- **Prompt settings menu labels shortened:** compact system-prompt names, internal event keys hidden.
- **Russian localization caught up for reflections**, rewritten idiomatically.

## 2026-06-30

- **Mod versioning added.** `About/About.xml` now carries `modVersion` (`0.1.0` initially), and
  `scripts/publish.ps1` stamps that version into both the main and Russian localization payloads.
  `-Version <value>` can override the payload version for a release.
- **Workshop/docs cleanup.** `scripts/publish.ps1` no longer ships `Source/` or other
  development-only folders to Workshop, while maintainer docs remain included. `DOCUMENTATION.md` was
  shortened around current architecture and policy, and `EVENT_PROMPT_MAP.md` now uses current-state
  Mermaid diagrams for capture, prompt policy, overrides, and active weights.
- **Generation and diary UI polish.** Quest acceptance now only marks quests seen; completed/failed
  outcomes generate colony-effort pages. Rare quadrum reflections add long end-of-quadrum pages from
  dated highlights with XML tuning and optional `forcedModel`. The Diary tab unread marker moved to
  the top center, rewrite moved to a subdued footer icon, and the work/social random-generation
  sliders were merged into one migrated weight.
- **Starting arrival context improved.** Founding-colonist arrival prompts now receive each pawn's
  childhood/adulthood title, full in-game backstory description, and compact backstory effects
  (skill bonuses, disabled work/tasks/tags, required tags, and forced/disallowed traits), and the
  neutral arrival instruction asks the model to connect those facts with the starting scenario.
- **Dev event test panel.** Dev mode now exposes `Pawn Diary > Event test panel...` with real vanilla
  triggers for registered diary sources, prompt fixtures, former Diary tab dev tools, persisted
  selections/sections, Def selection for trigger types, and a purge action for compact archived pages.
- **Retention and archive controls.** Active hot pages and compact archive rows now have separate
  per-pawn caps (`maxActiveDiaryEvents`, default/max 100; `maxArchivedDiaryEvents`, default 10000).
  Old oversized hot caps are clamped and older hot rows compact into archive rows on load/save.
- **Review hardening pass.** Tightened API-key masking/log redaction, memoized interaction
  classification and tagged grammar checks, added O(1) diary lookup by pawn id, shared same-tick
  colonist snapshots, snapshotted failover lanes, fixed arrival/death tie ordering, stripped all stray
  reasoning closing tags, validated conflicting observed-condition map flags, made text truncation
  surrogate-safe, aligned event-id lookup casing, prefiltered kill fallback work, and localized
  dev-only LLM debug labels.
- **Observed conditions.** Added the scan-backed system for lasting colony states (`MapDanger`,
  `GameCondition`, `ThingPresent`, `PawnHediff`) with pure lifecycle planning, debounce-aware
  persistence, prompt candidates, XML defs for map threats/toxic fallout/solar flare/Anomaly gray
  flesh evidence, EN/RU strings, 62 pure tests, docs, and a rebuilt DLL. Retired the fixed-timeout
  `MetalhorrorSuspicion` event window; `MetalhorrorEmergence` ships disabled pending observable-state
  verification. Follow-up hardening made recording transactional/retryable, skipped empty pawn-hediff
  scans, added invalid subject-pawn scope validation, and corrected save comments/header dates.
- **Renderer/save-compat extraction.** Continued Plan 9 by moving expanded-card measurement and
  rendering into helper classes while leaving behavior/visuals unchanged. Plan 6 moved load-repair
  normalization into `DiarySaveNormalization`, removed dead wrappers, added 46 pure
  save-normalization assertions, documented Scribe compatibility/runbook coverage, and rebuilt the
  DLL.
- **Archive compaction fixes.** Hardened compacted failed/stale page fallback text/title, cleared
  matching archive rows from dev prompt-suite reset, prevented hidden-hot/archive duplicate cards,
  moved overflow selection into `DiaryArchiveCompactionPlanner`, added coverage, and rebuilt the DLL.
  Same-year diary refreshes now update selected-year layout in place so generation/title completion no
  longer flickers to the loading panel.

## 2026-06-29

- **Archive compaction landed.** Added the compact `diaryArchiveEntries` save store and
  `DiaryArchiveRepository`, so old completed/stale/failed displayable pages compact before hot refs
  are dropped while active pending rows stay hot. The Diary tab, Social-log links, dev export,
  archive eligibility tests, and the earlier Plan 3 design doc were updated around the new archive
  flow.
- **Large-history Diary tab performance improved.** `DiaryContextFields` now scans context strings
  without repeated `Split` allocations, and sliced year/card builds tolerate generation-side state
  ticks without restarting unless the visible structure changes. Large histories load much faster
  while keeping save data and parsing semantics unchanged.
- **Thought classification tightened.** Risky broad positive/negative thought substring tokens were
  replaced with exact defName lists plus precise prefix/suffix/segment matchers for mod coverage,
  with pure `GroupNameMatcher` tests covering opinion-flipped death/loss regressions.
- **Event ingestion bus completed and hardened.** All 17 catalog sources now submit uniform
  `DiarySignal` payloads through one dispatch/dedup/emit path with no save-format changes. Review
  fixes made dedup pruning respect each key's own window, made zero/negative windows opt out cleanly,
  restored pre-roll dedup ordering, and updated stale comments/docs.
- **Gray flesh event-window monitor disabled.** `MetalhorrorSuspicion` is disabled in XML because
  the gray-flesh spawn signal could leave the status effectively always active; the row remains as a
  documented template until a safer monitor exists.

## 2026-06-28

- **Prompt and condition style cleanup.** Memory-decay hediffs now stay prompt context instead of
  forcing a lost-thread style, hediff style overrides suppress duplicate matching prompt guidance,
  external game/mod text included in prompts is flattened and capped, and bad title/quest follow-up
  text now falls back to safe humanized excerpts.
- **Russian localization shipped and polished.** A full Russian Keyed/DefInjected set now ships with
  glossary-aligned RimWorld terms, rewritten writing styles, localized humor cues, neutral
  placeholder guidance, UI/prompt copyedits, complete DefInjected coverage, and key/placeholder
  parity checks.
- **Russian Workshop payload support.** `scripts/publish.ps1` now builds Russian as a separate
  language-mod payload with translated metadata, localized preview art, separate Workshop id support,
  and local junction installs alongside the main payload.
- **Dev/UI access fixes.** Dev mode can export all saved diary pages and backing records to a UTF-8
  text file, prompt-suite fixtures ignore live prompt enchantments, and Diary tab registration/opening
  is retried defensively after startup.
- **Condition and event-window features.** XML hediff style overrides can temporarily force writing
  styles for active conditions; event windows now cover birthdays, heart attacks, and prison breaks.
  Review hardening added trigger-rule prefilters, isolated optional event-window failures from other
  capture paths, and restored missing English DefInjected stubs.

## 2026-06-27

- **XML event-window support expanded.** `DiaryEventWindowDef` can now create start/end/timeout
  diary entries from incident, quest lifecycle, spawned-thing, and letter signals, with built-in
  windows for metalhorror suspicion, ancient dangers, and void monolith discovery or activation.

- **Anomaly and hediff prompt routing improved.** `DeathPall` and `UnnaturalDarkness` now route
  through more specific mood groups, Anomaly hediffs such as revenant hypnosis and cube effects can
  trigger immediate/progression entries, and common drug hediffs gained localized prompt condition
  overrides.

- **Long-history retention and UI performance reworked.** The history cap is now per pawn, the
  retention plan is covered by pure tests, background maintenance uses a bounded hot window, and the
  Diary tab virtualizes long lists with sliced loading, unread flags, pawn-view reuse, stale-entry
  handling, archived-pending fallbacks, and dev-mode stress history.

## 2026-06-26

- **Localization, packaging, and maintainer docs cleaned up.** DefInjected folder names were fixed,
  the Workshop preview was replaced with human-made art, publish output now includes source and
  reference docs, and `DOCUMENTATION.md` was condensed into a current-state guide.

- **Generation and retention reliability improved.** Catch-up scans became demand-driven, orphan
  recovery moved to its own pass, retained diary events gained a settings cap, and completed LLM
  results now drain while the game is paused.

- **Prompt and compatibility policy expanded.** Event prompt lookup now falls back through several
  XML keys for modded compatibility, SpeakUp rows route as promoted chitchat, tagged social-log
  grammar uses a reply-suppression guard, and stock writing styles were retuned around distinct
  mechanics.

- **Runtime and UI hardening landed.** Diary hooks, ticks, save/load work, startup tab injection,
  and immediate-mode drawing now isolate failures; mental-break card styling was softened; unread
  markers skip world inspect panes; and thinking-model self-edit cleanup gained parser coverage.

## 2026-06-25

- **Diary tab surfaced as the default.** Fresh settings now use the pawn inspect tab, selected
  humanlike corpses can show Diary too, unread markers appear in tab mode, and command mode remains
  available with its underline/writing dots.
- **Generation controls expanded.** Event prompts can prefer a configured model while keeping normal
  failover, dev-mode cards can regenerate a saved POV page, raids gained timing/prompt tuning, and
  eligible first-person entries can receive subtle XML-tuned humor cues.
- **Large structural extraction pass.** Per-POV state collapsed into `PovSlot`, saved events moved
  into `DiaryEventRepository`, generation code split by stage, Harmony patches split by capture
  domain, settings/UI code moved into focused files, and dead diary-bound helpers were removed while
  preserving existing Scribe keys and behavior.
- **Diary tab performance and cache work.** Long-history drawing now memoizes counts by render token,
  culls offscreen cards, caches expanded-card measurements, and routes visible-entry reuse/year
  ordering through `DiaryTabVisibleEntriesCache`.
- **Pure helpers and tests broadened.** Prompt enchantment planning, text decorations, LLM request
  JSON, parser speech-marker guards, API lane identity, and context-builder substeps moved into
  focused pure helpers with targeted coverage.
- **XML/localization boundaries tightened.** Consciousness/stagger/mood/intoxication policy moved to
  XML fallbacks, localized colony naming moved out of saved models, instruction resolution moved out
  of settings DTOs, live pawn fact capture moved to `PawnFactCapture`, and English DefInjected stubs
  were filled for new prompt/event text.
- **Release and review cleanup.** Workshop metadata, README/publish output, and package-id handling
  were prepared for public release; review fixes addressed stale card-height caching, unknown
  decoration validation, shared API pinning, and small dead facade surface.

## 2026-06-24

- **Native Ollama API mode removed.** API lanes now use OpenAI-compatible Chat Completions or
  OpenAI Responses only; local Ollama remains usable through its OpenAI-compatible endpoint.

- **Harmony declared as a mod dependency.** `About/About.xml` now declares `brrainz.harmony` for
  RimWorld 1.6 while keeping the bundled DLL as a run-from-clone fallback.

- **API lanes hardened for free-tier pools.** Routing modes, lane reordering, auth modes, Gemini
  reasoning support, exponential cooldowns, failover snapshots, settings cleanup, byte-capped reads,
  and prompt-lab auth options were added.

- **Pawn ability-use events added.** Successful ability activations can create cooldown-weighted solo
  entries with ability, category, target, and cooldown context.

- **Anomaly psychic ritual events added.** Completed psychic rituals fan out to relevant pawns with
  quality/context text and darker XML-guided prompt rules.

- **Ideology ritual events added.** Finished non-canceled rituals create role-aware entries for
  authors, targets, participants, and spectators, with DLC-safe edge-group policy.

- **Important-event status context added.** Prompt enchantments can add one weighted Royalty title or
  Ideology role cue for important events.

- **Personas reworked into writing styles.** Settings, picker text, prompt defaults, and presets now
  describe writing mechanics instead of chat personas while keeping internal save keys stable.

- **Per-event-type prompt variation.** Interaction groups can define instruction and tone variant
  pools, with persisted instruction rolls and deterministic tone picks.

- **Diary UI tweaks.** Older cards collapse by default after the first three entries, and save-time
  cleanup strips echoed schema punctuation while preserving prose.

- **Reliability fixes.** Startup patching, cooldown failover, no-auth lane identity, query-key URL
  fragments, and prompt-variant seeds were hardened.

- **Docs and live-smoke coverage added.** RimBridge/GABS hook-validation notes, an auto-test
  scenario, and a live-smoke Lua fixture were added.

- **Save compatibility documented.** Player metadata and persistence docs now state that diary
  history is self-contained and does not attach gameplay defs/components to pawns or maps.

## 2026-06-23

- **Settings window compacted.** Request tuning, Prompt Studio, writing-style editing, and the hidden
  generated-speech Social-log toggle were reorganized into a smaller settings surface.

- **Diary entry point moved to pawn selection.** Selecting one eligible colonist or corpse now shows
  a Diary command button instead of the old inspector tab-strip button.

- **Mod icon added.** `About/ModIcon.png`, `modIconPath`, and publish-script texture copying were
  added for the mod icon and runtime command icons.

- **Save-time tag sanitizer improved.** Valid speech blocks are preserved while malformed closers and
  hallucinated bracket tags are repaired or stripped.

- **Prompt-lab coverage expanded.** Static prompt fields, generated Romance/Raid/Quest contexts, and
  XML template/event prompt checks are now covered.

- **Thinking-model response parsing fixed.** Typed visible output now wins over flattened reasoning,
  more reasoning part types are skipped, and blank Ollama messages fall back to root `response`.

- **Prompt Studio can edit event-source guidance.** XML catalog prompt and enhancement text can now
  be overridden per event source.

- **Review hardening pass landed.** Save/load dedup, endpoint editing, capped HTTP reads, deferred
  PlayLog grammar rendering, reflection warnings, diary lookup null tolerance, name-highlight
  throttling, DLC context reads, and death-context wording were tightened.

- **Day reflections require an XML-controlled important signal.** End-of-day summaries now skip
  filler-only days by default, with XML able to re-enable quiet summaries.

## 2026-06-22

- **Quest event source added.** Accepted and ended quests now create lifecycle entries with compact
  quest, issuer, reward, and result context.

- **Raid event source added.** Raid incidents fan out solo entries to eligible colonists with
  incident/faction/points payloads and colony-level deduplication.

- **Prompt structure split.** Shared system prompts were shortened, with event-source guidance moved
  into `DiaryEventPromptDef` rows before group instruction/tone flavor.

- **Diary prose nudged toward immediacy.** Prompt guidance now asks for one sensory detail, one
  emotional beat, and an implied consequence from supplied facts.

- **Diary tab and prompt suite updated.** The tab is taller, dev cards gained copy support, and the
  Prompt suite became a data-driven dropdown with clearable prompt-only cards.

## 2026-06-21

- **Formatting system matured.** Dev previews, stronger staggered speech, conflict/mental-break page
  washes, dark/strange speech variants, and status-aware pawn-name highlighting were added.

- **Event Catalog completed for current live sources.** Capture decisions now cover solo, pair,
  batch, ambient, day-reflection, and neutral arrival/death routes.

- **Romance relation events added.** Lover, spouse, ex-lover, and ex-spouse changes now route through
  the Romance domain.

- **Hardening pass landed.** Work sampling, catalog dispatch tests, recorders, age/consciousness
  gating, save/load behavior, parser handling, caches, MiniJson, and provider support were tightened.

## 2026-06-19 - 2026-06-20

- **Generated speech Social-log injection prototyped.** Direct-speech rows can be generated for
  initiator entries, but Social tab display remains unreliable, so the setting stays hidden and off.

## 2026-06-17 - 2026-06-18

- **Workshop release and publishing flow prepared.** Workshop metadata, preview art, publish script,
  package-id cleanup, and verification hooks were added.

- **LLM compatibility broadened.** Chat Completions, OpenAI Responses, native Ollama Chat, reasoning
  cleanup, and raw-response debug views were added; native Ollama was later removed.

- **Pipeline extracted into pure contracts.** Prompt planning, response parsing, text decorations,
  domain recovery, and diary architecture moved into focused helpers with tests.

- **Health, combat, and social capture expanded.** Hediff progression, combat tale batching, insult
  batching, direct-speech POV rules, and important health/capacity cues were added.

## 2026-06-16

- **Core experience matured.** Work entries, prompt-lab support, title generation, UI readability,
  generation weights, pending recovery, LLM retries/failover, batching, day reflections, and
  save/load robustness improved.

## 2026-06-14 - 2026-06-15

- **Diary gameplay coverage broadened.** Arrival/death chronicles, writing personas, DLC-safe
  context patterns, broader event routing, localization coverage, the pawn Diary tab, context
  builders, and early event display/generation flow were established.

## 2026-06-13

- **Initial diary system.** Added the base diary event model, generation path, UI surface, and
  RimWorld integration.
