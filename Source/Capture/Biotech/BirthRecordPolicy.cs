// Pure persistence policy for canonical Biotech births. Runtime adapters supply saved event fields
// and current naming state; this file validates pending rows, chooses a deterministic duplicate, and
// decides when waiting can no longer improve the child's event-time display name.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Matches one durable diary row to an exact canonical family birth.</summary>
    internal static class BirthRecordPolicy
    {
        /// <summary>Requires the synthetic defName plus exact family-arc and child identifiers.</summary>
        public static bool Matches(
            string interactionDefName,
            string recordedFamilyArcId,
            string recordedChildId,
            string expectedFamilyArcId,
            string expectedChildId)
        {
            string arcId = Clean(expectedFamilyArcId);
            string childId = Clean(expectedChildId);
            return arcId.Length > 0
                && childId.Length > 0
                && string.Equals(interactionDefName, BiotechEventDefNames.FamilyBirth, StringComparison.Ordinal)
                && string.Equals(Clean(recordedFamilyArcId), arcId, StringComparison.Ordinal)
                && string.Equals(Clean(recordedChildId), childId, StringComparison.Ordinal);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }

    /// <summary>Normalizes saved naming ownership and decides its bounded flush point.</summary>
    internal static class PendingBiotechBirthPolicy
    {
        /// <summary>
        /// Drops malformed/future rows, de-duplicates by family arc, caps writers at two, applies only
        /// the requested defensive truncation ceiling, and returns stable birth-tick/arc ordering. Live
        /// callers use the fixed hard ceiling here and enforce the XML limit before admitting a new owner,
        /// so normal load/maintenance never discards already-claimed work.
        /// </summary>
        public static List<PendingBiotechBirthState> Normalize(
            IList<PendingBiotechBirthState> source,
            int currentTick,
            int maximumRows = BiotechPendingOwnershipLimits.DefaultMaximumRows)
        {
            int now = Math.Max(0, currentTick);
            Dictionary<string, PendingBiotechBirthState> byArc =
                new Dictionary<string, PendingBiotechBirthState>(StringComparer.Ordinal);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    PendingBiotechBirthState row = NormalizeRow(source[i], now);
                    if (row == null)
                    {
                        continue;
                    }

                    string arcId = row.snapshot.familyArcId;
                    PendingBiotechBirthState existing;
                    if (!byArc.TryGetValue(arcId, out existing)
                        || Compare(row, existing) < 0)
                    {
                        byArc[arcId] = row;
                    }
                }
            }

            List<PendingBiotechBirthState> result =
                new List<PendingBiotechBirthState>(byArc.Values);
            result.Sort(Compare);
            int rowCap = BiotechPendingOwnershipLimits.NormalizeMaximumRows(maximumRows);
            if (result.Count > rowCap)
            {
                // Compare sorts oldest first. In an extreme/corrupt save, discard the oldest overflow
                // and keep the newest ownership rows; output remains in stable chronological order.
                result.RemoveRange(0, result.Count - rowCap);
            }
            return result;
        }

        /// <summary>
        /// Flushes on accepted naming, an elapsed naming deadline, loss of one remaining writer, or
        /// after the XML grace when the child is dead/unavailable and waiting cannot improve the name.
        /// </summary>
        public static bool ShouldFlush(
            PendingBiotechBirthState pending,
            BirthChildNamingState current,
            int currentTick,
            int unavailableGraceTicks,
            int currentWriterCount = -1)
        {
            if (pending?.snapshot == null)
            {
                return false;
            }

            int now = Math.Max(0, currentTick);
            int grace = Math.Max(0, unavailableGraceTicks);
            int since = Math.Max(pending.createdTick, pending.snapshot.birthTick);
            int expectedWriters = pending.writers?.writers?.Count ?? 0;
            if (currentWriterCount > 0 && currentWriterCount < expectedWriters)
            {
                // The frozen pair can no longer be emitted truthfully. Use the current name and the
                // remaining exact adult now rather than waiting for naming while eligibility decays.
                return true;
            }
            if (expectedWriters > 0 && currentWriterCount == 0
                && now >= since && now - since >= grace)
            {
                // No truthful POV remains. Wake the component so it can discard the orphaned owner
                // after the same bounded recovery interval instead of retaining it indefinitely.
                return true;
            }

            if (current != null && current.found)
            {
                if (current.namingDeadline == -1)
                {
                    return true;
                }

                if (current.namingDeadline >= 0 && now >= current.namingDeadline)
                {
                    return true;
                }

                if (!current.dead)
                {
                    return false;
                }
            }

            return now >= since && now - since >= grace;
        }

        private static PendingBiotechBirthState NormalizeRow(
            PendingBiotechBirthState row,
            int currentTick)
        {
            BirthMutationSnapshot snapshot = row?.snapshot;
            if (snapshot == null)
            {
                return null;
            }

            snapshot.familyArcId = Clean(snapshot.familyArcId);
            snapshot.childId = Clean(snapshot.childId);
            snapshot.currentChildName = BiotechContextText.Clean(snapshot.currentChildName);
            snapshot.correlationId = Clean(snapshot.correlationId);
            if (snapshot.familyArcId.Length == 0
                || snapshot.childId.Length == 0
                || !BiotechBirthOutcomeTokens.IsKnown(snapshot.outcomeToken)
                || !BiotechBirthMethodTokens.IsKnown(snapshot.methodToken)
                || snapshot.birthTick < 0
                || (currentTick > 0 && snapshot.birthTick > currentTick))
            {
                return null;
            }

            snapshot.namingDeadline = snapshot.namingDeadline < -1 ? -1 : snapshot.namingDeadline;
            snapshot.namingResolved = snapshot.namingResolved || snapshot.namingDeadline == -1;
            row.createdTick = Math.Max(snapshot.birthTick, Math.Min(currentTick, Math.Max(0, row.createdTick)));
            row.writers = NormalizeWriters(row.writers);
            row.eventContext = NormalizeEventContext(row.eventContext, snapshot.birthTick, row.writers);
            return row.writers.writers.Count == 0 ? null : row;
        }

        private static BirthEventContextSnapshot NormalizeEventContext(
            BirthEventContextSnapshot source,
            int birthTick,
            BirthWriterSelection writers)
        {
            if (source == null)
            {
                // Legacy pending rows did not save event-time context. Runtime emission preserves the
                // birth tick and falls back to live context only for those pre-fix saves.
                return null;
            }

            BirthEventContextSnapshot result = new BirthEventContextSnapshot
            {
                birthTick = Math.Max(0, birthTick),
                birthDate = source.birthDate ?? string.Empty
            };
            List<BirthWriterContextSnapshot> rows = source.writers;
            if (rows == null || writers?.writers == null)
            {
                return result;
            }

            HashSet<string> accepted = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < writers.writers.Count; i++)
            {
                string writerId = Clean(writers.writers[i]?.pawnId);
                if (writerId.Length > 0)
                {
                    accepted.Add(writerId);
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count && result.writers.Count < 2; i++)
            {
                BirthWriterContextSnapshot row = rows[i];
                string pawnId = Clean(row?.pawnId);
                if (pawnId.Length == 0 || !accepted.Contains(pawnId) || !seen.Add(pawnId))
                {
                    continue;
                }

                result.writers.Add(new BirthWriterContextSnapshot
                {
                    pawnId = pawnId,
                    displayName = BiotechContextText.Clean(row.displayName),
                    pawnSummary = row.pawnSummary ?? string.Empty,
                    surroundings = row.surroundings ?? string.Empty,
                    continuity = row.continuity ?? string.Empty,
                    pairContinuity = row.pairContinuity ?? string.Empty,
                    lastOpener = row.lastOpener ?? string.Empty,
                    previousEntryEnding = row.previousEntryEnding ?? string.Empty,
                    weapon = row.weapon ?? string.Empty,
                    staggeredIntensity = Math.Max(0, Math.Min(4, row.staggeredIntensity)),
                    textDecorationFacts = row.textDecorationFacts ?? string.Empty,
                    skipFirstPersonGeneration = row.skipFirstPersonGeneration
                });
            }

            return result;
        }

        private static BirthWriterSelection NormalizeWriters(BirthWriterSelection source)
        {
            BirthWriterSelection result = new BirthWriterSelection();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<BirthWriterFact> rows = source?.writers;
            if (rows == null)
            {
                return result;
            }

            for (int i = 0; i < rows.Count && result.writers.Count < 2; i++)
            {
                BirthWriterFact writer = rows[i];
                string id = Clean(writer?.pawnId);
                string role = Clean(writer?.roleToken);
                if (id.Length == 0 || !IsWriterRole(role) || !ids.Add(id))
                {
                    continue;
                }

                result.writers.Add(new BirthWriterFact
                {
                    pawnId = id,
                    displayName = BiotechContextText.Clean(writer.displayName),
                    roleToken = role
                });
            }

            return result;
        }

        private static bool IsWriterRole(string role)
        {
            return role == BiotechFamilyRoleTokens.Birther
                || role == BiotechFamilyRoleTokens.GeneticMother
                || role == BiotechFamilyRoleTokens.Father;
        }

        private static int Compare(PendingBiotechBirthState left, PendingBiotechBirthState right)
        {
            int value = left.snapshot.birthTick.CompareTo(right.snapshot.birthTick);
            if (value != 0)
            {
                return value;
            }

            return string.CompareOrdinal(left.snapshot.familyArcId, right.snapshot.familyArcId);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
