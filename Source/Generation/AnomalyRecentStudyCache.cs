// Bounded, unsaved recent-study correlation for containment writer selection. This cache is
// deliberately separate from consume-once StudiedEntity Tale ownership: a Tale may consume its exact
// page claim immediately, while a later real breach can still truthfully identify the recent studier.
// Rows contain detached IDs and ticks only—never Pawn, Thing, Job, Def, Map, or comp references.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;

namespace PawnDiary
{
    /// <summary>Retains exact entity/researcher study relationships for a bounded recent window.</summary>
    internal static class AnomalyRecentStudyCache
    {
        private static readonly List<AnomalyRecentStudyFact> Studies =
            new List<AnomalyRecentStudyFact>();

        /// <summary>Adds or refreshes one exact detached relationship and enforces expiry/caps.</summary>
        internal static bool Register(
            AnomalyRecentStudyFact study,
            int nowTick,
            int maximumAgeTicks,
            int maximumStudies = AnomalyPolicyLimits.DefaultRecentStudies)
        {
            AnomalyRecentStudyFact normalized = Clone(study);
            if (!Valid(normalized) || nowTick < normalized.studiedTick) return false;
            int age = Window(maximumAgeTicks);
            Prune(nowTick, age);
            string identity = Identity(normalized);
            for (int i = Studies.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Identity(Studies[i]), identity, StringComparison.Ordinal))
                    Studies.RemoveAt(i);
            }

            Studies.Add(normalized);
            int cap = maximumStudies < 1
                    || maximumStudies > AnomalyPolicyLimits.MaximumRecentStudies
                ? AnomalyPolicyLimits.DefaultRecentStudies
                : maximumStudies;
            if (Studies.Count > cap) Studies.RemoveRange(0, Studies.Count - cap);
            return true;
        }

        /// <summary>Checks one exact entity/researcher pair without consuming the relationship.</summary>
        internal static bool Matches(
            string studiedEntityId,
            string studierPawnId,
            int nowTick,
            int maximumAgeTicks)
        {
            string entityId = (studiedEntityId ?? string.Empty).Trim();
            string pawnId = (studierPawnId ?? string.Empty).Trim();
            if (entityId.Length == 0 || pawnId.Length == 0 || nowTick < 0) return false;
            int age = Window(maximumAgeTicks);
            Prune(nowTick, age);
            for (int i = Studies.Count - 1; i >= 0; i--)
            {
                AnomalyRecentStudyFact row = Studies[i];
                if (string.Equals(row.studiedEntityId, entityId, StringComparison.Ordinal)
                    && string.Equals(row.studierPawnId, pawnId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the bounded set of recent researchers for one entity after one expiry pass. Live
        /// containment capture uses this batch form so a large map does not rescan the cache once per pawn.
        /// </summary>
        internal static HashSet<string> MatchingStudierPawnIds(
            string studiedEntityId,
            int nowTick,
            int maximumAgeTicks)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            string entityId = (studiedEntityId ?? string.Empty).Trim();
            if (entityId.Length == 0 || nowTick < 0) return result;

            Prune(nowTick, Window(maximumAgeTicks));
            for (int i = 0; i < Studies.Count; i++)
            {
                AnomalyRecentStudyFact row = Studies[i];
                if (string.Equals(row.studiedEntityId, entityId, StringComparison.Ordinal))
                    result.Add(row.studierPawnId);
            }

            return result;
        }

        /// <summary>Clears all relationships at a game/new-game/load boundary.</summary>
        internal static void Clear()
        {
            Studies.Clear();
        }

        internal static int CountForTests => Studies.Count;

        /// <summary>Returns detached clones so an in-game fixture can preserve the player's cache.</summary>
        internal static List<AnomalyRecentStudyFact> SnapshotForTests()
        {
            List<AnomalyRecentStudyFact> result = new List<AnomalyRecentStudyFact>();
            for (int i = 0; i < Studies.Count; i++) result.Add(Clone(Studies[i]));
            return result;
        }

        /// <summary>Restores only valid detached rows after an isolated fixture.</summary>
        internal static void RestoreForTests(List<AnomalyRecentStudyFact> snapshot)
        {
            Studies.Clear();
            if (snapshot == null) return;
            for (int i = 0;
                i < snapshot.Count && Studies.Count < AnomalyPolicyLimits.MaximumRecentStudies;
                i++)
            {
                AnomalyRecentStudyFact normalized = Clone(snapshot[i]);
                if (Valid(normalized)) Studies.Add(normalized);
            }
        }

        private static void Prune(int nowTick, int maximumAgeTicks)
        {
            for (int i = Studies.Count - 1; i >= 0; i--)
            {
                AnomalyRecentStudyFact row = Studies[i];
                long age = row == null ? long.MaxValue : (long)nowTick - row.studiedTick;
                if (!Valid(row) || age < 0 || age > maximumAgeTicks) Studies.RemoveAt(i);
            }
        }

        private static bool Valid(AnomalyRecentStudyFact study)
        {
            return study != null && study.studiedTick >= 0
                && !string.IsNullOrWhiteSpace(study.studierPawnId)
                && !string.IsNullOrWhiteSpace(study.studiedEntityId);
        }

        private static int Window(int value)
        {
            return value < 0 ? AnomalyPolicyLimits.DefaultRecentStudierMaxAgeTicks : value;
        }

        private static string Identity(AnomalyRecentStudyFact study)
        {
            return (study?.studiedEntityId ?? string.Empty) + "|"
                + (study?.studierPawnId ?? string.Empty);
        }

        private static AnomalyRecentStudyFact Clone(AnomalyRecentStudyFact source)
        {
            if (source == null) return null;
            return new AnomalyRecentStudyFact
            {
                studierPawnId = (source.studierPawnId ?? string.Empty).Trim(),
                studiedEntityId = (source.studiedEntityId ?? string.Empty).Trim(),
                studiedDefName = (source.studiedDefName ?? string.Empty).Trim(),
                studiedTick = source.studiedTick
            };
        }
    }
}
