# Pawn Diary — Odyssey Support Implementation Plan

Status: Phases O1.0-O1.5 plus Narrative N2-O implemented on 2026-07-17 against RimWorld 1.6.4871. Frozen contracts now have
assembly-free policy, XML projection, guarded live location/mobile-home capture, and additive bounded
journey/history persistence, state-only takeoff/travel/landing hooks, and the exact-POV journey/home
provider plus departure/landing evidence factories; O1.4 adds the one novelty-gated landing event,
bounded launch policy, and package-gated groups; O1.5 projects the complete landing truth into the
important prompt templates, protects its core fields under every context preset, adds a localized dev
fixture, and pins routine/no-writer/old-save/mid-flight/`TileSettled` boundaries. This was started as a
user-directed scheduling exception while five Biotech Phase 4 manual acceptance rows remain open.
The focused O1 runtime/save acceptance completed on 2026-07-17: base-only startup skipped all five
Odyssey lifecycle fixtures cleanly, real cancellation and cross-layer travel/landing passed, and the
Phase A/B/C reload sequence preserved the frozen keys, rejected replay/resurrection, and deleted both
reserved saves. The broader loaded component/prompt-flow rerun remains separate, and later O2/O3
life-support and Mechhive hooks still require their own spikes.

Scheduling authority: implement Odyssey phases only in the waves assigned by
`DLC_SUPPORT_MASTER_IMPLEMENTATION_PLAN.md`; this file remains the technical authority for Odyssey.

This is the next major standalone DLC plan from `design/DLC_ATMOSPHERE_RESEARCH.md`. Biotech owns
the roadmap's priority-one growth/family work; Odyssey's gravship journey and landing chapters are
priority two. The smaller Anomaly ritual split remains a worthwhile independent XML slice, but it
does not replace Odyssey as the next flagship arc.

The plan turns Odyssey's existing launch, orbital-debris, vacuum, and weather fragments into one
coherent story: a mobile home leaves, travels, and sometimes arrives somewhere important enough to
remember. The implementation deliberately reuses Pawn Diary's event catalog, XML prompt policy,
plain snapshot boundary, persistence, and generation queue rather than creating a parallel DLC
pipeline.

Cross-DLC journey/home integration follows
`DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md`. Odyssey owns journey truth and landing page
authorization; the shared layer owns relevant family, belief, identity, pressure, and prior-chapter
selection. Odyssey must behave identically when no other DLC provider exists.

Implementation must follow `AGENTS.md` and `skills/pawndiary-engineering/SKILL.md`: collect live
RimWorld state in guarded impure adapters, pass only plain facts into pure policy, keep tuning and
prompt prose in XML, build and document every behavior slice, and remain silent and safe when
Odyssey is not active.

## 1. Review outcome and recommended release boundary

### 1.1 What the repository review found

The current workspace contains a main atmosphere roadmap and standalone Ideology, Royalty, and
Biotech plans. Those standalone documents explicitly describe future implementation; their planned
types, state machines, and hooks are not present in production code yet. Pawn Diary does already
ship smaller DLC-aware features through guarded context reads and XML string matching, so “DLC
support exists” and “the new standalone plans are implemented” must not be treated as the same
claim.

| Artifact or runtime area | Reviewed state |
|---|---|
| `design/DLC_ATMOSPHERE_RESEARCH.md` | Current cross-DLC roadmap; Odyssey journey/landing is priority two. |
| `IDEOLOGY_SUPPORT_IMPLEMENTATION_PLAN.md` | Design plan only; no planned belief resolver or reflection route exists in production. |
| `ROYALTY_SUPPORT_IMPLEMENTATION_PLAN.md` | Scope-review plan only; no planned persona/succession state exists in production. |
| `BIOTECH_SUPPORT_IMPLEMENTATION_PLAN.md` | Implementation plan only; its planned growth/family state is not production code. |
| Current Odyssey runtime support | Launch ritual matching, orbital-debris matching, vacuum harm, generic visible condition context, and a few weather matchers. |
| Main Odyssey gap | No committed journey state, landing chapter, destination novelty, or mobile-home context. |

This plan therefore starts from the current production baseline. A coding agent must not assume that
shared abstractions proposed by another standalone plan already exist.

### 1.2 Recommended release sequence

Odyssey should ship in three independently reviewable increments.

#### O1 — Gravship journey, landing, and mobile-home context

Required for the first Odyssey release:

- Keep the existing gravship ritual as the canonical departure ceremony.
- Never create a second page from takeoff or the travel commit.
- Capture a transient takeoff intent and commit one saved active journey only when vanilla really
  calls `GravshipUtility.TravelTo`.
- Capture final landing facts and optionally create one novelty-gated landing chapter after
  `WorldComponent_GravshipController.LandingEnded` succeeds.
- Recognize first orbit, a new biome category, a configured major destination, a meaningful
  homecoming, a sufficiently long journey, or a rough landing as possible chapter reasons.
- Update journey history even when no page is written, so settings and cooldowns cannot manufacture
  false “first” claims later.
- Select at most two writers for a landing: pilot, copilot, then a deterministic eligible crew
  witness.
- Add concise, guarded mobile-home context to ordinary entries written aboard or beside the tracked
  gravship.
- Correct the launch prompt so it describes a departure ceremony, not an unobserved landing.
- Add persistence, old-save baselining, pure policy tests, focused RimTests, localization,
  documentation, changelog entries, and the rebuilt committed DLL.

#### O2 — Environmental pressure and visible landing consequences

- Represent persistent seasonal flooding through the existing generic observed-condition system.
- Audit visible Odyssey `GameConditionDef` values as prompt atmosphere before adding any new page
  source; `BuildActiveMapConditionsSummary` already handles many of them generically.
- Add `GravNausea` as bounded bodily prompt context.
- Correlate an exact negative landing outcome into the O1 landing page only after the outcome-worker
  dispatch has a verified, fail-soft hook.
- Add a colony-wide life-support crisis only after a separate spike finds a visible state change and
  a bounded, non-polling or low-frequency observation seam.

#### O3 — Exploration chapters and the Mechhive ending

- Extend XML-owned destination categories for major Odyssey sites and gravcore quest destinations.
- Let existing quest acceptance/completion pages own quest lifecycle facts; landing owns physical
  arrival.
- Add gravcore recovery or Mechhive choice context only after exact player-visible source and choice
  facts have been verified.
- Add an arc reflection after the Mechhive ending only when its exact resolution owner is known.

O1 is implemented through O1.5. O2's seasonal-flood, `GravNausea`, and exact landing-outcome slices
are implemented; life support remains behind its separate feasibility gate. O3 is
intentionally a reviewed direction, not authorization to guess at Mechhive choice state.

## 2. Product outcome

After the full Odyssey plan is implemented:

1. The existing launch entry reads as a real departure ceremony. It does not claim that a
   destination was selected or reached before the player has done so.
2. Takeoff and travel quietly establish a saved journey. They do not create competing pages.
3. A landing can become a chapter when it changes the crew's sense of home: first orbit, a new kind
   of world, a major site, a return, a long crossing, or a rough arrival.
4. Routine hops update history without writing. Repeating the same biome/site category cannot
   generate a stream of “first” entries.
5. The pilot receives first choice of perspective, the copilot second, and eligible crew provide a
   deterministic fallback. A whole ship never produces a colony-wide landing fan-out.
6. Ordinary entries know the human-scale setting—ship name, aboard/landed/orbital state, and visible
   destination character—without fuel, engine, cell, subsystem, or world-generation dumps.
7. A landing at a quest site describes arrival only. It does not claim the quest is complete or
   reveal a hidden threat.
8. Seasonal flooding and visible Odyssey conditions color later entries without each condition
   becoming a repetitive page.
9. A negative landing outcome enriches the one landing chapter when exact facts are available; it
   never competes as a second page.
10. The Mechhive ending is recorded only from an exact visible resolution source, with no guessed
    “destroyed” or “controlled” choice.
11. Optional Ideology, Royalty, or Biotech enrichment can consume plain journey facets later, but O1
    does not depend on any of those standalone plans being implemented.
12. With Odyssey inactive, a base-game colony sees no errors, settings clutter, empty Odyssey
    fields, changed page eligibility, or DLC dependency.

## 3. Scope and non-goals

### 3.1 Required O1 scope

- A guarded Odyssey live-data boundary in `DlcContext`.
- Plain location, takeoff, journey, landing, writer-candidate, novelty-state, and landing-plan
  contracts.
- One XML-owned `DiaryOdysseyPolicyDef` for thresholds, caps, category mappings, salience, and prompt
  limits.
