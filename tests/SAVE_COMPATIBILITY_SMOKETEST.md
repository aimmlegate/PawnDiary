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
- `Source/Models/NarrativeContinuityState.cs` (per-POV evidence/reference/key rows and their caps),
- `Source/Models/PendingBiotechBirthState.cs`, `PendingBiotechGrowthMoment.cs`,
  `BiotechFamilyArcState.cs`, `BiotechPawnProgressionState.cs`, `PawnArcState.cs` (Biotech state),
- `Source/Core/DiaryGameComponent.BiotechFamily.cs` / `DiaryGameComponent.BiotechGrowth.cs`
  (`ExposeBiotechFamilyData` / `ExposeBiotechGrowthData`, maintenance, version bootstrap),
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

## Odyssey real-lifecycle and three-phase save/reload fixture

RimTest Redux runs a test synchronously inside the current `Game`. Calling
`GameDataSaveLoader.LoadGame` from that method disposes the runner, the test assembly's live fixture,
and its cleanup scope before an assertion can continue. Pawn Diary therefore automates every
deterministic phase but leaves the two process boundaries as explicit operator steps.

Prerequisites: use a disposable Odyssey-enabled colony with a loaded surface map, no active/travelling
gravship, and no parked player grav engine on the current map. Enable only Harmony, RimTest Redux,
Pawn Diary, and Odyssey for the focused run. The runtime tests log a visible `SKIP` reason instead of
touching state when Odyssey is inactive or these safety preconditions are not met.

**Live checkpoint (2026-07-17):** Phase A passed in the supplied all-DLC loaded run and wrote
`PawnDiary_Odyssey_RimTest_PhaseA_Active`. The next operator step is to return to the main menu, load
that save, keep the game paused, and run `SaveReloadPhaseBVerifyAndCompleteJourney` only. Phase C
remains pending after Phase B writes its completed save.

1. Run `PawnDiaryOdysseyRuntimeLifecycleTests.RealInitiateTakeoffCancellationLeavesNoSavedJourneyOrPage`
   and `RealHarmonyLifecycleTravelsAndLandsExactlyOnce`.
   - The cutscene starters enter their real vanilla public methods, but a last-priority test prefix
     suppresses the graphics/capture originals after Pawn Diary's installed prefixes receive the live
     payloads.
   - `GravshipUtility.TravelTo` and private `LandingEnded` run their real vanilla bodies.
   - Expected: the cancelled takeoff has transient intent only; the cross-layer trip retains the true
     surface origin after engine/pilot despawn and vanilla tile rewriting; successful landing creates
     one rough-landing page and one emitted marker; replay creates neither.
2. Run `SaveReloadPhaseAWriteActiveJourneySave`.
   - It overwrites the deliberately reserved save name
     `PawnDiary_Odyssey_RimTest_PhaseA_Active`, using real `TravelTo` to save an active gravship journey.
   - It seeds trustworthy bounded history, current-home tenure (`1234`), launch-page cooldown (`2345`),
     and a real `InitiateLanding` pending row. The pending row is intentionally transient and is not one
     of the frozen save keys; Phase B must find it absent after load.
   - The test restores the currently loaded colony after writing. Only the named disposable save remains.
3. Return to the main menu and load `PawnDiary_Odyssey_RimTest_PhaseA_Active`. Keep the game paused.
   Run `SaveReloadPhaseBVerifyAndCompleteJourney` only.
   - Expected: `odysseyActiveJourney` and `odysseyTravelHistory` restore; trust, home key/tenure,
     launch cooldown, and the newest bounded location rows match Phase A; transient intent/pending are
     null; real landing completion creates exactly one page/marker and a duplicate finish callback is
     inert.
   - Phase B writes `PawnDiary_Odyssey_RimTest_PhaseB_Completed`, then consumes/cleans the live Phase A
     fixture. Load the new save rather than continuing to play the consumed Phase A instance.
4. Return to the main menu and load `PawnDiary_Odyssey_RimTest_PhaseB_Completed`. Run
   `SaveReloadPhaseCVerifyNoResurrection` only.
   - Expected: exactly one canonical landing page and one emitted journey marker survive; active journey,
     takeoff intent, pending landing, and travelling world object remain absent; a stale completion call
     cannot add an event or marker.
   - Phase C removes fixture events/diary rows/pawns/engine and deletes both reserved save files in a
     failure-safe `finally`. If RimWorld was closed before Phase C, delete those two named saves manually.

Record the RimWorld build, active mod list, language, and Phase A/B/C log lines. A successful C# build
only proves the fixture compiles; it does not count as executing either Odyssey or base-only runtime.

### Acceptance record (2026-07-17)

- **Host:** RimWorld `1.6.4871 rev591`, English, Harmony `2.4.1.0`.
- **Odyssey inactive:** isolated profile with Harmony, base RimWorld, Laboratory (RimTest Redux's
  dependency), RimTest Redux, and Pawn Diary. All five runtime fixtures logged the explicit
  `ModsConfig.OdysseyActive is false` skip before touching Odyssey state. No Pawn Diary Odyssey
  patch-registration, missing-Def, XML, or type-initializer error was found in `Player.log`.
- **Odyssey active:** isolated profile adding only Odyssey to the same list, using a disposable dev
  colony. The real cancellation and cross-layer lifecycle fixtures completed without a focused test
  error. Phase A, Phase B, and Phase C each logged `PASS`; the two reloads preserved the frozen active
  journey/history contracts, omitted transient intent/pending rows, completed exactly one landing
  page/marker, rejected replay, and resurrected no lifecycle state.
- **Cleanup:** Phase C removed both reserved save files; neither the isolated nor normal save folder
  contains `PawnDiary_Odyssey_RimTest_PhaseA_Active` or
  `PawnDiary_Odyssey_RimTest_PhaseB_Completed`.
