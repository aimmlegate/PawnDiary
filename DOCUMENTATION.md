# Pawn Diary - Architecture & Behavior

> Current-state design guide for the mod. When behavior or structure changes, update this file and
> add a dated entry to [CHANGELOG.md](CHANGELOG.md) in the same change.

_Last updated: 2026-06-17 (prompt context policy streamlining)_

---

## 1. Purpose

Pawn Diary records meaningful moments for free colonists and asks an OpenAI-compatible
`/chat/completions` endpoint to rewrite them as short diary pages. RimWorld loads the compiled DLL
at startup; there is no `main()`. Harmony patches, game components, Defs, and inspector tabs are
discovered by RimWorld and called during normal game lifecycle events.

The Diary inspector tab is inserted after the vanilla Needs tab and is visible only for colonist
pawns, including colonist corpses. Animals, prisoners, slaves, enemies, visitors, and non-colonist
participants are ignored. Mixed events create a solo entry for the eligible colonist. Pairwise
events between two eligible colonists generate the initiator POV first, then the recipient POV with
the initiator page supplied as hidden continuity context.

Active recorded sources include social interactions, social fights, mental breaks, vanilla tales,
arrivals, deaths, quality crafts, relic installs, mood-affecting game conditions, temporary and
scanned staged thoughts, sampled work moments, and end-of-day reflections. Discovery/GameEvent
recording code exists but is intentionally disabled until the RimWorld 1.6 hook path is repaired.

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
| `DiaryPatches.cs` | Patch entry points for interactions, mental states, tales, deaths, arrivals, crafts, relics, mood events, thoughts, and hediff signals. |
| `DiscoveryPatches.cs` | Quarantined, unregistered discovery hook experiments. |
| `DiaryGameComponent*.cs` | Recording, batching, generation scans, save/load, lookup indexes, and public UI access. Event-source partials own their `Record*` methods. |
| `DiaryEvent.cs` | Saved event model: raw/generated text, statuses, errors, context, source ids, LLM metadata, titles, and Scribe persistence. |
| `PawnDiaryRecord.cs` | Per-pawn event index, saved persona preset, and generation toggle. |
| `DiaryContextBuilder.cs` | Compact pawn, surroundings, relationship, health, weapon, and opener context. |
| `DiaryPromptBuilder.cs` | Builds pairwise, solo, neutral arrival/death, reflection, and title prompts, then applies per-event context policies. |
| `PromptEnchantments.cs` | XML-driven state matcher that may append one weighted-random `prompt_enchantment:` line to policy-selected first-person prompts. |
| `LlmClient.cs` | Background HTTP queue, per-lane concurrency, retries, failover, deadlines, result queue, and main-thread debug-log handoff. |
| `InteractionGroups.cs` | XML-backed classifiers for Interaction, MentalState, Tale, MoodEvent, Thought, GameEvent, and Work domains. |
| `DiaryTuningDef.cs` | XML thresholds, cooldowns, weights, scanner intervals, and safe code defaults. |
| `DiaryPromptDef.cs` | XML-backed prompt instructions and system prompts. |
| `DiaryPersonaDef.cs` / `PersonaAffinity.cs` | XML personas plus trait/backstory/theme weighting for first persona selection. |
| `DlcContext.cs` | One guarded home for optional DLC pawn context (`xenotype=`, `title=`, `faith=`) and DLC-safe prompt-enchantment matchers. |
| `MoodImpact.cs` | Shared positive/negative/neutral mood-impact tokens and classification. |
| `PawnDiaryMod.cs` / `PawnDiarySettings.cs` | Settings data, API lane editor, Prompt Studio, Persona Presets editor, model-list fetching, and group editor. |
| `ITab_Pawn_Diary.cs` | Production diary view, dev diagnostics, linked previews, year paging, collapsed entries, animations, and click targets. |
| `DiaryTextFormat.cs` | Converts light markdown and quoted speech into Unity rich text. |
| `MiniJson.cs` | Dependency-free JSON parser compatible with RimWorld's Unity Mono runtime. |

---

## 3. Data Flow

All sources funnel into `DiaryGameComponent`:

1. A Harmony hook or periodic scanner sees a candidate moment.
2. The source method checks group enablement, pawn eligibility, dedup windows, and source-specific
   filters.
3. `AddSoloEvent` or `AddPairwiseEvent` creates a `DiaryEvent`, registers it in `diaryEvents` and
   `eventsById`, and stores the id in each eligible pawn's `PawnDiaryRecord`.
4. Generation queues immediately when possible and is retried by background scans.
5. `LlmClient` sends requests on selected API lanes and returns results to the main thread.
6. `ApplyLlmResult` stores generated text or failure state; successful main entries may queue a
   short title follow-up.
7. `EntriesFor` reads saved events for the tab without triggering generation.

