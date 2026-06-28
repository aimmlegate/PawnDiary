# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state.

## 2026-06-29

- **Unified event ingestion behind a `DiaryEvents.Submit` bus.** Every captured event now enters
  through one front door and runs a single shared pipeline (`DiaryGameComponent.Dispatch`):
  guard → pure catalog `Decide` → consolidated dedup → `Emit`. Each source's capture+emit logic
  moved out of a bespoke `RecordXxx` method into a small uniform `DiarySignal` subclass under
  `Source/Ingestion/`; colony-wide sources use `DiaryFanoutSignal` so the colony dedup
  (peek-then-mark-after-first) lives in one place. The ~12 per-source dedup dictionaries collapse to
  one transient `recentEvents` store keyed by the existing raw source-prefixed keys. This is internal
  plumbing only — no `DiaryEvent` field, Scribe key, `interactionDefName`, or `gameContext` format
  changed, so saves load identically. Migrated so far: Thought, Inspiration, Ability (solo), Romance,
  MentalState (pair), Raid, MoodEvent, Ritual, PsychicRitual (fan-out); remaining sources migrate
  incrementally to the same pattern and coexist via the shared dedup store. See DOCUMENTATION.md §3.1
  and the §4 coverage table.

## 2026-06-28

- **Memory decay kept as prompt context.** `Alzheimers`, `Dementia`, and Anomaly `CrumblingMind`
  now use the regular memory-decay prompt enchantment only; the automatic lost-thread writing-style
  override was removed, while `CrumbledMind` keeps its stronger collapse style override.

- **Hediff prompt/style duplication fixed.** When an active hediff temporarily overrides the writing
  style, matching hediff prompt-enchantment candidates are now suppressed so localized condition
  guidance does not appear twice in the same prompt; unrelated prompt enchantments can still win.

- **External game/mod prompt text capped.** Imported RimWorld or mod text used in prompts, including
  hediff descriptions, live labels, DLC title/role labels, scenario descriptions, and quest
  descriptions, is now flattened to one line and capped at two sentences; Pawn Diary's own prompt
  XML/Keyed instructions and writing styles are left uncapped.

- **Russian prompt and event wording hardened.** Russian event facts, prompt instructions, persona
  examples, dev previews, and psy terminology now avoid gendered placeholder grammar, replace
  literal calques such as "psychocasts", and document the neutral-placeholder rule for future
  localization edits.

- **Russian UI wording reworked.** Visible Russian Keyed strings now avoid rough technical
  anglicisms in the diary tab, model-connection settings, prompt editor, persona editor, dev
  controls, and debug/error messages; the Russian glossary and localization notes now document the
  preferred UI terms.

- **Prompt suite context isolation fixed.** Dev prompt-suite captures now ignore live prompt
  enchantments, including active event-window context such as metalhorror suspicion, so one manual
  event-window test cannot bleed into later prompt fixtures.

- **Russian DefInjected coverage completed.** Prompt template field labels, prompt enchantment
  labels, condition writing-style override labels, and ritual-quality prompt context labels now have
  matching English stubs and Russian translations, clearing the Pawn Diary entries from RimWorld's
  Russian translation report.

- **Russian Workshop localization packaging added.** `scripts/publish.ps1` now builds Russian as a
  separate language-mod payload by default, excludes it from the main payload, writes translated
  Russian `About.xml` metadata, uses a Russian-localized Workshop preview, installs both payloads
  into the detected RimWorld `Mods` folder through junctions by default, and lets the localization
  Workshop id live separately.

- **Dev diary export added.** RimWorld Dev Mode now shows an "Export all diary pages" settings
  button that writes the current game's saved pawn pages and backing event records to a UTF-8 text
  file under RimWorld's save-data folder and copies the path to the clipboard.

- **Diary tab access hardened.** The inspect-tab draw path and command/link open helpers now
  re-apply Diary tab registration once after startup, and command-only access temporarily unhides
  the registered tab before calling RimWorld's inspect-pane opener.

- **Russian humor cues localized as Russian humor.** The hidden per-entry humor prompts now use
  Russian dry understatement, bureaucratic phrasing, household complaints, and gallows-practical
  patterns instead of literal English deadpan/punchline translations.

- **Russian localization copyedit pass.** The generated Russian text was tightened after the initial
  import: UI labels and tooltips are shorter, prompt instructions read as natural Russian instead of
  literal English calques, glossary-backed RimWorld terms were rechecked, and the intentionally
  rebuilt `DiaryPersonaDef` writing styles were left intact.

- **Russian translation added.** A full `Languages/Russian (Русский)/` set ships alongside the
  English source: every Keyed UI/prompt string and the initial DefInjected files for event prompts,
  event windows, humor cues, interaction groups, personas, and shared prompts. Game terms follow
  RimWorld's official Russian glossary (e.g. налёт, нашествие насекомых, серая плоть, монолит
  пустоты, Go-сок, люциферий, психиновый чай). The writing-style personas are **not** translated
  literally — each is rebuilt on a fitting Russian literary tradition with fresh synthetic examples
  and no author names. Key parity and `{0}`/`{PAWN}`/`{WORK}` placeholder integrity verified against
  the English source.

- **Prompt and title cleanup tightened.** Bad title follow-up responses with
  markup/control/schema characters now fall back to generic diary excerpts, while quest prompt facts
  reject placeholder names, humanize fallback names, and keep raw quest defNames out of generated
  text.

- **Condition-driven writing styles added.** `DiaryHediffPersonaOverrideDef` XML rules can
  temporarily force a writing style from active hediff defNames without changing the pawn's saved
  style, covering inhumanization, memory loss, joywire/bliss, mindscrew pain, and trauma-savant
  silence.

- **Birthday, heart attack, and prison-break events added.** XML event windows now record one-shot
  birthday and heart-attack entries for the affected pawn, plus map-scoped prison-break entries for
  eligible colonists on the affected map.

- **Event-window capture hardened after review.** The hot `Thing.SpawnSetup` and `AddHediff` signal
  paths now skip label resolution and per-signal rule allocation unless a window could match, via
  cached trigger rules and a pure `EventWindowPolicy.CouldMatchByDefName` pre-filter (covered by
  tests). Optional event-window recording is isolated so a failure there can no longer skip the
  established raid/hediff/quest capture, and the English DefInjected stubs for the new condition
  writing styles and the raid prompt/enhancement were brought back in sync with the source defs.

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
