# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state.

## 2026-07-02

- **Per-lane reasoning-tag picker and capability-aware reasoning effort.** Reasoning models wrap
  their private thinking in many different wrappers (`<think>`, `<thinking>`, `<reasoning>`,
  `<analysis>`, and exotic ones like `<reflection>`/`<scratchpad>` for RP-tuned models), and there is
  no single "reasoning" wire format across providers. Two new per-lane controls address both
  symptoms. (1) A **"Reasoning tag" dropdown** (default *Auto*) lets a player pin the exact tag a
  model emits; the chosen tag is stripped *in addition to* the built-in broad guess-list, so exotic
  wrappers no longer leak into saved diary text, while common tags keep working as a safety net.
  (2) When an endpoint advertises per-model **reasoning capability** in its `/models` response
  (OpenRouter and some gateways — `reasoning.supported_efforts`/`default_enabled`), the effort
  dropdown now only offers levels the model accepts, the row shows a tooltip of what the model
  supports, and the outgoing request **clamps** `reasoning_effort` so it never carries a value the
  model rejects (the direct fix for "400 Thinking budget is not supported for this model" on
  non-reasoning models like Gemma). Providers that do not advertise capability (OpenAI-direct, local
  GGUF servers) degrade gracefully to today's unconditional behavior. New pure
  `ModelReasoningCapability` (parse + clamp policy), `ModelCapabilityCache` (process-wide
  endpoint+model keyed cache), and `ApiEndpointPolicy.NormalizeReasoningTag`; `StripReasoningTextBlocks`
  gained a tag-parameterized overload. `LlmResponseParserTests` and `DiaryPipelineTests` cover the
  new tag stripping and the capability clamping. (DOCUMENTATION §6.)

## 2026-07-02

- **Single-item interaction batch flushes now become standalone entries.** If an XML `PairEvent`
  interaction batch, such as insults, opens but only collects one social-log moment before the quiet
  window expires or the game saves, the flush now emits a normal standalone interaction entry: original
  defName/label, first POV texts, normal group instruction, and no `batch=interaction` prompt marker.
  Multi-item batches still use the combined synthetic defName/label and batch instruction. A
  `DiaryPipelineTests` regression pins that `events=1` without a batch marker selects the standalone
  prompt template. (DOCUMENTATION §5.)
- **Lasting prompt overrides gained an extra stale-state guard.** Observed-condition prompt bias now
  stops once the condition has been missing past its end debounce, even if the saved row is retained to
  retry an optional end diary page because no eligible pawn is available. That prevents a resolved
  condition from keeping the LLM in its override tone through retry bookkeeping. Persistent
  `DiaryEventWindowDef` rows also now validate that they have either a positive timeout or a usable end
  signal, and the pure tests cover the prompt-activity boundary plus shipped event-window XML close
  paths. (DOCUMENTATION §5/§5.1.)
- **Public mod-integration API v1 (inbound events).** Other mods can now push moments into a pawn's
  diary through the stable `PawnDiary.Integration.PawnDiaryApi.SubmitEvent(ExternalEventRequest)`
  facade — validated, crash-isolated, main-thread-guarded, and routed through the normal
  `DiaryEvents.Submit` bus as the new `External` event source (enum value + pure
  `ExternalEventData.Decide` + `ExternalEventSpec` + `ExternalEventSignal`). Prompt policy stays in
  XML: an External-domain `DiaryInteractionGroupDef` must claim the submitted `eventKey`
  (required-match; unclaimed keys warn once and record nothing), with a broad
  `DiaryEventPrompt_External` fallback row and a new `externalEventDedupTicks` tuning knob (adapters
  can override dedup per request). Groups also gained `enableWhenPackageIdsLoaded` — the inverse of
  `disableWhenPackageIdsLoaded` — so future in-core compatibility packs stay inert without their
  target mod. A Debug Actions entry ("Submit test external event...") exercises the whole path via
  the built-in `externalDevTest` group; EN/RU strings added, and `DiaryCapturePolicyTests` covers
  the External decision, dedup-key, and game-context formats. Public contract: `INTEGRATIONS.md`;
  architecture: `DOCUMENTATION.md` §3.7. A complete buildable adapter template ships in
  `integrations/PawnDiary.ExampleAdapter/` (own About.xml/csproj/GameComponent/group XML), with
  `scripts/deploy-integrations.ps1` to copy adapters to the RimWorld `Mods/` root during
  development.
