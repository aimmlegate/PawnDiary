# Changelog

Dated history of every change to the mod. Add an entry here with each change, newest first.
`DOCUMENTATION.md` describes the current design; this file records how it got there.

- **2026-06-17 (Diary tab year paging)**
  - Enlarged the Diary inspector tab and fixed the scrollable content height so inter-entry gaps
    are included in the scrollbar range.
  - Added an in-game-year pager above the Diary list, using each entry's saved display date so old
    saves remain pageable without requiring a new absolute-tick field.
  - Collapsed older entries to their date/title headers by default, kept the newest 15 visible
    entries open, and added click-to-toggle expansion with lightweight height animation plus
    session-local manual state.
  - Cleaned up the collapsed-row rendering so closed entries use a compact header-only border
    instead of clipped full-card body chrome.
  - Fixed collapsed/animating row hover hitboxes so a closed entry no longer highlights like its
    hidden full-height card is still open.

- **2026-06-17 (death fallback entries)**
  - Added a `Pawn.Kill` postfix fallback so condition deaths such as malnutrition/starvation still
    generate a final neutral death entry when vanilla does not emit a death Tale.

- **2026-06-17 (Diary tab after Needs)**
  - Moved the Diary inspector tab insertion point from after Social to after the vanilla Needs tab.

- **2026-06-17 (simpler discovery fallbacks)**
  - Added direct discovery fallbacks for ancient-danger `TriggerUnfogged` activations and Anomaly
    monolith study work, plus debug breadcrumbs for GameEvent discovery recording decisions.

- **2026-06-17 (game-event patch registration fix)**
  - Fixed the fog-reveal and monolith Harmony hooks to bind RimWorld method arguments by position,
    preventing a `PatchAll` abort that could stop ordinary diary events from registering.

- **2026-06-17 (pending title indicator alignment)**
  - Aligned the pending-title dot animation with the future title text start instead of drifting
    toward the right edge of the diary card header.

- **2026-06-17 (staged situational thought generation)**
  - Added a lightweight scanner for staged situational thoughts that do not fire
    `MemoryThoughtHandler.TryGainMemory`, covering malnourishment/starvation progression,
    tired/exhausted, trapped/entombed underground, and chemical hunger/starvation.
  - Added XML-tunable progression rules with per-category deduping so an active episode can write
    worsening stages once without repeating the same state, and skips recovery pages when the food
    thought disappears.

- **2026-06-16 (map discovery diary events)**
  - Added synthetic GameEvent diary entries for pawn-caused map discoveries, including ancient mech
    threat reveals, nearby `CompLetterOnRevealed` objects, and void/fallen monolith investigation
    or activation.
  - Added a `gameDiscovery` XML group with ominous discovery tone/instruction plus localized raw
    event text, so discovered events generate from the pawn who found or activated them.

- **2026-06-16 (stricter prompt length contract)**
  - Tightened the default diary, reflection, neutral, and appended user instructions with explicit
    complete-sentence and word-count limits, plus "prefer shorter complete output" wording.
  - Updated matching C# fallback defaults and prompt-lab fallback prompts so experiments use the
    same length contract as the in-game XML defaults.
  - Added cleanup for under-limit LLM responses that still end in a dangling sentence fragment,
    preventing API-side `max_tokens` stops from saving broken trailing text.

- **2026-06-16 (sentence-aware LLM trimming)**
  - Changed the local LLM response cap to prefer the last complete sentence before `maxTokens`,
    avoiding mid-sentence diary fragments when a model ignores the requested length.

- **2026-06-16 (tick-time colonist scan snapshots)**
  - Changed scheduled diary scans to iterate one-tick snapshots of RimWorld's free-colonist list,
    preventing `Collection was modified` errors if pawn membership changes while tick work records
    sleep reflections, work entries, or persona counts.

- **2026-06-16 (random generation weight settings)**
  - Added saved settings sliders for work and social random generation weights, defaulting to 1x.
  - Wired work weight into periodic work-sampling odds and social weight into batched interaction
    promotion odds, with 0x suppressing that random source and 5x as the settings cap.

