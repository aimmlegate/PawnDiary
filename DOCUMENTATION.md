# Pawn Diary - Architecture & Behavior

Current-state guide for the mod. When behavior or structure changes, update this file and add a
dated entry to [CHANGELOG.md](CHANGELOG.md).

_Last updated: 2026-06-18 (compact current-state docs)_

---

## 1. Purpose

Pawn Diary records meaningful moments for free colonists and rewrites them as short diary pages
through configured compatible LLM API lanes. RimWorld loads the compiled DLL at startup; Harmony
patches, `GameComponent`s, Defs, and inspector tabs are discovered by RimWorld lifecycle hooks.

The Diary tab is inserted after Needs and appears for colonists, including corpses. Animals,
prisoners, slaves, enemies, visitors, and non-colonist participants are ignored. Mixed events create
a solo entry for the eligible colonist. Pairwise colonist events generate initiator POV first, then
recipient POV with the initiator page as hidden continuity context.

Neutral arrival pages are forced to the first visible/generated page. Neutral death pages are
terminal; later same-tick events for that pawn are hidden and not generated.

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
|   |-- Core/                      DiaryGameComponent partials, event capture, batching
|   |-- Defs/                      Def classes and XML lookup helpers
|   |-- Generation/                context, prompt facade, LLM client
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
| `DiaryModStartup.cs` | Applies patches and injects the Diary tab. |
| `DiaryPatches.cs` | Harmony hooks for interactions, mental states, inspirations, tales, deaths, arrivals, crafts, relics, mood events, thoughts, and hediff signals. |
| `DiaryGameComponent*.cs` | Recording, batching, scans, save/load, lookup indexes, generation queueing, and public UI access. |
| `DiaryEvent.cs` / `PawnDiaryRecord.cs` | Saved event model and per-pawn event index/persona/generation toggle. |
| `DiaryPromptBuilder.cs` | Thin facade from saved event to typed prompt plan. |
| `Source/Pipeline/*` | DTO contracts plus pure prompt planning, response postprocessing, context parsing, and text decoration. |
| `DiaryContextBuilder.cs` / `DlcContext.cs` | Compact pawn/surroundings/relationship/health/weapon/context helpers; DLC reads are centralized and guarded. |
| `InteractionGroups.cs` | XML-backed classifiers for Interaction, MentalState, Tale, MoodEvent, Thought, Inspiration, Work, and Hediff. |
| `DiarySignalPolicyDef.cs` / `DiaryTuningDef.cs` | XML signal policies, scanner odds/cooldowns, and fallback/shared tuning. |
| `DiaryPromptDef.cs` / `DiaryPromptTemplateDefs.xml` | Shared system prompts, final instructions, and field templates. |
| `DiaryUiStyleDef.cs` / `DiaryTextDecorationDef.cs` | XML-owned display style and text-decoration rules. |
| `DiaryPersonaDef.cs` / `PersonaAffinity.cs` | XML personas and weighted first-persona selection. |
| `LlmClient.cs` / `LlmResponseParser.cs` | HTTP queue/failover/concurrency and pure provider response parsing. |
| `PawnDiaryMod.cs` / `PawnDiarySettings.cs` | Settings data, API lane editor, system-prompt overrides, Persona Presets editor. |
| `ITab_Pawn_Diary*.cs` / `DiaryTextFormat.cs` | Diary UI, paging, cards, linked previews, debug controls, and safe rich-text formatting. |
| `MiniJson.cs` | Runtime-safe JSON parser; do not replace with unsupported dependencies. |

---

## 3. Data Flow

1. A Harmony hook or scanner sees a candidate moment.
2. Source code checks pawn eligibility, XML group enablement, dedup windows, and source filters.
3. `AddSoloEvent` or `AddPairwiseEvent` creates a `DiaryEvent`, saves semantic `colorCue`,
   per-POV facts for text decoration, and references from each eligible pawn record.
4. Generation queues immediately where possible and is retried by periodic scans.
5. `DiaryPipelineAdapters` copies impure event/XML/localization/settings state into DTOs.
6. `DiaryPromptPlanner` returns a pure `DiaryPromptPlan`: template key, system prompt, user prompt,
   and `DiaryResponseRules`.
7. `LlmClient` sends the request, parses provider JSON, applies postprocessing, and returns results
   to the main thread.
8. `ApplyLlmResult` stores text or failure state; successful main entries may queue a title request.
9. `EntriesFor` reads saved events for the Diary tab. The tab caches built views until the pawn's
   render token changes.

