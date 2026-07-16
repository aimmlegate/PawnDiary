// In-game orchestration tests for the canonical Biotech birth emitter. Pure tests cover exact birth
// classification, family-arc attachment, writer selection, naming ownership, and save normalization;
// this fixture proves the detached result becomes one pair page with shared context/evidence, the saved
// naming owner flushes once through live pawn lookup, and loaded prompt templates hide private IDs. Test
// pawns generate only under prompt-test capture, so no LLM request can leave the loaded game.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises canonical birth dispatch, delayed naming ownership, and prompt capture.</summary>
    [TestSuite]
    public static class PawnDiaryBiotechBirthFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo DiariesByIdField = typeof(DiaryGameComponent).GetField(
            "diariesById",
            PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn birther;
        private static Pawn father;
        private static Pawn child;
        private static MethodInfo dispatchBirthMethod;
        private static MethodInfo maintainPendingBirthsMethod;
        private static FieldInfo pendingBirthsField;

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
            maintainPendingBirthsMethod = typeof(DiaryGameComponent).GetMethod(
                "MaintainPendingBiotechBirths",
                PrivateInstance);
            pendingBirthsField = typeof(DiaryGameComponent).GetField(
                "pendingBiotechBirths",
                PrivateInstance);
            if (dispatchBirthMethod == null || maintainPendingBirthsMethod == null || pendingBirthsField == null)
            {
                throw new AssertionException(
                    "Could not bind the production birth dispatch/pending-ownership members.");
            }
            scope.RegisterCleanup(RemovePendingFixtureRows);
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
                maintainPendingBirthsMethod = null;
                pendingBirthsField = null;
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
            RequireContext(diaryEvent, "tale=" + FamilyBirthEventData.DefName);
            RequireContext(diaryEvent, "biotech_birth=true");
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
            PawnDiaryRimTestScope.Require(diaryEvent.IsImportant(),
                "Canonical birth did not route through the important Tale-domain family-birth group.");

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

        /// <summary>A delayed naming flush uses the prompt/display facts frozen at the birth boundary.</summary>
        [Test]
        public static void CanonicalBirthUsesFrozenEventTimeContext()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech birth] not applicable (Biotech inactive).");
                return;
            }

            BirthFixture fixture = Fixture();
            fixture.eventContext = new BirthEventContextSnapshot
            {
                birthTick = fixture.snapshot.birthTick,
                birthDate = "frozen birth date",
                writers = new List<BirthWriterContextSnapshot>
                {
                    FrozenWriter(birther, "Ari at birth", "birther summary", "birther room",
                        "birther-child continuity", "birther-father continuity"),
                    FrozenWriter(father, "Cy at birth", "father summary", "father room",
                        "father-child continuity", "father-birther continuity")
                }
            };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeDispatch(fixture),
                FamilyBirthEventData.DefName,
                birther,
                father);

            PawnDiaryRimTestScope.Require(diaryEvent.tick == fixture.snapshot.birthTick,
                "Frozen birth tick was not used by the event factory.");
            PawnDiaryRimTestScope.Require(diaryEvent.date == "frozen birth date",
                "Frozen birth date was not used by the event factory.");
            PawnDiaryRimTestScope.Require(diaryEvent.initiatorName == "Ari at birth"
                    && diaryEvent.recipientName == "Cy at birth",
                "Writer names drifted after the birth boundary.");
            PawnDiaryRimTestScope.Require(diaryEvent.initiatorPawnSummary == "birther summary"
                    && diaryEvent.recipientPawnSummary == "father summary",
                "Writer summaries did not come from the frozen birth context.");
            PawnDiaryRimTestScope.Require(diaryEvent.initiatorSurroundings == "birther room"
                    && diaryEvent.recipientSurroundings == "father room",
                "Writer surroundings did not come from the frozen birth context.");
            PawnDiaryRimTestScope.Require(diaryEvent.initiatorContinuity == "birther-father continuity"
                    && diaryEvent.recipientContinuity == "father-birther continuity",
                "Pair continuity did not come from the frozen birth context.");
        }

        /// <summary>A delayed birth sorts before the birther's later/same-call final-death boundary.</summary>
        [Test]
        public static void HistoricalBirthRemainsBeforeFinalDeathBoundary()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech birth] not applicable (Biotech inactive).");
                return;
            }

            BirthFixture fixture = Fixture();
            string birtherId = birther.GetUniqueLoadID();
            DiaryEvent death = scope.Component.AddSoloEvent(
                birther,
                null,
                "RimTestFinalDeath",
                "death",
                "death",
                string.Empty,
                "death_description=true; death_victim_id=" + birtherId);
            DiaryEvent birth = scope.FireAndRequireEvent(
                () => InvokeDispatch(fixture),
                FamilyBirthEventData.DefName,
                birther,
                father);

            RequireEventBefore(birther, birth, death,
                "Historical birth was sorted outside the final-death boundary.");
        }

        /// <summary>Fail-open mature signals retain their captured tick when released after vanilla.</summary>
        [Test]
        public static void ReleasedMatureFallbackRemainsBeforeFinalDeathBoundary()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech birth] not applicable (Biotech inactive).");
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            HistoricalFallbackSignal signal = new HistoricalFallbackSignal(birther, Math.Max(0, now - 10));
            BirthCorrelationScope ownership = BiotechBirthCorrelation.BeginBirth(
                null,
                BiotechPolicySnapshot.CreateDefault());
            bool closed = false;
            try
            {
                PawnDiaryRimTestScope.Require(
                    BiotechBirthCorrelation.TryStageMatureSignal("BabyBorn", signal),
                    "The XML-owned mature birth signal was not staged.");
                string birtherId = birther.GetUniqueLoadID();
                DiaryEvent death = scope.Component.AddSoloEvent(
                    birther,
                    null,
                    "RimTestFallbackFinalDeath",
                    "death",
                    "death",
                    string.Empty,
                    "death_description=true; death_victim_id=" + birtherId);

                BiotechBirthCorrelation.CloseBirth(ownership, false, now, 2500);
                closed = true;
                PawnDiaryRimTestScope.Require(signal.emittedEvent != null,
                    "The unclaimed mature fallback signal was not released.");
                PawnDiaryRimTestScope.Require(signal.emittedEvent.tick == signal.Payload.Tick,
                    "The released mature fallback did not retain its captured tick.");
                RequireEventBefore(birther, signal.emittedEvent, death,
                    "Released mature fallback was sorted outside the final-death boundary.");
            }
            finally
            {
                if (!closed)
                {
                    BiotechBirthCorrelation.CloseBirth(ownership, true, now, 2500);
                }
            }
        }

        /// <summary>
        /// A saved unnamed birth wakes through the production naming poll, resolves the live child's final
        /// visible name, emits once at the original tick with frozen writer context, and removes ownership.
        /// </summary>
        [Test]
        public static void PendingBirthFlushesOnceAfterLiveNamingResolution()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech pending birth] not applicable (Biotech inactive).");
                return;
            }

            scope.SpawnAsLiveColonist(birther);
            scope.SpawnAsLiveColonist(father);
            scope.SpawnAsLiveColonist(child);
            child.Name = new NameTriple("RimTest", "B1 Named Child", "Diary");
            child.babyNamingDeadline = -1;

            BirthFixture fixture = Fixture();
            fixture.snapshot.currentChildName = "Baby";
            fixture.snapshot.namingResolved = false;
            fixture.snapshot.namingDeadline = fixture.snapshot.birthTick + 60000;
            fixture.eventContext = new BirthEventContextSnapshot
            {
                birthTick = fixture.snapshot.birthTick,
                birthDate = "frozen pending birth date",
                writers = new List<BirthWriterContextSnapshot>
                {
                    FrozenWriter(birther, "Ari at birth", "birther snapshot", "birth room",
                        "birther-child continuity", "birther-father continuity"),
                    FrozenWriter(father, "Cy at birth", "father snapshot", "birth room",
                        "father-child continuity", "father-birther continuity")
                }
            };
            PendingBirthRows().Add(new PendingBiotechBirthState
            {
                snapshot = fixture.snapshot,
                writers = fixture.writers,
                eventContext = fixture.eventContext,
                createdTick = fixture.snapshot.birthTick
            });

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                InvokeMaintainPendingBirths,
                FamilyBirthEventData.DefName,
                birther,
                father);

            PawnDiaryRimTestScope.Require(
                diaryEvent.tick == fixture.snapshot.birthTick,
                "The naming flush did not retain the original birth tick.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.date == "frozen pending birth date",
                "The naming flush did not retain the event-time birth date.");
            RequireContext(diaryEvent, "child_name=B1 Named Child");
            PawnDiaryRimTestScope.Require(
                !PendingBirthRows().Exists(row => row?.snapshot?.childId == child.GetUniqueLoadID()),
                "The completed naming flush left its pending birth owner behind.");
            scope.RequireNoNewEvent(InvokeMaintainPendingBirths);
        }

        /// <summary>
        /// Loaded pair-important templates retain the exact child, outcome, method, and adult roles for
        /// Full/Balanced/Compact prompts without exposing stable IDs or the family correlation key.
        /// </summary>
        [Test]
        public static void BirthPromptsStayTruthfulAcrossAllDetailPresets()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[PawnDiary RimTest Biotech birth prompts] not applicable (Biotech inactive).");
                return;
            }

            RequireLoadedBirthPromptFields();
            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(birther, true);
            scope.Component.SetDiaryGenerationEnabled(father, true);
            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };

            for (int i = 0; i < levels.Length; i++)
            {
                if (i > 0)
                {
                    child = scope.CreateAdultColonist();
                }

                child.Name = new NameTriple("RimTest", "B1 Prompt Child " + i, "Diary");
                PawnDiaryMod.Settings.contextDetailLevel = levels[i];
                BirthFixture fixture = Fixture();
                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => InvokeDispatch(fixture),
                    FamilyBirthEventData.DefName,
                    birther,
                    father);
                string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);
                string suffix = " (" + levels[i] + ")";

                RequirePromptContains(prompt, child.LabelShortCap, "child name" + suffix);
                RequirePromptContains(prompt, BiotechBirthOutcomeTokens.Healthy, "birth outcome" + suffix);
                RequirePromptContains(prompt, BiotechBirthMethodTokens.Pregnancy, "birth method" + suffix);
                RequirePromptContains(prompt, BiotechFamilyRoleTokens.Birther, "writer role" + suffix);
                RequirePromptContains(prompt, BiotechFamilyRoleTokens.Father, "related writer role" + suffix);
                RequirePromptOmits(prompt, child.GetUniqueLoadID(), "child Thing ID" + suffix);
                RequirePromptOmits(prompt, birther.GetUniqueLoadID(), "birther Thing ID" + suffix);
                RequirePromptOmits(prompt, father.GetUniqueLoadID(), "father Thing ID" + suffix);
                RequirePromptOmits(prompt, fixture.snapshot.familyArcId, "family arc ID" + suffix);
                RequirePromptOmits(prompt, fixture.snapshot.correlationId, "correlation token" + suffix);
            }
        }

        private static BirthFixture Fixture()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            // Production family arcs are child-owned. Keeping the child id in this fixture key lets
            // one test model distinct births while repeated dispatches for the same child still deduplicate.
            string arcId = "biotech-family|rimtest-birth|" + child.GetUniqueLoadID();
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

        private static BirthWriterContextSnapshot FrozenWriter(
            Pawn pawn,
            string name,
            string summary,
            string surroundings,
            string soloContinuity,
            string pairContinuity)
        {
            return new BirthWriterContextSnapshot
            {
                pawnId = pawn.GetUniqueLoadID(),
                displayName = name,
                pawnSummary = summary,
                surroundings = surroundings,
                continuity = soloContinuity,
                pairContinuity = pairContinuity,
                lastOpener = string.Empty,
                previousEntryEnding = string.Empty,
                weapon = string.Empty,
                staggeredIntensity = 3,
                textDecorationFacts = "fixture=frozen"
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
                    fixture.eventContext,
                    true
                });
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static void InvokeMaintainPendingBirths()
        {
            try
            {
                maintainPendingBirthsMethod.Invoke(scope.Component, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static List<PendingBiotechBirthState> PendingBirthRows()
        {
            List<PendingBiotechBirthState> rows = pendingBirthsField?.GetValue(scope.Component)
                as List<PendingBiotechBirthState>;
            if (rows == null)
            {
                throw new AssertionException("Could not inspect pending Biotech birth ownership.");
            }

            return rows;
        }

        private static void RemovePendingFixtureRows()
        {
            if (scope?.Component == null || pendingBirthsField == null)
            {
                return;
            }

            List<PendingBiotechBirthState> rows = pendingBirthsField.GetValue(scope.Component)
                as List<PendingBiotechBirthState>;
            rows?.RemoveAll(row => row?.snapshot?.familyArcId != null
                && row.snapshot.familyArcId.StartsWith(
                    "biotech-family|rimtest-birth|",
                    StringComparison.Ordinal));
        }

        private static void RequireLoadedBirthPromptFields()
        {
            List<DiaryPromptFieldDef> fields = DiaryPromptTemplates.FieldsFor(DiaryPromptTemplates.PairImportant);
            string[] requiredContextKeys =
            {
                "child_name",
                "birth_outcome",
                "birth_method",
                "initiator_family_role",
                "recipient_family_role"
            };

            for (int keyIndex = 0; keyIndex < requiredContextKeys.Length; keyIndex++)
            {
                string requiredKey = requiredContextKeys[keyIndex];
                bool found = false;
                for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    DiaryPromptFieldDef field = fields[fieldIndex];
                    if (field != null
                        && field.enabled
                        && string.Equals(field.source, "GameContext", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(field.contextKey, requiredKey, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                PawnDiaryRimTestScope.Require(found,
                    "The loaded PairImportant prompt template is missing required birth context field '"
                    + requiredKey + "'. This usually means RimWorld loaded a stale or mixed Pawn Diary Def "
                    + "copy beside the current RimTest DLL; disable duplicate Pawn Diary packages and ensure "
                    + "ClassLibrary1 is the active copy.");
            }
        }

        private static void RequirePromptContains(string prompt, string fragment, string label)
        {
            PawnDiaryRimTestScope.Require(
                prompt != null && prompt.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0,
                "The birth prompt omitted " + label + " ('" + fragment + "').");
        }

        private static void RequirePromptOmits(string prompt, string fragment, string label)
        {
            if (string.IsNullOrEmpty(fragment))
            {
                return;
            }

            PawnDiaryRimTestScope.Require(
                prompt == null || prompt.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0,
                "The birth prompt leaked " + label + " ('" + fragment + "').");
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

        private static void RequireEventBefore(
            Pawn pawn,
            DiaryEvent earlier,
            DiaryEvent later,
            string failure)
        {
            Dictionary<string, PawnDiaryRecord> diaries = DiariesByIdField?.GetValue(scope.Component)
                as Dictionary<string, PawnDiaryRecord>;
            PawnDiaryRecord diary = null;
            string pawnId = pawn?.GetUniqueLoadID();
            PawnDiaryRimTestScope.Require(diaries != null && pawnId != null
                    && diaries.TryGetValue(pawnId, out diary) && diary?.eventIds != null,
                "Could not inspect the pawn diary index.");
            int earlierIndex = diary.eventIds.IndexOf(earlier?.eventId);
            int laterIndex = diary.eventIds.IndexOf(later?.eventId);
            PawnDiaryRimTestScope.Require(
                earlierIndex >= 0 && laterIndex >= 0 && earlierIndex < laterIndex,
                failure);
        }

        private sealed class BirthFixture
        {
            public BirthMutationSnapshot snapshot;
            public BirthWriterSelection writers;
            public List<Pawn> writerPawns;
            public BirthEventContextSnapshot eventContext;
        }

        private sealed class HistoricalFallbackSignal : DiarySignal
        {
            private readonly Pawn pawn;
            private readonly FamilyBirthEventData payload;

            public HistoricalFallbackSignal(Pawn pawn, int tick)
            {
                this.pawn = pawn;
                string pawnId = pawn.GetUniqueLoadID();
                payload = new FamilyBirthEventData
                {
                    PawnId = pawnId,
                    Tick = tick,
                    FamilyArcId = "biotech-family|rimtest-fallback|" + pawnId,
                    FirstWriterId = pawnId,
                    FirstWriterEligible = true,
                    HasValidSnapshot = true
                };
            }

            public DiaryEvent emittedEvent;

            public override DiaryEventData Payload => payload;

            public override CaptureContext BuildContext()
            {
                return DiaryGameComponent.BuildCaptureContext(true, true, true, true);
            }

            public override string DedupKey => "rimtest-mature-fallback|" + payload.PawnId;

            public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
            {
                emittedEvent = CreateSoloEvent(
                    sink,
                    pawn,
                    null,
                    "RimTestMatureBirthFallback",
                    "birth fallback",
                    "birth fallback",
                    string.Empty,
                    "source=rimtest_mature_birth_fallback");
            }
        }
    }
}
