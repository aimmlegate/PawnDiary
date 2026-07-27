// Loaded-game fixtures for Pawn Diary's cosmetic RNG boundary. RimWorld exposes Verse.Rand as one
// process-global seeded stream, so an unguarded diary roll can silently change later simulation
// outcomes. These tests establish a known outer stream, invoke one real stable-seeded generation
// adapter and one real one-shot capture adapter, then prove the next two outer draws still match the
// untouched control sequence.
//
// The stable-path fixture also verifies the user-visible contract behind Regenerate: identical
// (eventId, writerId, salt) inputs reproduce the same optional voice cue, while event IDs and reroll
// salts can only choose inside the writer's stable tier repertoire. No prompt is queued and no event
// is persisted; these are narrow adapter tests over the already-loaded Def catalog.
using System;
using System.Collections.Generic;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves stable-seeded generation and isolated one-shot capture rolls leave Verse's outer gameplay
    /// RNG stream untouched, and that the stable humor seed reproduces while its reroll salt can move to
    /// another candidate. Requires a loaded game only for the real Pawn Diary Def catalog.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRngIsolationFixtureTests
    {
        private const int OuterSeed = 192837465;
        private const string EventId = "RimTest_RngIsolation_Event";
        private const string WriterId = "Pawn_RimTest_RngIsolation";
        private const int SaltSearchLimit = 128;

        private static float originalHumorChance;
        private static int originalHumorCueRepertoireSize;
        private static bool humorTuningSnapshotted;

        /// <summary>
        /// Forces humor selection on so every stable-adapter call traverses both the chance gate and the
        /// weighted candidate pick. The original XML-loaded tuning value is restored after each test.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            PawnDiaryRimTestScope.Require(tuning != null, "The Pawn Diary tuning Def was not loaded.");

            originalHumorChance = tuning.humorChance;
            originalHumorCueRepertoireSize = tuning.humorCueRepertoireSize;
            humorTuningSnapshotted = true;
            tuning.humorChance = 1f;
            tuning.humorCueRepertoireSize = 2;
        }

        /// <summary>Restores the developer's real humor tuning even when an assertion fails.</summary>
        [AfterEach]
        public static void TearDown()
        {
            if (!humorTuningSnapshotted)
            {
                return;
            }

            DiaryTuning.Current.humorChance = originalHumorChance;
            DiaryTuning.Current.humorCueRepertoireSize = originalHumorCueRepertoireSize;
            humorTuningSnapshotted = false;
        }

        /// <summary>
        /// The generation-time humor selector uses a stable private seed. Its chance and weighted-pick
        /// draws must not advance the known outer Verse.Rand sequence.
        /// </summary>
        [Test]
        public static void StableSeededHumorAdapterPreservesOuterRandStream()
        {
            DiaryEvent diaryEvent = StableEvent();
            string selectedCue = null;

            RequireOuterStreamPreserved(
                () => selectedCue = HumorCues.CueFor(diaryEvent, null, WriterId, 0),
                "stable-seeded HumorCues.CueFor");

            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(selectedCue),
                "Forced-on HumorCues.CueFor returned no loaded cue, so the fixture did not traverse "
                + "the weighted generation-time selection path.");
        }

        /// <summary>
        /// Interaction instruction variants are a capture-once decision whose selected text is frozen
        /// into the event. Its unseeded private state must restore the outer gameplay sequence exactly.
        /// </summary>
        [Test]
        public static void OneShotInstructionAdapterPreservesOuterRandStream()
        {
            DiaryInteractionGroupDef group = new DiaryInteractionGroupDef
            {
                defName = "RimTest_RngIsolation_Group",
                instruction = "fallback instruction",
                instructions = new List<string>
                {
                    "first capture variant",
                    "second capture variant",
                    "third capture variant"
                }
            };
            string selectedInstruction = null;

            RequireOuterStreamPreserved(
                () => selectedInstruction = InteractionGroups.InstructionForGroup(group),
                "one-shot InteractionGroups.InstructionForGroup");

            PawnDiaryRimTestScope.Require(
                group.instructions.Contains(selectedInstruction),
                "The one-shot instruction adapter did not select one of the supplied capture variants.");
        }

        /// <summary>
        /// Repeated stable inputs reproduce one cue exactly. Advancing the reroll salt through a bounded
        /// deterministic sequence must eventually reach another loaded cue, proving the salt is part of
        /// the actual adapter selection rather than merely producing a different unused integer.
        /// </summary>
        [Test]
        public static void StableHumorSeedReproducesAndSaltCanChangeCandidate()
        {
            DiaryEvent diaryEvent = StableEvent();
            int seed = HumorChancePolicy.StableSeed(EventId, WriterId, 0);
            List<DiaryHumorCueDef> candidates = EligibleCandidates(true);
            List<string> candidateKeys = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                candidateKeys.Add(candidates[i].defName);
            }

            List<string> repertoireKeys = HumorChancePolicy.StableRepertoire(
                candidateKeys,
                WriterId,
                DiaryHumorCues.TierGallows,
                DiaryTuning.HumorCueRepertoireSize);
            PawnDiaryRimTestScope.Require(
                repertoireKeys.Count == 2,
                "The forced fixture writer did not receive the expected two-cue Gallows repertoire.");

            HashSet<string> repertoireRules = new HashSet<string>(StringComparer.Ordinal);
            for (int keyIndex = 0; keyIndex < repertoireKeys.Count; keyIndex++)
            {
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    DiaryHumorCueDef candidate = candidates[candidateIndex];
                    if (string.Equals(candidate.defName, repertoireKeys[keyIndex], StringComparison.Ordinal))
                    {
                        repertoireRules.Add(candidate.rule);
                        break;
                    }
                }
            }

            PawnDiaryRimTestScope.Require(
                repertoireRules.Count == 2,
                "The loaded two-cue repertoire did not expose two distinct rules for selection.");

            PawnDiaryRimTestScope.Require(
                seed == HumorChancePolicy.StableSeed(EventId, WriterId, 0),
                "Identical event/writer/salt inputs did not reproduce the same stable seed.");
            PawnDiaryRimTestScope.Require(
                seed != HumorChancePolicy.StableSeed(EventId, WriterId, 1),
                "Incrementing the reroll salt did not change the stable seed.");

            string original = HumorCues.CueFor(diaryEvent, null, WriterId, 0);
            string replay = HumorCues.CueFor(diaryEvent, null, WriterId, 0);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(original),
                "Forced-on humor selection returned no loaded candidate.");
            PawnDiaryRimTestScope.Require(
                repertoireRules.Contains(original),
                "The original stable selection escaped the writer's Gallows repertoire.");
            PawnDiaryRimTestScope.Require(
                string.Equals(original, replay, StringComparison.Ordinal),
                "Identical event/writer/salt inputs selected different humor cues.");

            int changedAtSalt = -1;
            for (int salt = 1; salt <= SaltSearchLimit; salt++)
            {
                string rerolled = HumorCues.CueFor(diaryEvent, null, WriterId, salt);
                PawnDiaryRimTestScope.Require(
                    repertoireRules.Contains(rerolled),
                    "Reroll salt " + salt + " selected outside the writer's stable repertoire.");
                if (!string.Equals(original, rerolled, StringComparison.Ordinal))
                {
                    changedAtSalt = salt;
                    break;
                }
            }

            PawnDiaryRimTestScope.Require(
                changedAtSalt > 0,
                "Advancing the stable reroll salt through " + SaltSearchLimit
                + " values never selected another loaded humor cue. The loaded catalog may have "
                + "collapsed to one usable candidate, or the adapter may no longer consume the salt.");

            DiaryEvent otherEvent = StableEvent(EventId + "_Other");
            for (int salt = 0; salt < 16; salt++)
            {
                string selected = HumorCues.CueFor(otherEvent, null, WriterId, salt);
                PawnDiaryRimTestScope.Require(
                    repertoireRules.Contains(selected),
                    "A different event ID selected outside the writer's stable Gallows repertoire.");
            }

            List<string> replayedRepertoire = HumorChancePolicy.StableRepertoire(
                candidateKeys,
                WriterId,
                DiaryHumorCues.TierGallows,
                DiaryTuning.HumorCueRepertoireSize);
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    string.Join("|", repertoireKeys),
                    string.Join("|", replayedRepertoire),
                    StringComparison.Ordinal),
                "Re-evaluating moment seeds changed the writer's stable repertoire keys.");
        }

        // Builds a minimal high-stakes event. An unknown group is conservatively important, so the real
        // selector uses the loaded Gallows tier without needing to create or mutate any game object.
        private static DiaryEvent StableEvent(string eventId = EventId)
        {
            return new DiaryEvent
            {
                eventId = eventId,
                interactionDefName = "RimTest_RngIsolation",
                gameContext = "event=RimTest_RngIsolation"
            };
        }

        private static List<DiaryHumorCueDef> EligibleCandidates(bool gallows)
        {
            List<DiaryHumorCueDef> result = new List<DiaryHumorCueDef>();
            IReadOnlyList<DiaryHumorCueDef> all = DiaryHumorCues.All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryHumorCueDef def = all[i];
                if (def != null
                    && !string.IsNullOrWhiteSpace(def.defName)
                    && !string.IsNullOrWhiteSpace(def.rule)
                    && DiaryHumorCues.HasRecognizedTier(def)
                    && DiaryHumorCues.IsGallows(def) == gallows
                    && def.weight > 0f
                    && !float.IsNaN(def.weight)
                    && !float.IsInfinity(def.weight))
                {
                    result.Add(def);
                }
            }

            return result;
        }

        // Compares two draws rather than one to catch both an advanced state and an incorrectly restored
        // state. Each branch is nested in PushState so the fixture itself also leaves the game's stream
        // exactly as it found it, even if the adapter or an assertion throws.
        private static void RequireOuterStreamPreserved(Action adapter, string adapterName)
        {
            float expectedFirst;
            float expectedSecond;
            Rand.PushState(OuterSeed);
            try
            {
                expectedFirst = Rand.Value;
                expectedSecond = Rand.Value;
            }
            finally
            {
                Rand.PopState();
            }

            float actualFirst;
            float actualSecond;
            Rand.PushState(OuterSeed);
            try
            {
                adapter();
                actualFirst = Rand.Value;
                actualSecond = Rand.Value;
            }
            finally
            {
                Rand.PopState();
            }

            PawnDiaryRimTestScope.Require(
                actualFirst.Equals(expectedFirst) && actualSecond.Equals(expectedSecond),
                adapterName + " advanced or replaced Verse's outer gameplay RNG stream. Expected "
                + expectedFirst + ", " + expectedSecond + " but observed "
                + actualFirst + ", " + actualSecond + ".");
        }
    }
}
