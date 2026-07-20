// Loaded-game Anomaly A2.2 fixtures. They invoke vanilla's exact GhoulInfusion worker on disposable
// pawns, keep every possible writer generation-disabled, and verify transformation truth, exact POVs,
// synchronous DidSurgery ownership, exception cleanup, save silence, and ordinary Tale fallback.
using System;
using System.Collections;
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
    /// <summary>Exercises the exact A2.2 non-ghoul-to-ghoul transformation boundary.</summary>
    [TestSuite]
    public static class PawnDiaryGhoulTransformationFlowTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Anomaly ghoul] ";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo PrefixMethod = typeof(DiaryAnomalyPatches).GetMethod(
            "GhoulInfusionPrefix", PrivateInstance | BindingFlags.Static);
        private static readonly MethodInfo FinalizerMethod = typeof(DiaryAnomalyPatches).GetMethod(
            "GhoulInfusionFinalizer", PrivateInstance | BindingFlags.Static);
        private static readonly MethodInfo ExposeAnomalyDataMethod =
            typeof(DiaryGameComponent).GetMethod("ExposeAnomalyData", PrivateInstance);
        private static readonly MethodInfo BootstrapAnomalyMethod =
            typeof(DiaryGameComponent).GetMethod("BootstrapAnomalyForLoadedSave", PrivateInstance);
        private static readonly FieldInfo PendingTaleBatchesField =
            typeof(DiaryGameComponent).GetField("pendingTaleBatches", PrivateInstance);
        private static readonly FieldInfo TalesListField = ResolveTalesListField();

        private static PawnDiaryRimTestScope scope;
        private static DiaryAnomalyPolicyDef policyDef;
        private static bool originalEnabled;
        private static int originalMaxWriters;
        private static HashSet<Tale> originalTales;
        private static DiaryGameComponent scribeTarget;

        /// <summary>Snapshots shared policy/Tale state and enables only the required groups.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                "anomalyGhoulTransformation", "talehealth", "talecombat");
            scope.OwnDiaryEventsCreatedAfterThisPoint();
            policyDef = DefDatabase<DiaryAnomalyPolicyDef>.GetNamedSilentFail(
                DiaryAnomalyPolicy.DefName);
            PawnDiaryRimTestScope.Require(policyDef != null,
                "The Anomaly narrative policy Def was not loaded.");
            originalEnabled = policyDef.ghoulTransformationEnabled;
            originalMaxWriters = policyDef.ghoulTransformationMaxWriters;
            originalTales = SnapshotTales();
            policyDef.ghoulTransformationEnabled = true;
            policyDef.ghoulTransformationMaxWriters =
                AnomalyPolicyLimits.MaximumGhoulTransformationWriters;
            AnomalyTransientState.Reset();
        }

        /// <summary>Restores policy, synchronous scopes, vanilla Tales, and disposable pawns.</summary>
        [AfterEach]
        public static void TearDown()
        {
            Exception firstFailure = null;
            try
            {
                AnomalyTransientState.Reset();
                if (policyDef != null)
                {
                    policyDef.ghoulTransformationEnabled = originalEnabled;
                    policyDef.ghoulTransformationMaxWriters = originalMaxWriters;
                }
                RemoveFixtureTales();
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
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                scribeTarget = null;
                originalTales = null;
                policyDef = null;
                scope = null;
            }
            if (firstFailure != null) throw firstFailure;
        }

        /// <summary>Proves off-DLC inertness and the exact installed 1.6 signature/patch set.</summary>
        [Test]
        public static void ExactRegistrationAndNoDlcGate()
        {
            bool active = ModsConfig.AnomalyActive;
            PawnDiaryRimTestScope.Require(
                DiaryAnomalyPatches.GhoulTransformationHookReady == active,
                "Ghoul transformation hook readiness did not match Anomaly availability.");
            PawnDiaryRimTestScope.Require(!DlcContext.IsGhoul(null),
                "The guarded ghoul accessor did not fail closed for a missing pawn.");
            if (!active)
            {
                Log.Message(LogPrefix + "exact infusion hook: not applicable (Anomaly inactive).");
                return;
            }

            MethodInfo method = AccessTools.DeclaredMethod(
                typeof(Recipe_GhoulInfusion),
                "ApplyOnPawn",
                new[]
                {
                    typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn),
                    typeof(List<Thing>), typeof(Bill)
                }) as MethodInfo;
            Patches patches = method == null ? null : Harmony.GetPatchInfo(method);
            string[] names = method?.GetParameters().Select(parameter => parameter.Name).ToArray();
            PawnDiaryRimTestScope.Require(method != null && method.IsPublic
                    && method.ReturnType == typeof(void)
                    && names != null
                    && names.SequenceEqual(new[] { "pawn", "part", "billDoer", "ingredients", "bill" })
                    && patches != null
                    && patches.Prefixes.Any(OwnedPatch)
                    && patches.Postfixes.Any(OwnedPatch)
                    && patches.Finalizers.Any(OwnedPatch),
                "The exact Recipe_GhoulInfusion.ApplyOnPawn signature lacks Pawn Diary's patch set.");
        }

        /// <summary>A real successful infusion creates one surgeon-first/subject-second page.</summary>
        [Test]
        public static void SuccessfulInfusionCreatesExactlyOneDedicatedPair()
        {
            if (!RequireAnomalyOrReport(nameof(SuccessfulInfusionCreatesExactlyOneDedicatedPair))) return;
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateEligiblePawn();

            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunInfusion(subject, surgeon, forceFailure: false),
                AnomalyEventDefNames.GhoulTransformation,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, surgeon, subject);
            RequireContext(page, "anomaly_kind=ghoul_transformation");
            RequireContext(page, "transformation=ghoul");
            RequireContext(page, "irreversible_choice=true");
            RequireContext(page, "initiator_witness_role=surgeon");
            RequireContext(page, "recipient_witness_role=subject");
            RequireContext(page, "ghoul_surgeon_id=" + surgeon.GetUniqueLoadID());
            RequireContext(page, "ghoul_subject_id=" + subject.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(page.initiatorText)
                    && !string.IsNullOrWhiteSpace(page.recipientText)
                    && page.initiatorText.Contains(surgeon.LabelShortCap)
                    && page.initiatorText.Contains(subject.LabelShortCap)
                    && page.recipientText.Contains(surgeon.LabelShortCap)
                    && page.recipientText.Contains(subject.LabelShortCap),
                "The localized ghoul fallbacks lost an exact visible role label.");
            PawnDiaryRimTestScope.Require(DlcContext.IsGhoul(subject),
                "Vanilla returned without committing the ghoul transformation.");
        }

        /// <summary>A real forced surgery failure leaves the subject non-ghoul and page-silent.</summary>
        [Test]
        public static void FailedSurgeryCannotManufactureTransformation()
        {
            if (!RequireAnomalyOrReport(nameof(FailedSurgeryCannotManufactureTransformation))) return;
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateEligiblePawn();
            scope.RequireNoNewEvent(() => RunInfusion(subject, surgeon, forceFailure: true));
            PawnDiaryRimTestScope.Require(!DlcContext.IsGhoul(subject)
                    && GhoulInfusionScope.CountForTests == 0,
                "A failed surgery transformed the subject or leaked its ownership scope.");
        }

        /// <summary>An already-ghoul call cannot become A2.2 and releases ordinary DidSurgery.</summary>
        [Test]
        public static void AlreadyGhoulCallKeepsGenericSurgeryTale()
        {
            if (!RequireAnomalyOrReport(nameof(AlreadyGhoulCallKeepsGenericSurgeryTale))) return;
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateEligiblePawn();
            MutantUtility.SetPawnAsMutantInstantly(subject, MutantDefOf.Ghoul);
            PawnDiaryRimTestScope.Require(DlcContext.IsGhoul(subject),
                "The already-ghoul fixture did not establish vanilla ghoul state.");

            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunInfusion(subject, surgeon, forceFailure: false),
                AnomalySurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                surgeon,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, surgeon);
        }

        /// <summary>An ineligible future ghoul cannot write; the exact eligible surgeon writes solo.</summary>
        [Test]
        public static void IneligibleSubjectUsesSurgeonOnly()
        {
            if (!RequireAnomalyOrReport(nameof(IneligibleSubjectUsesSurgeonOnly))) return;
            Pawn subject = CreateIneligiblePawn();
            Pawn surgeon = CreateEligiblePawn();
            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunInfusion(subject, surgeon, forceFailure: false),
                AnomalyEventDefNames.GhoulTransformation,
                surgeon,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, surgeon);
            RequireContext(page, "witness_role=surgeon");
        }

        /// <summary>An ineligible surgeon cannot write; captured pre-state authorizes the subject solo.</summary>
        [Test]
        public static void IneligibleSurgeonUsesSubjectOnly()
        {
            if (!RequireAnomalyOrReport(nameof(IneligibleSurgeonUsesSubjectOnly))) return;
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateIneligiblePawn();
            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunInfusion(subject, surgeon, forceFailure: false),
                AnomalyEventDefNames.GhoulTransformation,
                subject,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, subject);
            RequireContext(page, "witness_role=subject");
        }

        /// <summary>Disabled specialized output releases the unchanged generic Tale after mutation.</summary>
        [Test]
        public static void DisabledOutputKeepsGenericSurgeryTale()
        {
            if (!RequireAnomalyOrReport(nameof(DisabledOutputKeepsGenericSurgeryTale))) return;
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateEligiblePawn();
            policyDef.ghoulTransformationEnabled = false;
            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunInfusion(subject, surgeon, forceFailure: false),
                AnomalySurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                surgeon,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, surgeon);
            PawnDiaryRimTestScope.Require(DlcContext.IsGhoul(subject),
                "Disabled diary output changed vanilla's successful transformation.");
        }

        /// <summary>An exceptional unwind releases the deferred Tale and removes its frame.</summary>
        [Test]
        public static void FinalizerFailsOpenAndClearsScope()
        {
            if (!RequireAnomalyOrReport(nameof(FinalizerFailsOpenAndClearsScope))) return;
            PawnDiaryRimTestScope.Require(PrefixMethod != null && FinalizerMethod != null,
                "Could not resolve Pawn Diary's ghoul prefix/finalizer callbacks.");
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateEligiblePawn();
            TaleDef didSurgery = DefDatabase<TaleDef>.GetNamedSilentFail(
                AnomalySurgeryTaleOwnershipPolicy.DidSurgeryDefName);
            Exception original = new InvalidOperationException("A2.2 finalizer fixture");

            DiaryEvent page = scope.FireAndRequireEvent(() =>
            {
                object[] prefixArgs = { subject, surgeon, null };
                PrefixMethod.Invoke(null, prefixArgs);
                GhoulTransformationCapture capture = prefixArgs[2] as GhoulTransformationCapture;
                PawnDiaryRimTestScope.Require(capture != null,
                    "The exceptional fixture did not open an exact ghoul scope.");
                // Model an exception after vanilla committed the mutation and Tale but before the
                // outer call could return. The finalizer must release that already-parked signal.
                MutantUtility.SetPawnAsMutantInstantly(subject, MutantDefOf.Ghoul);
                TaleRecorder.RecordTale(didSurgery, surgeon, subject);
                PawnDiaryRimTestScope.Require(GhoulInfusionScope.HasDeferredTaleForTests,
                    "The exceptional fixture did not park its exact DidSurgery signal.");
                object returned = FinalizerMethod.Invoke(null, new object[] { original, capture });
                PawnDiaryRimTestScope.Require(ReferenceEquals(returned, original),
                    "The finalizer did not preserve vanilla's original exception.");
            }, AnomalySurgeryTaleOwnershipPolicy.DidSurgeryDefName, surgeon, null, true);
            // The deferred generic Tale was captured after vanilla changed the subject into a ghoul.
            // Its unchanged ordinary eligibility rules therefore retain only the surgeon's POV.
            scope.RequireSoloRef(page, surgeon);
            PawnDiaryRimTestScope.Require(GhoulInfusionScope.CountForTests == 0,
                "The ghoul finalizer leaked a synchronous frame.");
        }

        /// <summary>An ordinary unscoped DidSurgery Tale never enters A2.2 ownership.</summary>
        [Test]
        public static void UnscopedDidSurgeryUsesGenericRoute()
        {
            Pawn surgeon = CreateEligiblePawn();
            Pawn subject = CreateEligiblePawn();
            TaleDef didSurgery = DefDatabase<TaleDef>.GetNamedSilentFail(
                AnomalySurgeryTaleOwnershipPolicy.DidSurgeryDefName);
            DiaryEvent page = scope.FireAndRequireEvent(
                () => TaleRecorder.RecordTale(didSurgery, surgeon, subject),
                AnomalySurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, surgeon, subject);
        }

        /// <summary>Lifecycle reset clears A2.2 and the two Anomaly surgery scopes cannot overlap.</summary>
        [Test]
        public static void LifecycleResetAndA21MutualExclusion()
        {
            AnomalyTransientState.Reset();
            GhoulTransformationCapture ghoul = DummyGhoulCapture();
            CreepJoinerSurgicalInspectionCapture creep = DummyCreepCapture();
            PawnDiaryRimTestScope.Require(GhoulInfusionScope.Begin(ghoul, 4)
                    && !CreepJoinerSurgicalInspectionScope.Begin(creep, 4),
                "A2.1 incorrectly overlapped an active A2.2 Tale owner.");
            AnomalyTransientState.Reset();
            ghoul = DummyGhoulCapture();
            creep = DummyCreepCapture();
            PawnDiaryRimTestScope.Require(CreepJoinerSurgicalInspectionScope.Begin(creep, 4)
                    && !GhoulInfusionScope.Begin(ghoul, 4),
                "A2.2 incorrectly overlapped an active A2.1 Tale owner.");
            AnomalyTransientState.Reset();
            PawnDiaryRimTestScope.Require(GhoulInfusionScope.CountForTests == 0
                    && CreepJoinerSurgicalInspectionScope.CountForTests == 0,
                "The Anomaly lifecycle reset leaked a surgery owner.");
        }

        /// <summary>Actual Anomaly save data has no ghoul replay marker and load bootstrap emits no page.</summary>
        [Test]
        public static void SaveLoadAndOldSaveBootstrapDoNotReplayTransformation()
        {
            if (!RequireAnomalyOrReport(nameof(SaveLoadAndOldSaveBootstrapDoNotReplayTransformation))) return;
            PawnDiaryRimTestScope.Require(ExposeAnomalyDataMethod != null && BootstrapAnomalyMethod != null,
                "Could not resolve the actual Anomaly save/bootstrap adapters.");
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateEligiblePawn();
            scope.FireAndRequireEvent(
                () => RunInfusion(subject, surgeon, forceFailure: false),
                AnomalyEventDefNames.GhoulTransformation,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);

            string savedXml = RoundTripActualAnomalyState();
            PawnDiaryRimTestScope.Require(
                savedXml.IndexOf("ghoulTransformation", StringComparison.OrdinalIgnoreCase) < 0
                    && savedXml.IndexOf("ghoulReplay", StringComparison.OrdinalIgnoreCase) < 0,
                "A2.2 unexpectedly added a persisted ghoul replay/catch-up marker.");
            scope.RequireNoNewEvent(() => BootstrapAnomalyMethod.Invoke(scope.Component, null));
        }

        /// <summary>A completed A2.2 scope cannot intercept an ordinary later batched injury Tale.</summary>
        [Test]
        public static void LaterOrdinaryInjuryRouteRemainsUnaffected()
        {
            if (!RequireAnomalyOrReport(nameof(LaterOrdinaryInjuryRouteRemainsUnaffected))) return;
            Pawn subject = CreateEligiblePawn();
            Pawn surgeon = CreateEligiblePawn();
            scope.FireAndRequireEvent(
                () => RunInfusion(subject, surgeon, forceFailure: false),
                AnomalyEventDefNames.GhoulTransformation,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);

            Pawn victim = CreateEligiblePawn();
            Pawn attacker = CreateEligiblePawn();
            IDictionary batches = PendingTaleBatches();
            string victimKey = TaleCombatBatchKey(victim);
            string attackerKey = TaleCombatBatchKey(attacker);
            scope.RegisterCleanup(() =>
            {
                batches?.Remove(victimKey);
                batches?.Remove(attackerKey);
            });

            // Wounded belongs to the XML-configured delayed combat batch. It should create no
            // immediate page, but both eligible POVs must enter their exact ordinary batch keys.
            scope.RequireNoNewEvent(
                () => TaleRecorder.RecordTale(TaleDefOf.Wounded, victim, attacker, DamageDefOf.Cut));
            PawnDiaryRimTestScope.Require(
                batches.Contains(victimKey) && batches.Contains(attackerKey)
                    && GhoulInfusionScope.CountForTests == 0,
                "The completed ghoul scope intercepted or dropped a later ordinary injury batch.");
        }

        private static IDictionary PendingTaleBatches()
        {
            IDictionary batches = PendingTaleBatchesField?.GetValue(scope.Component) as IDictionary;
            PawnDiaryRimTestScope.Require(batches != null,
                "Could not read the pending Tale batches for the injury-route assertion.");
            return batches;
        }

        private static string TaleCombatBatchKey(Pawn pawn)
        {
            return "talecombat|tale|" + pawn.GetUniqueLoadID();
        }

        private static Pawn CreateEligiblePawn()
        {
            Pawn pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            return pawn;
        }

        private static Pawn CreateIneligiblePawn()
        {
            Pawn pawn = scope.CreateTrackedPawn(PawnKindDefOf.Colonist, faction: null);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(
                Find.CurrentMap.Center, Find.CurrentMap, 10), Find.CurrentMap);
            PawnDiaryRimTestScope.Require(!DiaryGameComponent.IsDiaryEligible(pawn),
                "The ineligible ghoul-role fixture unexpectedly qualified for a diary.");
            return pawn;
        }

        // Vanilla's recipe owns the transition and Tale ordering. The success fixture removes only the
        // shared outcome roll; the failure fixture installs one deterministic zero-damage failure row.
        private static void RunInfusion(Pawn subject, Pawn surgeon, bool forceFailure)
        {
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("GhoulInfusion");
            PawnDiaryRimTestScope.Require(recipe?.Worker is Recipe_GhoulInfusion,
                "The vanilla GhoulInfusion recipe worker was unavailable.");
            SurgeryOutcomeEffectDef originalOutcome = recipe.surgeryOutcomeEffect;
            try
            {
                recipe.surgeryOutcomeEffect = forceFailure
                    ? new SurgeryOutcomeEffectDef
                    {
                        outcomes = new List<SurgeryOutcome>
                        {
                            new SurgeryOutcome_Failure
                            {
                                chance = 1f,
                                totalDamage = 0,
                                applyEffectsToPart = false,
                                failure = true
                            }
                        }
                    }
                    : null;
                recipe.Worker.ApplyOnPawn(subject, null, surgeon, new List<Thing>(), null);
            }
            finally
            {
                recipe.surgeryOutcomeEffect = originalOutcome;
            }
        }

        private static GhoulTransformationCapture DummyGhoulCapture()
        {
            return new GhoulTransformationCapture
            {
                facts = new GhoulTransformationFacts
                {
                    subjectPawnId = "Pawn_GhoulResetFixture",
                    surgeonPawnId = "Pawn_GhoulSurgeonResetFixture",
                    tick = Find.TickManager?.TicksGame ?? 0
                }
            };
        }

        private static CreepJoinerSurgicalInspectionCapture DummyCreepCapture()
        {
            return new CreepJoinerSurgicalInspectionCapture
            {
                facts = new CreepJoinerSurgicalDisclosureFacts
                {
                    subjectPawnId = "Pawn_CreepResetFixture",
                    surgeonPawnId = "Pawn_CreepSurgeonResetFixture",
                    tick = Find.TickManager?.TicksGame ?? 0
                }
            };
        }

        private static void RequireContext(DiaryEvent page, string token)
        {
            PawnDiaryRimTestScope.Require(page != null
                    && (page.gameContext ?? string.Empty).Contains(token),
                "Ghoul event context omitted exact token '" + token + "'.");
        }

        private static bool RequireAnomalyOrReport(string testName)
        {
            if (ModsConfig.AnomalyActive) return true;
            PawnDiaryRimTestScope.Require(!DiaryAnomalyPatches.GhoulTransformationHookReady
                    && !DlcContext.IsGhoul(null),
                "Ghoul transformation capture remained live without Anomaly.");
            Log.Message(LogPrefix + testName + ": not applicable (Anomaly inactive).");
            return false;
        }

        private static bool OwnedPatch(Patch patch)
        {
            return patch.owner == "aimml.pawndiary"
                && patch.PatchMethod?.DeclaringType == typeof(DiaryAnomalyPatches);
        }

        private static string RoundTripActualAnomalyState()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_anomaly_ghoul_" + Guid.NewGuid().ToString("N") + ".xml");
            scribeTarget = scope.Component;
            try
            {
                ActualAnomalyStateAdapter save = new ActualAnomalyStateAdapter();
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref save, "obj");
                Scribe.saver.FinalizeSaving();
                string xml = System.IO.File.ReadAllText(path);

                ActualAnomalyStateAdapter loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
                PawnDiaryRimTestScope.Require(loaded != null,
                    "The actual Anomaly state adapter loaded null.");
                return xml;
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                scribeTarget = null;
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch
                {
                    // Best-effort temp cleanup must not hide the Scribe assertion.
                }
            }
        }

        private sealed class ActualAnomalyStateAdapter : IExposable
        {
            public void ExposeData()
            {
                PawnDiaryRimTestScope.Require(scribeTarget != null && ExposeAnomalyDataMethod != null,
                    "The actual Anomaly Scribe method has no live component target.");
                ExposeAnomalyDataMethod.Invoke(scribeTarget, null);
            }
        }

        private static FieldInfo ResolveTalesListField()
        {
            FieldInfo[] fields = typeof(TaleManager).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return fields.FirstOrDefault(field => typeof(IList).IsAssignableFrom(field.FieldType)
                && field.FieldType.IsGenericType
                && field.FieldType.GetGenericArguments()[0] == typeof(Tale));
        }

        private static HashSet<Tale> SnapshotTales()
        {
            HashSet<Tale> result = new HashSet<Tale>();
            IList rows = TalesListField?.GetValue(Find.TaleManager) as IList;
            if (rows == null) return result;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] is Tale tale) result.Add(tale);
            return result;
        }

        private static void RemoveFixtureTales()
        {
            if (originalTales == null) return;
            IList rows = TalesListField?.GetValue(Find.TaleManager) as IList;
            if (rows == null) return;
            for (int i = rows.Count - 1; i >= 0; i--)
                if (rows[i] is Tale tale && !originalTales.Contains(tale)) rows.RemoveAt(i);
        }
    }
}
