# Event To Prompt Map

Human-readable reference for how recorded Pawn Diary events become LLM prompts.

This document describes the current runtime mapping. The authoritative sources are:

- `Source/Pipeline/DiaryPromptPlanner.cs` for template selection.
- `1.6/Defs/DiaryPromptTemplateDefs.xml` for prompt fields.
- `Source/Capture/Events/*.cs` and the batch flushers in `Source/Core/` for `gameContext` facts.

## 1. Prompt Selection

Every event is projected into a `DiaryEventPayload`, then `DiaryPromptPlanner.TemplateKeyFor`
chooses one template key. The checks run in this order:

| Condition | Template |
|---|---|
| Title follow-up request | `Title` |
| Event has `death_description=true` | `DeathDescription` |
| Event has `arrival_description=true` | `ArrivalDescription` |
| Pair event and XML group is combat | `PairCombat` |
| Pair event and `gameContext` contains `batch=` | `PairBatched` |
| Pair event and XML group is important or missing | `PairImportant` |
| Pair event and XML group is not important | `PairDefault` |
| Solo event marked as a day reflection | `SoloDayReflection` |
| Solo event with `mood_event=`, `thought=`, `inspiration=`, `work=`, or `hediff=` | `SoloInternalState` |
| Solo event with `batch=` and combat group | `SoloImportant` |
| Solo event with `batch=` and non-combat group | `SoloBatched` |
| Solo event and XML group is important or missing | `SoloImportant` |
| Solo event and XML group is not important | `SoloDefault` |

Important details:

- Death and arrival prompts are neutral, persona-free, and prompt-enchantment-free.
- Pair prompts generate the initiator first. The recipient prompt may include the generated initiator
  entry as hidden continuity context.
- Direct-speech instructions are only appended for normal social Interaction prompts and
  interaction batches. They are not appended for non-Interaction source markers, ambient day notes,
  death, arrival, day reflection, or dev mock events.
- Empty values and the placeholder values `none`, `n/a`, and `unknown` are omitted from the final
  user prompt.

## 2. Template Field Sets

The XML template controls field order and labels. These are the fields currently sent when a value is
available:

| Template | Fields |
|---|---|
| `PairDefault` | `event`, `pov`, `role`, `with`, `what you saw`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `royal title`, `ideoligion role`, `important context`, `setting`, `relationship`, `my last opener (not repeat)` |
| `PairImportant` | `event`, `pov`, `role`, `with`, `what you saw`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `royal title`, `ideoligion role`, `important context`, `setting`, `tone`, `relationship`, `my last opener (not repeat)`, `initiator diary (hidden context)` |
| `PairCombat` | `event`, `pov`, `role`, `with`, `what you saw`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `you`, `royal title`, `ideoligion role`, `important context`, `setting`, `tone`, `relationship`, `my last opener (not repeat)`, `weapon`, `initiator diary (hidden context)` |
| `PairBatched` | `event`, `pov`, `role`, `with`, `what you saw`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `royal title`, `ideoligion role`, `important context`, `setting`, `my last opener (not repeat)` |
| `SoloDefault` | `event`, `pov`, `what happened`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `royal title`, `ideoligion role`, `important context`, `setting`, `my last opener (not repeat)` |
| `SoloImportant` | `event`, `pov`, `what happened`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `you`, `royal title`, `ideoligion role`, `important context`, `setting`, `tone`, `my last opener (not repeat)` |
| `SoloInternalState` | `event`, `pov`, `what happened`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `royal title`, `ideoligion role`, `important context`, `setting`, `my last opener (not repeat)` |
| `SoloBatched` | `event`, `pov`, `what happened`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `royal title`, `ideoligion role`, `important context`, `setting`, `my last opener (not repeat)` |
| `SoloDayReflection` | `event`, `pov`, `what happened`, `instruction`, `ritual role`, `ritual title`, `ability`, `ability category`, `ability target`, `ability cooldown ticks`, `you`, `royal title`, `ideoligion role`, `important context`, `setting`, `my last opener (not repeat)` |
| `DeathDescription` | `event`, `deceased`, `what happened`, `death facts`, `deceased pawn`, `setting` |
| `ArrivalDescription` | `event`, `colonist`, `what happened`, `arrival facts`, `colonist pawn`, `setting` |
| `Title` | `entry` |

The ritual, ability, and DLC-status fields only render when the event context has real values. The
placeholder values `none`, `n/a`, and `unknown` are omitted, so normal prompts do not show empty
ritual/ability/status lines.

## 3. Event Source Map

