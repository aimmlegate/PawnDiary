# Pawn Diary - Architecture & Behavior

> Living design doc for the current mod. When behavior or structure changes, update this file and
> add a dated entry to [CHANGELOG.md](CHANGELOG.md) in the same change.

_Last updated: 2026-06-16 (first-release group catalog cleanup)_

---

## 1. What the mod does

Pawn Diary records meaningful moments for free colonists and asks an OpenAI-compatible
`/chat/completions` endpoint to rewrite them as short diary pages. The Diary inspector tab is
injected immediately after the vanilla Social tab and is visible only for colonist pawns, including
corpses of colonists.

Recorded sources:
- social interactions from `PlayLog.Add`
- social fights and mental breaks from `MentalStateHandler.TryStartMentalState`
- vanilla notable tales from `TaleRecorder.RecordTale`
- later colony arrivals from `Pawn.SetFaction`
- colonist deaths using `Pawn.Kill` plus death TaleDefs
- masterwork/legendary crafts and relic installs from narrow hooks
- mood-affecting game conditions from `GameConditionManager.RegisterCondition`
- temporary thoughts from `MemoryThoughtHandler.TryGainMemory`
- sampled work moments from current Work-tab jobs
- end-of-day reflections when a pawn lies down

Only `pawn.IsColonist` pawns receive diary entries. Animals, prisoners, slaves, enemies, and
visitors are ignored. Mixed events create a solo entry for the eligible colonist only. Pairwise
events between two eligible colonists use sequential POV generation: initiator first, then recipient
with the initiator entry included as hidden continuity context.

Deaths and arrivals are neutral chronicle entries rather than persona-based first-person pages.
Arrival entries are enforced as the first visible/generated page for a pawn; death entries are
terminal, so later same-tick events are hidden and not generated for that pawn.

---

## 2. Repository map

RimWorld loads `About/`, `1.6/`, and `Languages/`. C# source lives under `Source/`; the compiled
DLL is output to `1.6/Assemblies/PawnDiary.dll` and is committed so the mod runs from a clone.

```
PawnDiary/
|-- About/                       mod metadata
|-- 1.6/
|   |-- Assemblies/PawnDiary.dll  build output
|   `-- Defs/                     XML defs: groups, tuning, prompts, personas
|-- Languages/                   Keyed strings and DefInjected localization
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
|-- skills/                      shared repo skills
`-- *.md                         repo docs
```

Important files:

| File | Role |
|---|---|
| `DiaryModStartup.cs` | Applies Harmony patches and injects the Diary tab after Social. |
| `DiaryPatches.cs` | Patch entry points for interactions, mental states, tales, deaths, arrivals, crafts, relics, mood events, thoughts, and hediff day-summary signals. |
| `DiaryGameComponent*.cs` | Orchestrates recording, batching, generation scans, save/load, lookups, and public UI access. Event-source partials own their `Record*` method. |
| `DiaryEvent.cs` | Saved event model: raw text, context, statuses, generated text, LLM metadata, titles, source ids, and save/load. |
| `PawnDiaryRecord.cs` | Per-pawn event index, persona preset, and generation toggle. |
| `DiaryContextBuilder.cs` | Compact pawn, surroundings, atmosphere, relationship, health, passion, and opener context. |
| `DiaryPromptBuilder.cs` | Builds pairwise, solo, neutral arrival/death, and title prompts. |
| `LlmClient.cs` | Background HTTP queue, per-lane concurrency, retries, failover, deadlines, result queue, and main-thread debug log handoff. |
| `InteractionGroups.cs` | `DiaryInteractionGroupDef` and classifiers for Interaction, MentalState, Tale, MoodEvent, Thought, and Work domains. |
| `DiaryTuningDef.cs` | XML-backed thresholds/cooldowns/weights with safe code defaults. |
| `DiaryPromptDef.cs` | XML-backed instructions and system prompts. |
| `DiaryPersonaDef.cs` / `PersonaAffinity.cs` | XML personas plus trait/backstory/theme weighting for initial persona selection. |
| `DlcContext.cs` | One guarded home for DLC-gated pawn data (`xenotype=`, `title=`, `faith=`). |
| `MoodImpact.cs` | Shared positive/negative/neutral mood-impact tokens and classification. |
| `PawnDiaryMod.cs` / `PawnDiarySettings.cs` | Mod settings, API lane editor, model-list fetcher, group prompt editor. |
| `ITab_Pawn_Diary.cs` | Production diary view, dev diagnostics, linked-entry previews, animations, and click targets. |
| `MiniJson.cs` | Dependency-free JSON parser compatible with RimWorld's Unity Mono runtime. |

