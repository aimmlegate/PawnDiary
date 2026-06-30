// Pure retention math for per-pawn diary-style caps. Given each pawn's ordered id/key list (newest id
// at the end) and a per-pawn limit, it works out how many of each pawn's oldest pages fall past the cap
// and which ids still survive somewhere. Active retention uses this survivor set to keep shared hot
// events until every pawn has dropped them; archive retention uses it to keep each pawn's newest
// compact archive rows. No RimWorld / Verse / Unity types live here, which is exactly what lets a plain
// console harness exercise the decision without loading the game (tests/DiaryRetentionTests).
//
// New to C#/this repo? "pure" here means the method only reads its inputs and returns a result object;
// it never mutates the inputs, touches game state/settings, or writes the save. The impure caller
// (DiaryGameComponent.EventRetention) takes the returned plan and applies it to the live lists. See
// AGENTS.md ("architecture barriers") for why the decision and the mutation are kept apart.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Outcome of planning one per-pawn retention pass: how many oldest pages each pawn drops, and the
    /// union of ids/keys still referenced by some pawn afterwards.
    /// </summary>
    public sealed class DiaryRetentionResult
    {
        /// <summary>True when at least one pawn was over the cap and has pages to drop.</summary>
        public bool TrimmedAny;

        /// <summary>
        /// Parallel to the planned input: how many ids to remove from the front (oldest end) of each
        /// pawn's list. 0 means that pawn keeps everything. Always non-null.
        /// </summary>
        public int[] DropCounts;

        /// <summary>
        /// Ids/keys still referenced by at least one pawn after the planned drops. Empty when nothing
        /// is trimmed (the caller then skips the sweep). Compared case-insensitively, so a shared id
        /// survives until every pawn that held it has dropped it.
        /// </summary>
        public HashSet<string> Referenced;
    }

    /// <summary>
    /// Plans a per-pawn diary history cap. Kept separate from the component/repository so the
    /// trim-and-keep decision can be unit-tested without RimWorld.
    /// </summary>
    public static class DiaryRetentionPlan
    {
        /// <summary>
        /// Plans a retention pass without mutating the inputs. Each pawn keeps its newest
        /// <paramref name="perPawnLimit"/> ids (the oldest sit at the front of each list); the survivor
        /// union is reported so a page shared by two pawns survives until both have dropped it. A null
        /// input or negative limit plans nothing. <see cref="DiaryRetentionResult.DropCounts"/> lines up
        /// index-for-index with <paramref name="perPawnEventIds"/>, including null entries (treated as
        /// empty), so the caller can apply drops by position.
        /// </summary>
        public static DiaryRetentionResult Plan(
            IReadOnlyList<IReadOnlyList<string>> perPawnEventIds, int perPawnLimit)
        {
            int pawnCount = perPawnEventIds == null ? 0 : perPawnEventIds.Count;
            DiaryRetentionResult result = new DiaryRetentionResult
            {
                TrimmedAny = false,
                DropCounts = new int[pawnCount],
                Referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };

            if (perPawnEventIds == null || perPawnLimit < 0)
            {
                return result;
            }

            // 1. Work out each pawn's overflow (how many oldest ids fall past the cap).
            for (int i = 0; i < pawnCount; i++)
            {
                IReadOnlyList<string> ids = perPawnEventIds[i];
                if (ids == null)
                {
                    continue;
                }

                int overflow = ids.Count - perPawnLimit;
                if (overflow > 0)
                {
                    result.DropCounts[i] = overflow;
                    result.TrimmedAny = true;
                }
            }

            if (!result.TrimmedAny)
            {
                return result;
            }

            // 2. Collect survivors (each pawn's ids from its drop count onward) so the caller can sweep
            //    the master store down to exactly this set. Blank ids are ignored.
            for (int i = 0; i < pawnCount; i++)
            {
                IReadOnlyList<string> ids = perPawnEventIds[i];
                if (ids == null)
                {
                    continue;
                }

                for (int j = result.DropCounts[i]; j < ids.Count; j++)
                {
                    string id = ids[j];
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result.Referenced.Add(id);
                    }
                }
            }

            return result;
        }
    }
}
