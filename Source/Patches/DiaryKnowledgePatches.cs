// Knowledge-capture Harmony patches (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §2.1): the closed-list
// gameplay signals that have no diary event of their own — ideological role changes, ideology
// conversion (also the adopted-culture switch, §4.1), and implant/prosthetic removal. Death
// fan-out and quiet-hediff capture ride the existing death/health patches instead.
//
// All targets are base-game types compiled into Assembly-CSharp regardless of DLC ownership
// (AGENTS.md "DLC-safety"): Precept_Role/Pawn_IdeoTracker hooks simply never fire without
// Ideology content. Capture is NOT gated by the player's memory switch — that switch controls
// prompt injection only (§3.2).
//
// New to this? See AGENTS.md ("Harmony patches"). PatchAll discovers these via [HarmonyPatch].
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Ideological role appointment — capture-only, no diary page (§2.1).</summary>
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.Notify_PawnAssigned))]
    internal static class PreceptRoleAssignedKnowledgePatch
    {
        public static void Postfix(Precept_Role __instance, Pawn newPawn)
        {
            if (newPawn == null || __instance == null || !DiaryGameComponent.GamePlaying)
            {
                return;
            }

            DiaryPatchSafety.Run("PreceptRoleAssignedKnowledgePatch", () =>
            {
                DiaryGameComponent.Instance?.CaptureRoleKnowledge(
                    newPawn, __instance.LabelCap, __instance.ideo?.name, true);
            });
        }
    }

    /// <summary>Ideological role removal — capture-only, no diary page (§2.1).</summary>
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.Notify_PawnUnassigned))]
    internal static class PreceptRoleUnassignedKnowledgePatch
    {
        public static void Postfix(Precept_Role __instance, Pawn oldPawn)
        {
            if (oldPawn == null || __instance == null || !DiaryGameComponent.GamePlaying)
            {
                return;
            }

            DiaryPatchSafety.Run("PreceptRoleUnassignedKnowledgePatch", () =>
            {
                DiaryGameComponent.Instance?.CaptureRoleKnowledge(
                    oldPawn, __instance.LabelCap, __instance.ideo?.name, false);
            });
        }
    }

    /// <summary>
    /// Ideology conversion (§2.1): SetIdeo is the single sink for both initial assignment and
    /// conversion — an old ideo that is non-null and different proves a conversion. Independent
    /// of the belief-mutation patches so knowledge capture works even with belief context off.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
    internal static class IdeoConversionKnowledgePatch
    {
        /// <summary>Old ideo captured before the swap; null state means "not a conversion".</summary>
        public static void Prefix(Pawn_IdeoTracker __instance, Ideo ideo, out Ideo __state)
        {
            __state = null;
            if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying)
            {
                return;
            }

            Ideo previous = __instance?.Ideo;
            if (previous != null && ideo != null && previous != ideo)
            {
                __state = previous;
            }
        }

        public static void Postfix(Pawn_IdeoTracker __instance, Ideo ideo, Ideo __state)
        {
            if (__state == null || ideo == null)
            {
                return;
            }

            DiaryPatchSafety.Run("IdeoConversionKnowledgePatch", () =>
            {
                Pawn pawn = TrackerPawn(__instance);
                if (pawn == null)
                {
                    return;
                }

                DiaryGameComponent.Instance?.CaptureIdeoConversionKnowledge(
                    pawn, __state.name, ideo.name, ideo.culture?.defName);
            });
        }

        // Pawn_IdeoTracker keeps its pawn in a private field; resolve it once. A null accessor
        // (field renamed by a game update) disables this capture without breaking anything else.
        private static AccessTools.FieldRef<Pawn_IdeoTracker, Pawn> pawnField;
        private static bool pawnFieldSearched;

        private static Pawn TrackerPawn(Pawn_IdeoTracker tracker)
        {
            if (tracker == null)
            {
                return null;
            }

            if (!pawnFieldSearched)
            {
                pawnFieldSearched = true;
                try
                {
                    pawnField = AccessTools.FieldRefAccess<Pawn_IdeoTracker, Pawn>("pawn");
                }
                catch (System.Exception)
                {
                    pawnField = null;
                    Log.WarningOnce("[Pawn Diary] Pawn_IdeoTracker.pawn changed; conversion "
                        + "knowledge capture is disabled.",
                        "PawnDiary.Knowledge.IdeoTrackerPawn".GetHashCode());
                }
            }

            return pawnField != null ? pawnField(tracker) : null;
        }
    }

    /// <summary>
    /// Implant/prosthetic removal (§2.1): RemoveHediff has no diary page, so the knowledge
    /// channel listens directly. Cheap type-narrowing first — most removals are wounds/buffs.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    internal static class HediffRemovedKnowledgePatch
    {
        public static void Postfix(Hediff hediff)
        {
            if (hediff?.def == null || !hediff.def.countsAsAddedPartOrImplant
                || hediff.pawn == null || !DiaryGameComponent.GamePlaying)
            {
                return;
            }

            DiaryPatchSafety.Run("HediffRemovedKnowledgePatch", () =>
            {
                DiaryGameComponent.Instance?.CaptureHediffKnowledge(
                    hediff.pawn,
                    hediff.def.defName,
                    hediff.def.label,
                    hediff.Part?.def?.defName,
                    hediff.Part?.LabelCap,
                    true,
                    true);
            });
        }
    }
}
