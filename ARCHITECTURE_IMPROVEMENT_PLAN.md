# Pawn Diary — Architecture Improvement Plan (Open Slices)

Last updated: 2026-06-30 · Last verified against code: 2026-06-30

Implementation-ready plans for the **unresolved** architecture-improvement work. Resolved/completed
cards are intentionally omitted. Each plan below is a self-contained slice to hand to one future
agent. Of the original 12 plans, 9 remain open after **Plans 2 and 3 were completed on 2026-06-29**
and **Plan 6 was completed on 2026-06-30** (see CHANGELOG); their cards are kept below, marked
COMPLETED, for reference. All remaining plans were
checked against the current `Source/` tree on the verify date and confirmed open; current-state notes
are inline where the code has moved (Plans 8, 10, 11, 12). Plan 12 is the former standalone
lasting-game-state roadmap, folded in here. **Plan 12 Passes 1–7 shipped on 2026-06-30** (the observed
conditions system: pure policy + tests, Def/XML/strings, GameComponent scanner, and the MapDanger /
GameCondition / ThingPresent / PawnHediff observers, with the `MetalhorrorSuspicion` window retired in
favor of `AnomalyGrayFleshEvidence`); **Pass 8 remains blocked** on the not-yet-built XML context-fact
pipeline. See CHANGELOG 2026-06-30 and the annotated card below.

## Global Implementation Protocol

For every slice:

1. Read `AGENTS.md`, `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, and the relevant source files named
   in the card.
2. Implement only one card at a time.
3. Preserve no-DLC safety. Optional DLC or mod content must no-op when absent.
4. Preserve save/settings compatibility. Never rename existing Scribe keys unless the card explicitly
   includes a migration plan.
5. Keep prompt/UI text localizable:
   - UI strings go in `Languages/English/Keyed/PawnDiary.xml`.
   - Def labels/instructions/tones/prompts use DefInjected folders.
   - Background-thread transport errors should remain raw English; `.Translate()` is main-thread only.
6. Keep tunables in XML Defs with C# fallbacks.
7. Keep the architecture barrier:
   `impure RimWorld capture -> plain payload/context -> pure decision/planning/parsing/formatting -> impure persistence/transport/UI`.
8. Update `DOCUMENTATION.md` and `CHANGELOG.md` when behavior or structure changes.
9. Rebuild after C# changes:
   `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`.
10. Run relevant tests when touching pure helpers:
    - `tests/DiaryCapturePolicyTests`
    - `tests/DiaryPipelineTests`
    - `tests/DiaryTextDecorationTests`
    - `tests/LlmResponseParserTests`
    - `tests/PromptVariantsTests`

## RimWorld-Specific Guardrails

- Do not add paid DLC dependencies for optional DLC-aware behavior.
- DLC C# types compile even when the DLC is not owned; runtime content/state can be absent. Guard DLC
  behavior with `ModsConfig.<Dlc>Active`, null checks, or plain string matchers.
- Avoid `DefDatabase<T>.GetNamed("DlcDef")` for optional content. Use string matchers or
  `GetNamedSilentFail`.
- Harmony patches should preserve vanilla behavior and isolate failures with one-time logging.
- IMGUI windows redraw every frame. Do not start HTTP requests, heavy scans, or allocations directly
  from layout code except through explicit button actions and cached snapshots.
- Scribe load/save code runs in multiple modes. Any new `IExposable` type needs explicit defaults,
  null repair, and post-load normalization.

---

# Plan 1 — First-Time API Setup And Connection Health UX

## Goal

Make it obvious whether Pawn Diary can currently generate entries, and guide a new player through one
working API lane without requiring them to understand the full advanced lane table.

## Non-Goals

- Do not change LLM request semantics.
- Do not remove advanced API lane controls.
- Do not store or display raw API secrets in diagnostics or labels.

## Primary Files

- `Source/Settings/PawnDiaryMod.SettingsWindow.cs`
- `Source/Settings/PawnDiaryMod.ApiLanes.cs`
- `Source/Settings/ApiConnectionController.cs`
- `Source/Settings/PawnDiarySettings.cs`
- `Source/Pipeline/ApiEndpointPolicy.cs`
- `Source/Settings/EndpointUtility.cs`
- `Source/Core/DiaryGameComponent.ApiLanes.cs`
- `Languages/English/Keyed/PawnDiary.xml`
- `README.md`
- `DOCUMENTATION.md`

## Implementation Steps

1. **Map current settings UI entry points.**
   - Find where the main settings scroll content begins.
   - Find how API lane rows are rendered and how connection/model fetch buttons are wired.
   - Do not move network calls into the draw loop.

2. **Add a health snapshot helper.**
   - Create a small settings-layer DTO, for example `ApiSetupHealthSnapshot`.
   - It should summarize, without network calls:
     - configured lane count;
     - enabled valid lane count;
     - blank URL/model rows;
     - selected routing mode;
     - prompt-test mode;
     - last connection/model-fetch status already held by `ApiConnectionController`.
   - Keep this helper free of Unity GUI code so it can be tested later if useful.

3. **Add a compact setup/health block.**
   - Render it above the advanced API lane list.
   - Show one high-level status line:
     - ready;
     - no lane configured;
     - no enabled usable lane;
     - model missing;
     - last test failed;
     - prompt-test mode active.
   - Add a short explanation and one obvious next action.

4. **Add common local presets.**
   - Preset buttons should create/update one lane with safe defaults:
     - LM Studio / generic local OpenAI-compatible: `http://localhost:1234`;
     - Ollama OpenAI-compatible endpoint;
     - Custom hosted OpenAI-compatible placeholder.
   - Preserve existing lane rows unless the user explicitly resets/replaces.
   - Do not guess an API key.

5. **Add a “test and use” path.**
   - Reuse `ApiConnectionController`.
   - Button should test a target row and, on success, leave it enabled and normalized.
   - Failure should show localized, sanitized status.
   - Keep stale-result detection from existing controller patterns.

6. **Keep advanced controls available.**
   - The health block should not hide expert lane editing.
   - It should point to the existing lane table for advanced models/failover.

