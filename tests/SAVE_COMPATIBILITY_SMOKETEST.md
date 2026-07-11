# Pawn Diary — Save/Settings Compatibility Smoke Runbook

This runbook is the **Option B** half of Plan 6 (Save And Settings Compatibility Fixtures). The
pure post-load normalization logic (null-coalesces, cross-slot surroundings chain, neutral-text
merge, legacy `gameContext` rebuild, year extraction, status reclassification, defensive clamps)
is unit-tested in `tests/DiarySaveNormalizationTests/` without RimWorld. The pieces below **cannot**
be pure-tested because they need the live RimWorld runtime, so they are covered by this repeatable
in-game smoke procedure instead.

Run this whenever you touch:
- `Source/Models/DiaryEvent.cs` (`ExposeData`, `NormalizeOnLoad`, `ScribePawnSlot`, `ScribeNeutralSlot`),
- `Source/Models/PawnDiaryRecord.cs` (`ExposeData` — writing-style + psychotype + pin/band fields),
- `Source/Core/DiaryGameComponent.Voice.cs` (`EnsureVoiceStage` crystallization/backfill and the rolls),
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
| **Pre-psychotype adult with entries** | A save from a version before the psychotype layer, whose colonist already has generated first-person pages. | On load the pawn's psychotype must read **Neutral** and their *writing style must not change* (established voice frozen). Any shift, or an exception, is a regression. |
| **Pre-psychotype entry-less record** | A pre-psychotype save whose diary record exists but has no generated first-person entries yet. | The pawn should roll a fresh psychotype lazily on its next entry (band-appropriate), not stay blank forever and not crash. |
| **Child crossing the crystallization age** | A colonist younger than `psychotypeCrystallizationAgeYears` (default 13) with child-band voice rolls, aged past 13 (dev "set age" is fine). | Both **unpinned** layers must re-roll onto the adult catalogs and the band stamp must flip to `adult`; a **pinned** (player-chosen) layer must survive unchanged. |
| **Player-pinned voice** | A colonist whose writing style and/or psychotype the player picked/edited/re-rolled in the per-pawn editor. | The pinned layer is never auto-re-rolled at crystallization or backfill. |
| **RimTalk Tier B toggle** | RimTalk bridge active with Tier B on, then toggled off (and vice versa). | Toggling on places a **psychotype** (not writing-style) override; toggling off clears it. Loading an old save that had the *style*-based Tier B override must sweep that stale style override on load. |
| **`enablePsychotypes` off** | The master psychotype toggle turned off in settings. | First-person prompts omit the psychotype block, pending rolls stay deferred, and existing saved psychotypes are preserved (re-enabling restores them). |

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

## Mod-compatibility adapters (1-2-3 Personalities / VSIE)

These separate adapter mods live under `integrations/` and are inert without their target mod. Run
these manual checks when you touch either adapter's Defs or the Personalities bridge assembly.

**VSIE compat (`PawnDiary.Vsie`, XML + a small assembly).** With Pawn Diary + Vanilla Social
Interactions Expanded active:
1. Dev-trigger (or wait for) a `VSIE_Vent`, a `VSIE_Teaching*`, a `VSIE_Discord`, and a
   `VSIE_BestFriend` relation. Confirm each is captured with the intended shape: venting and teaching
   fold into the ambient day-note (a charged vent may promote to its own confiding entry); `VSIE_Discord`
   is an insult, so it lands in the core **insults & fights** entry (batched with the social fight it
   usually starts), NOT as a co-working note; and best-friend appears as a relationship-milestone entry
   (not a chat log).
2. **Gatherings.** Dev-trigger a `VSIE_BirthdayParty` and a `VSIE_Funeral` (Debug Actions → try both;
   they run through `GatheringWorker.TryExecute`). Confirm each produces exactly **one** diary entry for
   the organizer — a birthday-party entry (`vsieBirthdayGathering`) and a funeral entry
   (`vsieFuneralGathering`) — with no "unclaimed external eventKey" warning. Trigger a flavor gathering
   (e.g. `VSIE_MovieNight` or `VSIE_Skygazing`) and confirm it does **not** create its own event entry
   (its mood residue may still show up later as a `vsie_thoughts` social-afterthought). Note the two
   gathering groups are External-domain, so they do **not** appear in Pawn Diary's Events tab — their
   toggles live in this adapter's own mod settings (next check). VSIE's four non-External XML groups
   (venting, teaching, best-friend, mood thoughts) **do** appear in Pawn Diary's Events tab.
   - **Settings toggle.** Open `Options → Mod settings → Pawn Diary: VSIE gatherings`. Turn **off**
     "Funerals", dev-trigger a `VSIE_Funeral`, and confirm **no** funeral entry is written (birthdays,
     still on, are unaffected). Turn it back on and confirm funerals record again. With VSIE absent the
     settings page still opens and saves (shows the "not in the active mod list" note).
3. Remove VSIE from the mod list. Confirm the four `vsie_*` groups **and** the two External gathering
   groups do **not** appear in Pawn Diary's event settings, the core `insults` group shows no
   `VSIE_Discord` behavior, and there are no warnings — the whole adapter (Defs, the `1.6/Patches/`
   Discord routing, and the `PawnDiaryVsie.dll` gathering hook, whose `PatchAll` is skipped when VSIE is
   absent) is inert.

**1-2-3 Personalities bridge (`PawnDiary.PersonalitiesBridge`, XML + assembly).** With Pawn Diary +
1-2-3 Personalities M1 (and, for the interaction/thought groups, M2) active:
1. Set the bridge mode to **Override** (default). Let one pass run and confirm each colonist's Psychotype
   Studio shows a **pre-filled, editable** custom rule matching their Enneagram root.
2. **Edit** one colonist's custom rule by hand, then **save and reload.** The edit must survive the
   round-trip (change detection is saved as `<mode>:<root>`, so an unchanged pawn is never re-seeded).
   Change a colonist's personality (dev-mode) and confirm the rule re-seeds on the next pass.
3. Switch to **Map to a built-in psychotype** and confirm each colonist's base psychotype becomes the
   mapped built-in type (pinned); switch to **Off** and confirm existing player-owned values are left
   untouched (nothing is destructively cleared).
4. **Experimental LLM transform:** with a lane configured, set the mode to the LLM tier, keep or edit the
   prompt, and confirm one call fires per personality change and replaces the custom rule with compact
   text; with no lane (or a forced failure) confirm it falls back to the built-in override text.
5. **Migration:** loading a save made by the *previous* bridge version once releases its locked external
   overrides (the `FinalizeInit` sweep) so the new editable layers are visible. The old
   `provideContextLine` / `usePersonalityOutlook` settings keys are dropped; the new `mode` defaults to
   Override.

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
