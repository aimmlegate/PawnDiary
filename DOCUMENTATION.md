# Pawn Diary - Maintainer Guide

Last updated: 2026-06-26

Related files:

- `AGENTS.md`: detailed rules for code agents and deep architecture constraints.
- `EVENT_PROMPT_MAP.md`: event-to-prompt coverage map.
- `CHANGELOG.md`: milestone history.

## 1. Purpose

Pawn Diary records meaningful RimWorld colony moments and turns them into short diary pages through
configured OpenAI-compatible API lanes. RimWorld loads the compiled DLL at startup through Harmony
patches, Defs, a `GameComponent`, and an inspector tab. There is no `main()`.

Diary pages belong only to free humanlike colonists old enough for first-person writing
(`DiaryTuningDef.minimumFirstPersonAgeYears`, default 13). Animals, prisoners, slaves, enemies,
visitors, non-colonists, and underage colonists do not own pages. If only one participant is eligible,
the event becomes a solo entry. If two eligible colonists are involved, the initiator entry is
generated first and the recipient entry gets hidden continuity from it.

Arrivals and deaths are neutral boundary pages. Arrival pages introduce a pawn's diary; death pages
end it and hide later same-tick events for that pawn.

## 2. Repository Map

RimWorld loads `About/`, `1.6/`, `Languages/`, and the compiled DLL in
`1.6/Assemblies/PawnDiary.dll`. Source and tests are kept in the repo for development.

| Path | Role |
|---|---|
| `About/` | Mod metadata, preview, icon, dependency declaration. |
| `1.6/Defs/` | XML-owned policy: event groups, tuning, prompts, styles, UI, text effects. |
| `Languages/` | Keyed and DefInjected English text. |
| `Source/Capture/` | Pure Event Catalog payloads and decisions. |
| `Source/Core/` | `DiaryGameComponent` partials: capture adapters, save/load, scans, generation queue. |
| `Source/Generation/` | Runtime context builders, prompt adapters, LLM client, DLC-safe live reads. |
| `Source/Pipeline/` | Pure prompt planning, request JSON, response cleanup, text decoration, API policy. |
| `Source/Patches/` | Harmony startup, domain hooks, inspect-tab/command patches. |
| `Source/Settings/` | Saved settings, API lane UI/controller, Prompt Studio, Writing Style Studio. |
| `Source/UI/` | Diary inspect tab, card rendering, paging, formatting. |
| `tests/` | Standalone pure-helper test projects. |
| `prompt-lab/` | Prompt fixture and variant validation harness. |
| `scripts/publish.ps1` | Local Workshop payload prep. |

## 3. Runtime Flow

1. A Harmony hook or scanner notices a candidate event.
2. The adapter snapshots live RimWorld facts, settings/XML gates, dedup state, and any random roll.
3. The pure Event Catalog decides whether to drop, create a solo/pair/neutral entry, batch, or route
   to day reflection.
4. `AddSoloEvent` or `AddPairwiseEvent` creates a saved `DiaryEvent` and indexes it on eligible
   pawn records. The active-event retention cap then drops oldest events beyond the configured
   limit and scrubs pawn references to them.
5. Generation queues immediately when possible; periodic scans retry pending or orphaned work.
6. `DiaryPipelineAdapters` copies runtime/XML/localized state into pure pipeline DTOs.
7. Pure helpers assemble prompts, serialize request JSON, parse provider responses, and clean text.
8. `LlmClient` sends requests and returns results to the main thread; successful main pages may queue
   a title follow-up.

`GameComponentTick` runs game-time scans and generation rescans roughly every 200 ticks. Orphaned
"writing..." recovery is a separate, slower full-history pass every 600 ticks. Completed LLM results
and debug logs are drained from both `GameComponentTick` and `GameComponentUpdate`, so requests that
were already queued can finish and apply while the game is paused. Pending generation is not saved;
it resets on load and is requeued. First-person generation is skipped for pawns below the XML
Consciousness floor, while neutral arrival/death pages bypass that guard.

## 4. Event Sources

| Source | How it is observed | Result |
|---|---|---|
| Social interactions | `PlayLog.Add` | Pair, solo, batched, or ambient note by XML group. |
| Mental states | `MentalStateHandler.TryStartMentalState` | Social fighting can be pairwise; other breaks are solo. |
| Romance | `Pawn_RelationsTracker.AddDirectRelation` | Pairwise lover/spouse/ex relation moments. |
| Tales and combat | `TaleRecorder.RecordTale` | Solo, pair, delayed combat batches, or death description. |
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first page. |
| Deaths | `Pawn.Kill` plus XML death TaleDefs | Neutral final page. |
| Mood events | `GameConditionManager.RegisterCondition` | One entry per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | XML-filtered memory entries; ambient thoughts can batch. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, chemical, and similar worsening stages. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo inspiration entry. |
| Hediffs | `Pawn_HealthTracker.AddHediff` and scan | Immediate or day-reflection health entries by XML policy. |
| Work | Periodic current-job sampling | Non-social, non-violent work, controlled by XML odds/cooldowns. |
| Raids and infestations | `IncidentWorker.TryExecute` | Fan-out to eligible colonists; ordinary raids can delay generation. |
| Quests | `Quest.Accept`, `Quest.End`, defensive UI/state scan | Accepted, completed, and failed quest entries. |
| Rituals | Ideology and psychic ritual completion hooks | Fan-out by role/perspective when DLC content is active. |
| Abilities | `Ability.Activate` overloads | Cooldown-weighted caster entry. |
| Day reflections | Sleep/rest trigger | One reflective page per pawn/day when important signals exist. |

