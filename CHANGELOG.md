# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state.

## 2026-06-26

- **Generation scan pressure reduced.** Pending-generation catch-up scans are now demand-driven and
  run at most every 200 ticks after load, delayed raids, or orphan recovery request one. Orphaned
  "writing..." recovery moved to a separate 600-tick pass, and the recurring scan no longer performs
  a full missing-title sweep; missing titles are swept on load and settings save, while successful
  main entries still queue their title follow-up immediately.

- **Active diary event cap added.** Settings now include a numeric hard cap for retained
  `DiaryEvent` records (default 1000). The game keeps the newest events, prunes older event refs
  from pawn diaries, and excludes trimmed pages from UI and background scans.

- **Workshop preview updated to human-made art.** Updated the preview image; thanks
  u/KyraDragoness for the provided art.

- **Queued generation now settles while paused.** Completed LLM results and their follow-up title or
  recipient requests are drained from the real-time update hook, while game-tick scanners stay tied
  to unpaused simulation ticks.

- **Workshop payload now ships source and reference docs.** `scripts/publish.ps1` now copies
  `Source/`, `DOCUMENTATION.md`, `CHANGELOG.md`, and `EVENT_PROMPT_MAP.md` into the generated
  `dist` mod while skipping transient build artifacts.

- **Documentation condensed for human maintainers.** `DOCUMENTATION.md` is now a shorter current-state
  guide with implementation playbook detail moved back to `AGENTS.md`, `EVENT_PROMPT_MAP.md`, code
  comments, and tests.

- **Event prompt policy can now target modded XML keys.** `DiaryEventPromptDef` lookup now falls
  back through source defName, interaction group, classifier key, and broad domain, so compatibility
  patches can add prompt text, enhancement text, and forced-model preferences in XML.

- **Runtime exception handling hardened.** Harmony hooks, diary ticks, save/load work, startup tab
  injection, and immediate-mode UI drawing now isolate diary failures with balanced cleanup and
  one-time logging, so capture or draw faults no longer break vanilla surfaces.

- **Mental-break diary card green softened.** Mental-break cards now use a muted sage accent with a
  lower-intensity wash and header rule.

- **SpeakUp interactions now route as promoted chitchat.** The built-in SpeakUp group matches
  `JPT.speakup` rows by source package, treats them as low-odds ambient social texture, and suppresses
  normal smalltalk routing while SpeakUp is loaded.

- **Tagged social-log grammar now renders under a reply-suppression guard.** Generated conversation
  text is used in prompts only when the optional guard is registered, with neutral fallback text
  otherwise.

- **Built-in writing styles retuned around author-inspired mechanics.** The 30 stock
  `DiaryPersonaDef` presets now use distinct mechanical rules, small synthetic examples, synced
  DefInjected English stubs, and the new default `spare-iceberg` fallback.

- **Diary tab unread marker skips world inspect panes.** The marker now verifies it is drawing on the
  Diary inspect tab and treats world-inspect UI as having no diary pawn, avoiding selector errors on
  world screens.

- **Thinking-model self-edit cleanup tightened.** Response cleanup now strips common reasoning-model
  self-revision transcripts while preserving legitimate in-world "Wait," prose, with parser tests for
  the reported leak shapes.

## 2026-06-25

- **Diary tab now appears on selected colonist corpses.** Startup registration adds the shared
  `ITab_Pawn_Diary` to humanlike corpse defs as well as live pawn defs.

- **Optional event-model routing added.** Event prompts can name a preferred configured model; valid
  matches are tried first and normal API failover still handles blanks, unknown models, or failed
  calls.

- **Diary tab unread marker added.** Inspect-tab mode now shows an XML-tuned marker when the selected
  pawn has newly finished diary pages, and opening Diary acknowledges it.

- **Diary tab is now the default surface.** Fresh settings use the normal pawn inspect tab by
  default, with the existing setting still able to return Diary to the bottom command button.

- **Dev-only diary-page regeneration added.** Expanded dev-mode cards can requeue a saved page,
  clear stale debug/title metadata, and regenerate the linked POV when it is still available.

- **Post-refactor review fixes landed.** Fixed stale diary-card height caching, validated unknown
  text-decoration kinds, shared API lane-pinning logic, removed small dead facade surface, and kept
  the wider save-model/settings refactors deferred.

