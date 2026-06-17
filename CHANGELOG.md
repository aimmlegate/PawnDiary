# Changelog

Dated history of important changes to the mod, newest first. `DOCUMENTATION.md` describes the
current design; this file records how it got there.

- **2026-06-17 (hediff mod support fixes)**
  - Made hediff progression and immediate-event dedup keys body-part aware, so same-def conditions
    on different parts do not mask each other.
  - Removed the obsolete `daySummaryHediffMinSeverity` tuning field; Hediff group `<minSeverity>`
    is now the single severity gate.
  - Included Hediff groups in the settings height estimate so larger compatibility catalogs remain
    scrollable.

- **2026-06-17 (XML hediff mod support)**
  - Added a generic Hediff event-group domain with XML policies for day-reflection or immediate
    health-condition diary signals, so modded hediff support can be added with Def patches instead
    of per-mod C#.
  - Routed `AddHediff` and a new severity-step scanner through that XML layer while preserving the
    previous major bad non-injury affliction behavior through the `hediffMajorHealth` catch-all.
  - Exposed Hediff groups in the Events settings UI and documented XML compatibility examples for
    extension mods.

- **2026-06-17 (inspiration diary events)**
  - Added solo diary entries for successful pawn inspirations through
    `InspirationHandler.TryStartInspiration`, with a new Inspiration event group and settings
    header.
  - Removed Royalty `WordOfInspiration` from the ritual/social interaction matcher so the target's
    resulting inspiration is recorded as the diary event.

- **2026-06-17 (POV-only quoted speech)**
  - Tightened interaction prompt dialogue rules so quoted direct speech can only come from the
    current POV pawn; other pawns' speech should be paraphrased without quotes.

- **2026-06-17 (verse persona presets)**
  - Added restrained `plainspoken-poet` and `lowkey-rapper` diary persona presets, with
    low-Consciousness variants and rules that avoid overdone lyricism or forced rhyme.

- **2026-06-17 (fragmented persona presets)**
  - Added `fractured-pattern-seer` and `word-salad-oracle` diary persona presets for readable
    fragmented-association and controlled word-salad voices, including low-Consciousness variants.

- **2026-06-17 (rare atmosphere formatting)**
  - Added a default-on `enableAtmosphericFormatting` setting for display-only diary typography.
  - Limited unusual layout to extreme entries: fractured mental-break prose, unsettled anomaly/dark
    prose, centered memorial death descriptions, and staggered variable-size words for first-person
    pages recorded while the pawn was severely intoxicated or low-Consciousness.
  - Mapped Anomaly's in-game "strange chat" to `DisturbingChat` and gave only the initiator POV an
    anomaly-green accent plus dramatic distorted direct-speech formatting.
  - Increased strange-chat direct-speech distortion so quoted words look more visibly uncanny while
    still leaving the saved generated text untouched.
  - Split `DisturbingChat` into its own strange-chat interaction group and gave it chitchat's
    ambient batching and promotion odds, so ordinary strange chats stay rare while loaded moments
    can still become full pairwise entries.
  - Added a localized paired-interaction prompt rule requiring at least one short direct-speech
    sentence in double quotes, so two-pawn diary entries read less like pure description.
  - Added a softer direct-speech cue for single-POV interaction entries, used only when quoted
    speech fits naturally.
  - Stored per-POV staggered intensity on new diary events so worse impairment affects more words
    without changing prompts or saved generated text.

- **2026-06-17 (diary color cue consistency)**
  - Added a saved `colorCue` key for diary events so card accents no longer depend on localized
    group labels or generated titles.
  - Retuned Diary tab accents toward RimWorld-like colors: hostile red for combat/crisis, orange for
    social fights, green for mental breaks, light blue for daze/wander breaks, deep red for dark or
    anomaly entries, white for deep talk and day reflections, and light white-gray for non-important
    entries.

- **2026-06-17 (persona consciousness modifier settings)**
  - Extended Persona presets settings so built-in and custom personas can edit their clouded,
    fading, and barely-conscious voice modifiers.
  - Preserved XML inheritance for old built-in persona overrides until a player changes those
    modifier fields, avoiding silent loss of existing low-Consciousness tuning.

