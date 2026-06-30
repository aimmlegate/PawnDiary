# Changelog

Milestone history of Pawn Diary, newest first. Grouped by milestone, not by commit; routine
refactors, rebuilt DLLs, and follow-up fixes are folded into the feature bullet they shipped with.
Companion: [DOCUMENTATION.md](DOCUMENTATION.md) describes the current state.

## 2026-06-30

- **Maintainer documentation reworked.** `DOCUMENTATION.md` now has a human-readable top-level mod
  flow with a Mermaid diagram, plus tighter XML policy, prompt, save-compatibility, and localization
  sections that keep the high-signal rules without the historical text dumps.
- **Event prompt map replaced with current-state Mermaid diagrams.** `EVENT_PROMPT_MAP.md` now maps
  the event listeners, capture decisions, prompt policy lookup, template selection, prompt
  enchantments, overrides, and active weights from the current code and XML.
- **Quest acceptance no longer generates diary pages.** Quest accept hooks now only mark accepted
  quests as seen and feed generic event-window policy; diary entries are generated for completed and
  failed quest outcomes. Quest outcome prompts now frame resolution or loss as colony effort instead
  of implying the POV pawn personally performed the quest work.
- **Rare quadrum reflections added.** Pawns can now write one longer reflective page near the end of
  a quadrum when they have enough important entries. The timing is deterministically spread across a
  3-day window per pawn/quadrum, the ordinary day reflection is skipped when the quadrum page fires,
  the prompt uses dated highlights, the UI gets a distinct quadrum-reflection cue, and
  `DiaryEventPrompt_QuadrumReflection.forcedModel` can pin these longer pages to a specific active
  model. XML tuning defaults require 6 important entries, send at most 8 highlight events to the
  prompt, and allow a 350-token response.
- **Diary tab unread marker moved.** The inspect-tab unread marker now draws at the top middle of
  the Diary tab, with a wider XML-tuned draw box clamped inside the vanilla tab width.
- **Random generation weight simplified.** The settings page now has one shared random-generation
  slider instead of separate work and social sliders. It scales optional chance-gated diary pages
  for work sampling, batched social promotion, and ability-use sampling; old work/social saved
  values are migrated into the new setting on load.
- **Diary page rewrite action moved into normal UI.** Expanded non-archived diary pages now show a
  subdued rewrite icon beside the model/provenance footer, using a Def-backed tint and a pencil-style
  icon instead of the brighter dev-only reload control. Archived compact pages still hide rewrite
  because their retry state is intentionally discarded.
- **Dev event test panel added.** RimWorld dev mode now exposes `Pawn Diary > Event test panel...`
  in Debug Actions. The panel selects an eligible colonist and optional partner, then offers real
  vanilla trigger buttons for common diary sources: thoughts, inspirations, mental states, tales,
  hediffs, game conditions, social log entries, romance relations, arrivals, deaths, raids, quests,
  abilities, work scans, thought progression, and day reflection. It also keeps the prompt-only
  fixture batch/clear tools, and the prompt suite now covers every registered diary event source,
  including arrival, death, thought progression, raid, quest, ritual, and ability. The former Diary
  tab dev action strip moved into this panel too, including mock history fill, persona selection, and
  transient card-format previews. The panel is now split into Events, Diary, and Fixtures sections
  and saves its selected pawn, partner, active section, scroll positions, selected real-trigger Defs,
  and selected prompt fixtures with the current game. The Events section can now change the Def fired
  by the thought, inspiration, mental-state, tale, hediff, game-condition, interaction, relation,
  incident, quest, and ability triggers. The Diary section now also has a dev-only purge button for
  removing the selected pawn's compact archived pages without touching hot diary entries.
- **Split active and archived per-pawn retention settings.** The settings page now has separate caps
  for full hot diary pages (`maxActiveDiaryEvents`, default/range cap 100) and compact archived pages
  (`maxArchivedDiaryEvents`, default 10000). Archive retention keeps each pawn's newest compact rows,
  supports 0 archived rows for players who want cold pages purged, and shares the normal retention
  cache invalidation path on save/load/settings apply. Existing settings above 100 are clamped down,
  compacting older hot pages into archive rows on load/save instead of keeping thousands of full
  `DiaryEvent` records per pawn.