7. **Documentation.**
   - Update README with a short first-run recipe.
   - Update `DOCUMENTATION.md` settings/API section.
   - Add changelog entry.

## RimWorld Gotchas

- `.Translate()` only on the main thread.
- Settings are immediate-mode; UI state must live in fields, settings, or controller state, not locals
  that must survive frames.
- Do not test network endpoints automatically every draw.

## Validation

- Build.
- In-game settings smoke test with:
  - no lanes;
  - one blank lane;
  - bad URL;
  - local no-auth endpoint;
  - valid endpoint with model list if available;
  - prompt-test mode enabled.
- Confirm logs do not leak API keys or query strings.

---

# Plan 2 — Tighten Thought Classification And Broad Token Matching

> **Status: COMPLETED 2026-06-29.** Shipped: `DiaryInteractionGroupDef` gained `matchPrefixes` /
> `matchSuffixes` / `matchSegments` (pure helper `Source/Capture/GroupNameMatcher.cs`, unit-tested);
> `thoughtPositive` / `thoughtNegative` rewritten to exact `defName` lists + conservative
> segment/prefix/suffix matchers, dropping the risky substring tokens; opinion-flipped death/loss
> thoughts routed by group order. See CHANGELOG 2026-06-29. Card kept for reference.

## Goal

Reduce wrong prompt tone/importance for vanilla and modded thoughts by replacing risky broad
substring matching with narrower XML policy and, only if necessary, tested matcher semantics.

## Non-Goals

- Do not redesign all interaction-group matching.
- Do not add hard dependencies on optional mods or DLC.
- Do not remove broad fallback coverage without checking vanilla behavior.

## Primary Files

- `1.6/Defs/DiaryInteractionGroupDefs.xml`
- `1.6/Defs/DiarySignalPolicyDefs.xml`
- `Source/Defs/InteractionGroups.cs`
- `Source/Capture/Events/ThoughtEventData.cs`
- `tests/DiaryCapturePolicyTests/Program.cs`
- `Languages/English/DefInjected/PawnDiary.DiaryInteractionGroupDef/`
- `DOCUMENTATION.md`

## Implementation Steps

1. **Inventory current thought groups.**
   - List every `domain=Thought` group.
   - Note exact defName matches, broad tokens, order, `important`, and default enablement.
   - Keep the TODO comments as a checklist until resolved.

2. **Audit vanilla ThoughtDefs.**
   - Use RimWorld def search tools or source XML to identify vanilla defNames currently caught only
     by broad tokens.
   - Convert high-risk tokens into exact defNames where possible.
   - Preserve intended vanilla coverage.

3. **Decide if XML-only is enough.**
   - Preferred first slice: XML-only exact lists and narrower tokens.
   - If still too blunt, add matcher fields to `DiaryInteractionGroupDef`, such as:
     - `matchPrefixes`;
     - `matchSuffixes`;
     - `matchSegments` for CamelCase/underscore segment matching.
   - Keep old `matchTokens` behavior unchanged for compatibility.

4. **If adding matcher code, isolate pure string matching.**
   - Add a small helper that accepts plain strings/lists and returns match/no-match.
   - Test that helper without live DefDatabase.
   - Do not put live `Def` reads into pure tests.

5. **Add tricky fixtures.**
   - Include representative vanilla thoughts.
   - Include invented modded defName strings that should not match broad categories accidentally.
   - Lock expected group or no-group behavior.

6. **Localization.**
   - If group labels/instructions/tones change, update DefInjected English stubs.
   - Avoid blank indexed translation entries for instruction/tone variants.

7. **Documentation.**
   - Document any new matcher fields in XML policy docs.
   - Add changelog entry.

## RimWorld Gotchas

- XML matcher strings are safe for absent DLC/mod content when they are plain strings.
- Do not add unguarded XML references to optional defs.
- Group order matters: first match wins.

## Validation

- Build if C# changes.
- XML parse/load smoke test.
- Run relevant tests.
- In-game smoke: trigger a positive thought, negative thought, ambient thought, and thought
  progression.
- If testing mod compatibility, load target mod as a companion and confirm no errors when absent.

---

# Plan 3 — Real Archive Compaction For Old Diary Events

> **Status: COMPLETED 2026-06-29.** Shipped: `ArchivedDiaryEntry` + `DiaryArchiveRepository` save
> compact display-only old pages under `diaryArchiveEntries`; retention now archive-then-drops old
> completed/stale/failed displayable refs; the Diary tab year index merges hot and archived candidates;
> archived rows do not regenerate or enter LLM/title/orphan scans; dev export and Social-log entry
> lookup include archive rows. Prompt-only dev rows intentionally stay hot because compaction drops full
> prompt text. Design record: `ARCHIVE_COMPACTION_DESIGN.md`.

## Goal

Replace destructive long-history trimming with a two-tier model: hot `DiaryEvent` records for active
generation/maintenance, and cold archive records for cheap permanent display history.

## Non-Goals

- Do not attempt this as a one-hour cleanup.
- Do not change generated text format or prompt policy.
- Do not make archived entries regenerate unless explicitly designed.

## Primary Files

- `Source/Core/DiaryGameComponent.EventRetention.cs`
- `Source/Core/DiaryEventRepository.cs`
- `Source/Core/DiaryGameComponent.cs`
- `Source/Models/DiaryEvent.cs`
- `Source/Models/DiaryEntry.cs`
- `Source/UI/DiaryTabVisibleEntriesCache.cs`
- `Source/UI/ITab_Pawn_Diary*.cs`
- `Source/Settings/PawnDiarySettings.cs`
- `DOCUMENTATION.md`

## Implementation Phases

### Phase 0 — Design Before Code

Create a short design section or separate design note covering:

- new Scribe keys;
- archived record schema;
- migration path;
- UI merge rules;
- behavior for pending/failed old entries;
- dev-mode regeneration limits;
- how active cap/hot window interact with archive.

Do not implement until this is reviewed.

### Phase 1 — Add Archive Data Model

