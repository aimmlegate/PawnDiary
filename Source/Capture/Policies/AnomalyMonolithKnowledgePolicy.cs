// Pure ownership for one saved monolith-study snapshot and the next exact activation boundary.
// Runtime pawn lookup, labels, Scribe mutation, and event-window emission remain in adapters.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Consumes the next activation once and exposes study context only while still current.</summary>
    internal static class AnomalyMonolithKnowledgePolicy
    {
        /// <summary>Returns consume/attach ownership for one saved snapshot and exact activation.</summary>
        public static AnomalyMonolithKnowledgeDecision Decide(
            AnomalyMonolithKnowledgeSnapshot snapshot,
            AnomalyMonolithActivationFacts activation,
            int maximumAgeTicks)
        {
            AnomalyMonolithKnowledgeDecision result = new AnomalyMonolithKnowledgeDecision();
            if (activation == null || activation.tick < 0)
            {
                return result;
            }

            result.previousLevelDefName = Clean(activation.previousLevelDefName);
            result.reachedLevelDefName = Clean(activation.reachedLevelDefName);
            if (result.previousLevelDefName.Length == 0 || result.reachedLevelDefName.Length == 0
                || string.Equals(result.previousLevelDefName, result.reachedLevelDefName,
                    StringComparison.Ordinal))
            {
                return result;
            }

            if (snapshot == null || snapshot.consumed || snapshot.tick < 0
                || activation.tick < snapshot.tick)
            {
                return result;
            }

            // The first later activation owns this snapshot even when it has aged out. Consuming an
            // expired row prevents it from leaking into a still-later monolith chapter.
            result.consume = true;
            int ageLimit = maximumAgeTicks < 0 ? 0 : maximumAgeTicks;
            long age = (long)activation.tick - snapshot.tick;
            string researcher = Clean(snapshot.researcherPawnId);
            string stage = CleanStage(snapshot.studyStage);
            if (age > ageLimit || researcher.Length == 0
                || (stage.Length == 0 && !snapshot.becameActivatable))
            {
                return result;
            }

            result.attach = true;
            result.researcherPawnId = researcher;
            result.studyStage = stage;
            result.becameActivatable = snapshot.becameActivatable;
            return result;
        }

        private static string Clean(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            return clean.Length > 0 && clean.Length <= 200
                && clean.IndexOf('|') < 0 && clean.IndexOf(';') < 0
                && clean.IndexOf('=') < 0 && clean.IndexOf('\r') < 0 && clean.IndexOf('\n') < 0
                ? clean
                : string.Empty;
        }

        private static string CleanStage(string value)
        {
            string clean = Clean(value);
            return clean == AnomalyStudyStageTokens.FirstBreakthrough
                    || clean == AnomalyStudyStageTokens.CompletedKind
                    || clean == AnomalyStudyStageTokens.Promoted
                ? clean
                : string.Empty;
        }
    }
}
