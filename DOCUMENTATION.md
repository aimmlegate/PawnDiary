# Pawn Diary - Architecture & Behavior

> Current-state design guide for the mod. When behavior or structure changes, update this file and
> add a dated entry to [CHANGELOG.md](CHANGELOG.md) in the same change.

_Last updated: 2026-06-17 (reasoning block scrub)_

---

## 1. Purpose

Pawn Diary records meaningful moments for free colonists and asks a configured compatible LLM API
lane to rewrite them as short diary pages. RimWorld loads the compiled DLL
at startup; there is no `main()`. Harmony patches, game components, Defs, and inspector tabs are
discovered by RimWorld and called during normal game lifecycle events.

The Diary inspector tab is inserted after the vanilla Needs tab and is visible only for colonist
pawns, including colonist corpses. Animals, prisoners, slaves, enemies, visitors, and non-colonist
participants are ignored. Mixed events create a solo entry for the eligible colonist. Pairwise
events between two eligible colonists generate the initiator POV first, then the recipient POV with
the initiator page supplied as hidden continuity context.

Active recorded sources include social interactions, social fights, mental breaks, vanilla tales,
arrivals, deaths, quality crafts, relic installs, mood-affecting game conditions, temporary and
scanned staged thoughts, XML-driven hediff health signals, sampled work moments, and end-of-day
reflections.

Arrivals and deaths are neutral chronicle entries, not persona-based first-person pages. Arrival
pages are forced to be a pawn's first visible/generated entry. Death pages are terminal; later
same-tick events are hidden and not generated for that pawn.

---

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, and `Languages/`. C# source lives under `Source/`; the build output
`1.6/Assemblies/PawnDiary.dll` is committed so the mod runs directly from a clone.

