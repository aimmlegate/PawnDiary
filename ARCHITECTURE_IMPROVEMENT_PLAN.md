# Pawn Diary Architecture Improvement Plan

Generated: 2026-06-25

This file is a handoff plan for future agent runs. It summarizes the architecture-mode review of the whole `Source/` tree, including the adversarial rebuttal results, suggested slice order, concrete run cards, validation steps, and rejected false leads.

Use this as a planning document, not as a mandate to do every refactor at once. Each run should take one small slice, preserve behavior, update docs/changelog when behavior or structure changes, rebuild, and avoid no-DLC regressions.

## Global Rules For Every Slice

- Follow `AGENTS.md`: smallest safe change, keep docs current, build after the change, do not break no-DLC games.
- Preserve the architecture barrier: impure RimWorld/Verse collection -> plain payload/context -> pure selection/planning/parsing/formatting -> impure transport/persistence/UI adapter.
- Keep tunables in XML with safe C# fallbacks where needed.
- Do not add hardcoded player-facing UI or prompt text.
- Do not reference DLC types/defs in C# for optional DLC-aware behavior. Prefer string matchers in XML.
- Keep save/load compatibility. Any change touching `DiaryEvent.ExposeData`, `PawnDiarySettings.ExposeData`, or `IExposable` fields needs explicit old-save reasoning and tests where feasible.
- C# changes require rebuilding:
  `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`
- If a slice changes behavior or structure, update `DOCUMENTATION.md` and add a dated entry to `CHANGELOG.md` in the same change.

## Recommended Run Order

1. API lane identity/labels: highest dedup value, low behavior risk.
2. Barrier fixes: ~~`PawnFactCapture`~~ (done), `InstructionFor*` off settings DTO, `NameForRole` localization move.
3. Tunables to XML: intoxication, consciousness thresholds, mood condition families.
4. `DiaryContextBuilder` split.
5. `PawnDiaryMod` settings UI/async split.
6. `PawnDiarySettings` persona/override extraction.
7. `DiaryGameComponent.Generation` split: lane selection, titles, pawn lookup.
8. `DiaryGameComponent` repository/state bags.
9. Tier-3 cleanups as opportunity allows.
10. `DiaryEvent` `PovSlot` refactor last, with old-save load tests.
11. One-line stale comment fix at `DiaryEventCatalog.cs:63` can be done anytime.

## Run Card 1: API Lane Identity And Labels

Priority: High

Status: Resolved 2026-06-25. Implemented as shared pure `ApiLaneIdentity` and `ApiLaneLabels`
helpers with call-site rewrites, focused `DiaryPipelineTests`, documentation updates, and a Debug
DLL rebuild.

Evidence:
- `Source/Generation/LlmClient.cs:416` (`LaneKey`)
- `Source/Generation/LlmClient.cs:1189` (`SameAttemptLane`)
- `Source/Core/DiaryGameComponent.Generation.cs:706` / `:724` (`FindMatchingActiveLane`, `SameGenerationLane`)
- `Source/Settings/PawnDiaryMod.cs:1721` / `:1740` (`FetchTargetStillMatches`, `ConnectionTestTargetStillMatches`)
- `LaneList` / `LaneLabel` / `TrimForLog` are duplicated across `LlmClient`, `DiaryGameComponent.Generation`, and `PawnDiaryMod`.

Problem:
- Lane identity is implemented three ways with subtle equality differences. A future endpoint field change can desync failover, cooldowns, and stale settings-row matching.

Suggested change:
- Add one `ApiLaneIdentity` value type, likely under `Source/Settings/` or `Source/Generation/` depending on existing ownership.
- Provide `ApiLaneIdentity.Of(ApiEndpointConfig)`.
- Support explicit equality modes/parameters, for example including or excluding API key and reasoning/model fields as required by current call sites.
- Add a separate sanitized `ApiLaneLabels` helper for logging/display. Never log API keys.

Pros:
- One source of truth for lane identity.
- Removes duplicated label/list formatting.
- Low behavior risk if existing equality quirks are encoded explicitly.

Cons / risks:
- Do not accidentally flatten per-site equality differences.
- Formatter must not leak API keys.

Validation:
- Build.
- Add pure tests if there is an existing suitable test project for settings/generation helpers.
- Manually audit each old call site and verify the new equality mode matches old behavior.

