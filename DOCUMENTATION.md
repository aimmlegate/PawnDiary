# Pawn Diary - Architecture & Behavior

Current-state guide for the mod. When behavior or structure changes, update this file and add a
dated entry to [CHANGELOG.md](CHANGELOG.md).

_Last updated: 2026-06-21 (priority review fixes)_

---

## 1. Purpose

Pawn Diary records meaningful moments for free colonists and rewrites them as short diary pages
through configured compatible LLM API lanes. RimWorld loads the compiled DLL at startup; Harmony
patches, `GameComponent`s, Defs, and inspector tabs are discovered by RimWorld lifecycle hooks.

The Diary tab is inserted after Needs and appears for colonists, including corpses. Animals,
prisoners, slaves, enemies, visitors, non-colonist participants, and colonists below
`DiaryTuningDef.minimumFirstPersonAgeYears` (13 by default) are ignored for diary ownership and
generation. Mixed events create a solo entry for the eligible colonist. Pairwise colonist events
generate initiator POV first, then recipient POV with the initiator page as hidden continuity
context.

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
|   |-- Capture/                   Event Catalog: pure event types, payloads, decision reducers, registry
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
| `DiaryPatches.cs` | Harmony hooks for interactions, mental states, inspirations, tales, deaths, arrivals, mood events, thoughts, and hediff signals. |
| `Source/Capture/*` | Event Catalog: `DiaryEventType` enum (the source list), `XxxEventData` payloads, pure `XxxEventData.Decide` reducers, and `DiaryEventCatalog` dispatch. The "should this event be recorded?" decision for migrated sources lives here, unit-tested without RimWorld. See §4a. |
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

1. A Harmony hook or scanner sees a candidate moment. Gameplay event recorders no-op until RimWorld
   is actually in play, so startup pawn generation/scenario setup never reads calendar ticks before
   the world clock exists.
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
   Load/tick sweeps requeue missing titles for completed entries when title generation is enabled.
   If Social-log speech injection is enabled, the first valid initiator `[[speech]]...[[/speech]]`
   block also creates one new `PlayLogEntry_Interaction` after the LLM result is ready.
9. `EntriesFor` reads saved events for the Diary tab. The tab caches built views until the pawn's
   render token changes, and reuses filtered/year/measurement buffers between draw frames.

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
| Arrivals | Starting-colonist scan and `Pawn.SetFaction` | Neutral first-page arrival with prior faction/recruiter/kind/creepjoiner/surroundings context when available. |
| Deaths | `Pawn.Kill` prefix/postfix plus XML-marked death TaleDefs | Neutral final page with cached cause/context; XML marks which Tale pawn slot is the victim; fallback covers natural deaths without duplicating tale-backed deaths. |
| Mood events | `GameConditionManager.RegisterCondition` | Once per eligible colonist on affected maps. |
| Thoughts | `MemoryThoughtHandler.TryGainMemory` | Temporary memories filtered by XML thresholds/tokens; ambient thoughts can batch into one day note. |
| Thought progression | Periodic scan | Hunger, rest, outdoors, and chemical stages record first tracked and later worsening stages; recovery writes nothing. |
| Inspirations | `InspirationHandler.TryStartInspiration` | Solo entry for the inspired pawn. Word of Inspiration records through the resulting target inspiration. |
| Hediffs | `Pawn_HealthTracker.AddHediff` plus progression scan | XML Hediff groups choose Immediate or DayReflection, add/progression gates, severity steps, dedup, and weights. |
| Work | Periodic current-job sampling | Skips social/violent work, applies XML odds/cooldowns and `workGenerationWeight`, classifies as dark, passion, strain, or routine. |
| Day reflections | Sleep/rest trigger | One candidate per pawn from major events, opinion shifts, health signals, and low-weight filler when enabled. |

---

## 4a. Event Catalog — how to add a new event source

`Source/Capture/` is a small registry layer that lets new gameplay sources plug into the diary with
one predictable shape. It is the C#/RimWorld analog of Redux "action types + reducers": a single
enumerated list of every source the diary can react to, a typed payload for each, and a pure reducer
that decides what to do with one. The impure RimWorld-facing half (label resolution, localized text,
game-context string, save mutation, LLM queue) stays in the existing `DiaryGameComponent.RecordX`
methods. This split keeps the decision unit-testable without RimWorld assemblies.

Anatomy:

- `DiaryEventType` — one enum value per source. The list is the diary's coverage at a glance.
- `XxxEventData : DiaryEventData` — primitive payload captured from the live RimWorld hook (defName,
  magnitude, duration, ...). Plus a pure `static Decide(data, ctx) → CaptureDecision`.
