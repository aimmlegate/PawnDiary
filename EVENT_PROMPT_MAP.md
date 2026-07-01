# Event To Prompt Mermaid Map

Current-state reference for how Pawn Diary turns observed RimWorld moments into promptable diary
items. This file describes the shipped code and XML only.

Authoritative sources:

- `Source/Ingestion/`: event signals and fan-out signals.
- `Source/Capture/Events/`: pure capture decisions and game-context formats.
- `Source/Core/DiaryGameComponent.*.cs`: dispatch, event creation, prompt queuing, scans, event
  windows, observed conditions, and day/quadrum reflections.
- `Source/Generation/DiaryPipelineAdapters.cs`: runtime/XML/localization adapter into prompt DTOs.
- `Source/Pipeline/DiaryPromptPlanner.cs`: pure template selection and prompt planning.
- `Source/Generation/PromptEnchantments.cs` and `Source/Pipeline/PromptEnchantmentPlanner.cs`:
  prompt-enchantment collection, weighting, suppression, and final selection.
- `1.6/Defs/DiaryInteractionGroupDefs.xml`: event groups, importance, combat, instructions, tones,
  batching, social promotion, hediff routing.
- `1.6/Defs/DiaryEventPromptDefs.xml`: event prompt/enhancement/forced-model rows.
- `1.6/Defs/DiaryPromptTemplateDefs.xml`: rendered prompt templates and fields.
- `1.6/Defs/DiaryPromptEnchantmentDefs.xml`, `DiaryHediffPersonaOverrideDefs.xml`,
  `DiaryEventWindowDefs.xml`, `DiaryObservedConditionDefs.xml`, `DiaryHumorCueDefs.xml`,
  `DiaryTuningDef.xml`, and `DiarySignalPolicyDefs.xml`: side-channel prompt context and weights.

## 1. End-To-End Diary Item Flow