Docs:
- Update architecture/settings sections if a new helper type/module is introduced.
- Add `CHANGELOG.md` entry if code changes.

## Run Card 2: Extract `PawnFactCapture` From `DiaryEvent`

Priority: High

Status: Resolved 2026-06-25. Implemented as a new guarded collector
`Source/Generation/PawnFactCapture.cs` (modeled after `DlcContext`) owning the live pawn reads, with
pure value setters `DiaryEvent.SetTextDecorationFacts` / `SetStaggeredIntensity` replacing the old
`Pawn`-taking capture methods. Event-record call sites in `DiaryGameComponent.EventFactory.cs` now
snapshot plain `int`/`string` values and store them. Saved fields and Scribe keys are unchanged, the
Debug DLL was rebuilt, and all five pure test projects pass (602 assertions).

Evidence:
- `Source/Models/DiaryEvent.cs:1200` (`CaptureTextDecorationContext`)
- `Source/Models/DiaryEvent.cs:1220` (`CaptureStaggeredIntensity`)
- `Source/Models/DiaryEvent.cs:1251` (`StaggeredIntensityForPawn`)
- `Source/Models/DiaryEvent.cs:1263` (`LowConsciousnessStaggeredIntensity`)
- `Source/Models/DiaryEvent.cs:1294` (`IntoxicationStaggeredIntensity`)
- `Source/Models/DiaryEvent.cs:1317` (`IsIntoxicatingHediff`)
- `Source/Models/DiaryEvent.cs:1376` (`PawnTextDecorationContext`)

Problem:
- `DiaryEvent` is a persisted model but reads live `Pawn` health, hediffs, capacities, and traits. This is the clearest pure/impure barrier violation found.

Suggested change:
- Add `Source/Generation/PawnFactCapture.cs` or similar, modeled after `DlcContext` as the one home for guarded pawn reads.
- Move live pawn reads from `DiaryEvent` into that impure collector.
- Leave only serialized text-decoration facts and staggered-intensity fields/setters on `DiaryEvent`.
- Update call sites from `diaryEvent.Capture*(role, pawn)` to collect facts first and set plain values on the event.

Pros:
- Restores `DiaryEvent` purity.
- Makes pawn snapshot policy auditable in one place.
- Unblocks pure tests around `DiaryEvent`/view logic.

Cons / risks:
- Small call-site churn.
- Keep the new collector out of `Source/Pipeline` if it reads live Verse/RimWorld state.

Validation:
- Build.
- Add/extend tests for any pure helper extracted from the old methods.
- Confirm no direct `pawn.genes`, `pawn.royalty`, or `pawn.ideo` reads are introduced outside `DlcContext`.

Docs:
- Update `DOCUMENTATION.md` architecture/barrier discussion.
- Add `CHANGELOG.md` entry.

## Run Card 3: Move Settings Instruction Resolution Off `PawnDiarySettings`

Priority: Medium

Evidence:
- `Source/Settings/PawnDiarySettings.cs:750-1024` (`InstructionFor*` family)
- `Source/Settings/PawnDiarySettings.cs:1060` (`PromptVariants.Pick` via `Rand.Range`)

Problem:
- `PawnDiarySettings` is a save DTO, but it performs DefDatabase classification and RNG policy selection.

Suggested change:
- Introduce `DiaryInstructionResolver` or place the logic on `InteractionGroups` if that is more consistent.
- Settings should keep persisted overrides only.
- Resolver should classify the incoming Def/string, ask settings for the override, and roll prompt variants in the capture path.

Pros:
- Removes classification/RNG from persistence object.
- Makes policy lookup easier to test and reason about.

Cons / risks:
- Mechanical caller churn.
- Preserve the current roll timing. Rebuttal resolved this as safe: comments around `PawnDiarySettings.cs:1057-1059` say the result is immediately frozen into `diaryEvent.instruction` by capture callers.

Validation:
- Build.
- Add pure tests for resolver behavior if feasible.
- Verify callers still persist `diaryEvent.instruction` immediately.

Docs:
- Update `DOCUMENTATION.md` settings/prompt-policy section.
- Add `CHANGELOG.md` entry.

## Run Card 4: Move `DiaryEvent.NameForRole` Localization To Adapter

Priority: Low

Evidence:
- `Source/Models/DiaryEvent.cs:1089` calls `.Translate()`.
- Sole production caller found in review: `DiaryPipelineAdapters.DirectSpeechInstructionFor` around `Source/Pipeline/DiaryPipelineAdapters.cs:355`.

