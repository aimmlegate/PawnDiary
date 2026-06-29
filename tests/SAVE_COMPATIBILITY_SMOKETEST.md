# Pawn Diary — Save/Settings Compatibility Smoke Runbook

This runbook is the **Option B** half of Plan 6 (Save And Settings Compatibility Fixtures). The
pure post-load normalization logic (null-coalesces, cross-slot surroundings chain, neutral-text
merge, legacy `gameContext` rebuild, year extraction, status reclassification, defensive clamps)
is unit-tested in `tests/DiarySaveNormalizationTests/` without RimWorld. The pieces below **cannot**
be pure-tested because they need the live RimWorld runtime, so they are covered by this repeatable
in-game smoke procedure instead.

Run this whenever you touch:
- `Source/Models/DiaryEvent.cs` (`ExposeData`, `NormalizeOnLoad`, `ScribePawnSlot`, `ScribeNeutralSlot`),
- `Source/Models/ArchivedDiaryEntry.cs` (`ExposeData`, `NormalizeOnLoad`),
- `Source/Settings/PawnDiarySettings.cs` or anything under `Source/Settings/` that owns `ExposeData`,
- the Scribe keys written by any of the above, or
- `Source/Core/DiaryEventRepository.cs` / `DiaryArchiveRepository.cs` load/index paths.

It is intentionally **manual**: forcing RimWorld `Scribe` into a pure test project would break the
no-RimWorld convention every other `tests/` project follows, and `Scribe.Look` semantics differ
between `LoadSaveMode`s in ways that only the real host exercises.

---

## Why a runbook and not an automated Scribe test

`Scribe_Values.Look` / `Scribe_Collections.Look` are statics on RimWorld's `Verse.Scribe`, which is
only usable once RimWorld has initialized its save system (cross-references resolved,
`PostLoadInit` orderings, `DebugLoadIDsMembersShouldBeActive`, etc.). A standalone console harness
cannot replicate that without effectively embedding the game. Plan 6 Step 4 deliberately chose this
runbook over a RimWorld-referenced test project to keep the `tests/` tree pure-only.

---

## Fixture save files

Keep one player save per fixture in a personal scratch folder (NOT committed — they reference your
local mod install). Suggested set, mirroring the pure-test fixture inventory plus the settings-only
fixtures:

| Fixture | What the save must contain | What a regression looks like |
|---|---|---|
| **Pre-title entry** | A completed diary page whose title follow-up was still `pending` when the previous session saved. | Title shows an indefinite "writing..." state, or the recovery sweep re-queues forever. |
| **Failed entry** | A page with `status=failed` and an error string. | Failed pages get retried every scan, or lose their error text. |
| **Pending archived fallback candidate** | A page archived as a stale fallback (no generated text, but a saved prompt). | The page disappears after load, or renders as "not generated" instead of the fallback body. |
| **Pair event with recipient state** | One `DiaryEvent` with both initiator and recipient POVs, recipient surroundings blank. | The two POVs show different surroundings, or surroundings is `null`/empty. |
| **Neutral arrival** | A pair event with no saved `neutralText`. | Neutral chronicle page is blank, or shows only one POV, or changes shape across loads. |
| **Neutral death** | A pair event where both POVs saw the same death text and no `neutralText` was saved. | The death line is duplicated, or the merge format changes. |
| **Legacy settings (old persona/prompt/group)** | A `ModSettings` block written by an older Pawn Diary version (pre-persona-preset or pre-prompt-override fields). | Settings fail to load, defaults wipe saved overrides, or the group-enable map is reset. |
| **Missing/blank fields** | A save missing one each of `eventId`, `gameContext`, `instruction`, `colorCue`, `moodImpact`, `year`. | NullReferenceException on load, or a blank diary card, or `5500`-style year shows as `0`. |

The pure fixtures are duplicated here because the **Scribe read path itself** (key lookup, default
fallback, `PostLoadInit` ordering) is what this runbook exercises — the pure tests only cover the
field-level math that runs after Scribe has populated the fields.

---

## Procedure

1. **Build first.** From the repo root:
   ```
   MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
   ```
   Confirm `1.6/Assemblies/PawnDiary.dll` was rewritten; stage it before launching the game so RimWorld
   loads your changes (the committed DLL is what the game runs).

2. **Back up the fixture save** before each load. Smoke testing mutates the save on write-back; keep
   a pristine copy so the fixture is repeatable.

3. **Load the fixture save** in RimWorld. Watch the debug console (`Development log`) for:
   - any `NullReferenceException` or `Exception` whose stack mentions `PawnDiary`,
   - any `Scribe`-related warning about missing/unresolved keys,
   - any `DiaryEventRepository` / `DiaryArchiveRepository` repair warnings.

4. **Inspect the Diary tab** for each fixture pawn. Confirm:
   - every page is present and non-blank,
   - statuses are correct (failed stays failed; completed pages show their text; stale pages show the
     fallback "You see that: ..." body, not an empty card),
   - pair events show shared surroundings and a merged neutral page,
   - titles render or are absent per the fixture's expectation (no stuck "writing title...").

5. **Save the game again, then reload that second save.** This catches asymmetric read/write bugs — a
   field that loads but does not round-trip back is the most common Scribe regression.

6. **Open Mod Settings.** For the legacy-settings fixture, confirm every override you set in the old
   format is still present (API lanes, system-prompt overrides, event-prompt overrides, persona
   presets, interaction-group enable map). None should be silently reset to defaults.

7. **Run the pure test projects** as well (they cover the field math this runbook does not):
   ```
   dotnet run --project tests/DiarySaveNormalizationTests/DiarySaveNormalizationTests.csproj
   dotnet run --project tests/DiaryPipelineTests/DiaryPipelineTests.csproj
   ```
   ...or run `.githooks/verify.ps1` which runs the full pure suite plus the DLL build.

---

## What counts as a regression

Any of:
- a load-time exception in Pawn Diary code,
- a field that is `null` in the diary UI after load,
- a status, title, surroundings, neutral-text, year, or settings-override value that differs from
  the same save loaded on the previous Pawn Diary version,
- a Scribe-key rename visible in the raw save XML (see the stable-key contract in
  `DOCUMENTATION.md §9`).

If you intended to rename a stable Scribe key, the migration plan must be in the change card and the
old key must still be read during a transition window — that is the one carve-out from the
"never rename existing Scribe keys" rule.
