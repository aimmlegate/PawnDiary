# Event catalog

This is the searchable manual test matrix for two different inventories: **31 runtime event-source routes** and **120 core XML classification groups**. A runtime route proves that a signal can reach the common dispatcher. A group proves how a reached signal is classified. Neither count substitutes for the other.

Use prompt-test mode when you need to inspect the selected template, model lane, or final prompt without sending a request. “Admitted” below means pawn eligibility, player settings, chance, deduplication, pacing, and route-specific semantic checks all passed.

## Runtime event-source index

| Runtime ID | Player-visible family | Capture mechanism | Prerequisite | Expected outcome |
|---|---|---|---|---|
| `Thought` | Thoughts and memories | immediate notification | Base game | A solo internal-state page, ambient note, or no page according to the matched Thought policy. |
| `Inspiration` | Inspirations | immediate notification | Base game | One solo internal-state page when enabled and admitted. |
| `MoodEvent` | Map and colony mood events | immediate notification | Base game or matching DLC/mod condition | One solo page per admitted map colonist. |
| `MentalState` | Mental breaks and social fights | immediate notification | Base game | A solo internal-state page; social fights carry the paired opponent facts when known. |
| `Tale` | Combat, work, life, health, and incident tales | immediate notification | Base game or matching DLC/mod TaleDef | Immediate or batched solo/paired page; dedicated source-owned pages suppress matching generic tales. |
| `Hediff` | Health and body changes | immediate notification | Base game or matching DLC/mod hediff | Immediate solo page or day-reflection evidence, as the nested group policy specifies. |
| `Interaction` | Social interactions | immediate notification | Base game or matching social mod | Separate POV pages, a pair batch, or an ambient solo note. |
| `Arrival` | Colonist arrivals | multi-step correlation | Base game | A neutral arrival description stored at the pawn diary lifetime boundary. |
| `Death` | Colonist deaths | multi-step correlation | Base game | A neutral death description closing the pawn diary lifetime. |
| `Work` | Work experiences | scheduled state check | Base game | Solo page or low-salience pacing/reflection evidence. |
| `ThoughtProgression` | Lasting thought progression | scheduled state check | Base game or matching DLC thought | A solo internal-state page for a newly crossed stage. |
| `DayReflection` | Daily reflections | scheduled state check | Base game | One solo day-reflection page; low-salience folded moments may supply the evidence. |
| `Progression` | Birthdays, skills, titles, genes, records, and anniversaries | scheduled state check | Base game; individual subroutes may need DLC | One solo milestone page, subject to the exact progression policy. |
| `ArcReflection` | Year and narrative-arc reflections | scheduled state check | Base game | One longer solo arc-reflection page. |
| `BeliefReflection` | Belief reflections | scheduled state check | Ideology | One solo belief-reflection page. |
| `SocialReflection` | Delayed social reflections | scheduled state check | Base game | One initiator-only solo reflection page for an admitted meaningful interaction. |
| `Romance` | Relationship milestones | immediate notification | Base game | Two independent POV pages when both pawns are eligible; otherwise the eligible POV only. |
| `Raid` | Raids and infestations | immediate notification | Base game or matching DLC/mod incident | One solo page per admitted colonist on the target map. |
| `Quest` | Quest lifecycle | immediate notification | Base game or matching DLC/mod quest | All-eligible fan-out or one deterministic map witness, as the group policy specifies. |
| `Ritual` | Ritual outcomes | immediate notification | Ideology; Anomaly/Odyssey add optional ritual families | Separate participant/organizer/target POV pages. |
| `Ability` | Ability uses | immediate notification | Any loaded content providing pawn abilities | One solo page for the caster when chance, cooldown, and group policy admit it. |
| `External` | Adapter-submitted events | external submission | Pawn Diary external integrations enabled | A normal generated page, adapter-guided page, or caller-authored direct page. |
| `GrowthMoment` | Biotech growth moments | multi-step correlation | Biotech | An important first-person growth page with frozen before/after facts. |
| `FamilyBirth` | Biotech births and lineage | multi-step correlation | Biotech | Separate important family POV pages for eligible participants. |
| `BiotechBond` | Psychic bonds and deathrest | multi-step correlation | Biotech | Important paired or solo lifecycle page with verified cause only when known. |
| `GravshipJourney` | Gravship journeys | multi-step correlation | Odyssey | One important solo page or one paired journey event with at most two POVs. |
| `OdysseyEvent` | Odyssey source-owned outcomes | multi-step correlation | Odyssey | One important source-owned outcome page; generic quest capture remains the fail-open fallback. |
| `PersonaWeapon` | Persona-weapon lifecycle | multi-step correlation | Royalty | Important lifecycle or milestone page. |
| `RoyalPermit` | Dramatic royal permits | multi-step correlation | Royalty | One important solo permit page for the title holder. |
| `AnomalyEvent` | Anomaly study, containment, transformations, and terminal outcomes | multi-step correlation | Anomaly | One important source-owned page; matching generic Tales are suppressed. |
| `ArtImmortalized` | Colony deeds depicted in art | multi-step correlation | Base game | One low-salience solo page per deed, written by the subject when possible or the artist otherwise. |

<!-- repowiki:runtime {"id":"Thought"} -->
## Runtime source: `Thought`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Thoughts and memories |
| Capture mechanism | immediate notification |
| Prerequisite | Base game |
| Reproducible setup/trigger | Give an eligible colonist a new memory/thought. |
| Expected signal route | Thought hook -> ThoughtSignal -> common dispatcher. |
| Expected outcome/evidence | A solo internal-state page, ambient note, or no page according to the matched Thought policy. |
| Classification mapping | Thought-domain groups. |
| Source | [Source/Ingestion/Sources/ThoughtSignal.cs](../../../Source/Ingestion/Sources/ThoughtSignal.cs) — `ThoughtSignal` |

<!-- repowiki:runtime {"id":"Inspiration"} -->
## Runtime source: `Inspiration`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Inspirations |
| Capture mechanism | immediate notification |
| Prerequisite | Base game |
| Reproducible setup/trigger | Give an eligible colonist an inspiration. |
| Expected signal route | Inspiration gain -> InspirationSignal -> common dispatcher. |
| Expected outcome/evidence | One solo internal-state page when enabled and admitted. |
| Classification mapping | Inspiration-domain groups. |
| Source | [Source/Ingestion/Sources/InspirationSignal.cs](../../../Source/Ingestion/Sources/InspirationSignal.cs) — `InspirationSignal` |

<!-- repowiki:runtime {"id":"MoodEvent"} -->
## Runtime source: `MoodEvent`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Map and colony mood events |
| Capture mechanism | immediate notification |
| Prerequisite | Base game or matching DLC/mod condition |
| Reproducible setup/trigger | Register a matching game condition on a player map. |
| Expected signal route | Game-condition registration -> MoodEventSignal -> fan-out dispatcher. |
| Expected outcome/evidence | One solo page per admitted map colonist. |
| Classification mapping | MoodEvent-domain groups. |
| Source | [Source/Ingestion/Sources/MoodEventSignal.cs](../../../Source/Ingestion/Sources/MoodEventSignal.cs) — `MoodEventSignal` |

<!-- repowiki:runtime {"id":"MentalState"} -->
## Runtime source: `MentalState`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Mental breaks and social fights |
| Capture mechanism | immediate notification |
| Prerequisite | Base game |
| Reproducible setup/trigger | Start a matching mental state on an eligible colonist. |
| Expected signal route | Confirmed mental-state start -> MentalStateSignal -> dispatcher. |
| Expected outcome/evidence | A solo internal-state page; social fights carry the paired opponent facts when known. |
| Classification mapping | MentalState-domain groups. |
| Source | [Source/Ingestion/Sources/MentalStateSignal.cs](../../../Source/Ingestion/Sources/MentalStateSignal.cs) — `MentalStateSignal` |

<!-- repowiki:runtime {"id":"Tale"} -->
## Runtime source: `Tale`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Combat, work, life, health, and incident tales |
| Capture mechanism | immediate notification |
| Prerequisite | Base game or matching DLC/mod TaleDef |
| Reproducible setup/trigger | Perform an action that records a matching TaleDef. |
| Expected signal route | TaleRecorder boundary -> TaleSignal -> dispatcher or Tale batch. |
| Expected outcome/evidence | Immediate or batched solo/paired page; dedicated source-owned pages suppress matching generic tales. |
| Classification mapping | Tale-domain groups. |
| Source | [Source/Ingestion/Sources/TaleSignal.cs](../../../Source/Ingestion/Sources/TaleSignal.cs) — `TaleSignal` |

<!-- repowiki:runtime {"id":"Hediff"} -->
## Runtime source: `Hediff`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Health and body changes |
| Capture mechanism | immediate notification |
| Prerequisite | Base game or matching DLC/mod hediff |
| Reproducible setup/trigger | Add or materially progress a matching hediff. |
| Expected signal route | Health hook or progression scan -> HediffSignal -> dispatcher. |
| Expected outcome/evidence | Immediate solo page or day-reflection evidence, as the nested group policy specifies. |
| Classification mapping | Hediff-domain groups. |
| Source | [Source/Ingestion/Sources/HediffSignal.cs](../../../Source/Ingestion/Sources/HediffSignal.cs) — `HediffSignal` |

<!-- repowiki:runtime {"id":"Interaction"} -->
## Runtime source: `Interaction`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Social interactions |
| Capture mechanism | immediate notification |
| Prerequisite | Base game or matching social mod |
| Reproducible setup/trigger | Cause a matching social-log interaction between eligible pawns. |
| Expected signal route | PlayLog.Add -> InteractionSignal -> immediate, promoted, or batched route. |
| Expected outcome/evidence | Separate POV pages, a pair batch, or an ambient solo note. |
| Classification mapping | Interaction-domain groups. |
| Source | [Source/Ingestion/Sources/InteractionSignal.cs](../../../Source/Ingestion/Sources/InteractionSignal.cs) — `InteractionSignal` |

<!-- repowiki:runtime {"id":"Arrival"} -->
## Runtime source: `Arrival`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Colonist arrivals |
| Capture mechanism | multi-step correlation |
| Prerequisite | Base game |
| Reproducible setup/trigger | Start a colony or add a humanlike colonist through a confirmed join route. |
| Expected signal route | Arrival hook/initial baseline -> ArrivalSignal -> lifetime boundary -> dispatcher. |
| Expected outcome/evidence | A neutral arrival description stored at the pawn diary lifetime boundary. |
| Classification mapping | The exact synthetic arrival route maps to `arrival`. |
| Source | [Source/Ingestion/Sources/ArrivalSignal.cs](../../../Source/Ingestion/Sources/ArrivalSignal.cs) — `ArrivalSignal` |

<!-- repowiki:runtime {"id":"Death"} -->
## Runtime source: `Death`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Colonist deaths |
| Capture mechanism | multi-step correlation |
| Prerequisite | Base game |
| Reproducible setup/trigger | Kill an eligible current or former diary owner. |
| Expected signal route | Death notification plus Tale ownership correlation -> DeathFallbackSignal when no richer death Tale owns it. |
| Expected outcome/evidence | A neutral death description closing the pawn diary lifetime. |
| Classification mapping | Death Tale policy where available; fallback is route-owned. |
| Source | [Source/Ingestion/Sources/DeathFallbackSignal.cs](../../../Source/Ingestion/Sources/DeathFallbackSignal.cs) — `DeathFallbackSignal` |

<!-- repowiki:runtime {"id":"Work"} -->
## Runtime source: `Work`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Work experiences |
| Capture mechanism | scheduled state check |
| Prerequisite | Base game |
| Reproducible setup/trigger | Keep a colonist on passionate, straining, routine, or configured study work until the work scan runs. |
| Expected signal route | Periodic work snapshot comparison -> WorkSignal -> dispatcher. |
| Expected outcome/evidence | Solo page or low-salience pacing/reflection evidence. |
| Classification mapping | Work-domain groups. |
| Source | [Source/Ingestion/Sources/WorkSignal.cs](../../../Source/Ingestion/Sources/WorkSignal.cs) — `WorkSignal` |

<!-- repowiki:runtime {"id":"ThoughtProgression"} -->
## Runtime source: `ThoughtProgression`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Lasting thought progression |
| Capture mechanism | scheduled state check |
| Prerequisite | Base game or matching DLC thought |
| Reproducible setup/trigger | Keep a tracked thought active long enough to cross a configured stage. |
| Expected signal route | Periodic thought-stage comparison -> ThoughtProgressionSignal -> dispatcher. |
| Expected outcome/evidence | A solo internal-state page for a newly crossed stage. |
| Classification mapping | Thought-domain classification plus thought-progression policy. |
| Source | [Source/Ingestion/Sources/ThoughtProgressionSignal.cs](../../../Source/Ingestion/Sources/ThoughtProgressionSignal.cs) — `ThoughtProgressionSignal` |

<!-- repowiki:runtime {"id":"DayReflection"} -->
## Runtime source: `DayReflection`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Daily reflections |
| Capture mechanism | scheduled state check |
| Prerequisite | Base game |
| Reproducible setup/trigger | Accumulate eligible day evidence and cross the daily reflection boundary. |
| Expected signal route | Daily scheduler -> selected evidence -> DayReflectionSignal. |
| Expected outcome/evidence | One solo day-reflection page; low-salience folded moments may supply the evidence. |
| Classification mapping | The `dayreflection` group. |
| Source | [Source/Ingestion/Sources/DayReflectionSignal.cs](../../../Source/Ingestion/Sources/DayReflectionSignal.cs) — `DayReflectionSignal` |

<!-- repowiki:runtime {"id":"Progression"} -->
## Runtime source: `Progression`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Birthdays, skills, titles, genes, records, and anniversaries |
| Capture mechanism | scheduled state check |
| Prerequisite | Base game; individual subroutes may need DLC |
| Reproducible setup/trigger | Cross a configured progression threshold or calendar milestone. |
| Expected signal route | Progression/anniversary scanners -> ProgressionSignal -> dispatcher. |
| Expected outcome/evidence | One solo milestone page, subject to the exact progression policy. |
| Classification mapping | Progression-domain groups. |
| Source | [Source/Ingestion/Sources/ProgressionSignal.cs](../../../Source/Ingestion/Sources/ProgressionSignal.cs) — `ProgressionSignal` |

