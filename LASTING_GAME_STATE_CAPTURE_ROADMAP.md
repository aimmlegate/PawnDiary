# Lasting Game-State Capture Roadmap

Status: planning document only.
Created: 2026-06-29.

Purpose:
- Give future agent passes a concrete implementation plan for stable capture of lasting colony
  states.
- Keep this plan separate from `ARCHITECTURE_IMPROVEMENT_PLAN.md`.
- Make adding new long-lived conditions predictable, XML-owned, no-DLC safe, and testable.

## Goal

Capture lasting game problems from live state, not from guessed durations.

Examples:
- active map threat;
- hostile presence on a home map;
- active game conditions affecting the map;
- anomaly evidence such as gray flesh still present on the map;
- confirmed emergence of a previously hidden anomaly problem;
- pawn-level ongoing visible conditions that should influence prompts.

The target behavior is:
- if the game is still in the state, diary prompts can know about it;
- if the state ended, prompts stop treating it as active;
- if the save loads during the state, the next scan rediscovers it;
- if a signal was missed, scanning still recovers;
- if only a recent clue exists, call it recent evidence, not truth.

## Current Problem

Current long-running event windows are signal-based:
- `RecordEventWindowSignal(...)` receives a source/def/signal label.
- `DiaryEventWindowDef` decides whether that signal starts or ends a window.
- `ActiveEventWindowState` remembers active windows.
- `ScanEventWindowTimeouts()` eventually removes timed-out windows.

That works for one-shot narrative moments:
- birthday;
- void monolith discovery;
- prison break start;
- ancient danger letter;
- other clear lifecycle signals.

It is not reliable for lasting states:
- a fixed `timeoutTicks` cannot prove a metalhorror suspicion is still unresolved;
- a missed end signal leaves stale context;
- a save/load in the middle can drift from reality;
- player response time varies widely;
- modded content may produce signals in a different order;
- prompts can keep forcing threat language after the threat is gone.

Rule:
- `DiaryEventWindowDef` is for signal windows.
- Lasting states need observed conditions.
- Ticks may control scan interval, debounce, and cooldown, but not truth.

## Architecture

Add a new system: observed conditions.

High-level flow:
1. `DiaryGameComponentTickInner()` runs a condition scan on an XML-owned interval.
2. The scanner reads cheap live RimWorld state at the edge.
3. It emits plain `ObservedConditionObservation` DTOs.
4. A pure policy diffs observations against saved active state.
5. The impure adapter applies the plan: start, refresh, end, remove, record diary pages, and expose
   prompt candidates.

This keeps the same project boundary we use elsewhere:
- live game reads stay in `Source/Core/` or a scanner at the edge;
- decisions are pure and testable;
- XML owns policy, thresholds, prompt weight, and wording;
- saved state is additive and defensive.

## New Def

Add `DiaryObservedConditionDef` under `Source/Defs/`.

Suggested fields:
- `defName`
- `label`
- `enabled`
- `conditionKey`
- `scope`: `Map`, `Pawn`, or `Colony`
- `observerType`: `MapDanger`, `GameCondition`, `ThingPresent`, `PawnHediff`, `RecentEvidence`
- `pollIntervalTicks`
- `startDebounceTicks`
- `endDebounceTicks`
- `dedupTicks`
- `recordStartEvent`
- `recordEndEvent`
- `recordScope`: `MapColonists`, `SubjectPawn`, or future narrower scopes
- `promptEnabled`
- `promptWeight`
- `normalPromptWeightMultiplier`
- `colorCue`
- `instruction`
- `startTextKey`
- `endTextKey`
- `promptPriorityKey`
- `promptConditionKey`
- `promptDescriptionKey`
- `promptCueKeys`

Matcher fields:
- `matchDefNames`: exact string matches only by default;
- `matchDefNameContains`: optional, disabled unless a concrete use case needs it;
- `matchLabels`: avoid unless no stable defName exists;
- `minDangerRating`;
- `minHostileCount`;
- `includeHomeMapsOnly`;
- `includeNonPlayerMaps`;
- `maxEvidenceLabels`;
- `maxEvidenceChars`;
- `recentEvidenceTtlTicks`.

Prefer exact `defName` string matchers. They are no-DLC safe when the content is absent, because the
strings simply never match.

## Saved State

Add `ActiveObservedConditionState` under `Source/Models/`.

Suggested fields:
- `conditionDefName`
- `conditionKey`
- `scope`
- `mapUniqueId`
- `subjectPawnId`
- `firstObservedTick`
- `lastObservedTick`
- `firstMissingTick`
- `lastSeenEvidenceDefName`
- `lastSeenEvidenceLabel`
- `lastSeenEvidenceCount`
- `startRecorded`
- `endRecorded`

