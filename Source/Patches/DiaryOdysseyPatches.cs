// Odyssey lifecycle hooks. Public stable seams use exact Harmony attributes; private LandingEnded and
// O3's verified Cerebrex-core owner are registered defensively through DiaryPatchRegistrar. Every body
// is DLC-gated and fail-soft, so failure can only omit Pawn Diary state—not alter vanilla behavior.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Ingestion;
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
            const string targetLabel =
                "WorldComponent_GravshipController.LandingEnded()";
            Type type = typeof(WorldComponent_GravshipController);
            MethodBase target = AccessTools.DeclaredMethod(type, "LandingEnded", Type.EmptyTypes);
            gravshipField = AccessTools.Field(type, "gravship");
            mapField = AccessTools.Field(type, "map");
            if (target == null || gravshipField == null || mapField == null)
            {
                Log.Warning("[Pawn Diary] Odyssey LandingEnded changed; successful landing history "
                    + "capture is disabled while vanilla landing remains untouched.");
                DiaryPatchManifest.Report(
                    "Odyssey",
                    targetLabel,
                    DiaryPatchManifest.HookStatus.Degraded,
                    "method or required fields changed; landing history capture disabled");
                return;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(OdysseyLandingEndedPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(OdysseyLandingEndedPatch), nameof(Postfix)));
                HooksReady = true;
                DiaryPatchManifest.Report(
                    "Odyssey", targetLabel, DiaryPatchManifest.HookStatus.Applied);
            }
            catch (Exception exception)
            {
                HooksReady = false;
                Log.Warning("[Pawn Diary] Could not register Odyssey landing-finish history capture; "
                    + "vanilla landing remains untouched. " + exception);
                DiaryPatchManifest.Report(
                    "Odyssey",
                    targetLabel,
                    DiaryPatchManifest.HookStatus.Failed,
                    exception.GetType().Name + ": " + exception.Message);
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
            const string targetLabel = "LandingOutcomeWorker.ApplyOutcome overrides";
            outcomeDefField = AccessTools.Field(typeof(LandingOutcomeWorker), "def");
            MethodInfo postfix = AccessTools.DeclaredMethod(
                typeof(OdysseyLandingOutcomePatch), nameof(Postfix));
            if (outcomeDefField == null || postfix == null)
            {
                Log.Warning("[Pawn Diary] Odyssey landing-outcome internals changed; exact outcome "
                    + "context is disabled while vanilla landing remains untouched.");
                DiaryPatchManifest.Report(
                    "Odyssey",
                    targetLabel,
                    DiaryPatchManifest.HookStatus.Degraded,
                    "Def field or postfix changed; exact outcome context disabled");
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
            if (HooksReady)
            {
                DiaryPatchManifest.Report(
                    "Odyssey",
                    targetLabel,
                    DiaryPatchManifest.HookStatus.Applied,
                    "patched " + patched + " overrides");
            }
            else
            {
                Log.Warning("[Pawn Diary] No concrete Odyssey landing-outcome override was found; "
                    + "exact outcome context is disabled.");
                DiaryPatchManifest.Report(
                    "Odyssey",
                    targetLabel,
                    DiaryPatchManifest.HookStatus.Degraded,
                    "no concrete override found");
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

    /// <summary>
    /// Defensively patches the exact private owner of Odyssey's destroy-versus-scavenge ending. Vanilla
    /// Quest completion is deferred only inside this synchronous frame and released unless the
    /// dedicated operator-authored page is proven to exist.
    /// </summary>
    internal static class OdysseyMechhiveOutcomePatch
    {
        private const string CompTypeName = "RimWorld.CompCerebrexCore";
        private const string MethodName = "DeactivateCore";

        /// <summary>True only when the exact private method and every correlation field were patched.</summary>
        internal static bool HookReady { get; private set; }

        /// <summary>Registers CompCerebrexCore.DeactivateCore(bool scavenging) fail-soft.</summary>
        internal static void TryRegister(Harmony harmony)
        {
            HookReady = false;
            if (harmony == null || !ModsConfig.OdysseyActive)
            {
                if (harmony != null)
                {
                    DiaryPatchManifest.Report(
                        "Odyssey",
                        "CompCerebrexCore.DeactivateCore(bool scavenging)",
                        DiaryPatchManifest.HookStatus.Skipped,
                        "Odyssey inactive");
                }
                return;
            }
            const string targetLabel =
                "CompCerebrexCore.DeactivateCore(bool scavenging)";
            try
            {
                Type compType = AccessTools.TypeByName(CompTypeName);
                Type questPartType = AccessTools.TypeByName(
                    OdysseyMechhiveSourceTokens.QuestPartTypeName);
                MethodInfo target = compType == null
                    ? null
                    : AccessTools.DeclaredMethod(
                        compType, MethodName, new[] { typeof(bool) }) as MethodInfo;
                ParameterInfo[] parameters = target?.GetParameters();
                bool exact = target != null && target.IsPrivate && !target.IsStatic
                    && target.ReturnType == typeof(void)
                    && parameters != null && parameters.Length == 1
                    && parameters[0].ParameterType == typeof(bool)
                    && string.Equals(
                        parameters[0].Name, "scavenging", StringComparison.Ordinal)
                    && DlcContext.ResolveOdysseyMechhiveReflection(compType, questPartType);
                if (!exact)
                {
                    WarnMissing(
                        "the exact private CompCerebrexCore.DeactivateCore(bool scavenging) "
                            + "owner or its quest-correlation fields were not found");
                    DiaryPatchManifest.Report(
                        "Odyssey",
                        targetLabel,
                        DiaryPatchManifest.HookStatus.Degraded,
                        "target or quest-correlation fields not found");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(
                        typeof(OdysseyMechhiveOutcomePatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(
                        typeof(OdysseyMechhiveOutcomePatch), nameof(Postfix)),
                    finalizer: new HarmonyMethod(
                        typeof(OdysseyMechhiveOutcomePatch), nameof(Finalizer)));
                HookReady = true;
                DiaryPatchManifest.Report(
                    "Odyssey", targetLabel, DiaryPatchManifest.HookStatus.Applied);
            }
            catch (Exception exception)
            {
                HookReady = false;
                WarnMissing(
                    "registration failed: " + exception.GetType().Name + ": "
                        + exception.Message);
                DiaryPatchManifest.Report(
                    "Odyssey",
                    targetLabel,
                    DiaryPatchManifest.HookStatus.Failed,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        /// <summary>Freezes exact operator/quest facts and opens deferred Quest ownership.</summary>
        private static void Prefix(
            object __instance,
            bool scavenging,
            ref OdysseyMechhiveOutcomeCapture __state)
        {
            __state = null;
            if (!HookReady || !ModsConfig.OdysseyActive
                || Verse.Current.ProgramState != ProgramState.Playing)
            {
                return;
            }

            OdysseyMechhiveOutcomeCapture captured = null;
            DiaryPatchSafety.Run("Odyssey.Mechhive.Prefix", () =>
            {
                DiaryGameComponent component = DiaryGameComponent.Instance;
                if (component == null || component.HasCommittedOdysseyMechhiveOutcome) return;
                if (!DlcContext.TryCaptureOdysseyMechhiveOutcomeBefore(
                    __instance, scavenging, out captured))
                {
                    return;
                }

                OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
                DiaryInteractionGroupDef group = InteractionGroups.ClassifyOdysseyEvent(
                    OdysseyMechhiveEventDefNames.Outcome);
                bool groupEnabled = group != null && PawnDiaryMod.Settings != null
                    && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
                captured.suppressesQuestSuccess =
                    OdysseyMechhiveOutcomePolicy.PredictsDedicatedPage(
                        captured.facts.actorEligible,
                        policy.enabled,
                        policy.mechhiveOutcomePageEnabled,
                        groupEnabled);
                if (!OdysseyMechhiveOutcomeScope.Begin(
                    captured, policy.mechhiveOutcomeMaximumDepth))
                {
                    captured = null;
                }
            });
            __state = captured;
        }

        /// <summary>Verifies the committed branch and owns Quest success only after page creation.</summary>
        private static void Postfix(
            object __instance,
            OdysseyMechhiveOutcomeCapture __state)
        {
            if (__state == null) return;
            OdysseyMechhiveOutcomeClose close = null;
            DiaryPatchSafety.Run("Odyssey.Mechhive.Postfix.Close", () =>
            {
                close = OdysseyMechhiveOutcomeScope.Complete(__state);
            });
            if (close == null) return;
            ReleaseQuestSignals(close.releaseQuests);
            if (!close.matched) return;

            OdysseyMechhiveOutcomePlan plan = null;
            bool dedicatedEventCreated = false;
            DiaryPatchSafety.Run("Odyssey.Mechhive.Postfix.Complete", () =>
            {
                DiaryGameComponent component = DiaryGameComponent.Instance;
                if (component != null)
                {
                    plan = component.CompleteOdysseyMechhiveOutcome(
                        __instance,
                        close.capture,
                        close.deferredQuest != null,
                        out dedicatedEventCreated);
                }
            });
            bool suppress = OdysseyMechhiveOutcomePolicy.OwnsQuestSuccess(plan)
                && dedicatedEventCreated
                && close.deferredQuest != null;
            if (!suppress) ReleaseQuestSignal(close.deferredQuest);
        }

        /// <summary>Always aborts an unfinished frame and preserves vanilla's original exception.</summary>
        private static Exception Finalizer(
            Exception __exception,
            OdysseyMechhiveOutcomeCapture __state)
        {
            if (__state == null || __state.scopeClosed) return __exception;
            OdysseyMechhiveOutcomeClose close = null;
            try
            {
                close = OdysseyMechhiveOutcomeScope.Abort(__state);
            }
            catch (Exception cleanupException)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Odyssey Mechhive scope cleanup failed; vanilla's exception "
                        + "is preserved: " + cleanupException,
                    "PawnDiary.Odyssey.Mechhive.Finalizer".GetHashCode());
            }
            if (close != null) ReleaseQuestSignals(close.releaseQuests);
            return __exception;
        }

        private static void ReleaseQuestSignals(List<QuestFanoutSignal> signals)
        {
            if (signals == null) return;
            for (int i = 0; i < signals.Count; i++) ReleaseQuestSignal(signals[i]);
        }

        private static void ReleaseQuestSignal(QuestFanoutSignal signal)
        {
            if (signal == null) return;
            DiaryPatchSafety.Run("Odyssey.Mechhive.QuestRelease", () =>
            {
                DiaryEvents.Submit(signal);
            });
        }

        private static void WarnMissing(string reason)
        {
            Log.WarningOnce(
                "[Pawn Diary] Odyssey Mechhive exact ending capture is disabled while the "
                    + "generic Quest route remains unchanged: " + reason + ".",
                "PawnDiary.Odyssey.Mechhive.MissingHook".GetHashCode());
        }
    }
}
