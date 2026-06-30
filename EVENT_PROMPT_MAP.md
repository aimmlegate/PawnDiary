# Event To Prompt Map

Current-state reference for how recorded Pawn Diary events become LLM prompts.

Authoritative sources:

- `Source/Pipeline/DiaryPromptPlanner.cs` selects the prompt template.
- `Source/Generation/DiaryPipelineAdapters.cs` resolves XML policy into plain prompt DTOs.
- `Source/Pipeline/DiaryEventPromptKeys.cs` builds event-prompt lookup keys.
- `1.6/Defs/DiaryPromptTemplateDefs.xml` owns prompt field order.
- `1.6/Defs/DiaryEventPromptDefs.xml` owns event prompt, event enhancement, and optional forced model.
- `1.6/Defs/DiaryInteractionGroupDefs.xml` owns source grouping, importance, combat flags, instructions, and tones.

## 1. Prompt Policy Lookup

Every saved `DiaryEvent` is projected into a `DiaryEventPayload`. The adapter builds event-prompt
candidate keys in this order:

1. Saved source defName (`payload.defName`, usually an `InteractionDef`, `TaleDef`, `IncidentDef`,
   synthetic source token, or other captured Def name).
2. Matched `DiaryInteractionGroupDef.defName`.
3. Domain classifier key (`signal` for Quest, `defName;behavior` for Ritual, `defName;category` for
   Ability, otherwise the saved source defName).
4. Broad fallback key: `Death`, `Arrival`, `QuadrumReflection`, `DayReflection`, or the recovered
   source domain.

Each candidate can match either `DiaryEventPromptDef.eventType` or `DiaryEventPromptDef.defName`.
`prompt`, `enhancement`, and `forcedModel` resolve independently: a narrow XML Def may provide only
one field and leave the others to the next broader loaded policy. Prompt Studio overrides use the
same stable key.

The shipped broad event-prompt rows are:

| Key | Used for |
|---|---|
| `Interaction` | Social-log interactions and fallback non-marker events. |
| `MentalState` | Mental breaks, social fights, insult sprees. |
| `Tale` | Non-death TaleRecorder events and Tale batches. |
| `MoodEvent` | Map-wide GameCondition mood entries. |
| `Thought` | Gained memories, thought progression, and ambient thought notes. |
| `Inspiration` | Inspiration start events. |
| `Romance` | Lover/spouse/ex relation milestones. |
| `Work` | Periodic work sampling. |
| `Hediff` | Immediate hediff and severity-progression entries. |
| `Raid` | Hostile, friendly, drop-pod, and infestation raid entries. |
| `Quest` | Completed and failed quest outcome entries; accepted is bookkeeping/event-window only. |
| `Ritual` | Finished rituals and Anomaly psychic rituals. |
| `Ability` | Successful pawn ability uses. |
| `DayReflection` | End-of-day reflection entries. |
| `QuadrumReflection` | Rare long reflections across one quadrum. |
| `Arrival` | Neutral first-page arrival notes. |
| `Death` | Neutral terminal death notes. |

There is no separate broad `ThoughtProgression` row; those entries carry `thought=` context and use
the `Thought` prompt policy.

## 2. Template Selection

`DiaryPromptPlanner.TemplateKeyFor` chooses one template per request:

| Runtime condition | Template |
|---|---|
| Title follow-up request | `Title` |
| `death_description=true` | `DeathDescription` |
| `arrival_description=true` | `ArrivalDescription` |
| Pair event and group is combat | `PairCombat` |
| Pair event with `batch=` | `PairBatched` |
| Pair event and group is important or missing | `PairImportant` |
| Pair event and group is non-important | `PairDefault` |
| Solo quadrum reflection | `SoloQuadrumReflection` |
| Solo day reflection | `SoloDayReflection` |
| Solo event with `mood_event=`, `thought=`, `inspiration=`, `work=`, or `hediff=` | `SoloInternalState` |
| Solo event with `batch=` and combat group | `SoloImportant` |
| Solo event with `batch=` and non-combat group | `SoloBatched` |
| Solo event and group is important or missing | `SoloImportant` |
| Solo event and group is non-important | `SoloDefault` |

Notes:

- Neutral death and arrival prompts disable persona, prompt enchantments, and direct-speech additions.
- Title prompts only receive the generated entry text.
- Pair prompts generate the initiator first. A recipient prompt may include the initiator entry as
  hidden continuity.
