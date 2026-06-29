# Pawn Diary Architecture Improvement Plan

Generated: 2026-06-25
Last updated: 2026-06-29

This file is a handoff plan for future agent runs. It summarizes the architecture-mode review of the
whole `Source/` tree, including the adversarial rebuttal results, suggested slice order, concrete run
cards, validation steps, and rejected false leads. The older run cards are kept for audit history;
many are already resolved.

Use this as a planning document, not as a mandate to do every refactor at once. Each run should take one small slice, preserve behavior, update docs/changelog when behavior or structure changes, rebuild, and avoid no-DLC regressions.

Current focus: make event capture stable and make new event sources easy to add. A recent external
pattern review found useful ideas around hook choice, event-source isolation, and richer bounded
context snapshots. Treat those ideas as design input only. Do not copy source code, class shapes,
strings, or policy wholesale; adapt only the patterns that fit Pawn Diary's architecture and
licensing constraints.

Adoption review conclusion: Pawn Diary already has most of the event architecture worth keeping:
domain patch files, a defensive patch registrar, pure Event Catalog decisions, XML-owned policy,
bounded surroundings/pawn summaries, prompt enchantments, event windows, and pure capture-policy
tests. Do not adopt a broad scraper, per-pawn story component, or gameplay objective system.

What we should adopt is narrower: a future **context fact layer**. It should make additional pawn,
relationship, equipment, world, and colony facts available to prompt templates through XML-selected
fields, without sending those facts to every prompt and without dumping everything into the model.

## Global Rules For Every Slice

- Follow `AGENTS.md`: smallest safe change, keep docs current, build after the change, do not break no-DLC games.
- Preserve the architecture barrier: impure RimWorld/Verse collection -> plain payload/context -> pure selection/planning/parsing/formatting -> impure transport/persistence/UI adapter.
- Keep tunables in XML with safe C# fallbacks where needed.
- Do not add hardcoded player-facing UI or prompt text.
- Do not reference DLC types/defs in C# for optional DLC-aware behavior. Prefer string matchers in XML.
- Keep save/load compatibility. Any change touching `DiaryEvent.ExposeData`, `PawnDiarySettings.ExposeData`, or `IExposable` fields needs explicit old-save reasoning and tests where feasible.
- C# changes require rebuilding:
  `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`
- If a slice changes behavior or structure, update `DOCUMENTATION.md` and add a dated entry to `CHANGELOG.md` in the same change.

## Recommended Run Order

1. XML-configurable context fact pipeline (Run Card 20).
2. Live observed-condition monitor for lasting map/anomaly problems (Run Card 25).
3. Event-source addition guide and manifest (Run Card 19).
4. Skill-learning and non-romance relation signals (Run Card 24).
5. Completed-work capture evaluation only if the current work sampler proves too imprecise (Run Card
   21).
6. Shared event adapter helpers only where duplication is already hurting additions (Run Card 22).
7. Event-source test and debug-fixture coverage for any new source or prompt field (Run Card 23).
8. `PawnDiaryMod` settings UI/async split.
9. `DiaryGameComponent.Generation` split: lane selection, titles, pawn lookup.
10. Tier-3 cleanups as opportunity allows.
11. One-line stale comment fix at `DiaryEventCatalog.cs:63` can be done anytime.

Historical resolved cards below are still useful as examples of the expected slice size, validation
style, and doc/changelog discipline. Do not redo them unless the code has drifted.

## Stable Event Capture Expansion Plan

Goal:
- Adding a new event source should be a repeatable small slice, not an expedition through patches,
  settings, prompt code, and save state.
- Capture should prefer stable RimWorld lifecycle hooks and XML string matching over fragile direct
  content references.
- Live game reads stay at the edge. New sources should produce plain event/context snapshots, then
  route through `Source/Capture/` and the existing prompt pipeline.

Need review:
- We do not need a new broad event architecture. `Source/Patches/`, `DiaryPatchRegistrar`,
  `DiaryGameComponent.Record*`, `Source/Capture/Events/*`, and `DiaryEventCatalog` already provide
  the main structure.
- We do not need an exhaustive scraper. `DiaryContextBuilder.BuildSurroundingsSummary` already
  captures room role, indoor/outdoor state, weather when important, biome, temperature, beauty,
  active map conditions, recent threat letters, nearby things, and current job reports.
  `BuildPawnSummary` already captures sex, life stage, DLC identity, mood, health, low capacities,
  and top thoughts.
- We do not need per-pawn components or gameplay progression counters for the diary's current
  product goal.
- We do need a configurable way to save more context facts at event capture time, then let XML prompt
  templates decide which facts to render for which prompt shapes.
- We do need a separate model for lasting game states such as active map danger, anomaly evidence,
  gray flesh suspicion, or other problems that can outlive the signal that first revealed them.
  A start signal plus `timeoutTicks` is not a reliable source of truth for those states.
- We may need a better event-source handoff checklist, because that reduces future implementation
  mistakes without changing runtime behavior.
- We may need a completed-work hook if real saves show periodic work sampling misses important
  moments or records them too vaguely.
- We need future support for these fact families, but they should be captured as bounded, sanitized
  fields and rendered only when an XML prompt template asks for them:
  skill learning context, direct relation context, backstory titles/echo, trait list, skill map,
  pawn records, direct relations/friend/rival/opinion, apparel/inventory, abilities/psylink,
  zone/time phrase, faction leaders, and colony resources.

What we are adapting:
- Use focused hooks for real lifecycle moments when RimWorld exposes them, rather than broad polling
  by default.
- For lasting states with no reliable one-shot hook, use bounded `GameComponent` scans with
  XML-owned intervals, dedup keys, and cheap prefilters. Ticks should control scan cadence,
  debounce, and cooldown only; the live game state should decide whether the problem is active.
- Snapshot richer pawn/world context when it improves prompts, but keep it bounded, capped, and
  template-controlled.
- Treat every hook as optional and defensive: one bad patch must not block startup, vanilla behavior,
  or unrelated event capture.

Reference pattern observed:
- A single startup patch pass installs many direct Harmony hooks: pawn generation, faction changes,
  save-load finalization, quest endings, job completion, hediff additions, direct relations, skill
  learning, deaths, incidents, game conditions, social interactions, rituals, and UI drawing.
- Per-pawn state lives on a `ThingComp`, which caches profile-like facts, invalidates them from
  change hooks, stores counters, and drives story progression from `CompTick`.
- A `GameComponent` polls long-running world state when there is no clean one-shot signal, using a
  fixed interval plus chance/dedup logic.
- A broad pawn/context scraper turns many live facts into flat grammar symbols: identity, backstory,
  traits, skills, health, records, relationships, social opinions, world/weather/season, DLC facts,
  inventory/apparel/abilities, current job, room/zone, and faction/resource summaries.

Benefits of that pattern:
- It captures some moments at their natural lifecycle boundary, especially completed jobs where the
  current job is about to be replaced.
- Profile invalidation hooks make derived context fresher without scraping everything every tick.
- A scraper-style snapshot gives text generation a large vocabulary of facts and can automatically
  benefit from modded content labels.
- Per-pawn counters make "wait until this pawn has done X" gameplay objectives easy.

Costs of that pattern for Pawn Diary:
- A broad patch surface is harder to audit and easier to break across RimWorld versions.
- Per-pawn `ThingComp` state would be a new persistence surface and does not fit the diary's current
  "record events, then generate pages" model.
- A general scraper can bloat prompts, mix policy with prose, and leak live RimWorld objects into
  otherwise pure pipeline code unless it is aggressively bounded.
- Direct DLC-aware reads and direct def lookups must be stricter here because Pawn Diary promises
  no-DLC safety and localization-friendly prompts.
- Gameplay-progress systems, counters, and mechanical consequences are outside Pawn Diary's scope;
  our pages should observe the colony, not add quests or pawn development mechanics.

Pawn Diary's intended approach:
- Keep hooks thin. A patch should collect only the facts that exist at that exact lifecycle point,
  then call a `DiaryGameComponent.Record*` adapter.
- Keep decisions pure. Adapters snapshot eligibility, settings/XML gates, dedup keys, and random
  rolls, then call `XxxEventData.Decide` through the Event Catalog.
- Keep prompt context curated. Add facts to bounded collectors and prompt templates, not to a global
  "everything about this pawn" dump.
- Keep optional content reactive. Prefer XML string matchers and labels from active runtime objects;
  do not require DLC defs or types to exist in loaded content.
- Add event sources one at a time with tests and docs. A new hook is justified only when it records
  a meaningful moment the current system misses or records too late.