<!-- repowiki:runtime {"id":"ArcReflection"} -->
## Runtime source: `ArcReflection`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Year and narrative-arc reflections |
| Capture mechanism | scheduled state check |
| Prerequisite | Base game |
| Reproducible setup/trigger | Reach the configured arc/year boundary with eligible remembered evidence. |
| Expected signal route | Arc scheduler and memory selector -> ArcReflectionSignal. |
| Expected outcome/evidence | One longer solo arc-reflection page. |
| Classification mapping | The `reflection` group. |
| Source | [Source/Ingestion/Sources/ArcReflectionSignal.cs](../../../Source/Ingestion/Sources/ArcReflectionSignal.cs) — `ArcReflectionSignal` |

<!-- repowiki:runtime {"id":"BeliefReflection"} -->
## Runtime source: `BeliefReflection`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Belief reflections |
| Capture mechanism | scheduled state check |
| Prerequisite | Ideology |
| Reproducible setup/trigger | Accumulate belief evidence and reach the configured belief-reflection boundary. |
| Expected signal route | Ideology-gated belief scheduler -> BeliefReflectionSignal. |
| Expected outcome/evidence | One solo belief-reflection page. |
| Classification mapping | The `reflectionBelief` group. |
| Source | [Source/Ingestion/Sources/BeliefReflectionSignal.cs](../../../Source/Ingestion/Sources/BeliefReflectionSignal.cs) — `BeliefReflectionSignal` |

<!-- repowiki:runtime {"id":"SocialReflection"} -->
## Runtime source: `SocialReflection`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Delayed social reflections |
| Capture mechanism | scheduled state check |
| Prerequisite | Base game |
| Reproducible setup/trigger | Let two diary-eligible colonists complete an interaction in a group marked `socialReflectionEligible`, then wait for the admitted deterministic delay. |
| Expected signal route | Accepted InteractionSignal -> saved pending row -> delayed scheduler -> SocialReflectionSignal -> dispatcher. |
| Expected outcome/evidence | One initiator-only solo reflection page at most once for the claimed source interaction. |
| Classification mapping | The `socialReflection` group. |
| Source | [Source/Ingestion/Sources/SocialReflectionSignal.cs](../../../Source/Ingestion/Sources/SocialReflectionSignal.cs) — `SocialReflectionSignal` |

<!-- repowiki:runtime {"id":"Romance"} -->
## Runtime source: `Romance`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Relationship milestones |
| Capture mechanism | immediate notification |
| Prerequisite | Base game |
| Reproducible setup/trigger | Form or remove a lover, fiancé, spouse, or matching ex relation. |
| Expected signal route | Confirmed direct relation transition -> RomanceSignal -> paired dispatcher. |
| Expected outcome/evidence | Two independent POV pages when both pawns are eligible; otherwise the eligible POV only. |
| Classification mapping | Romance-domain groups, principally `romance_relation`. |
| Source | [Source/Ingestion/Sources/RomanceSignal.cs](../../../Source/Ingestion/Sources/RomanceSignal.cs) — `RomanceSignal` |

<!-- repowiki:runtime {"id":"Raid"} -->
## Runtime source: `Raid`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Raids and infestations |
| Capture mechanism | immediate notification |
| Prerequisite | Base game or matching DLC/mod incident |
| Reproducible setup/trigger | Execute a matching raid, infestation, or entity-attack incident. |
| Expected signal route | Confirmed incident execution -> RaidSignal -> map fan-out dispatcher. |
| Expected outcome/evidence | One solo page per admitted colonist on the target map. |
| Classification mapping | Raid-domain groups. |
| Source | [Source/Ingestion/Sources/RaidSignal.cs](../../../Source/Ingestion/Sources/RaidSignal.cs) — `RaidSignal` |

<!-- repowiki:runtime {"id":"Quest"} -->
## Runtime source: `Quest`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Quest lifecycle |
| Capture mechanism | immediate notification |
| Prerequisite | Base game or matching DLC/mod quest |
| Reproducible setup/trigger | Accept, complete, or fail a quest; acceptance is disabled by default. |
| Expected signal route | Quest lifecycle hook (plus acceptance reconciliation) -> QuestSignal -> fan-out. |
| Expected outcome/evidence | All-eligible fan-out or one deterministic map witness, as the group policy specifies. |
| Classification mapping | Quest-domain groups. |
| Source | [Source/Ingestion/Sources/QuestSignal.cs](../../../Source/Ingestion/Sources/QuestSignal.cs) — `QuestSignal` |

<!-- repowiki:runtime {"id":"Ritual"} -->
## Runtime source: `Ritual`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Ritual outcomes |
| Capture mechanism | immediate notification |
| Prerequisite | Ideology; Anomaly/Odyssey add optional ritual families |
| Reproducible setup/trigger | Finish a matching ritual with eligible participants. |
| Expected signal route | Confirmed ritual outcome -> RitualSignal/PsychicRitualSignal -> role-aware fan-out. |
| Expected outcome/evidence | Separate participant/organizer/target POV pages. |
| Classification mapping | Ritual-domain groups. |
| Source | [Source/Ingestion/Sources/RitualSignal.cs](../../../Source/Ingestion/Sources/RitualSignal.cs) — `RitualSignal` |

<!-- repowiki:runtime {"id":"Ability"} -->
## Runtime source: `Ability`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Ability uses |
| Capture mechanism | immediate notification |
| Prerequisite | Any loaded content providing pawn abilities |
| Reproducible setup/trigger | Successfully activate a matching ability; short-cooldown uses are sampled more heavily. |
| Expected signal route | Successful activation -> AbilitySignal -> dispatcher. |
| Expected outcome/evidence | One solo page for the caster when chance, cooldown, and group policy admit it. |
| Classification mapping | Ability-domain groups. |
| Source | [Source/Ingestion/Sources/AbilitySignal.cs](../../../Source/Ingestion/Sources/AbilitySignal.cs) — `AbilitySignal` |

<!-- repowiki:runtime {"id":"External"} -->
## Runtime source: `External`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Adapter-submitted events |
| Capture mechanism | external submission |
| Prerequisite | Pawn Diary external integrations enabled |
| Reproducible setup/trigger | Use a shipped adapter trigger or call the public SubmitEvent/SubmitPromptEntry/SubmitDirectEntry API. |
| Expected signal route | Adapter -> PawnDiaryApi -> ExternalEventSignal (or direct-entry sibling) -> dispatcher. |
| Expected outcome/evidence | A normal generated page, adapter-guided page, or caller-authored direct page. |
| Classification mapping | External-domain groups supplied by core or the adapter. |
| Source | [Source/Ingestion/Sources/ExternalEventSignal.cs](../../../Source/Ingestion/Sources/ExternalEventSignal.cs) — `ExternalEventSignal` |

<!-- repowiki:runtime {"id":"GrowthMoment"} -->
## Runtime source: `GrowthMoment`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Biotech growth moments |
| Capture mechanism | multi-step correlation |
| Prerequisite | Biotech |
| Reproducible setup/trigger | Complete a child growth moment with a chosen trait, passions, or responsibility change. |
| Expected signal route | Growth letter/dialog facts -> correlation -> GrowthMomentSignal. |
| Expected outcome/evidence | An important first-person growth page with frozen before/after facts. |
| Classification mapping | The `progressionGrowthMoment` group. |
| Source | [Source/Ingestion/Sources/GrowthMomentSignal.cs](../../../Source/Ingestion/Sources/GrowthMomentSignal.cs) — `GrowthMomentSignal` |

<!-- repowiki:runtime {"id":"FamilyBirth"} -->
## Runtime source: `FamilyBirth`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Biotech births and lineage |
| Capture mechanism | multi-step correlation |
| Prerequisite | Biotech |
| Reproducible setup/trigger | Complete a live birth or other supported birth outcome. |
| Expected signal route | Birth outcome plus parent/doctor correlation -> FamilyBirthSignal. |
| Expected outcome/evidence | Separate important family POV pages for eligible participants. |
| Classification mapping | The `biotechFamilyBirth` group. |
| Source | [Source/Ingestion/Sources/FamilyBirthSignal.cs](../../../Source/Ingestion/Sources/FamilyBirthSignal.cs) — `FamilyBirthSignal` |

<!-- repowiki:runtime {"id":"BiotechBond"} -->
## Runtime source: `BiotechBond`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Psychic bonds and deathrest |
| Capture mechanism | multi-step correlation |
| Prerequisite | Biotech |
| Reproducible setup/trigger | Form/rupture a psychic bond or interrupt a tracked deathrest. |
| Expected signal route | Bond/deathrest state correlation -> BiotechBondSignal. |
| Expected outcome/evidence | Important paired or solo lifecycle page with verified cause only when known. |
| Classification mapping | `biotechPsychicBondLifecycle` or `biotechDeathrestInterrupted`. |
| Source | [Source/Ingestion/Sources/BiotechBondSignal.cs](../../../Source/Ingestion/Sources/BiotechBondSignal.cs) — `BiotechBondSignal` |

<!-- repowiki:runtime {"id":"GravshipJourney"} -->
## Runtime source: `GravshipJourney`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Gravship journeys |
| Capture mechanism | multi-step correlation |
| Prerequisite | Odyssey |
| Reproducible setup/trigger | Launch, travel, and then successfully land a gravship. |
| Expected signal route | Takeoff/travel state -> landing correlation -> GravshipJourneySignal. |
| Expected outcome/evidence | One important solo page or one paired journey event with at most two POVs. |
| Classification mapping | The `odysseyGravshipLanding` group. |
| Source | [Source/Ingestion/Sources/GravshipJourneySignal.cs](../../../Source/Ingestion/Sources/GravshipJourneySignal.cs) — `GravshipJourneySignal` |

<!-- repowiki:runtime {"id":"OdysseyEvent"} -->
## Runtime source: `OdysseyEvent`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Odyssey source-owned outcomes |
| Capture mechanism | multi-step correlation |
| Prerequisite | Odyssey |
| Reproducible setup/trigger | Resolve the Mechhive by the verified destroy or scavenge route. |
| Expected signal route | Quest/source scope correlation -> OdysseyMechhiveOutcomeSignal. |
| Expected outcome/evidence | One important source-owned outcome page; generic quest capture remains the fail-open fallback. |
| Classification mapping | The `odysseyMechhiveOutcome` group. |
| Source | [Source/Ingestion/Sources/OdysseyMechhiveOutcomeSignal.cs](../../../Source/Ingestion/Sources/OdysseyMechhiveOutcomeSignal.cs) — `OdysseyMechhiveOutcomeSignal` |

<!-- repowiki:runtime {"id":"PersonaWeapon"} -->
## Runtime source: `PersonaWeapon`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Persona-weapon lifecycle |
| Capture mechanism | multi-step correlation |
| Prerequisite | Royalty |
| Reproducible setup/trigger | Form, meaningfully separate, recover, end, or reach the first consequential-kill milestone. |
| Expected signal route | Persona weapon state/Tale correlation -> PersonaWeaponSignal. |
| Expected outcome/evidence | Important lifecycle or milestone page. |
| Classification mapping | `personaWeaponLifecycle`; Tale-owned kill milestones use `personaWeaponMilestone`. |
| Source | [Source/Ingestion/Sources/PersonaWeaponSignal.cs](../../../Source/Ingestion/Sources/PersonaWeaponSignal.cs) — `PersonaWeaponSignal` |

<!-- repowiki:runtime {"id":"RoyalPermit"} -->
## Runtime source: `RoyalPermit`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Dramatic royal permits |
| Capture mechanism | multi-step correlation |
| Prerequisite | Royalty |
| Reproducible setup/trigger | Successfully use an allowlisted military aid, shuttle, strike, or salvo permit. |
| Expected signal route | Permit use plus incident/arrival correlation -> RoyalPermitSignal. |
| Expected outcome/evidence | One important solo permit page for the title holder. |
| Classification mapping | The `royalPermitDramatic` group. |
| Source | [Source/Ingestion/Sources/RoyalPermitSignal.cs](../../../Source/Ingestion/Sources/RoyalPermitSignal.cs) — `RoyalPermitSignal` |

<!-- repowiki:runtime {"id":"AnomalyEvent"} -->
## Runtime source: `AnomalyEvent`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Anomaly study, containment, transformations, and terminal outcomes |
| Capture mechanism | multi-step correlation |
| Prerequisite | Anomaly |
| Reproducible setup/trigger | Complete a supported study breakthrough, breach, visible creepjoiner outcome, ghoul transformation, or void outcome. |
| Expected signal route | Dedicated source hooks/correlators -> Anomaly event envelope -> dispatcher. |
| Expected outcome/evidence | One important source-owned page; matching generic Tales are suppressed. |
| Classification mapping | `anomalyStudyBreakthrough`, `anomalyContainmentBreach`, `anomalyCreepJoinerOutcome`, `anomalyGhoulTransformation`, or `anomalyVoidOutcome`. |
| Source | [Source/Capture/Events/AnomalyEventData.cs](../../../Source/Capture/Events/AnomalyEventData.cs) — `AnomalyEventData` |

<!-- repowiki:runtime {"id":"ArtImmortalized"} -->
## Runtime source: `ArtImmortalized`

| Test field | Source-verified expectation |
|---|---|
| Player-visible family | Colony deeds depicted in art |
| Capture mechanism | multi-step correlation |
| Prerequisite | Base game |
| Reproducible setup/trigger | Create a sculpture whose generated art tale points to a colony deed not already immortalized. |
| Expected signal route | Art description/Tale identity -> ownership check -> ArtImmortalizedSignal. |
| Expected outcome/evidence | One low-salience solo page per deed, written by the subject when possible or the artist otherwise. |
| Classification mapping | The `artImmortalized` group. |
| Source | [Source/Ingestion/Sources/ArtImmortalizedSignal.cs](../../../Source/Ingestion/Sources/ArtImmortalizedSignal.cs) — `ArtImmortalizedSignal` |

## Core interaction-group index