- One saved active journey plus bounded saved novelty/history state.
- Exact takeoff, travel-commit, landing-start, and landing-finish correlation.
- A concrete `GravshipJourney` event catalog route. Do not create a generic cross-DLC journey type.
- One canonical landing event with one or two POV roles.
- A small launch-policy adapter that can cap or cool down the existing launch ritual without
  changing unrelated rituals.
- Mobile-home prompt context.
- Load baselining, cache reset, fail-soft hook registration, and no-DLC behavior.
- Pure tests, focused RimTests, English/Russian localization, docs, changelog, build, and DLL.

### 3.2 O2 scope

- An Odyssey-gated `ThingPresent` observed-condition Def for `SeasonalFlood`.
- Removal of the ineffective `Flooding` mood-event matcher.
- A `GravNausea` prompt enchantment.
- Exact landing-outcome context only after a verified override-aware patch strategy.
- A life-support crisis only if a later spike can prove visible state, ownership, hysteresis, and
  cost.

### 3.3 O3 scope

- XML classifications for a deliberately small set of visible destination/site categories.
- Existing quest-route enrichment for exact gravcore and Mechhive quest roots.
- A verified Mechhive resolution chapter and one later arc reflection.
- Participant selection bounded to pawns who actually travelled, fought, operated, or witnessed the
  resolution.

### 3.4 Explicit non-goals

- No page for every launch, takeoff, landing, generated map, opportunity site, fish, or animal
  training step.
- No separate page when `TravelTo` commits the trip.
- No generic `JourneyChapterEventData`, `AmbientPressureEventData`, or shared DLC event pipeline.
- No live `Pawn`, `Map`, `Def`, gravship, controller, Verse, Unity, settings, or Harmony object in
  pure policy or saved journey contracts.
- No prompt dump of tile IDs, engine power, fuel, launch cooldown, ship footprint, subsystem state,
  travel percentage, or landing coordinates.
- No English keyword parsing of biome/site labels to infer exploration, salvage, combat, or danger.
- No hidden site contents, unrevealed threats, future quest outcomes, or internal world-generation
  tags in a prompt.
- No exact negative-outcome name in O1. O1 may preserve only the truthful coarse fact that the launch
  was flagged for a rough landing.
- No life-support alarm from normal orbital life and no per-tick ship scan.
- No Mechhive ending page until the destroy/control source is verified.
- No attempt to retrofit journey facts into already-generated diary entries.
- No new DLC dependency in `About/About.xml`.
- No LLM-driven movement, landing, quest, ship, pawn, or game-state mutation.

## 4. Definition of done

### 4.1 O1 is complete only when

- Completing the launch ritual creates no takeoff or travel duplicate.
- The launch prompt never says the ship landed and never names an unselected destination.
- `InitiateTakeoff` alone does not commit a journey.
- `TravelTo` commits exactly one active journey with stable origin, destination, crew, and timing
  facts even if the earlier transient intent is missing.
- A cancelled destination selection or aborted takeoff creates no saved completed journey.
- `InitiateLanding` alone creates no page.
- A successful `LandingEnded` evaluates novelty once, applies history once, and produces at most one
  event with at most two POV entries.
- A repeated routine landing updates state silently.
- First-orbit, new-category, major-site, homecoming, long-journey, and rough-landing rules are
  deterministic, XML-tuned, and pure.
- The chosen primary and optional secondary reason are traceable to saved event-time facts.
- Pilot/copilot/crew selection is deterministic and falls back cleanly when a pawn is dead,
  ineligible, missing, or lacks a diary.
- The generated entry remains truthful when the gravship is renamed, the biome changes later, a
  site disappears, or a pawn leaves the colony.
- Saving mid-flight and loading before landing preserves the active trip without a catch-up
  departure page.
- Loading an old save silently baselines history and never calls the next observed landing
  “first-ever” solely because Pawn Diary did not previously track it.
- The private landing hook missing after a game update produces one warning, no exception, no
  suppression of vanilla, and no partially emitted page.
- `TileSettled` remains non-duplicating: its no-pawn Tale is ignored by the current `TaleSignal`.
- Odyssey inactive means every new read, patch body, state observer, formatter, and settings row
  cleanly no-ops.
- All pure suites, focused RimTests, XML parsing, localization checks, Debug MSBuild, and repository
  verification pass.

### 4.2 The full Odyssey program is complete only when

- Seasonal flood state shades prompts through one bounded observed condition and creates no page by
  default.
- Visible Odyssey `GameCondition` context is not duplicated by unnecessary bespoke observers.
- `GravNausea` can shade a relevant entry without becoming a standalone sickness stream.
- An exact landing consequence, when supported, is merged into the landing event.
- Major-site and gravcore chapters use visible exact categories and preserve quest ownership.
- A Mechhive resolution names the actual visible choice and outcome from an exact source.
- The terminal chapter can schedule one arc reflection without replaying on load.
- `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, `TEST_COVERAGE_PLAN.md`, `CHANGELOG.md`, localization,
  prompt fixtures, and the committed DLL match every shipped increment.

## 5. Reviewed current coverage and gaps

| Current seam | What it already does | Gap or correction |
|---|---|---|
| `ritualGravship` XML group | Matches `GravshipLaunch` and `RitualBehaviorWorker_GravshipLaunch`. | Its prose says “launch or landing” although the source only observes launch ritual completion. |
| `RitualOutcomePatch` | Captures a completed ritual and fans out role-specific solo entries. | It runs before destination selection and has no journey correlation or Odyssey writer cap. |
| Tale XML | Matches `OrbitalDebris`. | It does not establish a travel or landing lifecycle. |
| `DiaryEnchant_VacuumExposure` | Matches actual `VacuumExposure` and `VacuumBurn` hediffs. | No `GravNausea` context and no colony-level visible life-support policy. |
| Mood-event weather group | Matches `VolcanicAsh` and `Flooding`. | `Flooding` is not the real persistent flood Def; the incident is `SeasonalFlooding` and the map Thing is `SeasonalFlood`. |
| `BuildActiveMapConditionsSummary` | Adds visible, UI-displayed active conditions to surroundings generically. | This already covers many Odyssey conditions; bespoke duplicates would add noise. |
| Observed-condition framework | Supports `GameCondition` and `ThingPresent` observers with pure policy and saved state. | No Odyssey `SeasonalFlood` Def is configured yet. |
| `DlcContext` | Guards Biotech, Royalty, Ideology, and selected Anomaly reads. | It has no Odyssey ship/location projection. |
| `DiaryGameComponent` persistence | Scribes events, windows, observed conditions, cooldowns, and other state. | No active gravship journey or travel history. |
| Event catalog | Has concrete sources for rituals, quests, progression, arrivals, and others. | No concrete gravship journey event. |
| `TileSettled` vanilla Tale | Records settlement of a generated landing tile. | It is a no-pawn Tale, so current `TaleSignal` drops it; retain this behavior as a regression test. |

The current behavior is the fallback. If a new correlation cannot establish exact ownership, it must
omit the optional fact or page rather than weakening the existing routes.

## 6. Verified RimWorld 1.6 seams

These findings were checked against the locally installed RimWorld 1.6 managed assembly and Odyssey
Defs. Exact signatures must be re-checked immediately before implementation.

### 6.1 DLC identity and activation

- Odyssey package ID: `Ludeon.RimWorld.Odyssey`.
- Every live Odyssey adapter starts with `ModsConfig.OdysseyActive`.
- Odyssey C# types exist in `Assembly-CSharp.dll` even when the player does not own or enable the
  content pack. Successful compilation is not a DLC-safety test.

### 6.2 Departure and travel

- `RitualOutcomeEffectWorker_GravshipLaunch.Apply` stores launch information and then starts
  destination selection or confirmation.
- The existing `LordJob_Ritual.ApplyOutcome` postfix therefore observes the ceremony before a final
  destination is available. Launch prompt text must not invent one.
- `WorldComponent_GravshipController.InitiateTakeoff(Building_GravEngine engine, PlanetTile
  targetTile)` is public and supplies the selected destination plus the still-live origin map.
- Private `TakeoffEnded()` eventually calls `GravshipUtility.TravelTo(Gravship gravship,
  PlanetTile oldTile, PlanetTile newTile)`.
- `GravshipUtility.TravelTo` is public static and is the preferred commit seam. It means vanilla
  actually converted the captured ship into a travelling world object.
- `Gravship` exposes `Engine`, `Pawns`, `Label`, `destinationTile`, and a stable world-object ID.
- `Building_GravEngine.launchInfo` is currently a public `LaunchInfo` field whose `pilot`, `copilot`,
  `quality`, and `doNegativeOutcome` fields are also public. Treat this as optional version-fragile
  input anyway: journey capture must fail soft and cannot depend on every field remaining available.

Do not patch private `TakeoffEnded` merely to learn that travel committed. The public `TravelTo`
call is narrower and more resilient.

### 6.3 Arrival and landing

- `Gravship.TickInterval` eventually calls `GravshipUtility.ArriveExistingMap` or
  `GravshipUtility.ArriveNewMap`.
- `ArriveNewMap` may generate a map/site and records the no-pawn `TileSettled` Tale before landing
  placement.
- `WorldComponent_GravshipController.InitiateLanding(Gravship gravship, Map map, IntVec3
  landingPos, Rot4 landingRot)` is public and supplies the chosen landing map.
- Private `LandingEnded()` performs final placement and scenario notification. It is the verified
  successful-finish seam.
- The controller holds the active gravship, landing map, origin/destination tiles, and landing marker
  while the sequence is in progress.

Register `LandingEnded` manually through `DiaryPatchRegistrar` using exact zero-parameter lookup.
If lookup fails, log one warning and leave vanilla untouched.

### 6.4 Landing outcomes

`LandingOutcomeWorker.ApplyOutcome(Gravship)` is virtual. Vanilla subclasses override it:

- `MinorGravshipCrash`
- `ThrusterBreakdown`
- `OverheatedGravEngine`
- `GravNausea`

Patching only the base virtual method does not reliably observe overrides. O1 stores only a coarse
rough-landing flag from launch information. O2 must either register every discovered concrete
override defensively or find a later exact result seam; it must not claim an outcome from chance
alone.

### 6.5 Human-readable mobile-home reads

Verified public helpers include:

- `GravshipUtility.PlayerHasGravEngine(Map)`
- `GravshipUtility.GetPlayerGravEngine_NewTemp(Map)`
- `GravshipUtility.IsOnboardGravship_NewTemp(...)`
- `GravshipUtility.TryGetNameOfGravshipOnMap(Map, out string)`
- `Game.Gravship` while a gravship world object is active

Use these behind `DlcContext` and cache only safe per-map results if profiling shows a need. Do not
reflect over ship contents merely to build ordinary prompt context.

### 6.6 Environment and destinations

Verified Odyssey condition/incident names include:

- Visible conditions such as `VolcanicAsh`, `VolcanicDebris`, `LavaFlow`, `DarkenedSkies`,
  `BioluminescentSpores`, `WindyGameCondition`, `Drought`, `DeepFreeze`, and `GillRot`.
- Hidden/internal `DroughtInitial`, which must not reach prompts as a player-facing fact.
- Incident `SeasonalFlooding` and persistent map Thing `SeasonalFlood`.
- Hediffs `VacuumBurn`, `VacuumExposure`, and `GravNausea`.

Verified quest/site families include gravcore relay, insect lair, ancient reactor, orbital
platform, stockpile, crashed platform, frozen terraformer, Mechhive, wreckage, asteroid, station,
and satellite opportunities. Their exact IDs may classify a visible destination in XML; they must
not be converted into claims about contents the crew has not discovered.

## 7. Target architecture

### 7.1 Lifecycle and ownership flow

```text
Gravship launch ritual
  -> existing Ritual route owns optional departure ceremony
  -> no destination claim