---

## 3. Data flow

All event sources funnel into `DiaryGameComponent`:

1. A Harmony hook or periodic scanner sees a candidate moment.
2. The source method checks group enablement, pawn eligibility, dedup windows, and source-specific
   filters.
3. `AddSoloEvent` or `AddPairwiseEvent` creates one `DiaryEvent`, adds it to `diaryEvents`, and
   stores the event id in each involved eligible pawn's `PawnDiaryRecord`.
4. Generation is queued immediately when possible and is also retried by background scans.
5. `LlmClient` sends the request on a selected API lane, reports results back to the main thread,
   and `ApplyLlmResult` stores generated text or failure state.
6. If title generation is enabled, a successful main entry queues a short title follow-up.
7. `EntriesFor` reads saved events for the tab. It does not trigger generation.

Background generation is driven from `GameComponentTick`. Every ~120 ticks it drains completed LLM
results, flushes queued debug logs, recovers orphaned pending entries, and queues pending diary work.
Loading a game normalizes pending statuses back to not-generated so they can be retried.

---

## 4. Event sources

### Interactions

`PlayLog.Add` forwards interaction log rows to `RecordInteraction`. The Interaction-domain group
decides whether the row is recorded, how it is labeled, whether it batches, and what instruction is
sent to the model. Small-talk, animal-handling, and teaching groups ship as ambient day notes rather
than immediate paired entries, with optional promotion for unusually salient moments.

### Mental states

`TryStartMentalState` records `SocialFighting` as pairwise when both pawns are eligible, or solo
when only one colonist is eligible. Other mental states become solo break entries for the breaking
colonist. Mirrored social-fight calls and repeated breaks are deduplicated.

### Tales and synthetic tale events

`TaleRecorder.RecordTale` extracts live pawns from vanilla tale subclasses and creates solo or
pairwise events. TaleDefs covered by more precise hooks are skipped, and GameCondition-like TaleDefs
are skipped so they are recorded once through the MoodEvent path.

Synthetic tale-style events cover:
- masterwork and legendary crafts from `QualityUtility.SendCraftNotification`
- ideology relic installation from `JobDriver_InstallRelic`

### Arrivals and deaths

Starting colonists receive neutral arrival pages after maps/free-colonist lists exist. Later joins
are detected through `Pawn.SetFaction` when a humanlike pawn becomes `Faction.OfPlayer`; the cache
captures prior faction, recruiter, pawn kind, creepjoiner flag, and surroundings.

Deaths use a `Pawn.Kill` prefix to cache cause details before RimWorld mutates health state. When a
death TaleDef is accepted, the victim's event is marked `death_description=true`, gets cached death
facts, and queues the neutral death prompt.

### Mood events, thoughts, work, and day reflections

Mood events are mood-affecting game conditions recorded once per eligible colonist on affected maps.
Thoughts are temporary memories filtered by XML tuning: ignored tokens, bypass tokens, eating
thresholds, minimum mood offset, ambient tokens, and dedup windows. Ambient thoughts can be folded
into the end-of-day reflection.

Work recording is sampled periodically rather than hook-based. It looks at
`CurJob.workGiverDef.workType`, skips social and violent work, applies XML-configured odds and
cooldowns, then classifies the moment as passionate, straining, routine, or dark-study work.

When `daySummaryEnabled` is true, sleep/rest triggers one `DayReflection` candidate per pawn using a
weighted selection of major day events, opinion shifts, major new afflictions, and low-weight filler.

---

