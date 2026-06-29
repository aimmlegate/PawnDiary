# Pawn Diary - Archive Compaction Design

Status: Phase 0 design for `ARCHITECTURE_IMPROVEMENT_PLAN.md` Plan 3. Implemented 2026-06-29 with
the planned `diaryArchiveEntries` key and archive-then-drop retention. This note remains as the design
record for the shipped save/schema behavior.

## 1. Current State

The current system has one saved master list, `diaryEvents`, owned by `DiaryEventRepository`. Per-pawn
`PawnDiaryRecord.eventIds` lists point into that master list. `ApplyActiveEventLimit` trims each
pawn's oldest refs when `maxActiveDiaryEvents` is exceeded, then `DiaryEventRepository.RetainOnly`
removes any master-list event no pawn still references.

`DiaryTuningDef.activeScanEventWindow` already makes old full `DiaryEvent` rows cold for generation,
title catch-up, orphan recovery, day-summary evidence, work cooldowns, and prompt continuity. Those old
rows still stay in `diaryEvents` and can render in the Diary UI until the per-pawn cap destroys them.
This is a scan archive, not real compact storage.

The new design keeps the hot/cold scan split, but converts old display-safe pawn pages into a compact
archive before removing their heavy `DiaryEvent` backing rows.

## 2. Goals And Non-Goals

Goals:

- Preserve old visible diary pages after per-pawn hot refs age past the active cap.
- Keep generation, retry, title catch-up, orphan recovery, and prompt continuity limited to hot
  `DiaryEvent` rows.
- Reduce save bloat by excluding prompts, raw responses, retry state, transient errors, and live-context
  fields that are not needed to draw an old page.
- Keep old saves loading when the archive key is absent.
- Keep no-DLC games safe by storing only plain strings and primitive display data.

Non-goals:

- Archived entries do not regenerate in this slice.
- Archived entries do not feed future prompts or continuity scans.
- The generated diary prose format and prompt policy do not change.
- This does not redesign all UI virtualization; it extends the existing year-index pipeline.

## 3. New Save Keys

Keep existing keys unchanged:

- `diaries`
- `diaryEvents`
- `activeEventWindows`
- `generatedSpeechPlayLogTexts`
- setting key `maxActiveDiaryEvents`

Add one key owned by a new repository:

- `diaryArchiveEntries` - `List<ArchivedDiaryEntry>` saved with `LookMode.Deep`.

An absent `diaryArchiveEntries` key means an old save. It loads as an empty archive and does not trigger
automatic conversion until the code phase explicitly runs compaction.

## 4. Archived Record Schema

Use one archived row per displayed pawn POV, not one row per event. A pair event can therefore produce
two archive rows that share `eventId` but have different `pawnId` and `povRole`. This mirrors the UI,
lets one pawn archive a shared event while the other pawn still keeps a hot ref, and avoids rehydrating
full multi-role `DiaryEvent` state.

Proposed type: `ArchivedDiaryEntry : IExposable` under `Source/Models/`.

Identity and ordering fields:

- `eventId`
- `pawnId`
- `povRole`
- `entryKey` or a deterministic post-load key built from `eventId|pawnId|povRole`
- `tick`
- `date`
- `year`

Display text fields:

- `text` - raw event/fallback text for the displayed POV.
- `generatedText` - saved generated prose, or the prebuilt fallback body for an archived failed/stale
  entry.
- `title` - saved title or prebuilt fallback title.
- `status` - compact display status only, usually `complete`, `failed`, `skipped`, or `prompt_only`.
- `modelLabel` - optional model label only if the normal UI deliberately displays it for completed old
  pages. Do not store endpoint URLs.

Display metadata fields:

- `groupLabel`
- `interactionDefName`
- `interactionLabel`
- `colorCue`
- `atmosphereCue`
- `important`
- `staggeredIntensity`
- `textDecorationFacts`
- `decorationDomain`
- `decorationTags` or another compact replacement for the small subset of `gameContext` needed by
  text-decoration rules.

Linked-entry fields:

- `linkedPawnId`
- `linkedPawnName`
- `linkedRole`
- `linkedPreviewText`
- `linkedGenerated`
- `linkedTitle`

Do not save:

- full LLM prompt text
- raw response text
- retry/orphan/title-generation state
- title prompts or title raw responses
- raw API endpoint URL
- live pawn summaries, surroundings, weapon, continuity, or prompt-enchantment facts unless a specific
  display feature still needs a compact value

## 5. Archive Repository

Add `DiaryArchiveRepository` under `Source/Core/` and keep `DiaryGameComponent` as lifecycle owner.

Responsibilities:

- Save/load `diaryArchiveEntries` through `ExposeArchive`.
- Repair null lists and null rows during `PostLoadInit`.
- Rebuild transient indexes after load and after mutation.
- Index by `pawnId` for UI enumeration.
- Index by `eventId|pawnId|povRole` for duplicate repair and conversion idempotence.
- Optionally index by `eventId` for linked-entry navigation and dev cleanup.
- Provide `EntriesForPawn(pawnId)` and count helpers without exposing mutable lists.
- Provide `RemoveForEventIds(HashSet<string>)` so dev prompt-suite resets can remove archived synthetic
  entries alongside hot events.

Duplicate repair rule: first valid row wins for a key, matching the current `DiaryEventRepository`
index behavior. Rows with blank `eventId`, `pawnId`, or `povRole` are dropped on repair.

## 6. Compaction Flow

Replace destructive trim with archive-then-drop:

1. Read `maxActiveDiaryEvents` exactly as today. The Scribe key and setting field name stay unchanged.
2. Plan which old per-pawn refs are past the cap.
3. For each ref past the cap, resolve the hot `DiaryEvent` and the pawn's display role.
4. If that role is archiveable, build an `ArchivedDiaryEntry` before removing the per-pawn ref.
5. If archive write succeeds or a duplicate archive row already exists, remove the per-pawn ref.
6. If a role is not archiveable, leave that ref hot even if the list remains above the cap.
7. After per-pawn refs are updated, sweep `diaryEvents` down to ids still referenced by a hot diary
   record. Archived rows are not hot references and do not keep a full `DiaryEvent` alive.
8. Clear orphan candidates and bump `DiaryStateVersion` when hot refs, hot events, or archive rows
   change.

Archiveability rules:

- Complete generated role: archive.
- Skipped role with visible display text: archive.
- Prompt-only role: archive only for dev/prompt-test visibility, preserving current dev behavior.
- Failed role with display fallback: archive the fallback body/title and status.
- Archived-stale attempted role that has no generated text but has a saved prompt: archive the same
  localized fallback body/title the UI currently builds, then drop the hot ref.
- Pending or not-generated role still inside the hot scan window: keep hot and retry later.
- Never-attempted role with no generated text and no prompt: drop only if it is invisible under current
  UI rules; otherwise keep hot until an explicit rule handles it.

The conversion builder is impure because it reads `DiaryEvent` display methods and localized fallback
strings on the main thread. The archive/drop eligibility policy is pure in
`Source/Pipeline/DiaryArchiveEligibility.cs` and covered by `DiaryPipelineTests`; any future
trim-selection math that can remain pure should stay in `Source/Pipeline/` and be covered by
`tests/DiaryRetentionTests`, `tests/DiaryPipelineTests`, or a new archive-compaction test.

## 7. UI Merge Rules

Extend the existing sliced year-index pipeline instead of adding a second UI path.

`DiaryTabYearIndexBuild` should process two phases:

- hot per-pawn refs from `PawnDiaryRecord.eventIds`
- archived rows from `DiaryArchiveRepository.EntriesForPawn(pawnId)`

Both phases append a common candidate shape that can materialize a `DiaryEntryView`. Hot candidates call
`DiaryEvent.ToViewFor`; archived candidates call `ArchivedDiaryEntry.ToView`. The final selected-year
list keeps the current ordering rule: newest first by `tick`, then deterministic tie breakers such as
`eventId` and `povRole`.

Deduplicate by `eventId|pawnId|povRole`. If both a hot event and an archive row exist for the same page,
prefer the hot event so active generation/debug state is not hidden.

Year paging includes both hot and archived candidates. `RenderTokenFor` must include hot ref count,
archive count for that pawn, and `DiaryStateVersion.Current`, or use a similar structural token so the
tab invalidates when compaction adds archive rows.

`DiaryEntryView` should gain an `Archived` flag. Dev-only regeneration and raw debug controls should hide
or explain their limits for archived entries because raw prompts/responses are intentionally absent.
Copy-text and normal card display can keep working from the compact view.

## 8. Migration Path

Phase order should stay conservative:

1. Add the archive model and repository with save/load repair. Old saves load with an empty archive.
2. Add UI merge behind repository enumeration. With an empty archive, behavior is unchanged.
3. Add compaction. Only after UI merge exists may retention remove hot refs that have archive rows.
4. Consider a one-time post-load compaction pass only after manual save/load testing proves old pages
   display from archive records.

Do not rename existing Scribe keys. Do not migrate `diaryEvents` wholesale on first load before the UI
can display archive rows. Do not add a DLC dependency.

## 9. Active Cap And Hot Window Semantics

After compaction ships, `maxActiveDiaryEvents` means per-pawn hot refs, not total lifetime history. The
field and Scribe key keep the old name for compatibility. Archived entries are not counted against this
hot cap.

`DiaryTuningDef.activeScanEventWindow` remains a global newest-hot-event window across `diaryEvents`.
Archived rows never enter `ActiveScanEvents`, title sweeps, orphan recovery, day-summary signal scans,
work cooldown scans, or prompt continuity builders.

## 10. Validation Plan

Before implementation:

- Run `dotnet run --project tests/DiaryRetentionTests/DiaryRetentionTests.csproj`.
- Run `dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj`.
- Note the baseline results before editing runtime code.

After each implementation phase:

- Run the same pure tests again.
- Add or extend pure tests for any new compaction planner or display-state helper.
- Build with `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug` after C# changes.
- Load an old save with no `diaryArchiveEntries` key.
- Create completed entries, force retention, save, reload, and verify old pages still display.
- Stress test thousands of archived pages and year paging.
- Verify archived pages do not queue main LLM calls, title calls, or orphan recovery.
- Verify corpse diary view includes archived arrival/death/history pages.
- Verify dev regenerate/debug controls are hidden or explanatory for archived entries.

Baseline captured before implementation:

- `DiaryRetentionTests` passed 45 assertions.
- `DiaryPipelineTests` passed 276 assertions.

Implementation notes:

- Prompt-only dev capture rows are left hot rather than compacted, because compact archive rows
  intentionally do not retain full prompt text.
- Archived rows keep compact PlayLog ids so old generated Social-log row clicks can still open an
  archived page when the source PlayLog row still exists.
- `DiaryArchiveEligibility` pins the archive/drop decisions in pure tests, including the review fixes
  that keep title-pending and prompt-only rows hot while allowing cold blank overflow refs to drop.