- **2026-06-16 (README and mod description refresh)**
  - Rewrote the README as a player-facing overview covering the Diary tab, event coverage, LLM
    setup, experimental status, and prompt-lab workflow.
  - Expanded the RimWorld-visible `About/About.xml` description from the README summary so the
    in-game mod list now reflects current diary, persona, event, and local-LLM behavior.
  - Added a temporary generated RimWorld-themed `About/Preview.png` image for the mod list.

- **2026-06-16 (prompt customization settings pass)**
  - Added saved mod-settings overrides for every Def-backed prompt text that actually reaches LLM
    generation: the single-entry instruction, recipient follow-up instruction, neutral death/arrival
    instructions, and the title follow-up instruction, alongside the existing system prompts.
  - Switched prompt generation to read those live settings-backed values instead of the XML defs
    directly, so in-game customization now affects diary, neutral chronicle, and title requests
    without a restart.
  - Reworked the settings window into a more user-friendly prompt workflow: a new Prompt Studio
    summary + picker/editor for all prompt texts, plus prettier event-group cards with quick Edit
    buttons, override summaries, and a framed per-group instruction editor.
  - Followed up on clipping in that UI by widening the model-fetch button and moving the long
    prompt-reset and group save/restore actions onto full-width rows inside their cards.
  - Corrected the group editor card height so the bottom restore action no longer spills outside
    the framed container.

- **2026-06-16 (prompt context handling guidance)**
  - Tightened diary and reflection system prompts so structured context is treated as private
    evidence for voice/subtext instead of echoed as a checklist.
  - Added explicit max-token-budget and return-only-text guidance to the diary/reflection prompt
    defaults, XML defs, and prompt-lab copies.

- **2026-06-16 (diary text readability pass)**
  - Added `Source/UI/DiaryTextFormat.cs`, which rewrites each diary line into Unity rich text: light
    markdown (`**bold**`, `*italic*`, `# ` headings, `- `/`* ` bullets, `> ` block quotes) becomes
    tags, and quoted speech is colored inline so the spoken words stand out without lighting up the
    surrounding narration.
  - Replaced the over-eager whole-line dialogue heuristic (any stray quote or early colon turned a
    prose line bold and colored) with span-level inline coloring. The diary tab now renders body text
    through one rich-text body style instead of switching between dialogue/narrative line styles;
    the first-seen fade is applied via `GUI.color` so inline-colored spans fade with the rest.
  - Typography polish: more line/paragraph leading, a faint warm "page" tint behind each card's body
    text, and a luminance-based brightness floor for the pawn's favorite dialogue color so deep
    blues/reds stay readable on the dark card.
  - Card chrome polish for a tidier journal look: warmer page tint, a warm hairline rule under each
    header, a soft highlight beside the group accent "spine", warmer parchment-ink header text, and a
    little more breathing room between the header and the body.

- **2026-06-16 (prompt atmosphere cleanup)**
  - Removed the pawn-state `atmosphere:` line from first-person LLM prompts and prompt-lab fixtures,
    leaving event group `tone:` as the single emotional-register cue.
  - Kept pawn mood, health, low capacities, and top thoughts in the `you:` summary so pawn state
    still reaches the model without duplicating the tone field.

- **2026-06-16 (nearby-object weighted selection)**
  - Updated surroundings context generation so nearby objects are now summarized as 1–2 weighted-random
    picks from nearby candidates instead of an unweighted fixed list.
  - Added distance-nearby weighting so fire, corpses, and buildings are more likely to appear in the
    prompt context, preserving variety via replacement-free random draws.
  - Updated `DOCUMENTATION.md` to document the new nearby-selection behavior and tuning implications.

- **2026-06-16 (performance & robustness pass)**
  - Added an `eventId -> DiaryEvent` lookup index (`eventsById`) so `FindEvent` is O(1); all event
    creation routes through `RegisterDiaryEvent` and the index is rebuilt on load (`RebuildEventIndex`).
    The diary tab now computes each pawn's arrival/death boundary once per draw (`ComputeDiaryBounds`)
    instead of re-deriving it per event, removing the quadratic per-frame cost that froze the tab on
    long-running colonies.
  - Raised `ServicePointManager.DefaultConnectionLimit` so `maxConcurrentRequests` is no longer
    silently throttled to 2 connections per host by the transport layer.
  - Made the settings model-fetch hand its result back to the main thread for application instead of
    calling `.Translate()` / mutating settings in the (possibly background-thread) await continuation.
  - Hardened the LLM send loop against unexpected exceptions (reports a failure result instead of an
    unobserved task exception), bounded the diary tab's first-seen fade cache, capped the death/arrival
    context caches, and replaced magic LLM status strings with the `DiaryEvent` constants.

