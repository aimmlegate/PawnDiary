// Art immortalization rules (Quality Wave §7.2, H6).
//
// When a sculpture's generated art tale is about a colony deed, one quiet diary page may be written
// about it. Everything that decides WHETHER and BY WHOM is pure and lives in
// Source/Pipeline/ArtImmortalizationPolicy.cs, so it is verified here without RimWorld: the exact
// tale-identity contract (and its fail-closed edges), the ownership key, deterministic per-artwork
// sampling, and the three-tier writer order with its order-independent tie-breaks.
using System.Collections.Generic;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestArtImmortalizationPolicy()
        {
            TestArtTaleIdentity();
            TestArtOwnershipKey();
            TestArtDeterministicSampling();
            TestArtWriterOrder();
        }

        private static ArtWriterCandidate ArtCandidate(
            string pawnId, int loadId, bool eligible,
            bool dominant = false, bool concerned = false, bool sculptor = false, bool hasDiary = false)
        {
            return new ArtWriterCandidate
            {
                pawnId = pawnId,
                loadId = loadId,
                eligible = eligible,
                dominant = dominant,
                concerned = concerned,
                sculptor = sculptor,
                hasColonyDiary = hasDiary
            };
        }

        // --- Exact deed identity, and every way it can fail closed --------------------------------
        private static void TestArtTaleIdentity()
        {
            AssertEqual("H6 a deed identity is its def name and numeric ID",
                "KilledMajorThreat#42", ArtImmortalizationPolicy.TaleIdentity("KilledMajorThreat", 42));
            AssertEqual("H6 the first tale of a game is a valid identity",
                "Butchered#0", ArtImmortalizationPolicy.TaleIdentity("Butchered", 0));
            AssertEqual("H6 a missing def name yields no identity",
                string.Empty, ArtImmortalizationPolicy.TaleIdentity("   ", 42));
            AssertEqual("H6 a negative tale ID yields no identity",
                string.Empty, ArtImmortalizationPolicy.TaleIdentity("KilledMajorThreat", -1));
            AssertEqual("H6 a def name carrying the separator yields no identity",
                string.Empty, ArtImmortalizationPolicy.TaleIdentity("Bad#Name", 42));
            AssertEqual("H6 a def name carrying a context delimiter yields no identity",
                string.Empty, ArtImmortalizationPolicy.TaleIdentity("Bad;Name", 42));

            AssertTrue("H6 a well-formed identity verifies",
                ArtImmortalizationPolicy.IsVerifiedTaleIdentity("KilledMajorThreat#42"));
            AssertTrue("H6 surrounding whitespace does not break verification",
                ArtImmortalizationPolicy.IsVerifiedTaleIdentity("  KilledMajorThreat#42  "));

            // The point of the whole contract: prose can never be mistaken for an identity, because
            // once-per-deed ownership must not degrade to matching a translated art description.
            AssertTrue("H6 a translated art label is not an identity",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("Killing of the pirate chief"));
            AssertTrue("H6 a bare def name is not an identity",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("KilledMajorThreat"));
            AssertTrue("H6 a missing ID is not an identity",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("KilledMajorThreat#"));
            AssertTrue("H6 a missing def name is not an identity",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("#42"));
            AssertTrue("H6 a non-numeric ID is not an identity",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("KilledMajorThreat#4a2"));
            AssertTrue("H6 a doubled separator is not an identity",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("Killed#Major#42"));
            AssertTrue("H6 an identity carrying the ownership-key delimiter is rejected",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("Killed|Major#42"));
            AssertTrue("H6 an identity carrying a context delimiter is rejected",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("Killed;Major#42"));
            AssertTrue("H6 a blank identity is rejected",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity("   "));
            AssertTrue("H6 an absent identity is rejected",
                !ArtImmortalizationPolicy.IsVerifiedTaleIdentity(null));
        }

        // --- Ownership key --------------------------------------------------------------------------
        private static void TestArtOwnershipKey()
        {
            AssertEqual("H6 the ownership key is the prefixed exact identity",
                "art-tale|KilledMajorThreat#42",
                ArtImmortalizationPolicy.OwnershipKey("KilledMajorThreat#42"));
            AssertEqual("H6 the ownership key trims its identity",
                "art-tale|KilledMajorThreat#42",
                ArtImmortalizationPolicy.OwnershipKey(" KilledMajorThreat#42 "));
            AssertEqual("H6 an unverifiable identity produces no ownership key",
                string.Empty, ArtImmortalizationPolicy.OwnershipKey("Killing of the pirate chief"));
            AssertEqual("H6 an absent identity produces no ownership key",
                string.Empty, ArtImmortalizationPolicy.OwnershipKey(null));
        }

        // --- Deterministic per-artwork sampling -----------------------------------------------------
        private static void TestArtDeterministicSampling()
        {
            float roll = ArtImmortalizationPolicy.DeterministicRoll("Thing_SculptureLarge7", "KilledMajorThreat#42");
            AssertTrue("H6 the sample stays in [0,1)", roll >= 0f && roll < 1f);
            AssertTrue("H6 re-initializing the same artwork cannot reroll it",
                roll == ArtImmortalizationPolicy.DeterministicRoll(
                    "Thing_SculptureLarge7", "KilledMajorThreat#42"));
            AssertTrue("H6 a second artwork about the same deed samples independently",
                roll != ArtImmortalizationPolicy.DeterministicRoll(
                    "Thing_SculptureLarge8", "KilledMajorThreat#42"));
            AssertTrue("H6 the same artwork about a different deed samples independently",
                roll != ArtImmortalizationPolicy.DeterministicRoll(
                    "Thing_SculptureLarge7", "KilledMajorThreat#43"));

            AssertTrue("H6 a sample below the chance writes",
                ArtImmortalizationPolicy.ShouldWrite(0.34f, 0.35f));
            AssertTrue("H6 the chance threshold is half-open",
                !ArtImmortalizationPolicy.ShouldWrite(0.35f, 0.35f));
            AssertTrue("H6 chance 1 always writes",
                ArtImmortalizationPolicy.ShouldWrite(0.999999f, 1f));
            AssertTrue("H6 chance 0 never writes",
                !ArtImmortalizationPolicy.ShouldWrite(0f, 0f));
            AssertTrue("H6 invalid samples never write",
                !ArtImmortalizationPolicy.ShouldWrite(float.NaN, 1f)
                    && !ArtImmortalizationPolicy.ShouldWrite(-0.01f, 1f)
                    && !ArtImmortalizationPolicy.ShouldWrite(1f, 1f));
            AssertTrue("H6 invalid chances never write",
                !ArtImmortalizationPolicy.ShouldWrite(0f, float.NaN)
                    && !ArtImmortalizationPolicy.ShouldWrite(0f, float.NegativeInfinity));
        }

        // --- Writer order -----------------------------------------------------------------------------
        private static void TestArtWriterOrder()
        {
            // Tier 1: the deed's own subject writes, even though a lower-ID colonist is also concerned.
            List<ArtWriterCandidate> withSubject = new List<ArtWriterCandidate>
            {
                ArtCandidate("witness", 1, eligible: true, concerned: true, hasDiary: true),
                ArtCandidate("subject", 9, eligible: true, dominant: true, concerned: true, hasDiary: true),
                ArtCandidate("artist", 5, eligible: true, sculptor: true, hasDiary: true)
            };
            AssertEqual("H6 the pawn the deed is about writes it",
                "subject", ArtImmortalizationPolicy.SelectWriter(withSubject).pawnId);

            // Tier 2: the subject cannot write, so the lowest-ID concerned colonist does.
            List<ArtWriterCandidate> subjectIneligible = new List<ArtWriterCandidate>
            {
                ArtCandidate("witnessHigh", 7, eligible: true, concerned: true, hasDiary: true),
                ArtCandidate("witnessLow", 2, eligible: true, concerned: true, hasDiary: true),
                ArtCandidate("subject", 9, eligible: false, dominant: true, concerned: true, hasDiary: true),
                ArtCandidate("artist", 5, eligible: true, sculptor: true, hasDiary: true)
            };
            AssertEqual("H6 the lowest-ID concerned colonist writes when the subject cannot",
                "witnessLow", ArtImmortalizationPolicy.SelectWriter(subjectIneligible).pawnId);

            // Reversing the list must not change the answer: RimWorld's colonist iteration order is
            // not a contract, and the same save must always pick the same writer.
            List<ArtWriterCandidate> reversed = new List<ArtWriterCandidate>
            {
                subjectIneligible[3], subjectIneligible[2], subjectIneligible[1], subjectIneligible[0]
            };
            AssertEqual("H6 the writer does not depend on iteration order",
                "witnessLow", ArtImmortalizationPolicy.SelectWriter(reversed).pawnId);

            AssertEqual("H6 an exact ID tie falls back to the ordinal pawn ID",
                "aaa",
                ArtImmortalizationPolicy.SelectWriter(new List<ArtWriterCandidate>
                {
                    ArtCandidate("zzz", 4, eligible: true, concerned: true, hasDiary: true),
                    ArtCandidate("aaa", 4, eligible: true, concerned: true, hasDiary: true)
                }).pawnId);

            // Tier 3: art about a dead colonist who still has a diary — the artist may write it.
            List<ArtWriterCandidate> deceasedSubject = new List<ArtWriterCandidate>
            {
                ArtCandidate("deadSubject", 9, eligible: false, dominant: true, concerned: true, hasDiary: true),
                ArtCandidate("artist", 5, eligible: true, sculptor: true, hasDiary: true)
            };
            AssertEqual("H6 the artist may write about a deed whose subject has died",
                "artist", ArtImmortalizationPolicy.SelectWriter(deceasedSubject).pawnId);

            // ...but only when the deed concerns someone who kept a diary here. A trader's souvenir
            // about strangers stays silent.
            List<ArtWriterCandidate> strangerDeed = new List<ArtWriterCandidate>
            {
                ArtCandidate("stranger", 9, eligible: false, dominant: true, concerned: true, hasDiary: false),
                ArtCandidate("artist", 5, eligible: true, sculptor: true, hasDiary: true)
            };
            AssertTrue("H6 art about strangers stays silent even with an eligible artist",
                ArtImmortalizationPolicy.SelectWriter(strangerDeed) == null);
            AssertTrue("H6 art with no colony connection at all stays silent",
                ArtImmortalizationPolicy.SelectWriter(new List<ArtWriterCandidate>
                {
                    ArtCandidate("artist", 5, eligible: true, sculptor: true, hasDiary: true)
                }) == null);

            // The artist's own diary does not qualify them under tier 3 — the rule is about the DEED.
            AssertTrue("H6 the artist's own diary does not qualify an unrelated deed",
                ArtImmortalizationPolicy.SelectWriter(new List<ArtWriterCandidate>
                {
                    ArtCandidate("outsider", 9, eligible: false, dominant: true, concerned: true, hasDiary: false),
                    ArtCandidate("artist", 5, eligible: true, sculptor: true, hasDiary: true)
                }) == null);

            // An artist who is also in the deed is simply a tier-2 candidate.
            AssertEqual("H6 an artist who is also in the deed writes as a concerned colonist",
                "artist",
                ArtImmortalizationPolicy.SelectWriter(new List<ArtWriterCandidate>
                {
                    ArtCandidate("artist", 5, eligible: true, concerned: true, sculptor: true, hasDiary: true)
                }).pawnId);

            AssertTrue("H6 nobody eligible means no page",
                ArtImmortalizationPolicy.SelectWriter(new List<ArtWriterCandidate>
                {
                    ArtCandidate("subject", 9, eligible: false, dominant: true, concerned: true, hasDiary: true)
                }) == null);
            AssertTrue("H6 an empty candidate list means no page",
                ArtImmortalizationPolicy.SelectWriter(new List<ArtWriterCandidate>()) == null);
            AssertTrue("H6 an absent candidate list means no page",
                ArtImmortalizationPolicy.SelectWriter(null) == null);
            AssertTrue("H6 a candidate with no pawn ID is ignored",
                ArtImmortalizationPolicy.SelectWriter(new List<ArtWriterCandidate>
                {
                    null,
                    ArtCandidate("  ", 1, eligible: true, dominant: true, concerned: true, hasDiary: true)
                }) == null);
        }
    }
}
