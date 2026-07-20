# Pawn Diary — Anomaly Support Implementation Plan

Status: A0 implemented as Master Wave 2, 2026-07-15; A1.0–A1.4 and A2.0–A2.1 implemented as Master Wave 7,
2026-07-20; A2.2–A3 remain implementation-ready plans. The
RimWorld 1.6 feasibility spike is complete for study-note milestones, containment escapes, visible
creepjoiner outcomes, ghoul infusion, and both terminal void choices. Production now includes the A0
exact psychic-ritual routing and monolith activation chapters described below.

Scheduling authority: implement Anomaly phases only in the waves assigned by
`DLC_SUPPORT_MASTER_IMPLEMENTATION_PLAN.md`; this file remains the technical authority for Anomaly.

Anomaly was the final standalone DLC planning artifact. Its small A0 semantic-precision release has
now shipped independently; A1 study capture, containment breach capture, and A1 hardening/delivery
and visible creepjoiner state/outcomes including surgical disclosure are implemented, while A2.2–A3 remain scheduled in the master plan.

The plan turns Anomaly's already broad atmospheric coverage into a coherent human story:
curiosity becomes knowledge, knowledge enables a choice, containment can fail, apparently human
arrivals may reveal themselves, bodily identity can be surrendered, and the void eventually
receives an explicit answer. It reuses Pawn Diary's current ritual, event-window, Tale, event
catalog, XML policy, generation, and persistence paths instead of building a parallel DLC system.

Cross-DLC enrichment follows `DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md`. A0's ritual split is
XML-only; its exact monolith windows reuse the existing hook and attach only visible N1 chapter
evidence. A1–A3 source pages must use the same shared evidence/reference contract. The shared layer
never weakens Anomaly's spoiler firewall.

Implementation must follow `AGENTS.md` and `skills/pawndiary-engineering/SKILL.md`: collect live
RimWorld state only in DLC-gated impure adapters, copy it into plain facts, keep decisions pure and
tested, put tuning and prompt policy in XML, document and build every behavior slice, and cleanly
no-op when Anomaly is inactive.

## 1. Review outcome and recommended release boundary

### 1.1 What the repository review found

The workspace contains the cross-DLC atmosphere roadmap plus standalone Ideology, Royalty,
Biotech, and Odyssey plans. Git history and the current source tree show that these artifacts are
plans, not implementations of the state machines and event types they describe. Pawn Diary does
already ship substantial DLC-aware behavior through guarded context reads, generic capture hooks,
and XML string matching; those existing features must not be confused with completion of the newer
standalone plans.

| Artifact or runtime area | Reviewed state |
|---|---|
| `design/DLC_ATMOSPHERE_RESEARCH.md` | Current cross-DLC roadmap. Anomaly's fantasy is curiosity → knowledge → temptation → irreversible cost. |
| `IDEOLOGY_SUPPORT_IMPLEMENTATION_PLAN.md` | Design plan only; its belief resolver and reflection route are not production code. |
| `ROYALTY_SUPPORT_IMPLEMENTATION_PLAN.md` | Scope-review plan only; its persona/succession state is not production code. |
| `BIOTECH_SUPPORT_IMPLEMENTATION_PLAN.md` | Implementation plan only; its growth/family/mechanitor state is not production code. |
| `ODYSSEY_SUPPORT_IMPLEMENTATION_PLAN.md` | Scope-review plan only; its saved journey and landing chapters are not production code. |
| Current Anomaly ritual support | Successful psychic rituals fan out through `DiaryEventType.Ritual`, with exact ritual defName, perspective, quality, and exact-family Anomaly guidance plus a modded fallback. |
| Current Anomaly atmosphere | Monolith discovery/activation, visible and hidden metalhorror pressure, pit gates, fleshmass, nociosphere, obelisks, unnatural corpses, major conditions, cube effects, void hediffs, thoughts, raids, and Tales already have useful generic coverage. |
| Main Anomaly gap | Exact ritual meaning, researcher-owned study milestones, containment consequence, and visible creepjoiner outcomes including surgical disclosure are connected; ghoul identity and exact terminal void chapters remain. |

This plan starts from that production baseline. A coding agent must not assume that a cross-DLC
facet, event type, state model, or helper proposed by another plan already exists.

### 1.2 Recommended release sequence

Anomaly should ship as four independently reviewable increments.

#### A0 — Semantic precision with existing event routes

A0 is the smallest, safest slice and may ship independently of the deeper program:

- Split the generic psychic-ritual prompt group into six exact semantic families while keeping the
  existing generic group as the future/modded fallback.
- Match the full classifier keys already emitted by production code, such as
  `PsychicRitual;VoidProvocation`, rather than bare labels or translated text.
- Keep page count, participants, role instructions, success rules, and generation flow unchanged.
- Split the existing generic monolith activation event window into truthful Stirring, Waking, and
  Void Awakened chapters using the reached `MonolithLevelDef.defName` already supplied by the patch.
- Keep monolith discovery separate and do not invent a page for the automatic Gleaming state.
- Add XML classification tests, DefInjected translations, keyed fallback text for the new monolith
  windows, documentation, changelog, XML validation, and the normal build verification.

The ritual half of A0 is XML-only. The monolith half reuses the current
`Building_VoidMonolith.Activate(Pawn)` hook and event-window engine, so it also needs no new C# hook.

#### A1 — Knowledge and containment consequence

A1 is the first flagship Anomaly release:

- Observe actual study-note thresholds after `CompStudyUnlocks.OnStudied` changes `Progress`.
- Make the exact studier the author of rare knowledge milestones.
- Record only first-ever breakthrough, completed study of a distinct entity kind, and explicitly
  XML-promoted milestones; ordinary study ticks and intermediate notes remain state-only.
- Keep monolith study notes state-only by default and use them to enrich the next exact monolith
  activation, especially when study makes the monolith activatable.
- Aggregate one `CompHoldingPlatformTarget.Escape(bool)` call tree into one containment-breach
  moment, including same-room joiners.
- Distinguish involuntary escape from capture, intentional ejection, release, death, and ordinary
  holding-platform work.
- Select at most two deterministic, eligible witnesses; never imply that a witness caused the
  breach unless an exact future source supplies that fact.
- Add one DLC-specific catalog route, pure policies, bounded transient caches, saved milestone
  history, event ownership, deduplication, tests, localization, docs, changelog, and the rebuilt DLL.

#### A2 — Visible reveal and irreversible identity

A2 adds two independent, spoiler-sensitive features:

- Open a saved creepjoiner arc from the existing arrival event without creating another acceptance
  page.
- Record only outcomes already made visible to the player: successful surgical disclosure,
  rejection, aggression/transformation, or departure.
- Do not create a general `DoDownside` page in A2. Existing thought/hediff/incident routes may cover
  visible effects, while hidden effects—especially a metalhorror implant—remain secret.
- Capture successful `Recipe_GhoulInfusion.ApplyOnPawn` as one irreversible identity transition,
  with the subject and surgeon only when their roles are exact.
- Suppress the correlated generic `DidSurgery` Tale only while the exact Anomaly source owns and
  successfully emits the replacement event; otherwise the current Tale remains the fallback.
- Never record routine ghoul infusion upgrades, resurrection, hunger, combat, or body-part churn as
  new identity chapters.

The creepjoiner and ghoul pieces may merge separately after their own acceptance tests pass.

#### A3 — The exact answer to the void

- Patch the public terminal methods `VoidAwakeningUtility.EmbraceTheVoid(Pawn)` and
  `VoidAwakeningUtility.DisruptTheLink(Pawn)`, not the private delayed dialog callback.
- Emit only after the chosen method returns normally and the expected terminal monolith level is
  visible.
- Record one exact outcome—`embraced` or `disrupted`—for the choosing pawn.
- Suppress the matching `EmbracedTheVoid` or `ClosedTheVoid` Tale only inside that successful
  correlation; if the dedicated hook is unavailable, the existing Tale remains the fail-open path.
- Optionally request one rare arc reflection after the canonical outcome page records; the
  reflection must not restate the event or become a second ending page.

A0, A1, and A2.0–A2.1 are implemented. A2.2 remains implementation-ready for
the ghoul recipe; general creepjoiner-downside classification stays deferred. A3 remains
implementation-ready for both terminal choices.

## 2. Product outcome

After the full Anomaly plan is implemented:

1. A psychic ritual about stealing age or skill no longer receives the same prompt guidance as
   blood rain, abduction, or death refusal.
2. A target, invoker, participant, and spectator still write from their actual captured
   perspectives. Prompt prose never grants a downstream effect that the completion hook did not
   verify.
3. Discovering the monolith, stirring it, waking it, and awakening the void read as distinct
   chapters rather than repeated versions of one generic activation.
4. Study produces a few researcher-owned breakthroughs, not a page per work tick or per minor
   progress increment.
5. Repeatedly studying another instance of an already completed entity kind does not manufacture a
   new “first understanding” claim.
6. A containment breach becomes one event even when several entities escape together. Intentional
   release is not mislabeled as failure.
7. The breach page names only visible escaped entities and witnesses. It does not expose hidden
   containment rolls, unexplored abilities, or an invented person at fault.
8. A creepjoiner's arrival and later visible outcome form one continuity arc without storing or
   prompting on an unrevealed downside.
9. Surgical inspection can record what was actually disclosed and who performed the inspection;
   failed surgery and “nothing found” do not claim a hidden truth.
10. Ghoul infusion is remembered as an irreversible choice once. The generic surgery Tale does not
    compete with it.
11. Embracing the void and disrupting the link produce different, exact terminal chapters from the
    pawn who made the choice.
12. Existing metalhorror, pit-gate, fleshmass, nociosphere, obelisk, cube, void-state, raid,
    condition, thought, hediff, and Tale coverage remains intact unless one row is explicitly owned
    by a new canonical event.
13. Optional Ideology, Royalty, Biotech, or Odyssey enrichment may later consume plain Anomaly
    facets, but no Anomaly release depends on those standalone plans being implemented.
14. With Anomaly inactive, base-game players see no errors, empty Anomaly prompt fields, settings
    clutter from the new groups, changed eligibility, or DLC dependency.

## 3. Scope and non-goals

### 3.1 Required A0 scope

- Six exact psychic-ritual families before the current generic fallback.
- Exact classification of all 16 installed RimWorld 1.6 psychic ritual defs.
- Three monolith activation meanings using the current event-window source.
- No increase in ritual or activation event count.
- Package-gated settings visibility for the new Anomaly groups.
- Classification, fallback, localization, XML, documentation, and build verification.

### 3.2 Required A1 scope

- One plain Anomaly event payload and catalog spec for new exact Anomaly moments.
- Pure study salience, breach aggregation, witness selection, and dedup policies.
- Exact study threshold capture with an eligible studier author.
- Saved first/completed study history and silent old-save baselining.
- State-only monolith study enrichment for later activation.
- One aggregated involuntary containment-breach event.
- Bounded transient Tale suppression for a qualifying `StudiedEntity` duplicate.
- XML-owned thresholds, writer caps, radii, cooldowns, instructions, tones, and colors.
- Standalone pure tests, focused RimTests, docs, changelog, and build.

### 3.3 A2 scope

- Saved creepjoiner arc identity and last visible phase.
- Existing arrival as the arc opener.
- Successful surgical disclosure.
- Exact rejection, aggression/transformation, and departure outcomes.
- Successful ghoul infusion as one identity transition.
- Event-time snapshots and correlated `DidSurgery` ownership.

### 3.4 A3 scope

- Exact `embraced` and `disrupted` terminal outcomes.
- Successful-return verification and fallback Tale ownership.
- One actor POV and optional post-event arc-reflection request.
- Terminal-state persistence sufficient to prevent replay or contradictory endings.

### 3.5 Explicit non-goals

The first program does **not** include:

- a page for every entity discovery, study note, study job, research point, or codex entry;
- patching `EntityCodex.SetDiscovered` as a researcher-owned source—it has no studier parameter;
- a page for capturing an entity, placing it on a platform, changing containment mode, extracting
  bioferrite, or intentionally releasing it;
- a general containment-strength simulator or numeric mechanics dump;
- revealing creepjoiner benefit, downside, aggression, rejection, or metalhorror state before the
  game visibly discloses it;
- a generic `Pawn_CreepJoinerTracker.DoDownside` page in A2;
- a second creepjoiner acceptance page beside the existing arrival event;
- a page for every ghoul enhancement, resurrection, injury, hunger cycle, or kill;
- directly patching private `CompVoidNode.CloseNode` or guessing a dialog choice before it resolves;
- a separate page for the automatic Gleaming monolith level;
- replacing the existing observed-condition system for metalhorrors, pit gates, fleshmass,
  nociospheres, obelisks, unnatural corpses, or weather-like Anomaly conditions;
- an Anomaly dependency in `About/About.xml`;
- English parsing to infer event identity, hidden state, morality, or causality;
- implementing another DLC plan's proposed generic facets as a prerequisite.

## 4. Definition of done

### 4.1 A0 is complete only when