The core catalog currently contains **120 groups** and **731 explicit matcher tuples**. The tuple count includes exact, ordinal-exact, prefix, suffix, segment, substring-token, and package-ID items; catch-alls and synthetic batch names are reported separately.

| Group ID | Settings label | Domain | Default | Behavior | Matchers |
|---|---|---|---:|---|---:|
| `romance` | Romance & dating | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 13 |
| `romance_relation` | Romance milestones | `Romance` | yes | paired first-person pages, one independent POV per eligible pawn | 5 |
| `recruit` | Recruitment & prison | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 12 |
| `slavery` | Slavery | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 5 |
| `counsel` | Counsel | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 2 |
| `conversion` | Ideology & conversion | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 13 |
| `trial` | Trials & accusations | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 4 |
| `strangechat` | Strange chat | `Interaction` | yes | batched ambient note, normally one solo note per pawn/day | 1 |
| `anomaly` | Anomaly & dark | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 13 |
| `insults` | Insults & fights | `Interaction` | yes | batched paired page after the interaction batch flushes | 9 |
| `ritual` | Rituals & speeches | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 19 |
| `animal` | Animal handling | `Interaction` | yes | batched ambient note, normally one solo note per pawn/day | 10 |
| `heartfelt` | Heartfelt talk | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 10 |
| `teaching` | Teaching & lessons | `Interaction` | yes | batched ambient note, normally one solo note per pawn/day | 5 |
| `smalltalk` | Small talk | `Interaction` | yes | batched ambient note, normally one solo note per pawn/day | 15 |
| `arrival` | Arrival | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 2 |
| `eventWindowVoidMonolith` | Void monolith | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 4 |
| `eventWindowHeartAttack` | Heart attack | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `eventWindowBirthday` | Birthday | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `eventWindowAncientDanger` | Ancient danger | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `eventWindowPrisonBreak` | Prison break | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `eventWindowMechCluster` | Mech cluster | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `observedPitGate` | Pit gate | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `observedFleshmassHeart` | Fleshmass heart | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `artImmortalized` | Immortalized in art | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `observedBiotechPollution` | Colony pollution | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 3 |
| `other` | A quiet day | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 0 |
| `dayreflection` | Day's reflection | `Reflection` | yes | scheduled reflection page | 1 |
| `quadrumreflection` | Quadrum reflection | `Reflection` | yes | scheduled reflection page | 1 |
| `reflectionBelief` | Belief reflection | `Reflection` | yes | scheduled reflection page | 1 |
| `socialReflection` | Social reflection | `Reflection` | yes | scheduled reflection page | 1 |
| `reflection` | Reflection | `Reflection` | yes | scheduled reflection page | 1 |
| `beliefCrisis` | Crisis of belief | `MentalState` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `socialfight` | Social fights | `MentalState` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `insultspree` | Insult sprees | `MentalState` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `mentalbreakViolent` | Violent breaks | `MentalState` | yes | immediate solo or route-owned page when the source is admitted | 11 |
| `mentalbreakEscape` | Withdrawal breaks | `MentalState` | yes | immediate solo or route-owned page when the source is admitted | 7 |
| `mentalbreakIndulgent` | Compulsive breaks | `MentalState` | yes | immediate solo or route-owned page when the source is admitted | 7 |
| `mentalbreak` | Mental breaks | `MentalState` | yes | immediate solo or route-owned page when the source is admitted | 0 |
| `personaWeaponMilestone` | Persona weapon milestones | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `talecombat` | Combat, injuries & death | `Tale` | yes | batched solo page after the Tale batch flushes | 15 |
| `talehealth` | Health & medicine | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 8 |
| `biotechFamilyBirth` | Family birth | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `talelife` | Life milestones | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 14 |
| `talequality` | Masterworks & legendary crafts | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `talework` | Work & achievements | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 6 |
| `taleanomaly` | Anomaly horror | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 8 |
| `taleincident` | Raids, disasters & colony events | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 30 |
| `talequiet` | Quiet personal moments | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 14 |
| `taleother` | A notable day | `Tale` | yes | immediate solo or route-owned page when the source is admitted | 0 |
| `moodeventPositive` | Positive mood events | `MoodEvent` | yes | immediate solo or route-owned page when the source is admitted | 3 |
| `moodeventWeatherHardship` | Climate hardship | `MoodEvent` | yes | immediate solo or route-owned page when the source is admitted | 8 |
| `moodeventStormDanger` | Sky violence | `MoodEvent` | yes | immediate solo or route-owned page when the source is admitted | 4 |
| `moodeventNegative` | Negative mood events | `MoodEvent` | yes | immediate solo or route-owned page when the source is admitted | 9 |
| `moodeventMixed` | Situationally mixed mood events | `MoodEvent` | yes | immediate solo or route-owned page when the source is admitted | 2 |
| `moodeventOther` | Passing moods | `MoodEvent` | yes | immediate solo or route-owned page when the source is admitted | 0 |
| `thoughtPregnancyFamily` | Pregnancy memories | `Thought` | yes | immediate solo or route-owned page when the source is admitted | 5 |
| `thoughtPositive` | Positive thoughts | `Thought` | yes | immediate solo or route-owned page when the source is admitted | 97 |
| `thoughtNegative` | Negative thoughts | `Thought` | yes | immediate solo or route-owned page when the source is admitted | 197 |
| `thoughtOther` | Passing thoughts | `Thought` | yes | immediate solo or route-owned page when the source is admitted | 0 |
| `inspiration` | Inspirations | `Inspiration` | yes | immediate solo or route-owned page when the source is admitted | 8 |
| `workDarkStudy` | Dark study work | `Work` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `workPassion` | Passionate work | `Work` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `workStrain` | Straining work | `Work` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `workRoutine` | Routine work | `Work` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `hediffPartGainedAnomalous` | Anomalous body changes | `Hediff` | yes | immediate solo page from a health observation | 5 |
| `hediffPartGainedArtificial` | Artificial body parts | `Hediff` | yes | immediate solo page from a health observation | 1 |
| `hediffPartLostNatural` | Lost body parts | `Hediff` | yes | immediate solo page from a health observation | 1 |
| `hediffPregnancy` | Pregnancy | `Hediff` | yes | immediate solo page from a health observation | 2 |
| `hediffLabor` | Labor | `Hediff` | yes | immediate solo page from a health observation | 2 |
| `hediffAnomalyCompulsion` | Anomaly compulsions | `Hediff` | yes | immediate solo page from a health observation | 6 |
| `hediffMajorHealth` | Major health changes | `Hediff` | yes | reflection evidence; appears in a later day reflection | 0 |
| `raidFriendly` | Friendly arrivals & raids | `Raid` | yes | colony/map fan-out: one solo page per admitted colonist | 2 |
| `raidDropPod` | Drop-pod raids | `Raid` | yes | colony/map fan-out: one solo page per admitted colonist | 1 |
| `raidInfestation` | Infestations | `Raid` | yes | colony/map fan-out: one solo page per admitted colonist | 2 |
| `raidAnomalyEntities` | Entity attacks | `Raid` | yes | colony/map fan-out: one solo page per admitted colonist | 11 |
| `raid` | Raids | `Raid` | yes | colony/map fan-out: one solo page per admitted colonist | 0 |
| `questRoyalAscent` | Royal Ascent | `Quest` | yes | one deterministic map-witness page | 1 |
| `questAccepted` | Quest accepted | `Quest` | no | colony/map fan-out: one solo page per admitted colonist | 1 |
| `questCompleted` | Quest completed | `Quest` | yes | colony/map fan-out: one solo page per admitted colonist | 1 |
| `questFailed` | Quest failed | `Quest` | yes | colony/map fan-out: one solo page per admitted colonist | 1 |
| `ritualConversion` | Conversion rituals | `Ritual` | yes | ritual participant/role fan-out page | 1 |
| `ritualRoyal` | Royal rituals | `Ritual` | yes | ritual participant/role fan-out page | 6 |
| `ritualChildbirth` | Childbirth ritual | `Ritual` | yes | ritual participant/role fan-out page | 2 |
| `ritualGravship` | Gravship launch | `Ritual` | yes | ritual participant/role fan-out page | 2 |
| `personaWeaponLifecycle` | Persona weapon bonds | `PersonaWeapon` | yes | immediate solo or route-owned page when the source is admitted | 4 |
| `royalPermitDramatic` | Dramatic royal permits | `RoyalPermit` | yes | immediate solo or route-owned page when the source is admitted | 4 |
| `odysseyGravshipLanding` | Gravship landing | `GravshipJourney` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `odysseyMechhiveOutcome` | Fate of the Mechhive | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `anomalyStudyBreakthrough` | Anomaly study breakthroughs | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `anomalyContainmentBreach` | Anomaly containment breaches | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `anomalyCreepJoinerOutcome` | Visible strange-arrival outcomes | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `anomalyGhoulTransformation` | Ghoul transformations | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `anomalyVoidOutcome` | Answer to the void | `Interaction` | yes | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page | 1 |
| `ritualAnomalyInvitation` | Anomaly invitations | `Ritual` | yes | ritual participant/role fan-out page | 3 |
| `ritualAnomalyFleshAndWeather` | Flesh and hostile weather rituals | `Ritual` | yes | ritual participant/role fan-out page | 4 |
| `ritualAnomalyPredation` | Predatory psychic rituals | `Ritual` | yes | ritual participant/role fan-out page | 3 |
| `ritualAnomalyMind` | Mind-altering psychic rituals | `Ritual` | yes | ritual participant/role fan-out page | 3 |
| `ritualAnomalyAbduction` | Skip abduction rituals | `Ritual` | yes | ritual participant/role fan-out page | 2 |
| `ritualAnomalyDeathRefusal` | Death-refusal ritual | `Ritual` | yes | ritual participant/role fan-out page | 1 |
| `ritualAnomalyPsychic` | Psychic rituals | `Ritual` | yes | ritual participant/role fan-out page | 1 |
| `ritualFinished` | Rituals | `Ritual` | yes | ritual participant/role fan-out page | 0 |
| `abilityPsycast` | Psycasts | `Ability` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `abilityHostile` | Combat abilities | `Ability` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `abilityUsed` | Abilities | `Ability` | yes | immediate solo or route-owned page when the source is admitted | 0 |
| `biotechPsychicBondLifecycle` | Psychic-bond lifecycle | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 2 |
| `biotechDeathrestInterrupted` | Interrupted deathrest | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionMechanitorLifecycle` | Mechanitor lifecycle | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 7 |
| `progressionGrowthMoment` | Growth moment | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionSkillPassion` | Passion skill milestone | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionPsylink` | Psylink gained | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionXenotype` | Gene identity changed | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 2 |
| `progressionRoyalTitle` | Royal rank and succession | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 7 |
| `progressionTraitGained` | New trait | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionBirthday` | Birthday | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionArrivalAnniversary` | Colony anniversary | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionDeathAnniversary` | Remembered loss | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionRecordMilestone` | Personal record | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 1 |
| `progressionOther` | Progression | `Progression` | yes | immediate solo or route-owned page when the source is admitted | 0 |
| `externalDevTest` | External test event | `External` | yes | adapter-submitted page using the request shape chosen by the adapter | 1 |