Problem:
- The persisted model reaches localization. This is small but violates the model boundary.

Suggested change:
- Move the colony-label translation into the adapter/caller.
- Do not simply delete the method without replacing the caller behavior.

Pros:
- Keeps localization in the adapter layer.

Cons / risks:
- Minimal.

Validation:
- Build.
- Confirm direct-speech prompt text is unchanged.

Docs:
- Changelog only if the slice changes code structure.

## Run Card 5: Move Hardcoded Tunables To XML

Priority: High

Evidence:
- `Source/Models/DiaryEvent.cs:1327-1338` hardcoded intoxication keyword list.
- `Source/Models/DiaryEvent.cs:1271-1289` display staggering thresholds.
- `Source/Core/DiaryGameComponent.Generation.cs:27` generation consciousness gate.
- `Source/Generation/DiaryContextBuilder.cs:1345` / `:1360` hardcoded positive/negative mood condition defNames.

Problem:
- Feature policy is hardcoded in C# instead of XML, violating repo rule 3.

Suggested change:
- Route intoxication labels through existing `DiaryTextDecorationRule.anyHediffLabelContains` rather than adding a parallel tuning field.
- Move consciousness thresholds to `DiaryTuningDef`, preserving separate values for generation gate vs display staggering.
- Move mood positive/negative condition string lists to XML/Def-backed tuning with C# fallback defaults.

Pros:
- Modders/DLCs can extend classifications by string without code.
- Aligns with current string-matcher pattern.

Cons / risks:
- Ship XML defaults and safe C# fallbacks.
- XML entries should remain plain strings; do not reference DLC defs unless `MayRequire` is appropriate.

Validation:
- Build.
- XML parse/verification hooks if available.
- Add/extend tests for threshold/list resolution if there are pure test projects.

Docs:
- Update `DOCUMENTATION.md` tuning/localization or Def section.
- Add `CHANGELOG.md` entry.

## Run Card 6: Split `DiaryContextBuilder` By Concern

Priority: Medium

Evidence:
- Pure helpers: `Source/Generation/DiaryContextBuilder.cs:1003` (`CleanLine`), `:1046` (`MoodBucket`), `:1067` (`PainBucket`), `:1098` (`OpinionBucket`), `:1124` (`AgeBucket`).
- Impure collectors: `:294` (`BuildSurroundingsSummary`), `:526` (`BuildPawnSummary`), `:632-1001` summary family.

Problem:
- One static builder mixes pure formatting/bucketing, impure Pawn/Map collection, and mood policy.

Suggested change:
- Keep impure collectors in `DiaryContextBuilder`.
- Move pure bucket/format helpers to `DiaryBuckets.cs` or similar.
- Move mood-impact policy to `MoodImpactClassifier`.

Pros:
- Pure helpers become testable without Verse objects.
- File responsibilities become clear.

Cons / risks:
- Avoid broad DTO rewrites in the first slice; start with pure helper extraction.

Validation:
- Build.
- Add/extend pure tests for bucket behavior.

Docs:
- Update `DOCUMENTATION.md` generation/context section.
- Add `CHANGELOG.md` entry.

## Run Card 7: Split `PawnDiaryMod` Settings UI And Async Controllers

Priority: Medium

Evidence:
- `Source/Settings/PawnDiaryMod.cs:89` whole settings window.
- `:169` API endpoint editor.
- `:730` Prompt Studio.
- `:1054` Persona Studio.
- `:1410` connection test.
- `:1521` model fetch.
- `:1595` / `:1656` apply async results.

Problem:
- The `Mod` subclass is also a large IMGUI renderer and async HTTP state machine.

Suggested change:
- First do a partial-class file split: API lanes, Prompt Studio, Persona Studio, settings widgets.
- Then extract an `ApiConnectionController` owning connection tests, model fetch, pending results, and stale-row matching.
- Extract common settings UI widget helpers.

Pros:
- Easier navigation.
- Thread-handoff code isolated.
- Sets up reuse of `ApiLaneIdentity` from Run Card 1.

Cons / risks:
- Keep `.Translate()` and shared-collection writes on the main GUI thread.
- Immediate-mode UI state must remain explicit.

Validation:
- Build.
- Manually inspect settings-window flows after compile if possible in-game.

