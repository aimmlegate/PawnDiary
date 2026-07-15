// Event-time adapter for Narrative Continuity. Future DLC sources supply already-authorized, plain
// evidence and their own guarded factual candidates; this builder snapshots XML policy on the main
// thread and calls the pure selector. N1 deliberately ships no real DLC provider or hook: the only
// candidate lane is the synthetic/core fixture used by tests and the future N2 source seam.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety" and "architecture barriers").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Plain event-time input for the Narrative Continuity adapter. It contains no Pawn, Def, map,
    /// tracker, or provider object, so future source adapters can capture their live data before
    /// constructing it.
    /// </summary>
    internal class NarrativeContextBuildRequest
    {
        public string eventId = string.Empty;
        public int eventTick;
        public string povPawnId = string.Empty;
        public string povRole = string.Empty;
        public List<NarrativeEvidence> evidence = new List<NarrativeEvidence>();
        // N1 has no live provider registry. Tests provide core fixture candidates through this plain list;
        // N2 will replace that narrow seam with source-owned guarded provider adapters.
        public List<NarrativeLensCandidate> coreCandidates = new List<NarrativeLensCandidate>();
        public List<string> recentSelectedCandidateKeys = new List<string>();
        public PromptContextDetailLevel contextDetailLevel = PromptContextDetailLevel.Full;
        public int deterministicSeed = 1;
    }

    /// <summary>Plain result applied to one saved POV slot after the selector has completed.</summary>
    internal class NarrativeContextBuildResult
    {
        public List<NarrativeEvidence> evidence = new List<NarrativeEvidence>();
        public NarrativeContextSelection selection = new NarrativeContextSelection();
    }

    /// <summary>
    /// Main-thread boundary that turns an authorized event snapshot into a pure selection request. A
    /// malformed Def or selector failure logs once and returns empty context; it never cancels the page
    /// that the source already authorized.
    /// </summary>
    internal static class NarrativeContextBuilder
    {
        /// <summary>
        /// Builds one frozen per-POV context result. Empty/unknown evidence returns an empty selection
        /// without consulting a provider, which is the normal N1/no-DLC path.
        /// </summary>
        public static NarrativeContextBuildResult Build(NarrativeContextBuildRequest request)
        {
            NarrativeContextBuildResult result = new NarrativeContextBuildResult();
            if (request == null)
            {
                return result;
            }

            NarrativePolicySnapshot policy;
            try
            {
                // Def access and DefInjected localization are main-thread work. The pure selector below
                // sees only this copied snapshot.
                policy = DiaryNarrativeContinuityPolicy.Snapshot();
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Narrative Continuity policy snapshot failed; this page keeps ordinary context: "
                    + exception,
                    "PawnDiary.NarrativeContextBuilder.Policy".GetHashCode());
                return result;
            }

            result.evidence = NarrativePersistencePolicy.NormalizeEvidence(
                request.evidence,
                request.eventId,
                request.eventTick,
                request.povPawnId,
                request.povRole,
                policy.maxEvidencePerPov);
            if (result.evidence.Count == 0)
            {
                return result;
            }

            try
            {
                result.selection = NarrativeContextSelector.Select(new NarrativeContextRequest
                {
                    evidence = result.evidence,
                    candidates = request.coreCandidates ?? new List<NarrativeLensCandidate>(),
                    policy = policy,
                    currentTick = request.eventTick,
                    deterministicSeed = request.deterministicSeed,
                    recentSelectedCandidateKeys = NarrativePersistencePolicy.NormalizeSelectedCandidateKeys(
                        request.recentSelectedCandidateKeys,
                        policy.maxRecentSelectedCandidateKeys),
                    detailLevel = DetailLevelFor(request.contextDetailLevel)
                });
            }
            catch (Exception exception)
            {
                // Selection is optional enrichment. A malformed synthetic/future provider candidate must
                // degrade to the ordinary entry rather than aborting its source-owned event.
                Log.ErrorOnce(
                    "[Pawn Diary] Narrative Continuity selection failed; this page keeps ordinary context: "
                    + exception,
                    "PawnDiary.NarrativeContextBuilder.Select".GetHashCode());
            }

            return result;
        }

        private static string DetailLevelFor(PromptContextDetailLevel detailLevel)
        {
            switch (PromptContextSelector.Normalize(detailLevel))
            {
                case PromptContextDetailLevel.Compact:
                    return NarrativeDetailLevelTokens.Compact;
                case PromptContextDetailLevel.Balanced:
                    return NarrativeDetailLevelTokens.Balanced;
                default:
                    return NarrativeDetailLevelTokens.Full;
            }
        }
    }
}