`GameComponentTick` drains completed results, flushes dev debug logs, recovers orphaned pending
entries, and queues pending work roughly every 120 ticks. On load, pending statuses reset to
not-generated. Non-neutral POVs below 11% Consciousness are skipped; neutral arrival/death bypass
that guard. Scheduled scans snapshot free colonists before iterating.

---

## 4. Event Sources

| Source | Capture | Output |
|---|---|---|
| Social interactions | `PlayLog.Add` -> `RecordInteraction` | Pairwise, solo, pair batch, or ambient day note by XML group. Insults batch per pair; small talk, strange chat, animal, and teaching are usually ambient. |
| Mental states | `MentalStateHandler.TryStartMentalState` | `SocialFighting` pairwise when both pawns are eligible, otherwise solo; other accepted breaks are solo. |
| Tales | `TaleRecorder.RecordTale` | Solo or pairwise notable events; precise hooks and GameCondition-like tales are skipped to avoid duplicates. |
| Combat tales | Tale-domain batch policy | Non-death combat evidence becomes delayed per-pawn solo batches; death descriptions stay immediate neutral entries. |
| Synthetic tales | `QualityUtility.SendCraftNotification`, relic-install patch | Masterwork/legendary crafts and relic installs. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first-page arrival with prior faction/recruiter/kind/creepjoiner/surroundings context when available. |
| Deaths | `Pawn.Kill` prefix/postfix plus death TaleDefs | Neutral final page with cached cause/context; fallback covers natural deaths without duplicating tale-backed deaths. |
| Mood events | `GameConditionManager.RegisterCondition` | Once per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | Temporary memories filtered by XML thresholds/tokens; ambient thoughts can batch into one day note. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, and chemical stages record first tracked and later worsening stages; recovery writes nothing. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo entry for the inspired pawn. Word of Inspiration records through the resulting target inspiration. |
| Hediffs | `Pawn_HealthTracker.AddHediff` plus progression scan | XML Hediff groups choose Immediate or DayReflection, add/progression gates, severity steps, dedup, and weights. |
| Work | Periodic current-job sampling | Skips social/violent work, applies XML odds/cooldowns and `workGenerationWeight`, classifies as dark, passion, strain, or routine. |
| Day reflections | Sleep/rest trigger | One candidate per pawn from major events, opinion shifts, health signals, and low-weight filler when enabled. |

---

## 5. XML Policy

`1.6/Defs/DiaryInteractionGroupDefs.xml` owns group matching, instructions, color cues, batching,
promotion, Hediff policy, and default enablement. Domains are Interaction, MentalState, Tale,
MoodEvent, Thought, Inspiration, Work, and Hediff. Matching is domain-scoped by exact `defName` or
substring token; XML order matters and catch-all groups go last. All shipped groups default on.

Batch policy:

- Interaction `PairEvent`: merges quick rows for a pawn pair or def.
- Interaction `AmbientDayNote`: accumulates low-stakes rows per pawn/day into one solo memory.
- Tale batches: delayed solo entries per pawn; `talecombat` uses a 7,500-tick quiet window or ten
  events.
- Promotion: batched social rows can become immediate pairwise entries from opinion intensity,
  asymmetry, low needs, or extreme mood, multiplied by `socialGenerationWeight`.

Hediff policy lives on Hediff-domain groups. `<hediff>` controls mode (`Immediate` or
`DayReflection`), visible/bad/injury gates, severity thresholds, always qualifiers, add/progression
recording, severity steps, dedup, day-reflection weight, and optional localized text keys.

`DiarySignalPolicyDefs.xml` owns tracker-specific thought/work policy: thresholds, tokens, staged
progression, ambient batching, scan odds, and cooldowns. `DiaryTuningDef.xml` keeps shared fallback
tuning for mood/health buckets, nearby context, day-reflection weights, and scanner intervals. Code
fallbacks keep the mod usable if XML is absent.

---

## 6. Prompts, Personas, Titles

Prompts are compact `key: value` lines. Empty values and `none` / `n/a` / `unknown` sentinels are
dropped. Numeric context uses invariant dot decimals, but player-facing prompt facts prefer word
buckets over raw scores.

Prompt shape is selected by `DiaryPromptPlanner.TemplateKeyFor`: `PairDefault`, `PairImportant`,
`PairCombat`, `PairBatched`, `SoloDefault`, `SoloImportant`, `SoloInternalState`, `SoloBatched`,
`SoloDayReflection`, `DeathDescription`, `ArrivalDescription`, or `Title`. XML templates choose
fields and optional system/final-instruction overrides; `DiaryPromptDef.xml` supplies shared
defaults.