- **Observed fixture hardening:** a run-at-startup invocation at the Odyssey-enabled main menu exposed
  null-host failures before any loaded game existed. The test assembly now checks `Current.Game` and
  `DiaryGameComponent.Instance` before using `Find` or instance-field reflection. The post-fix live
  main-menu rerun logged all five intended skips and no Odyssey runtime-suite error, closing this
  acceptance row.

---

## Biotech B1 Phase 4 acceptance matrix

Run this matrix before marking Biotech Phase 4 complete. Keep prompt-test mode on for prompt review so
no model request leaves the game; turn it off only when deliberately reviewing generated prose.

The Biotech RimTest suites now automate the real vanilla age-7/10/13 growth-letter boundary,
`NoTrait`/multiple-passion/nickname/responsibility choices, auto-resolution, postponed-owner Scribe
recovery, delayed live-child naming, Full/Balanced/Compact prompt projection, live pre-cap admission,
loaded RimTalk shared-memory injection, and DLC-off pending-row maintenance. The supplied all-DLC run
passed the Biotech growth/prompt/pre-cap paths; RimTalk and DLC-off maintenance still need their
matching profiles. The DLC-off fixture no longer skips young colonies: it temporarily uses zero
recovery grace and fails loudly if the base-only profile is not in a loaded game.

### Acceptance record (2026-07-17)

The user confirmed the following manual rows passed. Exact RimWorld version, mod-list, save-fixture,
and language metadata were not supplied with the result, so append those details here if a later
release audit requires a reproducible run record.

- [x] Row 1 — base game only (no Biotech).
- [x] Row 2 — old-save baseline.
- [ ] **AUTOMATION PASSED / MANUAL TODO:** Row 3 — `PawnDiaryBiotechGrowthFlowTests` passed with Biotech; manually click one real growth letter to inspect presentation.
- [ ] **AUTOMATION PASSED / MANUAL TODO:** Row 4 — the loaded prompt fixtures passed; manually inspect the localized preview rendering.
- [x] Row 5 — family observation + SpeakUp.
- [x] Row 6 — birth/naming matrix.
- [ ] **AUTOMATED RUN TODO:** Row 7 — run `PawnDiaryRimTalkBridgeRuntimeTests` with RimTalk + bridge; manually inspect one real chat/read UI result.
- [x] Row 8 — caps/performance.
- [x] Row 9 — growth, birth, and component-state pre-cap fixtures passed in the all-DLC loaded run; no separate manual behavior check remains.
- [ ] **AUTOMATED RUN TODO:** Row 10 — run `PawnDiaryBiotechDlcOffMaintenanceTests` in a base-only colony; the real Biotech on → off → on restart sequence remains manual.

Biotech Phase 4 remains open until the remaining RimTalk/base-only automated profiles and manual-only
checks identified above are recorded as passed.

1. **Base game only (no Biotech).** Start and load a colony with Pawn Diary + Harmony only. Confirm
   `progressionGrowthMoment` and `biotechFamilyBirth` are absent from Events settings, the development
   log has no Pawn Diary/DLC/Scribe error, ordinary birthdays still work, and save → reload remains
   clean. `About.xml` must list Harmony as the only required mod.
2. **Old save baseline.** Load the oldest supported pre-Biotech-Pawn-Diary save once with Biotech
   active and existing children/parents. The first pass may create silent exact child/parent baseline
   rows, but must create no historical pregnancy, lesson, birth, or growth page. Save and reload; no
   baseline page may appear on the second load, and existing diary pages/settings must remain intact.
3. **Growth ownership.** Exercise ages 7, 10, and 13, including a `NoTrait` choice, multiple passion
   changes, a nickname, new responsibilities, auto-resolution, and a postponed letter saved/reloaded
   before selection. Each committed choice produces at most one canonical page, consumes the ordinary
   Birthday owner only while genuinely pending, and never prints numeric tier/option/work lists.
   **Automation:** run `PawnDiaryBiotechGrowthFlowTests`. **Manual-only TODO:** click one actual vanilla
   growth letter and confirm its presentation/interaction is normal; the fixture calls the same methods
   directly and therefore cannot judge the rendered letter.
4. **Prompt detail and previews.** In the event test panel generate the localized **Biotech growth
   moment** and **Biotech family birth** fixtures under Full, Balanced, and Compact. Full should show
   every supplied story field. Compact must still show the localized growth opportunity description,
   chosen trait and interest, plus the birth child/outcome/writer role. No prompt may show a Thing ID, family-arc ID,
   tick, numeric growth tier, or correlation token. **Automation:** the growth and birth flow suites
   assert the prompt bodies under all three presets. **Manual-only TODO:** visually inspect localized
   preview labels, wrapping, and readability in the event test panel.
5. **Family observation + SpeakUp.** With SpeakUp loaded, disable Pawn Diary's ordinary teaching page
   group, then allow exact BabyPlay/Lesson activity before the next growth moment. No lesson page is
   required, but the later canonical growth page should use the observed parent/teacher role and
   qualitative upbringing when the exact evidence threshold is met. Repeat once without SpeakUp; page
   ownership and family arc identity must remain the same.
6. **Birth/naming matrix.** Exercise healthy, infant-illness, stillbirth, pregnancy, surrogacy,
   growth-vat, ritual/non-ritual, one/two/no eligible writer, naming before/after save, birther death,
   and a deliberately failed owner. Each successful owner emits once at the original birth tick; the
   child is the subject and never a POV. A failed/disabled owner releases the mature fallback once.
   Also form a caravan with the whole family (writers + newborn) during the naming window: the
   pending page must survive, resolve the caravan pawns, and emit normally — never the
   "lost every exact adult writer" discard. Repeat once with a pregnant colonist leaving on a
   caravan mid-pregnancy: her arc must stay open (no `ended_unknown`), and the birth on return must
   link to the same family arc.
