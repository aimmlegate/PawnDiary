# Changelog

High-signal project history, newest first. Keep this file grouped by feature or milestone, not by
individual commit. Routine refactors, follow-up fixes, rebuilt DLLs, and assertion-count changes do
not need separate entries unless they change behavior or document an important risk.

## 2026-06-24

- **Psychic ritual capture moved to completion.** Anomaly psychic rituals now record from
  `LordToil_PsychicRitual.RitualCompleted` instead of graph end, avoiding start/transition
  captures, and ritual quality is passed to prompts as XML-tuned plain words rather than raw
  decimals. Ritual prompts now treat quality as indirect emotional/aftermath weight instead of
  naming the label directly.
- **Diary entry cards default to compact rows.** The newest entries no longer auto-expand into tall
  full-text panels; `autoExpandedEntryCount` now defaults to `0`, while click-to-expand behavior
  remains unchanged.
- **API auth headers made editable.** Fixed `api-key` and `x-api-key` auth choices were replaced by
  one custom-header mode that defaults to `x-goog-api-key`; old saved rows migrate to custom-header
  auth with their previous header name.
- **OpenAI-compatible Chat reasoning added.** Chat-compatible rows now expose the same Reasoning
  selector as Responses rows and send `reasoning_effort`, allowing Gemini's OpenAI-compatible
  `/chat/completions` endpoint to reduce or disable thinking when the selected model supports it.
- **Pawn ability-use events added.** Successful `Ability.Activate` calls now create cooldown-weighted
  solo diary candidates for the caster. Faster-cooldown abilities get lower capture odds, while
  rare long-cooldown abilities are more likely to be kept; prompts receive ability name, category,
  target, and cooldown fields.
- **Anomaly psychic ritual events added.** Successful `PsychicRitualGraph.End` completions now
  generate solo entries for invoker, target, participants, and spectators without sending ritual
  role/title fields. The new Ritual-domain XML group uses the dark color cue, and invoker prompts
  require one unsettling `[[speech]]...[[/speech]]` block with invented or incomprehensible ritual
  words; the display formatter supplies the visual distortion.
- **Ideology ritual events added.** Finished, non-canceled `LordJob_Ritual` outcomes now generate
  separate solo entries for author, target, participants, and spectators, with role/title context,
  both Royalty title and Ideology role status fields, and perspective-specific prompt instructions.
  Ritual policy includes DLC-safe XML edge groups for Royalty throne/anima rituals, Biotech
  childbirth, and Odyssey gravship launch/flight/landing flavor.
- **Important-event status context added.** Prompt enchantments now use the generic `important
  context` field and can rarely add one weighted DLC-safe status cue for important events: Royalty
  title or Ideology role. The cues share the existing one-candidate picker, so royal and ideology
  status cannot both appear in the same prompt.
- **Live hook validation workflow documented.** `DOCUMENTATION.md` now records the RimBridge/GABS
  prompt-test-mode procedure for validating real Harmony hooks, including expected capture logs,
  safe debug actions, and common false negatives.
- **Live event auto-test scenario documented.** The validation notes now include a phase-by-phase
  scenario for every implemented event source and route shape, including quest accepted/completed/
  failed lifecycle checks, raid fan-out, Tale/death routes, hediff/day-reflection routes, scanner
  sources, and the need for a dev event dump or helper when log-only evidence is too weak.
- **GABS live smoke fixture added.** `scripts/gabs/pawndiary-live-smoke.lua` runs a compact
  script-agent mental-state hook check through `rimbridge/run_lua_file`, keeping the live-game
  transcript to one bounded result instead of many chat-visible tool calls.
- **Harmony startup hook restored for RimWorld 1.6.** Generated Social-log speech display now
  resolves `PlayLogEntry_Interaction.ToGameStringFromPOV_Worker` with an old-name fallback, avoiding
  the `PatchAll` startup failure that could leave later real event hooks unregistered.
