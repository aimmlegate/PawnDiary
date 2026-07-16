# Pawn Diary — Biotech Support Implementation Plan

> **Status:** Phases 0–3 plus Narrative N2-B implemented in Master Wave 3, 2026-07-16. Phase 4's
> automated release-hardening slice is implemented: important templates project bounded B1 facts,
> Full/Balanced/Compact and indexed localization contracts are pinned, localized dev previews exist,
> pending owner lists have XML-owned defensive caps, no-DLC metadata is asserted, and SpeakUp/RimTalk
> logic/build smoke checks pass. The loaded-game no-DLC/old-save/growth/birth/adapter matrix remains
> the Phase 4 exit item; B2 must not begin before it is accepted. Canonical age-7/10/13 growth
> observation, postponed-choice persistence, auto resolution, ordinary Birthday fallback, progression
> consumption, saved family arcs, exact lesson/play evidence, truthful child/supporter writer shapes,
> canonical birth/naming ownership, mature-source arbitration, and source-owned N1 evidence are live and
> no-DLC-safe. N2-B adds bounded exact family/current visible identity lenses through the shared fixed
> provider list.
>
> **Scheduling authority:** implement Biotech phases only in the waves assigned by
> `DLC_SUPPORT_MASTER_IMPLEMENTATION_PLAN.md`; this file remains the technical authority for Biotech.
>
> **Decision:** Biotech family/growth is the first flagship DLC arc after the shared foundation and
> low-risk Anomaly semantic pass, as assigned by the master order. This is consistent with
> `design/DLC_ATMOSPHERE_RESEARCH.md`, which identifies growth moments and family continuity as the
> highest-value remaining DLC arc.
>
> **Compatibility target:** RimWorld 1.6, base-game-safe. Biotech enrichments must cleanly no-op when
> Biotech is inactive and must not add a Biotech dependency to `About/About.xml`.
>
> **Shared continuity:** growth, family, mechanitor, gene, bond, and pollution pages participate
> through `DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md`. Biotech remains the source owner;
> the shared layer owns cross-DLC selection, references, and reflection coordination.

This plan turns the broad DLC atmosphere research into reviewable implementation slices. It is
intentionally concrete about event ownership, save state, hook timing, prompt facts, settings,
duplicate suppression, and tests. A coding agent should still implement one phase at a time and
stop at every exit gate.

## 1. Review outcome and recommended release boundary

Biotech is too broad for one safe release. The recommended program has three independently
shippable boundaries:

### B1 — Growth moments and family continuity

This is the required first release and the reason to do Biotech next.

Split B1 into two mergeable sub-slices:

- **B1a — canonical growth moments:** capture the committed age-7/10/13 choice, merge birthday,
  trait, passion, and work-unlock signals into one event, and optionally give one observed parent or
  teacher a second perspective.
- **B1b — persistent family continuity:** keep child/parent identity by stable IDs from pregnancy or
  birth through naming, teaching, and later growth moments; replace disconnected birth tale,
  thought, and ritual pages with one canonical family birth event.

B1a may merge and ship before B1b if its fallback and save/load tests pass. B1b then enriches later
growth pages without changing B1a's event identity.

### B2 — Inherited identity and mechanitor responsibility

B2 adds two separate features that may ship independently:

- salient gene identity and exact xenogerm-change ownership;
- a human-centered mechanitor arc: mechlink, first controlled mech, first consequential mech combat,
  significant mech loss, and called boss chapters.

B2 must not block B1. It reuses B1's lessons—event-time snapshots, stable IDs, exact owner events,
and setting-independent observation—but does not share its save records.

### B3 — A changing environment and exceptional biological bonds

B3 contains lower-frequency atmosphere work:

- pollution thresholds and reclamation through the observed-condition system;
- psychic-bond creation and rupture;
- first severe interrupted deathrest;
- optional first severe hemogen crisis after a separate noise review.

B3 is explicitly not required for the first Biotech release.

### Review decisions that are now frozen

1. A growth moment is recorded **after choices are committed**, not when the birthday fires.
2. A growth birthday produces at most one canonical event. It cannot also produce separate birthday,
   selected-trait, passion-skill, or work-unlock pages.
3. Family observation is saved and keyed by pawn IDs. It does not depend on the `teaching` group,
   generated prose, or an eligible child diary record.
4. Birth is correlated at `PregnancyUtility.ApplyBirthOutcome`; this is the one boundary that knows
   outcome, child, birther, genetic mother, father, doctor, vat/ritual path, and immediate health
   consequence together.
5. Naming may delay birth-page generation, but never delays or loses the saved family arc.
6. Gene prompts receive two to four selected themes, never the whole gene list.
7. Mechanitor pages belong to the human controller. Routine mechs, bandwidth changes, gestation,
   charging, and work-mode changes remain silent.
8. Pollution is persistent atmosphere with bounded threshold pages, not one page per wastepack or
   polluted cell.
9. Cross-DLC primitives such as `identity_transition`, `bond_lifecycle`, and `ambient_pressure` are
   optional context facets only. They are not generic page-producing event types.

## 2. Product outcome

After B1, a child growing up in the colony should read as one life rather than several unrelated
notifications:

- pregnancy and labor identify the same family arc where exact parent facts exist;
- birth is remembered by up to two appropriate adults without duplicating the childbirth ritual,
  `GaveBirth` tale, and `BabyBorn` thoughts;
- the child's eventual name and stable parent IDs survive save/load;
- ordinary play and lessons accumulate quietly as upbringing evidence;
- at ages 7, 10, and 13, the actual selected trait and passions become one strong page;
- the page can mention the breadth of opportunity and remembered teaching without quoting growth
  tiers, choice counts, percentages, or work restrictions;
- one parent or demonstrably involved teacher may receive a companion perspective;
- age 13 naturally uses the adult voice-stage transition already owned by Pawn Diary.

After B2, bodily identity and mechanitor responsibility should also have continuity:

- a custom xenotype is described through its most consequential themes rather than only its label;
- xenogerm implantation records the actual before/after change once;
- a mechlink begins a controller arc;
- first command, first consequential combat, long-serving loss, and boss chapters remember the same
  mechanitor and mech IDs.

After B3, pollution and exceptional biological bonds should color ordinary entries and create only
rare threshold/lifecycle pages.

The result should still feel like a diary, not a Biotech activity log.

## 3. Scope and non-goals

### 3.1 Required B1 scope

- Guarded Biotech snapshot helpers in `DlcContext`.
- Exact growth capture around `Pawn_AgeTracker.BirthdayBiological`,
  `ChoiceLetter_GrowthMoment.ConfigureGrowthLetter`, and
  `ChoiceLetter_GrowthMoment.MakeChoices`.
- Saved pending growth state for postponed letters and save/load.
- A pure growth diff, opportunity-band policy, supporter selector, and context formatter.
- One concrete `GrowthMomentEventData`/Spec/Signal/catalog route.
- Immediate progression-baseline consumption for growth-added traits and newly passionate skills.
- Saved `BiotechFamilyArcState` keyed by stable IDs, even while the child is younger than diary age.
- Pregnancy/labor observation and context enrichment when exact `HediffWithParents` facts exist.
- Exact birth correlation and a saved pending birth while naming is unresolved.
- One concrete `FamilyBirthEventData`/Spec/Signal/catalog route.
- Quiet lesson/play observation from exact XML policy, independent of the teaching page setting.
- Birth/growth prompt groups, settings behavior, English keys, DefInjected text, tests, docs,
  changelog, and rebuilt DLL for each shipped code slice.

### 3.2 B2 scope

- Plain active-gene projection and a deterministic XML-tuned salience selector.
- Event-relative gene delta selection for xenogerm implantation/reimplantation.
- A fallback gene/xenotype observer with an explicit old-save baseline.
- Mechlink install/remove, first overseer relation, first consequential controlled-mech combat,
  named/long-serving mech loss, boss call, and boss defeat.
- Saved per-pawn gene observation and mechanitor arc state.
- Exact duplicate arbitration against progression, ability, tale, raid, death, and thought routes.

### 3.3 B3 scope

- A cheap map-pollution observer using the already-maintained world-tile pollution fraction.
- XML thresholds for meaningful, severe, and critical pollution plus reclamation.
- Highest-band-only prompt influence and bounded map witnesses for optional pages.
- Psychic-bond formation/rupture with canonical pair deduplication.
- First severe interrupted-deathrest capture with cooldown/lifetime policy.
- Optional severe hemogen state only if playtesting proves it does not duplicate the existing
  `DiaryEnchant_HemogenCraving` prompt context.

### 3.4 Explicit non-goals

- No raw gene list in a prompt or diary page.
- No page for each growth point, learning-need change, lesson, baby play interaction, feeding,
  crying episode, or childcare job.
- No page for each pregnancy trimester unless a later, separately reviewed policy justifies it.
- No automatic assertion that a parent wanted a pregnancy, enjoyed a birth, or was a good parent.
- No legal/guardian claim inferred from proximity or one lesson. Use `teacher` or `supporting_adult`
  when only observed activity is known.
- No page for every mech gestated, repaired, drafted, charged, assigned, or disconnected briefly.
- No bandwidth-delta, control-group-count, or apparel-stat page.
- No claim that the boss caller personally landed the killing blow.
- No page for wastepack creation, hauling, disposal, atomization, or each pollution change.
- No routine hemogen feeding or completed deathrest page.
- No psychic-bond proximity or distance complaint page.
- No Biotech `<modDependencies>` entry.
- No new generic cross-DLC event type named `IdentityTransition`, `BondLifecycle`, or
  `AmbientPressure`.
- No reliance on translated English text for identity or matching.

## 4. Definition of done

### 4.1 B1a is complete only when

- a player-committed growth choice produces exactly one canonical event;
- the event captures the actual chosen trait when any, each actually changed passion, age, nickname
  change, opportunity band, and whether new responsibilities opened;
- `ChoiceLetter_GrowthMoment.NoTrait` never appears as a trait;
- the prompt omits raw growth tier, option count, work-type list, stats, and UI terminology;
- postponing the growth letter, saving, loading, and later choosing produces one event;
- the ordinary birthday is delayed/suppressed only when a real pending growth choice owns it;
- auto-resolved growth birthdays use an exact before/after diff or fall back to the ordinary birthday;
- the selected trait does not reappear from the periodic trait scanner;
- newly passionate high-skill subjects do not immediately produce a second skill-milestone page;
- one supporter is selected deterministically from exact parent/teaching evidence, or omitted;
- disabling the growth group has documented, tested fallback behavior;
- no-Biotech, destroyed-pawn, missing-hook, and malformed-save paths no-op or fall back cleanly.

### 4.2 B1b is complete only when

- pregnancy/labor/birth/growth records share a saved family-arc ID where exact correlation exists;
- old saves baseline current children and parents without invented pregnancy or birth pages;
- newborn arcs exist even though newborns are not diary-eligible;
- `ApplyBirthOutcome` produces one birth event at most, despite nested tale/thought/ritual signals;
- healthy birth, infant illness, stillbirth, surrogacy, and growth-vat paths use only observed facts;
- the birther, genetic mother, and father are not collapsed into one role when they differ;
- no more than two eligible adults receive birth POVs;
- naming completion or deadline flushes the pending event without losing the current child identity;
- save/load before naming preserves the pending birth and emits once afterward;
- exact lesson/BabyPlay observations update saved counters even if the teaching group is disabled or
  SpeakUp is loaded;
- daily lesson/play pages remain governed by the existing interaction group and batching policy;
- growth context summarizes observed upbringing in qualitative bands, not counts;
- family state is bounded and prunable without removing live minor-child arcs;
- the build runs with Biotech inactive.

### 4.3 B2 is complete only when