7. **RimTalk and bridge smoke.** Load RimTalk Bridge, generate/read recent growth and birth pages, and
   confirm its context/memory injection accepts them as ordinary Progression/Tale entries without a
   duplicate Pawn Diary page, recursive submission, or exception. Rebuild and run both bridge logic
   suites before the playthrough. **Automation:** run `PawnDiaryRimTalkBridgeRuntimeTests` with both
   optional packages active. **Manual-only TODO:** open one real RimTalk chat/read surface and confirm
   the injected memories are presented normally; recursion/duplicate/state assertions are automated.
8. **Caps/performance.** Inspect the save after the matrix: `pendingBiotechGrowthMoments` and
   `pendingBiotechBirths` should normally be empty. Their XML values are admission limits (`256` by
   default): a full list rejects only the incoming owner and releases its ordinary fallback; it never
   evicts an established owner. A save loaded with more than the XML limit may temporarily retain those
   established rows until they resolve, but normalization must stay at or below the fixed `2048`-row
   corruption ceiling. Each family arc has at most `maximumSupporterRows` rows (`12` by default). Run a
   multi-family colony at speed 4 through several progression scans and one naming poll; there must be
   no per-tick warnings, repeated pages, or visible hitch from B1 state.
9. **Pre-cap ownership fixture.** Load or instrument a save with more than 256 valid pending growth and
   birth rows but fewer than 2048. Confirm load preserves every established row, saving round-trips them,
   and a newly triggered growth/birth owner fails open without changing the existing set. Resolve enough
   old rows to move the count below the current admission limit, trigger one new owner, and confirm
   admission resumes without a duplicate or missing page. **Automation only:** run
   `PawnDiaryBiotechComponentStateFixtureTests`, `PawnDiaryBiotechGrowthFlowTests`, and
   `PawnDiaryBiotechBirthFlowTests`; no separate manual step remains once all three pass in game.
10. **Biotech removed mid-save.** Take a Biotech save that contains at least one open family arc, one
   pending growth row, and one pending birth row (instrument via dev if needed), then disable Biotech
   and load. Expected: no Pawn Diary/Scribe error beyond RimWorld's own missing-content warnings; the
   Biotech settings rows disappear; pending growth rows release the ordinary Birthday page (or drop
   silently for dead pawns) on the normal cadence; pending birth rows still flush their canonical page
   with the birth-time child name after the naming grace (the naming poll finds nothing without the
   DLC), or prune with a single warning when every writer is gone; family arcs keep compacting and
   pruning. Save again, reload, and confirm the state keeps shrinking rather than freezing. Re-enable
   Biotech and confirm the game loads cleanly and remaining state resumes normal maintenance — nothing
   double-fires for pages already written while the DLC was off. **Automation:** in a base-only colony,
   run `PawnDiaryBiotechDlcOffMaintenanceTests` to prove frozen growth/birth maintenance and replay
   silence. **Manual-only TODO:** perform the actual three-launch Biotech on → off → on mod-list sequence,
   inspect the development log, settings-row visibility, open-arc compaction, and final reload.

Record the RimWorld version, active mod list, save fixture, language, and pass/fail result for each
row. Automated builds and pure/RimTest source coverage do not replace these live ownership checks.

---

## Biotech Phase 5 gene-identity acceptance (not a Phase 4 gate)

This separate TODO does not change the five open B1 rows above. Run it with generation disabled or
prompt-capture mode so no network request can leave the game.

1. **Base-only safety.** Load Base + Harmony + RimTest Redux + Pawn Diary with Biotech inactive. Run
   `GeneIdentityProjectionAndVersionedBaselineAreSilent`, `RealReimplantHookEmitsOnceAndClaimsAbilityScope`,
   and `RealImplantItemHookEmitsOneBoundedGenePage`. The guarded fixture must take its inactive branch;
   both exact fixtures must visibly skip. Check `Player.log` for patch-registration, missing-Def,
   type-initializer, XML, or gene-observation errors.
2. **Old-save baseline.** With Biotech active, load an old save whose colonists already have vanilla
   and/or custom genes. Keep Progression output disabled through one scan, save, reload, then re-enable
   it. Confirm no historical page appears and the save contains nested
   `geneIdentityObservationState` with `geneObservationVersion=2`, current xenotype identity, bounded
   `geneObservedDefNames`, and `geneObservedMembershipTruncated=true` only when the XML cap was hit.
3. **Exact ownership.** In a disposable Biotech colony run the two exact fixtures above. Confirm the
   real vanilla methods each create exactly one recipient-only `GeneIdentityChanged` event. Reimplant
   context must carry `xenogerm_reimplant`, the other pawn, selected themes, and no generic Ability
   duplicate; item implantation must carry `xenogerm_implant`. Replaying unchanged membership must be
   silent. Inspect `Player.log` and verify every test pawn, Xenogerm, Hediff, event, diary row, and
   transient ability scope is removed.
4. **Fallback and caps.** Outside an exact call, cause a stable xenotype Def transition or at least the
   XML minimum (default two) installed-gene membership changes, then let the slow progression scan run.
   Confirm one `observed_change` page with at most four separator-safe themes. A single membership-only
   fluctuation, active/suppressed recalculation, and a language switch with the same stable Def must be
   silent. Confirm full membership never appears in `gameContext` or the prompt preview.
5. **Reload/no resurrection.** Save after one exact and one fallback page, reload, and wait through two
   progression scans. Confirm both remain exactly once, no transient ability owner survives load, and
   later genuine mutations still record. Repeat loading that save without Biotech: Pawn Diary must
   retain primitive saved rows without resolving a DLC Def or emitting a page. Save once in that
   DLC-off state, re-enable Biotech, reload, and confirm the first gene scan silently writes a fresh
   version-2 baseline instead of emitting a catch-up identity page.

**Live checkpoint (2026-07-17):** RimWorld 1.6.4871 rev591 later ran the loaded all-DLC suite at
227/228. Both exact vanilla xenogerm methods, the corrected cooldown-separated reimplant replay, and
the N3-B salient identity context/key/evidence assertions passed. The sole failure was the loaded
official-DLC expected-group matrix omitting the intentionally Biotech-gated `progressionXenotype` row;
the fixture now includes it. Exact ownership item 3 is confirmed. The corrected matrix itself needs
one rerun; base-only, minimal-mod log, fallback/caps, and reload items remain open.