- Add an `ArchivedDiaryEntry` or similarly named `IExposable` type.
- Store only display fields, for example:
  - event id;
  - pawn id;
  - role;
  - tick/date/year;
  - title/display title;
  - generated/display text;
  - event label/group/color cue;
  - linked event/pair metadata needed by UI;
  - model label only if intentionally displayed.
- Exclude raw prompts, raw responses, retry state, title generation state, and transient errors by
  default.

### Phase 2 — Add Archive Repository

- Add `DiaryArchiveRepository` with its own new Scribe key, e.g. `diaryArchiveEntries`.
- Keep `DiaryGameComponent` as lifecycle owner.
- Provide lookup/enumeration by pawn id and year.
- Repair null lists and duplicate keys after load.

### Phase 3 — Convert Old Completed Events

- Change retention so old completed entries are converted to archive before removing hot events.
- For old pending/failed entries, use the existing archived fallback text policy before archiving or
  leave them hot until they age out by explicit rule.
- Scrub hot event refs only after archive records exist.
- Preserve current behavior until a setting or cap triggers conversion.

### Phase 4 — UI Merge

- Teach `DiaryTabVisibleEntriesCache` to merge hot `DiaryEntryView`s and archived display views.
- Keep ordering stable by tick/date/event id.
- Ensure year paging includes archive years.
- Mark archived entries so dev-only regenerate/copy-debug controls can hide or explain limits.

### Phase 5 — Settings And Migration

- Keep old saves loading without archive records.
- New saves should write both hot and archive state.
- Existing `maxActiveDiaryEvents` may become “hot event cap,” not “history cap.”
- Consider a one-time post-load archive pass only after safe UI merge exists.

## RimWorld Gotchas

- Scribe cannot serialize arbitrary dictionaries directly without parallel lists or custom handling.
- `PostLoadInit` must repair nulls and rebuild indexes.
- Save bloat can still happen if archived records keep raw debug strings.
- UI code must not rehydrate archived entries into generation scans.

## Validation

- Build.
- Load old save with no archive key.
- Create/generated entries, force retention, save/load, verify old pages still display.
- Stress test thousands of pages and year paging.
- Verify archived pages do not queue LLM, titles, or orphan recovery.
- Verify corpse diary view includes archive history.

---

# Plan 4 — Dev Diagnostics Dashboard

## Goal

Give maintainers one read-only place to diagnose capture, generation, API, title, orphan, and retention
state during a test run.

## Non-Goals

- Do not build a full save editor.
- Do not expose API secrets.
- Do not make diagnostics required for normal players.

## Primary Files

- `Source/Settings/PawnDiaryMod.SettingsWindow.cs`
- `Source/Settings/PawnDiaryMod.SettingsWidgets.cs`
- `Source/Core/DiaryGameComponent*.cs`
- `Source/Generation/LlmClient.cs`
- `Source/Capture/*`
- `Source/Pipeline/ApiLaneIdentity.cs`
- `Languages/English/Keyed/PawnDiary.xml`
- `DOCUMENTATION.md`

## Implementation Steps

1. **Start with a snapshot DTO.**
   - Add `DiaryDiagnosticsSnapshot` and small child DTOs.
   - Build it on demand from main-thread-safe state.
   - Include counts, not huge object graphs.

2. **Expose component state safely.**
   - `DiaryGameComponent` can report:
     - hot event count;
     - per-status counts;
     - pending/failed/title counts;
     - orphan candidate count;
     - active cap/hot window;
     - recent event ids/labels if cheap.

3. **Expose LLM state safely.**
   - Add a sanitized `LlmClient` diagnostics snapshot:
     - active lane labels;
     - in-flight count;
     - cooldown/failover status;
     - last sanitized error per lane if already stored or easy to store.
   - Never include API keys or full query strings.

4. **Add capture-decision history later.**
   - Phase 1 can show counts only.
   - Phase 2 can add a bounded ring buffer of recent capture decisions.
   - If drop reasons are needed, introduce a richer pure result type carefully rather than stuffing
     strings into every path.

5. **Render dev-only UI.**
   - Add a collapsible diagnostics section visible only in dev mode or behind an advanced setting.
   - Add “refresh” and “copy diagnostics” buttons.
   - Cache the last snapshot; do not recalculate heavy data every frame.

6. **Documentation.**
   - Explain what the dashboard can and cannot prove.
   - Add bug-report instructions using copied diagnostics.

## RimWorld Gotchas

- IMGUI redraws every frame; diagnostics snapshots should be manual or throttled.
- Logs and copied text must not contain secrets.
- Do not enumerate all maps/pawns/events every draw.

## Validation

- Build.
- Open settings in dev mode with no game loaded and with a game loaded.
- Generate a few entries and verify counts move.
- Force an API failure and verify sanitized display.
- Copy diagnostics and inspect for secrets.

---

# Plan 5 — Player-Facing Event Frequency Controls

## Goal

Let normal players reduce or increase diary volume without editing XML, while keeping XML Defs as the
canonical policy layer for maintainers.

## Non-Goals

- Do not expose every XML group as a settings row in this slice.
- Do not change default capture frequency.
- Do not rewrite old entries when settings change.

## Primary Files

- `Source/Settings/PawnDiarySettings.cs`
- `Source/Settings/PawnDiaryMod.SettingsWindow.cs`
- `Source/Core/DiaryGameComponent.*.cs`
- `Source/Capture/CaptureContext.cs`
- `Source/Capture/Events/*EventData.cs`
- `1.6/Defs/DiarySignalPolicyDefs.xml`
- `Languages/English/Keyed/PawnDiary.xml`
- `tests/DiaryCapturePolicyTests/Program.cs`

## Implementation Steps

1. **Define user-level controls.**
   - Add an enum such as `DiaryFrequencyLevel`: Off, Rare, Normal, Frequent.
   - Add settings fields with defaults preserving current behavior.
   - Candidate controls:
     - social;
     - work;
     - thoughts;
     - day reflections;
     - raids/combat if feasible.