- gene projection and salience selection are pure, deterministic, XML-tuned, and capped at four;
- exact xenogerm mutations own one page and update scanner state immediately;
- routine recalculation and active/suppressed toggles do not create gene pages;
- old saves baseline the current gene set and current mechanitor state silently;
- mechlink install, first controlled mech, first consequential mech combat, significant mech loss,
  and each boss call/defeat chapter emit no more than one page at their defined boundary;
- automatic numeric mech names do not count as player-named;
- tenure begins only when Pawn Diary actually observes the overseer relation; old saves do not invent
  long service;
- boss-defeat prose distinguishes `called by` from `killed by`;
- bandwidth and routine mech operations remain silent;
- all duplicate-source orderings have fail-open tests.

### 4.4 B3 is complete only when

- pollution polling does not enumerate every map cell;
- the highest active pollution band is the only pollution prompt candidate for a map;
- meaningful contamination, escalation, and reclamation produce bounded pages at most once per
  threshold episode;
- existing observed-condition behavior is unchanged for Defs without the new fields;
- recursive psychic-bond methods produce one pair event, not two;
- bond rupture does not claim a breakup, betrayal, or death unless that cause is exact;
- routine deathrest and feeding remain context-only;
- Biotech-inactive smoke tests pass.

## 5. Reviewed current coverage and gaps

| Area | Current coverage | Missing behavior / plan response |
|---|---|---|
| Birthdays | `BirthdayBiological` immediately submits the XML `Birthday` event window. | A growth choice happens later; stage the birthday and let committed growth own it. |
| Traits | Progression scan emits newly observed traits. | Consume the post-growth trait baseline immediately. |
| Passions | Passion skills can qualify for skill milestones. | Baseline selected skills after growth so the same choice cannot trigger a second milestone. |
| Xenotype | Progression stores only previous/current xenotype label and defName. | Add event-relative salient gene themes and a dedicated baseline version. |
| Pregnancy | `hediffPregnancy`, pregnancy thoughts, and prompt enchantment exist. | Add exact family IDs and arc continuity; do not add trimester spam. |
| Labor | `hediffLabor` exists. | Link it to the same open pregnancy/family arc. |
| Birth | `GaveBirth`, `BabyBorn`, and `ritualChildbirth` can each record separately. | Correlate at `ApplyBirthOutcome` and emit one canonical family event. |
| Teaching | `teaching` batches `BabyPlay` and `Lesson*`; pending participant IDs are transient and emitted context keeps names only. | Add separate saved observation counters by child/adult ID before page settings gate. |
| Hemogen/deathrest | Prompt enchantments already color entries. | Keep routine states as context; add only rare severe lifecycle moments in B3. |
| Psychic bond | Torn-bond thoughts/hediffs may reach generic paths. | Capture exact pair lifecycle and claim only its direct secondary signals. |
| Mechanitor | No persistent diary arc. | Add controller-owned milestone state and concrete owner events. |
| Pollution | No map pollution threshold observer. | Extend observed conditions with a cheap, qualitative pollution observer. |

Two existing details materially affect B1:

- The current minimum first-person age is seven, so the age-seven growth moment can be the child's
  first diary page.
- The `teaching` group is disabled when `JPT.speakup` is loaded. Family continuity therefore cannot
  treat that group or its transient batch as authoritative observation.

## 6. Verified RimWorld 1.6 seams

These APIs were checked against the installed 1.6 `Assembly-CSharp.dll`. Re-verify signatures when
the game build changes.

### 6.1 Growth and birthday timing

`Verse.Pawn_AgeTracker.BirthdayBiological(int birthdayAge)`:

- applies age effects and work-type unlocks;
- calls `TryChildGrowthMoment` for ages in `GrowthUtility.GrowthMomentAges`;
- auto-selects a passion/trait when the pawn should not receive a player letter;
- otherwise constructs and configures `ChoiceLetter_GrowthMoment`;
- returns before the player has committed a choice.

`RimWorld.ChoiceLetter_GrowthMoment.ConfigureGrowthLetter(...)` stores:

- `pawn`;
- `growthTier`;
- `enabledWorkTypes`;
- `oldName`;
- passion/trait choice counts.

`ChoiceLetter_GrowthMoment.MakeChoices(List<SkillDef> skills, Trait trait)`:

- returns early if `ArchiveView` is true;
- sets `choiceMade`, `chosenPassions`, and `chosenTrait`;
- increments the selected passions;
- adds the selected trait unless it is null/`NoTrait`;
- may add an automatic sexuality trait at age 13;
- resets growth points.

The method is the exact committed-choice point. A prefix/postfix pair must compare `choiceMade`
false -> true and use actual before/after skill/trait snapshots, not merely trust the parameters.

The growth letter is itself Scribed and can remain pending for up to 120,000 ticks. Pawn Diary's
own pending ownership must also be Scribed because the ordinary birthday has already fired.

### 6.2 Pregnancy, birth, and naming

`Verse.HediffWithParents` exposes guarded `Mother`, `Father`, and `geneSet` and Scribes the parent
references. `Hediff.loadID`/`GetUniqueLoadID()` supplies stable pregnancy/labor instance identity.

`Verse.Hediff_Pregnant.StartLabor()` transfers the same parents/gene set into the labor hediff.
`Hediff_Pregnant.Miscarry()` gives the birther and exact other parents the corresponding memories.

`RimWorld.PregnancyUtility.ApplyBirthOutcome(...)` is the canonical birth boundary. Its arguments
include outcome, quality, ritual, genes, genetic mother, birther Thing, father, doctor, ritual job,
assignments, and letter suppression. It:

- creates the child;
- assigns parent/parent-birth relations;
- records `GaveBirth` for live births with a pawn birther;
- gives `BabyBorn` or `Stillbirth` memories;
- applies infant illness, postpartum exhaustion, lactation, and possible birther death;
- creates a baby naming letter where appropriate;
- returns the child Pawn or stillborn Corpse.

The child's `babyNamingDeadline` begins at birth. Accepting `Dialog_NamePawn` assigns the name and
sets the deadline to `-1`; expiry leaves the current temporary name. A saved pending birth can poll
that stable field without patching the naming UI.

### 6.3 Gene mutation

`GeneUtility.ImplantXenogermItem(Pawn, Xenogerm)` and
`GeneUtility.ReimplantXenogerm(Pawn caster, Pawn recipient)` are exact mutation boundaries. Both can
be wrapped with before/after `DlcContext` snapshots. `ReimplantXenogerm` also has an ability path, so
its canonical owner must suppress the matching generic ability event.

`GeneDef` exposes localized label/description and structural fields for appearance, aging, needs,
abilities, traits, capacities, damage, environment, resources, social effects, and mental effects.
Only a guarded main-thread adapter may read these live Defs. Pure selection receives bounded
`GeneFact` values.

### 6.4 Mechanitor lifecycle

`Verse.Hediff_Mechlink.PostAdd(DamageInfo?)` and `PostRemoved()` are exact install/removal points.
Removal calls `pawn.mechanitor.Notify_MechlinkRemoved()` and removes overseer relations, so the
pre-removal snapshot must run before vanilla mutation.

`Pawn_MechanitorTracker` exposes `OverseenPawns`, `ControlledPawns`, and control groups. Bandwidth
callbacks are noisy and are explicitly not event hooks.

An overseer relationship is a normal direct relation with defName `Overseer`. The existing
`Pawn_RelationsTracker.AddDirectRelation` patch can notify a Biotech observer before submitting the
romance signal; the Biotech observer must string-match `Overseer` and guard the DLC.

`MechanitorUtility.GetOverseer(Pawn mech)` resolves the controller. A pawn-death prefix can snapshot
that controller before relations are altered.

`CompUseEffect_CallBossgroup.DoEffect(Pawn usedBy)` has the caller and exact `bossgroupDef`.
`GameComponent_Bossgroup.Notify_BossgroupCalled(BossgroupDef)` confirms the call, while
`Notify_PawnKilled(Pawn)` confirms a boss-kind death but does not expose the killer. The saved boss
chapter must therefore say who called the boss and that the boss was defeated, never who dealt the
final blow unless a separate exact death fact supplies it.

### 6.5 Pollution

`PollutionGrid.TotalPollution` is cheap. `TotalPollutionPercent` is not: it rebuilds
`AllPollutableCells` by enumerating map cells. `PollutionGrid.PollutionTick()` already writes the
fraction to `Find.WorldGrid[map.Tile].pollution` whenever the grid is dirty. The diary observer should
poll that maintained world-tile value on a slow cadence.

### 6.6 Psychic bond and deathrest

`Gene_PsychicBonding.BondTo(Pawn)` and `RemoveBond()` recurse into the partner's gene. A sorted pawn-ID
pair plus a short recent-owner token is mandatory to avoid two events.

`RemoveBond()` directly creates `PsychicBondTorn` thoughts/hediffs and may cause a mental break. The
bond owner may claim only those exact nested secondary signals.

`Gene_Deathrest.Wake()` reads `DeathrestPercent`; when below one it adds
`InterruptedDeathrest`, removes the active deathrest hediff, and resets counters. A prefix must
snapshot the percent because the postfix sees zeroed state.

## 7. Target architecture

Every slice follows the repository boundary:

```text
guarded RimWorld hook / slow observer
    -> plain event-time Biotech snapshot
    -> pure diff, significance, ownership, or selection policy
    -> one concrete source event (or context-only state)
    -> existing event factory / prompt planner
    -> persistence, transport, and UI
```

No pure method may accept a live `Pawn`, `Gene`, `GeneDef`, `Hediff`, `ChoiceLetter`, `Thing`, `Map`,
`Pawn_MechanitorTracker`, settings object, Harmony object, or translation result it expects to
resolve later.

### 7.1 Guarded live-data boundary

Extend `Source/Generation/DlcContext.cs` rather than scattering reads. Proposed guarded entry points:

- `TryCaptureGrowthPawn(Pawn, out GrowthPawnSnapshot)`;
- `TryCaptureFamilyHediff(Pawn, Hediff, out FamilyHediffSnapshot)`;
- `TryCaptureBirthParticipants(...)`;
- `TryCaptureGeneIdentity(Pawn, out GeneIdentitySnapshot)`;
- `TryResolveOverseerRelation(Pawn, Pawn, string relationDefName, out OverseerRelationSnapshot)`;
- `TryCaptureMechanitor(Pawn, out MechanitorSnapshot)`;
- `TryCaptureControlledMechDeath(Pawn, out ControlledMechSnapshot)`;
- `TryReadMapPollution(Map, out float fraction)`;
- `TryCapturePsychicBond(...)`;
- `TryCaptureDeathrest(Gene_Deathrest, out DeathrestSnapshot)`.

Every method begins with `ModsConfig.BiotechActive`, validates trackers/references, returns false or
an empty value when unavailable, and copies all prompt-visible text through the normal cleaner and
caps on the main thread.

### 7.2 Plain B1 contracts

#### `GrowthPawnSnapshot`

- `pawnId`, `displayName`, `biologicalAge`;
- `traitKeys`, plus bounded label/description facts for newly selected traits;
- per-skill `skillDefName`, label, passion token, and current level;
- `growthTier` for internal pure banding only;
- `shortNameBefore` / `shortNameAfter` when captured as a mutation;
- `hasNewResponsibilities`;
- `capturedTick`.

#### `GrowthMomentMutation`

- `childId`, `age`, `stageToken`;
- `selectedTrait` or empty;
- `additionalTraitKeysToConsume` but no additional prompt description;
- changed `PassionMutation` rows;
- `opportunityBand`;
- `nicknameChanged`, old/new short name;
- `newResponsibilities`;
- `familyArcId`;
- optional selected supporter and qualitative upbringing facts;
- `sourceToken = player_choice | auto_resolved`;
- `correlationId`.

#### `BiotechFamilyArcState`