- [ ] **TODO:** Record exact RimWorld version, language, active mod list, old-save fixture, all RimTest
  results, prompt/context evidence, cleanup audit, both DLC branches, and relevant `Player.log` lines.

---

## Biotech Phase 6 mechanitor acceptance

Run with generation disabled or prompt capture. The pure suite already pins baseline/name/tenure/Tale
roles/caps, and `PawnDiaryBiotechMechanitorFlowTests` automates a real mechlink install/removal plus all
verified Harmony registrations. The following live behavior remains explicitly TODO:

1. **Base-only safety.** Load Base + Harmony + RimTest Redux + Pawn Diary without Biotech. Run the
   Phase-6 suite: the real lifecycle test must skip, the patch audit must pass, and `Player.log` must
   contain no missing Def, type initializer, XML, tracker, or patch-registration error.
2. **Old-save baseline and settings.** Load an old Biotech mechanitor with one or more controlled mechs,
   wait through a progression scan, save/reload, and inspect the nested `mechanitorObservationState`.
   Existing control must silently consume both historical first flags, record bounded stable IDs and
   tenure beginning at the first Pawn Diary observation (not the older vanilla relation tick), and
   create no catch-up page even with the group disabled/re-enabled.
3. **Exact first lifecycle.** On a fresh pawn, install a mechlink and connect the first mech. Confirm one
   install and one first-controlled controller page. Add/reassign/disconnect later mechs and change
   bandwidth/control groups repeatedly; routine churn must stay silent. Remove the mechlink while
   alive for one removal page, then separately kill a mechanitor and confirm death-driven removal adds
   no extra removal page.
4. **Combat and loss ownership.** Let a currently controlled mech produce one configured combat Tale.
   Confirm one controller `BiotechFirstControlledMechCombat` page and no generic Tale duplicate; later
   configured combat is silent. First produce a same-faction/friendly-fire Tale and confirm it neither
   emits nor consumes the hostile first-combat milestone. Kill a recent numerical mech such as
   “Lifter 1” (silent), a custom-
   named mech (one loss page), and an unnamed mech observed for the XML 15-day boundary (one loss page).
   Check exact death facts where available and verify the controller is never described as the attacker.
5. **Boss chapter and reload.** Use the real boss caller, confirm one saved caller-owned call page, save
   before defeat, reload, then defeat that exact boss. Confirm one `BiotechBossDefeated` page says only
   that the called threat was defeated, never who landed the final blow. Repeated manager/Tale order and
   a second reload must not duplicate either chapter; unrelated boss deaths must fail open. With two
   controllers holding overlapping same-kind calls, spawn and defeat both bosses and confirm each exact
   pawn ID resolves only its own caller.

- [ ] **TODO:** Record RimWorld version, language, active mod list, old/fresh save fixtures, Phase-6
  RimTest results, all five live rows, cleanup audit, both DLC branches, and relevant `Player.log` lines.

---

## Royalty Phase 2 persona lifecycle acceptance

This is a new Wave-5 gate and does not replace or close any Biotech B1 row above. Run once with
Royalty active and prompt-test mode enabled, then repeat the no-DLC safety row with Base + Harmony +
Pawn Diary only. Use a fresh coded persona weapon for each destructive/transfer branch so one branch
cannot satisfy another through leftover saved state.

- [x] **AUTOMATED:** User-confirmed the Phase-2 loaded automated suite green on 2026-07-18.
- [ ] **MANUAL LATER:** Rows 1–9 below, localized prompt previews, save excerpts, and the
  Royalty-absent log audit still need a recorded hands-on pass. The automated result does not close
  these rows.

| # | Scenario | Expected result | Regression signal |
|---:|---|---|---|
| 1 | Code a fresh persona weapon to an eligible colonist during normal play. Repeat equip callbacks without changing ownership. | Exactly one `PersonaWeaponBondFormed` page, one saved positive epoch, exact weapon/pawn context, and no duplicate bonded Thought page. | No page, repeated formation, wrong pawn/weapon, or a second Thought page. |
| 2 | Briefly switch the bonded pawn to another primary weapon, then re-equip the persona weapon before `60000` observable ticks. | No separation or recovery page; saved state returns to bonded. | Either lifecycle page appears, or pending state survives after recovery. |
| 3 | Keep the persona weapon observable but not primary across the `60000`-tick threshold and at least one `2500`-tick reconciliation deadline. | Exactly one `PersonaWeaponBondSeparated` page; later scans do not repeat it. | Early/missing/repeated separation, or a generic catch-up page. |
| 4 | Re-equip after row 3, then separately try recovery after only a short unrecorded swap. | Exactly one `PersonaWeaponBondRecovered` page for the recorded separation; the short-swap branch stays silent. | Missing/repeated recovery or recovery without an accepted separation page. |
| 5 | Destroy a live coded weapon; in a separate fixture kill its bonded pawn. | Destruction creates one `PersonaWeaponBondEnded` page with the exact cause. Pawn death advances state but creates no standalone Phase-2 ending page; a later Phase-3 canonical death owner may enrich the existing death route. | Duplicate/wrong-cause ending or a standalone `PersonaWeaponBondEnded` page on pawn death. |
| 6 | Move the weapon/pawn through caravan, transport, despawn, and map-removal transitions, then restore availability. | Unavailable/map-removal evidence never proves separation and emits no lifecycle page by itself. | Off-map travel produces separation/ending or log spam. |
| 7 | Transfer the exact coded weapon from one living pawn to another through the supported coding path. | One new formation page for the new pawn/epoch with exact previous-pawn context; the old epoch never recovers or repeats. | Same epoch reused, wrong POV, duplicate ending/formation, or later old-owner recovery. |
| 8 | Save/reload while bonded, pending short separation, meaningfully separated, and after an emitted recovery/ending; cross the threshold across a reload. | The deep-scribed ledger resumes each phase exactly once, preserves elapsed truth, and creates no old-save baseline/catch-up page. | Phase reset, duplicate page, invented old bond, lost pending duration, or corrupted Scribe row. |
| 9 | Load Base + Harmony + Pawn Diary with Royalty absent and exercise normal play/save/reload. | Royalty hooks/readers no-op; no missing Def/type/patch error and no persona page. | Startup/load exception, XML error, warning spam, or Royalty content dependency. |