- **The revealed-metalhorror prompt override now lasts the whole threat and no longer.** The
  `MetalhorrorEmergence` observed condition biases diary prompts away from ordinary health/mood context
  while a visible metalhorror is on the map. Its end trigger is the live `Metalhorror` ThingDef leaving
  `ListerThings`, which is reliable: when a metalhorror dies it becomes a `Corpse_Entity` (a different
  def), so `ThingsOfDef(Metalhorror)` stops matching and the override releases shortly after the kill.
  The earlier fix gave this condition a 2-day `maxActiveTicks` cap as a blunt band-aid against lingering
  remnants — but that cap also cut the override off mid-rampage for any metalhorror situation lasting more
  than two in-game days, which was the opposite of what was wanted. The cap is removed; the natural
  death-trigger is now the only end path, so a multi-day rampage keeps the override live until the
  metalhorror actually dies.
- **Added a hidden-infection "insurance" tone for the post-kill metalhorror situation.** Killing the one
  emerged metalhorror often does not end the threat: metalhorror infects multiple colonists and emerges
  from a single host at roughly half-colony infection, so others can still be carrying the implant
  silently. A new `MetalhorrorInfection` observed condition, backed by a new `MapHiddenHediff` observer
  type, senses "is any home-map colonist infected?" as a map-level boolean and keeps a softer dread-tone
  override alive until the colony is genuinely clean. The observer is tone-only by contract — it emits an
  empty evidence label and the prompt prose never names a hediff or a host — so the hidden mechanic stays
  hidden. This is the first observer that intentionally senses hidden pawn state; the existing `PawnHediff`
  observer still skips hidden hediffs and is unchanged. DLC-safe: matched by plain defName string, inert
  without Anomaly. (New observer covered by `TestObservedConditionDecayXmlPolicy`; DOCUMENTATION §5.1.)
- **The Test connection button stopped freezing every other API row while one test ran.** Each row's
  Test button now runs independently: clicking Test on row B while row A is still testing starts B
  immediately, and each row shows its own "Testing…"/success/failure status line. Previously a single
  global in-flight flag blocked every row's button until the one running test finished — a
  pre-existing limit that became visible once the Gemma `none`-effort fix (below) made tests actually
  succeed and hold that flag for the full request duration. `ApiConnectionController` now keeps
  per-row state with a per-row generation counter for stale-result rejection and a thread-safe
  `ConcurrentQueue` result handoff drained each UI frame. The Fetch-models button on the same screen
  is still single-flight global and unchanged. (DOCUMENTATION §8.)
- **Gemma (and other non-reasoning models) stopped failing Chat Completions lanes set to
  Reasoning → None.** The Chat Completions request body now omits `reasoning_effort` when the saved
  effort is `none`, matching how `default` already behaves. Previously the serializer sent
  `reasoning_effort:"none"`, which OpenAI-compatible gateways translate into a thinking-budget
  request that non-reasoning models reject — e.g. Google's endpoint returned HTTP 400 "Thinking
  budget is not supported for this model." for `models/gemma-4-*`. OpenAI Responses mode is
  unchanged, since `none` is a real, server-honored wire value there. (Pure test in
  `DiaryPipelineTests`; DOCUMENTATION §8 documents the per-mode serialization rule.)
