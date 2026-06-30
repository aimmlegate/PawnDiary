# Pawn Arc Reflection Implementation Plan

Status: ready for implementation.

This plan extends Pawn Diary with rare pawn life-arc reflections while avoiding a separate
"history facts" database. The existing diary pages remain the history layer. New gameplay coverage
is added as ordinary diary entries first; the annual arc entry samples those entries later.

## Core Decisions

- Do not build a separate pawn-history or fact-store layer.
- Regular diary entries are the source of truth for pawn history.
- Add only small persisted scheduling/progression state where runtime detection needs it.
- Keep daily and quadrum reflections, but keep their job distinct from the arc entry.
- Use one global per-pawn arc cooldown/cadence, not per-category cooldowns.
- Generate about one arc entry per pawn per game year, with an optional second only for a major event.
- Add a forced yearly fallback so eligible pawns do not go a whole year without an arc chapter.
- Skill progression entries only fire for skills where the pawn has passion.
- Psylink level gains are progression events and should be covered explicitly.
- Devtools must be able to preview and simulate daily, quadrum, progression, and arc prompts.

## Existing Coverage Check

| Topic | Current coverage | MVP action |
|---|---|---|
| Daily reflection | Implemented in `DiaryGameComponent.DaySummary.cs` using `DayReflectionEventData`. | Keep. Improve prompt wording only if needed. |
| Quadrum reflection | Implemented as `QuadrumReflection` through the day-reflection source. | Keep. Add dev prompt fixture and diagnostics. |
| Arrival/backstory | Starting arrival context already includes scenario and backstory detail. | Reuse as context. Do not duplicate in arc state. |
| Love, marriage, breakups | Covered by `Romance` relation changes and social logs. | Reuse. |
| Deaths | Covered by tale-backed death entries and fallback death description. | Reuse. |
| Health effects | Hediff domain, health tales, pregnancy/labor, heart attack event window. | Reuse; maybe tune event-window importance. |
| Operations/surgery | `DidSurgery` and health tales exist. | Reuse. |
| Work | Sampled work entries exist, including passionate work. | Reuse. |
| Skill progression | Only vanilla master-skill tales are covered; non-passion master skill currently matches XML too. | Add passion-only skill milestone scanner; suppress non-passion master tale if needed. |
| Royal title | Current title is in pawn summary through `DlcContext.RoyalTitle`. | Add "title changed" progression entry. |
| Xenotype/sanguophage | Current xenotype is in pawn summary through `DlcContext.Xenotype`. | Add "xenotype changed" progression entry. |
| Traits | Used for persona/style and decoration snapshots. No gained/lost diary event. | Skip MVP unless an easy stable hook appears. |
| Persona override hediffs | Existing temporary persona override policy. | Reuse as style/context; no arc state. |
| Void monolith | Discovery and activation event windows exist. | Ensure they count as important arc/quadrum memories. |
| Mechanitor progression | Generic quests/raids/thoughts may cover parts, but no dedicated boss progression. | Skip MVP or cover via XML event-window if a stable signal/quest def is identified. |
| Psylink levels | Psycast ability use exists, but psylink level gain does not. | Add explicit psylink progression scanner. |
| Devtools | Event test panel, prompt suite, day reflection trigger already exist. | Extend with quadrum, progression, arc fixtures, and diagnostics. |

## User-Facing Entry Types

### Regular Diary Entries

These are ordinary entries that become source material for quadrum and yearly arc reflections:

- Passion skill milestone.
- Psylink level gained.
- Royal title changed.
- Xenotype changed, including sanguophage.

These should feel like normal diary moments, not arc summaries.

### Daily Reflection

Job: what today left in the pawn's head tonight.

Rules:

- Uses only today/recent day highlights.
- May include normal entries from the day.
- Must not summarize every event.
- Does not reset arc cooldown.

### Quadrum Reflection

Job: a seasonal pattern over about 15 days.

Rules:

- Uses current quadrum important entries.
- Excludes daily, quadrum, and arc reflections.
- Does not reset arc cooldown.
- Should connect highlights, not list them.

### Yearly Arc Reflection

Job: rare life chapter about who the pawn is becoming.

Rules:

- Samples existing diary pages from the year.
- Uses current pawn summary and optional arrival/backstory context.
- Excludes daily/quadrum/arc reflections by default.
- Does not require a separate fact store.
- Hard capped to one or two entries per game year per pawn.