```mermaid
flowchart TD
    subgraph Hooks["Observed events we listen to"]
        I["PlayLog.Add<br/>InteractionSignal<br/>pair, solo, interaction batch, ambient note"]
        T["MemoryThoughtHandler.TryGainMemory<br/>ThoughtSignal<br/>solo or ambient thought"]
        TP["Periodic thought-stage scan<br/>ThoughtProgressionSignal<br/>solo"]
        PR["Periodic pawn progression scan<br/>ProgressionSignal<br/>solo"]
        IN["InspirationHandler.TryStartInspiration<br/>InspirationSignal<br/>solo"]
        AB["Ability.Activate overloads<br/>AbilitySignal<br/>cooldown-sampled solo"]
        RO["Pawn_RelationsTracker.AddDirectRelation<br/>RomanceSignal<br/>pair"]
        MS["MentalStateHandler.TryStartMentalState<br/>MentalStateSignal<br/>pair social fight or solo break"]
        TA["TaleRecorder.RecordTale<br/>TaleSignal<br/>solo, pair, tale batch, or death description"]
        DF["Pawn.Kill fallback<br/>DeathFallbackSignal<br/>neutral death page when no death Tale exists"]
        HE["Pawn_HealthTracker.AddHediff<br/>HediffSignal<br/>immediate page or day-reflection signal"]
        HS["Periodic hediff severity scan<br/>HediffSignal<br/>progression page or day-reflection signal"]
        WO["Periodic current-job scan<br/>WorkSignal<br/>chance-gated solo work page"]
        RA["IncidentWorker.TryExecute<br/>RaidFanoutSignal<br/>one solo page per eligible map colonist"]
        MO["GameConditionManager.RegisterCondition<br/>MoodEventFanoutSignal<br/>one solo page per eligible map colonist"]
        QU["Quest.Accept, Quest.End, quest-state scan<br/>QuestFanoutSignal<br/>accepted bookkeeping; completed/failed pages"]
        RI["Ideology and psychic ritual completion hooks<br/>RitualFanoutSignal or PsychicRitualFanoutSignal<br/>role fan-out solo pages"]
        AR["Starting-colonist scan and Pawn.SetFaction<br/>ArrivalSignal<br/>neutral arrival page"]
        DR["Sleep/rest day flush<br/>DayReflectionSignal<br/>ordinary day reflection or rare quadrum reflection"]
        ARC["Sleep/rest day flush or major progression<br/>ArcReflectionSignal<br/>rare yearly life-arc reflection"]
    end

    subgraph Generic["Generic page and prompt side channels"]
        EW["Event-window signals<br/>Incident, Quest, ThingSpawned, Letter, ProximityLetter,<br/>VoidMonolith, PawnAge, Hediff, PrisonBreak"]
        OC["Observed-condition scan<br/>MapDanger, GameCondition, ThingPresent, PawnHediff"]
    end

    I --> Submit
    T --> Submit
    TP --> Submit
    PR --> Submit
    IN --> Submit
    AB --> Submit
    RO --> Submit
    MS --> Submit
    TA --> Submit
    DF --> Submit
    HE --> Submit
    HS --> Submit
    WO --> Submit
    RA --> Submit
    MO --> Submit
    QU --> Submit
    RI --> Submit
    AR --> Submit
    DR --> Submit
    ARC --> Submit

    Submit["DiaryEvents.Submit(signal)"] --> Dispatch["DiaryGameComponent.Dispatch"]
    Dispatch --> Guard["CanRecordGameplayEventNow guard"]
    Guard --> Dedup["recentEvents dedup check<br/>source key plus source-specific window"]
    Dedup --> Payload["Signal builds DiaryEventData payload<br/>and CaptureContext"]
    Payload --> Catalog["DiaryEventCatalog.Get(type).Decide(payload, ctx)"]
    Catalog --> Decision{"CaptureDecision"}
    Decision -- Drop --> Drop["No DiaryEvent"]
    Decision -- GenerateSolo --> Solo["AddSoloEvent"]
    Decision -- GeneratePair --> Pair["AddPairwiseEvent"]
    Decision -- GenerateSoloDeathDescription --> Death["AddSoloEvent + death_description=true"]
    Decision -- GenerateSoloArrivalDescription --> Arrival["AddSoloEvent + arrival_description=true"]
    Decision -- Batch or ambient --> Batch["RecordBatchedInteraction, RecordBatchedTale,<br/>RecordAmbientThought, or day filler"]

    EW --> EWRecord["RecordEventWindowPhase<br/>direct AddSoloEvent"]
    OC --> OCPlan["ObservedConditionPolicy.Plan<br/>saved active condition state"]
    OCPlan --> OCRecord{"recordStartEvent or recordEndEvent?"}
    OCRecord -- Yes --> OCPage["RecordObservedConditionPage<br/>direct AddSoloEvent"]
    OCRecord -- No --> OCOnly["Prompt-bias state only"]

    Solo --> Store
    Pair --> Store
    Death --> Store
    Arrival --> Store
    EWRecord --> Store
    OCPage --> Store
    Batch --> Later["Later batch flush or day reflection may create a DiaryEvent"]
    Later --> Store

    Store["DiaryEventRepository + pawn diary refs"] --> Retention["Active retention may compact old display POVs<br/>into ArchivedDiaryEntry rows"]
    Store --> Queue{"Queue generation now?"}
    Queue -- No --> Pending["Stay hot pending or delayed<br/>catch-up scan may retry"]
    Queue -- Yes --> GenRoute["EnsureGenerationQueued"]
    GenRoute --> Neutral{"Neutral arrival or death?"}
    Neutral -- Death --> DeathPrompt["BuildDeathDescriptionPromptPlan"]
    Neutral -- Arrival --> ArrivalPrompt["BuildArrivalDescriptionPromptPlan"]
    Neutral -- No --> PairCheck{"Pair event?"}
    PairCheck -- Yes --> Seq["Sequential pair flow<br/>initiator first; recipient may get hidden initiator text"]
    PairCheck -- No --> SoloPrompt["BuildInteractionPromptPlan"]
    Seq --> PromptBuild
    SoloPrompt --> PromptBuild
    DeathPrompt --> PromptBuild
    ArrivalPrompt --> PromptBuild
    PromptBuild["DiaryPipelineAdapters + DiaryPromptPlanner<br/>build system prompt, user prompt, response rules"] --> QueuePrompt["QueuePrompt stamps prompt and LLM metadata"]
    QueuePrompt --> TestMode{"Dev prompt-test mode?"}
    TestMode -- Yes --> PromptOnly["MarkPromptOnly<br/>no API call"]
    TestMode -- No --> Lane["Select API lane<br/>forced model may prefer one active lane"]
    Lane --> LLM["LlmClient.Enqueue<br/>background HTTP with failover"]
    LLM --> Apply["Main-thread result drain<br/>save text, status, model, retry/failure"]
    Apply --> Title{"generateTitles and main entry complete?"}
    Title -- Yes --> TitlePrompt["BuildTitlePromptPlan<br/>TitleMaxTokens=40"]
    TitlePrompt --> LLM
    Title -- No --> Done["Diary page visible in UI"]
    PromptOnly --> Done
    Retention --> Done
```

Important boundaries in the diagram:

- `DiaryEvents.Submit` is the bus for catalog sources. Event-window and observed-condition page
  recording bypass the bus after their own generic policy has matched; they still create normal
  `DiaryEvent` records and use the same generation path.
- Event-window pages save `event_window=` context. Observed-condition pages save
  `observed_condition=` context. Those markers are not separate prompt domains today, so generated
  pages from those systems use the saved defName plus the normal Interaction fallback unless a more
  specific group or event-prompt row is added.
- Accepted quest signals are listened to and stored for bookkeeping/event-window policy, but current
  capture policy does not generate accepted quest diary pages. Completed and failed quest signals do.

## 2. Prompt Policy And Template Selection