- Every installed 1.6 psychic ritual classifier key maps to exactly one intended exact family.
- A future/modded `PsychicRitual;SomethingUnknown` still maps to `ritualAnomalyPsychic`.
- Exact groups appear before order `776`, use `matchDefNames`, and do not use translated labels.
- Each family instruction respects the supplied perspective and forbids unverified downstream
  results.
- `VoidMonolithActivation` retains stable ownership of the first Stirring transition, while new
  Waking and Void Awakened windows match only their exact reached levels.
- One activation signal matches one activation window; the old generic rule cannot also fire.
- No new C# hook is needed for A0.
- English DefInjected and Keyed entries are complete, with no blank indexed translation slots.
- Classification tests, XML parsing, documentation, changelog, and Debug build all pass.

### 4.2 A1 is complete only when

- `CompStudyUnlocks.OnStudied` compares before/after progress and never emits without an actual
  threshold transition.
- Intermediate study updates are state-only unless XML explicitly promotes them.
- The exact eligible studier owns a knowledge page; no substitute is invented when the studier is
  ineligible.
- First-ever and completed-kind history updates even when generation is disabled or no page can be
  written, so later setting changes cannot manufacture false firsts.
- A progress jump crossing multiple notes produces at most one milestone event.
- Monolith study facts enrich a later activation without producing a default standalone note page.
- One outer containment escape call produces at most one logical breach and at most the configured
  writer cap, including nested same-room escape joins.
- `Building_HoldingPlatform.EjectContents`, `Notify_ReleasedFromPlatform`, capture, death, and load
  reconstruction do not create breach pages.
- New canonical events suppress only their exact generic duplicates and only inside a bounded,
  matching correlation.
- The new event type is registered in the catalog and has pure decision tests.
- All caches are bounded and reset on new game/load; all saved collections normalize nulls and bad
  records.
- No-DLC, missing-hook, old-save, save/load, disabled-setting, and repeated-event tests pass.
- Documentation, changelog, localization, XML, pure tests, focused RimTests, core build, and committed
  `1.6/Assemblies/PawnDiary.dll` are complete.

### 4.3 A2 is complete only when

- Existing arrival opens or updates the creepjoiner arc without a second page.
- Saved creepjoiner records contain no unrevealed downside/benefit identity.
- A surgical page requires successful surgery plus an actual visible disclosure result.
- “Nothing found”, surgery failure, hidden metalhorror implantation, and silent downside execution
  cannot reveal a secret through a page, context token, log, or saved record.
- Rejection/aggression/departure emit only after a verified state transition.
- One source chain that calls `DoDownside` → `DoAggressive` or `DoLeave` still produces one outcome.
- Ghoul infusion verifies a non-ghoul → ghoul transition after vanilla returns.
- Failed ghoul surgery leaves no page, no permanent history mutation, and no stale Tale suppression.
- Correlated `DidSurgery` ownership fails open to the current Tale when the dedicated source is not
  healthy.

### 4.4 A3 is complete only when

- `EmbraceTheVoid` can create only `embraced`; `DisruptTheLink` can create only `disrupted`.
- The event emits after normal return and expected terminal level verification.
- Exactly one canonical ending page owns the matching level change, Tale, quest ending, and choice.
- A patch-registration failure leaves the current exact Tale path working.
- Loading an already-ended colony silently baselines the terminal state and writes no catch-up page.
- An optional reflection can be requested only after the normal page records, uses the existing
  reflection scheduler, and avoids event recap.

## 5. Reviewed current coverage and gaps

| Story area | Current production coverage | Planned change |
|---|---|---|
| Psychic ritual completion | Exact defName, role/perspective, outcome, and quality; one generic Anomaly group | A0 exact semantic families, same source and volume |
| Monolith discovery | Exact proximity-letter event window with subject pawn | Keep unchanged |
| Monolith activation | Exact activator and reached level, but one generic activation instruction | A0 phase-specific Stirring/Waking/Void Awakened windows |
| Monolith study | Vanilla letters only; no diary ownership or researcher continuity | A1 state-only knowledge snapshot attached to next activation |
| Entity study | Generic `StudiedEntity` Tale at the end of some pawn-study jobs | A1 rare threshold milestone owned by exact studier; conditional Tale suppression |
| Entity codex discovery | No dedicated diary source | Deferred because `SetDiscovered` lacks researcher identity |
| Containment | Ambient entity/condition coverage, no exact breach chapter | A1 one aggregated involuntary breach |
| Creepjoiner arrival | Existing arrival context includes `creepjoiner=true`; A2.0 now opens the saved visible-only arc without another page | Keep the canonical Arrival owner |
| Creepjoiner outcome | A2.0 owns exact rejection, aggression, and departure transitions with deterministic visible witnesses | Keep general downside classification deferred |
| Ghoul state | Body-mod stance recognizes ghouls; generic surgery may record | A2 one exact transformation, no routine ghoul lifecycle spam |
| Final void choice | `ClosedTheVoid` and `EmbracedTheVoid` Tales already route through the anomaly Tale group | A3 exact terminal event with fail-open Tale fallback |
| Metalhorrors | Gray-flesh evidence, emergence, hidden infection pressure, raid/thought/hediff routes | Preserve; new sources must dedup against overlapping visible aftermath |
| Pit gate/fleshmass/nociosphere/obelisks/corpse | Generic observed-condition atmosphere and selected start pages | Preserve |
| Cube and void bodily states | Hediff/thought prompt routing | Preserve |

The guiding rule is subtraction before addition: a new source must either provide facts the current
route cannot know or improve ownership of one existing page. It must not merely restate an Anomaly
notification in darker prose.

## 6. Verified RimWorld 1.6 seams

These seams were checked against the installed RimWorld 1.6 assembly, build `1.6.4871 rev590`, and
the installed Anomaly XML. Re-check signatures at implementation time because game updates can
change them.

### 6.1 DLC identity and ritual classifier

- Official package ID: `Ludeon.RimWorld.Anomaly`.
- `LordToil_PsychicRitual.RitualCompleted` is already patched by Pawn Diary.
- `PsychicRitualFanoutSignal` classifies with the exact key
  `PsychicRitual;<PsychicRitualDef.defName>`.
- Current role fan-out already distinguishes invoker, target, participant, and spectator and
  deduplicates a pawn assigned to more than one collection.
- The hook proves completion, perspective, and quality. It does **not** prove every later spawn,
  coma, mutation, abduction destination, or lasting effect.

### 6.2 Monolith progression

- `Building_VoidMonolith.Activate(Pawn)` calls `Find.Anomaly.IncrementLevel()` and retains the exact
  activator.
- Pawn Diary already patches that method defensively and forwards the reached level defName to the
  event-window engine.
- Installed levels are `Inactive`, `Stirring`, `Waking`, `VoidAwakened`, `Gleaming`, `Embraced`, and
  `Disrupted` with numeric levels 0 through 6.
- `GameComponent_Anomaly.IncrementLevel()` and `SetLevel(MonolithLevelDef, bool)` both call private
  `Notify_LevelChanged(bool)`, which updates research, conditions, quests, and the global
  `MonolithLevelChanged` signal.
- Player activation owns the transitions to Stirring, Waking, and Void Awakened. The terminal
  utility methods own Embraced and Disrupted. There is no need to patch private
  `Notify_LevelChanged` for the planned releases.

### 6.3 Study milestones

- `CompStudyUnlocks.OnStudied(Pawn studier, float amount, KnowledgeCategoryDef category = null)` is
  the exact threshold method.
- Its public `Progress` property exposes the committed progress after private
  `RegisterStudyLevel` advances one or more notes.
- `CompStudyUnlocksMonolith` inherits that method and mirrors progress into
  `GameComponent_Anomaly.Notify_MonolithStudyIncreased`.
- `CompStudiable.Study` reaches `Find.StudyManager.Study` and `StudyAnomaly`; both call
  `Thing.Notify_Studied(studier, ...)`, which reaches the study-unlock comp with the exact studier.
- `EntityCodex.SetDiscovered` exposes entry/Thing facts but no studier. It is unsuitable as the
  primary researcher-owned page source.
- `JobDriver_StudyInteract` records `TaleDefOf.StudiedEntity` only at the end of some entity-study
  jobs, after threshold calls may already have occurred. This permits a bounded pair/tick
  suppression marker only when a qualifying milestone page was actually accepted.

### 6.4 Containment

- `CompHoldingPlatformTarget.Notify_HeldOnPlatform(ThingOwner)` is capture/placement, not a breach.
- `Notify_ReleasedFromPlatform()` follows release/ejection and provides no actor or reason.
- `CompHoldingPlatformTarget.Escape(bool initiator)` ejects the held pawn, marks it escaping, may
  recursively call `Escape(false)` for other entities in the same room, and finally sends one
  threat letter for the aggregated escape.
- `Building_HoldingPlatform.EjectContents()` is also used for intentional release, so patching it as
  a breach source would be wrong.
- A prefix/postfix scope around `Escape(bool)` can snapshot positions before ejection, collect
  nested escapees, verify the outcome, and emit once when the outer call exits.

### 6.5 Creepjoiners

> **Reconfirmed against installed RimWorld 1.6 on 2026-07-20.** All three A2.0 targets are public,
> parameterless `void` methods on `Pawn_CreepJoinerTracker`. The private committed markers are exactly
> `triggeredRejection`, `triggeredAggressive`, and `hasLeft` (all `bool`); `joinedTick` and `speaker`
> remain public. `DoRejection` sets its marker before its worker, and vanilla rejection workers nest
> `DoLeave` or `DoAggressive`. `DoDownside` also nests those exact methods for its visible worker paths.

- `Pawn_CreepJoinerTracker.Notify_ChangedFaction()` records the joined tick when the pawn becomes a
  colonist. Existing `Pawn.SetFaction` arrival capture already produces the acceptance page.
- Public visible lifecycle methods include `DoRejection()`, `DoAggressive()`, and `DoLeave()`.
- `DoSurgicalInspection(Pawn surgeon, StringBuilder)` returns whether it appended disclosure text;
  `Recipe_SurgicalInspection.ApplyOnPawn` sends the visible letter and later records `DidSurgery`.
- `DoDownside()` can execute both visible and hidden outcomes. Some downside defs have letters;
  others, including metalhorror behavior, must not be disclosed by the diary.
- The tracker's private phase flags can verify that a public method actually transitioned. Any
  reflection over them belongs in a defensive registrar with cached `FieldInfo` and a warning/no-op
  path.

### 6.6 Ghoul transformation

- `Recipe_GhoulInfusion.ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer,
  List<Thing> ingredients, Bill bill)` first checks surgery failure.
- On success it calls `MutantUtility.SetPawnAsMutantInstantly(pawn, MutantDefOf.Ghoul)`, ensures
  player faction, and records `TaleDefOf.DidSurgery` when a surgeon exists.
- Prefix facts plus postfix `DlcContext.IsGhoul` verification distinguish success from failure and
  support exact Tale ownership.

### 6.7 Terminal void outcome

- `CompVoidNode.OnInteracted(Pawn)` only opens the choice dialog.
- Private `CloseNode(bool, int, int)` stores a delayed choice and later calls one public terminal
  method. It is not the preferred hook.
- `RimWorld.Utility.VoidAwakeningUtility.EmbraceTheVoid(Pawn)` ends the quest, sets level
  `Embraced`, applies visible pawn consequences, and records `EmbracedTheVoid`.
- `RimWorld.Utility.VoidAwakeningUtility.DisruptTheLink(Pawn)` ends the quest, sets level
  `Disrupted`, applies colony consequences/collapse, and records `ClosedTheVoid`.
- These exact methods know the actor and final branch. A prefix can open Tale ownership; a postfix
  can verify normal completion and the expected level before emitting.

## 7. Target architecture

### 7.1 Source ownership flow

```text
existing psychic ritual signal
  -> exact XML family
  -> existing RitualEventData / RitualEventSpec
  -> existing role fan-out and queue

existing monolith activation patch
  -> reached level defName
  -> exact event-window Def
  -> existing event-window page

new exact Anomaly callback
  -> guarded live snapshot
  -> plain source-specific facts
  -> pure policy / writer plan / dedup plan
  -> AnomalyEventData + AnomalyEventSpec
  -> bounded solo/fan-out signal
  -> existing DiaryEvent factory and queue

exact source that also records a generic Tale
  -> begin bounded ownership scope
  -> TaleSignal sees matching scope and defers only that duplicate
  -> source returns and verifies success
  -> success discards the deferred Tale and emits the canonical Anomaly event
  -> failure/mismatch clears ownership and dispatches the deferred Tale unchanged
  -> clear scope in all paths
```

### 7.2 Architecture barriers

The implementation must preserve these boundaries:

1. **Impure collection:** Harmony adapters may read `Pawn`, comps, defs, map positions, letters, and
   current game state on the main thread.
2. **DLC boundary:** raw Anomaly pawn state—creepjoiner tracker, mutant/ghoul state, and similar
   pawn-owned data—must be exposed through guarded `DlcContext` accessors. Non-pawn DLC comps may be
   read only inside Anomaly-gated source adapters.