- **2026-06-17 (route cleanup)**
  - Added an explicit `arrival` interaction group for `PawnDiary_Arrival`, so arrival pages use the
    Arrival chip, important styling, and Events toggle instead of the `other` catch-all.
  - Removed inert legacy `enableLlm` and `keepRawEntryOnFailure` settings fields.
  - Removed stale Keyed strings from old status, restore-prompt, small-talk, and atmosphere paths.
  - Dropped the no-op prompt-lab `--all` switch from CLI help, package scripts, and README examples.

- **2026-06-17 (discovery event removal)**
  - Removed the non-functioning discovery event generation path: disabled Harmony stubs, recorder
    partial, GameEvent classifier/settings helpers, XML group, and unused Keyed strings.
  - Updated documentation so the active event-source list no longer advertises quarantined
    discovery generation.

- **2026-06-17 (low-consciousness persona rules)**
  - Kept the below-11% Consciousness generation guard intact while routing still-conscious impaired
    pawns through clouded, fading, and barely-conscious prompt states.
  - Replaced the generic barely-conscious persona override with persona-specific XML rules on every
    built-in `DiaryPersonaDef`, so translators and persona authors can tune each impaired voice.
  - Added localized Keyed words for the compact Consciousness `important health:` cue.

- **2026-06-17 (compact important-health prompt cues)**
  - Changed hediff prompt enchantments from a generic `prompt_enchantment:` metadata line to a
    compact `important health:` cue with urgency, condition/body part, and capped impact reasons.
  - Added first-person system-prompt guidance that important health cues are high-priority body and
    mood pressure, not checklist text to recite.
  - Moved the generated important-health cue words into Keyed translations, leaving only stable XML
    tuning tokens hardcoded in the resolver.
  - Localized the ambient interaction `with {pawn}` prompt-context connector.

- **2026-06-17 (persona settings selector)**
  - Replaced the Persona Presets button grid with a compact selector menu and expanded the persona
    editor layout so long labels and all theme tags fit without clipping.

- **2026-06-17 (insult-spree batching)**
  - Added a three-in-game-hour pairwise batch policy for insult interactions, preserving RimWorld's
    own play-log insult text as evidence while producing one diary generation for repeated jabs.
  - Added a disabled-by-default `InsultingSpree` mental-state group so generic insult-spree break
    pages do not duplicate the richer batched insult entries unless a player enables them.

- **2026-06-17 (number-light prompt context)**
  - Replaced generated prompt-context numbers with word buckets where practical: life stage instead
    of age, mood/pain/bleeding/opinion/thought impact buckets, and hediff intensity words.
  - Removed numeric list markers from batched/ambient prompt evidence and changed the "more quiet
    moments" line to avoid exact counts.
  - Reworded default prompt instructions and thought-event instructions to use prose length/effect
    guidance instead of digit ranges or mood-offset language.

- **2026-06-17 (hediff-weight prompt enchantments)**
  - Replaced XML-authored prompt-enchantment prose with live hediff context built from the game's
    condition label, body part, severity, and description.
  - Kept `DiaryPromptEnchantmentDefs.xml` as a weighting/eligibility table for visible hediffs and
    removed gene/title/stat/capacity enchantment matchers from the runtime path.
  - Broadened prompt-enchantment resolution so every first-person prompt with `persona:` may include
    one weighted live health-condition hint; neutral chronicle/title prompts still omit it.
  - Kept `my last opener (not repeat)` in first-person prompts as a compact anti-repetition cue.

- **2026-06-17 (prompt context policy streamlining)**
  - Added event-type prompt context policies so routine/internal entries send minimal first-person
    prompts, social entries keep relationship context, and combat/crisis entries keep pawn state,
    setting, current POV weapon, hidden initiator context, and optional prompt enchantments.
  - Curated neutral arrival/death fact lines instead of dumping full `gameContext` metadata into the
    prompt.
  - Removed unused opinion summary, atmosphere, and burning-passion prompt plumbing from event
    creation and `DiaryEvent` persistence.

- **2026-06-17 (consciousness prompt enchantments)**
  - Added XML `capacityBelow` matchers for pawn capacities, with optional `minValue` for exclusive
    ranges.
  - Added starter Consciousness-band enchantments while leaving Royalty titles as regular
    pawn-summary `title=` context instead of title-specific enchantment Defs.

