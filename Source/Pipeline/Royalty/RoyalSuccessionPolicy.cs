// Pure Royalty Phase-5 succession policy. It accepts only an exact candidate plus the enclosing
// death commit, then matches delayed title callbacks without retaining any RimWorld object.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Validates, commits, advances, invalidates, deduplicates, and caps succession facts.</summary>
    internal static class RoyalSuccessionPolicy
    {
        public static RoyalSuccessionFact Commit(
            RoyalSuccessionCandidateSnapshot candidate,
            RoyalSuccessionCommitObservation observation)
        {
            if (!ValidCandidate(candidate) || observation == null
                || candidate.heirAlreadyHeldEqualOrHigherTitle || !observation.wasInherited
                || !Eq(candidate.correlationId, observation.correlationId)
                || !Eq(candidate.deceasedPawnId, observation.deceasedPawnId)
                || !Eq(candidate.factionId, observation.factionId)
                || !Eq(candidate.inheritedTitleDefName, observation.inheritedTitleDefName)
                || observation.commitTick < candidate.candidateTick) return null;

            return new RoyalSuccessionFact
            {
                correlationId = Clean(candidate.correlationId),
                deceasedPawnId = CleanId(candidate.deceasedPawnId),
                deceasedPawnName = CleanText(candidate.deceasedPawnName),
                heirPawnId = CleanId(candidate.heirPawnId),
                heirPawnName = CleanText(candidate.heirPawnName),
                factionId = CleanId(candidate.factionId),
                factionName = CleanText(candidate.factionName),
                inheritedTitleDefName = CleanId(candidate.inheritedTitleDefName),
                inheritedTitleLabel = CleanText(candidate.inheritedTitleLabel),
                inheritedTitleSeniority = Math.Max(0, candidate.inheritedTitleSeniority),
                previousHeirTitleDefName = CleanId(candidate.previousHeirTitleDefName),
                previousHeirTitleLabel = CleanText(candidate.previousHeirTitleLabel),
                previousHeirTitleSeniority = Math.Max(-1, candidate.previousHeirTitleSeniority),
                currentHeirTitleDefName = CleanId(candidate.previousHeirTitleDefName),
                currentHeirTitleLabel = CleanText(candidate.previousHeirTitleLabel),
                currentHeirTitleSeniority = Math.Max(-1, candidate.previousHeirTitleSeniority),
                candidateTick = candidate.candidateTick,
                commitTick = observation.commitTick,
                // Bestowing offers have no vanilla acceptance deadline. Keep the bounded proof until
                // its monotonic title chain reaches the exact target or a contradictory edge retires it.
                expiresTick = int.MaxValue
            };
        }

        /// <summary>Returns whether an in-flight title callback is compatible with this candidate's chain.</summary>
        public static bool MatchesCandidateMutation(
            RoyalSuccessionCandidateSnapshot candidate,
            RoyalTitleMutationSnapshot mutation)
        {
            if (!ValidCandidate(candidate) || mutation == null
                || !Eq(candidate.heirPawnId, mutation.pawnId)
                || !Eq(candidate.factionId, mutation.factionId)) return false;
            RoyalSuccessionMutationDisposition disposition = ClassifyTitleStep(
                candidate.previousHeirTitleDefName,
                candidate.previousHeirTitleSeniority,
                candidate.inheritedTitleDefName,
                candidate.inheritedTitleSeniority,
                mutation);
            return IsClaim(disposition);
        }

        /// <summary>
        /// Classifies one same-pawn/faction mutation against the saved monotonic inheritance chain.
        /// A mismatched predecessor retires the proof instead of letting an old death claim a later,
        /// independent promotion that merely happens to end at the same title.
        /// </summary>
        public static RoyalSuccessionMutationDisposition ClassifyMutation(
            RoyalSuccessionFact fact,
            RoyalTitleMutationSnapshot mutation,
            string correlationId,
            int now)
        {
            if (!ValidFact(fact, now) || mutation == null
                || !Eq(fact.correlationId, correlationId)
                || !Eq(fact.heirPawnId, mutation.pawnId)
                || !Eq(fact.factionId, mutation.factionId))
                return RoyalSuccessionMutationDisposition.Unrelated;
            return ClassifyTitleStep(
                CursorDefName(fact), CursorSeniority(fact),
                fact.inheritedTitleDefName, fact.inheritedTitleSeniority,
                mutation);
        }

        public static bool MatchesMutation(
            RoyalSuccessionFact fact,
            RoyalTitleMutationSnapshot mutation,
            string correlationId,
            int now)
        {
            return IsClaim(ClassifyMutation(fact, mutation, correlationId, now));
        }

        /// <summary>Returns a detached copy whose chain cursor has consumed one compatible mutation.</summary>
        public static RoyalSuccessionFact AdvanceMutation(
            RoyalSuccessionFact fact,
            RoyalTitleMutationSnapshot mutation,
            int now)
        {
            RoyalSuccessionMutationDisposition disposition = ClassifyMutation(
                fact, mutation, fact?.correlationId, now);
            if (!IsClaim(disposition)) return null;
            RoyalSuccessionFact advanced = Copy(fact);
            advanced.currentHeirTitleDefName = CleanId(mutation.newTitle?.titleDefName);
            advanced.currentHeirTitleLabel = CleanText(mutation.newTitle?.titleLabel);
            advanced.currentHeirTitleSeniority = Math.Max(-1, mutation.newTitle?.seniority ?? -1);
            advanced.titleMutationClaimed = disposition == RoyalSuccessionMutationDisposition.ClaimTarget;
            advanced.expiresTick = int.MaxValue;
            return advanced;
        }

        /// <summary>Compares the exact detached edge identity used by the transient duplicate cache.</summary>
        public static bool SameMutation(RoyalTitleMutationSnapshot left, RoyalTitleMutationSnapshot right)
        {
            return left != null && right != null
                && left.tick == right.tick
                && Eq(left.pawnId, right.pawnId)
                && Eq(left.factionId, right.factionId)
                && Eq(left.previousTitle?.titleDefName, right.previousTitle?.titleDefName)
                && Eq(left.newTitle?.titleDefName, right.newTitle?.titleDefName);
        }

        public static bool ValidAppointment(RoyalHeirAppointmentSnapshot appointment)
        {
            return appointment != null && appointment.sourceToken == "change_royal_heir_quest"
                && SafeId(appointment.titleHolderPawnId) && SafeId(appointment.heirPawnId)
                && SafeId(appointment.factionId) && SafeId(appointment.titleDefName)
                && appointment.observedTick >= 0
                && !Eq(appointment.previousHeirPawnId, appointment.heirPawnId);
        }

        public static List<RoyalSuccessionFact> Normalize(
            IList<RoyalSuccessionFact> source,
            int now,
            int requestedCap)
        {
            int cap = requestedCap < 1 || requestedCap > 256 ? 64 : requestedCap;
            List<RoyalSuccessionFact> rows = new List<RoyalSuccessionFact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < (source?.Count ?? 0); i++)
            {
                RoyalSuccessionFact row = source[i];
                // titleMutationClaimed was scribed by the first Phase-5 implementation. Treat such
                // legacy rows as terminal and remove them; pending rows migrate to chain persistence.
                if (!ValidFact(row, now) || row.titleMutationClaimed) continue;
                string key = EdgeKey(row);
                if (key.Length == 0 || !seen.Add(key)) continue;
                rows.Add(Copy(row));
            }
            rows.Sort((left, right) =>
            {
                int tick = right.commitTick.CompareTo(left.commitTick);
                return tick != 0 ? tick : string.CompareOrdinal(EdgeKey(left), EdgeKey(right));
            });
            if (rows.Count > cap) rows.RemoveRange(cap, rows.Count - cap);
            rows.Sort((left, right) =>
            {
                int tick = left.commitTick.CompareTo(right.commitTick);
                return tick != 0 ? tick : string.CompareOrdinal(EdgeKey(left), EdgeKey(right));
            });
            return rows;
        }

        public static string EdgeKey(RoyalSuccessionFact fact)
        {
            if (fact == null) return string.Empty;
            return CleanId(fact.deceasedPawnId) + "|" + CleanId(fact.heirPawnId) + "|"
                + CleanId(fact.factionId) + "|" + CleanId(fact.inheritedTitleDefName) + "|"
                + fact.commitTick;
        }

        private static bool ValidCandidate(RoyalSuccessionCandidateSnapshot value)
        {
            return value != null && SafeCorrelation(value.correlationId)
                && SafeId(value.deceasedPawnId) && SafeId(value.heirPawnId)
                && !Eq(value.deceasedPawnId, value.heirPawnId)
                && SafeId(value.factionId) && SafeId(value.inheritedTitleDefName)
                && value.candidateTick >= 0 && value.inheritedTitleSeniority >= 0;
        }

        private static bool ValidFact(RoyalSuccessionFact value, int now)
        {
            return value != null && SafeCorrelation(value.correlationId)
                && SafeId(value.deceasedPawnId) && SafeId(value.heirPawnId)
                && !Eq(value.deceasedPawnId, value.heirPawnId)
                && SafeId(value.factionId) && SafeId(value.inheritedTitleDefName)
                && value.candidateTick >= 0 && value.commitTick >= value.candidateTick
                && value.expiresTick >= value.commitTick
                // Old Phase-5 rows used expiresTick as a one-hour deadline. It is retained only as
                // additive save-schema data; pending chain ownership now ends by evidence, not time.
                && now >= value.commitTick;
        }

        private static RoyalSuccessionFact Copy(RoyalSuccessionFact value)
        {
            return new RoyalSuccessionFact
            {
                correlationId = Clean(value.correlationId), deceasedPawnId = CleanId(value.deceasedPawnId),
                deceasedPawnName = CleanText(value.deceasedPawnName), heirPawnId = CleanId(value.heirPawnId),
                heirPawnName = CleanText(value.heirPawnName), factionId = CleanId(value.factionId),
                factionName = CleanText(value.factionName), inheritedTitleDefName = CleanId(value.inheritedTitleDefName),
                inheritedTitleLabel = CleanText(value.inheritedTitleLabel),
                inheritedTitleSeniority = Math.Max(0, value.inheritedTitleSeniority),
                previousHeirTitleDefName = CleanId(value.previousHeirTitleDefName),
                previousHeirTitleLabel = CleanText(value.previousHeirTitleLabel),
                previousHeirTitleSeniority = Math.Max(-1, value.previousHeirTitleSeniority),
                currentHeirTitleDefName = CursorDefName(value),
                currentHeirTitleLabel = CursorLabel(value),
                currentHeirTitleSeniority = CursorSeniority(value),
                candidateTick = value.candidateTick, commitTick = value.commitTick,
                expiresTick = int.MaxValue, pageClaimed = value.pageClaimed,
                titleMutationClaimed = value.titleMutationClaimed
            };
        }

        private static RoyalSuccessionMutationDisposition ClassifyTitleStep(
            string cursorDefName,
            int cursorSeniority,
            string targetDefName,
            int targetSeniority,
            RoyalTitleMutationSnapshot mutation)
        {
            if (mutation?.newTitle == null || !SafeId(mutation.newTitle.titleDefName))
                return RoyalSuccessionMutationDisposition.Invalidate;
            string previousDefName = mutation.previousTitle?.titleDefName ?? string.Empty;
            if (!Eq(cursorDefName, previousDefName))
                return RoyalSuccessionMutationDisposition.Invalidate;

            string newDefName = mutation.newTitle.titleDefName;
            if (Eq(newDefName, targetDefName) && !Eq(newDefName, previousDefName))
                return RoyalSuccessionMutationDisposition.ClaimTarget;

            int previousSeniority = mutation.previousTitle?.seniority ?? Math.Max(-1, cursorSeniority);
            int nextSeniority = mutation.newTitle.seniority;
            if (nextSeniority <= previousSeniority || nextSeniority >= targetSeniority)
                return RoyalSuccessionMutationDisposition.Invalidate;
            return RoyalSuccessionMutationDisposition.ClaimIntermediate;
        }

        private static bool IsClaim(RoyalSuccessionMutationDisposition disposition)
        {
            return disposition == RoyalSuccessionMutationDisposition.ClaimIntermediate
                || disposition == RoyalSuccessionMutationDisposition.ClaimTarget;
        }

        private static string CursorDefName(RoyalSuccessionFact value)
        {
            string current = CleanId(value?.currentHeirTitleDefName);
            if (current.Length > 0 || string.IsNullOrWhiteSpace(value?.previousHeirTitleDefName)) return current;
            return CleanId(value.previousHeirTitleDefName);
        }

        private static string CursorLabel(RoyalSuccessionFact value)
        {
            string current = CleanText(value?.currentHeirTitleLabel);
            if (current.Length > 0 || string.IsNullOrWhiteSpace(value?.previousHeirTitleLabel)) return current;
            return CleanText(value.previousHeirTitleLabel);
        }

        private static int CursorSeniority(RoyalSuccessionFact value)
        {
            if (value == null) return -1;
            if (!string.IsNullOrWhiteSpace(value.currentHeirTitleDefName)
                || string.IsNullOrWhiteSpace(value.previousHeirTitleDefName))
                return Math.Max(-1, value.currentHeirTitleSeniority);
            return Math.Max(-1, value.previousHeirTitleSeniority);
        }

        private static bool Eq(string left, string right)
        {
            return string.Equals(Clean(left), Clean(right), StringComparison.Ordinal);
        }

        private static bool SafeId(string value) { return CleanId(value).Length > 0; }
        private static bool SafeCorrelation(string value)
        {
            string cleaned = Clean(value);
            return cleaned.Length > 0 && cleaned.IndexOf(';') < 0;
        }
        private static string CleanId(string value)
        {
            string cleaned = Clean(value);
            return cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0 ? string.Empty : cleaned;
        }
        private static string CleanText(string value)
        {
            string cleaned = Clean(value).Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
            return cleaned.Length <= 120 ? cleaned : cleaned.Substring(0, 120).TrimEnd();
        }
        private static string Clean(string value) { return (value ?? string.Empty).Trim(); }
    }
}