## New Source Architecture

Use the existing Event Catalog pattern.

### Progression Source

Add one generic progression source instead of separate event types for skill/title/xenotype/psylink.

Files:

- `Source/Capture/DiaryEventType.cs`
  - Add `Progression`.
- `Source/Capture/Events/ProgressionEventData.cs`
  - Plain DTO and pure `Decide`.
- `Source/Capture/Specs/ProgressionEventSpec.cs`
  - Thin spec wrapper.
- `Source/Capture/Catalog/DiaryEventCatalog.cs`
  - Register the spec.
- `Source/Ingestion/Sources/ProgressionSignal.cs`
  - Emits one solo diary event.
- `Source/Pipeline/DiaryEventDomainClassifier.cs`
  - Add `progression=` marker and `Progression` domain.
- `Source/Defs/InteractionGroups.cs`
  - Add `GroupDomain.Progression`.

Suggested `ProgressionEventData` fields:

```csharp
public string DefName;
public string Kind;
public string Label;
public string PreviousValue;
public string NewValue;
public string Context;
public bool AlreadyRecorded;
```

Suggested decision:

```text
drop if data/context missing
drop if defName or kind empty
drop if not eligible/user enabled/signal enabled
drop if AlreadyRecorded
otherwise GenerateSolo
```

Suggested context markers:

```text
progression=SkillMilestone;
progression_kind=skill;
skill=Construction;
skill_level=12;
previous_skill_milestone=8;
passion=major
```

```text
progression=PsylinkLevel;
progression_kind=psylink;
psylink_level=4;
previous_psylink_level=3
```

```text
progression=XenotypeChanged;
progression_kind=xenotype;
previous_xenotype=Baseliner;
xenotype=Sanguophage;
xenotype_def=Sanguophage;
sanguophage=true
```

```text
progression=RoyalTitleChanged;
progression_kind=royal_title;
previous_title=Yeoman;
title=Knight
```

### XML Progression Groups

Add groups to `1.6/Defs/DiaryInteractionGroupDefs.xml`:

- `progressionSkillPassion`
- `progressionPsylink`
- `progressionXenotype`
- `progressionRoyalTitle`
- `progressionOther`

Each group should be important by default except possibly `progressionOther`.

Add prompt policy to `1.6/Defs/DiaryEventPromptDefs.xml`:

- Broad `Progression`.
- Specific rows for skill, psylink, xenotype, and royal title if needed.

Add progression context fields to the solo prompt templates in
`1.6/Defs/DiaryPromptTemplateDefs.xml`:

- `progression_kind`
- `skill`
- `skill_level`
- `passion`
- `psylink_level`
- `previous_psylink_level`
- `xenotype`
- `previous_xenotype`
- `sanguophage`
- `title`
- `previous_title`

Keep field labels schema-like and English, matching existing prompt schema carve-outs.

## Passion Skill Milestones

### Scope

Only record skill milestones for passion skills.

Record when:

```text
pawn.skills exists
skill.passion != Passion.None
skill.Level reaches a configured milestone
milestone > highest recorded milestone for this pawn/skill
```

Default milestones:

```text
8, 12, 16, 20
```

Level 20 should still be recorded for passion skills. Non-passion level 20 should not create a new
progression entry.

### Existing Master-Skill Tale

Current XML matches both:

- `GainedMasterSkillWithPassion`
- `GainedMasterSkillWithoutPassion`

MVP options:

1. Leave vanilla tale coverage alone and rely on the new passion scanner for richer entries.
2. Prefer stricter behavior: add an earlier XML group for `GainedMasterSkillWithoutPassion` with
   `defaultEnabled=false`, or remove it from the important `talework` group.

Preferred MVP: suppress non-passion master-skill diary generation so the user rule stays clear.

### Runtime State

This state is not a history layer; it is scanner bookkeeping.

Add a small progression state under `PawnDiaryRecord`:

```csharp
public PawnProgressionState progressionState;
```

Suggested shape:

```csharp
public class PawnProgressionState : IExposable
{
    public List<SkillMilestoneState> skillMilestones;
    public int highestPsylinkLevelRecorded;
    public string lastObservedXenotypeDefName;
    public string lastObservedXenotypeLabel;
    public string lastObservedRoyalTitle;
    public bool baselineProgressionOnNextScan;
}

public class SkillMilestoneState : IExposable
{
    public string skillDefName;
    public int highestMilestone;
}
```