- **Review hardening pass (combined bug/perf/security review).** A multi-source review pass.
  - **API key privacy (S1).** API keys are now masked in the settings window by default, with a
    per-row **Show/Hide** toggle (`PawnDiaryMod.ApiLanes.cs`); a key is never rendered in cleartext
    until the player reveals that row. `key=`/`token=` query parameters and `Bearer <token>` values
    are redacted from every log line and from player-visible generation errors (new
    `ApiLaneLabels.RedactSecrets`, applied in `TrimForLog` and on the result error in `LlmClient`), so
    a networking message that echoes the request URL can no longer leak the key. Query-parameter auth
    now shows a caution tooltip recommending header-based auth.
  - **Hot-path classification memoized (P1/P2/P3).** `InteractionGroups` now caches classification
    results by `Def` and by `domain+defName` (`classifyByDef`/`classifyByDomainName`, rebuilt with
    `cachedAll`), collapsing the per-call O(groups) `Matches` scan to a dictionary lookup. This runs on
    the `PlayLog.Add` hot path (every logged interaction classifies 2–3×) and in every periodic capture
    scan (the hediff scan was O(colonists × hediffs × groups)). `HasTaggedLogGrammar` — a recursive
    rule-pack walk that allocated a `HashSet` per captured interaction — is now memoized per
    `InteractionDef`.
  - **O(1) diary-record lookup (P5).** Added `diariesById`, a pawnId→record index mirroring the event
    store's index, so `FindDiary`/`FindDiaryByPawnId` no longer linear-scan every record (including dead
    colonists) on each captured event. Rebuilt in PostLoadInit; kept in sync on create (`diaries` is
    append-only).
  - **Shared per-tick colonist snapshot (P4).** `SnapshotFreeColonists` caches its copy for the current
    tick so the several scans that fire on independent timers share one allocation when they coincide;
    reset on each `Game` construction since `TicksGame` can repeat across games. (The diary tab's
    name-highlight rebuild, P6, was reviewed and left as-is — already 250-tick cached and tab-open only.)
  - **Correctness fixes (lows).** Failover lanes are now snapshotted with `.Copy()` when stamped onto a
    request (C1), so a mid-flight settings edit can't tear the values the background HTTP worker reads.
    The arrival page now reliably sorts as the oldest entry and the death page as the newest on a tick
    tie via a new `DiaryEntryView.BoundaryRank` (C2), so a game-start thought can't render above the
    arrival page. `StripOrphanClosingReasoningTags` now removes *every* stray closing reasoning tag of a
    name, not just the first (C3). `DiaryObservedConditionDef.ConfigErrors` flags the
    `includeHomeMapsOnly`+`includeNonPlayerMaps` conflict instead of silently preferring home maps (C4).
    A shared surrogate-safe `TextTruncation.SafePrefix` replaces raw `Substring(0, n)` caps across
    prompt/UI/evidence/raw-response truncation so an emoji/astral glyph at the cut is never half-sliced
    (B1). The event-id index now uses `OrdinalIgnoreCase` to match the retention sweep's id set (B2).
  - **Polish.** The `Pawn.Kill` postfix now pre-filters on death-page eligibility (humanlike colonist)
    before building/submitting the fallback signal, so the common animal/raider/mech kill does no bus
    work (D1). The dev-only LLM debug block's field labels are now localized Keyed strings rather than
    hardcoded English (D2).