InitiateTakeoff
  -> transient guarded takeoff intent
  -> no page, no saved completed journey

GravshipUtility.TravelTo
  -> commit one saved active journey
  -> update departure history
  -> no page

InitiateLanding
  -> transient landing snapshot
  -> no page

private LandingEnded
  -> confirm success
  -> pure novelty + writer plan
  -> apply bounded history once
  -> optionally submit one GravshipJourney event
```

### 7.2 Architecture barriers

The implementation follows four layers:

1. **Impure adapters** read the controller, gravship, maps, pawns, Defs, settings, and current tick on
   the main thread.
2. **Plain snapshots** carry primitive strings, numbers, booleans, enums, and bounded lists.
3. **Pure policy** classifies location, novelty, page reason, cooldown, prompt facts, and writers.
4. **Impure sinks** update saved state, construct a `DiaryEvent`, queue generation, and localize
   player-facing prose.

No policy method may accept a live `Pawn`, `Map`, `PlanetTile`, `Gravship`, `Def`, Harmony handle,
settings singleton, or `DiaryGameComponent`.

### 7.3 Proposed plain contracts

Names may change during the first implementation slice, but responsibilities must remain separate:

- `OdysseyLocationSnapshot` — stable layer/tile key, surface/orbit, biome/site Def names and cleaned
  labels, visible category, player-home flags, and knowledge flags.
- `OdysseyTakeoffIntent` — engine/ship key, origin snapshot, target key, pilot/copilot facts, eligible
  crew candidates, launch quality band, rough flag, and capture tick.
- `OdysseyJourneySnapshot` — committed journey ID, origin/destination snapshots, departure tick,
  ship/pilot/copilot/crew facts, and source completeness.
- `OdysseyLandingObservation` — active journey plus final visible destination and landing tick.
- `OdysseyTravelHistorySnapshot` — initialized flag, feature-start tick, visited category keys,
  former-home keys, last launch/landing page ticks, and bounded prior journey facts.
- `OdysseyWriterCandidate` — pawn ID, cleaned name, role token, eligibility, presence, and stable
  tie-break data.
- `OdysseyLandingPlan` — write/drop, primary/secondary reason tokens, importance, selected writer
  IDs/roles, context facts, and exact state mutations.

Saved state classes implement `IExposable`, normalize null collections after load, and keep their
public surface novice-readable with summaries and comments.

### 7.4 Concrete event route

Add:

- `DiaryEventType.GravshipJourney`
- `GravshipJourneyEventData`
- `GravshipJourneyEventSpec`
- one catalog registration
- `GravshipJourneySignal`
- `GroupDomain.GravshipJourney`

The event payload contains the pure landing plan and primitive facts. The signal may temporarily
hold live selected `Pawn` references only for immediate event creation, following existing signal
patterns. Saved events retain IDs, names, roles, and event-time context only.

Use existing `GenerateSolo` and `GeneratePair` decisions. A pair-shaped event holds two POV entries
inside one canonical `DiaryEvent`; do not create one fan-out event per crew member.

### 7.5 Required shared narrative evidence

After Narrative Continuity N1 exists:

- committed departure uses `journey_chapter=departed`, ship subject, and
  `odyssey-journey|<journeyId>`; the ritual page may receive this only when the later commit can be
  truthfully correlated without rewriting an already queued event;
- successful landing uses `journey_chapter=arrived` or `returned`, the same journey arc key, exact
  ship/place subjects, and the departure event as `relatedEventId` when available;
- active mobile-home, vacuum, flood, or location pressure is a guarded provider candidate scoped to
  the exact ship/map/location, not automatically another page;
- hidden site threats, uncommitted destinations, and Mechhive choices remain absent until their
  source phases verify visible truth.

Odyssey's provider may propose active mobile-home/journey/homecoming and location pressure. It must
not dump fuel, engines, cells, subsystem state, or world-generation internals. Biotech family/home
facts are the recommended first cross-provider acceptance case, but Odyssey code must know them only
through the shared evidence/candidate contract.

## 8. Journey lifecycle policy

### 8.1 Departure ceremony ownership

The existing ritual remains the only page owner for departure. O1 changes its wording and bounded
eligibility, not its source:

- Phrase it as a completed launch ceremony or final preparation, not guaranteed lift-off.
- Include ship name, origin setting, ritual quality, and the pawn's actual ritual role when known.
- Omit destination because destination selection occurs afterward.
- Consult pure Odyssey launch policy only for the exact XML-configured launch group.
- Permit a page for a first observed launch ceremony, leaving a long-held home, or after an
  XML-defined launch-page cooldown.
- Routine repeat ceremonies within the cooldown stay silent.
- Limit launch writers with an optional XML-owned fan-out cap; default behavior for every other
  ritual remains unchanged.
- If Odyssey policy is absent or the adapter fails, preserve current ritual behavior rather than
  suppressing all launch entries.

The policy's stable internal group key belongs in XML, not as a scattered C# string.

### 8.2 Takeoff intent

At `InitiateTakeoff` prefix:

- Return immediately unless the game is playing and Odyssey is active.
- Capture the origin map before vanilla can abandon it.
- Capture selected target tile, ship/engine ID and name, pilot/copilot if available, ritual/launch
  quality band, rough flag, crew candidates, and tick.
- Store only one bounded transient intent matching vanilla's single active controller.
- Do not write save state and do not submit an event.

If a mod starts a second concurrent takeoff while an intent exists, replace only an exact same
engine intent. Otherwise warn once in development builds and fail closed rather than cross-linking
two ships.

### 8.3 Travel commit

At `GravshipUtility.TravelTo`, a prefix first copies the original `oldTile` into Harmony `__state`
because vanilla rewrites that by-value argument onto `newTile.Layer` during cross-layer travel. The
postfix then:

- Require Odyssey active and a valid gravship/destination.
- Correlate the transient intent by engine/world-object identity and a short XML time window.
- Build a complete journey from the intent when possible.
- Fall back to `oldTile`, `newTile`, `gravship.Engine`, `gravship.Pawns`, and current labels when the
  intent is absent.
- Use a stable journey ID such as `odyssey-journey|<engine-id>|<departure-tick>`.
- Commit one active journey and mark the origin/home observation.
- Clear the matching transient intent.
- Never emit a page here.

Applying the same commit twice must be idempotent by journey ID.

### 8.4 Landing start and finish

At `InitiateLanding` prefix:

- Snapshot the actual target map and visible destination facts.
- Correlate the active journey by gravship identity.
- Store a bounded transient pending landing.
- Do not mutate novelty history and do not emit.

At private `LandingEnded`:

- Prefix: preserve any final controller fields that vanilla will clear and capture eligible live
  writer references.
- Postfix: only after the original returns successfully, combine that snapshot with saved journey
  state and run pure landing policy.
- Apply every returned history mutation once, including for a dropped page.
- Submit `GravshipJourneySignal` only when the plan authorizes a page and a writer remains eligible.
- Clear the active journey and transient landing after state is safely applied.

If `LandingEnded` throws, the ordinary Harmony postfix does not authorize a page. Leave pending
state for bounded cleanup/recovery and never report a successful landing.

### 8.5 Incomplete and abandoned journeys

- A new takeoff intent replaces an expired intent after the XML timeout.
- An active journey may survive save/load and map transitions.
- A journey with no matching landing stays saved until a later travel commit, explicit vanilla
  cancellation evidence, or a conservative XML retention cap.
- Expiration clears correlation state silently; it does not create a “lost ship” story.
- Unknown origin/destination fields remain absent. The formatter never substitutes invented prose.

## 9. Mobile-home context policy

### 9.1 Guarded collection

Add a `DlcContext` entry point that returns an empty snapshot unless:

- `ModsConfig.OdysseyActive` is true;
- the pawn and map exist;
- the map has a player grav engine;
- the pawn is actually onboard or the event explicitly concerns that gravship.

Use `TryGetNameOfGravshipOnMap` for the player-facing name and the map/tile/site APIs for visible
location. If exact onboard detection fails, omit “aboard” rather than treating every pawn on a
landing map as crew.

### 9.2 Pure formatting

`OdysseyContextFormatter` may produce bounded facts such as:

- ship name;
- aboard/landed/orbital state;
- surface versus orbit;
- visible biome label;
- visible landmark/site category;
- whether this is a former/current home.

Append the result from `DiaryContextBuilder.BuildSurroundingsSummary`, not as an always-on identity
fact in `BuildPawnSummary`. Avoid repeating biome text already present in surroundings.

### 9.3 Excluded context

Never include:

- fuel or fuel cost;
- grav engine/thruster counts or condition;
- launch cooldown;
- ship wealth, footprint, room/cell counts, vacuum grid internals, or power state;
- raw tile/layer IDs;
- hidden site parts, quest nodes, or generated threats;
- exact travel percentage.

Normal orbit should sound enclosed or unusual only when prompt policy chooses that tone. It must not
be described as a crisis.

## 10. Landing novelty and writer policy

### 10.1 Authorizing reasons

The pure policy can authorize a landing chapter for these stable reason tokens:

| Reason | Required evidence |
|---|---|
| `first_orbit` | History is trustworthy and this is the first committed surface-to-orbit arrival. |
| `new_biome_category` | XML maps the visible biome to a category not yet visited after baselining. |
| `major_destination` | Exact visible destination/site Def maps to an enabled major category. |
| `homecoming` | Destination matches a previously tracked home and the crew has since completed another journey. |
| `long_journey` | Landing tick minus committed departure tick crosses the XML threshold. |
| `rough_landing` | Saved launch facts truthfully indicate a rough landing or O2 later confirms an exact outcome. |

Unknown/modded biome or site Defs may appear as bounded visible labels, but do not authorize novelty
unless XML explicitly maps them or a conservative “unknown category” rule is enabled.

### 10.2 Selection order and cooldown

Suggested default priority:

1. major destination;
2. homecoming;
3. first orbit;
4. new biome category;
5. rough landing;
6. long journey.

XML owns the order/weights and whether a reason bypasses the ordinary landing cooldown. The plan
returns one primary and at most one secondary reason. It never sends the whole candidate list to the
LLM.

History updates occur before the next observation but after the plan is computed from the previous
snapshot. A disabled group, no eligible writer, or cooldown drop still marks the destination
visited.

### 10.3 Writer selection

Select at most two distinct writers:

1. eligible pilot present at landing;
2. eligible copilot present at landing;
3. deterministic eligible crew witness ordered by stable pawn ID;
4. no page when nobody is eligible.

Do not use random global `Rand` for writer selection. The same snapshot must produce the same
writers in pure tests and after reload recovery.

Role tokens are stable schema values (`pilot`, `copilot`, `crew`), intentionally English like other
structured prompt labels. Player-facing role prose is Keyed localization.

### 10.4 Event-time truth

Save:

- cleaned ship, origin, destination, biome, and site labels;
- stable Def/category tokens;
- journey duration band, not raw mechanics in the prompt;
- writer IDs/names/roles;
- primary/secondary reason;
- rough/quality bands;
- only visible knowledge flags.

Later renames, map deletion, pawn death, site removal, or Def changes must not alter an existing
entry.

## 11. Environmental and landing-consequence policy (O2)

> **Progress 2026-07-17:** §11.1 and §11.3 are implemented with localized XML policy, assembly-free
> contracts, and a focused loaded-Def RimTest. The generic visible-condition audit found no new owner
> was needed for §11.2. Section 11.4 is implemented through discovered concrete-worker postfixes that
> capture only successfully applied exact outcomes and enrich the same landing page. Section 11.5
> remains a separate evidence-gated spike.

### 11.1 Seasonal flooding

The current `Flooding` mood matcher cannot work as intended. Implement the smallest correction:

- Remove `Flooding` from the MoodEvent matcher.
- Add an Odyssey-gated `DiaryObservedConditionDef` using `ThingPresent` and exact ThingDef string
  `SeasonalFlood`.
- Use `MayRequire="Ludeon.RimWorld.Odyssey"` on the custom Def so no-DLC settings remain clean.
- Configure it as prompt activity with no page on start/end by default.
- Use XML hysteresis/cooldown/refresh values supported by the observer and pure tests.

Do not add a bespoke C# flood poller unless the existing generic observer proves insufficient.

### 11.2 Visible conditions

`BuildActiveMapConditionsSummary` already appends visible, display-on-UI active conditions. Before
adding an Odyssey observed-condition Def:

1. prove the condition is not already represented;
2. prove it is visible to the pawn/player;
3. decide whether it needs persistence beyond the active `GameCondition`;
4. prefer prompt shading over a page.

Never expose `DroughtInitial` merely because its Def exists.

### 11.3 Bodily consequences

Add `GravNausea` to `DiaryPromptEnchantmentDefs.xml` with XML chance/weight/severity and DefInjected
text. It shades an already-authorized entry and does not create an immediate health page by itself.

Existing `VacuumExposure`/`VacuumBurn` behavior remains. Do not add a second Odyssey-specific vacuum
event route.

### 11.4 Exact landing outcomes

**Implemented 2026-07-17.** Startup defensively discovers each concrete
`LandingOutcomeWorker.ApplyOutcome(Gravship)` override and patches it with a shared postfix. The
postfix runs only after a successful concrete worker return, reads its exact `LandingOutcomeDef`,
correlates the same live ship to the transient finish row, and projects the localized letter label as
`landing_outcome`. The canonical landing remains the sole event owner; no outcome state is persisted.
Missing/changed overrides log once and omit only this optional fact.

O2 may enrich the O1 event with an exact outcome only if:

- every concrete override is discovered at startup and patched defensively;
- the actual applied outcome Def/worker is captured, not inferred from probability;
- correlation identifies the same active journey;
- one landing event owns the prose;
- missing override hooks fall back to `rough_landing=true` or no fact.

No outcome subclass gets its own page.

### 11.5 Life-support feasibility gate

Before implementation, a separate spike must identify:

- a player-visible pressure/life-support state;
- the map/ship owner;
- transition boundaries for normal → warning → crisis → recovered;
- hysteresis and scan cadence;
- a no-DLC and no-gravship fast path;
- a way to avoid describing ordinary orbit as emergency.

If those cannot be established without hot polling or hidden internals, life-support crisis remains
deferred.

## 12. Exploration and Mechhive policy (O3)

### 12.1 Destination classification

Use exact Def-name mappings in `DiaryOdysseyPolicyDef` to project visible destinations into a small
category set such as:

- asteroid;
- wreckage;
- orbital platform/station;
- ancient site;
- gravcore site;
- Mechhive;
- ordinary settlement/home;
- unknown.

The category is a stable internal token. Player-facing labels come from cleaned, bounded live Def or
world-object labels. Do not infer category by searching English text.

### 12.2 Quest ownership

- Quest acceptance owns accepting the objective.
- Landing owns physical arrival.
- Quest completion/failure owns the terminal result.
- A gravcore recovery fact belongs to exact quest completion or item-acquisition evidence, not to
  merely landing at a related site.
- Same-tick auto-accept/arrival cases require an explicit correlation test before either route is
  suppressed.

Landing may include `destination_site=<visible label>` but never `quest_completed=true`.

### 12.3 Mechhive feasibility gate

The installed game contains `QuestNode_Root_Gravcore_Mechhive`, but this O1 spike did not verify the
exact destroy-versus-control resolution owner. Before O3 coding:

1. locate the exact quest signal, outcome worker, or committed game-state mutation;
2. prove which choice/outcome is visible at that point;
3. identify participant/witness facts;
4. verify save/load replay behavior;
5. define ownership against the existing Quest route;
6. add a fail-soft registration path.

Until all six are answered, the existing quest completion page is the only terminal owner and must
not guess the choice.

## 13. Event ownership and dedup matrix

| Gameplay moment | Canonical owner | Lower-level behavior |
|---|---|---|
| Gravship launch ritual | Existing Ritual route | Odyssey policy may add context/cap/cooldown; no destination claim. |
| Destination selected / takeoff initiated | None | Capture transient intent only. |
| Takeoff completed / `TravelTo` | None | Commit saved journey only. |
| World travel ticks | None | No polling page and no progress prose. |
| Landing placement started | None | Snapshot pending landing only. |
| Landing completed | `GravshipJourney` | One novelty-gated event, one or two POVs. |
| Negative landing outcome | Same landing event | O1 coarse flag; O2 exact enrichment; never a second page. |
| `TileSettled` Tale | None in Pawn Diary | Current no-pawn Tale drop remains. |
| Quest/site accepted | Quest route | Separate commitment phase unless an exact duplicate is proven. |
| Arrival at quest/site | Landing route | Describe arrival, not completion or hidden contents. |
| Quest completed/failed | Quest route | Terminal result; no repeated landing page. |
| Vacuum injury | Existing Hediff/enchantment routes | Context only unless existing health policy independently authorizes a page. |
| `GravNausea` | Prompt enchantment | No standalone page. |
| Visible active Odyssey condition | Existing surroundings/observer policy | Prompt shading; page only if separately approved. |
| Seasonal flood | `ThingPresent` observed condition | Persistent tone, no default page. |
| Mechhive choice | Existing Quest route until O3 exact owner | No guessed specialized page. |

Dedup keys use stable journey/event IDs and phase tokens, not translated labels.

## 14. Persistence and save compatibility

### 14.1 Saved active journey

Suggested `OdysseyJourneyState` fields:

- schema version;
- journey ID;
- engine/gravship stable ID and cleaned ship name;
- origin/destination `OdysseyLocationSnapshot`;
- departure tick;
- pilot/copilot IDs and names;
- bounded crew ID/name list;
- launch quality band;
- rough-landing flag;
- source-completeness flags;
- landing-emitted/applied guard.

Use one active journey because vanilla's controller exposes one active gravship lifecycle. If a mod
introduces concurrent travel, fail closed rather than silently cross-correlating; multi-journey
support is a later schema change.

### 14.2 Saved history

Suggested `OdysseyTravelHistoryState` fields:

- schema version;
- initialized and history-trust flags;
- feature-start tick;
- committed journey count;
- visited layer/category/location keys;
- current and former home keys;
- last departure/landing observations;
- last launch page and landing page ticks;
- bounded emitted journey IDs.

Store stable strings/ints and event-time labels. Do not Scribe live Defs, maps, pawns, tiles,
gravships, controllers, or Harmony objects.

### 14.3 Scribe behavior

- Add additive, uniquely named keys in `DiaryGameComponent.ExposeData`.
- Initialize collections before save and normalize them during `PostLoadInit`.
- Clamp corrupt/negative ticks and oversized lists.
- Deduplicate keys case-insensitively where Def/package semantics require it.
- Preserve unknown future fields by using a versioned migration method rather than destructive
  reset.
- Clear transient takeoff/landing/reflection caches on constructor, `StartedNewGame`, and
  `LoadedGame` paths.

### 14.4 Old-save baseline

On the first load with O1:

- Set `historyInitialized=true` and `historyTrustworthyForFirstClaims=false`.
- Seed only the currently visible ship/location/home facts.
- If a gravship is already travelling, create an incomplete active journey baseline from
  `Game.Gravship` without a departure page.
- Allow a later landing to be recorded for major-site, rough-landing, or other event-local reasons.
- Do not authorize `first_orbit` or `new_biome_category` from incomplete pre-feature history.
- Mark history trustworthy for new first-claim comparisons only after a post-feature committed
  journey establishes the relevant baseline.

This avoids false “first-ever” prose while still recording meaningful future events.

## 15. XML, prompts, settings, and localization

### 15.1 `DiaryOdysseyPolicyDef`

XML owns at least:

- activation and stable launch/landing group keys;
- takeoff and landing correlation windows;
- stale active-journey retention;
- launch and landing page cooldowns;
- long-journey tick threshold;
- long-held-home threshold;
- max launch writers and max landing writers;
- primary reason priority/weights;
- cooldown-bypass reasons;
- biome-to-category mappings;
- site/quest-to-category mappings;
- major destination allowlist;
- prompt field/item/character caps;
- history/list retention caps;
- optional compatibility corrections.

Code fallbacks are defensive defaults only. They are not the primary feature policy.

### 15.2 Interaction groups

- Retune `ritualGravship` to departure-only truth.
- Gate it for `Ludeon.RimWorld.Odyssey` so no-DLC settings remain clean.
- Add `odysseyGravshipLanding` in `GroupDomain.GravshipJourney` with exact synthetic landing Def
  matching and Odyssey package gating.
- Give landing its own instruction/tone variants and settings label.
- Keep group order deterministic and place a catch-all only if the new domain genuinely needs one.

### 15.3 Stable prompt schema

Permitted structured fields include:

- `odyssey_journey=true`
- `journey_phase=landing`
- `ship_name=...`
- `origin=...`
- `destination=...`
- `destination_layer=surface|orbit|unknown`
- `destination_biome=...`
- `destination_site=...`
- `journey_reason=...`
- `journey_secondary_reason=...`
- `journey_duration=short|ordinary|long`
- `pilot=...`
- `copilot=...`
- `crew_count=...`
- `pov_journey_role=pilot|copilot|crew`
- `rough_landing=true|false`
- `launch_quality=...`

Schema labels and role/sentinel tokens stay stable English by the documented carve-out. Visible
values and all prose are cleaned, bounded, and localized.

A pair-shaped `DiaryEvent` owns one saved `gameContext`, so it stores bounded internal
initiator/recipient journey-role mappings. Prompt planning removes those internal keys and projects
exactly one public `pov_journey_role` for the requested POV; a shared pair context must never stamp
the pilot's role onto the copilot prompt.

Do not include raw tile IDs, internal Def descriptions, coordinates, hidden site parts, or every
crew name.

### 15.4 Localization

Every shipped slice updates both:

- `Languages/English`
- `Languages/Russian (Русский)`

Use Keyed strings for event labels, fallback text, role prose, settings, warnings shown to the
player, and any main-thread prompt fragments. Use DefInjected strings for group/policy label,
instruction, tone, and variant lists. Keep list indices aligned across languages.

Do not call `Translate()` from a background LLM thread. Snapshot localized player-visible values on
the main thread before queueing.

## 16. File-level change map

### 16.1 Proposed new production files for O1

- `Source/Defs/DiaryOdysseyPolicyDef.cs`
- `Source/Models/OdysseyJourneyState.cs`
- `Source/Pipeline/OdysseyJourneyContracts.cs`
- `Source/Pipeline/OdysseyLocationPolicy.cs`
- `Source/Pipeline/OdysseyLandingPolicy.cs`
- `Source/Pipeline/OdysseyWriterPolicy.cs`
- `Source/Pipeline/OdysseyContextFormatter.cs`
- `Source/Generation/OdysseyJourneyCorrelation.cs`
- `Source/Capture/Events/GravshipJourneyEventData.cs`
- `Source/Capture/Specs/GravshipJourneyEventSpec.cs`
- `Source/Ingestion/Sources/GravshipJourneySignal.cs`
- `Source/Core/DiaryGameComponent.Odyssey.cs`
- `Source/Patches/DiaryOdysseyPatches.cs`
- `tests/DiaryOdysseyPolicyTests/DiaryOdysseyPolicyTests.csproj`
- `tests/DiaryOdysseyPolicyTests/Program.cs`
- `tests/PawnDiary.RimTest/PawnDiaryOdysseyJourneyFlowTests.cs`

If an earlier DLC implementation has already made `DlcContext` partial, use
`Source/Generation/DlcContext.Odyssey.cs`. Otherwise, making it partial is an acceptable small
structural slice documented at implementation time.

### 16.2 Existing production files likely to change in O1

- `Source/Generation/DlcContext.cs` — guarded Odyssey projection or partial declaration.
- `Source/Generation/DiaryContextBuilder.cs` — append bounded mobile-home surroundings.
- `Source/Capture/DiaryEventType.cs` — add concrete `GravshipJourney`.
- `Source/Capture/Catalog/DiaryEventCatalog.cs` — register the new Spec.
- `Source/Defs/InteractionGroups.cs` — add concrete domain and optional bounded ritual fan-out policy.
- `Source/Pipeline/DiaryEventDomainClassifier.cs` — classify the new payload/domain.
- `Source/Patches/DiaryPatchRegistrar.cs` — defensive `LandingEnded` registration.
- `Source/Core/DiaryGameComponent.cs` — Scribe and lifecycle normalization/reset hooks.
- `Source/Ingestion/Sources/RitualSignal.cs` — exact launch context/cap/cooldown adapter only.
- `1.6/Defs/DiaryInteractionGroupDefs.xml` — launch correction and landing group.
- a new `1.6/Defs/DiaryOdysseyPolicyDefs.xml` — thresholds and mappings.
- English/Russian Keyed and DefInjected localization.
- `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, `TEST_COVERAGE_PLAN.md`, `CHANGELOG.md`.
- `1.6/Assemblies/PawnDiary.dll` after every C# slice.