- **2026-06-17 (starter prompt enchantment catalog)**
  - Expanded `DiaryPromptEnchantmentDefs.xml` with starter hediff enchantments for alcohol,
    hangover, luciferium, chemical addiction/withdrawal cravings, flake/yayo, smokeleaf, psychic
    hangover, blindness, dementia/Alzheimer's, trauma savant, resurrection psychosis, joywire,
    abasia, mindscrew, pregnancy, hemogen craving, psychic bond torn, and Anomaly conditions.
  - Left missing body parts out of the starter catalog after review.

- **2026-06-17 (hediff severity prompt tiers)**
  - Added four fixed hediff severity levels for prompt enchantments: XML `hediffSeverityTiers` can
    override prompt text, chance/frequency, selection weight, and urgency weighting per level.
  - Updated the starter sickness and blood-loss enchantments so higher severity can appear more
    often and use stronger instructions without asking the model to choose the severity.

- **2026-06-17 (consciousness generation guard)**
  - Blocked new non-neutral main/title LLM generation while the POV pawn is below 11%
    Consciousness by marking that POV `skipped`, while still allowing neutral arrival/death
    descriptions and healthy paired POVs.

- **2026-06-17 (prompt enchantments)**
  - Added XML-driven prompt enchantments: first-person prompts may append one weighted-random,
    chance-gated `prompt_enchantment:` rule based on visible hediffs, active genes, royal titles, or
    pawn stat thresholds.
  - Added an enabled-by-default settings toggle, starter enchantment Defs, DLC-safe gene/title
    matchers in `DlcContext`, and rebuilt the committed assembly.

- **2026-06-17 (documentation and changelog compaction)**
  - Compacted `DOCUMENTATION.md` and this changelog while preserving current architecture, runtime
    constraints, localization rules, event coverage, settings behavior, and historical milestones.

- **2026-06-17 (caches, personas, UI paging, and discovery quarantine)**
  - Bounded transient caches: recent-event dedup dictionaries now prune expired keys after reaching
    512 entries, and Diary-tab first-seen/expansion animation caches are capped.
  - Cached the merged persona catalog and invalidated it after settings-backed persona edits or
    load-time normalization; model-facing numeric context now uses invariant dot-decimal formatting.
  - Added Persona presets settings: edit built-ins, add/delete custom personas, normalize allowed
    tags, and expose custom presets in runtime picker/generation flows.
  - Added year paging for long diary histories, a year selector with page counts, newest-15 expanded
    default, older-entry collapse/expand animation, corrected collapsed-row geometry, and bounded
    hover/click hitboxes.
  - Added a dev-mode mock-page filler that creates up to 360 completed test pages for year-paging
    and collapse testing without calling an LLM.
  - Moved the Diary inspector tab insertion point from after Social to after vanilla Needs.
  - Added a `Pawn.Kill` postfix fallback so natural condition deaths such as starvation still get a
    neutral final entry when vanilla emits no death Tale.
  - Added staged situational thought scanning for malnourishment/starvation, tired/exhausted,
    trapped/entombed underground, and chemical hunger/starvation, with XML progression rules and
    no recovery-page spam.
  - Tried and then quarantined map-discovery hooks: fog reveal, ancient-danger, monolith, and debug
    fallback work was isolated in unregistered `DiscoveryPatches.cs`; `DiscoveryEventsEnabled=false`
    makes discovery recording no-op until repaired.
  - Polished title behavior and presentation: pending-title animation aligns with future title text,
    mod preview image was refreshed with a RimWorld-style `Pawn Diary` treatment, and compact rows
    match expanded card headers more closely.

