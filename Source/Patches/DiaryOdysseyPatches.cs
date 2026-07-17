// Odyssey O1.3 lifecycle hooks. Public stable seams use exact Harmony attributes; private
// LandingEnded is registered defensively through DiaryPatchRegistrar. Every body is gated by the DLC
// flag and patch safety, so failure can only omit Pawn Diary state—not alter vanilla gravship behavior.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary
{
    /// <summary>Captures takeoff intent before vanilla removes the ship from its origin map.</summary>
    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateTakeoff),
        new[] { typeof(Building_GravEngine), typeof(PlanetTile) })]
    internal static class OdysseyInitiateTakeoffPatch
    {
        private static void Prefix(Building_GravEngine engine, PlanetTile targetTile)
        {
            if (!ModsConfig.OdysseyActive || Verse.Current.ProgramState != ProgramState.Playing) return;
            DiaryPatchSafety.Run("Odyssey.InitiateTakeoff", () =>
            {
                DiaryGameComponent.Instance?.ObserveOdysseyTakeoffIntent(engine, targetTile);
            });
        }
    }

    /// <summary>Commits one saved journey only after vanilla creates the travelling world object.</summary>
    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.TravelTo),
        new[] { typeof(Gravship), typeof(PlanetTile), typeof(PlanetTile) })]
    internal static class OdysseyTravelToPatch
    {
        /// <summary>
        /// Preserves the true origin before vanilla rewrites its by-value oldTile argument onto the
        /// destination layer for cross-layer travel.
        /// </summary>
        private static void Prefix(PlanetTile oldTile, ref PlanetTile __state)
        {
            __state = oldTile;
        }

        private static void Postfix(Gravship gravship, PlanetTile newTile, PlanetTile __state)
        {
            if (!ModsConfig.OdysseyActive || Verse.Current.ProgramState != ProgramState.Playing) return;
            DiaryPatchSafety.Run("Odyssey.TravelTo", () =>
            {
                DiaryGameComponent.Instance?.ObserveOdysseyTravelCommit(gravship, __state, newTile);
            });
        }
    }

    /// <summary>Snapshots the chosen landing map without applying novelty history or emitting a page.</summary>
    [HarmonyPatch(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateLanding),
        new[] { typeof(Gravship), typeof(Map), typeof(IntVec3), typeof(Rot4) })]
    internal static class OdysseyInitiateLandingPatch
    {
        private static void Prefix(Gravship gravship, Map map)
        {
            if (!ModsConfig.OdysseyActive || Verse.Current.ProgramState != ProgramState.Playing) return;
            DiaryPatchSafety.Run("Odyssey.InitiateLanding", () =>
            {
                DiaryGameComponent.Instance?.ObserveOdysseyLandingStart(gravship, map);
            });
        }
    }

    /// <summary>
    /// Defensively owns the private successful-finish seam. Prefix preserves controller fields before
    /// vanilla clears them; an ordinary postfix runs only when the original returns successfully.
    /// </summary>
    internal static class OdysseyLandingEndedPatch
    {
        /// <summary>
        /// Transient Harmony state. The detached landing facts may persist; live pawns exist only
        /// across this synchronous vanilla call so O1.4 can resolve the exact selected POVs.
        /// </summary>
        private sealed class LandingPatchState
        {
            public OdysseyPendingLanding pending;
            public List<Pawn> livePawns = new List<Pawn>();
        }

        private static FieldInfo gravshipField;
        private static FieldInfo mapField;

        /// <summary>True only when the exact private method and both required fields were patched.</summary>
        public static bool HooksReady { get; private set; }

        /// <summary>Registers the verified zero-parameter private landing finish method fail-soft.</summary>
        public static void TryRegister(Harmony harmony)
        {
            HooksReady = false;
            if (harmony == null) return;
            Type type = typeof(WorldComponent_GravshipController);
            MethodBase target = AccessTools.DeclaredMethod(type, "LandingEnded", Type.EmptyTypes);
            gravshipField = AccessTools.Field(type, "gravship");
            mapField = AccessTools.Field(type, "map");
            if (target == null || gravshipField == null || mapField == null)
            {
                Log.Warning("[Pawn Diary] Odyssey LandingEnded changed; successful landing history "
                    + "capture is disabled while vanilla landing remains untouched.");
                return;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(OdysseyLandingEndedPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(OdysseyLandingEndedPatch), nameof(Postfix)));
                HooksReady = true;
            }
            catch (Exception exception)
            {
                HooksReady = false;
                Log.Warning("[Pawn Diary] Could not register Odyssey landing-finish history capture; "
                    + "vanilla landing remains untouched. " + exception);
            }
        }

        private static void Prefix(
            WorldComponent_GravshipController __instance,
            ref LandingPatchState __state)
        {
            LandingPatchState captured = null;
            if (ModsConfig.OdysseyActive
                && Verse.Current.ProgramState == ProgramState.Playing
                && __instance != null)
            {
                DiaryPatchSafety.Run("Odyssey.LandingEnded.Prefix", () =>
                {
                    Gravship gravship = gravshipField.GetValue(__instance) as Gravship;
                    Map map = mapField.GetValue(__instance) as Map;
                    OdysseyPendingLanding pending =
                        DiaryGameComponent.Instance?.BeginOdysseyLandingFinish(gravship, map);
                    if (pending != null)
                    {
                        captured = new LandingPatchState { pending = pending };
                        IEnumerable<Pawn> pawns = gravship?.Pawns;
                        if (pawns != null)
                        {
                            foreach (Pawn pawn in pawns)
                            {
                                if (pawn != null) captured.livePawns.Add(pawn);
                            }
                        }
                    }
                });
            }

            __state = captured;
        }

        private static void Postfix(LandingPatchState __state)
        {
            if (__state == null) return;
            DiaryPatchSafety.Run("Odyssey.LandingEnded.Postfix", () =>
            {
                DiaryGameComponent.Instance?.CompleteOdysseyLanding(__state.pending, __state.livePawns);
            });
        }
    }

    /// <summary>
    /// Defensively patches every concrete landing-outcome override. A postfix observes only workers
    /// that returned successfully, then enriches the already pending canonical landing instead of
    /// creating another page. Discovery also covers compatible modded outcome subclasses.
    /// </summary>
    internal static class OdysseyLandingOutcomePatch
    {
        private static FieldInfo outcomeDefField;

        /// <summary>True when the private Def field and at least one concrete override were patched.</summary>
        public static bool HooksReady { get; private set; }

        /// <summary>Registers all declared ApplyOutcome overrides through exact signature lookup.</summary>
        public static void TryRegister(Harmony harmony)
        {
            HooksReady = false;
            if (harmony == null) return;
            outcomeDefField = AccessTools.Field(typeof(LandingOutcomeWorker), "def");
            MethodInfo postfix = AccessTools.DeclaredMethod(
                typeof(OdysseyLandingOutcomePatch), nameof(Postfix));
            if (outcomeDefField == null || postfix == null)
            {
                Log.Warning("[Pawn Diary] Odyssey landing-outcome internals changed; exact outcome "
                    + "context is disabled while vanilla landing remains untouched.");
                return;
            }

            int patched = 0;
            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || type.IsAbstract || type == typeof(LandingOutcomeWorker)
                    || !typeof(LandingOutcomeWorker).IsAssignableFrom(type))
                {
                    continue;
                }

                MethodBase target = AccessTools.DeclaredMethod(
                    type, nameof(LandingOutcomeWorker.ApplyOutcome), new[] { typeof(Gravship) });
                if (target == null) continue;
                try
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                    patched++;
                }
                catch (Exception exception)
                {
                    Log.Warning("[Pawn Diary] Could not patch Odyssey landing outcome " + type.FullName
                        + "; that exact outcome will omit context: " + exception.Message);
                }
            }

            HooksReady = patched > 0;
            if (!HooksReady)
            {
                Log.Warning("[Pawn Diary] No concrete Odyssey landing-outcome override was found; "
                    + "exact outcome context is disabled.");
            }
        }

        private static void Postfix(LandingOutcomeWorker __instance, Gravship gravship)
        {
            if (!ModsConfig.OdysseyActive || Verse.Current.ProgramState != ProgramState.Playing
                || __instance == null || gravship == null)
            {
                return;
            }

            DiaryPatchSafety.Run("Odyssey.LandingOutcome.Postfix", () =>
            {
                LandingOutcomeDef outcome = outcomeDefField.GetValue(__instance) as LandingOutcomeDef;
                if (outcome == null) return;
                DiaryGameComponent.Instance?.ObserveOdysseyLandingOutcome(
                    gravship,
                    outcome.defName,
                    outcome.letterLabel);
            });
        }
    }
}