- Direct-speech instructions are appended only for normal social Interaction prompts and interaction
  batches.
- Blank values and the sentinels `none`, `n/a`, and `unknown` are omitted from rendered prompt fields.

## 3. Template Fields

Field lists are XML-owned. To keep this map compact, shared groups are named once below.

Common first-person fields:

- Identity/evidence: `event`, `pov`, first-person evidence (`what happened` for solo, `what you saw`
  for pair); pair templates also add `role` and `with`.
- Policy: `instruction`, `event prompt`, `event enhancement`, `tone`.
- Optional context families: ritual (`ritual role`, `ritual title`), ability (`ability`, `ability
  category`, `ability target`, `ability cooldown ticks`), raid (`raid arrival mode`, `raid strategy`,
  `raid points`), DLC status (`royal title`, `ideoligion role`).
- Surrounding pressure: `important context`, `setting`, `my last opener (not repeat)`.

Template differences:

| Template | Extra fields beyond the common set |
|---|---|
| `PairDefault` | `relationship` |
| `PairImportant` | `relationship`, `initiator diary (hidden context)` |
| `PairCombat` | `you`, `relationship`, `weapon`, `initiator diary (hidden context)` |
| `PairBatched` | No relationship or hidden initiator field. |
| `SoloDefault` | Common solo fields only. |
| `SoloImportant` | `you` |
| `SoloInternalState` | Common solo fields only. |
| `SoloBatched` | Common solo fields only. |
| `SoloDayReflection` | `you`; direct-speech additions disabled. |
| `SoloQuadrumReflection` | `you`, `quadrum dates`, `important entry count`; direct-speech additions disabled. |
| `DeathDescription` | `event`, `event prompt`, `event enhancement`, `deceased`, `what happened`, `death facts`, `deceased pawn`, `setting`. |
| `ArrivalDescription` | `event`, `event prompt`, `event enhancement`, `colonist`, `what happened`, `arrival facts`, `colonist pawn`, `setting`. |
| `Title` | `entry` |

## 4. Event Source Map