Lasting game-state capture model:
- Keep `DiaryEventWindowDef` for signal windows: birthdays, void monolith discovery/activation,
  ancient danger letters, prison breaks, and other moments where a reliable signal is the diary
  event. A timeout can be useful for cleanup, but it should not pretend to prove that a lasting
  problem ended.
- Add a separate observed-condition path for states that can be queried from the current game:
  map danger, hostile map threat, active game conditions, gray flesh/anomaly evidence, metalhorror
  emergence, important hediff presence, and similar colony problems.
- Treat triggers as evidence, not truth. A letter, incident, spawned thing, or hediff hook can wake
  the monitor or annotate evidence, but the monitor still recomputes whether the condition is active
  from `Map`, `Pawn`, `Thing`, `GameCondition`, and saved recent-evidence state.
- Store active observed conditions as saved plain state keyed by condition, scope, map id, and
  optional pawn id. Suggested fields: `conditionDefName`, `conditionKey`, `scope`, `mapUniqueId`,
  `subjectPawnId`, `firstObservedTick`, `lastObservedTick`, `lastSeenEvidenceLabel`,
  `lastSeenEvidenceDefName`, `startRecorded`, and `endRecorded`.
- Each scan builds a current observation set, then diffs it against saved state:
  new observation -> wait for start debounce -> record start/prompt context;
  continuing observation -> refresh `lastObservedTick` and evidence;
  missing observation -> wait for end debounce -> record end/remove active state.
- Save/load behavior should be self-healing. If a save loads while a map threat is active but no
  saved observed-condition state exists, the next scan should rediscover it. If a stale state loads
  after the problem ended, the next scan should age it out through the end debounce.
- Prompt integration should mirror event-window enchantments: active observed conditions contribute
  prompt candidates and optional context facts, but XML decides which conditions suppress normal
  prompts or force threat/anomaly language.
- Do not re-enable long-lived `DiaryEventWindowDef` entries such as metalhorror suspicion by adding
  a bigger timeout. Rebuild them as observed conditions first, then use event windows only for
  reliable start/end diary pages if those pages are still wanted.

Context fact architecture we should build instead of copying a scraper:
- Add an impure `DiaryContextFactCollector` under `Source/Generation/`. It reads live RimWorld state
  and returns a plain `DiaryContextFactSet` of sanitized `key=value` facts.
- Save fact sets per POV on `DiaryEvent`, likely as compact strings such as
  `initiatorContextFacts` and `recipientContextFacts`, using the same defensive parsing style as
  `DiaryContextFields`. Old saves default these fields to empty.
- Add prompt assembler source tokens such as `PovContextFact`, `OtherContextFact`,
  `InitiatorContextFact`, and `RecipientContextFact`. Existing `DiaryPromptFieldDef.contextKey`
  selects the fact key, so XML can add a field like `source=PovContextFact`,
  `contextKey=skill_best`, `label=important context`.
- Keep `gameContext` for event facts (`quest_signal`, `raid_points`, `work`, etc.). Do not overload
  it with per-pawn identity/inventory/social facts.
- Add XML policy for fact capture and rendering. Prefer a `DiaryContextFactDef` or a tuning-backed
  fact policy with `key`, `category`, `enabled`, `defaultCapture`, `maxItems`, `maxChars`, and
  optional event-domain filters. Prompt templates still decide what is sent.
- Collect facts at event creation, not prompt time, so regeneration uses the same snapshot and
  saved diaries remain stable.
- Keep every fact category bounded. Full maps/lists mean "complete within a cap and stable order",
  not unbounded dumps.

Initial context fact keys to reserve:
- `backstory_childhood`, `backstory_adulthood`, `backstory_echo`
- `traits`, `trait_count`
- `skill_best`, `skill_worst`, `skill_passions`, `skill_map`
- `record_kills`, `record_days_colonist`, `record_mental_states`
- `relations_direct`, `relation_friend`, `relation_rival`, `relation_lovers_family`
- `apparel_summary`, `inventory_summary`
- `abilities_summary`, `psylink_level`, `neural_heat`
- `zone`, `season`, `hour`, `time_phrase`
- `faction_friend`, `faction_hostile`, `faction_leaders`
- `silver_count`, `resource_summary`

Fact category priority:
- Phase 1: low-cost identity/social/skill facts — backstory titles, traits, best/worst skill,
  passions, records, direct relation summary, friend/rival, zone, season/hour.
- Phase 2: medium-cost equipment/power facts — apparel, inventory, abilities, psylink.
- Phase 3: colony/world economy facts — faction leaders, silver/resources. These are broad and
  should stay disabled by default unless a prompt/event domain asks for them.

What we are not adopting:
- Do not copy external code or mirror another mod's type names, strings, gameplay systems, or broad
  per-pawn component model.
- Do not move policy into hardcoded C# prose. Prompt text, thresholds, odds, routing, and labels
  belong in XML/Keyed/DefInjected sources with safe fallbacks.
- Do not introduce direct optional-DLC def lookups or scattered `pawn.genes` / `pawn.royalty` /
  `pawn.ideo` reads. Use XML string matchers and `DlcContext`.
- Do not add a general "scrape everything" prompt dump. Context must be curated so entries remain
  stable, short, and testable.

Capture inventory from the reference pattern that Pawn Diary currently skips or handles differently:

| Area | Reference-style capture | Pawn Diary status | Why skip, adapt, or defer |
|---|---|---|---|
| Pawn generation | New pawn generated, then delayed evaluation if free colonist. | Arrivals use starting-colonist scan and `Pawn.SetFaction`. | Keep current arrival path; generation itself is too broad and catches many non-diary pawns. |
| Save/load finalization | Re-evaluates every colonist and starts missing state. | Load catch-up requeues generation/title/orphan work and existing scans resume. | Avoid load-time behavior changes unless a concrete missed event is found. |
| Quest ending | Tracks active quest id and marks success/failure. | Quest accept/end capture already exists. | Keep current quest pipeline; only improve prompt facts if needed. |
| Completed jobs | Prefix captures current job before it changes; postfix records successful completion. | Work currently samples current jobs periodically. | Candidate for Run Card 21 because it may produce better work moments, but it is hot and needs strict gates. |
| All direct relations | Any direct relation invalidates profile. | Romance captures lover/spouse/ex relation changes. | Future work: save direct relation context facts, and optionally add an XML-filtered non-romance relation signal. |
| Skill learning | Large XP gains invalidate profile. | No direct skill-up event capture. | Future work: save skill context facts, and optionally add a skill-burst signal/day-reflection source. Raw learning ticks are noisy. |
| Significant hediff additions | Invalidate profile and optionally start special arcs. | Hediff add/progression capture exists with XML policy. | Already covered for diary purposes; do not add gameplay arc behavior. |
| Pawn death | Notes death-related state and resolves active systems. | `Pawn.Kill` plus death TaleDefs create final pages. | Already covered; keep neutral final-page semantics. |
| Incident-specific world events | Ship chunks, solar flare, ancient caskets, eclipse polling. | Mood events, raids, event windows, letters, hediffs, and conditions cover many world beats. | Add narrow event windows or mood/event groups when a specific missed beat matters; avoid hardcoded one-off prose. |
| Social interaction counts | `TryInteractWith` increments counters for both pawns. | Social diary capture uses `PlayLog.Add` and XML batching. | Counters are for gameplay objectives; not needed unless day reflections need aggregate social facts. |
| Ritual participation counts | Ritual cleanup increments per-pawn counters. | Ritual and psychic ritual completion capture exists. | Completion entries are enough for diary; counters can wait until a reflection feature needs them. |
| Character-card overlay | Adds outcome row to vanilla bio UI. | Diary owns its own inspect tab. | Out of scope. Do not modify vanilla bio UI for diary architecture. |
| Exhaustive grammar facts | Emits many flat facts for grammar resolution. | Prompt summaries and enchantments emit curated fields. | Adapt as bounded collectors/templates only; no global scraper dump. |
| Backstory facts | Childhood/adulthood titles. | Mostly absent from prompts. | Planned context facts: title fields plus a capped echo if enabled. |
| Trait list | Trait labels/defs and summary. | Prompt enchantments/style may use some traits indirectly. | Planned context facts: capped list, no descriptions by default. |
| Skill best/worst/passions | Skill levels, labels, best/worst, passions. | Work prompts have limited work facts. | Planned context facts: best/worst/passions first, compact full map only when XML enables it. |
| Health summary | Visible hediff labels, parts, good/bad counts. | Hediff events and text decoration facts exist. | Candidate for improved pawn summary; cap and sanitize external labels. |
| Records | Kills, time as colonist, mental-state count. | Mostly skipped. | Planned context facts: useful for reflections and veteran identity, but keep optional. |
| Relationships/social opinion | Direct relations, friend/rival, opinion values. | Romance events and pair continuity exist. | Planned context facts: cap direct relations and avoid exposing giant social maps. |
| World snapshot | Weather, season, biome, time of day, colony name. | Surroundings/context are compact. | Add only missing structured facts: zone, season, hour/time phrase. |
| Current job/activity | Current job label, target, activity phrase. | Work sampler has current work facts. | Improve through shared activity snapshot; keep prose in Keyed/XML, not hardcoded C#. |
| Room/zone | Room role and zone name. | Mostly skipped. | Good candidate for optional surroundings context. |
| Inventory/apparel/abilities | Carried items, worn gear, abilities, psylink details. | Equipped weapon helper exists; most other details skipped. | Planned later context facts with strict caps and disabled-by-default full lists. |
| Faction/resource summaries | Nearby faction leaders, silver, special resource stock. | Quest/raid prompts carry focused faction/reward facts. | Planned later context facts, mostly disabled by default and event-domain-gated. |
| DLC identity | Royal title, ideoligion, xenotype, genes, anomaly markers. | `DlcContext` adds title/faith/xenotype; hediff/event groups string-match DLC content. | Keep centralized in `DlcContext` and XML. Add new DLC facts only through that route. |