3. **Plain facts:** once captured, policy receives strings, ints, bools, enums, and lists of plain
   candidates. It never receives `Pawn`, `Thing`, `Def`, `Map`, `ChoiceLetter`, Harmony objects, or
   settings singletons.
4. **Pure policy:** salience, milestone history decisions, aggregation, witness ranking, phase
   arbitration, and duplicate ownership are deterministic and standalone-testable.
5. **Impure emission:** a signal resolves captured stable pawn IDs back to live pawns only long
   enough to build the event and queue it.
6. **XML ownership:** thresholds, allowlists, family mappings, radii, caps, cooldowns, instructions,
   tones, colors, and prompt variants live in Defs with safe code fallbacks.
7. **Persistence:** saved state stores only stable IDs, stable defNames/tokens, ticks, small booleans,
   and event IDs. Transient call scopes are never Scribed.

### 7.3 Proposed plain contracts

Names are provisional; the separation of facts is not.

```csharp
internal enum AnomalyMomentKind
{
    StudyBreakthrough,
    ContainmentBreach,
    CreepJoinerOutcome,
    GhoulTransformation,
    VoidOutcome
}

internal sealed class AnomalyStudyFacts
{
    public string studiedDefName;
    public string studiedLabel;
    public string codexEntryDefName;
    public string knowledgeCategoryDefName;
    public string studierPawnId;
    public int oldProgress;
    public int newProgress;
    public int noteThresholdsCrossed;
    public bool completedBefore;
    public bool completedAfter;
    public bool isMonolith;
    public bool monolithActivatableBefore;
    public bool monolithActivatableAfter;
}

internal sealed class ContainmentEscapeFacts
{
    public string escapeId;
    public int tick;
    public int mapId;
    public List<ContainedEntityFact> entities;
    public List<AnomalyWriterCandidate> witnesses;
}

internal sealed class CreepJoinerOutcomeFacts
{
    public string pawnId;
    public string phase;
    public string visibleResultToken;
    public string surgeonOrSpeakerPawnId;
    public bool transitioned;
    public bool playerVisible;
    public bool subjectEligibleBefore;
    public List<AnomalyWriterCandidate> witnesses;
}

internal sealed class GhoulTransformationFacts
{
    public string subjectPawnId;
    public string surgeonPawnId;
    public bool wasGhoul;
    public bool isGhoul;
    public bool subjectEligibleBefore;
    public bool surgeonEligible;
}

internal sealed class VoidOutcomeFacts
{
    public string actorPawnId;
    public string outcome;
    public string reachedLevelDefName;
    public bool actorEligible;
    public bool methodReturned;
}
```

`ContainedEntityFact` contains only the entity's stable ID, visible label, defName/mutant defName,
pre-escape platform position, and verified escaped flag. `AnomalyWriterCandidate` contains only a
stable pawn ID, exact-role flags, distance bucket, eligibility, and deterministic tie-break key.

### 7.4 Catalog route

Add one `DiaryEventType.AnomalyEvent`. The route foundation may land in A1.1 before its live sources
only when it remains page-inert: the registered envelope/spec, exact XML routing, persistence, and
tests can exist, but no source signal is registered. It becomes page-producing only after all of the
following source-side pieces land together:

- `AnomalyEventData` with normalized `Kind`, stable source key, visibility/authorization flags, and
  a deterministic dedup key;
- `AnomalyEventSpec` registered in `DiaryEventCatalog`;
- pure `Decide` coverage for every supported kind and invalid/default values;
- catalog contract and migration-sentinel test updates.

Then each source phase adds its source-specific signal, exact fact adapter/planner, and live RimTests
before that signal is registered. This phased boundary lets A1.1 prove save/catalog compatibility
without claiming that an untested study or containment hook is live.

One catalog type does not mean one generic policy. Study, containment, creepjoiner, ghoul, and void
retain separate fact DTOs and pure planners. `AnomalyEventData` is only their common dispatch
envelope, scoped to one DLC; it is not a cross-DLC page-producing primitive.

### 7.5 Required shared narrative evidence

After Narrative Continuity N1 exists, A1–A3 source events attach per-POV evidence such as:

- `identity_transition=ghoul`;
- `journey_chapter=void_answer`;
- `ambient_pressure=containment_breach`.

Use `anomaly-monolith|<campaignEpoch>` for visible monolith/void chapters, the transformed pawn as the
ghoul subject, and exact map/entity scope for a known containment breach. Creepjoiner evidence begins
only at the plan's visible reveal boundary; arrival, hidden downside, infection, infiltrator, and
unresolved outcome facts remain absent.

These facets never decide capture or produce another immediate page. The Anomaly provider may propose
only visibly known transformation/chapter/pressure facts and must return empty for unknown POV state.
Shared selector or provider failure preserves the canonical Anomaly page without enrichment.

### 7.6 Fail-open nested Tale transaction

Surgical inspection, ghoul infusion, and both terminal void methods call `TaleRecorder.RecordTale`
**inside** the vanilla method, before Pawn Diary's source postfix can verify the final state. Their
ownership scope must therefore defer a matching `TaleSignal`; it must not permanently drop it at
Tale time.

Freeze this transaction:

1. Source prefix opens a scope keyed by exact expected Tale defName and pawn IDs.
2. `TaleSignal` sees the scope, constructs/copies its normal fallback facts, and parks one deferred
   signal in that synchronous scope instead of dispatching it.
3. Source postfix verifies the committed Anomaly result.
4. On success, discard the deferred generic signal and emit the canonical Anomaly event.
5. On mismatch, normal-return failure, or caught exception, first remove the ownership marker and
   then dispatch the deferred Tale through the ordinary path so it cannot be deferred again.
6. A Harmony finalizer/guarded cleanup handles exceptional exits and flushes any deferred fallback.
7. If the optional source hook never registered, no scope exists and Tale behavior is unchanged.

The scope may hold the already-built `TaleSignal` or a purpose-built plain fallback snapshot only
for the synchronous outer call. It must retain no live Tale/Pawn/Def beyond that callback, cannot be
Scribed, is depth-safe, and accepts at most one exact matching Tale.

## 8. Psychic-ritual family policy (A0)

### 8.1 Exact family catalog

The installed Anomaly version contains 16 psychic ritual defs. Add six exact groups before the
current order-`776` fallback:

| Proposed group | Exact classifier keys | Narrative center |
|---|---|---|
| `ritualAnomalyInvitation` | `PsychicRitual;VoidProvocation`, `PsychicRitual;SummonAnimals`, `PsychicRitual;SummonShamblers` | Calling something toward the colony; invitation, anticipation, and uncertainty |
| `ritualAnomalyFleshAndWeather` | `PsychicRitual;SummonPitGate`, `PsychicRitual;SummonFleshbeasts`, `PsychicRitual;SummonFleshbeastsPlayer`, `PsychicRitual;BloodRain` | Deliberately making the environment bodily or hostile |
| `ritualAnomalyPredation` | `PsychicRitual;Philophagy`, `PsychicRitual;Chronophagy`, `PsychicRitual;Psychophagy` | Taking skill, years, or psychic capacity from a target |
| `ritualAnomalyMind` | `PsychicRitual;Brainwipe`, `PsychicRitual;PleasurePulse`, `PsychicRitual;NeurosisPulse` | Deliberate alteration of memory, feeling, or behavior |
| `ritualAnomalyAbduction` | `PsychicRitual;SkipAbduction`, `PsychicRitual;SkipAbductionPlayer` | Reaching through distance and bringing a person into danger |
| `ritualAnomalyDeathRefusal` | `PsychicRitual;ImbueDeathRefusal` | Choosing an unnatural relationship with death |

Use consecutive orders `770` through `775`. Keep `ritualAnomalyPsychic` at `776` with its current
`matchTokens` fallback so future vanilla or modded psychic rituals remain supported.

Every new group should include:

```xml
<enableWhenPackageIdsLoaded>
  <li>Ludeon.RimWorld.Anomaly</li>
</enableWhenPackageIdsLoaded>
```

This hides the new setting rows and makes them unavailable at classification time without Anomaly.
Exact strings would already sit inert, but the explicit gate also prevents no-DLC settings clutter.

### 8.2 Prompt truth contract

The family instruction may describe the ritual's **known intention** and the captured pawn's place
in it. It must not assert an effect the completion hook does not prove.

Examples of forbidden leaps:

- `VoidProvocation` completion does not prove which entity arrived or that it was captured.
- a summoning ritual does not prove the spawned threat harmed anyone;
- `SkipAbduction` completion does not prove the abductee's identity unless the supplied target facts
  name them;
- phagy completion does not authorize every POV to claim stolen skill, age, coma, or guilt;
- `Brainwipe` completion does not authorize a spectator to narrate erased memories as their own;
- `ImbueDeathRefusal` completion does not mean the target has already died and returned.

The current localized per-perspective instruction remains appended after the family instruction.
Family prose should explicitly defer embodiment and victimhood to
`psychic_ritual_perspective=...` and the supplied other-pawn facts.

### 8.3 Settings and migration behavior

Splitting one settings group into six changes the settings surface. Freeze these semantics:

- existing saves with no override for a new group use each new group's `defaultEnabled=true`;
- an old override for `ritualAnomalyPsychic` continues to control only the fallback group—it is not
  silently copied into six unrelated keys;
- the A0 release notes must call out the finer controls;
- no code migration guesses that disabling the old generic row meant disabling all future exact
  families;
- group `defName` values become stable settings keys once shipped and must not be renamed later.

### 8.4 A0 ritual tests

At minimum, tests must prove:

- all 16 exact classifier keys map to their intended family;
- exact matching is case-insensitive but does not use translated labels;
- `PsychicRitual;SummonFleshbeastsPlayer` cannot be swallowed by a less precise group;
- an unknown psychic ritual reaches the existing fallback;
- an Ideology ritual classifier key does not enter an Anomaly group;
- each group is unavailable when the official package gate is absent;
- XML order is unique and below the fallback;
- all DefInjected labels, instructions, tones, and tone variants exist.

## 9. Monolith chapter policy (A0/A1)

### 9.1 Discovery remains the prologue

Keep `VoidMonolithDiscovery` unchanged. It already records the subject pawn who entered the
proximity-letter radius and writes a grounded first encounter. Discovery does not mean activation,
study, or acceptance of the void.

### 9.2 Exact activation chapters

The event-window engine tests every Def independently, so a generic activation rule plus exact
rules would duplicate. Replace the current generic start match with this mutually exclusive set:

| Window Def | Source match | Meaning |
|---|---|---|
| Existing `VoidMonolithActivation` | `source=VoidMonolith`, `signal=activated`, exact `defName=Stirring` | The colony deliberately stirs the monolith for the first time |
| New `VoidMonolithWaking` | same source/signal, exact `defName=Waking` | Knowledge has become a second deliberate escalation |
| New `VoidMonolithVoidAwakened` | same source/signal, exact `defName=VoidAwakened` | The colony crosses into the final active void chapter |

Retaining `VoidMonolithActivation` for Stirring preserves the existing Def/settings/display key for
the first transition. The two new Defs use new stable window keys and localized fallback text.

Each is a one-shot subject-pawn event:

- `keepActive=false`;
- `recordScope=SubjectPawn`;
- `recordStartEvent=true`;
- no timeout or inferred end;
- exact level match required;
- current activator is the author;
- no colony fan-out.

### 9.3 Knowledge enrichment

A1 may attach the most recent qualifying monolith-study snapshot to the **next** activation when:

- it occurred after the previous monolith activation;
- it belongs to the same game and monolith arc;
- its researcher ID still resolves only for contextual naming, not capture eligibility;
- it is within the XML maximum age; and
- the snapshot has not already been consumed by an activation.

Bounded context may include:

- `monolith_previous_level=...`;
- `monolith_reached_level=...`;
- `monolith_last_researcher=...`;
- `monolith_study_stage=...`;
- `monolith_became_activatable=true` only when the before/after check proved it.

The activator remains the author. A researcher named in context does not receive a second page and
is not credited with the activation.

### 9.4 Excluded monolith transitions

- Do not patch private `GameComponent_Anomaly.Notify_LevelChanged` merely to catch every level.
- Do not create a standalone Gleaming page. It is automatic and lacks a human actor.
- Do not turn `SetLevel(Waking, silent:true)` cleanup/backtracking into a chapter.
- Embraced and Disrupted belong to A3's exact terminal sources, not activation windows.
- Loading at an existing level silently baselines state; it never replays discovery or activation.

## 10. Knowledge-milestone policy (A1)

### 10.1 Capture transaction

Register a defensive prefix/postfix for the exact overload
`CompStudyUnlocks.OnStudied(Pawn,float,KnowledgeCategoryDef)`:

1. Gate immediately on `ModsConfig.AnomalyActive`, `DiaryGameComponent.GamePlaying`, non-null comp,
   non-null studier, and a healthy policy Def.
