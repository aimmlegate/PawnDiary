// Thought-related Harmony patches. ThoughtGainPatch is registered defensively because RimWorld's
// memory-gain overload is a fragile reflection target and should not be allowed to break PatchAll.
// New to this? See AGENTS.md ("Harmony patches").
using System.Reflection;
using HarmonyLib;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Fires when a pawn gains a temporary thought (Thought_Memory). We only record thoughts
    // that have an expiration (durationDays > 0), filtering out permanent traits and
    // low-magnitude thoughts. The patch catches all temporary mood thoughts at the moment
    // they are added to the pawn's memory collection. We hook the Thought_Memory overload of
    // TryGainMemory because the ThoughtDef overload delegates to it, so this one catches both.
    /// <summary>
    /// Defensively captures gained temporary memories through MemoryThoughtHandler.TryGainMemory.
    /// </summary>
    public static class ThoughtGainPatch
    {
        /// <summary>
        /// Patches MemoryThoughtHandler.TryGainMemory(Thought_Memory, Pawn), if it can still be
        /// found. Safe to call even when the method name has changed: it logs and skips.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            MethodBase target = AccessTools.Method(
                typeof(MemoryThoughtHandler), "TryGainMemory",
                new[] { typeof(Thought_Memory), typeof(Pawn) });
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Could not find MemoryThoughtHandler.TryGainMemory(Thought_Memory, Pawn); "
                    + "thought diary entries are disabled (a RimWorld update likely renamed it).");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ThoughtGainPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Harmony Postfix for MemoryThoughtHandler.TryGainMemory. Fires after a memory thought is
        /// gained by a pawn. Forwards temporary thoughts (Thought_Memory with expiration) to the
        /// diary. __instance is the owning pawn's handler; __0 is the gained memory.
        /// </summary>
        public static void Postfix(MemoryThoughtHandler __instance, Thought_Memory __0)
        {
            DiaryPatchSafety.Run("ThoughtGainPatch", () =>
            {
                if (__instance == null || __0 == null || __0.def == null)
                {
                    return;
                }

                Pawn pawn = __instance.pawn;
                if (pawn == null)
                {
                    return;
                }

                DiaryEvents.Submit(new ThoughtSignal(pawn, __0));
            });
        }
    }
}