- **2026-06-16 (creepjoiner void persona bias)**
  - Added four dark, void, and unsettling persona presets and tagged them with the new internal
    `void` theme.
  - Added a creepjoiner-only first-roll bonus for void-tagged personas so creepjoiners strongly
    prefer those voices without making them common for ordinary colonists.

- **2026-06-16 (pending title header animation)**
  - Exposed per-entry title-pending state in `DiaryEntryView` so the UI can distinguish "page is
    written" from "short title follow-up is still running."
  - Added a header-level animated placeholder for diary cards whose title request is still pending,
    keeping the date visible until the stored title arrives.

- **2026-06-16 (first-release group catalog cleanup)**
  - Dropped an unreleased placeholder interaction group from the XML catalog and kept prompt-lab
    generated fixtures aligned with the first-release group set.
  - Updated docs and a stale source comment example to describe the shipped group catalog cleanly.

- **2026-06-16 (documentation compaction and parity audit)**
  - Re-reviewed `DOCUMENTATION.md` against the current source, corrected the stale tab-placement
    overview to Social-tab placement, and compacted the design doc into a current-state guide.
  - Compacted this changelog into dated summary groups while preserving the important behavioral
    history and keeping the newest documentation change explicit.

- **2026-06-16 (work entries, prompt lab, titles, UI polish, cleanup)**
  - Added sampled pawn-work diary entries from current Work-tab jobs, with Work-domain XML groups
    for passionate, straining, routine, and dark-study work plus XML tuning for odds/cooldowns.
  - Restored `prompt-lab/` as an XML-driven Node prompt-testing harness with generated fixtures,
    manual fixtures, model overrides, and git-ignored markdown result snapshots.
  - Made LLM titles default on, removed first-line/first-sentence title fallback, moved title user
    instructions into `DiaryPromptDef.titleUserInstruction`, and cleared stale pending title states
    on load so interrupted titles retry.
  - Added a local hard cap for overlong diary responses and updated the default diary system prompt
    to tell models to stay within the request token limit.
  - Polished diary cards with group accent colors/chips, date-title headers, title fade-in/pulse,
    finished-text fade-in, dot-only writing indicators, and player-facing wording about diary pages
    rather than generation.
  - Enforced arrival pages as first visible/generated entries and death pages as terminal entries,
    including same-tick boundary handling and startup generation ordering.
  - Removed legacy surfaces: prompt-lab's broken old path before restoration, combined dual-response
    parsing, paired-mode toggle, obsolete migration/save fields, old response fallback parsing,
    legacy small-talk tuning, and unsupported pre-event-model display paths.

- **2026-06-16 (event architecture, batching, day summaries, reliability)**
  - Split `DiaryGameComponent` into focused partials, including one file per event source, plus
    public API, event factory, generation, lookup, batching, ambient thoughts, and day summary files.
  - Extracted `MoodImpact` as the shared home for positive/negative/neutral mood tokens and mood
    direction classification.
  - Added configurable interaction batching with `PairEvent` and `AmbientDayNote` modes, then added
    weighted promotion so salient batched interactions can become immediate pairwise events.
  - Added ambient interaction/thought day notes and then folded low-impact daily material into a
    richer end-of-day `DayReflection` selected from major events, opinion shifts, new afflictions,
    and low-weight filler.
  - Added temporary thought diary entries via `MemoryThoughtHandler.TryGainMemory`, fixed the patch
    target, and added review fixes for filtering/dedup/localization.
  - Added dev-mode "show generating entries", thread-safe debug logging through main-thread flushes,
    stale/orphan pending recovery, and a fix for founding-colonist startup thoughts getting stranded
    by session resets.
  - Improved the settings window with self-measuring scroll height, section styling, two-column event
    toggles, a per-group prompt editor, compact API editor rows, model fetch/pick controls, and a
    first-click scroll fix.