### 16.3 O2/O3 files

O2 should prefer edits to existing XML:

- `1.6/Defs/DiaryObservedConditionDefs.xml`
- `1.6/Defs/DiaryPromptEnchantmentDefs.xml`
- their English/Russian DefInjected mirrors
- Odyssey policy XML

Add outcome or life-support C# files only after their feasibility gates pass. O3 should first extend
quest/site mappings and existing quest context; add a specialized patch only when the exact
Mechhive owner is verified.

## 17. Phased implementation sequence

Each phase is a separate smallest-safe change. Every behavior/structure phase updates affected docs
and `CHANGELOG.md`, runs relevant tests, builds Debug, and stages the rebuilt DLL when C# changed.

### Phase O1.0 — Freeze contracts and re-check signatures

> **Implementation status (2026-07-17): complete; documentation/contracts only.** Rechecked the
> installed RimWorld 1.6.4871 assembly and Odyssey Defs, corrected the launch-info visibility note,
> and froze the O1 names, save keys, arc grammar, schema tokens, ownership, and old-save behavior
> below. No C#, XML Def, save field, settings row, Harmony patch, or page behavior was added.

1. Re-read current code through CodeGraph.
2. Re-check exact installed method signatures and private field names.
3. Confirm the package gate and no-DLC settings behavior.
4. Confirm Narrative Continuity N0 tokens and freeze saved keys, event/group names, journey arc-key
   grammar, reason tokens, and old-save semantics.
