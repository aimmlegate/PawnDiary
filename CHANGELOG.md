# Changelog

Dated history of every change to the mod. Add an entry here with each change, newest first.
`DOCUMENTATION.md` describes the current design; this file records how it got there.

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
