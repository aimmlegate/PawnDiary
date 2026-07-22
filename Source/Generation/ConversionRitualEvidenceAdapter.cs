// Main-thread adapter for exact completed conversion-ritual evidence. It asks DlcContext for a
// detached organizer belief snapshot and the bounded mutation cache for the target's newest detached
// row. Any optional failure returns false so the already-authorized ordinary ritual page continues.
using System;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Captures event-time organizer role and target mutation facts without owning pages.</summary>
    internal static class ConversionRitualEvidenceAdapter
    {
        private static bool failForTests;

        public static bool TryCapture(
            Pawn organizer,
            Pawn target,
            int eventTick,
            ConversionRitualPolicySnapshot conversionPolicy,
            out BeliefSourcePreceptFact organizerRolePrecept,
            out BeliefMutationSnapshot targetMutation)
        {
            organizerRolePrecept = null;
            targetMutation = null;
            try
            {
                if (failForTests) throw new InvalidOperationException("RimTest conversion evidence fault");
                if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying
                    || conversionPolicy == null) return false;

                BeliefPolicySnapshot beliefPolicy = DiaryBeliefPolicy.Snapshot();
                if (!beliefPolicy.enabled) return true;

                BeliefSnapshot organizerSnapshot =
                    DlcContext.CaptureBeliefSnapshot(organizer, beliefPolicy);
                organizerRolePrecept = ExactOrganizerRole(
                    organizerSnapshot, conversionPolicy.organizerIdeologyRoleDefName);

                string targetId = target?.GetUniqueLoadID() ?? string.Empty;
                BeliefMutationSnapshot newest = BeliefMutationCache.PeekLatest(
                    targetId, eventTick, beliefPolicy);
                targetMutation = ConversionRitualPolicy.SelectTargetMutation(
                    targetId, organizerSnapshot?.ideologyId, eventTick, newest, conversionPolicy);
                return true;
            }
            catch (Exception exception)
            {
                Type type = exception.GetType();
                Log.WarningOnce(
                    "[Pawn Diary] Ideology conversion-ritual enrichment failed; the completed ritual "
                        + "keeps ordinary context: " + type.FullName + ": " + exception.Message,
                    ("PawnDiary.ConversionRitualEvidenceAdapter." + type.FullName).GetHashCode());
                organizerRolePrecept = null;
                targetMutation = null;
                return false;
            }
        }

        private static BeliefSourcePreceptFact ExactOrganizerRole(
            BeliefSnapshot snapshot, string expectedDefName)
        {
            if (snapshot?.precepts == null || string.IsNullOrWhiteSpace(expectedDefName)) return null;
            BeliefPreceptFact match = null;
            for (int i = 0; i < snapshot.precepts.Count; i++)
            {
                BeliefPreceptFact candidate = snapshot.precepts[i];
                if (candidate == null || !candidate.proselytizes
                    || !string.Equals(candidate.defName, expectedDefName, StringComparison.Ordinal))
                    continue;
                if (match != null) return null;
                match = candidate;
            }
            return match == null ? null : new BeliefSourcePreceptFact
            {
                instanceId = match.instanceId ?? string.Empty,
                defName = match.defName ?? string.Empty
            };
        }

        /// <summary>RimTest-only fault seam for the optional-enrichment fail-open contract.</summary>
        internal static void SetFailureForTests(bool value)
        {
            failForTests = value;
        }
    }
}
