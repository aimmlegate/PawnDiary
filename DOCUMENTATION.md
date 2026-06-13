# Pawn Diary — Architecture & Behavior

> **Maintenance note:** This file is the living design doc for the mod. Whenever the
> mod's behavior or structure changes, update the relevant section **and** add a line to
> the Changelog at the bottom. Keep it accurate — it is the source of truth for "what
> happens now".

_Last updated: 2026-06-13 (lean prompt context)_

---

## 1. What the mod does

Pawn Diary watches humanlike pawns' social **interactions**, **social fights**, and
**mental breaks**, keeps the meaningful ones, and uses an LLM (any OpenAI-compatible
`/chat/completions` endpoint — e.g. a local LM Studio / llama.cpp server) to rewrite each
event into a short first-person diary entry. Each pawn's diary is shown in an **inspector
tab next to the vanilla Log tab**.

Pairwise events (interactions, social fights) produce two entries — one from each pawn's
point of view — by default in a **single LLM request** ("dual POV"). Solo events (mental
breaks) produce a single entry from the breaking pawn's point of view.

---

## 2. File map

| File | Responsibility |
|------|----------------|
| `About/About.xml` | Mod metadata. `packageId = aimml.pawndiary`, supports RimWorld 1.6. |
| `InteractionGroups.cs` | The interaction **group catalog** + classifier (`InteractionGroups.Classify`). Each group = a themed bucket with a default enabled state and default diary prompt. |
| `Languages/English/Keyed/PawnDiary.xml` | UI strings (currently the Diary tab label). |
| `Properties/AssemblyInfo.cs` | Assembly metadata. |
| `ClassLibrary1.csproj` | Build config. Targets .NET Framework 4.7.2, outputs to `1.6/Assemblies/PawnDiary.dll`. |
| `DiaryModStartup.cs` | `[StaticConstructorOnStartup]`: applies Harmony patches and injects the Diary `ITab` after the Log tab on humanlike pawn defs. |
| `DiaryPatches.cs` | Harmony postfixes: `PlayLog.Add` → `RecordInteraction`; `MentalStateHandler.TryStartMentalState` → `RecordMentalState` (social fights + mental breaks). |
| `DiaryGameComponent.cs` | Core logic: recording, context building, generation queueing, applying results, save/load. Also defines `DiaryEvent` and `PawnDiaryRecord`. |
| `LlmClient.cs` | Async HTTP client to the endpoint: queueing, concurrency gate, timeouts, retries, request/response JSON. Defines `LlmGenerationRequest` / `LlmGenerationResult`. |
| `MiniJson.cs` | Dependency-free JSON parser (see §8). |
| `PawnDiarySettings.cs` | All mod settings + clamping + save/load, including per-group enable/instruction maps. |
| `PawnDiaryMod.cs` | `Mod` class, settings UI, `ModelListClient` (fetch model list), `EndpointUtility` (URL building). |
| `ITab_Pawn_Diary.cs` | The inspector tab that renders a pawn's diary. |
| `DiaryEntry.cs` | `DiaryEntry` (legacy stored entry) and `DiaryEntryView` (display model: `DisplayText`/`StatusText`/`DebugText`). |
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
      │   • builds a DiaryEvent immediately (NO batching — one event per occurrence)
      │   • adds event to diaryEvents + the involved pawns' PawnDiaryRecord(s)
      │   • queues generation (pairwise: dual/single §4; solo: single initiator)
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

### Dual POV (default, `dualPovGeneration = true`)
- One request per event. The prompt (`BuildDualInteractionPrompt`) asks the model to
  return both entries delimited by `[INITIATOR]` and `[RECIPIENT]` markers.
- `DiaryEvent.ParseDualResponse` splits the response and fills both POVs. If markers are
  missing it falls back to using the whole response for both (so nothing is lost).
- Token budget is doubled (`maxTokens * 2`) for the combined response.
- Routed through the `"dual"` POV role; `ApplyDualResult` writes both POVs complete.

### Single POV (legacy, `dualPovGeneration = false`)
- Initiator generated on record; recipient generated lazily when that pawn's tab is
  opened. Two separate requests. Kept for fallback; off by default.

### Solo events (mental breaks)
- `DiaryEvent.solo == true`. Only the breaking pawn is involved (recipient fields blank).
  `CanQueueDual()` returns false for solo, so generation always uses the single initiator
  POV via `BuildSoloPrompt` (a `mental_event` prompt). The target of a targeted break (e.g.
  MurderousRage) is named in the text/context but does not get their own entry.

All paths share the same per-event context fields and the system prompt.

### Prompt context — signal only, no filler
Prompts are assembled line-by-line via `AppendField`, which **drops any field that is empty
or a placeholder** (`none` / `n/a` / `unknown`). Builders are written to return empty when
there's nothing worth saying, so the model never spends tokens on noise:
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

Events are organized into **groups** (`InteractionGroups.cs`). Each group is a single
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
`Settings.IsInteractionEnabled(def)` → `IsGroupEnabled(Classify(def).Key)`. So an interaction
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

To add/retune groups, edit the `InteractionGroups.All` catalog (key, label, domain, default
enabled, default instruction, explicit defNames, and substring tokens).

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
| `maxTokens` | 160 | 32–2048. Doubled for dual-POV requests. |
| `temperature` | 0.8 | 0–2. |
| `dualPovGeneration` | true | Dual vs. single POV (§4). |
| `systemPrompt` | see `DefaultSystemPrompt` | Sent as a `system` message; empty = no system message. |
| `groupEnabled` / `groupInstructions` | per-group defaults | Maps keyed by `InteractionGroup.Key` (see §5). Absent key = use the group's default enabled state / default instruction. |
| `enableLlm`, `keepRawEntryOnFailure`, `sendApiKeyAsBearerToken` | true | Currently forced on in `ClampValues`. |

Settings UI lives in `PawnDiaryMod.DoSettingsWindowContents` (connection, model fetch,
dual toggle, concurrency slider, system prompt editor, and the interaction-group editor —
a checkbox per group plus a prompt editor for the selected group).

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
MSBuild ClassLibrary1.csproj /t:Build /p:Configuration=Debug
```

Output: `1.6/Assemblies/PawnDiary.dll`. Restart RimWorld (or the save) to load the new DLL.

---

## 12. Changelog

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