- **API lane edge cases fixed.** Cooldown failover now snapshots lane readiness once per request so
  timing changes cannot skip every lane, `No auth` lanes ignore stale saved key text for lane
  identity, settings saves prune cooldowns for removed/reconfigured rows, `key=` query auth replaces
  existing keys while preserving URL fragments, and prompt variant hash seeds are always
  non-negative.
- **Personas reworked into writing styles.** Player-facing settings, per-pawn picker text, prompt
  defaults, and prompt-lab docs now frame the feature as writing styles rather than chat personas.
  The shipped catalog was retuned toward prose mechanics (rhythm, sentence shape, imagery, emphasis,
  and detail choice), and the system prompt wrapper now tells models not to roleplay a chat persona,
  add catchphrases, or invent dialogue. Internal `DiaryPersonaDef`/save keys remain unchanged for
  compatibility.
- **Writing-style presets rewritten as diary habits.** Built-in style rules now describe how each
  pawn tends to write notes: what they notice first, how they pace sentences, what they omit, and how
  they compress feeling into prose. Preset labels/rules were fully refreshed while preserving
  existing DefNames and theme tags for save compatibility and first-roll weighting.
- **Writing styles separated for small models.** The stock catalog now gives each preset a stronger
  mechanical signature — sentence count/order, hard stops, fragments, questions, repeated words,
  ledger clauses, body logs, silence lines, or time-fold cues — so smaller local models are less
  likely to collapse nearby styles into the same diary output.
- **API lane routing hardened for free-tier pools.** Connection settings now include a global routing
  mode (Balanced, Prefer top rows, or Failover only), compact arrow controls to reorder API rows, and
  per-row auth styles for Bearer, no auth, `api-key`, `x-api-key`, or `key=` query providers.
  Transient lane failures and timeouts now apply automatic runtime cooldowns with exponential
  backoff, while successful responses clear the lane cooldown. Prompt-lab gained matching
  `--auth-mode` support.
- **Tone pools enabled for stock prompts.** Every tone-bearing interaction group now ships two
  distinct `tones` variants, and every first-person prompt template renders the `tone:` field so the
  selected tone reaches the model. Prompt-lab `--all-variants` now crosses tone variants alongside
  instruction variants and prompt-enchantment variants, with coverage checks for both pools.
- **Prompt variants tightened against hallucinated facts.** Reworded shipped instruction variants so
  they no longer ask small local models for exact words, witnesses, aftermath, or other facts unless
  the supplied context carries them. Prompt-lab now parses XML `instructions` / `tones`; generated
  all-variant fixtures cross instruction/tone variants with the prompt-enchantment matrix and the
  coverage check verifies every configured variant renders at least once.
- **Per-event-type prompt variation.** Interaction groups can now carry an `instructions` variant
  pool (and, optionally, a `tones` pool) alongside the singular `instruction` / `tone`; one wording
  is chosen per entry so the Nth raid no longer reads identically to the first. Instruction variants
  roll once at capture and are persisted on the entry; tone variants (when used) pick
  deterministically by event id. Seeded selection lives in a new pure helper
  (`Source/Generation/PromptVariants.cs`, tested in `tests/PromptVariantsTests`), keeping the pure
  prompt renderer and the prompt-lab golden harness untouched. Eight high-frequency groups (romance,
  insults, small talk, mental breaks, combat/death, raids, positive/negative thoughts) ship 3
  **lens-distinct** instruction variants each — different sensory/narrative/temporal entry points
  (concrete nouns, not emotional synonyms) so small models actually separate them rather than
  collapsing near-synonyms to one output. The nine toneless groups gained distinct singular tones,
  and two duplicate tones across distinct event types (raids vs. disasters; anomaly vs. strange chat)
  were differentiated.
- **Save compatibility documented.** Add/remove safety is now explicit in player-facing metadata and
  persistence docs: Pawn Diary records only self-contained diary history, does not attach gameplay
  defs/components to pawns or maps, and calls out the old generated Social-log injection caveat.

## 2026-06-23