Use lists rather than `Dictionary` for simpler save compatibility and easier normalization.

### Baseline Behavior

On old saves or first scan:

- Snapshot current passion skill milestones.
- Do not emit catch-up entries.
- After baseline, only future increases emit.

This prevents loading an old colony from producing a burst of skill pages.

### Pure Helper

Add a pure helper under `Source/Pipeline` or `Source/Capture`:

```csharp
ProgressionMilestonePolicy
```

It should accept plain values:

```text
current level
has passion
configured milestones
previous recorded milestone
baseline mode
```

It returns:

```text
new highest milestone
whether to emit
which milestone to emit
```

## Psylink Level Progression

### Scope

Record every psylink level increase. There are only six levels and each is narratively important.

### DLC Safety

Do not require Royalty.

Implementation should:

- scan live hediffs defensively
- match hediff defNames by string from XML/tuning
- no-op when Royalty content is absent
- avoid `DefDatabase<T>.GetNamed` for DLC defs

Default hediff defName matcher:

```text
PsychicAmplifier
```

### Level Detection

Implement a guarded helper:

```csharp
TryGetPsylinkLevel(Hediff hediff, out int level)
```

Detection order:

1. If the hediff exposes an integer `level` or `Level` field/property, use it.
2. Else use a known hediff-level interface/type only if already safe in this runtime.
3. Else fallback to `CurStageIndex + 1` when stages are available.
4. Clamp to `1..6`.
5. If no reliable level can be found, return false.

The exact runtime shape should be verified in-game with devtools before enabling the scanner by
default.

### State

Persist only:

```text
highestPsylinkLevelRecorded
```

Baseline current psylink on first scan/load. Emit only future increases.

## Royal Title Progression

### Scope

Record when the pawn's highest royal title changes after baseline.

Use `DlcContext` as the only home for DLC reads.

Add or extend `DlcContext` accessors if needed:

```csharp
RoyalTitleLabel(Pawn pawn)
RoyalTitleDefName(Pawn pawn)
```

For MVP, label alone is acceptable for prompt text, but a defName is better for dedup/stability.

Baseline current title on first scan. Do not emit a title page for starting titled pawns.

## Xenotype And Sanguophage Progression

### Scope

Record when the pawn's xenotype changes after baseline.

Use `DlcContext` only for gene/xenotype reads.

Add:

```csharp
XenotypeLabel(Pawn pawn)
XenotypeDefName(Pawn pawn)
```

Context should include:

```text
xenotype=<label>
xenotype_def=<defName>
previous_xenotype=<label>
sanguophage=true/false
```

Set `sanguophage=true` when the xenotype defName matches `Sanguophage` case-insensitively.

Do not track individual gene lists for MVP.

## Trait Progression

Skip for MVP.

Reason:

- Trait add/remove hooks are less central to the current architecture.
- Traits already affect persona/style.
- Trait changes are rarer but mod-sensitive.

Revisit later only if there is a stable base-game hook or a safe scanner with small state.

## Mechanitor Progression

Skip dedicated mechanitor progression for MVP.

Use existing coverage:

- quests
- raids
- thoughts
- ability use
- tales

Possible later approach:

- XML event-window rules for stable mechanitor boss quest/incident/letter defNames.
- No C# DLC type references unless guarded.

## Event Window Importance

Some event-window entries are already generated but may classify as generic `Interaction` unless
handled explicitly.

MVP should ensure these are important for quadrum/arc sampling:

- `VoidMonolithDiscovery`
- `VoidMonolithActivation`
- `HeartAttack`
- `Birthday`
- `AncientDanger`
- `PrisonBreak`

Implementation options:

1. Add specific `Interaction` XML groups matching those event-window defNames before the catch-all.
2. Or update arc/quadrum candidate selection to treat `event_window=` contexts as important.

Preferred MVP: add XML groups. This keeps policy visible and avoids hidden hardcoded importance.

## Arc Reflection Source

Add a dedicated source instead of overloading `DayReflection`.

Files:

- `Source/Capture/DiaryEventType.cs`
  - Add `ArcReflection`.