2. **Add a policy adapter, not scattered checks.**
   - Create a helper such as `DiaryFrequencyPolicy` that converts settings + source kind into:
     - enabled/gated;
     - chance multiplier;
     - threshold multiplier;
     - cooldown multiplier.
   - Keep it testable with plain inputs where possible.

3. **Wire existing weights through the helper.**
   - Existing `workGenerationWeight` and `socialGenerationWeight` can map to or coexist with the new
     controls.
   - Avoid double-multiplying old settings unexpectedly.

4. **Apply at capture decision boundaries.**
   - Prefer feeding adjusted values into `CaptureContext` or event data policy snapshots.
   - Do not let pure `EventData.Decide` read global settings directly.

5. **Add UI.**
   - Compact setting block with clear wording: affects future entries only.
   - Localize labels/tooltips.

6. **Tests.**
   - Add pure tests for off/rare/normal/frequent behavior where it affects capture decisions.
   - Ensure Normal equals current behavior.

## RimWorld Gotchas

- Settings are global mod settings, not per-save unless already designed otherwise.
- Changing settings mid-save should affect future capture/generation only.
- Avoid a UI with too many sliders; presets are easier for players.

## Validation

- Build.
- Run capture policy tests.
- In-game smoke:
  - social off prevents new social diary events;
  - work rare/frequent changes sampling without errors;
  - day reflection off prevents future reflections;
  - old pages remain visible.

---

# Plan 6 — Save And Settings Compatibility Fixtures

> **Status: COMPLETED 2026-06-30.** Shipped: pure post-load repair extracted from
> `DiaryEvent.NormalizeOnLoad` / `ArchivedDiaryEntry.NormalizeOnLoad` into
> `Source/Pipeline/DiarySaveNormalization.cs` (null-coalesces, cross-slot surroundings chain,
> neutral-text merge, legacy `gameContext`/`instruction` rebuild, year extraction, defensive clamps);
> new pure `tests/DiarySaveNormalizationTests/` (46 assertions) covers the fixture inventory and is
> wired into `.githooks/verify.ps1`. Step 4 chose **Option B**: `tests/SAVE_COMPATIBILITY_SMOKETEST.md`
> runbook for the real-Scribe/settings/colorCue/GUID pieces that cannot be pure-tested. Stable
> Scribe-key contract + migration pattern documented in `DOCUMENTATION.md §9` (§9a/§9b/§9c). No Scribe
> keys renamed; behavior unchanged. See CHANGELOG 2026-06-30. Card kept for reference.

## Goal

Turn current save/settings compatibility reasoning into concrete fixtures or repeatable smoke tests so
future refactors can detect accidental Scribe/key regressions.

## Non-Goals

- Do not force Verse/RimWorld Scribe into existing pure tests if it becomes brittle.
- Do not rewrite save format solely for testing.

## Primary Files

- `Source/Models/DiaryEvent.cs`
- `Source/Core/DiaryEventRepository.cs`
- `Source/Settings/PawnDiarySettings.cs`
- `tests/`
- `.githooks/verify.ps1`
- `DOCUMENTATION.md`

## Implementation Steps

1. **Classify what can be pure-tested.**
   - Null/default normalization helpers can often be extracted and tested.
   - Scribe XML load/save likely needs a RimWorld-referenced harness or manual smoke procedure.

2. **Create fixture inventory.**
   - Pre-title entry.
   - Failed entry.
   - Pending archived fallback candidate.
   - Pair event with recipient state.
   - Neutral arrival.
   - Neutral death.
   - Legacy settings with old persona/prompt/group fields.
   - Missing/blank fields repaired by `NormalizeOnLoad`.

3. **Add pure normalization tests first.**
   - If a normalization branch can be moved into a helper taking plain strings/statuses, test that.
   - Do not weaken production Scribe behavior for test convenience.

4. **Decide on Scribe harness.**
   - Option A: a RimWorld-referenced test project that loads fixture XML through Scribe.
   - Option B: a documented in-game smoke-save procedure.
   - Option C: scripted verification in `.githooks/verify.ps1` only if reliable on developer machines.

5. **Document compatibility contract.**
   - List Scribe keys considered stable.
   - Document allowed migration patterns.

## RimWorld Gotchas

- `Scribe_Collections.Look` dictionary/list behavior differs by mode.
- Some objects need `PostLoadInit` to become valid.
- Standalone tests may fail if they accidentally require Unity/RimWorld initialization.

## Validation

- Fixture or smoke harness passes.
- Existing tests still pass.
- Build.

---

# Plan 7 — Compatibility XML Packs For Popular Mods

## Goal

Improve diary quality for specific popular mods through narrow XML policy, not broad generic matching
or hard dependencies.

## Non-Goals

- Do not guess packageIds/defNames.
- Do not edit Workshop mods.
- Do not make Pawn Diary require the target mod.

## Primary Files

- `1.6/Defs/DiaryInteractionGroupDefs.xml`
- `1.6/Defs/DiaryEventPromptDefs.xml`
- `Languages/English/DefInjected/PawnDiary.DiaryInteractionGroupDef/`
- `Languages/English/DefInjected/PawnDiary.DiaryEventPromptDef/`
- `DOCUMENTATION.md`
- `EVENT_PROMPT_MAP.md` if new prompt routes are meaningful

## Implementation Steps

1. **Choose one target mod per slice.**
   - Use installed mod registry/tools to get exact packageId.
   - Inspect the target mod's XML defs read-only.
   - Identify event sources Pawn Diary already sees: InteractionDef, ThoughtDef, HediffDef,
     AbilityDef, IncidentDef, ritual defs, etc.

2. **Design narrow groups.**
   - Prefer `matchPackageIds` plus exact `matchDefNames`.
   - Use `disableWhenPackageIdsLoaded` only to prevent duplicate/bad generic routing.
   - Keep group order specific before generic catch-alls.

3. **Add prompt policy only where useful.**
   - Use `DiaryEventPromptDef` for source/group-specific prompt or enhancement.
   - Avoid duplicating broad domain prompt text.
   - Add forced model only if there is a clear reason.

4. **Localize Def text.**
   - Update DefInjected labels/instructions/tones/prompts.
   - Keep indexed variant keys aligned with list order.

