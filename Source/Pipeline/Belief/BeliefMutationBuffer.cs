// Pure, bounded coalescing storage for short-lived Ideology mutations. Nested vanilla mutation calls
// overlap in sequence space and merge into one earliest-before/latest-after row; sequential actions do
// not. Reads are non-consuming because two already-authorized POV pages may share the same event fact.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stores detached mutation facts and correlates them by exact pawn identity and tick.</summary>
    internal sealed class BeliefMutationBuffer
    {
        private readonly List<BeliefMutationSnapshot> rows = new List<BeliefMutationSnapshot>();

        public int Count => rows.Count;

        /// <summary>Adds or merges one overlapping call chain, then enforces age and count bounds.</summary>
        public void RecordOrMerge(
            BeliefMutationSnapshot mutation,
            int currentTick,
            int maximumEntries,
            int windowTicks)
        {
            int cap = Math.Max(1, Math.Min(2048, maximumEntries));
            int window = Math.Max(0, Math.Min(600, windowTicks));
            Prune(currentTick, window);
            BeliefMutationSnapshot copy = Copy(mutation);
            if (copy == null) return;

            // One outer call can contain several completed sibling calls already stored as separate
            // rows. Keep widening the accumulator and rescan until every transitive overlap belongs
            // to one earliest-before/latest-after row.
            BeliefMutationSnapshot merged = copy;
            bool foundOverlap;
            do
            {
                foundOverlap = false;
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    BeliefMutationSnapshot existing = rows[i];
                    if (!CanCoalesce(existing, merged)) continue;
                    MergeInto(existing, merged);
                    rows.RemoveAt(i);
                    merged = existing;
                    foundOverlap = true;
                }
            }
            while (foundOverlap);

            if (!merged.observedMutation) return;
            rows.Add(merged);
            if (rows.Count > cap) rows.RemoveRange(0, rows.Count - cap);
        }

        /// <summary>Returns the newest exact-pawn row inside the policy window without consuming it.</summary>
        public BeliefMutationSnapshot PeekLatest(string pawnId, int eventTick, int windowTicks)
        {
            string wanted = SafeId(pawnId);
            if (wanted.Length == 0) return null;
            int window = Math.Max(0, Math.Min(600, windowTicks));
            Prune(eventTick, window);
            BeliefMutationSnapshot best = null;
            for (int i = 0; i < rows.Count; i++)
            {
                BeliefMutationSnapshot row = rows[i];
                if (row == null || !string.Equals(row.pawnId, wanted, StringComparison.Ordinal)
                    || Math.Abs((long)eventTick - row.capturedTick) > window) continue;
                if (best == null || row.completedSequence > best.completedSequence) best = row;
            }
            return Copy(best);
        }

        /// <summary>Clears all transient rows at a game/test boundary.</summary>
        public void Clear()
        {
            rows.Clear();
        }

        private void Prune(int currentTick, int windowTicks)
        {
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                BeliefMutationSnapshot row = rows[i];
                // Absolute distance also removes rows stranded in the "future" after a tick reset,
                // save rollback, or test fixture clock regression.
                if (row == null || Math.Abs((long)currentTick - row.capturedTick) > windowTicks)
                    rows.RemoveAt(i);
            }
        }

        private static bool CanCoalesce(BeliefMutationSnapshot left, BeliefMutationSnapshot right)
        {
            return left != null && right != null
                && left.capturedTick == right.capturedTick
                && string.Equals(left.pawnId, right.pawnId, StringComparison.Ordinal)
                && left.startedSequence > 0 && left.completedSequence >= left.startedSequence
                && right.startedSequence > 0 && right.completedSequence >= right.startedSequence
                && left.startedSequence <= right.completedSequence
                && right.startedSequence <= left.completedSequence;
        }

        private static void MergeInto(BeliefMutationSnapshot target, BeliefMutationSnapshot source)
        {
            if (source.startedSequence < target.startedSequence)
            {
                target.startedSequence = source.startedSequence;
                target.beforeIdeologyId = source.beforeIdeologyId;
                target.beforeIdeologyName = source.beforeIdeologyName;
                target.hasBeforeCertainty = source.hasBeforeCertainty;
                target.beforeCertainty = source.beforeCertainty;
            }
            if (source.completedSequence > target.completedSequence)
            {
                target.completedSequence = source.completedSequence;
                target.afterIdeologyId = source.afterIdeologyId;
                target.afterIdeologyName = source.afterIdeologyName;
                target.hasAfterCertainty = source.hasAfterCertainty;
                target.afterCertainty = source.afterCertainty;
            }
            if (source.attemptedIdeologyId.Length > 0)
            {
                target.attemptedIdeologyId = source.attemptedIdeologyId;
                target.attemptedIdeologyName = source.attemptedIdeologyName;
            }
            if (source.conversionSucceeded.HasValue)
                target.conversionSucceeded = source.conversionSucceeded;
            AddUnique(target.causeTokens, source.causeTokens);
            BeliefMutationPolicy.RefreshDerivedFacts(target);
        }

        private static BeliefMutationSnapshot Copy(BeliefMutationSnapshot source)
        {
            string pawnId = SafeId(source?.pawnId);
            if (source == null || pawnId.Length == 0 || !source.HasUsefulFact) return null;
            BeliefMutationSnapshot result = new BeliefMutationSnapshot
            {
                pawnId = pawnId,
                capturedTick = Math.Max(0, source.capturedTick),
                beforeIdeologyId = SafeId(source.beforeIdeologyId),
                beforeIdeologyName = SafeText(source.beforeIdeologyName),
                afterIdeologyId = SafeId(source.afterIdeologyId),
                afterIdeologyName = SafeText(source.afterIdeologyName),
                attemptedIdeologyId = SafeId(source.attemptedIdeologyId),
                attemptedIdeologyName = SafeText(source.attemptedIdeologyName),
                hasBeforeCertainty = source.hasBeforeCertainty,
                beforeCertainty = Clamp01(source.beforeCertainty),
                hasAfterCertainty = source.hasAfterCertainty,
                afterCertainty = Clamp01(source.afterCertainty),
                ideologyChanged = source.ideologyChanged,
                certaintyChanged = source.certaintyChanged,
                conversionSucceeded = source.conversionSucceeded,
                startedSequence = Math.Max(0L, source.startedSequence),
                completedSequence = Math.Max(0L, source.completedSequence),
                observedMutation = source.observedMutation
            };
            AddUnique(result.causeTokens, source.causeTokens);
            return result;
        }

        private static void AddUnique(List<string> target, List<string> source)
        {
            if (target == null || source == null) return;
            for (int i = 0; i < source.Count && target.Count < 16; i++)
            {
                string value = SafeId(source[i]);
                if (!BeliefMutationCauseTokens.IsKnown(value)) continue;
                bool found = false;
                for (int j = 0; j < target.Count; j++)
                    if (string.Equals(target[j], value, StringComparison.Ordinal)) { found = true; break; }
                if (!found) target.Add(value);
            }
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

        private static string SafeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            return trimmed.Length <= 320 ? trimmed : trimmed.Substring(0, 320);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
