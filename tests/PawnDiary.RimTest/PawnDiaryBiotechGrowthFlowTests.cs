// In-game orchestration tests for the canonical Biotech growth owner. Pure tests already prove the
// before/after diff; this fixture proves the impure boundary creates one family-keyed page, attaches N1
// source evidence, consumes progression baselines, and releases the mature Birthday fallback when the
// canonical row is disabled. The test pawn is generation-disabled, so no LLM request can leave the game.
//
// These tests use detached snapshots rather than opening vanilla's growth-choice UI. The exact
// ConfigureGrowthLetter/MakeChoices signatures and false-to-true transition remain covered by runtime
// patch registration plus the manual age-7/10/13 acceptance matrix in the Biotech plan.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves canonical emission, family context, once-only consumption, ordinary fallback, and disabled-page
    /// observation against a real loaded DiaryGameComponent and isolated eligible pawn.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryBiotechGrowthFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static MethodInfo completeGrowthMethod;
        private static MethodInfo findDiaryMethod;

        /// <summary>Creates a no-generation pawn and enables the shared Progression signal.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            pawn = scope.CreateAdultColonist();
            completeGrowthMethod = typeof(DiaryGameComponent).GetMethod(
                "CompleteBiotechGrowth",
                PrivateInstance);
            findDiaryMethod = typeof(DiaryGameComponent).GetMethod("FindDiary", PrivateInstance);
            RequireHandle(completeGrowthMethod, "CompleteBiotechGrowth");
            RequireHandle(findDiaryMethod, "FindDiary");

            DiarySignalPolicyDef signal = DiarySignalPolicies.ForKey(DiarySignalPolicies.Progression);
            bool original = signal.enabled;
            signal.enabled = true;
            scope.RegisterCleanup(() => signal.enabled = original);
        }

        /// <summary>Restores settings, removes test events/diary state, and destroys the isolated pawn.</summary>
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
                completeGrowthMethod = null;
                findDiaryMethod = null;
            }
        }

        /// <summary>
        /// A verified age-13 mutation creates exactly one canonical solo page, carries qualitative
        /// context plus source-owned identity evidence, consumes every post-choice trait, advances the
        /// newly passionate skill baseline, and rejects a second completion for the same pawn/age.
        /// </summary>
        [Test]
        public static void CanonicalGrowthEmitsOnceAndConsumesProgressionBaselines()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", true);
            SetGroupEnabled("eventWindowBirthday", true);
            GrowthFixture fixture = Fixture(age: 13);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeComplete(fixture),
                BiotechEventDefNames.GrowthMoment,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);
            RequireContext(diaryEvent, "growth_moment=true");
            RequireContext(diaryEvent, "growth_stage=age_13");
            RequireContext(diaryEvent, "opportunity_band=");
            RequireContext(diaryEvent, "family_arc_id=biotech-family|");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext.IndexOf("growth_tier=", StringComparison.Ordinal) < 0,
                "Canonical growth context leaked the private numeric growth tier.");

            List<NarrativeEvidence> evidence = diaryEvent.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                evidence.Exists(item => item != null
                    && item.facet == NarrativeFacetTokens.IdentityTransition
                    && item.sourceDomain == "biotech_growth"
                    && item.sourceDefName == BiotechEventDefNames.GrowthMoment),
                "Canonical growth page did not retain its source-owned N1 identity-transition evidence.");

            PawnProgressionState progression = ProgressionState();
            PawnDiaryRimTestScope.Require(
                progression.EnsureBiotechState().HasConsumedGrowthAge(13),
                "Canonical age 13 was not marked consumed.");
            PawnDiaryRimTestScope.Require(
                progression.knownTraitKeys.Contains("Kind|0")
                    && progression.knownTraitKeys.Contains("Tough|0")
                    && progression.knownTraitKeys.Contains("Bisexual|0"),
                "Post-growth trait baselines did not consume selected plus automatic age-13 traits.");
            PawnDiaryRimTestScope.Require(
                progression.HighestSkillMilestone("Shooting") > 0,
                "Newly passionate high-skill subject did not advance its saved milestone baseline.");

            scope.RequireNoNewEvent(() => InvokeComplete(fixture));

            // Simulate a damaged/legacy nested row while the canonical event itself survived. The
            // durable event is still authoritative: completion must rebuild consumption, not release a
            // Birthday or add a second growth page.
            progression.EnsureBiotechState().consumedGrowthAges.Clear();
            scope.RequireNoNewEvent(() => InvokeComplete(fixture));
            PawnDiaryRimTestScope.Require(
                progression.EnsureBiotechState().HasConsumedGrowthAge(13),
                "Existing canonical page did not repair a missing consumed-age marker.");
        }

        /// <summary>
        /// An explicit canonical-group opt-out with Birthday enabled releases one ordinary page only
        /// after completion, while still consuming the exact growth state.
        /// </summary>
        [Test]
        public static void DisabledCanonicalGroupReleasesOneOrdinaryBirthday()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", false);
            SetGroupEnabled("eventWindowBirthday", true);
            GrowthFixture fixture = Fixture(age: 10);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeComplete(fixture),
                "Birthday",
                pawn,
                null);
            scope.RequireSoloRef(diaryEvent, pawn);
            PawnDiaryRimTestScope.Require(
                ProgressionState().EnsureBiotechState().HasConsumedGrowthAge(10),
                "Fallback birthday did not consume canonical growth ownership.");

            scope.RequireNoNewEvent(() => InvokeComplete(fixture));
        }

        /// <summary>
        /// Disabling both canonical growth and Birthday creates no page, but observation and dedup state
        /// still advance so the periodic trait/skill scanners cannot replay the same mutation.
        /// </summary>
        [Test]
        public static void BothGroupsDisabledStillConsumeWithoutPage()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech growth] not applicable (Biotech inactive).");
                return;
            }

            SetGroupEnabled("progressionGrowthMoment", false);
            SetGroupEnabled("eventWindowBirthday", false);
            GrowthFixture fixture = Fixture(age: 7);

            scope.RequireNoNewEvent(() => InvokeComplete(fixture));
            PawnProgressionState progression = ProgressionState();
            PawnDiaryRimTestScope.Require(
                progression.EnsureBiotechState().HasConsumedGrowthAge(7),
                "Disabled growth observation did not mark age 7 consumed.");
            PawnDiaryRimTestScope.Require(
                progression.knownTraitKeys.Contains("Tough|0"),
                "Disabled growth observation did not consume the post-choice trait baseline.");
        }

        private static GrowthFixture Fixture(int age)
        {
            string pawnId = pawn.GetUniqueLoadID();
            GrowthPawnSnapshot before = Snapshot(pawnId, age, "Alex", "none", "Kind|0");
            GrowthPawnSnapshot after = Snapshot(
                pawnId,
                age,
                age == 13 ? "Lex" : "Alex",
                "minor",
                "Kind|0",
                "Tough|0",
                age == 13 ? "Bisexual|0" : string.Empty);
            after.hasNewResponsibilities = age == 13;
            GrowthMomentMutation mutation = GrowthMomentPolicy.Diff(
                before,
                after,
                new GrowthCommittedChoice
                {
                    selectedTraitKey = "Tough|0",
                    selectedPassionSkillDefNames = new List<string> { "Shooting" },
                    sourceToken = BiotechGrowthSourceTokens.PlayerChoice
                },
                DiaryBiotechPolicy.Snapshot());
            PawnDiaryRimTestScope.Require(mutation != null, "Growth fixture did not produce a verified mutation.");
            return new GrowthFixture { before = before, after = after, mutation = mutation, age = age };
        }

        private static GrowthPawnSnapshot Snapshot(
            string pawnId,
            int age,
            string shortName,
            string shootingPassion,
            params string[] traitKeys)
        {
            GrowthPawnSnapshot snapshot = new GrowthPawnSnapshot
            {
                pawnId = pawnId,
                displayName = pawn.LabelShortCap,
                biologicalAge = age,
                growthTier = 6,
                shortName = shortName,
                skills = new List<GrowthSkillFact>
                {
                    new GrowthSkillFact
                    {
                        skillDefName = "Shooting",
                        label = "shooting",
                        passion = shootingPassion,
                        level = 20
                    }
                }
            };
            for (int i = 0; i < traitKeys.Length; i++)
            {
                string key = traitKeys[i];
                if (!string.IsNullOrWhiteSpace(key))
                {
                    snapshot.traits.Add(new GrowthTraitFact
                    {
                        traitKey = key,
                        label = key.Split('|')[0].ToLowerInvariant(),
                        description = "verified growth trait"
                    });
                }
            }

            return snapshot;
        }

        private static void InvokeComplete(GrowthFixture fixture)
        {
            Invoke(completeGrowthMethod, new object[]
            {
                pawn,
                fixture.before,
                fixture.after,
                fixture.mutation,
                fixture.age
            });
        }

        private static PawnProgressionState ProgressionState()
        {
            PawnDiaryRecord diary = Invoke(findDiaryMethod, new object[] { pawn, false }) as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Growth fixture could not resolve the test pawn's diary record.");
            }

            return diary.EnsureProgressionState();
        }

        private static object Invoke(MethodInfo method, object[] arguments)
        {
            try
            {
                return method.Invoke(scope.Component, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static void SetGroupEnabled(string defName, bool enabled)
        {
            PawnDiaryMod.Settings?.SetGroupEnabled(defName, enabled);
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Growth page context did not contain '" + fragment + "'.");
        }

        private static void RequireHandle(object handle, string member)
        {
            if (handle == null)
            {
                throw new AssertionException("Could not bind production growth member '" + member + "'.");
            }
        }

        private sealed class GrowthFixture
        {
            public GrowthPawnSnapshot before;
            public GrowthPawnSnapshot after;
            public GrowthMomentMutation mutation;
            public int age;
        }
    }
}