- [ ] **TODO:** Record exact RimWorld version, language, active mod list, fresh/old-save fixtures,
  rows 1–9, all four localized prompt previews, both DLC branches, save excerpts, and relevant
  `Player.log` lines. Until recorded, Royalty Phase 2 is automated-green but not manually
  acceptance-complete.

---

## Royalty Phase 3 persona combat and death acceptance

Phase 3 reuses the existing Tale, Thought, and death owners. Use a newly coded weapon/bond epoch for
each branch, keep generation disabled unless inspecting a prompt, and inspect the saved event/context
as well as the visible page. The first qualifying kill is a vanilla qualifying Tale such as
`KilledMajorThreat`; an arbitrary ordinary kill is not sufficient.

- [x] **ASSEMBLY-FREE:** Pure Royalty/capture/pipeline tests pass. The expanded real-kill,
  delayed-Tale-flush, disabled-output, non-primary-death, and save-flag RimTest fixtures compile.
- [x] **AUTOMATED LOADED RERUN:** After the timing fix and two fixture-query corrections, the focused
  rerun passed 244/244 on 2026-07-19. This does not close the hands-on rows below.
- [ ] **MANUAL LATER:** Perform and record every row below.

| # | Scenario | Expected result | Regression signal |
|---:|---|---|---|
| 1 | With the exact coded persona weapon equipped as primary, make its pawn trigger a configured qualifying Tale (`KilledMajorThreat` in vanilla). | Exactly one solo killer-POV `PersonaWeaponFirstConsequentialKill` page. Context keeps the Tale domain and source Tale, exact killer/victim roles, weapon/bond/trait facts, and no standalone `persona_weapon=` domain marker. | Pair/batch/victim POV, missing source Tale, duplicate page, or a standalone persona event. |
| 2 | Trigger a second qualifying Tale with the same saved bond epoch in the same tick, then flush Tale batches. | No second first-kill milestone; exactly one ordinary kill Thought and one ordinary batched combat page remain for the second kill. | A later kill is retold as the first, or the earlier owner globally suppresses its Thought/Tale. |
| 3 | Disable `personaWeaponMilestone`, perform the first qualifying kill, re-enable it, then perform another qualifying kill. | The first truth is consumed without a milestone page; later kills never catch up. The original Tale and any unclaimed exact kill Thought keep their ordinary routes. | Catch-up first-kill page, lost ordinary Tale, or lost Thought. |
| 4 | Try ordinary/nonqualifying kills, a qualifying Tale with the bonded weapon not primary, mismatched killer/victim scope, and unrelated Thoughts. | Existing Tale/Thought/death behavior is unchanged and the persona milestone is not consumed by an unverified weapon kill. | Persona context leaks onto unrelated events or ordinary capture disappears. |
| 5 | Verify a persona trait with an exact `killThought`, first with an accepted milestone and then with milestone output disabled/rejected. | The accepted canonical page claims the matching Thought without a duplicate; unclaimed/rejected signals produce the normal Thought page exactly once. | Duplicate milestone+Thought, wrong Def claimed, or silently lost Thought. |
| 6 | Kill a colonist who is the bonded wielder through an existing Tale death route and separately through `Pawn.Kill(null)` fallback; repeat while the still-coded bond is live but the weapon is non-primary. | The one existing death-description page retains pre-`UnCode` weapon/bond/trait facts and `bond_end_cause=pawn_death`; no standalone `PersonaWeaponBondEnded` page appears. | Missing persona facts for primary or separated/non-primary state, a second death/ending page, or first-person narration by the dead pawn. |
| 7 | Make a qualifying persona kill whose victim also qualifies for the existing victim-death route. | One solo killer milestone and the existing victim death description coexist under their own dedup owners; no victim route is stolen. | Killer page suppresses victim death, or either route duplicates. |
| 8 | Save/reload after an observed-but-unrecorded disabled/rejected first kill and after a recorded first-kill page, then make another qualifying kill. | `firstConsequentialKillObserved` and `firstConsequentialKillEventRecorded` retain their distinct meanings; neither save invents another first. | Flags collapse, reset, catch up, or duplicate after reload. |
| 9 | Repeat normal play/save/reload with Royalty absent. | Tale, Thought, and death capture work normally; persona hooks/context are inert with no missing Def/type/patch warnings. | Startup/load exception, warning spam, or Royalty-only context/page without Royalty. |

- [ ] **TODO:** Record exact RimWorld version, language, active mod list, rows 1–9, the localized
  first-kill and enriched-death prompt previews, saved flag excerpts, both DLC branches, and relevant
  `Player.log` lines before calling Phase 3 acceptance-complete.

---

## Royalty Phase 4 title and psylink acceptance

Phase 4 owns exact faction-title edges and title/psylink cause arbitration. Use a fresh eligible pawn
for each destructive branch. Keep prompt-test mode enabled while inspecting prompts; no model request
is required. The last automated loaded run is green at 256/256. Post-review coverage expands the
compiled suite to 258 tests; those two new save/expiry fixtures still require a loaded execution, and
all manual rows below remain open.

