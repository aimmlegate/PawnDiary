// Pure bounded correlation storage for recently observed HistoryEvents. The Harmony observer first
// projects live game arguments into BeliefHistoryObservation, then this class keeps only strings and
// ticks. It cannot create diary pages and has no RimWorld/Verse/Unity dependency.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stores short-lived history identities and returns exact nearby matches per pawn.</summary>
    internal sealed class BeliefHistoryCorrelationBuffer
    {
        private readonly List<BeliefHistoryObservation> observations =
            new List<BeliefHistoryObservation>();

        /// <summary>Number of retained plain observations, exposed for focused diagnostics/tests.</summary>
        public int Count => observations.Count;

        /// <summary>
        /// Adds one sanitized observation, pruning stale and oldest rows so the cache stays bounded.
        /// Empty events and rows without a visible pawn are ignored.
        /// </summary>
        public void Observe(
            BeliefHistoryObservation observation,
            int currentTick,
            int maximumEntries,
            int windowTicks)
        {
            int cap = Math.Max(1, Math.Min(2048, maximumEntries));
            int window = Math.Max(0, Math.Min(600, windowTicks));
            Prune(currentTick, window);
            BeliefHistoryObservation copy = Copy(observation);
            if (copy == null) return;

            observations.Add(copy);
            if (observations.Count > cap)
                observations.RemoveRange(0, observations.Count - cap);
        }

        /// <summary>
        /// Returns deduplicated event Def identities observed for the exact POV pawn close to the
        /// event tick. Reading does not consume rows because one HistoryEvent can legitimately support
        /// two independently authorized POV pages.
        /// </summary>
        public List<string> NearbyDefNames(string pawnId, int eventTick, int windowTicks)
        {
            List<string> result = new List<string>();
            string wantedPawn = SafeId(pawnId);
            if (wantedPawn.Length == 0) return result;

            int window = Math.Max(0, Math.Min(600, windowTicks));
            Prune(eventTick, window);
            for (int i = 0; i < observations.Count; i++)
            {
                BeliefHistoryObservation row = observations[i];
                if (row == null || Math.Abs((long)eventTick - row.tick) > window
                    || !Contains(row.visiblePawnIds, wantedPawn)) continue;
                AddUnique(result, row.historyEventDefName, 32);
            }
            return result;
        }

        /// <summary>Clears process-static state at every game boundary.</summary>
        public void Clear()
        {
            observations.Clear();
        }

        private void Prune(int currentTick, int windowTicks)
        {
            for (int i = observations.Count - 1; i >= 0; i--)
            {
                BeliefHistoryObservation row = observations[i];
                if (row == null || currentTick >= row.tick && currentTick - (long)row.tick > windowTicks)
                    observations.RemoveAt(i);
            }
        }

        private static BeliefHistoryObservation Copy(BeliefHistoryObservation source)
        {
            string defName = SafeId(source?.historyEventDefName);
            if (source == null || defName.Length == 0 || source.visiblePawnIds == null)
                return null;

            BeliefHistoryObservation result = new BeliefHistoryObservation
            {
                tick = Math.Max(0, source.tick),
                historyEventDefName = defName
            };
            for (int i = 0; i < source.visiblePawnIds.Count; i++)
                AddUnique(result.visiblePawnIds, source.visiblePawnIds[i], 16);
            return result.visiblePawnIds.Count == 0 ? null : result;
        }

        private static bool Contains(List<string> values, string wanted)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], wanted, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void AddUnique(List<string> target, string value, int cap)
        {
            if (target == null || target.Count >= cap) return;
            string cleaned = SafeId(value);
            if (cleaned.Length == 0) return;
            for (int i = 0; i < target.Count; i++)
                if (string.Equals(target[i], cleaned, StringComparison.OrdinalIgnoreCase)) return;
            target.Add(cleaned);
        }

        private static string SafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length > 160) trimmed = trimmed.Substring(0, 160);
            for (int i = 0; i < trimmed.Length; i++)
                if (char.IsControl(trimmed[i]) || char.IsWhiteSpace(trimmed[i])) return string.Empty;
            return trimmed;
        }
    }
}