Docs:
- Update `DOCUMENTATION.md` settings UI structure.
- Add `CHANGELOG.md` entry.

## Run Card 8: Extract Persona And Override State From `PawnDiarySettings`

Priority: Low to Medium

Evidence:
- `Source/Settings/PawnDiarySettings.cs:1146-1358` persona CRUD.
- `Source/Settings/PawnDiarySettings.cs:400-580` event prompt override dictionary plumbing.
- `Source/Settings/PawnDiarySettings.cs:261` serializes `personaPresets` via one `Scribe_Collections.Look(..., LookMode.Deep)` key.

Problem:
- Settings save object also owns persona catalog mutation and generic override-dictionary logic.

Suggested change:
- Add `PersonaPresetStore` while preserving the same Scribe key.
- Add a reusable override-dictionary helper if it reduces duplicate normalize/look-up code.

Pros:
- Shrinks settings object.
- Keeps persona logic coherent.

Cons / risks:
- Preserve Scribe keys exactly.
- `.Translate()` currently in `AddCustomPersona` must remain main-thread.

Validation:
- Build.
- Old settings save compatibility reasoning.

Docs:
- Update `DOCUMENTATION.md` settings/persona section.
- Add `CHANGELOG.md` entry.

## Run Card 9: Split `DiaryGameComponent.Generation.cs`

Priority: Medium

Evidence:
- `Source/Core/DiaryGameComponent.Generation.cs:615-769` lane policy.
- `:852-1085` title subsystem.
- `:1257-1361` live pawn lookup.
- `:796-824` lane label/list duplicates.

Problem:
- One partial handles generation orchestration, API lane selection, title generation, and pawn lookup.

Suggested change:
- Extract `ApiLaneSelection` first, taking lane list, routing mode, and an `isCooling` probe such as `Func<ApiEndpointConfig, bool>`.
- Extract title generation into a `DiaryGameComponent.Titles.cs` partial or collaborator.
- Extract live pawn lookup into `PawnLocator`.

Pros:
- Smaller generation file.
- Lane selection becomes testable with plain config snapshots.

Cons / risks:
- Keep lane selector pure-ish; do not let it read global settings or Defs directly.

Validation:
- Build.
- Add pure tests for lane selection if feasible.

Docs:
- Update `DOCUMENTATION.md` core/generation map.
- Add `CHANGELOG.md` entry.

## Run Card 10: Extract `DiaryGameComponent` Repository / State Bags

Priority: Medium

Evidence:
- `Source/Core/DiaryGameComponent.cs:60-118` central saved/transient state.
- `Source/Core/DiaryGameComponent.cs:331` tick orchestration.

Problem:
- The partial class is mostly per-concern, but central state ownership is large and shared.

Suggested change:
- Extract `DiaryEventRepository` around `diaryEvents`, `eventsById`, `FindEvent`, `RegisterDiaryEvent` before broader services.
- Then extract state bags such as `PendingBatchState` or `RecentDedupState` if they reduce private-field coupling.
- Keep `DiaryGameComponent` as the only RimWorld lifecycle/save owner.

Pros:
- Clarifies ownership with less blast radius than service extraction.

Cons / risks:
- Do not make services receive live `Pawn`/`Def` when a plain snapshot would do.
- Preserve save/load lifecycle semantics.

Validation:
- Build.
- Save/load smoke reasoning.

Docs:
- Update `DOCUMENTATION.md` core component state section.
- Add `CHANGELOG.md` entry.

## Run Card 11: Optional `LlmRequestJsonBuilder`

Priority: Low

Evidence:
- `Source/Generation/LlmClient.cs:1099-1225` request JSON construction and escaping.

Problem:
- JSON construction is separable from HTTP transport, but it currently works and is auditable.

Suggested change:
- Extract pure `LlmRequestJsonBuilder` only when JSON request logic next needs modification.
- Keep `reasoning_effort` and `max_output_tokens` mode-specific logic with the builder.

Pros:
- Testable pure serialization.

Cons / risks:
- Not urgent. Avoid churn unless changing this area anyway.

Validation:
- Build.
- Add serialization tests for each compatibility mode if extracted.

Docs:
- Update docs/changelog if implemented.

## Run Card 12: LLM Parser Marker Duplication Test

Priority: Low

Evidence:
- `Source/Generation/LlmResponseParser.cs:1082-1083` mirrors speech markers from `DiaryDirectSpeechParser`.

