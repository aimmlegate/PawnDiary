// Loaded-game Quality Wave B2 fixture. It crosses a real vanilla thought-memory hook, captures the
// pawn's mood and one optional thought at event time, persists that detached POV fact, and renders
// the exact frozen string through the shipped SoloInternalState prompt template.
using System;
using System.Collections.Generic;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves a normal eligible internal-state event freezes a compact mood snapshot and projects it
    /// through the real prompt pipeline without network traffic. Batch event-time retention is covered
    /// by <see cref="PawnDiaryInteractionBatchFlowTests"/>.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryQualityWaveMoodFlowTests
    {
        private const string ThoughtDefName = "NewColonyOptimism";

        private static PawnDiaryRimTestScope scope;
        private static Pawn writer;
        private static Pawn partner;

        /// <summary>
        /// Creates one live disposable writer in Prompt Test Mode and makes probability deterministic
        /// while retaining the production event/pawn/role hash path.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("thoughtPositive", "heartfelt");
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            writer = scope.CreateGeneratingAdultColonist();
            partner = scope.CreateAdultColonist();
            writer.Name = new NameTriple("Quality", "B2 Writer", "Test");
            partner.Name = new NameTriple("Quality", "B2 Partner", "Test");
            scope.SpawnAsLiveColonist(writer);
            scope.SpawnAsLiveColonist(partner);
            ForceMoodSnapshotAlways();
            scope.RegisterCleanup(() => RemoveThoughtMemories(writer, ThoughtDefName));
        }

        /// <summary>Restores tuning/prompt state and destroys the disposable pawn and diary row.</summary>
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
            }
        }

        /// <summary>
        /// Gains a real vanilla thought at five-percent mood, then proves the persisted event and
        /// rendered prompt contain the same frozen compact field.
        /// </summary>
        [Test]
        public static void ThoughtFreezesAndProjectsEventTimeMood()
        {
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(ThoughtDefName);
            Require(thoughtDef != null,
                "Required vanilla thought Def was not loaded: " + ThoughtDefName);

            writer.needs.mood.CurLevel = 0.05f;
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => GainThought(writer, thoughtDef),
                ThoughtDefName,
                writer,
                null);

            scope.RequireSoloRef(diaryEvent, writer);
            string expectedPrefix = "mood=" + DiaryBuckets.MoodBucket(5);
            string snapshot = diaryEvent.MoodSnapshotForRole(DiaryEvent.InitiatorRole);
            Require(snapshot.StartsWith(expectedPrefix, StringComparison.Ordinal),
                "B2 did not freeze the thought-time mood. Expected '" + expectedPrefix
                    + "', got '" + snapshot + "'.");

            string prompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);
            Require(prompt.IndexOf(snapshot, StringComparison.Ordinal) >= 0,
                "The real SoloInternalState prompt did not project the frozen B2 MoodSnapshot.");
        }

        /// <summary>
        /// Forces the master chance to zero after setup and proves a fully eligible event keeps the
        /// additive slot empty. This crosses the real capture seam rather than testing probability
        /// arithmetic alone.
        /// </summary>
        [Test]
        public static void ZeroApplyChanceOmitsEligibleMood()
        {
            DiaryTuning.Current.moodSnapshotApplyChance = 0f;
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(ThoughtDefName);
            Require(thoughtDef != null,
                "Required vanilla thought Def was not loaded: " + ThoughtDefName);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => GainThought(writer, thoughtDef),
                ThoughtDefName,
                writer,
                null);

            Require(string.IsNullOrEmpty(
                    diaryEvent.MoodSnapshotForRole(DiaryEvent.InitiatorRole)),
                "B2 populated a MoodSnapshot under a zero master apply chance.");
        }

        /// <summary>
        /// Fires an ordinary DeepTalk with every B2 chance forced to one. Its context has no internal
        /// or batch marker, so both POV slots must remain empty before any snapshot work is attempted.
        /// </summary>
        [Test]
        public static void OrdinarySocialEventDoesNotSampleMood()
        {
            InteractionDef interactionDef =
                DefDatabase<InteractionDef>.GetNamedSilentFail("DeepTalk");
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

            Require(string.IsNullOrEmpty(
                    diaryEvent.MoodSnapshotForRole(DiaryEvent.InitiatorRole)),
                "B2 sampled the initiator of an ineligible ordinary social event.");
            Require(string.IsNullOrEmpty(
                    diaryEvent.MoodSnapshotForRole(DiaryEvent.RecipientRole)),
                "B2 sampled the recipient of an ineligible ordinary social event.");
        }

        /// <summary>
        /// Makes every B2 chance one for deterministic loaded coverage and restores the exact values
        /// when the scope tears down.
        /// </summary>
        private static void ForceMoodSnapshotAlways()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            float originalApplyChance = tuning.moodSnapshotApplyChance;
            tuning.moodSnapshotApplyChance = 1f;

            List<MoodSnapshotChanceRule> rules = tuning.moodSnapshotChances;
            float[] originalRuleChances = rules == null ? null : new float[rules.Count];
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    MoodSnapshotChanceRule rule = rules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    originalRuleChances[i] = rule.chance;
                    rule.chance = 1f;
                }
            }

            scope.RegisterCleanup(() =>
            {
                tuning.moodSnapshotApplyChance = originalApplyChance;
                if (rules == null || originalRuleChances == null)
                {
                    return;
                }

                int count = Math.Min(rules.Count, originalRuleChances.Length);
                for (int i = 0; i < count; i++)
                {
                    if (rules[i] != null)
                    {
                        rules[i].chance = originalRuleChances[i];
                    }
                }
            });
        }

        private static void GainThought(Pawn subject, ThoughtDef thoughtDef)
        {
            subject.needs.mood.thoughts.memories.TryGainMemory(thoughtDef, null, null);
        }

        private static void RemoveThoughtMemories(Pawn subject, string thoughtDefName)
        {
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            MemoryThoughtHandler memories = subject?.needs?.mood?.thoughts?.memories;
            if (thoughtDef != null && memories != null)
            {
                memories.RemoveMemoriesOfDef(thoughtDef);
            }
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryRimTestScope.Require(condition, message);
        }
    }
}