- **Settings window compacted.** Request tuning now lives inside the expanded connection section,
  visible helper paragraphs were removed, Prompt Studio is collapsible with system and event prompts
  sharing one selector/editor block, persona presets now edit inside one highlighted block, and the
  nonworking generated-speech Social-log injection toggle/path is hidden and forced off.
- **Diary entry point moved to pawn selection.** The Diary inspect tab remains the same UI internally,
  but its inspector tab-strip button is hidden; selecting one eligible pawn or colonist corpse now
  shows a Diary command button with a custom journal-and-pen icon that opens or closes the same tab.
- **Mod icon added.** `About/ModIcon.png` and the texture-backed `modIconPath` now use a larger
  journal-and-pen mark for RimWorld's mod list while leaving the Workshop preview image unchanged.
- **Release payload includes the mod icon.** The publish script now carries `About/ModIcon.png`
  into `dist` alongside the existing Workshop preview assets.
- **Release payload includes runtime textures.** The publish script now carries the command-icon
  texture folder into `dist` so published builds use the current Diary button icon.
- **Generated tag sanitizer added.** Saved LLM output now preserves valid
  `[[speech]]...[[/speech]]` blocks while repairing malformed speech closers and stripping or
  flattening hallucinated bracket tags from small local models before diary text is persisted.
- **Prompt lab coverage catches XML prompt drift.** Static arrival/death fixtures now render their
  `DiaryEventPromptDef` prompt/enhancement fields, generated contexts cover Romance/Raid/Quest
  markers, and `npm test` checks all XML template/event prompt types plus all-variants pass coverage.
- **Thinking-model response parsing fixed.** Typed visible output now beats flattened reasoning text,
  more reasoning part types are skipped, and blank Ollama messages can fall back to root `response`.
- **Prompt Studio can edit event-source guidance.** Mod settings now expose each
  `DiaryEventPromptDef` event type through a selector and save per-event `prompt` / `enhancement`
  overrides while preserving XML as the default catalog.
- **Review hardening pass.** Fixed save/load day-reflection dedup, settings endpoint editing, capped
  HTTP response reads before JSON parsing, avoided hot-path PlayLog grammar rendering before capture
  eligibility, added one-time reflection warnings, made diary lookups tolerate null records, throttled
  live-name highlight rebuilding, centralized Anomaly creepjoiner reads in `DlcContext`, avoided direct
  Royalty suppression type handling, and removed an English prompt connector from death context facts.
- **Day reflections require an XML-controlled important signal.** End-of-day summaries now drop
  filler-only days by default: ambient small talk and passing moods can still color a reflection,
  but only after an XML-configured important signal kind happened to that pawn. The default
  `daySummaryImportantSignalKinds` are `event`, `opinion`, and `hediff`; adding `filler` in XML
  intentionally allows quiet filler-only summaries again.

## 2026-06-22

- **Diary prose nudged toward immediacy.** First-person prompts now ask for one concrete sensory
  detail, one emotional beat, and one implied consequence/tension from supplied facts, and event-type
  enhancements were sharpened for more vivid but still grounded entries.
- **Prompt structure split for finer event control.** Shared system prompts were shortened to global
  essentials, and broad event-source guidance moved into `DiaryEventPromptDef` rows rendered as
  separate `event prompt:` and `event enhancement:` fields before per-group `instruction:` flavor.
- **Quest accept capture hardened.** Accepted-quest capture now keeps the direct `Quest.Accept`
  hook, patches RimWorld's generated `MainTabWindow_Quests` accept action defensively, and scans
  `Quest.EverAccepted` every 120 ticks. The scan catches accepted-state transitions even if a UI or
  modded accept path skips both Harmony hooks.
- **Quest lifecycle group recovery fixed.** Saved quest entries keep the real quest script defName,
  while XML Quest groups match lifecycle signals; display and prompt-policy recovery now classify
  Quest entries from the saved `signal=` field so accepted/completed/failed entries retain their own
  label, importance, and tone instead of falling through to the failed-quest group.
