# Changelog

Dated history of every change to the mod. **Add an entry here with each change** (newest first).
This is the single history file that `DOCUMENTATION.md` and `AGENTS.md` both point to; the design
doc itself describes only "what happens now".

- **2026-06-15 (multi-model debug logs)**
  - Added `[PawnDiary debug]` RimWorld log lines around API lane selection and LLM failover:
    configured vs active lane counts, selected primary model, failover order, skipped blank/duplicate
    lanes, each attempted model, success lane, and all-lanes-failed summaries.

- **2026-06-15 (starting persona randomization)**
  - New pawn diary records now start with a random `DiaryPersonaDef` instead of always using the
    configured default persona. Existing saves and existing pawn records keep their saved persona.

- **2026-06-15 (subtle diary model tag)**
  - Diary cards now show the generating model id as a tiny, low-contrast note at the bottom-right
    of each generated entry, making multi-model provenance visible without drawing attention.
  - Increased the model tag's line box so the tiny text is not clipped by RimWorld's UI font metrics.

- **2026-06-15 (automatic failover to the next model)**
  - On an error, a request now **falls over to the next configured API/model** instead of failing.
    `SendWithRetries` was restructured to try the chosen lane first, then each other lane in order;
    a lane that hits a permanent error, exhausts its transient retries, or times out hands off to
    the next. A failure is only reported when **every** lane fails. Per-lane logic was extracted into
    `TryLane`; candidate order is built by `BuildAttemptTargets` (LlmClient) / `BuildFailoverTargets`
    (DiaryGameComponent).
  - The deadline (`timeoutSeconds`) is now **per lane attempt** (worst-case total ≈ lanes × timeout).
  - `LlmGenerationResult` now reports the lane that actually produced the text (`endpointUrl`,
    `modelName`); `ApplyLlmResult` updates the recorded LLM meta so the debug block and **paired
    recipient pinning** follow the lane that really worked, not just the originally chosen one.
  - Single-API users are unaffected (no other lanes ⇒ no failover; behavior identical to before).

- **2026-06-15 (parallel multi-API generation)**
  - Settings now hold a **list of API lanes** (`apiEndpoints`, `List<ApiEndpointConfig>`) instead of
    a single endpoint/key/model. Each lane is one endpoint + key + **one** model; add more rows for
    more models. Old single-endpoint saves migrate into one row automatically (legacy
    `endpointUrl`/`apiKey`/`modelName` fields are kept only to seed it).
  - Diary requests are **distributed across lanes round-robin** and run in parallel. `LlmClient`'s
    single global concurrency gate became **one gate per lane** (keyed by endpoint+model), each
    sized to `maxConcurrentRequests`; lanes run independently. Added `LlmClient.NextRoundRobinIndex`.
  - **Sequential pinning:** in paired dual-POV mode the recipient reuses the **same lane the
    initiator used** (matched by the endpoint+model recorded on the event), so a sequential pair
    stays on one model. Target selection lives in `DiaryGameComponent.SelectApiTarget`.
  - Rebuilt the settings UI as an **array editor** with **+ Add API** / **− Remove** / **Reset**
    buttons and per-row "Fetch models" + "Pick"; the per-concurrency slider is now labelled per-API.
  - Added keys `PawnDiary.Settings.ApisHeader/ApiLabel/AddApi/RemoveApi` and
    `PawnDiary.Error.NoApiConfigured`; updated the max-concurrent help text.
- **2026-06-15 (diary review fixes)**
  - Fixed diary entry text clipping: `EntryHeight` measured wrapped text at `width - 16f` but the
    tab drew it at `width - 20f`, so long entries overflowed their card. Both now use `width - 20f`.
  - Restored the per-pawn persona picker to the Diary tab (it had been hidden during the UI
    rework, leaving `PersonaFor`/`SetPersona` unreachable). `ControlsHeight` grew to fit it.
  - Capacity keywords now honor the tunable `lowCapacityThreshold` again instead of a hardcoded
    `0.80f`; the def default (and `DiaryTuningDef.xml`) is set to `0.80` so behavior is unchanged.
  - Replaced `IsCombatRelated`'s `defName` substring sniffing with a data-driven `combat` bool on
    `DiaryInteractionGroupDef` (set `true` on the `insults` group in XML); mirrors the `important` flag.
  - Cached the roleplay `GUIStyle`s instead of allocating one per line every frame, and measure
    each entry's height once per frame (reused by the height sum, scroll jump, and draw loop).
  - Fixed malformed `LinkedEntryView` XML doc comments (missing `<summary>` open tags).