- **Lasting game-state capture: observed conditions (Plan 12).** Added a system, parallel to event
  windows, for *lasting* colony states that must be read from live state rather than inferred from a
  guessed duration. A new pure lifecycle diff (`Source/Capture/ObservedConditions/`:
  `ObservedConditionObservation`/`StateSnapshot`/`DefSnapshot`/`Decision`/`Plan`/`Policy`) decides
  start/refresh/end purely from "is it in this scan's observations?", with `startDebounceTicks` /
  `endDebounceTicks` gating recording only — never truth. The impure edge
  (`Source/Core/DiaryGameComponent.ObservedConditions.cs`) polls each enabled
  `DiaryObservedConditionDef` on its own `pollIntervalTicks` (checked on a short global gate),
  snapshots live state into plain DTOs, applies the plan, persists active rows
  (`ActiveObservedConditionState`, additive Scribe key `activeObservedConditions`), and exposes prompt
  candidates beside the event-window ones. Ticks gate debounce only, so a save loaded mid-state is
  rediscovered by the next scan and a missed signal is recovered by scanning. Four observer types,
  all DLC-safe (plain-string / vanilla-API matchers that find nothing when content is absent):
  **MapDanger** (home-map `dangerWatcher.DangerRating` / spawned-hostile thresholds), **GameCondition**
  (exact `gameConditionManager.ActiveConditions` defName match), **ThingPresent** (indexed
  `ListerThings.ThingsOfDef`, observable evidence only), and **PawnHediff** (pawn-scoped, *visible*
  hediffs only so hidden mechanics are never surfaced); `RecentEvidence` is reserved and currently a
  no-op. Shipped defs in `1.6/Defs/DiaryObservedConditionDefs.xml`: `MapThreatActive`,
  `ToxicFalloutActive`, `SolarFlareActive` (enabled, prompt-tone only) and `AnomalyGrayFleshEvidence`
  (enabled, Anomaly-gated, records one observable "found gray flesh" page and strongly biases prompts
  while the evidence is physically present). **Retired the `MetalhorrorSuspicion` event window** —
  whose fixed timeout could never prove the suspicion was still unresolved and whose `ThingSpawned`
  start signal left it effectively always active — replacing it with `AnomalyGrayFleshEvidence`;
  removed its def and EN/RU Keyed + DefInjected strings. Shipped `MetalhorrorEmergence` (PawnHediff)
  **disabled** with empty matchers pending verification of the observable post-emergence state, so it
  can never reveal hidden mechanics. Added EN + RU Keyed/DefInjected strings, a new pure test project
  `tests/DiaryObservedConditionTests/` (62 assertions: start/refresh/end debounce, no duplicate start,
  save/load rediscovery, stale-Def drop, per-map and per-pawn independence) wired into
  `.githooks/verify.ps1`, documented the system in `DOCUMENTATION.md §5.1` (plus §4/§6/§9/§12 notes),
  and rebuilt `1.6/Assemblies/PawnDiary.dll`. Plan 12 Pass 8 (XML context-fact bridge) stays blocked
  on the not-yet-built XML context-fact pipeline.

- **Observed conditions: review hardening (follow-up to Plan 12).** Made observed-condition page
  recording transactional so a start/end page can no longer be permanently lost: the adapter now tries
  to write the page *before* committing the saved-state change, and if no eligible recipient pawn was
  available it leaves `startRecorded`/`endRecorded` false and retains the row, so the next scan
  re-enters the transition instead of dropping it; the per-phase dedup key is consumed only after at
  least one page is actually written (`RecordObservedConditionPage` now returns a satisfied/retryable
  signal, matching the split `IsRecentlyRecorded`→`MarkRecentlyRecorded` pattern the event-window
  recorder already uses). The `PawnHediff` observer now skips the pawn/hediff scan when its matcher
  lists are empty (not just null — the Def initializes them non-null), avoiding a pointless walk on an
  enabled-but-unconfigured def. Added `DiaryObservedConditionDef.ConfigErrors` load-time validation
  that rejects `recordScope=SubjectPawn` without `scope=Pawn` (the mismatch clears the subject id and
  would otherwise never record a page). Corrected the `ActiveObservedConditionState.scope` save-comment
  (Scribe persists enums by name, not int) and the stale header date in
  `ARCHITECTURE_IMPROVEMENT_PLAN.md`. No XML, Scribe keys, or pure-policy behavior changed; pure tests
  still pass (62 assertions) and `1.6/Assemblies/PawnDiary.dll` rebuilt.
- **Continued Diary UI renderer/measurer extraction (Plan 9).** Moved expanded-card height
  measurement and its render-token/width/debug/highlight invalidation cache into
  `DiaryEntryCardMeasurer`, and routed expanded-card painting through an explicit renderer request so
  `FillTab` keeps scroll virtualization, expansion state, and GUI group lifetime while card drawing
  lives with the entry-card helpers. Behavior and visuals are intended unchanged; the remaining
  validation is in-game Diary tab smoke because the extracted code depends on Verse/Unity IMGUI text
  measurement. Rebuilt `1.6/Assemblies/PawnDiary.dll`.