<!-- repowiki:group {"defName":"romance","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RomanceAttempt","MarriageProposal","Breakup","Sentence_RomanceAttemptAccepted","Sentence_RomanceAttemptRejected","Sentence_MarriageProposalAccepted","Sentence_MarriageProposalRejected","Sentence_MarriageProposalRejectedBrokeUp"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Romance","Marriage","Breakup","Date","Hookup"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `romance` — Romance & dating

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Romance & dating** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `RomanceAttempt`, `MarriageProposal`, `Breakup`, `Sentence_RomanceAttemptAccepted`, `Sentence_RomanceAttemptRejected`, `Sentence_MarriageProposalAccepted`, `Sentence_MarriageProposalRejected`, `Sentence_MarriageProposalRejectedBrokeUp`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Romance`, `Marriage`, `Breakup`, `Date`, `Hookup`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `romance`

<!-- repowiki:group {"defName":"romance_relation","domain":"Romance","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Lover","Fiance","Spouse","ExLover","ExSpouse"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `romance_relation` — Romance milestones

| Policy field | Expected value |
|---|---|
| Domain | `Romance` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages, one independent POV per eligible pawn |
| Setup/trigger cue | Create the matching direct pawn-relation transition. |
| Expected evidence | The page or later batch/reflection uses the **Romance milestones** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Lover`, `Fiance`, `Spouse`, `ExLover`, `ExSpouse`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `romance_relation`

<!-- repowiki:group {"defName":"recruit","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BuildRapport","RecruitAttempt","Sentence_RecruitAttemptAccepted","Sentence_RecruitAttemptRejected","ReduceWill","EnslaveAttempt","SparkJailbreak"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Recruit","Rapport","Jailbreak","Enslave","ReduceWill"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `recruit` — Recruitment & prison

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Recruitment & prison** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BuildRapport`, `RecruitAttempt`, `Sentence_RecruitAttemptAccepted`, `Sentence_RecruitAttemptRejected`, `ReduceWill`, `EnslaveAttempt`, `SparkJailbreak`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Recruit`, `Rapport`, `Jailbreak`, `Enslave`, `ReduceWill`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `recruit`

<!-- repowiki:group {"defName":"slavery","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Suppress","SparkSlaveRebellion"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Suppress","Slave","Rebellion"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `slavery` — Slavery

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Slavery** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Suppress`, `SparkSlaveRebellion`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Suppress`, `Slave`, `Rebellion`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `slavery`

<!-- repowiki:group {"defName":"counsel","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":["Counsel_Success","Counsel_Failure"],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `counsel` — Counsel

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Ideology (`Ludeon.RimWorld.Ideology`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Counsel** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: `Counsel_Success`, `Counsel_Failure`
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `counsel`

<!-- repowiki:group {"defName":"conversion","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["ConvertIdeoAttempt","Convert_Success","Convert_Failure","PreachHealth","WorkDrive","Indoctrinate","RS_WorshipInteraction"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Convert","Preach","Indoctrinate","Worship","WorkDrive","Ideo"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `conversion` — Ideology & conversion

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Ideology & conversion** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `ConvertIdeoAttempt`, `Convert_Success`, `Convert_Failure`, `PreachHealth`, `WorkDrive`, `Indoctrinate`, `RS_WorshipInteraction`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Convert`, `Preach`, `Indoctrinate`, `Worship`, `WorkDrive`, `Ideo`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `conversion`

<!-- repowiki:group {"defName":"trial","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Trial_Accuse","Trial_Defend"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Trial","Accuse"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `trial` — Trials & accusations

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Trials & accusations** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Trial_Accuse`, `Trial_Defend`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Trial`, `Accuse`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `trial`

<!-- repowiki:group {"defName":"strangechat","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"StrangeChatAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["DisturbingChat"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `strangechat` — Strange chat

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | batched ambient note, normally one solo note per pawn/day |
| Batch contract | mode `AmbientDayNote`; scope `Pair`; window 60000 ticks; flush cap 999; synthetic ID `StrangeChatAmbientDay` |
| Promotion | A configured weighted chance can promote a matching batched moment to an immediate pair event. |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Strange chat** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `DisturbingChat`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `strangechat`

<!-- repowiki:group {"defName":"anomaly","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["DarkDialogue","CreepyWords","InhumanRambling","OccultTeaching","PrisonerStudyAnomaly","InterrogateIdentity"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Dark","Creepy","Inhuman","Disturbing","Occult","Anomaly","Interrogate"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `anomaly` — Anomaly & dark

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Anomaly & dark** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `DarkDialogue`, `CreepyWords`, `InhumanRambling`, `OccultTeaching`, `PrisonerStudyAnomaly`, `InterrogateIdentity`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Dark`, `Creepy`, `Inhuman`, `Disturbing`, `Occult`, `Anomaly`, `Interrogate`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `anomaly`

<!-- repowiki:group {"defName":"insults","domain":"Interaction","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":true,"batchMode":"PairEvent","batchScope":"Pair","batchSyntheticDefName":"InsultBatch","batchWindowTicks":7500,"batchMaxEvents":8,"catchAll":false,"matchDefNames":["Insult","Slight","Sentence_SocialFightStarted","Sentence_SocialFightConvoInitiatorStarted","Sentence_SocialFightConvoRecipientStarted"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Insult","Slight","Fight","Rebuff"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `insults` — Insults & fights

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / yes |
| Page/batch/reflection behavior | batched paired page after the interaction batch flushes |
| Batch contract | mode `PairEvent`; scope `Pair`; window 7500 ticks; flush cap 8; synthetic ID `InsultBatch` |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Insults & fights** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Insult`, `Slight`, `Sentence_SocialFightStarted`, `Sentence_SocialFightConvoInitiatorStarted`, `Sentence_SocialFightConvoRecipientStarted`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Insult`, `Slight`, `Fight`, `Rebuff`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `insults`

<!-- repowiki:group {"defName":"ritual","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["SpeechUtility","Speech_Duel","Speech_Funeral","Speech_Leader","Speech_Sacrifice","Speech_Scarification","Speech_Blinding","Speech_Execution","Speech_TreeConnection","Speech_Conversion","Speech_AcceptRole","Speech_RemoveRole","WordOfTrust","WordOfJoy","WordOfLove","WordOfSerenity","WordOfInspiration"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Speech","Ritual"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritual` — Rituals & speeches

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Rituals & speeches** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `SpeechUtility`, `Speech_Duel`, `Speech_Funeral`, `Speech_Leader`, `Speech_Sacrifice`, `Speech_Scarification`, `Speech_Blinding`, `Speech_Execution`, `Speech_TreeConnection`, `Speech_Conversion`, `Speech_AcceptRole`, `Speech_RemoveRole`, `WordOfTrust`, `WordOfJoy`, `WordOfLove`, `WordOfSerenity`, `WordOfInspiration`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Speech`, `Ritual`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritual`

<!-- repowiki:group {"defName":"animal","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"AnimalAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["AnimalChat","TameAttempt","TrainAttempt","Nuzzle","ReleaseToWild"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Animal","Tame","Train","Nuzzle","ReleaseToWild"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `animal` — Animal handling

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | batched ambient note, normally one solo note per pawn/day |
| Batch contract | mode `AmbientDayNote`; scope `Pair`; window 60000 ticks; flush cap 999; synthetic ID `AnimalAmbientDay` |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Animal handling** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `AnimalChat`, `TameAttempt`, `TrainAttempt`, `Nuzzle`, `ReleaseToWild`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Animal`, `Tame`, `Train`, `Nuzzle`, `ReleaseToWild`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `animal`

<!-- repowiki:group {"defName":"heartfelt","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["DeepTalk","KindWords","Reassure","SnapOut_CalmDownInteraction"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["DeepTalk","KindWords","Reassure","Comfort","CalmDown","SnapOut"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `heartfelt` — Heartfelt talk

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Heartfelt talk** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `DeepTalk`, `KindWords`, `Reassure`, `SnapOut_CalmDownInteraction`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `DeepTalk`, `KindWords`, `Reassure`, `Comfort`, `CalmDown`, `SnapOut`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `heartfelt`

<!-- repowiki:group {"defName":"teaching","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"TeachingAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["BabyPlay"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Lesson","Teaching","BabyPlay","Baby"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":["JPT.speakup"],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `teaching` — Teaching & lessons

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | batched ambient note, normally one solo note per pawn/day |
| Batch contract | mode `AmbientDayNote`; scope `Pair`; window 60000 ticks; flush cap 999; synthetic ID `TeachingAmbientDay` |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Teaching & lessons** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BabyPlay`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Lesson`, `Teaching`, `BabyPlay`, `Baby`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `teaching`

<!-- repowiki:group {"defName":"smalltalk","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":true,"batchMode":"AmbientDayNote","batchScope":"Pair","batchSyntheticDefName":"SmallTalkAmbientDay","batchWindowTicks":60000,"batchMaxEvents":999,"catchAll":false,"matchDefNames":["Chitchat","Conversation","EndConversation","HangOut","PrudeSeen","TourFinished","GR_TalkingToHumans","GR_UwUTalkingToHumans","LetsTalkEatTogether","OfferFood","SanguophageChat"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Chitchat","Chat","Conversation","HangOut"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `smalltalk` — Small talk

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | batched ambient note, normally one solo note per pawn/day |
| Batch contract | mode `AmbientDayNote`; scope `Pair`; window 60000 ticks; flush cap 999; synthetic ID `SmallTalkAmbientDay` |
| Promotion | A configured weighted chance can promote a matching batched moment to an immediate pair event. |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Small talk** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Chitchat`, `Conversation`, `EndConversation`, `HangOut`, `PrudeSeen`, `TourFinished`, `GR_TalkingToHumans`, `GR_UwUTalkingToHumans`, `LetsTalkEatTogether`, `OfferFood`, `SanguophageChat`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Chitchat`, `Chat`, `Conversation`, `HangOut`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `smalltalk`

<!-- repowiki:group {"defName":"arrival","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_Arrival","PawnDiary_BrainwipeArrival"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `arrival` — Arrival

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Arrival** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_Arrival`, `PawnDiary_BrainwipeArrival`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `arrival`

<!-- repowiki:group {"defName":"eventWindowVoidMonolith","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["VoidMonolithDiscovery","VoidMonolithActivation","VoidMonolithWaking","VoidMonolithVoidAwakened"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `eventWindowVoidMonolith` — Void monolith

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Void monolith** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `VoidMonolithDiscovery`, `VoidMonolithActivation`, `VoidMonolithWaking`, `VoidMonolithVoidAwakened`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `eventWindowVoidMonolith`

<!-- repowiki:group {"defName":"eventWindowHeartAttack","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["HeartAttack"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `eventWindowHeartAttack` — Heart attack

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Heart attack** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `HeartAttack`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `eventWindowHeartAttack`

<!-- repowiki:group {"defName":"eventWindowBirthday","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Birthday"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `eventWindowBirthday` — Birthday

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Birthday** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Birthday`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `eventWindowBirthday`

<!-- repowiki:group {"defName":"eventWindowAncientDanger","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["AncientDanger"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `eventWindowAncientDanger` — Ancient danger

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Ancient danger** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `AncientDanger`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `eventWindowAncientDanger`

<!-- repowiki:group {"defName":"eventWindowPrisonBreak","domain":"Interaction","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PrisonBreak"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `eventWindowPrisonBreak` — Prison break

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / yes |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Prison break** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PrisonBreak`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `eventWindowPrisonBreak`

<!-- repowiki:group {"defName":"eventWindowMechCluster","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["MechClusterLanded"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `eventWindowMechCluster` — Mech cluster

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Mech cluster** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `MechClusterLanded`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `eventWindowMechCluster`

<!-- repowiki:group {"defName":"observedPitGate","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PitGatePresence"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `observedPitGate` — Pit gate

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Pit gate** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PitGatePresence`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `observedPitGate`

<!-- repowiki:group {"defName":"observedFleshmassHeart","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["FleshmassHeartPresence"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `observedFleshmassHeart` — Fleshmass heart

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Fleshmass heart** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `FleshmassHeartPresence`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `observedFleshmassHeart`

<!-- repowiki:group {"defName":"artImmortalized","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_ArtImmortalized"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `artImmortalized` — Immortalized in art

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Immortalized in art** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_ArtImmortalized`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `artImmortalized`

<!-- repowiki:group {"defName":"observedBiotechPollution","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BiotechPollutionMeaningful","BiotechPollutionSevere","BiotechPollutionCritical"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `observedBiotechPollution` — Colony pollution

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Colony pollution** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BiotechPollutionMeaningful`, `BiotechPollutionSevere`, `BiotechPollutionCritical`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `observedBiotechPollution`

<!-- repowiki:group {"defName":"other","domain":"Interaction","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `other` — A quiet day

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches the catch-all policy. |
| Expected evidence | The page or later batch/reflection uses the **A quiet day** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `other`

<!-- repowiki:group {"defName":"dayreflection","domain":"Reflection","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["DayReflection"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `dayreflection` — Day's reflection

| Policy field | Expected value |
|---|---|
| Domain | `Reflection` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | scheduled reflection page |
| Setup/trigger cue | Reach the configured day, quadrum, belief, or arc reflection boundary. |
| Expected evidence | The page or later batch/reflection uses the **Day's reflection** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `DayReflection`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `dayreflection`

<!-- repowiki:group {"defName":"quadrumreflection","domain":"Reflection","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["QuadrumReflection"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `quadrumreflection` — Quadrum reflection

| Policy field | Expected value |
|---|---|
| Domain | `Reflection` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | scheduled reflection page |
| Setup/trigger cue | Reach the configured day, quadrum, belief, or arc reflection boundary. |
| Expected evidence | The page or later batch/reflection uses the **Quadrum reflection** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `QuadrumReflection`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `quadrumreflection`

<!-- repowiki:group {"defName":"reflectionBelief","domain":"Reflection","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnBeliefReflection"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `reflectionBelief` — Belief reflection

| Policy field | Expected value |
|---|---|
| Domain | `Reflection` |
| Prerequisite | Ideology (`Ludeon.RimWorld.Ideology`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | scheduled reflection page |
| Setup/trigger cue | Reach the configured day, quadrum, belief, or arc reflection boundary. |
| Expected evidence | The page or later batch/reflection uses the **Belief reflection** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnBeliefReflection`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `reflectionBelief`

<!-- repowiki:group {"defName":"socialReflection","domain":"Reflection","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_SocialReflection"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `socialReflection` — Social reflection

| Policy field | Expected value |
|---|---|
| Domain | `Reflection` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | scheduled reflection page |
| Setup/trigger cue | Complete an interaction in a group marked `socialReflectionEligible`, pass its deterministic chance and cooldown checks, and wait for the delayed follow-up. |
| Expected evidence | The initiator-only page uses the **Social reflection** group label and the dedicated social-reflection prompt boundary. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_SocialReflection`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `socialReflection`

<!-- repowiki:group {"defName":"reflection","domain":"Reflection","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnArcReflection"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `reflection` — Reflection

| Policy field | Expected value |
|---|---|
| Domain | `Reflection` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | scheduled reflection page |
| Setup/trigger cue | Reach the configured day, quadrum, belief, or arc reflection boundary. |
| Expected evidence | The page or later batch/reflection uses the **Reflection** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnArcReflection`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `reflection`

<!-- repowiki:group {"defName":"beliefCrisis","domain":"MentalState","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["IdeoChange"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `beliefCrisis` — Crisis of belief

| Policy field | Expected value |
|---|---|
| Domain | `MentalState` |
| Prerequisite | Ideology (`Ludeon.RimWorld.Ideology`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Expected evidence | The page or later batch/reflection uses the **Crisis of belief** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `IdeoChange`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `beliefCrisis`

<!-- repowiki:group {"defName":"socialfight","domain":"MentalState","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["SocialFighting"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `socialfight` — Social fights

| Policy field | Expected value |
|---|---|
| Domain | `MentalState` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Expected evidence | The page or later batch/reflection uses the **Social fights** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `SocialFighting`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `socialfight`

<!-- repowiki:group {"defName":"insultspree","domain":"MentalState","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["InsultingSpree"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `insultspree` — Insult sprees

| Policy field | Expected value |
|---|---|
| Domain | `MentalState` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Expected evidence | The page or later batch/reflection uses the **Insult sprees** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `InsultingSpree`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `insultspree`

<!-- repowiki:group {"defName":"mentalbreakViolent","domain":"MentalState","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Berserk","MurderousRage","Slaughterer","Tantrum","TargetedTantrum","BerserkMechanoid","BerserkTrance"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":["Berserk","Rage","Tantrum","Slaughterer"],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `mentalbreakViolent` — Violent breaks

| Policy field | Expected value |
|---|---|
| Domain | `MentalState` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / yes |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Expected evidence | The page or later batch/reflection uses the **Violent breaks** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Berserk`, `MurderousRage`, `Slaughterer`, `Tantrum`, `TargetedTantrum`, `BerserkMechanoid`, `BerserkTrance`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: `Berserk`, `Rage`, `Tantrum`, `Slaughterer`
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `mentalbreakViolent`

<!-- repowiki:group {"defName":"mentalbreakEscape","domain":"MentalState","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Wander_Sad","Wander_OwnRoom","Wander_Psychotic","GiveUpExit","RunWild","PanicFlee"],"matchOrdinalDefNames":[],"matchPrefixes":["Wander"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `mentalbreakEscape` — Withdrawal breaks

| Policy field | Expected value |
|---|---|
| Domain | `MentalState` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Expected evidence | The page or later batch/reflection uses the **Withdrawal breaks** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Wander_Sad`, `Wander_OwnRoom`, `Wander_Psychotic`, `GiveUpExit`, `RunWild`, `PanicFlee`
- Ordinal exact names: —
- Prefixes: `Wander`
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `mentalbreakEscape`

<!-- repowiki:group {"defName":"mentalbreakIndulgent","domain":"MentalState","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Binging_Food","Binging_DrugExtreme","Binging_DrugMajor","FireStartingSpree","CorpseObsession","Jailbreaker"],"matchOrdinalDefNames":[],"matchPrefixes":["Binging"],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `mentalbreakIndulgent` — Compulsive breaks

| Policy field | Expected value |
|---|---|
| Domain | `MentalState` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Expected evidence | The page or later batch/reflection uses the **Compulsive breaks** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Binging_Food`, `Binging_DrugExtreme`, `Binging_DrugMajor`, `FireStartingSpree`, `CorpseObsession`, `Jailbreaker`
- Ordinal exact names: —
- Prefixes: `Binging`
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `mentalbreakIndulgent`

<!-- repowiki:group {"defName":"mentalbreak","domain":"MentalState","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `mentalbreak` — Mental breaks

| Policy field | Expected value |
|---|---|
| Domain | `MentalState` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start a matching mental state (Dev Mode mental-state tools are the most reproducible route). |
| Expected evidence | The page or later batch/reflection uses the **Mental breaks** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `mentalbreak`

<!-- repowiki:group {"defName":"personaWeaponMilestone","domain":"Tale","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PersonaWeaponFirstConsequentialKill"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `personaWeaponMilestone` — Persona weapon milestones

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / yes |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Persona weapon milestones** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PersonaWeaponFirstConsequentialKill`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `personaWeaponMilestone`

<!-- repowiki:group {"defName":"talecombat","domain":"Tale","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":true,"batchMode":"PairEvent","batchScope":"Pair","batchSyntheticDefName":"TaleCombatBatch","batchWindowTicks":7500,"batchMaxEvents":10,"catchAll":false,"matchDefNames":["Downed","Wounded","KilledBy","KilledCapacity","KilledLongRange","KilledMajorThreat","KilledMelee","KilledMortar","KilledChild","KilledColonist","KilledColonyAnimal","PawnDiary_DeathFallback","DefeatedHostileFactionLeader","CollapseDodged","WasOnFire"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `talecombat` — Combat, injuries & death

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / yes |
| Page/batch/reflection behavior | batched solo page after the Tale batch flushes |
| Batch contract | mode `PairEvent`; scope `Pair`; window 7500 ticks; flush cap 10; synthetic ID `TaleCombatBatch` |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Combat, injuries & death** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Downed`, `Wounded`, `KilledBy`, `KilledCapacity`, `KilledLongRange`, `KilledMajorThreat`, `KilledMelee`, `KilledMortar`, `KilledChild`, `KilledColonist`, `KilledColonyAnimal`, `PawnDiary_DeathFallback`, `DefeatedHostileFactionLeader`, `CollapseDodged`, `WasOnFire`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Death-Tale victim slots: initiator `KilledBy`; recipient `KilledCapacity`, `KilledLongRange`, `KilledMajorThreat`, `KilledMelee`, `KilledMortar`, `KilledChild`, `KilledColonist`, `KilledColonyAnimal`.

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `talecombat`

<!-- repowiki:group {"defName":"talehealth","domain":"Tale","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["DidSurgery","HealedMe","IllnessRevealed","HeatstrokeRevealed","HypothermiaRevealed","ToxicityRevealed","VacuumExposureRevealed","Exhausted"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `talehealth` — Health & medicine

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Health & medicine** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `DidSurgery`, `HealedMe`, `IllnessRevealed`, `HeatstrokeRevealed`, `HypothermiaRevealed`, `ToxicityRevealed`, `VacuumExposureRevealed`, `Exhausted`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `talehealth`

<!-- repowiki:group {"defName":"biotechFamilyBirth","domain":"Tale","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BiotechFamilyBirth"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `biotechFamilyBirth` — Family birth

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Family birth** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BiotechFamilyBirth`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `biotechFamilyBirth`

<!-- repowiki:group {"defName":"talelife","domain":"Tale","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["GaveBirth","Captured","Recruited","KidnappedColonist","ExecutedPrisoner","SoldPrisoner","EnteredCryptosleep","PutIntoCryptosleep","LandedInPod","BecameLover","Breakup","Marriage","BondedWithAnimal","TamedAnimal"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `talelife` — Life milestones

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Life milestones** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `GaveBirth`, `Captured`, `Recruited`, `KidnappedColonist`, `ExecutedPrisoner`, `SoldPrisoner`, `EnteredCryptosleep`, `PutIntoCryptosleep`, `LandedInPod`, `BecameLover`, `Breakup`, `Marriage`, `BondedWithAnimal`, `TamedAnimal`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `talelife`

<!-- repowiki:group {"defName":"talequality","domain":"Tale","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["CraftedArt"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `talequality` — Masterworks & legendary crafts

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Masterworks & legendary crafts** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `CraftedArt`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `talequality`

<!-- repowiki:group {"defName":"talework","domain":"Tale","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["FinishedResearchProject","CompletedLongConstructionProject","CompletedLongCraftingProject","MinedValuable","GainedMasterSkillWithoutPassion","GainedMasterSkillWithPassion"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `talework` — Work & achievements

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Work & achievements** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `FinishedResearchProject`, `CompletedLongConstructionProject`, `CompletedLongCraftingProject`, `MinedValuable`, `GainedMasterSkillWithoutPassion`, `GainedMasterSkillWithPassion`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `talework`

<!-- repowiki:group {"defName":"taleanomaly","domain":"Tale","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["StudiedEntity","MutatedMyArm","PerformedPsychicRitual","ClosedTheVoid","EmbracedTheVoid","DeathPall","UnnaturalDarkness","NoxiousHaze"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `taleanomaly` — Anomaly horror

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Anomaly horror** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `StudiedEntity`, `MutatedMyArm`, `PerformedPsychicRitual`, `ClosedTheVoid`, `EmbracedTheVoid`, `DeathPall`, `UnnaturalDarkness`, `NoxiousHaze`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `taleanomaly`

<!-- repowiki:group {"defName":"taleincident","domain":"Tale","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Raid","Infestation","ManhunterPack","MajorThreat","MeteoriteImpact","ShipPartCrash","Flashstorm","Tornado","TornadoFromItem","ToxicFallout","VolcanicWinter","Eclipse","Aurora","NoxiousHaze","DeathPall","UnnaturalDarkness","OrbitalDebris","StudiedEntity","ClosedTheVoid","EmbracedTheVoid","CaravanAmbushDefeated","CaravanAmbushedByHumanlike","CaravanAmbushedByManhunter","CaravanAssaultSuccessful","CaravanDemand","CaravanFled","CaravanFormed","CaravanMeeting","EndGame_ShipEscape","LaunchedShip"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `taleincident` — Raids, disasters & colony events

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Raids, disasters & colony events** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Raid`, `Infestation`, `ManhunterPack`, `MajorThreat`, `MeteoriteImpact`, `ShipPartCrash`, `Flashstorm`, `Tornado`, `TornadoFromItem`, `ToxicFallout`, `VolcanicWinter`, `Eclipse`, `Aurora`, `NoxiousHaze`, `DeathPall`, `UnnaturalDarkness`, `OrbitalDebris`, `StudiedEntity`, `ClosedTheVoid`, `EmbracedTheVoid`, `CaravanAmbushDefeated`, `CaravanAmbushedByHumanlike`, `CaravanAmbushedByManhunter`, `CaravanAssaultSuccessful`, `CaravanDemand`, `CaravanFled`, `CaravanFormed`, `CaravanMeeting`, `EndGame_ShipEscape`, `LaunchedShip`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `taleincident`

<!-- repowiki:group {"defName":"talequiet","domain":"Tale","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["AteRawHumanlikeMeat","AttendedConcert","AttendedParty","BuiltSnowman","Drunk","HeldConcert","Meditated","PerformedPsychicRitual","PlayedGame","Prayed","ReadBook","Vomited","WalkedNaked","VisitedGrave"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `talequiet` — Quiet personal moments

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **Quiet personal moments** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `AteRawHumanlikeMeat`, `AttendedConcert`, `AttendedParty`, `BuiltSnowman`, `Drunk`, `HeldConcert`, `Meditated`, `PerformedPsychicRitual`, `PlayedGame`, `Prayed`, `ReadBook`, `Vomited`, `WalkedNaked`, `VisitedGrave`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `talequiet`

<!-- repowiki:group {"defName":"taleother","domain":"Tale","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `taleother` — A notable day

| Policy field | Expected value |
|---|---|
| Domain | `Tale` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Cause RimWorld to record a matching TaleDef; use the ordinary gameplay action named by the group. |
| Expected evidence | The page or later batch/reflection uses the **A notable day** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `taleother`

<!-- repowiki:group {"defName":"moodeventPositive","domain":"MoodEvent","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Aurora","PsychicSoothe","BioluminescentSpores"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `moodeventPositive` — Positive mood events

| Policy field | Expected value |
|---|---|
| Domain | `MoodEvent` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start or register a matching game condition, then allow the event signal to run. |
| Expected evidence | The page or later batch/reflection uses the **Positive mood events** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Aurora`, `PsychicSoothe`, `BioluminescentSpores`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `moodeventPositive`

<!-- repowiki:group {"defName":"moodeventWeatherHardship","domain":"MoodEvent","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["ColdSnap","HeatWave","VolcanicWinter","VolcanicAsh","UnnaturalHeat","Drought","DarkenedSkies","DeepFreeze"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `moodeventWeatherHardship` — Climate hardship

| Policy field | Expected value |
|---|---|
| Domain | `MoodEvent` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start or register a matching game condition, then allow the event signal to run. |
| Expected evidence | The page or later batch/reflection uses the **Climate hardship** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `ColdSnap`, `HeatWave`, `VolcanicWinter`, `VolcanicAsh`, `UnnaturalHeat`, `Drought`, `DarkenedSkies`, `DeepFreeze`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `moodeventWeatherHardship`

<!-- repowiki:group {"defName":"moodeventStormDanger","domain":"MoodEvent","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Flashstorm","BloodRain","VolcanicDebris","LavaFlow"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `moodeventStormDanger` — Sky violence

| Policy field | Expected value |
|---|---|
| Domain | `MoodEvent` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start or register a matching game condition, then allow the event signal to run. |
| Expected evidence | The page or later batch/reflection uses the **Sky violence** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Flashstorm`, `BloodRain`, `VolcanicDebris`, `LavaFlow`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `moodeventStormDanger`

<!-- repowiki:group {"defName":"moodeventNegative","domain":"MoodEvent","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Eclipse","PsychicDrone","GrayPall","DeathPall","ToxicFallout","SolarFlare","NoxiousHaze","HateChantDrone","GillRot"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `moodeventNegative` — Negative mood events

| Policy field | Expected value |
|---|---|
| Domain | `MoodEvent` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start or register a matching game condition, then allow the event signal to run. |
| Expected evidence | The page or later batch/reflection uses the **Negative mood events** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Eclipse`, `PsychicDrone`, `GrayPall`, `DeathPall`, `ToxicFallout`, `SolarFlare`, `NoxiousHaze`, `HateChantDrone`, `GillRot`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `moodeventNegative`

<!-- repowiki:group {"defName":"moodeventMixed","domain":"MoodEvent","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsychicSuppression","UnnaturalDarkness"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `moodeventMixed` — Situationally mixed mood events

| Policy field | Expected value |
|---|---|
| Domain | `MoodEvent` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start or register a matching game condition, then allow the event signal to run. |
| Expected evidence | The page or later batch/reflection uses the **Situationally mixed mood events** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsychicSuppression`, `UnnaturalDarkness`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `moodeventMixed`

<!-- repowiki:group {"defName":"moodeventOther","domain":"MoodEvent","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `moodeventOther` — Passing moods

| Policy field | Expected value |
|---|---|
| Domain | `MoodEvent` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Start or register a matching game condition, then allow the event signal to run. |
| Expected evidence | The page or later batch/reflection uses the **Passing moods** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `moodeventOther`

<!-- repowiki:group {"defName":"thoughtPregnancyFamily","domain":"Thought","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PregnancyTerminated","PregnancyEnded","Stillbirth","Miscarried","PartnerMiscarried"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `thoughtPregnancyFamily` — Pregnancy memories

| Policy field | Expected value |
|---|---|
| Domain | `Thought` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Expected evidence | The page or later batch/reflection uses the **Pregnancy memories** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PregnancyTerminated`, `PregnancyEnded`, `Stillbirth`, `Miscarried`, `PartnerMiscarried`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `thoughtPregnancyFamily`

<!-- repowiki:group {"defName":"thoughtPositive","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["GotMarried","AttendedWedding","AttendedParty","AttendedConcert","HeldConcert","EncouragingSpeech","InspirationalSpeech","NewColonyOptimism","NewColonyHope","Catharsis","Nuzzled","KnowBuriedInSarcophagus","DefeatedHostileFactionLeader","DefeatedHostileFactionLeaderOpinion","DefeatedMechCluster","DefeatedInsectHive","RescuedRelative","Rescued","TravelAnticipation","AteInImpressiveDiningRoom","JoyActivityInImpressiveRecRoom","SleptInBedroom","AteLavishMeal","AteFineMeal","AteHumanlikeMeatDirectCannibal","AteHumanlikeMeatAsIngredientCannibal","DeepTalk","KindWords","KindWordsMood","RapportBuilt","HadCatharticFight","RescuedMe","RescuedMeByOfferingHelp","RecruitedMe","HoneymoonPhase","GotSomeLovin","KilledMyRival","ArtifactMoodBoost","PawnWithBadOpinionDied","PawnWithBadOpinionLost","HonorableBestowingCeremony","GrandioseBestowingCeremony","ReignedInThroneroom","DecreeMet","RelicsCollected","BiosculpterPleasure","Counselled","Counselled_MoodBoost","TendedByMedicalSpecialist","ParticipatedInRaid_Respected","RecentConquest_Respected","TrialExonerated","FunParty","UnforgettableParty","GoodFuneral","HeartwarmingFuneral","FunFestival","UnforgettableFestival","SatisfyingSacrifice","SpectacularSacrifice","BeautifulSkyLanterns","UnforgettableSkyLanterns","SatisfyingScarification","SpectacularScarification","SatisfyingBlinding","SpectacularBlinding","EffectiveConversion","MasterfulConversion","GoodDuel","UnforgettableDuel","SatisfyingExecution","SpectacularExecution","BabyBorn","GigglingBaby","MyGigglingBaby","WasTaught","GaveLesson","DarknessLifted","HealedMe","ClosedTheVoidOpinion"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":["Impressive","Spectacular","Unforgettable","Heartwarming","Satisfying","Beautiful","Masterful","Grandiose","Honorable","Encouraging","Inspirational","Pleasure","Catharsis","Cathartic","Optimism","Hope","Rapport"],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `thoughtPositive` — Positive thoughts

| Policy field | Expected value |
|---|---|
| Domain | `Thought` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Expected evidence | The page or later batch/reflection uses the **Positive thoughts** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `GotMarried`, `AttendedWedding`, `AttendedParty`, `AttendedConcert`, `HeldConcert`, `EncouragingSpeech`, `InspirationalSpeech`, `NewColonyOptimism`, `NewColonyHope`, `Catharsis`, `Nuzzled`, `KnowBuriedInSarcophagus`, `DefeatedHostileFactionLeader`, `DefeatedHostileFactionLeaderOpinion`, `DefeatedMechCluster`, `DefeatedInsectHive`, `RescuedRelative`, `Rescued`, `TravelAnticipation`, `AteInImpressiveDiningRoom`, `JoyActivityInImpressiveRecRoom`, `SleptInBedroom`, `AteLavishMeal`, `AteFineMeal`, `AteHumanlikeMeatDirectCannibal`, `AteHumanlikeMeatAsIngredientCannibal`, `DeepTalk`, `KindWords`, `KindWordsMood`, `RapportBuilt`, `HadCatharticFight`, `RescuedMe`, `RescuedMeByOfferingHelp`, `RecruitedMe`, `HoneymoonPhase`, `GotSomeLovin`, `KilledMyRival`, `ArtifactMoodBoost`, `PawnWithBadOpinionDied`, `PawnWithBadOpinionLost`, `HonorableBestowingCeremony`, `GrandioseBestowingCeremony`, `ReignedInThroneroom`, `DecreeMet`, `RelicsCollected`, `BiosculpterPleasure`, `Counselled`, `Counselled_MoodBoost`, `TendedByMedicalSpecialist`, `ParticipatedInRaid_Respected`, `RecentConquest_Respected`, `TrialExonerated`, `FunParty`, `UnforgettableParty`, `GoodFuneral`, `HeartwarmingFuneral`, `FunFestival`, `UnforgettableFestival`, `SatisfyingSacrifice`, `SpectacularSacrifice`, `BeautifulSkyLanterns`, `UnforgettableSkyLanterns`, `SatisfyingScarification`, `SpectacularScarification`, `SatisfyingBlinding`, `SpectacularBlinding`, `EffectiveConversion`, `MasterfulConversion`, `GoodDuel`, `UnforgettableDuel`, `SatisfyingExecution`, `SpectacularExecution`, `BabyBorn`, `GigglingBaby`, `MyGigglingBaby`, `WasTaught`, `GaveLesson`, `DarknessLifted`, `HealedMe`, `ClosedTheVoidOpinion`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: `Impressive`, `Spectacular`, `Unforgettable`, `Heartwarming`, `Satisfying`, `Beautiful`, `Masterful`, `Grandiose`, `Honorable`, `Encouraging`, `Inspirational`, `Pleasure`, `Catharsis`, `Cathartic`, `Optimism`, `Hope`, `Rapport`
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `thoughtPositive`

<!-- repowiki:group {"defName":"thoughtNegative","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["NeedFood","NeedRest","NeedOutdoors","DrugDesireInterest","DrugDesireFascination","KnowGuestExecuted","KnowColonistExecuted","KnowPrisonerDiedInnocent","KnowColonistDied","BondedAnimalDied","MySonDied","MyDaughterDied","MyHusbandDied","MyWifeDied","MyFianceDied","MyFianceeDied","MyLoverDied","MyBrotherDied","MySisterDied","MyGrandchildDied","MyFatherDied","MyMotherDied","MyNieceDied","MyNephewDied","MyHalfSiblingDied","MyAuntDied","MyUncleDied","MyGrandparentDied","MyCousinDied","MyKinDied","ColonistLost","BondedAnimalReleased","BondedAnimalLost","MySonLost","MyDaughterLost","MyHusbandLost","MyWifeLost","MyFianceLost","MyFianceeLost","MyLoverLost","MyBrotherLost","MySisterLost","MyGrandchildLost","MyFatherLost","MyMotherLost","MyNieceLost","MyNephewLost","MyHalfSiblingLost","MyAuntLost","MyUncleLost","MyGrandparentLost","MyCousinLost","MyKinLost","AteWithoutTable","SleepDisturbed","SleptOutside","SleptOnGround","SleptInCold","SleptInHeat","SleptInBarracks","SoakingWet","OnDuty","KnowPrisonerSold","KnowGuestOrganHarvested","KnowColonistOrganHarvested","MyOrganHarvested","WasImprisoned","KnowButcheredHumanlikeCorpse","ObservedLayingCorpse","ObservedLayingRottingCorpse","WitnessedDeathAlly","WitnessedDeathNonAlly","WitnessedDeathFamily","DeniedJoining","ColonistBanished","ColonistBanishedToDie","PrisonerBanishedToDie","BondedAnimalBanished","FailedToRescueRelative","AteRawFood","AteKibble","AteCorpse","AteHumanlikeMeatDirect","AteHumanlikeMeatAsIngredient","AteInsectMeatDirect","AteInsectMeatAsIngredient","AteRottenFood","Slighted","Insulted","InsultedMood","HadAngeringFight","HarmedMe","BotchedMySurgery","CrashedTogether","SoldMyLovedOne","SoldMyBondedAnimal","SoldMyBondedAnimalMood","ForcedMeToTakeDrugs","ForcedMeToTakeDrugsMood","ForcedMeToTakeLuciferium","ForcedMeToTakeLuciferiumMood","RebuffedMyRomanceAttempt","RebuffedMyRomanceAttemptMood","FailedRomanceAttemptOnMe","BrokeUpWithMe","BrokeUpWithMeMood","CheatedOnMe","CheatedOnMeMood","DivorcedMe","DivorcedMeMood","RejectedMyProposal","RejectedMyProposalMood","IRejectedTheirProposal","KilledMyFriend","KilledMyLover","KilledMyFiance","KilledMySpouse","KilledMyFather","KilledMyMother","KilledMySon","KilledMyDaughter","KilledMyBrother","KilledMySister","KilledMyKin","KilledMyBondedAnimal","TerribleSpeech","UninspiringSpeech","TerribleBestowingCeremony","UnimpressiveBestowingCeremony","Disinherited","DecreeFailed","NeuroquakeEcho","AteFoodInappropriateForTitle","TerribleParty","BoringParty","TerribleFuneral","LacklusterFuneral","TerribleFestival","BoringFestival","TerribleSacrifice","BoringSacrifice","TerribleSkyLanterns","UnimpressiveSkyLanterns","TerribleScarification","BoringScarification","TerribleBlinding","BoringBlinding","TerribleDuel","BoringDuel","AwkwardExecution","TrialFailed","TrialConvicted","ConnectedTreeDied","DryadDied","WillDiminished","FailedConvertAbilityInitiator","FailedConvertAbilityRecipient","CounselFailed","ObservedTerror","ObservedGibbetCage","ObservedSkullspike","WasEnslaved","SleptInRoomWithSlave","LookChangeDesired","CryingBaby","MyCryingBaby","FedOn","FedOn_Social","PsychicBondTorn","XenogermHarvested_Prisoner","AteTwistedMeat","PsychicRitualVictim","MutatedMyArm","PsychicRitualGuilt","DrainedMySkills","UsedMeForPsychicRitual","HeardInhumanRambling"],"matchOrdinalDefNames":[],"matchPrefixes":["Banished","Imprisoned"],"matchSuffixes":["Died"],"matchSegments":["Terrible","Boring","Lackluster","Unimpressive","Awkward","Missing","Disrespected","Sickness","Exhaustion","Disturbed","Rotting","Harvested","Cheated","Divorced","Rejected","Slighted","Insulted"],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `thoughtNegative` — Negative thoughts

| Policy field | Expected value |
|---|---|
| Domain | `Thought` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Expected evidence | The page or later batch/reflection uses the **Negative thoughts** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `NeedFood`, `NeedRest`, `NeedOutdoors`, `DrugDesireInterest`, `DrugDesireFascination`, `KnowGuestExecuted`, `KnowColonistExecuted`, `KnowPrisonerDiedInnocent`, `KnowColonistDied`, `BondedAnimalDied`, `MySonDied`, `MyDaughterDied`, `MyHusbandDied`, `MyWifeDied`, `MyFianceDied`, `MyFianceeDied`, `MyLoverDied`, `MyBrotherDied`, `MySisterDied`, `MyGrandchildDied`, `MyFatherDied`, `MyMotherDied`, `MyNieceDied`, `MyNephewDied`, `MyHalfSiblingDied`, `MyAuntDied`, `MyUncleDied`, `MyGrandparentDied`, `MyCousinDied`, `MyKinDied`, `ColonistLost`, `BondedAnimalReleased`, `BondedAnimalLost`, `MySonLost`, `MyDaughterLost`, `MyHusbandLost`, `MyWifeLost`, `MyFianceLost`, `MyFianceeLost`, `MyLoverLost`, `MyBrotherLost`, `MySisterLost`, `MyGrandchildLost`, `MyFatherLost`, `MyMotherLost`, `MyNieceLost`, `MyNephewLost`, `MyHalfSiblingLost`, `MyAuntLost`, `MyUncleLost`, `MyGrandparentLost`, `MyCousinLost`, `MyKinLost`, `AteWithoutTable`, `SleepDisturbed`, `SleptOutside`, `SleptOnGround`, `SleptInCold`, `SleptInHeat`, `SleptInBarracks`, `SoakingWet`, `OnDuty`, `KnowPrisonerSold`, `KnowGuestOrganHarvested`, `KnowColonistOrganHarvested`, `MyOrganHarvested`, `WasImprisoned`, `KnowButcheredHumanlikeCorpse`, `ObservedLayingCorpse`, `ObservedLayingRottingCorpse`, `WitnessedDeathAlly`, `WitnessedDeathNonAlly`, `WitnessedDeathFamily`, `DeniedJoining`, `ColonistBanished`, `ColonistBanishedToDie`, `PrisonerBanishedToDie`, `BondedAnimalBanished`, `FailedToRescueRelative`, `AteRawFood`, `AteKibble`, `AteCorpse`, `AteHumanlikeMeatDirect`, `AteHumanlikeMeatAsIngredient`, `AteInsectMeatDirect`, `AteInsectMeatAsIngredient`, `AteRottenFood`, `Slighted`, `Insulted`, `InsultedMood`, `HadAngeringFight`, `HarmedMe`, `BotchedMySurgery`, `CrashedTogether`, `SoldMyLovedOne`, `SoldMyBondedAnimal`, `SoldMyBondedAnimalMood`, `ForcedMeToTakeDrugs`, `ForcedMeToTakeDrugsMood`, `ForcedMeToTakeLuciferium`, `ForcedMeToTakeLuciferiumMood`, `RebuffedMyRomanceAttempt`, `RebuffedMyRomanceAttemptMood`, `FailedRomanceAttemptOnMe`, `BrokeUpWithMe`, `BrokeUpWithMeMood`, `CheatedOnMe`, `CheatedOnMeMood`, `DivorcedMe`, `DivorcedMeMood`, `RejectedMyProposal`, `RejectedMyProposalMood`, `IRejectedTheirProposal`, `KilledMyFriend`, `KilledMyLover`, `KilledMyFiance`, `KilledMySpouse`, `KilledMyFather`, `KilledMyMother`, `KilledMySon`, `KilledMyDaughter`, `KilledMyBrother`, `KilledMySister`, `KilledMyKin`, `KilledMyBondedAnimal`, `TerribleSpeech`, `UninspiringSpeech`, `TerribleBestowingCeremony`, `UnimpressiveBestowingCeremony`, `Disinherited`, `DecreeFailed`, `NeuroquakeEcho`, `AteFoodInappropriateForTitle`, `TerribleParty`, `BoringParty`, `TerribleFuneral`, `LacklusterFuneral`, `TerribleFestival`, `BoringFestival`, `TerribleSacrifice`, `BoringSacrifice`, `TerribleSkyLanterns`, `UnimpressiveSkyLanterns`, `TerribleScarification`, `BoringScarification`, `TerribleBlinding`, `BoringBlinding`, `TerribleDuel`, `BoringDuel`, `AwkwardExecution`, `TrialFailed`, `TrialConvicted`, `ConnectedTreeDied`, `DryadDied`, `WillDiminished`, `FailedConvertAbilityInitiator`, `FailedConvertAbilityRecipient`, `CounselFailed`, `ObservedTerror`, `ObservedGibbetCage`, `ObservedSkullspike`, `WasEnslaved`, `SleptInRoomWithSlave`, `LookChangeDesired`, `CryingBaby`, `MyCryingBaby`, `FedOn`, `FedOn_Social`, `PsychicBondTorn`, `XenogermHarvested_Prisoner`, `AteTwistedMeat`, `PsychicRitualVictim`, `MutatedMyArm`, `PsychicRitualGuilt`, `DrainedMySkills`, `UsedMeForPsychicRitual`, `HeardInhumanRambling`
- Ordinal exact names: —
- Prefixes: `Banished`, `Imprisoned`
- Suffixes: `Died`
- Whole segments: `Terrible`, `Boring`, `Lackluster`, `Unimpressive`, `Awkward`, `Missing`, `Disrespected`, `Sickness`, `Exhaustion`, `Disturbed`, `Rotting`, `Harvested`, `Cheated`, `Divorced`, `Rejected`, `Slighted`, `Insulted`
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `thoughtNegative`

<!-- repowiki:group {"defName":"thoughtOther","domain":"Thought","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `thoughtOther` — Passing thoughts

| Policy field | Expected value |
|---|---|
| Domain | `Thought` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Give an eligible colonist a matching thought/memory and allow the thought hook or scanner to observe it. |
| Expected evidence | The page or later batch/reflection uses the **Passing thoughts** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `thoughtOther`

<!-- repowiki:group {"defName":"inspiration","domain":"Inspiration","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Frenzy_Work","Frenzy_Go","Frenzy_Shoot","Inspired_Trade","Inspired_Recruitment","Inspired_Taming","Inspired_Surgery","Inspired_Creativity"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `inspiration` — Inspirations

| Policy field | Expected value |
|---|---|
| Domain | `Inspiration` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Give an eligible colonist a matching inspiration. |
| Expected evidence | The page or later batch/reflection uses the **Inspirations** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Frenzy_Work`, `Frenzy_Go`, `Frenzy_Shoot`, `Inspired_Trade`, `Inspired_Recruitment`, `Inspired_Taming`, `Inspired_Surgery`, `Inspired_Creativity`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `inspiration`

<!-- repowiki:group {"defName":"workDarkStudy","domain":"Work","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_WorkDarkStudy"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `workDarkStudy` — Dark study work

| Policy field | Expected value |
|---|---|
| Domain | `Work` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Keep an eligible colonist doing matching work until the scheduled work scan observes it. |
| Expected evidence | The page or later batch/reflection uses the **Dark study work** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_WorkDarkStudy`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `workDarkStudy`

<!-- repowiki:group {"defName":"workPassion","domain":"Work","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_WorkPassion"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `workPassion` — Passionate work

| Policy field | Expected value |
|---|---|
| Domain | `Work` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Keep an eligible colonist doing matching work until the scheduled work scan observes it. |
| Expected evidence | The page or later batch/reflection uses the **Passionate work** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_WorkPassion`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `workPassion`

<!-- repowiki:group {"defName":"workStrain","domain":"Work","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_WorkStrain"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `workStrain` — Straining work

| Policy field | Expected value |
|---|---|
| Domain | `Work` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Keep an eligible colonist doing matching work until the scheduled work scan observes it. |
| Expected evidence | The page or later batch/reflection uses the **Straining work** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_WorkStrain`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `workStrain`

<!-- repowiki:group {"defName":"workRoutine","domain":"Work","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_WorkRoutine"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `workRoutine` — Routine work

| Policy field | Expected value |
|---|---|
| Domain | `Work` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Keep an eligible colonist doing matching work until the scheduled work scan observes it. |
| Expected evidence | The page or later batch/reflection uses the **Routine work** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_WorkRoutine`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `workRoutine`

<!-- repowiki:group {"defName":"hediffPartGainedAnomalous","domain":"Hediff","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["AdrenalHeart_addedpart","CorrosiveHeart_addedpart","MetalbloodHeart_addedpart","RevenantVertebrae_addedpart"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":["organicpart"],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `hediffPartGainedAnomalous` — Anomalous body changes

| Policy field | Expected value |
|---|---|
| Domain | `Hediff` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo page from a health observation |
| Health policy | `Immediate`; add=yes; progression=no |
| Setup/trigger cue | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Expected evidence | The page or later batch/reflection uses the **Anomalous body changes** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `AdrenalHeart_addedpart`, `CorrosiveHeart_addedpart`, `MetalbloodHeart_addedpart`, `RevenantVertebrae_addedpart`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: `organicpart`
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `hediffPartGainedAnomalous`

<!-- repowiki:group {"defName":"hediffPartGainedArtificial","domain":"Hediff","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":["addedpart"],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `hediffPartGainedArtificial` — Artificial body parts

| Policy field | Expected value |
|---|---|
| Domain | `Hediff` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo page from a health observation |
| Health policy | `Immediate`; add=yes; progression=no |
| Setup/trigger cue | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Expected evidence | The page or later batch/reflection uses the **Artificial body parts** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: `addedpart`
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `hediffPartGainedArtificial`

<!-- repowiki:group {"defName":"hediffPartLostNatural","domain":"Hediff","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":["missingpart"],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `hediffPartLostNatural` — Lost body parts

| Policy field | Expected value |
|---|---|
| Domain | `Hediff` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo page from a health observation |
| Health policy | `Immediate`; add=yes; progression=no |
| Setup/trigger cue | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Expected evidence | The page or later batch/reflection uses the **Lost body parts** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: `missingpart`
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `hediffPartLostNatural`

<!-- repowiki:group {"defName":"hediffPregnancy","domain":"Hediff","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PregnantHuman","Pregnant"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `hediffPregnancy` — Pregnancy

| Policy field | Expected value |
|---|---|
| Domain | `Hediff` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo page from a health observation |
| Health policy | `Immediate`; add=yes; progression=yes |
| Setup/trigger cue | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Expected evidence | The page or later batch/reflection uses the **Pregnancy** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PregnantHuman`, `Pregnant`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `hediffPregnancy`

<!-- repowiki:group {"defName":"hediffLabor","domain":"Hediff","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PregnancyLabor","PregnancyLaborPushing"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `hediffLabor` — Labor

| Policy field | Expected value |
|---|---|
| Domain | `Hediff` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo page from a health observation |
| Health policy | `Immediate`; add=yes; progression=no |
| Setup/trigger cue | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Expected evidence | The page or later batch/reflection uses the **Labor** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PregnancyLabor`, `PregnancyLaborPushing`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `hediffLabor`

<!-- repowiki:group {"defName":"hediffAnomalyCompulsion","domain":"Hediff","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RevenantHypnosis","CubeInterest","CubeWithdrawal","CubeRage","CorpseTorment","Inhumanized"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `hediffAnomalyCompulsion` — Anomaly compulsions

| Policy field | Expected value |
|---|---|
| Domain | `Hediff` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo page from a health observation |
| Health policy | `Immediate`; add=yes; progression=yes |
| Setup/trigger cue | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Expected evidence | The page or later batch/reflection uses the **Anomaly compulsions** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `RevenantHypnosis`, `CubeInterest`, `CubeWithdrawal`, `CubeRage`, `CorpseTorment`, `Inhumanized`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `hediffAnomalyCompulsion`

<!-- repowiki:group {"defName":"hediffMajorHealth","domain":"Hediff","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `hediffMajorHealth` — Major health changes

| Policy field | Expected value |
|---|---|
| Domain | `Hediff` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | reflection evidence; appears in a later day reflection |
| Health policy | `DayReflection`; add=yes; progression=yes |
| Setup/trigger cue | Apply or progress a matching health condition; the nested health policy decides immediate page versus reflection evidence. |
| Expected evidence | The page or later batch/reflection uses the **Major health changes** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `hediffMajorHealth`

<!-- repowiki:group {"defName":"raidFriendly","domain":"Raid","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RaidFriendly"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["RaidFriendly"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `raidFriendly` — Friendly arrivals & raids

| Policy field | Expected value |
|---|---|
| Domain | `Raid` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Trigger the matching raid/infestation incident against a player map. |
| Expected evidence | The page or later batch/reflection uses the **Friendly arrivals & raids** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `RaidFriendly`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `RaidFriendly`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `raidFriendly`

<!-- repowiki:group {"defName":"raidDropPod","domain":"Raid","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Drop"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `raidDropPod` — Drop-pod raids

| Policy field | Expected value |
|---|---|
| Domain | `Raid` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Trigger the matching raid/infestation incident against a player map. |
| Expected evidence | The page or later batch/reflection uses the **Drop-pod raids** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Drop`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `raidDropPod`

<!-- repowiki:group {"defName":"raidInfestation","domain":"Raid","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["Infestation"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Infestation"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `raidInfestation` — Infestations

| Policy field | Expected value |
|---|---|
| Domain | `Raid` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Trigger the matching raid/infestation incident against a player map. |
| Expected evidence | The page or later batch/reflection uses the **Infestations** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `Infestation`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Infestation`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `raidInfestation`

<!-- repowiki:group {"defName":"raidAnomalyEntities","domain":"Raid","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Sightstealer","Shambler","Fleshbeast","Gorehulk","Devourer","Chimera","Noctol","Metalhorror","Revenant","FleshmassHeart","PitGate"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `raidAnomalyEntities` — Entity attacks

| Policy field | Expected value |
|---|---|
| Domain | `Raid` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Trigger the matching raid/infestation incident against a player map. |
| Expected evidence | The page or later batch/reflection uses the **Entity attacks** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Sightstealer`, `Shambler`, `Fleshbeast`, `Gorehulk`, `Devourer`, `Chimera`, `Noctol`, `Metalhorror`, `Revenant`, `FleshmassHeart`, `PitGate`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `raidAnomalyEntities`

<!-- repowiki:group {"defName":"raid","domain":"Raid","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `raid` — Raids

| Policy field | Expected value |
|---|---|
| Domain | `Raid` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Trigger the matching raid/infestation incident against a player map. |
| Expected evidence | The page or later batch/reflection uses the **Raids** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `raid`

<!-- repowiki:group {"defName":"questRoyalAscent","domain":"Quest","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["EndGame_RoyalAscent"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `questRoyalAscent` — Royal Ascent

| Policy field | Expected value |
|---|---|
| Domain | `Quest` |
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | one deterministic map-witness page |
| Setup/trigger cue | Accept, complete, or fail a quest whose root or lifecycle signal matches this policy. |
| Expected evidence | The page or later batch/reflection uses the **Royal Ascent** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `EndGame_RoyalAscent`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `questRoyalAscent`

<!-- repowiki:group {"defName":"questAccepted","domain":"Quest","defaultEnabled":false,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["accepted"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `questAccepted` — Quest accepted

| Policy field | Expected value |
|---|---|
| Domain | `Quest` |
| Prerequisite | Base game |
| Default enabled | no; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Accept, complete, or fail a quest whose root or lifecycle signal matches this policy. |
| Expected evidence | The page or later batch/reflection uses the **Quest accepted** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `accepted`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `questAccepted`

<!-- repowiki:group {"defName":"questCompleted","domain":"Quest","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["completed"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `questCompleted` — Quest completed

| Policy field | Expected value |
|---|---|
| Domain | `Quest` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Accept, complete, or fail a quest whose root or lifecycle signal matches this policy. |
| Expected evidence | The page or later batch/reflection uses the **Quest completed** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `completed`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `questCompleted`

<!-- repowiki:group {"defName":"questFailed","domain":"Quest","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["failed"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `questFailed` — Quest failed

| Policy field | Expected value |
|---|---|
| Domain | `Quest` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | colony/map fan-out: one solo page per admitted colonist |
| Setup/trigger cue | Accept, complete, or fail a quest whose root or lifecycle signal matches this policy. |
| Expected evidence | The page or later batch/reflection uses the **Quest failed** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `failed`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `questFailed`

<!-- repowiki:group {"defName":"ritualConversion","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":["Conversion;RitualBehaviorWorker_Conversion"],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Ideology"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualConversion` — Conversion rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Ideology (`Ludeon.RimWorld.Ideology`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Conversion rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: `Conversion;RitualBehaviorWorker_Conversion`
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualConversion`

<!-- repowiki:group {"defName":"ritualRoyal","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["ThroneSpeech","BestowingCeremony","AnimaTreeLinking","RitualOutcomeEffectWorker_Bestowing","RitualBehaviorWorker_ThroneSpeech","RitualBehaviorWorker_AnimaLinking"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualRoyal` — Royal rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Royal rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `ThroneSpeech`, `BestowingCeremony`, `AnimaTreeLinking`, `RitualOutcomeEffectWorker_Bestowing`, `RitualBehaviorWorker_ThroneSpeech`, `RitualBehaviorWorker_AnimaLinking`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualRoyal`

<!-- repowiki:group {"defName":"ritualChildbirth","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["ChildBirth","RitualBehaviorWorker_ChildBirth"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualChildbirth` — Childbirth ritual

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Childbirth ritual** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `ChildBirth`, `RitualBehaviorWorker_ChildBirth`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualChildbirth`

<!-- repowiki:group {"defName":"ritualGravship","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["GravshipLaunch","RitualBehaviorWorker_GravshipLaunch"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Odyssey"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualGravship` — Gravship launch

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Gravship launch** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `GravshipLaunch`, `RitualBehaviorWorker_GravshipLaunch`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualGravship`

<!-- repowiki:group {"defName":"personaWeaponLifecycle","domain":"PersonaWeapon","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PersonaWeaponBondFormed","PersonaWeaponBondSeparated","PersonaWeaponBondRecovered","PersonaWeaponBondEnded"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `personaWeaponLifecycle` — Persona weapon bonds

| Policy field | Expected value |
|---|---|
| Domain | `PersonaWeapon` |
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Form, separate, recover, or end the matching persona-weapon bond. |
| Expected evidence | The page or later batch/reflection uses the **Persona weapon bonds** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PersonaWeaponBondFormed`, `PersonaWeaponBondSeparated`, `PersonaWeaponBondRecovered`, `PersonaWeaponBondEnded`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `personaWeaponLifecycle`

<!-- repowiki:group {"defName":"royalPermitDramatic","domain":"RoyalPermit","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RoyalPermitMilitaryAid","RoyalPermitTransportShuttle","RoyalPermitOrbitalStrike","RoyalPermitOrbitalSalvo"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `royalPermitDramatic` — Dramatic royal permits

| Policy field | Expected value |
|---|---|
| Domain | `RoyalPermit` |
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Successfully use an allowlisted dramatic royal permit. |
| Expected evidence | The page or later batch/reflection uses the **Dramatic royal permits** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `RoyalPermitMilitaryAid`, `RoyalPermitTransportShuttle`, `RoyalPermitOrbitalStrike`, `RoyalPermitOrbitalSalvo`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `royalPermitDramatic`

<!-- repowiki:group {"defName":"odysseyGravshipLanding","domain":"GravshipJourney","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["OdysseyGravshipLanding"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Odyssey"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `odysseyGravshipLanding` — Gravship landing

| Policy field | Expected value |
|---|---|
| Domain | `GravshipJourney` |
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Complete a qualifying gravship landing. |
| Expected evidence | The page or later batch/reflection uses the **Gravship landing** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `OdysseyGravshipLanding`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `odysseyGravshipLanding`

<!-- repowiki:group {"defName":"odysseyMechhiveOutcome","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_OdysseyMechhiveOutcome"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Odyssey"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `odysseyMechhiveOutcome` — Fate of the Mechhive

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Odyssey (`Ludeon.RimWorld.Odyssey`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Fate of the Mechhive** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_OdysseyMechhiveOutcome`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `odysseyMechhiveOutcome`

<!-- repowiki:group {"defName":"anomalyStudyBreakthrough","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_AnomalyStudyBreakthrough"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `anomalyStudyBreakthrough` — Anomaly study breakthroughs

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Anomaly study breakthroughs** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_AnomalyStudyBreakthrough`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `anomalyStudyBreakthrough`

<!-- repowiki:group {"defName":"anomalyContainmentBreach","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_ContainmentBreach"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `anomalyContainmentBreach` — Anomaly containment breaches

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Anomaly containment breaches** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_ContainmentBreach`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `anomalyContainmentBreach`

<!-- repowiki:group {"defName":"anomalyCreepJoinerOutcome","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_CreepJoinerOutcome"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `anomalyCreepJoinerOutcome` — Visible strange-arrival outcomes

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Visible strange-arrival outcomes** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_CreepJoinerOutcome`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `anomalyCreepJoinerOutcome`

<!-- repowiki:group {"defName":"anomalyGhoulTransformation","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_GhoulTransformation"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `anomalyGhoulTransformation` — Ghoul transformations

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Ghoul transformations** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_GhoulTransformation`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `anomalyGhoulTransformation`

<!-- repowiki:group {"defName":"anomalyVoidOutcome","domain":"Interaction","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnDiary_VoidOutcome"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `anomalyVoidOutcome` — Answer to the void

| Policy field | Expected value |
|---|---|
| Domain | `Interaction` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | paired first-person pages when two eligible POVs exist; otherwise the route-specific solo page |
| Setup/trigger cue | Cause a social-log or synthetic interaction whose identifier matches one of the matcher values below. |
| Expected evidence | The page or later batch/reflection uses the **Answer to the void** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnDiary_VoidOutcome`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `anomalyVoidOutcome`

<!-- repowiki:group {"defName":"ritualAnomalyInvitation","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsychicRitual;VoidProvocation","PsychicRitual;SummonAnimals","PsychicRitual;SummonShamblers"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualAnomalyInvitation` — Anomaly invitations

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Anomaly invitations** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsychicRitual;VoidProvocation`, `PsychicRitual;SummonAnimals`, `PsychicRitual;SummonShamblers`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualAnomalyInvitation`

<!-- repowiki:group {"defName":"ritualAnomalyFleshAndWeather","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsychicRitual;SummonPitGate","PsychicRitual;SummonFleshbeasts","PsychicRitual;SummonFleshbeastsPlayer","PsychicRitual;BloodRain"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualAnomalyFleshAndWeather` — Flesh and hostile weather rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Flesh and hostile weather rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsychicRitual;SummonPitGate`, `PsychicRitual;SummonFleshbeasts`, `PsychicRitual;SummonFleshbeastsPlayer`, `PsychicRitual;BloodRain`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualAnomalyFleshAndWeather`

<!-- repowiki:group {"defName":"ritualAnomalyPredation","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsychicRitual;Philophagy","PsychicRitual;Chronophagy","PsychicRitual;Psychophagy"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualAnomalyPredation` — Predatory psychic rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Predatory psychic rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsychicRitual;Philophagy`, `PsychicRitual;Chronophagy`, `PsychicRitual;Psychophagy`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualAnomalyPredation`

<!-- repowiki:group {"defName":"ritualAnomalyMind","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsychicRitual;Brainwipe","PsychicRitual;PleasurePulse","PsychicRitual;NeurosisPulse"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualAnomalyMind` — Mind-altering psychic rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Mind-altering psychic rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsychicRitual;Brainwipe`, `PsychicRitual;PleasurePulse`, `PsychicRitual;NeurosisPulse`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualAnomalyMind`

<!-- repowiki:group {"defName":"ritualAnomalyAbduction","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsychicRitual;SkipAbduction","PsychicRitual;SkipAbductionPlayer"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualAnomalyAbduction` — Skip abduction rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Skip abduction rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsychicRitual;SkipAbduction`, `PsychicRitual;SkipAbductionPlayer`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualAnomalyAbduction`

<!-- repowiki:group {"defName":"ritualAnomalyDeathRefusal","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsychicRitual;ImbueDeathRefusal"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualAnomalyDeathRefusal` — Death-refusal ritual

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Death-refusal ritual** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsychicRitual;ImbueDeathRefusal`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualAnomalyDeathRefusal`

<!-- repowiki:group {"defName":"ritualAnomalyPsychic","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["PsychicRitual"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Anomaly"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualAnomalyPsychic` — Psychic rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Anomaly (`Ludeon.RimWorld.Anomaly`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Psychic rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `PsychicRitual`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualAnomalyPsychic`

<!-- repowiki:group {"defName":"ritualFinished","domain":"Ritual","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `ritualFinished` — Rituals

| Policy field | Expected value |
|---|---|
| Domain | `Ritual` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | ritual participant/role fan-out page |
| Setup/trigger cue | Complete a matching ritual with at least one diary-eligible participant. |
| Expected evidence | The page or later batch/reflection uses the **Rituals** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `ritualFinished`

<!-- repowiki:group {"defName":"abilityPsycast","domain":"Ability","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Psycast"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `abilityPsycast` — Psycasts

| Policy field | Expected value |
|---|---|
| Domain | `Ability` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Successfully activate a matching pawn ability. |
| Expected evidence | The page or later batch/reflection uses the **Psycasts** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Psycast`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `abilityPsycast`

<!-- repowiki:group {"defName":"abilityHostile","domain":"Ability","defaultEnabled":true,"important":true,"combat":true,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":["Hostile"],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `abilityHostile` — Combat abilities

| Policy field | Expected value |
|---|---|
| Domain | `Ability` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / yes |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Successfully activate a matching pawn ability. |
| Expected evidence | The page or later batch/reflection uses the **Combat abilities** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: `Hostile`
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `abilityHostile`

<!-- repowiki:group {"defName":"abilityUsed","domain":"Ability","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `abilityUsed` — Abilities

| Policy field | Expected value |
|---|---|
| Domain | `Ability` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Successfully activate a matching pawn ability. |
| Expected evidence | The page or later batch/reflection uses the **Abilities** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `abilityUsed`

<!-- repowiki:group {"defName":"biotechPsychicBondLifecycle","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BiotechPsychicBondFormed","BiotechPsychicBondRuptured"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `biotechPsychicBondLifecycle` — Psychic-bond lifecycle

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Psychic-bond lifecycle** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BiotechPsychicBondFormed`, `BiotechPsychicBondRuptured`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `biotechPsychicBondLifecycle`

<!-- repowiki:group {"defName":"biotechDeathrestInterrupted","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BiotechDeathrestInterrupted"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `biotechDeathrestInterrupted` — Interrupted deathrest

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Interrupted deathrest** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BiotechDeathrestInterrupted`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `biotechDeathrestInterrupted`

<!-- repowiki:group {"defName":"progressionMechanitorLifecycle","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BiotechMechlinkInstalled","BiotechMechlinkRemoved","BiotechFirstControlledMech","BiotechFirstControlledMechCombat","BiotechSignificantMechLoss","BiotechBossCalled","BiotechBossDefeated"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionMechanitorLifecycle` — Mechanitor lifecycle

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Mechanitor lifecycle** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BiotechMechlinkInstalled`, `BiotechMechlinkRemoved`, `BiotechFirstControlledMech`, `BiotechFirstControlledMechCombat`, `BiotechSignificantMechLoss`, `BiotechBossCalled`, `BiotechBossDefeated`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionMechanitorLifecycle`

<!-- repowiki:group {"defName":"progressionGrowthMoment","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BiotechGrowthMoment"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionGrowthMoment` — Growth moment

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Growth moment** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BiotechGrowthMoment`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionGrowthMoment`

<!-- repowiki:group {"defName":"progressionSkillPassion","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["SkillMilestone"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionSkillPassion` — Passion skill milestone

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Passion skill milestone** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `SkillMilestone`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionSkillPassion`

<!-- repowiki:group {"defName":"progressionPsylink","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PsylinkLevel"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionPsylink` — Psylink gained

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Psylink gained** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PsylinkLevel`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionPsylink`

<!-- repowiki:group {"defName":"progressionXenotype","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["XenotypeChanged","GeneIdentityChanged"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Biotech"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionXenotype` — Gene identity changed

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Biotech (`Ludeon.RimWorld.Biotech`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Gene identity changed** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `XenotypeChanged`, `GeneIdentityChanged`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionXenotype`

<!-- repowiki:group {"defName":"progressionRoyalTitle","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RoyalTitleChanged","RoyalTitleGained","RoyalTitlePromoted","RoyalTitleDemoted","RoyalTitleLost","RoyalSuccession","RoyalHeirAppointed"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":["Ludeon.RimWorld.Royalty"],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionRoyalTitle` — Royal rank and succession

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Royalty (`Ludeon.RimWorld.Royalty`) |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Royal rank and succession** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `RoyalTitleChanged`, `RoyalTitleGained`, `RoyalTitlePromoted`, `RoyalTitleDemoted`, `RoyalTitleLost`, `RoyalSuccession`, `RoyalHeirAppointed`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionRoyalTitle`

<!-- repowiki:group {"defName":"progressionTraitGained","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["TraitGained"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionTraitGained` — New trait

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **New trait** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `TraitGained`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionTraitGained`

<!-- repowiki:group {"defName":"progressionBirthday","domain":"Progression","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["PawnBirthday"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionBirthday` — Birthday

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Birthday** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `PawnBirthday`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionBirthday`

<!-- repowiki:group {"defName":"progressionArrivalAnniversary","domain":"Progression","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["ArrivalAnniversary"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionArrivalAnniversary` — Colony anniversary

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Colony anniversary** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `ArrivalAnniversary`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionArrivalAnniversary`

<!-- repowiki:group {"defName":"progressionDeathAnniversary","domain":"Progression","defaultEnabled":true,"important":true,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["BondedDeathAnniversary"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionDeathAnniversary` — Remembered loss

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | yes / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Remembered loss** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `BondedDeathAnniversary`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionDeathAnniversary`

<!-- repowiki:group {"defName":"progressionRecordMilestone","domain":"Progression","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["RecordMilestone"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionRecordMilestone` — Personal record

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Personal record** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `RecordMilestone`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionRecordMilestone`

<!-- repowiki:group {"defName":"progressionOther","domain":"Progression","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":true,"matchDefNames":[],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `progressionOther` — Progression

| Policy field | Expected value |
|---|---|
| Domain | `Progression` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | immediate solo or route-owned page when the source is admitted |
| Setup/trigger cue | Reach the matching synthetic progression milestone during its scheduled or correlated check. |
| Expected evidence | The page or later batch/reflection uses the **Progression** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: —
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: yes

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `progressionOther`

<!-- repowiki:group {"defName":"externalDevTest","domain":"External","defaultEnabled":true,"important":false,"combat":false,"batchEnabled":false,"batchMode":"none","batchScope":"","batchSyntheticDefName":"","batchWindowTicks":0,"batchMaxEvents":0,"catchAll":false,"matchDefNames":["pawndiary_dev_test"],"matchOrdinalDefNames":[],"matchPrefixes":[],"matchSuffixes":[],"matchSegments":[],"matchTokens":[],"matchPackageIds":[],"enableWhenPackageIdsLoaded":[],"disableWhenPackageIdsLoaded":[],"disableWhenCaptureCapabilitiesReady":[]} -->
## Group: `externalDevTest` — External test event

| Policy field | Expected value |
|---|---|
| Domain | `External` |
| Prerequisite | Base game |
| Default enabled | yes; an existing player override still wins |
| Important / combat | no / no |
| Page/batch/reflection behavior | adapter-submitted page using the request shape chosen by the adapter |
| Setup/trigger cue | Submit the exact event key through a shipped adapter or `PawnDiaryApi`. |
| Expected evidence | The page or later batch/reflection uses the **External test event** group label. Prompt-test output shows this group's instruction/tone; important/combat flags affect card emphasis and combat context. |

Matcher inventory (first matching group by XML order wins):

- Exact names: `pawndiary_dev_test`
- Ordinal exact names: —
- Prefixes: —
- Suffixes: —
- Whole segments: —
- Substring tokens: —
- Source package IDs: —
- Catch-all: no

Source: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml) — `externalDevTest`

## Source of truth

- Runtime registration: [Source/Capture/Catalog/DiaryEventCatalog.cs](../../../Source/Capture/Catalog/DiaryEventCatalog.cs) — `DiaryEventCatalog.EnsureInitialized`.
- Event identifiers: [Source/Capture/DiaryEventType.cs](../../../Source/Capture/DiaryEventType.cs) — `DiaryEventType`.
- Core classification: [1.6/Defs/DiaryInteractionGroupDefs.xml](../../../1.6/Defs/DiaryInteractionGroupDefs.xml).
- Package-specific compatibility groups are cataloged separately in [Compatibility](Compatibility.md).