Save/load rules:
- Additive Scribe fields only.
- Null strings normalize to empty in `PostLoadInit`.
- Missing/removed Defs should age out without errors.
- Old saves should default to an empty observed-condition list.

Why save active state:
- avoids duplicate start pages;
- allows debounce to survive save/load;
- keeps prompt candidates active across sessions;
- lets stale conditions end cleanly after live scans stop observing them.

## Pure Policy

Add pure policy types, likely under `Source/Capture/ObservedConditions/`.

Suggested types:
- `ObservedConditionObservation`
- `ObservedConditionStateSnapshot`
- `ObservedConditionDefSnapshot`
- `ObservedConditionDecision`
- `ObservedConditionPlan`
- `ObservedConditionPolicy`

Policy inputs:
- current tick;
- saved states;
- current observations;
- def snapshots with debounce/cooldown policy.

Policy outputs:
- `StartPending`
- `StartRecorded`
- `Refresh`
- `EndPending`
- `EndRecorded`
- `DropStale`

Important behavior:
- New observation starts pending state.
- Start event records only after `startDebounceTicks`.
- Continuing observation refreshes `lastObservedTick`, evidence label, evidence def, and count.
- Missing observation marks `firstMissingTick`.
- End event records only after `endDebounceTicks`.
- No duplicate start while observed.
- Multiple maps and pawn-scoped states remain independent.

## Scanner Types

### MapDanger

Purpose:
- active while a player-home map is materially dangerous.

Potential live inputs:
- map danger rating;
- spawned hostile pawn count;
- recent threat letters only as labels/evidence;
- active raid or infestation-like map state only if available through safe base-game APIs.

Rules:
- recent letters annotate, they do not define truth;
- skip maps that are not player home unless XML explicitly opts in;
- cap hostile counts and labels;
- do not resolve labels until cheap gates pass.

First Def:
- `MapThreatActive`

### GameCondition

Purpose:
- active while `map.gameConditionManager.ActiveConditions` contains a matching defName.

Use for:
- solar flare;
- toxic fallout;
- eclipse;
- psychic drone;
- other active conditions where the game already owns start/end truth.

Rules:
- exact defName string matchers;
- no direct DLC Def lookup;
- current active condition is truth.

### ThingPresent

Purpose:
- active while matching spawned things, filth, samples, or evidence objects remain on the map.

Use for:
- gray flesh evidence;
- anomaly clues visible on the map;
- other physical evidence that can be inspected through present things.

Rules:
- conservative scan interval;
- map/home gate first;
- exact defName match first;
- cap scan count, evidence count, and labels;
- use labels as observable evidence, not hidden explanations.

First Def:
- `AnomalyEvidencePresent`

### PawnHediff

Purpose:
- active while relevant pawns currently carry a matching visible or otherwise appropriate hediff.

Use for:
- confirmed pawn-level conditions;
- visible infection-like states;
- confirmed anomaly emergence if the game exposes it through hediffs.

Rules:
- do not reveal hidden mechanics through prompt text;
- only use explicit labels when the game has made the condition observable;
- string-match hediff defNames;
- keep DLC data centralized and guarded.

First Def:
- `MetalhorrorEmergence`, only if current game state can identify a confirmed/observable emergence.

### RecentEvidence

Purpose:
- bounded fallback when the game exposes only a signal or letter and no stable state object.

Rules:
- label it recent evidence;
- give it a TTL;
- do not treat it as proof of an active threat;
- prefer replacing it with `ThingPresent`, `PawnHediff`, or `GameCondition` when those become
  available.

## Prompt Integration

Add observed-condition prompt candidates beside event-window prompt candidates.

Suggested method:
- `ActiveObservedConditionPromptCandidates(Pawn pawn, out float normalCandidateWeightMultiplier)`

Behavior:
- active observed condition can add priority text, condition text, and cues;
- XML controls prompt weight and normal prompt suppression;
- map-scoped conditions apply only to pawns on that map;
- pawn-scoped conditions apply only to the subject pawn;
- stale conditions are not prompt candidates after end debounce finishes.

Context facts:
- observed conditions should also be available as context facts once the context fact pipeline
  exists;
- suggested keys: `active_map_threat`, `active_game_condition`, `visible_anomaly_evidence`,
  `confirmed_anomaly_threat`, `recent_threat_evidence`.

## Event Recording

Observed conditions may optionally record start/end diary pages.

Use start pages when:
- a colony-wide condition becomes narratively important;
- the condition is observable;
- XML opts into `recordStartEvent`.

Use end pages when:
- the end is meaningful to diary history;
- XML opts into `recordEndEvent`.

Skip pages when:
- the condition only exists to guide prompt tone;
- the signal is too noisy;
- the condition would reveal hidden mechanics.