- **2026-06-15 (revised diary context)**
  - Removed `traits=` from pawn summary to reduce token usage and focus on more impactful context.
  - Changed `top_thoughts=` to `thoughts=`: now sends exactly one positive and one negative
    thought, selected via weighted random (stronger effects are more likely to be chosen).
  - Changed `low_capacities=` to only show Moving, Talking, Sight, and Hearing when below 80%,
    using localized keyword labels (e.g. "impaired movement") instead of percentages.
  - Added `my last opener (not repeat):` field — the first sentence of the pawn's most recent
    diary entry, included as a hint to avoid repetitive openings.
  - Added `burning passion:` field — a randomly selected passion (major 3x weight, minor 1x
    weight) from the pawn's skills. Only included for important events (not small talk, chit
    chat, etc.). Major passions show as `"Mining (burning)"`, minor as `"Plants"`.
  - Added `IsImportant()` method to `DiaryEvent` to check if the event's group is important.
  - Added `RandomBurningPassion()` and `LatestDiaryOpener()` methods to `DiaryContextBuilder`.
  - Added `initiatorLastOpener`, `recipientLastOpener`, `initiatorBurningPassion`,
    `recipientBurningPassion` fields to `DiaryEvent` with save/load support.
  - Added capacity keyword localization keys: `PawnDiary.Capacity.Moving`,
    `PawnDiary.Capacity.Talking`, `PawnDiary.Capacity.Sight`, `PawnDiary.Capacity.Hearing`.

- **2026-06-15 (linked diary entries between pawns)**
  - Added `LinkedEntryView` model: a compact, truncated preview of the other pawn's diary
    entry for the same interaction event, attached to each `DiaryEntryView`.
  - In the initiator's diary entry, the recipient's preview card appears **after** the main text.
  - In the recipient's diary entry, the initiator's preview card appears **before** the main text.
  - Clicking a linked-entry card selects the other pawn, opens their Diary tab, and scrolls to
    the same event — mirroring the existing Social-tab click-through pattern.
  - `DiaryEvent.RoleEquals` is now public so the UI tab can compare roles.
  - Added `DiaryGameComponent.FindEventById` as a public accessor for the private `FindEvent`.
  - Added localization keys: `LinkedInitiator`, `LinkedRecipient`, `LinkedNotGenerated`,
    `LinkedNoText`, `LinkedTooltip`.

- **2026-06-15 (social-adjacent diary tab and click-through)**
  - Moved the Diary inspector tab to immediately after the vanilla Social tab.
  - Added PlayLog id tracking on diary events, including batched small-talk rows.
  - Clicking a Social-tab interaction row now opens the Diary tab and scrolls to the matching
    generated entry when one exists; otherwise vanilla click behavior continues.

- **2026-06-15 (diary tab roleplay UI pass)**
  - Moved the Diary inspector tab from the far-right position to immediately after the vanilla
    Health tab.
  - Added roleplay-log rendering for generated entries: narration is muted/italic, and
    dialogue-looking lines are bold/italic with the pawn's favorite color.
  - Added XML-backed `DiaryInteractionGroupDef.important` display metadata and quiet/important
    entry markers in the Diary tab.

- **2026-06-15 (safe social-memory summaries)**
  - Changed `BuildSocialThoughtsSummary` to aggregate only stored social memories instead of
    calling RimWorld's situational social thought recalculation. This keeps animal interactions
    such as Nuzzle from logging vanilla `ThoughtWorker_Joyous` null-reference errors while diary
    prompt context is being built.

- **2026-06-15 (diary tab production UI pass)**
  - Moved the Diary inspector tab to the far-right/final pawn tab position.
  - Changed the Diary tab to show only finished generated entries; pending/raw/debug rows are hidden.
  - Temporarily hid the pawn-tab persona selector/rule text, leaving only the per-pawn generation
    checkbox visible.
  - Added a compact generating badge and switched entry cards to built-in RimWorld `Widgets`
    chrome (`DrawMenuSection`, `DrawTitleBG`, `DrawHighlightIfMouseover`, `LabelFit`).