5. Add no runtime behavior.

Exit: reviewers agree on event ownership, contracts, and saved tokens.

#### O1.0 frozen contract registry

These spellings are schema/save compatibility. Later phases may add fields or tokens, but must not
rename or reinterpret these once production state exists.

**DLC identity and policy names**

- package ID: `Ludeon.RimWorld.Odyssey`;
- live gate: `ModsConfig.OdysseyActive`;
- policy Def name: `Diary_Odyssey`;
- existing launch group: `ritualGravship`;
- new landing group: `odysseyGravshipLanding`;
- new group domain / event type: `GravshipJourney`;
- synthetic landing event Def name: `OdysseyGravshipLanding`.

The new landing group must use `enableWhenPackageIdsLoaded=Ludeon.RimWorld.Odyssey`. The existing
`ritualGravship` group is not package-gated today; O1.4 owns adding that gate together with its
departure-only wording so a base-only settings screen becomes clean in the same behavioral slice.

**Additive component save keys**

- `odysseyActiveJourney` — nullable `OdysseyJourneyState` for the one committed trip;
- `odysseyTravelHistory` — non-null `OdysseyTravelHistoryState` containing initialization/trust,
  bounded novelty history, homes, timestamps, and emitted-journey IDs.

Each saved model owns its internal `schemaVersion`; O1 does not add a third component-level version
key. Missing keys mean a pre-feature save and enter the silent baseline described below.