## Run Card 19: Event-Source Addition Guide And Manifest

Priority: High

Problem:
- The Event Catalog is clean, but adding a new source still requires knowing the unwritten path
  across patches, `DiaryGameComponent` partials, event data/specs, XML groups, prompts, tests, and
  docs.

Suggested change:
- Add a concise "new event source checklist" to `DOCUMENTATION.md` or a small dedicated doc linked
  from it.
- Add or update a manifest table listing each event source, hook/scanner, owner partial, event data
  type, XML policy file, dedup key shape, DLC/mod gates, and test coverage.
- The checklist should require this sequence:
  1. Pick the smallest stable signal source.
  2. Add or reuse a `DiaryEventType`.
  3. Add a plain `XxxEventData` plus `XxxEventSpec` under `Source/Capture/`.
  4. Register the spec in `DiaryEventCatalog`.
  5. Add an adapter method on the relevant `DiaryGameComponent.*.cs` partial.
  6. Add a patch or scanner that forwards only narrow facts to the adapter.
  7. Add XML group/prompt/tuning policy.
  8. Add pure capture-policy tests.
  9. Update docs/changelog and build.

Why:
- Future agents get one path to follow, and review can focus on whether the source obeys the path
  instead of rediscovering the architecture each time.

Validation:
- Docs-only slice: no build required unless code comments or source files are changed.
- If the manifest references current code, verify every row against the current source.

Docs:
- This is itself a docs slice. No `CHANGELOG.md` entry is needed unless product behavior changes.

## Run Card 20: XML-Configurable Context Fact Pipeline

Priority: High

Problem:
- We will eventually need richer facts available for prompts: skill learning context, direct
  relations, backstories, traits, skill maps, pawn records, apparel, inventory, abilities, psylink,
  zone/time, faction leaders, and colony resources.
- We do **not** want those facts sent to every prompt, mixed into generic pawn summaries, or scraped
  at prompt time. They need to be captured once, saved, and rendered only when XML prompt templates
  opt into them.

Suggested change:
- Add a context fact collector under `Source/Generation/`:
  - `DiaryContextFactCollector` reads live `Pawn`/`Map`/`Faction` state at event creation.
  - `DiaryContextFactSet` is a plain DTO or compact key/value string with deterministic ordering.
  - `DiaryContextFactFormatter` handles pure formatting/capping where possible.
- Persist facts per POV on `DiaryEvent`:
  - Add saved strings like `initiatorContextFacts`, `recipientContextFacts`, and, only if needed,
    `neutralContextFacts`.
  - Use stable Scribe keys and default old saves to empty strings.
  - Keep `gameContext` for event facts only; per-pawn context belongs in the new per-POV fields.
- Add prompt rendering hooks:
  - Extend `DiaryPovPayload` / `PromptValues` with the selected POV's context fact string.
  - Add `PromptAssembler` source tokens such as `PovContextFact`, `OtherContextFact`,
    `InitiatorContextFact`, and `RecipientContextFact`.
  - Reuse `DiaryPromptFieldDef.contextKey`, so XML can render one fact with a normal field:
    `source=PovContextFact`, `contextKey=skill_best`, `label=important context`.
  - Update the Node/prompt-lab mirror for any new source token, because the prompt assembler golden
    check expects C# and JS source resolution to agree.
- Add XML-owned capture policy:
  - Prefer a small `DiaryContextFactDef` or tuning-backed policy with `key`, `category`, `enabled`,
    `defaultCapture`, `maxItems`, `maxChars`, and optional event-domain filters.
  - Capture only enabled fact categories. Rendering remains controlled by
    `DiaryPromptTemplateDef` fields.
  - Ship conservative defaults: small identity/social/skill facts on; heavy inventory/faction
    facts off until a prompt template or event domain explicitly needs them.

Fact slices:
- Phase 1, low-cost and broadly useful:
  - backstory titles and optional capped backstory echo;
  - trait list, capped and label-only;
  - best/worst skill, passions, compact skill map;
  - pawn records: kills, days as colonist, mental-state count;
  - direct relation summary, lover/family slots, friend/rival/opinion;
  - zone, season, hour, time phrase.
- Phase 2, medium-cost:
  - apparel summary, inventory summary;
  - abilities summary, psylink level, neural heat.
- Phase 3, broad colony/world facts:
  - friendly/hostile faction leaders;
  - silver count and selected resource summaries.

Why:
- This keeps the useful part of the reference pattern — lots of available facts — but fits Pawn
  Diary's architecture: capture once at the impure edge, save plain data, let pure prompt planning
  render only XML-selected fields.
- It avoids a global scraper dump and keeps prompt size under template control.

Risks:
- Save bloat. Facts must be capped, and heavy categories should be disabled by default.
- Prompt bloat. XML templates must add facts only where they improve a prompt shape.
- Localization. Model-facing labels and prose fragments must come from Keyed/Def text; external
  RimWorld/mod labels must be sanitized and capped.
- Staleness. Facts are snapshots. That is intentional, but docs should say facts describe the moment
  of capture, not the pawn's current state after regeneration.

Validation:
- Add pure tests for fact encoding/decoding, capping, deterministic ordering, and prompt assembler
  source resolution.
- Add prompt pipeline tests proving XML can include one context fact without changing other
  templates.
- Build after C# changes.
- Inspect prompt-test output for at least one pair, solo, day-reflection, arrival, and death prompt
  before enabling more than Phase 1.

Docs:
- Update `DOCUMENTATION.md` prompt/context sections and file map.
- Add a `CHANGELOG.md` entry when implemented.

## Run Card 21: Completed-Work Capture Evaluation

Priority: Medium

Problem:
- Work capture currently relies on periodic current-job sampling. That is stable and cheap, but it
  can miss short completed jobs or capture a pawn mid-task rather than at the meaningful completion
  moment.

Suggested change:
- First, evaluate whether the current work sampler misses important work moments in practice. Do not
  add a hook just because one exists.
- If completion capture is justified, add a narrow defensive hook around job completion:
  - capture the live job/work type before RimWorld advances to the next job;
  - after success, forward the captured plain facts to a `DiaryGameComponent` adapter;
  - route through `WorkEventData` if the existing contract is enough, or add a separate
    completed-work data type if policy differs.
- Keep XML odds/cooldowns and dedup in control so completed work cannot flood diaries.

Why:
- Completion hooks can produce more accurate work moments, but they are hot and easy to over-record.
  The right target is a measured improvement over the existing sampler, not a second competing work
  system.

Adoption decision:
- Not approved for implementation yet. First run prompt/debug captures from ordinary work events and
  decide whether the current sampler actually produces bad or late diary pages.

Risks:
- Job lifecycle hooks are hot. Avoid allocations and expensive label resolution before cheap gates.
- The hook must tolerate null jobs, queued jobs, failed jobs, and pawns that are no longer eligible.

Validation:
- Add/extend pure tests for the work capture decision.
- Build.
- Manual in-game smoke test is valuable for this slice because job timing is lifecycle-sensitive.

Docs:
- Update `DOCUMENTATION.md` event-source and work sections if implemented.
- Add `CHANGELOG.md` entry if behavior changes.

## Run Card 22: Shared Event Adapter Helpers

Priority: Medium