### Social Interactions

| Item | Value |
|---|---|
| Source | `PlayLog.Add` social interactions, through `InteractionEventData`. |
| Main context marker | `def=<InteractionDef>; label=<label>` |
| Extra context | Optional `worker=<workerClass>`, `initiatorThought=<label>`, `recipientThought=<label>`. |
| Immediate pair prompt | `PairCombat` for combat groups, otherwise `PairImportant` or `PairDefault` from XML group importance. |
| Immediate solo prompt | `SoloImportant` or `SoloDefault` from XML group importance. |
| Interaction pair batch | `PairBatched` with `group=<group>; batch=interaction; events=<n>; first_tick=<tick>; last_tick=<tick>`. |
| Interaction solo batch | `SoloBatched`, or `SoloImportant` if the batch group is combat. |
| Ambient interaction day note | `SoloBatched` with `group=<group>; batch=ambient_day_note; events=<n>; day=<day>; participants=<names>; first_tick=<tick>; last_tick=<tick>`. |
| Prompt evidence | The per-POV social log text, or a bullet-style batch summary. XML group `instruction` and `tone` are added when present. |

### Mental States

| Item | Value |
|---|---|
| Source | `MentalStateHandler.TryStartMentalState`, through `MentalStateEventData`. |
| Pair context | `mental_state=<defName>; label=<label>; reason=<reason>` for `SocialFighting` with two eligible pawns. |
| Solo context | `mental_state=<defName>; label=<label>; target=<pawn>; reason=<reason>` for other breaks or one eligible pawn. |
| Pair prompt | `PairCombat`; the pipeline treats MentalState pair events as combat. |
| Solo prompt | Usually `SoloImportant`, unless the XML group is explicitly non-important. |
| Prompt evidence | Mental-state label, optional target/reason, pawn summary on important prompts, surroundings, tone, health hint, and weapon for pair combat prompts. |

### Romance Relation Changes

| Item | Value |
|---|---|
| Source | `Pawn_RelationsTracker.AddDirectRelation`, through `RomanceEventData`. |
| Context | `romance=<relationDef>; label=<label>; kind=<married|lover|breakup|divorce>` |
| Prompt | Pair prompt, normally `PairImportant` because the `romance_relation` XML group is important by default. |
| Prompt evidence | Both POV texts, relationship continuity, XML romance instruction/tone, surroundings, health hint, and hidden initiator context for the recipient. |
| Direct speech | Not added; this is a relation milestone, not a Social-log interaction prompt. |

### Tales

| Item | Value |
|---|---|
| Source | `TaleRecorder.RecordTale`, through `TaleEventData`. |
| Context | `tale=<TaleDef>; label=<label>; taleClass=<class>` |
| Extra context | Optional `attachedDef=<defName>; attachedLabel=<label>` for Tale data carrying another Def such as skill, research, damage type, or crafted object kind. |
| Pair prompt | `PairCombat`, `PairImportant`, or `PairDefault` depending on the Tale XML group. |
| Solo prompt | `SoloImportant` or `SoloDefault` depending on XML group importance. |
| Batched Tale prompt | `SoloBatched` with `tale=<syntheticOrFirstDef>; label=<label>; taleClass=TaleBatch; group=<group>; batch=tale; events=<n>; day=<day>; first_tick=<tick>; last_tick=<tick>; tale_defs=<defs>; participants=<names>`. |
| Death Tale prompt | `DeathDescription`, not a first-person prompt. Adds death context described below. |
| Prompt evidence | Factual Tale text, attached Def label when present, XML Tale instruction/tone, pawn summaries on important prompts, surroundings, health hint, and hidden initiator context for pair recipient prompts. |

### Death Descriptions

| Item | Value |
|---|---|
| Source | Death-marked Tales or the `Pawn.Kill` fallback through `DeathEventData`. |
| Context | `tale=<def>; label=<label>; taleClass=<class>; death_description=true; death_victim=<name>; death_victim_id=<id>; death_victim_role=<initiator|recipient>` |
| Extra context | Optional `other_pawn=<name>`, `damage=<label>`, `damageDef=<defName>`, `damageAmount=<amount>`, `hitPart=<part>`, `instigator=<name>`, `weapon=<label>`, `weaponDef=<defName>`, `tool=<label>`, `culprit=<hediff>`, `culpritDef=<defName>`, `culpritPart=<part>`, `culpritSeverity=<value>`, `destroyed_or_missing_parts=<list>`, `other_lethal_conditions=<list>`, `death_surroundings=<summary>`. |
| Prompt | Always `DeathDescription`. |
| Prompt evidence | `deceased`, neutral raw death text, synthesized `death facts`, deceased pawn summary, and death-specific surroundings. |
| Persona/enchantment/direct speech | Disabled. |

