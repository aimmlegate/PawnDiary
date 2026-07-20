// Loaded-game containment-breach coverage for Anomaly A1.3 plus its N3-A pressure lens.
//
// These fixtures create disposable holding platforms and entities, then call vanilla's real
// CompHoldingPlatformTarget.Escape(bool) method. Harmony therefore has to cross the same prefix,
// recursive Escape(false), postfix, and finalizer seams used in play. Every possible writer has diary
// generation disabled, so no LLM request can leave the game. Teardown restores policy Def fields,
// transient caches, vanilla letters, RNG state (through PawnDiaryRimTestScope), the Anomaly component's
// has-built-platform flag, and all spawned things even when an assertion fails.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises the real RimWorld 1.6 containment escape call tree and its silent siblings.</summary>
    [TestSuite]
    public static class PawnDiaryAnomalyContainmentFlowTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Anomaly containment] ";
        private static readonly FieldInfo JoinChanceCurveField = typeof(CompHoldingPlatformTarget)
            .GetField("JoinEscapeChanceFromEscapeIntervalCurve",
                BindingFlags.Static | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static DiaryAnomalyPolicyDef policyDef;
        private static bool originalContainmentEnabled;
        private static int originalWitnessRadius;
        private static int originalMaxWriters;
        private static int originalMaxEntityLabels;
        private static int originalDedupTicks;
        private static int originalRecentStudierTicks;
        private static string originalNarrativeFormat;
        private static PromptContextDetailLevel originalContextDetailLevel;
        private static bool originalHasBuiltPlatform;
        private static List<AnomalyStudyTaleClaim> originalStudyClaims;
        private static List<AnomalyRecentStudyFact> originalRecentStudies;
        private static HashSet<Letter> originalLetters;

        /// <summary>Snapshots every process/global value the containment fixtures may change.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("anomalyContainmentBreach");
            scope.OwnDiaryEventsCreatedAfterThisPoint();
            policyDef = DefDatabase<DiaryAnomalyPolicyDef>.GetNamedSilentFail(
                DiaryAnomalyPolicy.DefName);
            PawnDiaryRimTestScope.Require(policyDef != null,
                "The core Diary_AnomalyPolicy Def was not loaded.");
            originalContainmentEnabled = policyDef.containmentEnabled;
            originalWitnessRadius = policyDef.containmentWitnessRadius;
            originalMaxWriters = policyDef.containmentMaxWriters;
            originalMaxEntityLabels = policyDef.containmentMaxEntityLabelsInContext;
            originalDedupTicks = policyDef.containmentDedupTicks;
            originalRecentStudierTicks = policyDef.recentStudierMaxAgeTicks;
            originalNarrativeFormat = policyDef.containmentBreachNarrativeFormat;
            originalContextDetailLevel = PawnDiaryMod.Settings.contextDetailLevel;
            PawnDiaryMod.Settings.contextDetailLevel = PromptContextDetailLevel.Full;
            originalStudyClaims = AnomalyStudySuppressionCache.SnapshotForTests();
            originalRecentStudies = AnomalyRecentStudyCache.SnapshotForTests();
            originalLetters = new HashSet<Letter>(
                Find.LetterStack?.LettersListForReading ?? new List<Letter>());
            originalHasBuiltPlatform = ModsConfig.AnomalyActive
                && Find.Anomaly != null && Find.Anomaly.hasBuiltHoldingPlatform;
            AnomalyTransientState.Reset();
        }

        /// <summary>Restores policy/cache/runtime state and then lets the common harness audit leaks.</summary>
        [AfterEach]
        public static void TearDown()
        {
            Exception firstFailure = null;
            try
            {
                ContainmentEscapeScopeStack.Clear();
                if (originalStudyClaims != null)
                    AnomalyStudySuppressionCache.RestoreForTests(originalStudyClaims);
                if (originalRecentStudies != null)
                    AnomalyRecentStudyCache.RestoreForTests(originalRecentStudies);
                RestorePolicy();
                RemoveFixtureLetters();
                if (ModsConfig.AnomalyActive && Verse.Current.Game != null && Find.Anomaly != null)
                    Find.Anomaly.hasBuiltHoldingPlatform = originalHasBuiltPlatform;
            }
            catch (Exception exception)
            {
                firstFailure = exception;
            }

            try
            {
                scope?.TearDown();
            }
            catch (Exception exception)
            {
                if (firstFailure == null) firstFailure = exception;
            }
            finally
            {
                originalLetters = null;
                originalRecentStudies = null;
                originalStudyClaims = null;
                policyDef = null;
                scope = null;
            }

            if (firstFailure != null) throw firstFailure;
        }

        /// <summary>Proves off-DLC inertness and exact prefix/postfix/finalizer ownership on-DLC.</summary>
        [Test]
        public static void EscapeHookRegistrationMatchesAnomalyAvailability()
        {
            PawnDiaryRimTestScope.Require(
                DiaryAnomalyPatches.ContainmentHookReady == ModsConfig.AnomalyActive,
                "The containment hook readiness flag did not match Anomaly availability.");

            AnomalyContainmentEscapeCapture nullCapture;
            PawnDiaryRimTestScope.Require(
                !DlcContext.TryCaptureAnomalyContainmentBefore(null, 60, out nullCapture)
                    && nullCapture == null,
                "The guarded containment adapter accepted a null target.");
            if (!ModsConfig.AnomalyActive)
            {
                PawnDiaryRimTestScope.Require(
                    ContainmentEscapeScopeStack.Begin(null, true) == null
                        && ContainmentEscapeScopeStack.ActiveCallDepthForTests == 0
                        && ContainmentEscapeScopeStack.ActiveScopeDepthForTests == 0,
                    "The Anomaly-off containment hook left active scope state.");
                Log.Message(LogPrefix + "exact Escape hook: not applicable (Anomaly inactive). ");
                return;
            }

            MethodInfo target = AccessTools.DeclaredMethod(
                typeof(CompHoldingPlatformTarget), "Escape", new[] { typeof(bool) }) as MethodInfo;
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            PawnDiaryRimTestScope.Require(target != null && target.ReturnType == typeof(void)
                    && target.GetParameters().Length == 1
                    && target.GetParameters()[0].Name == "initiator"
                    && patches != null
                    && patches.Prefixes.Any(OwnedContainmentPatch)
                    && patches.Postfixes.Any(OwnedContainmentPatch)
                    && patches.Finalizers.Any(OwnedContainmentPatch),
                "The exact CompHoldingPlatformTarget.Escape(bool initiator) method lacks Pawn Diary's "
                    + "prefix/postfix/finalizer set.");

            RequireNoContainmentPatch(typeof(Building_HoldingPlatform), "EjectContents", Type.EmptyTypes);
            RequireNoContainmentPatch(typeof(Building_HoldingPlatform), "Notify_PawnDied",
                new[] { typeof(Pawn), typeof(DamageInfo?) });
            RequireNoContainmentPatch(typeof(CompHoldingPlatformTarget), "Notify_HeldOnPlatform",
                new[] { typeof(ThingOwner) });
            RequireNoContainmentPatch(typeof(CompHoldingPlatformTarget), "Notify_ReleasedFromPlatform",
                Type.EmptyTypes);
        }

        /// <summary>One real escape produces one visible, bounded event after ejection is verified.</summary>
        [Test]
        public static void OneRealEscapeCreatesOneBoundedVisibleEvent()
        {
            if (!RequireAnomalyOrReport(nameof(OneRealEscapeCreatesOneBoundedVisibleEvent))) return;
            List<IntVec3> cells = FindCleanRoomCells(2);
            Pawn writer = CreateWriterAt(cells[1]);
            UseOnlyCandidates(writer);
            HeldFixture held = CreateHeldFixture(cells[0]);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => held.target.Escape(initiator: true),
                AnomalyEventDefNames.ContainmentBreach,
                writer,
                null,
                rejectOtherTestPawnEvents: true);

            PawnDiaryRimTestScope.Require(!held.target.CurrentlyHeldOnPlatform
                    && held.target.HeldPlatform == null
                    && held.entity.Spawned && held.entity.Map == Find.CurrentMap
                    && held.target.isEscaping,
                "The real Escape call did not visibly release the exact held entity.");
            RequireContext(page, "escaped_count=1");
            RequireContext(page, "same_room_cascade=false");
            RequireContext(page, "setting=");
            PawnDiaryRimTestScope.Require(
                !page.gameContext.Contains(held.platform.GetUniqueLoadID())
                    && !page.gameContext.Contains(held.platform.Position.ToString()),
                "Bounded context leaked a platform identity or exact position.");
            string expectedFallback = "PawnDiary.Event.Anomaly.Containment.Fallback"
                .Translate(
                    writer.LabelShortCap,
                    DiaryLineCleaner.CleanLine(held.entity.LabelShortCap))
                .Resolve();
            PawnDiaryRimTestScope.Require(string.Equals(
                    page.initiatorText,
                    expectedFallback,
                    StringComparison.Ordinal),
                "Localized containment fallback did not use the exact visible-label-only text.");
            RequireNarrative(page, DiaryEvent.InitiatorRole, held.entity);
            RequireCleanScope();
        }

        /// <summary>Missing or malformed optional prose preserves the canonical page and exact reference.</summary>
        [Test]
        public static void NarrativeFormatFailureKeepsCanonicalContainmentPage()
        {
            if (!RequireAnomalyOrReport(nameof(NarrativeFormatFailureKeepsCanonicalContainmentPage))) return;
            List<IntVec3> cells = FindCleanRoomCells(3);
            Pawn writer = CreateWriterAt(cells[2]);
            UseOnlyCandidates(writer);
            string[] badFormats = { " ", "{1}" };
            for (int i = 0; i < badFormats.Length; i++)
            {
                policyDef.containmentBreachNarrativeFormat = badFormats[i];
                HeldFixture held = CreateHeldFixture(cells[i]);
                DiaryEvent page = scope.FireAndRequireEvent(
                    () => held.target.Escape(initiator: true),
                    AnomalyEventDefNames.ContainmentBreach,
                    writer,
                    null,
                    rejectOtherTestPawnEvents: true);
                List<NarrativeEvidence> evidence =
                    page.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
                PawnDiaryRimTestScope.Require(evidence.Count == 1
                        && evidence[0].subjectId == held.entity.GetUniqueLoadID()
                        && page.NarrativeReferencesForRole(DiaryEvent.InitiatorRole).Count == 1
                        && page.NarrativeSelectedCandidateKeysForRole(
                            DiaryEvent.InitiatorRole).Count == 0
                        && string.IsNullOrWhiteSpace(
                            page.NarrativeContextForRole(DiaryEvent.InitiatorRole)),
                    "A missing/malformed containment format revoked source evidence or invented a lens.");
            }
            RequireCleanScope();
        }

        /// <summary>Vanilla's recursive same-room Escape(false) path aggregates into one capped page.</summary>
        [Test]
        public static void RecursiveSameRoomCascadeProducesOneEvent()
        {
            if (!RequireAnomalyOrReport(nameof(RecursiveSameRoomCascadeProducesOneEvent))) return;
            List<IntVec3> cells = FindCleanRoomCells(3);
            Pawn writer = CreateWriterAt(cells[2]);
            UseOnlyCandidates(writer);
            policyDef.containmentMaxEntityLabelsInContext = 1;
            HeldFixture outer = CreateHeldFixture(cells[0]);
            HeldFixture nested = CreateHeldFixture(cells[1]);
            ForceVanillaCascadeChance();

            DiaryEvent page = scope.FireAndRequireEvent(
                () => outer.target.Escape(initiator: true),
                AnomalyEventDefNames.ContainmentBreach,
                writer,
                null,
                rejectOtherTestPawnEvents: true);

            PawnDiaryRimTestScope.Require(!outer.target.CurrentlyHeldOnPlatform
                    && !nested.target.CurrentlyHeldOnPlatform,
                "The forced vanilla same-room cascade did not release both entities.");
            RequireContext(page, "escaped_count=2");
            RequireContext(page, "additional_escaped_count=1");
            RequireContext(page, "same_room_cascade=true");
            RequireCleanScope();
        }

        /// <summary>Non-Escape release callbacks, platform ejection, and held death never claim a breach.</summary>
        [Test]
        public static void IntentionalAndDeathReleasePathsRemainSilent()
        {
            if (!RequireAnomalyOrReport(nameof(IntentionalAndDeathReleasePathsRemainSilent))) return;
            List<IntVec3> cells = FindCleanRoomCells(3);
            Pawn writer = CreateWriterAt(cells[2]);
            UseOnlyCandidates(writer);

            HeldFixture intentional = CreateHeldFixture(cells[0]);
            scope.RequireNoNewEvent(() => intentional.target.Notify_ReleasedFromPlatform());
            PawnDiaryRimTestScope.Require(intentional.target.CurrentlyHeldOnPlatform,
                "Notify_ReleasedFromPlatform unexpectedly ejected the fixture entity.");
            scope.RequireNoNewEvent(() => intentional.platform.EjectContents());
            PawnDiaryRimTestScope.Require(!intentional.target.CurrentlyHeldOnPlatform
                    && !intentional.target.isEscaping,
                "Intentional EjectContents changed release state into an escape.");

            HeldFixture death = CreateHeldFixture(cells[1]);
            scope.RequireNoNewEvent(() => death.platform.Notify_PawnDied(death.entity, null));
            PawnDiaryRimTestScope.Require(!death.target.CurrentlyHeldOnPlatform
                    && !death.target.isEscaping,
                "The held-death release path was mistaken for an Escape call.");
            RequireCleanScope();
        }

        /// <summary>Both XML gates and an empty writer set drop output without changing vanilla escape.</summary>
        [Test]
        public static void DisabledGatesAndNoEligibleWriterRemainSilent()
        {
            if (!RequireAnomalyOrReport(nameof(DisabledGatesAndNoEligibleWriterRemainSilent))) return;
            List<IntVec3> cells = FindCleanRoomCells(4);
            Pawn writer = CreateWriterAt(cells[3]);
            UseOnlyCandidates(writer);

            HeldFixture groupDisabled = CreateHeldFixture(cells[0]);
            PawnDiaryMod.Settings.SetGroupEnabled("anomalyContainmentBreach", false);
            scope.RequireNoNewEvent(() => groupDisabled.target.Escape(initiator: true));
            PawnDiaryRimTestScope.Require(!groupDisabled.target.CurrentlyHeldOnPlatform,
                "Disabling the group interfered with vanilla escape.");

            PawnDiaryMod.Settings.SetGroupEnabled("anomalyContainmentBreach", true);
            policyDef.containmentEnabled = false;
            HeldFixture policyDisabled = CreateHeldFixture(cells[1]);
            scope.RequireNoNewEvent(() => policyDisabled.target.Escape(initiator: true));
            PawnDiaryRimTestScope.Require(!policyDisabled.target.CurrentlyHeldOnPlatform,
                "Disabling containment policy interfered with vanilla escape.");

            policyDef.containmentEnabled = true;
            ContainmentEscapeScopeStack.SetCandidateFilterForTests(candidate => false);
            HeldFixture noWriter = CreateHeldFixture(cells[2]);
            scope.RequireNoNewEvent(() => noWriter.target.Escape(initiator: true));
            PawnDiaryRimTestScope.Require(!noWriter.target.CurrentlyHeldOnPlatform,
                "An empty writer set interfered with vanilla escape.");
            RequireCleanScope();
        }

        /// <summary>Nearby, recent exact studier, radius, two-writer cap, and POV roles stay deterministic.</summary>
        [Test]
        public static void LoadedWriterOrderingUsesNearbyThenRecentStudier()
        {
            if (!RequireAnomalyOrReport(nameof(LoadedWriterOrderingUsesNearbyThenRecentStudier))) return;
            IntVec3 platformCell = FindCleanRoomCells(1)[0];
            HeldFixture held = CreateHeldFixture(platformCell);
            Room room = held.platform.GetRoom();
            IntVec3 nearbyCell = FindPawnCellAtDistance(
                room, platformCell, 4, 4, new HashSet<IntVec3>());
            HashSet<IntVec3> used = new HashSet<IntVec3> { platformCell, nearbyCell };
            IntVec3 recentCell = FindPawnCellAtDistance(
                room, platformCell, 9, int.MaxValue, used);
            used.Add(recentCell);
            IntVec3 fallbackCell = FindPawnCellAtDistance(
                room, platformCell, 9, int.MaxValue, used);

            Pawn nearby = CreateWriterAt(nearbyCell);
            Pawn recent = CreateWriterAt(recentCell);
            Pawn fallback = CreateWriterAt(fallbackCell);
            UseOnlyCandidates(nearby, recent, fallback);
            policyDef.containmentWitnessRadius = 2;
            policyDef.containmentMaxWriters = 2;
            int tick = Find.TickManager?.TicksGame ?? 0;
            PawnDiaryRimTestScope.Require(AnomalyRecentStudyCache.Register(
                    new AnomalyRecentStudyFact
                    {
                        studierPawnId = recent.GetUniqueLoadID(),
                        studiedEntityId = held.entity.GetUniqueLoadID(),
                        studiedDefName = held.entity.def.defName,
                        studiedTick = tick
                    },
                    tick,
                    policyDef.recentStudierMaxAgeTicks),
                "Could not seed exact recent-study evidence for the loaded writer fixture.");

            DiaryEvent page = scope.FireAndRequireEvent(
                () => held.target.Escape(initiator: true),
                AnomalyEventDefNames.ContainmentBreach,
                nearby,
                recent,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, nearby, recent);
            RequireContext(page, "initiator_witness_role=nearby");
            RequireContext(page, "recipient_witness_role=recent_studier");
            RequireNarrative(page, DiaryEvent.InitiatorRole, held.entity);
            RequireNarrative(page, DiaryEvent.RecipientRole, held.entity);
            PawnDiaryRimTestScope.Require(
                page.initiatorPawnId != fallback.GetUniqueLoadID()
                    && page.recipientPawnId != fallback.GetUniqueLoadID(),
                "The two-writer cap admitted a lower-priority colony fallback.");
            RequireCleanScope();
        }

        /// <summary>Only the exact same map/tick/outer-entity key deduplicates; a distinct entity survives.</summary>
        [Test]
        public static void ExactDedupSuppressesOnlyTheSameOuterEscape()
        {
            if (!RequireAnomalyOrReport(nameof(ExactDedupSuppressesOnlyTheSameOuterEscape))) return;
            List<IntVec3> cells = FindCleanRoomCells(3);
            Pawn writer = CreateWriterAt(cells[1]);
            UseOnlyCandidates(writer);
            HeldFixture first = CreateHeldFixture(cells[0]);

            scope.FireAndRequireEvent(
                () => first.target.Escape(initiator: true),
                AnomalyEventDefNames.ContainmentBreach,
                writer,
                null,
                rejectOtherTestPawnEvents: true);

            Rehold(first);
            scope.RequireNoNewEvent(() => first.target.Escape(initiator: true));

            HeldFixture distinct = CreateHeldFixture(cells[2]);
            scope.FireAndRequireEvent(
                () => distinct.target.Escape(initiator: true),
                AnomalyEventDefNames.ContainmentBreach,
                writer,
                null,
                rejectOtherTestPawnEvents: true);
            RequireCleanScope();
        }

        /// <summary>A vanilla exception runs the Harmony finalizer and cannot poison the next escape.</summary>
        [Test]
        public static void EscapeExceptionFinalizerCleansEveryScope()
        {
            if (!RequireAnomalyOrReport(nameof(EscapeExceptionFinalizerCleansEveryScope))) return;
            List<IntVec3> cells = FindCleanRoomCells(2);
            Pawn writer = CreateWriterAt(cells[1]);
            UseOnlyCandidates(writer);
            HeldFixture held = CreateHeldFixture(cells[0]);
            ThingOwner owner = held.platform.innerContainer;
            bool threw = false;

            scope.RequireNoNewEvent(() =>
            {
                try
                {
                    held.platform.innerContainer = null;
                    held.target.Escape(initiator: true);
                }
                catch
                {
                    threw = true;
                }
                finally
                {
                    held.platform.innerContainer = owner;
                }
            });
            PawnDiaryRimTestScope.Require(threw,
                "The deliberately invalid vanilla container did not reach the Escape finalizer path.");
            PawnDiaryRimTestScope.Require(held.target.CurrentlyHeldOnPlatform
                    && held.platform.HeldPawn == held.entity,
                "The exception fixture no longer held the entity, so a successful retry would not "
                    + "prove finalizer recovery.");
            RequireCleanScope();

            DiaryEvent recovered = scope.FireAndRequireEvent(
                () => held.target.Escape(initiator: true),
                AnomalyEventDefNames.ContainmentBreach,
                writer,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(recovered, "escaped_count=1");
            RequireCleanScope();
        }

        /// <summary>Repeated and out-of-order closes cannot leak or revive a containment scope.</summary>
        [Test]
        public static void ScopeClosureIsIdempotentAndRejectsAnUnhealthyOuterClose()
        {
            if (!RequireAnomalyOrReport(
                nameof(ScopeClosureIsIdempotentAndRejectsAnUnhealthyOuterClose))) return;
            HeldFixture held = CreateHeldFixture(FindCleanRoomCells(1)[0]);

            ContainmentEscapeCallState aborted =
                ContainmentEscapeScopeStack.Begin(held.target, initiator: true);
            PawnDiaryRimTestScope.Require(aborted != null,
                "Could not open the direct abort fixture scope.");
            ContainmentEscapeScopeStack.Abort(aborted);
            ContainmentEscapeScopeStack.Abort(aborted);
            PawnDiaryRimTestScope.Require(
                ContainmentEscapeScopeStack.Complete(aborted) == null,
                "A completed abort frame was revived by a later close.");
            RequireCleanScope();

            ContainmentEscapeCallState outer =
                ContainmentEscapeScopeStack.Begin(held.target, initiator: true);
            ContainmentEscapeCallState nested =
                ContainmentEscapeScopeStack.Begin(held.target, initiator: false);
            PawnDiaryRimTestScope.Require(outer != null && nested != null,
                "Could not open the direct unhealthy-close fixture frames.");
            PawnDiaryRimTestScope.Require(
                ContainmentEscapeScopeStack.Complete(outer) == null && nested.completed,
                "Closing an outer frame ahead of its descendant did not reject and retire the scope.");
            ContainmentEscapeScopeStack.Abort(nested);
            RequireCleanScope();
        }

        /// <summary>New-game/load cleanup clears both detached study evidence and unfinished frames.</summary>
        [Test]
        public static void AnomalyLifecycleResetClearsContainmentAndStudyCaches()
        {
            if (!RequireAnomalyOrReport(nameof(AnomalyLifecycleResetClearsContainmentAndStudyCaches))) return;
            List<IntVec3> cells = FindCleanRoomCells(1);
            HeldFixture held = CreateHeldFixture(cells[0]);
            int tick = Find.TickManager?.TicksGame ?? 0;
            PawnDiaryRimTestScope.Require(AnomalyRecentStudyCache.Register(
                    new AnomalyRecentStudyFact
                    {
                        studierPawnId = "Pawn_TestRecentStudier",
                        studiedEntityId = held.entity.GetUniqueLoadID(),
                        studiedTick = tick
                    },
                    tick,
                    60),
                "Could not seed lifecycle recent-study evidence.");
            PawnDiaryRimTestScope.Require(
                ContainmentEscapeScopeStack.Begin(held.target, true) != null,
                "Could not open the fixture containment scope before lifecycle reset.");

            AnomalyTransientState.Reset();
            PawnDiaryRimTestScope.Require(AnomalyRecentStudyCache.CountForTests == 0
                    && AnomalyStudySuppressionCache.CountForTests == 0
                    && ContainmentEscapeScopeStack.ActiveCallDepthForTests == 0
                    && ContainmentEscapeScopeStack.ActiveScopeDepthForTests == 0,
                "The Anomaly lifecycle reset leaked a recent study or containment frame.");
        }

        private static bool OwnedContainmentPatch(Patch patch)
        {
            return patch.owner == "aimml.pawndiary"
                && patch.PatchMethod?.DeclaringType == typeof(DiaryAnomalyPatches);
        }

        private static void RequireNoContainmentPatch(Type type, string name, Type[] parameters)
        {
            MethodBase method = AccessTools.DeclaredMethod(type, name, parameters);
            Patches patches = method == null ? null : Harmony.GetPatchInfo(method);
            bool owned = patches != null && (patches.Prefixes.Any(OwnedContainmentPatch)
                || patches.Postfixes.Any(OwnedContainmentPatch)
                || patches.Finalizers.Any(OwnedContainmentPatch));
            PawnDiaryRimTestScope.Require(method != null && !owned,
                "Pawn Diary must not patch non-breach release path " + type.Name + "." + name + ".");
        }

        private static bool RequireAnomalyOrReport(string testName)
        {
            if (ModsConfig.AnomalyActive) return true;
            PawnDiaryRimTestScope.Require(!DiaryAnomalyPatches.ContainmentHookReady,
                "Containment hook remained ready without Anomaly.");
            Log.Message(LogPrefix + testName + ": not applicable (Anomaly inactive). ");
            return false;
        }

        private static HeldFixture CreateHeldFixture(IntVec3 cell)
        {
            Map map = Find.CurrentMap;
            ThingDef platformDef = FindHoldingPlatformDef();
            PawnKindDef entityKind = DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def?.race != null
                    && def.race.GetCompProperties<CompProperties_HoldingPlatformTarget>() != null)
                .OrderBy(def => def.defName, StringComparer.Ordinal)
                .FirstOrDefault();
            PawnDiaryRimTestScope.Require(platformDef != null && entityKind != null,
                "Anomaly is active but no loaded holding-platform/entity fixture Def was found.");

            Building_HoldingPlatform platform = ThingMaker.MakeThing(platformDef)
                as Building_HoldingPlatform;
            Pawn entity = scope.CreateTrackedPawn(entityKind, Faction.OfEntities);
            CompHoldingPlatformTarget target = entity.TryGetComp<CompHoldingPlatformTarget>();
            PawnDiaryRimTestScope.Require(platform != null && target != null,
                "The loaded Anomaly fixture did not construct the expected platform/target comp.");
            Thing spawned = GenSpawn.Spawn(platform, cell, map);
            PawnDiaryRimTestScope.Require(ReferenceEquals(spawned, platform) && platform.Spawned,
                "The disposable holding platform did not spawn at its prevalidated anchor.");
            scope.RegisterCleanup(() => DestroyPlatform(platform));
            PawnDiaryRimTestScope.Require(platform.innerContainer.TryAdd(entity)
                    && target.CurrentlyHeldOnPlatform && target.HeldPlatform == platform,
                "The disposable entity was not held by the spawned platform.");
            return new HeldFixture
            {
                platform = platform,
                entity = entity,
                target = target
            };
        }

        private static Pawn CreateWriterAt(IntVec3 cell)
        {
            Pawn writer = scope.CreateAdultColonist();
            GenSpawn.Spawn(writer, cell, Find.CurrentMap);
            return writer;
        }

        private static void UseOnlyCandidates(params Pawn[] pawns)
        {
            HashSet<string> ids = new HashSet<string>(
                pawns.Where(pawn => pawn != null).Select(pawn => pawn.GetUniqueLoadID()),
                StringComparer.Ordinal);
            ContainmentEscapeScopeStack.SetCandidateFilterForTests(
                candidate => candidate != null && ids.Contains(candidate.pawnId));
        }

        private static void Rehold(HeldFixture held)
        {
            if (held.entity.Spawned) held.entity.DeSpawn();
            held.target.isEscaping = false;
            PawnDiaryRimTestScope.Require(held.platform.innerContainer.TryAdd(held.entity)
                    && held.target.CurrentlyHeldOnPlatform,
                "Could not rehold the exact entity for the dedup fixture.");
        }

        private static void ForceVanillaCascadeChance()
        {
            SimpleCurve curve = JoinChanceCurveField?.GetValue(null) as SimpleCurve;
            PawnDiaryRimTestScope.Require(curve != null,
                "Could not locate vanilla's same-room escape join curve.");
            List<CurvePoint> original = new List<CurvePoint>(curve.Points);
            scope.RegisterCleanup(() => curve.SetPoints(original));
            curve.SetPoints(new[] { new CurvePoint(-100000f, 1f), new CurvePoint(100000f, 1f) });
        }

        private static List<IntVec3> FindCleanRoomCells(int count)
        {
            Map map = Find.CurrentMap;
            PawnDiaryRimTestScope.Require(map != null,
                "Containment RimTests require a loaded map.");
            ThingDef platformDef = FindHoldingPlatformDef();
            PawnDiaryRimTestScope.Require(platformDef != null,
                "Anomaly is active but no loaded holding-platform fixture Def was found.");
            foreach (IntVec3 seed in map.AllCells)
            {
                Room room = seed.GetRoom(map);
                // Vanilla Escape(bool) needs a Room, but it does not require that room to be
                // indoors. Open terrain has a real Room too. Accepting it keeps these fixtures
                // independent of player-built structures while the occupied-platform guard below
                // prevents the forced cascade from touching an existing contained entity.
                if (room == null || RoomHasOccupiedPlatform(room))
                    continue;

                // Callers may use any returned cell as a 3x3 platform anchor. Reserve a one-cell
                // margin around each full footprint so simultaneous fixtures cannot overlap or wipe
                // one another. This also prevents the edge-cell out-of-bounds failure that a
                // center-only check misses.
                List<IntVec3> cells = new List<IntVec3>();
                List<CellRect> reserved = new List<CellRect>();
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(seed, 14f, true))
                {
                    CellRect footprint;
                    if (!TryGetCleanPlatformFootprint(cell, room, map, platformDef, out footprint))
                        continue;
                    CellRect padded = footprint.ExpandedBy(1);
                    if (!padded.InBounds(map) || reserved.Any(rect => rect.Overlaps(padded)))
                        continue;
                    cells.Add(cell);
                    reserved.Add(padded);
                    if (cells.Count >= count) return cells;
                }
            }

            throw new AssertionException(
                "Containment RimTests need one clean room with space for " + count
                    + " non-overlapping 3x3 holding-platform footprint(s) and no occupied platform.");
        }

        private static bool TryGetCleanPlatformFootprint(
            IntVec3 center,
            Room room,
            Map map,
            ThingDef platformDef,
            out CellRect footprint)
        {
            footprint = GenAdj.OccupiedRect(center, Rot4.North, platformDef.Size);
            if (!footprint.InBounds(map)) return false;
            foreach (IntVec3 cell in footprint)
            {
                if (cell.GetRoom(map) != room || !cell.Standable(map) || cell.Fogged(map)
                    || cell.GetEdifice(map) != null || cell.GetFirstPawn(map) != null)
                {
                    return false;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing is Pawn || GenSpawn.SpawningWipes(platformDef, thing.def)) return false;
                }
            }
            return true;
        }

        private static ThingDef FindHoldingPlatformDef()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def?.thingClass != null
                    && typeof(Building_HoldingPlatform).IsAssignableFrom(def.thingClass))
                .OrderBy(def => def.defName, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static bool RoomHasOccupiedPlatform(Room room)
        {
            return room.ContainedAndAdjacentThings.Any(thing =>
                thing is Building_HoldingPlatform platform && platform.Occupied);
        }

        private static IntVec3 FindPawnCellAtDistance(
            Room room,
            IntVec3 origin,
            int minimumSquared,
            int maximumSquared,
            HashSet<IntVec3> used)
        {
            Map map = Find.CurrentMap;
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(origin, 14f, true))
            {
                int distance = (candidate - origin).LengthHorizontalSquared;
                if (candidate.InBounds(map) && candidate.GetRoom(map) == room
                    && candidate.Standable(map) && !candidate.Fogged(map)
                    && candidate.GetEdifice(map) == null && candidate.GetFirstPawn(map) == null
                    && !used.Contains(candidate) && distance >= minimumSquared
                    && distance <= maximumSquared)
                {
                    return candidate;
                }
            }

            throw new AssertionException(
                "The fixture room did not contain a clean pawn cell in the required distance band.");
        }

        private static void RequireContext(DiaryEvent page, string token)
        {
            PawnDiaryRimTestScope.Require(page != null
                    && (page.gameContext ?? string.Empty).Contains(token),
                "Containment event context omitted exact token '" + token + "'.");
        }

        private static void RequireNarrative(DiaryEvent page, string role, Pawn escapedEntity)
        {
            string entityId = escapedEntity.GetUniqueLoadID();
            List<NarrativeEvidence> evidence = page.NarrativeEvidenceForRole(role);
            PawnDiaryRimTestScope.Require(evidence.Count == 1
                    && evidence[0].facet == NarrativeFacetTokens.AmbientPressure
                    && evidence[0].phase == AnomalyNarrativeContinuityTokens.Breached
                    && evidence[0].subjectKind == NarrativeSubjectKindTokens.Entity
                    && evidence[0].subjectId == entityId
                    && evidence[0].arcKey.StartsWith("anomaly-breach|", StringComparison.Ordinal)
                    && evidence[0].arcKey.EndsWith("|" + entityId, StringComparison.Ordinal)
                    && evidence[0].sourceDomain
                        == AnomalyNarrativeContinuityTokens.ContainmentSourceDomain
                    && evidence[0].sourceDefName
                        == AnomalyNarrativeContinuityTokens.ContainmentSourceDefName
                    && evidence[0].pawnCanKnow == true,
                "Containment N3-A evidence was not exact for role '" + role + "'.");
            string expectedKey = "anomaly|pressure|breach|"
                + evidence[0].arcKey.Substring("anomaly-breach|".Length);
            PawnDiaryRimTestScope.Require(
                page.NarrativeSelectedCandidateKeysForRole(role).Contains(expectedKey)
                    && !string.IsNullOrWhiteSpace(page.NarrativeContextForRole(role))
                    && page.NarrativeReferencesForRole(role).Exists(reference => reference != null
                        && reference.facet == NarrativeFacetTokens.AmbientPressure
                        && reference.subjectId == entityId
                        && reference.arcKey == evidence[0].arcKey),
                "Containment N3-A did not freeze its stable pressure lens for role '" + role + "'.");
        }

        private static void RequireCleanScope()
        {
            PawnDiaryRimTestScope.Require(
                ContainmentEscapeScopeStack.ActiveCallDepthForTests == 0
                    && ContainmentEscapeScopeStack.ActiveScopeDepthForTests == 0,
                "Containment capture leaked a call frame or outer scope.");
        }

        private static void DestroyPlatform(Building_HoldingPlatform platform)
        {
            if (platform == null || platform.Destroyed) return;
            if (platform.innerContainer != null && platform.Occupied) platform.EjectContents();
            platform.Destroy(DestroyMode.Vanish);
        }

        private static void RestorePolicy()
        {
            if (policyDef == null) return;
            policyDef.containmentEnabled = originalContainmentEnabled;
            policyDef.containmentWitnessRadius = originalWitnessRadius;
            policyDef.containmentMaxWriters = originalMaxWriters;
            policyDef.containmentMaxEntityLabelsInContext = originalMaxEntityLabels;
            policyDef.containmentDedupTicks = originalDedupTicks;
            policyDef.recentStudierMaxAgeTicks = originalRecentStudierTicks;
            policyDef.containmentBreachNarrativeFormat = originalNarrativeFormat;
            if (PawnDiaryMod.Settings != null)
                PawnDiaryMod.Settings.contextDetailLevel = originalContextDetailLevel;
        }

        private static void RemoveFixtureLetters()
        {
            if (Verse.Current.Game == null || originalLetters == null) return;
            List<Letter> letters = Find.LetterStack?.LettersListForReading;
            if (letters == null) return;
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                Letter letter = letters[i];
                if (!originalLetters.Contains(letter)) Find.LetterStack.RemoveLetter(letter);
            }
        }

        private sealed class HeldFixture
        {
            public Building_HoldingPlatform platform;
            public Pawn entity;
            public CompHoldingPlatformTarget target;
        }
    }
}
