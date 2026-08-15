// Pure projection for removing one pawn's delayed Biotech autobiography at a memory-reset boundary.
// Growth choices have one writer, while a pending birth can retain a second adult's independent POV;
// this helper operates only on detached save DTOs and never reads live RimWorld or DLC state.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Removes exact writer ownership without discarding another adult's birth memory.</summary>
    internal static class BiotechPendingWriterResetPolicy
    {
        /// <summary>Returns every pending growth row except rows owned by the exact wiped pawn.</summary>
        public static List<PendingBiotechGrowthMoment> RemoveGrowthWriter(
            IList<PendingBiotechGrowthMoment> source,
            string writerPawnId)
        {
            List<PendingBiotechGrowthMoment> result = new List<PendingBiotechGrowthMoment>();
            if (source == null)
            {
                return result;
            }

            string targetId = Clean(writerPawnId);
            for (int i = 0; i < source.Count; i++)
            {
                PendingBiotechGrowthMoment row = source[i];
                if (targetId.Length == 0
                    || row == null
                    || !string.Equals(Clean(row.pawnId), targetId, StringComparison.Ordinal))
                {
                    result.Add(row);
                }
            }

            return result;
        }

        /// <summary>
        /// Removes the exact adult from each pending birth's writer list and event-time contexts.
        /// A row survives with its original shared birth facts when another truthful writer remains;
        /// a target-only row is discarded because it has no autobiographical owner left.
        /// </summary>
        public static List<PendingBiotechBirthState> RemoveBirthWriter(
            IList<PendingBiotechBirthState> source,
            string writerPawnId)
        {
            List<PendingBiotechBirthState> result = new List<PendingBiotechBirthState>();
            if (source == null)
            {
                return result;
            }

            string targetId = Clean(writerPawnId);
            for (int i = 0; i < source.Count; i++)
            {
                PendingBiotechBirthState row = source[i];
                if (targetId.Length == 0)
                {
                    result.Add(row);
                    continue;
                }

                // An owner with no writer can never produce a truthful first-person page. Loaded state
                // is normally normalized before this boundary, but drop a malformed orphan defensively.
                if (row?.writers?.writers == null || row.writers.writers.Count == 0)
                {
                    continue;
                }

                bool removedTarget = false;
                BirthWriterSelection survivingWriters = new BirthWriterSelection();
                HashSet<string> survivingIds = new HashSet<string>(StringComparer.Ordinal);
                for (int writerIndex = 0; writerIndex < row.writers.writers.Count; writerIndex++)
                {
                    BirthWriterFact writer = row.writers.writers[writerIndex];
                    string writerId = Clean(writer?.pawnId);
                    if (string.Equals(writerId, targetId, StringComparison.Ordinal))
                    {
                        removedTarget = true;
                        continue;
                    }

                    // Loaded rows are normalized already. This defensive check prevents a corrupt null
                    // placeholder from keeping a target-only birth owner alive after its real writer left.
                    if (writerId.Length > 0)
                    {
                        survivingWriters.writers.Add(writer);
                        survivingIds.Add(writerId);
                    }
                }

                if (!removedTarget)
                {
                    result.Add(row);
                    continue;
                }

                if (survivingWriters.writers.Count == 0)
                {
                    continue;
                }

                result.Add(new PendingBiotechBirthState
                {
                    snapshot = row.snapshot,
                    writers = survivingWriters,
                    eventContext = ProjectEventContext(row.eventContext, survivingIds),
                    createdTick = row.createdTick
                });
            }

            return result;
        }

        /// <summary>Retains only event-time context rows belonging to surviving exact writers.</summary>
        private static BirthEventContextSnapshot ProjectEventContext(
            BirthEventContextSnapshot source,
            HashSet<string> survivingIds)
        {
            if (source == null)
            {
                return null;
            }

            BirthEventContextSnapshot result = new BirthEventContextSnapshot
            {
                birthTick = source.birthTick,
                birthDate = source.birthDate
            };
            if (source.writers == null || survivingIds == null || survivingIds.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < source.writers.Count; i++)
            {
                BirthWriterContextSnapshot context = source.writers[i];
                if (context != null && survivingIds.Contains(Clean(context.pawnId)))
                {
                    result.writers.Add(context);
                }
            }

            return result;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