The first loaded-game run on RimWorld 1.6.4871 reached 249/252. Two failures shared one runtime cause:
Harmony rejected the title postfix's non-vanilla `currentTitle` argument name, so the hook audit and
real promotion fixture both failed. The third was an outdated four-token ritual-catalog fixture after
Phase 4 added two narrow bestowing tokens. The second run reached 250/252 and confirmed both fixes.
Its title loss shared the promotion's old pawn/faction/tick dedup key because the paused runner kept
both real edges in one tick; the key now includes transition and exact before/after title Def names.
The other failure was the older non-primary death fixture colliding with another loaded mod's
equipment-removal patch, so that fixture now establishes the pending state through Pawn Diary's exact
observer. The subsequent user-confirmed loaded rerun passed 252/252. Adversarial hardening then added
four loaded fixtures and strengthened several existing ones; the expanded user-confirmed rerun passed
256/256.

- [x] **ASSEMBLY-FREE/BUILD:** Pure suites pass 318 Royalty, 2,437 pipeline, 665 capture-policy, and
  125 Narrative Continuity assertions; runtime and 258-test RimTest assemblies build.
- [x] **PRIOR AUTOMATED LOADED BASELINE:** The 252/252 user-confirmed run covered
  `PawnDiaryRoyaltyProgressionFlowTests`, the exact Royalty hook audit in
  `PawnDiaryRoyaltyFlowTests`, `PawnDiaryRoyaltyStateFixtureTests`, and Royalty Def/prompt smoke tests.
- [x] **EXPANDED AUTOMATED LOADED RERUN:** All 256 tests passed, including the real neuroformer comp,
  disabled ritual non-transfer, reverse-order title memory, real lifecycle reset, same-tick loss label,
  and no-Royalty immediate-load invalidation additions.
- [ ] **POST-REVIEW LOADED RERUN:** Run all 258 tests, including save-time scanner-title reconciliation
  and the equal-expiry combined psylink/title-memory owner. The attendee-first non-claim assertion is
  folded into the existing bestowing fixture.
- [ ] **MANUAL LATER:** Perform and record every row below before marking R1 acceptance-complete.

| # | Scenario | Expected result | Regression signal |
|---:|---|---|---|
| 1 | Grant a first Empire title, promote it, reduce/demote it, then remove it completely. Include a second faction or same-looking localized title if available. | One exact `RoyalTitleGained`, `RoyalTitlePromoted`, `RoyalTitleDemoted`, and `RoyalTitleLost` page per real edge. Each names the correct faction and exact before/after title; unchanged callbacks are silent. | Generic `RoyalTitleChanged`, label-based rank, wrong faction, missing loss, or repeated scanner page. |
| 2 | Disable `progressionRoyalTitle`/Progression, change a title, re-enable, and wait through a scanner cadence. | Saved per-faction truth advances while disabled and no catch-up page appears. | Re-enable invents a historical promotion/loss or the saved baseline remains stale. |
| 3 | Complete an imperial bestowing that grants both title and psylink with another eligible attendee present. Repeat once with `ritualRoyal` disabled. | Enabled: the target's one ritual perspective carries cause, faction, title before/after, and psylink before/after; ordinary attendee perspectives remain but do not claim the target mutation; no separate title, psylink, or matching title-Thought page. Disabled: no page and no later Progression/Thought fallback. | Missing target facts, attendee page copying target mutation context, separate progression/Thought page, or a disabled ceremony leaking through another group. |
| 4 | Complete anima-tree linking. | One existing anima ritual page carries the exact psylink change; no separate `PsylinkLevel` page. | Missing psylink facts or a duplicate progression page. |
| 5 | Use a neuroformer. | One cause-aware `PsylinkLevel` page with `psylink_cause=neuroformer`; the later scanner is silent. | Unknown cause, no page, or a scanner duplicate. |
| 6 | Exercise a modded/direct title or psylink mutation that bypasses an exact hook, and separately prevent the expected ritual route from arriving until expiry. Let another pending target die/leave before expiry. | The scanner detects a disappeared faction as exact loss; an eligible expired mutation fails open into at most one truthful enabled progression route; an expired missing-pawn owner is pruned silently. | Silent title loss, repeated/dropped enabled fallback, or a stale missing-pawn owner occupying the cap. |
| 7 | Gain an exact royal-title award/loss memory without a matching title/ritual owner. Test both correlation expiry and saving inside the window. | After expiry it follows ordinary Thought capture once, unchanged; a pre-expiry save flushes the same ordinary page before serialization. | Globally suppressed ThoughtDef, save/load loss, permanent pending row, or duplicate Thought. |
| 8 | Save/reload with several faction observations and while a ritual mutation is briefly pending. Also load an old pre-Phase-4 save. | Faction observations/psylink baseline survive. Transient ownership resets at load and never emits a stale fallback; old saves silently baseline without retroactive pages. | Missing faction row, invented loss/gain, stale post-load fallback, or load exception. |
| 9 | Run Base + Harmony + Pawn Diary with Royalty absent, including loading a Royalty-authored save, pausing, and immediately resaving before the first tick. Continue ordinary Thought/Tale/progression play. | Royalty settings/content remain unavailable or inert; saved Royalty title/psylink truth is preserved but availability is invalidated during `LoadedGame`; no missing Def/type/patch error, page, or warning spam. | Stale `royaltyObservationAvailable=true` in the immediate resave, empty snapshot interpreted as title loss, startup exception, or Royalty-only output. |
| 10 | Inspect Full/Balanced/Compact English and Russian prompt previews for all four title edges, bestowing, anima linking, and neuroformer. | Required pawn/cause/faction/title/psylink facts remain; Compact removes optional duty prose first; no IDs/ticks/correlation tokens leak. | Missing central fact, untranslated key, internal identifier, or duty prose displacing before/after truth. |

- [ ] **TODO:** Record exact RimWorld version/build, languages, active mod lists, rows 1–10, prompt
  previews, save excerpts, both DLC branches, and relevant `Player.log` lines. Until
  recorded, Phase 4 and R1 remain acceptance-open.

---

## Royalty Phase 5 succession acceptance

Phase 5 correlates an exact inheritance candidate with the outer `wasInherited` commit and owns only
that edge. Run on a disposable Royalty colony with prompt-test mode enabled. The runtime and expanded
267-test RimTest assemblies build. The first expanded loaded run reached 264/267 because three
generated fixture pawns were not discoverable through the live-colonist roster; after spawning only
those disposable heir/writer pawns, the user-confirmed corrected run passed 267/267.