5. **Absence check.**
   - Confirm the XML is inert when target mod is absent.
   - Plain string matchers are safe; unguarded actual def references are not.

## RimWorld Gotchas

- Workshop directories are read-only.
- PackageId case/spelling matters for matching; use exact packageId from metadata.
- Def names from optional DLC/mods should be plain strings unless guarded.

## Validation

- XML parse/load smoke test without the target mod.
- In-game smoke with the target mod as a companion if available.
- Trigger at least one target event or inspect debug capture/prompt output.

---

# Plan 8 — Generated Social-Log Speech Decision Or Experiment

## Goal

Make a clear decision about the dormant generated Social-log speech surface: quarantine/remove it
further, or revive it as an explicit experimental feature with safeguards.

## Current State

The feature is already dormant: the setting `injectGeneratedSpeechToPlayLog` defaults to `false`
**and** the only call to `TryInjectGeneratedSpeechPlayLogEntry` is commented out in
`Source/Core/DiaryGameComponent.GenerationDispatch.cs`. The runtime path itself (parse the first
speech block, build a `PlayLogEntry_Interaction`, remember its display text) is fully written in
`Source/Core/DiaryGameComponent.PlayLogSpeech.cs`. So the code already sits close to Option A; Option
B means re-enabling that call site behind an explicit dev-only opt-in.

## Non-Goals

- Do not make Social-log speech required for diary correctness.
- Do not enable it by default.
- Do not let generated speech masquerade as reliable vanilla fact without clear opt-in.

## Primary Files

- `Source/Core/DiaryGameComponent.GenerationDispatch.cs` (injection call site, currently commented out)
- `Source/Core/DiaryGameComponent.PlayLogSpeech.cs` (injection logic + generated-speech map fields)
- `Source/Patches/DiarySocialLogPatches.cs` (PlayLog display patch)
- `Source/Pipeline/DiaryDirectSpeechParser.cs`
- `Source/Settings/PawnDiarySettings.cs`
- `Source/Settings/PawnDiaryMod.SettingsWindow.cs`
- `tests/DiaryPipelineTests/Program.cs`
- `DOCUMENTATION.md`

## Implementation Steps

### Option A — Quarantine/Retire

1. Keep legacy Scribe/settings fields loading.
2. Hide or remove any remaining UI references.
3. Add comments explaining the feature is intentionally unsupported.
4. Ensure generated speech maps are pruned and harmless.

### Option B — Experimental Revival

1. Add an advanced/dev-only setting with scary wording.
2. Only inject when:
   - setting enabled;
   - parsed direct speech is valid;
   - event has a real InteractionDef;
   - duplicate guard says no existing row;
   - both pawns/log context still exist.
3. Preserve vanilla PlayLog behavior on failure.
4. Add clear one-time warnings for unsupported cases.
5. Test save/load display of generated speech text.
6. Ensure disabling stops future injection and leaves existing entries stable.

## RimWorld Gotchas

- PlayLog rows and `ToGameStringFromPOV` are fragile UI surfaces.
- Grammar rendering can have side effects with some mods; keep existing SpeakUp guard behavior in mind.
- Generated Social-log text can confuse players if not clearly optional.

## Validation

- Build.
- Parser tests for speech markers.
- In-game social interaction smoke test.
- Save/load with generated row if revived.
- Verify off-by-default behavior.

---

# Plan 9 — Further Diary UI Renderer And Measurer Extraction

## Goal

Reduce risk in the Diary tab by separating card measurement/rendering from tab state and data caching,
without changing visuals or behavior.

## Non-Goals

- Do not redesign Diary UI.
- Do not move Unity GUI code into pure pipeline folders.
- Do not change entry ordering, expansion defaults, or dev controls.

## Primary Files

- `Source/UI/ITab_Pawn_Diary.cs`
- `Source/UI/ITab_Pawn_Diary.EntryCards.cs`
- `Source/UI/ITab_Pawn_Diary.RoleplayText.cs`
- `Source/UI/ITab_Pawn_Diary.Controls.cs`
- `Source/UI/DiaryTabVisibleEntriesCache.cs`
- `Source/Defs/DiaryUiStyleDef.cs`
- `1.6/Defs/DiaryUiStyleDef.xml`

## Implementation Steps

1. **Identify a no-behavior extraction seam.**
   - Good first seam: height measurement helpers.
   - Avoid moving expansion state, scroll state, or selected pawn state initially.

2. **Create UI-layer helper types.**
   - `DiaryEntryCardMeasurer` for height calculations.
   - Later `DiaryEntryCardRenderer` for drawing.
   - Keep them under `Source/UI` and allow Unity/Verse GUI dependencies.

3. **Pass explicit context.**
   - Width, debug flag, style values, highlight state, expansion blend, linked-entry state.
   - Do not let helpers reach back into hidden tab fields unless necessary.

4. **Preserve caches.**
   - Maintain invalidation on render token, width, debug flag, highlight version, and expansion version.
   - Do not recalculate full card heights every frame.

5. **Extract in small commits/slices.**
   - Measurement first.
   - Rendering second.
   - Dev controls last, if ever.

## RimWorld Gotchas

- IMGUI layout and draw can happen many times; allocations show up quickly.
- GUI color/font/style state must be restored after drawing.
- Rich text measurement must match rich text rendering.

## Validation

- Build.
- In-game UI smoke:
  - no entries;
  - one completed entry;
  - linked pair entries;
  - pending/dev entries;
  - long 6,000-page mock history;
  - corpse diary view;
  - year paging and expansion animation.

---

# Plan 10 — Net-New Event Source Expansion

## Goal

Add future event sources only when they have clear storytelling value and fit the existing catalog,
XML, prompt, and test architecture.

## Non-Goals

- Do not add a placeholder enum value without implementation.
- Do not duplicate existing sources such as raids, quests, mood events, or tales.
- Do not fan out colony-wide entries without considering API volume.

## Primary Files