2. Prefix captures `Progress`, `Completed`, parent stable defName/visible label, optional codex entry,
   knowledge category, monolith identity, and `Building_VoidMonolith.CanActivate` when applicable.
3. Vanilla runs and may cross zero, one, or several private thresholds.
4. Postfix captures the same committed fields.
5. If `newProgress <= oldProgress`, clear transient state and stop.
6. Copy all source facts into `AnomalyStudyFacts`; do not pass the comp or pawn into policy.
7. Pure policy updates history and returns `StateOnly`, `Generate`, or `DropInvalid`.
8. Only `Generate` submits an `AnomalyStudySignal` for the exact studier.

Do not patch private `RegisterStudyLevel`. The public method plus before/after progress proves the
same result with a more stable signature and collapses multi-threshold jumps naturally.

### 10.2 Default authorizing reasons

One study transition may authorize a page for multiple reasons, but still emits once. Default
reasons are:

1. **First colony breakthrough:** the first valid Anomaly study-note threshold observed after a
   clean new-game baseline.
2. **Completed entity kind:** `Completed` changes false → true for a stable studied defName that has
   not previously completed in this save.
3. **XML promotion:** an exact `studiedDefName + stage/progress` rule explicitly marks a milestone
   as story-sized.

Monolith thresholds are state-only by default. If the before/after check changes `CanActivate` from
false to true, preserve that fact for the next activation rather than writing a second page just
before the activator's canonical chapter.

### 10.3 History and settings independence

Observation and generation are separate:

- mark `firstBreakthroughObserved=true` on the first valid threshold even if the author is
  ineligible, the group is disabled, generation is off, or the queue rejects the page;
- mark a stable entity-kind completion as observed whenever vanilla completes it;
- changing settings later cannot turn an old completion into a new first;
- XML promotion controls page authorization, not whether the underlying completion enters history;
- history uses stable defName keys, not localized labels or individual `Thing` IDs.

This is the same anti-retroactivity rule used by progression baselines elsewhere in Pawn Diary.

### 10.4 Writer and context policy

The exact studier is the only default author. If the studier is not diary-eligible at event time,
the state updates and no page is generated. Do not select an unrelated witness for a private
knowledge breakthrough.

Prompt context is bounded to:

- studied visible label and stable defName;
- optional codex/category label and stable defName;
- semantic stage: `first_breakthrough`, `completed_kind`, or XML promotion token;
- whether the studied subject is a contained entity or monolith;
- a short event-time setting snapshot;
- optional prior monolith level when relevant.

Do not include raw research points, random thresholds, total letter text, hidden abilities,
containment chances, undiscovered codex descriptions, or full study-note history. Progress numbers
may be retained in saved/debug facts but should be projected to semantic stages before prompting.

### 10.5 `StudiedEntity` ownership

`JobDriver_StudyInteract` may record `StudiedEntity` after the study interactions finish. Freeze this
ownership rule:

- when no dedicated knowledge page was accepted, the current Tale path remains untouched;
- when a dedicated page was accepted, store a short-lived suppression key containing exact
  studier ID, studied pawn/entity ID or defName, and tick window;
- `TaleSignal` suppresses only a matching `StudiedEntity` pair inside that window;
- a mismatched researcher, entity, or expired key cannot be suppressed;
- suppression entries are consumed once, capped, and cleared on game/load transitions.

This preserves the generic fallback while preventing one study job from generating two pages.

### 10.6 Old-save baseline

On the first A1 scan/load of an old save with Anomaly active:

- set a schema/version marker;
- baseline the current monolith level and current completed study kinds that can be found cheaply
  from live maps/known tracked subjects;
- set `firstBreakthroughObserved=true` when any committed study progress already exists;
- do not iterate the whole world or codex to reconstruct a fictional research chronology;
- do not write catch-up pages;
- allow genuinely future completion of an uncompleted kind to record normally.

If a complete baseline cannot be proven for an off-map/despawned entity, prefer silence over a
false “first.”

## 11. Containment-breach policy (A1)

### 11.1 One call tree, one breach

`Escape(true)` can recursively call `Escape(false)` for same-room entities. Use one main-thread,
unsaved aggregation scope:

1. Outer prefix creates an escape ID and snapshots the initiating entity, platform, map, position,
   and nearby writer candidates before ejection.
2. Every nested prefix appends its entity/platform snapshot to the active scope.
3. Each postfix marks only entities that are no longer held and visibly entered the escape state.
4. Nested postfixes never emit.
5. Outer postfix closes the scope, runs pure aggregation/writer policy, and submits at most one
   fan-out signal.
6. A `finally`/Harmony finalizer cleanup path removes the scope if vanilla or another mod throws.

The scope must be reentrancy-safe even though RimWorld currently executes this path on the main
thread. A small stack/depth counter is safer than one unqualified static field.

### 11.2 Writer selection

XML owns `witnessRadius`, `maxWriters`, and tie-break policy. Recommended default `maxWriters` is 2,
but the normal result should usually be one author.

Rank candidates deterministically:

1. eligible colonist physically within the configured radius of an escaping platform at the outer
   prefix;
2. exact recent eligible studier of one escaped entity, but only when a bounded study cache proves
   the relationship and the pawn is on the affected map;
3. closest eligible free colonist on the affected home map as a last-resort colony witness;
4. stable pawn ID as final tie-break.

Do not consume `Verse.Rand` for selection. Do not claim the witness was a guard, researcher,
captor, or person at fault unless the selected candidate carries that exact captured role.

### 11.3 Event context

One breach context may contain:

- `anomaly_kind=containment_breach`;
- `escaped_count=<bounded integer>`;
- up to `maxEntityLabelsInContext` visible entity labels and stable defNames;
- `additional_escaped_count` when the list was truncated;
- `witness_role=nearby|recent_studier|colony_witness`;
- visible room/map surroundings captured before ejection;
- whether the outer entity initiated a same-room cascade.

It must not contain:

- hidden minimum/maximum containment rolls;
- secret entity abilities or undiscovered codex text;
- a guessed cause such as neglect, sabotage, low power, or weak walls;
- an intentional-release actor;
- full platform inventories or every cell position.

### 11.4 Non-breach matrix

| Source | A1 behavior |
|---|---|
| `Notify_HeldOnPlatform` | State/context only; no page |
| Intentional `EjectContents` | No breach page |
| `Notify_ReleasedFromPlatform` | No breach page |
| Entity dies while held | Existing death/condition routes; no breach page |
| Platform destroyed and contents ejected without `Escape` | No claim unless a later exact source is designed |
| `Escape(false)` nested under an outer escape | Aggregate into outer event |
| Direct/DEV `Escape(true)` | Eligible if the same verified visible transition occurs; tag debug only in dev diagnostics, never prompt |
| Save/load reconstruction | No event |

### 11.5 Duplicate arbitration

The containment breach owns the initial escape fact. Within its configured correlation window:

- A1.3 suppresses only the exact replay of its own map/start-tick/outer-entity source key. Vanilla
  `Escape(bool)` emits no entity-bearing Tale or raid row, and the current generic raid/thought
  adapters cannot prove both the escaped entity and the same start tick, so they deliberately fail
  open instead of applying a broad same-tick ban;
- if a later source contract exposes that exact entity/start identity, A1.4 or the source's own phase
  may add a bounded ownership seam and suppress only the proven matching restatement;
- let later actual injuries, deaths, kidnappings, fires, or mental breaks record normally;
- do not suppress an observed condition merely because the entity is still present—the condition
  is ongoing atmosphere, not a second breach page;
- coalesce thought bursts caused by the immediate letter into ambient/context when exact matching is
  possible; otherwise rely on existing thought thresholds rather than a broad global ban.

## 12. Creepjoiner reveal policy (A2)

### 12.1 Arc state and arrival ownership

> **A2.0 implementation (2026-07-20): complete.** The canonical arrival route now owns joined-state
> initialization and optional arrival-event identity. Schema 2 deep-scribes the exact seven primitive
> fields below, normalizes at most 4,096 inputs to at most 512 stable rows, and treats future rows as
> terminal replay barriers. An active pre-A2 save silently baselines current joined player
> creepjoiners; Anomaly-inactive loads defer. Age alone never prunes a terminal row without proof that
> its continuity reference is disposable.

The existing arrival route remains canonical for acceptance. When its context contains
`creepjoiner=true`, create or update a saved state-only record:

```text
pawnId
arrivalEventId (optional when an event was actually created)
joinedTick
lastVisiblePhase
lastVisibleEventId
terminal
schemaVersion
```

Do **not** save benefit, downside, aggression, rejection, hidden host state, trigger tick, or worker
type while those facts are secret. An old save with an already-joined creepjoiner receives a silent
baseline with no invented arrival page.

### 12.2 Surgical disclosure

Use a two-stage correlation:

1. `Recipe_SurgicalInspection.ApplyOnPawn` prefix opens a bounded surgery scope for subject and
   surgeon.
2. `Pawn_CreepJoinerTracker.DoSurgicalInspection` may append disclosure text and return true.
3. Cache only a boolean `creepjoinerDisclosure=true` plus safe visible phase facts; do not copy
   unrevealed Def identities into the scope before the method succeeds.
4. Recipe postfix verifies normal completion and an actual disclosure result.
5. Defer the matching `DidSurgery` Tale inside the ownership scope.
6. On verified disclosure, discard that deferred Tale and emit one `CreepJoinerOutcome` with phase
   `surgical_reveal`.
7. On surgery failure, `Nothing`, `DetectedNoLetter`, exception, or missing correlation, clear the
   ownership marker and dispatch any deferred Tale through the ordinary path.

The prompt may say that the inspection disclosed something abnormal and include the exact visible
letter-derived category if it can be represented without storing hidden mechanics. It must not
quote the whole letter or expose future phases.

### 12.3 Rejection, aggression, and departure

Patch public methods defensively and compare before/after state:

- `DoRejection()` → phase `rejected` only when the private triggered flag changes and the response
  becomes player-visible;
- `DoAggressive()` → phase `aggressive` only when `CanTriggerAggressive` was true and the method
  commits its visible response;
- `DoLeave()` → phase `departed` only when the pawn actually leaves player faction/enters the exit
  behavior and `hasLeft` changes.

If a downside worker calls one of those methods, the nested exact outcome owns the page. A separate
`DoDownside` postfix must not emit another.

`DoRejection` is the one nested semantic exception: visible vanilla rejection workers currently call
either `DoLeave` or `DoAggressive`, so a tiny bounded synchronous owner suppresses that nested page and
lets the outer verified rejection emit once. `AggressiveRejection` persists/narrates the strongest
visible `aggressive`/`hostile` state while retaining safe rejection-response provenance. A letterless
modded rejection does not open the owner, allowing its nested visible method to own naturally; a
committed outer marker with no page becomes a blank terminal replay barrier. The postfix/finalizer
always unwinds visible ownership. `DoDownside` itself remains unpatched, so its nested exact method
still owns naturally.

### 12.4 Writer policy

- Surgical disclosure: surgeon first; subject may receive the paired perspective only if still
  eligible and the prompt clearly distinguishes patient from examiner.
- Rejection/departure before joining: exact speaker when eligible, otherwise one nearby witness.
- Departure after joining: subject may write if eligible at the pre-transition snapshot; otherwise
  use one nearby eligible colonist.
- Aggression/transformation: one exact nearby eligible witness. Do not write first-person as a pawn
  after it has visibly become a hostile entity or otherwise ceased to be an eligible colonist.
- Never fan out to the whole colony.

### 12.5 Spoiler firewall

The following are forbidden until vanilla makes them visible:

- downside/benefit/aggression/rejection defName or label;
- countdown/MTB timing;
- metalhorror infection or host identity;
- future fleshbeast/sightstealer transformation;
- organ decay, crumbling mind, psychic agony, jailbreaking, or departure intent;
- surgical text that the actual inspection did not disclose.

`DoDownside()` remains unpatched for page generation in A2. A later release may add an XML allowlist
for downside defs with a verified public letter, but hidden and no-letter outcomes must stay denied
by code even if XML is misconfigured.

## 13. Ghoul-transformation policy (A2)

### 13.1 Exact transaction

1. Recipe prefix gates on Anomaly, snapshots subject/surgeon stable IDs, subject eligibility, and
   `wasGhoul` through `DlcContext`.
2. Open a Tale-ownership scope for the exact `DidSurgery` pair; a matching Tale is deferred, not
   dropped.
3. Vanilla runs its surgery failure check and possible transformation.
4. Recipe postfix reads `isGhoul` through `DlcContext`.
5. Generate only when `!wasGhoul && isGhoul` and the method returned normally.
6. Success discards the deferred generic Tale and emits the dedicated event; every failure path
   clears ownership and flushes any deferred Tale unchanged.

Do not infer success from recipe invocation, ingredients consumed, the bill disappearing, or the
presence of a generic surgery Tale alone.

### 13.2 POV and prompt contract

Prefer a pair event when both exact roles are eligible:

- subject: bodily/identity transition and the last human moment;
- surgeon: responsibility for carrying out an irreversible choice.