- **Save/settings compatibility fixtures (Plan 6).** Turned the save-model post-load repair path into
  fixture-covered, regression-detectable code. The pure parts of `DiaryEvent.NormalizeOnLoad` and
  `ArchivedDiaryEntry.NormalizeOnLoad` (null-coalesces, the cross-slot surroundings chain where a pair
  event's recipient borrows the initiator's surroundings, the neutral-chronicle text merge, the legacy
  `gameContext`/`instruction` rebuild, year extraction, and defensive clamps) moved into a new pure
  helper `Source/Pipeline/DiarySaveNormalization.cs`. The impure steps stayed on the save models:
  fresh-`eventId` GUID minting, `DefDatabase`-backed `ResolveColorCue`, the Scribe read/write
  round-trip itself, and settings clamp/rebuild. Behavior is unchanged; **no Scribe keys were
  renamed**. Dead private wrappers (`DiaryEvent.EmptyIfNull`/`NormalizeLoadedStatus`/
  `NormalizeLoadedTitleStatus`, and `ArchivedDiaryEntry.ExtractYear` — its two call sites now route
  through the pure helper) were removed. Added `tests/DiarySaveNormalizationTests/` (46 assertions)
  covering the Plan 6 fixture inventory (pre-title, failed, pending archived fallback candidate, pair
  event with recipient state, neutral arrival/death merge, legacy-field rebuild, missing/blank-field
  repair) and wired it into `.githooks/verify.ps1`. Plan 6 Step 4 chose **Option B** for the Scribe
  round-trip itself: added `tests/SAVE_COMPATIBILITY_SMOKETEST.md`, a repeatable in-game smoke
  runbook for the parts that cannot be pure-tested (real Scribe XML round-trip, legacy
  persona/prompt/group settings repair, color-cue resolution, GUID minting). Documented the stable
  Scribe-key contract and migration pattern in `DOCUMENTATION.md §9` (new §9a/§9b/§9c) and added the
  new test project to the doc test list. Rebuilt `1.6/Assemblies/PawnDiary.dll`.

- **Hardened archive compaction after review.** A failed/stale page now keeps the same body and title
  after it is compacted: the shared, pure `DiaryArchiveFallback` resolver derives the fallback fact from
  the saved prompt at archive time and bakes it into the archived `text`, and the Diary card re-resolves
  from that same text once the prompt is gone (previously archived stale pages silently fell back to raw
  event text, which could differ for death/arrival pages). The dev prompt-suite reset
  (`ClearPromptSuiteForDev`) now also drops matching archived rows via `DiaryArchiveRepository.RemoveForEventIds`,
  so a generated-then-compacted test entry can no longer survive as an orphan. The tab's hot↔archive
  dedup now claims a hot POV key even when the hot event is momentarily hidden, preventing a stray
  duplicate card. Overflow trim selection moved into the pure, tested `DiaryArchiveCompactionPlanner`,
  and the retention loop now stages archive rows and commits the write only for refs it actually drops.
  Added `DiaryPipelineTests` coverage for the fallback round-trip and overflow selection. Rebuilt
  `1.6/Assemblies/PawnDiary.dll`.

- **Fixed Diary tab loading flicker during interaction.** Same-year refreshes triggered by completed
  generation or title updates now rebuild selected-year row layout in place once that year has already
  rendered, so scrolling and expand/collapse interactions no longer briefly swap the card list for the
  loading panel. Cold opens, uncached pawn switches, and uncached year loads still show progress.
  Rebuilt `1.6/Assemblies/PawnDiary.dll`.

## 2026-06-29