`GameComponentTick` drains completed LLM results, flushes queued debug logs, recovers orphaned
pending entries, and queues pending diary work every ~120 ticks. On load, pending statuses reset to
not-generated so interrupted work can retry.

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
model. Small-talk, animal-handling, and teaching groups are usually ambient day notes, with optional
promotion for unusually salient moments.

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

### Game Events And Discoveries

The GameEvent recorder was built for map/story discoveries such as ancient dangers, ancient mech
threats, hidden revealed things, and monolith beats. It is currently disabled:

- attempted Harmony hook classes live in `Source/Patches/DiscoveryPatches.cs` without `[HarmonyPatch]`
  attributes, so `PatchAll` does not register them;
- `DiaryGameComponent.DiscoveryEventsEnabled` returns `false`;
- recorder methods and disabled stubs guard on that flag and no-op before creating events.

### Arrivals And Deaths

Starting colonists receive neutral arrival pages after maps/free-colonist lists exist. Later joins
are detected through `Pawn.SetFaction` when a humanlike pawn becomes `Faction.OfPlayer`; cached
context includes prior faction, recruiter, pawn kind, creepjoiner flag, and surroundings.

Deaths use a `Pawn.Kill` prefix to cache cause details before RimWorld mutates health state. Accepted
death TaleDefs mark the victim event as `death_description=true` and queue the neutral death prompt.
A `Pawn.Kill` postfix fallback writes the same kind of final entry when vanilla emits no death Tale,
covering natural condition deaths such as malnutrition/starvation without duplicating Tale-backed
combat deaths.

### Mood Events, Thoughts, Work, And Day Reflections

Mood events are mood-affecting game conditions recorded once per eligible colonist on affected maps.
Thoughts are temporary memories filtered by XML tuning: ignored tokens, bypass tokens, eating
thresholds, minimum mood offset, ambient tokens, and dedup windows. Ambient thoughts can fold into
end-of-day reflection.

`ThoughtProgression` scans staged situational thoughts that do not pass through the memory hook:
severe food states, tired/exhausted, trapped/entombed underground, and chemical hunger/starvation.
It records the first tracked stage in an episode and any later worsening stage once; clearing the
thought ends the episode without writing a recovery entry.

Work recording samples current Work-tab jobs periodically. It reads `CurJob.workGiverDef.workType`,
skips social and violent work, applies XML odds/cooldowns plus the `workGenerationWeight` multiplier,
then classifies the moment as passionate, straining, routine, or dark-study work.

When `daySummaryEnabled` is true, sleep/rest triggers one `DayReflection` candidate per pawn using a
weighted selection of major day events, opinion shifts, major new afflictions, and low-weight filler.

---

## 5. Groups, Batching, And Tuning

`1.6/Defs/DiaryInteractionGroupDefs.xml` defines seven classifier domains:

| Domain | Classifier | Typical groups |
|---|---|---|
| Interaction | `Classify(InteractionDef)` | romance, recruitment, ideology, anomaly, insults, animal, heartfelt, smalltalk |
| MentalState | `ClassifyMentalState(MentalStateDef)` | social fights, mental breaks |
| Tale | `ClassifyTale(TaleDef)` | combat/death, health/medicine, milestones, crafts, relics, raids, anomaly horror |
| MoodEvent | `ClassifyMoodEvent(GameConditionDef)` | positive, negative, mixed, passing moods |
| Thought | `ClassifyThought(ThoughtDef)` | positive, negative, passing thoughts |
| GameEvent | `ClassifyGameEvent(string)` | map discoveries, ancient mech threats, monoliths |
| Work | `ClassifyWork(string)` | dark study, passionate, straining, routine |

Matching is domain-scoped by exact `defName` or substring token. XML order matters; catch-all groups
go last. Settings store per-group enabled flags and instruction overrides keyed by group `defName`;
missing settings use XML defaults.

Batch policies live on groups:

- `PairEvent` merges quick rows for a pawn pair or def.
- `AmbientDayNote` accumulates low-stakes rows per pawn/day into one solo memory.
- Promotion policies let batched interactions escape into immediate pairwise events based on opinion
  intensity, opinion asymmetry, low needs, or extreme mood, then apply `socialGenerationWeight`.

`1.6/Defs/DiaryTuningDef.xml` holds dedup windows, scanner intervals, mood/health/beauty buckets,
thought thresholds, staged thought rules, work odds/cooldowns, day-reflection weights, and
nearby-context tuning. Code defaults keep the mod usable if XML fails to load.

---

## 6. Prompts, Personas, And Titles

Prompts are compact `key: value` lines. `AppendField` drops empty values and `none` / `n/a` /
`unknown` sentinels. Numeric structured context uses invariant dot-decimal formatting so prompts do
not vary by player OS locale.