- [x] **ASSEMBLY-FREE/BUILD:** `RoyaltyContextTests` passes 358 assertions,
  `DiaryPipelineTests` passes 2,493, and the runtime plus 267-test RimTest assemblies build.
- [x] **AUTOMATED LOADED:** All 267 tests pass, including strict one-page real inheritance and its
  instant Freeholder step, equal-or-higher silence, delayed target claim/retirement, bestowing/title
  dedup, explicit `ChangeRoyalHeir`, nonempty component-ledger Scribe, missing-key/old-expiry
  normalization, Royalty-inactive hook/scope silence, and load-reset fixtures.
- [ ] **MANUAL LATER:** Perform every row below before calling Phase 5 acceptance-complete.

| # | Scenario | Expected result | Regression signal |
|---:|---|---|---|
| 1 | Kill one titled pawn after assigning an eligible lower-ranked/no-title colonist as exact heir. | One heir-POV `RoyalSuccession` page names the exact deceased holder, heir, inherited title, and faction. The same title edge creates no ordinary title/Thought page. | Candidate-only page, deceased POV, wrong faction/rank, or duplicate promotion/Thought. |
| 2 | In one outer death action, exercise multiple inheritable title edges if the active mod profile supplies multiple royal factions/titles. | One page per distinct exact deceased/heir/faction/title edge, including distinct edges in the same tick; no merge by labels/tick alone. | Missing edge, same-tick merge, or one edge borrowing another faction/title. |
| 3 | Exercise no successor, failed inheritance, malformed/missing relation, and an heir already holding an equal or higher title. | No succession page, no invented promotion/demotion/claim, and unrelated title callbacks remain on ordinary routes. | Any page authorized by `TryInherit` candidate alone or by death/rank change inference. |
| 4 | Give a titleless heir an inheritable Acolyte-or-higher claim, delay the offered ceremony well beyond 2,500 ticks, then accept it. Separately introduce a same-pawn/faction title edge whose predecessor does not match the saved cursor. | The instant no-title-to-Freeholder edge is owned as an intermediate step; the delayed exact target is still claimed once and retires the fact. The contradictory edge invalidates ownership and follows ordinary routing. No title event is lost when the outer commit fails or throws. | Duplicate title/bestowing page, one-hour timeout, swallowed ordinary mutation, or a terminal/stale fact relabeling a later promotion. |
| 5 | Allow vanilla's instant award/bestowing path to overlap inheritance, including a title-plus-psylink action if reachable. | Succession owns the inheritance statement; the ceremony/ritual may retain independent truthful psylink/ceremony facts but does not restate inheritance as progression. | Missing psylink/ritual truth or two pages claiming the same inherited title. |
| 6 | Call automatic/direct `SetHeir`, then complete the explicit `ChangeRoyalHeir` quest signal with a different chosen heir. | Automatic/direct assignment is silent. The explicit quest creates one heir-POV `RoyalHeirAppointed` page with heir/title/faction and no deceased marker. | Automatic bookkeeping page, appointment presented as death/completed inheritance, or wrong POV. |
| 7 | Save/reload a nonempty committed chain after its intermediate title but before the target; also load a first-version Phase-5 row whose short `expiresTick` is already in the past and a pre-Phase-5 save missing `royaltyPendingSuccessions`. | The cursor round-trips and can claim only its exact delayed successor. The old pending row migrates to terminal persistence; already claimed/malformed rows prune; a missing old key becomes an empty ledger with no catch-up page. Active candidates and exact-edge duplicate cache never survive load, and the ledger remains capped. | Load exception, candidate/cache serialized as fact, one-hour expiry, stale post-load page, unbounded list, or lost exact delayed claim. |
| 8 | Inspect Full/Balanced/Compact English and Russian succession/appointment prompt previews. | Required supplied identities/title/faction remain. Appointment omits deceased. No pawn/faction/title IDs, ticks, correlation IDs, commit flags, or `wasInherited` appear. | Missing central fact, untranslated key, invented death, or internal proof metadata in prose. |
| 9 | Run Base + Harmony + Pawn Diary without Royalty; load/save a Royalty-authored Phase-5 save and continue ordinary title-independent play. | All three Phase-5 hooks/readers are inert, saved detached rows normalize safely, and no missing Def/type/patch warning, page, or DLC dependency appears. | Startup/load exception, warning spam, Royalty-only output, or paid-DLC requirement. |

- [ ] **TODO:** Record exact RimWorld assembly/build, language, active mod lists, all manual rows above,
  prompt captures, save excerpts, and Royalty-on/off logs. The 267/267 automated loaded result is
  confirmed; Phase 5 remains hands-on acceptance-open until the remaining evidence is recorded.

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
   text. Type into the editor while it runs and confirm completion does not overwrite that unsaved text.
   With no lane (or a forced failure) confirm it falls back to the built-in override text; then configure
   a lane and confirm the saved configuration fingerprint makes the pawn retry.
5. **Migration:** loading a save made by the *previous* bridge version once releases its locked external
   overrides (the first-tick sweep, including when 1-2-3 is inactive) so the new editable layers are
   visible, while a player custom rule beneath the override survives. The retired `provideContextLine`
   key is dropped; a legacy `usePersonalityOutlook=false` loads as Off, otherwise a missing new mode
   migrates to Override.

## Core XML compatibility packs (Alpha/VIE/WBR/VTE/Hospitality/VEE)

Run each target both present and absent; absent means no groups in the Events settings, no capture,
and no warning. Exact-string matchers should also stay inert when optional DLC content is unavailable.