### Arrivals

| Item | Value |
|---|---|
| Source | Starting-colonist scan and `Pawn.SetFaction`, through `ArrivalEventData`. |
| Context | `arrival_description=true; arrival_pawn=<name>; arrival_pawn_id=<id>` |
| Starting-pawn context | `arrival_source=game_start`, optional `scenario_name=<name>`, `scenario_description=<text>`. |
| Later-join context | `arrival_source=set_faction`, optional `priorFaction=<name>`, `pawnKind=<defName>`, `recruiter=<name>`, `creepjoiner=true`, `arrival_surroundings=<summary>`. Fallback paths may use `arrival_source=<fallback>` and `pawnKind=<defName>`. |
| Prompt | Always `ArrivalDescription`. |
| Prompt evidence | `colonist`, neutral raw arrival text, synthesized `arrival facts`, pawn summary, and surroundings. |
| Persona/enchantment/direct speech | Disabled. |

### Mood Events

| Item | Value |
|---|---|
| Source | `GameConditionManager.RegisterCondition`, through `MoodEventData`. |
| Context | `mood_event=<GameConditionDef>; label=<label>; mood_impact=<positive|negative|neutral>` |
| Prompt | `SoloInternalState`. |
| Prompt evidence | Per-pawn condition text, XML MoodEvent instruction/tone, mood impact in context, surroundings, health hint, and last opener. |

### Raids

| Item | Value |
|---|---|
| Source | `IncidentWorker.TryExecute` (filtered to `IncidentWorker_Raid`), through `RaidEventData`. Minimal realization: one solo entry per eligible colonist on the raid's target map. |
| Context | `raid=<IncidentDef>; label=<label>; faction=<FactionDef\|unknown>; points=<int>` |
| Hostile-raid prompt | `SoloImportant` — the `raid` XML group is important. |
| Friendly-raid prompt | `SoloDefault` — the `raidFriendly` XML group is not important. |
| Prompt evidence | Per-pawn raid text, XML Raid instruction/tone, raider faction and threat points in context, surroundings, health hint, and last opener. The minimal payload carries only incident defName + raider faction + raid points; strategy, raider count, and loadout are intentionally not captured. |
| Colony dedup | One window per raid, keyed by incident/faction/points/map. |

### Quests

| Item | Value |
|---|---|
| Source | `Quest.Accept` plus a defensive `MainTabWindow_Quests` accept-action fallback, a `Quest.EverAccepted` state scan, and `Quest.End` (filtered to `QuestEndOutcome.Success` / `Fail`), through `QuestEventData`. Colony-wide: every eligible colonist gets their own solo entry. |
| Recording rule | Only accepted quests are recorded. Offered-but-not-accepted quests (`QuestManager.Add`) are ignored entirely; the Accept hook is the entry point. |
| Signals | One `DiaryEventType.Quest` carries three signals via the `Signal` field: `accepted` (on `Quest.Accept` or the accepted-state scan), `completed` (on `Quest.End` with `Success`), `failed` (on `Quest.End` with `Fail`). Each signal routes to its own XML group via `ClassifyQuest(signal)`. |
| Context | `quest=<QuestScriptDef>; signal=<accepted\|completed\|failed>; label=<label>; faction=<FactionDef\|unknown>; rewards=<summary\|none>` |
| Description | The quest's prose description is NOT in the context marker — it lives in the localized event text (it is prose, not a structured field). |
| Rewards | Aggregated from `QuestPart_DropPods.thingDefs` into a short summary. Delayed/choice reward parts are not aggregated; absent rewards fall back to the `none` sentinel. |
| Prompt | `SoloImportant` for all three signals (the `questAccepted` / `questCompleted` / `questFailed` XML groups are all important). |
| Prompt evidence | Per-pawn quest text (carrying the description prose), XML Quest instruction/tone, issuer faction and reward summary in context, surroundings, health hint, and last opener. |
| Colony dedup | One window per quest signal, keyed by quest id + signal. |

### Rituals