When recording:
- reuse the existing solo/map-colonist event-window style where practical;
- build `gameContext` with plain key/value facts;
- keep prompt prose in XML/Keyed strings;
- avoid hardcoded player-facing English in C#.

## First Implementation Roadmap

### Pass 1: Pure Policy And Save Model

Files likely touched:
- `Source/Models/ActiveObservedConditionState.cs`
- `Source/Capture/ObservedConditions/*.cs`
- pure test project files

Deliver:
- DTOs;
- saved state model;
- pure diff planner;
- tests for start, refresh, end, duplicate prevention, and reload behavior.

No RimWorld scanner yet.

### Pass 2: Def And XML Skeleton

Files likely touched:
- `Source/Defs/DiaryObservedConditionDef.cs`
- `1.6/Defs/DiaryObservedConditionDefs.xml`
- `Languages/English/Keyed/PawnDiary.xml`
- XML parse tests or existing hook verification

Deliver:
- XML-owned policy fields;
- disabled or low-risk sample defs;
- no-DLC safe string matcher docs in XML comments.

### Pass 3: GameComponent Integration

Files likely touched:
- `Source/Core/DiaryGameComponent.cs`
- `Source/Core/DiaryGameComponent.ObservedConditions.cs`

Deliver:
- saved `activeObservedConditions` list;
- `nextObservedConditionScanTick`;
- tick scan gate;
- scanner dispatcher;
- no enabled heavy scanning by default.

### Pass 4: MapThreatActive

Deliver:
- `MapDanger` observer;
- map/home gates;
- hostile count/danger threshold;
- prompt candidate integration;
- manual smoke test during a raid or hostile map state.

This is the safest first real observer because map danger is broad, visible, and not hidden content.

### Pass 5: GameCondition Observer

Deliver:
- active condition scan through the map's game condition manager;
- exact defName matching;
- prompt candidates for active condition context;
- no direct DLC content dependency.

This gives stable long-running weather/world conditions without relying on incident start signals.

### Pass 6: AnomalyEvidencePresent

Deliver:
- `ThingPresent` observer for gray flesh/evidence things or filth;
- replace any long-lived signal-plus-timeout suspicion window with observed evidence;
- prompt text must describe observable evidence, not hidden mechanics.

Important:
- do not simply re-enable metalhorror suspicion with a longer timeout.

### Pass 7: MetalhorrorEmergence

Deliver:
- confirmed active threat observer using current spawned/observable things or hediffs;
- separate this from suspicion/evidence;
- only use explicit monster/threat wording when the game has revealed it.

### Pass 8: Context Fact Bridge

Deliver after the context fact pipeline exists:
- active observed-condition facts available to prompt templates;
- XML templates choose what to include;
- prompt output tests for representative solo, pair, and neutral prompts.

## What Not To Do

- Do not increase `timeoutTicks` to make lasting states "feel" longer.
- Do not use a trigger as proof that a condition remains active.
- Do not scan every thing on every tick.
- Do not put prompt policy or prose directly in C#.
- Do not directly reference optional DLC defs or call `DefDatabase<T>.GetNamed` for DLC content.
- Do not reveal hidden anomaly mechanics before the game makes them observable.
- Do not build a global "scrape everything" dump for prompts.

## Validation Checklist

Pure tests:
- start debounce works;
- start event is not duplicated;
- refresh updates evidence;
- end debounce works;
- stale state drops when Def is disabled or removed;
- reload with active observation rediscovers without duplicate start;
- reload with stale saved state ends cleanly;
- multiple maps do not cross-contaminate;
- pawn-scoped conditions do not affect unrelated pawns.

Runtime/build checks:
- XML well-formed check;
- pure helper tests;
- `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`;
- committed DLL freshness check if C# changed.

Manual smoke tests:
- active raid or hostile map state produces `MapThreatActive`;
- ending the threat removes prompt pressure after end debounce;
- active game condition appears and clears from prompt candidates;
- anomaly evidence remains active only while evidence is present or recent evidence TTL is valid;
- save/load in the middle of each state self-heals.

## Open Questions For Implementation Passes

- Exact best vanilla API for map danger should be verified in the current RimWorld assembly before
  coding `MapDanger`.
- Exact active/observable metalhorror state should be verified before enabling explicit emergence
  prompts.
- Thing scanning needs profiling in a real save before broad matchers are enabled.
- We may eventually want composite conditions, but only after simple observer types are working and
  tested.

## Done Bar

The observed-condition system is ready when:
- lasting map/anomaly states are represented by observed conditions, not event-window timeouts;
- prompt candidates follow current live state after debounce;
- save/load does not lose or duplicate active states;
- XML can add a new condition without new C# unless it needs a new observer type;
- tests cover the pure diff behavior;
- no-DLC games run cleanly with all optional content absent.