Problem:
- Many `DiaryGameComponent.Record*` methods repeat the same adapter chores: gameplay gate, eligibility
  snapshot, XML/user enable gates, catalog lookup, decision switch, dedup key handling, and event
  factory calls. Repetition is sometimes clearer, but it raises the cost of adding new sources.

Suggested change:
- Do not introduce a generic framework up front.
- Extract only the pieces that are already duplicated across several sources and are hard to keep
  consistent, such as:
  - a small helper for catalog lookup with null-as-drop behavior;
  - shared dedup-key builders for common solo/pair shapes;
  - source-local context builders that return plain data before event creation.
- Keep source-specific branching visible in each partial. The helper should remove boilerplate, not
  hide policy.

Why:
- New event additions should feel like filling a small adapter pattern, while still making event
  behavior readable in the source file that owns it.

Risks:
- A too-generic adapter layer will make reviews harder and can obscure special cases like death
  descriptions, batched interactions, or day reflections.

Validation:
- Behavior-preserving refactor: existing capture-policy tests should pass unchanged.
- Build.
- Add focused tests only if pure helper behavior is non-trivial.

Docs:
- Update `DOCUMENTATION.md` architecture/file-map sections if new helper files are added.
- Add `CHANGELOG.md` entry for structural code changes.

## Run Card 23: Event-Source Tests And Debug Fixtures

Priority: Medium

Problem:
- The pure Event Catalog has good coverage, but new sources can still regress at the adapter/prompt
  boundary if they lack fixtures showing the final prompt fields.

Suggested change:
- For every new source, add:
  - pure `XxxEventData.Decide` tests in `DiaryCapturePolicyTests`;
  - prompt/planner tests when the source adds new prompt fields;
  - a Prompt Suite fixture or dev debug trigger when feasible, so maintainers can inspect the prompt
    without waiting for rare gameplay.
- Keep fixtures lightweight and deterministic. Do not require RimWorld assemblies for pure tests.

Why:
- Event additions fail most often at boundaries: a source records, but the prompt lacks the facts
  that made the event worth recording. Fixtures make that visible.

Validation:
- Run the relevant pure test projects.
- Build after C# changes.

Docs:
- Document any new dev fixture in `DOCUMENTATION.md` only if maintainers need to know it exists.

## Run Card 24: Skill-Burst And Non-Romance Relation Signals

Priority: Medium

Problem:
- Two requested future capture areas are signals, not just context facts:
  - noticeable skill learning bursts or level-up moments;
  - direct relation changes beyond romance, such as family, rivalry, bonded social roles, or
    mod-added relation defs.
- Capturing these as ordinary prompt context is useful, but it does not record that "this just
  happened." Capturing every tick/change as a diary event would be too noisy.

Suggested change:
- Skill learning:
  - Add a defensive hook around `Pawn_SkillTracker.Learn` only after confirming the method shape for
    the current RimWorld version.
  - Snapshot plain facts: pawn id, skill defName/label, old/new level if available, XP amount,
    passion, and whether a level boundary was crossed.
  - Route through either a new `SkillEventData` or a day-reflection signal. Default should be
    conservative: level-up or large burst only, with XML cooldowns.
  - Do not record tiny continuous learning ticks.
- Direct relation changes:
  - Extend the existing `Pawn_RelationsTracker.AddDirectRelation` path, but keep current romance
    behavior intact.
  - Add a separate `RelationEventData` or day-reflection signal for non-romance relations.
  - XML policy should decide which relation defs/tokens are recordable and whether they become
    immediate diary entries or reflection-only signals.
  - Keep pair dedup strict because RimWorld may add mirrored relations.
- Both sources should feed the context fact layer:
  - A skill event should ensure skill facts are captured for that entry.
  - A relation event should ensure relation/friend/rival facts are captured for that entry.

Why:
- These are meaningful personal-history changes, but they are noisy at raw hook level. The safe
  shape is signal -> plain payload -> pure decision -> XML cooldown/threshold -> optional prompt
  fields.

Risks:
- Hot hooks. Skill learning can fire frequently, so cheap gates must happen before label resolution
  or allocation.
- Relation spam. Family and modded relation graphs can change in clusters; dedup and XML filters are
  required.
- Save compatibility. Any new saved fields should be additive and default empty/false.

Validation:
- Add pure capture-policy tests for drop/record thresholds, cooldowns, mirrored relation dedup, and
  XML-disabled behavior.
- Add prompt tests only for any new prompt fields.
- Build.
- Manual smoke test is valuable because both hooks are lifecycle-sensitive.

Docs:
- Update `DOCUMENTATION.md` event-source table if implemented.
- Add `CHANGELOG.md` entry for behavior changes.

## Run Card 25: Live Observed-Condition Monitor

Priority: High

Problem:
- Some important colony problems are lasting states, not one-shot events. Examples: a map is still
  dangerous, hostile threats are still present, gray flesh evidence is still unresolved, a
  metalhorror has actually emerged, or an active game condition is still affecting the map.
- The current event-window model is signal-driven. It can remember "a signal happened" and remove
  the active window after `timeoutTicks`, but it cannot prove that the underlying problem is still
  active or ended. This is why long-lived anomaly windows are unsafe: missed signals, reloads, slow
  player response, and modded timing all make fixed tick durations drift from reality.

Suggested change:
- Add a new XML Def, tentatively `DiaryObservedConditionDef`, separate from
  `DiaryEventWindowDef`.
- Add a saved state model, tentatively `ActiveObservedConditionState`, with additive Scribe fields:
  `conditionDefName`, `conditionKey`, `scope`, `mapUniqueId`, `subjectPawnId`,
  `firstObservedTick`, `lastObservedTick`, `lastSeenEvidenceLabel`,
  `lastSeenEvidenceDefName`, `startRecorded`, and `endRecorded`.
- Add a scanner partial, tentatively `DiaryGameComponent.ObservedConditions.cs`, called from
  `GameComponentTickInner` on an XML-owned interval. It should:
  1. Load enabled condition defs.
  2. Build current observations from cheap live game reads.
  3. Diff observations against saved active state.
  4. Record start/end diary pages only after configured debounce.
  5. Feed active prompt candidates while the condition is still observed.
- Add pure monitor policy code under `Source/Capture/` or `Source/Pipeline/` for diff decisions, so
  tests can cover start debounce, missing/end debounce, duplicate prevention, and reload catch-up
  without RimWorld assemblies.

Initial observer types:
- `MapDanger`: active when a player-home map's danger rating or hostile spawned-pawn count crosses
  XML thresholds. Evidence can include danger level, hostile count, and the newest recent threat
  letter label, but letters should annotate the state rather than define it.
- `GameCondition`: active while `map.GameConditionManager.ActiveConditions` contains a matching
  defName string. This is cheap and no-DLC safe when matchers are plain strings.
- `PawnHediff`: active while relevant spawned/colonist pawns currently carry a matching hediff
  defName string. Use this for visible, current pawn conditions; keep hidden-anomaly wording out of
  prompts unless the fact is observable.
- `ThingPresent`: active while matching spawned things/filth/evidence are still present on the map.
  Run at a conservative interval, cap evidence labels/counts, and avoid per-tick all-thing scans.
- `RecentEvidence`: a bounded fallback for cases where the game exposes only a letter/signal and no
  stable state object. This may have a TTL, but the docs must call it "recent evidence," not proof
  that the underlying threat is still active.

First target conditions:
- `MapThreatActive`: derive from live map danger and hostile presence. Use recent raid/threat
  letters only as labels.
- `AnomalyEvidencePresent`: derive from present gray flesh/evidence things or filth. This replaces
  the disabled signal-plus-timeout metalhorror suspicion window.
- `MetalhorrorEmergence`: derive from currently spawned/observable metalhorror-related things or
  hediffs. This should be a different condition from "suspicion" so prompts do not reveal hidden
  mechanics before the game has revealed them.

Why:
- The source of truth becomes the current game state, not "we saw something N ticks ago."
- Save/load becomes self-healing: active threats are rediscovered after load, and stale saved
  states end when scans no longer observe them.
- XML still owns policy, thresholds, prompt weight, and wording, so adding a new lasting condition
  does not require hardcoded C# prose.
- The model fits the existing architecture: impure scan -> plain observation DTO -> pure diff
  decision -> adapter records event/prompt state.

Benefits over trigger-plus-timeout windows:
- Fewer stale prompts after the problem is gone.
- Fewer missed prompts after save/load or missed hooks.
- Cleaner distinction between "recent clue" and "active threat."
- Easier addition of new lasting conditions because each observer type has one tested contract.

