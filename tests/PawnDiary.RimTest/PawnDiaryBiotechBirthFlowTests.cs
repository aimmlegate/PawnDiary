// In-game orchestration tests for the canonical Biotech birth emitter. Pure tests cover exact birth
// classification, family-arc attachment, writer selection, naming ownership, and save normalization;
// this fixture proves the detached result becomes one pair page with shared context/evidence and that
// the durable family/child owner prevents a second page. All test pawns have generation disabled, so
// no LLM request can leave the loaded game.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises the final canonical birth dispatch against a real loaded component.</summary>
    [TestSuite]
    public static class PawnDiaryBiotechBirthFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static PawnDiaryRimTestScope scope;
        private static Pawn birther;
        private static Pawn father;
        private static Pawn child;
        private static MethodInfo dispatchBirthMethod;

        /// <summary>Creates isolated adult writers plus a test subject and enables the birth group.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("biotechFamilyBirth");
            birther = scope.CreateAdultColonist();
            father = scope.CreateAdultColonist();
            child = scope.CreateAdultColonist();
            dispatchBirthMethod = typeof(DiaryGameComponent).GetMethod(
                "DispatchBiotechBirth",
                PrivateInstance);
            if (dispatchBirthMethod == null)
            {
                throw new AssertionException(
                    "Could not bind production birth member 'DispatchBiotechBirth'.");
            }
        }

        /// <summary>Restores settings, removes test events/diaries, and destroys every test pawn.</summary>
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
                birther = null;
                father = null;
                child = null;
                dispatchBirthMethod = null;
            }
        }

        /// <summary>
        /// Two exact adult writers receive one shared page at the original birth tick. The child remains
        /// the subject rather than a POV, and both roles retain source-owned bond-lifecycle evidence.
        /// </summary>
        [Test]
        public static void CanonicalBirthCreatesOnePairPageWithEvidence()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech birth] not applicable (Biotech inactive).");
                return;
            }

            BirthFixture fixture = Fixture();
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeDispatch(fixture),
                FamilyBirthEventData.DefName,
                birther,
                father);

            scope.RequirePairRefs(diaryEvent, birther, father);
            RequireContext(diaryEvent, "family_birth=true");
            RequireContext(diaryEvent, "family_arc_id=" + fixture.snapshot.familyArcId);
            RequireContext(diaryEvent, "child_id=" + fixture.snapshot.childId);
            RequireContext(diaryEvent, "birth_outcome=healthy");
            RequireContext(diaryEvent, "birth_method=pregnancy");
            RequireContext(diaryEvent, "initiator_family_role=birther");
            RequireContext(diaryEvent, "recipient_family_role=father");
            PawnDiaryRimTestScope.Require(
                diaryEvent.tick == fixture.snapshot.birthTick,
                "Canonical birth did not preserve the original event tick.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.initiatorPawnId != child.GetUniqueLoadID()
                    && diaryEvent.recipientPawnId != child.GetUniqueLoadID(),
                "The newborn was incorrectly selected as a diary POV.");

            RequireBondEvidence(diaryEvent, DiaryEvent.InitiatorRole, fixture.snapshot);
            RequireBondEvidence(diaryEvent, DiaryEvent.RecipientRole, fixture.snapshot);
        }

        /// <summary>The saved family/child ownership check rejects a repeated canonical completion.</summary>
        [Test]
        public static void CanonicalBirthIsDurablyOnceOnly()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech birth] not applicable (Biotech inactive).");
                return;
            }

            BirthFixture fixture = Fixture();
            scope.FireAndRequireEvent(
                () => InvokeDispatch(fixture),
                FamilyBirthEventData.DefName,
                birther,
                father);
            scope.RequireNoNewEvent(() => InvokeDispatch(fixture));
        }

        private static BirthFixture Fixture()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            string arcId = "biotech-family|rimtest-birth|" + birther.GetUniqueLoadID();
            BirthMutationSnapshot snapshot = new BirthMutationSnapshot
            {
                familyArcId = arcId,
                childId = child.GetUniqueLoadID(),
                currentChildName = child.LabelShortCap,
                birther = Participant(birther, BiotechFamilyRoleTokens.Birther),
                geneticMother = Participant(birther, BiotechFamilyRoleTokens.GeneticMother),
                father = Participant(father, BiotechFamilyRoleTokens.Father),
                outcomeToken = BiotechBirthOutcomeTokens.Healthy,
                methodToken = BiotechBirthMethodTokens.Pregnancy,
                namingDeadline = -1,
                namingResolved = true,
                birthTick = Math.Max(0, now - 30),
                correlationId = "birth|" + arcId
            };
            BirthWriterSelection writers = new BirthWriterSelection
            {
                writers = new List<BirthWriterFact>
                {
                    Writer(birther, BiotechFamilyRoleTokens.Birther),
                    Writer(father, BiotechFamilyRoleTokens.Father)
                }
            };
            return new BirthFixture
            {
                snapshot = snapshot,
                writers = writers,
                writerPawns = new List<Pawn> { birther, father }
            };
        }

        private static FamilyParticipantFact Participant(Pawn pawn, string role)
        {
            return new FamilyParticipantFact
            {
                pawnId = pawn.GetUniqueLoadID(),
                displayName = pawn.LabelShortCap,
                roleToken = role,
                eligible = true
            };
        }

        private static BirthWriterFact Writer(Pawn pawn, string role)
        {
            return new BirthWriterFact
            {
                pawnId = pawn.GetUniqueLoadID(),
                displayName = pawn.LabelShortCap,
                roleToken = role
            };
        }

        private static bool InvokeDispatch(BirthFixture fixture)
        {
            try
            {
                return (bool)dispatchBirthMethod.Invoke(scope.Component, new object[]
                {
                    fixture.snapshot,
                    fixture.writers,
                    fixture.writerPawns,
                    child,
                    true
                });
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static void RequireContext(DiaryEvent diaryEvent, string fragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(fragment, StringComparison.Ordinal) >= 0,
                "Birth page context did not contain '" + fragment + "'.");
        }

        private static void RequireBondEvidence(
            DiaryEvent diaryEvent,
            string role,
            BirthMutationSnapshot snapshot)
        {
            List<NarrativeEvidence> evidence = diaryEvent.NarrativeEvidenceForRole(role);
            PawnDiaryRimTestScope.Require(
                evidence.Exists(item => item != null
                    && item.facet == NarrativeFacetTokens.BondLifecycle
                    && item.phase == BiotechBirthOutcomeTokens.Healthy
                    && item.subjectId == snapshot.childId
                    && item.arcKey == snapshot.familyArcId
                    && item.sourceDomain == "biotech_birth"
                    && item.sourceDefName == FamilyBirthEventData.DefName),
                "Canonical birth did not retain bond-lifecycle evidence for role '" + role + "'.");
        }

        private sealed class BirthFixture
        {
            public BirthMutationSnapshot snapshot;
            public BirthWriterSelection writers;
            public List<Pawn> writerPawns;
        }
    }
}