Problem:
- Sentinels can silently diverge.

Suggested change:
- Prefer a test assertion that the local constants match `DiaryDirectSpeechParser.DefaultOpenMarker` / `DefaultCloseMarker`, preserving parser isolation.

Pros:
- No production code churn.

Cons / risks:
- Minor.

Validation:
- Run relevant parser tests plus build.

Docs:
- Changelog if test-only change is tracked.

## Run Card 13: Move `DiaryPipelineAdapters` Out Of Pure `Pipeline` Folder

Priority: Low

Evidence:
- `Source/Pipeline/DiaryPipelineAdapters.cs:1` says the file is impure.
- It accepts `DiaryEvent`, translates strings, and reads settings/Defs around `:18`, `:56`, `:91`, `:97`.

Problem:
- Folder boundary implies `Pipeline` is pure, but this adapter is intentionally impure.

Suggested change:
- Move the file to `Source/Generation` or `Source/Adapters` while keeping namespace/API stable if that minimizes churn.
- Optional split: `DiaryEventPayloadFactory` and `DiaryPolicySnapshotProvider`.

Pros:
- Makes folder/module boundary honest.

Cons / risks:
- Update docs/file map.

Validation:
- Build.

Docs:
- Update `DOCUMENTATION.md` file map/architecture.
- Add `CHANGELOG.md` entry.

## Run Card 14: Split `DiaryTextDecorations`

Priority: Low

Evidence:
- `Source/Pipeline/DiaryTextDecorations.cs` combines constants/DTOs, rule matching, rich-text mutation, fact serialization, tag parsing, and condition matching.

Problem:
- One large pure-ish utility owns several separable subsystems.

Suggested change:
- Split into separate files/classes behind a facade: `DiaryTextDecorationContracts`, `DiaryTextDecorationMatcher`, `DiaryTextDecorationFactCodec`, `DiaryRichTextDecorators`.

Pros:
- Easier tests and review.

Cons / risks:
- More internal types.
- Do not introduce Unity GUI dependencies into matcher/codec.

Validation:
- Build.
- Existing decoration tests if present; add focused tests if feasible.

Docs:
- Update docs/changelog if implemented.

## Run Card 15: `ITab_Pawn_Diary` UI Extraction

Priority: Low

Evidence:
- `Source/UI/ITab_Pawn_Diary.cs:22-36` caches.
- `:56` style accessors.
- `:229` `FillTab` retrieves data, filters, layouts, measures, scrolls, and draws.
- `:538` visible cache rebuild.
- `Source/UI/ITab_Pawn_Diary.EntryCards.cs:653` card measurement.

Problem:
- UI partials are split by file but still one view object with data retrieval, cache, layout, measurement, rendering, and dev controls.

Suggested change:
- Extract `DiaryTabVisibleEntriesCache` first. Treat it as impure if it stores live UI pawn references.
- Later extract `DiaryEntryCardRenderer` / `DiaryEntryMeasurer` as UI-layer classes, not pure pipeline classes.

Pros:
- `FillTab` becomes focused on drawing.

Cons / risks:
- Immediate-mode UI state ownership is easy to get wrong.

Validation:
- Build.
- In-game UI smoke test if possible.

Docs:
- Update docs/changelog if implemented.

## Run Card 16: Split `DiaryPatches` By Domain

Priority: Low

Evidence:
- `Source/Patches/DiaryPatches.cs:25` death patch.
- `:49` arrival/faction patch.
- `:87` hediff patch.
- `:189` PlayLog interaction patch.
- `:496` generated quest-UI closure patch.
- `:664` thought-gain patch.

Problem:
- One Harmony patch file mixes unrelated capture domains and fragile optional registration.

Suggested change:
- Split by domain: deaths, arrivals, social log, quests, health, thoughts.
- Centralize fragile/generated-name registrations in a defensive `DiaryPatchRegistrar`, especially `QuestUiAcceptPatch.TryRegister` and `ThoughtGainPatch.TryRegister`.

Pros:
- Easier patch audit and troubleshooting.

Cons / risks:
- Keep Harmony attributes discoverable.
- Keep optional/DLC-ish patches defensive.

Validation:
- Build.
- Startup patch-registration smoke reasoning/log check if possible.

Docs:
- Update docs/changelog if implemented.