| Source | Capture/context markers | Prompt route |
|---|---|---|
| Social interactions | `PlayLog.Add`; saved source is the `InteractionDef`; context includes `def=`, `label=`, optional worker/thought facts. Batches add `batch=interaction`; ambient notes add `batch=ambient_day_note`. | Pair or solo templates by XML group combat/importance. Pair batches use `PairBatched`; solo batches use `SoloBatched` unless combat. Ambient notes are solo batched. |
| Mental states | `MentalStateHandler.TryStartMentalState`; `mental_state=`, `label=`, optional `target=` and `reason=`. | `SocialFighting` pair events are `PairCombat`; other breaks are solo, usually `SoloImportant` unless the group is non-important. |
| Romance relations | `Pawn_RelationsTracker.AddDirectRelation`; `romance=`, `label=`, `kind=married|lover|breakup|divorce`. | Pair prompt, normally `PairImportant`. Direct speech is not appended. |
| Tales | `TaleRecorder.RecordTale`; `tale=`, `label=`, `taleClass=`, optional attached Def facts. Tale batches add `batch=tale`. | Pair or solo templates by Tale group combat/importance; Tale batches are `SoloBatched`; death-marked tales become `DeathDescription`. |
| Death descriptions | Death-marked Tales or `Pawn.Kill` fallback; `death_description=true`, victim role/id/name, cause and surroundings facts. | Always `DeathDescription`; neutral/persona-free/enchantment-free. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction`; `arrival_description=true`, arriving pawn facts, scenario/prior faction/recruiter/kind/creepjoiner facts when available. | Always `ArrivalDescription`; neutral/persona-free/enchantment-free. |
| Mood events | `GameConditionManager.RegisterCondition`; `mood_event=`, `label=`, `mood_impact=`. | `SoloInternalState`. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory`; `thought=`, `label=`, mood/duration facts. Ambient thought notes also carry `batch=ambient_day_note`. | `SoloInternalState`; checked before `batch=`, so ambient thought notes stay internal-state prompts. |
| Thought progression | Periodic needs/thought-stage scan; `thought=`, `thought_progression=`, stage/severity/mood facts. | `SoloInternalState` using the broad `Thought` event-prompt policy. |
| Inspirations | `InspirationHandler.TryStartInspiration`; `inspiration=`, `label=`, duration/reason facts. | `SoloInternalState`. |
| Hediffs | `Pawn_HealthTracker.AddHediff` and severity scan; `hediff=`, `source=add|severity_progression`, `group=`, `mode=`, severity/stage/body-part facts. | Immediate entries use `SoloInternalState`; day-reflection-mode hediffs feed the later day reflection instead of generating immediately. |
| Work | Periodic current-job sampling; `work=`, `work_giver=`, mood/passion/skill/chore/dark-study facts. | `SoloInternalState`. |
| Raids/infestations | `IncidentWorker.TryExecute` filtered to raid workers; `raid=`, `label=`, `faction=`, `points=`, optional `arrival_mode=` and `strategy=`. | One solo entry per eligible colonist on the target map. `raidFriendly` is non-important (`SoloDefault`); hostile, drop-pod, and infestation groups are important (`SoloImportant`). Ordinary raids delay generation; drop-pod raids and infestations generate immediately. |
| Quests | `Quest.Accept`, quest UI fallback, accepted-state scan, and `Quest.End`; `quest=`, `signal=accepted|completed|failed`, label/faction/reward facts. | Accepted signals are tracked but do not generate diary pages. Completed and failed signals create one solo important shared-effort entry per eligible colonist, selecting `questCompleted` or `questFailed`. |
| Rituals | `LordJob_Ritual.ApplyOutcome` and `LordToil_PsychicRitual.RitualCompleted`; `ritual=` or `psychic_ritual=`, perspective/outcome/quality facts, plus role/title/status for non-psychic rituals. | Solo important entries fanned out to organizer/invoker, target, participants, and spectators. Psychic ritual entries intentionally omit `ritual title` and `ritual role`; invoker prompts require one unsettling speech block. |
| Abilities | Successful `Ability.Activate` overloads; `ability=`, `ability_label=`, `ability_category=`, cooldown/chance/target facts. | Solo important entries. `abilityHostile` is combat-marked for styling/policy, but solo non-batched ability prompts still use `SoloImportant`. |
| Day reflections | Sleep/rest summary trigger; `day_reflection=true`, day/highlight/candidate/filler/signal facts. | `SoloDayReflection`, generated only when XML-configured important signal kinds justify the day. |
| Quadrum reflections | Sleep/rest summary trigger near the end of a quadrum; `day_reflection=true`, `quadrum_reflection=true`, quadrum/date/highlight/candidate/signal facts. | `SoloQuadrumReflection`, generated only once per pawn/quadrum when at least `quadrumReflectionMinImportantEntries` important saved entries exist. The prompt sends at most `quadrumReflectionMaxPromptEvents` dated highlights, while preserving the full qualifying count as context. |

## 5. Field Source Glossary

| Source token | Meaning |
|---|---|
| `EventNoun` | Lower-cased event label. |
| `PovName`, `PovRole`, `OtherPawnName` | Current writer, role, and paired pawn. |
| `PovText`, `NeutralText` | Captured evidence text for first-person or neutral prompts. |
| `Instruction` | XML group instruction or event-specific instruction frozen at capture time. |
| `EventPrompt`, `EventEnhancement` | Source-level guidance from `DiaryEventPromptDef` or Prompt Studio overrides. |
| `PawnSummary` | Snapshot of the POV pawn, used by important/combat/day/quadrum-reflection templates. |
| `PromptEnchantment` | One optional live pressure line; disabled for death, arrival, and title templates. |
| `Setting`, `Tone`, `Relationship`, `LastOpener`, `Weapon` | Surroundings, XML tone variant, continuity, repeated-opener guard, and equipped weapon. |
| `HiddenInitiatorEntry` | Initiator's generated entry, only for recipient follow-up pair prompts. |
| `DeathVictim`, `DeathFacts`, `DeathPawnSummary`, `DeathSetting` | Neutral death prompt facts extracted from saved context. |
| `ArrivalPawn`, `ArrivalFacts` | Neutral arrival prompt facts extracted from saved context. |
| `EntryText` | Generated entry text passed to the title prompt. |
| `GameContext` | Direct lookup into saved `gameContext` by `contextKey`; used by ritual, ability, raid, and DLC-status fields. |