Layer boundaries:

- Event hooks, `DiaryGameComponent`, settings, XML lookup, localization, IO, RNG, and saved-game
  mutation are impure.
- `DiaryPipelineAdapters` is the only bridge from impure event/XML/localization state into DTOs.
- `DiaryPromptPlanner`, `PromptAssembler`, `DiaryContextFields`, `LlmResponseParser`,
  `DiaryResponsePostprocessor`, and `DiaryTextDecorations` are pure and testable.
- `LlmClient` is transport; `DiaryEvent`, title handlers, and UI are persistence/display adapters.

Personas come from `DiaryPersonaDef` plus settings overrides/custom rows. Weighted first persona
selection uses base weight, trait/backstory theme matches, creepjoiner bonuses for `void`, and a
soft duplicate penalty among living free colonists. Persona `rule` text is injected into the system
prompt for first-person templates. Neutral arrival/death and title prompts are persona-free.

Prompt enchantments are XML-weighted live health/capacity cues. When enabled, eligible first-person
templates may add exactly one localized `important health:` field selected from hediff/capacity
matches. Health cues are pressure, not the subject, unless the event itself is medical. Low
Consciousness is represented by capacity enchantments; it no longer changes persona text.

Direct speech is only allowed for initiator/single-POV interaction prompts, using closed
`[[speech]]...[[/speech]]` blocks for words plausibly spoken aloud by the POV pawn. Recipient
follow-up prompts forbid speech blocks and receive hidden initiator continuity.

Title generation defaults on. Successful main entries queue a capped title follow-up pinned to the
successful lane when possible. Titles store separately from main text and render as `date - title`;
entries without titles render date-only.

---

## 7. Settings And UI

Core settings:

| Setting | Default | Notes |
|---|---:|---|
| `apiEndpoints` | one local lane | URL, model, key, enabled flag, compatibility mode, and mode-specific knobs. Disabled or blank-model rows are skipped. |
| `timeoutSeconds` | 30 | 5-300 seconds per lane attempt. |
| `maxConcurrentRequests` | 4 | 1-16 per API lane. |
| `maxTokens` | 100 | API cap plus local sentence-aware trimming. |
| `temperature` | 0.8 | 0-2 sampling slider. |
| `generateTitles` | true | Queues title follow-ups. |
| `enableAtmosphericFormatting` | true | Enables display-only extreme-entry layout. |
| `enablePromptEnchantments` | true | Enables one live health/capacity cue in eligible prompts. |
| `workGenerationWeight` / `socialGenerationWeight` | 1 | 0-5 multipliers for work sampling and social promotion. |
| system prompt overrides | empty | Saved overrides for four shared prompts; blank uses XML. |
| event filters | XML defaults | `DiaryInteractionGroupDefs.xml` owns matching/defaults; old saved toggles are ignored. |
| `personaPresets` | empty | Built-in overrides plus custom personas. |

Settings UI includes Connection, Generation, Prompt Studio, and Persona Presets. API lanes support
OpenAI-compatible Chat Completions, OpenAI Responses, and native Ollama Chat, including model
fetch/pick, per-row connection tests, Responses reasoning effort, and Ollama thinking output.

Production Diary tab shows completed generated pages. Dev mode adds generation enablement, persona
picker, pending/raw/failure rows, prompt/status diagnostics, in-progress indicators, and a mock-page
fill button. Long histories page by in-game year; newest visible entries expand by default.

Cards show date/title, semantic accent and group chip, subtle model id, linked POV previews, and
title-pending animation. `DiaryUiStyleDef.xml` owns visual constants. `DiaryTextFormat` escapes raw
model rich-text tags, then converts light markdown and valid speech markers to Unity rich text.
`DiaryTextDecorationDefs.xml` owns display-only decorations such as staggered direct speech for
intoxication/anesthesia and Zalgo direct speech for anomaly/dark cues. Decorations are recomputed
from saved plain facts; generated text is not mutated on save.

Social-tab log rows can jump to matching diary entries, including older year pages.

---

## 8. LLM Reliability

Each enabled endpoint/model/mode row is an API lane. New requests round-robin across lanes.
Recipient follow-ups and title requests pin to the prior successful lane when possible. Per-lane
`SemaphoreSlim` guards enforce concurrency, and `ServicePointManager.DefaultConnectionLimit` is
raised so Mono does not cap a host at two connections.