**Alpha Memes.** With Alpha Memes + Ideology, complete one configured funeral and one non-funeral
ceremony (rodeo/sparring are useful). Confirm `alphamemes_funeral` and `alphamemes_rituals` win before
core `ritualFinished`, and that the group-specific instruction is present alongside the pawn's ritual
role. Let an eligible `AM_UnforgettableRodeo`-style memory clear the global mood threshold, trigger
`AM_Speech_Baptism`, and confirm the thought/baptism groups. Vanilla funerals must remain in their
existing group. If Funeral Framework also reports the rite, verify one ceremony does not become two
near-identical pages.

**VIE Memes.** Complete a bonfire and a wicker-man burning: they must classify to
`viememes_rituals` and `viememes_darkrites`, with role + group guidance and no core fallback. Verify one
eligible outcome memory, then interrogate a prisoner and confirm the participating colonist owns the
`viememes_interrogation` entry. A fleshcrafted part should remain in the appropriate core added-part
group. With Alpha Memes loaded simultaneously, both ritual prefixes must keep their own groups.

**Way Better Romance.** Force the three real interactions: a hookup attempt should produce a paired
`wbr_hookup` entry; date/hangout invitations should enter the min-2 ambient batch (with a promoted
single tested separately). Let one rebuff/failure memory clear the global threshold and confirm
`wbr_thoughts`. Vanilla `RomanceAttempt`, `MarriageProposal`, and new Lover relations must still route
to the core romance groups. Review one rejection day for interaction + thought near-duplication.

**Vanilla Traits Expanded.** Create/re-roll several new `VTE_Vengeful` pawns and inspect the intended
psychotype affinity pull; dev-trigger the kleptomaniac, technophobe-tantrum, and panic-freezing states
and confirm `vte_mentalbreaks`. Trigger an allowlisted memory such as `VTE_CouldNotFinishItem`, then keep
situational thoughts such as `VTE_MyRivalsAreAlive` / `VTE_AnimalsInColony` active and confirm only the
real memory gets VTE voice. Without VTE, inspect the psychotype policy and confirm the `PatchOperationFindMod` added no
rules. Save/load a rolled core psychotype, remove VTE, and confirm it remains valid.

**Hospitality.** Receive a visitor group and confirm exactly one package-gated
`HospitalityGuestsArrived` MapWitness page, not one per colonist and never one in a vanilla/no-Hospitality
game. Repeated colonist diplomacy/charm with a guest must reach the colonist's min-4 ambient hosting
note despite the ineligible guest; guest-to-guest scrounging must create nothing, while a colonist who
actually participates may receive `hospitality_scrounge`. Trigger `HappyGuestJoins` and confirm the
page describes a guest asking/wanting to join—not a completed join before the player answers the
ChoiceLetter. Repeat with the Continued fork (same package ID). Phase-2 guest-presence context is not
implemented and should not appear.

**Vanilla Events Expanded.** While `VEE_Drought` is active, API Explorer prompt preview should show the
condition tint but no standalone start page; clear it and wait through debounce. Repeat one tint in the
No-Odyssey configuration. `RaidEnemyPurple` and `InfestationPurple` must classify to `vee_raids`, while
the neutral `VEE_VisitorGroupRaid` must not be labeled a betrayal. Earthquake, meteorites, space
battle/shuttle crash, and purple manhunter families must each create one labeled MapWitness page and
dedup a retry inside 2,500 ticks. A visible VEE hediff may feed a day reflection, never N immediate
pages. Hidden `MightJoin`/`Traitor` state and situational VEE thoughts are deliberately uncaptured.

## SpeakUp adapter

1. With SpeakUp but no adapter, compare against a pre-change save: frozen `speakup_chitchat` setting
   and `SpeakUpAmbientDay` pages still work, ambient/promotion behavior is unchanged, and the core
   reply-scheduling guard prevents render-time reply storms.
2. Add `PawnDiary.SpeakUp`: the fallback disappears from Events settings, the five lower-order groups
   appear, a conversation meeting the default ≥3 reply threshold produces one
   `speakupbridge_conversation`, and deep-talk promotion can stand alone without an unclaimed-key warning.
   Exercise SpeakUp's 1- and 5-lines-per-conversation settings and adapter thresholds 1 and 5.
3. Force-load the adapter without SpeakUp: it must stay inert—gated groups absent, no patches, no
   repeated missing-member warnings.
4. Load a pre-adapter save containing `SpeakUpAmbientDay` pages and a `speakup_chitchat` toggle in both
   configurations. Old pages display and the saved fallback toggle survives.
5. Let either participant die, despawn, enter a caravan/gravship, or leave the map mid-talk. The
   transient accumulation must be dropped without an exception or partial event.
6. Read a full diary day with whole-conversation capture enabled. Confirm whether retained Tier-1
   fragments plus Tier-2 summaries feel duplicative; record the playtest decision before changing the
   intentional coexistence policy.

## Rimpsyche bridge

1. With Pawn Diary + Rimpsyche + bridge, stress 20+ conversations. Ordinary rows should feed the
   ambient group without grammar reentrancy; a high-|alignment| exchange should produce one claimed
   `rimpsyche_conversation` entry with localized topic/sign and no raw float.
2. Inspect a prompt preview for the localized top descriptors/interests. Toggle Tier B off/on and use
   `GetPsychotype`/the pawn editor to confirm the source-owned override clears and reapplies; load a new
   game and confirm no previous colony value leaks.
3. Force-load the bridge without Rimpsyche: gated groups stay absent, the game loads, and Tier C emits
   at most one graceful disabled warning. With Rimpsyche but no bridge, Pawn Diary should log nothing
   Rimpsyche-specific.
4. Save with a Tier-B override, remove the bridge, then load. Core override-source-missing behavior must
   fall back cleanly with no Scribe or missing-type exception.
5. Save immediately after an accepted charged conversation, reload, and trigger the pair again inside
   60,000 ticks. The persisted pair cooldown must suppress it; after the boundary it may record again.
6. Confirm the reflection postfix fires once on the installed 1.0.41 hook and embedded interest labels
   localize correctly. Tune the XML `0.55` threshold only from observed alignment distribution.

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