Costs and risks:
- Scanning can be expensive if observer definitions are broad. Use XML intervals, cheap gates, caps,
  and map/home checks before resolving labels.
- False positives are possible if matchers are too broad. Prefer exact defName strings and separate
  "evidence" conditions from "confirmed threat" conditions.
- Hidden anomaly information can leak if prompt labels are careless. Prompts should use observable
  evidence labels for suspicion and reserve explicit monster names for confirmed emergence.
- The saved state list is a new persistence surface. Fields must be additive, old saves must default
  cleanly, and disabled/removed defs should age out without errors.

Validation:
- Pure tests for the observed-condition diff:
  - starts only after `startDebounceTicks`;
  - does not duplicate a recorded start while still observed;
  - refreshes evidence while observed;
  - ends only after `endDebounceTicks` missing;
  - rediscovers active state after a simulated reload;
  - handles multiple maps and subject-pawn scoped conditions independently.
- XML parse/load smoke test for the new Def.
- Build after C# changes.
- Manual in-game smoke test for one map-threat condition and one anomaly-evidence condition before
  enabling high prompt weight defaults.

Docs:
- Update `DOCUMENTATION.md` event-window/event-source sections to explain signal windows versus
  observed conditions.
- Add a `CHANGELOG.md` entry when implemented.

## Run Card 1: API Lane Identity And Labels

Priority: High

Status: Resolved 2026-06-25. Implemented as shared pure `ApiLaneIdentity` and `ApiLaneLabels`
helpers with call-site rewrites, focused `DiaryPipelineTests`, documentation updates, and a Debug
DLL rebuild.

Evidence:
- `Source/Generation/LlmClient.cs:416` (`LaneKey`)
- `Source/Generation/LlmClient.cs:1189` (`SameAttemptLane`)
- `Source/Core/DiaryGameComponent.Generation.cs:706` / `:724` (`FindMatchingActiveLane`, `SameGenerationLane`)
- `Source/Settings/PawnDiaryMod.cs:1721` / `:1740` (`FetchTargetStillMatches`, `ConnectionTestTargetStillMatches`)
- `LaneList` / `LaneLabel` / `TrimForLog` are duplicated across `LlmClient`, `DiaryGameComponent.Generation`, and `PawnDiaryMod`.

Problem:
- Lane identity is implemented three ways with subtle equality differences. A future endpoint field change can desync failover, cooldowns, and stale settings-row matching.

Suggested change:
- Add one `ApiLaneIdentity` value type, likely under `Source/Settings/` or `Source/Generation/` depending on existing ownership.
- Provide `ApiLaneIdentity.Of(ApiEndpointConfig)`.
- Support explicit equality modes/parameters, for example including or excluding API key and reasoning/model fields as required by current call sites.
- Add a separate sanitized `ApiLaneLabels` helper for logging/display. Never log API keys.

Pros:
- One source of truth for lane identity.
- Removes duplicated label/list formatting.
- Low behavior risk if existing equality quirks are encoded explicitly.

Cons / risks:
- Do not accidentally flatten per-site equality differences.
- Formatter must not leak API keys.

Validation:
- Build.
- Add pure tests if there is an existing suitable test project for settings/generation helpers.
- Manually audit each old call site and verify the new equality mode matches old behavior.

Docs:
- Update architecture/settings sections if a new helper type/module is introduced.
- Add `CHANGELOG.md` entry if code changes.

## Run Card 2: Extract `PawnFactCapture` From `DiaryEvent`

Priority: High

Status: Resolved 2026-06-25. Implemented as a new guarded collector
`Source/Generation/PawnFactCapture.cs` (modeled after `DlcContext`) owning the live pawn reads, with
pure value setters `DiaryEvent.SetTextDecorationFacts` / `SetStaggeredIntensity` replacing the old
`Pawn`-taking capture methods. Event-record call sites in `DiaryGameComponent.EventFactory.cs` now
snapshot plain `int`/`string` values and store them. Saved fields and Scribe keys are unchanged, the
Debug DLL was rebuilt, and all five pure test projects pass (602 assertions).

Evidence:
- `Source/Models/DiaryEvent.cs:1200` (`CaptureTextDecorationContext`)
- `Source/Models/DiaryEvent.cs:1220` (`CaptureStaggeredIntensity`)
- `Source/Models/DiaryEvent.cs:1251` (`StaggeredIntensityForPawn`)
- `Source/Models/DiaryEvent.cs:1263` (`LowConsciousnessStaggeredIntensity`)
- `Source/Models/DiaryEvent.cs:1294` (`IntoxicationStaggeredIntensity`)
- `Source/Models/DiaryEvent.cs:1317` (`IsIntoxicatingHediff`)
- `Source/Models/DiaryEvent.cs:1376` (`PawnTextDecorationContext`)

Problem:
- `DiaryEvent` is a persisted model but reads live `Pawn` health, hediffs, capacities, and traits. This is the clearest pure/impure barrier violation found.

Suggested change:
- Add `Source/Generation/PawnFactCapture.cs` or similar, modeled after `DlcContext` as the one home for guarded pawn reads.
- Move live pawn reads from `DiaryEvent` into that impure collector.
- Leave only serialized text-decoration facts and staggered-intensity fields/setters on `DiaryEvent`.
- Update call sites from `diaryEvent.Capture*(role, pawn)` to collect facts first and set plain values on the event.

Pros:
- Restores `DiaryEvent` purity.
- Makes pawn snapshot policy auditable in one place.
- Unblocks pure tests around `DiaryEvent`/view logic.

Cons / risks:
- Small call-site churn.
- Keep the new collector out of `Source/Pipeline` if it reads live Verse/RimWorld state.

Validation:
- Build.
- Add/extend tests for any pure helper extracted from the old methods.
- Confirm no direct `pawn.genes`, `pawn.royalty`, or `pawn.ideo` reads are introduced outside `DlcContext`.

Docs:
- Update `DOCUMENTATION.md` architecture/barrier discussion.
- Add `CHANGELOG.md` entry.

## Run Card 3: Move Settings Instruction Resolution Off `PawnDiarySettings`

Priority: Medium

Status: Resolved 2026-06-25. The `InstructionFor*` family (10 Def/signal-takers + `InstructionForGroup`)
moved from `PawnDiarySettings` to static methods on `InteractionGroups` (beside `Classify*`). They read
no settings state (instructions are XML-only — no saved overrides), so the move restores the save-DTO
boundary. All 14 capture call sites in `DiaryGameComponent.*.cs` now call
`InteractionGroups.InstructionFor*(...)`; the stale caller name in `PromptVariants.cs` and the stale
"user-defined instruction" doc comment were fixed; DOCUMENTATION §2/§7 and CHANGELOG updated; Debug DLL
rebuilt; all five pure test projects pass (602 assertions).

Evidence:
- `Source/Settings/PawnDiarySettings.cs:750-1024` (`InstructionFor*` family)
- `Source/Settings/PawnDiarySettings.cs:1060` (`PromptVariants.Pick` via `Rand.Range`)

Problem:
- `PawnDiarySettings` is a save DTO, but it performs DefDatabase classification and RNG policy selection.

Suggested change:
- Introduce `DiaryInstructionResolver` or place the logic on `InteractionGroups` if that is more consistent.
- Settings should keep persisted overrides only.
- Resolver should classify the incoming Def/string, ask settings for the override, and roll prompt variants in the capture path.

Pros:
- Removes classification/RNG from persistence object.
- Makes policy lookup easier to test and reason about.

Cons / risks:
- Mechanical caller churn.
- Preserve the current roll timing. Rebuttal resolved this as safe: comments around `PawnDiarySettings.cs:1057-1059` say the result is immediately frozen into `diaryEvent.instruction` by capture callers.

Validation:
- Build.
- Add pure tests for resolver behavior if feasible.
- Verify callers still persist `diaryEvent.instruction` immediately.

Docs:
- Update `DOCUMENTATION.md` settings/prompt-policy section.
- Add `CHANGELOG.md` entry.

## Run Card 4: Move `DiaryEvent.NameForRole` Localization To Adapter

Priority: Low