- **2026-06-15 (docs restructuring)**
  - Extracted this changelog out of `DOCUMENTATION.md` into this file. The design doc now points
    here; keep updating it the same way — a dated entry per change, newest first.
  - Folded `CSHARP-NOTES.md` (the C#/RimWorld→JS/TS primer) into `AGENTS.md` and removed the
    standalone file. The inline `// see CSHARP-NOTES.md` comments now point to `AGENTS.md`.
  - Added two standing rules to `AGENTS.md`: keep new UI/prompt text localization-friendly
    (Keyed `.Translate()`; see `DOCUMENTATION.md §12`), and comment new code extensively for AI
    agents and novice devs.

- **2026-06-15 (localization-friendly strings)**
  - Moved all UI strings (settings window, diary tab, entry status) and all natural-language
    prompt text (event fallback sentences, surroundings/relationship/health words, and the
    mood/beauty/pain/opinion buckets) into `Languages/English/Keyed/PawnDiary.xml`, resolved via
    `.Translate()`. See `DOCUMENTATION.md §12`.
  - Structured prompt field keys (`pov:`, `setting:`, `sex=`, …), the `initiator`/`recipient`
    role words, and the `none`/`n/a`/`unknown` skip-sentinels stay English as a stable schema;
    the `LLM debug` block and background-thread `LlmClient` errors stay English too.
  - No behavior change for English players; a translator can now localize **both** the UI and the
    LLM prompt by translating one Keyed file (plus DefInjected for the persona/group/prompt Defs).

- **2026-06-15 (agent guidance doc rename)**
  - Replaced `CLAUDE.md` with `AGENTS.md` so shared repository agent rules are tool-agnostic.
  - Updated the file-map docs to reference `AGENTS.md`.

- **2026-06-14 (documentation parity fixes)**
  - Corrected the `dualPovGeneration` settings description to match implementation:
    disabling paired mode queues both POV requests independently (not lazily).
  - Corrected persistence docs: pending entries regenerate through the background
    generation scan, not on diary-tab view.

- **2026-06-14 (background-only generation)**
  - All LLM diary generation is now driven by a background tick scan, never by UI actions.
    `EntriesFor` (the diary tab) is a pure read with no side effects.
  - `GameComponentTick` runs `QueueAllPendingGenerations` every ~2 seconds (120 ticks),
    scanning for `not_generated` events and queueing generation where enabled.
  - Generation is also queued immediately on game load and when a pawn's generation is
    re-enabled via the settings checkbox.
  - Single-POV mode now queues both initiator and recipient at record time instead of
    lazily queueing the recipient on tab-open.
  - Small-talk batches no longer flush on diary tab open; they flush purely on timer,
    max-events cap, and save. The doc previously claimed a tab-open flush that was never
    implemented.
  - Removed `FlushSmallTalkBatchesForPawn` (only caller was `EntriesFor`).

- **2026-06-14 (colonist-only diary eligibility)**
  - Diary entries are now generated **only for free colonists** (`pawn.IsColonist`).
    Animals, prisoners, slaves, enemies, and visitors never receive diary entries.
  - Mixed interactions (one colonist + one ineligible pawn) produce a **solo** event for
    the colonist only, preventing the blocking issue where an ineligible initiator (e.g.
    animal Nuzzle) would queue a POV that the colonist recipient's entry depends on in
    sequential mode.
  - Interactions between two ineligible pawns are silently skipped.
  - `RecordMentalState` now requires the breaking pawn to be an eligible colonist; social
    fights between a colonist and a non-colonist produce a solo entry for the colonist.
  - `IsDiaryEligible(pawn)` (humanlike + colonist) replaces the old `IsHumanlike` checks
    throughout recording and event-ref creation.
  - `ITab_Pawn_Diary.IsVisible` now also requires `pawn.IsColonist`, hiding the tab for
    non-colonist humanlikes.
  - Public API guards: `DiaryGenerationEnabledFor`, `SetDiaryGenerationEnabled`,
    `PersonaFor`, `SetPersona` all return early for non-colonist pawns.

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
  - Persona is now a **separate line** in fixtures (`initiator persona: random`,
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