| Item | Value |
|---|---|
| Source | `LordJob_Ritual.ApplyOutcome` and `LordToil_PsychicRitual.RitualCompleted`, through `RitualEventData`. Only finished/successful, non-canceled rituals record. |
| Recording rule | One finished ritual fans out to separate solo entries for the author/invoker, target pawn when available, participants, and spectators. Duplicate pawns are recorded once, in that priority order. |
| Context | `ritual=<Precept_Ritual>; ritual_title=<title>; ritual_behavior=<worker>; ritual_perspective=<author\|target\|participant\|spectator>; ritual_role=<role>; royal_title=<title\|none>; ideological_role=<role\|none>; outcome=finished; quality=<terrible\|weak\|decent\|strong\|excellent\|unknown>` |
| Psychic ritual context | `psychic_ritual=<PsychicRitualDef>; psychic_ritual_perspective=<invoker\|target\|participant\|spectator>; outcome=finished; quality=<terrible\|weak\|decent\|strong\|excellent\|unknown>`. These entries intentionally do not send `ritual_title` or `ritual_role`. |
| Quality policy | `DiaryTuningDef.xml` owns the ritual quality bands. Prompts should respect the supplied quality as pressure on confidence, aftermath, and emotional weight, but should not quote or explain the label directly. |
| Prompt | Usually `SoloImportant`; all shipped Ritual groups are important. |
| Perspective instruction | Author/invoker, target, participant, and spectator entries each get a separate localized instruction after the ritual has finished. Psychic ritual invoker entries request exactly one standalone `[[speech]]...[[/speech]]` block containing unsettling invented ritual speech. |
| Edge groups | Royalty `ThroneSpeech` / `AnimaTreeLinking` use more courtly or psyfocus/anima flavor; Biotech `ChildBirth` stays medically and emotionally appropriate; Odyssey `GravshipLaunch` is technical, launch/flight/landing focused; Anomaly psychic rituals use the dark color cue and unsettling atmosphere, with display-side distortion applied to invoker speech blocks from saved psychic-ritual context. |
| Prompt evidence | Per-pawn ritual text, XML Ritual instruction/tone, role/title/status context, ritual behavior, surroundings, important context, and last opener. |
| Dedup | One window per ritual defName + organizer + target + tick. |

### Abilities

| Item | Value |
|---|---|
| Source | Successful `Ability.Activate(LocalTargetInfo, LocalTargetInfo)` and `Ability.Activate(GlobalTargetInfo)` calls, through `AbilityEventData`. |
| Recording rule | One solo entry for the caster if the pawn is diary-eligible, the XML Ability group is enabled, the dedup key is clear, and the cooldown-weighted roll succeeds. |
| Weighting | `CooldownWeightedChance = min + (max-min) * cooldown/(cooldown+reference)`, using `abilityUseMinChance`, `abilityUseMaxChance`, and `abilityUseReferenceCooldownTicks` from `DiaryTuningDef.xml`. Faster cooldowns therefore have lower capture odds. |
| Context | `ability=<AbilityDef>; ability_label=<label>; ability_category=<category\|unknown>; ability_cooldown_ticks=<ticks>; ability_record_chance=<chance>; ability_target=<target>` when a target label is available. |
| Prompt | Usually `SoloImportant`; the shipped Ability groups are important. `abilityHostile` is combat-marked, though ability entries are solo so they still use solo templates. |
| Prompt evidence | Per-pawn ability text, XML Ability instruction/tone, ability name/category/target/cooldown fields, surroundings, important context, and last opener. |
| Dedup | One window per caster + ability + target + activation tick. |

### Thoughts

| Item | Value |
|---|---|
| Source | `MemoryThoughtHandler.TryGainMemory`, through `ThoughtEventData`. |
| Context | `thought=<ThoughtDef>; label=<label>; mood_impact=<positive|negative|neutral>; mood_offset=<value>; duration_days=<value>` |
| Normal prompt | `SoloInternalState`. |
| Ambient thought prompt | Also `SoloInternalState` because `thought=` is checked before `batch=`. Context becomes `thought=ThoughtAmbientDay; batch=ambient_day_note; events=<n>; day=<day>; mood_impact=<impact>; mood_offset_sum=<sum>; first_tick=<tick>; last_tick=<tick>`. |
| Prompt evidence | Raw thought text or ambient bullet summary, XML thought instruction, mood values in context, surroundings, health hint, and last opener. |

### Thought Progression

| Item | Value |
|---|---|
| Source | Periodic need/thought-stage scan, through `ThoughtProgressionEventData`. |
| Context | `thought=<ThoughtDef>; thought_progression=<category>; label=<label>; stage_index=<n>; severity=<value>; mood_impact=<impact>; mood_offset=<value>` |
| Prompt | `SoloInternalState`. |
| Prompt evidence | Progression text, category/stage/severity context, XML instruction, surroundings, health hint, and last opener. |

