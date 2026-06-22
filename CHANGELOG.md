# Changelog

High-signal project history, newest first. Keep this file grouped by feature or milestone, not by
individual commit. Routine refactors, follow-up fixes, rebuilt DLLs, and assertion-count changes do
not need separate entries unless they change behavior or document an important risk.

## 2026-06-23

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