## 5. Groups, batching, and tuning

`1.6/Defs/DiaryInteractionGroupDefs.xml` defines groups for six domains:

| Domain | Classifier | Typical groups |
|---|---|---|
| Interaction | `Classify(InteractionDef)` | romance, recruitment, ideology, anomaly, insults, animal, heartfelt, smalltalk |
| MentalState | `ClassifyMentalState(MentalStateDef)` | social fights, mental breaks |
| Tale | `ClassifyTale(TaleDef)` | combat/death, health/medicine, milestones, crafts, relics, raids, anomaly horror |
| MoodEvent | `ClassifyMoodEvent(GameConditionDef)` | positive, negative, mixed, passing moods |
| Thought | `ClassifyThought(ThoughtDef)` | positive, negative, passing thoughts |
| Work | `ClassifyWork(string)` | dark study, passionate, straining, routine |

Matching is domain-scoped by exact `defName` or substring token. XML order matters, with catch-all
groups last. Settings store per-group enabled flags and instruction overrides keyed by group
`defName`; missing settings use XML defaults.

Batch policies are XML data on groups. `PairEvent` merges quick rows for a pawn pair or def; 
`AmbientDayNote` accumulates low-stakes rows per pawn/day into one solo memory. Promotion policies
can let batched interactions escape into immediate pairwise events based on opinion intensity,
opinion asymmetry, low needs, or extreme mood. These signals are numeric/game-state based, not
localized text matching.

`1.6/Defs/DiaryTuningDef.xml` holds dedup windows, scanner intervals, mood/health/beauty buckets,
thought thresholds, work odds/cooldowns, and day-reflection weights. Code defaults keep the mod
usable if XML fails to load.

---

## 6. Prompts, personas, and titles

Prompts are compact `key: value` lines. `AppendField` drops empty values and `none` / `n/a` /
`unknown` sentinels so the model does not spend tokens on filler.

Main prompt context can include:
- `event`, `pov`, `role`, `with`, `what you saw` / `what happened`, and group `instruction`
- pawn summary: sex, age, DLC identity lines, mood, health, low capacities, and top thoughts
- persona rule, surroundings, pawn-state atmosphere, event tone, relationship continuity
- latest opener to avoid repetition
- burning passion and weapon only when the event is important or combat-related

Narrative mode chooses the system prompt at dispatch:
- `systemPrompt` for first-person diary pages
- `systemPromptReflection` for `DayReflection`
- `systemPromptNeutral` for neutral arrival/death chronicles
- `systemPromptTitle` for title follow-ups only

Persona presets are `DiaryPersonaDef` XML. A new pawn's first persona is selected with
`DiaryPersonas.WeightedStartingPersona`: every persona has base weight, matching trait/backstory
themes add weight through `PersonaAffinity`, and voices already used by living free colonists get a
soft duplicate penalty. Existing records keep their saved persona. Manual persona editing is a
dev-mode Diary-tab control.

Title generation defaults on. After a successful main entry, `QueueTitleRequest` sends the entry text
plus `DiaryPromptDef.titleUserInstruction`, capped at `TitleMaxTokens = 40`, pinned to the main
entry's lane when possible. Stored titles render as `date — title`; entries without stored titles
render date-only. There is no first-line fallback. Title statuses are separate from main-entry
statuses so orphan recovery does not touch them.

---

## 7. Settings and UI

Global settings live in `PawnDiarySettings`:

| Setting | Default | Notes |
|---|---:|---|
| `apiEndpoints` | one enabled local lane | URL, model, key, enabled flag; blank-model or disabled rows are skipped. |
| `timeoutSeconds` | 30 | 5-300; per-lane-attempt deadline after a request leaves the queue. |
| `maxConcurrentRequests` | 4 | 1-16 per API lane. Use 1 for a single local model. |
| `maxTokens` | 100 | API `max_tokens` plus local hard response cap. |
| `temperature` | 0.8 | 0-2. |
| `generateTitles` | true | Queues title follow-ups for successful main entries. |
| `systemPrompt*` | XML defaults | Diary, reflection, neutral, and title system prompts. |
| `groupEnabled` / `groupInstructions` | XML defaults | Per-group recording toggles and instruction overrides. |
| `showApiSettings` | true | Settings-window API editor expansion state. |
| `showPersonaSettings` / `showLlmDebugInfo` / `showGeneratingEntries` | false | Dev-mode Diary-tab preferences. |
| `enableLlm` / `keepRawEntryOnFailure` | true | Currently forced on by `ClampValues`. |