- `familyArcId`;
- `pregnancyHediffId`, `laborHediffId`, `childId`;
- `birtherId`, `geneticMotherId`, `fatherId`;
- last bounded display names for those identities;
- `openedTick`, `birthTick`, `lastObservedTick`;
- `birthOutcomeToken`, `birthMethodToken`, `childNameAtBirth`, `currentChildName`;
- `namingResolved`, `closed`, `baselineOnly`;
- bounded `FamilySupportObservationState` rows keyed by adult ID;
- recorded growth ages and last summarized observation tick.

The arc ID is pregnancy-based when a pregnancy exists, for example
`biotech-family|<birther-id>|<pregnancy-hediff-id>`. A vat/modded birth with no matched pregnancy uses
`biotech-family|<child-id>`. The ID never changes after birth attaches the child.

#### `FamilySupportObservationState`

- `adultId`, `lastDisplayName`;
- exact relation token if known: `parent`, `birth_parent`, or empty;
- `lessonCount`, `babyPlayCount`, optional future `careCount`;
- `firstObservedTick`, `lastObservedTick`.

Counts are saved evidence for banding and deterministic selection. They are never sent raw to the
LLM.

#### `BirthMutationSnapshot`

- `familyArcId`, `childId`, current child name;
- `birther`, `geneticMother`, `father` as distinct `FamilyParticipantFact` values;
- `outcomeToken = healthy | infant_illness | stillbirth`;
- `methodToken = pregnancy | surrogacy | growth_vat`;
- `birtherDied`, `ritualBirth`;
- qualitative outcome/quality band only when exact;
- `namingDeadline`, `namingResolved`;
- `birthTick`, `correlationId`.

Do not add doctor as a writer in B1. Doctor identity may be a bounded context fact when supplied by
`ApplyBirthOutcome`, but family perspectives take precedence.

### 7.3 Plain B2/B3 contracts

#### `GeneFact` and `GeneIdentitySnapshot`

`GeneFact` contains `defName`, cleaned label/description, `endogene|xenogene`, structural category
tokens, and pure ranking flags. `GeneIdentitySnapshot` contains pawn/xenotype identity and a bounded,
sorted active membership set plus projected facts. The membership set is internal diff state; prompt
formatters see only selected facts.

#### `GeneMutationSnapshot`

- pawn and old/new xenotype identity;
- added/removed gene DefNames for diffing;
- two to four selected `GeneThemeFact` rows;
- exact cause token when known;
- other pawn ID/name for reimplantation when exact;
- correlation ID and tick.

#### `MechanitorArcState`

- mechanitor ID/name and baseline version;
- mechlink present/first-installed tick;
- `firstControlledMechRecorded`, `firstConsequentialMechCombatRecorded`;
- bounded `ControlledMechState` rows with mech ID/name, kind label, first-observed tick,
  player-named flag, and loss-recorded flag;
- bounded active/completed `BossChapterState` rows keyed by boss-group DefName/call epoch.

#### `BondMutationSnapshot` / `DeathrestMutationSnapshot`

These contain sorted pair IDs and before/after phase for psychic bonds, or pawn ID plus qualitative
completion band for deathrest. Causes are optional and omitted unless exact.

### 7.4 Saved-state ownership

Add component-owned deep-scribed lists for records that must exist without an eligible diary:

- `biotechFamilyArcs`;
- `pendingBiotechGrowthMoments`;
- `pendingBiotechBirths`.

Per-eligible-pawn observation belongs with the existing `PawnProgressionState` through one nested
`BiotechPawnProgressionState`:

- gene baseline/version and last membership snapshot;
- growth ages consumed if no family arc exists;
- mechanitor arc state;
- bond/deathrest lifetime/cooldown markers.

Pollution continues to use `activeObservedConditions`; do not build a parallel pollution state
machine.

### 7.5 Transient correlation caches

Use bounded, resettable main-thread caches/scopes for:

- active birthday -> configured growth letter;
- active `MakeChoices` mutation;
- active birth outcome and nested tale/thought/hediff signals;
- recent birth owner -> later childbirth ritual postfix;
- active xenogerm mutation -> later ability postfix;
- mech death before relation cleanup;
- active boss call and recent boss defeat;
- recursive psychic-bond pair ownership;
- interrupted deathrest -> nested hediff signal.

Each cache must expire by elapsed ticks, have a defensive size cap, expose `Clear()`, and be cleared
from new-game/load initialization. If a canonical owner fails, staged mature signals are released in
their original form where possible.

### 7.6 Event types and page ownership

Add only concrete types that cannot fit an existing owner:

- `GrowthMomentEventData` — solo or child/supporter pair;
- `FamilyBirthEventData` — solo or two-adult pair;
- `MechanitorEventData` — controller milestone when an existing tale/death page cannot use the
  controller as POV;
- `BiotechBondEventData` — psychic-bond pair lifecycle.

Reuse/enrich existing types where they already own the moment:

- pregnancy/labor/loss -> `HediffEventData` or `ThoughtEventData` plus family context;
- exact xenogerm and fallback gene identity -> `ProgressionEventData` with richer facts;
- mech-assisted tale that can be rerouted to the controller -> Tale source owns qualification, then
  emits `MechanitorEventData` once;
- controlled-mech death -> `MechanitorEventData` because the dead mech is not an eligible POV;
- pollution -> observed-condition pages/context;
- interrupted deathrest -> `ProgressionEventData` or one narrow Biotech progression defName;
- sanguophage transformation -> gene identity transition, not a second sanguophage type.

Suggested stable synthetic `interactionDefName` values:

| Moment | Stable value |
|---|---|
| Growth choice | `BiotechGrowthMoment` |
| Family birth | `BiotechFamilyBirth` |
| Mechlink installed/removed | `BiotechMechlinkInstalled`, `BiotechMechlinkRemoved` |
| First controlled mech | `BiotechFirstControlledMech` |
| First mech combat | `BiotechFirstConsequentialMechCombat` |
| Significant mech loss | `BiotechSignificantMechLoss` |
| Boss call/defeat | `BiotechBossCalled`, `BiotechBossDefeated` |
| Psychic bond | `BiotechPsychicBondFormed`, `BiotechPsychicBondRuptured` |
| Interrupted deathrest | `BiotechDeathrestInterrupted` |

### 7.7 Required shared narrative evidence

After Narrative Continuity N1 exists, attach the following per-POV evidence to every canonical event
introduced by the matching phase:

| Biotech moment | Facet | Suggested topics/providers |
|---|---|---|
| Growth moment / xenogerm / mechlink | `identity_transition` | `family`, `body_modification`, `child_labor`, `autonomous_weapons` only when event facts support them |
| Birth / psychic bond / significant mech loss | `bond_lifecycle` | `family`, `bonding`, `death` only when exact |
| Boss call/defeat | `journey_chapter` | `autonomous_weapons`, `violence`, `duty` |
| Pollution | `ambient_pressure` | `pollution`, `nature`, `home` |

Unknown facets/topics are ignored. Biotech must compile and behave correctly without the Ideology
implementation merged.

Use stable identities rather than labels:

- family continuity: `familyArcId` is already the complete key
  `biotech-family|<birtherId>|<pregnancyHediffId>` or `biotech-family|<childId>` and is copied
  unchanged into Narrative Continuity (never double-prefixed);
- psychic bond: canonical sorted pawn IDs plus a bond epoch;
- mechanitor: `biotech-mechanitor|<mechanitorPawnId>|<arcEpoch>` and exact mech subject IDs for loss;
- identity transition: pawn subject plus exact source phase; do not encode localized xenotype names;
- pollution: exact map/home scope, not a colony-global pressure on pawns elsewhere.

Biotech's provider may propose a matching family/psychic/mech bond, salient identity fact, or
location-applicable pollution pressure. It must not list genes, every controlled mech, or inferred
parental emotion. B1 and Odyssey O1 are the recommended first real-provider pair for Narrative
Continuity N2: growth/family events aboard a committed gravship and landing/homecoming events with an
exact family connection are the acceptance showcase.

## 8. Growth-moment policy

### 8.1 Birthday state machine

Extend the existing `BiologicalBirthdayEventWindowPatch` rather than adding a competing birthday
patch.

1. **Prefix:** when Biotech is active and the age is a growth birthday, snapshot traits, passions,
   name, age, and internal growth tier into a short correlation state. Do not suppress anything yet.
2. **Nested configure hook:** `ConfigureGrowthLetter` associates the real letter with the prefix
   snapshot and creates/updates a saved `PendingBiotechGrowthMoment`.
3. **Birthday postfix:**
   - if a configured letter owns the birthday, do not call the ordinary
     `RecordEventWindowBirthday` yet;
   - if no letter exists but exact before/after state changed through the auto path, emit the
     auto-resolved composite immediately;
   - if no composite mutation exists, preserve the ordinary birthday unchanged.
4. **Choice prefix:** verify `choiceMade == false`, snapshot the final pre-choice state, and load the
   saved pending owner by pawn/letter identity.
5. **Choice postfix:** only a false -> true transition may complete the event. Diff actual traits and
   passions, attach `chosenTrait`/`chosenPassions` only where the mutation succeeded, update family
   and progression state, then dispatch one canonical signal.
6. **Cleanup:** remove pending state only after event creation succeeds or after a documented fallback
   is emitted/consumed. LLM failure does not recreate the event; normal generation retry owns it.

### 8.2 Save/load and postponed letters

`PendingBiotechGrowthMoment` stores no live letter reference. Key it by pawn ID, birthday age, and
the birthday tick/correlation ID. The live letter is already Scribed by RimWorld; after load,
`MakeChoices` resolves the pending row by pawn ID + age and nearest unresolved tick.

Normalize on load:

- null strings/lists -> empty;
- impossible ages -> remove;
- duplicate pawn+age unresolved rows -> keep the newest valid row;
- future ticks -> clamp/reset;
- missing/destroyed pawn beyond the XML expiry -> discard without a page;
- unresolved row with no plausible live letter after the grace window -> release one ordinary
  birthday only if that birthday setting is enabled and no canonical growth event was recorded.

Do not scan or open the letter UI. The player's postponed choice remains vanilla-owned.

### 8.3 Pure mutation diff

`GrowthMomentPolicy.Diff(before, after, committedChoice)` must:

- return empty when pawn ID/age do not match or no actual relevant mutation occurred;
- include a selected trait only if it exists after the method and is not the `NoTrait` sentinel;
- retain all new trait keys in `additionalTraitKeysToConsume`, including age-13 automatic traits,
  without automatically exposing their labels in prompt context;
- include only skills whose passion actually increased;
- preserve before/after passion as stable tokens for policy, but format the prompt as `new interest`
  or `deepened interest`, not `Minor`/`Major`;
- use the actual post-choice short name to detect a nickname change;
- carry only a boolean `newResponsibilities`, not work types/counts;
- return a stable correlation/dedup key `growth|pawnId|age`.

For auto-resolved growth, include a trait description only when the diff can identify one unambiguous
new trait. If more than one appears, consume them all for dedup but use generic identity wording.

### 8.4 Opportunity bands

Map the internal 0..8 growth tier through XML policy in a new `DiaryBiotechPolicyDef`. Default band
tokens should be qualitative, for example:

| Internal tier | Prompt band |
|---|---|
| 0–2 | `narrow` |
| 3–5 | `mixed` |
| 6–7 | `broad` |
| 8 | `exceptional` |

The exact thresholds and localized/DefInjected descriptions are XML-owned. The LLM receives the band
description, never the number. Wording must describe opportunity available to the child, not grade
the parents or claim causation.

### 8.5 Supporter selection

Build plain `FamilySupportCandidate` rows from saved family state and current eligibility. The pure
selector chooses at most one:

1. eligible direct parent/birth-parent with observed activity;
2. eligible direct parent/birth-parent even without captured activity;
3. eligible adult with enough exact lesson/BabyPlay evidence to be called `teacher`;
4. no supporter.

Within a tier, rank qualitative engagement band, recency, same-map availability, then stable pawn ID.
Do not use random global `Rand`; deterministic tie-breaking keeps save/load and prompt previews stable.

The role token must match evidence:

- `parent` for the direct parent relation;
- `birth_parent` for the distinct parent-birth relation;
- `teacher` for observed lessons/play without parent relation;
- never `guardian` unless a future exact vanilla/mod contract supplies that role.

If the child is ineligible but the supporter is eligible, emit a supporter solo page. If both are
eligible, create a pair event with child initiator and supporter recipient. If neither is eligible,
consume observation state without creating a page.

### 8.6 Growth context contract

Representative saved keys:

- `growth_moment=true`, `child_id`, `birthday_age`, `growth_stage`, `family_arc_id`;
- `opportunity_band`, `observed_upbringing_band`;
- `selected_trait`, `selected_trait_description` when unambiguous;
- `new_interest_1..4`, `interest_change_1..4`;
- `nickname_changed`, `previous_name`, `current_name`;
- `new_responsibilities=true`;
- `supporter_id`, `supporter_name`, `supporter_role`;
- `initiator_family_role=child`, `recipient_family_role=<token>`;
- optional `narrative_facets` and `belief_topics`.

Omit absent optional keys. Sanitize semicolons/newlines and cap every external value.

### 8.7 Duplicate arbitration

When growth owns the birthday:

- suppress/delay the `Birthday` event-window start;
- immediately add all current trait keys to `knownTraitKeys` after completion;
- update the chosen skills' saved milestone baseline to the highest already-reached threshold;
- do not create a separate passion event;
- treat age-13 voice-stage change as context/voice selection, not a second event;
- mark `growth|pawnId|age` consumed even when the user disabled page generation.

Observation must not depend on the global Progression signal switch. Settings control page creation,
not whether state advances.

### 8.8 Settings and fallback behavior

Add a `progressionGrowthMoment` group, available only when package
`Ludeon.RimWorld.Biotech` is loaded. To preserve existing user intent:

- an explicit new growth-group override wins;
- otherwise an explicit `eventWindowBirthday` override supplies the effective initial value;
- otherwise use the new group's XML default.

If canonical growth is disabled but ordinary birthdays are effectively enabled, release exactly one
ordinary birthday at choice completion so it cannot precede the actual growth decision. Consume
trait/passion baselines either way. If both are disabled, create no page.

### 8.9 Hook failure behavior

- If `MakeChoices` cannot be patched, do not suppress ordinary birthdays.
- If `ConfigureGrowthLetter` cannot be correlated, the birthday remains ordinary.
- If the pure diff throws or returns empty, release the ordinary birthday and baseline current
  progression state.
- If supporter selection fails, keep the child solo event.
- Log each missing stable method once; no hot-hook spam.

## 9. Family-continuity policy

### 9.1 Opening and linking an arc

The family observer runs before ordinary hediff/thought/interaction settings gates.

On an added `PregnantHuman`/`Pregnant` hediff, `DlcContext.TryCaptureFamilyHediff` may open an arc only
when:

- Biotech is active;
- the hediff is `HediffWithParents`;
- the birther pawn and hediff load ID are valid;
- at least one exact participant ID is available.

The birther, `Mother`, and `Father` may be the same or different pawns. Store distinct roles rather
than assuming `hediff.pawn == Mother`.

When `PregnancyLabor`/labor-pushing appears, attach its hediff ID to the newest compatible open arc
for the birther and parent set. If no pregnancy arc exists, open a baseline-only labor arc rather
than fabricating a pregnancy start.

At birth, match the open arc by birther ID first, then exact genetic-mother/father set and recency.
Attach the child ID and birth facts. If no safe unique match exists, create a child-keyed birth arc;
never merge two ambiguous pregnancies.

### 9.2 Existing pregnancy and labor pages

Do not create new pregnancy or labor event types. Extend their event-time context with available:

- `family_arc_id`;
- `birther_id`;
- `genetic_mother_id` and cleaned name;
- `father_id` and cleaned name;
- `family_stage=pregnancy|labor`;
- role tokens and optional cross-DLC topics.

No field may reveal gene contents, predicted child xenotype, birth outcome, or future health. The
pregnancy page knows only what is currently known.

Existing hediff policy/settings continue to decide whether those pages exist. Family observation
still advances when those groups are disabled.

### 9.3 Canonical birth correlation scope

Patch `PregnancyUtility.ApplyBirthOutcome` with prefix, postfix, and finalizer semantics:

1. **Prefix:** open a `BirthCorrelationScope` containing participant IDs, ritual identity, birther
   state, and any matched family arc. Stage direct nested `GaveBirth`, `BabyBorn`, `Stillbirth`,
   postpartum, and childbirth-ritual secondary signals rather than immediately dispatching them.
2. **Postfix:** resolve the returned Pawn or Corpse/inner pawn, capture exact outcome and post-birth
   participant state, attach/create the family arc, and decide canonical ownership.
3. **Finalizer:** always close the scope. If vanilla threw or the canonical snapshot is invalid,
   release staged mature signals in original order. Return the original exception unchanged.

The scope must be stack-safe for nested/modded calls and main-thread-only. Never retain live
participants after the call; copy IDs and bounded labels into saved state.

### 9.4 Outcome vocabulary

Use only exact tokens:

- `healthy` when the returned live child has no immediate infant-illness outcome;
- `infant_illness` for the exact neutral/ill outcome;
- `stillbirth` when the returned Thing is the child's corpse/stillborn outcome;
- `birther_died=true` only when the birther was alive before and dead after;
- `surrogacy` only when birther and genetic mother are both known and distinct;
- `growth_vat` only when `birtherThing` is a growth vat;
- otherwise `pregnancy`.

Do not convert ritual positivity index, quality float, or medical chances into invented prose.
`DiaryBiotechPolicyDef` may map exact outcome/quality inputs to qualitative `difficult`, `uncertain`,
or `smooth` bands if tests prove the mapping; otherwise omit the band.

### 9.5 Naming delay

After a valid birth snapshot:

- if the child's naming deadline is already `-1`, emit immediately with the current name;
- otherwise save `PendingBiotechBirthState` and poll it on a modest interval;
- emit when `babyNamingDeadline == -1`, when the deadline has passed, or when the child becomes
  unavailable/dead and waiting longer cannot improve the name;
- always use the current cleaned child label at flush time;
- store the birth-time temporary name separately only for diagnostics/migration, not as a claim that
  it was the final name.

Saving before naming writes the pending row. Loading must not replay the nested tale/thought/ritual
signals; the pending canonical birth already owns them.

The delay affects page generation only. Family relations, birth outcome, and arc IDs are committed
to saved state in the birth postfix.

### 9.6 Birth writer selection

Build unique eligible adult candidates from:

1. birther;
2. genetic mother when distinct;
3. father when distinct.

The pure selector emits at most two perspectives:

- birther first when eligible;
- then a distinct genetic mother;
- then father;
- if no birther is eligible, genetic mother then father.

This ordering is role certainty, not emotional importance. Do not infer that an omitted third adult
cared less. Keep every exact participant in shared context even when not selected as a POV.

For a pair event, include role-specific fallback text and keys such as
`initiator_family_role=birther` and `recipient_family_role=father`. If only one adult is eligible,
emit solo. The newborn/stillborn child is the subject, never a first-person writer.

### 9.7 Canonical birth settings and mature fallbacks

Add a `biotechFamilyBirth` group. Its effective initial state should preserve prior explicit intent:

- an explicit `biotechFamilyBirth` override wins;
- otherwise a non-ritual birth inherits an explicit `talelife` override for `GaveBirth`;
- otherwise a ritual birth is enabled when either its explicit `ritualChildbirth` or `talelife`
  route would have been enabled;
- otherwise use the new group default.

When canonical birth is enabled and has an eligible writer, it claims the staged `GaveBirth` tale,
`BabyBorn`/`Stillbirth` memories, and matching childbirth ritual page.

When canonical birth is disabled or no canonical writer exists, release the mature signals through
their normal settings. Preserve their original dedup behavior; do not silently consume all birth
coverage merely because the richer owner could not write.

### 9.8 Pregnancy loss and non-birth endings

B1 does not need a new pregnancy-loss event type.

- Patch/observe `Hediff_Pregnant.Miscarry` to close the exact arc with `loss` and attach its family ID
  to the existing `Miscarried`/`PartnerMiscarried` thought route.
- Correlate those sibling thoughts so at most the intended POV rows survive; do not claim a birth.
- For termination or unknown hediff disappearance, close the arc with an exact token only when a
  visible thought/operation supplies it. Otherwise mark `ended_unknown` silently.
- Never infer miscarriage, abortion, or death from a missing hediff alone.

### 9.9 Quiet teaching and baby-play observation

The saved observer is not a page source. Feed it before normal capture gates from:

- `PlayLog.Add` for exact `BabyPlay` and `Lesson*` InteractionDef identity;
- accepted `GaveLesson`/`WasTaught` social memories when `otherPawn` is exact, as a defensive second
  route with a short pair+tick dedup;
- future exact childcare hooks only after separate review.

Use XML policy:

- exact `BabyPlay`;
- safe `Lesson` prefix;
- optional exact modded additions;
- category token `baby_play|lesson`;
- per-pair dedup window;
- maximum stored adult rows and qualitative count thresholds.

Do not reuse the existing group's broad `Baby` substring matcher for saved family evidence.

Resolve child/adult roles from developmental stage at event time. If both are adults/babies, or the
roles are ambiguous, do not update family state. A lesson may attach to an existing child arc or
create a baseline child arc keyed by child ID.

### 9.10 Upbringing summary

At growth time, pure policy converts evidence since the last summarized growth age into bounded
facts:

- `observed_upbringing_band=none_seen|some|steady|many`;
- up to two activity categories such as `lessons` and `play`;
- selected supporter role/name;
- optional `family_continuity=since_birth|observed_childhood|baseline_only`.

`none_seen` means Pawn Diary did not observe qualifying interactions; prompt wording must not say the
child received no care. Prefer omission over a negative claim.

After a growth event is consumed, advance `lastSummarizedObservationTick`. Keep lifetime counters for
supporter selection but do not repeat the same childhood evidence verbatim at ages 10 and 13.

### 9.11 Arc retention

Keep arcs while any of these are true:

- pregnancy/labor/birth is unresolved;
- a child is alive and younger than the configured post-growth retention age;
- a pending birth/growth event references the arc;
- an unsummarized support observation exists.

After the last growth age plus the XML retention window, compact the arc to IDs, roles, birth token,
recorded growth ages, and last labels; drop detailed counters. Remove only when no saved event or
pending state needs it. Caps and compaction must be deterministic.

## 10. Gene-identity policy (B2)

### 10.1 Projection, not live selection

`DlcContext.TryCaptureGeneIdentity` walks `pawn.genes.GenesListForReading` only when Biotech is active
and the tracker exists. For each active gene, copy a plain `GeneFact`:

- stable DefName;
- localized cleaned label and base description;
- `endogene|xenogene`;
- structural categories derived from reliable `GeneDef` fields;
- whether it supplies an ability, forced/suppressed trait, resource/need, appearance, aging,
  environment dependency, violence/emotion, social effect, or major capacity/stat effect;
- optional magnitude band, never raw stat values in prompt prose.

Do not use `DescriptionFull` if it expands mechanics/stats. Do not call arbitrary mod getters. Missing
or malformed text leaves the fact label-only.

### 10.2 Pure salience selector

Add `GeneSaliencePolicy.Select(snapshot, mutation, policy)` under a standalone pure test project.

Requirements:

- output two to four themes when enough facts exist, fewer when not;
- prefer genes actually added/removed by the event;
- prefer one defining need/ability/body/environment/social theme over several near-duplicates;
- collapse same-category facts unless an XML exception allows two;
- use deterministic score + stable tie-break; no game RNG;
- enforce per-description and total-character caps;
- never include hidden inactive/suppressed bookkeeping as a change;
- return empty rather than invent meaning from a label with no usable fact.

