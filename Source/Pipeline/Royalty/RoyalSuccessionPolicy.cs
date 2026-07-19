// Pure Royalty Phase-5 succession policy. It accepts only an exact candidate plus the enclosing
// death commit, then matches delayed title callbacks without retaining any RimWorld object.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Validates, commits, matches, expires, deduplicates, and caps succession facts.</summary>
    internal static class RoyalSuccessionPolicy
    {
        public static RoyalSuccessionFact Commit(
            RoyalSuccessionCandidateSnapshot candidate,
            RoyalSuccessionCommitObservation observation,
            int lifetimeTicks)
        {
            if (!ValidCandidate(candidate) || observation == null
                || candidate.heirAlreadyHeldEqualOrHigherTitle || !observation.wasInherited
                || !Eq(candidate.correlationId, observation.correlationId)
                || !Eq(candidate.deceasedPawnId, observation.deceasedPawnId)
                || !Eq(candidate.factionId, observation.factionId)
                || !Eq(candidate.inheritedTitleDefName, observation.inheritedTitleDefName)
                || observation.commitTick < candidate.candidateTick) return null;

            int lifetime = lifetimeTicks > 0 ? lifetimeTicks : 2500;
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
                candidateTick = candidate.candidateTick,
                commitTick = observation.commitTick,
                expiresTick = SafeAdd(observation.commitTick, lifetime)
            };
        }

        public static bool MatchesMutation(
            RoyalSuccessionFact fact,
            RoyalTitleMutationSnapshot mutation,
            string correlationId,
            int now)
        {
            if (!ValidFact(fact, now) || mutation == null || mutation.newTitle == null
                || !Eq(fact.correlationId, correlationId)
                || !Eq(fact.heirPawnId, mutation.pawnId)
                || !Eq(fact.factionId, mutation.factionId)
                || !Eq(fact.inheritedTitleDefName, mutation.newTitle.titleDefName)) return false;
            string previous = mutation.previousTitle?.titleDefName ?? string.Empty;
            return string.IsNullOrEmpty(fact.previousHeirTitleDefName)
                ? string.IsNullOrEmpty(previous)
                : Eq(fact.previousHeirTitleDefName, previous);
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
                if (!ValidFact(row, now)) continue;
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
                && SafeId(value.factionId) && SafeId(value.inheritedTitleDefName)
                && value.candidateTick >= 0 && value.inheritedTitleSeniority >= 0;
        }

        private static bool ValidFact(RoyalSuccessionFact value, int now)
        {
            return value != null && SafeCorrelation(value.correlationId)
                && SafeId(value.deceasedPawnId) && SafeId(value.heirPawnId)
                && SafeId(value.factionId) && SafeId(value.inheritedTitleDefName)
                && value.candidateTick >= 0 && value.commitTick >= value.candidateTick
                && value.expiresTick >= value.commitTick
                && now >= value.commitTick && now <= value.expiresTick;
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
                candidateTick = value.candidateTick, commitTick = value.commitTick,
                expiresTick = value.expiresTick, pageClaimed = value.pageClaimed,
                titleMutationClaimed = value.titleMutationClaimed
            };
        }

        private static int SafeAdd(int value, int amount)
        {
            long result = (long)value + amount;
            return result > int.MaxValue ? int.MaxValue : (int)result;
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