- `CaptureContext` — pre-computed impure facts (eligibility, user/signal/ambient enable flags, game
  tick). Filled by the caller so the reducer never touches DefDatabase/Settings/tick manager.
- `CaptureDecision` — `Drop` / `GenerateSolo` / `GeneratePair` / `RouteAmbient`. MentalState is the
  first migrated source using `GeneratePair` (social fights); future pair sources (romance, raid
  pairs) reuse it.
- `DiaryEventSpec` — abstract reducer wrapper, one subclass per source, registered in
  `DiaryEventCatalog` so callers dispatch with a single `catalog.Get(type).Decide(...)`.

Migrated in the current slice: **Thought** (richest source — token filter, general/eating magnitude
threshold, bypass tokens, ambient routing), **Inspiration** (trivial source — eligibility + user
toggle only), **MoodEvent** (first multi-pawn fan-out — one GameCondition → one solo entry per
affected colonist, fan-out loop stays in `RecordMoodEvent`), **MentalState** (first pair source
— social fights emit `GeneratePair`, every other break emits `GenerateSolo`), and **Tale** (partial
migration — drop-gate is in the catalog; the batch/death/pair/solo shape dispatch stays in
`RecordTale` because the current `CaptureDecision` does not encode those outcomes). The other
sources in §4 still use their pre-Catalog `RecordX` code; they migrate source-by-source in later
slices.

Adding a new source (the 4-step recipe):

1. Add a value to `DiaryEventType`.
2. Write `XxxEventData : DiaryEventData` with primitive fields + a pure `Decide(data, ctx)` (and any
   per-source policy snapshot type it needs, like `ThoughtCapturePolicy`).
3. Write `XxxEventSpec : DiaryEventSpec` that delegates `Decide` to the payload.
4. `Register(new XxxEventSpec())` in `DiaryEventCatalog.EnsureInitialized`. The hook side calls
   `DiaryGameComponent.RecordXxx`, which snapshots facts, asks the catalog, and performs the impure
   side-effects.

Adding per-source tunable content (weights, prompt-template keys, RNG filters) belongs on the
`XxxEventSpec` class or in a dedicated XML Def — not in the base contract — so sources stay
independent. Existing `DiarySignalPolicyDef`/`DiaryInteractionGroupDef` already own today's tokens,
thresholds, dedup windows, and prompt policy; new sources either reuse them or add their own Def
following the same shape. See `AGENTS.md` rule 2 (impure listener → plain context → pure decision →
impure sink) and rule 3 (XML owns tunable values).

### Migration recipe — step-by-step

When migrating an existing source (or adding a net-new one) to the Event Catalog, follow this
checklist. `tests/DiaryCapturePolicyTests` enforces steps 1+5 automatically — it fails if a new enum
value has no Spec (`TestCatalogContract`) or if a planned source appears in the enum without being
removed from the sentinel list (`TestMigrationSentinel`).

1. **Enum entry.** Add a value to `DiaryEventType`. (The contract test will now fail until step 5.)
2. **Payload + pure decision.** Write `Source/Capture/Events/XxxEventData.cs` with primitive fields
   + a `static Decide(XxxEventData, CaptureContext) → CaptureDecision`. If the source has token /
   threshold / weight policy, add a frozen policy snapshot type (like `ThoughtCapturePolicy`) and
   pass it on the payload. `Decide` must not touch RimWorld types.
3. **Pure game-context builder.** Write `XxxEventData.BuildGameContext(...)` (and any other pure
   string assembly the source needs). The leading `"xxx="` marker is load-bearing — the UI parses
   it to recover the domain. Add a format test to `DiaryCapturePolicyTests` so the format is locked.
4. **Spec wrapper.** Write `Source/Capture/Specs/XxxEventSpec.cs` that delegates `Decide` to the
   payload's static method. (Future per-source metadata — `Weight`, `PromptKey`, RNG filters —
   belongs here as fields/properties, not in the base contract.)
5. **Registration.** Add `Register(new XxxEventSpec())` to `DiaryEventCatalog.EnsureInitialized`.
6. **Impure RecordX rewrite.** Update `DiaryGameComponent.RecordXxx` to: snapshot live facts into
   `XxxEventData` + `CaptureContext` → `catalog.Get(type).Decide(...)` → if `Drop` return → perform
   dedup → call the pure `BuildGameContext` → create `DiaryEvent` via `AddSoloEvent` (or
   `AddPairwiseEvent` for pair sources) → queue LLM. The RecordX should contain NO business logic,
   only Verse reads + sink side-effects.