XML owns category weights, event-delta bonus, xenogene/endogene weighting, maximum themes, duplicate
category penalty, exact force/exclude corrections, and text caps.

### 10.3 Exact xenogerm ownership

For `ImplantXenogermItem` and `ReimplantXenogerm`:

1. prefix captures recipient before-state;
2. postfix captures after-state and computes a plain mutation;
3. empty mutation emits nothing but still refreshes observation state;
4. non-empty mutation emits one enriched Progression event with exact cause;
5. update the gene baseline before the next scanner pass;
6. register a recent owner token so the outer ability path cannot create a generic duplicate.

For reimplantation, the caster is context only unless a later reviewed design adds a second POV. Do
not describe consent or sacrifice unless the source supplies it.

### 10.4 Fallback observer and migration

Replace the label-only xenotype baseline with a versioned `GeneIdentityObservationState` while
retaining old scalar fields for migration.

- first scan of an old save copies current xenotype and membership silently;
- exact mutation hooks update state immediately;
- fallback scan compares membership and xenotype identity, not temporary active/suppressed status;
- emit only when xenotype identity changed or a membership delta passes XML significance;
- cause token is omitted/`observed_change`, never guessed;
- observation advances even when the progression group or signal is disabled.

The current `progressionXenotype` group may remain the fallback setting. Add exact synthetic groups
before it only if separate prompt/settings control is useful after playtesting.

### 10.5 Context contract

Representative keys:

- `gene_identity_transition=true`;
- `previous_xenotype`, `previous_xenotype_def`;
- `xenotype`, `xenotype_def`;
- `gene_change_cause`;
- `gene_theme_1..4`, `gene_theme_description_1..4`, `gene_theme_change_1..4`;
- optional `other_pawn`, `other_pawn_id`;
- `narrative_facets=identity_transition`;
- supported belief topics only.

Never serialize the complete membership set into `gameContext`.

## 11. Mechanitor-arc policy (B2)

### 11.1 State and silent baseline

On the first Biotech observer pass for an old save:

- note whether the pawn already has a mechlink;
- capture currently overseen mech IDs as baseline rows with `firstObservedTick=now`;
- mark first-controlled and first-combat milestones consumed when current state proves they predate
  observation;
- do not mark any mech long-serving until it has remained observed for the configured duration;
- baseline already-called/defeated boss state without pages where the component exposes it.

The observer runs on a slow cadence and after exact hooks as fallback. It does not poll bandwidth for
events.

### 11.2 Mechlink install and loss

Patch `Hediff_Mechlink.PostAdd` after vanilla succeeds:

- gate playing state, Biotech, eligible colonist, and absent->present mutation;
- create/update `MechanitorArcState`;
- emit `BiotechMechlinkInstalled` once;
- add `identity_transition` and exact gene/ideology topics when available.

Patch `PostRemoved` with a prefix snapshot:

- if the pawn is alive and the mechlink truly becomes absent, emit `BiotechMechlinkRemoved`;
- if removal is part of pawn death, let the death owner carry mechanitor context instead;
- snapshot overseen mech IDs before vanilla removes relations;
- never describe the control network's future fate unless observed.

### 11.3 First controlled mech

In the existing `AddDirectRelation` postfix, call the Biotech observer before `RomanceSignal`.
String-match relation defName `Overseer`; guarded projection determines which pawn is the human
mechanitor and which is the mech.

If the relation is newly observed after baseline and the milestone is unconsumed:

- save mech ID/name/kind and assignment tick;
- emit one mechanitor solo page;
- mark the milestone consumed when the event is created, not after LLM success.

Every later mech is state only.

### 11.4 Player-named and long-serving mechs

A mech is `player_named` only when its `NameSingle` is non-numerical (or a future exact flag proves a
custom name). Automatic `Lifter 1`-style numeric names do not qualify by name alone.

Long service uses elapsed ticks since Pawn Diary observed the exact overseer relation. XML owns the
minimum. Old saves start at load, so they cannot immediately backfill a long-serving loss.

### 11.5 First consequential combat through mechs

Qualify at the existing Tale/death source before its ordinary group gate:

- exact instigator is a controlled mech;
- exact overseer is an eligible mechanitor;
- event is a consequential hostile combat outcome allowed by XML (initially a hostile humanlike kill
  or similarly strong existing Tale family);
- milestone is not consumed.

The Tale source owns the gameplay moment but reroutes the POV to one `MechanitorEventData` entry. It
stores the source TaleDef and controlled mech identity in context, suppresses the generic same-action
Tale page, and does not claim the mechanitor personally struck the target.

### 11.6 Significant mech loss

A separate guarded snapshot around `Pawn.Kill` captures the mech's overseer before relation cleanup.
After death is confirmed, loss qualifies only when:

- overseer is an eligible mechanitor;
- mech is player-named **or** observed tenure meets XML minimum;
- this mech-loss milestone has not already been recorded.

Emit one controller page with exact death cause facts when available. Routine unnamed/recent mech
loss remains silent. If the same death is a boss defeat or colony tale, apply the ownership matrix in
§14 rather than creating multiple pages.

### 11.7 Boss call and defeat chapters

At `CompUseEffect_CallBossgroup.DoEffect(Pawn usedBy)`:

- prefix captures caller, boss-group DefName/label, map, and next call epoch;
- postfix/manager notification confirms the call succeeded;
- save a `BossChapterState` and emit `BiotechBossCalled` for the caller once.

At `GameComponent_Bossgroup.Notify_PawnKilled(Pawn)`:

- verify the pawn maps to an active called boss group;
- close the earliest matching active chapter;
- emit `BiotechBossDefeated` for the saved caller if eligible;
- include boss/group identity and elapsed chapter band;
- state explicitly only that the caller summoned the threat and the threat was defeated.

Generic raid/tale/death pages may still exist for distinct colony moments, but a matching boss-call
or boss-defeat signal cannot create a second mechanitor milestone for the same chapter.

### 11.8 Deferred mechanitor ideas

Do not implement in B2 without a new exact seam review:

- command-tier unlocks inferred from bandwidth/control groups;
- pages for every boss wave rather than meaningful boss-group chapters;
- routine disconnection/reconnection;
- mech personality or first-person mech diaries;
- every mechanitor implant/stat improvement.

## 12. Pollution policy (B3)

### 12.1 Extend observed conditions conservatively

Add `MapPollution` to `ObservedConditionObserverType` and the minimum fields needed by pollution Defs:

- `minPollutionFraction` and optional `maxPollutionFraction`;
- `exclusiveFamilyKey`;
- `severityRank`;
- optional `maxPagePawns` (zero preserves current unlimited behavior).

For all existing observer types/Defs, defaults preserve current behavior exactly.

`CollectMapPollutionObservations`:

- returns immediately when Biotech is inactive;
- considers eligible home maps only;
- reads `DlcContext.TryReadMapPollution`, backed by world-tile pollution;
- maps the value to an integer evidence band, not a raw prompt percentage;
- performs no cell enumeration and no Thing scan.

### 12.2 Threshold Defs

Use separate conditions in one exclusive family:

- `BiotechPollutionMeaningful`: start page optional; end page is reclamation;
- `BiotechPollutionSevere`: start/escalation page; no end page;
- `BiotechPollutionCritical`: start/escalation page; no end page.

All may stay active for state-machine purposes, but prompt candidate selection keeps only the
highest `severityRank` per family/map. The meaningful condition remains active under severe
pollution so its eventual end correctly means reclamation below the first threshold.

XML owns thresholds, debounce, cooldown, tone, text, prompt weight, and page switches.

### 12.3 Bounded witnesses

Existing observed-condition map pages fan out to all eligible colonists. For pollution Defs set
`maxPagePawns=2`. A pure stable selector chooses witnesses from exact eligible map candidates using:

- exact pollution-related visible health relevance when available;
- otherwise deterministic hash of condition, map, transition tick, and pawn ID.

This is perspective sampling, not a claim that chosen pawns are uniquely affected. Existing Defs
with zero/unset cap keep their current fan-out.

### 12.4 Context and anti-noise

Prompt keys are qualitative: `pollution_band=meaningful|severe|critical`,
`pollution_transition=start|escalated|reclaimed`, map label, and `ambient_pressure`. Do not expose
cell counts or percentages.

Wastepacks and individual pollution edits never create pages. A threshold must pass start/end
debounce and cooldown. Old saves baseline current pollution without start/escalation catch-up; a
later real threshold crossing may record.

## 13. Psychic bond, deathrest, and hemogen policy (B3)

### 13.1 Psychic-bond formation

Wrap `Gene_PsychicBonding.BondTo`:

- prefix captures owner/new partner and whether the canonical sorted pair was already bonded;
- postfix verifies the mutual bond exists;
- recursive partner invocation is suppressed by the active pair scope;
- emit one pair event if either/both pawns are eligible;
- context says psychic bond formed, not romance, consent, destiny, or permanence.

Baseline current bonded pairs silently on old saves.

### 13.2 Psychic-bond rupture

Wrap `RemoveBond` with a pair snapshot. Determine cause only from exact facts:

- `death` if one partner is already dead in the relevant mutation;
- `gene_removed` only when an exact gene-removal scope owns it;
- otherwise omit cause/use `unknown` internally.

The canonical rupture event claims only nested `PsychicBondTorn` thoughts/hediffs and an exact
same-scope mental break. It does not suppress unrelated death or romance pages. Recursive calls use
the same sorted pair+epoch and emit once.

### 13.3 Interrupted deathrest

Wrap `Gene_Deathrest.Wake`:

- prefix captures pawn, current `DeathrestPercent`, and active deathrest state;
- postfix confirms `InterruptedDeathrest` was added;
- pure policy requires a severe completion band and lifetime/cooldown eligibility;
- emit one `BiotechDeathrestInterrupted` solo page;
- claim the nested generic hediff signal for the same action;
- omit cause unless exact.

XML owns the severe threshold and cooldown. Routine wake at completion emits nothing.

### 13.4 Hemogen

Keep the existing `DiaryEnchant_HemogenCraving` as the default behavior. Only add a page-producing
severe-crisis observer if playtests demonstrate a memorable transition not already represented by
an ordinary event.

If added later, it must:

- use a visible hediff/need threshold through `DlcContext`;
- baseline old saves;
- record first severe/cooldown episodes only;
- never record feeding;
- avoid duplicating a same-tick mental break, collapse, or medical page.

## 14. Event ownership and dedup matrix

| Gameplay moment | Canonical owner | Secondary signals claimed | Fail-open behavior |
|---|---|---|---|
| Growth choice | `GrowthMomentSignal` after committed mutation | birthday, selected-trait scan, selected-skill milestone | ordinary birthday at completion |
| Auto-resolved growth | birthday before/after coordinator | birthday/trait/skill duplicates | ordinary birthday when diff is empty |
| Pregnancy starts | existing Hediff event | none; family state observes independently | existing Hediff unchanged |
| Labor starts | existing Hediff event | none; family state observes independently | existing Hediff unchanged |
| Birth | `FamilyBirthSignal` after naming policy | `GaveBirth`, `BabyBorn`/`Stillbirth`, childbirth ritual | release staged mature signals |
| Miscarriage | existing Thought event enriched by exact scope | sibling same-action loss signals per POV policy | existing thoughts |
| Xenogerm implant/reimplant | enriched Progression event | fallback xenotype scan, matching outer Ability | existing ability/progression where valid |
| Mechlink install/remove | exact mechanitor transition | same-action Hediff/progression marker | generic hediff or silent observation |
| First controlled mech | overseer relation owner | no routine relation page | silent state update |
| First mech combat | Tale-qualified mechanitor owner | same-action generic Tale page | original Tale |
| Significant mech loss | controlled-mech death owner | same-mech generic mechanitor loss only | ordinary colony/death signals remain |
| Boss call | boss-use owner | matching ability/incident milestone | generic event if canonical invalid |
| Boss defeat | active boss chapter | duplicate mechanitor/tale milestone | generic combat/tale remains |
| Pollution crossing | observed-condition state | lower-band prompt candidate only | context-only/no page |
| Psychic bond formed/ruptured | sorted pair owner | direct PsychicBond/Torn hediff/thought duplicates | mature signal released where possible |
| Interrupted deathrest | deathrest owner | same-action `InterruptedDeathrest` hediff | generic hediff |

