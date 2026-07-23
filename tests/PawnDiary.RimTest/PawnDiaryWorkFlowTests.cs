// In-game work-sampling tests for Pawn Diary's periodic work scanner (EVT-12, design/TEST_COVERAGE_PLAN.md §3).
//
// RimWorld work is long-running and repetitive, so there is no one-shot Harmony hook: the component's
// ScanPawnWorkForDiaryEvents (DiaryGameComponent.Work.cs) periodically samples each free colonist and
// submits a WorkSignal (Source/Ingestion/Sources/WorkSignal.cs) through DiaryEvents.Submit. That private
// scan only iterates spawned free colonists on a map, so instead of driving the whole scan these tests
// invoke its exact per-pawn unit directly — `DiaryEvents.Submit(new WorkSignal(pawn))` — which is what the
// scan does for each colonist. This needs no map, matching how the reaction suite fires unspawned pawns.
//
// The WorkSignal constructor reads the pawn's CURRENT job (pawn.CurJob.workGiverDef.workType), classifies
// its mood (passion / negative chore / dark study), runs the persistent same-work cooldown check and a
// weighted chance roll, and only a passing roll becomes a solo work page. To make the chance/cooldown
// deterministic these tests force the XML-backed Work signal policy (baseChance clamps to 1, a long
// same-type cooldown) instead of looping until a random roll passes; every mutation is restored in
// teardown. Work state is injected by setting the pawn's current job to one whose fabricated WorkGiverDef
// points at a chosen WorkTypeDef, so the exact production reader (TryGetCurrentWork) sees it.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-12 Work.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;
using Verse.AI;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a controlled current-work assignment, sampled through the real WorkSignal capture
    /// path, records an eligible-work solo page carrying passion / chore / dark-study facts, that a
    /// same-work repeat is suppressed on the next scan, and that ignored (Social/Violent) work never
    /// records. These tests require a loaded game because the capture pipeline ignores events at the
    /// main menu; they never enable per-pawn generation, so no LLM request can leave the game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryWorkFlowTests
    {
        // A large, deterministic base chance so the weighted roll always clamps to 1 (passes), and a long
        // same-type cooldown so a freshly recorded work page reliably suppresses the next same-work roll.
        private const float ForcedPassChance = 1000000f;
        private const int ForcedSameTypeCooldownTicks = 600000;

        private static PawnDiaryRimTestScope scope;
        private static Pawn workerPawn;

        /// <summary>
        /// Opens a fresh scope, enables every Work-domain group this suite drives, creates one isolated
        /// generation-disabled colonist, and forces the Work signal policy + generation-chance weight to
        /// deterministic values (restored in teardown) so the chance/cooldown gates are not random.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("workPassion", "workStrain", "workRoutine", "workDarkStudy");
            workerPawn = scope.CreateAdultColonist();
            ForceDeterministicWorkPolicy();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived — even
        /// when a test above threw partway through.
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
                workerPawn = null;
            }
        }

        /// <summary>
        /// EVT-12. Eligible passion work with the effective chance forced to pass emits exactly one solo
        /// work page whose context marks the passion fact.
        /// </summary>
        [Test]
        public static void PassionWorkEmitsSoloEventWithPassionFact()
        {
            WorkTypeDef passionWork = MakeWorkType("PawnDiaryTest_PassionWork", WorkTags.None,
                new List<SkillDef> { SkillDefOf.Plants });
            GivePassion(workerPawn, SkillDefOf.Plants, Passion.Major);
            SetCurrentWork(workerPawn, passionWork, "tending crops");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new WorkSignal(workerPawn)),
                WorkEventData.PassionDefName,
                workerPawn,
                null);

            scope.RequireSoloRef(diaryEvent, workerPawn);
            RequireContextContains(diaryEvent, "passion=true");
            RequireContextContains(diaryEvent, "dark_study=false");
        }

        /// <summary>
        /// EVT-12. Eligible cleaning/dumb work emits a strain page whose context marks the negative-chore
        /// fact (not a passion fact).
        /// </summary>
        [Test]
        public static void ChoreWorkEmitsSoloEventWithChoreFact()
        {
            WorkTypeDef choreWork = MakeWorkType("PawnDiaryTest_ChoreWork", WorkTags.ManualDumb, null);
            SetCurrentWork(workerPawn, choreWork, "scrubbing floors");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new WorkSignal(workerPawn)),
                WorkEventData.StrainDefName,
                workerPawn,
                null);

            scope.RequireSoloRef(diaryEvent, workerPawn);
            RequireContextContains(diaryEvent, "dumb_or_cleaning=true");
            RequireContextContains(diaryEvent, "passion=false");
        }

        /// <summary>
        /// EVT-12. Work whose type name is the Anomaly "DarkStudy" key routes to the dark-study page and
        /// marks the dark-study fact. The mod's dark-study branch keys purely on the WorkTypeDef defName
        /// (WorkSignal.IsDarkStudy), so a controlled work type exercises it without requiring the DLC.
        /// </summary>
        [Test]
        public static void DarkStudyWorkEmitsSoloEventWithDarkStudyFact()
        {
            WorkTypeDef darkStudyWork = MakeWorkType("DarkStudy", WorkTags.None, null);
            SetCurrentWork(workerPawn, darkStudyWork, "poring over anomalous notes");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new WorkSignal(workerPawn)),
                WorkEventData.DarkStudyDefName,
                workerPawn,
                null);

            scope.RequireSoloRef(diaryEvent, workerPawn);
            RequireContextContains(diaryEvent, "dark_study=true");
        }

        /// <summary>
        /// EVT-12. After one work page is recorded for a work type, sampling the SAME work type again
        /// within the same-type cooldown window is suppressed: the second scan records nothing.
        /// </summary>
        [Test]
        public static void SameWorkRepeatIsSuppressedOnNextScan()
        {
            WorkTypeDef passionWork = MakeWorkType("PawnDiaryTest_RepeatWork", WorkTags.None,
                new List<SkillDef> { SkillDefOf.Plants });
            GivePassion(workerPawn, SkillDefOf.Plants, Passion.Major);
            SetCurrentWork(workerPawn, passionWork, "tending crops");

            // First scan records the page.
            scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new WorkSignal(workerPawn)),
                WorkEventData.PassionDefName,
                workerPawn,
                null);

            // Second scan of the identical work is dropped by the persistent same-work cooldown.
            scope.RequireNoNewEvent(() => DiaryEvents.Submit(new WorkSignal(workerPawn)));
        }

        /// <summary>
        /// EVT-12. Ignored work (a Violent/Social work type) is never eligible for a work page, so its
        /// scan records nothing regardless of chance — the deterministic negative gate.
        /// </summary>
        [Test]
        public static void IgnoredWorkTypeEmitsNothing()
        {
            WorkTypeDef violentWork = MakeWorkType("PawnDiaryTest_ViolentWork", WorkTags.Violent, null);
            SetCurrentWork(workerPawn, violentWork, "hunting");

            scope.RequireNoNewEvent(() => DiaryEvents.Submit(new WorkSignal(workerPawn)));
        }

        // ----- test helpers -----------------------------------------------------------------------

        /// <summary>
        /// Forces the Work signal policy so the weighted chance roll always passes (base chance clamps to
        /// 1, every mood multiplier is 1) and the same-type cooldown window is long and known. Also pins
        /// the global generation-chance weight to 1. Every field is snapshotted and restored in teardown,
        /// so the developer's live tuning/settings are untouched after the suite.
        /// </summary>
        private static void ForceDeterministicWorkPolicy()
        {
            DiarySignalPolicyDef policy = DiarySignalPolicies.ForKey(DiarySignalPolicies.Work);

            bool originalEnabled = policy.enabled;
            float originalBaseChance = policy.baseChance;
            float originalPassionMultiplier = policy.passionChanceMultiplier;
            float originalNegativeMultiplier = policy.negativeChanceMultiplier;
            float originalDarkStudyMultiplier = policy.darkStudyChanceMultiplier;
            float originalRecentDifferentMultiplier = policy.recentDifferentTypeMultiplier;
            int originalSameTypeCooldown = policy.sameTypeCooldownTicks;
            int originalLowSkillThreshold = policy.lowSkillThreshold;

            policy.enabled = true;
            policy.baseChance = ForcedPassChance;
            policy.passionChanceMultiplier = 1f;
            policy.negativeChanceMultiplier = 1f;
            policy.darkStudyChanceMultiplier = 1f;
            policy.recentDifferentTypeMultiplier = 1f;
            policy.sameTypeCooldownTicks = ForcedSameTypeCooldownTicks;
            policy.lowSkillThreshold = 0;

            scope.RegisterCleanup(() =>
            {
                policy.enabled = originalEnabled;
                policy.baseChance = originalBaseChance;
                policy.passionChanceMultiplier = originalPassionMultiplier;
                policy.negativeChanceMultiplier = originalNegativeMultiplier;
                policy.darkStudyChanceMultiplier = originalDarkStudyMultiplier;
                policy.recentDifferentTypeMultiplier = originalRecentDifferentMultiplier;
                policy.sameTypeCooldownTicks = originalSameTypeCooldown;
                policy.lowSkillThreshold = originalLowSkillThreshold;
            });

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings != null)
            {
                float originalWeight = settings.generationChanceWeight;
                settings.generationChanceWeight = 1f;
                scope.RegisterCleanup(() => settings.generationChanceWeight = originalWeight);
            }
        }

        /// <summary>
        /// Builds an unregistered WorkTypeDef the test fully controls. It is never added to the
        /// DefDatabase (the WorkSignal only reads it through the assigned job), so it needs no cleanup.
        /// </summary>
        private static WorkTypeDef MakeWorkType(string defName, WorkTags tags, List<SkillDef> relevantSkills)
        {
            return new WorkTypeDef
            {
                defName = defName,
                workTags = tags,
                relevantSkills = relevantSkills,
                gerundLabel = defName,
                labelShort = defName
            };
        }

        /// <summary>
        /// Points the pawn's current job at a fabricated WorkGiverDef whose workType is the given type, so
        /// the production reader (WorkSignal.TryGetCurrentWork → pawn.CurJob.workGiverDef.workType) samples
        /// exactly this work. The previous job is restored in teardown.
        /// </summary>
        private static void SetCurrentWork(Pawn pawn, WorkTypeDef workType, string workGiverLabel)
        {
            if (pawn?.jobs == null)
            {
                throw new AssertionException("Test pawn has no job tracker to assign work to.");
            }

            WorkGiverDef workGiver = new WorkGiverDef
            {
                defName = "PawnDiaryTest_WorkGiver_" + workType.defName,
                label = workGiverLabel,
                workType = workType
            };

            Job previousJob = pawn.jobs.curJob;
            Job job = new Job(JobDefOf.Wait) { workGiverDef = workGiver };
            pawn.jobs.curJob = job;

            scope.RegisterCleanup(() =>
            {
                if (pawn.jobs != null)
                {
                    pawn.jobs.curJob = previousJob;
                }
            });
        }

        /// <summary>Sets a pawn's passion for one skill and restores the original passion in teardown.</summary>
        private static void GivePassion(Pawn pawn, SkillDef skill, Passion passion)
        {
            SkillRecord record = pawn?.skills?.GetSkill(skill);
            if (record == null)
            {
                throw new AssertionException("Test pawn is missing the '" + skill?.defName + "' skill record.");
            }

            Passion originalPassion = record.passion;
            record.passion = passion;
            scope.RegisterCleanup(() => record.passion = originalPassion);
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The work event context did not contain the expected fact '" + expectedFragment + "'.");
        }
    }
}