```mermaid
flowchart TD
    Event["Saved DiaryEvent"] --> Payload["DiaryPipelineAdapters.ToPayload"]
    Payload --> Domain["DiaryEventDomainClassifier.DomainForContext"]
    Domain --> Markers["Markers:<br/>tale, mood_event, thought, inspiration, romance,<br/>work, hediff, mental_state, raid, quest,<br/>ritual, psychic_ritual, ability, progression<br/>else Interaction"]
    Markers --> Classifier["GroupClassifierKey"]
    Classifier --> QuestKey["Quest uses signal=accepted/completed/failed"]
    Classifier --> RitualKey["Ritual uses defName plus ritual_behavior<br/>or PsychicRitual plus defName"]
    Classifier --> AbilityKey["Ability uses defName plus ability_category"]
    Classifier --> DefaultKey["All other domains use saved defName"]

    QuestKey --> Group["InteractionGroups.ClassifyDefName(domain, classifierKey)"]
    RitualKey --> Group
    AbilityKey --> Group
    DefaultKey --> Group
    Group --> CandidateKeys["DiaryEventPromptKeys.CandidateKeys"]
    CandidateKeys --> K1["1. saved source defName"]
    CandidateKeys --> K2["2. matched DiaryInteractionGroupDef.defName"]
    CandidateKeys --> K3["3. classifier key"]
    CandidateKeys --> K4["4. fallback key:<br/>Death, Arrival, ArcReflection,<br/>QuadrumReflection, DayReflection, or domain"]

    K1 --> Resolve["Resolve prompt, enhancement, forcedModel independently"]
    K2 --> Resolve
    K3 --> Resolve
    K4 --> Resolve
    Resolve --> Override{"Prompt Studio override exists<br/>for stable key?"}
    Override -- Yes --> SettingsValue["Use settings override value"]
    Override -- No --> XmlValue["Use first nonblank XML value"]
    SettingsValue --> Policy["DiaryGroupPolicy"]
    XmlValue --> Policy

    Policy --> Template{"DiaryPromptPlanner.TemplateKeyFor"}
    Template -- titleRequest --> Title["Title"]
    Template -- death_description=true --> DeathDescription["DeathDescription"]
    Template -- arrival_description=true --> ArrivalDescription["ArrivalDescription"]
    Template -- pair and combat --> PairCombat["PairCombat"]
    Template -- pair and batch= --> PairBatched["PairBatched"]
    Template -- pair and important or missing group --> PairImportant["PairImportant"]
    Template -- pair and non-important --> PairDefault["PairDefault"]
    Template -- arc_reflection=true --> SoloArcReflection["SoloArcReflection"]
    Template -- quadrum_reflection=true --> SoloQuadrumReflection["SoloQuadrumReflection"]
    Template -- day_reflection=true --> SoloDayReflection["SoloDayReflection"]
    Template -- solo internal marker --> SoloInternalState["SoloInternalState<br/>mood_event, thought, inspiration, work, hediff"]
    Template -- solo batch and combat --> SoloImportantBatch["SoloImportant"]
    Template -- solo batch and non-combat --> SoloBatched["SoloBatched"]
    Template -- solo important or missing group --> SoloImportant["SoloImportant"]
    Template -- solo non-important --> SoloDefault["SoloDefault"]
```

Current shipped event-prompt rows in `DiaryEventPromptDefs.xml`:

| Event prompt row | Key | Prompt | Enhancement | Forced model in XML |
|---|---:|---:|---:|---:|
| `DiaryEventPrompt_Interaction` | `Interaction` | yes | yes | blank |
| `DiaryEventPrompt_MentalState` | `MentalState` | yes | yes | blank |
| `DiaryEventPrompt_Tale` | `Tale` | yes | yes | blank |
| `DiaryEventPrompt_MoodEvent` | `MoodEvent` | yes | yes | blank |
| `DiaryEventPrompt_Thought` | `Thought` | yes | yes | blank |
| `DiaryEventPrompt_Inspiration` | `Inspiration` | yes | yes | blank |
| `DiaryEventPrompt_Romance` | `Romance` | yes | yes | blank |
| `DiaryEventPrompt_Work` | `Work` | yes | yes | blank |
| `DiaryEventPrompt_Hediff` | `Hediff` | yes | yes | blank |
| `DiaryEventPrompt_Raid` | `Raid` | yes | yes | blank |
| `DiaryEventPrompt_Quest` | `Quest` | yes | yes | blank |
| `DiaryEventPrompt_Ritual` | `Ritual` | yes | yes | blank |
| `DiaryEventPrompt_Ability` | `Ability` | yes | yes | blank |
| `DiaryEventPrompt_DayReflection` | `DayReflection` | yes | yes | blank |
| `DiaryEventPrompt_QuadrumReflection` | `QuadrumReflection` | yes | yes | blank |
| `DiaryEventPrompt_Progression` | `Progression` | yes | yes | blank |
| `DiaryEventPrompt_ArcReflection` | `ArcReflection` | yes | yes | blank |
| `DiaryEventPrompt_Arrival` | `Arrival` | yes | yes | blank |
| `DiaryEventPrompt_Death` | `Death` | yes | yes | blank |

