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
matching profiles.

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
   relation tenure, and create no catch-up page even with the group disabled/re-enabled.
3. **Exact first lifecycle.** On a fresh pawn, install a mechlink and connect the first mech. Confirm one
   install and one first-controlled controller page. Add/reassign/disconnect later mechs and change
   bandwidth/control groups repeatedly; routine churn must stay silent. Remove the mechlink while
   alive for one removal page, then separately kill a mechanitor and confirm death-driven removal adds
   no extra removal page.
4. **Combat and loss ownership.** Let a currently controlled mech produce one configured combat Tale.
   Confirm one controller `BiotechFirstControlledMechCombat` page and no generic Tale duplicate; later
   configured combat is silent. Kill a recent numerical mech such as “Lifter 1” (silent), a custom-
   named mech (one loss page), and an unnamed mech observed for the XML 15-day boundary (one loss page).
   Check exact death facts where available and verify the controller is never described as the attacker.
5. **Boss chapter and reload.** Use the real boss caller, confirm one saved caller-owned call page, save
   before defeat, reload, then defeat that exact boss. Confirm one `BiotechBossDefeated` page says only
   that the called threat was defeated, never who landed the final blow. Repeated manager/Tale order and
   a second reload must not duplicate either chapter; unrelated boss deaths must fail open.

- [ ] **TODO:** Record RimWorld version, language, active mod list, old/fresh save fixtures, Phase-6
  RimTest results, all five live rows, cleanup audit, both DLC branches, and relevant `Player.log` lines.

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
