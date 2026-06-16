# Pawn Diary — Architecture & Behavior

> **Maintenance note:** This file is the living design doc for the mod — it describes "what
> happens now". Whenever the mod's behavior or structure changes, update the relevant section
> here **and** add a dated line to [`CHANGELOG.md`](CHANGELOG.md), in the same change.

_Last updated: 2026-06-16 (temporary thought diary entries)_

---

## 1. What the mod does

Pawn Diary watches **colonist** pawns' social **interactions**, **social fights**,
**mental breaks**, RimWorld **notable tales** (deaths, injuries, surgeries, births,
recruitment, research, raids, disasters, and similar non-social history events),
**mood-affecting game conditions** (aurora, eclipse, psychic drone, toxic fallout, etc.),
and **temporary thoughts** (mood thoughts with expiration, such as ate a fine meal,
beautiful fire, colonist banished, etc.), keeps the meaningful ones, and uses an LLM (any
OpenAI-compatible `/chat/completions` endpoint — e.g. a local LM Studio / llama.cpp server)
to rewrite each event into a short first-person diary entry. Each pawn's diary is shown in an
**inspector tab after Health** on that pawn's UI.

Colonist deaths are the exception to the first-person rule: when a death Tale names a player
colonist as the victim, the mod generates a separate **persona-independent death description**
from the neutral role. It uses the death hook's cause details plus nearby context and is shown in
the deceased pawn's diary instead of a first-person entry from a dead pawn.

Only **free colonists** (`pawn.IsColonist`) are eligible for diary entries. Animals,
prisoners, slaves, enemies, and visitors never receive diary entries. When a mixed
interaction involves one eligible colonist and one ineligible pawn (e.g. a colonist
nuzzled by an animal, or a colonist chatting with a prisoner), a **solo** entry is created
for the colonist only — the ineligible pawn's POV is never generated and cannot block the
colonist's entry. Interactions between two ineligible pawns are silently skipped.

Each pawn also has saved diary controls, shown above the entries in the pawn tab: a
**persona preset** picker that shapes their writing voice, and a checkbox to disable LLM
diary generation for that pawn while keeping raw events recorded.

Pairwise events (interactions, social fights, and two-pawn tales) between two eligible colonists produce two
entries — one from each pawn's point of view — by default as **paired sequential POV**:
first the initiator entry, then the recipient entry with the initiator's generated text
included as hidden continuity context. Solo events (mental breaks, single-pawn tales, or
any event where only one pawn is eligible) produce a single entry from that pawn's point of view.

---

## 2. File map

**Repository layout** — RimWorld loads only `About/`, the version folder `1.6/`, and `Languages/`.
The C# **source lives in `Source/` (which the game ignores)**; the compiled DLL is written to
`1.6/Assemblies/`:

```
PawnDiary/
├─ About/                       mod metadata
├─ 1.6/
│  ├─ Assemblies/PawnDiary.dll  build output (committed)
│  └─ Defs/                     editable XML data (groups, tuning, prompts, personas)
├─ Languages/                   UI strings
├─ Source/                      all C# + PawnDiary.csproj/.slnx (game ignores this)
│  ├─ Core/ Models/ Generation/ Defs/ Patches/ UI/ Settings/ Util/
│  └─ Properties/ Libs/
├─ prompt-lab/                  offline prompt harness (Node)
├─ skills/                      shared agent skills (tool-agnostic)
├─ AGENTS.md                    shared repo rules for code agents
├─ CLAUDE.md                    Claude Code wrapper (shared skill)
├─ CODEX.md                     Codex wrapper (shared skill)
└─ *.md                         docs (this file, AGENTS.md, CHANGELOG.md, README.md)
```

The table lists files by name; all `.cs` live under `Source/<area>/` per the tree above.