- **Removed dead diary-bounds helpers.** Deleted three unused lookup helpers superseded by the live
  tick-based diary-bound checks; behavior and save data are unchanged.

- **`DiaryEvent` per-POV duplication collapsed into `PovSlot` slots.** Initiator, recipient, and
  neutral state now share one slot shape with facade properties preserving existing callers and flat
  Scribe keys.

- **Prompt enchantments split into collector/planner.** Live pawn reads now snapshot plain
  candidates, while pure planning handles weighted selection and formatting with XML-owned tuning.

- **Harmony patches split by capture domain.** The old monolithic patch file is now divided by
  gameplay area, with fragile manual registrations centralized in `DiaryPatchRegistrar`.

- **Diary tab per-frame cost capped for long histories.** Completed/pending counts are memoized by
  render token, card drawing is viewport-culled, and expanded-card height measurement is cached.

- **Diary tab visible-entry cache extracted.** Entry reuse, dev filtering, year pages, preview
  insertion, generating counts, and selected-year ordering now live in
  `DiaryTabVisibleEntriesCache`.

- **`DiaryTextDecorations` split behind a facade.** Matching, fact encoding, rich-text mutation, and
  decoration contracts now live in focused pure helpers while the public API stays stable.

- **Diary pipeline adapter moved out of the pure folder.** The impure bridge now lives beside
  generation code, leaving `Source/Pipeline` for pure prompt, response, decoration, and API helpers.

- **LLM parser speech-marker guard added.** Tests now ensure private response-parser speech sentinels
  stay in sync with the direct-speech parser defaults.

- **LLM request JSON serialization extracted.** Chat Completions and Responses request bodies now
  come from a pure builder with coverage for mode fields, trimming, escaping, and reasoning options.

- **Event store extracted from `DiaryGameComponent`.** Saved events and the id lookup index now live
  in `DiaryEventRepository`, keeping the existing Scribe key and load behavior.

- **`DiaryGameComponent.Generation.cs` split by pipeline stage.** Queue orchestration, dispatch,
  API lanes, eligibility, and interaction helpers moved into focused partial files with unchanged
  behavior and save data.

- **Persona and override state extracted from `PawnDiarySettings`.** Writing-style CRUD and prompt
  override dictionaries now live in dedicated settings stores while preserving original Scribe keys.

- **`PawnDiaryMod` settings UI split.** The mod entry point now delegates settings layout, API
  lanes, Prompt Studio, Persona Studio, shared widgets, and async connection state to focused files.

- **`DiaryContextBuilder` split by concern.** Pure line cleanup, translated bucket labels, and
  mood-impact classification moved into separate helpers; live Pawn/Map context collection remains
  in the builder.

- **Hardcoded tunables moved to XML.** Consciousness gates, stagger thresholds, mood-impact
  defName families, and intoxication matching now use XML-owned policy with code fallbacks.

- **Localized colony name moved off the saved model.** Neutral POV prompt naming now happens in the
  adapter, keeping `.Translate()` out of `DiaryEvent`.

- **Prompt instruction resolution moved off the settings save DTO.** Interaction instruction
  selection now lives with XML group classification instead of saved settings state.

- **Live-pawn fact capture extracted from the saved model.** `DiaryEvent` now stores plain captured
  values while `PawnFactCapture` handles live hediff, capacity, trait, and stagger reads.

- **API lane identity centralized.** Gate keys, cooldowns, duplicate checks, pinning, stale-result
  matching, and lane labels now use shared pure helpers with tests.

- **Public release metadata cleaned.** Workshop metadata, README copy, publish output reporting, and
  package-id handling were prepared for public release.

- **Translation readiness tightened.** English DefInjected stubs now cover new prompt instructions,
  quest/raid groups, and interaction tone variants.

- **Hidden per-entry humor cues added.** Eligible first-person entries can receive XML-tuned subtle
  structural humor cues matched to event stakes, without adding UI or settings.

- **Raid generation timing retuned.** Ordinary raids delay generation for anticipation context, while
  drop-pod raids and infestations bypass the delay and use dedicated prompt instructions.

- **Diary entry access can use the pawn tab again.** A setting can move Diary back into the normal
  pawn inspect tab while command mode keeps its underline and writing dots.

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