- **Raid event source added (minimal realization).** `IncidentWorker.TryExecute` (filtered to
  `IncidentWorker_Raid`) now fans out one solo diary entry per eligible colonist on the raid's target
  map. Payload is intentionally minimal: incident defName, raider faction defName, and raid points.
  New `Raid` group domain with a catch-all "Raids" group plus a "Friendly arrivals & raids" subset.
  Colony-level dedup keys by incident/map/faction/points (`raidDedupTicks`).
- **Quest event source added (rich context).** `Quest.Accept` and `Quest.End` hooks now record the
  full quest lifecycle. Only accepted quests are recorded (offered-but-not-accepted quests are
  ignored). `QuestEndOutcome.Success` -> "completed", `Fail` -> "failed"; each signal gets its own
  prompt group and tone. Rich context — quest description (capped at 600 chars), issuer faction
  defName, and an item-rewards summary scanned from `QuestPart_DropPods` (capped at 300 chars) — is
  embedded in the localized event text and the `quest=` game-context marker. One entry per eligible
  colonist per signal; dedup keys by quest id + signal (`questDedupTicks`).
- **Diary tab made taller.** Default tab height raised (650 to 800) so more entries fit without
  scrolling; tunable via `tabHeight` in `DiaryUiStyleDef.xml`.
- **Dev-only copy button on entry cards.** A subtle copy icon at the right edge of each card header
  (dev mode only) copies that card's text to the clipboard — the captured prompt for prompt-only
  cards, otherwise the generated text.
- **Copy button moved to bottom-left.** The dev copy badge now sits at the bottom-left of expanded
  cards only (removed from collapsed), rests at ~0.5 alpha and brightens on hover, and reserves a
  dev-only footer so it clears the model-name line.
- **Prompt test suite reworked to a dropdown.** The dev-mode "Prompt suite" button now opens a
  dropdown of event categories (data-driven from a single registry, so new categories auto-appear);
  selecting one captures exactly one plain, undecorated prompt-only card and replaces any previous
  selection. A new "Clear test prompts" button deletes every prompt-test entry.
- **Dev prompt test mode added.** RimWorld dev mode now reveals a mod-settings switch that captures
  real event system/user prompts as prompt-only diary cards while skipping all LLM generation.

## 2026-06-21

- **Diary formatting previews added.** Dev-mode Diary controls now include transient preview buttons
  for plain prose, markdown, speech blocks, combat pages, social fights, mental-break fractured
  pages, extreme darkness, linked cards, writing/title animations, death/memorial pages, staggered
  speech, and strange-chat distortion without saving mock events.
- **Staggered speech made stronger.** Intoxication/anesthesia speech decoration now uses the maximum
  staggered word-size intensity so the effect is visible in the Diary tab.
- **Conflict diary cards now read as conflict.** Combat-cued entries use a stronger hostile page
  wash/header rule, social-fight entries use a distinct orange conflict wash, and simulated preview
  chips show the selected sample type.
- **Mental-break diary cards look more broken.** Fractured pages now use larger staggered offsets,
  alternating italics, stronger spacing, and a mental-break page wash/header rule.
- **Dark and strange speech split apart.** Extreme-dark direct speech now uses a dimmed-word
  treatment, while strange-chat speech keeps the Zalgo glyph distortion.
- **Pawn names now stand out in diary prose.** Rendered diary text highlights live humanlike pawn
  names with status-aware colors, falling back to bold when a name cannot be colored safely.
- **Event Catalog completed for current live sources.** The capture decision layer now covers
  Thought, Inspiration, MoodEvent, MentalState, Tale, Hediff, Interaction, Romance, Arrival, Death
  fallback, Work, ThoughtProgression, and DayReflection. Remaining direct event writers are route
  sinks such as batch flushers, ambient notes, generation queues, and event factories.
- **Catalog outcomes now model real routes.** `CaptureDecision` supports solo, pair, batch, ambient,
  day-reflection, neutral death-description, and neutral arrival-description outcomes. This lets the
  pure layer choose the event shape while RimWorld adapters keep side effects at the edge.
