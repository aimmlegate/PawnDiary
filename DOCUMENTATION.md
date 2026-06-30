# Pawn Diary - Maintainer Guide

Last updated: 2026-06-30

Related files:

- `AGENTS.md`: detailed rules for code agents and deep architecture constraints.
- `EVENT_PROMPT_MAP.md`: event-to-prompt coverage map.
- `ARCHIVE_COMPACTION_DESIGN.md`: reviewed-before-code design for real cold archive compaction.
- `CHANGELOG.md`: milestone history.

## 1. Purpose

Pawn Diary records meaningful RimWorld colony moments and turns them into short diary pages through
configured OpenAI-compatible API lanes. RimWorld loads the compiled DLL at startup through Harmony
patches, Defs, a `GameComponent`, and an inspector tab. There is no `main()`.

Diary pages belong only to free humanlike colonists old enough for first-person writing
(`DiaryTuningDef.minimumFirstPersonAgeYears`, default 13). Animals, prisoners, slaves, enemies,
visitors, non-colonists, and underage colonists do not own pages. If only one participant is eligible,
the event becomes a solo entry. If two eligible colonists are involved, the initiator entry is
generated first and the recipient entry gets hidden continuity from it.

Arrivals and deaths are neutral boundary pages. Arrival pages introduce a pawn's diary; death pages
end it and hide later same-tick events for that pawn.

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, `Languages/`, and the compiled DLL in
`1.6/Assemblies/PawnDiary.dll`. Source and tests are kept in the repo for development.

| Path | Role |
|---|---|
| `About/` | Mod metadata, preview, icon, dependency declaration. |
| `1.6/Defs/` | XML-owned policy: event groups, tuning, prompts, styles, UI, text effects. |
| `Languages/` | Keyed and DefInjected English text plus optional translation sources. |
| `Source/Capture/` | Pure Event Catalog payloads and decisions. |
| `Source/Ingestion/` | `DiaryEvents.Submit` bus + one `DiarySignal` capture/emit class per source (impure edge). |
| `Source/Core/` | `DiaryGameComponent` partials: dispatch pipeline, save/load, scans, generation queue. |
| `Source/Generation/` | Runtime context builders, prompt adapters, LLM client, DLC-safe live reads. |
| `Source/Pipeline/` | Pure prompt planning, archive eligibility, request JSON, response cleanup, text decoration, API policy. |
| `Source/Patches/` | Harmony startup, domain hooks, inspect-tab/command patches. |
| `Source/Settings/` | Saved settings, API lane UI/controller, Prompt Studio, Writing Style Studio. |
| `Source/UI/` | Diary inspect tab, card rendering, paging, formatting. |
| `tests/` | Standalone pure-helper test projects. |
| `prompt-lab/` | Prompt fixture and variant validation harness. |
| `scripts/publish.ps1` | Local Workshop payload prep. |

## 3. Runtime Flow

1. A Harmony hook or scanner notices a candidate event.
2. The adapter snapshots live RimWorld facts, settings/XML gates, dedup state, and any random roll.
3. The pure Event Catalog decides whether to drop, create a solo/pair/neutral entry, batch, or route
   to day reflection.
4. `AddSoloEvent` or `AddPairwiseEvent` creates a saved `DiaryEvent` and indexes it on eligible
   pawn records. The active-event retention cap then archives old displayable POVs into compact
   `ArchivedDiaryEntry` rows before dropping their hot refs.
5. Generation queues immediately when possible; periodic scans retry pending or orphaned work.
6. `DiaryPipelineAdapters` copies runtime/XML/localized state into pure pipeline DTOs.
7. Pure helpers assemble prompts, serialize request JSON, parse provider responses, and clean text.
8. `LlmClient` sends requests and returns results to the main thread; successful main pages may queue
   a title follow-up.

`GameComponentTick` runs game-time scans continuously, but the expensive active-event generation
rescan is demand-driven: load catch-up, delayed raid entries, and recovered orphaned work request a
pass, then the catch-up pass runs at most every 200 ticks until it is no longer needed. Those
generation/title/orphan scans use the XML-tuned hot window (`activeScanEventWindow`, default 1000,
a global count across all pawns); older hot events can be compacted into archive rows that stay
visible but are not retried or title-backfilled.
Orphaned "writing..." recovery is a separate, slower hot-window pass every 600 ticks. Completed LLM
results and debug logs are drained from both `GameComponentTick` and `GameComponentUpdate`, so
requests that were already queued can finish and apply while the game is paused. Pending generation
is not saved; it resets on load and is requeued when the event is still inside the hot window.
First-person generation is skipped for pawns below the XML Consciousness floor, while neutral
arrival/death pages bypass that guard.

### 3.1 Ingestion bus (`DiaryEvents.Submit`)

Every captured event enters through one front door: `DiaryEvents.Submit(signal)` (`Source/Ingestion/`).
A Harmony hook (or scanner) builds the matching `DiarySignal` subclass and submits it; the shared
dispatcher `DiaryGameComponent.Dispatch` then runs the universal steps that each `RecordXxx` method
used to copy-paste:

1. **guard** — `CanRecordGameplayEventNow` (game must be in play);
2. **dedup-check** — one consolidated transient `recentEvents` store, keyed by the signal's raw
   source-prefixed key (e.g. `"thought|pawnId|defName"`). The check runs *before* `Decide` so a
   deduped event skips the pure decision, and so a source that draws impure state before emitting
   (notably `AbilitySignal`'s `Rand.Value` roll) does not consume that state on a dropped duplicate;
3. **decide** — `DiaryEventCatalog.Get(payload.EventType).Decide(payload, ctx)` (the pure, XML-backed
   filter);
4. **dedup-mark** — the key is marked only after `Decide` passes, so an event the catalog drops
   (e.g. an ability that fails its cooldown-weighted chance roll) does not consume the window;
5. **emit** — `signal.Emit(sink, decision)` builds the localized text + game-context, creates the
   `DiaryEvent` via the factory, and queues generation (or routes to the ambient/batch sinks).

A `DiarySignal` is the impure capture+emit half of one source; the pure decision and game-context
format stay in `Source/Capture/Events/*EventData.cs` (unit-tested without RimWorld). Colony-wide
sources extend `DiaryFanoutSignal`: the dispatcher peeks the colony dedup key once, runs each
per-pawn child through the same decide→dedup→emit path, and marks the colony key only after at least
one entry emits. Signals reach the component through a narrow internal emit surface
(`AddSoloEvent`/`AddPairwiseEvent`, `QueueSolo`/`QueuePair`/`DelaySolo`, `RecordAmbientThought`,
`BuildCaptureContext`, `IsDiaryEligible`) so generation internals stay private.

Adding a reaction to a new event is now: write one Harmony hook + one `DiarySignal` subclass (capture
into a payload, build the context, emit), register the source's `Spec` in the catalog, and add its
XML policy. The filter/dedup/route glue is inherited from `Dispatch`, not re-implemented.

**Migration status: complete.** Every catalog source (`DiaryEventType`) now routes through the bus —
the Harmony-hook sources (Thought, Inspiration, Ability, Romance, MentalState, Tale, Death,
Interaction, Raid, MoodEvent, Ritual/PsychicRitual, Hediff, Quest, Arrival) and the tick-scanner /
flush sources (Work, ThoughtProgression, DayReflection). There are no remaining `RecordXxx` capture
methods. Two patterns reach the bus:

- **One-shot captures** (Harmony hooks) build a `DiarySignal` and call `DiaryEvents.Submit`.
- **Scanner / flush sources** are still driven by their periodic component scans (work sampling,
  situational-thought progression, hediff severity, quest accept-state, day reflection), but each scan
  now builds a signal per pawn and submits it instead of recording inline. A scan whose own episode
  state depends on whether the event recorded (ThoughtProgression's recorded-stage set, DayReflection's
  written-day guard) calls `Dispatch` directly and reads its `bool` result.

The per-source dedup dictionaries are gone, replaced by the single consolidated `recentEvents` store
(legacy scan state like `knownAcceptedQuestIds` and the per-episode staging sets remain, since they
are not event dedup). Each entry records the source's OWN dedup window alongside the tick, and the
prune sweep evicts a key only once THAT window has elapsed (pure policy in
`Source/Capture/RecentEventExpiry.cs`, unit-tested); a short-window source firing the sweep can
therefore never evict a still-live long-window key, and a zero/negative window means "opt out of
dedup" rather than "wipe the store". The coverage table below lists each source's signal.

## 4. Event Sources

The catalog of every event the diary reacts to (`DiaryEventType`), with the `DiarySignal` that carries
it onto the bus.

| Event type | Observed by | Ingestion | Shape |
|---|---|---|---|
| Thought | `MemoryThoughtHandler.TryGainMemory` | `ThoughtSignal` | solo (+ ambient) |
| Inspiration | `InspirationHandler.TryStartInspiration` | `InspirationSignal` | solo |
| Ability | `Ability.Activate` overloads | `AbilitySignal` | solo (sampled) |
| Romance | `Pawn_RelationsTracker.AddDirectRelation` | `RomanceSignal` | pair |
| Raid | `IncidentWorker.TryExecute` | `RaidFanoutSignal` | fan-out |
| MoodEvent | `GameConditionManager.RegisterCondition` | `MoodEventFanoutSignal` | fan-out |
| MentalState | `MentalStateHandler.TryStartMentalState` | `MentalStateSignal` | pair + solo |
| Tale | `TaleRecorder.RecordTale` | `TaleSignal` | solo / batch / death |
| Hediff | `Pawn_HealthTracker.AddHediff` + scan | `HediffSignal` | solo / day-reflection |
| Interaction | `PlayLog.Add` | `InteractionSignal` | pair / solo / batch / ambient |
| Work | Periodic job sampling | `WorkSignal` (via work scan) | solo |
| ThoughtProgression | Periodic scan | `ThoughtProgressionSignal` (via scan) | solo |
| DayReflection | Sleep/rest flush | `DayReflectionSignal` (aggregation flush) | solo |
| Quest | `Quest.Accept`/`End` + state scan | `QuestFanoutSignal` | fan-out |
| Ritual | Ideology/psychic ritual completion | `RitualFanoutSignal` / `PsychicRitualFanoutSignal` | fan-out |
| Death | `Pawn.Kill` + death TaleDefs | `DeathFallbackSignal` (+ Tale death routes) | neutral description |
| Arrival | Starting scan + `Pawn.SetFaction` | `ArrivalSignal` | neutral description |

| Source | How it is observed | Result |
|---|---|---|
| Social interactions | `PlayLog.Add` | Pair, solo, batched, or ambient note by XML group. |
| Mental states | `MentalStateHandler.TryStartMentalState` | Social fighting can be pairwise; other breaks are solo. |
| Romance | `Pawn_RelationsTracker.AddDirectRelation` | Pairwise lover/spouse/ex relation moments. |
| Tales and combat | `TaleRecorder.RecordTale` | Solo, pair, delayed combat batches, or death description. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first page. |
| Deaths | `Pawn.Kill` plus XML death TaleDefs | Neutral final page. |
| Mood events | `GameConditionManager.RegisterCondition` | One entry per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | XML-filtered memory entries; ambient thoughts can batch. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, chemical, and similar worsening stages. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo inspiration entry. |
| Hediffs | `Pawn_HealthTracker.AddHediff` and scan | Immediate or day-reflection health entries by XML policy, including string-matched Anomaly mental afflictions. |
| Work | Periodic current-job sampling | Non-social, non-violent work, controlled by XML odds/cooldowns. |
| Raids and infestations | `IncidentWorker.TryExecute` | Fan-out to eligible colonists; ordinary raids can delay generation. |
| Quests | `Quest.Accept`, `Quest.End`, defensive UI/state scan | Accepted, completed, and failed quest entries; prompt labels reject placeholder names and humanize code-like quest defNames. |
| Event windows | `IncidentWorker.TryExecute`, `Quest` lifecycle, `Thing.SpawnSetup`, `SignalAction_Letter`, `CompProximityLetter`, `Building_VoidMonolith.Activate`, `Pawn_AgeTracker.BirthdayBiological`, `Pawn_HealthTracker.AddHediff`, `PrisonBreakUtility.StartPrisonBreak` | XML starts/ends narrative windows or one-shot events, writes phase entries, and can bias prompts while active. |
| Observed conditions | Periodic live-state scan (map danger, active game conditions, evidence things, pawn hediffs) | Lasting states read from live state, not a guessed duration: bias prompts while present, optionally record start/end pages, and end after a debounce when live state stops showing them (Plan 12; see §5.1). |
| Rituals | Ideology and psychic ritual completion hooks | Fan-out by role/perspective when DLC content is active. |
| Abilities | `Ability.Activate` overloads | Cooldown-weighted caster entry. |
| Day reflections | Sleep/rest trigger | One reflective page per pawn/day when important signals exist. |

Hooks are grouped by domain under `Source/Patches/`. Fragile reflection targets register through
`DiaryPatchRegistrar` so missing methods warn and no-op instead of breaking startup. Capture hooks,
per-tick work, save/load bookkeeping, startup registration, and vanilla UI overlays isolate failures
with one-time logging and preserve vanilla behavior.

## 5. XML Policy

Most feature tuning lives in XML so changes do not require recompiling:

- `DiaryInteractionGroupDefs.xml`: event classification, instructions, tones, color cues, batching,
  hediff modes, package-aware compatibility routing, and default enablement.
- `DiaryEventWindowDefs.xml`: generic start/end/timeout windows or one-shot events over incident,
  quest, spawned thing, letter, proximity-letter, and special story-object signals, including phase
  diary text and active prompt weighting.
- `DiaryObservedConditionDefs.xml`: lasting colony states re-derived from live game state each poll
  (map danger, active game conditions, observable evidence things, pawn hediffs) with start/end
  debounce, optional diary pages, and active prompt weighting (Plan 12, see §5.1).
- `DiaryEventPromptDefs.xml`: event prompt text, event enhancement text, and optional forced model.
- `DiaryPromptTemplateDefs.xml`: which structured fields each prompt shape renders.
- `DiaryPromptDef.xml`: shared system/final instructions.
- `DiaryPersonaDefs.xml`: built-in writing styles.
- `DiaryHediffPersonaOverrideDefs.xml`: active hediffs that temporarily force a writing style.
- `DiaryPromptEnchantmentDefs.xml` and `DiaryHumorCueDefs.xml`: optional live-context and subtle
  humor cues.
- `DiarySignalPolicyDefs.xml` and `DiaryTuningDef.xml`: scan intervals, odds, cooldowns, thresholds,
  day-reflection policy, and shared fallback tuning.
- `DiaryUiStyleDef.xml` and `DiaryTextDecorationDefs.xml`: Diary UI dimensions/colors and display
  rich-text decoration.

Interaction groups match by domain, exact `defName`, and a precision-ordered set of token
matchers, plus an optional source package ID. From most to least precise, the token matchers are:
`matchPrefixes` (defName starts with the token), `matchSuffixes` (defName ends with the token),
`matchSegments` (a whole CamelCase/underscore/digit word of the defName equals the token — `"Food"`
matches `NeedFood`/`AteRawFood` but not `Foodstuff`), and the legacy blunt `matchTokens` (plain
case-insensitive substring). Prefer the precise matchers and exact `defName` lists for new groups;
`matchTokens` stays only for compatibility because a substring like `"Good"` also claims the vanilla
grief thought `PawnWithGoodOpinionDied`. The pure matcher logic lives in
`Source/Capture/GroupNameMatcher.cs` (no RimWorld types) so it is unit-tested directly. Across
groups, lower `order` wins ("first match wins"), so a specific group can intercept a defName before
a broader group's matcher sees it — the `thoughtPositive` group (order 500) claims the positive
opinion-flipped `PawnWithBadOpinionDied` before `thoughtNegative`'s `Died` suffix (order 510). Event prompt policy resolves from most specific to broadest key: source defName, interaction
group, classifier key, then domain. Prompt text, enhancement text, and forced-model text resolve
independently so narrow XML can override one field and inherit the others.
Mood-impact fallback lists in `DiaryTuningDef.xml` classify GameCondition defNames that are always
positive or negative when no live thought offset is measurable; Anomaly `GrayPall` and `DeathPall`
are negative string matches there. `UnnaturalDarkness` is routed through the mixed MoodEvent group
instead because its Anomaly ThoughtDef can be negative or positive depending on the pawn.

For optional DLC or mod content, prefer string matchers in XML. Do not hard-reference DLC defs in C#
or XML unless they are guarded; absent DLC content should simply never match.
The Anomaly Hediff group follows this pattern: `RevenantHypnosis`, `CubeInterest`,
`CubeWithdrawal`, `CubeRage`, `CorpseTorment`, and `Inhumanized` are exact string matches that create
immediate Hediff diary entries on add and on configured severity-step progression when that DLC
content is present. The same conditions also have prompt-enchantment cues, so any ordinary
first-person prompt they win adds an `important context:` line such as `high priority; moderate cube
withdrawal; compulsive absence, restless need; condition detail: ...`. `descriptionOverrideKey` can
replace the standard game description per Def; otherwise the localized `HediffDef.description` is
cleaned and capped before it reaches the model. If a hediff also wins a temporary writing-style
override, its matching prompt-enchantment candidate is suppressed so the localized condition text
does not repeat as both style and context; unrelated hediff/status candidates remain eligible.
`DiaryHediffPersonaOverrideDefs.xml` can also temporarily force the prompt POV pawn's writing style
from active hediffs; `Inhumanized` currently
uses this to force the `DiaryPersona_InhumanizedVoid` dark void style while that hediff is active.
Base-game `Alzheimers`, base-game `Dementia`, and Anomaly `CrumblingMind` stay regular
memory-decay prompt enchantments rather than writing-style overrides, while Anomaly `CrumbledMind`
uses the stronger `DiaryPersona_CrumbledMindCollapse` mind-crumbled style. Base-game `Joywire` forces the
`DiaryPersona_JoywireHaze` bright-fog style, and Anomaly `BlissLobotomy` uses the stronger
`DiaryPersona_BlissLobotomyHaze` blank-bliss style. Royalty `Mindscrew` forces the
`DiaryPersona_MindscrewPain` pain-needle style when that optional DLC hediff is present. Base-game
`TraumaSavant` has the high-priority `DiaryPersona_TraumaSavantSilent` style, which forbids dialogue
or `[[speech]]` blocks because the pawn cannot speak. Base-game drug highs use the same
prompt-enchantment override hook for `AlcoholHigh`, `Hangover`,
`AmbrosiaHigh`, `GoJuiceHigh`, `LuciferiumHigh`, `FlakeHigh`, `PsychiteTeaHigh`, `YayoHigh`, and
`SmokeleafHigh`, with XML cue keys grounded in the vanilla Hediff and Thought defs.

Event windows are the generic system for ongoing threats, one-shot warnings, or story beats that
are not hediffs.
`DiaryEventWindowDef` rows define `startSignals`, `endSignals`, `timeoutTicks`, phase diary text,
and active prompt policy. Current signal sources are `Incident/executed`, `Quest/accepted`,
`Quest/completed`, `Quest/failed`, `ThingSpawned/spawned`, `Letter/received`,
`ProximityLetter/received`, `VoidMonolith/activated`, `PawnAge/birthday`, `Hediff/added`, and
`PrisonBreak/started`; matchers are exact defName strings or broad tokens. `keepActive=false` makes
the start signal a one-shot diary event without saving an active window. `recordScope=SubjectPawn`
records only the pawn carried by the signal, used by vanilla letter triggers such as ancient danger,
proximity-letter triggers such as void monolith discovery, completed monolith activations, birthdays,
and target health events. An active window is saved, can write
start/end/timeout diary entries, and can add a weighted prompt candidate while multiplying ordinary
prompt enchantments down. Setting
`normalPromptWeightMultiplier` to `0` makes the window fully override ordinary health/status prompt
enchantments until it ends or times out. The former `MetalhorrorSuspicion` window was retired in
favor of the Plan 12 observed condition `AnomalyGrayFleshEvidence` (see §5.1): a fixed `timeoutTicks`
could never prove the suspicion was still unresolved, and the gray-flesh `ThingSpawned` start signal
left it effectively always active, so it is now driven by whether gray-flesh evidence is physically
present on the map. The built-in `AncientDanger` rule
matches vanilla's `AncientShrineWarning` letter key and records a single entry for the approaching
pawn only. The built-in `VoidMonolithDiscovery` rule matches the Anomaly `VoidMonolith` proximity
letter and records a single extreme-dark discovery entry for the nearby pawn only. The built-in
`VoidMonolithActivation` rule matches completed void monolith activations, uses the reached
`MonolithLevelDef` as its signal defName for future XML splits, and records one extreme-dark entry
for the activating pawn. `Birthday` records a target-only uneasy aging entry from the direct
`BirthdayBiological` hook instead of inferring the moment from age-related hediffs. `HeartAttack`
records a target-only danger entry from `Hediff/added` with defName `HeartAttack`; vanilla pawn heart
attacks do not emit their own letter or message, while Anomaly's `MessageHeartAttack` belongs to the
fleshmass heart and is unrelated. `PrisonBreak` records one danger entry for every eligible colonist
on the affected map from the shared prison-break utility overload used by both natural and sparked
breakouts.

On hot signal paths (every spawned thing, every added hediff) the recorder first runs a cheap,
allocation-free pre-filter — `EventWindowPolicy.CouldMatchByDefName` against each def's cached
trigger rules — and only resolves the signal label when some window could actually match. Optional
event-window recording is also isolated from the established raid/hediff/quest capture, so a window
failure can never suppress the diary entry those hooks already write.

### 5.1 Observed conditions (lasting game state, Plan 12)

Event windows react to one-shot *signals* and then guess a duration with `timeoutTicks`. That is
wrong for *lasting* states (an active raid, toxic fallout, gray-flesh evidence): a fixed timeout
cannot prove the state is still real, a missed end signal leaves stale context, and a save loaded
mid-state drifts from reality. Observed conditions fix this by re-deriving truth from **live game
state** on every poll. `DiaryObservedConditionDef` rows declare one `observerType`, what to match,
and how to debounce; the rest is generic.

The flow keeps the usual barrier — live reads at the edge, pure decisions in the middle:
`DiaryGameComponent.ObservedConditions.cs` scans the due defs (each def's own `pollIntervalTicks`,
checked on a short global gate), snapshots what it currently sees into plain
`ObservedConditionObservation` DTOs, and hands them to the pure
`ObservedConditionPolicy.Plan(...)` (under `Source/Capture/ObservedConditions/`, no Verse, unit-tested
in `tests/DiaryObservedConditionTests`). The policy diffs observations against the saved active state
and returns decisions: `StartPending` → `StartRecorded` (after `startDebounceTicks`) → `Refresh` while
seen → `EndPending` → `EndRecorded` (after `endDebounceTicks`), or `DropStale` when the Def is gone or
a never-started condition disappears. The impure adapter then persists/forgets the saved rows
(`ActiveObservedConditionState`, Scribe key `activeObservedConditions`) and optionally records start/end
diary pages. Ticks gate debounce only; whether a condition is active is always answered by "is it in
this scan's observations?", so a save loaded mid-state is rediscovered and a missed signal is recovered
by the next scan. A def not polled this pass is excluded from the diff entirely, so it never looks
falsely "missing". Page recording is transactional: the adapter tries to write the start/end page
*before* committing the saved-state change, and if no eligible recipient was available it leaves
`startRecorded`/`endRecorded` false and retains the row (rather than dropping it), so the next scan
re-enters the same transition instead of permanently losing the page; the per-phase dedup key is
consumed only after at least one page is actually written, mirroring the event-window recorder.

Observer types, all DLC-safe (plain-string / vanilla-API matchers that find nothing when content is
absent): **MapDanger** (active while a home map's `dangerWatcher.DangerRating` ≥ `minDangerRating`, or
spawned hostiles ≥ `minHostileCount`), **GameCondition** (active while `gameConditionManager`
holds a matching condition defName — the game owns start/end truth), **ThingPresent** (active while
matching spawned things/filth remain, found via the indexed `ListerThings.ThingsOfDef`, never a
full-map scan; describes observable evidence only), and **PawnHediff** (pawn-scoped, active while a
matching **visible** hediff is present — hidden hediffs are skipped so nothing secret is revealed).
`RecentEvidence` is reserved for a future bounded letter/signal fallback and is currently a no-op.

Shipped defs: `MapThreatActive` (enabled, prompt-tone only), `ToxicFalloutActive` /
`SolarFlareActive` (enabled GameConditions, prompt-tone only), `AnomalyGrayFleshEvidence` (enabled,
records one observable "found gray flesh" page and strongly biases prompts toward unease while the
evidence is physically present — the Anomaly-gated replacement for the retired `MetalhorrorSuspicion`
window), and `MetalhorrorEmergence` (PawnHediff, shipped **disabled** with empty matchers until the
observable post-emergence state is verified — see ARCHITECTURE_IMPROVEMENT_PLAN.md Plan 12 "Open
questions"; it must never surface hidden mechanics).

`DiaryObservedConditionDef` validates its configuration at load via `ConfigErrors`: `recordScope=
SubjectPawn` is rejected unless `scope=Pawn`, because every non-Pawn scope clears the subject id when
building an observation and so could never resolve a page recipient (the mismatch would otherwise fail
silently with no diary page ever recorded).

## 6. Prompts And Writing Styles

Prompts are compact `key: value` lines. Empty values and `none`/`n/a`/`unknown` sentinels are dropped.
Prompt templates cover pair, solo, batched, day-reflection, neutral death, neutral arrival, and title
requests.
Quest prompts keep the raw `quest=` defName only in saved context for UI/domain classification; the
model-facing fields use `quest_label`, `quest_signal`, `quest_faction`, and `quest_rewards`. The
label path rejects placeholder `QuestName`, humanizes PascalCase/underscore fallbacks, removes the
standalone word `Quest`, and the Quest event enhancement tells the model not to copy the quest name
verbatim into the diary line.

System prompts stay short and general. Event-specific guidance comes from `DiaryEventPromptDef` and
per-group instructions/tones. Groups can define instruction/tone variant pools; instructions roll once
at capture and are saved, while tones are deterministic by event id.

Prompt Studio edits shared system prompts and event prompt/enhancement/forced-model overrides.
Writing-style presets are saved settings backed by `DiaryPersonaDef`; the code still uses "persona"
in some field names for save compatibility, but the player-facing feature is writing style.
Active hediffs can temporarily override the saved style through `DiaryHediffPersonaOverrideDef`
rules. These overrides are prompt-time only: they do not change the saved style picker value, and a
missing/off-map pawn falls back to its saved style. Memory-decay hediffs such as `Alzheimers`,
`Dementia`, and `CrumblingMind` deliberately remain prompt enchantments instead of automatic style
overrides.

Prompt enchantments add one weighted live-context pressure cue to eligible first-person prompts.
When a hediff-driven writing-style override is active, the hediff sources that selected that override
are removed from the normal prompt-enchantment pool to avoid duplicating the same condition in the
system style block and the `important context:` line.
Imported game/mod prompt text such as live hediff descriptions, hediff/capacity labels, DLC title or
role labels, scenario descriptions, and quest descriptions is flattened to one prompt line and capped
to its first two sentences before being sent to the model. Pawn Diary's own XML/Keyed prompt
instructions, writing styles, humor cues, and field labels are not sentence-capped by this guard.
Active event windows and active observed conditions (§5.1) both feed the same prompt-enchantment
planner as extra XML-weighted candidates, so an unresolved colony threat can shape unrelated diary
pages until it closes — by the event window's end signal/timeout, or by the observed condition's live
state ending after its end debounce. Each source can also dampen ordinary health/mood context through
its own `normalPromptWeightMultiplier`; the two multipliers compose. Dev prompt-suite fixtures opt out of live prompt enchantments, including active
event-window candidates, so captured test prompts stay isolated from earlier manual event-window
tests. Humor cues are hidden, XML-weighted, and folded into the writing-style block.
Direct speech is allowed only in selected first-person interaction prompts with a closed
`[[speech]]...[[/speech]]` block.

Generated Social-log speech injection remains disabled/hidden. The saved setting exists for
compatibility, but the call site is off. Title generation is enabled by default. Successful main
entries queue their own title follow-up immediately; the full missing-title sweep runs only after
load or when settings are saved, not on every generation rescan. Title responses are validated
before they are saved: markup/control/schema characters such as leaked tag tokens are rejected, and
the stored title falls back to the first few words of the finished diary entry with `...` appended.

## 7. Settings And UI

Main settings cover API lanes, routing mode, request tuning, title generation, atmospheric
formatting, prompt enchantments, work/social weights, the active diary-event cap, event filters,
prompt overrides, and writing style presets. Dev mode exposes prompt-test mode, a full diary export
button, and extra diagnostics. The export writes every saved pawn diary page plus the backing event
records to `PawnDiaryExports/` under RimWorld's save-data folder, and copies the generated file path
to the clipboard.

The Diary UI is an inspect tab registered for humanlike pawns and their corpse defs. By default it
appears in the pawn inspect-tab row for eligible colonists and selected colonist corpses. A setting can
instead hide the tab and add a bottom command button that opens the same UI. Programmatic opens from
that command, Social-log links, and linked-entry cards temporarily expose the hidden tab long enough
for RimWorld's inspect-pane opener to resolve it, then clear that state when the tab closes. The
inspect-tab draw path and programmatic open helper also re-apply tab registration once after startup,
covering load orders where RimWorld finalizes resolved tab lists after static constructors. The
command helper is marked with RimWorld's `StaticConstructorOnStartup` because it owns the static Unity
texture cache for the button icon; the icon itself still loads lazily from the main-thread gizmo path
and falls back to the vanilla book icon if the mod texture is missing.

Production UI shows completed pages. Dev mode also shows pending/failure rows, raw prompt/status
data, formatting previews, prompt-suite tools, copy buttons, regeneration controls, and a completed
mock-page filler for stress testing long histories. That filler seeds 6,000 saved pages over 3
in-game years (about 2,000 pages per year) without calling the LLM, and dev-mode retention skips
mock stress histories so autosaves do not immediately shrink the fixture. Histories page by in-game
year; newest cards start expanded. Long histories are kept cheap by the active-event cap,
visible-entry caching, sliced main-thread year indexing, cached virtual row offsets/heights, and
viewport drawing that only emits cards inside the scroll slice plus the XML-tuned overscan buffer
(`virtualizedEntryOverscanHeight`, default 800 pixels above and below the viewport). The sliced indexer
uses `uiHistoryScanMaxEventsPerFrame` and `uiHistoryScanFrameBudgetSeconds` for year indexing,
selected-year card materialization, and selected-year row layout, so opening a pawn with thousands of
pages shows a loading panel instead of freezing the game. The loading panel reports the active load
phase only when no usable cached list exists yet: first open, uncached pawn switch, or opening a year
with no cached cards. The tab keeps a small LRU of loaded pawn views so returning to a recent pawn
restores its visible list instead of rebuilding from zero. Same-pawn index refreshes and selected-year
card refreshes build quietly behind the currently visible list. Once a year is visible, same-year data
and layout refreshes, including completed generation/title updates, scroll, highlight refreshes, and
collapse/expand, rebuild row offsets in place instead of returning to the loading panel; loaded large
years seed the clicked card's current blend so the clicked card can still animate open or closed.
Expanded-card height measurement is isolated in `DiaryEntryCardMeasurer`, which owns the wrapped-text
height cache and invalidates it on card width, debug display, render token, and pawn-name highlight
revision. Expanded-card drawing is routed through a renderer request in
`ITab_Pawn_Diary.EntryCards.cs`, leaving selection, scroll, sliced layout, and expansion state in the
inspect tab while keeping the Verse/Unity IMGUI measurement and draw paths together.
Selected-year rebuilds invalidate row layout defensively so virtualized row offset
arrays cannot be reused against a changed list. An in-progress sliced build (year index, selected-year
cards, or row layout) is invalidated only by a STRUCTURAL change — a different pawn, a tab filter
toggle, or a different event count — never by a `DiaryStateVersion` tick. That counter is process-wide,
so it advances whenever any pawn's entry status/text/title changes anywhere in the colony; tying the
build identity to it made active generation reset an in-progress scan to event zero on every tick, so
a large history could never finish loading. Letting each scan complete once started keeps switching
responsive under generation; the completed index quietly refreshes behind the visible list to pick up
the new state within a few frames. Per-event work in those sliced scans is kept cheap by
`DiaryContextFields`: each indexed event and each materialized card calls it several times (arrival
and death bounds checks, status reads, and source-domain recovery, which probes up to ~13 markers).
It scans the context string in place and allocates only when a value is returned, so the common
"key absent" path is allocation-free — important because the per-frame time budget would otherwise
run out after only a couple of entries, making a long history take many seconds to load. The tab indexer does not perform the older
cross-colony arrival-page fallback scan while opening; it scans the selected pawn's saved diary
references once, resumes selected-year loading across frames, skips any bad/stale entry with a
one-time log, then slices the selected year's card and layout work. Inspect-tab and command badges
do not touch saved diary records during pawn
selection; they read a transient per-pawn status cache. The new-page badge is backed by a saved
per-pawn unread flag that is set when main LLM text finishes and cleared when that pawn's Diary tab
opens, while writing dots reuse cached pending counts after the Diary tab finishes its sliced load.
Archived pages use the same cards and copy controls as hot pages, but dev-only regeneration is hidden
because compact archive rows intentionally discard prompt/raw-response/retry state.

`DiaryTextFormat` escapes raw model rich text before applying safe formatting. Display-only text
decorations and pawn-name highlights happen at render time; generated text is not mutated on save.

## 8. API And Reliability

Each enabled endpoint/model/mode/auth row is an API lane. Supported request modes are OpenAI-compatible
Chat Completions and OpenAI Responses. Auth can be bearer, no auth, custom API-key header, or `key=`
query parameter. Logs strip secrets and query strings.

Routing modes are Balanced, Prefer top rows, and Failover only. A `DiaryEventPromptDef.forcedModel`
can try a matching active model first; blank, unknown, disabled, or failed forced lanes fall back to
normal routing. Recipient follow-ups and title requests try to pin to the previous successful lane.

`LlmClient` handles concurrency, per-lane cooldowns, transient retries, timeout/permanent failures,
session cancellation on new game/load, and result handoff to the main thread. `LlmResponseParser`
supports Chat and Responses output shapes, strips reasoning/transcript leaks, cleans malformed speech
markers, and trims saved text locally.

## 9. Save Data And Compatibility

`DiaryGameComponent.ExposeData` saves per-pawn records (`diaries`), the hot event list
(`diaryEvents`), compact archive rows (`diaryArchiveEntries`), active XML event windows
(`activeEventWindows`), and active observed conditions (`activeObservedConditions`).
`DiaryEventRepository` owns the hot event list and rebuilds its id lookup index
after load. `DiaryArchiveRepository` owns one display-only `ArchivedDiaryEntry` row per archived pawn
POV, repairs duplicate/null rows after load, and indexes rows by pawn id for the Diary tab. Per-pawn
event lists prune blank, duplicate, or dangling hot refs. Active event windows normalize after load so
renamed/missing defs fail closed instead of blocking future prompt context. Active observed conditions
(`ActiveObservedConditionState`) are additive and normalize after load too: null strings coalesce,
keyless rows drop, duplicate identities collapse, and a row whose Def is gone ages out on the next scan
(`DropStale`) without errors — old saves simply load an empty list.

The diary-history cap is **per pawn** (default 3000, editable from settings, range 1–10000). Each
pawn keeps only its newest configured number of **hot** pages: `ApplyActiveEventLimit` looks at the
oldest refs past the cap, copies completed/stale/failed displayable POVs into `diaryArchiveEntries`,
then removes only those hot refs whose archive row exists (or refs that are invalid/out-of-bounds).
Still-active pending/not-generated refs stay hot instead of being destroyed. Finally, the hot event
store is swept down to the union of remaining hot refs, so a shared pair event stays as a full
`DiaryEvent` until both pawns have either kept or archived their POVs. It runs after load, before save,
after new event creation, and when settings are saved. The common (nothing-over-cap) path costs one
`Count` check per pawn. Because the cap is per pawn, background scan cost no longer scales with total
lifetime history: maintenance walks the global hot window below, not the compact archive. (The
`maxActiveDiaryEvents` field/Scribe key keeps its historical name for save compatibility even though
its meaning is now per-pawn hot refs.)

Separately, `DiaryTuningDef.activeScanEventWindow` (default 1000, XML only, a global count across all
pawns) defines the newest saved hot events considered for retry, title catch-up, orphan recovery,
day-summary event evidence, work cooldowns, and prompt continuity/opener history. Compact archive rows
never enter those scans. If an old attempted page has no generated text, retention can archive it as a
display-only fallback: the UI shows a localized "You see that: ..." body, derives a short display-only
title from the first few words, and uses the footer note to say the page failed to generate instead of
showing a model name. Because the compact archive drops the raw prompt, the shared pure
`DiaryArchiveFallback` resolver derives that fallback fact from the prompt **at archive time** and bakes
it into the archived `text`; the live card re-resolves from that same text afterward, so the body and
title stay identical before and after compaction. Prompt-only dev capture rows stay hot because the
archive deliberately does not store full prompt text. The archive/drop eligibility checks live in
`Source/Pipeline/DiaryArchiveEligibility.cs`, and the per-pawn overflow trim order lives in the pure
`Source/Pipeline/DiaryArchiveCompactionPlanner.cs`, so the title-pending, prompt-only, stale-fallback,
cold-undisplayable, and budget/order rules are covered without loading RimWorld. Retention stages each
archive row first and commits the write only for refs it actually drops; the dev prompt-suite reset
clears matching archive rows via `DiaryArchiveRepository.RemoveForEventIds` so a generated-then-compacted
test entry cannot survive as an orphan.

`DiaryEvent` saves raw/generated text, statuses/errors, context, source ids, prompts, titles, LLM
metadata, semantic color cue, and compact display facts. Per-role state is stored in initiator,
recipient, and neutral slots but serialized under the historical flat Scribe keys.

Adding Pawn Diary to an existing save is safe; it starts recording future events. Removing it is
gameplay-safe because the mod does not persist custom pawn/map components or gameplay defs onto
vanilla objects. The diary UI/history disappears without the mod, and old generated Social-log rows
from dev builds may remain in the vanilla play log.

### 9a. Scribe-key stability contract

The string tags passed to `Scribe_Values.Look` / `Scribe_Collections.Look` (e.g. `"eventId"`,
`"interactionDefName"`, `"initiatorPawnId"`, `"neutralText"`, `"maxActiveDiaryEvents"`,
`"apiEndpoints"`, `"interactionGroupEnabled"`) are a **public, stable save-format API**. Renaming
any of them silently breaks every existing player save: the old key stops being read on load and
the field falls back to its default, so a completed diary page can look freshly-reset, a setting can
revert, or a pair event can lose a POV. Treat every Scribe key the way you would a network or on-disk
schema field.

Authoritative key lists (read these before renaming anything):
- `DiaryEvent.ExposeData` + `DiaryEvent.ScribePawnSlot` / `ScribeNeutralSlot` — the hot-event save
  shape, including the flat `initiator*` / `recipient*` / `neutral*` POV key names that predate the
  `PovSlot` refactor and are deliberately preserved.
- `ArchivedDiaryEntry.ExposeData` — the compact archive-row save shape.
- `DiaryGameComponent.ExposeData` (`diaries`, `diaryEvents`, `diaryArchiveEntries`,
  `activeEventWindows`) and the per-pawn record list — the top-level containers.
- `PawnDiarySettings.ExposeData` (and the `PersonaPresetStore` / `PromptOverrideDictionary`
  `ExposeData` helpers it calls) — the mod-settings shape, including the persona-preset and
  event-prompt-override maps.

Historically-preserved names that must NOT be renamed even though their meaning has shifted:
- `maxActiveDiaryEvents` — the field now means the per-pawn hot-page cap, but the Scribe key keeps
  its original name so older settings files load without losing the configured value.

### 9b. Post-load repair, and where it is tested

Loaded fields are never assumed non-null or in-range. Each `IExposable` model has a
`NormalizeOnLoad` step that runs in `LoadSaveMode.PostLoadInit` to repair cross-version saves. The
pure, RimWorld-free parts of that repair (null-coalesces, the cross-slot surroundings chain, the
neutral-text merge, the legacy `gameContext`/`instruction` rebuild, year extraction, status
reclassification, defensive clamps) live in `Source/Pipeline/DiarySaveNormalization.cs` and
`Source/Pipeline/DiaryGenerationStatus.cs`, and are unit-tested by
`tests/DiarySaveNormalizationTests/` (and the status fixtures in `tests/DiaryPipelineTests/`).

The impure parts that cannot be pure-tested stay on the save models:
- **Fresh `eventId` minting** for pre-id saves (`Guid.NewGuid` is non-deterministic).
- **`colorCue` resolution** for older saves (`DiaryEvent.ResolveColorCue` classifies via
  `GroupForDisplay`, which reads the loaded `DefDatabase`).
- **The Scribe read/write round-trip itself**, including settings legacy-field repair
  (`PawnDiarySettings.ClampValues`, `NormalizeEndpointUrls`, persona-preset/override-map
  rehydration). These are covered by the in-game smoke procedure in
  `tests/SAVE_COMPATIBILITY_SMOKETEST.md` — run it whenever you touch any `ExposeData` or rename a
  Scribe key.

### 9c. Allowed migration pattern

The only accepted reason to rename a stable Scribe key is an intentional format change with a
migration plan, and even then:

1. Keep reading the **old** key during a transition window so existing saves load.
2. On load, if the old key is present and the new key is absent, copy the value across.
3. Write only the new key on save.
4. Document the rename, the window, and the removal date in `CHANGELOG.md` and this section.

Never rename a key "for cleanliness" alone.

## 10. Runtime And DLC Constraints

- Runtime is RimWorld's Unity Mono. Use only assemblies available in `RimWorldWin64_Data/Managed`
  plus the bundled/declared Harmony dependency.
- JSON uses `Source/Util/MiniJson.cs`. Do not add `System.Web.Extensions` or external JSON libraries.
- Harmony is declared in `About/About.xml`; runtime and build copies of `0Harmony.dll` are kept in
  `1.6/Assemblies/` and `Source/Libs/`.
- No paid DLC is required. Optional DLC data must no-op cleanly when absent.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` and null checks.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use string matching or
  `GetNamedSilentFail`.

## 11. Localization

Player-facing UI strings and natural-language prompt text use
`Languages/English/Keyed/PawnDiary.xml` through `.Translate()` on the main thread. Def text uses
DefInjected translations.

Keep DefInjected English stubs in sync when editing XML labels, instructions, tones, event prompts,
writing-style rules, or shared prompts. Variant pools use indexed DefInjected keys such as
`<group.instructions.0>`; avoid blank list entries so indices stay aligned. Custom Pawn Diary Def
translation folders use fully qualified C# type names, for example
`Languages/English/DefInjected/PawnDiary.DiaryInteractionGroupDef/`; simple folder names do not
resolve for namespaced custom Defs in RimWorld's language loader. English and Russian currently
mirror eleven localization XML files: `Keyed/PawnDiary.xml` plus ten custom-Def `DefInjected`
folders, including prompt-template field labels and ritual-quality labels that can reach prompts.

Raw English is intentional for internal prompt/context schema tokens (`thought=`, saved context
keys, role ids), sentinel tokens (`initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`),
defNames, API model ids, and background-thread `LlmClient` errors. XML-owned prompt-template labels
are localized through `DiaryPromptTemplateDef` DefInjected entries so Russian prompts do not mix
localized guidance with English field names.

The source tree carries a Russian translation under `Languages/Russian (Русский)/` (folder name
matches RimWorld's Core language def), mirroring the English `Keyed` + `DefInjected` layout
key-for-key. Game terms use the official RimWorld Russian glossary; the `DiaryPersonaDef` writing
styles are deliberately *not* literal translations but reconstructions on Russian literary
traditions with synthetic, author-less examples. Russian UI strings should stay compact enough for
RimWorld's narrow settings and tab surfaces, and should avoid unexplained English calques in visible
labels: prefer plain Russian words such as "address", "connection", "distribution", "editor", and
"instruction" over "endpoint", "routing", "studio", or "system prompt" translations. Keep raw
`API`, `OpenAI`, `URL`, `Bearer`, `XML`, and `UTF-8` only where they name a protocol, product,
scheme, format, or file encoding. Russian prompt prose should be idiomatic instructions for the
model rather than literal English calques. Russian strings that contain dynamic pawn, target, work,
or event placeholders should avoid making the placeholder the subject of a gendered or numbered
past-tense verb/adjective unless the code guarantees that agreement. Prefer neutral forms such as
`У {0}: ...`, `{0}: ...`, `Событие у {0}: ...`, `Применение способности {1}: {0}`, or passive and
impersonal constructions. Russian humor cues should likewise use local dry, bureaucratic, and
household understatement patterns instead of translating English deadpan/punchline mechanics. When
adding or renaming an English key, add the matching Russian key in the same file, or the entry
silently falls back to English in a Russian game. Canonical game-term translations are recorded in
`Languages/Russian (Русский)/GLOSSARY.md` (mined from RimWorld's official RU language packs); reuse
it so terminology stays consistent with the base game.

## 12. Build, Tests, Prompt Lab

Build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

If `MSBuild` is not on `PATH`, use Visual Studio Developer PowerShell or locate it with `vswhere`.
The Debug build writes `1.6/Assemblies/PawnDiary.dll`, which is committed on purpose.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryRetentionTests/DiaryRetentionTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/PromptVariantsTests/PromptVariantsTests.csproj
dotnet run --project tests/DiarySaveNormalizationTests/DiarySaveNormalizationTests.csproj
dotnet run --project tests/DiaryObservedConditionTests/DiaryObservedConditionTests.csproj
```

Prompt lab:

```powershell
cd prompt-lab
npm test
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

Live hook checks use a disposable save, dev mode, prompt-test mode, and RimBridge/GABS. Prompt-test
mode intercepts only after a real event reaches `QueuePrompt`; a successful capture logs:

```text
[PawnDiary debug] Captured prompt without generation event=<id> role=<role>
```

Release payloads are prepared with:

```powershell
scripts\publish.ps1
```

The script builds a throwaway Release DLL, copies runnable mod files, runtime textures, `Source/`
without `bin`/`obj`, and reference docs into `dist/<published packageId>`, and installs the payloads
into the detected RimWorld `Mods` folder through junctions by default. Pass `-InstallToMods:$false`
to prepare `dist/` only.

Russian is packaged as a separate Workshop localization mod by default. The script produces the
normal main payload plus `dist/<published packageId>.russian`; the main payload excludes
`Languages/Russian (Русский)/`. The localization payload contains only its own translated Russian
`About/` metadata, `About/Preview-Russian.png` copied as the Workshop `Preview.png`, and the Russian
language folder. It declares a dependency/load-after on the main published packageId, uses packageId
`<published packageId>.russian` unless overridden with `-RussianLocalizationPackageId`, and installs
its own junction next to the main mod junction. Before updating an existing localization Workshop
item, either pass `-RussianLocalizationPublishedFileId <id>` or store that id in
`About/PublishedFileId-Russian.txt`; the script copies it into the localization payload as
`About/PublishedFileId.txt`. Use `-SplitRussianLocalization:$false` or
`-IncludeRussianInMainPayload` only for a legacy bundled-language payload.

## 13. When Changing The Mod

- Follow `AGENTS.md` for detailed architecture, DLC, localization, and validation rules.
- Keep tunable policy in XML when possible.
- Keep live RimWorld objects at adapter/UI/transport edges; pure helpers should use DTOs/primitives.
- Add or update focused pure tests when changing pure logic.
- Update `DOCUMENTATION.md` and `CHANGELOG.md` for behavior, structure, release, or workflow changes.