- `Source/Capture/DiaryEventType.cs`
- `Source/Capture/Events/`
- `Source/Capture/Specs/`
- `Source/Capture/Catalog/DiaryEventCatalog.cs`
- `Source/Ingestion/DiaryEvents.cs`, `Source/Ingestion/DiarySignal.cs`
- `Source/Ingestion/Sources/` (the new per-source `DiarySignal` subclass lives here)
- `Source/Core/DiaryGameComponent.*.cs`
- `Source/Patches/`
- `1.6/Defs/DiaryInteractionGroupDefs.xml`
- `1.6/Defs/DiaryEventPromptDefs.xml`
- `1.6/Defs/DiaryPromptTemplateDefs.xml` if a new template is truly needed
- `tests/DiaryCapturePolicyTests/Program.cs`
- `EVENT_PROMPT_MAP.md`

## Implementation Steps

1. **Write a one-paragraph source proposal.**
   - What RimWorld moment?
   - Who writes the page?
   - Why is it not already captured?
   - How often can it fire?
   - Is it DLC/mod optional?

2. **Find the correct hook/scanner.**
   - Prefer stable base-game choke points.
   - Use Harmony only when there is no existing safe scan/source.
   - Fragile or generated-name patches go through defensive manual registration.

3. **Add pure capture layer.**
   - Add `DiaryEventType` value.
   - Add `XxxEventData` with plain fields only.
   - Add `XxxEventSpec`.
   - Register in `DiaryEventCatalog`.
   - Add `Decide` tests.

4. **Add the impure ingestion signal.**
   - Add a `DiarySignal` subclass under `Source/Ingestion/Sources/` (mirror an existing one such as
     `ThoughtSignal`). In its constructor, snapshot live RimWorld facts into `XxxEventData` and gate
     no-DLC/optional-mod content; expose `Payload`, `BuildContext`, `DedupKey`, `DedupWindowTicks`,
     and `Emit`.
   - Do NOT add a per-source dedup dictionary. Dedup is now consolidated by the bus from `DedupKey` /
     `DedupWindowTicks`; colony-wide sources extend `DiaryFanoutSignal` instead.
   - Submit it from the hook/scanner via `DiaryEvents.Submit`. The shared
     `DiaryGameComponent.Dispatch` then runs guard -> pure `Decide` -> dedup -> `Emit`.

5. **Add XML and prompt policy.**
   - Interaction group with domain/source key.
   - Event prompt/enhancement if broad fallback is not enough.
   - Prompt template only if existing templates cannot represent the source.

6. **Update docs and tests.**
   - `EVENT_PROMPT_MAP.md` source map.
   - `DOCUMENTATION.md` event source list.
   - `CHANGELOG.md`.

## RimWorld Gotchas

- Some incidents/tales fire multiple times or per map; dedup carefully.
- Colony-wide fan-out can create many API requests at once.
- DLC methods may exist even when DLC content is absent.

## Validation

- Build.
- Pure capture tests.
- In-game trigger smoke test.
- No-DLC reasoning/smoke for DLC-aware source.
- Verify event filters/frequency controls interact correctly if Plan 5 exists.

---

# Plan 11 — Prompt-Lab Regression Fixtures

## Goal

Give prompt authors a repeatable way to verify assembled prompt structure and representative event
coverage before changing XML prompt text, personas, humor cues, or templates.

## Current State

A structural golden harness already exists: `prompt-lab/golden/cases.json` + `expected.json`, checked
by `prompt-lab/check-golden.js` (and `check-generated.js`). Its cases already assert structure, not
prose (persona absent for death/title, gated enchantments omitted, template selection, sentinel
omission). This plan is therefore **extend + wire**, not build-from-scratch. Remaining gaps: (a) the
harness is not run by `.githooks/verify.ps1`, so it is not yet a regression gate; (b) coverage is by
prompt-assembly shape, not the representative event list below; (c) there is no documented "bless"
workflow for intentional changes.

## Non-Goals

- Do not require a real LLM for automated tests.
- Do not overfit brittle full-prompt snapshots if structural assertions are enough.
- Do not replace human qualitative review.

## Primary Files

- `prompt-lab/`
- `Source/Pipeline/DiaryPromptPlanner.cs`
- `Source/Generation/DiaryPipelineAdapters.cs`
- `Source/Pipeline/DiaryEventPromptKeys.cs`
- `1.6/Defs/DiaryPromptTemplateDefs.xml`
- `1.6/Defs/DiaryEventPromptDefs.xml`
- `1.6/Defs/DiaryPersonaDefs.xml`
- `tests/DiaryPipelineTests/Program.cs` if shared helpers are needed
- `EVENT_PROMPT_MAP.md`

## Implementation Steps

1. **Inventory existing prompt-lab harness.**
   - Determine how fixtures are represented today.
   - Keep any existing workflow compatible.

2. **Choose representative fixtures.**
   - Romance accepted/rejected.
   - Social fight.
   - Raid warning/contact.
   - Death description.
   - Arrival description.
   - Thought progression.
   - Work note.
   - Ritual.
   - Ability.
   - Day reflection.

3. **Assert structure, not model prose.**
   - Required fields present.
   - Forbidden fields absent, e.g. persona absent for death/arrival/title.
   - Correct template chosen.
   - Direct-speech instruction present only where allowed.
   - Forced model carried in metadata, not prompt text.
   - Empty/unknown sentinel values omitted.

4. **Add optional snapshot review.**
   - Store curated prompt snapshots only if maintainers agree to update them intentionally.
   - Prefer normalized snapshots with volatile fields removed.

5. **Document the workflow.**
   - How to run fixtures.
   - How to bless intentional prompt changes.
   - How to add a new fixture when adding an event source.

## RimWorld Gotchas

- Def-backed prompt text may require RimWorld Def loading; keep pure prompt planner tests separate
  from full Def integration unless the harness already handles it.
- Localization/DefInjected text can affect prompt text; snapshots need clear language assumptions.

## Validation

- Prompt-lab fixtures pass.
- Existing pipeline tests still pass.
- Build if C# changes.

---

# Plan 12 — Lasting Game-State Capture (Observed Conditions)

