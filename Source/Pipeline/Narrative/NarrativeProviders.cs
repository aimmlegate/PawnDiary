// Pure provider orchestration for Narrative Continuity. Runtime adapters copy guarded DLC state into
// these plain snapshots; this file turns those facts into optional candidates without touching Pawn,
// DefDatabase, settings, localization, or any paid-DLC type.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable Biotech family-continuity states understood by the first real provider.</summary>
    internal static class BiotechNarrativeContinuityTokens
    {
        public const string SinceBirth = "since_birth";
        public const string ObservedChildhood = "observed_childhood";
        public const string BaselineFamily = "baseline_family";
    }

    /// <summary>
    /// Plain, event-time Biotech facts. Text fields are already localized and sanitized by the main-thread
    /// adapter; stable IDs and tokens remain prose-free for matching and deterministic candidate keys.
    /// </summary>
    internal class BiotechNarrativeSnapshot
    {
        public bool providerAvailable;
        public string povPawnId = string.Empty;
        public string childId = string.Empty;
        public string familyArcId = string.Empty;
        public string familyContinuity = string.Empty;
        public string familyText = string.Empty;
        public string xenotypeDefName = string.Empty;
        public string identityText = string.Empty;
        public int sourceTick;
        public bool pawnCanKnow;
        public bool hasVerifiedPovConnection;
    }

    /// <summary>Pure candidate factory for visible Biotech family and current identity facts.</summary>
    internal static class BiotechNarrativeProvider
    {
        /// <summary>
        /// Classifies a saved family arc without inferring emotion or kinship. An exact birth is strongest;
        /// observed upbringing follows; a current exact parent relation is the quiet baseline.
        /// </summary>
        public static string FamilyContinuity(
            bool birthKnown,
            bool hasObservedUpbringing,
            bool hasExactFamilyConnection)
        {
            if (birthKnown) return BiotechNarrativeContinuityTokens.SinceBirth;
            if (hasObservedUpbringing) return BiotechNarrativeContinuityTokens.ObservedChildhood;
            return hasExactFamilyConnection ? BiotechNarrativeContinuityTokens.BaselineFamily : string.Empty;
        }

        /// <summary>Returns candidates only when authorized evidence exactly reaches this snapshot.</summary>
        public static List<NarrativeLensCandidate> Build(
            List<NarrativeEvidence> evidence,
            BiotechNarrativeSnapshot snapshot)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            if (snapshot == null || !snapshot.providerAvailable || !snapshot.pawnCanKnow
                || !snapshot.hasVerifiedPovConnection || !CouldApply(evidence, snapshot))
            {
                return result;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.familyArcId)
                && !string.IsNullOrWhiteSpace(snapshot.familyContinuity)
                && !string.IsNullOrWhiteSpace(snapshot.familyText))
            {
                result.Add(new NarrativeLensCandidate
                {
                    candidateKey = "biotech|family|" + snapshot.familyArcId,
                    provider = NarrativeProviderTokens.Biotech,
                    category = NarrativeCategoryTokens.Bond,
                    text = snapshot.familyText.Trim(),
                    facet = NarrativeFacetTokens.IdentityTransition,
                    subjectKind = NarrativeSubjectKindTokens.Family,
                    subjectId = snapshot.familyArcId,
                    arcKey = snapshot.familyArcId,
                    topicTokens = new List<string> { "family" },
                    sourceTick = snapshot.sourceTick,
                    salience = NarrativeSalienceTokens.Meaningful,
                    relationship = NarrativeRelationshipTokens.ExactArc,
                    pawnCanKnow = true,
                    providerAvailable = true,
                    hasVerifiedPovConnection = true
                });
            }

            if (!string.IsNullOrWhiteSpace(snapshot.childId)
                && !string.IsNullOrWhiteSpace(snapshot.xenotypeDefName)
                && !string.IsNullOrWhiteSpace(snapshot.identityText))
            {
                result.Add(new NarrativeLensCandidate
                {
                    candidateKey = "biotech|identity|" + snapshot.childId + "|" + snapshot.xenotypeDefName.Trim(),
                    provider = NarrativeProviderTokens.Biotech,
                    category = NarrativeCategoryTokens.Identity,
                    text = snapshot.identityText.Trim(),
                    facet = NarrativeFacetTokens.IdentityTransition,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = snapshot.childId,
                    topicTokens = new List<string> { "identity" },
                    sourceTick = snapshot.sourceTick,
                    salience = NarrativeSalienceTokens.Meaningful,
                    relationship = NarrativeRelationshipTokens.ExactSubject,
                    pawnCanKnow = true,
                    providerAvailable = true,
                    hasVerifiedPovConnection = true
                });
            }

            return result;
        }

        private static bool CouldApply(List<NarrativeEvidence> evidence, BiotechNarrativeSnapshot snapshot)
        {
            if (evidence == null) return false;
            for (int i = 0; i < evidence.Count; i++)
            {
                NarrativeEvidence row = evidence[i];
                if (row == null || row.pawnCanKnow != true
                    || row.facet != NarrativeFacetTokens.IdentityTransition)
                {
                    continue;
                }

                if ((!string.IsNullOrWhiteSpace(snapshot.familyArcId)
                        && row.arcKey == snapshot.familyArcId)
                    || (!string.IsNullOrWhiteSpace(snapshot.childId)
                        && row.subjectKind == NarrativeSubjectKindTokens.Pawn
                        && row.subjectId == snapshot.childId))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Fixed provider list. Empty calls are intentional N2 stubs: each later source wave replaces only
    /// its own row, and an absent DLC/provider remains the ordinary zero-candidate path.
    /// </summary>
    internal static class NarrativeProviderOrchestrator
    {
        public static List<NarrativeLensCandidate> Collect(
            List<NarrativeEvidence> evidence,
            List<NarrativeLensCandidate> coreCandidates,
            BiotechNarrativeSnapshot biotech)
        {
            List<NarrativeLensCandidate> result = new List<NarrativeLensCandidate>();
            Add(result, coreCandidates); // Core/synthetic fixtures remain first and deterministic.
            Add(result, RoyaltyCandidates());
            Add(result, IdeologyCandidates());
            Add(result, BiotechNarrativeProvider.Build(evidence, biotech));
            Add(result, AnomalyCandidates());
            Add(result, OdysseyCandidates());
            return result;
        }

        private static List<NarrativeLensCandidate> RoyaltyCandidates() => new List<NarrativeLensCandidate>();
        private static List<NarrativeLensCandidate> IdeologyCandidates() => new List<NarrativeLensCandidate>();
        private static List<NarrativeLensCandidate> AnomalyCandidates() => new List<NarrativeLensCandidate>();
        private static List<NarrativeLensCandidate> OdysseyCandidates() => new List<NarrativeLensCandidate>();

        private static void Add(List<NarrativeLensCandidate> destination, List<NarrativeLensCandidate> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null) destination.Add(source[i]);
            }
        }
    }
}