```
PawnDiary/
|-- About/                       mod metadata and preview image
|-- 1.6/
|   |-- Assemblies/PawnDiary.dll  committed build output
|   `-- Defs/                     groups, tuning, prompts, personas, prompt enchantments
|-- Languages/                   Keyed and DefInjected localization
|-- Source/
|   |-- Core/                    DiaryGameComponent partials and event pipeline
|   |-- Defs/                    Def classes and XML-backed lookup helpers
|   |-- Generation/              prompt/context/LLM helpers
|   |-- Models/                  saved/display models
|   |-- Patches/                 Harmony startup and hooks
|   |-- Settings/                mod settings and settings UI
|   |-- UI/                      Diary inspector tab
|   `-- Util/                    MiniJson
|-- prompt-lab/                  Node prompt-testing harness
|-- skills/                      repo workflow skills
`-- *.md                         repo docs
```

Key files:

| File | Role |
|---|---|
| `DiaryModStartup.cs` | Applies Harmony patches and injects the Diary tab after Needs. |
| `DiaryPatches.cs` | Patch entry points for interactions, mental states, inspirations, tales, deaths, arrivals, crafts, relics, mood events, thoughts, and hediff signals. |
| `DiaryGameComponent*.cs` | Recording, batching, generation scans, save/load, lookup indexes, and public UI access. Event-source partials own their `Record*` methods. |
| `DiaryEvent.cs` | Saved event model: raw/generated text, statuses, errors, context, source ids, LLM metadata, titles, semantic color cue, staggered text intensity, and Scribe persistence. |
| `PawnDiaryRecord.cs` | Per-pawn event index, saved persona preset, and generation toggle. |
| `DiaryContextBuilder.cs` | Compact pawn, surroundings, relationship, health, weapon, and opener context. |
| `DiaryContextFields.cs` | Exact parser for saved semicolon-delimited `gameContext` key/value fields. |
| `DiaryPromptBuilder.cs` | Builds pairwise, solo, neutral arrival/death, reflection, and title prompts, then applies per-event context policies. |
| `PromptEnchantments.cs` | Weighted hediff matcher that may append one compact live `important health:` cue to persona-bearing first-person prompts. |
| `LlmClient.cs` | Background HTTP queue, per-lane concurrency, retries, failover, deadlines, result queue, and main-thread debug-log handoff. |
| `InteractionGroups.cs` | XML-backed classifiers for Interaction, MentalState, Tale, MoodEvent, Thought, Inspiration, Work, and Hediff domains. |
| `DiaryTuningDef.cs` | XML thresholds, cooldowns, weights, scanner intervals, hediff progression scans, and safe code defaults. |
| `DiaryPromptDef.cs` | XML-backed prompt instructions and system prompts. |
| `DiaryPersonaDef.cs` / `PersonaAffinity.cs` | XML personas plus trait/backstory/theme weighting for first persona selection. |
| `DlcContext.cs` | One guarded home for optional DLC pawn context (`xenotype=`, `title=`, `faith=`). |
| `MoodImpact.cs` | Shared positive/negative/neutral mood-impact tokens and classification. |
| `PawnDiaryMod.cs` / `PawnDiarySettings.cs` | Settings data, API lane editor, Prompt Studio, Persona Presets editor, and group editor. |
| `EndpointUtility.cs` / `ModelListClient.cs` | Settings/generation endpoint URL normalization and settings-time model discovery (`/models` or Ollama `/api/tags`). |
| `ITab_Pawn_Diary*.cs` | Production diary view split as one partial class: orchestration, dev controls, year paging, entry cards, expansion state, linked previews, and roleplay text layout. |
| `DiaryTextFormat.cs` | Escapes raw generated rich-text tags, then converts light markdown and quoted speech into Unity rich text. |
| `MiniJson.cs` | Dependency-free JSON parser compatible with RimWorld's Unity Mono runtime. |

---

## 3. Data Flow

All sources funnel into `DiaryGameComponent`:

1. A Harmony hook or periodic scanner sees a candidate moment.
2. The source method checks group enablement, pawn eligibility, dedup windows, and source-specific
   filters.
3. `AddSoloEvent` or `AddPairwiseEvent` creates a `DiaryEvent`, stamps its semantic color cue and
   any low-Consciousness/intoxication staggered-text intensity,
   registers it in `diaryEvents` and `eventsById`, and stores the id in each eligible pawn's
   `PawnDiaryRecord`.
4. Generation queues immediately when possible and is retried by background scans.
5. `LlmClient` sends requests on selected API lanes and returns results to the main thread.
6. `ApplyLlmResult` stores generated text or failure state; successful main entries may queue a
   short title follow-up.
7. `EntriesFor` reads saved events for the tab without triggering generation.

`GameComponentTick` drains completed LLM results, flushes queued debug logs when dev LLM diagnostics
are enabled, recovers orphaned pending entries, and queues pending diary work every ~120 ticks. On
load, pending statuses reset to not-generated so interrupted work can retry.

Generation checks the POV pawn's live Consciousness before creating or queueing first-person work.
Non-neutral POVs below 11% Consciousness are marked `skipped`, so they do not fill the retry backlog
or LLM queue. Neutral arrival/death descriptions bypass this guard, and a healthy pawn in a paired
event can still write even if the other POV was skipped.

Scheduled scans take short-lived snapshots of RimWorld's live free-colonist list before recording
entries, preventing collection-modified errors if pawns join, leave, die, or change maps mid-scan.
Transient dedup dictionaries prune old keys once they reach 512 entries, using each source's
configured dedup window.

---

## 4. Event Sources

### Interactions

`PlayLog.Add` forwards interaction log rows to `RecordInteraction`. Interaction groups decide
whether a row records, how it is labeled, whether it batches, and what instruction reaches the
model. The forwarded text is rendered through RimWorld's own play-log POV string, so insult topics
and other UI-log details are preserved in the `what you saw` prompt field. Insults batch per pawn
pair for a three-hour quiet window (or eight rows) so insult sprees produce one evidence-rich entry
instead of one LLM request per jab. Small-talk, strange-chat, animal-handling, and teaching groups
are usually ambient day notes, with optional promotion for unusually salient moments. Strange chat
uses the same promotion odds as chitchat while keeping its own unsettling instruction and anomaly
green display cue.

### Mental States

`MentalStateHandler.TryStartMentalState` records `SocialFighting` as pairwise when both pawns are
eligible, or solo when only one colonist is eligible. Other accepted mental states become solo break
entries. Mirrored social-fight calls and repeated breaks are deduplicated.

### Tales And Synthetic Tales

`TaleRecorder.RecordTale` extracts live pawns from vanilla tale subclasses and creates solo or
pairwise entries. TaleDefs handled by more precise hooks are skipped, and GameCondition-like
TaleDefs are skipped so they record once through MoodEvent.

Synthetic tale-style hooks cover masterwork/legendary crafts from
`QualityUtility.SendCraftNotification` and ideology relic installation from `JobDriver_InstallRelic`.

### Arrivals And Deaths

Starting colonists receive neutral arrival pages after maps/free-colonist lists exist. Later joins
are detected through `Pawn.SetFaction` when a humanlike pawn becomes `Faction.OfPlayer`; cached
context includes prior faction, recruiter, pawn kind, creepjoiner flag, and surroundings.
The synthetic `arrival` interaction group matches `PawnDiary_Arrival`, so arrival pages have their
own chip, importance state, and Events toggle instead of falling through to the interaction
catch-all. Arrival generation still uses the dedicated `arrivalDescriptionInstruction` prompt text.

Deaths use a `Pawn.Kill` prefix to cache cause details before RimWorld mutates health state. Accepted
death TaleDefs mark the victim event as `death_description=true` and queue the neutral death prompt.
A `Pawn.Kill` postfix fallback writes the same kind of final entry when vanilla emits no death Tale,
covering natural condition deaths such as malnutrition/starvation without duplicating Tale-backed
combat deaths.

### Mood Events, Thoughts, Inspirations, Health, Work, And Day Reflections

Mood events are mood-affecting game conditions recorded once per eligible colonist on affected maps.
Thoughts are temporary memories filtered by XML tuning: ignored tokens, bypass tokens, eating
thresholds, minimum mood-effect thresholds, ambient tokens, and dedup windows. Ambient thoughts can fold into
end-of-day reflection.

`ThoughtProgression` scans staged situational thoughts that do not pass through the memory hook:
severe food states, tired/exhausted, trapped/entombed underground, and chemical hunger/starvation.
It records the first tracked stage in an episode and any later worsening stage once; clearing the
thought ends the episode without writing a recovery entry.

Inspirations are recorded through `InspirationHandler.TryStartInspiration` after vanilla accepts the
new inspiration. The target pawn receives a solo entry with an `inspiration=` context marker, the
`InspirationDef` label, duration, and any reason RimWorld supplied. `WordOfInspiration` is not
classified as a social ritual interaction in this mod; the resulting target inspiration is the diary
event.

Health-condition signals are driven by the Hediff domain in
`1.6/Defs/DiaryInteractionGroupDefs.xml`. `Pawn_HealthTracker.AddHediff` catches new hediffs, and a
lightweight scanner watches active matched hediffs for XML-configured severity-step increases. The
default `hediffMajorHealth` catch-all preserves the old behavior: bad non-injury hediffs can become
day-reflection candidates when they are chronic, sickness-causing, addiction/missing-part hediffs,
or pass the XML severity gate. Mod support XML can add lower-order Hediff groups for specific
modded hediff defNames and choose `DayReflection` or `Immediate` output without C# patches.
Progression and immediate-event dedup keys include the body part, so same-def hediffs on different
parts do not suppress each other; day reflections still collapse same-def health signals into one
readable daily highlight. Pregnancy and labor use dedicated immediate Hediff groups keyed by
`Pregnant`, `PregnantHuman`, `PregnancyLabor`, and `PregnancyLaborPushing`; birth itself still flows
through the `GaveBirth` TaleDef in the Life milestones group. Pregnancy termination, stillbirth, and
miscarriage memories are classified by a dedicated Thought group.

Work recording samples current Work-tab jobs periodically. It reads `CurJob.workGiverDef.workType`,
skips social and violent work, applies XML odds/cooldowns plus the `workGenerationWeight` multiplier,
then classifies the moment as passionate, straining, routine, or dark-study work.

When `daySummaryEnabled` is true, sleep/rest triggers one `DayReflection` candidate per pawn using a
weighted selection of major day events, opinion shifts, health-condition signals, and low-weight filler.

---

## 5. Groups, Batching, And Tuning

`1.6/Defs/DiaryInteractionGroupDefs.xml` defines nine classifier domains:

| Domain | Classifier | Typical groups |
|---|---|---|
| Interaction | `Classify(InteractionDef)` | romance, recruitment, ideology, anomaly, insults, animal, heartfelt, smalltalk |
| MentalState | `ClassifyMentalState(MentalStateDef)` | social fights, mental breaks |
| Tale | `ClassifyTale(TaleDef)` | combat/death, health/medicine, milestones, crafts, relics, raids, anomaly horror |
| MoodEvent | `ClassifyMoodEvent(GameConditionDef)` | positive, negative, mixed, passing moods |
| Thought | `ClassifyThought(ThoughtDef)` | pregnancy memories, positive, negative, passing thoughts |
| Inspiration | `ClassifyInspiration(InspirationDef)` | pawn inspirations |
| Work | `ClassifyWork(string)` | dark study, passionate, straining, routine |
| Hediff | `ClassifyHediff(HediffDef)` | pregnancy, labor, major health changes, modded health signals |

Matching is domain-scoped by exact `defName` or substring token. XML order matters; catch-all groups
go last. Settings store per-group enabled flags and instruction overrides keyed by group `defName`;
missing settings use XML defaults.

Social-interaction compatibility should stay XML-only when the other mod extends RimWorld's normal
social system. If the mod emits `InteractionDef` rows through the play log, add a new
Interaction-domain group or patch `matchDefNames`/`matchTokens`, `batch`, `promotion`,
`instruction`, `tone`, and `defaultEnabled`. A C# adapter is only needed when a mod bypasses
RimWorld's `PlayLogEntry_Interaction`, thoughts, tales, mental states, or hediff trackers entirely.

Groups may also set a `colorCue`, which is saved on new `DiaryEvent`s and drives only the Diary
tab's accent strip/chip. The UI maps cues to RimWorld-like colors: combat uses hostile red, social
fights/insult-fight rows use orange, generic mental breaks use green, daze/wander breaks use light
blue, strange chat uses anomaly green, anomaly/dark-study events use deep red, deep talk and day reflection use white, and
non-important entries without a specific cue use light white-gray. Older saves without `colorCue`
derive the same cue from saved `gameContext` markers and XML group classification.

Batch policies live on groups:

- `PairEvent` merges quick rows for a pawn pair or def.
- `AmbientDayNote` accumulates low-stakes rows per pawn/day into one solo memory.
- The insults group uses `PairEvent` with `includeInteractionLabel=false`, so each evidence line is
  the actual game log sentence rather than a repeated "Insult:" prefix.
- Promotion policies let batched interactions escape into immediate pairwise events based on opinion
  intensity, opinion asymmetry, low needs, or extreme mood, then apply `socialGenerationWeight`.

Hediff policies live on Hediff-domain groups. The optional `<hediff>` block controls output mode
(`DayReflection` or `Immediate`), visible/bad/injury gates, severity thresholds, special always
qualifiers (`chronicAlways`, `sickThoughtAlways`, `addictionAlways`, `missingPartAlways`),
`recordOnAdd`, `recordOnSeverityIncrease`, `severityStep`, `dedupTicks`, and
`dayReflectionWeight`. Immediate-mode policies can also set `appearedTextKey` and
`progressedTextKey`, localized Keyed fallback strings that receive the pawn label as `{0}` and the
hediff label as `{1}`. The policy's `<minSeverity>` is the recording gate for that group; the
general tuning Def only controls the scanner interval and reflection selection weight. This is the
generic mod-support layer: a compatibility patch can add:

```xml
<PawnDiary.DiaryInteractionGroupDef>
  <defName>myModMutationHediffs</defName>
  <label>My Mod mutations</label>
  <order>690</order>
  <domain>Hediff</domain>
  <defaultEnabled>true</defaultEnabled>
  <instruction>a body changing in a way the pawn cannot ignore</instruction>
  <hediff>
    <mode>Immediate</mode>
    <visibleOnly>true</visibleOnly>
    <badOnly>false</badOnly>
    <minSeverity>0.2</minSeverity>
    <recordOnAdd>true</recordOnAdd>
    <recordOnSeverityIncrease>true</recordOnSeverityIncrease>
    <severityStep>0.25</severityStep>
  </hediff>
  <matchDefNames>
    <li>MyMod_Mutation</li>
  </matchDefNames>