- **Pre-release performance pass removed two hitch/garbage sources.** The open Diary tab no longer
  re-measures every expanded card on each periodic pawn-name-highlight refresh: the highlight
  version (which invalidates the card-height cache and row layout) now advances only when the
  rebuilt name/color set really changed, via the new pure `DiaryNameHighlighter.SameHighlights`
  (covered by `DiaryTextDecorationTests`). The three hottest Harmony hooks — `Thing.SpawnSetup`,
  `Pawn_HealthTracker.AddHediff`, and `MemoryThoughtHandler.TryGainMemory` — now run through a new
  state-passing `DiaryPatchSafety.Run` overload so their patch bodies allocate no per-call closure,
  and the per-tick batch-flush scanners allocate their key lists lazily instead of every tick while
  a batch is pending. No behavior change; also refreshed the stale partial-class file map in the
  `DiaryGameComponent.cs` header.
- **Diary inspect tabs no longer show unread markers.** The pawn inspect-tab Diary button now stays
  visually quiet when new pages are waiting; the bottom command overlay still signals unread/writing
  status when command mode is enabled, and obsolete XML style knobs for the tab marker were removed.
- **Advanced prompt-enchantment settings stopped exposing the frequency alias.** The Prompt policy
  editor now exposes `chance` as the single top-level appearance-odds control for prompt
  enchantments; legacy `frequency` XML remains accepted but saved `*.frequency` Advanced overrides
  are pruned as removed-editor entries.
- **Russian localization review filled the prompt tuning UI gaps.** Added the missing Russian Keyed
  strings for advanced prompt settings and Prompt Studio, synchronized prompt-template and
  interaction DefInjected text with the current XML source, fixed a malformed persona speech marker,
  and replaced awkward test-fixture wording such as "синтетическое задание" with natural Russian UI
  equivalents. A follow-up wording pass also replaced code-flavored Russian UI phrases like
  "пачка", "активный уклон", "сырая инструкция", and visible "пешка" labels with shorter
  localization-friendly wording; a RimWorld-terminology pass now uses "рейд"/"рейдеры" and
  "вдохновение" consistently, fixes the "идеолигия" typo, and removes remaining visible
  code-flavored wording from Russian prompt and UI text.
- **Prompt policy node settings stopped mirroring XML translation defaults.** Literal override boxes
  for key-backed prompt policy (`*Text`, `conditionLabel`, cue lists, batch/hediff text) now stay
  blank when XML owns the Keyed default, so node settings no longer expose raw translation keys or
  copy their resolved text into saved overrides.
- **Pawn Diary no longer bundles Harmony.** The mod now ships only `PawnDiary.dll` under
  `1.6/Assemblies/`; the Harmony runtime comes from the declared `brrainz.harmony` dependency at
  game-time. `1.6/Assemblies/0Harmony.dll` was removed, `scripts/publish.ps1` no longer copies it,
  and `.githooks/verify.ps1` now fails the build if a bundled `0Harmony.dll` appears in the shipped
  output. `0Harmony.dll` remains in `Source/Libs/` as a build-time-only compile reference
  (`Private=False`, never copied to output). (The plan's `PackageReference`/`Lib.Harmony` idiom is
  documented in the csproj but is not used: the legacy non-SDK csproj cannot be restored by the
  current .NET 10 / MSBuild 18 toolchain, so the proven compile-time-reference approach is kept.)
- **Project builds from any checkout location.** RimWorld/Unity assembly hint paths now resolve
  through a configurable `RimWorldManaged` MSBuild property (overridable via `/p:RimWorldManaged=...`
  or the `RIMWORLD_MANAGED` env var) instead of a hard-coded `..\..\..\RimWorldWin64_Data` relative
  path that only worked inside RimWorld's `Mods/` folder.
- **Arrival capture no longer risks an early-worldgen "Could not find player faction" log.**
  `ArrivalContextCache.Capture` and `PawnSetFactionPatch.Postfix` now check `GamePlaying` first and
  use `Faction.OfPlayerSilentFail` instead of the logging `Faction.OfPlayer` getter; the
  observed-conditions hostile counter and pawn name-highlight color picker use the same null-safe
  read.