- **Implemented real archive compaction for old diary events.** Added compact per-POV
  `ArchivedDiaryEntry` rows saved under the new `diaryArchiveEntries` key and owned by
  `DiaryArchiveRepository`. The per-pawn cap now means hot `DiaryEvent` refs: old completed pages, stale
  attempted pages, and failed pages with raw display text are copied to the archive before their hot ref
  is removed; still-active pending/not-generated refs remain hot. The Diary tab's sliced year index
  merges hot and archived candidates, render tokens include archive counts, archived arrival/death rows
  still define diary lifespan, Social-log entry lookup can open archived pages via compact PlayLog ids,
  and dev export includes archived pages. Archived rows do not regenerate and never enter LLM retry,
  title catch-up, orphan recovery, day-summary evidence, work cooldown, or prompt-continuity scans.
  Prompt-only dev capture rows intentionally stay hot because the compact archive discards full prompt
  text. Archive/drop eligibility lives in the pure `DiaryArchiveEligibility` helper, with focused
  `DiaryPipelineTests` coverage for title-pending, prompt-only, stale fallback, and cold undisplayable
  rows. Rebuilt `1.6/Assemblies/PawnDiary.dll`.

- **Archive compaction design captured before implementation.** Added `ARCHIVE_COMPACTION_DESIGN.md`
  for Plan 3, covering the proposed `diaryArchiveEntries` Scribe key, one-row-per-displayed-POV archive
  schema, archive repository, archive-then-drop retention flow, UI merge rules, migration path,
  pending/failed old-entry policy, dev-regeneration limits, hot-cap semantics, and before/after test
  plan. This is documentation only; runtime retention still uses the existing full-`DiaryEvent`
  storage until the design is reviewed and implemented.