</PawnDiary.DiaryInteractionGroupDef>
```

`1.6/Defs/DiaryTuningDef.xml` holds dedup windows, scanner intervals, mood/health/beauty buckets,
thought thresholds, staged thought rules, the hediff progression scan interval, work odds/cooldowns,
day-reflection weights, and nearby-context tuning. Hediff severity gates live on Hediff-domain group
policies, not in this tuning Def. Code defaults keep the mod usable if XML fails to load.

---

## 6. Prompts, Personas, And Titles

Prompts are compact `key: value` lines. `AppendField` drops empty values and `none` / `n/a` /
`unknown` sentinels. Numeric structured context uses invariant dot-decimal formatting so prompts do
not vary by player OS locale.

Main first-person prompts no longer use one broad template. `DiaryPromptBuilder` applies a small
per-event context policy:

- Routine/internal entries (work, thoughts, mood events, ambient/batched notes) send the event, POV,
  group instruction, persona, setting, last-opener continuity, and possibly one compact live health
  cue, then skip broad pawn state, relationship, hidden initiator text, and weapon.
- Meaningful non-batched social entries also carry setting, relationship continuity, tone when the
  group is important, and hidden initiator context only for important paired recipient follow-ups.
- Combat/crisis entries add pawn summary, setting, tone, relationship for paired events, current POV
  weapon, hidden initiator context, and the same optional prompt enchantment path.
- End-of-day reflections add pawn summary, setting, and last-opener continuity to the selected
  highlights, but skip relationship and weapon.
- Major solo events add pawn summary, setting, tone, and last-opener continuity, but skip
  relationship and weapon unless combat.

Pawn summaries may contain DLC identity lines, life stage, mood, health, low capacities, and top
thoughts when the policy includes `you:`. Generated structured context avoids sending numeric
scores where possible: age, mood, pain, bleeding, opinion, thought impact, and hediff severity are
bucketed into words. Surroundings include a couple of nearby objects chosen with weighted random
selection, favoring important context such as fire, corpses, and buildings without making every entry
identical. First-person prompts now include `setting:` whenever the pawn has a spawned-map
surroundings summary. The surroundings line always says whether the pawn is indoors or outdoors;
weather and biome are appended only when the room is psychologically outdoors. The same setting
line may also include up to three visible active map conditions and one fresh recent threat letter
while a player-home map is still in a danger state. These are context hints for the current diary
entry, not standalone diary events. Neutral arrival/death prompts parse curated facts from
`gameContext` instead of dumping the whole metadata string.

Prompt enchantments are weighted hediff matchers in
`1.6/Defs/DiaryPromptEnchantmentDefs.xml`. When `enablePromptEnchantments` is on, every
first-person prompt that includes `persona:` may add exactly one `important health:` field next to
it. The field is still omitted when no configured visible hediff matches or the selected match fails
its chance roll. Neutral arrival/death prompts and title follow-ups never use prompt enchantments.
Low Consciousness above the hard skip floor is treated as the most important live health cue:
clouded, fading, and barely-conscious bands emit compact Keyed `important health:` values before
normal hediff matching, so a pawn near collapse does not lose that signal to a less important wound.

XML no longer contains prompt prose for hediffs. It only lists eligible `hediffDefNames`,
`minHediffSeverity`, `chance`/`frequency`, `weight`, and the weight-multiplier `severity`. Matching
live hediffs first pass their chance roll, then one winner is selected by configured weight plus
live urgency signals such as severity, life-threatening state, bleeding, pain, and health impact.
The prompt text itself is a compact priority cue built from RimWorld's live data:
`important health: high priority; <urgency> <condition> [in <body part>]; <top impact cues>`.
Impact cues are capped so the model sees only the strongest reasons, such as life-threatening,
bleeding, severe pain, body weakness, addiction pressure, or mood pressure. The first-person system
prompts explicitly tell the model that health is physical/mood pressure, not the subject, unless the
actual event is medical. They also cap health mentions to one short phrase so a wound cue cannot
turn a conversion, insult, work moment, or thought into an invented treatment scene. The
`important health:` field key remains an English schema label by the localization carve-out, but
every natural-language word in its value comes from Keyed translations or RimWorld's localized
hediff/body-part labels.

Visible hediff matchers can define optional `hediffSeverityTiers`. Four fixed code levels are
available: minor, moderate, major, and critical. Code-owned numeric thresholds decide which tier
wins without exposing those numbers to the prompt. The highest configured level at or below the live
hediff severity wins and may override
`chance`/`frequency`, `weight`, and the weight-multiplier `severity`; omitted tier fields inherit
from the parent Def.

The bundled starter catalog covers illness, blood loss, drug highs, non-luciferium chemical
addictions/withdrawals, luciferium dependency, chronic cognitive/sensory conditions, pregnancy,
hemogen craving, psychic bond trauma, and Royalty/Biotech/Anomaly hediffs such as abasia,
mindscrew, cube states, void exposure, corpse torment, inhumanization, and flesh appendages. Missing
body parts are intentionally not matched by the starter catalog. Royalty titles and xenotypes remain
normal pawn-summary context, not prompt enchantments.

System prompts are selected by narrative mode:

| Mode | Prompt |
|---|---|
| First-person diary | `systemPrompt` |
| End-of-day reflection | `systemPromptReflection` |
| Arrival/death chronicle | `systemPromptNeutral` |
| Title follow-up | `systemPromptTitle` |

Default prompt contracts ask for complete, short entries using prose length cues rather than numeric
word ranges. They now name the actual event (`event`, `what you saw` / `what happened`,
`instruction`, POV, and role) as the subject that must drive the entry. Persona, relationship,
setting, weapon, mood, thought, health, hidden entries, and last-opener fields are supporting
context for voice, focus, and subtext, not alternate scenes to write instead. The first-person diary
and day-reflection system prompts also require at least one concrete event detail, warn against
vague filler and invented props/treatment/dialogue. They now include an explicit low-creative-reach
guard for roleplay-tuned models: do not add new names, factions, places, backstory, memories,
symbols, threats, promises, consequences, or off-screen actions. Compact good/bad examples help
small local models see the intended transformation.

Paired two-pawn interaction prompts append separate localized direct-speech rules for initiator and
recipient POVs. Each rule names the current POV pawn and the opposite pawn, then allows quoted
speech only for words plausibly spoken by the current POV pawn. The opposite pawn's words must be
paraphrased without quotation marks, and the prompt must not invent POV speech just to add dialogue.
The appended cues include short good/wrong examples. Single-POV interaction prompts get the same
name-specific POV-only cue. Non-interaction solo entries do not get that cue; the prompt builder
explicitly rejects neutral arrival/death, dev mock, mental-state, tale, mood, thought, inspiration,
work, hediff, day-reflection, and ambient-day contexts before checking whether an event's defName is
a real RimWorld `InteractionDef`.

Player-customizable, Def-backed user-message instructions:

- `singlePovInstruction`
- `recipientFollowupInstruction`
- `deathDescriptionInstruction`
- `arrivalDescriptionInstruction`
- `titleUserInstruction`

Existing saves keep prompt overrides in `PawnDiarySettings`; Prompt Studio can reset one prompt or
all prompts to current XML defaults. During `PostLoadInit`, settings migrate only prompt fields that
exactly match known older shipped defaults, after newline normalization, so default users receive
prompt-quality fixes while custom Prompt Studio text is preserved.

Persona presets start from `DiaryPersonaDef` XML, then settings may layer built-in overrides and
custom personas. `DiaryPersonas.WeightedStartingPersona` uses base weight, trait/backstory theme
matches from `PersonaAffinity`, a large creepjoiner bonus for `void` personas, and a soft duplicate
penalty for voices already used by living free colonists. Custom personas must keep at least one
allowed tag: `grim`, `warm`, `hostile`, `anxious`, `analytical`, `dramatic`, `social`,
`whimsical`, `noble`, or `void`. Saved records keep their persona defName and fall back to the
current default if it disappears. The merged effective catalog is cached and invalidated when
settings-backed personas change or are normalized after load. The built-in catalog includes readable
extreme voices such as `fractured-pattern-seer` and `word-salad-oracle`; their rules ask for
fragmented association or controlled word-salad texture while preserving at least one clear event
detail. It also includes restrained verse-flavored voices such as `plainspoken-poet` and
`lowkey-rapper`, tuned for light imagery or cadence rather than constant rhyme.

Each built-in `DiaryPersonaDef` can also define `cloudedConsciousnessRule`,
`fadingConsciousnessRule`, and `barelyConsciousRule`. When a live pawn is conscious enough to write
but impaired, generation keeps the same persona label and swaps the normal persona text for that
persona's matching low-Consciousness rule. Persona settings can override those three modifiers for
built-in personas, while unchanged built-in rows continue inheriting XML so old saves and future XML
edits do not lose their default impaired-voice tuning. Custom personas can define their own modifier
text; blank custom modifier boxes fall back to the persona's normal rule.

Title generation defaults on. Successful main entries queue `QueueTitleRequest`, capped at
`TitleMaxTokens = 40` and pinned to the producing lane when possible. Stored titles render as
`date - title`; entries without titles render date-only. Pending titles use a header-level animated
placeholder aligned to the future title start. Title statuses are separate from main-entry statuses.

---

## 7. Settings And UI

Core settings:

| Setting | Default | Notes |
|---|---:|---|
| `apiEndpoints` | one enabled local lane | URL, model, key, enabled flag, compatibility mode, and optional mode-specific knobs; blank-model or disabled rows are skipped. |
| `timeoutSeconds` | 30 | 5-300; per-lane-attempt deadline after a request leaves the queue. |
| `maxConcurrentRequests` | 4 | 1-16 per API lane; use 1 for a single local model. |
| `maxTokens` | 100 | API cap plus local sentence-aware response trimming. |
| `temperature` | 0.8 | 0-2. |
| `generateTitles` | true | Queues title follow-ups for successful main entries. |
| `enableAtmosphericFormatting` | true | Allows rare display-only text layout effects for extreme entries. |
| `enablePromptEnchantments` | true | Allows weighted live hediff context to append one first-person `important health:` cue. |
| `workGenerationWeight` / `socialGenerationWeight` | 1 | 0-5 multipliers for sampled work and batched-social promotion. |
| `systemPrompt*` | XML defaults | Diary, reflection, neutral, and title system prompts. |
| prompt instruction overrides | XML defaults | User-message prompt texts listed in section 6. |
| `groupEnabled` / `groupInstructions` | XML defaults | Per-group recording toggles and instructions. |
| `personaPresets` | empty | Built-in overrides plus custom personas. |
| dev/UI toggles | varies | API/persona/debug/generating-entry visibility. |

The settings window contains Connection, Diary writing, Prompt Studio, Persona presets, and Events
sections. It supports multiple API lanes, compatibility-mode selection, model fetching/picking,
prompt resets, persona selection
through a compact selector menu, single-card persona editing, custom persona creation/deletion,
low-Consciousness persona modifier editing, grouped event toggles, and one per-group instruction
editor. Each API lane can speak OpenAI-compatible chat completions, OpenAI Responses, or native
Ollama chat; Responses lanes can send a reasoning-effort override, and Ollama lanes can request
native thinking output while saving only final message content. Known reasoning/thinking transcript
blocks embedded in normal content are stripped before anything is persisted. Model-list fetches are
generation-stamped and tied to the endpoint row's URL/API-key/mode snapshot, so removing, resetting,
or editing rows while a request is in flight cannot auto-fill a different row.

The production Diary tab shows only completed generated pages. Dev mode adds writing enablement,
persona picker, pending rows, raw/failure rows, prompt/status diagnostics, in-progress indicators,
and a test button that tops the selected pawn up to 360 completed mock pages without calling an LLM.

Long histories are paged by in-game year using each entry's saved display date. The center year label
opens a selector menu with visible years and page counts; adjacent buttons move newer/older. Within a
year page, the newest 15 visible entries open by default and older entries collapse to compact
date/title headers. Header clicks toggle expansion with lightweight animation and session-local
manual state. UI caches for first-seen fades and expansion blends are capped and periodically cleared.

Generated cards show date/title, semantic color accent and group chip, page tint, a subtle model id,
linked previews for the other pawn's POV, and title-pending animation. `DiaryTextFormat` converts
light markdown (`**bold**`, `*italic*`, headings, bullets, block quotes) and inline quoted speech to
Unity rich text after replacing raw generated `<...>` brackets with safe visible brackets, so model
output cannot inject Unity rich-text tags. When `enableAtmosphericFormatting` is on, only extreme
entries get additional
display-only typography: mental-break pages can be split into fractured sentence blocks,
anomaly/dark pages can render with uneasy insets/italics, death-description chronicle pages can
render as centered memorial blocks, and first-person pages written while the pawn was severely
intoxicated or low-Consciousness can get deterministic random-looking variable-size words. Higher
stored stagger intensity affects more words. Anomaly's in-game "strange chat" is the
`DisturbingChat` InteractionDef; it gives the recipient the `SpokeToDisturbing` social thought and
`SpokeToDisturbingMood` mood thought ("unsettling conversation"). It has its own exact-match
interaction group before the broader anomaly group, using chitchat's ambient batching and promotion
odds so only unusually salient strange chats become immediate pairwise entries. Only the initiator
POV for promoted exact `DisturbingChat` entries gets the special anomaly-green accent and dramatic
distorted direct-speech formatting. This formatting is measured and drawn through the same block
pipeline so scroll heights stay stable, and it never changes prompts or saved generated text.
Social-tab log rows can jump to matching diary entries, including older year pages.

---

## 8. LLM Reliability

`LlmClient` treats each enabled endpoint+model row as an API lane:

- each lane chooses its own request URL, payload, and response parser from its compatibility mode:
  `/chat/completions`, `/responses`, or Ollama `/api/chat`;
- new requests are assigned round-robin;
- recipient pairwise requests and title follow-ups pin to the lane that produced the prior text when
  possible;
- lanes run independently behind per-lane `SemaphoreSlim` guards;
- `ServicePointManager.DefaultConnectionLimit` is raised so transport limits do not cap concurrency
  at two connections per host;
- requests carry ordered failover targets and try the next lane after permanent errors, exhausted
  retries, timeout, or empty content;
- transient errors retry up to three times per lane;
- responses are locally trimmed to `maxTokens`, preferring the last complete sentence, with dangling
  final fragments removed for main diary/note responses. OpenAI Responses reasoning lanes get extra
  API output-token headroom because that API counts hidden reasoning tokens against
  `max_output_tokens`; the saved text is still capped locally;
- structured Responses reasoning items and common text transcript blocks (`<think>...</think>`,
  reasoning/thinking fences, and `Reasoning: ... Final:` prefixes) are removed after the endpoint
  completes and before debug/saved entry text is stored;
- background debug logs are queued and flushed on the main thread only when the dev LLM debug toggle
  is enabled;
- stale sessions are cancelled when a new `DiaryGameComponent` is constructed;
- pending entries with no in-flight request reset only after two consecutive orphan scans.

If no enabled lane with a model exists, the entry fails with `PawnDiary.Error.NoApiConfigured` and
raw text is kept when configured.

---

## 9. Persistence

`DiaryGameComponent.ExposeData` saves `diaries` and `diaryEvents`. The `eventId -> DiaryEvent`
lookup index is rebuilt from `diaryEvents` on load so `FindEvent` stays O(1).

`DiaryEvent` saves raw event text, generated text, statuses/errors, context, source ids, LLM
metadata, semantic `colorCue`, per-POV titles, per-POV staggered text intensity, and per-POV
pre-length-cleanup final-answer text for debug inspection. Full prompt strings are still not
persisted; dev diagnostics can show prompts built during the current session.
Tale/mental/source classification is inferred from stable `gameContext` fields such as `tale=`,
`mental_state=`, `mood_event=`, `thought=`, `inspiration=`, `work=`, and `hediff=`. Exact key/value
lookups go through `DiaryContextFields`, so pawn ids and other field values do not collide by
substring prefix.

Pending requests are not persisted. On load, pending main statuses reset to not-generated, and stale
pending title statuses without stored titles are cleared. Short-lived death/arrival context caches
are also cleared when a new `DiaryGameComponent` starts a game session.

### TODO: Full `DiaryEvent` Role-Slot Extraction

The current saved event shape is still role-by-field: `initiator*`, `recipient*`, and `neutral*`
fields each carry their own pawn id, display text, generated text, status, title, prompt/debug data,
LLM metadata, surroundings, continuity, opener, weapon, and staggered-text state. A future cleanup
should extract those repeated field families into explicit saved role slots while preserving old
saves.

Rough safe-route outline:

- Add a small `DiaryEventRoleSlot : IExposable` model with a stable `role` string
  (`initiator`, `recipient`, `neutral`), optional `pawnId`, display `pawnName`, raw/source text,
  generated text, status/error, prompt/debug text, title fields, LLM endpoint/model metadata,
  pawn summary, surroundings, continuity, last opener, weapon, and staggered-text intensity.
- Add `List<DiaryEventRoleSlot> roleSlots` to `DiaryEvent` as an additive saved field. Keep all
  existing `initiator*`, `recipient*`, and `neutral*` fields during the migration; old saves should
  load exactly as they do now.
- In `DiaryEvent.ExposeData`, after loading legacy fields, hydrate missing role slots from the
  legacy field families. Also backfill legacy fields from slots while callers are still mixed, so
  either representation can be read during the transition.
- Keep role constants and prompt schema labels unchanged. Existing prompt text and localization
  carve-outs depend on `initiator`, `recipient`, and `neutral` staying stable.
- Add slot accessors first: `SlotForRole`, `SlotForPawn`, `NameForRole`, `TextForRole`,
  `GeneratedTextFor`, `StatusFor`, `TitleForRole`, `SurroundingsForRole`, `ContinuityForRole`,
  `LastOpenerForRole`, `StaggeredIntensityForRole`, and status/title mutation helpers. Make current
  public methods delegate to slots instead of duplicating switch logic.
- Change event creation (`AddSoloEvent`, `AddPairwiseEvent`, neutral arrival/death/reflection
  creation) to create role slots while still filling legacy fields. Do not broaden semantics in the
  same pass: this is a storage/refactor step, not arbitrary multi-participant event support yet.
- Move `DiaryPromptBuilder`, generation queueing, `ApplyLlmResult`, title queueing, and diary tab
  view creation onto slot accessors. Keep old field names out of new code except inside migration
  and compatibility shims.
- Only after all readers/writers use slots, consider retiring direct legacy-field writes. Removing
  the old saved fields should be a separate compatibility decision, not part of the first extraction.
- Validate with at least these manual scenarios: load an old save; create a new pairwise social
  event; create a solo event; generate arrival/death neutral descriptions; queue title follow-ups;
  handle a skipped low-Consciousness POV; navigate linked entries in the Diary tab; save and reload.

---

## 10. Runtime And DLC Constraints

- RimWorld runs this mod on Unity Mono. Only assemblies shipped in `RimWorldWin64_Data/Managed` are
  guaranteed at runtime.
- Do not add `System.Web.Extensions`, `JavaScriptSerializer`, or external JSON dependencies.
  Runtime JSON is parsed by `MiniJson`.
- The mod declares no paid DLC dependency. Optional DLC content must no-op safely when the DLC is
  absent.
- Prefer string `defName` matchers in XML for DLC-aware groups. DLC defNames simply never appear
  without the DLC.
- DLC-gated pawn data belongs in `DlcContext`, guarded by both `ModsConfig.<Dlc>Active` and null
  checks, returning empty strings when absent.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use `GetNamedSilentFail` or the
  matcher pattern.
- XML references to DLC defs need `MayRequire`; plain string matcher lists do not.

---

## 11. Localization

Player-facing UI strings and natural-language prompt text are Keyed entries in
`Languages/English/Keyed/PawnDiary.xml`, resolved with `.Translate()` on the main thread. Def text
(`label`, `instruction`, `tone`, persona `rule`, persona low-Consciousness rules, prompt defs, and
hediff labels/body-part labels used by prompt enchantments) localizes through DefInjected files. The
health-cue words generated by `PromptEnchantments.cs` localize through Keyed
`PawnDiary.Prompt.Health.*` entries; other generated prompt-context connector words localize through
`PawnDiary.Ctx.*` entries.

Kept in English intentionally:

- prompt schema labels such as `event:`, `role:`, `sex=`, `thought=`, `inspiration=`;
- role/sentinel words such as `initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`;
- internal defNames, trait keys, backstory categories, and persona theme tags;
- background-thread `LlmClient` error strings and dev/debug diagnostics.

To add a language, copy `Languages/English` to `Languages/<Language>`, translate Keyed values, and
optionally add DefInjected translations for XML Def text.

---

## 12. Build And Prompt Lab

Build:

```
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

Output is `1.6/Assemblies/PawnDiary.dll`. If `MSBuild` is not on `PATH`, use `vswhere` to locate it
or build from a Visual Studio Developer PowerShell.

Prompt lab:

```
cd prompt-lab
npm run from-defs
node run.js --from-defs --save --model <model-name>
```

The prompt lab reads `DiaryPromptDef.xml`, `DiaryPersonaDefs.xml`,
`DiaryInteractionGroupDefs.xml`, and the English Keyed direct-speech prompt cues. Generated
first-person fixtures mirror `DiaryPromptBuilder`'s compact context policy: persona, optional
`important health:`, last-opener, relationship, tone, setting, weapon, and hidden initiator fields
appear only in the same branches as the in-game prompt builder. Title fixtures and title follow-ups
use `DiaryPromptDef.titleUserInstruction`.

Saved prompt-lab results go to `prompt-lab/results/<model-name>/<timestamp>.md`, which is ignored by
git.

---

## 13. Changelog

Full history lives in [CHANGELOG.md](CHANGELOG.md). Add a dated entry there with every change.