Secondary arbitration must work in both callback orders. Keep short pending-secondary and recent-owner
tokens. Tests must cover owner-before-secondary, secondary-before-owner, owner failure, expiry, and
save/load clearing.

## 15. Persistence and save compatibility

### 15.1 Scribe keys

Freeze additive top-level keys before implementation:

- `biotechFamilyArcs`;
- `pendingBiotechGrowthMoments`;
- `pendingBiotechBirths`;
- nested `biotechProgressionState` inside `PawnProgressionState`.

All new records implement `IExposable`, initialize lists at declaration, normalize in
`PostLoadInit`, and contain no saved live `Pawn`, `Thing`, `Map`, `Gene`, `Hediff`, or letter
reference. Stable IDs and bounded labels are sufficient.

Do not rename existing progression Scribe keys. Keep
`lastObservedXenotypeDefName`/`lastObservedXenotypeLabel` for old-save migration even after the new
gene state becomes authoritative.

### 15.2 Baseline/version markers

Use explicit integer schema/baseline versions:

- `familyObservationVersion` at component level;
- `growthObservationVersion` in pending/per-pawn state;
- `geneObservationVersion`;
- `mechanitorObservationVersion`;
- `bondObservationVersion`;
- `pollution` baseline encoded through existing observed-condition state initialization.

An empty list is not a baseline marker. Empty can legitimately mean no children, genes, controlled
mechs, or bonds.

### 15.3 Old-save behavior

On first load after each slice:

- discover current child-parent relations and create baseline-only family arcs without pregnancy or
  birth outcome claims;
- do not backfill prior births, birthdays, lessons, growth choices, xenogerm changes, mechlink
  installs, controlled mechs, boss calls, bonds, or pollution threshold starts;
- baseline current gene membership and xenotype;
- baseline current mechlink/overseer/boss state;
- baseline current psychic bonds;
- initialize pollution condition state without a start page;
- preserve future real transitions.

The family bootstrap may read living player children and direct `Parent`/`ParentBirth` relations.
It must not infer pregnancy roles or an exact birther where the relation does not distinguish them.

### 15.4 Normalization and corruption tolerance

For every record:

- null strings -> empty;
- null collections -> new empty lists;
- trim IDs/tokens; reject blank primary keys;
- clamp counts/ticks/bands;
- deduplicate rows by stable identity with deterministic newest-valid selection;
- cap gene facts, supporter rows, controlled mechs, boss chapters, and pending rows;
- reset impossible future/negative ticks;
- discard unresolved pending rows beyond their XML grace period;
- preserve valid event ownership markers even when the live pawn is gone;
- log one bounded warning for malformed state, not one per tick.

### 15.5 Event-time truth

Every generated event keeps its Biotech facts in the saved semicolon-delimited `gameContext`. Prompt
regeneration reads that snapshot, not current pawn genes, name, relations, pollution, or mechanitor
state. Later changes must not rewrite the past.

Family state may update the child name before the pending birth is first emitted. Once an event is
created, its context is immutable apart from existing generation/status fields.

### 15.6 Save timing

Before saving:

- do not force unresolved growth choices;
- do not flush a pending birth merely because a save occurs;
- close transient scopes and release any staged mature signals that cannot be safely serialized;
- persist canonical pending growth/birth rows;
- preserve existing interaction/tale flush behavior.

After loading:

- clear all transient correlation caches;
- normalize/reindex Biotech state;
- reconcile pending growth rows with current pawn/age, not a live letter reference;
- resume birth naming polls;
- run silent baselines before any fallback observer can emit.

## 16. XML, prompts, settings, and localization

### 16.1 `DiaryBiotechPolicyDef`

Add one Def class/instance for tunable Biotech policy, not scattered C# constants. Proposed fields:

**Growth/family**

- growth tier band thresholds/descriptions;
- growth pending expiry/grace;
- family activity exact defNames/prefixes and pair dedup;
- support observation band thresholds;
- supporter minimum evidence and maximum rows;
- family arc retention/compaction age;
- birth naming poll/grace;
- maximum birth writers (hard defensive cap remains two).

**Genes**

- maximum themes;
- category weights and same-category penalty;
- event-delta/xenogene bonuses;
- minimum fallback significance;
- exact force/exclude corrections;
- per-fact and total text caps.

**Mechanitor**

- long-service ticks;
- consequential Tale families;
- boss chapter/correlation expiry;
- maximum controlled-mech/boss rows.

**B3**

- interrupted-deathrest severe threshold/cooldown;
- psychic-bond correlation expiry;
- any policy not already native to `DiaryObservedConditionDef`.

Code supplies safe fallbacks if the Def is missing. Stable save tokens and defensive caps may remain
in C#; narrative policy belongs in XML.

### 16.2 Interaction groups

Add exact groups in `1.6/Defs/DiaryInteractionGroupDefs.xml`, before catch-alls:

- `progressionGrowthMoment` in `Progression` domain matching `BiotechGrowthMoment`;
- `biotechFamilyBirth` in a concrete supported domain chosen during Phase 0 (prefer `Tale` if the
  classifier can accept a synthetic birth source without a new domain);
- exact mechanitor milestone groups in `Progression`/`Tale` as appropriate;
- psychic-bond/deathrest exact groups in `Progression` or another existing concrete domain.

Use `enableWhenPackageIdsLoaded` with `Ludeon.RimWorld.Biotech`. Matcher values are plain strings.
Do not reference Biotech Defs as XML objects.

Group instructions must enforce:

- growth is identity/opportunity, not a stats screen;
- birth allows mixed fear, grief, exhaustion, relief, or uncertainty and never assumes joy;
- gene changes use only selected themes;
- mechanitor moments center responsibility rather than machine specifications;
- bond rupture omits unproven cause;
- deathrest interruption omits unproven wake cause.

### 16.3 Observed-condition Defs

Add pollution Defs to `DiaryObservedConditionDefs.xml` using the new `MapPollution` observer. All
threshold/tone/page fields are XML-owned. No `MayRequire` is needed for plain numeric policy and our
own enum; the observer itself gates `ModsConfig.BiotechActive`.

### 16.4 Prompt contract

Prefer existing `GameContext` projection rather than adding a parallel model field. Add exact
`contextKey` selectors/template fragments only where current prompt modes would otherwise omit the
new facts.

Required formatting tests cover full, balanced, and compact modes. Compact mode keeps the central
fact:

- growth: trait/interests + opportunity band;
- birth: child/outcome + writer role;
- genes: top two themes;
- mechanitor: milestone + named mech/boss;
- pollution: band/transition;
- bond/deathrest: phase change.

Low-priority details drop first. IDs, raw internal tiers, ticks, and correlation tokens never become
natural-language prompt lines.

### 16.5 Settings

New groups appear in the existing event-filter UI; avoid a separate top-level `Enable Biotech`
switch. Package-unavailable groups stay hidden/unavailable through current group policy.

Add helper methods for effective legacy-inherited values described in §§8.8 and 9.7. Once a player
explicitly toggles the new group, save that override normally. Tests must cover absent override,
legacy false, legacy true, and new explicit override.

Family/gene/mechanitor observation remains active while the DLC is active even if generation groups
are off. It performs state bookkeeping only and must not make LLM calls.

### 16.6 Localization

All UI- or prompt-facing new English goes to the appropriate localization path:

- group `label`, `instruction`, `tones`, and policy descriptions in Defs with `DefInjected` entries;
- event labels/fallback texts/settings help in
  `Languages/English/Keyed/PawnDiary.xml` and `Translate()` on the main thread;
- structured schema tokens (`growth_moment=`, role tokens, `unknown`) remain stable English per the
  existing localization carve-out.

Never call `Translate()` from LLM/background code. Clean and store translated event-time labels on
the main thread.

Suggested new Keyed families:

- `PawnDiary.Event.Biotech.Growth.*`;
- `PawnDiary.Event.Biotech.Birth.*`;
- `PawnDiary.Event.Biotech.Mechanitor.*`;
- `PawnDiary.Event.Biotech.Bond.*`;
- `PawnDiary.Event.Biotech.Deathrest.*`;
- `PawnDiary.Settings.Biotech.*` where automatic advanced-field localization is insufficient.

## 17. File-level change map

Exact filenames may consolidate during Phase 0, but ownership should remain clear.

### 17.1 New production files

| File | Responsibility |
|---|---|
| `Source/Defs/DiaryBiotechPolicyDef.cs` | XML schema and safe fallbacks for Biotech policy. |
| `1.6/Defs/DiaryBiotechPolicyDef.xml` | Tunable growth/family/gene/mechanitor/B3 values. |
| `Source/Capture/Biotech/GrowthMomentPolicy.cs` | Pure before/after diff and significance. |
| `Source/Capture/Biotech/FamilySupportPolicy.cs` | Pure supporter selection and evidence bands. |
| `Source/Capture/Biotech/BirthOwnershipPolicy.cs` | Pure outcome/writer/owner decision. |
| `Source/Capture/Biotech/GeneSaliencePolicy.cs` | Pure theme ranking/dedup. |
| `Source/Capture/Biotech/MechanitorMilestonePolicy.cs` | Pure first/significant milestone decisions. |
| `Source/Capture/Events/GrowthMomentEventData.cs` | Concrete growth event payload. |
| `Source/Capture/Events/FamilyBirthEventData.cs` | Concrete birth payload. |
| `Source/Capture/Events/MechanitorEventData.cs` | Concrete controller milestone payload. |
| `Source/Capture/Events/BiotechBondEventData.cs` | Concrete psychic-bond payload. |
| matching `Source/Capture/Specs/*.cs` | Catalog decisions for new types. |
| matching `Source/Ingestion/Sources/*.cs` | Impure signal emitters. |
| `Source/Models/BiotechFamilyArcState.cs` | Deep-scribed family/support state. |
| `Source/Models/PendingBiotechGrowthMoment.cs` | Saved postponed-choice ownership. |
| `Source/Models/PendingBiotechBirthState.cs` | Saved birth/naming ownership. |
| `Source/Models/BiotechPawnProgressionState.cs` | Gene/mechanitor/bond/deathrest baseline state. |
| `Source/Core/DiaryGameComponent.BiotechGrowth.cs` | Growth orchestration. |
| `Source/Core/DiaryGameComponent.BiotechFamily.cs` | Family/birth observation and polls. |
| `Source/Core/DiaryGameComponent.BiotechGenes.cs` | Gene mutation/scanner orchestration. |
| `Source/Core/DiaryGameComponent.BiotechMechanitor.cs` | Mechanitor state/milestones. |
| `Source/Generation/BiotechCorrelation.cs` | Bounded transient owner scopes/caches, if not split by concern. |
| `Source/Patches/DiaryBiotechPatches.cs` | Stable Biotech lifecycle patches. |
| `tests/DiaryBiotechPolicyTests/*` | Standalone pure policy suite. |

Keep pure files free of Verse/RimWorld references. If one large correlation file becomes difficult
to reason about, split growth/birth/gene/mechanitor/bond scopes rather than creating a universal DLC
event coordinator.

### 17.2 Existing production files likely to change