If only one is eligible, emit one solo perspective. If neither is eligible, update any required
dedup/baseline state and write nothing; do not select an unrelated colonist to dramatize a surgery
they may not have witnessed.

Context may include:

- `anomaly_kind=ghoul_transformation`;
- `transformation=ghoul`;
- `subject=...` and `surgeon=...` when exact;
- `irreversible_choice=true` as stable prompt schema;
- bounded pre-transition identity facts already safe in ordinary pawn summary.

Do not include mutation implementation details, removed work types, raw tracker state, future
upgrades, or moral judgment not supplied by an optional implemented belief facet.

### 13.3 Progression baseline handoff

After a successful transformation, update any already-implemented progression baselines that would
otherwise see the ghoul transition's side effects as unrelated later changes. Examples include
royal title/ideoligion/personality state only when those trackers exist and their owning feature is
already implemented. This is a consistency handoff, not authorization to implement other DLC plans
inside A2.

## 14. Terminal void policy (A3)

### 14.1 Exact ownership transaction

For each public terminal method:

1. Prefix checks Anomaly active, actor non-null, game playing, and no terminal outcome already
   committed in Pawn Diary state.
2. Snapshot actor identity/eligibility and open a correlation for the exact expected Tale and
   outcome token.
3. The matching Tale raised inside vanilla is deferred only by that active correlation.
4. Vanilla returns.
5. Postfix verifies expected terminal level: `Embraced` or `Disrupted`.
6. Commit saved outcome and submit one canonical `VoidOutcome` event.
7. On verification failure or exception, clear ownership and dispatch the deferred Tale unchanged;
   clear the correlation in every path.

If patch registration fails, no correlation opens and the current `taleanomaly` path continues to
record `EmbracedTheVoid` or `ClosedTheVoid`. If postfix verification fails, the deferred Tale is
dispatched after clearing the scope; it must never silently lose the only ending page.

### 14.2 Context contract

Shared facts:

- `anomaly_kind=void_outcome`;
- `void_outcome=embraced|disrupted`;
- `actor=...`;
- `monolith_level=Embraced|Disrupted`;
- `terminal=true`;
- bounded current setting and actor summary.

Embraced may describe accepting personal transformation and continued relation to the void because
the exact method applies those consequences. Disrupted may describe severing the link and the
colony's visible release because the exact method performs rehumanization and collapse. Neither
prompt may invent what happened to off-map enemies, future anomalies, or the actor's unexpressed
motives.

### 14.3 Writer and reflection

The choosing pawn is the only canonical author. Pocket-map companions are not co-choosers merely
because they return through the same utility.

After the outcome page successfully records, A3 may call the existing rare arc-reflection scheduler
with a stable reason such as `anomaly_void_outcome`. The request must:

- point at the canonical event ID;
- carry `avoid_related_event_recap=true`;
- obey existing reflection rate limits and settings;
- be optional when the actor cannot write;
- never run before the canonical event exists.

### 14.4 Ending dedup

The dedicated terminal event owns, for its exact correlation only:

- the terminal monolith level;
- the matching Tale;
- the quest-success fact;
- the actor's choice;
- the immediate terminal chapter.

Later hediffs, memories, deaths, relationships, and ordinary post-ending life remain eligible under
their normal policies. The ownership window must not broadly suppress every void-related event.

## 15. Event ownership and dedup matrix

| Gameplay moment | Canonical owner after planned phase | Suppressed/absorbed overlap | Still allowed later |
|---|---|---|---|
| Psychic ritual completes | Existing `Ritual` event with exact A0 group | Same-tick ritual-result thought/Tale only after an exact correlation is proven; A0 guidance alone adds no suppression | Actual later injury, entity arrival, capture, death, mood, or condition |
| Monolith discovered | Existing `VoidMonolithDiscovery` window | Duplicate proximity signal | Later activation and study |
| Monolith reaches Stirring/Waking/VoidAwakened | Exact activation window | Generic activation window and same reached-level restatement | Later level, consequence, quest, study, terminal outcome |
| First/complete entity study milestone | A1 `AnomalyEvent` study page | Matching end-of-job `StudiedEntity` Tale only when the dedicated page was accepted | Later distinct entity-kind completion, actual entity behavior |
| Monolith study note | State-only knowledge snapshot | No page to suppress | Next activation consumes enrichment |
| Several entities escape one room | One A1 containment-breach event | Nested escape calls and exact self-replay; future same-start restatements only after an entity/start contract exists | Current generic rows fail open; later injuries, deaths, raids, ongoing observed atmosphere |
| Intentional release | No A1 page | Nothing | Later consequences under normal sources |
| Creepjoiner joins | Existing arrival | Any proposed separate acceptance page | Later visible arc outcome |
| Surgical inspection discloses creepjoiner truth | A2 creepjoiner outcome | Matching `DidSurgery` Tale | Later visible downside or departure |
| Creepjoiner downside calls aggression/leave | Exact nested visible outcome | Outer generic downside page | Later combat/injury/death |
| Hidden metalhorror downside | No reveal page | Any attempted creepjoiner-secret context | Existing spoiler-safe hidden-condition atmosphere and eventual visible emergence |
| Ghoul infusion succeeds | A2 ghoul transformation | Matching `DidSurgery` Tale | Later injuries/death; routine ghoul state remains ordinary |
| Ghoul infusion fails | Existing surgery failure behavior; no dedicated page | Stale correlation | Any real later event |
| Void embraced | A3 exact outcome | `EmbracedTheVoid` Tale, terminal level/quest restatement | Later personal consequences and optional non-recap reflection |
| Link disrupted | A3 exact outcome | `ClosedTheVoid` Tale, terminal level/quest restatement | Later colony life and optional non-recap reflection |

Dedup must be identity-aware and narrow. A shared word such as `void`, `entity`, `surgery`, or
`anomaly` is never enough to suppress another source.

## 16. Persistence and save compatibility

### 16.1 Proposed saved state

Add Anomaly state to `DiaryGameComponent` only when A1 begins. A0 needs no new persistence.

```text
anomalySupportSchemaVersion
anomalyFirstStudyBreakthroughObserved
anomalyCompletedStudyDefNames
anomalyPromotedStudyMilestoneKeys
anomalyMonolithBaselineLevelDefName
anomalyLastMonolithKnowledgeSnapshot
anomalyCreepJoinerArcs
anomalyTerminalOutcome
anomalyTerminalActorPawnId
anomalyTerminalEventId
```

The completed/promotion collections need only stable string keys and can be bounded by the number of
Defs/milestones actually observed. The monolith snapshot contains stable researcher ID, semantic
stage, tick, reached study progress, `becameActivatable`, and consumed flag. It does not store a
live letter or monolith reference.

`CreepJoinerArcState` is a small deep-scribed record as defined in §12.1. It contains only visible
phase history. Prune terminal records after an XML retention interval only if their event IDs are no
longer needed for continuity; never prune in a way that allows an old outcome to replay as new.

As of A2.0, current schema 2 implements the first seven component keys through
`anomalyCreepJoinerArcs`; the three terminal-void keys remain future A3 design. Structural normalization
is live, while terminal rows deliberately receive no age-only pruning until exact reference/liveness
evidence can satisfy the preceding replay-safety condition.

### 16.2 Scribe keys and versioning

- Freeze every Scribe key once released.
- Use a dedicated integer schema version and explicit forward normalization.
- Initialize missing lists/records in `PostLoadInit`.
- Drop null records, blank stable IDs, invalid phases, negative ticks, and duplicate keys
  deterministically.
- Unknown future enum/token values normalize to safe `unknown`/state-only behavior, not page
  generation.
- Never rename old keys merely to match a later class/property rename.
- Add save-normalization tests for blank, duplicate, corrupt, and partial records.

### 16.3 Old-save behavior

On the first load after each phase ships:

- **A1:** baseline existing monolith/study history conservatively and emit nothing.
- **A2:** create state-only records for current player creepjoiners; do not infer arrival, secret,
  or past visible outcomes. Current ghouls are baseline state, not new transformations.
- **A3:** if the monolith is already Embraced or Disrupted, save that terminal token silently and
  emit nothing.

Old saves must not receive a burst of “first,” “completed,” “revealed,” “transformed,” or “ending”
pages merely because the mod learned to observe them.

### 16.4 Event-time truth

All generated prompt facts must be copied into the `DiaryEvent` before asynchronous generation.
The background request must not later read:

- current monolith level;
- current containment roster;
- current creepjoiner tracker;
- current mutant state;
- current pawn faction/eligibility;
- live `ChoiceLetter` or translated Def state.

If a pawn dies, leaves, transforms again, or the map changes before generation runs, the entry still
describes the captured event-time facts.

### 16.5 Transient caches

Transient, unsaved helpers may include:

- `AnomalyTaleOwnershipCache`;
- `ContainmentEscapeScopeStack`;
- `AnomalyStudySuppressionCache`;
- `CreepJoinerSurgeryScope`;
- `GhoulInfusionScope`;
- `VoidOutcomeScope`.

Each must have:

- a hard entry cap in XML or stable defensive code fallback;
- a tick expiry where the call is not strictly nested;
- an exact non-reused vanilla job identity may outlive that fallback expiry only while the cache stays
  hard-capped/lifecycle-cleared and a different job is guaranteed to fail open;
- consume-once semantics;
- exact stable identity keys;
- `Clear()` called during game initialization/load (`FinalizeInit` or the repository's current
  equivalent reset path);
- no `Pawn`, `Thing`, `Map`, `Def`, letter, or comp retained beyond the synchronous callback.

## 17. XML, prompts, settings, and localization

### 17.1 `DiaryAnomalyPolicyDef`

Add one policy Def with safe code fallbacks. Suggested fields:

```text
studyEnabled
recordFirstStudyBreakthrough
recordCompletedEntityKind
promotedStudyMilestones[]
monolithKnowledgeMaxAgeTicks
studyTaleSuppressionTicks

containmentEnabled
containmentWitnessRadius
containmentMaxWriters
containmentMaxEntityLabelsInContext
containmentDedupTicks
recentStudierMaxAgeTicks

creepJoinerEnabled
creepJoinerOutcomeDedupTicks
creepJoinerArcRetentionTicks
creepJoinerMaxWitnesses

ghoulTransformationEnabled
voidOutcomeEnabled
taleOwnershipMaxDepth
taleOwnershipExpiryTicks
```

`promotedStudyMilestones` should use stable structured entries such as studied defName plus semantic
stage/progress and should be safe when a mod removes that Def. It must not use a direct optional-DLC
Def reference that would error without Anomaly.

Policy validation should clamp negative values, writer caps, radii, label caps, and expiry windows.
Use conservative fallbacks when the Def is absent or malformed.

### 17.2 Interaction groups

Add package-gated groups for:

- six ritual families from §8;
- `anomalyStudyBreakthrough` matching stable synthetic key
  `PawnDiary_AnomalyStudyBreakthrough`;
- `anomalyContainmentBreach` matching `PawnDiary_ContainmentBreach`;
- `anomalyCreepJoinerOutcome` matching `PawnDiary_CreepJoinerOutcome`;
- `anomalyGhoulTransformation` matching `PawnDiary_GhoulTransformation`;
- `anomalyVoidOutcome` matching `PawnDiary_VoidOutcome`.

The new exact event groups can live in the existing Interaction classification domain because the
factory and saved-event recovery already understand stable synthetic defNames there. Do not add a
new XML domain solely for the DLC.

Every group needs:

- unique stable `defName` and deterministic `order`;
- `enableWhenPackageIdsLoaded=Ludeon.RimWorld.Anomaly`;
- localized label/instruction/tone/variants via DefInjected;
- appropriate importance and color cue;
- explicit anti-spoiler/no-invented-causality guidance.

### 17.3 Stable prompt schema

Structured schema labels intentionally stay English per the localization carve-out. Proposed tokens
include:

```text
anomaly_kind=study_breakthrough|containment_breach|creepjoiner_outcome|ghoul_transformation|void_outcome
study_stage=first_breakthrough|completed_kind|promoted
studied_def=...
knowledge_category=...
monolith_previous_level=...
monolith_reached_level=...
monolith_became_activatable=true|false
escaped_count=...
escaped_entities=...
witness_role=nearby|recent_studier|colony_witness
creepjoiner_phase=surgical_reveal|rejected|aggressive|departed
visible_result=...
rejection_response=true
transformation=ghoul
void_outcome=embraced|disrupted
terminal=true|false
```

Never add tokens such as:

```text
hidden_downside=...
metalhorror_host=...
downside_trigger_tick=...
containment_escape_roll=...
future_entity_ability=...
```

### 17.4 Player-facing fallback text

Every new event needs concise Keyed fallback text for generation-disabled/failure paths. It must
name only captured facts and make the POV role clear. Examples of semantic shapes, not final English:

- researcher: reached a genuine breakthrough while studying the named subject;
- breach witness: saw one or more named entities break free;
- surgeon/patient: an inspection visibly disclosed something abnormal;
- creepjoiner witness: saw the named pawn reject, turn aggressive, or leave;
- ghoul subject/surgeon: underwent/performed the successful infusion;
- void actor: embraced the void or disrupted the link.