The settings window has Connection, Diary writing, System prompt, and Events sections. The API editor
supports multiple lanes, model fetching/picking, row enablement, add/remove, and reset. The Events
section shows grouped toggles by domain plus one per-group prompt editor.

The Diary tab production view shows only finished generated pages. Dev mode reveals per-pawn writing
enablement, persona picker, pending rows, raw/failure rows, prompt/status diagnostics, and in-progress
dot indicators. Generated cards show date/title, group accent and chip, roleplay text styling, a
subtle model id, and linked previews for the other pawn's POV on pairwise events. Social-tab log rows
can jump to matching diary entries.

---

## 8. LLM reliability

`LlmClient` treats each enabled endpoint+model row as an API lane:
- new requests are assigned round-robin
- pairwise recipient requests and title follow-ups pin to the lane that actually produced the prior
  text when possible
- lanes run independently, each guarded by its own `SemaphoreSlim`
- each request carries ordered failover targets and tries the next lane after permanent errors,
  exhausted retries, or timeout
- transient errors retry up to 3 times per lane; permanent 4xx and empty content move on
- successful content is trimmed locally to `maxTokens`
- debug logs from background workers are queued and flushed on the main thread
- stale sessions are cancelled when a new `DiaryGameComponent` is constructed
- pending entries with no in-flight request are reset only after two consecutive orphan scans

If no enabled lane with a model exists, the entry fails with `PawnDiary.Error.NoApiConfigured` and
raw text is kept when configured.

---

## 9. Persistence

`DiaryGameComponent.ExposeData` saves `diaries` and `diaryEvents`. `DiaryEvent` saves raw text,
generated text, statuses/errors, context, source ids, LLM metadata, and per-POV titles. Prompts are
rebuilt on demand rather than saved. Tale/mental/source classification is inferred from stable
`gameContext` markers such as `tale=`, `mental_state=`, `mood_event=`, `thought=`, and `work=`.

Pending requests themselves are not persisted. On load, pending main statuses reset to
not-generated, and stale pending title statuses without stored titles are cleared so both can retry.

---

## 10. Runtime constraints

- RimWorld runs this mod on Unity Mono. Only assemblies shipped in
  `RimWorldWin64_Data/Managed` are guaranteed at runtime.
- Do not add `System.Web.Extensions`, `JavaScriptSerializer`, or external JSON dependencies.
  Runtime JSON is parsed by `MiniJson`.
- The mod declares no paid DLC dependency. Optional DLC content must be reactive and safe when the
  DLC is absent.
- Prefer string `defName` matchers in XML for DLC-aware groups. DLC defNames simply never appear
  without the DLC.
- DLC-gated pawn data belongs in `DlcContext`, guarded by both `ModsConfig.<Dlc>Active` and null
  checks, returning empty strings when absent.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use `GetNamedSilentFail` or the
  matcher pattern.

---

## 11. Localization

Player-facing UI strings and natural-language prompt text are keys in
`Languages/English/Keyed/PawnDiary.xml`, resolved with `.Translate()` on the main thread. Def text
(`label`, `instruction`, `tone`, persona `rule`, prompt defs) localizes through DefInjected files.

Kept in English intentionally:
- prompt schema labels such as `event:`, `role:`, `sex=`, `thought=`
- role/sentinel words such as `initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`
- internal defNames, trait keys, backstory categories, and persona theme tags
- background-thread `LlmClient` error strings and dev/debug diagnostics

To add a language, copy `Languages/English` to `Languages/<Language>`, translate Keyed values, and
optionally add DefInjected translations for XML Def text.

---

## 12. Building and prompt lab

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