| File | Responsibility |
|------|----------------|
| `About/About.xml` | Mod metadata. `packageId = aimml.pawndiary`, supports RimWorld 1.6. |
| `InteractionGroups.cs` | Defines `DiaryInteractionGroupDef : Def` (the group type + `Matches`), optional `InteractionBatchPolicy` data for quick social-log batching, optional `InteractionPromotionPolicy` data that lets an interesting moment escape a batch into its own event, and the `InteractionGroups` classifier over the `DefDatabase`. Supports five domains: Interaction, MentalState, Tale, MoodEvent, and Thought. The group **data** now lives in XML (see `1.6/Defs/` below), so groups/prompts/batch policies are editable without recompiling. |
| `Languages/English/Keyed/PawnDiary.xml` | All player-facing UI strings **and** the natural-language prompt text (event sentences, context words, buckets), resolved via `.Translate()`. See §12 Localization. |
| `Source/Properties/AssemblyInfo.cs` | Assembly metadata. |
| `Source/PawnDiary.csproj` | Build config (.NET Framework 4.7.2; recursive `**\*.cs` glob, so new files need no project edit), outputs to `1.6/Assemblies/PawnDiary.dll`. `Source/PawnDiary.slnx` is the solution. |
| `DiaryModStartup.cs` | `[StaticConstructorOnStartup]`: applies Harmony patches and injects the Diary `ITab` after the vanilla Social tab on humanlike pawn defs. |
| `DiaryPatches.cs` | Harmony patches: `PlayLog.Add` → `RecordInteraction`; `MentalStateHandler.TryStartMentalState` → `RecordMentalState` (social fights + mental breaks); `TaleRecorder.RecordTale` → `RecordTale` (notable non-social history events); `Pawn.Kill` → `DeathContextCache` (killing blow/cause details for colonist death descriptions); `Pawn.SetFaction` → `ArrivalContextCache` / `RecordColonistArrival` (later colony joins, including DLC/modded paths that become `Faction.OfPlayer`); `QualityUtility.SendCraftNotification` → `RecordCraftedQuality` (masterwork/legendary crafts); `JobDriver_InstallRelic` completion → `RecordRelicInstalled`; `GameConditionManager.RegisterCondition` → `RecordMoodEvent` (mood-affecting game conditions); `MemoryThoughtHandler.TryGainMemory` → `RecordThought` (temporary thoughts with expiration). |
| `DiaryGameComponent*.cs` | Orchestrator: recording (interactions, mental states, tales, mood events, thoughts), generation queueing, applying results, save/load, lookups. (Context/prompt building and the data models were split into the files below.) The class is one `partial class` split across files — no behavior change, the compiler merges them. There is **one file per event we listen for**, each owning its `Record*` hook plus that event's text/context helpers: `.Interactions.cs` (social interactions), `.MentalStates.cs` (social fights + mental breaks, incl. break text/`ReasonSuffix`), `.Tales.cs` (notable-history tales — `RecordTale`, the tale/death helpers, and the `TaleDefsCoveredElsewhere`/`DeathTaleDefs` sets), `.CraftedAndRelics.cs` (masterwork/legendary crafts + relic installs — synthetic `tale=` events), `.MoodEvents.cs` (mood-affecting game conditions), `.Thoughts.cs` (temporary memory thoughts), `.Arrivals.cs` (neutral first-entry colony arrivals), `.InteractionBatching.cs` (XML-configured batching for quick social logs — `RecordBatchedInteraction` + `PendingInteractionBatch`), `.AmbientThoughts.cs` (day-note batching for low-impact temporary thoughts). The remaining files hold cross-event machinery: `DiaryGameComponent.cs` (state + lifecycle tick/save/load), `.PublicApi.cs` (UI read/write entry points), `.EventFactory.cs` (`AddSoloEvent`/`AddPairwiseEvent` build + register every event funnels through), `.Generation.cs` (prompt build, API-lane selection, LLM dispatch/apply), `.Lookup.cs` (find records/events, eligibility, dedup, persona seeding). |
| `DiaryEvent.cs` | The `DiaryEvent` model: per-POV text, context, prompts, generated text, status, originating PlayLog ids; save/load; applying LLM results (incl. legacy dual-POV parse). |
| `PawnDiaryRecord.cs` | The `PawnDiaryRecord` model: one pawn's event-id index + legacy entries + saved persona/generation toggle; save/load. |
| `DiaryContextBuilder.cs` | Static helpers turning game state into the compact context strings (pawn profile, surroundings, atmosphere, relationship/continuity, opinions) + formatting/bucket helpers. |
| `MoodImpact.cs` | Single home for the mood-direction tokens (`"positive"`/`"negative"`/`"neutral"`) shared by mood-event and thought entries: the constants (saved on `DiaryEvent.moodImpact` and embedded in `gameContext`), the ±0.5 `Classify` threshold, and `PickText` (picks the positive/negative/neutral translation key). Replaces the literals/blocks that were duplicated across `DiaryGameComponent` and `DiaryContextBuilder`. |
| `ArrivalContextCache.cs` | Transient bridge around `Pawn.SetFaction`: captures prior faction, recruiter, pawn kind, creepjoiner flag, and nearby context for neutral first-entry colony-arrival descriptions. |
| `DeathContextCache.cs` | Transient bridge between `Pawn.Kill` and `TaleRecorder.RecordTale`: captures `DamageInfo`, culprit hediff, damaged/destroyed body parts, lethal conditions, and nearby context for neutral colonist death descriptions. |
| `DlcContext.cs` | The single, double-guarded home for reading DLC-gated pawn data (Biotech `xenotype=`, Royalty `title=`, Ideology `faith=`). Each accessor checks `ModsConfig.<Dlc>Active` + null and returns empty when the DLC/trait is absent, so a no-DLC game never breaks. See §8 and AGENTS.md ("DLC-safety"). |
| `DiaryPromptBuilder.cs` | Static helpers that assemble the final prompt text (single, paired sequential, solo, neutral arrival description, and neutral death description). |
| `LlmClient.cs` | Async HTTP client to the endpoint: queueing, concurrency gate, timeouts, retries, request/response JSON. Defines `LlmGenerationRequest` / `LlmGenerationResult`. |
| `MiniJson.cs` | Dependency-free JSON parser (see §8). |
| `PawnDiarySettings.cs` | All mod settings + clamping + save/load, including per-group enable/instruction overrides (keyed by group `defName`). Includes `IsThoughtEnabled`/`InstructionForThought` for the Thought domain. |
| `DiaryTuningDef.cs` | Defines `DiaryTuningDef : Def` + `DiaryTuning.Current` (tuning knobs: dedup windows, thresholds, buckets). Data lives in XML; falls back to safe code defaults if the Def is absent. |
| `DiaryPromptDef.cs` | Defines `DiaryPromptDef : Def` + `DiaryPrompts.Current` (single-entry instructions, recipient follow-up instruction, legacy dual markers, and the default system prompt). |
| `DiaryPersonaDef.cs` | Defines `DiaryPersonaDef : Def` + `DiaryPersonas` lookup/fallback helpers. Data lives in XML and is selected per pawn. |
| `PawnDiaryMod.cs` | `Mod` class, settings UI, `ModelListClient` (fetch model list), `EndpointUtility` (URL building). |
| `ITab_Pawn_Diary.cs` | The inspector tab that renders a pawn's generated diary entries with roleplay styling, importance markers, linked-entry cross-pawn previews (click-to-navigate), and that pawn's generation toggle. |
| `DiaryEntry.cs` | `DiaryEntry` (legacy stored entry), `DiaryEntryView` (display model: `DisplayText`/`StatusText`/`DebugText`), and `LinkedEntryView` (truncated preview of the other pawn's entry for cross-linking). |
| `1.6/Defs/*.xml` | **Editable data Defs** loaded at startup (no recompile): `DiaryInteractionGroupDefs.xml` (interaction, mental-state, tale, mood-event, and thought groups + matchers + prompts), `DiaryTuningDef.xml` (tuning numbers including thought thresholds/token lists), `DiaryPromptDef.xml` (prompt instructions, legacy markers, system prompt/default persona), and `DiaryPersonaDefs.xml` (selectable writing personas). |
| `skills/pawndiary-engineering/SKILL.md` | Shared source-of-truth skill workflow for this repo (used by Claude Code, Codex, and OpenCode wrappers). |
| `AGENTS.md` | Guide for code agents: the working rules (docs, localization, comments, build), skill-routing rules, and the C#/RimWorld→JS/TS primer (Defs/`DefDatabase`, `IExposable`, Harmony, `ref`/`out`, `async`, LINQ, …). Start here. |
| `CLAUDE.md` | Thin Claude Code wrapper pointing to the shared PawnDiary skill and AGENTS constraints. |
| `CODEX.md` | Thin Codex wrapper pointing to the shared PawnDiary skill and AGENTS constraints. |
| `CHANGELOG.md` | Dated history of every change; add a line with each change. |
| `prompt-lab/` | Standalone Node harness for tinkering with prompts **outside the game** (see its own README). Editable fixtures mirror the mod's prompt format; the runner fires them at the endpoint and prints/parses the result. `personas.txt` is the writing-style catalog used in fixtures. `_system.txt` is the shared system prompt. Not loaded by RimWorld. |

---

## 3. Data flow

```
Pawn interaction                         Pawn enters a mental state                 RimWorld records a notable tale
      │  (vanilla logs it)                     │  (social fight / break)                    │  (history/art event)
      ▼                                        ▼                                            ▼
PlayLog.add ─postfix─▶ PlayLogAddPatch   MentalStateHandler.TryStartMentalState       TaleRecorder.RecordTale
      │                                        │   ─postfix─▶ MentalStateStartPatch          │   ─postfix─▶ TaleRecorderPatch
      │                                        │   (reads handler.pawn, stateDef,            │
      │                                        │    otherPawn, reason)                       │
      ▼                                        ▼                                            ▼
DiaryGameComponent.RecordInteraction     DiaryGameComponent.RecordMentalState         DiaryGameComponent.RecordTale
      │                                        │   • group enabled? (§5)                     │   • Tale group enabled? (§5)
      │                                        │   • pawn must be eligible colonist (§4)     │   • extract one/two pawns from TaleData
      │                                        │   • SocialFighting + eligible otherPawn     │   • both ineligible → skip; mixed → solo
      │                                        │     → pairwise (dedup mirrored call)        │   • one/two eligible → solo/pairwise
      │                                        │   • otherwise → solo break                  │   • skip TaleDefs already covered by
      │   • significance filter (§5)            │                                                mental-state hooks
      │   • eligibility filter: both ineligible → skip; mixed → solo for colonist (§4)
      │   • Small talk group → buffered by pawn pair, then flushed as one combined DiaryEvent
      │   • all other enabled interactions → builds one DiaryEvent immediately
      │   • adds event to diaryEvents + the involved pawns' PawnDiaryRecord(s) (eligible pawns only)
      │   • queues generation (pairwise: paired sequential/single §4; solo: single initiator)
      ▼                                        ▼                                            ▼
LlmClient.Enqueue ─▶ concurrency gate ─▶ SendOnce(HTTP) ─▶ Completed queue

A game condition starts (aurora, eclipse, psychic drone, toxic fallout, etc.)
      │  (vanilla registers it)
      ▼
GameConditionManager.RegisterCondition ─postfix─▶ GameConditionStartPatch
      │
      ▼
DiaryGameComponent.RecordMoodEvent
      │   • MoodEvent group enabled? (§5)
      │   • for each eligible colonist on affected maps
      │     → solo event (dedup by condition defName)
      │   • adds event to diaryEvents + the pawn's PawnDiaryRecord
      │   • queues generation (solo initiator)
      ▼
LlmClient.Enqueue ─▶ (same pipeline)
```

Two additional narrow hooks feed the same event pipeline:
- New-game startup records one neutral **arrival** entry for each starting colonist on the first
  tick where maps/free-colonist lists exist. These entries include the active scenario name and
  description plus the pawn summary, so founding pawns get a first page grounded in the scenario.
- `Pawn.SetFaction` records later humanlike pawns who become `Faction.OfPlayer`. This catches the
  broad join family through the actual runtime transition: recruitment, wanderer/quest joins,
  Anomaly creepjoiners (`pawn.IsCreepJoiner`), and modded arrivals that set the player faction.
  The cache captures prior faction, recruiter, pawn kind, creepjoiner flag, and surroundings.
- `QualityUtility.SendCraftNotification` records **Masterwork** and **Legendary** crafted items
  as solo events for the crafter (`tale=CraftedMasterwork` / `tale=CraftedLegendary`). Art is
  skipped here — vanilla's `CraftedArt` tale already covers it, so this avoids a double entry.
- `JobDriver_InstallRelic`'s completion action records the pawn who installs an ideology relic
  in a reliquary (`tale=RelicInstalled`).
- `GameConditionManager.RegisterCondition` records **mood-affecting game conditions**
  (aurora, eclipse, psychic drone, toxic fallout, etc.) as solo events for each eligible
  colonist on affected maps (`mood_event=<defName>`). Several of these (Eclipse, Aurora,
  ToxicFallout, VolcanicWinter, Flashstorm) are *also* TaleDefs; the Tale hook skips any tale
  whose defName matches a GameConditionDef, so each is recorded once here — never twice.

**Background generation.** All LLM diary generation is driven by background ticks, never by
UI actions. `GameComponentTick` runs `QueueAllPendingGenerations` every ~2 seconds (120 ticks),
scanning all `not_generated` events and queueing any whose pawns have generation enabled.
This also fires immediately on game load and when a player re-enables generation for a pawn.
Opening the diary tab (`EntriesFor`) is a pure read with no side effects.

---

## 4. Generation modes

Controlled by the `dualPovGeneration` setting.

### Pawn eligibility (colonist-only)

Diary entries are **only generated for free colonists** (`pawn.IsColonist == true`).
Animals, prisoners, slaves, enemies, and visitors are never eligible. This is enforced
at every entry point via `IsDiaryEligible(pawn)` (humanlike + colonist):

- **Two ineligible pawns**: interaction is silently skipped — no event is created.
- **One eligible, one ineligible**: a **solo** event is created for the eligible colonist
  only. The ineligible pawn appears as `otherPawn` context (for opinions/surroundings)
  but never gets a POV or a `PawnDiaryRecord`. This prevents the blocking issue where
  an ineligible initiator (e.g. an animal's Nuzzle) would queue a POV that the eligible
  recipient's entry depends on in sequential mode.
- **Two eligible colonists**: normal pairwise flow (both POVs generated).

This applies uniformly to interactions, social fights, mental breaks, TaleRecorder events, and
interaction batches. The inspector tab (`ITab_Pawn_Diary`) is also hidden for non-colonist pawns.

### Paired sequential POV (default, `dualPovGeneration = true`)
- Pairwise events start by queueing the initiator request only. Its prompt
  (`BuildSequentialInteractionPrompt(..., "initiator")`) asks for exactly one diary entry.
- When the initiator result is applied, `QueueRecipientAfterInitiatorResult` queues the
  recipient request. The recipient prompt includes the generated initiator entry as
  `initiator diary (hidden context)` and instructs the model not to treat it as something
  the recipient has read.
- If the initiator request fails, the recipient POV is marked failed too, because the
  required hidden context does not exist.
- Old `[INITIATOR]` / `[RECIPIENT]` parser code remains dormant for compatibility, but new
  requests are ordinary one-entry POV requests.

### Single POV (legacy, `dualPovGeneration = false`)
- Both initiator and recipient are queued as separate independent requests at record time.
- Kept for fallback; off by default.

### Solo events (mental breaks and single-pawn tales)
- `DiaryEvent.solo == true`. Only one eligible pawn is involved (recipient fields blank).
  Generation always uses the single initiator POV via `BuildSoloPrompt`. The target of a
  targeted break (e.g. MurderousRage), or the other pawn in a mixed-eligibility tale, is named
  in the text/context but does not get their own entry.

### Colony arrival descriptions
- Every tracked pawn can receive one neutral first entry explaining how they joined the colony.
  It is a factual third-person colony record, not a persona-based first-person diary entry.
- Starting colonists are recorded after new-game map creation and receive `arrival_source=game_start`
  plus the scenario name/description and their pawn summary. This lets the prompt frame them as
  founders of the chosen scenario rather than generic recruits.
- Later joins are caught at `Pawn.SetFaction(..., Faction.OfPlayer, recruiter)`. The prefix caches
  prior faction, recruiter, pawn kind, creepjoiner status, and surroundings; the postfix records
  the event only if vanilla confirms the pawn is now a colonist. This covers vanilla recruitment,
  wanderer/quest joins, Anomaly creepjoiners, and modded paths that converge on player faction.
- Arrival events carry `arrival_description=true` / `arrival_pawn_id=...` in `gameContext` and queue
  only the `neutral` role through `BuildArrivalDescriptionPrompt`, using
  `DiaryPromptDef.arrivalDescriptionInstruction`. `DiaryEvent.ToViewFor` shows this neutral entry
  only in the arriving pawn's diary.

### Colonist death descriptions
- `Pawn.Kill` is patched with a prefix that caches the killing `DamageInfo`, exact culprit hediff,
  damaged body part, destroyed/missing parts, other life-threatening conditions, and nearby
  surroundings while the data is still available.
- When `TaleRecorder.RecordTale` later accepts a death Tale (`KilledBy`, `KilledMelee`,
  `KilledColonist`, etc.) and the victim is a player colonist, `RecordTale` consumes that cache,
  appends `death_description=true` plus death facts to `gameContext`, and queues the `neutral`
  role with `BuildDeathDescriptionPrompt`.
- The death prompt uses `DiaryPromptDef.deathDescriptionInstruction`, does **not** include persona
  or relationship continuity, and asks for one short third-person description of how the colonist
  died. In the deceased pawn's diary, `DiaryEvent.ToViewFor` shows the neutral death text instead
  of a first-person victim POV.
- A death description is terminal for that pawn. `EntriesFor`, pending-generation scans, and new
  event-reference adds all suppress that pawn's events with ticks later than the final death
  description, so nothing generates or displays after the death entry.

All paths share the same per-event context fields and the system prompt.

### Per-pawn persona and generation toggle
- Each `PawnDiaryRecord` saves `personaDefName` and `diaryGenerationEnabled`.
- Persona options are `DiaryPersonaDef` XML Defs in `1.6/Defs/DiaryPersonaDefs.xml`, with
  `DiaryPromptDef.defaultPersonaDefName` providing the fallback/default (`DiaryPersona_StoicSurvivor`).
- **Initial persona is a weighted roll, not uniform random.** When a pawn's record is first created,
  `DiaryPersonas.WeightedStartingPersona(pawn, usedCounts)` picks the starting voice:
  - **Trait/backstory fit.** Each persona carries coarse `<themes>` keywords in XML (e.g. `grim`,
    `warm`, `anxious`). `Source/Generation/PersonaAffinity.cs` maps RimWorld trait defNames (and
    spectrum-trait degrees, e.g. `Nerves:-1` = nervous) and backstory `spawnCategories` to that
    same vocabulary, then adds `ThemeBonus` to a persona's weight per theme it shares with the pawn.
    A persona with no matching theme still has the flat base weight, so nothing is impossible.
  - **Soft colony de-duplication.** `DiaryGameComponent.BuildUsedPersonaCounts` counts how many
    current living free colonists already use each persona; the weight is multiplied by
    `DuplicatePenalty` (≈0.25) once per existing user, so repeats are rare but still possible once
    the catalog is spread thin (clamped to a small floor so weights never reach zero — assignment
    never starves even with more colonists than personas).
  - Themes are internal keywords only; they are **not** shown to the player and are **not** localized.
- Existing records keep their saved persona — the weighted roll only runs the first time a pawn
  enters the diary system; the manual picker overrides it freely. Missing/invalid saved persona
  values fall back to the default above.
- The Diary tab creates a pawn's record on first edit/open. In normal play it shows only the diary
  pages; RimWorld dev mode reveals the per-pawn writing checkbox plus the persona picker (a
  float-menu of every `DiaryPersonaDef`) above the entries.
- Disabled generation does **not** delete or skip events; it only prevents future LLM requests for
that pawn. Raw event text is still stored, but the production diary tab only displays finished
generated entries.
- Re-enabling generation for a pawn immediately queues any `not_generated` events for that pawn.
- In paired sequential mode, if the initiator has generation disabled but the recipient does not,
  the recipient can still generate from the base event prompt without hidden initiator context.

### Prompt context — signal only, no filler
Prompts are assembled line-by-line via `AppendField`, which **drops any field that is empty
or a placeholder** (`none` / `n/a` / `unknown`). Builders are written to return empty when
there's nothing worth saying, so the model never spends tokens on noise:
- **Persona**: the selected writing-style rule is sent as a `persona:` line, separate from
  gameplay facts such as mood and relationship. Each persona now includes emotional triggers,
  sentence openers, and stylistic tips (e.g., "Opens with blunt observations").
- **Atmosphere** (`BuildAtmosphere`): a short emotional anchor combining the pawn's mood bucket
  and their opinion-based relationship tone (e.g., "tense friction", "bright warmth", "bleak
  hostility"). Helps small models establish emotional tone without complex inference. Empty
  for neutral moods and neutral opinions.
- **Pawn summary** (`BuildPawnSummary`): compact profile — `sex=`, `age=`, then DLC identity
  (each omitted without its DLC, via `DlcContext`): `xenotype=` (Biotech; skips plain Baseliner),
  `title=` (Royalty; highest title), `faith=` (Ideology; ideoligion + role) — then `mood=`
  (bucket + %), `health=` (empty when healthy), `low_capacities=` (only Moving/Talking/Sight/Hearing
  when below 80%, using localized keyword labels like "impaired movement"), `thoughts=` (exactly one
  positive and one negative thought, weighted random so stronger effects are more likely).
- **Surroundings** (`BuildSurroundingsSummary`): weather and biome only when **outdoors**;
  temperature only when actually cold (≤0°C) or hot (≥32°C); beauty only when notably nice
  or grim; nearby things / current job only when present.
- **Health** (`BuildHealthSummary`): empty when healthy — only pain, bleeding, downed,
  pain-shock, and notable conditions are sent.
- **Relationship** (`BuildContinuitySummary`): a compact memory line per pawn —
  relation kind (spouse/rival/…) + `opinion ±N (bucket)` + **why** (the top stored social memories
  driving it, aggregated and signed, via `BuildSocialThoughtsSummary`; situational social thought
  workers are not recalculated here) + **last wrote** (the
  most recent diary line that pawn produced about the other — a lightweight memory layer for
  continuity).
- **My last opener** (`LatestDiaryOpener`): the first sentence of the pawn's most recent diary
  entry, included as a hint to avoid repetitive openings. Empty for the first entry.
- **Burning passion** (`RandomBurningPassion`): a randomly selected passion (major 3x weight,
  minor 1x weight) from the pawn's skills, shown as `"Mining (burning)"` for major or
  `"Plants"` for minor. Only included for **important events** (not small talk, chit chat, etc.).
- Dropped from prompts entirely (still stored on the event): raw per-beat logs,
  `shared_event`, `sequence`, `game`/worker details, and the separate `opinions` line.

---

## 5. Interaction groups (recording + per-group prompt)

Events are organized into **groups**, defined as `DiaryInteractionGroupDef` Defs loaded from
`1.6/Defs/DiaryInteractionGroupDefs.xml` (edit + restart, no recompile). Each group is a single
settings entry with two things: an **enabled toggle** (is it recorded?) and a **diary prompt
instruction** (shared by every event in the group). This replaces the old per-defName
"significant" allowlist *and* the old per-defName instruction map — they are now one system.

Groups have a **domain**: `Interaction` (matches `InteractionDef`s), `MentalState`
(matches `MentalStateDef`s), `Tale` (matches `TaleDef`s from RimWorld's notable-history
system), `MoodEvent` (matches `GameConditionDef`s that affect mood), or `Thought`
(matches `ThoughtDef`s with expiration). Classification is domain-scoped so they never cross-match:
- `Classify(InteractionDef)` → first matching Interaction-domain group, else **A quiet day**.
- `ClassifyMentalState(MentalStateDef)` → first matching MentalState-domain group, else
  **Mental breaks**.
- `ClassifyTale(TaleDef)` → first matching Tale-domain group, else **A notable day**.
- `ClassifyMoodEvent(GameConditionDef)` → first matching MoodEvent-domain group, else
  **Passing moods**.
- `ClassifyThought(ThoughtDef)` → first matching Thought-domain group, else **Passing thoughts**.

Matching is by exact `defName` or a substring token; order within a domain matters
(specific themes before generic), with the catch-all last.

**Recording:** `DiaryGameComponent.IsInteractionSignificant(def)` →
`Settings.IsInteractionEnabled(def)` → `IsGroupEnabled(Classify(def).defName)`. So an interaction
is recorded iff its group is enabled.

**Prompt:** `InteractionInstruction(def)` → `Settings.InstructionFor(def)` →
`InstructionForGroup(Classify(def))` (per-group override, else the group's default).

**Groups and default enabled state** (toggle any of these in mod settings):

| Group | Default | Examples |
|-------|---------|----------|
| NSFW / RJW | **on** | `Sex_*`, `Rape_*`, `Necro_*`, `SexTame*`, `Speech_sex` |
| Romance & dating | on | RomanceAttempt, MarriageProposal, Breakup, Psychology dates/hookups, Speech_DateRitual |
| Recruitment & prison | on | BuildRapport, RecruitAttempt, ReduceWill, EnslaveAttempt, SparkJailbreak |
| Slavery | on | Suppress, SparkSlaveRebellion |
| Ideology & conversion | on | ConvertIdeoAttempt, Convert_*, Counsel_*, PreachHealth, WorkDrive, Indoctrinate, Worship |
| Trials & accusations | on | Trial_Accuse, Trial_Defend |
| Anomaly & dark | on | DarkDialogue, CreepyWords, InhumanRambling, OccultTeaching, InterrogateIdentity |
| Insults & fights | on | Insult, Slight, Sentence_SocialFight* |
| Rituals & speeches | on | Speech_* (Funeral, Execution, Leader, …), WordOf* psycasts |
| Animal handling | on | AnimalChat, TameAttempt, TrainAttempt, Nuzzle, ReleaseToWild |
| Heartfelt talk | on | DeepTalk, KindWords, Reassure, SnapOut_CalmDown |
| Teaching & lessons | **off** | Lesson*, BabyPlay |
| Small talk | **off** | Chitchat, Conversation, HangOut, OfferFood, SanguophageChat |
| A quiet day | **off** | anything unmatched (e.g. RiMindInteraction) |
| **Social fights** (mental) | on | `SocialFighting` — pairwise, two POV entries |
| **Mental breaks** (mental) | on | Berserk, Tantrum, Wander_Sad, Binging_*, MurderousRage, … (catch-all for mental states) |
| **Combat, injuries & death** (tale) | on | Downed, Wounded, KilledBy, KilledMajorThreat, WasOnFire |
| **Health & medicine** (tale) | on | DidSurgery, HealedMe, IllnessRevealed, HeatstrokeRevealed, Exhausted |
| **Life milestones** (tale) | on | GaveBirth, Captured, Recruited, Marriage, Breakup, BondedWithAnimal |
| **Masterworks & legendary crafts** (synthetic tale) | on | CraftedMasterwork, CraftedLegendary, CraftedArt |
| **Relics** (synthetic tale) | on | RelicInstalled |
| **Work & achievements** (tale) | **off** | FinishedResearchProject, CompletedLongConstructionProject, GainedMasterSkill* |
| **Anomaly horror** (tale) | on | StudiedEntity, MutatedMyArm, PerformedPsychicRitual, ClosedTheVoid, EmbracedTheVoid, DeathPall, UnnaturalDarkness, NoxiousHaze |
| **Raids, disasters & colony events** (tale) | on | Raid, Infestation, ManhunterPack, MeteoriteImpact, ShipPartCrash, CaravanAmbushed*, LaunchedShip † |
| **Quiet personal moments** (tale) | **off** | AttendedParty, Meditated, Prayed, ReadBook, Vomited, WalkedNaked, VisitedGrave |
| **A notable day** (tale) | **off** | any unmatched TaleDef |
| **Positive mood events** (mood) | on | Aurora, Party, PsychicSoothe |
| **Negative mood events** (mood) | on | Eclipse, PsychicDrone, GrayPall, ToxicFallout |
| **Situationally mixed mood events** (mood) | on | PsychicSuppressorMale, PsychicSuppressorFemale |
| **Passing moods** (mood) | on | any unmatched GameConditionDef |
| **Positive thoughts** (thought) | on | Apparel*, Beautiful*, Good*, Joy*, Happy*, etc. (filtered by `thoughtMinMoodOffset`) |
| **Negative thoughts** (thought) | on | Pain*, Ugly*, Bad*, Sad*, etc. (filtered by `thoughtMinMoodOffset`) |
| **Passing thoughts** (thought) | on | any unmatched ThoughtDef with expiration |

† Incidents that are also game conditions (Eclipse, Aurora, ToxicFallout, VolcanicWinter, Flashstorm) are recorded once via the **MoodEvent** domain, not as tales.

**Thought recording** is driven by a Harmony postfix on `MemoryThoughtHandler.TryGainMemory`
(the `Thought_Memory` overload, which the `ThoughtDef` overload also routes through). Only
temporary thoughts (`durationDays > 0`) are considered. Filtering rules are configurable via
`DiaryTuningDef.xml`:
- `thoughtIgnoreTokens`: substring tokens for thoughts always skipped (room stats, corpse observations).
- `thoughtBypassThresholdTokens`: substring tokens for thoughts that skip the magnitude check
  (death, banishment, abandonment — always recorded regardless of mood offset).
- `thoughtEatingTokens`: substring tokens for eating thoughts, which use a higher threshold.
- `thoughtMinMoodOffset` (default ±5): minimum absolute mood offset for general thoughts.
- `thoughtEatingMinMoodOffset` (default ±15): minimum absolute mood offset for eating thoughts.
- `thoughtAmbientTokens`: substring tokens for threshold-passing thoughts that should become
  background mood material instead of immediate entries (e.g. nuzzling, party/concert attendance,
  speech reactions, bad barracks, rotting corpse observations).
- `thoughtAmbientWindowTicks`, `thoughtAmbientMinEventsToWrite`, `thoughtAmbientMaxSampleLines`:
  day-note timing, minimum count, and prompt evidence cap for ambient thought notes.

The prompt instruction for thought groups tells the LLM to match dramatism to the mood offset
magnitude: minor offsets get a passing mention, strong offsets get raw emotion and detail.
Thoughts matching `thoughtAmbientTokens` still pass through the normal ignore, threshold, and dedup
filters first; they then accumulate per pawn/day into one solo `ThoughtAmbientDay` entry whose
prompt asks for emotional texture rather than a thought-by-thought report. If the note has enough
material, it can flush when the pawn settles into RimWorld's `LayDown` or `LayDownResting` job
(normal sleep or rest). Otherwise the quiet window, day change, or save-time flush remains the
fallback.

To add/retune groups, edit `1.6/Defs/DiaryInteractionGroupDefs.xml` (`defName`, `label`, `order`,
`domain`, `defaultEnabled`, `important`, `combat`, optional `batch`, optional `promotion`,
`instruction`, `matchDefNames`, `matchTokens`, `catchAll`) and restart
— no recompile. `order` controls classification order within a domain (lowest first; keep the
catch-all highest). Group `defName`s are the stable keys player settings save overrides under, so
don't rename them.
The Diary tab uses `important` only as a display hint: important groups get a warm marker,
while quiet groups get a muted marker. It does not affect recording or generation. `important`
also gates the `burning passion:` prompt field; `combat` (data-driven, default false) gates the
`weapon:` field — together they decide whether the equipped weapon is added to the prompt.

**Tuning knobs** (dedup windows, legacy batch fallbacks, scan radius/temperature, the
mood/pain/beauty/opinion bucket thresholds, and thought recording thresholds/token lists)
likewise live in XML — `1.6/Defs/DiaryTuningDef.xml`, backing `DiaryTuningDef` /
`DiaryTuning.Current`. Every value defaults to the shipped number, so deleting the file or
any field changes nothing.

**Interaction batching:** an Interaction-domain group can define an optional `<batch>` policy in
`DiaryInteractionGroupDefs.xml`. Matching social-log rows are buffered instead of queued
immediately. `mode` chooses the output shape:

- `PairEvent` is the original merged-entry behavior. Rows flush when no new matching event arrives
  for `windowTicks`, when the batch reaches `maxEvents`, or before saving. `scope=Pair` batches all
  matching interactions for the same pawn pair; `scope=Def` keeps separate batches per
  `InteractionDef` within the pair. Flushed batches become normal pairwise or solo `DiaryEvent`s.
- `AmbientDayNote` treats low-stakes interactions as background texture. Rows accumulate per
  pawn/group/in-game day and flush into one solo diary memory, with no pawn-pawn POV chain. Once
  the configured `minEventsToWrite` is met, the note can flush when the pawn starts RimWorld's
  `LayDown` or `LayDownResting` job, which makes bedtime/rest feel like the natural diary-writing
  moment. Thin notes stay pending instead of being forced into the diary. `maxSampleLines` caps how
  much evidence is passed to the LLM; `windowTicks=60000` keeps normal flushing aligned with the day
  boundary while save-time flushing still prevents data loss.

`syntheticDefName` and the `*Key` fields choose the combined event identity and localized
label/header/fallback/instruction text; the classifier automatically treats a group's own
`syntheticDefName` as matching that group for Diary tab display. `includeInteractionLabel` controls
whether each numbered line includes the original interaction label. If `windowTicks` or `maxEvents`
are omitted, the legacy `smallTalkBatchWindowTicks` and `smallTalkBatchMaxEvents` tuning values are
used as fallbacks.

The shipped `smalltalk`, `animal`, and `teaching` groups use `AmbientDayNote`. `smalltalk` writes
only after at least three low-stakes social moments for a pawn in the day; animal handling and
teaching write only after at least two. The prompt wording tells the LLM to write a natural diary
recollection, not a list or report. Important social groups are not batched unless their own XML
group opts in; for example `DeepTalk` is classified under `heartfelt`, so it records and queues
immediately by default.

**Promotion (escaping the batch):** aggressive `AmbientDayNote` batching can starve the diary of
individually-interesting moments, since everything for a pawn/day collapses into one note (or is
dropped under `minEventsToWrite`). A batch group can add an optional `<promotion>` policy
(`InteractionPromotionPolicy`) so each matching moment gets a weighted-random roll *before* it is
batched. On a win, the moment skips batching and falls through to a normal **immediate pairwise
`DiaryEvent`** (using the group's existing label/text/instruction); on a loss it batches as usual.
The roll happens in `RecordInteraction` via `ShouldPromoteInteraction`/`PromotionChance`
(`DiaryGameComponent.InteractionBatching.cs`), and is only consulted for groups that also batch
(`HasPromotionPolicy` requires `HasBatchPolicy`). The probability is `baseChance` plus a bonus per
notable signal, clamped to `maxChance`:

- **Social dynamic** — `opinionStrongBonus` when the strongest `OpinionOf` between the pair is at/
  above `opinionStrongThreshold` (intense love/hate); `opinionAsymmetryBonus` when the two opinions
  differ by at least `opinionAsymmetryThreshold` (lopsided, one-way feelings).
- **Pawn-state salience** — `needLowBonus` when either pawn has a core need (food/rest/joy) at/below
  `needLowThreshold`; `moodExtremeBonus` when either pawn's mood is at/below `moodLowThreshold`
  (near a mental break). Pawns lacking a need simply don't trigger that signal.

Every signal reads structured, language-independent data (opinion numbers, need levels) — there is
no text/topic matching — so promotion behaves identically in every language. The shipped
`smalltalk` group enables a conservative policy (`baseChance` 0.04, `maxChance` 0.6); other batch
groups can opt in the same way, and `enabled=false` reverts to pure batching without deleting the
block.

**Mental states** are captured via the `MentalStateHandler.TryStartMentalState` postfix:
- `SocialFighting` with an eligible colonist `otherPawn` → a **pairwise** event (both pawns).
  `TryStartMentalState` fires once per participant, so the mirrored call is de-duplicated by
  pair within `SocialFightDedupTicks` (300).
- Any other mental state → a **solo** event from the breaking pawn (de-duplicated per
  pawn+state within `MentalBreakDedupTicks` ≈ one day). A target pawn, if any, is named in
  the text but gets no entry of their own.
- Only eligible colonist pawns are recorded (animal manhunter/panic states and
  non-colonist mental breaks are ignored). Social fights between a colonist and a
  non-colonist are recorded as solo events for the colonist only.

**Notable tales** are captured via the `TaleRecorder.RecordTale` postfix after vanilla accepts
the tale. The recorder extracts one or two pawns from `Tale_SinglePawn` / `Tale_DoublePawn`
subclasses, then creates a solo or pairwise `DiaryEvent` using the same eligibility and
generation rules as interactions. Tale events are tagged in `gameContext` with `tale=...` so
saved events classify back into the Tale domain without adding a save-breaking source field.
TaleDefs already covered more precisely by the mental-state hook (`SocialFight`,
`MentalStateBerserk`, `MentalStateGaveUp`) are skipped. Repeated TaleDef+pawn combinations are
de-duplicated within `taleDedupTicks` (default 2500).

Death TaleDefs get one extra pass: if the victim is a player colonist, the event also carries
`death_description=true`, `death_victim_id=...`, the victim role, and cached death facts from
`Pawn.Kill`. The deceased pawn's visible entry uses the neutral death description prompt rather
than a persona-based first-person diary entry. That death description is treated as the pawn's
final diary event: later events for that pawn are not queued for generation and are filtered out
of the diary view.

This includes pawn medical operations when vanilla records `DidSurgery` / `HealedMe`, so the mod
does not add a second surgery hook that would duplicate successful operations.

**Synthetic tale-style events** cover vanilla moments that do not arrive as TaleDefs:
- Masterwork and Legendary production is captured from `QualityUtility.SendCraftNotification`.
  Only the crafter receives a solo entry. These entries classify into the Tale domain using
  synthetic defNames (`CraftedMasterwork`, `CraftedLegendary`). Art is excluded — it already
  arrives as vanilla's `CraftedArt` tale, so recording it here too would double-log one sculpture.
- Relic installation is captured from `JobDriver_InstallRelic`'s final action. Only the installer
  receives a solo entry (`RelicInstalled`). Relic discovery letters are colony-level and do not
  reliably name a pawn, so they are not used for diary ownership.

---

## 6. Settings (`PawnDiarySettings`)

| Setting | Default | Range / notes |
|---------|---------|---------------|
| `apiEndpoints` | one row: enabled `http://localhost:1234/v1` / `local-model` | **List of API lanes**, each = enabled flag + base URL + key + **one** model (`ApiEndpointConfig`). Requests spread across enabled rows and run in parallel (§7). Add rows with **+ Add API**; disabled rows stay saved but are skipped, and a model is required per active row (blank-model rows are skipped). Legacy single-endpoint saves migrate into one enabled row automatically. |
| `showApiSettings` | true | UI-only preference for whether the compact API/model setup block is expanded in mod settings. |
| `showPersonaSettings` | false | Dev-mode-only UI preference. When RimWorld dev mode is on, this reveals the manual persona picker in the pawn Diary tab. Normal mode always hides persona controls. |
| `showLlmDebugInfo` | false | Dev-mode-only UI preference. When RimWorld dev mode is on, this shows raw/pending/failed entries plus the LLM endpoint/model/status/error/prompt diagnostic block in the pawn Diary tab. Normal mode always hides it. |
| `showGeneratingEntries` | false | Dev-mode-only UI preference. When RimWorld dev mode is on, this reveals entries still in the generation pipeline (in-progress or stuck on "writing...") in the pawn Diary tab — without the full diagnostic block — so a stuck event is visible instead of only counted by the badge. Implied by `showLlmDebugInfo` (which already shows pending rows). Normal mode always hides them. |
| `endpointUrl` / `apiKey` / `modelName` | localhost / _(empty)_ / `local-model` | **Legacy** single-endpoint fields, kept only to seed `apiEndpoints` on first load. |
| `timeoutSeconds` | 30 | 5–300. **Per-request deadline** — also the "stuck request" purge window (§7). |
| `maxConcurrentRequests` | 4 | 1–16. Max requests in flight **per API**; the rest queue on that API. Different APIs always run in parallel. **Use 1 for a single local model.** |
| `maxTokens` | 160 | 32–2048. Applied to each one-entry request. |
| `temperature` | 0.8 | 0–2. |
| `dualPovGeneration` | true | Paired sequential vs. independent single-POV requests for both sides (§4). |
| `systemPrompt` | from `DiaryPromptDef` XML | Sent as a `system` message; edit `1.6/Defs/DiaryPromptDef.xml` then click "Restore default" to apply (existing saves persist their saved value). |
| `groupEnabled` / `groupInstructions` | per-group defaults | Maps keyed by `InteractionGroup.Key` (see §5). Absent key = use the group's default enabled state / default instruction. |
| `enableLlm`, `keepRawEntryOnFailure`, `sendApiKeyAsBearerToken` | true | Currently forced on in `ClampValues`. |

Settings UI lives in `PawnDiaryMod.DoSettingsWindowContents`, organized into medium-font **sections**
(`SectionTitle`) with muted hint text (`DrawHint`): **Connection** (the hideable **API lanes
editor** — `DrawApiEndpointsEditor` — each lane is a framed block with an enabled toggle,
endpoint/model/API-key fields, per-row "Fetch" + "Pick", and **+ Add API** / **− Remove** /
**Reset** buttons), **Generation** (paired-POV toggle + per-API concurrency slider), **System
prompt** (editor + restore), and **Events** (the event-group editor). The group editor draws the
~30 enable toggles in **two columns** per domain (`DrawGroupTogglesForDomain` / `DrawGroupToggle`,
each showing its prompt as a hover tooltip), followed by a per-group prompt editor — an "Editing
prompt for: X" header with a **Change group** picker, the instruction text area, and Save/Restore.
The scroll view is **self-measuring with a current-layout estimate**: it uses
`EstimateSettingsContentHeight()` to handle same-frame expand/collapse of the API/model editor,
then records the drawn `listing.CurHeight` (`lastSettingsContentHeight`) for the next frame and
clamps the scroll position, so every control stays scrollable and clickable no matter how many
groups or model rows are listed.

Per-pawn diary controls live in `ITab_Pawn_Diary`, not the global mod settings window. They are
stored in each pawn's `PawnDiaryRecord`: persona preset and `diaryGenerationEnabled` (default
enabled). The generation checkbox is always visible. The persona picker is a dev-mode-only control:
dev mode shows a "Show persona settings" toggle, and normal mode hides persona editing entirely.

---

## 7. Concurrency & reliability (`LlmClient`)

- **Per-API lanes:** each configured API has its own `SemaphoreSlim` gate (keyed by
  endpoint+model), sized to `maxConcurrentRequests`. At most N requests are in flight **per API**;
  lanes run independently, so several APIs work in parallel. Requests are **never dropped** for
  being "too many".
- **Distribution:** `QueuePrompt` (`DiaryGameComponent`) assigns each new event to the next enabled
  lane round-robin (`LlmClient.NextRoundRobinIndex`). The recipient half of a paired/sequential event is
  pinned to the **same lane the initiator used** (matched via the endpoint+model recorded on the
  event) so a sequential pair stays on one model. No enabled configured lane ⇒ the entry fails with
  `PawnDiary.Error.NoApiConfigured` (raw text kept if `keepRawEntryOnFailure`).
- **Failover ("use next model"):** each request carries the chosen lane plus the **other lanes as
  ordered failover targets** (`failoverTargets`). `SendWithRetries` tries each lane via `TryLane`;
  a lane that returns a permanent error, exhausts its retries, or times out hands off to the next
  lane. Only when **every** lane fails is a failure reported. On success the result reports which
  lane actually produced the text, and `ApplyLlmResult` updates the recorded LLM meta so a paired
  recipient pins to the model that really worked.
- **Debug lane logs:** each queued request writes `[PawnDiary debug]` lines to the RimWorld log:
  configured vs active lane counts, selected primary lane and reason, failover order, skipped
  blank/duplicate lanes, each lane attempt, success lane, and all-lanes-failed summaries. API keys
  are never logged. These lines originate on background send workers, but `Log.Message` is
  **main-thread only** (it mutates the queue the in-game log window enumerates during OnGUI, so an
  off-thread write throws "Collection was modified"). So `LlmClient.LogDebug` logs directly only
  when already on the main thread (`UnityData.IsInMainThread`) and otherwise queues the line for
  `FlushPendingLogs`, which `DiaryGameComponent.GameComponentTick` drains on the main thread —
  mirroring how finished results cross the thread boundary.
- **Stuck-request purge:** once a request acquires a lane's slot, a hard deadline (`timeoutSeconds`)
  covers that **lane attempt including its retries**. When it fires the lane is abandoned (and
  failover moves on); if it was the last lane, the entry is reported as
  `"Timed out waiting for the model."` and the slot is freed. The deadline clock starts only after
  leaving the queue, so waiting in line does not count against it. (Worst-case total wait ≈ lanes ×
  `timeoutSeconds`.)
- **Stale-session purge:** `BeginSession` bumps a session id and cancels the old token; requests
  from an ended session are dropped instead of wasting a slot. It is called **once per loaded Game,
  from the `DiaryGameComponent` constructor** — which runs first on both new game and load, so it
  purges any requests left over from a previous Game. `StartedNewGame`/`LoadedGame` deliberately do
  **not** call it again: by the time they run, this Game has already queued startup entries (e.g.
  founding-colonist starting thoughts from `GiveAllStartingPlayerPawnsThought` during `InitNewGame`),
  and a second `BeginSession` would cancel those mid-flight and strand them on "Generating".
- **Orphan recovery (backstop):** each generation scan, `RecoverOrphanedPendingGenerations` resets
  any POV that is `pending` but has no in-flight request (`LlmClient.IsInFlight` false) back to
  not-generated, so the queue pass that follows re-drives it. This catches an entry left on
  "Generating" if a request is ever dropped without reporting a result (e.g. a future session
  cancel). It only acts on an entry seen orphaned for **two consecutive scans**, so a request that
  merely finished between scans (result still queued for the main thread) is never re-sent, and the
  in-flight check prevents double-sending a request that is still running.
- **Retries:** up to 3 attempts per lane for transient errors (429, 5xx, network) within that
  lane's deadline; permanent errors (other 4xx, empty content) skip straight to the next lane.

---

## 8. RimWorld / Mono constraints (important)

- The mod runs on RimWorld's **Unity Mono runtime**, which only has the assemblies in
  `RimWorldWin64_Data/Managed`. A mod can reference anything at compile time, but
  referencing an assembly not present at runtime causes a `TypeLoadException`.
- **`System.Web.Extensions` / `JavaScriptSerializer` is NOT available at runtime.** JSON is
  therefore parsed with the hand-written `MiniJson` (objects → `Dictionary<string,object>`,
  arrays → `object[]`, etc.). Do not reintroduce `JavaScriptSerializer` or add a NuGet
  JSON library unless it is shipped in the mod's own `Assemblies`.
- **No paid DLC required.** `About/About.xml` declares no DLC dependency, and there are no
  `[DefOf]`/`DefDatabase.GetNamed` lookups of DLC defs. DLC *events* are handled **reactively** —
  patches hook base-game choke points (`PlayLog.Add`, `MentalStateHandler`, `TaleRecorder`,
  `GameConditionManager`) and the interaction groups classify by matching `defName` **strings**
  (§5), so DLC defNames are simply inert when the DLC is absent. DLC-gated **pawn data**
  (Biotech genes, Royalty titles, Ideology faith) is read **only** through `DlcContext` (§4),
  which double-guards every access (`ModsConfig.<Dlc>Active` + null-check) and returns empty when
  absent — so a no-DLC game omits those prompt lines rather than crashing. New features must
  preserve this — see **AGENTS.md → "DLC-safety"**.

---

## 9. UI

- **Inspector tab** (`ITab_Pawn_Diary`), injected immediately after the vanilla **Social** tab on all
humanlike pawn defs at startup. The tab is **visible only for free colonists**
(`pawn.IsColonist`); animals, prisoners, slaves, enemies, and visitors never see it.
Renders newest-first: each card uses a diary-like `date: subject` header plus the generated diary
text. The text is drawn in a roleplay-log style: narration is muted/italic, and dialogue-looking
lines are bold/italic and colored with the pawn's RimWorld favorite color. Each generated card ends
with a tiny, low-contrast model id so multi-model output can be traced without making the card feel
technical. Pending, failed-without-output, raw fallback, debug, and persona-editing details are
hidden from the production tab. In RimWorld dev mode, the tab adds the per-pawn writing checkbox,
toggles for persona controls, LLM diagnostics, and showing in-progress entries; the debug toggle
reveals raw/pending/failed rows plus the existing diagnostic block with endpoint, model, status,
error, and prompt. The lighter "Show pages still being written" toggle reveals only the
in-progress/stuck rows (rendered with the "writing..." placeholder) without that diagnostic block,
so a stuck event can be inspected rather than only counted. A compact writing badge appears in the
tab while pending entries exist.
- Clicking a vanilla Social-tab interaction log row opens the Diary tab and scrolls to the
  matching generated entry when that row maps to one; otherwise RimWorld's normal click behavior
  continues. Small-talk batches keep every represented PlayLog id, so any row in the batch can
  jump to the combined diary entry after generation completes.
- There is **no** standalone window or gizmo anymore, and **no** colony/neutral events
  view (both removed — see Changelog).

---

## 10. Persistence (save/load)

- `DiaryGameComponent.ExposeData` saves `diaries` (per-pawn records) and `diaryEvents`.
- `DiaryEvent` is `IExposable`; generated text, statuses, errors, context summaries, and
  originating PlayLog ids are scribed. Tale and mental-state source kinds are inferred from
  stable `gameContext` markers (`tale=...`, `mental_state=...`) to avoid adding save fields.
  Prompts are not scribed (rebuilt on demand).
- Pending requests are not persisted; on load, pending statuses normalize back to
  not-generated and regenerate via the background queue scan.
- **Backward-compat:** dormant `neutral*` fields remain scribed so older saves load; they
  are no longer populated or shown.

---

## 11. Building

From a developer command prompt / MSBuild:

```
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
```

Output: `1.6/Assemblies/PawnDiary.dll`. Restart RimWorld (or the save) to load the new DLL.

---

## 12. Localization

The mod is **localization-ready**: every player-facing UI string and every natural-language
word fed to the LLM is a key in `Languages/English/Keyed/PawnDiary.xml`, resolved at runtime via
`"Key".Translate()`. RimWorld resolves `.Translate()` against the **game's active language**, so
translating that one file (plus the DefInjected text below) localizes the whole mod — *including
the prompt the model reads*. That is the point: with the prompt scaffolding also translated, no
stray English biases the model toward writing in English when the player runs a translated game.

**Kept in English on purpose — a stable machine "schema", not prose:**
- The structured prompt **field labels** in `DiaryPromptBuilder` (`event:`, `pov:`, `role:`,
  `with:`, `what you saw:`, `what happened:`, `you:`, `persona:`, `setting:`, `atmosphere:`,
  `relationship:`, `my last opener (not repeat):`, `burning passion:`, `instruction:`,
  `initiator diary (hidden context):`) and the `key=` summary sub-keys in
  `BuildPawnSummary` (`sex=`, `age=`, `mood=`, `health=`, `low_capacities=`, `thoughts=`, …).
- The `initiator` / `recipient` role words, and the `none` / `n/a` / `unknown` skip-sentinels
  that `AppendField` filters on.

These read like JSON keys; the (translated) instruction drives the output language. Only the
*values* beside them — buckets (`happy`, `beautiful`, `hostile`…), state words (`outdoors`,
`downed`, `corpse of …`), and full event sentences — are localized.

**Also left in English** (not normal player-facing prose): the greyed `LLM debug` block under
each entry, and `LlmClient` network-error messages — the latter are built on background threads,
where `.Translate()` is not thread-safe.

**Def-based text** (persona `rule`, group `label` / `instruction`, and `DiaryPromptDef`'s
`systemPrompt` + wrapped instructions) is localized the RimWorld way — via
`Languages/<lang>/DefInjected/…`, not Keyed. The English in `1.6/Defs/*.xml` (and the C# field
defaults) is the source/fallback.

**To add a language:** copy `Languages/English` to `Languages/<Language>`, translate the Keyed
values, and (optionally) add `DefInjected` files for the persona/group/prompt Def text.

---

## 13. Changelog

The full dated history lives in [`CHANGELOG.md`](CHANGELOG.md) (repo root). Add a dated entry there with every change.