Final English belongs in `Languages/English/Keyed/PawnDiary.xml`; other languages can translate the
same keys. Prompt instructions/tone in Defs belong in `DefInjected`, not Keyed.

### 17.5 Localization safety

- Call `.Translate()` only on RimWorld's main thread.
- Do not send translation keys to the background LLM client.
- Sanitize player/mod-provided names and localized labels with existing prompt-text helpers.
- Do not parse translated letters/descriptions to decide branch, identity, or visibility.
- XML Def text (`label`, `instruction`, `tone`, variants) uses DefInjected.
- UI/fallback/prompt-fragment text uses Keyed.
- Stable schema tokens, defNames, roles, and `none`/`n/a`/`unknown` stay English by design.

## 18. File-level change map

The exact split may change during implementation, but responsibilities should remain separate.

### 18.1 Proposed new production files

| File | Responsibility |
|---|---|
| `Source/Capture/Events/AnomalyEventData.cs` | Plain catalog envelope, kind normalization, dedup key, context projection |
| `Source/Capture/Specs/AnomalyEventSpec.cs` | Catalog registration decision adapter |
| `Source/Capture/Policies/AnomalyStudyPolicy.cs` | Pure milestone/history plan |
| `Source/Capture/Policies/AnomalyStudyContextFormatter.cs` | Bounded semantic study/monolith prompt projection |
| `Source/Capture/Policies/AnomalyMonolithKnowledgePolicy.cs` | Pure next-activation consume/attach ownership |
| `Source/Capture/Policies/ContainmentBreachPolicy.cs` | Pure aggregation and witness ranking |
| `Source/Capture/Policies/CreepJoinerOutcomePolicy.cs` | Pure visible-phase arbitration and writer plan |
| `Source/Capture/Policies/AnomalyTaleOwnershipPolicy.cs` | Pure exact matching/fail-open ownership decisions |
| `Source/Ingestion/Sources/AnomalyStudySignal.cs` | Implemented A1.2 exact-researcher study page; later source signals remain separate |
| `Source/Generation/DlcContext.Anomaly.cs` | Guarded live-to-plain study/monolith collectors |
| `Source/Generation/AnomalyStudySuppressionCache.cs` | Bounded transient study/Tale ownership state |
| `Source/Models/CreepJoinerArcState.cs` | Deep-scribed visible arc record |
| `Source/Models/AnomalyMonolithKnowledgeState.cs` | Small saved monolith enrichment snapshot |
| `Source/Defs/DiaryAnomalyPolicyDef.cs` | XML policy schema, fallback/default validation |
| `Source/Patches/DiaryAnomalyPatches.cs` | Defensive exact Anomaly patch registration and callbacks |
| `1.6/Defs/DiaryAnomalyPolicyDefs.xml` | Tunable policy values and milestone promotion rules |
| `tests/DiaryAnomalyPolicyTests/` | Standalone pure policy test project |

Combining very small policy files is acceptable if comments and testability remain clear. Do not
combine live Harmony adapters with pure policy merely to reduce file count.

### 18.2 Existing production files likely to change

| File | Planned change |
|---|---|
| `Source/Capture/DiaryEventType.cs` | Turn `AnomalyEvent` from comment-only future source into a real enum member in A1 |
| `Source/Capture/Catalog/DiaryEventCatalog.cs` | Register `AnomalyEventSpec` |
| `Source/Generation/DlcContext.cs` | Guarded creepjoiner/ghoul/Anomaly pawn accessors only |
| `Source/Ingestion/Sources/TaleSignal.cs` | Consult exact bounded ownership/suppression facts and append safe enrichment/fallback behavior |
| `Source/Generation/ArrivalContextCache.cs` or arrival emission owner | Notify creepjoiner arc state from the existing canonical arrival without duplicating the page |
| `Source/Core/DiaryGameComponent.cs` | Save fields, initialization, normalization, cache clearing |
| `Source/Core/DiaryGameComponent.EventWindows.cs` | Consume optional monolith study enrichment without changing source truth |
| `Source/Patches/DiaryPatchRegistrar.cs` | Call one `DiaryAnomalyPatches.TryRegister` entry |
| `1.6/Defs/DiaryInteractionGroupDefs.xml` | Exact ritual and new synthetic event groups |
| `1.6/Defs/DiaryEventWindowDefs.xml` | Mutually exclusive monolith activation windows |
| `Languages/English/DefInjected/PawnDiary.DiaryInteractionGroupDef/DiaryInteractionGroupDefs.xml` | Group translations |
| `Languages/English/DefInjected/PawnDiary.DiaryEventWindowDef/DiaryEventWindowDefs.xml` | New monolith window translations if this Def type has injected fields |
| `Languages/English/Keyed/PawnDiary.xml` | New fallback, settings, and prompt strings |
| `DOCUMENTATION.md` | Runtime flow, event source, XML policy, persistence, DLC-safety, localization notes per shipped slice |
| `CHANGELOG.md` | Dated behavior line per shipped slice |
| `1.6/Assemblies/PawnDiary.dll` | Rebuilt after every C# behavior slice |

Before editing a listed file, re-run CodeGraph on the exact symbols and inspect current git diff so
unrelated user changes are preserved.

### 18.3 Tests to add or update

- new pure `DiaryAnomalyPolicyTests`;
- `DiaryCapturePolicyTests` for the event catalog, payload decisions, dedup keys, and enum sentinel;
- `DiaryPipelineTests` for classifier keys and prompt-field projection;
- `DiarySaveNormalizationTests` for new saved records;
- `PawnDiary.RimTest` fixtures for hook flow, no-DLC, XML classification, save/load, and fallback
  ownership;
- existing localization/Def load checks;
- existing Tale, event-window, ritual, arrival, observed-condition, and view-model tests where
  ownership changes could regress them.

## 19. Phased implementation sequence

Each numbered phase is a separate smallest-safe change. Do not implement all releases in one diff.

### Phase A0.0 — Reconfirm baseline and freeze XML keys

> **Implementation status (2026-07-15): complete.** Local installed Defs reconfirmed all 16 ritual
> keys plus `Stirring`, `Waking`, `VoidAwakened`, and automatic `Gleaming`; tests freeze the six
> group names, orders `770–775`, two new window names, exact classifier keys, and package gates.

- Re-run CodeGraph on ritual classification and event-window ownership.
- Re-check the installed psychic ritual and monolith level defNames.
- Freeze six group defNames, two new window defNames, classifier keys, orders, and package gate.
- Add/adjust pure classifier tests before changing XML behavior.

**Exit gate:** reviewed key table and failing tests that express the intended classification.

A0.0–A0.1 remain XML-only and do not depend on Narrative Continuity. A0.2 reuses the N1 write seam
only to freeze exact, visible monolith chapter evidence on its existing pages; it still adds no live
Anomaly provider. Narrative Continuity N0–N1 must land before A1.1 creates the general Anomaly
catalog route/persistence; A1.0 may freeze its pure contracts in parallel using the canonical shared
tokens.

### Phase A0.1 — Exact ritual guidance

> **Implementation status (2026-07-15): complete.** Six exact families and English/Russian
> DefInjected text shipped; the package-gated order-`776` token fallback remains for unknown/modded
> psychic rituals, with page count and role fan-out unchanged.

- Add six XML groups and DefInjected translations.
- Preserve the generic fallback at order 776.
- Verify settings visibility with and without Anomaly.
- Update docs/changelog and run XML, pure tests, and Debug build.

**Exit gate:** page count and role fan-out unchanged; all 16 defs exact; unknown ritual fallback.

### Phase A0.2 — Monolith chapter split

> **Implementation status (2026-07-15): complete.** `VoidMonolithActivation` now owns only
> `Stirring`; exact `VoidMonolithWaking` and `VoidMonolithVoidAwakened` windows own the next two
> player-driven levels. Each saves visible `journey_chapter` evidence/reference on
> `anomaly-monolith|0`; `Gleaming` stays silent and there is still no live Anomaly provider.

- Restrict existing activation window to Stirring.
- Add exact Waking and Void Awakened windows and Keyed fallback text.
- Update interaction group matching for new stable window defNames.
- Test one signal → one window for all reached levels.
- Update docs/changelog and build.

**Exit gate:** discovery plus three distinct activation meanings, no generic duplicate, no Gleaming
page.

### Phase A1.0 — Pure contracts, policy Def, and tests

> **Implementation status (2026-07-20): complete.** `DiaryAnomalyPolicyDef` and its primitive-only
> XML row copy into a detached snapshot with conservative fallbacks; plain study/containment/Tale
> DTOs and pure planners freeze observation-versus-generation, deterministic writer, bounded entity,
> exact fail-open ownership, synthetic event, and prompt-token contracts. The Def adapter returns an
> independently normalized snapshot and caps promotion rows at 128. The new assembly-free suite passes
> 211 assertions. The existing 291-test RimTest assembly adds only read-only singleton-Def/default/
> snapshot-isolation assertions. A manual main-menu run passed that fixture among all 46 non-colony
> tests; 245 loaded-game fixtures rejected the absent game, leaving the full loaded-colony run pending.
> A1.0 adds no catalog route, Scribe key, runtime hook, signal, page, group, setting, or live Anomaly
> read.

- Add policy schema/defaults.
- Add plain DTOs and pure study/breach/Tale-ownership policies.
- Create standalone test project before live patches.
- Freeze synthetic event keys and prompt token vocabulary.

**Exit gate:** exhaustive pure tests pass without RimWorld assemblies.

### Phase A1.1 — Catalog route and persistence

> **Implementation status (2026-07-20): complete and green in-game.** The
> shared `AnomalyEvent` envelope/spec is registered and rejects unknown kind/Def pairs, unverified
> sources, hidden/replayed events, malformed identity, and missing eligible writers. Six additive
> save keys own normalized study history and one optional monolith-knowledge snapshot. New games
> start from trustworthy empty history; pre-A1 saves with Anomaly active scan loaded study comps once
> and deliberately mark incomplete history as already observed, preferring silence over a false
> first. The bounded consume-once study/Tale cache is unsaved and clears at every game/load/finalize
> boundary.
>
> Five exact package-gated XML groups/settings plus English/Russian DefInjected and Keyed fallbacks
> are present. Their dedicated classifier accepts only available exact-name rows and cannot fall
> through to any Interaction token/prefix/suffix/segment/package/batch/catch-all matcher.
> N3-A is wired as an explicit zero-candidate provider. At the A1.1 boundary there was still no
> Anomaly Harmony registration, signal, page emission, tick work, or hidden-state projection.
> Focused suites pass
> 320 Anomaly, 708 catalog, 83 save-normalization, and 135 Narrative assertions; the runtime and
> 295-test RimTest assemblies build. Three loaded fixtures now exercise actual component Scribe,
> missing-key normalization, deep snapshot state, deferred no-DLC migration, one-time active-DLC
> baseline, and transient reset. Actual execution totals are 1/1 passed, 0 failed for the
> no-Anomaly main-menu exact-classifier fixture; user-confirmed 3/3 passed, 0 failed for the loaded
> no-Anomaly state fixtures; and user-confirmed 3/3 passed, 0 failed for the same loaded fixtures
> with Anomaly active. These 7/7 executions cover the exact classifier, real six-key Scribe
> round-trip, missing-key defaults, DLC-off deferred migration, DLC-on one-time baseline, and
> transient reset.

- Add/register `AnomalyEvent` catalog type.
- Add saved study/monolith state and normalization.
- Add cache clear/reset lifecycle.
- Add XML groups/settings/localization but keep live sources unregistered until their tests exist.

**Exit gate:** catalog contract, save normalization, old-save baseline, no-DLC Def load, and build
pass.

### Phase A1.2 — Study capture

> **Implementation status (2026-07-20): code-complete; focused in-game execution evidenced by the
> later full active-suite run.** The
> exact public study overload registers defensively only with Anomaly active. Prefix/postfix live
> reads are centralized in `DlcContext` and converted into detached facts; pure policy owns semantic
> stages and history. `AnomalyStudySignal` writes only for the exact eligible studier, while the
> bounded consume-once cache suppresses only its matching `StudiedEntity` fallback after a dedicated
> page was actually created. Exact vanilla job identity keeps a slow study job authoritative beyond
> the fallback tick window without allowing a later job on the same pair to borrow ownership.
> Monolith study remains state-only and its bounded snapshot is consumed
> by the next exact activation; automatic activation advances state without a page. Prompt context
> contains semantic/visible facts and excludes raw progress, private thresholds, hidden abilities,
> containment chances, and undiscovered codex prose.
>
> `DiaryAnomalyPolicyTests` passes 362 assertions. Ten focused RimTests bring the assembly to
> 305 compiled tests and cover hook registration/inertness, no threshold, one threshold, multi-note
> jump, completion, disabled group, real Scribe/no replay, exact consume-once Tale ownership, delayed
> exact-job ownership, and fail-closed missing monolith-level state.
> Core and RimTest assemblies built at that boundary. The later user-confirmed corrected 315/315
> Anomaly-active full-suite result corresponds to the unchanged 315-fixture assembly whose source
> inventory contains all ten A1.2 fixtures. This is aggregate user-confirmed evidence, not a preserved
> per-method run artifact; it closes this row under the project's acceptance of that reported full-suite
> result without treating compilation alone as execution.