Main first-person prompts no longer use one broad template. `DiaryPromptBuilder` applies a small
per-event context policy:

- Routine/internal entries (work, thoughts, mood events, ambient/batched notes) send the event, POV,
  group instruction, and persona, then skip broad pawn state, setting, relationship, opener, hidden
  initiator text, weapon, and prompt enchantments.
- Meaningful non-batched social entries add relationship continuity, tone when the group is
  important, and hidden initiator context only for important paired recipient follow-ups.
- Combat/crisis entries add pawn summary, setting, tone, relationship for paired events, current POV
  weapon, hidden initiator context, and optional prompt enchantment.
- End-of-day reflections add pawn summary to the selected highlights, but skip setting,
  relationship, opener, weapon, and prompt enchantment.
- Major solo tales/discoveries add pawn summary, setting, and tone, but skip relationship/opener,
  weapon unless combat, and prompt enchantment unless combat.

Pawn summaries may contain DLC identity lines, mood, health, low capacities, and top thoughts when
the policy includes `you:`. Surroundings include 1-2 nearby objects chosen with weighted random
selection, favoring important context such as fire, corpses, and buildings without making every entry
identical. Neutral arrival/death prompts parse curated facts from `gameContext` instead of dumping
the whole metadata string.

Prompt enchantments are XML Defs in `1.6/Defs/DiaryPromptEnchantmentDefs.xml`. When
`enablePromptEnchantments` is on, only policy-selected first-person prompts (currently
combat/crisis prompts) resolve enchantments and may add exactly one `prompt_enchantment:` field
after `persona:`.
Routine prompts do not scan enchantment Defs or consume a random enchantment roll. Each Def can
match live pawn state by visible hediff defName, active gene defName, highest royal-title defName, or
pawn `StatDef` / `PawnCapacityDef` below a threshold. Capacity thresholds can include `minValue` to
make exclusive ranges, such as Consciousness bands. Matching Defs first pass their
`chance`/`frequency` roll, then one winner is selected by `weight * severity`.

Visible hediff matchers can define optional `hediffSeverityTiers`. Four fixed code levels are
available: `minor` at severity 0.05, `moderate` at 0.25, `major` at 0.50, and `critical` at 0.75.
The highest configured level at or below the live hediff severity wins and may override `rule`,
`chance`/`frequency`, `weight`, and the weight-multiplier `severity`; omitted tier fields inherit
from the parent Def. If no tier matches, the parent Def is used. Neutral arrival/death prompts and
title follow-ups never use prompt enchantments.

The bundled starter catalog covers illness, blood loss, drug highs, non-luciferium chemical
addictions/withdrawals, luciferium dependency, chronic cognitive/sensory conditions, pregnancy,
hemogen craving, psychic bond trauma, and Royalty/Biotech/Anomaly hediffs such as abasia,
mindscrew, cube states, void exposure, corpse torment, inhumanization, and flesh appendages. Missing
body parts are intentionally not matched by the starter catalog. Royalty titles are sent through the
normal pawn-summary `title=` line rather than starter title enchantments. The starter enchantments do
include Consciousness capacity bands from dimmed awareness down to barely conscious; the separate
below-11% guard still skips generation before these prompt rules can apply.

System prompts are selected by narrative mode:

| Mode | Prompt |
|---|---|
| First-person diary | `systemPrompt` |
| End-of-day reflection | `systemPromptReflection` |
| Arrival/death chronicle | `systemPromptNeutral` |
| Title follow-up | `systemPromptTitle` |

Default prompt contracts ask for complete, short entries: diary pages 1-3 sentences / 35-75 words,
reflections 2-4 sentences / 50-90 words, neutral notes 1-3 sentences / 25-65 words. They tell the
model to use structured context as private evidence for voice, focus, and subtext, not as a checklist
to echo.

Player-customizable, Def-backed user-message instructions:

- `singlePovInstruction`
- `recipientFollowupInstruction`
- `deathDescriptionInstruction`
- `arrivalDescriptionInstruction`
- `titleUserInstruction`

Existing saves keep prompt overrides in `PawnDiarySettings`; Prompt Studio can reset one prompt or
all prompts to current XML defaults.

Persona presets start from `DiaryPersonaDef` XML, then settings may layer built-in overrides and
custom personas. `DiaryPersonas.WeightedStartingPersona` uses base weight, trait/backstory theme
matches from `PersonaAffinity`, a large creepjoiner bonus for `void` personas, and a soft duplicate
penalty for voices already used by living free colonists. Custom personas must keep at least one
allowed tag: `grim`, `warm`, `hostile`, `anxious`, `analytical`, `dramatic`, `social`,
`whimsical`, `noble`, or `void`. Saved records keep their persona defName and fall back to the
current default if it disappears. The merged effective catalog is cached and invalidated when
settings-backed personas change or are normalized after load.

