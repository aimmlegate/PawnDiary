// PawnDiaryMemoryM11RuntimeFixture.cs — shared loaded-game scaffolding for the M11 Memory
// repository, exact-identity, command-drain, DLC-absence, and performance RimTest suites.
//
// The fixture never opens a Window and never sends provider traffic. It seeds only disposable
// PawnDiaryRimTestScope owners, advances the production Library's bounded update slices through its
// private component seam, and resets process-local Library publications after every test.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Reusable builders and bounded polling for loaded M11 tests.</summary>
    internal static class PawnDiaryMemoryM11RuntimeFixture
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const int MaximumPublicationSlices = 4096;

        private static readonly MethodInfo RefreshLibraryMethod =
            typeof(DiaryGameComponent).GetMethod(
                "RefreshMemoryLibraryPublications", PrivateInstance);
        private static readonly MethodInfo ResetLibraryMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ResetMemoryLibraryTransient", PrivateInstance);
        private static readonly MethodInfo DrainLibraryCommandsMethod =
            typeof(DiaryGameComponent).GetMethod(
                "DrainMemoryLibraryCommands", PrivateInstance);
        private static readonly MethodInfo CompleteMaintenancePressureMethod =
            typeof(DiaryGameComponent).GetMethod(
                "TryCompleteMemoryMaintenanceCycle",
                PrivateInstance,
                null,
                new[]
                {
                    typeof(long),
                    typeof(Stopwatch),
                    typeof(long),
                    typeof(bool).MakeByRefType()
                },
                null);
        private static readonly MethodInfo ApplyLegacyKnowledgeEvictionMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ApplyKnowledgeEviction", PrivateInstance);
        private static readonly MethodInfo BeginFactualOwnerEnrollmentMethod =
            typeof(DiaryGameComponent).GetMethod(
                "BeginFactualOwnerEpochEnrollment",
                PrivateInstance,
                null,
                new[] { typeof(PawnKnowledgeState), typeof(bool) },
                null);
        private static readonly MethodInfo CreateObservationBudgetMethod =
            typeof(DiaryGameComponent).GetMethod(
                "CreateMemoryObservationBudgetSession", PrivateInstance);
        private static readonly MethodInfo PrepareObservationOwnerMethod =
            typeof(DiaryGameComponent).GetMethod(
                "PrepareMemoryObservationOwner", PrivateInstance);
        private static readonly MethodInfo ApplyObservationAwarenessMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ApplyMemoryAwarenessPlan", PrivateInstance);
        private static readonly MethodInfo CompleteObservationTickMethod =
            typeof(DiaryGameComponent).GetMethod(
                "CompleteMemoryObservationTick", PrivateInstance);
        private static readonly MethodInfo RemoveRelativeObservationMethod =
            typeof(DiaryGameComponent).GetMethod(
                "RemoveRelativeMemoryObservation", PrivateInstance);
        private static readonly MethodInfo RemoveGlobalFactionObservationMethod =
            typeof(DiaryGameComponent).GetMethod(
                "RemoveGlobalFactionSnapshots", PrivateInstance);
        private static readonly FieldInfo GlobalFactionSnapshotsField =
            typeof(DiaryGameComponent).GetField(
                "globalFactionSnapshots", PrivateInstance);
        private static readonly FieldInfo MemoryDiagnosticCountersField =
            typeof(DiaryGameComponent).GetField(
                "memoryDiagnosticCounters", PrivateInstance);
        private static readonly FieldInfo DiariesField =
            typeof(DiaryGameComponent).GetField("diaries", PrivateInstance);
        private static readonly MethodInfo ReadCapacityTuplePartMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ReadCapacityTuplePart", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>
        /// Captures monotonic allocator metadata before a disposable Brainwipe/enrollment. Restore it
        /// only after the scope-owned carrier rows are gone, so no real saved identity can be reused.
        /// </summary>
        internal sealed class AllocatorSnapshot
        {
            private readonly long epochSequence;
            private readonly string epochFallbackChain;
            private readonly long archiveReattributionGeneration;
            private readonly bool archiveReattributionDisabled;
            private readonly long factionGeneration;

            internal AllocatorSnapshot(DiaryGameComponent component)
            {
                Require(component != null,
                    "The allocator snapshot requires a loaded component.");
                epochSequence = GetField<long>(
                    component, "lastIssuedAutobiographicalEpochSequence");
                epochFallbackChain = GetField<string>(
                    component, "lastIssuedAutobiographicalEpochFallbackChain");
                archiveReattributionGeneration = GetField<long>(
                    component, "unresolvedArchiveReattributionGeneration");
                archiveReattributionDisabled = GetField<bool>(
                    component, "unresolvedArchiveReattributionDisabled");
                factionGeneration = GetField<long>(
                    component, "globalFactionSnapshotAllocatorGeneration");
            }

            /// <summary>Restores metadata after every disposable carrier has been removed.</summary>
            internal void Restore(DiaryGameComponent component)
            {
                if (component == null) return;
                SetField(component, "lastIssuedAutobiographicalEpochSequence", epochSequence);
                SetField(component, "lastIssuedAutobiographicalEpochFallbackChain",
                    epochFallbackChain);
                SetField(component, "unresolvedArchiveReattributionGeneration",
                    archiveReattributionGeneration);
                SetField(component, "unresolvedArchiveReattributionDisabled",
                    archiveReattributionDisabled);
                SetField(component, "globalFactionSnapshotAllocatorGeneration",
                    factionGeneration);
                component.MarkMemoryM4IndexesDirty();
                component.RebuildMemorySizeIndexes();
            }
        }

        /// <summary>Fails early when a private loaded seam was renamed.</summary>
        internal static void RequireReflectionSurface()
        {
            Require(RefreshLibraryMethod != null,
                "The production Memory Library refresh seam was renamed.");
            Require(ResetLibraryMethod != null,
                "The production Memory Library reset seam was renamed.");
            Require(DrainLibraryCommandsMethod != null,
                "The production Memory Library command-drain seam was renamed.");
            Require(CompleteMaintenancePressureMethod != null,
                "The production Memory maintenance pressure seam was renamed.");
            Require(ApplyLegacyKnowledgeEvictionMethod != null,
                "The production legacy knowledge eviction seam was renamed.");
            Require(BeginFactualOwnerEnrollmentMethod != null && DiariesField != null,
                "The production factual owner-enrollment seam was renamed.");
            Require(CreateObservationBudgetMethod != null
                    && PrepareObservationOwnerMethod != null
                    && ApplyObservationAwarenessMethod != null
                    && CompleteObservationTickMethod != null
                    && RemoveRelativeObservationMethod != null
                    && RemoveGlobalFactionObservationMethod != null
                    && GlobalFactionSnapshotsField != null
                    && MemoryDiagnosticCountersField != null
                    && ReadCapacityTuplePartMethod != null,
                "The production Memory observation budget seam was renamed.");
        }

        /// <summary>Creates one valid current owner envelope with Thread, Standalone, and Imported rows.</summary>
        internal static PawnKnowledgeState BuildCompleteOwner(
            string ownerId,
            string exactSubjectId,
            string equalSubjectLabel,
            int ordinal,
            int extraStandalone = 0,
            string subjectKind = null)
        {
            string exactSubjectKind = string.IsNullOrWhiteSpace(subjectKind)
                ? MemoryContractTokens.SubjectPawn
                : subjectKind;
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            string epoch = EpochToken(1000 + ordinal);
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
            state.autobiographicalEpochToken = epoch;
            state.originCultureDefName = "PawnDiary_RimTest_Origin_" + ordinal;
            state.originCultureSource = "rimtest";
            state.adoptedCultureDefName = "PawnDiary_RimTest_Adopted_" + ordinal;
            state.structuralRevision = 7;
            state.statusRevision = 5;

            SavedMemoryThreadRoot root = NewRoot(
                ownerId,
                epoch,
                exactSubjectId,
                equalSubjectLabel,
                ordinal,
                exactSubjectKind,
                now);
            state.threadRoots.Add(root);
            state.standaloneBlocks.Add(NewBlock(
                ownerId,
                epoch,
                "standalone-" + ordinal,
                string.Empty,
                MemoryContractTokens.CategoryPersonal,
                MemoryContractTokens.ImportanceImportant,
                exactSubjectId,
                equalSubjectLabel,
                "M11 standalone wording " + ordinal,
                Math.Max(0, now - 2),
                exactSubjectKind));
            for (int index = 0; index < extraStandalone; index++)
            {
                state.standaloneBlocks.Add(NewBlock(
                    ownerId,
                    epoch,
                    "extra-" + ordinal + "-" + index,
                    string.Empty,
                    CategoryFor(index),
                    ImportanceFor(index),
                    exactSubjectId,
                    equalSubjectLabel,
                    "M11 benchmark wording " + ordinal + " " + index,
                    Math.Max(0, now - 1 - index),
                    exactSubjectKind));
            }

            state.importedArchiveRows.Add(new SavedImportedMemoryRow
            {
                archiveRecordId = "imported-" + ordinal,
                savedOwnerIdentityKindToken = "exact_id",
                savedOwnerIdentityValue = ownerId,
                originalRecordId = "legacy-" + ordinal,
                sourceOccurrenceId = "legacy-occurrence-" + ordinal,
                sourceEventId = "legacy-event-" + ordinal,
                originalEventTick = Math.Max(0, now - 3),
                importedWording = "M11 imported wording " + ordinal + " — 世界 🙂",
                originalKindToken = MemoryContractTokens.KindEvent,
                originalSummaryRoleToken = MemoryContractTokens.SummaryRoleNone,
                originalCategoryToken = MemoryContractTokens.CategoryRelationships,
                originalImportanceToken = MemoryContractTokens.ImportanceRegular,
                routePolicyToken = "archive_only",
                sourceTypeToken = "legacy",
                conflictFingerprint = new string('a', 64),
                migrationReasonToken = "authored_conflict"
            });
            return state;
        }

        /// <summary>Creates exactly N distinct roots for the supported 4/12/64 thread-target smoke.</summary>
        internal static PawnKnowledgeState BuildThreadTargetOwner(string ownerId, int target)
        {
            Require(!string.IsNullOrWhiteSpace(ownerId) && target > 0,
                "The thread-target fixture requires an owner and positive N.");
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            string epoch = EpochToken(2000 + target);
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
            state.autobiographicalEpochToken = epoch;
            state.originCultureDefName = "PawnDiary_RimTest_PerformanceCulture";
            state.originCultureSource = "rimtest";
            state.structuralRevision = target;
            state.statusRevision = 1;
            for (int index = 0; index < target; index++)
            {
                state.threadRoots.Add(NewRoot(
                    ownerId,
                    epoch,
                    "Pawn_Performance_Subject_" + target + "_" + index,
                    "Long localized subject 世界 🙂 " + index,
                    3000 + (target * 100) + index,
                    MemoryContractTokens.SubjectPawn,
                    now));
            }
            return state;
        }

        /// <summary>Adds one distinct exact-subject root to an existing current owner fixture.</summary>
        internal static SavedMemoryThreadRoot AddThreadRoot(
            PawnKnowledgeState state,
            string subjectId,
            string subjectLabel,
            int ordinal,
            string subjectKind,
            string category)
        {
            Require(state != null && state.IsCurrentSchema()
                    && !string.IsNullOrWhiteSpace(state.pawnId)
                    && !string.IsNullOrWhiteSpace(state.autobiographicalEpochToken),
                "An additional root requires one current exact owner.");
            SavedMemoryThreadRoot root = NewRoot(
                state.pawnId,
                state.autobiographicalEpochToken,
                subjectId,
                subjectLabel,
                ordinal,
                subjectKind,
                Math.Max(0, Find.TickManager?.TicksGame ?? 0));
            for (int index = 0; index < root.visibleBlocks.Count; index++)
            {
                root.visibleBlocks[index].category = category;
            }
            state.threadRoots.Add(root);
            state.structuralRevision++;
            return root;
        }

        /// <summary>Publishes a disposable state into its scope-owned diary and invalidates indexes.</summary>
        internal static PawnDiaryRecord InstallOwner(
            PawnDiaryRimTestScope scope,
            Pawn pawn,
            PawnKnowledgeState state,
            string displayName)
        {
            Require(scope != null && pawn != null && state != null,
                "The loaded Memory fixture received an incomplete owner.");
            PawnDiaryRecord diary = scope.RequireDiaryRecord(pawn);
            diary.pawnName = displayName ?? string.Empty;
            diary.knowledgeState = state;
            scope.Component.MarkMemoryM4IndexesDirty();
            scope.Component.RebuildMemorySizeIndexes();
            DiaryStateVersion.Bump();
            ResetLibrary(scope.Component);
            return diary;
        }

        /// <summary>Clears only loaded-session Library publications and pending test commands.</summary>
        internal static void ResetLibrary(DiaryGameComponent component)
        {
            RequireReflectionSurface();
            if (component != null) ResetLibraryMethod.Invoke(component, null);
        }

        /// <summary>Resets transient publications and always removes every scope-owned saved row.</summary>
        internal static void ResetLibraryAndTearDown(PawnDiaryRimTestScope scope)
        {
            if (scope == null) return;
            try
            {
                ResetLibrary(scope.Component);
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>Runs one production update slice.</summary>
        internal static void RefreshLibrary(DiaryGameComponent component)
        {
            RequireReflectionSurface();
            RefreshLibraryMethod.Invoke(component, null);
        }

        /// <summary>Drains commands at the same non-draw component boundary used by the game loop.</summary>
        internal static void DrainLibraryCommands(DiaryGameComponent component)
        {
            RequireReflectionSurface();
            DrainLibraryCommandsMethod.Invoke(component, null);
        }

        /// <summary>Runs only the maintenance cycle's final pressure phase and returns its mutation.</summary>
        internal static bool CompleteMaintenancePressure(DiaryGameComponent component)
        {
            RequireReflectionSurface();
            object[] args =
            {
                (long)Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                Stopwatch.StartNew(),
                0L,
                false
            };
            bool completed = (bool)CompleteMaintenancePressureMethod.Invoke(component, args);
            Require(completed,
                "The pressure-only maintenance fixture unexpectedly deferred completion.");
            return (bool)args[3];
        }

        /// <summary>Runs the legacy-record retention adapter on the loaded disposable component.</summary>
        internal static void ApplyLegacyKnowledgeEviction(DiaryGameComponent component)
        {
            RequireReflectionSurface();
            ApplyLegacyKnowledgeEvictionMethod.Invoke(component, null);
        }

        /// <summary>Runs only the factual owner-directory enrollment transaction.</summary>
        internal static object BeginFactualOwnerEnrollment(
            DiaryGameComponent component,
            PawnKnowledgeState state,
            bool brainwipeCompletionLandmark)
        {
            RequireReflectionSurface();
            return BeginFactualOwnerEnrollmentMethod.Invoke(
                component, new object[] { state, brainwipeCompletionLandmark });
        }

        /// <summary>Creates the exact running budget used by one production observation slice.</summary>
        internal static object CreateObservationBudget(DiaryGameComponent component)
        {
            RequireReflectionSurface();
            return CreateObservationBudgetMethod.Invoke(component, null);
        }

        /// <summary>Plans one owner enrollment against a caller-owned observation budget.</summary>
        internal static object PrepareObservationOwner(
            DiaryGameComponent component,
            Pawn pawn,
            object budget)
        {
            RequireReflectionSurface();
            return PrepareObservationOwnerMethod.Invoke(
                component, new[] { (object)pawn, budget });
        }

        /// <summary>Commits one small baseline through the real observation admission transaction.</summary>
        internal static bool ApplyObservationBaseline(
            DiaryGameComponent component,
            object enrollment,
            object budget,
            string suffix)
        {
            RequireReflectionSurface();
            if (enrollment == null || budget == null) return false;
            var replacement = new KnowledgeAwarenessState
            {
                snapshotId = "rimtest-observation-" + (suffix ?? string.Empty),
                scopeKindToken = KnowledgeObservationTokens.ScopeRelative,
                subjectKind = KnowledgeObservationTokens.SubjectPawn,
                subjectId = "rimtest-subject-" + (suffix ?? string.Empty),
                factStreamToken = KnowledgeObservationTokens.StreamRelativeState,
                captureInvalidationGeneration = 1,
                knownnessEvidenceToken = KnowledgeObservationTokens.EvidenceDirect,
                firstObservedTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                lastObservedTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                lastSourceOccurrenceId = "rimtest-occurrence-" + (suffix ?? string.Empty),
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                snapshotRevision = 1
            };
            return (bool)ApplyObservationAwarenessMethod.Invoke(
                component,
                new[] { enrollment, replacement, null, string.Empty, budget });
        }

        /// <summary>Publishes the running observation indexes through the production slice tail.</summary>
        internal static void CompleteObservationTick(
            DiaryGameComponent component,
            object budget)
        {
            RequireReflectionSurface();
            CompleteObservationTickMethod.Invoke(component, new[] { budget });
        }

        /// <summary>Runs the production hidden-relative cleanup against one running budget.</summary>
        internal static void RemoveRelativeObservation(
            DiaryGameComponent component,
            string ownerId,
            string subjectId,
            object budget)
        {
            RequireReflectionSurface();
            RemoveRelativeObservationMethod.Invoke(
                component, new[] { (object)ownerId, subjectId, budget });
        }

        /// <summary>Runs production removal of one disappeared global faction snapshot.</summary>
        internal static void RemoveGlobalFactionObservation(
            DiaryGameComponent component,
            string factionInstanceId,
            object budget)
        {
            RequireReflectionSurface();
            RemoveGlobalFactionObservationMethod.Invoke(
                component, new[] { (object)factionInstanceId, budget });
        }

        /// <summary>Replaces the disposable component's global faction list with one exact row.</summary>
        internal static void InstallGlobalFactionObservation(
            DiaryGameComponent component,
            SavedGlobalFactionSnapshot row)
        {
            RequireReflectionSurface();
            var rows = GlobalFactionSnapshotsField.GetValue(component)
                as List<SavedGlobalFactionSnapshot>;
            Require(rows != null && row != null,
                "The global faction observation fixture could not install its saved row.");
            rows.Clear();
            rows.Add(row);
            component.RebuildMemorySizeIndexes();
        }

        /// <summary>Snapshots global faction rows so a loaded fixture can restore unrelated state.</summary>
        internal static List<SavedGlobalFactionSnapshot> SnapshotGlobalFactionObservations(
            DiaryGameComponent component)
        {
            RequireReflectionSurface();
            var rows = GlobalFactionSnapshotsField.GetValue(component)
                as List<SavedGlobalFactionSnapshot>;
            Require(rows != null, "The global faction observation list seam was renamed.");
            return new List<SavedGlobalFactionSnapshot>(rows);
        }

        /// <summary>Restores global faction rows captured before a disposable loaded fixture.</summary>
        internal static void RestoreGlobalFactionObservations(
            DiaryGameComponent component,
            List<SavedGlobalFactionSnapshot> snapshot)
        {
            RequireReflectionSurface();
            var rows = GlobalFactionSnapshotsField.GetValue(component)
                as List<SavedGlobalFactionSnapshot>;
            Require(rows != null, "The global faction observation list seam was renamed.");
            rows.Clear();
            if (snapshot != null) rows.AddRange(snapshot);
            component.RebuildMemorySizeIndexes();
        }

        /// <summary>Returns the disposable component's exact saved global-faction row count.</summary>
        internal static int GlobalFactionObservationCount(DiaryGameComponent component)
        {
            RequireReflectionSurface();
            var rows = GlobalFactionSnapshotsField.GetValue(component)
                as List<SavedGlobalFactionSnapshot>;
            Require(rows != null, "The global faction observation list seam was renamed.");
            return rows.Count;
        }

        /// <summary>Snapshots bounded saved diagnostics before an exact component-size fixture.</summary>
        internal static List<SavedMemoryDiagnosticCounter> SnapshotMemoryDiagnostics(
            DiaryGameComponent component)
        {
            RequireReflectionSurface();
            var rows = MemoryDiagnosticCountersField.GetValue(component)
                as List<SavedMemoryDiagnosticCounter>;
            Require(rows != null, "The saved Memory diagnostic list seam was renamed.");
            return new List<SavedMemoryDiagnosticCounter>(rows);
        }

        /// <summary>Replaces saved diagnostics and republishes exact transient size indexes.</summary>
        internal static void ReplaceMemoryDiagnostics(
            DiaryGameComponent component,
            List<SavedMemoryDiagnosticCounter> replacement)
        {
            RequireReflectionSurface();
            var rows = MemoryDiagnosticCountersField.GetValue(component)
                as List<SavedMemoryDiagnosticCounter>;
            Require(rows != null, "The saved Memory diagnostic list seam was renamed.");
            rows.Clear();
            if (replacement != null) rows.AddRange(replacement);
            component.RebuildMemorySizeIndexes();
        }

        /// <summary>Forces the epoch allocator into one loaded boundary fixture state.</summary>
        internal static void SetEpochAllocator(
            DiaryGameComponent component,
            long sequence,
            string fallbackChain)
        {
            SetField(component, "lastIssuedAutobiographicalEpochSequence", sequence);
            SetField(component, "lastIssuedAutobiographicalEpochFallbackChain",
                fallbackChain ?? string.Empty);
        }

        /// <summary>Returns the exact saved epoch allocator high-water sequence.</summary>
        internal static long EpochAllocatorSequence(DiaryGameComponent component)
        {
            return GetField<long>(component, "lastIssuedAutobiographicalEpochSequence");
        }

        /// <summary>Returns the exact saved epoch allocator fallback chain.</summary>
        internal static string EpochAllocatorFallbackChain(DiaryGameComponent component)
        {
            return GetField<string>(
                component, "lastIssuedAutobiographicalEpochFallbackChain");
        }

        /// <summary>Reads the transient logical-size publication generation.</summary>
        internal static long SizeIndexGeneration(DiaryGameComponent component)
        {
            return GetField<long>(component, "memorySizeIndexGeneration");
        }

        /// <summary>Reads the friend-only full logical-size walk counter.</summary>
        internal static long SizeIndexFullRebuildCount(DiaryGameComponent component)
        {
            return GetField<long>(component, "memorySizeIndexFullRebuildCount");
        }

        /// <summary>Reads or replaces the running observation session's exact global totals.</summary>
        internal static MemoryPayloadBudgetTotals ObservationGlobalTotals(
            object budget,
            MemoryPayloadBudgetTotals? replacement = null)
        {
            Require(budget != null, "The observation totals fixture received no budget.");
            FieldInfo field = budget.GetType().GetField(
                "global", BindingFlags.Instance | BindingFlags.Public);
            Require(field != null, "The observation global-total seam was renamed.");
            if (replacement.HasValue) field.SetValue(budget, replacement.Value);
            return (MemoryPayloadBudgetTotals)field.GetValue(budget);
        }

        /// <summary>Returns one owner's running observation byte totals.</summary>
        internal static DiaryGameComponent.MemoryOwnerByteTotals ObservationOwnerTotals(
            object budget,
            string ownerId)
        {
            Require(budget != null, "The observation owner-total fixture received no budget.");
            FieldInfo field = budget.GetType().GetField(
                "owners", BindingFlags.Instance | BindingFlags.Public);
            var owners = field?.GetValue(budget)
                as System.Collections.Generic.Dictionary<
                    string, DiaryGameComponent.MemoryOwnerByteTotals>;
            DiaryGameComponent.MemoryOwnerByteTotals totals =
                new DiaryGameComponent.MemoryOwnerByteTotals();
            Require(owners != null && owners.TryGetValue(ownerId, out totals) && totals.valid,
                "The observation owner-total seam omitted the requested owner.");
            return totals;
        }

        /// <summary>Returns the XML-normalized limits frozen into one observation session.</summary>
        internal static MemoryBudgetLimits ObservationBudgetLimits(object budget)
        {
            Require(budget != null, "The observation limit fixture received no budget.");
            FieldInfo field = budget.GetType().GetField(
                "limits", BindingFlags.Instance | BindingFlags.Public);
            Require(field != null, "The observation limit seam was renamed.");
            return (MemoryBudgetLimits)field.GetValue(budget);
        }

        /// <summary>
        /// Mirrors the production try/finally scope around factual admissions for a loaded fixture.
        /// Pass null to clear it before returning control to unrelated component work.
        /// </summary>
        internal static void SetActiveObservationAdmissionBudget(
            DiaryGameComponent component,
            object budget)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                "memoryObservationActiveAdmissionBudget", PrivateInstance);
            Require(field != null,
                "The scoped observation admission-budget seam was renamed.");
            field.SetValue(component, budget);
        }

        /// <summary>Reads or overrides the session active-owner count for an exact cap fixture.</summary>
        internal static int ObservationActiveOwnerCount(object budget, int? replacement = null)
        {
            Require(budget != null, "The observation owner-count fixture received no budget.");
            FieldInfo field = budget.GetType().GetField(
                "activeOwnerCount", BindingFlags.Instance | BindingFlags.Public);
            Require(field != null, "The observation active-owner count seam was renamed.");
            if (replacement.HasValue) field.SetValue(budget, replacement.Value);
            return (int)field.GetValue(budget);
        }

        /// <summary>Reads or overrides the active-plus-fence union count in one running session.</summary>
        internal static int ObservationNonArchiveEpochOwnerCount(
            object budget,
            int? replacement = null)
        {
            Require(budget != null, "The observation union-count fixture received no budget.");
            FieldInfo field = budget.GetType().GetField(
                "nonArchiveEpochOwnerCount", BindingFlags.Instance | BindingFlags.Public);
            Require(field != null, "The observation union-count seam was renamed.");
            if (replacement.HasValue) field.SetValue(budget, replacement.Value);
            return (int)field.GetValue(budget);
        }

        /// <summary>Returns the current XML-normalized observation owner cap.</summary>
        internal static int ObservationOwnerCap()
        {
            RequireReflectionSurface();
            long value = (long)ReadCapacityTuplePartMethod.Invoke(
                null, new object[] { "ownerSlotTriple", 0, 1000L, 4000L });
            Require(value > 0 && value <= int.MaxValue,
                "The observation owner cap is outside its defensive range.");
            return (int)value;
        }

        /// <summary>Returns the XML-normalized active-plus-fence owner-union cap.</summary>
        internal static int ObservationEpochFenceCap()
        {
            RequireReflectionSurface();
            long value = (long)ReadCapacityTuplePartMethod.Invoke(
                null, new object[] { "ownerSlotTriple", 1, 1001L, 4001L });
            Require(value > 0 && value <= int.MaxValue,
                "The observation epoch-fence cap is outside its defensive range.");
            return (int)value;
        }

        /// <summary>
        /// Appends exact synthetic current epoch holders for bounded directory-cap adapters. The
        /// returned references must be removed in finally; no live Pawn or page is created.
        /// </summary>
        internal static List<PawnDiaryRecord> AppendSyntheticOwnerDirectory(
            DiaryGameComponent component,
            int activeCount,
            int fenceCount,
            string suffix)
        {
            RequireReflectionSurface();
            var diaries = DiariesField.GetValue(component) as List<PawnDiaryRecord>;
            Require(diaries != null && activeCount >= 0 && fenceCount >= 0,
                "The synthetic owner-directory fixture received invalid input.");
            var added = new List<PawnDiaryRecord>(checked(activeCount + fenceCount));
            int total = checked(activeCount + fenceCount);
            for (int index = 0; index < total; index++)
            {
                string ownerId = "PawnDiary_RimTest_Directory_" + (suffix ?? string.Empty)
                    + "_" + index;
                PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
                state.autobiographicalEpochToken = EpochToken(500000L + index);
                state.epochFenceOnly = index >= activeCount;
                state.structuralRevision = 1;
                var diary = new PawnDiaryRecord
                {
                    pawnId = ownerId,
                    pawnName = ownerId,
                    knowledgeState = state
                };
                diaries.Add(diary);
                added.Add(diary);
            }
            return added;
        }

        /// <summary>Removes only exact synthetic directory records returned by the append helper.</summary>
        internal static void RemoveSyntheticOwnerDirectory(
            DiaryGameComponent component,
            List<PawnDiaryRecord> added)
        {
            RequireReflectionSurface();
            var diaries = DiariesField.GetValue(component) as List<PawnDiaryRecord>;
            Require(diaries != null, "The saved diary directory seam was renamed.");
            for (int index = 0; added != null && index < added.Count; index++)
                diaries.Remove(added[index]);
            component.MarkMemoryM4IndexesDirty();
            component.RebuildMemorySizeIndexes();
        }

        /// <summary>Builds one valid standalone block for exact post-enrollment M4 admission.</summary>
        internal static SavedMemoryBlock BuildStandaloneAdmissionBlock(
            string ownerId,
            string epoch,
            string suffix)
        {
            return NewBlock(
                ownerId,
                epoch,
                suffix,
                string.Empty,
                MemoryContractTokens.CategoryPersonal,
                MemoryContractTokens.ImportanceImportant,
                "rimtest-admission-subject-" + suffix,
                "Observation admission subject",
                "Observation admission wording " + suffix,
                Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                MemoryContractTokens.SubjectPawn);
        }

        /// <summary>Waits for the bounded owner directory and returns the exact disposable row.</summary>
        internal static MemoryLibraryOwnerRow RequireOwnerRow(
            DiaryGameComponent component,
            string displayName,
            out MemoryLibraryOwnerResult publication)
        {
            MemoryLibraryOwnerQuery query = new MemoryLibraryOwnerQuery
            {
                search = displayName,
                sortToken = "name",
                start = 0,
                count = 64,
                expectedDirectoryRevision = 0
            };
            for (int slice = 0; slice < MaximumPublicationSlices; slice++)
            {
                publication = component.QueryMemoryLibraryOwners(query);
                if (publication.status == MemoryLibraryStatuses.Ready)
                {
                    MemoryLibraryOwnerRow row = publication.rows.FirstOrDefault(candidate =>
                        candidate != null
                        && string.Equals(candidate.displayName, displayName,
                            StringComparison.Ordinal));
                    Require(row != null,
                        "The loaded Memory Library omitted disposable owner '" + displayName + "'.");
                    return row;
                }
                Require(publication.status == MemoryLibraryStatuses.Preparing,
                    "The owner directory failed while preparing: " + publication.status + ".");
                RefreshLibrary(component);
            }
            throw new AssertionException(
                "The loaded Memory Library owner directory exceeded its bounded fixture slices.");
        }

        /// <summary>Waits for one owner/view list publication.</summary>
        internal static MemoryLibraryListResult RequireList(
            DiaryGameComponent component,
            MemoryLibraryOwnerRow owner,
            long directoryRevision,
            string view,
            string search = "")
        {
            MemoryLibraryListQuery query = new MemoryLibraryListQuery
            {
                primaryHandle = Copy(owner.primaryHandle),
                activeOwnerEpochKey = Copy(owner.activeOwnerEpochKey),
                viewTag = view,
                filters = new MemoryLibraryFilters(),
                search = search ?? string.Empty,
                sortToken = "newest",
                listStart = 0,
                listCount = 64,
                expectedDirectoryRevision = directoryRevision,
                expectedListSnapshotRevision = 0
            };
            for (int slice = 0; slice < MaximumPublicationSlices; slice++)
            {
                MemoryLibraryListResult result = component.QueryMemoryLibraryList(query);
                if (result.status == MemoryLibraryStatuses.Ready) return result;
                Require(result.status == MemoryLibraryStatuses.Preparing,
                    "The " + view + " list failed while preparing: " + result.status + ".");
                RefreshLibrary(component);
            }
            throw new AssertionException(
                "The loaded Memory Library " + view + " list exceeded its bounded fixture slices.");
        }

        /// <summary>Creates one exact pinned list query from a prior publication.</summary>
        internal static MemoryLibraryListQuery PinnedListQuery(
            MemoryLibraryOwnerRow owner,
            MemoryLibraryListResult publication,
            string view)
        {
            return new MemoryLibraryListQuery
            {
                primaryHandle = Copy(owner.primaryHandle),
                activeOwnerEpochKey = Copy(owner.activeOwnerEpochKey),
                viewTag = view,
                filters = new MemoryLibraryFilters(),
                sortToken = "newest",
                listStart = 0,
                listCount = 64,
                expectedDirectoryRevision = publication.directoryRevision,
                expectedListSnapshotRevision = publication.listSnapshotRevision
            };
        }

        /// <summary>Builds an exact stable epoch token using the production composite codec.</summary>
        internal static string EpochToken(long sequence)
        {
            string epoch;
            Require(MemoryIdentityCodec.TryCreateNormalEpochToken(sequence, out epoch),
                "The M11 fixture could not create a canonical owner epoch.");
            return epoch;
        }

        /// <summary>Throws a RimTest assertion with a concise contract message.</summary>
        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }

        private static SavedMemoryThreadRoot NewRoot(
            string ownerId,
            string epoch,
            string subjectId,
            string subjectLabel,
            int ordinal,
            string subjectKind,
            long now)
        {
            MemoryRootIdentity rootIdentity = new MemoryRootIdentity
            {
                ownerPawnId = ownerId,
                ownerEpochToken = epoch,
                primarySubjectKind = subjectKind,
                primarySubjectId = subjectId
            };
            string rootId;
            Require(MemoryIdentityCodec.TryCreateRootId(rootIdentity, out rootId),
                "The M11 fixture could not create a canonical root identity.");
            string chapterId;
            Require(MemoryIdentityCodec.TryCreateChapterId(rootId, 1, out chapterId),
                "The M11 fixture could not create a canonical chapter identity.");
            SavedMemoryThreadRoot root = new SavedMemoryThreadRoot
            {
                rootId = rootId,
                ownerPawnId = ownerId,
                ownerEpochToken = epoch,
                subjectKind = subjectKind,
                subjectId = subjectId,
                frozenSubjectLabel = subjectLabel,
                structuralRevision = 3,
                statusRevision = 2,
                lastAppliedReducerRevision = 1,
                nextChapterOrdinal = 2
            };
            root.chapters.Add(new SavedMemoryChapter
            {
                chapterId = chapterId,
                ordinal = 1,
                phaseToken = "friend",
                openedTick = Math.Max(0, now - 4),
                lastActivityTick = Math.Max(0, now - 1),
                closedTick = -1,
                closureReasonToken = string.Empty,
                closed = false
            });
            SavedMemoryBlock block = NewBlock(
                ownerId,
                epoch,
                "thread-" + ordinal,
                chapterId,
                MemoryContractTokens.CategoryRelationships,
                MemoryContractTokens.ImportanceRegular,
                subjectId,
                subjectLabel,
                "M11 relationship wording " + ordinal,
                Math.Max(0, now - 1),
                subjectKind);
            block.rootId = rootId;
            root.visibleBlocks.Add(block);
            return root;
        }

        private static SavedMemoryBlock NewBlock(
            string ownerId,
            string epoch,
            string suffix,
            string chapterId,
            string category,
            string importance,
            string subjectId,
            string subjectLabel,
            string wording,
            long tick,
            string subjectKind)
        {
            string sourceOccurrenceId =
                OrdinalSegmentCodec.Segment("rimtest-memory-source-v1")
                + OrdinalSegmentCodec.Segment(suffix);
            string captureRuleId = "rimtest.rule." + suffix;
            string factDiscriminator = "fact-" + suffix;
            string recordId;
            Require(MemoryIdentityCodec.TryCreateRecordId(
                    new MemoryRecordIdentity
                    {
                        ownerPawnId = ownerId,
                        ownerEpochToken = epoch,
                        sourceOccurrenceId = sourceOccurrenceId,
                        captureRuleId = captureRuleId,
                        factDiscriminator = factDiscriminator
                    },
                    out recordId),
                "The M11 fixture could not create a canonical record identity.");
            string factId;
            Require(MemoryIdentityCodec.TryCreateFactId(
                    captureRuleId,
                    factDiscriminator,
                    "relationship_phase",
                    subjectKind,
                    subjectId,
                    "latest",
                    out factId),
                "The M11 fixture could not create a canonical fact identity.");
            string subjectRefId;
            Require(MemoryIdentityCodec.TryCreateSubjectRefId(
                    subjectKind,
                    subjectId,
                    "primary",
                    "direct",
                    out subjectRefId),
                "The M11 fixture could not create a canonical subject reference.");
            string provenanceRefId;
            Require(MemoryIdentityCodec.TryCreateProvenanceRefId(
                    "capture_signal",
                    sourceOccurrenceId,
                    "rimtest-event-" + suffix,
                    captureRuleId,
                    factDiscriminator,
                    string.Empty,
                    out provenanceRefId),
                "The M11 fixture could not create a canonical provenance reference.");
            SavedMemoryBlock block = new SavedMemoryBlock
            {
                recordId = recordId,
                sourceOccurrenceId = sourceOccurrenceId,
                sourceEventId = "rimtest-event-" + suffix,
                captureRuleId = captureRuleId,
                factDiscriminator = factDiscriminator,
                ownerPawnId = ownerId,
                ownerEpochToken = epoch,
                kind = MemoryContractTokens.KindEvent,
                summaryRole = MemoryContractTokens.SummaryRoleNone,
                category = category,
                importance = importance,
                originalEventTick = tick,
                ageUnknown = false,
                chapterId = chapterId ?? string.Empty,
                automaticWording = wording,
                formatRevision = 1,
                providerExposureState = "not_sent"
            };
            block.primarySubject = new SavedMemorySubjectRef
            {
                subjectRefId = subjectRefId,
                subjectKind = subjectKind,
                subjectId = subjectId,
                frozenLabel = subjectLabel,
                roleToken = "primary",
                knownnessToken = "direct"
            };
            block.facts.Add(new SavedMemoryCanonicalFact
            {
                factId = factId,
                factKind = "relationship_phase",
                canonicalSubjectKind = subjectKind,
                canonicalSubjectId = subjectId,
                aggregationToken = "latest",
                canonicalValueKind = "token",
                canonicalValue = "friend"
            });
            block.provenance.Add(new SavedMemoryProvenance
            {
                provenanceRefId = provenanceRefId,
                sourceKindToken = "capture_signal",
                sourceOccurrenceId = block.sourceOccurrenceId,
                sourceEventId = block.sourceEventId,
                captureRuleId = block.captureRuleId,
                factDiscriminator = block.factDiscriminator,
                integrationToken = string.Empty
            });
            return block;
        }

        private static string CategoryFor(int index)
        {
            switch (index % 4)
            {
                case 1: return MemoryContractTokens.CategoryRelationships;
                case 2: return MemoryContractTokens.CategoryFamily;
                case 3: return MemoryContractTokens.CategoryFactions;
                default: return MemoryContractTokens.CategoryPersonal;
            }
        }

        private static string ImportanceFor(int index)
        {
            switch (index % 3)
            {
                case 1: return MemoryContractTokens.ImportanceRegular;
                case 2: return MemoryContractTokens.ImportanceImportant;
                default: return MemoryContractTokens.ImportanceMinor;
            }
        }

        private static MemoryLibraryOwnerHandle Copy(MemoryLibraryOwnerHandle source)
        {
            return source == null
                ? null
                : new MemoryLibraryOwnerHandle(
                    source.scopeToken,
                    source.exactOwnerPawnIdOrEmpty,
                    source.epochTokenOrEmpty);
        }

        private static MemoryOwnerEpochKey Copy(MemoryOwnerEpochKey source)
        {
            return source == null
                ? null
                : new MemoryOwnerEpochKey
                {
                    ownerPawnId = source.ownerPawnId,
                    epochToken = source.epochToken
                };
        }

        private static T GetField<T>(DiaryGameComponent component, string name)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(name, PrivateInstance);
            Require(field != null, "The Memory allocator field '" + name + "' was renamed.");
            return (T)field.GetValue(component);
        }

        private static void SetField<T>(DiaryGameComponent component, string name, T value)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(name, PrivateInstance);
            Require(field != null, "The Memory allocator field '" + name + "' was renamed.");
            field.SetValue(component, value);
        }
    }
}
