// Loaded-game test for a save that still contains frozen Biotech B1 ownership after Biotech is removed.
// The saved rows are plain Pawn Diary DTOs, so this fixture can construct them in a base-only profile and
// prove maintenance releases/prunes them without resolving a DLC Def or tracker.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety") and docs/lore/scribe-saving.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises frozen pending growth/birth rows while <c>ModsConfig.BiotechActive</c> is false.</summary>
    [TestSuite]
    public static class PawnDiaryBiotechDlcOffMaintenanceTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string FixturePrefix = "pawndiary-rimtest-biotech-off|";

        /// <summary>
        /// A DLC-off scan releases one live pawn's ordinary Birthday and flushes one frozen canonical
        /// birth for its surviving adult writers; neither pending row remains or replays.
        /// </summary>
        [Test]
        public static void FrozenGrowthAndBirthRowsDrainWithoutBiotech()
        {
            if (ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech-off maintenance] not applicable (Biotech active).");
                return;
            }
            if (DiaryGameComponent.Instance == null || Find.TickManager == null)
            {
                Log.Message("[PawnDiary RimTest Biotech-off maintenance] skipped: load a base-only colony first.");
                return;
            }

            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            int requiredAge = Math.Max(policy.growthFallbackGraceTicks, policy.birthNamingGraceTicks) + 1;
            int now = Find.TickManager.TicksGame;
            if (now <= requiredAge)
            {
                Log.Message("[PawnDiary RimTest Biotech-off maintenance] skipped: the loaded colony is only "
                    + now + " ticks old; advance beyond " + requiredAge + " ticks.");
                return;
            }

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin(
                "eventWindowBirthday",
                "biotechFamilyBirth");
            FieldInfo growthField = RequireField("pendingBiotechGrowthMoments");
            FieldInfo birthField = RequireField("pendingBiotechBirths");
            MethodInfo maintainGrowth = RequireMethod("MaintainPendingBiotechGrowthMoments");
            MethodInfo maintainBirths = RequireMethod("MaintainPendingBiotechBirths");
            Pawn growthPawn = null;
            Pawn birther = null;
            Pawn father = null;
            Pawn child = null;
            try
            {
                growthPawn = scope.CreateAdultColonist();
                birther = scope.CreateAdultColonist();
                father = scope.CreateAdultColonist();
                child = scope.CreateAdultColonist();
                scope.SpawnAsLiveColonist(growthPawn);
                scope.SpawnAsLiveColonist(birther);
                scope.SpawnAsLiveColonist(father);
                scope.SpawnAsLiveColonist(child);

                int oldTick = now - requiredAge;
                string growthId = growthPawn.GetUniqueLoadID();
                string arcId = FixturePrefix + child.GetUniqueLoadID();
                List<PendingBiotechGrowthMoment> growthRows =
                    growthField.GetValue(scope.Component) as List<PendingBiotechGrowthMoment>;
                List<PendingBiotechBirthState> birthRows =
                    birthField.GetValue(scope.Component) as List<PendingBiotechBirthState>;
                PawnDiaryRimTestScope.Require(growthRows != null && birthRows != null,
                    "The DLC-off fixture could not inspect pending B1 ownership lists.");

                growthRows.Add(new PendingBiotechGrowthMoment
                {
                    pawnId = growthId,
                    birthdayAge = 7,
                    birthdayTick = oldTick,
                    configuredTick = oldTick,
                    growthTier = 4,
                    familyArcId = FixturePrefix + "growth|" + growthId,
                    correlationId = FixturePrefix + "growth-correlation|" + growthId,
                    birthdaySnapshot = new GrowthPawnSnapshot
                    {
                        pawnId = growthId,
                        displayName = growthPawn.LabelShortCap,
                        biologicalAge = 7,
                        growthTier = 4,
                        shortName = growthPawn.LabelShortCap
                    }
                });

                BirthMutationSnapshot birthSnapshot = new BirthMutationSnapshot
                {
                    familyArcId = arcId,
                    childId = child.GetUniqueLoadID(),
                    currentChildName = "Frozen B1 child",
                    birther = Participant(birther, BiotechFamilyRoleTokens.Birther),
                    geneticMother = Participant(birther, BiotechFamilyRoleTokens.GeneticMother),
                    father = Participant(father, BiotechFamilyRoleTokens.Father),
                    outcomeToken = BiotechBirthOutcomeTokens.Healthy,
                    methodToken = BiotechBirthMethodTokens.Pregnancy,
                    namingDeadline = oldTick + policy.birthNamingGraceTicks,
                    namingResolved = false,
                    birthTick = oldTick,
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
                birthRows.Add(new PendingBiotechBirthState
                {
                    snapshot = birthSnapshot,
                    writers = writers,
                    eventContext = new BirthEventContextSnapshot
                    {
                        birthTick = oldTick,
                        birthDate = "frozen DLC-off birth date",
                        writers = new List<BirthWriterContextSnapshot>
                        {
                            WriterContext(birther, BiotechFamilyRoleTokens.Birther),
                            WriterContext(father, BiotechFamilyRoleTokens.Father)
                        }
                    },
                    createdTick = oldTick
                });

                DiaryEvent birthday = scope.FireAndRequireEvent(
                    () => Invoke(maintainGrowth, scope.Component),
                    "Birthday",
                    growthPawn,
                    null);
                DiaryEvent birth = scope.FireAndRequireEvent(
                    () => Invoke(maintainBirths, scope.Component),
                    FamilyBirthEventData.DefName,
                    birther,
                    father);

                PawnDiaryRimTestScope.Require(birthday != null && birth != null,
                    "DLC-off maintenance did not release both promised pages.");
                PawnDiaryRimTestScope.Require(
                    !CurrentGrowthRows(growthField, scope.Component).Exists(row => row?.pawnId == growthId),
                    "The DLC-off growth row remained after ordinary Birthday release.");
                PawnDiaryRimTestScope.Require(
                    !CurrentBirthRows(birthField, scope.Component).Exists(
                        row => row?.snapshot?.familyArcId == arcId),
                    "The DLC-off birth row remained after its frozen canonical flush.");
                scope.RequireNoNewEvent(() => Invoke(maintainGrowth, scope.Component));
                scope.RequireNoNewEvent(() => Invoke(maintainBirths, scope.Component));
            }
            finally
            {
                CurrentGrowthRows(growthField, scope.Component).RemoveAll(
                    row => row?.familyArcId != null
                        && row.familyArcId.StartsWith(FixturePrefix, StringComparison.Ordinal));
                CurrentBirthRows(birthField, scope.Component).RemoveAll(
                    row => row?.snapshot?.familyArcId != null
                        && row.snapshot.familyArcId.StartsWith(FixturePrefix, StringComparison.Ordinal));
                scope.TearDown();
            }
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

        private static BirthWriterContextSnapshot WriterContext(Pawn pawn, string role)
        {
            return new BirthWriterContextSnapshot
            {
                pawnId = pawn.GetUniqueLoadID(),
                displayName = pawn.LabelShortCap,
                pawnSummary = "frozen DLC-off writer",
                surroundings = "frozen DLC-off room",
                continuity = "frozen family continuity",
                pairContinuity = "frozen pair continuity",
                lastOpener = string.Empty,
                previousEntryEnding = string.Empty,
                weapon = string.Empty,
                staggeredIntensity = 3,
                textDecorationFacts = "fixture=biotech_off; role=" + role
            };
        }

        private static FieldInfo RequireField(string name)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(name, PrivateInstance);
            if (field == null) throw new AssertionException("Could not resolve component field '" + name + "'.");
            return field;
        }

        private static MethodInfo RequireMethod(string name)
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(name, PrivateInstance);
            if (method == null) throw new AssertionException("Could not resolve component method '" + name + "'.");
            return method;
        }

        private static void Invoke(MethodInfo method, DiaryGameComponent component)
        {
            try
            {
                method.Invoke(component, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static List<PendingBiotechGrowthMoment> CurrentGrowthRows(
            FieldInfo field,
            DiaryGameComponent component)
        {
            return field.GetValue(component) as List<PendingBiotechGrowthMoment>
                ?? new List<PendingBiotechGrowthMoment>();
        }

        private static List<PendingBiotechBirthState> CurrentBirthRows(
            FieldInfo field,
            DiaryGameComponent component)
        {
            return field.GetValue(component) as List<PendingBiotechBirthState>
                ?? new List<PendingBiotechBirthState>();
        }
    }
}