- **Fixed diary tab lag when switching to a pawn with a large history.** The sliced history loader
  (year index, selected-year card materialization, and row layout) shares a per-frame time budget
  (`uiHistoryScanFrameBudgetSeconds`, ~0.75 ms) so opening a pawn with thousands of pages spreads
  the work across frames and shows a loading panel instead of freezing. That budget was being
  exhausted after only one or two entries per frame, so a long history took many seconds to load
  (and the panel barely progressed). The cost was `DiaryContextFields`, which every indexed event
  and every materialized card calls several times — for arrival/death bounds checks, status reads,
  and source-domain recovery, the last of which probes up to ~13 markers. Each `Value`/`HasMarker`
  call did `context.Split(';')`, allocating a string array plus a substring per field every time, so
  indexing a few thousand entries allocated millions of strings. `DiaryContextFields` now scans the
  context in place and allocates ONLY when a value is actually returned; the common "key absent"
  path (the overwhelming majority of the classifier's probes) is allocation-free. This cuts per-event
  cost by roughly an order of magnitude, so large histories load in a fraction of the time and the
  loading panel progresses visibly. Observable parsing semantics are unchanged (pinned by new
  `DiaryPipelineTests` cases covering trimming, case-insensitivity, empty segments, internal `=`,
  and the Pawn_1/Pawn_12 exact-key trap). No save data, Scribe key, or DefInjected text changed.
- **Hardened sliced builds against concurrent generation (defensive).** While diagnosing the lag, the
  in-progress year-index build, the in-progress row-layout build, and the completed row-layout cache
  were made resilient to `DiaryStateVersion` ticks: an in-progress build is now invalidated only by a
  structural change (different pawn, tab filter, or event count), never by a global state-version
  tick, and the completed row-layout cache no longer fires a synchronous full rebuild on a tick.
  State changes still arrive through the existing quiet index/card refresh. This prevents active
  generation from resetting an in-progress scan, complementing the per-event cost fix above.

- **Tighter thought classification and broad token matching.** Reduced wrong prompt tone for
  vanilla and modded thoughts by replacing the risky broad substring tokens on the `thoughtPositive`
  and `thoughtNegative` groups with precise matchers. The old `matchTokens` lists (`Good`, `Nice`,
  `Love`, `Social`, `Bad`, `Sad`, `Hot`, `Cold`, ...) were blunt enough to misclassify modded
  ThoughtDefs and even vanilla opinion-flipped grief thoughts (e.g. the substring `"Good"` claimed
  the negative `PawnWithGoodOpinionDied`; `"Bad"` claimed the positive `PawnWithBadOpinionDied`).
  Both groups now ship exact `defName` lists (vanilla + Royalty/Ideology/Biotech/Anomaly memory
  thoughts) plus conservative whole-segment/prefix/suffix matchers for mod coverage. To enable this,
  `DiaryInteractionGroupDef` gained three precise matcher fields — `matchPrefixes`, `matchSuffixes`,
  `matchSegments` (CamelCase/underscore/digit word equality) — tried in precision order before the
  legacy `matchTokens` substring fallback, which is unchanged for compatibility. The pure matching
  logic lives in `Source/Capture/GroupNameMatcher.cs` (no RimWorld types) and is unit-tested with
  tricky fixtures including the motivating regression. Opinion-flipped death/loss thoughts are
  routed by group order: `PawnWithBadOpinionDied`/`PawnWithBadOpinionLost` are claimed by
  `thoughtPositive` (order 500) before `thoughtNegative`'s `Died` suffix (order 510), while
  `PawnWithGoodOpinionDied` correctly routes negative. No save data, Scribe key, or DefInjected
  label/instruction/tone changed.

- **Unified event ingestion behind a `DiaryEvents.Submit` bus.** Every captured event now enters
  through one front door and runs a single shared pipeline (`DiaryGameComponent.Dispatch`):
  guard → pure catalog `Decide` → consolidated dedup → `Emit`. Each source's capture+emit logic
  moved out of a bespoke `RecordXxx` method into a small uniform `DiarySignal` subclass under
  `Source/Ingestion/`; colony-wide sources use `DiaryFanoutSignal` so the colony dedup
  (peek-then-mark-after-first) lives in one place. The ~12 per-source dedup dictionaries collapse to
  one transient `recentEvents` store keyed by the existing raw source-prefixed keys. This is internal
  plumbing only — no `DiaryEvent` field, Scribe key, `interactionDefName`, or `gameContext` format
  changed, so saves load identically. **All 17 catalog sources are migrated** — the Harmony-hook
  sources (Thought, Inspiration, Ability, Romance, MentalState, Tale, Death, Interaction, Raid,
  MoodEvent, Ritual/PsychicRitual, Hediff, Quest, Arrival) and the tick-scanner / flush sources
  (Work, ThoughtProgression, DayReflection), whose periodic scans now build a signal per pawn and
  submit it. `Dispatch` returns whether it emitted, so a scan whose episode state is coupled to the
  record outcome (ThoughtProgression's recorded-stage set, DayReflection's written-day guard) reads
  that result. No `RecordXxx` capture methods remain; the per-source dedup dictionaries are gone,
  replaced by the consolidated store. Pure DedupKey() tests pin the consolidated-store keys, and pure
  PlanEmit() functions for the two branchy sources (Tale, Interaction) make their emit routing —
  decision -> shape, plus Tale's solo POV + death-description flags — unit-testable. See
  DOCUMENTATION.md section 3.1 and the section 4 coverage table.

- **Ingestion-bus review fixes.** Three correctness issues found by adversarial review of the
  consolidated dedup store, plus stale comments:
  - The shared `recentEvents` prune sweep used the *current caller's* window against every key, so a
    short-window source crossing the 512-key threshold could evict a still-live long-window key
    (e.g. a 300-tick ability evicting a 60000-tick hediff) and re-admit an event the old per-source
    dictionaries suppressed. Each entry now stores its OWN window and the sweep evicts only keys that
    have expired by that window (pure policy in `Source/Capture/RecentEventExpiry.cs`, unit-tested).
  - A zero/negative dedup window now means "opt out of dedup" (no check, no mark, no prune) instead
    of "treat every shared key as expired", so a tuned-to-zero source can no longer wipe the whole
    store on its first prune.
  - `Dispatch` now runs the dedup CHECK before `Decide` and the MARK after, restoring the
    pre-refactor ability path where dedup ran before the `Rand.Value` roll; a deduped duplicate
    activation no longer advances RimWorld's global RNG, and a chance-roll failure still does not
    consume the dedup window.
  - Updated stale comments and XML-doc comments that still named the deleted `RecordMoodEvent`/
    `RecordRaid`/`RecordRomance` recorders and the pre-migration "legacy ingestion" map.
  No `DiaryEvent` field, Scribe key, or save format changed.

- **Gray flesh event-window monitor disabled.** The built-in `MetalhorrorSuspicion` event window is
  now disabled in XML because the `ThingSpawned` gray-flesh signal can leave the suspicion status
  effectively active all the time. The row remains documented as a template until a safer monitor is
  added.

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