- **2026-06-16 (prompts, generation weights, UI readability, and robustness)**
  - Tightened diary, reflection, neutral, and title prompt contracts with explicit sentence/word
    ranges, "prefer shorter complete output" guidance, return-only-text guidance, and stronger
    instructions to treat structured context as private evidence rather than prose to echo.
  - Added cleanup for under-limit LLM responses that still end with a dangling sentence fragment,
    and changed local response trimming to prefer the last complete sentence before `maxTokens`.
  - Removed the duplicated pawn-state `atmosphere:` prompt line while preserving mood, health, low
    capacities, top thoughts, and group `tone:`.
  - Added saved sliders for `workGenerationWeight` and `socialGenerationWeight`; work sampling and
    batched-social promotion honor 0x-5x multipliers.
  - Added settings overrides for every Def-backed user-message prompt text that reaches generation,
    plus Prompt Studio reset flows and a cleaner event-group editor with per-group instruction edits.
  - Reworked diary body rendering through `DiaryTextFormat`: light markdown becomes Unity rich text,
    quoted speech is colored inline from the pawn's favorite color, and card typography/chrome were
    warmed and cleaned up.
  - Changed surroundings context to weighted-random 1-2 nearby objects so important context objects
    such as fire, corpses, and buildings appear more often without becoming a fixed list.
  - Added one-tick snapshots for scheduled colonist scans, preventing collection-modified errors if
    pawn membership changes during sleep, work, or persona-count scans.
  - Added O(1) `eventId -> DiaryEvent` lookup rebuilds, consolidated event registration, and changed
    long-history Diary tab boundary calculation from per-event work to once per draw.
  - Raised `ServicePointManager.DefaultConnectionLimit`, moved model-fetch application back to the
    main thread, hardened LLM sends against unexpected exceptions, bounded death/arrival caches, and
    replaced magic LLM status strings with constants.
  - Refreshed the README, expanded `About/About.xml`, and added then refined the mod preview image.
  - Added temporary GameEvent discovery recording for map discoveries and monolith beats; this work
    was later disabled/quarantined on 2026-06-17.

- **2026-06-16 (work entries, prompt lab, titles, UI polish, and cleanup)**
  - Added sampled pawn-work diary entries from current Work-tab jobs, with Work-domain XML groups
    for passionate, straining, routine, and dark-study work plus XML odds/cooldowns.
  - Restored `prompt-lab/` as an XML-driven Node harness with generated/manual fixtures, model
    overrides, and git-ignored markdown result snapshots.
  - Made title generation default on, removed first-line/first-sentence fallback titles, moved title
    instructions into `DiaryPromptDef.titleUserInstruction`, and cleared stale pending title states
    on load so interrupted titles retry.
  - Polished diary cards with group accent colors/chips, date-title headers, title and entry
    animations, dot-only writing indicators, and player-facing wording about diary pages.
  - Enforced arrival pages as first visible/generated entries and death pages as terminal entries,
    including same-tick boundary handling and startup generation ordering.
  - Removed legacy paths: broken prompt-lab code before restoration, combined dual-response parsing,
    paired-mode toggle, obsolete save/migration fields, fallback response parsing, old small-talk
    tuning, and unsupported pre-event-model display paths.
  - Re-reviewed docs against source, extracted and compacted changelog history, and kept
    `DOCUMENTATION.md` aligned with current architecture.

- **2026-06-16 (event architecture, batching, reflections, and reliability)**
  - Split `DiaryGameComponent` into focused partials for event sources, public API, event factory,
    generation, lookup, batching, ambient thoughts, and day summaries.
  - Extracted `MoodImpact` as shared positive/negative/neutral mood classification.
  - Added configurable interaction batching with `PairEvent` and `AmbientDayNote`, plus weighted
    promotion of salient batched interactions into immediate pairwise events.
  - Added ambient interaction/thought day notes and richer end-of-day `DayReflection` selection from
    major events, opinion shifts, new afflictions, and filler.
  - Added temporary thought entries through `MemoryThoughtHandler.TryGainMemory`, fixed the patch
    target, and tightened filtering, deduping, and localization behavior.
  - Added dev-mode "show generating entries", thread-safe debug-log flushing, stale/orphan pending
    recovery, and a fix for founding-colonist startup thoughts stranded by session resets.
  - Improved settings layout with measured scroll height, section styling, two-column event toggles,
    prompt editing, compact API rows, model fetch/pick controls, and scroll-click fixes.
  - Split system prompts by narrative mode: diary, day reflection, neutral chronicle, and title.
    Added per-event `tone` metadata and kept production Diary views generated-only unless dev mode
    is enabled.