**Identity and dedup grammar**

- journey ID and shared Narrative Continuity arc key:
  `odyssey-journey|<shipStableId>|<departureTick>`;
- landing ownership/dedup key:
  `odyssey-landing|<shipStableId>|<departureTick>`.

`shipStableId` prefers the travelling `Gravship` load ID and falls back to the engine load ID only
when the world object is unavailable. It is trimmed and rejected if blank or if it contains `|`.
`departureTick` is the non-negative `TravelTo` commit tick. The journey ID already is the arc key;
never wrap it in a second `odyssey-journey|...` prefix.

**Stable structured tokens**

- narrative phases: `departed`, `arrived`, `returned`;
- landing reasons: `first_orbit`, `new_biome_category`, `major_destination`, `homecoming`,
  `long_journey`, `rough_landing`;
- POV roles: `pilot`, `copilot`, `crew`;
- destination layers: `surface`, `orbit`, `unknown`;
- duration bands: `short`, `ordinary`, `long`;
- launch-quality bands: `poor`, `ordinary`, `excellent`, `unknown`.

Reason priority, thresholds, mappings, and quality/duration cutoffs remain XML policy in O1.1/O1.2;
only the token spellings are frozen here. The prompt schema keys in §15.3 are likewise frozen.

**Installed 1.6.4871 signatures reconfirmed**

- `RitualOutcomeEffectWorker_GravshipLaunch.Apply(float progress, Dictionary<Pawn,int>
  totalPresence, LordJob_Ritual jobRitual)`;
- `Verse.WorldComponent_GravshipController.InitiateTakeoff(Building_GravEngine engine,
  PlanetTile targetTile)`;
- `RimWorld.GravshipUtility.TravelTo(Gravship gravship, PlanetTile oldTile,
  PlanetTile newTile)`;
- `Verse.WorldComponent_GravshipController.InitiateLanding(Gravship gravship, Map map,
  IntVec3 landingPos, Rot4 landingRot)`;
- private instance `Verse.WorldComponent_GravshipController.LandingEnded()` with zero parameters;
- `LandingOutcomeWorker.ApplyOutcome(Gravship gravship)` remains public virtual and all four shipped
  outcome workers override it;
- `Gravship.TickInterval(int delta)` remains protected virtual and is not an O1 patch target.

Reconfirmed supporting state: `WorldComponent_GravshipController.gravship` is private,
`landingMap` is public, `Gravship.destinationTile` is public, `Gravship.Engine`, `Pawns`, and `Label`
are public getters, `Game.Gravship` is a public property, and the launch-info fields listed in §6.2
are public in this build. `LandingEnded` still requires defensive manual registration and one warning
on lookup failure.

**Frozen old-save semantics**

1. Missing Odyssey keys initialize history without emitting a departure, landing, or catch-up page.
2. The first baseline sets `historyInitialized=true` and
   `historyTrustworthyForFirstClaims=false`, then stores only currently visible ship/location/home
   facts.
3. A ship already travelling becomes an incomplete active-journey baseline. Its later landing may
   qualify only from exact event-local reasons such as a mapped major destination or verified rough
   landing—not `first_orbit`, `new_biome_category`, or invented departure history.