- **Malformed quests are filtered from diary prompts.** `QuestManager.QuestsListForReading` can
  expose generated quests whose description resolves to `ERR:` placeholder text; a new pure helper
  `QuestEventData.IsMalformedResolvedQuestDescription` rejects those (and blank descriptions) so
  they never reach a diary page, while still allowing the generic event-window signal.
- **Harmony patching is now per-class.** `DiaryModStartup` replaced the single `harmony.PatchAll()`
  call with a `PatchAllSafely` sweep that patches each `[HarmonyPatch]` class independently, so one
  fragile target can no longer prevent later patches from registering.
- **Quest UI accept fallback is silent on a clean boot.** The version-specific generated-closure
  fallback in `QuestUiAcceptPatch` no longer logs a warning when it cannot resolve (the canonical
  `Quest.Accept` patch is the real hook); it surfaces only under dev mode.
- **Minor UI text-clipping hardening.** The diary card group-label chip is now 20px tall (inner
  label 18px after padding), and the Advanced settings group title measures its Medium-font line
  height instead of hard-coding 24px, reducing clipping under non-default UI scale / text-size
  accessibility settings.

## 2026-07-02

- **Diary export moved to Debug Actions and covers archived-only rows.** `Pawn Diary > Export all
  diary pages...` now writes hot pages, compact archived pages, archive rows without a live pawn diary
  record, and backing event records; the old settings-page export button was removed.
- **Destructive dev buttons are tinted red.** The event test panel now draws save-mutating and
  irreversible actions with the XML-owned `devDangerButtonColor`, including real event triggers,
  mock-page fill, archive purge, and prompt-suite clear.

## 2026-07-01

- **Prompt settings menu labels shortened.** The Shared/event prompts picker now uses compact system
  prompt names and hides internal event keys from event-prompt menu titles.
- **Archived-page purge is now a direct debug action.** RimWorld's Debug Actions menu now exposes
  `Pawn Diary > Purge archived entries for pawn...`, opening a pawn picker that clears only the
  selected pawn's compact archive rows while leaving active hot diary events untouched.
- **Diary tab pagination loading no longer flickers over quiet refreshes.** The tab now treats a
  year-index build as a full blocking load only when no cached year index exists, so loading a
  different year cannot make a same-pawn background refresh hide the pager or visible list.
- **Diary arcs now start with arrival and continue from the prior ending.** Starting-colonist
  arrivals are flushed before any non-arrival capture on new games, and arrival refs are inserted at
  the front of a pawn's diary index if another startup entry already exists. First-person prompt
  templates now include an XML-owned `PreviousEntryEnding` field fed from the previous page's final
  sentence excerpt, with `DiaryTuningDef` knobs for sentence count and max length; the pure planner,
  prompt-lab mirror, and pipeline tests cover the new source token.
- **Gray-flesh suspicion now hands off to emerged metalhorrors.** Observed conditions gained
  XML-owned `suppressWhenThingDefNames`; `AnomalyGrayFleshEvidence` now tracks analyzable gray-flesh
  samples and stops once a visible metalhorror or metalhorror debris exists, while
  `MetalhorrorEmergence` is enabled as a map-scoped observer for the spawned visible `Metalhorror`
  ThingDef instead of the hidden implant hediff path. Lasting event-window and observed-condition
  prompt enhancers now support age-based decay (`promptDecayTicks` / `promptDecayMinMultiplier`) so
  their selection weight drops and normal-context suppression relaxes over time; observed conditions
  also gained `maxActiveTicks` and saved restart cooldowns for force-ended identities, with
  gray-flesh suspicion force-stopping after two days and then using a two-day cooldown. The suspicion
  prompt now hides the sample's item label and writes the state as paranoia and fear that something
  may be infecting people and imitating them. Hediffs that match any active temporary writing-style
  override are now suppressed from prompt enchantments, so long-lived states such as inhumanization,
  trauma savant, joywire, bliss lobotomy, or mindscrew do not appear twice in the same prompt. Prompt
  policy settings expose the observed-condition decay, force-stop, cooldown, suppression, and
  evidence-label caps needed to tune this behavior in-game.
