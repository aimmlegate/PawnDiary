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
    internal static class ThoughtGainPatch
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
        /// Harmony Postfix for MemoryThoughtHandler.TryGainMemory. Fires after every TryGainMemory
        /// call — including ones vanilla rejected — and forwards only memories the pawn actually
        /// gained to the diary. __instance is the owning pawn's handler; __0 is the memory.
        /// </summary>
        public static void Postfix(MemoryThoughtHandler __instance, Thought_Memory __0)
        {
            // Hot hook: memories are gained constantly (meals, sleep, social slights). The
            // cheap guards stay outside the wrapper; the ThoughtSignal below is only built for
            // real, non-null memories.
            //
            // __0.pawn: a postfix also runs when TryGainMemory REJECTED the memory — vanilla's
            // accept-gates (ThoughtUtility.CanGetThought, the social-thought filters) early-return
            // BEFORE the line that assigns newThought.pawn. So a null thought.pawn means the pawn
            // never gained this thought: there is nothing to record, and building the signal anyway
            // crashed — ThoughtSignal calls thought.MoodOffset(), which reads the THOUGHT'S OWN
            // pawn field (not the handler pawn checked below) and dereferences it inside RimWorld's
            // nullifier chain. See the matching guard note in ThoughtSignal.
            if (__instance == null || __0 == null || __0.def == null || __0.pawn == null
                || __instance.pawn == null)
            {
                return;
            }

            DiaryPatchSafety.Run("ThoughtGainPatch", (pawn: __instance.pawn, memory: __0), s =>
            {
                DiaryEvents.Submit(new ThoughtSignal(s.pawn, s.memory));
            });
        }
    }
}
