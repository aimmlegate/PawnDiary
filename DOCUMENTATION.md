# Pawn Diary - Architecture & Behavior

Current-state guide for the mod. Keep this file focused on how the system works now. Keep
[CHANGELOG.md](CHANGELOG.md) grouped by milestone, not by individual commit.

_Last updated: 2026-06-22 (raid + quest event sources)_

---

## 1. Purpose

Pawn Diary records meaningful moments for free colonists and rewrites them as short diary pages
through configured compatible LLM API lanes. RimWorld loads the compiled DLL at startup; Harmony
patches, `GameComponent`s, Defs, and inspector tabs are discovered by RimWorld lifecycle hooks.

Diary ownership and first-person generation require a free humanlike colonist old enough for
`DiaryTuningDef.minimumFirstPersonAgeYears` (13 by default). Animals, prisoners, slaves, enemies,
visitors, non-colonist participants, and underage colonists do not own diary pages. Mixed events
become a solo entry for the eligible colonist. Pairwise colonist events generate initiator POV first,
then recipient POV with the initiator page as hidden continuity.

Neutral arrival pages are first-page entries. Neutral death pages are terminal: later same-tick
events for that pawn are hidden and not generated.

---

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, and `Languages/`. Source lives under `Source/`; build output
`1.6/Assemblies/PawnDiary.dll` is committed so a clone runs as a mod.

