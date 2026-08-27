// Knowledge-capture Harmony patches (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §2.1): the closed-list
// gameplay signals that have no diary event of their own — ideological role changes, ideology
// conversion (also the adopted-culture switch, §4.1), implant/prosthetic removal, plus M6's
// shadow-only social/faction dirty seams. Death fan-out and quiet-hediff capture ride the existing
// death/health patches instead.
//
// All targets are base-game types compiled into Assembly-CSharp regardless of DLC ownership
// (AGENTS.md "DLC-safety"): Precept_Role/Pawn_IdeoTracker hooks simply never fire without
// Ideology content. Capture is NOT gated by the player's memory switch — that switch controls
// prompt injection only (§3.2).
//
// New to this? See AGENTS.md ("Harmony patches"). PatchAll discovers these via [HarmonyPatch].
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Resolves Pawn_RelationsTracker's private owner once. A separate searched flag is required:
    /// caching only a null FieldRef would repeat Harmony's full reflection scan on every relation.
    /// </summary>
    internal static class KnowledgeObservationPatchAccess
    {
        private static AccessTools.FieldRef<Pawn_RelationsTracker, Pawn> pawnField;
        private static bool pawnFieldSearched;

        /// <summary>Returns the tracker owner, or null when RimWorld changed the private field.</summary>
        public static Pawn TrackerPawn(Pawn_RelationsTracker tracker)
        {
            if (tracker == null) return null;
            if (!pawnFieldSearched)
            {
                pawnFieldSearched = true;
                try
                {
                    pawnField = AccessTools.FieldRefAccess<Pawn_RelationsTracker, Pawn>("pawn");
                }
                catch (System.Exception)
                {
                    pawnField = null;
                    Log.WarningOnce(
                        "[Pawn Diary] Pawn_RelationsTracker.pawn changed; shadow social "
                        + "observation will rely on periodic reconciliation.",
                        "PawnDiary.MemoryObservation.RelationsPawn".GetHashCode());
                }
            }
            return pawnField == null ? null : pawnField(tracker);
        }
    }

    /// <summary>Formal relation addition dirties both exact directed snapshots.</summary>
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation),
        new[] { typeof(PawnRelationDef), typeof(Pawn) })]
    internal static class MemoryObservationRelationAddedPatch
    {
        /// <summary>Queues both directed views after vanilla commits the relation.</summary>
        public static void Postfix(Pawn_RelationsTracker __instance, Pawn otherPawn)
        {
            if (!DiaryGameComponent.GamePlaying || otherPawn == null) return;
            DiaryPatchSafety.Run("MemoryObservationRelationAddedPatch", () =>
            {
                DiaryGameComponent.Instance?.MarkMemoryObservationPairDirty(
                    KnowledgeObservationPatchAccess.TrackerPawn(__instance), otherPawn);
            });
        }
    }

    /// <summary>Only a committed formal relation removal dirties the directed snapshots.</summary>
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.TryRemoveDirectRelation),
        new[] { typeof(PawnRelationDef), typeof(Pawn) })]
    internal static class MemoryObservationRelationRemovedPatch
    {
        /// <summary>Queues both directed views only when vanilla reports a successful removal.</summary>
        public static void Postfix(
            Pawn_RelationsTracker __instance,
            Pawn otherPawn,
            bool __result)
        {
            if (!__result || !DiaryGameComponent.GamePlaying || otherPawn == null) return;
            DiaryPatchSafety.Run("MemoryObservationRelationRemovedPatch", () =>
            {
                DiaryGameComponent.Instance?.MarkMemoryObservationPairDirty(
                    KnowledgeObservationPatchAccess.TrackerPawn(__instance), otherPawn);
            });
        }
    }

    /// <summary>Records exact old/new faction instances after Pawn.SetFaction commits.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction),
        new[] { typeof(Faction), typeof(Pawn) })]
    internal static class MemoryObservationPawnFactionPatch
    {
        // The loaded-game RimTest fixture must make a generated pawn a colonist before most tests can
        // begin. That setup-only SetFaction call is not an observed game event, so the trusted test
        // assembly suppresses this one patch briefly instead of clearing the player's real pending queue.
        private static bool suppressedForTests;

        /// <summary>
        /// Lets the trusted RimTest harness exclude its own setup-only faction transition. Always pair
        /// <c>true</c> with <c>false</c> in a <c>finally</c> block so ordinary play cannot stay suppressed.
        /// </summary>
        internal static void SetSuppressedForTests(bool suppressed)
        {
            suppressedForTests = suppressed;
        }

        /// <summary>Freezes the old exact faction reference before vanilla changes it.</summary>
        public static void Prefix(Pawn __instance, out Faction __state)
        {
            __state = __instance?.Faction;
        }

        /// <summary>Queues exact old/new faction and related-owner reconciliation after commit.</summary>
        public static void Postfix(Pawn __instance, Faction __state)
        {
            if (suppressedForTests || __instance == null || !DiaryGameComponent.GamePlaying
                || __state == __instance.Faction) return;
            DiaryPatchSafety.Run("MemoryObservationPawnFactionPatch", () =>
            {
                DiaryGameComponent.Instance?.MarkMemoryObservationPawnFactionChanged(
                    __instance, __state, __instance.Faction);
            });
        }
    }

    /// <summary>Goodwill mutation is the common diplomacy sink for goodwill-using factions.</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith),
        new[]
        {
            typeof(Faction), typeof(int), typeof(bool), typeof(bool),
            typeof(HistoryEventDef), typeof(GlobalTargetInfo?)
        })]
    internal static class MemoryObservationFactionGoodwillPatch
    {
        /// <summary>Queues both exact faction instances after a successful goodwill mutation.</summary>
        public static void Postfix(Faction __instance, Faction other, bool __result)
        {
            if (!__result || !DiaryGameComponent.GamePlaying) return;
            DiaryPatchSafety.Run("MemoryObservationFactionGoodwillPatch", () =>
            {
                DiaryGameComponent.Instance?.MarkMemoryObservationFactionDirty(__instance, other);
            });
        }
    }

    /// <summary>Direct relation-kind mutation covers factions that do not use goodwill.</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelationDirect),
        new[]
        {
            typeof(Faction), typeof(FactionRelationKind), typeof(bool), typeof(string),
            typeof(GlobalTargetInfo?)
        })]
    internal static class MemoryObservationFactionRelationPatch
    {
        /// <summary>Queues both exact faction instances after direct relation-kind mutation.</summary>
        public static void Postfix(Faction __instance, Faction other)
        {
            if (!DiaryGameComponent.GamePlaying) return;
            DiaryPatchSafety.Run("MemoryObservationFactionRelationPatch", () =>
            {
                DiaryGameComponent.Instance?.MarkMemoryObservationFactionDirty(__instance, other);
            });
        }
    }

    /// <summary>Leader replacement after a death dirties only that exact faction instance.</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_LeaderDied))]
    internal static class MemoryObservationFactionLeaderDiedPatch
    {
        /// <summary>Queues the exact faction after vanilla replaces or clears its dead leader.</summary>
        public static void Postfix(Faction __instance)
        {
            if (!DiaryGameComponent.GamePlaying) return;
            DiaryPatchSafety.Run("MemoryObservationFactionLeaderDiedPatch", () =>
                DiaryGameComponent.Instance?.MarkMemoryObservationFactionDirty(__instance));
        }
    }

    /// <summary>Non-death leader replacement uses the same exact-instance dirty path.</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_LeaderLost))]
    internal static class MemoryObservationFactionLeaderLostPatch
    {
        /// <summary>Queues the exact faction after vanilla replaces or clears its lost leader.</summary>
        public static void Postfix(Faction __instance)
        {
            if (!DiaryGameComponent.GamePlaying) return;
            DiaryPatchSafety.Run("MemoryObservationFactionLeaderLostPatch", () =>
                DiaryGameComponent.Instance?.MarkMemoryObservationFactionDirty(__instance));
        }
    }

    /// <summary>FactionManager.Remove is private; string targeting plus the exact type avoids ambiguity.</summary>
    [HarmonyPatch(typeof(FactionManager), "Remove", new[] { typeof(Faction) })]
    internal static class MemoryObservationFactionRemovedPatch
    {
        /// <summary>Queues the removed exact instance after FactionManager commits removal.</summary>
        public static void Postfix(Faction faction)
        {
            if (!DiaryGameComponent.GamePlaying || faction == null) return;
            DiaryPatchSafety.Run("MemoryObservationFactionRemovedPatch", () =>
                DiaryGameComponent.Instance?.MarkMemoryObservationFactionRemoved(faction));
        }
    }

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
        internal sealed class ConversionCaptureState
        {
            public Ideo previousIdeo;
            public string previousCultureDefName = string.Empty;
        }

        /// <summary>Old ideo/culture captured before the swap; null means "not a conversion".</summary>
        public static void Prefix(Pawn_IdeoTracker __instance, Ideo ideo,
            out ConversionCaptureState __state)
        {
            __state = null;
            if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying)
            {
                return;
            }

            Ideo previous = __instance?.Ideo;
            if (previous != null && ideo != null && previous != ideo)
            {
                __state = new ConversionCaptureState
                {
                    previousIdeo = previous,
                    previousCultureDefName = previous.culture?.defName ?? string.Empty
                };
            }
        }

        public static void Postfix(Pawn_IdeoTracker __instance, Ideo ideo,
            ConversionCaptureState __state)
        {
            // SetIdeo can reject an attempted assignment (notably for babies). Re-read committed
            // state so an early-returned vanilla call cannot manufacture a conversion record.
            if (__state?.previousIdeo == null || ideo == null || __instance?.Ideo != ideo)
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
                    pawn,
                    __state.previousIdeo.name,
                    ideo.name,
                    ideo.culture?.defName,
                    __state.previousCultureDefName);
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