4. History becomes trustworthy for future first/new comparisons only after one post-feature
   `TravelTo` commit establishes the relevant baseline.
5. A missing/invalid hook, ship ID, or destination fails open to vanilla and creates no partial state
   or page.

**Frozen page ownership**

- launch ritual: existing Ritual route only;
- takeoff intent, `TravelTo`, and landing start: state only, no page;
- successful `LandingEnded`: the sole `GravshipJourney` landing owner;
- landing outcome: context on that landing, never a second page;
- `TileSettled`: no Pawn Diary owner;
- quest acceptance/completion/failure: existing Quest route.

O1.0–O1.1 may proceed beside Narrative Continuity N0. Narrative Continuity N1 must land before O1.2
adds persistence/prompt context, and the Odyssey provider/emissions join Narrative Continuity N2 with
Biotech B1 where practical.

### Phase O1.1 — Pure policy and tests

> **Implementation status (2026-07-17): complete; pure policy only.** Added detached contracts and
> exact location classification, deterministic two-writer selection, launch-quality/duration bands,
> landing reason/cooldown planning, bounded idempotent history mutation, silent old-save baselining,
> and prompt-safe bounded context. `DiaryOdysseyPolicyTests` passes 88 assertions without
> RimWorld/Verse/Unity references. No Def, save field, settings row, Harmony patch, or page behavior
> was added; O1.2 is next.

1. Add plain contracts.
2. Add XML snapshot/fallback projection for tests.
3. Implement location classification, novelty, cooldown, history mutation, and writer selection.
4. Implement bounded context formatting.
5. Add `DiaryOdysseyPolicyTests` with no Verse references.

Exit: all policy edge cases pass without RimWorld.

### Phase O1.2 — Guarded context and persistence

> **Implementation status (2026-07-17): complete; state/context only.** Added the XML-owned
> `Diary_Odyssey` policy and exact biome/site mappings, guarded map/gravship projection behind
> `ModsConfig.OdysseyActive`, detached active-journey/history Scribe models under the two frozen keys,
> bounded post-load repair, silent untrusted old-save baselining (including incomplete in-flight
> recovery), localized exact-pawn mobile-home surroundings, and focused RimTests. No takeoff,
> `TravelTo`, landing, Harmony, catalog, settings, or page route was added; O1.3 is next.

1. Add `DiaryOdysseyPolicyDef` and XML defaults.
2. Add `OdysseyJourneyState`/history Scribe models.
3. Extend `DiaryGameComponent` Scribe, normalization, lifecycle reset, and old-save baseline.
4. Add guarded `DlcContext` location capture.
5. Append mobile-home surroundings.
6. Build and add focused context/persistence RimTests.

Exit: save/load and no-DLC behavior work before any new page source exists.

### Phase O1.3 — State-only lifecycle hooks

> **Implementation status (2026-07-17): complete; state/history only.** Added guarded exact-signature
> prefixes/postfixes for `InitiateTakeoff`, `GravshipUtility.TravelTo`, and `InitiateLanding`, plus a
> defensive manual private `LandingEnded` registration. Transient intent/pending rows never scribe;
> `TravelTo` alone commits the active journey/departure history, replay is journey-ID idempotent, and
> successful finish applies landing history only after vanilla returns. `landingPageEnabled=false`
> remains XML-owned and the component has no event sink, so no O1.3 seam can create or consume a page.
> Pure lifecycle coverage now passes 112 assertions and focused RimTests pin hook registration/state flow.

1. Add `InitiateTakeoff` intent capture.
2. Add `TravelTo` commit.
3. Add `InitiateLanding` pending capture.
4. Register private `LandingEnded` defensively, initially applying history with page emission
   disabled in XML.
5. Add diagnostics and flow tests.

Exit: real travel creates correct state, routine play creates no new page, and missing hooks fail
soft.

### Narrative N2-O — Journey/home provider and references

> **Implementation status (2026-07-17): complete; provider/reference seam only.** Added one pure
> Odyssey home provider, exact-POV guarded event-time snapshots, DefInjected English/Russian factual
> prose, and pure departure/landing evidence factories with the frozen journey arc and ship/place
> references. Existing Biotech growth/birth pages may select the home lens only when vanilla proves
> their writer is aboard that exact gravship; matching landing evidence receives exact-arc relevance.
> Inactive/unknown/disconnected paths are silent. Shared pure coverage passes 104 assertions and loaded
> RimTests cover the fixed provider list plus exact-onboard adapter boundary. No Odyssey event, save
> field, settings row, or page owner was added in N2-O itself; completed O1.4 now consumes its landing
> evidence factory from the canonical event owner.

Exit: the exact family-on-gravship combination can select one frozen home lens, departure/arrival
references are ready for source-owned pages, and every absent/unverified path remains unchanged.

### Phase O1.4 — Landing event and launch truth

> **Implementation status (2026-07-17): complete.** Added the concrete event catalog route and exact
> package-gated landing group, enabled novelty-gated `LandingEnded` emission, committed page history
> only after durable event creation, attached source-owned arrival evidence, corrected launch prose,
> and applied the Odyssey-only launch cap/prior-departure cooldown. Pure capture/Odyssey/pipeline
> suites pass; the new loaded two-POV/transaction fixture compiles and will run with the combined O1
> in-game acceptance pass.

1. Add concrete catalog type, payload, Spec, signal, and domain.
2. Add landing group, prompt variants, role text, and settings.
3. Enable novelty-gated page emission.
4. Correct launch wording and add its bounded Odyssey policy.
5. Verify one event/two-POV maximum and no takeoff duplicate.

Exit: O1 acceptance scenarios pass in RimTest and a manual Odyssey save.

### Phase O1.5 — Hardening and delivery

> **Implementation status (2026-07-17): automated hardening complete; combined manual acceptance
> deferred.** Added append-only English/Russian important-template fields for the bounded landing
> schema, made phase/reason/duration/role/ship/origin/destination mandatory under Full, Balanced, and
> Compact, registered a localized pair prompt fixture, and added pure/loaded regressions for the
> no-pawn `TileSettled` Tale and an eligible-writer routine landing. Existing focused fixtures already
> cover intent-only cancellation, idempotent travel, writerless major landing, mid-flight Scribe,
> old-save distrust, major-site pair ownership, and no-DLC guarded reads. All automated validation is
> run in this slice; the §18.5 save scenarios remain the single deferred in-game pass.

1. Run old-save, mid-flight save/load, cancellation, routine-hop, major-site, and no-writer cases.
2. Verify `TileSettled` does not duplicate.
3. Run XML/localization validation, all pure projects, focused RimTests, Debug MSBuild, and the
   repository verification hook.
4. Review prompt fixtures in full/balanced/compact modes.
5. Update all docs and changelog, rebuild/stage `PawnDiary.dll`.

### Phase O2 — Environment and exact consequence

Ship seasonal flood and `GravNausea` as an XML-first change. Run the landing-outcome and
life-support spikes separately; implement only the parts whose gates pass.

### Phase O3 — Exploration and ending

Extend visible site categories first. Verify Mechhive resolution before adding specialized code,
then ship terminal ownership and arc reflection as separate slices.

## 18. Test plan

### 18.1 Pure policy tests

At minimum:

- null/invalid snapshots drop safely;
- first orbit requires trustworthy history;
- old-save baseline suppresses false first claims;
- new biome category emits once;
- repeated biome/category stays silent;
- major destination outranks ordinary novelty;
- homecoming requires a tracked former home and intervening travel;
- long journey uses XML threshold boundaries;
- rough landing can be primary or secondary according to XML;
- ordinary cooldown drops but still updates history;
- bypass reasons behave exactly as configured;
- disabled group still applies history mutation;
- duplicate journey ID is idempotent;
- primary/secondary reason count is capped;
- pilot/copilot selection order;
- missing/ineligible pilot falls back to crew;
- stable ID tie-break is deterministic;
- no eligible writer drops page but updates history;
- unknown/modded Def remains descriptive but not automatically novel;
- hidden destination facts never format;
- prompt caps and cleaning;
- no raw tile/engine mechanics appear.

### 18.2 Event catalog tests

- Every `DiaryEventType` remains registered.
- `GravshipJourneyEventData.Decide` respects eligibility, group setting, signal gates, and plan.
- Solo versus pair shape follows selected writer count.
- Event/group domain classification survives save-recovery by synthetic Def name.
- Existing types retain their decisions.

### 18.3 Focused RimTests