- Register exact public study hook defensively.
- Capture before/after facts and run pure policy.
- Implement exact author signal, history independence, monolith state-only enrichment, and matching
  `StudiedEntity` suppression.
- Add focused RimTests for no threshold, one threshold, progress jump, completion, disabled setting,
  and save/load.

**Exit gate:** rare milestone pages only; generic Tale preserved whenever dedicated ownership did
not succeed.

### Phase A1.3 — Containment breach

**Status (2026-07-20): implemented and adversarially hardened.** The exact Anomaly-active
`Escape(bool initiator)` seam now has a defensive prefix/postfix/finalizer registration. One bounded,
reentrancy-safe outer scope aggregates verified nested same-room escape calls; a separate detached
recent-study cache preserves truthful writer correlation after Tale ownership is consumed. Pure and
compiled loaded-game fixtures cover aggregation, caps, deterministic writers, exact deduplication,
silent release siblings, exception/lifecycle cleanup, and no-DLC registration. The new loaded fixtures
now accept any clean vanilla room, including open terrain, rather than depending on player-built or
roofed structures. Their allocator validates the complete holding-platform footprint, reserves
non-overlapping margins, and rejects map-edge, pawn, edifice, destructive-wipe, and occupied-platform
placements; failed setup cannot be masked by teardown. The first loaded attempts exposed the former
indoor-room and center-only placement assumptions. The corrected user-confirmed Anomaly-active loaded
rerun passed the original 9/9 with 0 failures. A later full 315-fixture Anomaly-active run passed 314,
including the new direct scope-state fixture; its sole failure was a false-positive fallback assertion
when a valid visible label contained the stable Def name. The fixture now requires exact equality with
the localized visible-label-only fallback. The user-confirmed corrected Anomaly-active rerun passed
all 315 compiled fixtures, including all ten containment fixtures. The separate Anomaly-inactive
profile remains in the manual in-game matrix.

- Add scoped aggregation around `Escape(bool)`.
- Verify nested joins, intentional release silence, deterministic writers, and exception cleanup.
- Add dedup ownership and bounded context.
- Add manual/dev trigger only behind existing dev tooling, not production UI.

**Exit gate:** one logical event for a cascade, zero for intentional release, no leaked scope.

### Phase A1.4 — A1 hardening and delivery

> **Status (2026-07-20): implementation and automated delivery complete; manual profiles remain
> explicit.** Adversarial review found three bounded defects: blank study labels could reach player-
> facing fallback as raw Def names; corrupt saved histories could inspect more than 4,096 invalid or
> duplicate rows; and outer containment capture rescanned the recent-study cache per eligible pawn and
> sorted the full eligible roster twice. The fallback now uses a localized neutral subject, history
> input inspection is capped at 4,096, recent-studier IDs are resolved in one bounded batch, and the
> full roster is ranked once before the writer derivative is sorted from at most 512 rows. Focused pure
> coverage passes 404 Anomaly and 84 save-normalization assertions. Runtime and 316-fixture RimTest
> assemblies build with 0 warnings and 0 errors. The full repository verifier passes whitespace/XML,
> all 14 pure projects, the runtime rebuild, and committed-DLL freshness. No A2 behavior or hidden-state
> projection was introduced.
>
> The previous user-confirmed corrected 315/315 Anomaly-active full-suite result remains aggregate
> evidence for the unchanged assembly containing all prior A1.2/A1.3 fixtures; no per-method run artifact
> survives.
> The user confirms the complete automated A1.4 Anomaly-active 316-fixture run green. This is aggregate
> user-confirmed evidence; no preserved per-method log is claimed. The separate Anomaly-inactive
> profile, disposable missing study/containment-hook compatibility profiles, and a real process-boundary
> save/reload remain explicit deferred rows. The exact procedure is recorded in
> `tests/SAVE_COMPATIBILITY_SMOKETEST.md`.

- [x] Audit no-DLC gates, independent missing-hook failure, and generic fallback preservation.
- [x] Profile hot paths/cache bounds and add focused bound/equivalence tests.
- [x] Review prompts/fallback for spoilers, invented causality, role truth, localization, and raw Defs.
- [x] Complete docs/changelog/localization and rebuild both assemblies.
- [x] Record the user-confirmed automated active 316-fixture run as aggregate green evidence.
- [ ] Execute the separate Anomaly-inactive, missing study/containment-hook, and process-boundary profiles.

### Phase A2.0 — Visible creepjoiner state

> **Status (2026-07-20): implemented, adversarially hardened, and loaded-accepted within the later
> 335-fixture aggregate run.**
> The canonical arrival now upserts one visible joined arc and attaches its event ID only after the
> existing arrival page is created. Schema 2 deep-scribes only pawn/arrival/joined/visible-phase/
> visible-event/terminal/version primitives; pure normalization covers malformed, duplicate, oversized,
> negative-tick, invalid-phase, and future rows under 4,096-input/512-output caps. Active old saves
> silently baseline current joined player creepjoiners; inactive saves remain pending.
>
> Independent fail-open registrations target the installed public parameterless `DoRejection`,
> `DoAggressive`, and `DoLeave` methods and cache their required private transition fields once.
> Detached before/after verification commits terminal visible history independently of settings. Pure
> selection uses exact speaker, eligible pre-departure subject, or one closest nearby witness as the
> phase permits; exact speaker requires same-map presence, and no RNG or now-hostile first-person POV
> is used. Visible rejection owns its nested exact response; aggressive rejection records the strongest
> visible hostile phase, letterless modded rejection releases a nested visible owner, and unpatched
> `DoDownside` lets its nested exact method own naturally. Committed but unverified/invisible markers
> close as blank terminal barriers. Context/fallback expose only generic visible phase/result, optional
> visible rejection provenance, subject identity/label, role, and terminal state.
>
> Focused suites pass 481 Anomaly and 122 save-normalization assertions. Eleven loaded A2.0 fixtures
> bring the RimTest assembly to 327 compiled tests and cover exact registration/no-`DoDownside`, one
> canonical/repeated arrival, rejection-with-nested-departure once, aggression, joined departure,
> aggressive rejection, letterless nested ownership, disabled-output state, live legacy baselining,
> disabled/no-op silence, role context, repeat suppression, and lifecycle cleanup. The first active run
> passed 321/323 overall; its two failures were test-only recipient mismatches after each solo page had
> already been counted. Rejection/aggression now assert a blank recipient role and the subject ID in
> captured context. The later user-provided 335-fixture full run passed every A2.0 fixture, closing the
> expanded 327-fixture acceptance debt as aggregate evidence. The user-confirmed A1.4 active 316 run
> is green aggregate evidence; the separate Anomaly-inactive profile, missing study/containment-hook
> compatibility profiles, and real
> process-boundary save/reload remain deferred.

- [x] Add saved visible-only arc records and silent old-save baselining.
- [x] Reuse arrival to initialize state.
- [x] Add pure phase arbitration/writer tests.
- [x] Register rejection/aggression/departure methods with private-field health checks.

**Exit gate:** no secret field enters state or prompt; nested visible outcomes emit once.

### Phase A2.1 — Surgical disclosure

> **Status (2026-07-20): implemented; latest expanded loaded run passed 334/335, with a corrected
> test-only context-key rerun pending.** A composite
> Anomaly-gated registration pins the exact installed public recipe, creepjoiner tracker, and Pawn
> inspection-result signatures. The bounded recipe scope accepts disclosure only when the exact tracker
> returns true and grows its builder, the overall Pawn result is letter-visible `Detected`, and the
> recipe returns normally. Only generic booleans and detached subject/surgeon identity, visible labels,
> eligibility, and tick cross the DLC boundary; appended letter prose and hidden tracker configuration
> are neither copied nor saved.
>
> Pure planning commits one nonterminal `surgical_reveal` / `disclosed` phase independently of output,
> selects the exact eligible surgeon first and exact eligible subject second under the XML writer cap,
> never selects a nearby witness, suppresses replay, and leaves later terminal outcomes available. The
> existing seven-field creepjoiner row and per-row schema version remain unchanged. Current load
> normalization preserves the new phase, while an A2.0 downgrade safely normalizes the then-unknown
> nonterminal phase back to `joined`.
>
> `DidSurgery` is deferred only inside the exact active surgeon-first/subject-second recipe scope and
> suppressed only after the dedicated page is actually created. “Nothing found”, `DetectedNoLetter`,
> surgery failure, exceptions, signature/correlation mismatch, expired/closed ownership, disabled output,
> and no-author cases release the ordinary `TaleSignal`; vanilla's historical Tale is untouched. Context
> and English/Russian fallback reveal only the generic disclosure and exact visible roles, never the
> benefit/downside identity, appended letter text, hidden host state, motive, or terminal outcome.
>
> Review hardening rejects unrelated Tales before taking the XML policy snapshot, treats a created
> dedicated page as the owner even if defensive event-ID attachment cannot finish, fails open on a
> mismatched existing arc identity, clears event IDs from merged blank replay barriers, and removes an
> ambiguous pronoun from the Russian subject fallback. Focused pure suites pass 532 Anomaly and 135
> save-normalization assertions. Eight new loaded fixtures,
> plus expanded registration/lifecycle assertions, bring RimTest to 335 compiled tests. They cover a real
> successful recipe, surgeon/subject pair and surgeon-only pre-join POVs, nothing-found and disabled-output
> generic-Tale fallback, an early recipe exit without tracker evidence, exception-finalizer fail-open,
> unscoped ownership fallback, later terminal continuity, exact composite patch ownership, and lifecycle
> cleanup. The first user-provided 335-fixture run passed 333 and failed only its two joined-subject
> pair assertions after each strict guard had already counted exactly one dedicated event. The shared
> fixture setup still forced A2.0's old one-writer cap, so A2.1 correctly produced a surgeon-only page;
> setup now uses the supported two-writer ceiling. This closes the embedded A2.0 327-fixture debt. The
> next user-provided run passed 334/335 and confirmed that cap correction; its sole failure was a live
> fixture expecting shortened `initiator_role` / `recipient_role` keys after the exact pair page had
> already been found. The frozen schema and pure suite use `initiator_witness_role` /
> `recipient_witness_role`, which the fixture now asserts. The corrected 335-fixture rerun remains
> pending.

- [x] Add recipe/tracker correlation and successful disclosure verification.
- [x] Implement exact `DidSurgery` ownership with fail-open behavior.
- [x] Add surgeon/subject POV tests and “nothing/failure” silence tests.

### Phase A2.2 — Ghoul transformation

- Add recipe prefix/postfix transition capture through `DlcContext`.
- Emit one identity event and own the matching Tale only after success.
- Add failure, already-ghoul, ineligible POV, save/load, and no-DLC tests.

### Phase A3.0 — Terminal void outcome

- Register both public terminal methods defensively.
- Add exact pre/post verification, saved terminal token, and Tale fallback ownership.
- Add optional existing-scheduler reflection request after canonical event success.
- Test embrace, disrupt, failed verification, patch unavailable, old ended save, and repeat calls.

**Exit gate:** one exact ending, never zero because of ownership failure and never two because of
Tale/level duplication.

## 20. Test plan

### 20.1 Pure policy tests

`DiaryAnomalyPolicyTests` should cover at least:

#### Ritual/classification

- all 16 exact mappings;
- unknown psychic fallback;
- package absent;
- exact order/no overlap;
- blank/malformed classifier key.

#### Study

- null/blank facts drop;
- no progress transition is state-only/drop;
- first breakthrough authorizes once;
- disabled settings still update history;
- completed kind authorizes once per stable defName;
- second instance of same defName stays state-only;
- multi-note jump emits once;
- monolith note state-only;
- false → true activatable captured for enrichment;
- XML exact promotion and malformed promotion;
- ineligible studier updates state but creates no page;
- suppression key exact pair/mismatch/expiry/consume-once.

#### Containment

- empty/invalid scope drops;
- one escape;
- nested two/three-entity aggregation;
- duplicate nested entity removed;
- entity not verified escaped removed;
- label cap and additional count;
- deterministic candidate ranking;
- witness radius boundary;
- writer cap 0/1/2/out-of-range normalization;
- no eligible writer;
- same input never consumes RNG and yields same plan;
- dedup key differs by map/outer escape identity.

#### Creepjoiner

- no transition drops;
- visible rejection/aggression/departure accepted;
- outer downside plus nested exact outcome emits once;
- surgical disclosure true/false;
- hidden outcome denied even if a malformed XML promotion asks for it;
- subject/surgeon/witness writer ordering;
- terminal phase cannot replay;
- state serialization contains no secret token.

#### Ghoul/void/Tale ownership