- `Source/Capture/Events/ArcReflectionEventData.cs`
- `Source/Capture/Specs/ArcReflectionEventSpec.cs`
- `Source/Capture/Catalog/DiaryEventCatalog.cs`
- `Source/Ingestion/Sources/ArcReflectionSignal.cs`
- `Source/Core/DiaryGameComponent.ArcReflection.cs`
- `Source/Pipeline/ArcReflectionSchedulePolicy.cs`
- `Source/Pipeline/ArcReflectionMemorySelector.cs`

Suggested def token:

```text
PawnArcReflection
```

Suggested context:

```text
arc_reflection=true;
arc_year=5504;
forced=true;
selected_memories=6;
candidate_memories=18;
entries_this_year=0
```

## Arc Cadence

RimWorld year is 60 days.

Default tuning:

```text
arcReflectionEnabled = true
arcReflectionMaxEntriesPerYear = 1
arcReflectionAllowSecondMajorEntry = true
arcReflectionSecondEntryMinGapDays = 30
arcReflectionMajorSeverityThreshold = 90
arcReflectionForceAfterYearDay = 45
arcReflectionMinMemoriesPreferred = 4
arcReflectionMinMemoriesForced = 3
arcReflectionMaxMemories = 8
arcReflectionRecentlyUsedMemoryCap = 16
```

Behavior:

```text
If entries_this_year == 0 and day_of_year >= force day:
    force one arc entry for eligible pawn.

Else if a major event occurs and no entry this year:
    allow an arc entry if schedule permits.

Else if a major event occurs, one entry already exists this year,
and second major entries are enabled,
and min gap has passed:
    allow second arc entry.

Else:
    do not generate arc entry.
```

Never generate more than two arc entries per pawn per year.

The forced annual entry ignores normal cooldown because its job is to guarantee one chapter.

## Arc Schedule State

Add to `PawnDiaryRecord`:

```csharp
public PawnArcScheduleState arcSchedule;
```

Suggested shape:

```csharp
public class PawnArcScheduleState : IExposable
{
    public int lastArcEntryTick = -1;
    public int lastArcEntryYear = int.MinValue;
    public int arcEntriesThisYear;
    public int forcedArcYear = int.MinValue;
    public List<string> recentlyUsedEventIds = new List<string>();
}
```

This is scheduling and repetition control only. It is not a history layer.

Normalize on load:

- null object becomes empty object
- null list becomes empty list
- cap `recentlyUsedEventIds`
- if loaded year differs from current year, reset `arcEntriesThisYear`

## Arc Trigger Points

### Annual Forced Trigger

Run from the same sleep/rest/day-summary path:

```text
FlushDaySummaryForPawn
    TryFlushArcReflectionForPawn
    TryFlushQuadrumReflectionForPawn
    normal day reflection
```

If arc emits, skip quadrum and day reflection for that rest moment.

### Major Event Trigger

After a major regular event is recorded, call a lightweight:

```csharp
ConsiderArcReflectionAfterEvent(DiaryEvent event, Pawn pawn)
```

MVP major events:

- xenotype changed to sanguophage
- psylink level 6
- major royal title increase, if severity policy marks it high
- possibly severe event-window entries like void activation

This method should:

- check schedule policy
- collect/sample memory candidates
- emit `ArcReflectionSignal` only if allowed

If blocked by cadence, do nothing. The event remains a normal diary entry and can be sampled later.

## Arc Memory Selection

No facts. Select existing diary pages.

Candidate sources:

- active hot `DiaryEvent` rows
- archived `ArchivedDiaryEntry` rows through `archive.EntriesForPawn(pawnId)`

Candidate filters:

- same pawn
- same game year preferred
- exclude daily reflection
- exclude quadrum reflection
- exclude arc reflection
- exclude death description unless implementing final-life arc later
- exclude prompt-only dev rows unless in dev fixture
- exclude recently used event ids when possible

Use generated diary text when available:

```text
generatedText -> text -> title/label fallback
```

Keep snippets short. The model should not receive whole pages.

Suggested snippet cap:

```text
220 characters per memory
```

Selection:

```text
min preferred: 4
min forced: 3
max: 8
weighted random without replacement
max same domain/group: 2
sort selected memories chronologically before prompt assembly
```

Simple weights:

```text
base = 10
important +20
same quadrum +10
has generated text +5
progression source +20
romance/death/health/void/high-danger +15
recently used -100
```

Move tunable weights to XML if implementation grows beyond these defaults.

## Arc Prompt

Add a new template:

```text
SoloArcReflection
```

System prompt:

```text
Write 5-9 first-person diary sentences as a rare life-chapter reflection.
Use the pawn's writing style.
The supplied memories are material, not a checklist.
Do not summarize each memory one by one.
Let one or two memories dominate emotionally and let others affect voice, fear, pride, grief, or hope.
Connect who the pawn was, what changed this year, and what they seem to be becoming.
Output only diary text.
```

Fields:

- event
- pov
- arc year
- selected memories
- instruction
- you
- current setting
- my last opener (not repeat)
- event prompt
- event enhancement

Raw event text should be a short memory list, for example:

```text
Selected memories from this year:
- 3 Aprimay: survived a heart attack.
- 8 Aprimay: reached Medical 12, a passion skill.
- 2 Jugust: gained psylink level 3.
- 9 Decembary: became a sanguophage.
```

Final instruction should emphasize human diary imitation:

```text
Write this like a real private diary entry, not a report, log, biography, or summary.
Do not mention every memory.
Use natural narrative links to earlier entries: "I keep thinking back to...", "It feels strange that...",
"I used to think...", only if it fits the pawn's voice.
```

## Daily And Quadrum Prompt Adjustment

Keep both daily and quadrum reflections.

Do not send arc history to daily/quadrum for MVP. That risks repetition.

Instead:

- daily sees only day highlights and current pawn summary
- quadrum sees dated quadrum highlights and current pawn summary
- arc sees sampled year memories and current pawn summary

Prompt wording should discourage log summaries:

- use "memories" and "what stayed with the pawn"
- avoid "summarize"
- tell the model not to cover every item
- tell it to write as private diary prose

## Devtools Test Suite

Extend existing devtools, do not create a separate framework.

Existing integration points:

- `Source/Dev/PawnDiaryDebugActions.cs`
- `Source/Core/DiaryGameComponent.PromptTestSuite.cs`
- `ClearPromptSuiteForDev`
- prompt test mode cards tagged with `dev_prompt_suite=true`

### Prompt Fixtures To Add

Add to `SuiteEntries`:

- `QuadrumReflection`
- `ProgressionSkillPassion`
- `ProgressionSkillNoPassionIgnored`
- `ProgressionPsylink`
- `ProgressionXenotypeSanguophage`
- `ProgressionRoyalTitle`
- `ArcReflectionForced`
- `ArcReflectionMajorEvent`
- `ArcReflectionCooldownBlocked`

`ProgressionSkillNoPassionIgnored` should be a diagnostics/test action, not a generated prompt
fixture, because ignored events should not create a page.

### Dev Panel Section

Add an `Arc / Reflections` section to the existing event test panel.

Controls:

- scenario dropdown
- `Seed arc memories`
- `Preview daily prompt`
- `Preview quadrum prompt`
- `Preview arc prompt`
- `Trigger real daily`
- `Trigger real quadrum`
- `Trigger real arc`
- `Clear arc test state`
- `Clear prompt suite`

Diagnostics:

```text
arc year
entries this year
forced year
last arc tick
candidate memories
selected memories
recently used ids
schedule decision
block reason
```

### Required Scenarios

- `Annual Forced Arc`
  - no arc entry this year
  - current year day >= 45
  - expected: arc allowed

- `Cooldown Blocked`
  - one arc entry already this year
  - major event too soon
  - expected: blocked, normal event remains

- `Second Major Entry`
  - one arc entry this year
  - major event after 30 days
  - expected: second allowed if tuning enables it

- `Year Cap Block`
  - two arc entries this year
  - expected: blocked

- `Passion Skill Milestone`
  - passion skill crosses milestone
  - expected: progression page

- `Non-Passion Skill Ignored`
  - non-passion skill crosses milestone
  - expected: no page, diagnostic says ignored

- `Psylink Level Gained`
  - psylink level increases
  - expected: progression page

- `Quadrum Reflection`
  - seed enough important entries
  - expected: quadrum prompt produced

Prompt previews should not mutate real arc schedule or progression state. Use cloned/synthetic data.

Real trigger buttons may mutate state, but only in DevMode and with clear labels.

## Pure Tests

Add or extend standalone tests under `tests/`.

### Progression Tests

Test helper:

```text
ProgressionMilestonePolicy
```

Cases:

- null/empty milestone list emits nothing
- non-passion skill emits nothing
- passion skill below milestone emits nothing
- passion skill crossing 8 emits 8
- jump from 7 to 12 emits 12, not two entries
- already recorded milestone does not repeat
- baseline mode records state but does not emit
- malformed milestone list is sorted/deduped