## Run Card 17: Split `PromptEnchantments` Collector / Planner

Priority: Medium

Evidence:
- `Source/Generation/PromptEnchantments.cs:119` hardcoded thresholds/cue cap.
- `:131` live `Pawn` and settings reads.
- `:138` `DefDatabase<DiaryPromptEnchantmentDef>`.
- `:146` live hediff reads.
- `:244` capacity reads.
- `:283` weighted random.
- `:299` prompt assembly.

Problem:
- Candidate collection, weighted selection, XML policy interpretation, and prompt-text assembly are fused around live `Pawn`/`Hediff`.

Suggested change:
- Extract an impure `PromptEnchantmentCollector` that produces plain candidate DTOs.
- Extract a `PromptEnchantmentPlanner` that selects/formats from DTOs with deterministic-roll test seams.
- Move thresholds/cue cap to XML/Def/tuning with fallbacks.

Pros:
- Keeps live pawn reads in an impure collector.
- Makes selection testable.

Cons / risks:
- Candidate DTO must carry enough data to avoid later live `Hediff` access.
- Planner must not accept `Pawn`, `Hediff`, `Def`, settings, or transport objects.
- Current DLC reads route through `DlcContext`; keep it that way.

Validation:
- Build.
- Pure planner tests if feasible.

Docs:
- Update docs/changelog if implemented.

## Run Card 18: Deferred `DiaryEvent` `PovSlot` Refactor

Priority: Low, high blast radius, do last

Evidence:
- `Source/Models/DiaryEvent.cs:1690-1942` has repeated initiator/recipient/neutral accessors.
- `Source/Models/DiaryEvent.cs:135-198` writes per-POV fields with explicit Scribe keys.
- `Source/Models/DiaryEvent.cs:200-431` `PostLoadInit` references many fields by name in bespoke normalization branches.

Problem:
- Per-POV field/accessor duplication is large and error-prone.

Suggested change:
- Introduce a `PovSlot` value type and have `SlotFor(role)` centralize field selection.
- Keep Scribe keys exactly the same by writing the same flat key names.

Pros:
- Collapses hundreds of repeated branches.
- Makes POV symmetry easier to maintain.

Cons / risks:
- Save compatibility via explicit Scribe keys is safe in principle, but the real blast radius is rewriting `PostLoadInit` normalization.
- Needs old-save load tests or very careful manual old-key reasoning.

Validation:
- Build.
- Add tests around load-normalization if possible.
- Manually verify every old Scribe key is preserved.

Docs:
- Update docs/changelog if implemented.

## Quick Comment Fix

Priority: Trivial

Evidence:
- `DiaryEventCatalog.cs:63` reportedly says something like "New sources added in future slices register here." The rebuttal found all sources are now migrated, so this phrasing is stale.

Suggested change:
- Update that comment only. This can be paired with any slice or done separately.

Validation:
- Build not strictly needed for a comment-only change, but repo rules say build after changes.

## Rejected Findings: Do Not Repeat These

### Rejected: "Capture migration is half-finished / 12 dead specs"

Status: Wrong.

Reason:
- The rebuttal verified all 17 registered event specs are live in production by reading the `Record*` partials.
- The earlier claim came from trusting an incomplete caller index for the generic method name `Get`.
- The `recent*Events` dictionaries are documented per-source dedup gates, not a competing legacy decision system.

Action:
- Do not delete specs.
- Do not start a "finish the migration" project for this claim.
- Only fix the stale comment in `DiaryEventCatalog.cs:63` if still present.

### Rejected: Cache `GetMoodOffsetFromConditionThoughts` For Hot Path

Status: Misread.

Reason:
- The caller already hoists the value out of the per-pawn loop (`MoodEvents.cs:60-62`) and passes it as a parameter.
- Caching would save at most an occasional scan per condition occurrence and is not worth the added state.

Action:
- Do not implement this cache unless profiling later shows a real issue.

## Suggested Prompt For Future Agents

Use this structure when assigning a slice:

```text
Follow AGENTS.md and ARCHITECTURE_IMPROVEMENT_PLAN.md. Implement only Run Card N: <title>. Keep the change minimal and behavior-preserving. Preserve no-DLC safety, save compatibility, localization rules, and the pure/impure boundary. Update DOCUMENTATION.md and CHANGELOG.md if behavior or structure changes. Rebuild with MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug. Do not touch unrelated run cards.
```