- **2026-06-16 (prompt modes and narrative behavior)**
  - Split system prompts by narrative mode: diary voice, day reflection, neutral chronicle, and title.
  - Added per-event group `tone` metadata so event mood can guide the model independently from pawn
    atmosphere.
  - Updated prompt and UI behavior so pending generation is background-only, production diary views
    hide raw/debug rows, and dev mode exposes troubleshooting controls.

- **2026-06-15 (arrivals, deaths, personas, DLC safety)**
  - Added neutral first-entry colony arrival pages for starting colonists and later joins detected
    through `Pawn.SetFaction`, including prior faction, recruiter, pawn kind, creepjoiner, scenario,
    and surroundings context.
  - Added neutral colonist death descriptions using `Pawn.Kill` cause caches plus death TaleDefs,
    with death facts such as damage, culprit hediff, missing parts, lethal conditions, and nearby
    context.
  - Dev-gated Diary-tab controls: normal play hides persona/debug/generation controls; dev mode can
    show the writing checkbox, persona picker, pending rows, and diagnostics.
  - Added trait/backstory-aware, colony-deduplicated initial persona rolls with XML `<themes>`,
    `PersonaAffinity`, base weight, theme bonus, duplicate penalty, and fallback random selection.
  - Expanded the persona catalog with more voice-driven presets and richer rules for small local
    models.
  - Added DLC-safety guidance and centralized guarded DLC pawn reads in `DlcContext` for Biotech
    xenotype, Royalty title, and Ideology faith.

- **2026-06-15 (event coverage and model routing)**
  - Added mood-event game conditions, skipped `ProblemCauser` conditions, and ensured conditions
    that also have TaleDefs record once via MoodEvent.
  - Added notable events beyond social logs through `TaleRecorder.RecordTale`, including raids,
    disasters, milestones, medicine, combat, research, and other history/art events.
  - Added quality craft and relic hooks plus Anomaly tale coverage while avoiding art duplicate logs.
  - Added multi-API lanes with per-lane concurrency, round-robin request assignment, automatic
    failover to next model, model-list fetch/pick UI, lane enable/disable, compact setup hiding, and
    debug logs that omit API keys.
  - Fixed patch robustness, duplicate entries, social-memory null references, combat classification,
    capacity thresholds, UI clipping, persona picker reachability, and linked-entry XML comments.

- **2026-06-15 (diary UI, context, localization, docs)**
  - Reworked prompt context for lean signal: no traits line, one positive and one negative thought,
    low-capacity keywords, latest opener, burning passion for important events, and safer social
    thought summaries.
  - Added linked-entry previews between pawns with click-through to the other POV and Social-tab
    log-row click-through to matching diary entries.
  - Moved the Diary tab next to Social, then refined roleplay rendering, production-only card views,
    hidden debug/raw rows, compact model provenance tags, and generating badges.
  - Extracted the changelog out of `DOCUMENTATION.md`, folded C# notes into `AGENTS.md`, renamed
    agent guidance to `AGENTS.md`, and added standing rules for docs, localization, comments, build,
    and DLC safety.
  - Localized UI and natural-language prompt text through Keyed translations while preserving prompt
    schema labels, sentinels, internal ids, debug text, and background-thread errors in English.

- **2026-06-14 (background generation, eligibility, personas, XML refactor)**
  - Corrected docs around paired generation and pending regeneration, then moved all generation to
    background scans instead of Diary-tab side effects.
  - Enforced colonist-only diary eligibility across recording, UI visibility, and public APIs; mixed
    events now create solo colonist entries.
  - Added in-game persona XML defs, default persona setting, per-pawn saved persona and generation
    toggle, and prompt persona lines.
  - Added small-talk batching and then replaced live dual-POV generation with paired sequential POV
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
  - Added prompt-lab, persona experiments, lean prompt context, social-fight and mental-break
    capture, and XML-backed interaction groups with per-group enablement/instructions.
  - Added the original documentation file.
