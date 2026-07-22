// Fail-open runtime boundary for optional authority-speech evidence. The policy itself is pure; this
// adapter exists to contain unexpected modded/runtime faults so the ordinary ritual page always wins.
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>Creates detached POV evidence without retaining any game object.</summary>
    internal static class AuthoritySpeechEvidenceAdapter
    {
        private static bool failForTests;

        /// <summary>Snapshots and matches XML policy behind one fail-open runtime boundary.</summary>
        public static bool TryMatch(
            string ritualDefName,
            string behaviorWorkerClassName,
            string outcomeWorkerClassName,
            string effectiveGroupDefName,
            string assignedSpeakerRoleId,
            bool ideologyActive,
            bool royaltyActive,
            out AuthoritySpeechPolicySnapshot policy,
            out AuthoritySpeechRouteSnapshot route)
        {
            policy = null;
            route = null;
            try
            {
                if (failForTests) throw new InvalidOperationException("authority speech adapter test fault");
                AuthoritySpeechPolicySnapshot candidate = DiaryAuthoritySpeechPolicy.Snapshot();
                AuthoritySpeechRouteSnapshot matched = AuthoritySpeechPolicy.Match(
                    ritualDefName, behaviorWorkerClassName, outcomeWorkerClassName,
                    effectiveGroupDefName, assignedSpeakerRoleId, ideologyActive, royaltyActive,
                    candidate);
                if (matched == null) return false;
                policy = candidate;
                route = matched;
                return true;
            }
            catch (Exception exception)
            {
                Warn(exception);
                return false;
            }
        }

        public static BeliefEventEvidence Capture(
            string pawnId,
            int eventTick,
            string ritualDefName,
            string pawnLabel,
            string perspective,
            AuthoritySpeechRouteSnapshot route,
            AuthoritySpeechPolicySnapshot policy)
        {
            try
            {
                if (failForTests) throw new InvalidOperationException("authority speech adapter test fault");
                return AuthoritySpeechPolicy.EvidenceFor(
                    pawnId, eventTick, ritualDefName, pawnLabel, perspective, route, policy);
            }
            catch (Exception exception)
            {
                Warn(exception);
                return null;
            }
        }

        private static void Warn(Exception exception)
        {
            Type type = exception.GetType();
            Log.WarningOnce(
                "[Pawn Diary] Authority-speech belief enrichment failed; this page keeps ordinary "
                + "ritual context: " + type.FullName + ": " + exception.Message,
                ("PawnDiary.AuthoritySpeechEvidenceAdapter." + type.FullName).GetHashCode());
        }

        /// <summary>RimTest-only fault seam for the optional-enrichment fail-open contract.</summary>
        internal static void SetFailureForTests(bool value)
        {
            failForTests = value;
        }
    }
}