```text
PawnDiary/
|-- About/                         mod metadata and preview
|-- 1.6/
|   |-- Assemblies/PawnDiary.dll    committed build output
|   `-- Defs/                       groups, tuning, prompts, personas, UI/text policy
|-- Languages/                     Keyed and DefInjected localization
|-- Source/
|   |-- Capture/                   Event Catalog pure payloads/specs/registry
|   |-- Core/                      DiaryGameComponent partials, capture, batching, generation queue
|   |-- Defs/                      Def classes and XML lookup helpers
|   |-- Generation/                context builders, prompt facade, LLM client
|   |-- Models/                    saved/display models
|   |-- Patches/                   Harmony startup and hooks
|   |-- Pipeline/                  pure prompt/response/decor contracts
|   |-- Settings/                  settings data and settings UI
|   |-- UI/                        Diary inspector tab
|   `-- Util/                      MiniJson
|-- prompt-lab/                    Node prompt-testing harness
|-- tests/                         standalone pure-helper tests
|-- skills/                        repo workflow skills
`-- *.md                           docs
```

Key files:

| File | Role |
|---|---|
| `DiaryModStartup.cs` / `DiaryPatches.cs` | Startup, tab injection, Harmony hooks. |
| `Source/Capture/*` | Event Catalog: `DiaryEventType`, `XxxEventData`, `XxxEventSpec`, and `DiaryEventCatalog`. |
| `DiaryGameComponent*.cs` | Recording, batching, scans, save/load, lookup indexes, and generation queueing. |
| `DiaryEvent.cs` / `PawnDiaryRecord.cs` | Saved event model and per-pawn diary index/settings. |
| `DiaryPromptBuilder.cs` / `Source/Pipeline/*` | Prompt facade plus pure planning, response cleanup, domain recovery, and text decoration. |
| `DiaryContextBuilder.cs` / `DlcContext.cs` | Pawn/surroundings/relationship/health/weapon context; DLC reads are centralized and guarded. |
| `InteractionGroups.cs`, `DiarySignalPolicyDef.cs`, `DiaryTuningDef.cs` | XML classifiers, odds, cooldowns, scanner policy, and shared tuning. |
| `DiaryPromptDef.cs`, `PromptArchitectureDefs.cs`, `DiaryPersonaDef.cs`, `DiaryUiStyleDef.cs`, `DiaryTextDecorationDef.cs` | XML-owned shared prompts, event prompt policy, persona, UI, and display policy. |
| `LlmClient.cs` / `LlmResponseParser.cs` | HTTP queue/failover/concurrency and pure provider response parsing. |
| `PawnDiaryMod.cs` / `PawnDiarySettings.cs` | Settings data and settings UI. |
| `ITab_Pawn_Diary*.cs` / `DiaryTextFormat.cs` | Diary UI, cards, paging, debug controls, and safe rich-text formatting. |
| `MiniJson.cs` | Runtime-safe JSON parser. Do not replace with unsupported dependencies. |

---

## 3. Data Flow

1. A Harmony hook or scanner sees a candidate gameplay moment. Recorders no-op until RimWorld is in
   play, so startup pawn generation and scenario setup do not read calendar ticks too early.
2. The adapter snapshots live RimWorld facts, XML/settings gates, dedup facts, and any RNG result
   that must stay impure.
3. The Event Catalog decides `Drop`, immediate event creation, neutral prompt route, or a batch/
   ambient/day-reflection route.
4. `AddSoloEvent` or `AddPairwiseEvent` creates a saved `DiaryEvent`, semantic `colorCue`, per-POV
   decoration facts, and references from eligible pawn records.
5. Generation queues immediately when possible and is retried by periodic scans.
6. `DiaryPipelineAdapters` copies event/XML/localization/settings state into DTOs.
7. Pure pipeline helpers produce a prompt plan, parse provider output, and postprocess text.
8. `LlmClient` sends requests, handles failover/concurrency, and returns results on the main thread.
9. `ApplyLlmResult` stores success or failure state; title generation and optional generated-speech
   Social-log injection run after successful main entries.
10. `EntriesFor` reads saved events for the Diary tab, which caches built views until the pawn render
   token changes.

`GameComponentTick` drains completed results, flushes main-thread debug logs, recovers orphaned
pending entries, and queues pending work roughly every 120 ticks. Pending generation state resets on
load. Non-neutral POVs below 11% Consciousness are skipped; neutral arrival/death bypass that guard.

---

## 4. Event Sources

| Source | Capture | Output |
|---|---|---|
| Social interactions | `PlayLog.Add` -> `RecordInteraction` | Pairwise, solo, pair batch, or ambient day note by XML group. |
| Mental states | `MentalStateHandler.TryStartMentalState` | `SocialFighting` pairwise when both pawns are eligible; other accepted breaks are solo. |
| Romance relation changes | `Pawn_RelationsTracker.AddDirectRelation` | Pairwise entries for `Lover`, `Spouse`, `ExLover`, `ExSpouse`. |
| Tales and combat tales | `TaleRecorder.RecordTale` plus Tale batch policy | Solo, pairwise, delayed combat batches, or neutral death descriptions. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first-page arrival with prior faction/recruiter/kind/creepjoiner/surroundings context. |
| Deaths | `Pawn.Kill` prefix/postfix plus XML-marked death TaleDefs | Neutral final page with cached cause/context; fallback covers non-Tale deaths. |
| Mood events | `GameConditionManager.RegisterCondition` | Once per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | Temporary memories filtered by XML thresholds/tokens; ambient thoughts can batch. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, and chemical stages when they first appear or worsen. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo entry for the inspired pawn. |
| Hediffs | `Pawn_HealthTracker.AddHediff` plus progression scan | Immediate or day-reflection health entries by XML Hediff policy. |
| Work | Periodic current-job sampling | Skips social/violent work, applies XML odds/cooldowns and `workGenerationWeight`. |
| Raids | `IncidentWorker.TryExecute` (filtered to `IncidentWorker_Raid`) | Once per eligible colonist on the raid's target map. Minimal payload: incident defName, raider faction defName, raid points. |
| Quests | `Quest.Accept`, a defensive `MainTabWindow_Quests` accept-action fallback, a `Quest.EverAccepted` state scan, and `Quest.End` | Only accepted quests are recorded. `Success` -> "completed", `Fail` -> "failed"; one entry per eligible colonist per signal, with description, issuer faction, and rewards context. |
| Day reflections | Sleep/rest trigger | One reflective entry per pawn/day only when at least one XML-configured important signal kind exists. The default important kinds are major events, opinion shifts, and health signals; filler can be folded in as background but cannot trigger a reflection by itself unless XML allows it. |

`PlayLog.Add` preflights live pawn eligibility and XML significance before rendering RimWorld's POV
grammar strings, so routine social-log rows that cannot become diary entries stay cheap.

---

## 5. Event Catalog

`Source/Capture/` is the pure decision layer:

- `DiaryEventType`: one enum value per source.
- `XxxEventData : DiaryEventData`: primitive payload plus `static Decide(data, ctx)`.
- `CaptureContext`: precomputed eligibility, user/signal/ambient enablement, and tick facts.
- `CaptureDecision`: `Drop`, `GenerateSolo`, `GeneratePair`, `RouteBatch`, `RouteAmbient`,
  `RouteDayReflection`, `GenerateSoloDeathDescription`, `GeneratePairDeathDescription`, or
  `GenerateSoloArrivalDescription`.
- `XxxEventSpec`: wrapper registered in `DiaryEventCatalog`.

Currently catalog-backed: Thought, Inspiration, MoodEvent, MentalState, Tale, Hediff, Interaction,
Romance, Arrival, Death fallback, Work, ThoughtProgression, DayReflection, Raid, and Quest.

DayReflection is a meta-source: the adapter counts all candidate cues plus the subset that are
important enough to justify a reflection. `DiaryTuningDef.daySummaryImportantSignalKinds` controls
which candidate kinds are important (`event`, `opinion`, `hediff`, `filler`). Its pure `Decide`
drops days with no important candidates, so ambient small talk can color a reflection after
something meaningful happened but cannot create one alone by default.

Direct `AddSoloEvent`/`AddPairwiseEvent` call sites that remain outside `RecordXxx` are sinks:
interaction/tale batch flushers, ambient day-note flushers, the generation dispatcher, and the event
factory. They execute routes chosen by catalog-backed sources rather than deciding whether a new
gameplay source should be captured.

Adding a source:

1. Add a `DiaryEventType` value.
2. Add `Source/Capture/Events/XxxEventData.cs` with primitive fields, `Decide`, and any pure context
   string builder.
3. Add `Source/Capture/Specs/XxxEventSpec.cs`.
4. Register it in `DiaryEventCatalog`.
5. Update the impure adapter to snapshot facts, ask the catalog, then execute the returned route.
6. Add/update `DiaryCapturePolicyTests`, including catalog dispatch coverage.

Keep RimWorld/Verse/Unity objects, `.Translate()`, settings reads, `Find.TickManager`, IO, RNG,
dedup mutation, event creation, and LLM queueing in adapters. Pure code must accept DTOs/primitives.

---

## 6. XML Policy

`1.6/Defs/DiaryInteractionGroupDefs.xml` owns group matching, instructions, color cues, batching,
promotion, Hediff policy, and default enablement. Domains include Interaction, MentalState, Tale,
MoodEvent, Thought, Inspiration, Romance, Work, Hediff, Raid, and Quest. Matching is domain-scoped
by exact `defName` or substring token; XML order matters and catch-all groups go last. The Quest
domain is unusual: its matchDefNames are lifecycle signals (`accepted`/`completed`/`failed`), not
defNames, because one `DiaryEventType.Quest` fans out to three prompt groups. Saved Quest entries
still keep the real `QuestScriptDef` in their source defName/context; display and prompt-policy
recovery classify them from the saved `signal=` context field.

`DiarySignalPolicyDefs.xml` owns tracker-specific thought/work policy: thresholds, tokens, staged
progression, ambient batching, scan odds, and cooldowns. `DiaryTuningDef.xml` keeps shared fallback
tuning for mood/health buckets, nearby context, minimum first-person age, day-reflection important
signal kinds/weights, weather mention chances, and scanner intervals.

Hediff-domain groups define Immediate vs DayReflection mode, visible/bad/injury gates, severity
thresholds, always qualifiers, add/progression recording, severity steps, dedup, and weights.

Tale-domain groups can declare death victim role lists, keeping death Tale classification data-owned
and DLC/mod friendly.

---

## 7. Prompts And Personas

Prompts are compact `key: value` lines. Empty values and `none` / `n/a` / `unknown` sentinels are
dropped. Prompt templates include pair, solo, batched, day-reflection, death-description,
arrival-description, and title shapes.

The system prompts are intentionally short and only carry global safety/format rules. Event-type
prompt control lives in `DiaryEventPromptDef`: `prompt` renders as `event prompt:`, and `enhancement`
renders as `event enhancement:`. Narrower XML group policy still renders as `instruction:` and
`tone:`. This keeps quests, raids, thoughts, work, health, romance, and other source types tunable
without editing C# or bloating the shared system prompt. The first-person system prompt asks the
model to make supplied facts immediate through one sensory detail, one emotional beat, and one
implied consequence or tension, while still forbidding invented facts.

Prompt Studio in mod settings can save per-event overrides for an existing
`DiaryEventPromptDef.prompt` or `.enhancement`. Empty text or text matching the localized XML value
clears the override, so XML remains the default catalog and new event types still start in Defs.

Layer boundaries:

- Impure: event hooks, `DiaryGameComponent`, settings, XML lookup, localization, IO, RNG, save
  mutation, and transport.
- Bridge: `DiaryPipelineAdapters`.
- Pure: `DiaryPromptPlanner`, `PromptAssembler`, `DiaryContextFields`, `LlmResponseParser`,
  `DiaryResponsePostprocessor`, and `DiaryTextDecorations`.

Personas come from `DiaryPersonaDef` plus settings overrides/custom rows. Weighted selection uses
base weight, trait/backstory matches, creepjoiner bonuses, and duplicate penalties. Persona text is
used for first-person templates only; neutral arrival/death and title prompts are persona-free.

Prompt enchantments are XML-weighted live health/capacity cues. Eligible first-person prompts may
add one localized `important health:` field as pressure, not as the subject unless the event itself
is medical. These are separate from `DiaryEventPromptDef.enhancement`, which is static event-type
guidance.

Direct speech is allowed only for initiator/single-POV interaction prompts, using one closed
`[[speech]]...[[/speech]]` block when source notes support it. Recipient follow-ups forbid speech
blocks and receive hidden initiator continuity.

Generated speech Social-log injection is optional. When enabled, a completed initiator result with a
valid speech block adds one fresh RimWorld Social-log row and stores the generated text by `LogID`.
Known issue: the row is added to `Find.PlayLog` but may not appear in RimWorld's Social tab UI.

Title generation defaults on. Successful main entries queue a capped title follow-up pinned to the
successful lane when possible.

---

## 8. Settings And UI

Core settings include API lanes, timeout, max concurrency, max tokens, temperature, title generation,
atmospheric formatting, prompt enchantments, generated-speech injection, work/social generation
weights, system prompt overrides, per-event prompt/enhancement overrides, XML-backed event filters,
and persona presets. RimWorld dev mode also reveals prompt test mode in mod settings: real gameplay
events still assemble their system and user prompts, but the generation queue marks the POV as
prompt-only and never calls the LLM client. Those prompt-only cards are shown in the Diary tab while
dev mode is on so prompt formatting can be checked from live events without producing generated
diary text.

API lanes support OpenAI-compatible Chat Completions, OpenAI Responses, and native Ollama Chat,
including model fetch/pick, per-row connection tests, Responses reasoning effort, and Ollama
thinking output. Endpoint URLs normalize on load/save, not every settings draw, so users can edit or
clear the active text field without it being rewritten mid-typing. Logs strip query strings.

The Diary tab shows completed pages in production. Dev mode adds generation enablement, persona
picker, pending/raw/failure rows, prompt/status diagnostics, in-progress indicators, transient
formatting preview rows for prose, markdown, speech, combat/social-fight/mental/dark/death
colors, linked cards, writing placeholders, title-pending animation, and atmosphere checks, plus
mock-page fill. Long histories page by in-game year. Cards show date/title, accent, group chip,
model id, linked POV previews, and title-pending animation.

The dev-mode Diary controls also include a **Prompt suite** button (next to the mock-page filler).
It turns prompt test mode on and opens a dropdown of event categories — insult, social fight,
romance, mental break, hediff, inspiration, work, thought, mood event, tale, and day reflection.
The dropdown is driven by a single data-driven registry (`DiaryGameComponent.AllSuiteEntries`), so
adding a future category means appending one entry there and it auto-appears in the menu. Picking a
category deletes any prior test entry and captures exactly **one** prompt-only card for that
category (rendered plainly, with no decoration), routed through the normal generation queue; picking
another replaces it. A companion **Clear test prompts** button deletes every prompt-test entry from
all colonists' diaries. Pair categories also produce a recipient POV card in a second colonist's
diary and are omitted from the menu when no second colonist exists. Death and arrival shapes are
intentionally excluded: a synthetic death/arrival event would become that pawn's diary boundary (see
`ComputeDiaryBounds`) and hide the pawn's real pages, so those two shapes are still tested through
real gameplay hooks.

The Diary tab itself is sized by `tabWidth`/`tabHeight` in `DiaryUiStyleDef.xml`. In dev mode every
expanded entry card also shows a subtle copy button at the bottom-left of the card: clicking it copies the
card's text to the clipboard — the captured prompt for prompt-only cards, otherwise the generated
text — so prompts and output can be pasted out for inspection. The badge rests at ~0.5 alpha,
brightens on hover, and reserves a dev-only footer so it clears the model-name line drawn above it.

`DiaryUiStyleDef.xml` owns visual constants. `DiaryTextFormat` escapes raw model rich-text tags,
then converts light markdown and valid speech markers to Unity rich text. `DiaryTextDecorationDef`
owns display-only decorations; intoxication/anesthesia speech uses the strongest staggered word-size
setting so it is visibly impaired, extreme-dark speech dims selected words instead of using the
strange-chat Zalgo effect, combat/social-fight color cues add stronger hostile/conflict page washes
and header rules, mental-break pages use stronger fractured spacing with their own wash, and generated
text itself is not mutated on save. The Diary tab also highlights live humanlike pawn names mentioned
in rendered prose: colonists use their favorite color when available, slaves/prisoners/enemies/neutral
pawns use XML-backed status colors, and ambiguous or uncolored matches fall back to bold-only text.

---

## 9. LLM Reliability

Each enabled endpoint/model/mode row is an API lane. New requests round-robin across lanes. Recipient
follow-ups and title requests pin to the prior successful lane when possible. Per-lane semaphores
enforce concurrency, and `ServicePointManager.DefaultConnectionLimit` is raised for Mono.

`LlmClient` builds mode-specific URLs/payloads, retries transient errors up to three times per lane,
and surfaces timeout/permanent/empty/incomplete responses as failure text. Generation and model-list
HTTP bodies are streamed with hard byte caps before JSON parsing/logging, so a bad endpoint cannot
force an unbounded response string allocation. Successful responses are trimmed locally to
`maxTokens`, preferring complete sentences for diary/note text.

`LlmResponseParser` extracts typed visible output before fallback fields, falls back from blank
Ollama content to root `response`, and strips structured or transcript-style reasoning/thinking
before debug or save persistence. API keys are never logged or saved in event metadata. New game
sessions cancel stale requests. Orphaned pending entries reset only after two scans.

If no enabled lane has a model, the entry fails with `PawnDiary.Error.NoApiConfigured`.

---

## 10. Persistence

`DiaryGameComponent.ExposeData` saves `diaries` and `diaryEvents`. Event indexes and transient
day-reflection written guards are rebuilt on load, and per-pawn event-id lists prune blank,
duplicate, or dangling references.

`DiaryEvent` saves raw/generated text, statuses/errors, context, source ids, LLM metadata, semantic
`colorCue`, titles, assembled prompts, compact per-POV hediff/trait facts, legacy staggered
intensity, and capped pre-cleanup debug text. Decorated rich text is not persisted.

Classification is inferred from stable `gameContext` fields such as `tale=`, `mental_state=`,
`mood_event=`, `thought=`, `inspiration=`, `romance=`, `work=`, `hediff=`, `raid=`, and `quest=`.

Pending requests are not persisted. Pending statuses reset on load and scans requeue eligible work.
Death and arrival caches evict oldest stale entries at their cap and clear when a new session starts.

TODO: `DiaryEvent` still stores repeated `initiator*`, `recipient*`, and `neutral*` field families.
A future migration should introduce saved role-slot objects, hydrate them from legacy fields, move
callers to slot accessors, and only then consider retiring direct legacy writes.

---

## 11. Runtime And DLC Constraints

- RimWorld runs on Unity Mono. Use only assemblies present in `RimWorldWin64_Data/Managed`.
- Do not add `System.Web.Extensions`, `JavaScriptSerializer`, or external JSON dependencies.
- The mod declares no paid DLC dependency. Optional DLC content must cleanly no-op when absent.
- Prefer XML string `defName` matchers for DLC-aware content; absent DLC defs simply never appear.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` and null checks;
  Anomaly creepjoiner checks use the same centralized helper.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use `GetNamedSilentFail` or XML
  string matching.
- XML references to DLC defs need `MayRequire`; plain string matcher lists do not.

---

## 12. Localization

Player-facing UI strings and natural-language prompt text live in
`Languages/English/Keyed/PawnDiary.xml` and are resolved with `.Translate()` on the main thread.
Def text (`label`, `instruction`, `tone`, persona `rule`, prompt defs/templates, hediff/body-part
labels) localizes through DefInjected.

Keep DefInjected English stubs in sync for `DiaryInteractionGroupDef`, `DiaryEventPromptDef`,
`DiaryPersonaDef`, and `DiaryPromptDef` when editing XML labels, instructions, tones, event prompts,
persona rules, or shared prompts.

Kept English intentionally: prompt schema labels (`event:`, `role:`, `thought=`), role/sentinel
words (`initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`), internal defNames/theme tags,
and background-thread `LlmClient` debug/error strings.

To add a language, copy `Languages/English` to `Languages/<Language>`, translate Keyed values, and
optionally add DefInjected translations for XML Def text.

---

## 13. Build, Tests, Prompt Lab

Build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

Output is `1.6/Assemblies/PawnDiary.dll`. If `MSBuild` is not on `PATH`, locate it with `vswhere`
or use a Visual Studio Developer PowerShell.

Enable hooks:

```powershell
git config core.hooksPath .githooks
```

`.githooks/verify.ps1` checks staged whitespace, XML/project well-formedness, pure tests, Debug
MSBuild, committed DLL freshness, and Harmony freshness. Emergency bypass:
`PAWNDIARY_SKIP_VERIFY_HOOKS=1`.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
```

Pure test harnesses compile without RimWorld/Unity assemblies. `DiaryCapturePolicyTests` covers
Event Catalog decisions and dispatch. `DiaryPipelineTests` covers prompt planning and domain
recovery.

Prompt lab:

```powershell
cd prompt-lab
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

Release payloads are made with `scripts/publish.ps1`; it builds a throwaway Release DLL and copies
only runnable mod files into `dist/<published packageId>`.

---

## 14. Changelog Policy

`CHANGELOG.md` is a milestone history, not a commit log. Add or update a dated section only for
user-visible changes, architecture changes, migrations, important fixes, release work, or known
issues. Prefer one grouped bullet over several micro-entries.
