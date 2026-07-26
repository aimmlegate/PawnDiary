// In-game health-condition capture tests for Pawn Diary's hediff signal path (design/TEST_COVERAGE_PLAN.md
// §3, EVT-11). The AddHediff Harmony hook (HealthTrackerAddHediffPatch) forwards colonist hediffs to
// HediffSignal, which classifies each against the XML Hediff-domain groups and either writes an
// immediate solo diary page or defers the change to the end-of-day reflection (a "day-signal"). These
// tests drive the real vanilla `Pawn_HealthTracker.AddHediff` choke point and assert the routing:
//   - EVT-11a: an added body part (peg leg) matches the Immediate "artificial body parts" group and
//     produces one solo diary event carrying the added-part + affected-body-part markers.
//   - EVT-11b: a plain injury (bruise) is ignored by every hediff group (excludeInjuries) and produces
//     nothing — injuries are owned by the death/tale pages, not the hediff page.
//   - EVT-11c: a worsening major-health condition (flu) is a day-signal: neither its appearance nor a
//     scanner-detected severity-step change writes an immediate page; both defer to the day reflection.
//
// All the fragile scaffolding — isolated non-generating pawns, snapshots, failure-safe teardown, and
// the no-leak audit — lives in the shared PawnDiaryRimTestScope harness. Every added hediff is removed
// through RegisterCleanup so the harness audit passes.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that health-condition changes reach the right Pawn Diary page: added parts create an
    /// immediate solo event with body-part markers, ignored hediffs (injuries) are dropped, and
    /// severity-tracked major conditions route to the deferred day-signal instead of an immediate page.
    /// Requires a loaded game because the production capture pipeline ignores events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryHediffFlowTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic =
            BindingFlags.Static | BindingFlags.NonPublic;

        private static readonly MethodInfo ScanHediffProgressionsMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ScanHediffProgressionsForDiaryEvents", PrivateInstance);
        private static readonly FieldInfo ActiveHediffProgressionsField =
            typeof(DiaryGameComponent).GetField(
                "activeHediffProgressions", PrivateInstance);
        private static readonly FieldInfo PendingDayHediffsField =
            typeof(DiaryGameComponent).GetField("pendingDayHediffs", PrivateInstance);
        private static readonly FieldInfo RecentEventsField =
            typeof(DiaryGameComponent).GetField("recentEvents", PrivateInstance);
        private static readonly FieldInfo CachedFreeColonistsField =
            typeof(DiaryGameComponent).GetField(
                "cachedFreeColonists", PrivateStatic);
        private static readonly FieldInfo CachedFreeColonistsTickField =
            typeof(DiaryGameComponent).GetField(
                "cachedFreeColonistsTick", PrivateStatic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        /// <summary>
        /// Opens a fresh scope, enables the two hediff groups this suite drives (the Immediate
        /// artificial-body-part group and the DayReflection major-health catch-all), and creates one
        /// isolated adult colonist with generation disabled.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("hediffPartGainedArtificial", "hediffMajorHealth");
            pawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived —
        /// even when the test above threw partway through.
        /// </summary>
        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                pawn = null;
            }
        }

        /// <summary>
        /// EVT-11a. Installs a peg leg (a Hediff_AddedPart) on a leg and verifies the AddHediff hook
        /// records one Immediate solo diary event whose interaction Def is the hediff's defName and
        /// whose gameContext carries the added-part body marker and the affected body-part label.
        /// </summary>
        [Test]
        public static void AddedBodyPartCreatesImmediateSoloEvent()
        {
            HediffDef pegLeg = RequireDef<HediffDef>("PegLeg");
            BodyPartRecord leg = RequireBodyPart(pawn, "Leg");

            Hediff hediff = HediffMaker.MakeHediff(pegLeg, pawn);
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));

            // The stored interactionDefName for a hediff event is the hediff's own defName (AddSoloEvent
            // is called with data.DefName = HediffDefName(hediff)), NOT any vanilla interaction name.
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => pawn.health.AddHediff(hediff, leg),
                "PegLeg",
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("part_kind=addedpart", StringComparison.OrdinalIgnoreCase) >= 0,
                "The hediff event context did not carry the added-part body marker.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("body_part=", StringComparison.OrdinalIgnoreCase) >= 0,
                "The hediff event context did not carry the affected body-part label.");
        }

        /// <summary>
        /// EVT-11b. Adds a plain bruise (a Hediff_Injury). Every hediff group sets excludeInjuries, so
        /// the signal fails the basic policy gate and no diary event is created — the negative gate.
        /// </summary>
        [Test]
        public static void InjuryHediffProducesNoEvent()
        {
            HediffDef bruise = RequireDef<HediffDef>("Bruise");
            BodyPartRecord torso = RequireBodyPart(pawn, "Torso");

            Hediff hediff = HediffMaker.MakeHediff(bruise, pawn);
            hediff.Severity = 5f;
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));

            scope.RequireNoNewEvent(() => pawn.health.AddHediff(hediff, torso));
        }

        /// <summary>
        /// EVT-11c. Drives the real top-level component severity scanner against one spawned, test-owned
        /// colonist. Its first snapshot baselines a reversible Flu without output; a later severity-stage
        /// increase adds exactly one progressed day-reflection record; a same-stage rescan cannot replay
        /// even after both downstream dedup and evidence are removed; removing the hediff makes the scanner
        /// discard its stale baseline. Player colonists and their transient scanner state are never exposed
        /// to the fixture.
        /// </summary>
        [Test]
        public static void TopLevelScannerBaselinesProgressesAndDoesNotReplay()
        {
            HediffDef flu = RequireDef<HediffDef>("Flu");
            Hediff hediff = HediffMaker.MakeHediff(flu, pawn);
            hediff.Severity = 0.4f;
            scope.RegisterCleanup(() => RemoveHediffIfPresent(pawn, hediff));

            DiaryInteractionGroupDef group;
            HediffSignalPolicy policy;
            bool matched = DiaryGameComponent.TryGetHediffPolicy(hediff, out group, out policy);
            PawnDiaryRimTestScope.Require(
                matched && group != null && policy != null,
                "The flu condition did not resolve to a Hediff-domain diary policy.");
            PawnDiaryRimTestScope.Require(
                string.Equals(group.defName, "hediffMajorHealth", StringComparison.Ordinal),
                "The flu condition did not classify to the major-health catch-all group.");
            PawnDiaryRimTestScope.Require(
                policy.mode == HediffDiaryMode.DayReflection,
                "The major-health group is expected to route health changes to the day reflection.");
            PawnDiaryRimTestScope.Require(
                policy.recordOnSeverityIncrease,
                "The major-health group is expected to track severity increases.");

            DiaryTuningDef tuning = DiaryTuning.Current;
            bool originalDaySummaryEnabled = tuning.daySummaryEnabled;
            tuning.daySummaryEnabled = true;
            scope.RegisterCleanup(
                () => tuning.daySummaryEnabled = originalDaySummaryEnabled);

            // Snapshot every component/static collection the real scanner touches, then make the shared
            // one-tick free-colonist cache contain only this test pawn. Spawn after taking the snapshot so
            // the original player cache can never retain the fixture pawn when it is restored.
            IDictionary activeProgressions = IsolateHediffScannerTo(pawn);
            scope.SpawnAsLiveColonist(pawn);

            string stateKey =
                pawn.GetUniqueLoadID() + "|Flu|whole_body";
            IDictionary pendingDayHediffs = PendingDayHediffs();
            string dayKey = pawn.GetUniqueLoadID() + "|"
                + (Find.TickManager.TicksAbs / GenDate.TicksPerDay);
            scope.RegisterCleanup(() => pendingDayHediffs.Remove(dayKey));
            pendingDayHediffs.Remove(dayKey);

            // AddHediff is still the real reversible vanilla boundary, but turn its page group off for
            // this one call so the test begins with no appearance evidence. The AddHediff hook establishes
            // its own baseline before the user gate; remove that test-owned row so the component scanner's
            // snapshotOnly pass below must create the baseline itself.
            PawnDiaryMod.Settings.SetGroupEnabled(group.defName, false);
            try
            {
                scope.RequireNoNewEvent(() => pawn.health.AddHediff(hediff));
            }
            finally
            {
                PawnDiaryMod.Settings.SetGroupEnabled(group.defName, true);
            }
            activeProgressions.Remove(stateKey);

            scope.RequireNoNewEvent(() => RunTopLevelHediffScanner(snapshotOnly: true));
            PawnDiaryRimTestScope.Require(
                activeProgressions.Contains(stateKey),
                "The top-level hediff scanner did not baseline the active Flu.");
            int baselineStage = ActiveProgressionStage(activeProgressions[stateKey]);
            PawnDiaryRimTestScope.Require(
                !pendingDayHediffs.Contains(dayKey),
                "The scanner's snapshot-only baseline emitted day-reflection evidence.");

            hediff.Severity = 0.7f;
            scope.RequireNoNewEvent(() => RunTopLevelHediffScanner(snapshotOnly: false));
            int progressedStage = ActiveProgressionStage(activeProgressions[stateKey]);
            PawnDiaryRimTestScope.Require(
                progressedStage > baselineStage,
                "The top-level hediff scanner did not advance the saved Flu severity stage.");
            RequireSingleProgressedDayRecord(pendingDayHediffs, dayKey, "Flu");

            // Remove both defenses below the scanner. If the scanner incorrectly re-submits the same
            // severity stage, the day record will reappear; a correct scanner sees no forward change.
            pendingDayHediffs.Remove(dayKey);
            RecentEvents().Remove(
                DiaryGameComponent.HediffDedupKey(
                    pawn, hediff, policy, HediffSignalSource.Progressed));
            scope.RequireNoNewEvent(() => RunTopLevelHediffScanner(snapshotOnly: false));
            PawnDiaryRimTestScope.Require(
                !pendingDayHediffs.Contains(dayKey)
                    && ActiveProgressionStage(activeProgressions[stateKey]) == progressedStage,
                "A same-stage top-level scan replayed the Flu progression.");

            // Removing a hediff is reversible and page-free. The next real scanner pass must remove its
            // transient episode row rather than leaving stale state behind.
            scope.RequireNoNewEvent(() =>
            {
                pawn.health.RemoveHediff(hediff);
                RunTopLevelHediffScanner(snapshotOnly: false);
            });
            PawnDiaryRimTestScope.Require(
                !activeProgressions.Contains(stateKey),
                "The top-level scanner retained a stale Flu baseline after removal.");
        }

        /// <summary>
        /// Gives the production scanner one test-owned colonist while retaining the player's exact
        /// per-pawn baselines and static one-tick colonist cache for teardown.
        /// </summary>
        private static IDictionary IsolateHediffScannerTo(Pawn testPawn)
        {
            if (ScanHediffProgressionsMethod == null
                || ActiveHediffProgressionsField == null
                || CachedFreeColonistsField == null
                || CachedFreeColonistsTickField == null)
            {
                throw new AssertionException(
                    "Could not resolve the top-level hediff scanner or its transient state.");
            }

            IDictionary active =
                ActiveHediffProgressionsField.GetValue(scope.Component) as IDictionary;
            if (active == null)
            {
                throw new AssertionException(
                    "DiaryGameComponent.activeHediffProgressions was unavailable.");
            }

            List<DictionaryEntry> originalActive = new List<DictionaryEntry>();
            foreach (DictionaryEntry entry in active)
            {
                originalActive.Add(entry);
            }

            object originalCachedColonists = CachedFreeColonistsField.GetValue(null);
            int originalCachedTick = (int)CachedFreeColonistsTickField.GetValue(null);
            scope.RegisterCleanup(() =>
            {
                active.Clear();
                for (int i = 0; i < originalActive.Count; i++)
                {
                    active[originalActive[i].Key] = originalActive[i].Value;
                }

                CachedFreeColonistsField.SetValue(null, originalCachedColonists);
                CachedFreeColonistsTickField.SetValue(null, originalCachedTick);
            });

            active.Clear();
            CachedFreeColonistsField.SetValue(
                null, new List<Pawn> { testPawn });
            CachedFreeColonistsTickField.SetValue(
                null, Find.TickManager.TicksGame);
            return active;
        }

        private static void RunTopLevelHediffScanner(bool snapshotOnly)
        {
            ScanHediffProgressionsMethod.Invoke(
                scope.Component, new object[] { snapshotOnly });
        }

        private static int ActiveProgressionStage(object state)
        {
            FieldInfo field = state?.GetType().GetField(
                "currentStage", BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                throw new AssertionException(
                    "Could not read ActiveHediffProgressionState.currentStage.");
            }

            return (int)field.GetValue(state);
        }

        private static IDictionary PendingDayHediffs()
        {
            IDictionary pending =
                PendingDayHediffsField?.GetValue(scope.Component) as IDictionary;
            if (pending == null)
            {
                throw new AssertionException(
                    "DiaryGameComponent.pendingDayHediffs was unavailable.");
            }

            return pending;
        }

        private static IDictionary RecentEvents()
        {
            IDictionary recent =
                RecentEventsField?.GetValue(scope.Component) as IDictionary;
            if (recent == null)
            {
                throw new AssertionException(
                    "DiaryGameComponent.recentEvents was unavailable.");
            }

            return recent;
        }

        private static void RequireSingleProgressedDayRecord(
            IDictionary pending,
            string dayKey,
            string expectedDefName)
        {
            IList records = pending[dayKey] as IList;
            PawnDiaryRimTestScope.Require(
                records != null && records.Count == 1,
                "The worsening Flu did not create exactly one deferred day record.");

            object record = records[0];
            Type recordType = record.GetType();
            FieldInfo defNameField = recordType.GetField(
                "defName", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo progressedField = recordType.GetField(
                "progressed", BindingFlags.Instance | BindingFlags.Public);
            PawnDiaryRimTestScope.Require(
                defNameField != null
                    && progressedField != null
                    && string.Equals(
                        defNameField.GetValue(record) as string,
                        expectedDefName,
                        StringComparison.Ordinal)
                    && (bool)progressedField.GetValue(record),
                "The scanner's deferred day record did not identify a progressed Flu.");
        }

        private static TDef RequireDef<TDef>(string defName) where TDef : Def
        {
            TDef def = DefDatabase<TDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required vanilla " + typeof(TDef).Name + " '" + defName + "' was not loaded.");
            }

            return def;
        }

        private static BodyPartRecord RequireBodyPart(Pawn pawn, string bodyPartDefName)
        {
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part?.def != null
                    && string.Equals(part.def.defName, bodyPartDefName, StringComparison.Ordinal))
                {
                    return part;
                }
            }

            throw new AssertionException(
                "Test pawn is missing a '" + bodyPartDefName + "' body part for the hediff test.");
        }

        private static void RemoveHediffIfPresent(Pawn pawn, Hediff hediff)
        {
            if (pawn?.health?.hediffSet?.hediffs != null
                && hediff != null
                && pawn.health.hediffSet.hediffs.Contains(hediff))
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }
}