`LlmClient` builds mode-specific URLs/payloads for Chat Completions, Responses, or Ollama Chat.
Requests carry ordered failover targets and retry transient errors up to three times per lane.
Timeouts, permanent errors, empty content, provider error statuses, and incomplete Responses/Ollama
bodies are surfaced as failure text. Successful responses are locally trimmed to `maxTokens`,
preferring complete sentences for diary/note text.

`LlmResponseParser` extracts provider text and strips reasoning/thinking output, including
structured Responses reasoning items, `<think>` blocks, lone leading `</think>`, reasoning fences,
and `Reasoning: ... Final:` prefixes. `DiaryResponsePostprocessor` applies prompt-time response
rules captured at queue time, so cleanup does not reread live game state.

Debug logs are flushed on the main thread only when the dev LLM debug toggle is enabled. API keys
are never logged or saved in event metadata. New game sessions cancel stale requests. Orphaned
pending entries reset only after two scans.

If no enabled lane has a model, the entry fails with `PawnDiary.Error.NoApiConfigured`.

---

## 9. Persistence

`DiaryGameComponent.ExposeData` saves `diaries` and `diaryEvents`; `eventId -> DiaryEvent` indexes
are rebuilt on load. `DiaryEvent` saves raw text, generated text, statuses/errors, context,
source ids, LLM metadata, semantic `colorCue`, titles, compact per-POV hediff/trait facts, legacy
staggered intensity, and capped pre-cleanup debug text. Full prompts and decorated rich text are not
persisted.

Classification is inferred from stable `gameContext` fields such as `tale=`, `mental_state=`,
`mood_event=`, `thought=`, `inspiration=`, `work=`, and `hediff=`, parsed through
`DiaryContextFields`. Pending requests are not persisted; pending statuses reset on load. Death and
arrival caches clear when a new session starts.

### TODO: Role-slot extraction

`DiaryEvent` still stores repeated `initiator*`, `recipient*`, and `neutral*` field families. A
future migration should add saved `DiaryEventRoleSlot` objects, hydrate slots from legacy fields on
load, keep role tokens stable (`initiator`, `recipient`, `neutral`), move callers to slot accessors,
and only later decide whether to retire direct legacy writes. Validate old-save load, solo/pairwise
events, arrival/death, titles, skipped low-Consciousness POVs, linked entries, and save/reload.

---

## 10. Runtime And DLC Constraints

- RimWorld runs on Unity Mono. Use only assemblies present in `RimWorldWin64_Data/Managed`.
- Do not add `System.Web.Extensions`, `JavaScriptSerializer`, or external JSON dependencies.
- The mod declares no paid DLC dependency. Optional DLC content must cleanly no-op when absent.
- Prefer XML string `defName` matchers for DLC-aware content; absent DLC defs simply never appear.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` and null checks.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use `GetNamedSilentFail` or XML
  string matching.
- XML references to DLC defs need `MayRequire`; plain string matcher lists do not.

---

## 11. Localization

Player-facing UI strings and natural-language prompt text live in
`Languages/English/Keyed/PawnDiary.xml` and are resolved with `.Translate()` on the main thread.
Def text (`label`, `instruction`, `tone`, persona `rule`, prompt defs/templates, hediff/body-part
labels) localizes through DefInjected. Prompt-enchantment and context connector words use Keyed
`PawnDiary.Prompt.Health.*` and `PawnDiary.Ctx.*` entries.

Kept English intentionally: prompt schema labels (`event:`, `role:`, `thought=`), role/sentinel
words (`initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`), internal defNames/theme tags,
and background-thread `LlmClient` debug/error strings.

To add a language, copy `Languages/English` to `Languages/<Language>`, translate Keyed values, and
optionally add DefInjected translations for XML Def text.

---

## 12. Build, Tests, Prompt Lab

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

`.githooks/verify.ps1` checks staged whitespace, XML well-formedness, pure tests, Debug MSBuild,
and committed-DLL freshness. If the build changes `1.6/Assemblies/PawnDiary.dll`, stage it and
retry. Emergency bypass: `PAWNDIARY_SKIP_VERIFY_HOOKS=1`.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
```

These harnesses compile without RimWorld/Unity assemblies; a Verse/XML Def dependency in pure code
should fail the tests at compile time.

Prompt lab:

```powershell
cd prompt-lab
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

Prompt lab mirrors XML prompt/group/persona/direct-speech policy, can cross every eligible group
with deterministic prompt-enchantment variants, and saves ignored markdown under
`prompt-lab/results/<model-name>/`.

---

## 13. Changelog

Full history lives in [CHANGELOG.md](CHANGELOG.md). Add a dated entry there with every change.