> **Status: Passes 1–7 SHIPPED 2026-06-30. Pass 8 BLOCKED.** Implemented: the pure
> `ObservedConditionPolicy` diff + DTOs (`Source/Capture/ObservedConditions/`),
> `ActiveObservedConditionState` save model, `DiaryObservedConditionDef` + sample defs, the
> `DiaryGameComponent.ObservedConditions.cs` scanner/adapter (saved `activeObservedConditions`,
> per-Def poll gate), the MapDanger / GameCondition / ThingPresent / PawnHediff observers, prompt-bias
> integration beside event windows, and the retirement of the always-on `MetalhorrorSuspicion` event
> window in favor of the `AnomalyGrayFleshEvidence` observed condition. `MetalhorrorEmergence` ships
> disabled (empty matchers) pending the Open-Questions verification of an observable post-emergence
> state. `RecentEvidence` is a defined-but-unwired observer type (no live feed). Pure tests in
> `tests/DiaryObservedConditionTests` (wired into `.githooks/verify.ps1`). Pass 8 (context-fact bridge)
> stays blocked: the XML context-fact pipeline it depends on does not exist yet. See CHANGELOG
> 2026-06-30. Card kept for reference and for the remaining Pass 8 work.

The largest open card, folded in from the former standalone lasting-game-state roadmap. Where Plan 10
adds *moment* sources, this adds *lasting-state* sources: colony conditions that must be read from
live state, not inferred from a guessed duration. Verified open against current code on 2026-06-29 —
none of the types below exist yet.

## Goal

Capture lasting colony problems from live state, not from guessed durations: active map threat,
hostile presence on a home map, active game conditions, anomaly evidence still present, a confirmed
emergence of a hidden problem, or a pawn-level ongoing visible condition that should color prompts.

Target behavior:

- while the game is still in the state, prompts can know about it;
- when the state ends, prompts stop treating it as active;
- a save loaded mid-state is rediscovered by the next scan;
- a missed signal is still recovered by scanning;
- a mere recent clue is called *recent evidence*, not truth.

## Why event windows are not enough

Lasting states today lean on the signal-based event-window system:
`RecordEventWindowSignal(...)` -> `DiaryEventWindowDef` (start/end policy) ->
`ActiveEventWindowState` (saved active windows) -> `ScanEventWindowTimeouts()` (eventual removal).

That fits one-shot narrative moments (birthday, void monolith discovery, prison-break start, ancient
danger letter). It is unreliable for lasting states: a fixed `timeoutTicks` cannot prove a metalhorror
suspicion is still unresolved; a missed end signal leaves stale context; a save/load mid-state drifts
from reality; player response time varies; modded content may reorder signals; and prompts can keep
forcing threat language after the threat is gone. The disabled `MetalhorrorSuspicion` window (turned
off in XML on 2026-06-29 because the `ThingSpawned` gray-flesh signal left it effectively
always-active) is the worked example of this failure.

Rule: `DiaryEventWindowDef` is for signal windows; lasting states need *observed conditions*. Ticks
may control scan interval, debounce, and cooldown — never truth.

## Architecture — observed conditions

A new system parallel to event windows:

1. `GameComponentTickInner()` runs a condition scan on an XML-owned interval.
2. The scanner reads cheap live RimWorld state at the edge.
3. It emits plain `ObservedConditionObservation` DTOs.
4. A pure policy diffs observations against saved active state.
5. The impure adapter applies the plan: start / refresh / end / drop, record diary pages, expose
   prompt candidates.

Same boundary as elsewhere: live reads at the edge, pure testable decisions, XML-owned policy and
wording, additive defensive saved state.

## New Def — `DiaryObservedConditionDef` (`Source/Defs/`)

Fields: `defName`, `label`, `enabled`, `conditionKey`, `scope` (`Map`/`Pawn`/`Colony`), `observerType`
(`MapDanger`/`GameCondition`/`ThingPresent`/`PawnHediff`/`RecentEvidence`), `pollIntervalTicks`,
`startDebounceTicks`, `endDebounceTicks`, `dedupTicks`, `recordStartEvent`, `recordEndEvent`,
`recordScope` (`MapColonists`/`SubjectPawn`), `promptEnabled`, `promptWeight`,
`normalPromptWeightMultiplier`, `colorCue`, `instruction`, `startTextKey`, `endTextKey`,
`promptPriorityKey`, `promptConditionKey`, `promptDescriptionKey`, `promptCueKeys`.

Matchers: `matchDefNames` (exact only by default), `matchDefNameContains` (off unless needed),
`matchLabels` (avoid unless no stable defName), `minDangerRating`, `minHostileCount`,
`includeHomeMapsOnly`, `includeNonPlayerMaps`, `maxEvidenceLabels`, `maxEvidenceChars`,
`recentEvidenceTtlTicks`. Prefer exact `defName` strings — no-DLC safe because absent content simply
never matches.

## Saved state — `ActiveObservedConditionState` (`Source/Models/`)

Fields: `conditionDefName`, `conditionKey`, `scope`, `mapUniqueId`, `subjectPawnId`,
`firstObservedTick`, `lastObservedTick`, `firstMissingTick`, `lastSeenEvidenceDefName`,
`lastSeenEvidenceLabel`, `lastSeenEvidenceCount`, `startRecorded`, `endRecorded`.

Save/load: additive Scribe fields only; null strings normalize to empty in `PostLoadInit`;
missing/removed Defs age out without errors; old saves default to an empty list. Saving active state
avoids duplicate start pages, lets debounce survive save/load, keeps prompt candidates active across
sessions, and ends stale conditions cleanly once live scans stop seeing them.

## Pure policy (`Source/Capture/ObservedConditions/`)

Types: `ObservedConditionObservation`, `ObservedConditionStateSnapshot`, `ObservedConditionDefSnapshot`,
`ObservedConditionDecision`, `ObservedConditionPlan`, `ObservedConditionPolicy`.

Inputs: current tick, saved states, current observations, def snapshots (debounce/cooldown).
Outputs: `StartPending`, `StartRecorded`, `Refresh`, `EndPending`, `EndRecorded`, `DropStale`.