| File | Change |
|---|---|
| `Source/Generation/DlcContext.cs` | All guarded Biotech live snapshots. |
| `Source/Capture/DiaryEventType.cs` | Add concrete new event types only. |
| `Source/Capture/Catalog/DiaryEventCatalog.cs` | Register new Specs. |
| `Source/Core/DiaryGameComponent.cs` | Saved lists, Scribe, lifecycle reset/polls. |
| `Source/Core/DiaryGameComponent.Progression.cs` | Growth consumption and versioned gene fallback. |
| `Source/Models/PawnArcState.cs` | Nested Biotech progression state. |
| `Source/Patches/DiaryEventSignalPatches.cs` | Extend existing birthday patch and secondary arbitration. |
| `Source/Patches/DiarySocialLogPatches.cs` | Family interaction observation; Overseer observer. |
| `Source/Patches/DiaryThoughtPatches.cs` | Exact family/birth/bond secondary observation before page gate. |
| `Source/Patches/DiaryHealthPatches.cs` | Family/deathrest secondary observation before Hediff signal. |
| `Source/Patches/DiaryDeathPatches.cs` | Controlled-mech death snapshot/confirmation. |
| `Source/Ingestion/Sources/TaleSignal.cs` | Birth/mech-combat ownership qualification/fail-open. |
| `Source/Ingestion/Sources/AbilitySignal.cs` | Exact gene/boss duplicate claim. |
| ritual signal path | Childbirth owner attachment/suppression. |
| `Source/Defs/DiaryObservedConditionDef.cs` | MapPollution and backward-compatible fields. |
| `Source/Core/DiaryGameComponent.ObservedConditions.cs` | Pollution collection, highest-family prompt filter, capped witnesses. |
| `1.6/Defs/DiaryInteractionGroupDefs.xml` | Exact Biotech groups/prompts. |
| `1.6/Defs/DiaryObservedConditionDefs.xml` | Pollution thresholds. |
| `Languages/English/Keyed/PawnDiary.xml` | New keys. |
| `DOCUMENTATION.md` | Architecture, file map, events, settings, save state, DLC safety, localization. |
| `CHANGELOG.md` | Dated line per shipped slice. |

### 17.3 Tests to add/update

- `tests/DiaryBiotechPolicyTests` for pure growth/family/gene/mechanitor policies;
- `DiaryCapturePolicyTests` for new event types/decisions/dedup keys;
- `DiaryObservedConditionTests` for pollution lifecycle/highest-band behavior;
- `DiaryPipelineTests` for prompt context, templates, compact/full modes, POV isolation;
- `DiarySaveNormalizationTests` for new records;
- `PawnDiary.RimTest` fixtures for exact live hooks, Scribe round trips, no-DLC smoke, settings
  inheritance, and duplicate-source orders.

## 18. Phased implementation sequence

Each phase is its own reviewable change. Update `DOCUMENTATION.md`, `CHANGELOG.md`, and the committed
DLL whenever a phase changes runtime behavior.

### Phase 0 — Freeze B1 contracts and settings semantics

> **Implementation status (2026-07-15): complete.** Added assembly-free growth/family DTOs and pure
> diff, banding, supporter, writer, settings, and context policies; froze the two event types and
> synthetic Def names, complete `biotech-family|...` arc grammar, additive Scribe/context keys, B1
> policy Def, English/Russian localization, and exact package-gated groups. Both catalog types are
> registered but inert: no signal, save row, Harmony hook, or page behavior is active. The next
> permitted slice is Phase 1.

1. Confirm Narrative Continuity N0 token/arc-key contracts, then add plain growth/family DTOs and pure
   policies that map to them.
2. Add `DiaryBiotechPolicyDef` B1 fields/defaults.
3. Add new event types/Specs to pure catalog tests without live hooks.
4. Freeze synthetic defNames, context keys, Scribe keys, and legacy settings inheritance.
5. Add localization/group XML and validate Def classification.

Exit gate: pure tests prove diff, banding, supporter/writer selection, settings inheritance, and
context formatting. No gameplay hook is active yet.

Narrative Continuity N0–N1 must land before Phase 1 creates/persists new B1 events. Phase 0 may proceed
in parallel, but it must not create a temporary Biotech-only cross-DLC prompt or reference schema.

### Phase 1 — Growth observation and ordinary fallback

> **Implementation status (2026-07-15): complete.** The existing birthday patch now owns exact
> before/after capture; `ConfigureGrowthLetter`/`MakeChoices` register atomically and fail open;
> detached pending rows survive Scribe round trips and normalize malformed/duplicate/future state;
> auto and committed paths emit at most one child-solo composite or release Birthday; every trait and
> newly passionate skill baseline plus the 7/10/13 consumed marker advances independently of page
> settings. The growth page attaches N1 identity evidence without introducing an N2 provider. Pure,
> Scribe, no-DLC, and loaded-component flow fixtures cover the exit matrix; the vanilla letter/UI
> click-through remains part of the manual in-game acceptance run.

1. Extend birthday prefix/postfix snapshotting.
2. Add `ConfigureGrowthLetter`/`MakeChoices` hooks and transient correlation.
3. Persist pending growth rows and normalize/load them.
4. Implement auto-resolved diff and ordinary birthday fallback.
5. Consume trait/skill progression baselines.
6. Initially emit child solo only; keep supporter contract ready.

Exit gate: age 7/10/13, postpone, save/load, no-trait, age-13 extra-trait, auto path, disabled group,
and missing-correlation tests all produce zero/one correct page.

### Phase 2 — Family state and growth supporter POV

> **Implementation status (2026-07-15): complete.** Deep-scribed stable-ID family arcs observe exact
> pregnancy/labor, BabyPlay, lesson, and accepted lesson-memory facts before page settings; old saves
> baseline only current visible child/parent state. Deterministic supporter selection drives child solo,
> supporter solo, or pair growth pages, while bounded retention and hot/archive replay checks preserve
> exact ownership without inventing past upbringing.

1. Add saved family arcs and old-save baseline bootstrap.
2. Observe pregnancy/labor and append family IDs to existing contexts.
3. Observe exact `BabyPlay`/`Lesson*` pairs independently of teaching settings.
4. Select one supporter and add pair/solo role-specific growth output.
5. Add arc retention/compaction.

Exit gate: SpeakUp-loaded/teaching-disabled observation still enriches a later growth page, while
ordinary lesson pages remain governed by existing settings.

### Phase 3 — Canonical birth and naming

> **Implementation status (2026-07-16): complete.** The exact RimWorld 1.6
> `PregnancyUtility.ApplyBirthOutcome` boundary now opens a stack-safe correlation scope, stages mature
> Tale/Thought sources, reserves the exact later ritual owner, resolves exact child/corpse outcome and birth method, attaches the family
> arc, and selects at most two unique adult writers. Detached pending naming rows normalize and round-trip,
> poll the current child/corpse name, preserve the original birth tick, and reject hot/archive replay by
> exact family+child identity. Disabled, invalid, missing-hook, writerless, and thrown ownership fail open;
> exact miscarriage closes/enriches the matched arc while unexplained hediff disappearance remains silent
> `ended_unknown`. Pure policy/XML tests, Scribe/no-DLC/signature fixtures, and loaded-component birth flow
> coverage exercise the automated boundary; the live childbirth/naming acceptance matrix remains Phase 4.

1. Add birth correlation scope and staged secondary signals.
2. Add `FamilyBirthEventData`/Signal and writer selection.
3. Attach/create family arcs at birth.
4. Persist/poll pending naming state.
5. Correlate ritual/tale/thought duplicates and implement fail-open.
6. Add miscarriage arc closure/enrichment.

Exit gate: healthy, illness, stillbirth, surrogacy, vat, ritual/non-ritual, one/two/no eligible
writer, naming before/after save, disabled canonical group, and thrown-owner tests pass.

### Phase 4 — B1 compatibility and release hardening

> **Implementation status (2026-07-16): automated hardening complete; manual acceptance pending.**
> Important pair/solo templates now project growth opportunity/choice and birth participant/outcome
> facts without exposing IDs, numeric tiers, ticks, or correlation tokens. Compact keeps the plan's
> central facts under an exhausted optional budget. English/Russian indexed labels and localized dev
> previews are covered. No-Biotech metadata/guards, existing Scribe and old-save normalization paths,
> both adapter logic suites/builds, supporter/transient limits, and new XML-owned pending-row caps have
> been audited. Run and record `tests/SAVE_COMPATIBILITY_SMOKETEST.md` before marking this phase complete.

1. Prompt previews and compact/full mode review.
2. No-Biotech and old-save smoke tests.
3. SpeakUp and other adapter smoke tests.
4. Performance/cap diagnostics.
5. Documentation/changelog/DLL and manual acceptance playthrough.

Exit gate: B1 definition of done is met. B2 work does not begin in the same change.

### Phase 5 — Salient genes

1. Add pure projection contract/salience policy and XML.
2. Add guarded snapshots and versioned silent baseline.
3. Add exact xenogerm hooks and ability arbitration.
4. Upgrade fallback xenotype progression context.
5. Harden custom/modded xenotype text/caps.

Exit gate: selected themes are relevant and bounded; exact/fallback routes emit once; old saves and
routine gene recalculation stay silent.

### Phase 6 — Mechanitor lifecycle

1. Add state/baseline and mechlink hooks.
2. Observe first Overseer relation.
3. Qualify first controlled-mech combat through Tale ownership.
4. Add significant mech loss around death.
5. Add boss call/defeat chapters.
6. Tune XML significance and caps.

Exit gate: every milestone matrix case and duplicate order passes; routine mech operations remain
silent in a long dev simulation.

### Phase 7 — Pollution

1. Extend observed-condition contracts with backward-compatible defaults.
2. Add cheap pollution collector and old-save baseline.
3. Add exclusive-family prompt filtering and capped witnesses.
4. Add threshold Defs and lifecycle tests.

Exit gate: no cell enumeration in the diary poll path; threshold/reclamation pages are bounded;
existing observed-condition fixtures remain unchanged.

### Phase 8 — Psychic bond and interrupted deathrest

1. Add pair/deathrest pure policy/state.
2. Add recursive bond hooks and direct-secondary arbitration.
3. Add interrupted deathrest hook and hediff claim.
4. Review whether severe hemogen needs any page beyond existing context.

Exit gate: recursive methods emit once, causes remain truthful, and routine feeding/deathrest stays
silent.

## 19. Test plan

### 19.1 Pure `DiaryBiotechPolicyTests`

Growth:

- no mutation -> empty;
- selected trait, `NoTrait`, and null trait;
- one/multiple actual passion increases;
- chosen passion that failed to change is omitted;
- age-13 additional trait consumed but not mislabelled as chosen;
- nickname before/after;
- opportunity boundary values;
- deterministic supporter tiers/ties;
- child solo, supporter solo, pair, and no-writer decisions;
- stable dedup key by pawn+age;
- raw tiers/counts/work list absent from formatted context.

Family/birth:

- distinct/same birther, genetic mother, father;
- pregnancy/labor/birth match and ambiguous non-match;
- healthy/ill/stillbirth/vat/surrogacy outcome tokens;
- maximum two writers and role ordering;
- naming pending/complete/expired;
- upbringing bands and no-evidence omission;
- activity pair dedup;
- retention/compaction.

Genes:

- 0/1/2/4/many facts;
- event delta outranks unchanged gene;
- category diversity/collapse;
- deterministic tie;
- force/exclude rules;
- malformed/oversized description;
- no full membership in prompt output.

Mechanitor/B3:

- first milestone vs baseline;
- automatic numeric vs custom mech name;
- tenure boundary;
- boss call/defeat semantics;
- pollution threshold/debounce/highest family;
- sorted bond pair recursion;
- deathrest threshold/cooldown.

### 19.2 Existing pure suites

`DiaryCapturePolicyTests`:

- catalog registration and solo/pair decisions;
- generic event-type dedup behavior;
- user/signal/eligibility gates;
- owner/fallback decision tables.

`DiaryObservedConditionTests`:

- existing Defs unchanged with default new fields;
- meaningful/severe/critical coexistence;
- reclamation only when meaningful condition ends;
- old-save baseline suppresses catch-up;
- witness cap.

`DiaryPipelineTests`:

- exact group -> classifier -> prompt precedence;
- context key projection;
- child/supporter and birth-role POV isolation;
- full/balanced/compact outputs;
- event-time snapshot remains after live state changes;
- optional facets/topics omitted safely when no resolver exists.

`DiarySaveNormalizationTests`:

- null/duplicate/oversized/future-tick records;
- pending growth/birth normalization;
- old xenotype scalar migration;
- family/mechanitor compaction.

### 19.3 RimTest scenarios

Add focused fixtures, likely:

- `PawnDiaryBiotechGrowthFlowTests.cs`;
- `PawnDiaryBiotechFamilyFlowTests.cs`;
- `PawnDiaryBiotechGeneFlowTests.cs`;
- `PawnDiaryBiotechMechanitorFlowTests.cs`;
- `PawnDiaryBiotechBondFlowTests.cs`;
- extend observed-condition/DLC-safety/Scribe fixtures.

Required B1 scenarios:

1. Age-7 letter config suppresses immediate birthday; choice creates one growth event.
2. NoTrait + passions creates one correct event.
3. Age-13 extra generated trait does not create a scanner duplicate.
4. Postponed letter round-trips through Scribe and completes once.
5. Disabled growth + enabled legacy birthday emits one delayed ordinary birthday.
6. Auto-resolved path diffs or falls back safely.
7. Exact lesson observations survive teaching group disabled/SpeakUp loaded.
8. Parent vs observed teacher supporter selection uses correct role.
9. Pregnancy -> labor -> healthy birth -> naming -> growth shares one arc ID.
10. Surrogacy keeps birther/genetic mother/father distinct.
11. Ritual birth produces one canonical event, no tale/thought/ritual duplicate.
12. Stillbirth uses adult POVs and never a child first-person page.
13. Save/load before naming emits once after rename/deadline.
14. Owner failure releases mature signals.
15. Old save with child/parents baselines without backfill.

Required B2/B3 scenarios are added with their phases, gated not-applicable when Biotech is inactive.

### 19.4 Verification commands

Run the focused suite after each phase, then the repository verification hook. Representative direct
commands:

```powershell
dotnet run --project tests\DiaryBiotechPolicyTests\DiaryBiotechPolicyTests.csproj
dotnet run --project tests\DiaryCapturePolicyTests\DiaryCapturePolicyTests.csproj
dotnet run --project tests\DiaryObservedConditionTests\DiaryObservedConditionTests.csproj
dotnet run --project tests\DiaryPipelineTests\DiaryPipelineTests.csproj
dotnet run --project tests\DiarySaveNormalizationTests\DiarySaveNormalizationTests.csproj
MSBuild tests\PawnDiary.RimTest\PawnDiary.RimTest.csproj /t:Build /p:Configuration=Debug
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
powershell -ExecutionPolicy Bypass -File .githooks\verify.ps1
git diff --check
```

Also parse every changed XML file explicitly if the hook output does not name it. Build output
`1.6/Assemblies/PawnDiary.dll` is committed and must be staged with C# changes.

## 20. Acceptance scenarios

### B1 player-facing scenarios

1. **One memorable growth page:** a ten-year-old gains Kind and an interest in Medicine. The diary
   writes one reflective growth page, not birthday + trait + skill pages.
2. **No selected trait:** a child chooses no trait but gains passions. The page does not call
   `NoTrait` a personality.
3. **Observed parent:** a parent who repeatedly taught the child receives one companion POV; counts
   and growth tier stay hidden.
4. **Observed teacher, not guardian:** a non-parent teacher may write, but the prompt calls them a
   teacher/supporting adult, never guardian.
5. **No evidence:** no supporter is invented and the child writes solo.
6. **Delayed choice:** the player postpones, saves, reloads, then chooses. One page appears after the
   choice with current facts.
7. **Disabled growth:** an existing user who disabled birthdays does not unexpectedly receive growth
   pages unless they enable the new group.
8. **Family birth:** birther and other parent each write one perspective about the same named child;
   there is no separate generic birth/ritual page.
9. **Surrogacy:** prompts preserve all exact roles without collapsing them into `mother`.
10. **Difficult outcome:** infant illness or stillbirth is described with restraint and no assumed
    single emotion.
11. **Long continuity:** age-7 growth can reference observed teaching since birth using qualitative
    wording and the same family arc.
12. **SpeakUp compatibility:** disabling core teaching pages does not erase family observation or
    create duplicate conversations.

### B2/B3 player-facing scenarios

13. A custom xenotype change mentions two or three defining bodily/need themes, not twenty genes.
14. Reimplantation produces one identity page and no generic ability duplicate.
15. A new mechanitor records mechlink and first controlled mech, not every later assignment.
16. Losing `Lifter 1` after a day is silent; losing a custom-named or proven long-serving mech is
    remembered once by the controller.
17. A boss caller can later write that the summoned threat was defeated without claiming the kill.
18. Pollution colors entries at the current highest band and writes at most bounded crossing/reclaim
    pages.
19. A psychic bond forms/ruptures once for the pair despite recursive vanilla methods.
20. A severe early wake may be remembered; ordinary completed deathrest and feeding remain silent.
21. With Biotech inactive, ordinary Pawn Diary behavior, save/load, settings, and prompts are
    unchanged and error-free.

## 21. Performance, DLC safety, and failure handling

### 21.1 Performance budgets

- Growth/birth/gene exact hooks do work only on rare lifecycle calls.
- Family interaction observation performs cheap string/role checks before lookup/allocation.
- Family records are indexed transiently by child ID, pregnancy hediff ID, and open birther ID;
  rebuild indexes after load rather than serializing dictionaries.
- Gene projection runs on exact change and slow fallback cadence, never per tick.
- Mechanitor fallback scans eligible colonists and their bounded overseer lists slowly; no bandwidth
  event polling.
- Pollution reads one maintained float per eligible map at the Def poll interval.
- Bond/deathrest hooks are rare.
- Every cache/list has XML policy and a hard defensive cap.

### 21.2 DLC safety checklist

- Every live Biotech read begins behind `ModsConfig.BiotechActive`.
- `pawn.genes`, Biotech family state, `pawn.mechanitor`, and related reads live in `DlcContext`.
- No `DefDatabase<T>.GetNamed` for a Biotech Def; use strings or `GetNamedSilentFail` where necessary.
- XML matchers are strings and package-gated where exposed as event groups.
- Stable DLC Harmony targets are allowed because the shared assembly always contains them; their
  bodies still gate the DLC.
- If a target becomes private/generated/fragile, move it to `DiaryPatchRegistrar.TryRegister` with
  a null check and warning rather than bare `PatchAll`.
- `About/About.xml` remains DLC-independent.

### 21.3 Fail-open rules

- Never let diary capture alter vanilla return values, choices, birth outcomes, relations, gene
  mutation, mech control, pollution, or bond state.
- Harmony finalizers return original exceptions.
- A richer owner failure releases/stops suppressing mature generic signals.
- A missing supporter/gene description/cause yields less context, not no event.
- A malformed saved pending row is discarded or downgraded to ordinary fallback; it does not block
  future birthdays/births.
- Logging uses `WarningOnce`/bounded reporter paths, never one line per hot event or poll.

### 21.4 Privacy/text safety

- Clean/cap player-authored child, pawn, xenotype, and mech names.
- Do not log those names in diagnostics/error strings beyond existing scrubbed mechanisms.
- Do not log full gene descriptions, family event prose, or LLM context.
- Dev diagnostics may count policy outcomes/tokens but must not dump player-authored text.

## 22. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Birthday page records before choice | Stage only after real `ConfigureGrowthLetter`; suppress in existing birthday postfix. |
| Pending choice is lost across save | Deep-scribe pawn/age/tick snapshot; resolve without live letter reference. |
| MakeChoices recursion/early return misreports choice | Require false->true and actual before/after diff. |
| Age-13 trait scanner duplicates | Consume every post-choice trait key, expose only chosen trait. |
| Passion milestone duplicates | Advance chosen skills' saved milestone baseline immediately. |
| Family state depends on ineligible child diary | Component-owned arc keyed by child/pregnancy IDs. |
| Teaching disabled by SpeakUp | Observe exact activity before page/group gate. |
| Birth has three adults but model supports two POVs | Deterministic max-two role selector; retain third in shared context. |
| Birth secondary callbacks occur in varying order | Stack scope + pending/recent owner tokens + fail-open tests. |
| Naming never resolves | Flush at deadline/unavailability with current name. |
| Gene selector becomes a disguised dump | Hard max four, category collapse, total text cap, tests. |
| Transient gene state creates noise | Compare membership/xenotype, not active suppression; exact hooks own known causes. |
| Every mech gets called named | Reject automatic numerical names; require tenure or custom name. |
| Boss page invents killer | Store caller separately; defeat hook confirms outcome only. |
| Pollution poll becomes O(map cells) | Read world-tile cached fraction; performance fixture/code review. |
| Multiple pollution bands color prompt | Exclusive-family highest-rank filter. |
| Recursive psychic bond emits twice | Canonical sorted pair+epoch scope. |
| DLC inactive but shared types compile | Runtime gates, null checks, no DLC Def lookup/dependency, smoke test. |
| Royalty/Ideology branches introduce shared facet contracts | Treat facets as optional tokens; rebase Phase 0 and reuse the merged seam without changing page ownership. |

## 23. Deferred follow-ons

After B1–B3, consider only with fresh evidence:

- a family reflection page after several growth moments, bounded to one childhood arc summary;
- exact childcare job completion if PlayLog/thought evidence proves insufficient;
- adoption/guardianship when vanilla/mod APIs provide exact durable roles;
- growth-vat entry/exit chapters separate from birth only if they are rare and non-duplicative;
- mechanitor command-tier milestones from a verified semantic unlock rather than bandwidth;
- recovery/resurrection of a significant mech as the closing half of a loss arc;
- first severe hemogen crisis after prompt-only coverage is measured;
- psychic-bond restoration after resurrection through an exact restore hook;
- mod extension APIs that submit plain family/gene/mechanitor evidence without live objects.

Do not implement these merely because the state is available. Reapply the question: “Why would this
pawn remember this event years later?”

## 24. Coding-agent handoff checklist

Before starting a phase:

- [ ] Read `AGENTS.md`, `skills/pawndiary-engineering/SKILL.md`, `DOCUMENTATION.md`, and this plan.
- [ ] Use CodeGraph first for every current code seam.
- [ ] Re-check the dirty worktree and preserve unrelated adapter DLL/Royalty-plan/Steam changes.
- [ ] Re-verify any RimWorld method signature touched by the phase.
- [ ] Confirm the phase's event owner and fail-open secondary route.
- [ ] Add/update the plan for the exact files/tests if current architecture has moved.

During implementation:

- [ ] Keep all DLC live reads in guarded `DlcContext` methods.
- [ ] Pass plain DTOs into pure policies.
- [ ] Put tunable thresholds/tokens/text/caps in XML with safe fallbacks.
- [ ] Use stable IDs and immutable event-time context.
- [ ] Advance observation state even when page generation is disabled.
- [ ] Mark event ownership when the `DiaryEvent` is created, not after LLM success.
- [ ] Add novice-friendly file headers, XML comments, and public summaries.
- [ ] Add focused pure tests before or with impure orchestration.
- [ ] Test both callback orders and owner failure for every claimed secondary signal.

Before handing off a shipped slice:

- [ ] Run focused pure tests.
- [ ] Build/update RimTest fixtures.
- [ ] Run the Debug MSBuild build and full verification hook.
- [ ] Parse changed XML and run `git diff --check`.
- [ ] Inspect `git status` so only intended files and the rebuilt Pawn Diary DLL are included.
- [ ] Update relevant `DOCUMENTATION.md` sections and add a dated `CHANGELOG.md` entry.
- [ ] Confirm no-Biotech load/tick/save behavior.
- [ ] Confirm the slice meets its exit gate and definition of done; do not bundle the next boundary.