7. **Sentinel cleanup.** If the source was listed in `PlannedNotYetMigratedSources` in
   `DiaryCapturePolicyTests/Program.cs`, remove its name. If it was a brand-new source never in the
   list, nothing to do.
8. **Pair sources only.** When migrating the first pair source (mental state social fights, future
   romance/raid), add `GeneratePair` to `CaptureDecision` and extend the sink in
   `DiaryGameComponent` to dispatch pair drafts. Solo sources stay unchanged. *(MentalState has
   already done this step — `GeneratePair` exists; future pair sources just reuse it.)*

What stays in `RecordXxx` (impure, NOT in the pure layer): `IsDiaryEligible(pawn)`, `.LabelCap.
Resolve()`, `.Translate()`, `Find.TickManager.TicksGame`, `RecentlyRecorded`, `AddSoloEvent`/
`AddPairwiseEvent`, `QueueLlmRewrite`. These cannot be unit-tested without RimWorld — review
carefully, and where possible keep them as one-liners around calls into the pure helpers.

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

Tale-domain groups can also declare `deathVictimInitiatorDefNames` and
`deathVictimRecipientDefNames`; those XML lists identify death TaleDefs and which pawn slot contains
the victim, so DLC/modded death classifications stay data-owned.

`DiarySignalPolicyDefs.xml` owns tracker-specific thought/work policy: thresholds, tokens, staged
progression, ambient batching, scan odds, and cooldowns. `DiaryTuningDef.xml` keeps shared fallback
tuning for mood/health buckets, nearby context, first-person minimum biological age,
day-reflection weights, and scanner intervals. Code fallbacks keep the mod usable if XML is absent.

Surroundings only include weather when the pawn is outdoors, and then only on a severity-weighted
roll (`DiaryTuningDef.weatherMentionChances`, keyed by `WeatherDef.defName`): clear skies are never
noted, mild weather rarely, dramatic weather almost always, so weather stops dominating diary
openings. Weathers absent from the list fall back to favorability-keyed chances, so DLC/modded
weather still scales with severity.

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
prompt for first-person templates. Neutral arrival/death and title prompts are persona-free. Each
`rule` is tuned for small local models: one dominant, imitable voice signature plus a short in-voice
example (`For example: "..."`) so weak models pattern-match a concrete sample instead of collapsing
to a generic literary voice. The example demonstrates voice only — the system prompt still requires
the entry to be built from the supplied event, so example content is never treated as a fact.

Prompt enchantments are XML-weighted live health/capacity cues. When enabled, eligible first-person
templates may add exactly one localized `important health:` field selected from hediff/capacity
matches. Health cues are pressure, not the subject, unless the event itself is medical. Low
Consciousness is represented by capacity enchantments; it no longer changes persona text.

Direct speech is only allowed for initiator/single-POV interaction prompts, using exactly one closed
`[[speech]]...[[/speech]]` block when source notes contain or strongly imply words spoken aloud by
the POV pawn. Recipient follow-up prompts forbid speech blocks and receive hidden initiator
continuity.

When `injectGeneratedSpeechToPlayLog` is enabled, that same direct-speech parser feeds RimWorld's
native Social log: a completed initiator result with a valid speech block adds one fresh social-log
interaction row containing the generated spoken line. The row is built from `playLogInteractionDefName`
(the originating interaction's real `InteractionDef`, kept because combined batches display a synthetic
group def that would not resolve), falling back to `interactionDefName` for direct events; events with
no underlying interaction (mental states, tales) and solo entries do not inject. The original PlayLog
row is not mutated, the
generated row is marked so Pawn Diary does not record it again, and the row's displayed text is
restored from saved `LogID` mapping after load. That map is pruned on save (dropping `LogID`s whose
PlayLog row has aged out) so it cannot grow without bound, and the display patch short-circuits when
a game holds no generated rows so it adds no per-row cost to vanilla Social-log rendering. Saved
events also clear stale generated-speech `LogID`s when the backing PlayLog row is gone, allowing a
future result to inject again instead of being blocked by an aged-out id.

Known open issue (under investigation): the inject path now succeeds — the row is added to
`Find.PlayLog`, the `LogID` is assigned, and Dev Mode logs `Injected generated speech into Social log`
— but the row does **not** yet appear in the Social tab's interaction log UI. The vessel
`InteractionDef`, participants, and generated text are all valid, so the remaining gap is in how/when
the Social tab enumerates or filters PlayLog rows for display, not in building the row. To diagnose,
enable Dev Mode and watch for the inject/skip log lines emitted by `TryInjectGeneratedSpeechPlayLogEntry`.