- **2026-06-15 (arrivals, deaths, personas, DLC safety, event coverage, and routing)**
  - Added neutral first-entry arrival pages for starting colonists and later joins via
    `Pawn.SetFaction`, including prior faction, recruiter, pawn kind, creepjoiner, scenario, and
    surroundings context.
  - Added neutral colonist death descriptions using `Pawn.Kill` cause caches plus death TaleDefs,
    including damage, culprit hediff, missing parts, lethal conditions, and nearby context.
  - Dev-gated Diary-tab controls so normal play hides persona/debug/generation controls while dev
    mode can show writing toggles, persona picker, pending rows, and diagnostics.
  - Added trait/backstory-aware, colony-deduplicated initial persona rolls with XML themes,
    `PersonaAffinity`, base weights, theme bonuses, duplicate penalties, fallback random selection,
    and a creepjoiner bonus for `void` personas.
  - Expanded the persona catalog with more voice-driven presets, richer rules for smaller local
    models, and four dark/void personas.
  - Added DLC-safety guidance and centralized guarded optional DLC pawn reads in `DlcContext` for
    Biotech xenotype, Royalty title, and Ideology faith.
  - Added MoodEvent game conditions, skipped `ProblemCauser` conditions, and ensured conditions with
    TaleDefs record once through MoodEvent.
  - Added notable TaleRecorder coverage for raids, disasters, milestones, medicine, combat, research,
    history/art events, quality crafts, relics, and Anomaly tales while avoiding duplicate art logs.
  - Added multi-API lanes with per-lane concurrency, round-robin assignment, automatic failover,
    model-list fetch/pick UI, lane enable/disable, compact setup hiding, and key-safe debug logs.
  - Fixed patch robustness, duplicate entries, social-memory null references, combat classification,
    capacity thresholds, UI clipping, persona picker reachability, and linked-entry XML comments.

- **2026-06-15 (diary UI, context, localization, and agent docs)**
  - Reworked prompt context for leaner signal: no traits line, one positive and one negative thought,
    low-capacity keywords, latest opener, important-event passion, and safer social-thought summaries.
  - Added linked-entry previews between pawns with click-through to the other POV, plus Social-tab
    log-row click-through to matching diary entries.
  - Refined Diary-tab roleplay rendering, production-only cards, hidden debug/raw rows, compact model
    provenance tags, and generating badges; the tab later moved from Social-adjacent to Needs-adjacent.
  - Extracted changelog history out of `DOCUMENTATION.md`, folded C# notes into `AGENTS.md`, renamed
    agent guidance to `AGENTS.md`, and added standing rules for docs, localization, comments, build,
    and DLC safety.
  - Localized UI and natural-language prompt text through Keyed translations while intentionally
    keeping prompt schema labels, sentinels, internal ids, debug text, and background-thread errors
    in English.

- **2026-06-14 (background generation, eligibility, personas, XML refactor)**
  - Moved generation to background scans instead of Diary-tab side effects, corrected paired
    generation/pending-regeneration docs, and made interrupted pending entries retry.
  - Enforced colonist-only eligibility across recording, UI visibility, and public APIs; mixed events
    now create solo colonist entries.
  - Added in-game persona XML defs, default persona setting, per-pawn saved persona and generation
    toggle, and prompt persona lines.
  - Added small-talk batching, then replaced live dual-POV generation with sequential paired POV
    generation.
  - Added `DiaryPromptDef` as the XML source for prompt instructions/system prompts and migrated old
    prompt/parser constants to XML-backed lookups.
  - Renamed project files to `PawnDiary.*`, moved source into `Source/`, split the original large
    component into models/context/prompts/orchestrator, moved interaction groups and tuning to XML
    Defs, and added novice-friendly comments/docs.

- **2026-06-13 (initial diary system)**
  - Replaced `JavaScriptSerializer` with `MiniJson` for RimWorld Mono compatibility.
  - Added the first LLM diary generation path with queueing, concurrency, timeout handling, raw
    fallback behavior, and a configurable system prompt.
  - Added the initial inspector-tab UI, replacing the earlier gizmo/window and removing the colony
    neutral-events view.
  - Added prompt-lab, persona experiments, lean prompt context, social-fight and mental-break capture,
    XML-backed interaction groups with per-group enablement/instructions, and the original docs.
