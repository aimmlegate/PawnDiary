// Impure edge for the external-override arbitration (see Source/Pipeline/ExternalOverrideArbitration.cs):
// resolves an integration sourceId to the owning mod's LOAD-ORDER index, so "the later-loading mod wins"
// can be decided. A sourceId is a packageId only BY CONVENTION (the Adapter Contract recommends it), so a
// string that matches no active mod resolves to UnknownLoadOrder and arbitration falls back to
// last-writer-wins.
//
// Threading + lifetime: only reached from the main-thread-gated Set*Override API paths, so the caches
// need no lock. They are deliberately NOT reset in GameComponent.FinalizeInit (the usual static-cache
// rule): the mod list and its order are fixed for the whole process, so a process-lifetime cache is
// correct — unlike per-colony state, nothing here can go stale across save/load.
//
// New to C#/RimWorld? See AGENTS.md in the repo root ("statics leak across exit-to-menu" — and why
// this one is exempt).
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Resolves cleaned integration sourceIds to active-mod load-order indexes, cached per process.
    /// Main thread only (callers are the main-thread-gated external override setters).
    /// </summary>
    internal static class ExternalSourceLoadOrder
    {
        // cleaned sourceId -> load-order index (or UnknownLoadOrder). Misses are cached too: the mod
        // list never changes at runtime, so a miss now is a miss forever this session.
        private static readonly Dictionary<string, int> IndexCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // "slot|caller|owner" keys already logged, so a losing adapter retrying every pass (the
        // intended handoff mechanism) produces one line per pairing, not a stream.
        private static readonly HashSet<string> LoggedRejections =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The load-order index of the active mod whose packageId equals <paramref name="cleanedSourceId"/>
        /// (case-insensitive, matching either the internal or player-facing id form), or
        /// <see cref="ExternalOverrideArbitration.UnknownLoadOrder"/> when no active mod matches.
        /// </summary>
        internal static int IndexFor(string cleanedSourceId)
        {
            if (string.IsNullOrWhiteSpace(cleanedSourceId))
            {
                return ExternalOverrideArbitration.UnknownLoadOrder;
            }

            int cached;
            if (IndexCache.TryGetValue(cleanedSourceId, out cached))
            {
                return cached;
            }

            int index = ExternalOverrideArbitration.UnknownLoadOrder;
            List<ModContentPack> running = LoadedModManager.RunningModsListForReading;
            if (running != null)
            {
                for (int i = 0; i < running.Count; i++)
                {
                    ModContentPack mod = running[i];
                    if (mod == null)
                    {
                        continue;
                    }

                    // PackageId is the normalized lowercase id; PackageIdPlayerFacing keeps the author's
                    // casing. Compare against both so either spelling of the convention resolves.
                    if (string.Equals(mod.PackageId, cleanedSourceId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(mod.PackageIdPlayerFacing, cleanedSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }

            IndexCache[cleanedSourceId] = index;
            return index;
        }

        /// <summary>
        /// Logs — once per slot/caller/owner pairing per session — that a write was rejected because an
        /// earlier-loading mod tried to displace a later-loading owner. A quiet message, not a warning:
        /// this is the arbitration working as designed, surfaced so adapter authors can see who won.
        /// </summary>
        internal static void LogKeptOwnerOnce(string slotName, string callerSourceId, string ownerSourceId)
        {
            string key = slotName + "|" + callerSourceId + "|" + ownerSourceId;
            if (!LoggedRejections.Add(key))
            {
                return;
            }

            Log.Message(
                "[Pawn Diary] Integration API: '" + callerSourceId + "' asked to set the external "
                + slotName + " override, but '" + ownerSourceId + "' loads later in the mod list and keeps"
                + " the slot. Reorder the mod list to change which integration owns this voice layer.");
        }
    }
}