- **Romance relation events added.** Lover, Spouse, ExLover, and ExSpouse relation changes create
  pairwise diary events through a dedicated Romance XML domain and catalog source.
- **Event migration hardening.** Work sampling no longer consumes cooldown/RNG before eligibility,
  signal, ignored-work, and user group gates. Catalog dispatch tests now exercise every registered
  Spec wrapper, not only direct `XxxEventData.Decide` reducers.
- **Synthetic crafted/relic events removed.** Vanilla Tale capture already covers crafted art
  quality, so redundant crafted-quality/relic-install hooks and XML/keyed dead entries were deleted.
- **Startup and eligibility safety tightened.** Gameplay recorders no-op until RimWorld is in play,
  first-person ownership/generation respects minimum biological age, and neutral arrival/death pages
  keep their special generation routes.
- **Save/load, performance, and parser reliability improved.** Prompt/debug metadata, title recovery,
  generated-speech PlayLog references, per-pawn event refs, diary bounds caches, live-pawn lookup
  caches, context parsing, MiniJson validation, and provider response parsing were hardened.

## 2026-06-19

- **Generated speech Social-log injection added.** Completed initiator diary entries can inject one
  generated direct-speech row into RimWorld's Social log when enabled, using the same parser as the
  Diary UI and avoiding feedback into Pawn Diary capture.
- **Direct-speech cleanup hardened.** Closed `[[speech]]...[[/speech]]` blocks are preserved during
  local response cleanup, generated speech maps prune stale PlayLog rows, and injection paths log
  useful dev diagnostics.
- **Known issue.** Injected Social-log rows are added to `Find.PlayLog` and get `LogID`s, but may not
  appear in RimWorld's Social tab UI; the remaining gap appears to be Social-tab enumeration/filtering.

## 2026-06-18

- **Beta release and publishing flow prepared.** Workshop metadata, preview art, README, publish
  script behavior, package-id cleanup, and local verification hooks were refreshed.
- **Prompt/persona system retuned.** Persona rules, few-shot examples, weather mentions, prompt
  studio, prompt lab, prompt enchantments, and atmospheric formatting were moved toward XML-owned,
  testable policy with better small-model behavior.
- **Pipeline extracted into pure contracts.** Prompt planning, response postprocessing, response
  parsing, text decorations, domain recovery, and related tests moved into standalone pure helpers.
- **UI and settings matured.** Diary tab caching, year paging, card polish, title generation,
  temperature/API controls, and XML-owned visual/text decoration policy were added.
- **Health, combat, and social capture expanded.** Hediff groups, hediff progression, combat tale
  batching, insult batching, direct-speech POV rules, and important health/capacity cues were added.

## 2026-06-17

- **LLM compatibility broadened.** Added OpenAI-compatible Chat Completions, OpenAI Responses, and
  native Ollama Chat modes, plus reasoning/thinking output cleanup and debug raw-response views.
- **Diary architecture split and hardened.** The Diary tab and helper code were split into focused
  partials/helpers, prompt guardrails were tightened, and prompt lab/runtime policy were kept in sync.
- **Event coverage expanded.** Inspiration, pregnancy/labor, modded health signals, insult sprees,
  important health cues, and discovery-quarantine behavior were added or refined.

## 2026-06-16

- **Core experience matured.** Work entries, prompt lab support, title generation, UI readability,
  social/work generation weights, pending recovery, LLM retries/failover, batching, day reflections,
  and save/load robustness were added.

## 2026-06-15

- **Diary gameplay coverage broadened.** Arrival and death chronicles, personas, DLC-safe context
  patterns, broader event routing, localization coverage, and repo agent guidance were added.
- **Diary UI established.** The pawn Diary tab, context builders, and early event display/generation
  flow became the main user-facing surface.

## 2026-06-14

- **Background generation and XML policy began.** Generation moved into background request flow,
  pawn eligibility tightened, personas were introduced, and prompt/event policy started moving out
  of hardcoded C# into XML.

## 2026-06-13

- **Initial diary system.** Introduced the base diary event model, generation path, UI surface, and
  RimWorld integration.