Hooks are grouped by domain under `Source/Patches/`. Fragile reflection targets register through
`DiaryPatchRegistrar` so missing methods warn and no-op instead of breaking startup. Capture hooks,
per-tick work, save/load bookkeeping, startup registration, and vanilla UI overlays isolate failures
with one-time logging and preserve vanilla behavior.

## 5. XML Policy

Most feature tuning lives in XML so changes do not require recompiling:

- `DiaryInteractionGroupDefs.xml`: event classification, instructions, tones, color cues, batching,
  hediff modes, package-aware compatibility routing, and default enablement.
- `DiaryEventPromptDefs.xml`: event prompt text, event enhancement text, and optional forced model.
- `DiaryPromptTemplateDefs.xml`: which structured fields each prompt shape renders.
- `DiaryPromptDef.xml`: shared system/final instructions.
- `DiaryPersonaDefs.xml`: built-in writing styles.
- `DiaryPromptEnchantmentDefs.xml` and `DiaryHumorCueDefs.xml`: optional live-context and subtle
  humor cues.
- `DiarySignalPolicyDefs.xml` and `DiaryTuningDef.xml`: scan intervals, odds, cooldowns, thresholds,
  day-reflection policy, and shared fallback tuning.
- `DiaryUiStyleDef.xml` and `DiaryTextDecorationDefs.xml`: Diary UI dimensions/colors and display
  rich-text decoration.

Interaction groups match by domain, exact `defName`, substring token, and optional source package
ID. Event prompt policy resolves from most specific to broadest key: source defName, interaction
group, classifier key, then domain. Prompt text, enhancement text, and forced-model text resolve
independently so narrow XML can override one field and inherit the others.

For optional DLC or mod content, prefer string matchers in XML. Do not hard-reference DLC defs in C#
or XML unless they are guarded; absent DLC content should simply never match.

## 6. Prompts And Writing Styles

Prompts are compact `key: value` lines. Empty values and `none`/`n/a`/`unknown` sentinels are dropped.
Prompt templates cover pair, solo, batched, day-reflection, neutral death, neutral arrival, and title
requests.

System prompts stay short and general. Event-specific guidance comes from `DiaryEventPromptDef` and
per-group instructions/tones. Groups can define instruction/tone variant pools; instructions roll once
at capture and are saved, while tones are deterministic by event id.

Prompt Studio edits shared system prompts and event prompt/enhancement/forced-model overrides.
Writing-style presets are saved settings backed by `DiaryPersonaDef`; the code still uses "persona"
in some field names for save compatibility, but the player-facing feature is writing style.

Prompt enchantments add one weighted live-context pressure cue to eligible first-person prompts.
Humor cues are hidden, XML-weighted, and folded into the writing-style block. Direct speech is allowed
only in selected first-person interaction prompts with a closed `[[speech]]...[[/speech]]` block.

Generated Social-log speech injection remains disabled/hidden. The saved setting exists for
compatibility, but the call site is off. Title generation is enabled by default. Successful main
entries queue their own title follow-up immediately; the full missing-title sweep runs only after
load or when settings are saved, not on every generation rescan.

## 7. Settings And UI

Main settings cover API lanes, routing mode, request tuning, title generation, atmospheric
formatting, prompt enchantments, work/social weights, the active diary-event cap, event filters,
prompt overrides, and writing style presets. Dev mode exposes prompt-test mode and extra
diagnostics.

The Diary UI is an inspect tab registered for humanlike pawns and their corpse defs. By default it
appears in the pawn inspect-tab row for eligible colonists and selected colonist corpses. A setting can
instead hide the tab and add a bottom command button that opens the same UI.

Production UI shows completed pages. Dev mode also shows pending/failure rows, raw prompt/status
data, formatting previews, prompt-suite tools, copy buttons, and regeneration controls. Histories page
by in-game year; newest cards start expanded. Long histories are kept cheap by the active-event cap,
visible-entry caching, height caching, and viewport culling.

`DiaryTextFormat` escapes raw model rich text before applying safe formatting. Display-only text
decorations and pawn-name highlights happen at render time; generated text is not mutated on save.

## 8. API And Reliability