- **Dev event panel click split.** Def-backed event rows now left-click to fire the shown trigger and
  right-click to open the Def selector, while ordinary dev-panel text buttons ignore right-clicks.
  Selector choices now store the chosen Def id directly, and button titles mirror the selected
  menu label so right-click selection feedback matches the committed trigger.
- **Resurrected pawns keep writing.** Death pages now remain historical boundary entries without
  permanently ending a pawn's diary if RimWorld brings that same pawn load ID back to life. Capture,
  generation, Diary tab rendering, dev export/mock helpers, and hot/archive retention all ignore the
  old final-death cutoff while the pawn is alive again; a later death becomes terminal again.
- **Tuning and prompt settings tabs for XML overrides.** The mod-settings window now has
  **Main**, **Prompts**, **Styles**, and **Tuning** tabs so prompt text, writing styles, and low-level
  XML parameters no longer compete on the same page. Prompts contains **Shared/event prompts** for
  the existing shared system prompt and event-prompt overrides, plus **Prompt policy and weights** for
  template prompts, final instructions, template field lists, prompt switches/token caps,
  prompt-enchantment weights/cues, humor cues, event-window and observed-condition prompt weights,
  interaction-group instructions/tone variants/batch-promotion weights, and hediff-driven writing-style
  override policy. Styles contains writing-style label/rule/tag editing. Tuning exposes scalar and
  line-based list/table knobs from `DiaryTuningDef`, `DiarySignalPolicyDef`, and
  `DiaryContextReactionDef` (dedup windows, weather chance rows, ritual quality labels,
  mood-condition families, thought token/progression policy, ability/work sampling,
  weather/health/enchantment thresholds, mood/pain/opinion buckets, day/quadrum/arc reflection,
  scanner intervals, signal policies, context reactions). Tuning and Prompt policy use a compact
  two-pane editor with per-field checkbox/slider/numeric/text/list/table widgets, per-field and
  per-group reset, accent coloring for customized values, filtering, and rich tooltips. Overrides
  persist per player (`TuningOverrideStore`) and take effect immediately by writing into live Def
  fields or nested policy objects; pristine XML defaults, sentinel values, and `<null>` inherited-list
  markers are snapshotted for Reset. Follow-up hardening makes Def-backed prompt-policy groups register
  lazily after `DefDatabase` is populated (so humor cues are visible) and replaces visible translation
  key editors with resolved literal text override fields for event-window, observed-condition,
  prompt-enchantment, context, batch, and hediff prompt text. Prompt policy template prompt boxes now
  show only raw per-template overrides, not inherited shared prompt text, so the Shared/event prompts
  subpage remains the only place that displays and edits shared prompts; pure tests pin that menu split.
  Literal overrides win over their Keyed counterparts at generation time (for example a hediff
  enchantment's `descriptionOverrideText` now takes precedence over `descriptionOverrideKey`), and any
  override saved under a now-removed translation-key field name is pruned from settings on load, since a
  lookup key cannot be carried over into a literal-text field.
- **Pawn arc reflections implemented.** Added passion skill, psylink, xenotype, and royal-title
  progression pages plus rare yearly arc reflections from de-duplicated hot/archive memories. XML now
  owns templates, cadence, major-arc/high-stakes policy, and reflection grouping; fixtures, pure
  tests, and `PAWN_ARC_REFLECTION_IMPLEMENTATION.md` cover the flow. Follow-up hardening keeps annual
  arcs independent of day summaries and backs off low-memory forced retries.
- **Russian localization caught up for reflections.** Added missing Russian Keyed and DefInjected
  coverage for quadrum reflection, progression, and yearly arc prompts, with Russian prompt prose
  rewritten idiomatically instead of translated line by line.
- **Generated-text sanitizer hardened.** Cleanup now handles incomplete speech markers, common
  `speach` typos, unfinished bracket/reasoning tags, and leaked Unity rich-text tags before save/UI
  display.

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