Title generation defaults on. Successful main entries queue a capped title follow-up pinned to the
successful lane when possible. Titles store separately from main text and render as `date - title`;
entries without titles render date-only. Completed entries that lack titles are swept on load and
periodic generation scans, so save/load interruptions and retroactive title enablement recover.

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
| `injectGeneratedSpeechToPlayLog` | false | Adds one fresh Social-log row for parsed initiator direct speech after generation succeeds. |
| `workGenerationWeight` / `socialGenerationWeight` | 1 | 0-5 multipliers for work sampling and social promotion. |
| system prompt overrides | empty | Saved overrides for four shared prompts; blank uses XML. |
| event filters | XML defaults | `DiaryInteractionGroupDefs.xml` owns matching/defaults; old saved toggles are ignored. |
| `personaPresets` | empty | Built-in overrides plus custom personas. |

Settings UI includes Connection, Generation, Prompt Studio, and Persona Presets. Generation exposes
temperature, timeout, and max-token controls. API lanes support OpenAI-compatible Chat Completions,
OpenAI Responses, and native Ollama Chat, including model fetch/pick, per-row connection tests,
Responses reasoning effort, and Ollama thinking output. Endpoint URLs normalize on load/save rather
than every draw frame, and connection-test logs strip query strings from endpoints.

Production Diary tab shows completed generated pages. Dev mode adds generation enablement, persona
picker, pending/raw/failure rows, prompt/status diagnostics, in-progress indicators, and a mock-page
fill button. Long histories page by in-game year; newest visible entries expand by default.

Cards show date/title, semantic accent and group chip, subtle model id, linked POV previews, and
title-pending animation. `DiaryUiStyleDef.xml` owns visual constants. `DiaryTextFormat` escapes raw
model rich-text tags, then converts light markdown and valid speech markers to Unity rich text.
`DiaryTextDecorationDefs.xml` owns display-only decorations such as staggered direct speech for
intoxication/anesthesia and Zalgo direct speech for anomaly/dark cues. Decorations are recomputed
from saved plain facts; generated text is not mutated on save.

Social-tab log rows can jump to matching diary entries, including older year pages. Generated
speech rows use the same click bridge.

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
are rebuilt on load, and per-pawn event-id lists prune blank, duplicate, or dangling references.
`DiaryEvent` saves raw text, generated text, statuses/errors, context, source ids, LLM metadata,
semantic `colorCue`, titles, assembled prompts, compact per-POV hediff/trait facts, legacy staggered
intensity, and capped pre-cleanup debug text. Decorated rich text is not persisted.

Classification is inferred from stable `gameContext` fields such as `tale=`, `mental_state=`,
`mood_event=`, `thought=`, `inspiration=`, `work=`, and `hediff=`, parsed through
`DiaryContextFields`. Pending requests are not persisted; pending statuses reset on load and scans
requeue eligible missing work. Death and arrival caches evict oldest stale entries at their cap and
clear when a new session starts.

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

The shipped English localization includes DefInjected stubs for `DiaryInteractionGroupDef`,
`DiaryPersonaDef`, and `DiaryPromptDef` under `Languages/English/DefInjected/`; keep those in sync
when editing XML labels, instructions, tones, persona rules, or shared system prompts.

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

Release payloads are made with `scripts/publish.ps1`. It builds a throwaway Release DLL, copies only
the runnable mod files into `dist/<published packageId>`, and rewrites the copied `About.xml` so a
local dev package suffix `.development` (and legacy `(developement)` / `(development)` markers) is
stripped from the published packageId/name. The source `About.xml` keeps the development suffix so
the dev copy can sit beside the Workshop copy without a duplicate package-id clash.

Enable hooks:

```powershell
git config core.hooksPath .githooks
```

`.githooks/verify.ps1` checks staged whitespace, XML/project well-formedness, pure tests, Debug
MSBuild, committed `PawnDiary.dll` freshness, and `0Harmony.dll` source/runtime freshness. If the
build changes committed runtime DLLs, stage them and retry. Emergency bypass:
`PAWNDIARY_SKIP_VERIFY_HOOKS=1`.

Pure tests:

```powershell
dotnet run --project tests/LlmResponseParserTests/LlmResponseParserTests.csproj
dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
dotnet run --project tests/DiaryTextDecorationTests/DiaryTextDecorationTests.csproj
dotnet run --project tests/DiaryCapturePolicyTests/DiaryCapturePolicyTests.csproj
```

These harnesses compile without RimWorld/Unity assemblies; a Verse/XML Def dependency in pure code
should fail the tests at compile time. `DiaryCapturePolicyTests` covers the Event Catalog decision
reducers (see §4a) — link only `Source/Capture/*.cs` files there, never a Verse-using file.

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
