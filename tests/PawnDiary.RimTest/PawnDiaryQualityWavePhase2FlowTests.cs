// Loaded-game fixtures for Quality Wave Phase 2. These tests cross the real RimWorld social APIs,
// event factory, per-POV persistence model, and prompt renderer; the shared scope keeps every pawn,
// relation, PlayLog row, diary row, setting, and prompt-test toggle disposable.
using System;
using PawnDiary.Capture;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves A5 freezes one bounded colony-identity roster per pair POV, projects it into the real
    /// prompt, excludes the current partner, and refuses to invent present-day facts for a captured
    /// historical birth boundary.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryQualityWavePhase2FlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn writer;
        private static Pawn partner;
        private static Pawn relative;

        // A5 reserves one slot for a *named* relation. Sibling cannot be used to build that fixture:
        // RimWorld derives it from shared parents, so Pawn_RelationsTracker.AddDirectRelation refuses
        // every implied def with a warning and silently leaves the two pawns unrelated. Parent is a
        // real direct relation, so one call gives the roster a guaranteed named row.
        private static readonly PawnRelationDef ReservedRelationDef = PawnRelationDefOf.Parent;

        /// <summary>
        /// Creates three live disposable colonists under prompt-test mode. The third pawn holds a
        /// direct family relation to the writer, guaranteeing A5's reserved-relation slot even in a
        /// busy colony.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("heartfelt");
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            writer = scope.CreateGeneratingAdultColonist();
            partner = scope.CreateGeneratingAdultColonist();
            relative = scope.CreateAdultColonist();
            writer.Name = new NameTriple("Quality", "A5 Writer", "Test");
            partner.Name = new NameTriple("Quality", "A5 Partner", "Test");
            relative.Name = new NameTriple("Quality", "A5 Relative", "Test");
            scope.SpawnAsLiveColonist(writer);
            scope.SpawnAsLiveColonist(partner);
            scope.SpawnAsLiveColonist(relative);
            writer.relations.AddDirectRelation(ReservedRelationDef, relative);
        }

        /// <summary>Restores prompt settings and destroys every disposable pawn and diary row.</summary>
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
                writer = null;
                partner = null;
                relative = null;
            }
        }

        /// <summary>
        /// Fires a real DeepTalk PlayLog row, then checks the event-time initiator slot and rendered
        /// prompt contain the reserved family relation but never duplicate the current pair partner.
        /// </summary>
        [Test]
        public static void PairEventFreezesAndProjectsIdentitySummary()
        {
            InteractionDef interactionDef = DefDatabase<InteractionDef>.GetNamedSilentFail("DeepTalk");
            Require(interactionDef != null, "Required vanilla DeepTalk InteractionDef was not loaded.");
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interactionDef,
                writer,
                partner);
            Require(entry != null, "Could not construct the vanilla DeepTalk PlayLog row.");
            scope.TrackPlayLogEntry(entry);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => Find.PlayLog.Add(entry),
                "DeepTalk",
                writer,
                partner);
            scope.RequirePairRefs(diaryEvent, writer, partner);

            string summary = diaryEvent.IdentitySummaryForRole(DiaryEvent.InitiatorRole);
            string relativeName = PromptTextSanitizer.LocalizedPromptText(relative.LabelShortCap);
            string partnerName = PromptTextSanitizer.LocalizedPromptText(partner.LabelShortCap);
            string relationLabel = PromptTextSanitizer.LocalizedPromptText(
                ReservedRelationDef.GetGenderSpecificLabelCap(relative));
            Require(summary.StartsWith("relationships=", StringComparison.Ordinal),
                "A5 identity summary did not use the stable structured prefix: " + summary);
            Require(summary.IndexOf(relativeName, StringComparison.Ordinal) >= 0,
                "A5 reserved relative was absent from the frozen identity roster: " + summary);
            Require(summary.IndexOf(relationLabel, StringComparison.Ordinal) >= 0,
                "A5 reserved family relation label was absent: " + summary);
            Require(summary.IndexOf(partnerName, StringComparison.Ordinal) < 0,
                "A5 duplicated the current pair partner inside key relationships: " + summary);
            Require(summary.IndexOf("opinion=", StringComparison.OrdinalIgnoreCase) < 0,
                "A5 exposed a numeric opinion field instead of qualitative sentiment: " + summary);

            string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);
            Require(prompt.IndexOf(summary, StringComparison.Ordinal) >= 0,
                "The real PairDefault prompt did not project the frozen A5 Identity field.");
        }

        /// <summary>
        /// Calls the production historical-birth overload with a non-null capture DTO. The live
        /// relative is deliberately visible, so empty identity slots prove the explicit chronology
        /// guard rather than an empty colony roster.
        /// </summary>
        [Test]
        public static void CapturedHistoricalPairDoesNotReadLiveIdentity()
        {
            int historicalTick = Math.Max(0, Find.TickManager.TicksGame - 1000);
            DiaryEvent diaryEvent = scope.Component.AddPairwiseEvent(
                writer,
                partner,
                "DeepTalk",
                "deep talk",
                "I remembered an earlier conversation.",
                "I remembered an earlier conversation.",
                string.Empty,
                "def=DeepTalk; label=deep talk",
                new BirthEventContextSnapshot
                {
                    birthTick = historicalTick,
                    birthDate = "captured historical boundary"
                },
                historicalTick);

            Require(diaryEvent != null, "Historical A5 fixture did not create a pair event.");
            Require(string.IsNullOrEmpty(
                    diaryEvent.IdentitySummaryForRole(DiaryEvent.InitiatorRole)),
                "Historical captured initiator read today's live relationship roster.");
            Require(string.IsNullOrEmpty(
                    diaryEvent.IdentitySummaryForRole(DiaryEvent.RecipientRole)),
                "Historical captured recipient read today's live relationship roster.");
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryRimTestScope.Require(condition, message);
        }
    }
}