### Psylink Tests

Pure helper should test level decision, not RimWorld hediff reflection.

Cases:

- no previous level, baseline suppresses
- level increase emits
- same level does not emit
- lower loaded value does not emit
- clamp invalid levels

### Arc Schedule Tests

Test:

- first yearly forced entry allowed after force day
- forced entry blocked before force day
- one entry this year blocks ordinary trigger
- second major allowed after configured gap
- second major blocked before gap
- two entries block all further entries
- new year resets count

### Arc Memory Selector Tests

Use pure DTOs, not `DiaryEvent`.

Cases:

- excludes reflection entries
- excludes recently used ids
- respects max memories
- reaches min forced memories when enough candidates exist
- caps same domain/group
- selected memories are sorted chronologically
- weighted random is deterministic for fixed seed
- empty candidates blocks non-forced arc

### Capture Policy Tests

Extend `tests/DiaryCapturePolicyTests`:

- `ProgressionEventData.Decide`
- `ArcReflectionEventData.Decide`
- context string builder formats

## Save Compatibility

Rules:

- Add new Scribe keys only.
- Do not rename existing keys.
- New objects are nullable on old saves and normalized after load.
- Never require DLC trackers to exist.
- Baseline progression scanners on first load to avoid retroactive spam.
- Cap saved recent-used lists.

New save keys:

```text
PawnDiaryRecord.progressionState
PawnDiaryRecord.arcSchedule
```

Post-load normalization:

```text
if progressionState == null: create empty baseline-pending state
if arcSchedule == null: create empty schedule
if lists == null: create empty lists
remove blank skill state keys
dedupe skill state keys
cap recentlyUsedEventIds
```

No migration is needed for old saves.

## DLC Safety

Follow the existing pattern:

- All raw DLC pawn reads go through `DlcContext`.
- Match optional content by string defName where possible.
- Do not call `DefDatabase<T>.GetNamed` for DLC defs.
- Do not add DLC dependencies to `About/About.xml`.
- Scanners must no-op cleanly when DLC trackers/content are absent.

Specifics:

- Biotech xenotype reads: `DlcContext`.
- Royal title reads: `DlcContext`.
- Psylink hediff detection: string matcher and guarded reflection/helper.
- Mechanitor-specific work skipped for MVP unless a stable string signal is identified.

## XML And Localization

New prompt/UI text:

- UI strings in `Languages/English/Keyed/PawnDiary.xml`.
- Prompt/group text in XML Defs and localized through DefInjected.
- Schema labels like `progression=`, `skill=`, `psylink_level=` stay English.

New tuning:

- Add arc cadence values to `DiaryTuningDef`.
- Add skill milestones to `DiaryTuningDef` or a small XML list Def.
- Add psylink hediff defName matchers to XML/tuning.

## Implementation Order

1. Add pure progression helper and tests.
2. Add `Progression` catalog source, domain classifier support, XML groups, prompt fields.
3. Add passion skill scanner with baseline state.
4. Add psylink scanner with baseline state.
5. Add xenotype/title scanner using `DlcContext`.
6. Add event-window importance XML groups.
7. Add pure arc schedule and memory selector helpers with tests.
8. Add `ArcReflection` catalog source and prompt template.
9. Wire annual forced trigger into the day-summary/rest path.
10. Wire major-event consideration after high-severity progression entries.
11. Extend devtools prompt fixtures and arc/reflection diagnostics.
12. Update `DOCUMENTATION.md` and `CHANGELOG.md`.
13. Run tests and build.

## Validation Checklist

Commands:

```powershell
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

Also validate touched XML with an XML parser.

In-game devtools smoke:

- Prompt test mode fixture generation.
- Real passion skill milestone.
- Non-passion skill ignored.
- Psylink level fixture.
- Forced annual arc.
- Cooldown blocked arc.
- Quadrum reflection fixture.
- Old save load without errors.
- No-DLC game no-ops for Royalty/Biotech/Anomaly-specific scans.

## Non-Goals For MVP

- No separate fact store.
- No purge/dedup fact system.
- No trait gained/lost source unless a stable hook is found quickly.
- No detailed mechanitor boss tracker.
- No individual gene list history.
- No daily/quadrum arc-history injection.
- No more than two arc entries per pawn per year.