Behavior: a new observation starts pending; the start event records only after `startDebounceTicks`;
continuing observation refreshes `lastObservedTick`/evidence; a missing observation marks
`firstMissingTick`; the end event records only after `endDebounceTicks`; no duplicate start while
observed; multiple maps and pawn-scoped states stay independent.

## Scanner types (start small, exact-match first)

- **MapDanger** (`MapThreatActive`) — active while a player-home map is materially dangerous (danger
  rating, spawned hostile count); recent letters annotate only; skip non-home maps unless XML opts in;
  cap counts/labels.
- **GameCondition** — active while `map.gameConditionManager.ActiveConditions` holds a matching
  defName (solar flare, toxic fallout, eclipse, psychic drone); exact string match; the game owns
  start/end truth.
- **ThingPresent** (`AnomalyEvidencePresent`) — active while matching spawned things/filth/samples
  remain; conservative interval; map/home gate first; cap scan and evidence counts; labels are
  observable evidence, not hidden explanation.
- **PawnHediff** (`MetalhorrorEmergence`, only if a confirmed/observable emergence is identifiable) —
  active while relevant pawns carry a matching hediff; never reveal hidden mechanics; centralize any
  DLC reads in `DlcContext` (see AGENTS.md).
- **RecentEvidence** — bounded fallback when the game exposes only a signal/letter; label it recent
  evidence, give it a TTL, and replace it with a real observer when one becomes available.

## Prompt integration

Add observed-condition candidates beside the existing event-window biasing. Mirror
`ActiveEventWindowPromptCandidates(Pawn pawn)` in `Source/Core/DiaryGameComponent.EventWindows.cs` and
return `List<PromptEnchantmentCandidate>` (that existing candidate type already carries weight plus
normal-prompt suppression), rather than a bespoke `out float` signature. Map-scoped conditions apply
only to pawns on that map; pawn-scoped only to the subject; a stale condition is no longer a candidate
after end debounce.

Context facts: once an XML-selected context-fact pipeline exists, expose keys like `active_map_threat`,
`active_game_condition`, `visible_anomaly_evidence`, `confirmed_anomaly_threat`,
`recent_threat_evidence`. **Verified gap:** that pipeline does not exist yet — there is a per-event
`AddContextFact` renderer in `DiaryPromptPlanner`, but not the XML-configurable fact-selection layer
this depends on, so Pass 8 below stays blocked until it is built.

## Optional event recording

Record start/end diary pages only when the condition is colony-narratively important and observable
and XML opts in (`recordStartEvent`/`recordEndEvent`). Skip pages when the condition only guides tone,
is too noisy, or would reveal hidden mechanics. When recording, reuse the existing solo/map-colonist
event-window style, build `gameContext` as plain key/value facts, and keep prose in XML/Keyed strings.

## Implementation passes

1. **Pure policy + save model** — DTOs, `ActiveObservedConditionState`, the pure diff planner, and
   tests (start/refresh/end, duplicate prevention, reload). No scanner yet.
2. **Def + XML skeleton** — `DiaryObservedConditionDef`, `1.6/Defs/DiaryObservedConditionDefs.xml`,
   Keyed strings; disabled/low-risk sample defs; no-DLC matcher docs in XML comments.
3. **GameComponent integration** — saved `activeObservedConditions`, `nextObservedConditionScanTick`,
   tick scan gate, and scanner dispatcher in `DiaryGameComponent.ObservedConditions.cs`; no heavy
   scanning enabled by default.
4. **MapThreatActive** — the safest first real observer (map danger is broad, visible, not hidden):
   map/home gates, danger/hostile thresholds, prompt candidate, manual raid smoke test.
5. **GameCondition observer** — scan the map's condition manager; exact defName match; prompt
   candidates for active-condition context; no DLC dependency.
6. **AnomalyEvidencePresent** — `ThingPresent` for gray-flesh/evidence; replace the disabled
   signal-plus-timeout suspicion window with observed evidence; describe observable evidence only. Do
   not simply re-enable `MetalhorrorSuspicion` with a longer timeout.
7. **MetalhorrorEmergence** — a confirmed-threat observer using spawned/observable things or hediffs;
   keep it separate from suspicion/evidence; explicit threat wording only once the game reveals it.
8. **Context-fact bridge** — blocked on the XML context-fact pipeline (see above): expose observed
   conditions as facts XML templates can include; add prompt-output tests for solo/pair/neutral.

## What not to do

- Do not inflate `timeoutTicks` to make lasting states "feel" longer.
- Do not treat a trigger as proof a condition is still active.
- Do not scan every thing on every tick.
- Do not put prompt policy/prose in C#, or reference optional DLC defs / `DefDatabase<T>.GetNamed` for
  DLC content.
- Do not reveal hidden anomaly mechanics before the game makes them observable.
- Do not build a global "scrape everything" prompt dump.

## Validation

Pure tests: start debounce; no duplicate start; refresh updates evidence; end debounce; stale state
drops when a Def is disabled/removed; reload with active observation rediscovers without duplicate
start; reload with stale state ends cleanly; multiple maps do not cross-contaminate; pawn-scoped
conditions do not affect unrelated pawns. Plus the XML well-formed check, pure tests, the Debug build,
and the committed-DLL freshness check if C# changed. Manual: a raid produces `MapThreatActive` and
clears after end debounce; a game condition appears/clears as a candidate; anomaly evidence stays
active only while present or within recent-evidence TTL; save/load mid-state self-heals.

## Open questions

- Verify the best vanilla map-danger API in the current RimWorld assembly before coding `MapDanger`.
- Verify the observable metalhorror state before enabling explicit emergence prompts.
- Profile thing scanning in a real save before enabling broad matchers.
- Composite conditions only after the simple observer types work and are tested.

## Done bar

Lasting map/anomaly states are represented by observed conditions, not event-window timeouts; prompt
candidates follow live state after debounce; save/load neither loses nor duplicates active states; XML
can add a condition without new C# unless a new observer type is needed; pure diff behavior is tested;
and a no-DLC game runs cleanly with all optional content absent.