- failed and already-ghoul transitions drop;
- exact non-ghoul → ghoul generates;
- exact Tale pair required;
- ownership mismatch/expiry/failure releases fallback;
- embraced method/level match;
- disrupted method/level match;
- contradictory method/level drops and fails open;
- terminal outcome records once.

### 20.2 Existing pure suites

Update and run at minimum:

```powershell
dotnet run --project tests/DiaryAnomalyPolicyTests/DiaryAnomalyPolicyTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiarySaveNormalizationTests/DiarySaveNormalizationTests.csproj
dotnet run --project tests/DiaryObservedConditionTests/DiaryObservedConditionTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
```

Run the repository's full `.githooks/verify.ps1` before delivery because XML/localization and
unrelated shared catalog contracts can fail outside the focused suites.

### 20.3 Focused RimTests

Add loaded-game fixtures for:

- exact psychic classifier groups with Anomaly active;
- package gate without Anomaly;
- each monolith activation signal matching one window;
- study threshold before/after capture and exact studier;
- progress jump and completion history;
- matching/mismatching `StudiedEntity` ownership;
- containment outer/nested aggregation and scope cleanup;
- intentional ejection silence;
- deterministic breach writers;
- creepjoiner arrival state without a second page;
- visible surgical disclosure and failed/nothing inspection;
- visible rejection/aggression/departure;
- hidden metalhorror path producing no reveal context;
- successful/failed ghoul infusion and Tale ownership;
- exact embraced/disrupted terminal outcomes and Tale fallback;
- save/load normalization and old-save baselining;
- every defensive patch missing-target path logging once and leaving the rest of the mod usable.

Build the in-game suite after the core:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
MSBuild tests\PawnDiary.RimTest\PawnDiary.RimTest.csproj /t:Build /p:Configuration=Debug
```

### 20.4 Manual acceptance run

For each shipped release, perform a small in-game run with generation disabled first so localized
fallback truth is visible, then one with normal generation:

- verify exact labels/instructions in the settings preview;
- inspect page count and POV roles;
- save before the event, trigger, save after, reload, and confirm no replay;
- toggle the relevant group off/on and confirm history does not reset;
- inspect dev prompt/context for forbidden secret/mechanics fields;
- repeat with Anomaly inactive and confirm silence/no settings row for new package-gated groups;
- inspect the log for one-time defensive warnings only.

## 21. Acceptance scenarios

### Scenario A — ritual meaning without added volume

The same four pawns complete Philophagy and later Blood Rain. Each completion produces the same
number of role-specific pages as today, but Philophagy guidance centers taking from a target while
Blood Rain centers deliberate environmental horror. Neither claims an unverified later casualty.

### Scenario B — future ritual fallback

A mod adds `PsychicRitual;SingToTheCube`. It matches none of the exact families and still reaches
`ritualAnomalyPsychic`. No code edit or crash is required.

### Scenario C — monolith chapters

One pawn discovers the monolith and later activates all three player-driven levels. The diary can
contain discovery, Stirring, Waking, and Void Awakened once each, with distinct truth. No Gleaming
page appears and no activation creates both exact and generic rows.

### Scenario D — ordinary study versus breakthrough

A researcher performs repeated ordinary study interactions. No page appears until the first valid
threshold/authorized completion. Later ticks remain silent. Completing the same entity kind on a
second specimen updates no false first and creates no duplicate completion page.

### Scenario E — study plus Tale

A qualifying final entity-study threshold produces one researcher page. The same job later records
`StudiedEntity`; exact correlation suppresses it. A different researcher's unrelated Tale remains
untouched.

### Scenario F — containment cascade

One entity escapes and draws two same-room entities out. The diary records one breach with a bounded
entity list and at most two witnesses. Later one colonist is injured; that injury remains eligible as
a separate consequence.

### Scenario G — intentional release

The player ejects an entity from a holding platform. No containment-breach page appears, even if the
entity later becomes hostile through another exact source.

### Scenario H — hidden creepjoiner downside

A joined creepjoiner carries the metalhorror downside. Arrival records only `creepjoiner=true`.
Neither saved state nor generated prompt names the implant/host before vanilla reveals it. Existing
spoiler-safe suspicion/emergence systems continue normally.

### Scenario I — surgical disclosure

An eligible surgeon successfully inspects a creepjoiner and vanilla visibly discloses abnormal
facts. One reveal event records surgeon/subject roles and the generic surgery Tale does not compete.
A failed inspection or “nothing found” creates no reveal page.

### Scenario J — ghoul infusion

An eligible surgeon successfully turns a colonist into a ghoul. One irreversible transformation
event records exact roles; `DidSurgery` is not duplicated. A failed operation leaves no page or stale
suppression.

### Scenario K — final choice

One pawn embraces the void in one test and disrupts it in another. Each save produces exactly one
correct terminal page. Disrupt never says embrace; embrace never says sever. If the dedicated hook
is deliberately unavailable, the existing exact Tale still records a fallback page.

### Scenario L — old save and no DLC

An old Anomaly save already contains study progress, ghouls, a joined creepjoiner, and a terminal
monolith. First load writes nothing retroactively. A separate base-game-only save loads with no
errors, no Anomaly state reads, no new Anomaly settings rows, and unchanged diary behavior.

## 22. Performance, DLC safety, and failure handling

### 22.1 Performance budgets

- A0 adds only Def classification rows; classification is already memoized.
- Study work runs only when vanilla crosses the public `OnStudied` callback; prefix guards precede
  label/context work.
- Containment work runs only on actual `Escape`, not each `CompTick` interval.
- Creepjoiner, ghoul, and terminal work runs only on exact public lifecycle methods.
- No new every-tick or every-pawn scanner is required.
- The outer containment call enumerates the affected map's spawned pawns once, scans the at-most-512
  recent-study cache once, ranks the eligible roster once, and caps retained cascade candidates at
  512; writer derivation sorts only that retained pool. Nested calls reuse it. Final output retains at
  most 64 escaped entities, 8 visible labels, and 2 writers.
- Reflection metadata is resolved once at registration and cached; never look up private fields per
  event.
- Transient caches have hard caps/expiry and compact without unbounded queues. Saved Anomaly history
  normalization inspects and retains no more than 4,096 rows per list, even when every row is malformed
  or duplicated.
- Pure selection uses stable sorting/hashing and does not consume simulation RNG.

### 22.2 DLC safety checklist

- Gate every new live callback on `ModsConfig.AnomalyActive` before DLC content reads.
- Put raw pawn creepjoiner/mutant reads behind `DlcContext` with a second null/state guard.
- Do not add a DLC dependency to `About/About.xml`.
- Use official package gating for new XML settings groups.
- Match Anomaly Defs by stable strings in our XML; do not use throwing `GetNamed` calls.
- If an XML node ever references an actual Anomaly Def, add
  `MayRequire="Ludeon.RimWorld.Anomaly"`; prefer string keys so that is unnecessary.
- Remember that compiling against the shared assembly proves nothing about no-DLC runtime safety.
- Add explicit no-Anomaly RimTests for accessors, Def availability, and patch registration.

### 22.3 Defensive patching

Register new hooks through one `DiaryAnomalyPatches.TryRegister(Harmony)` entry called from
`DiaryPatchRegistrar`:

- resolve exact type, method, overload, and any private field once;
- register only while Anomaly is active;
- log one precise warning for a missing source;
- set a capture-capability token only after the hook is actually healthy;
- disable only the affected subfeature when a hook is missing;
- preserve current generic ritual/Tale/observed-condition fallback routes;
- wrap callbacks in `DiaryPatchSafety.Run` and ensure correlation cleanup.

Public stable methods may technically support attribute patches, but the manual registrar gives
optional DLC features consistent health reporting and fail-open Tale ownership.

### 22.4 Thread and text safety

All hooks and `.Translate()` calls run on RimWorld's main thread. Background generation receives
only already-localized/sanitized plain strings. No cache retains live game objects for the LLM
thread.

### 22.5 Failure behavior

| Failure | Required behavior |
|---|---|
| Policy Def missing/malformed | Safe fallback values; clamp; one warning if useful |
| Study hook missing | No new milestones; generic `StudiedEntity` remains |
| Containment hook missing | No breach page; existing letter/atmosphere remains |
| Creep private field renamed | Disable only affected phase; never guess transition |
| Surgery correlation lost | Do not suppress generic `DidSurgery` |
| Terminal hook missing | Existing `ClosedTheVoid`/`EmbracedTheVoid` Tale remains |
| Actor/writer cannot resolve | Update truth/history; skip page rather than choose falsely |
| XML translation missing | Verification fails before release; do not ship placeholder English in code |
| Exception inside callback | Log through patch safety, clear transient scope, preserve game behavior |

## 23. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Exact ritual split creates settings clutter | Official package gate; six semantic families rather than 16 rows |
| Ritual prompt claims outcome not proven | Family instructions describe intention/completion; retain exact perspective and forbid downstream assertions |
| Monolith exact windows duplicate generic rule | Make rules mutually exclusive; test every reached level against all windows |
| Study creates too many pages | First/completed/promoted policy; monolith notes state-only; per-def history |
| Old save manufactures first breakthrough | Conservative schema baseline and no catch-up pages |
| Generic Tale disappears when dedicated event fails | Conditional, exact, fail-open ownership only after healthy correlation |
| Recursive containment emits one page per entity | Outer-scope depth aggregation and emit-once postfix |
| Release is mislabeled as escape | Hook `Escape(bool)`, never `EjectContents`/release callbacks |
| Breach prose blames a witness | Plain candidate roles and explicit no-causality prompt contract |
| Creepjoiner support leaks spoilers | Visible-only saved schema; no general downside page; code-level hidden denial |
| Aggression/leave public method returns without transition | Before/after flag/state verification through defensive cached fields |
| Ghoul page fires on failed surgery | Verify false→true ghoul state after normal return |
| Terminal page records before delayed choice resolves | Hook terminal utilities, not dialog/private delayed callback |
| Another mod patches the same source | Narrow prefix/postfix, normal-return checks, exact state verification, fail-open fallback |
| Cross-DLC plan dependency expands scope | Optional facets only; no assumption that another plan exists in code |

## 24. Deferred follow-ons

Review separately after shipped telemetry/manual play confirms page volume:

- selected researcher attribution for codex discovery through a proven same-call study correlation;
- an XML allowlist of visible `DoDownside` outcomes with hard code exclusions for hidden cases;
- exact creepjoiner benefit disclosure where vanilla exposes it safely;
- capture/re-capture chapters only if an exact human actor and story-sized transition can be found;
- a rare containment-recovery chapter linked to a prior breach;
- entity-specific study families after validating localization and spoiler boundaries;
- one post-ending colony reflection for non-actor survivors;
- a coherent ghoul lifecycle only if it can avoid upgrade/combat spam;
- explicit Anomaly/Ideology cross-DLC belief reactions after the Ideology facet seam actually ships;
- automatic Gleaming context if a human-readable, non-duplicative owner is later identified.

None of these is required for A0–A3, and none should be smuggled into an implementation phase
without its own source/ownership/noise review.

## 25. Coding-agent handoff checklist

Before beginning any phase:

- [ ] Read `AGENTS.md`, the engineering skill, and the relevant lore files.
- [ ] Inspect `.codegraph/` and use CodeGraph before grep/file reads for source discovery.
- [ ] Re-check git status and preserve unrelated user changes/untracked plans.
- [ ] Reconfirm installed RimWorld build and every exact target signature/defName.
- [ ] State the smallest phase being implemented; do not combine releases by default.
- [ ] Freeze new stable Def names, Scribe keys, synthetic event keys, and prompt tokens before code.

For every implementation diff:

- [ ] Gate Anomaly live reads before touching DLC state.
- [ ] Keep raw Anomaly pawn reads in `DlcContext`.
- [ ] Copy live state into plain DTOs before pure policy.
- [ ] Put tunable values and prompt prose in XML.
- [ ] Add novice-friendly file headers, public summaries, and non-obvious lifecycle comments.
- [ ] Prove event ownership and exact duplicate behavior.
- [ ] Prove settings-independent history and old-save silence.
- [ ] Prove transient scopes clear on success, mismatch, expiry, load, and exception.
- [ ] Add/update standalone pure tests before relying on RimTests.
- [ ] Add focused RimTests for live hooks and no-DLC behavior.
- [ ] Update `DOCUMENTATION.md` and add a dated `CHANGELOG.md` line for shipped behavior.
- [ ] Validate XML and localization.
- [ ] Run focused suites, full verification, and Debug MSBuild.
- [ ] Confirm `1.6/Assemblies/PawnDiary.dll` changed only when C# changed and is staged intentionally.
- [ ] Re-read generated prompt facts for spoilers, mechanics dumps, false causality, and wrong POV.

Release decision:

- [ ] A0 may ship without A1–A3.
- [ ] A1 study and containment may ship separately if each owns its duplicates and save state.
- [ ] A2 creepjoiner and ghoul slices may ship separately.
- [ ] A3 may ship only with verified fail-open Tale ownership.
- [ ] Deferred ideas remain deferred unless their own exit gate is added and reviewed.