Status: Resolved 2026-06-25. `DiaryEvent.NameForRole` (which called `.Translate()` for the
neutral/colony POV) was deleted; its sole caller, the adapter's
`DiaryPipelineAdapters.DirectSpeechInstructionFor`, now resolves the saved initiator/recipient
name inline and falls back to `"PawnDiary.Prompt.Colony".Translate().Resolve()` for neutral —
matching the colony-name handling already used by `ToPayload`. Role comparison is unchanged
(`RoleEquals` and the adapter's existing check are both `string.Equals(..., OrdinalIgnoreCase)`),
direct-speech prompt text is unchanged, the Debug DLL was rebuilt, and no pure test applies (the
touched code is impure and `NameForRole` was never pure-testable).

Evidence:
- `Source/Models/DiaryEvent.cs:1089` calls `.Translate()`.
- Sole production caller found in review: `DiaryPipelineAdapters.DirectSpeechInstructionFor` around `Source/Generation/DiaryPipelineAdapters.cs:355`.

Problem:
- The persisted model reaches localization. This is small but violates the model boundary.

Suggested change:
- Move the colony-label translation into the adapter/caller.
- Do not simply delete the method without replacing the caller behavior.

Pros:
- Keeps localization in the adapter layer.

Cons / risks:
- Minimal.

Validation:
- Build.
- Confirm direct-speech prompt text is unchanged.

Docs:
- Changelog only if the slice changes code structure.

## Run Card 5: Move Hardcoded Tunables To XML

Priority: High

Status: Resolved 2026-06-25. Three pockets of hardcoded feature policy moved to XML with the
original values as code fallbacks. The first-person Consciousness gate
(`DiaryTuningDef.minimumConsciousnessForFirstPersonGeneration`, default 0.11) and the 0..4
display-staggering intensity thresholds for low Consciousness and intoxication severity are now
`DiaryTuningDef` fields (read via `DiaryTuning.Current` in `PawnFactCapture` and
`DiaryGameComponent.Generation`). The positive/negative mood-impact `GameCondition` defName families
are now `DiaryTuningDef.positiveMoodConditionDefNames` / `negativeMoodConditionDefNames`, read by
`DiaryContextBuilder.IsKnownPositiveCondition` / `IsKnownNegativeCondition` through a shared
case-insensitive exact-match helper. Capture-time intoxication detection no longer keeps a parallel
keyword list: `PawnFactCapture.IsIntoxicatingHediff` builds a hediff fact and reuses the same
`Diary_TextDecorations` `StaggeredWordSizes` rule list as render-time decoration, via a new pure
`DiaryTextDecorations.HediffMatchesStaggeredRules` (robust to partially-populated modder rules: an
unset name list contributes nothing rather than matching everything). The decoration rule's label
list gains `hangover` (XML + C# fallback) to preserve alcohol-withdrawal coverage. `DiaryTuningDef.xml`
ships the matching values; a new `DiaryTextDecorationTests` case covers the helper; the Debug DLL was
rebuilt; all five pure test projects pass (609 assertions). No save-format, DLC, or localization change.

Evidence:
- `Source/Models/DiaryEvent.cs:1327-1338` hardcoded intoxication keyword list.
- `Source/Models/DiaryEvent.cs:1271-1289` display staggering thresholds.
- `Source/Core/DiaryGameComponent.Generation.cs:27` generation consciousness gate.
- `Source/Generation/DiaryContextBuilder.cs:1345` / `:1360` hardcoded positive/negative mood condition defNames.

Problem:
- Feature policy is hardcoded in C# instead of XML, violating repo rule 3.

Suggested change:
- Route intoxication labels through existing `DiaryTextDecorationRule.anyHediffLabelContains` rather than adding a parallel tuning field.
- Move consciousness thresholds to `DiaryTuningDef`, preserving separate values for generation gate vs display staggering.
- Move mood positive/negative condition string lists to XML/Def-backed tuning with C# fallback defaults.

Pros:
- Modders/DLCs can extend classifications by string without code.
- Aligns with current string-matcher pattern.

Cons / risks:
- Ship XML defaults and safe C# fallbacks.
- XML entries should remain plain strings; do not reference DLC defs unless `MayRequire` is appropriate.

Validation:
- Build.
- XML parse/verification hooks if available.
- Add/extend tests for threshold/list resolution if there are pure test projects.

Docs:
- Update `DOCUMENTATION.md` tuning/localization or Def section.
- Add `CHANGELOG.md` entry.

## Run Card 6: Split `DiaryContextBuilder` By Concern

Priority: Medium

Status: Resolved 2026-06-25. Implemented as three new focused files alongside the trimmed
`DiaryContextBuilder.cs`: `Source/Generation/DiaryLineCleaner.cs` (the pure, Verse-free `CleanLine`
text cleaner — isolated so it is unit-testable, now covered by a pure test in
`DiaryTextDecorationTests`), `Source/Generation/DiaryBuckets.cs` (the localized
mood/pain/opinion/age/beauty/bleed/effect band formatters — `.Translate()`-bound, so main-thread and
not Verse-free), and `Source/Generation/MoodImpactClassifier.cs` (the per-pawn GameCondition
mood-direction policy: `DetermineMoodImpact`, `GetMoodOffsetFromConditionThoughts`, and helpers).
`DiaryContextBuilder` now holds only the impure Pawn/Map/Archive context collectors and delegates to
the three. `AgeBucket` now takes a plain `int years` (the caller snapshots
`AgeBiologicalYears`) instead of a `Pawn`. All 89 `CleanLine` call sites across
capture/core/generation/pipeline and the two mood-method call sites in
`DiaryGameComponent.MoodEvents.cs` were rewritten to the new homes. Behavior is byte-for-byte
unchanged; no save-format, DLC, or localization change; Debug DLL rebuilt; all five pure test
projects pass (622 assertions).

Evidence:
- Pure helpers: `Source/Generation/DiaryContextBuilder.cs:1003` (`CleanLine`), `:1046` (`MoodBucket`), `:1067` (`PainBucket`), `:1098` (`OpinionBucket`), `:1124` (`AgeBucket`).
- Impure collectors: `:294` (`BuildSurroundingsSummary`), `:526` (`BuildPawnSummary`), `:632-1001` summary family.

Problem:
- One static builder mixes pure formatting/bucketing, impure Pawn/Map collection, and mood policy.

Suggested change:
- Keep impure collectors in `DiaryContextBuilder`.
- Move pure bucket/format helpers to `DiaryBuckets.cs` or similar.
- Move mood-impact policy to `MoodImpactClassifier`.

Pros:
- Pure helpers become testable without Verse objects.
- File responsibilities become clear.

Cons / risks:
- Avoid broad DTO rewrites in the first slice; start with pure helper extraction.

Validation:
- Build.
- Add/extend pure tests for bucket behavior.

Docs:
- Update `DOCUMENTATION.md` generation/context section.
- Add `CHANGELOG.md` entry.

## Run Card 7: Split `PawnDiaryMod` Settings UI And Async Controllers

Priority: Medium

Status: Completed 2026-06-25. `PawnDiaryMod` is now a small partial entry point, settings UI sections
live in focused partial files, and settings-window model fetch/connection-test state lives in
`ApiConnectionController`.

Evidence:
- `Source/Settings/PawnDiaryMod.cs:89` whole settings window.
- `:169` API endpoint editor.
- `:730` Prompt Studio.
- `:1054` Persona Studio.
- `:1410` connection test.
- `:1521` model fetch.
- `:1595` / `:1656` apply async results.

Problem:
- The `Mod` subclass is also a large IMGUI renderer and async HTTP state machine.

Suggested change:
- First do a partial-class file split: API lanes, Prompt Studio, Persona Studio, settings widgets.
- Then extract an `ApiConnectionController` owning connection tests, model fetch, pending results, and stale-row matching.
- Extract common settings UI widget helpers.

Pros:
- Easier navigation.
- Thread-handoff code isolated.
- Sets up reuse of `ApiLaneIdentity` from Run Card 1.

Cons / risks:
- Keep `.Translate()` and shared-collection writes on the main GUI thread.
- Immediate-mode UI state must remain explicit.

Validation:
- Build.
- Manually inspect settings-window flows after compile if possible in-game.

Docs:
- Update `DOCUMENTATION.md` settings UI structure.
- Add `CHANGELOG.md` entry.

## Run Card 8: Extract Persona And Override State From `PawnDiarySettings`

Priority: Low to Medium

Status: Resolved 2026-06-25. Writing-style (persona) CRUD, normalization, and theme policy moved to a
new `Source/Settings/PersonaPresetStore.cs` (with `PersonaPresetConfig`), and the duplicated
per-key override-dictionary plumbing behind the event-prompt and event-enhancement maps collapsed into
one reusable `Source/Settings/PromptOverrideDictionary.cs`. `PawnDiarySettings` owns one instance of
each (`personaPresets`, `eventPromptOverrides`, `eventEnhancementOverrides`) and delegates `ExposeData`
to them; each serializes under its original Scribe key, so existing saves load unchanged. Callers go
through the stores directly; only `ResetAllEventPromptOverrides` and `CustomizedEventPromptCount`
remain on settings (they span both event maps). System-prompt, connection, and generation settings are
unchanged. Behavior and save data unchanged; Debug DLL rebuilt; all five pure test projects pass
(622 assertions).

Evidence:
- `Source/Settings/PawnDiarySettings.cs:1146-1358` persona CRUD.
- `Source/Settings/PawnDiarySettings.cs:400-580` event prompt override dictionary plumbing.
- `Source/Settings/PawnDiarySettings.cs:261` serializes `personaPresets` via one `Scribe_Collections.Look(..., LookMode.Deep)` key.

Problem:
- Settings save object also owns persona catalog mutation and generic override-dictionary logic.

Suggested change:
- Add `PersonaPresetStore` while preserving the same Scribe key.
- Add a reusable override-dictionary helper if it reduces duplicate normalize/look-up code.

Pros:
- Shrinks settings object.
- Keeps persona logic coherent.

Cons / risks:
- Preserve Scribe keys exactly.
- `.Translate()` currently in `AddCustomPersona` must remain main-thread.

Validation:
- Build.
- Old settings save compatibility reasoning.

Docs:
- Update `DOCUMENTATION.md` settings/persona section.
- Add `CHANGELOG.md` entry.

## Run Card 9: Split `DiaryGameComponent.Generation.cs`

Priority: Medium

Evidence:
- `Source/Core/DiaryGameComponent.Generation.cs:615-769` lane policy.
- `:852-1085` title subsystem.
- `:1257-1361` live pawn lookup.
- `:796-824` lane label/list duplicates.

Problem:
- One partial handles generation orchestration, API lane selection, title generation, and pawn lookup.

Suggested change:
- Extract `ApiLaneSelection` first, taking lane list, routing mode, and an `isCooling` probe such as `Func<ApiEndpointConfig, bool>`.
- Extract title generation into a `DiaryGameComponent.Titles.cs` partial or collaborator.
- Extract live pawn lookup into `PawnLocator`.

Pros:
- Smaller generation file.
- Lane selection becomes testable with plain config snapshots.

Cons / risks:
- Keep lane selector pure-ish; do not let it read global settings or Defs directly.

Validation:
- Build.
- Add pure tests for lane selection if feasible.

Docs:
- Update `DOCUMENTATION.md` core/generation map.
- Add `CHANGELOG.md` entry.

## Run Card 10: Extract `DiaryGameComponent` Repository / State Bags

Priority: Medium

Status: Resolved 2026-06-25. Implemented as a new `Source/Core/DiaryEventRepository.cs` owning the
saved `diaryEvents` list, the `eventsById` lookup index, and `FindEvent`/`Register`/`RemoveEvent`/
`RemoveEvents`/`RebuildIndex`/`EnsureIndexReady`, plus a never-null `AllEvents` view and the
`ExposeEvents("diaryEvents")` Scribe helper. `DiaryGameComponent` constructs the repository and
remains the only RimWorld lifecycle/save owner: `ExposeData` delegates the event list to the
repository and rebuilds the index from `PostLoadInit`. Every partial now goes through the repository
(`events.FindEvent`, `events.Register`, `events.AllEvents`, `events.ContainsEvent`,
`events.RemoveEvents`); the private `FindEvent`/`RegisterDiaryEvent`/`RebuildEventIndex` were removed
and the dead `if (diaryEvents == null)` guards were dropped (the store is guaranteed non-null). The
`"diaryEvents"` Scribe key is unchanged, so existing saves load unchanged; behavior is identical. The
secondary state-bag suggestion (`PendingBatchState`/`RecentDedupState`) was intentionally not taken:
those dictionaries are each scoped to one `Record*` domain and already routed through the shared
`RecentlyRecorded` helpers, so a bag would add churn without reducing real coupling. The Debug DLL
was rebuilt; all five pure test projects pass (622 assertions). No pure test was added because the
repository holds `DiaryEvent` (an `IExposable`) and calls `Scribe`, so it is not Verse-free and cannot
join the standalone pure test projects.

Evidence:
- `Source/Core/DiaryGameComponent.cs:60-118` central saved/transient state.
- `Source/Core/DiaryGameComponent.cs:331` tick orchestration.

Problem:
- The partial class is mostly per-concern, but central state ownership is large and shared.

Suggested change:
- Extract `DiaryEventRepository` around `diaryEvents`, `eventsById`, `FindEvent`, `RegisterDiaryEvent` before broader services.
- Then extract state bags such as `PendingBatchState` or `RecentDedupState` if they reduce private-field coupling.
- Keep `DiaryGameComponent` as the only RimWorld lifecycle/save owner.

Pros:
- Clarifies ownership with less blast radius than service extraction.

Cons / risks:
- Do not make services receive live `Pawn`/`Def` when a plain snapshot would do.
- Preserve save/load lifecycle semantics.

Validation:
- Build.
- Save/load smoke reasoning.

Docs:
- Update `DOCUMENTATION.md` core component state section.
- Add `CHANGELOG.md` entry.

## Run Card 11: Optional `LlmRequestJsonBuilder`

Priority: Low

Status: Resolved 2026-06-25. Extracted pure `Pipeline/LlmRequestJsonBuilder`, kept the existing
mode-specific request body behavior in that builder, rewired `LlmClient` as the transport adapter, and
covered Chat Completions / Responses serialization in `DiaryPipelineTests`; Debug build and all five
pure test projects pass (626 assertions).

Evidence:
- `Source/Generation/LlmClient.cs:1099-1225` request JSON construction and escaping.

Problem:
- JSON construction is separable from HTTP transport, but it currently works and is auditable.

Suggested change:
- Extract pure `LlmRequestJsonBuilder` only when JSON request logic next needs modification.
- Keep `reasoning_effort` and `max_output_tokens` mode-specific logic with the builder.

Pros:
- Testable pure serialization.

Cons / risks:
- Not urgent. Avoid churn unless changing this area anyway.

Validation:
- Build.
- Add serialization tests for each compatibility mode if extracted.

Docs:
- Update docs/changelog if implemented.

## Run Card 12: LLM Parser Marker Duplication Test

Priority: Low

Status: Resolved 2026-06-25. Added a focused `LlmResponseParserTests` assertion that the private
response-parser speech markers match `DiaryDirectSpeechParser` defaults, with no production code
changes.

Evidence:
- `Source/Generation/LlmResponseParser.cs:1082-1083` mirrors speech markers from `DiaryDirectSpeechParser`.

Problem:
- Sentinels can silently diverge.

Suggested change:
- Prefer a test assertion that the local constants match `DiaryDirectSpeechParser.DefaultOpenMarker` / `DefaultCloseMarker`, preserving parser isolation.

Pros:
- No production code churn.

Cons / risks:
- Minor.

Validation:
- Run relevant parser tests plus build.

Docs:
- Changelog if test-only change is tracked.

## Run Card 13: Move `DiaryPipelineAdapters` Out Of Pure `Pipeline` Folder

Priority: Low

Status: Resolved 2026-06-25. `DiaryPipelineAdapters` moved to
`Source/Generation/DiaryPipelineAdapters.cs` with namespace/API stable, leaving `Source/Pipeline` for
pure prompt/response/decor and API policy helpers. Documentation and changelog were updated, and the
Debug build succeeds.

Evidence:
- `Source/Generation/DiaryPipelineAdapters.cs:1` says the file is impure.
- It accepts `DiaryEvent`, translates strings, and reads settings/Defs around `:18`, `:56`, `:91`, `:97`.

Problem:
- Folder boundary implies `Pipeline` is pure, but this adapter is intentionally impure.

Suggested change:
- Move the file to `Source/Generation` or `Source/Adapters` while keeping namespace/API stable if that minimizes churn.
- Optional split: `DiaryEventPayloadFactory` and `DiaryPolicySnapshotProvider`.

Pros:
- Makes folder/module boundary honest.

Cons / risks:
- Update docs/file map.

Validation:
- Build.

Docs:
- Update `DOCUMENTATION.md` file map/architecture.
- Add `CHANGELOG.md` entry.

## Run Card 14: Split `DiaryTextDecorations`

Priority: Low

Status: Resolved 2026-06-25. `DiaryTextDecorations` is now a stable facade over split pure files for
contracts, matching, fact serialization, and rich-text transforms. Documentation and changelog were
updated, `DiaryTextDecorationTests` and `DiaryPipelineTests` pass, and the Debug build succeeds.

Evidence:
- `Source/Pipeline/DiaryTextDecorations.cs` combines constants/DTOs, rule matching, rich-text mutation, fact serialization, tag parsing, and condition matching.

Problem:
- One large pure-ish utility owns several separable subsystems.

Suggested change:
- Split into separate files/classes behind a facade: `DiaryTextDecorationContracts`, `DiaryTextDecorationMatcher`, `DiaryTextDecorationFactCodec`, `DiaryRichTextDecorators`.

Pros:
- Easier tests and review.

Cons / risks:
- More internal types.
- Do not introduce Unity GUI dependencies into matcher/codec.

Validation:
- Build.
- Existing decoration tests if present; add focused tests if feasible.

Docs:
- Update docs/changelog if implemented.

## Run Card 15: `ITab_Pawn_Diary` UI Extraction

Priority: Low

Status: Resolved 2026-06-25. Extracted the first cache slice into
`Source/UI/DiaryTabVisibleEntriesCache.cs`, a UI-layer helper owned by the tab that stores live pawn /
render-token state, visible-entry filters, year pages, transient dev previews, generating counts, and
per-year ordering. `FillTab` now asks that helper for raw entries, visible entries, years, and ordered
pages while keeping measurement and drawing local. Behavior and save data are unchanged; no pure test
applies because the helper stores live `Pawn` references and uses RimWorld UI state.

Evidence:
- `Source/UI/ITab_Pawn_Diary.cs:22-36` caches.
- `:56` style accessors.
- `:229` `FillTab` retrieves data, filters, layouts, measures, scrolls, and draws.
- `:538` visible cache rebuild.
- `Source/UI/ITab_Pawn_Diary.EntryCards.cs:653` card measurement.

Problem:
- UI partials are split by file but still one view object with data retrieval, cache, layout, measurement, rendering, and dev controls.

Suggested change:
- Extract `DiaryTabVisibleEntriesCache` first. Treat it as impure if it stores live UI pawn references.
- Later extract `DiaryEntryCardRenderer` / `DiaryEntryMeasurer` as UI-layer classes, not pure pipeline classes.

Pros:
- `FillTab` becomes focused on drawing.

Cons / risks:
- Immediate-mode UI state ownership is easy to get wrong.

Validation:
- Build.
- In-game UI smoke test if possible.

Docs:
- Update docs/changelog if implemented.

## Run Card 16: Split `DiaryPatches` By Domain

Priority: Low

Status: Completed 2026-06-25. `DiaryPatches.cs` was replaced by domain-split patch files plus the
defensive `DiaryPatchRegistrar` manual registration choke point.

Evidence:
- `Source/Patches/DiaryPatches.cs:25` death patch.
- `:49` arrival/faction patch.
- `:87` hediff patch.
- `:189` PlayLog interaction patch.
- `:496` generated quest-UI closure patch.
- `:664` thought-gain patch.

Problem:
- One Harmony patch file mixes unrelated capture domains and fragile optional registration.

Suggested change:
- Split by domain: deaths, arrivals, social log, quests, health, thoughts.
- Centralize fragile/generated-name registrations in a defensive `DiaryPatchRegistrar`, especially `QuestUiAcceptPatch.TryRegister` and `ThoughtGainPatch.TryRegister`.

Pros:
- Easier patch audit and troubleshooting.

Cons / risks:
- Keep Harmony attributes discoverable.
- Keep optional/DLC-ish patches defensive.

Validation:
- Build.
- Startup patch-registration smoke reasoning/log check if possible.

Docs:
- Update docs/changelog if implemented.

## Run Card 17: Split `PromptEnchantments` Collector / Planner

Priority: Medium

Evidence:
- `Source/Generation/PromptEnchantments.cs:119` hardcoded thresholds/cue cap.
- `:131` live `Pawn` and settings reads.
- `:138` `DefDatabase<DiaryPromptEnchantmentDef>`.
- `:146` live hediff reads.
- `:244` capacity reads.
- `:283` weighted random.
- `:299` prompt assembly.

Problem:
- Candidate collection, weighted selection, XML policy interpretation, and prompt-text assembly are fused around live `Pawn`/`Hediff`.

Suggested change:
- Extract an impure `PromptEnchantmentCollector` that produces plain candidate DTOs.
- Extract a `PromptEnchantmentPlanner` that selects/formats from DTOs with deterministic-roll test seams.
- Move thresholds/cue cap to XML/Def/tuning with fallbacks.

Pros:
- Keeps live pawn reads in an impure collector.
- Makes selection testable.

Cons / risks:
- Candidate DTO must carry enough data to avoid later live `Hediff` access.
- Planner must not accept `Pawn`, `Hediff`, `Def`, settings, or transport objects.
- Current DLC reads route through `DlcContext`; keep it that way.

Validation:
- Build.
- Pure planner tests if feasible.

Docs:
- Update docs/changelog if implemented.

## Run Card 18: Deferred `DiaryEvent` `PovSlot` Refactor

Priority: Low, high blast radius, do last

Status: Resolved 2026-06-25. Implemented as a `public struct PovSlot` value type holding all
per-POV fields, three `initiatorSlot`/`recipientSlot`/`neutralSlot` storage fields, and a single
`private ref PovSlot SlotFor(role)` dispatcher that collapses the ~20 three-way accessor ladders to
one-liners. The three `ApplyLlmResultToInitiator/Recipient/Neutral` methods collapsed into one
`ApplyLlmResultToSlot(result, ref slot)`, and `PostLoadInit` normalization into one
`NormalizeLoadedSlot`. The historical public field names survive as facade properties so the ~60
external references compile unchanged; the flat Scribe keys are preserved so the save shape is
identical (old saves across the refactor boundary are no longer supported, per the explicit
relaxation). Neutral special-cases and all `DiaryStateVersion.Bump()` side-effects preserved. Debug
build clean (0 warnings/0 errors); pure tests green.

Evidence:
- `Source/Models/DiaryEvent.cs:1690-1942` has repeated initiator/recipient/neutral accessors.
- `Source/Models/DiaryEvent.cs:135-198` writes per-POV fields with explicit Scribe keys.
- `Source/Models/DiaryEvent.cs:200-431` `PostLoadInit` references many fields by name in bespoke normalization branches.

Problem:
- Per-POV field/accessor duplication is large and error-prone.

Suggested change:
- Introduce a `PovSlot` value type and have `SlotFor(role)` centralize field selection.
- Keep Scribe keys exactly the same by writing the same flat key names.

Pros:
- Collapses hundreds of repeated branches.
- Makes POV symmetry easier to maintain.

Cons / risks:
- Save compatibility via explicit Scribe keys is safe in principle, but the real blast radius is rewriting `PostLoadInit` normalization.
- Needs old-save load tests or very careful manual old-key reasoning.

Validation:
- Build.
- Add tests around load-normalization if possible.
- Manually verify every old Scribe key is preserved.

Docs:
- Update docs/changelog if implemented.

## Quick Comment Fix

Priority: Trivial

Evidence:
- `DiaryEventCatalog.cs:63` reportedly says something like "New sources added in future slices register here." The rebuttal found all sources are now migrated, so this phrasing is stale.

Suggested change:
- Update that comment only. This can be paired with any slice or done separately.

Validation:
- Build not strictly needed for a comment-only change, but repo rules say build after changes.

## Rejected Findings: Do Not Repeat These

### Rejected: "Capture migration is half-finished / 12 dead specs"

Status: Wrong.

Reason:
- The rebuttal verified all 17 registered event specs are live in production by reading the `Record*` partials.
- The earlier claim came from trusting an incomplete caller index for the generic method name `Get`.
- The `recent*Events` dictionaries are documented per-source dedup gates, not a competing legacy decision system.

Action:
- Do not delete specs.
- Do not start a "finish the migration" project for this claim.
- Only fix the stale comment in `DiaryEventCatalog.cs:63` if still present.

### Rejected: Cache `GetMoodOffsetFromConditionThoughts` For Hot Path

Status: Misread.

Reason:
- The caller already hoists the value out of the per-pawn loop (`MoodEvents.cs:60-62`) and passes it as a parameter.
- Caching would save at most an occasional scan per condition occurrence and is not worth the added state.

Action:
- Do not implement this cache unless profiling later shows a real issue.

## Suggested Prompt For Future Agents

Use this structure when assigning a slice:

```text
Follow AGENTS.md and ARCHITECTURE_IMPROVEMENT_PLAN.md. Implement only Run Card N: <title>. Keep the change minimal and behavior-preserving. Preserve no-DLC safety, save compatibility, localization rules, and the pure/impure boundary. Update DOCUMENTATION.md and CHANGELOG.md if behavior or structure changes. Rebuild with MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug. Do not touch unrelated run cards.
```