Each enabled endpoint/model/mode/auth row is an API lane. Supported request modes are OpenAI-compatible
Chat Completions and OpenAI Responses. Auth can be bearer, no auth, custom API-key header, or `key=`
query parameter. Logs strip secrets and query strings.

Routing modes are Balanced, Prefer top rows, and Failover only. A `DiaryEventPromptDef.forcedModel`
can try a matching active model first; blank, unknown, disabled, or failed forced lanes fall back to
normal routing. Recipient follow-ups and title requests try to pin to the previous successful lane.

`LlmClient` handles concurrency, per-lane cooldowns, transient retries, timeout/permanent failures,
session cancellation on new game/load, and result handoff to the main thread. `LlmResponseParser`
supports Chat and Responses output shapes, strips reasoning/transcript leaks, cleans malformed speech
markers, and trims saved text locally.

## 9. Save Data And Compatibility

`DiaryGameComponent.ExposeData` saves per-pawn records (`diaries`) and the event list (`diaryEvents`).
`DiaryEventRepository` owns the event list and rebuilds its id lookup index after load. Per-pawn event
lists prune blank, duplicate, or dangling refs.

The temporary active-event cap keeps only the newest configured number of `DiaryEvent` records
(default 1000, editable from settings). It runs after load, before save, after new event creation,
and when settings are saved. Trimmed events are removed from the master list and from every pawn's
event-id list, so older pages are no longer visible and background scans do not iterate them.

`DiaryEvent` saves raw/generated text, statuses/errors, context, source ids, prompts, titles, LLM
metadata, semantic color cue, and compact display facts. Per-role state is stored in initiator,
recipient, and neutral slots but serialized under the historical flat Scribe keys.

Adding Pawn Diary to an existing save is safe; it starts recording future events. Removing it is
gameplay-safe because the mod does not persist custom pawn/map components or gameplay defs onto
vanilla objects. The diary UI/history disappears without the mod, and old generated Social-log rows
from dev builds may remain in the vanilla play log.

## 10. Runtime And DLC Constraints

- Runtime is RimWorld's Unity Mono. Use only assemblies available in `RimWorldWin64_Data/Managed`
  plus the bundled/declared Harmony dependency.
- JSON uses `Source/Util/MiniJson.cs`. Do not add `System.Web.Extensions` or external JSON libraries.
- Harmony is declared in `About/About.xml`; runtime and build copies of `0Harmony.dll` are kept in
  `1.6/Assemblies/` and `Source/Libs/`.
- No paid DLC is required. Optional DLC data must no-op cleanly when absent.
- DLC pawn data belongs in `DlcContext`, guarded by `ModsConfig.<Dlc>Active` and null checks.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content; use string matching or
  `GetNamedSilentFail`.

## 11. Localization

Player-facing UI strings and natural-language prompt text use
`Languages/English/Keyed/PawnDiary.xml` through `.Translate()` on the main thread. Def text uses
DefInjected translations.

Keep DefInjected English stubs in sync when editing XML labels, instructions, tones, event prompts,
writing-style rules, or shared prompts. Variant pools use indexed DefInjected keys such as
`<group.instructions.0>`; avoid blank list entries so indices stay aligned.

Raw English is intentional for prompt schema labels (`event:`, `role:`, `thought=`), internal role
and sentinel tokens (`initiator`, `recipient`, `neutral`, `none`, `n/a`, `unknown`), defNames, API
model ids, and background-thread `LlmClient` errors.

## 12. Build, Tests, Prompt Lab

Build:

```powershell
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

If `MSBuild` is not on `PATH`, use Visual Studio Developer PowerShell or locate it with `vswhere`.
The Debug build writes `1.6/Assemblies/PawnDiary.dll`, which is committed on purpose.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
dotnet run --project tests/PromptVariantsTests/PromptVariantsTests.csproj
```

Prompt lab:

```powershell
cd prompt-lab
npm test
npm run from-defs
node run.js --from-defs --save --model <model-name>
node run.js --all-variants --passes 2 --save --no-title --model <model-name>
```

Live hook checks use a disposable save, dev mode, prompt-test mode, and RimBridge/GABS. Prompt-test
mode intercepts only after a real event reaches `QueuePrompt`; a successful capture logs:

```text
[PawnDiary debug] Captured prompt without generation event=<id> role=<role>
```

Release payloads are prepared with:

```powershell
scripts\publish.ps1
```

The script builds a throwaway Release DLL and copies runnable mod files, runtime textures, `Source/`
without `bin`/`obj`, and reference docs into `dist/<published packageId>`.

## 13. When Changing The Mod

- Follow `AGENTS.md` for detailed architecture, DLC, localization, and validation rules.
- Keep tunable policy in XML when possible.
- Keep live RimWorld objects at adapter/UI/transport edges; pure helpers should use DTOs/primitives.
- Add or update focused pure tests when changing pure logic.
- Update `DOCUMENTATION.md` and `CHANGELOG.md` for behavior, structure, release, or workflow changes.