Title generation defaults on. Successful main entries queue `QueueTitleRequest`, capped at
`TitleMaxTokens = 40` and pinned to the producing lane when possible. Stored titles render as
`date - title`; entries without titles render date-only. Pending titles use a header-level animated
placeholder aligned to the future title start. Title statuses are separate from main-entry statuses.

---

## 7. Settings And UI

Core settings:

| Setting | Default | Notes |
|---|---:|---|
| `apiEndpoints` | one enabled local lane | URL, model, key, enabled flag; blank-model or disabled rows are skipped. |
| `timeoutSeconds` | 30 | 5-300; per-lane-attempt deadline after a request leaves the queue. |
| `maxConcurrentRequests` | 4 | 1-16 per API lane; use 1 for a single local model. |
| `maxTokens` | 100 | API cap plus local sentence-aware response trimming. |
| `temperature` | 0.8 | 0-2. |
| `generateTitles` | true | Queues title follow-ups for successful main entries. |
| `enablePromptEnchantments` | true | Allows XML state rules to append one first-person `prompt_enchantment:` line. |
| `workGenerationWeight` / `socialGenerationWeight` | 1 | 0-5 multipliers for sampled work and batched-social promotion. |
| `systemPrompt*` | XML defaults | Diary, reflection, neutral, and title system prompts. |
| prompt instruction overrides | XML defaults | User-message prompt texts listed in section 6. |
| `groupEnabled` / `groupInstructions` | XML defaults | Per-group recording toggles and instructions. |
| `personaPresets` | empty | Built-in overrides plus custom personas. |
| dev/UI toggles | varies | API/persona/debug/generating-entry visibility. |
| `enableLlm` / `keepRawEntryOnFailure` | true | Currently forced on by `ClampValues`. |

The settings window contains Connection, Diary writing, Prompt Studio, Persona presets, and Events
sections. It supports multiple API lanes, model fetching/picking, prompt resets, single-card persona
editing, custom persona creation/deletion, grouped event toggles, and one per-group instruction
editor.

The production Diary tab shows only completed generated pages. Dev mode adds writing enablement,
persona picker, pending rows, raw/failure rows, prompt/status diagnostics, in-progress indicators,
and a test button that tops the selected pawn up to 360 completed mock pages without calling an LLM.

Long histories are paged by in-game year using each entry's saved display date. The center year label
opens a selector menu with visible years and page counts; adjacent buttons move newer/older. Within a
year page, the newest 15 visible entries open by default and older entries collapse to compact
date/title headers. Header clicks toggle expansion with lightweight animation and session-local
manual state. UI caches for first-seen fades and expansion blends are capped and periodically cleared.

Generated cards show date/title, group accent and chip, page tint, a subtle model id, linked previews
for the other pawn's POV, and title-pending animation. `DiaryTextFormat` converts light markdown
(`**bold**`, `*italic*`, headings, bullets, block quotes) and inline quoted speech to Unity rich
text. Social-tab log rows can jump to matching diary entries, including older year pages.

---

## 8. LLM Reliability

`LlmClient` treats each enabled endpoint+model row as an API lane:

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
  final fragments removed for main diary/note responses;
- background debug logs are queued and flushed on the main thread;
- stale sessions are cancelled when a new `DiaryGameComponent` is constructed;
- pending entries with no in-flight request reset only after two consecutive orphan scans.

If no enabled lane with a model exists, the entry fails with `PawnDiary.Error.NoApiConfigured` and
raw text is kept when configured.

---

## 9. Persistence

`DiaryGameComponent.ExposeData` saves `diaries` and `diaryEvents`. The `eventId -> DiaryEvent`
lookup index is rebuilt from `diaryEvents` on load so `FindEvent` stays O(1).

`DiaryEvent` saves raw text, generated text, statuses/errors, context, source ids, LLM metadata, and
per-POV titles. Full prompt strings are not persisted; dev diagnostics can show prompts built during
the current session. Tale/mental/source classification is inferred from stable `gameContext` markers
such as `tale=`, `mental_state=`, `mood_event=`, `thought=`, and `work=`.

Pending requests are not persisted. On load, pending main statuses reset to not-generated, and stale
pending title statuses without stored titles are cleared.

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
(`label`, `instruction`, `tone`, persona `rule`, prompt-enchantment `rule`, prompt defs) localizes
through DefInjected files.

Kept in English intentionally:

- prompt schema labels such as `event:`, `role:`, `sex=`, `thought=`;
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
node run.js --from-defs --all --save --model <model-name>
```

Saved prompt-lab results go to `prompt-lab/results/<model-name>/<timestamp>.md`, which is ignored by
git.

---

## 13. Changelog

Full history lives in [CHANGELOG.md](CHANGELOG.md). Add a dated entry there with every change.