- Odyssey inactive: every hook returns without state or event changes.
- Launch ritual instruction is departure-only.
- Launch fan-out cap does not affect other rituals.
- `InitiateTakeoff` produces intent only.
- `TravelTo` commits once and produces no event.
- Missing intent produces a conservative fallback journey.
- `InitiateLanding` produces no event.
- successful landing applies state and emits one event when authorized;
- routine landing applies state and emits none;
- event holds one/two selected POVs, never colony fan-out;
- private hook registration failure logs once and does not suppress vanilla;
- save/load mid-flight preserves journey;
- old-save mid-flight baseline omits false first claims;
- `TileSettled` no-pawn Tale remains dropped;
- mobile-home context appears only for the correct ship/map/pawn;
- full/balanced/compact prompts retain required journey facts;
- English/Russian DefInjected lists stay aligned.

### 18.4 O2/O3 tests

- `SeasonalFlood` starts/refreshes/ends one observed state with no default page.
- visible `GameCondition` context is not duplicated.
- `GravNausea` respects enchantment chance/severity policy.
- every dynamically discovered landing-outcome override reports exact applied outcome once.
- Mechhive choice tests remain absent until the source is verified; once added, both outcomes and
  load replay require fixtures.

### 18.5 Manual acceptance run

Use a small Odyssey save to exercise:

1. launch ceremony, cancel destination;
2. launch again and actually take off;
3. first orbit;
4. land in a mapped new biome category;
5. repeat the same category;
6. land at a configured major site;
7. return to a tracked former home;
8. save during travel and load before landing;
9. land with pilot ineligible and crew fallback;
10. disable/re-enable the landing group between trips;
11. load the same mod without Odyssey active.

Record event counts, selected writers, state transitions, prompts, and logs.

## 19. Acceptance scenarios

### Scenario A — first meaningful landing

The colony completes a launch ritual, selects an orbital destination, travels, and lands.

Expected:

- optional departure ceremony only from the Ritual route;
- no page at takeoff or `TravelTo`;
- one landing event if `first_orbit` is trustworthy;
- at most pilot plus copilot/crew POV;
- ship/destination facts are event-time snapshots.

### Scenario B — routine shuttle hop

The same crew repeats a short trip within an already visited category and cooldown.

Expected:

- travel/history update;
- no new landing page;
- no false “first” fact later.

### Scenario C — major site

The crew lands at an XML-classified visible gravcore or orbital site.

Expected:

- landing may use `major_destination`;
- it describes arrival only;
- quest acceptance/completion remains independently owned;
- no hidden threat or reward is revealed.

### Scenario D — old save mid-flight

O1 is installed while `Game.Gravship` is already travelling.

Expected:

- silent incomplete journey baseline;
- no catch-up departure;
- landing may record event-local major/rough facts;
- no `first_orbit` or `new_biome_category` claim from missing history.

### Scenario E — missing private hook

A later RimWorld version renames `LandingEnded`.

Expected:

- one clear warning;
- launch and all non-Odyssey diary behavior remain;
- no landing page or history application from an unconfirmed finish;
- vanilla landing is untouched.

### Scenario F — no DLC

Odyssey is inactive.

Expected:

- no hook body reaches DLC state;
- no Odyssey groups/settings or empty prompt lines;
- no load errors from XML;
- base-game behavior and tests are unchanged.

## 20. Performance, DLC safety, and failure handling

### 20.1 Performance

- Takeoff/travel/landing hooks are lifecycle events, not tick hooks.
- Journey history uses bounded sets/lists and O(1) exact-key checks.
- Writer selection inspects only the current gravship's bounded pawn list.
- Mobile-home context uses public map/gravship helpers and may cache per map/tick only if measured.
- Seasonal flood uses the existing observed-condition cadence.
- No full-world, all-map, all-Thing, or all-quest scan occurs per tick or per prompt.
- Do not consume global simulation `Rand` for cosmetic or deterministic policy.

### 20.2 DLC safety

- Gate every live adapter with `ModsConfig.OdysseyActive` plus null checks.
- Do not add an Odyssey mod dependency.
- Prefer plain exact strings in XML; use `MayRequire` where the custom Def itself should disappear
  without the DLC.
- Never call throwing `DefDatabase<T>.GetNamed` for Odyssey content.
- A referenced Odyssey C# type compiling successfully is not proof of runtime availability.
- No-DLC settings and prompts remain clean.

### 20.3 Patch safety

- Public stable methods may use exact Harmony attributes after signature re-check.
- Private `LandingEnded` must use `DiaryPatchRegistrar`.
- Optional private launch-info fields use cached `AccessTools` handles and degrade field-by-field.
- Every patch body runs through `DiaryPatchSafety`.
- Failure to capture Odyssey facts never blocks or changes vanilla takeoff/landing.
- Log hook-registration failure once, not per trip or tick.

### 20.4 Thread and text safety

- All live reads and localization occur on RimWorld's main thread.
- Pure policy receives sanitized primitive facts.
- LLM background work receives saved prompt strings only.
- Player-authored ship names and modded labels pass through existing cleaning and length caps.

## 21. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Private landing method changes | Exact manual lookup, one warning, no patch/no page fallback. |
| Launch ritual precedes destination selection | Departure-only wording; destination appears only after committed travel/landing. |
| Old saves look artificially novel | Trust flag and silent baseline; no first claims from incomplete history. |
| Routine travel creates page spam | Pure novelty gate, cooldown, visited categories, one-event cap. |
| Whole crew creates fan-out spam | Deterministic max-two writer plan. |
| Site labels reveal hidden content | Exact visible-category allowlist and knowledge flags; unknown stays unknown. |
| Landing outcome guessed from probability | O1 coarse flag only; O2 captures actual override or omits. |
| Quest and landing duplicate | Explicit phase ownership and correlation tests. |
| Modded concurrent gravships cross-link | Vanilla single-trip state, identity check, fail closed, later schema if proven needed. |
| Mobile context becomes a mechanics dump | Pure bounded formatter with explicit excluded fields. |
| Environmental features duplicate generic context | Audit `BuildActiveMapConditionsSummary` first; XML-first flood exception only. |
| Life-support observer is hot/noisy | Separate feasibility gate, hysteresis, measured cadence, defer if unsafe. |
| Translation is called off-thread | Snapshot all localized text before queueing. |
| Planned shared DLC types do not exist yet | O1 defines concrete Odyssey contracts and optional integration seams only. |

## 22. Deferred follow-ons

- Signature unique-weapon memories after the Royalty bond lifecycle actually exists, without calling
  ordinary Odyssey weapons sentient.
- Sentience-catalyst identity transition with owner/animal relationship facts.
- Alpha thrumbo and hive-queen quest-specific framing.
- Fishing as occasional work texture or reflection, not event spam.
- Multi-gravship state only if vanilla or a supported mod proves concurrent journeys.
- Cross-DLC belief interpretation through the future Ideology resolver.
- Cross-DLC family/child perspective on leaving home through the future Biotech plan.
- A shared long-arc reflection scheduler only after at least two concrete DLC arcs need the same
  proven contract.

## 23. Coding-agent handoff checklist

Before each Odyssey implementation slice:

- [ ] Re-read `AGENTS.md` and `skills/pawndiary-engineering/SKILL.md`.
- [ ] Use CodeGraph before grep/file reading for code discovery.
- [ ] Inspect the dirty worktree and preserve unrelated/user changes.
- [ ] Re-check the exact installed RimWorld 1.6 target signatures.
- [ ] State the slice's event owner and duplicate losers.
- [ ] Keep live Odyssey reads in guarded impure adapters.
- [ ] Keep policy and saved data free of Verse/Unity/Harmony/live objects.
- [ ] Put thresholds, lists, caps, odds, instructions, and tones in XML.
- [ ] Add/extend pure tests before or with behavior.
- [ ] Add focused RimTests for the live adapter and no-DLC path.
- [ ] Baseline old saves without catch-up pages or false first claims.
- [ ] Localize English and Russian Keyed/DefInjected text on the main thread.
- [ ] Update `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, `TEST_COVERAGE_PLAN.md`, and `CHANGELOG.md`
      for shipped behavior.
- [ ] Run XML/localization checks, all relevant pure suites, focused RimTests, and Debug MSBuild.
- [ ] Verify and stage the rebuilt `1.6/Assemblies/PawnDiary.dll` after C# changes.
- [ ] Manually verify launch → travel → landing, routine hop, save/load, hook failure, and no-DLC
      scenarios.

The first coding slice should be O1.1: plain contracts, pure novelty/history/writer policy, and its
standalone tests. Do not begin with Harmony patches.
