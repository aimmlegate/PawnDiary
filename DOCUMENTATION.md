# Pawn Diary — Architecture & Behavior

> **Maintenance note:** This file is the living design doc for the mod. Whenever the
> mod's behavior or structure changes, update the relevant section **and** add a line to
> the Changelog at the bottom. Keep it accurate — it is the source of truth for "what
> happens now".

_Last updated: 2026-06-14 (in-game persona layer)_

---

## 1. What the mod does

Pawn Diary watches humanlike pawns' social **interactions**, **social fights**, and
**mental breaks**, keeps the meaningful ones, and uses an LLM (any OpenAI-compatible
`/chat/completions` endpoint — e.g. a local LM Studio / llama.cpp server) to rewrite each
event into a short first-person diary entry. Each pawn's diary is shown in an **inspector
tab next to the vanilla Log tab**.

Each pawn also has saved diary controls in that tab: a **persona preset** that shapes their
writing voice, and a checkbox to disable LLM diary generation for that pawn while keeping raw
events recorded.

Pairwise events (interactions, social fights) produce two entries — one from each pawn's
point of view — by default as **paired sequential POV**: first the initiator entry, then
the recipient entry with the initiator's generated text included as hidden continuity
context. Solo events (mental breaks) produce a single entry from the breaking pawn's point
of view.

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
└─ *.md                         docs (this file, CLAUDE.md, CSHARP-NOTES.md, README.md)
```

The table lists files by name; all `.cs` live under `Source/<area>/` per the tree above.

| File | Responsibility |
|------|----------------|
| `About/About.xml` | Mod metadata. `packageId = aimml.pawndiary`, supports RimWorld 1.6. |
| `InteractionGroups.cs` | Defines `DiaryInteractionGroupDef : Def` (the group type + `Matches`) and the `InteractionGroups` classifier over the `DefDatabase`. The group **data** now lives in XML (see `1.6/Defs/` below), so groups/prompts are editable without recompiling. |
| `Languages/English/Keyed/PawnDiary.xml` | UI strings (currently the Diary tab label). |
| `Source/Properties/AssemblyInfo.cs` | Assembly metadata. |
| `Source/PawnDiary.csproj` | Build config (.NET Framework 4.7.2; recursive `**\*.cs` glob, so new files need no project edit), outputs to `1.6/Assemblies/PawnDiary.dll`. `Source/PawnDiary.slnx` is the solution. |
| `DiaryModStartup.cs` | `[StaticConstructorOnStartup]`: applies Harmony patches and injects the Diary `ITab` after the Log tab on humanlike pawn defs. |
| `DiaryPatches.cs` | Harmony postfixes: `PlayLog.Add` → `RecordInteraction`; `MentalStateHandler.TryStartMentalState` → `RecordMentalState` (social fights + mental breaks). |
| `DiaryGameComponent.cs` | Orchestrator: recording, generation queueing, applying results, save/load, lookups. (Context/prompt building and the data models were split into the files below.) |
| `DiaryEvent.cs` | The `DiaryEvent` model: per-POV text, context, prompts, generated text, status; save/load; applying LLM results (incl. legacy dual-POV parse). |
| `PawnDiaryRecord.cs` | The `PawnDiaryRecord` model: one pawn's event-id index + legacy entries + saved persona/generation toggle; save/load. |
| `DiaryContextBuilder.cs` | Static helpers turning game state into the compact context strings (pawn profile, surroundings, relationship/continuity, opinions) + formatting/bucket helpers. |
| `DiaryPromptBuilder.cs` | Static helpers that assemble the final prompt text (single, paired sequential, solo). |
| `LlmClient.cs` | Async HTTP client to the endpoint: queueing, concurrency gate, timeouts, retries, request/response JSON. Defines `LlmGenerationRequest` / `LlmGenerationResult`. |
| `MiniJson.cs` | Dependency-free JSON parser (see §8). |
| `PawnDiarySettings.cs` | All mod settings + clamping + save/load, including per-group enable/instruction overrides (keyed by group `defName`). |
| `DiaryTuningDef.cs` | Defines `DiaryTuningDef : Def` + `DiaryTuning.Current` (tuning knobs: dedup windows, thresholds, buckets). Data lives in XML; falls back to safe code defaults if the Def is absent. |
| `DiaryPromptDef.cs` | Defines `DiaryPromptDef : Def` + `DiaryPrompts.Current` (single-entry instructions, recipient follow-up instruction, legacy dual markers, and the default system prompt). |
| `DiaryPersonaDef.cs` | Defines `DiaryPersonaDef : Def` + `DiaryPersonas` lookup/fallback helpers. Data lives in XML and is selected per pawn. |
| `PawnDiaryMod.cs` | `Mod` class, settings UI, `ModelListClient` (fetch model list), `EndpointUtility` (URL building). |
| `ITab_Pawn_Diary.cs` | The inspector tab that renders a pawn's diary and exposes that pawn's persona/generation controls. |
| `DiaryEntry.cs` | `DiaryEntry` (legacy stored entry) and `DiaryEntryView` (display model: `DisplayText`/`StatusText`/`DebugText`). |
| `1.6/Defs/*.xml` | **Editable data Defs** loaded at startup (no recompile): `DiaryInteractionGroupDefs.xml` (the 16 groups + matchers + prompts), `DiaryTuningDef.xml` (tuning numbers), `DiaryPromptDef.xml` (prompt instructions, legacy markers, system prompt/default persona), and `DiaryPersonaDefs.xml` (selectable writing personas). |
| `CSHARP-NOTES.md` | Primer mapping the C#/RimWorld idioms used here (Defs/`DefDatabase`, `IExposable`, Harmony, `ref`/`out`, `async`, LINQ, …) to JS/TS analogies. |
| `prompt-lab/` | Standalone Node harness for tinkering with prompts **outside the game** (see its own README). Editable fixtures mirror the mod's prompt format; the runner fires them at the endpoint and prints/parses the result. `personas.txt` is the writing-style catalog used in fixtures. `_system.txt` is the shared system prompt. Not loaded by RimWorld. |

---

## 3. Data flow

```
Pawn interaction                         Pawn enters a mental state
      │  (vanilla logs it)                     │  (social fight / break)
      ▼                                        ▼
PlayLog.Add ─postfix─▶ PlayLogAddPatch   MentalStateHandler.TryStartMentalState ─postfix─▶ MentalStateStartPatch
      │                                        │   (reads handler.pawn, stateDef, otherPawn, reason)
      ▼                                        ▼
DiaryGameComponent.RecordInteraction     DiaryGameComponent.RecordMentalState
      │                                        │   • group enabled? (§5)
      │                                        │   • SocialFighting + humanlike otherPawn → pairwise (dedup mirrored call)
      │                                        │   • otherwise → solo break (breaking pawn's POV)
      │   • significance filter (§5)            │
      │   • Small talk group → buffered by pawn pair, then flushed as one combined DiaryEvent
      │   • all other enabled interactions → builds one DiaryEvent immediately
      │   • adds event to diaryEvents + the involved pawns' PawnDiaryRecord(s)
      │   • queues generation if the pawn allows it (pairwise: paired sequential/single §4; solo: single initiator)
      ▼                                        ▼
LlmClient.Enqueue ─▶ concurrency gate ─▶ SendOnce(HTTP) ─▶ Completed queue
      ▼
DiaryGameComponent.GameComponentTick (every tick)
      │   TryDequeueCompleted → ApplyLlmResult → DiaryEvent updated
      ▼
ITab_Pawn_Diary.FillTab
      │   EntriesFor(pawn) → renders entries, and lazily queues any not-yet-generated
```

---

## 4. Generation modes

Controlled by the `dualPovGeneration` setting.

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
- Initiator generated on record; recipient generated lazily when that pawn's tab is
  opened. Two separate requests. Kept for fallback; off by default.

### Solo events (mental breaks)
- `DiaryEvent.solo == true`. Only the breaking pawn is involved (recipient fields blank).
  Generation always uses the single initiator POV via `BuildSoloPrompt` (a `mental_event`
  prompt). The target of a targeted break (e.g. MurderousRage) is named in the text/context
  but does not get their own entry.

All paths share the same per-event context fields and the system prompt.

### Per-pawn persona and generation toggle
- Each `PawnDiaryRecord` saves `personaDefName` and `diaryGenerationEnabled`.
- Persona options are `DiaryPersonaDef` XML Defs in `1.6/Defs/DiaryPersonaDefs.xml`, with
  `DiaryPromptDef.defaultPersonaDefName` providing the fallback/default (`DiaryPersona_StoicSurvivor`).
- The Diary tab creates a pawn's record on first edit/open and shows a checkbox plus persona
  selector above the entries.
- Disabled generation does **not** delete or skip events; it only prevents future LLM requests for
  that pawn. The raw event text still appears in the diary.
- In paired sequential mode, if the initiator has generation disabled but the recipient does not,
  the recipient can still generate from the base event prompt without hidden initiator context.

### Prompt context — signal only, no filler
Prompts are assembled line-by-line via `AppendField`, which **drops any field that is empty
or a placeholder** (`none` / `n/a` / `unknown`). Builders are written to return empty when
there's nothing worth saying, so the model never spends tokens on noise:
- **Persona**: the selected writing-style rule is sent as a `persona:` line, separate from
  gameplay facts such as mood, traits, and relationship.
- **Surroundings** (`BuildSurroundingsSummary`): weather and biome only when **outdoors**;
  temperature only when actually cold (≤0°C) or hot (≥32°C); beauty only when notably nice
  or grim; nearby things / current job only when present.
- **Health** (`BuildHealthSummary`): empty when healthy — only pain, bleeding, downed,
  pain-shock, and notable conditions are sent.
- **Relationship** (`BuildContinuitySummary`): a compact memory line per pawn —
  relation kind (spouse/rival/…) + `opinion ±N (bucket)` + **why** (the top social memories
  driving it, aggregated and signed, via `BuildSocialThoughtsSummary`) + **last wrote** (the
  most recent diary line that pawn produced about the other — a lightweight memory layer for
  continuity).
- Dropped from prompts entirely (still stored on the event): raw per-beat logs,
  `shared_event`, `sequence`, `game`/worker details, and the separate `opinions` line.

---

## 5. Interaction groups (recording + per-group prompt)

Events are organized into **groups**, defined as `DiaryInteractionGroupDef` Defs loaded from
`1.6/Defs/DiaryInteractionGroupDefs.xml` (edit + restart, no recompile). Each group is a single
settings entry with two things: an **enabled toggle** (is it recorded?) and a **diary prompt
instruction** (shared by every event in the group). This replaces the old per-defName
"significant" allowlist *and* the old per-defName instruction map — they are now one system.

Groups have a **domain**: `Interaction` (matches `InteractionDef`s) or `MentalState`
(matches `MentalStateDef`s). Classification is domain-scoped so the two never cross-match:
- `Classify(InteractionDef)` → first matching Interaction-domain group, else **Other**.
- `ClassifyMentalState(MentalStateDef)` → first matching MentalState-domain group, else
  **Mental breaks**.

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
| Other / uncategorized | **off** | anything unmatched (e.g. RiMindInteraction) |
| **Social fights** (mental) | on | `SocialFighting` — pairwise, two POV entries |
| **Mental breaks** (mental) | on | Berserk, Tantrum, Wander_Sad, Binging_*, MurderousRage, … (catch-all for mental states) |

To add/retune groups, edit `1.6/Defs/DiaryInteractionGroupDefs.xml` (`defName`, `label`, `order`,
`domain`, `defaultEnabled`, `instruction`, `matchDefNames`, `matchTokens`, `catchAll`) and restart
— no recompile. `order` controls classification order within a domain (lowest first; keep the
catch-all highest). Group `defName`s are the stable keys player settings save overrides under, so
don't rename them.

**Tuning knobs** (dedup windows, small-talk batching, scan radius/temperature, and the
mood/pain/beauty/opinion bucket thresholds) likewise live in XML —
`1.6/Defs/DiaryTuningDef.xml`, backing `DiaryTuningDef` / `DiaryTuning.Current`. Every value
defaults to the shipped number, so deleting the file or any field changes nothing.

**Small talk batching:** interactions classified into the `smalltalk` group are buffered per
pawn pair instead of queued immediately. The batch flushes when no new small-talk event arrives
for `smallTalkBatchWindowTicks` (default 2500, about one in-game hour), when the pair reaches
`smallTalkBatchMaxEvents` (default 6), when either pawn's diary tab is opened, or before saving.
Each flushed batch becomes one normal pairwise `DiaryEvent`, so generation still uses the same
paired sequential/single-POV paths as other interactions. Important social groups are not
batched: for example `DeepTalk` is classified under `heartfelt`, so it records and queues
immediately.

**Mental states** are captured via the `MentalStateHandler.TryStartMentalState` postfix:
- `SocialFighting` with a humanlike `otherPawn` → a **pairwise** event (both pawns).
  `TryStartMentalState` fires once per participant, so the mirrored call is de-duplicated by
  pair within `SocialFightDedupTicks` (300).
- Any other mental state → a **solo** event from the breaking pawn (de-duplicated per
  pawn+state within `MentalBreakDedupTicks` ≈ one day). A target pawn, if any, is named in
  the text but gets no entry of their own.
- Only humanlike pawns are recorded (animal manhunter/panic states are ignored).

---

## 6. Settings (`PawnDiarySettings`)

| Setting | Default | Range / notes |
|---------|---------|---------------|
| `endpointUrl` | `http://localhost:1234/v1` | Base URL; `/chat/completions` and `/models` are derived from it. |
| `apiKey` | _(empty)_ | Sent as `Authorization: Bearer` when set. |
| `modelName` | `local-model` | Picked via "Fetch models" or typed. |
| `timeoutSeconds` | 30 | 5–300. **Per-request deadline** — also the "stuck request" purge window (§7). |
| `maxConcurrentRequests` | 4 | 1–16. Max requests in flight at once; the rest queue. **Use 1 for a single local model.** |
| `maxTokens` | 160 | 32–2048. Applied to each one-entry request. |
| `temperature` | 0.8 | 0–2. |
| `dualPovGeneration` | true | Paired sequential vs. lazy single POV (§4). |
| `systemPrompt` | from `DiaryPromptDef` XML | Sent as a `system` message; edit `1.6/Defs/DiaryPromptDef.xml` then click "Restore default" to apply (existing saves persist their saved value). |
| `groupEnabled` / `groupInstructions` | per-group defaults | Maps keyed by `InteractionGroup.Key` (see §5). Absent key = use the group's default enabled state / default instruction. |
| `enableLlm`, `keepRawEntryOnFailure`, `sendApiKeyAsBearerToken` | true | Currently forced on in `ClampValues`. |

Settings UI lives in `PawnDiaryMod.DoSettingsWindowContents` (connection, model fetch,
paired POV toggle, concurrency slider, system prompt editor, and the interaction-group editor —
a checkbox per group plus a prompt editor for the selected group).

Per-pawn diary controls live in `ITab_Pawn_Diary`, not the global mod settings window. They are
stored in each pawn's `PawnDiaryRecord`: persona preset and `diaryGenerationEnabled` (default
enabled).

---

## 7. Concurrency & reliability (`LlmClient`)

- **Queue + gate:** every request passes through a `SemaphoreSlim` sized to
  `maxConcurrentRequests`. At most N are in flight (awaiting a response/error); the rest
  wait in line. Requests are **never dropped** for being "too many".
- **Stuck-request purge:** once a request acquires a slot, a single hard deadline
  (`timeoutSeconds`) covers the whole request **including retries**. When it fires the
  request is abandoned, reported as `"Timed out waiting for the model."`, and the slot is
  freed. The deadline clock starts only after leaving the queue, so waiting in line does
  not count against it.
- **Stale-session purge:** `BeginSession` (new game / load) bumps a session id and cancels
  the old token. Requests from an ended session are dropped instead of wasting a slot.
- **Retries:** up to 3 attempts for transient errors (429, 5xx, network) within the
  deadline; permanent errors (other 4xx, empty content) fail immediately.

---

## 8. RimWorld / Mono constraints (important)

- The mod runs on RimWorld's **Unity Mono runtime**, which only has the assemblies in
  `RimWorldWin64_Data/Managed`. A mod can reference anything at compile time, but
  referencing an assembly not present at runtime causes a `TypeLoadException`.
- **`System.Web.Extensions` / `JavaScriptSerializer` is NOT available at runtime.** JSON is
  therefore parsed with the hand-written `MiniJson` (objects → `Dictionary<string,object>`,
  arrays → `object[]`, etc.). Do not reintroduce `JavaScriptSerializer` or add a NuGet
  JSON library unless it is shipped in the mod's own `Assemblies`.

---

## 9. UI

- **Inspector tab** (`ITab_Pawn_Diary`), injected after the vanilla **Log** tab on all
  humanlike pawn defs at startup. Renders newest-first: date + status header, the diary
  text (generated, or the raw fallback line while pending/failed), and a small grey debug
  block (event id, POV, endpoint, model, status, error, prompt).
- There is **no** standalone window or gizmo anymore, and **no** colony/neutral events
  view (both removed — see Changelog).

---

## 10. Persistence (save/load)

- `DiaryGameComponent.ExposeData` saves `diaries` (per-pawn records) and `diaryEvents`.
- `DiaryEvent` is `IExposable`; generated text, statuses, errors, and context summaries are
  scribed. Prompts are not scribed (rebuilt on demand).
- Pending requests are not persisted; on load, pending statuses normalize back to
  not-generated and regenerate when next viewed.
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

## 12. Changelog

- **2026-06-14 (in-game persona layer + per-pawn generation toggle)**
  - Added `DiaryPersonaDef` + `1.6/Defs/DiaryPersonaDefs.xml`, copying the 12 prompt-lab writing
    personas into RimWorld-loaded XML Defs.
  - Added `DiaryPromptDef.defaultPersonaDefName` so the default pawn persona can be changed in XML.
  - Added per-pawn saved fields on `PawnDiaryRecord`: `personaDefName` and
    `diaryGenerationEnabled` (default enabled).
  - Added controls at the top of the pawn Diary inspector tab: persona selector and a checkbox to
    pause LLM generation for that pawn without losing raw recorded events.
  - Prompt builders now include the selected persona as a `persona:` line. Paired sequential
    generation respects disabled pawns and lets an enabled recipient generate without hidden
    initiator context if the initiator is disabled.

- **2026-06-14 (small-talk batching)**
  - Added per-pawn-pair batching for the `smalltalk` interaction group (`Chitchat`,
    `Conversation`, `HangOut`, `OfferFood`, etc.). A batch flushes after a quiet window, at a
    max event count, when either pawn's diary opens, or before save.
  - Added XML-backed tuning knobs: `smallTalkBatchWindowTicks` and
    `smallTalkBatchMaxEvents`.

- **2026-06-14 (paired sequential POV generation)**
  - Replaced live dual-POV generation with a paired sequential flow: initiator request first,
    recipient request second after the initiator result is applied.
  - Rewrote pairwise prompts to request one diary entry at a time. Recipient prompts now include
    the generated initiator entry as hidden continuity context.
  - Updated the settings label/help and prompt Def defaults to match the new behavior. Legacy
    dual-marker parsing remains dormant for compatibility.

- **2026-06-14 (DiaryPromptDef: single XML source of truth for markers/prompts)**
  - Added `DiaryPromptDef` (`Source/Defs/DiaryPromptDef.cs`) + `1.6/Defs/DiaryPromptDef.xml`
    holding `dualInstruction`, `initiatorMarker`, `recipientMarker`, and the `systemPrompt`
    default. Read via `DiaryPrompts.Current` with safe-code-default fallback (same pattern as
    `DiaryTuning`).
  - The old dual prompt/parser path read its markers/instruction from `DiaryPrompts.Current`
    instead of three hardcoded copies.
  - `PawnDiarySettings.DefaultSystemPrompt` is now a property reading from
    `DiaryPrompts.Current.systemPrompt`. Existing saves keep their stored system prompt; edit
    the XML and click "Restore default" in-game to adopt the new default.

- **2026-06-14 (maintainability refactor: rename, split, XML Defs, docs)**
  - **Renamed the project files** `ClassLibrary1.csproj`/`.slnx` → `PawnDiary.*` (the code,
    namespace, assembly, and output DLL were already `PawnDiary`). The mod folder is unchanged
    (RimWorld identifies the mod by `packageId`, not folder name).
  - **Split the 1968-line `DiaryGameComponent.cs`** into focused files with no behavior change:
    `DiaryEvent.cs` + `PawnDiaryRecord.cs` (data models), `DiaryContextBuilder.cs` (context
    summaries + formatting/bucket helpers, now `static`), and `DiaryPromptBuilder.cs` (prompt
    assembly). The component is now a ~570-line orchestrator. Removed the dead `BuildXmlSummary`.
  - **Moved the interaction-group catalog to XML Defs.** `InteractionGroup` is now
    `DiaryInteractionGroupDef : Def`; the 16 groups live in `1.6/Defs/DiaryInteractionGroupDefs.xml`
    and load via `DefDatabase`. Group `defName`s equal the old keys, so saved per-group settings
    still apply. Add/retune groups by editing XML — no recompile.
  - **Moved tuning magic-numbers to a `DiaryTuningDef`** (`1.6/Defs/DiaryTuningDef.xml`, read via
    `DiaryTuning.Current` with a safe-defaults fallback): dedup windows, scan radius/temperature,
    capacity/health thresholds, diary-line length, and the beauty/mood/pain/opinion buckets.
  - **Documentation pass for non-C# maintainers:** added `CSHARP-NOTES.md` (a JS/TS-oriented primer
    on Defs, `IExposable`, Harmony, `ref`/`out`, `async`, LINQ, …), a plain-English header comment on
    every `.cs` file, and XML `///` docs on the main public types/methods.
  - **Organized the repo to RimWorld convention:** moved all C# + `PawnDiary.csproj`/`.slnx` +
    `Properties/` + `Libs/` into a `Source/` folder (the game ignores it), grouped by concern
    (`Core/`, `Models/`, `Generation/`, `Defs/`, `Patches/`, `UI/`, `Settings/`, `Util/`). The root
    now holds only `About/`, `1.6/`, `Languages/`, `Source/`, `prompt-lab/`, and docs. The `.csproj`
    now uses a recursive `**\*.cs` glob (new files auto-included); build is
    `MSBuild Source\PawnDiary.csproj` (output unchanged: `1.6/Assemblies/PawnDiary.dll`).

- **2026-06-13 (persona writing styles)**
  - Added `prompt-lab/personas.txt` — a catalog of 12 writing-style personas (stoic-survivor,
    fiery-hothead, melancholy-dreamer, earnest-optimist, cynical-realist, anxious-worrier,
    gentle-caretaker, grim-veteran, whimsical-eccentric, noble-idealist, bitter-loner,
    eager-socialite) with short voice descriptions.
  - Persona is now a **separate line** in fixturess (`initiator persona: random`,
    `recipient persona: random`, `you persona: random`) — the catalog stays out of the prompt.
    `run.js` resolves `random` to a randomly picked persona description at runtime.
  - Replaced `traits:` field with the persona system in all 6 prompt-lab fixtures.
  - Updated `_system.txt`: the model is now told to match the pawn's writing persona — their
    voice, sentence rhythm, and emotional register — rather than raw trait names.

- **2026-06-13 (prompt-lab)**
  - Added `prompt-lab/` — a dependency-free Node harness to iterate on prompts outside the
    game. Hand-authored fixtures (one per event type: dual interaction, social fight, solo
    mental break, single POV) mirror the mod's compact format using realistic def labels;
    `run.js` fires them at the configured endpoint and prints the response (+ parsed dual
    split). Shared system prompt in `prompts/_system.txt`.

- **2026-06-13 (lean prompt context)**
  - Reworked prompts to send **signal only**. New `AppendField` skips empty/placeholder
    fields; all three prompt builders (single, dual, solo) were rewritten compactly and now
    omit raw logs, `shared_event`, `sequence`, `game`/worker details, and `opinions`.
  - **Conditional context:** weather/biome only outdoors; temperature only when cold/hot;
    beauty only when notable; health omitted when healthy.
  - **Extended relationship (memory layer):** relationship now reads as relation kind +
    `opinion ±N (bucket)` + the social memories driving it (`BuildSocialThoughtsSummary`
    via `ThoughtHandler.GetSocialThoughts`) + the pawn's last diary line about the other.
  - Removed now-dead `BuildSharedEventSummary` and `FormatBeauty`.

- **2026-06-13 (social fights & mental breaks)**
  - Added capture of **mental states** via a `MentalStateHandler.TryStartMentalState`
    postfix (`MentalStateStartPatch`). `SocialFighting` → pairwise diary event (both POVs,
    mirrored-call de-dup); all other mental breaks → **solo** events from the breaking pawn,
    naming any target.
  - Added `DiaryEvent.solo` (single-POV, `BuildSoloPrompt`); `CanQueueDual` returns false for
    solo. Extracted `AddPairwiseEvent` (shared by interactions + social fights) and
    `AddSoloEvent`.
  - Interaction groups gained a **domain** (Interaction / MentalState) with domain-scoped
    classification; added **Social fights** and **Mental breaks** groups (both default on),
    shown under a "Mental states & breaks" header in settings.
  - Resolved the prior caveat: social fights ARE now captured (via the mental-state path, not
    `PlayLogEntry_Interaction`).

- **2026-06-13 (interaction groups)**
  - Consolidated the significance filter and the per-interaction prompt instructions into
    **interaction groups** (`InteractionGroups.cs`). Each group has one enable toggle + one
    diary prompt, edited together in settings. Recording now = "is this interaction's group
    enabled"; the prompt = the group's instruction.
  - Squashed the long per-def list into ~14 themed groups (Romance, Recruitment, Conversion,
    Rituals, Animal, NSFW, Teaching, Small talk, …). Defaults: most on; Teaching, Small talk,
    and Other off.
  - Removed `InteractionPromptTemplates.cs`, `Defs/InteractionPromptTemplates.xml`, and the
    old `SignificantInteractionNames`/`Tokens` sets. Settings now store per-group
    enable/instruction maps (old `interactionInstructions` is dropped; instruction overrides
    from older saves are not migrated).

- **2026-06-13**
  - Fixed runtime crash: replaced `JavaScriptSerializer` with `MiniJson` (Mono lacks
    `System.Web.Extensions`). Generation works again.
  - Added **dual-POV generation** (both entries in one request) behind `dualPovGeneration`
    (default on); kept the single-POV path as a disabled fallback.
  - Added a **system prompt** (configurable, sent as a `system` message).
  - Reworked `LlmClient` into a **queue + concurrency gate** (`maxConcurrentRequests`,
    default 4, range 1–16); overflow waits instead of failing. Added a **per-request hard
    deadline** that purges stuck requests, plus stale-session purge.
  - **Removed event batching** — each significant interaction is now its own diary entry
    (no buffering/merging of beats).
  - **UI rework:** moved the diary from a gizmo + window to an **inspector tab next to the
    Log tab** (`ITab_Pawn_Diary`); **removed the colony/neutral events view**.
  - Added this documentation file.
