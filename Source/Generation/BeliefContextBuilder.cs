// Impure Ideology Phase 1 orchestration. This is the one event-time bridge from a live Pawn to the
// pure resolver: it snapshots guarded doctrine through DlcContext, merges only plain recent history
// evidence, resolves exactly once, and returns a frozen full context block.
using System;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>One evaluated POV result, also carrying valence diagnostics needed by body-mod parity.</summary>
    internal sealed class BeliefContextBuildResult
    {
        public bool evaluated;
        public string fullContext = string.Empty;
        public BeliefStanceResolution resolution = new BeliefStanceResolution();

        public static BeliefContextBuildResult Empty(bool evaluated = false)
        {
            return new BeliefContextBuildResult { evaluated = evaluated };
        }
    }

    /// <summary>Builds one saved event-time belief block for one already-eligible POV.</summary>
    internal static class BeliefContextBuilder
    {
        public static BeliefContextBuildResult Build(
            Pawn pawn,
            BeliefEventEvidence sourceEvidence,
            string eventId,
            int eventTick,
            string povRole)
        {
            try
            {
                if (!ModsConfig.IdeologyActive || pawn == null || sourceEvidence == null
                    || !DiaryGameComponent.IsDiaryEligible(pawn))
                    return BeliefContextBuildResult.Empty();

                BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
                if (!policy.enabled) return BeliefContextBuildResult.Empty();

                string pawnId = pawn.GetUniqueLoadID();
                BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForPov(
                    sourceEvidence, eventId, eventTick, pawnId, povRole);
                BeliefEventEvidenceFactory.AddHistoryDefNames(evidence,
                    BeliefHistoryCorrelationCache.NearbyDefNames(pawnId, eventTick, policy));
                if (!BeliefEventEvidenceFactory.HasUsefulVisibleEvidence(evidence))
                    return BeliefContextBuildResult.Empty();

                BeliefSnapshot snapshot = DlcContext.CaptureBeliefSnapshot(pawn, policy);
                if (!snapshot.ideologyActive) return BeliefContextBuildResult.Empty();

                return Resolve(snapshot, evidence, eventId, pawnId, policy);
            }
            catch (Exception exception)
            {
                // Belief context is optional enrichment. A broken modded getter or malformed Def must
                // not unwind through the source factory before its ordinary diary event is registered.
                Type type = exception.GetType();
                Log.WarningOnce(
                    "[Pawn Diary] Ideology belief-context enrichment failed; this page keeps ordinary context: "
                    + type.FullName + ": " + exception.Message,
                    ("PawnDiary.BeliefContextBuilder." + type.FullName).GetHashCode());
                return BeliefContextBuildResult.Empty();
            }
        }

        /// <summary>
        /// Resolves a developer preview from an already-detached snapshot. This exists so the suite
        /// can derive its synthetic evidence and final context from one live capture without retaining
        /// an Ideo/Precept or evaluating the resolver twice.
        /// </summary>
        public static BeliefContextBuildResult BuildSyntheticPreview(
            BeliefSnapshot snapshot,
            BeliefEventEvidence evidence,
            string eventId,
            string pawnId,
            BeliefPolicySnapshot policy)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            if (!effective.enabled || snapshot?.ideologyActive != true
                || !BeliefEventEvidenceFactory.HasUsefulVisibleEvidence(evidence))
                return BeliefContextBuildResult.Empty();
            return Resolve(snapshot, evidence, eventId, pawnId, effective);
        }

        private static BeliefContextBuildResult Resolve(
            BeliefSnapshot snapshot,
            BeliefEventEvidence evidence,
            string eventId,
            string pawnId,
            BeliefPolicySnapshot policy)
        {
            BeliefStanceResolution resolution = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = snapshot,
                    evidence = evidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment,
                    deterministicSeed = HumorChancePolicy.StableSeed(eventId, pawnId)
                });
            return new BeliefContextBuildResult
            {
                evaluated = true,
                resolution = resolution,
                fullContext = BeliefContextFormatter.Format(
                    resolution, NarrativeDetailLevelTokens.Full, policy)
            };
        }
    }
}