The resolver supports exact defName and group rows, but the current shipped XML only defines the broad
rows above. Prompt Studio can still override prompt, enhancement, and forced model for resolved keys.

## 3. Prompt Enchantments, Writing-Style Overrides, Humor, And Forced Models

```mermaid
flowchart TD
    Queue["QueueLlmRewrite or QueueSequentialPairwiseRewrite"] --> Style["PersonaRuleFor"]
    Style --> SavedStyle["Saved pawn writing style"]
    Style --> HediffStyle["HediffPersonaOverrides.SelectOverride"]
    HediffStyle --> Priority["Highest matching priority wins<br/>not weighted-random"]
    Priority --> PersonaBlock["DiaryPersonas.RuleFor(selected or saved style)"]
    Priority --> Suppressed["Matched hediff defNames<br/>suppress duplicate prompt enchantments"]

    Queue --> Gate["DiaryPromptBuilder.ShouldResolvePromptEnchantment"]
    Gate --> TemplateFlag{"Template includePromptEnchantment?"}
    TemplateFlag -- No --> NoEnchant["No important context<br/>no humor cue"]
    TemplateFlag -- Yes --> Collect["PromptEnchantmentRuleFor"]

    Collect --> WindowCandidates["ActiveEventWindowPromptCandidates<br/>code-supported"]
    WindowCandidates --> WindowCurrent["Current XML: none active<br/>all shipped event windows keepActive=false and promptEnabled=false"]
    Collect --> ObservedCandidates["ActiveObservedConditionPromptCandidates<br/>active saved state only"]
    ObservedCandidates --> ObservedWeights["Extra candidates:<br/>MapThreat 6, ToxicFallout 4, SolarFlare 4,<br/>GrayFlesh 40 and normal multiplier 0"]
    Collect --> NormalCandidates["PromptEnchantmentCollector.Collect<br/>live hediff, capacity, RoyalTitle, IdeologyRole"]
    NormalCandidates --> ChanceGate["Per-def chance or severity-tier chance"]
    ChanceGate --> NormalWeight["Normal candidate weight formula"]
    Suppressed --> NormalCandidates
    ObservedWeights --> Multiplier["normalCandidateWeightMultiplier<br/>event-window multiplier times observed-condition multiplier"]
    Multiplier --> NormalWeight
    NormalWeight --> Pool["PromptEnchantments.RuleFor<br/>normal candidates multiplied first,<br/>then extra candidates are added"]
    Pool --> Pick["PromptEnchantmentPlanner.Build<br/>weighted pick of one candidate"]
    Pick --> ContextLine["important context: priority; condition; cues<br/>cue cap = 3"]

    Queue --> HumorGate["HumorCueFor uses same template gate"]
    HumorGate --> HumorChance["DiaryTuning.humorChance = 0.10"]
    HumorChance --> HumorTier["Light or gallows by event stakes"]
    HumorTier --> HumorPick["Weighted pick from DiaryHumorCueDefs<br/>all current cue weights = 1"]
    HumorPick --> PersonaBlock

    PersonaBlock --> System["PromptAssembler.ComposeSystem<br/>base system prompt plus style/humor block<br/>unless includePersona=false"]
    ContextLine --> User["PromptAssembler.RenderUserPrompt"]
    User --> Prompt["DiaryPromptPlan"]
    System --> Prompt
    Prompt --> Forced["forcedModelName from event prompt policy or Prompt Studio"]
    Forced --> Lane["SelectApiTarget tries matching active API lane first<br/>blank or unknown forced model is ignored"]
```

Template side effects:

| Template family | Persona/style block | Prompt enchantment | Humor cue | Direct speech instruction |
|---|---:|---:|---:|---:|
| Normal pair and solo templates | yes | yes | eligible | only when the saved event is a normal social Interaction prompt or interaction batch and template allows it |
| `SoloDayReflection` | yes | yes | eligible | no |
| `SoloQuadrumReflection` | yes | yes, but no `important context` field is present in current XML | eligible | no |
| `SoloArcReflection` | yes | yes, but no `important context` field is present in current XML | eligible | no |
| `DeathDescription` | no | no | no | no |
| `ArrivalDescription` | no | no | no | no |
| `Title` | no | no | no | no |

## 4. Current Event Groups That Affect Shape