### Inspirations

| Item | Value |
|---|---|
| Source | `InspirationHandler.TryStartInspiration`, through `InspirationEventData`. |
| Context | `inspiration=<InspirationDef>; label=<label>; duration_days=<value>; reason=<optional>` |
| Prompt | `SoloInternalState`. |
| Prompt evidence | Inspiration text, optional reason, XML Inspiration instruction/tone, surroundings, health hint, and last opener. |

### Hediffs

| Item | Value |
|---|---|
| Source | `Pawn_HealthTracker.AddHediff` and severity progression scan, through `HediffEventData`. |
| Context | `hediff=<HediffDef>; label=<label>; source=<add|severity_progression>; group=<group>; mode=<Immediate|DayReflection>; severity=<F2>; stage=<n>` |
| Extra context | Optional `stage_label=<label>`, `body_part=<label>`. |
| Immediate prompt | `SoloInternalState`. |
| Day-reflection route | Does not immediately generate a hediff prompt; it contributes weighted health evidence to the later day reflection. |
| Prompt evidence | Health event text, severity/stage/body-part context, XML Hediff instruction, surroundings, health hint, and last opener. |

### Work

| Item | Value |
|---|---|
| Source | Periodic current-job sampling, through `WorkEventData`. |
| Context | `work=<WorkTypeDef>; work_giver=<WorkGiverDef>; mood_impact=<impact>; passion=<true|false>; low_skill=<true|false>; dumb_or_cleaning=<true|false>; dark_study=<true|false>` |
| Prompt | `SoloInternalState`. |
| Synthetic defNames | `PawnDiary_WorkPassion`, `PawnDiary_WorkStrain`, `PawnDiary_WorkRoutine`, or `PawnDiary_WorkDarkStudy`. |
| Prompt evidence | Work text, work type/giver and skill/passion/chore/dark-study context, XML Work instruction/tone, surroundings, health hint, and last opener. |

### Day Reflections

| Item | Value |
|---|---|
| Source | Sleep/rest day-summary trigger, through `DayReflectionEventData`. |
| Context | `day_reflection=true; day=<day>; highlights=<n>; candidates=<n>; filler_moments=<n>; signals=<tags>` |
| Prompt | `SoloDayReflection`. |
| Prompt evidence | A curated highlight summary from the pawn's day, pawn summary, surroundings, important context hint, and last opener. |

## 4. Field Source Glossary

| Prompt field source | What it means |
|---|---|
| `EventNoun` | The event label lowered for use as `event`. |
| `PovName` | The pawn whose POV is being generated. Neutral prompts use the colony name or a neutral subject. |
| `PovRole` | `initiator`, `recipient`, or `neutral`. |
| `OtherPawnName` | The other pawn in a pair event. |
| `PovText` | Raw game evidence for this POV: a log line, generated factual summary, batch sample list, work/thought text, or day reflection text. |
| `NeutralText` | Raw neutral evidence for death and arrival descriptions. |
| `Instruction` | XML group instruction or event-specific instruction. |
| `PawnSummary` | Snapshot of the POV pawn: traits/backstory/health/social/life context assembled by the runtime adapter. |
| `PromptEnchantment` | One optional live health/capacity pressure line. Omitted when the template disables prompt enchantments or no signal is available. |
| `Setting` | Surroundings summary for the POV pawn. |
| `Tone` | XML group tone, if configured. |
| `Relationship` | Continuity and relationship context for this POV. |
| `LastOpener` | Recent diary opener to discourage repeated starts. |
| `Weapon` | Equipped weapon, used by combat templates. |
| `HiddenInitiatorEntry` | The initiator's generated diary text, only for a recipient prompt in a sequential pair event. |
| `DeathVictim` | Death subject from `death_victim` or the victim role. |
| `DeathFacts` | Compact facts extracted from death context: damage, hit part, instigator, weapon, tool, culprit, missing parts, other lethal conditions, other pawn, and death surroundings. |
| `DeathPawnSummary` | Pawn summary for the deceased pawn. |
| `DeathSetting` | Death-specific surroundings. |
| `ArrivalPawn` | Arriving colonist name from `arrival_pawn` or the event initiator. |
| `ArrivalFacts` | Compact facts extracted from arrival context: source, scenario, prior faction, pawn kind, recruiter, creepjoiner, and arrival surroundings. |
| `EntryText` | Generated diary entry text passed to the title prompt. |
| `GameContext` | Direct lookup into `gameContext` by `contextKey`; used by shipped ritual, ability, and DLC-status fields. |
