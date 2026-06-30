// Harmony patches for the net-new moment sources added after the event-bus migration: skill
// milestones, diplomacy transitions, significant trades, and caravan journey moments. Each patch is
// intentionally thin: snapshot live RimWorld state, build a DiarySignal, and submit it through the
// shared bus. New to this? See AGENTS.md ("Harmony patches").
using System;
using System.Collections.Generic;
using HarmonyLib;
using PawnDiary.Ingestion;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Captures colonist skill level increases and lets the signal decide whether an XML milestone
    /// threshold was crossed.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class SkillRecordLearnMilestonePatch
    {
        public static void Prefix(SkillRecord __instance, ref int __state)
        {
            int oldLevel = -1;
            DiaryPatchSafety.Run("SkillRecordLearnMilestonePatch.Prefix", () =>
            {
                if (__instance != null)
                {
                    oldLevel = __instance.Level;
                }
            });
            __state = oldLevel;
        }

        public static void Postfix(SkillRecord __instance, int __state)
        {
            DiaryPatchSafety.Run("SkillRecordLearnMilestonePatch.Postfix", () =>
            {
                if (__instance == null || __state < 0 || __instance.Level <= __state)
                {
                    return;
                }

                DiaryEvents.Submit(new SkillMilestoneSignal(
                    __instance.Pawn, __instance.def, __state, __instance.Level, __instance.passion.ToString()));
            });
        }
    }

    /// <summary>
    /// Captures relation-kind transitions involving the player faction.
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.SetRelationDirect))]
    public static class FactionRelationDirectPatch
    {
        public static void Prefix(Faction __instance, Faction other, ref FactionRelationPatchState __state)
        {
            FactionRelationPatchState state = null;
            DiaryPatchSafety.Run("FactionRelationDirectPatch.Prefix", () =>
            {
                if (!DiaryGameComponent.GamePlaying)
                {
                    return;
                }

                Faction tracked = TrackedNonPlayerFaction(__instance, other);
                Faction player = Faction.OfPlayer;
                if (tracked == null || player == null)
                {
                    return;
                }

                state = new FactionRelationPatchState
                {
                    faction = tracked,
                    oldKind = tracked.RelationKindWith(player).ToString(),
                    oldGoodwill = tracked.GoodwillWith(player)
                };
            });
            __state = state;
        }

        public static void Postfix(Faction __instance, Faction other, string reason, FactionRelationPatchState __state)
        {
            DiaryPatchSafety.Run("FactionRelationDirectPatch.Postfix", () =>
            {
                if (__state == null || __state.faction == null)
                {
                    return;
                }

                Faction player = Faction.OfPlayer;
                if (player == null)
                {
                    return;
                }

                string newKind = __state.faction.RelationKindWith(player).ToString();
                int newGoodwill = __state.faction.GoodwillWith(player);
                DiaryEvents.Submit(new FactionRelationFanoutSignal(
                    __state.faction, __state.oldKind, newKind, __state.oldGoodwill, newGoodwill, reason));
            });
        }

        private static Faction TrackedNonPlayerFaction(Faction first, Faction second)
        {
            Faction player = Faction.OfPlayer;
            if (player == null || first == null || second == null)
            {
                return null;
            }

            if (first == player && second != player)
            {
                return second;
            }

            if (second == player && first != player)
            {
                return first;
            }

            return null;
        }
    }

    public class FactionRelationPatchState
    {
        public Faction faction;
        public string oldKind;
        public int oldGoodwill;
    }

    /// <summary>
    /// Snapshots a pending trade before vanilla clears transfer counts, then submits only successful
    /// real trades in the postfix.
    /// </summary>
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class TradeDealExecutePatch
    {
        public static void Prefix(TradeDeal __instance, ref TradeDealSignal __state)
        {
            TradeDealSignal state = null;
            DiaryPatchSafety.Run("TradeDealExecutePatch.Prefix", () =>
            {
                state = new TradeDealSignal(__instance);
            });
            __state = state;
        }

        public static void Postfix(bool __result, ref bool actuallyTraded, TradeDealSignal __state)
        {
            bool succeeded = __result;
            bool traded = actuallyTraded;
            TradeDealSignal signal = __state;
            DiaryPatchSafety.Run("TradeDealExecutePatch.Postfix", () =>
            {
                if (succeeded && traded)
                {
                    DiaryEvents.Submit(signal);
                }
            });
        }
    }

    /// <summary>Captures caravans created by exiting a map toward a direction.</summary>
    [HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
        new[]
        {
            typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile), typeof(Direction8Way),
            typeof(PlanetTile), typeof(bool)
        })]
    public static class CaravanExitDirectionPatch
    {
        public static void Postfix(Caravan __result, PlanetTile destinationTile)
        {
            DiaryPatchSafety.Run("CaravanExitDirectionPatch", () =>
            {
                DiaryEvents.Submit(new CaravanJourneyFanoutSignal(
                    __result, PawnDiary.Capture.CaravanJourneyEventData.Departed,
                    "PawnDiary.Event.CaravanJourney.RouteDestination".Translate(destinationTile.ToString()).Resolve()));
            });
        }
    }

    /// <summary>Captures caravans created by exiting a map toward a destination tile.</summary>
    [HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
        new[]
        {
            typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile), typeof(PlanetTile),
            typeof(PlanetTile), typeof(bool)
        })]
    public static class CaravanExitTilePatch
    {
        public static void Postfix(Caravan __result, PlanetTile destinationTile)
        {
            DiaryPatchSafety.Run("CaravanExitTilePatch", () =>
            {
                DiaryEvents.Submit(new CaravanJourneyFanoutSignal(
                    __result, PawnDiary.Capture.CaravanJourneyEventData.Departed,
                    "PawnDiary.Event.CaravanJourney.RouteDestination".Translate(destinationTile.ToString()).Resolve()));
            });
        }
    }

    /// <summary>Captures a player caravan entering a map through a vanilla enter mode.</summary>
    [HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter),
        new[]
        {
            typeof(Caravan), typeof(Map), typeof(CaravanEnterMode), typeof(CaravanDropInventoryMode),
            typeof(bool), typeof(Predicate<IntVec3>)
        })]
    public static class CaravanEnterModePatch
    {
        public static void Prefix(Caravan caravan, Map map)
        {
            DiaryPatchSafety.Run("CaravanEnterModePatch", () =>
            {
                DiaryEvents.Submit(new CaravanJourneyFanoutSignal(
                    caravan, PawnDiary.Capture.CaravanJourneyEventData.Arrived, MapLabel(map)));
            });
        }

        private static string MapLabel(Map map)
        {
            string label = DiaryLineCleaner.CleanLine(map?.Parent?.LabelCap);
            return string.IsNullOrWhiteSpace(label)
                ? "PawnDiary.Event.CaravanJourney.RouteMap".Translate().Resolve()
                : label;
        }
    }

    /// <summary>Captures a player caravan entering a map through a cell resolver.</summary>
    [HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter),
        new[]
        {
            typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode),
            typeof(bool)
        })]
    public static class CaravanEnterCellResolverPatch
    {
        public static void Prefix(Caravan caravan, Map map)
        {
            DiaryPatchSafety.Run("CaravanEnterCellResolverPatch", () =>
            {
                DiaryEvents.Submit(new CaravanJourneyFanoutSignal(
                    caravan, PawnDiary.Capture.CaravanJourneyEventData.Arrived, MapLabel(map)));
            });
        }

        private static string MapLabel(Map map)
        {
            string label = DiaryLineCleaner.CleanLine(map?.Parent?.LabelCap);
            return string.IsNullOrWhiteSpace(label)
                ? "PawnDiary.Event.CaravanJourney.RouteMap".Translate().Resolve()
                : label;
        }
    }
}