```mermaid
flowchart LR
    subgraph Batches["Current batch groups"]
        BG1["speakup_chitchat<br/>AmbientDayNote<br/>window 60000<br/>min 8, sample 3<br/>promotion enabled"]
        BG2["strangechat<br/>AmbientDayNote<br/>window 60000<br/>min 3, sample 5<br/>promotion enabled"]
        BG3["smalltalk<br/>AmbientDayNote<br/>window 60000<br/>min 3, sample 5<br/>promotion enabled"]
        BG4["insults<br/>PairEvent batch<br/>window 7500<br/>max 8"]
        BG5["animal<br/>AmbientDayNote<br/>window 60000<br/>min 2, sample 5"]
        BG6["teaching<br/>AmbientDayNote<br/>window 60000<br/>min 2, sample 5"]
        BG7["talecombat<br/>Tale batch<br/>window 7500<br/>max 10, sample 8"]
    end

    subgraph HediffGroups["Current hediff routing groups"]
        HG1["hediffPregnancy<br/>Immediate<br/>record on add and severity step 0.34"]
        HG2["hediffLabor<br/>Immediate<br/>record on add only"]
        HG3["hediffAnomalyCompulsion<br/>Immediate<br/>record on add and severity step 0.25"]
        HG4["hediffMajorHealth<br/>DayReflection<br/>min severity 0.3, severity step 0.25,<br/>dayReflectionWeight 0.8"]
    end

    subgraph QuestGroups["Quest lifecycle groups"]
        Q1["questAccepted<br/>defaultEnabled=false<br/>bookkeeping only, no pages"]
        Q2["questCompleted<br/>enabled important fan-out pages"]
        Q3["questFailed<br/>enabled important fan-out pages"]
    end

    subgraph RaidGroups["Raid groups"]
        R1["raidFriendly<br/>non-important SoloDefault"]
        R2["raidDropPod<br/>important immediate SoloImportant"]
        R3["raidInfestation<br/>important immediate SoloImportant"]
        R4["raid catch-all<br/>important SoloImportant<br/>ordinary raids delay generation 2500 ticks"]
    end

    subgraph ProgressionGroups["Progression groups"]
        P1["progressionSkillPassion<br/>important SoloImportant<br/>passion skill milestones only"]
        P2["progressionPsylink<br/>important SoloImportant"]
        P3["progressionXenotype<br/>important SoloImportant"]
        P4["progressionRoyalTitle<br/>important SoloImportant"]
        P5["progressionOther<br/>non-important catch-all"]
    end
```

Group matching is domain-specific and first-match-wins by ascending `order`. Within a group, exact
defName matching is most precise, then prefixes, suffixes, CamelCase/underscore/digit segments, and
finally legacy substring tokens. Group `important` controls `SoloImportant`/`PairImportant` routing
and day/quadrum evidence. Group `combat` controls `PairCombat`, weapon prompt fields, and some
high-stakes humor classification.

## 5. Current Weights And Chance Formulas

```mermaid
flowchart TD
    W["Weight sources"] --> GW["Shared generationChanceWeight<br/>settings default 1, range 0..5"]
    GW --> SP["Social batch promotion<br/>chance = clamp01(PromotionChance * generationChanceWeight)"]
    GW --> Work["Work sampling<br/>chance = workBaseChance * context multipliers * generationChanceWeight"]
    GW --> Ability["Ability sampling<br/>chance = CooldownWeightedChance * generationChanceWeight"]
    W --> PE["Prompt enchantments<br/>one weighted candidate chosen for important context"]
    W --> OCW["Observed-condition prompt candidates<br/>extra weighted candidates and normal multipliers"]
    W --> DH["Day and quadrum reflections<br/>weighted highlight selection without replacement"]
    W --> Humor["Humor cues<br/>0.10 gate, then equal-weight cue pick"]
```

Source recording weights:

| Source | Current formula or value |
|---|---|
| Shared generation chance | `PawnDiarySettings.generationChanceWeight`, default `1`, clamped `0..5`. |
| SpeakUp chatter promotion | `base 0.005 + bonuses`, capped `0.08`, then multiplied by shared generation chance. Bonuses: strong opinion `+0.025` at abs opinion `>=40`; opinion asymmetry `+0.025` at delta `>=40`; low food/rest/joy `+0.025` at `<=0.25`; low mood `+0.025` at `<=0.25`. |
| Strange chat promotion | `base 0.04 + bonuses`, capped `0.6`, then multiplied by shared generation chance. Bonuses: strong opinion `+0.25`; opinion asymmetry `+0.2`; low need `+0.2`; low mood `+0.2`; same thresholds as above. |
| Small talk promotion | Same as strange chat: `base 0.04`, cap `0.6`, bonuses `+0.25/+0.2/+0.2/+0.2`, then shared generation chance. |
| Work sampling | Scan every `2500` ticks. Chance starts at `0.08`; passion multiplier `1.4`; negative chore/low skill multiplier `1.2`; dark study multiplier `1.5`; recent different work multiplier `0.5`; same work cooldown `180000` ticks; then shared generation chance and clamp. Social/violent work types are ignored. |
| Pawn progression | Scan every `2500` ticks. Passion skills emit only when reaching configured milestones `8/12/16/20`; first scan baselines. Psylink hediff defNames are XML string matchers; xenotype and royal-title reads go through DLC-safe `DlcContext`. Major arc triggers use XML severity/list policy: default threshold `90`, psylink severity `level / 6 * 100`, and `Sanguophage` as the default major xenotype defName. |
| Ability sampling | `min 0.03`, `max 0.75`, reference cooldown `60000` ticks. `CooldownWeightedChance = min + (max - min) * cooldown / (cooldown + reference)`, then shared generation chance and clamp. Dedup `300` ticks. |
| Ordinary raid generation delay | `2500` ticks. Drop-pod raids and infestations bypass the delay. |
| Day reflection highlights | Max `3`. Important event weight is `1` for combat and `0.7` for other important events. Hediff day signal default `0.8`. Opinion shift weight is `0.6 * min(2, abs(delta)/15)`. Filler weight is `0.15`, only when at least two filler moments exist. Weighted selection is without replacement with floor `0.0001`; if selected highlights contain no important signal, the strongest important candidate replaces the lightest selected highlight. |
| Quadrum reflection | Enabled. Due date is deterministic per pawn/quadrum inside final `3` days. Requires `6` important entries. Sends at most `8` weighted highlights. Max response tokens `350`. Highlight weights reuse combat `1` and other important `0.7`. |
| Arc reflection | Enabled. One forced yearly entry after day `45` when enough memories exist; optional second major-event entry after `30` days. A forced attempt that has too few memories backs off for `60000` ticks before rescanning. Samples up to `8` hot/archive diary memories, de-duplicates by event id, prefers same-year entries, excludes reflections/death descriptions/recently used ids, weights XML high-stakes defName tokens, and caps repeated domain/group memories. Max response tokens `420`. |
| Humor cues | Base gate `0.10`. High-stakes events use gallows cues; other events use light cues. All current humor cue XML rows have weight `1`. |

Prompt-enchantment selection formula:

- Hediff candidate chance: severity tier `frequency` or `chance` if a tier matches; otherwise Def
  `frequency` when nonnegative; otherwise Def `chance`; clamped `0..1`.
- Hediff candidate weight: `weight * severity * LiveSeverityWeight`, with tier `weight`/`severity`
  overriding the Def when nonnegative.
- `LiveSeverityWeight = max(0.1, 1 + clamp(severity,0,2)*0.5 + lifeThreateningBonus + bleedingBonus
  + clamp(painOffset,0,1) + clamp(-SummaryHealthPercentImpact,0,1))`, where life-threatening adds
  `1.5` and bleeding adds `clamp(bleedRate,0,2)*0.5`.
- Capacity candidate weight: `weight * severity * (1 + clamp01(1 - capacityLevel) * 2)`.
- RoyalTitle and IdeologyRole candidate weight: `weight * severity`, and they only enter the pool for
  important events.
- Normal candidates are multiplied by active event-window and observed-condition
  `normalPromptWeightMultiplier` values before extra event-window/observed-condition candidates are
  added.
- The planner picks one candidate by `candidate.weight / totalPositiveWeight`, then formats priority,
  condition, live impact cues, and configured cues. Current cue cap is `3`.

Current prompt-enchantment tuning thresholds:

| Threshold | Value |
|---|---:|
| Minor hediff severity | `0.05` |
| Moderate hediff severity | `0.25` |
| Major hediff severity | `0.50` |
| Critical hediff severity | `0.75` |
| Clouded consciousness below | `0.55` |
| Fading consciousness below | `0.35` |
| Barely conscious below | `0.20` |
| Max impact cues | `3` |
| First-person generation consciousness floor | `0.11` |

Current prompt-enchantment defs:

| Def | Source or match | Chance | Weight | Severity | Extra gate |
|---|---|---:|---:|---:|---|
| `DiaryEnchant_RoyalTitle` | `RoyalTitle` | `0.22` | `0.55` | `1` | important events only |
| `DiaryEnchant_IdeologyRole` | `IdeologyRole` | `0.22` | `0.55` | `1` | important events only |
| `DiaryEnchant_ConsciousnessClouded` | `Capacity:Consciousness` | `1` | `2.2` | `1.2` | `0.35 <= level < 0.55` |
| `DiaryEnchant_ConsciousnessFading` | `Capacity:Consciousness` | `1` | `3.2` | `1.5` | `0.20 <= level < 0.35` |
| `DiaryEnchant_ConsciousnessBarelyAwake` | `Capacity:Consciousness` | `1` | `5` | `2` | `level < 0.20` |
| `DiaryEnchant_FeverishBody` | `Flu, Malaria, Plague, GutWorms, MuscleParasites, FoodPoisoning, ToxicBuildup, WoundInfection` | `0.65` | `1.2` | `1.2` | min severity `0.05`; severity tiers |
| `DiaryEnchant_BloodLossUrgency` | `BloodLoss` | `0.75` | `1.4` | `1.6` | min severity `0.05`; severity tiers |
| `DiaryEnchant_AlcoholHigh` | `AlcoholHigh` | `0.55` | `0.9` | `1` | severity tiers |
| `DiaryEnchant_Hangover` | `Hangover` | `0.6` | `0.9` | `1.1` | severity tiers |
| `DiaryEnchant_AmbrosiaHigh` | `AmbrosiaHigh` | `0.45` | `0.8` | `0.9` |  |
| `DiaryEnchant_GoJuiceHigh` | `GoJuiceHigh` | `0.65` | `1.1` | `1.25` |  |
| `DiaryEnchant_LuciferiumHigh` | `LuciferiumHigh` | `0.45` | `1` | `1.2` |  |
| `DiaryEnchant_LuciferiumDependency` | `LuciferiumAddiction` | `0.75` | `1.2` | `1.4` | min severity `0.05`; severity tiers |
| `DiaryEnchant_ChemicalCraving` | alcohol, ambrosia, smokeleaf, psychite, wake-up, go-juice addiction/withdrawal hediffs | `0.55` | `1` | `1.2` | min severity `0.05`; severity tiers |
| `DiaryEnchant_FlakeHigh` | `FlakeHigh` | `0.65` | `1` | `1.15` |  |
| `DiaryEnchant_PsychiteTeaHigh` | `PsychiteTeaHigh` | `0.45` | `0.8` | `0.95` |  |
| `DiaryEnchant_YayoHigh` | `YayoHigh` | `0.65` | `1` | `1.15` |  |
| `DiaryEnchant_SmokeleafHigh` | `SmokeleafHigh` | `0.55` | `0.8` | `1` |  |
| `DiaryEnchant_PsychicHangover` | `PsychicHangover` | `0.7` | `1.1` | `1.25` | severity tiers |
| `DiaryEnchant_Blindness` | `Blindness` | `0.75` | `1` | `1.2` |  |
| `DiaryEnchant_MemoryDecay` | `Dementia, Alzheimers, CrumblingMind` | `0.8` | `1.2` | `1.3` |  |
| `DiaryEnchant_TraumaSavant` | `TraumaSavant` | `0.75` | `1.1` | `1.15` |  |
| `DiaryEnchant_ResurrectionPsychosis` | `ResurrectionPsychosis` | `0.85` | `1.4` | `1.5` | severity tiers |
| `DiaryEnchant_Joywire` | `Joywire` | `0.55` | `0.9` | `1.1` |  |
| `DiaryEnchant_ParalyticAbasia` | `Abasia` | `0.75` | `1.1` | `1.2` |  |
| `DiaryEnchant_Mindscrew` | `Mindscrew` | `0.65` | `1.2` | `1.3` |  |
| `DiaryEnchant_Pregnancy` | `Pregnant, PregnantHuman` | `0.45` | `0.9` | `1` |  |
| `DiaryEnchant_HemogenCraving` | `HemogenCraving` | `0.75` | `1.2` | `1.35` | severity tiers |
| `DiaryEnchant_PsychicBondTorn` | `PsychicBondTorn` | `0.8` | `1.3` | `1.4` |  |
| `DiaryEnchant_BlissLobotomy` | `BlissLobotomy` | `0.65` | `1` | `1.2` |  |
| `DiaryEnchant_RevenantHypnosis` | `RevenantHypnosis` | `0.85` | `1.5` | `1.6` |  |
| `DiaryEnchant_CubeInterest` | `CubeInterest` | `0.55` | `1` | `1.1` |  |
| `DiaryEnchant_CubeWithdrawal` | `CubeWithdrawal` | `0.85` | `1.4` | `1.5` | severity tiers |
| `DiaryEnchant_CubeRage` | `CubeRage` | `1` | `1.8` | `1.8` |  |
| `DiaryEnchant_VoidShockOrTouched` | `VoidShock, VoidTouched` | `0.85` | `1.4` | `1.5` |  |
| `DiaryEnchant_CorpseTorment` | `CorpseTorment` | `0.85` | `1.4` | `1.5` |  |
| `DiaryEnchant_Inhumanized` | `Inhumanized` | `1` | `2.2` | `2` | `visibleOnly=false` |
| `DiaryEnchant_FleshMutation` | `Tentacle, FleshTentacle, FleshWhip` | `0.7` | `1.1` | `1.2` |  |

Severity-tier overrides currently exist for `FeverishBody`, `BloodLossUrgency`, `AlcoholHigh`,
`Hangover`, `LuciferiumDependency`, `ChemicalCraving`, `PsychicHangover`,
`ResurrectionPsychosis`, `HemogenCraving`, and `CubeWithdrawal`. Blank tier fields inherit the Def
value.

Current hediff writing-style overrides:

| Override | Forced writing style | Priority | Match | Notes |
|---|---|---:|---|---|
| `HediffPersonaOverride_TraumaSavant` | `DiaryPersona_TraumaSavantSilent` | `110` | `TraumaSavant` | Highest current priority; visible hediff required by default. |
| `HediffPersonaOverride_Inhumanized` | `DiaryPersona_InhumanizedVoid` | `100` | `Inhumanized` | `visibleOnly=false`. |
| `HediffPersonaOverride_CrumbledMind` | `DiaryPersona_CrumbledMindCollapse` | `40` | `CrumbledMind` | Visible hediff required by default. |
| `HediffPersonaOverride_BlissLobotomy` | `DiaryPersona_BlissLobotomyHaze` | `35` | `BlissLobotomy` | Visible hediff required by default. |
| `HediffPersonaOverride_Mindscrew` | `DiaryPersona_MindscrewPain` | `25` | `Mindscrew` | Visible hediff required by default. |
| `HediffPersonaOverride_Joywire` | `DiaryPersona_JoywireHaze` | `20` | `Joywire` | Visible hediff required by default. |

Current observed-condition prompt candidates:

| Condition | Enabled | Observer | Scope | Prompt weight | Normal multiplier | Records page? |
|---|---:|---|---|---:|---:|---|
| `MapThreatActive` | yes | `MapDanger` | Map | `6` | `1` | no |
| `ToxicFalloutActive` | yes | `GameCondition` | Map | `4` | `1` | no |
| `SolarFlareActive` | yes | `GameCondition` | Map | `4` | `1` | no |
| `AnomalyGrayFleshEvidence` | yes | `ThingPresent` | Map | `40` | `0` | start page to map colonists, `extremeDark` cue |
| `MetalhorrorEmergence` | no | `PawnHediff` | Pawn | `60` | `0` | no; disabled and empty matchers |

Current event-window prompt candidates: none in shipped XML. The six current event windows
(`VoidMonolithDiscovery`, `VoidMonolithActivation`, `Birthday`, `HeartAttack`, `PrisonBreak`,
`AncientDanger`) all set `keepActive=false`, `promptEnabled=false`, and `promptWeight=0`; they can
record one-shot pages but do not bias later prompts while active.

## 6. Rendered Prompt Contents

```mermaid
flowchart TD
    Plan["DiaryPromptPlan"] --> System["systemPrompt<br/>template system prompt plus writing-style/humor block"]
    Plan --> User["userPrompt<br/>structured label: value lines plus final instruction"]
    User --> Fields["Template field list from DiaryPromptTemplateDefs.xml"]
    Fields --> Skip["PromptAssembler skips blank values<br/>and sentinels none, n/a, unknown"]
    Fields --> Common["Common first-person fields:<br/>event, pov, raw evidence, instruction,<br/>event prompt, event enhancement,<br/>important context, setting, tone, last opener"]
    Fields --> PairFields["Pair extras:<br/>role, with, relationship,<br/>hidden initiator diary for PairImportant and PairCombat"]
    Fields --> CombatFields["Combat extras:<br/>you, weapon"]
    Fields --> SourceFacts["Context facts:<br/>quest, ritual, ability, raid,<br/>progression, royal title, ideoligion role"]
    Fields --> Boundary["Neutral arrival/death:<br/>event prompt, enhancement, neutral facts,<br/>pawn summary, setting; no persona/enchantment"]
    Fields --> Reflection["Reflections:<br/>day selected highlights and pawn summary<br/>quadrum date range and important count<br/>arc selected year memories and cadence fields"]
    Fields --> Title["Title:<br/>entry text only"]
```

Current template keys:

| Key | Trigger | Current special behavior |
|---|---|---|
| `PairDefault` | Pair, non-combat, non-batched, non-important group | Relationship field; style/enchantment allowed. |
| `PairImportant` | Pair, non-combat, non-batched, important or missing group | Relationship plus hidden initiator field; style/enchantment allowed. |
| `PairCombat` | Pair and combat group, including MentalState domain | Pawn summary, weapon, hidden initiator field; style/enchantment allowed. |
| `PairBatched` | Pair and `batch=` marker, unless combat | No relationship or hidden initiator field; style/enchantment allowed. |
| `SoloDefault` | Solo, non-batched, non-internal, non-important group | Style/enchantment allowed. |
| `SoloImportant` | Solo important or solo batched combat | Pawn summary; style/enchantment allowed. |
| `SoloInternalState` | Solo with `mood_event=`, `thought=`, `inspiration=`, `work=`, or `hediff=` | Internal-state facts; style/enchantment allowed. |
| `SoloBatched` | Solo with `batch=`, non-combat | Batched evidence; style/enchantment allowed. |
| `SoloDayReflection` | `day_reflection=true` | Direct speech disabled; style/enchantment allowed. |
| `SoloQuadrumReflection` | `quadrum_reflection=true` | Direct speech disabled; `maxTokens=350`; current fields omit `important context`. |
| `SoloArcReflection` | `arc_reflection=true` | Direct speech disabled; `maxTokens=420`; selected memory evidence and arc cadence fields. |
| `DeathDescription` | `death_description=true` | Neutral, style-free, enchantment-free. |
| `ArrivalDescription` | `arrival_description=true` | Neutral, style-free, enchantment-free. |
| `Title` | title follow-up | Style-free, enchantment-free, `TitleMaxTokens=40`. |
