// Non-social gameplay signal Harmony patches. These hooks forward tales, mental states,
// inspirations, map conditions, raids, proximity letters, rituals, and ability use into
// DiaryGameComponent; the richer capture decisions stay in the component and pure Event Catalog helpers.
// New to this? See AGENTS.md ("Harmony patches").
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Ingestion;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PawnDiary
{
    // Fires whenever RimWorld accepts a notable "tale" for history/art generation. Tales cover
    // many non-social events, such as wounds, deaths, surgeries, births, recruitment, research,
    // construction/crafting milestones, raids, and disasters.
    /// <summary>
    /// Captures successful vanilla Tales for diary event classification.
    /// </summary>
    [HarmonyPatch(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale))]
    public static class TaleRecorderPatch
    {
        /// <summary>
        /// Harmony Postfix for TaleRecorder.RecordTale. The returned Tale is null when vanilla
        /// chose not to record it, so only successful tales are forwarded to DiaryGameComponent.
        /// </summary>
        public static void Postfix(Tale __result, TaleDef def)
        {
            DiaryPatchSafety.Run("TaleRecorderPatch", () =>
            {
                if (__result == null || def == null)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordTale(__result, def);
            });
        }
    }

    // Fires whenever a pawn enters a mental state: social fights (pairwise) and mental
    // breaks (solo). This is the single choke point for all mental states, so it catches
    // them regardless of how they were triggered.
    /// <summary>
    /// Captures successful mental-state starts for social-fight and mental-break diary entries.
    /// </summary>
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class MentalStateStartPatch
    {
        // Reflection accessor for the private MentalStateHandler.pawn field so we can read the subject pawn.
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(MentalStateHandler), "pawn");

        static MentalStateStartPatch()
        {
            if (PawnField == null)
            {
                Log.Warning("[PawnDiary] Could not find MentalStateHandler.pawn; mental-state diary events will not be captured.");
            }
        }

        /// <summary>
        /// Harmony Postfix for MentalStateHandler.TryStartMentalState. Forwards successful
        /// mental state transitions to DiaryGameComponent for diary recording.
        /// </summary>
        public static void Postfix(bool __result, MentalStateHandler __instance, MentalStateDef stateDef, string reason, Pawn otherPawn)
        {
            DiaryPatchSafety.Run("MentalStateStartPatch", () =>
            {
                if (!__result || stateDef == null || __instance == null)
                {
                    return;
                }

                Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
                if (pawn == null)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordMentalState(pawn, stateDef, otherPawn, reason);
            });
        }
    }

    // Fires whenever RimWorld accepts a pawn inspiration, whether it came from the random inspiration
    // system or another vanilla source such as a psycast, ritual, or drug effect.
    /// <summary>
    /// Captures accepted pawn inspirations after vanilla applies them.
    /// </summary>
    [HarmonyPatch(typeof(InspirationHandler), nameof(InspirationHandler.TryStartInspiration))]
    public static class InspirationStartPatch
    {
        /// <summary>
        /// Harmony Postfix for InspirationHandler.TryStartInspiration. Records only successful
        /// inspiration starts, after vanilla has accepted and applied the inspiration.
        /// </summary>
        public static void Postfix(bool __result, InspirationHandler __instance, InspirationDef def, string reason)
        {
            DiaryPatchSafety.Run("InspirationStartPatch", () =>
            {
                if (!__result || __instance == null || __instance.pawn == null || def == null)
                {
                    return;
                }

                DiaryEvents.Submit(new InspirationSignal(__instance.pawn, def, reason));
            });
        }
    }

    // Fires when a mood-affecting GameCondition starts (aurora, eclipse, psychic drone,
    // toxic fallout, etc.). The patch iterates eligible colonists on affected maps and records
    // a solo diary event for each one, so colonists can note how they feel about the event.
    /// <summary>
    /// Captures mood-affecting game conditions when they are registered.
    /// </summary>
    [HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.RegisterCondition))]
    public static class GameConditionStartPatch
    {
        /// <summary>
        /// Harmony Postfix for GameConditionManager.RegisterCondition. Detects
        /// mood-affecting game conditions (aurora, eclipse, psychic drone, etc.) and
        /// forwards each affected colonist to DiaryGameComponent.RecordMoodEvent.
        /// </summary>
        public static void Postfix(GameCondition cond)
        {
            DiaryPatchSafety.Run("GameConditionStartPatch", () =>
            {
                if (cond == null || cond.def == null)
                {
                    return;
                }

                // TODO: ProblemCauser conditions are too complex to handle correctly — skipped for now.
                if (cond.def.defName == "ProblemCauser")
                {
                    return;
                }

                DiaryEvents.Submit(new MoodEventFanoutSignal(cond));
            });
        }
    }

    // Fires once per successful incident. IncidentWorker.TryExecute is the single public entry point
    // every incident flows through, so we first emit a generic XML event-window signal for incidents
    // such as mech clusters, then keep the existing raid-like diary path for raids/infestations.
    /// <summary>
    /// Captures successful raid-like incident execution after spawned threats are available.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class RaidExecutePatch
    {
        /// <summary>
        /// Harmony Postfix for IncidentWorker.TryExecute. Forwards successful raid-like incidents to
        /// DiaryGameComponent.RecordRaid, which fans out to each eligible colonist on the target map.
        /// Non-raid incidents and failed executions are skipped.
        /// </summary>
        public static void Postfix(IncidentWorker __instance, IncidentParms parms, bool __result)
        {
            DiaryPatchSafety.Run("RaidExecutePatch", () =>
            {
                if (!__result || parms == null || __instance == null || __instance.def == null)
                {
                    return;
                }

                DiaryGameComponent component = DiaryGameComponent.Current;
                if (component == null)
                {
                    return;
                }

                // Isolate the optional event-window signal so a failure there cannot skip the mature
                // raid capture below (both ran in this one DiaryPatchSafety.Run lambda before).
                DiaryPatchSafety.Run("RaidExecutePatch.EventWindow",
                    () => component.RecordEventWindowIncident(__instance.def, parms));
                if (IsRaidLikeIncident(__instance))
                {
                    DiaryEvents.Submit(new RaidFanoutSignal(parms, __instance.def));
                }
            });
        }

        private static bool IsRaidLikeIncident(IncidentWorker worker)
        {
            if (worker is IncidentWorker_Raid)
            {
                return true;
            }

            string workerTypeName = worker?.GetType().Name;
            return !string.IsNullOrWhiteSpace(workerTypeName)
                && workerTypeName.IndexOf("Infestation", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // Fires after any Thing spawns into a map. Event-window XML filters by defName/source, so this
    // generic hook can cover visible clues and threat emergence without hardcoding Anomaly types.
    /// <summary>
    /// Captures spawned things as generic event-window signals.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup), new[] { typeof(Map), typeof(bool) })]
    public static class ThingSpawnedEventWindowPatch
    {
        /// <summary>
        /// Harmony Postfix for Thing.SpawnSetup. Respawns during save/load are skipped so loading a
        /// map does not replay every active window trigger.
        /// </summary>
        public static void Postfix(Thing __instance, Map map, bool respawningAfterLoad)
        {
            DiaryPatchSafety.Run("ThingSpawnedEventWindowPatch", () =>
            {
                if (respawningAfterLoad || __instance == null || __instance.def == null)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordEventWindowThingSpawned(__instance, map);
            });
        }
    }

    // Fires whenever RimWorld processes a biological birthday. Birthdays are not just flavor in
    // RimWorld: vanilla immediately runs age-related hediff givers from this path.
    /// <summary>
    /// Captures biological birthdays for XML event-window rules.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_AgeTracker), "BirthdayBiological")]
    public static class BiologicalBirthdayEventWindowPatch
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");

        /// <summary>
        /// Harmony Postfix for Pawn_AgeTracker.BirthdayBiological. The event is target-pawn scoped in XML.
        /// </summary>
        public static void Postfix(Pawn_AgeTracker __instance, int birthdayAge)
        {
            DiaryPatchSafety.Run("BiologicalBirthdayEventWindowPatch", () =>
            {
                Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
                if (pawn == null || !DiaryGameComponent.GamePlaying)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordEventWindowBirthday(pawn, birthdayAge);
            });
        }
    }

    /// <summary>
    /// Captures ThingComp proximity letters for XML event-window rules.
    /// </summary>
    public static class ProximityLetterEventWindowPatch
    {
        private const string TargetTypeName = "RimWorld.CompProximityLetter";
        private const string PropsTypeName = "RimWorld.CompProperties_ProximityLetter";
        private const string TargetMethodName = "CompTick";
        private const string LetterSentFieldName = "letterSent";
        private const string LetterLabelFieldName = "letterLabel";
        private const string RadiusFieldName = "radius";
        private const float FallbackRadius = 8f;

        private static FieldInfo LetterSentField;
        private static FieldInfo LetterLabelField;
        private static FieldInfo RadiusField;

        /// <summary>
        /// Registers the patch only when RimWorld exposes CompProximityLetter.CompTick in this build.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            Type targetType = AccessTools.TypeByName(TargetTypeName);
            if (targetType == null)
            {
                Log.Warning("[Pawn Diary] Could not find CompProximityLetter; proximity-letter event windows will not be captured.");
                return;
            }

            MethodBase target = AccessTools.DeclaredMethod(targetType, TargetMethodName);
            Type propsType = AccessTools.TypeByName(PropsTypeName);
            LetterSentField = AccessTools.Field(targetType, LetterSentFieldName);
            LetterLabelField = AccessTools.Field(propsType, LetterLabelFieldName);
            RadiusField = AccessTools.Field(propsType, RadiusFieldName);

            if (target == null || LetterSentField == null)
            {
                Log.Warning("[Pawn Diary] Could not find CompProximityLetter.CompTick; proximity-letter event windows will not be captured.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(ProximityLetterEventWindowPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(ProximityLetterEventWindowPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Saves the pre-tick state so the postfix can see exactly when the letter was first sent.
        /// </summary>
        private static void Prefix(object __instance, ref ProximityLetterState __state)
        {
            ProximityLetterState state = null;
            DiaryPatchSafety.Run("ProximityLetterEventWindowPatch.Prefix", () =>
            {
                if (__instance == null || LetterSent(__instance))
                {
                    return;
                }

                ThingComp comp = __instance as ThingComp;
                Thing parent = comp?.parent;
                state = new ProximityLetterState
                {
                    label = LetterLabel(comp),
                    subjectPawn = SubjectPawnNear(parent, Radius(comp))
                };
            });
            __state = state;
        }

        /// <summary>
        /// Forwards the proximity letter after vanilla marks it sent.
        /// </summary>
        private static void Postfix(object __instance, ProximityLetterState __state)
        {
            DiaryPatchSafety.Run("ProximityLetterEventWindowPatch.Postfix", () =>
            {
                if (__state == null || __instance == null || !LetterSent(__instance))
                {
                    return;
                }

                ThingComp comp = __instance as ThingComp;
                DiaryGameComponent.Current?.RecordEventWindowProximityLetter(
                    comp?.parent,
                    __state.label,
                    __state.subjectPawn);
            });
        }

        private static bool LetterSent(object comp)
        {
            object value = LetterSentField?.GetValue(comp);
            return value is bool && (bool)value;
        }

        private static string LetterLabel(ThingComp comp)
        {
            object value = LetterLabelField?.GetValue(comp?.props);
            string label = value as string;
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            return comp?.parent == null ? string.Empty : comp.parent.LabelShortCap;
        }

        private static float Radius(ThingComp comp)
        {
            object value = RadiusField?.GetValue(comp?.props);
            if (value is float)
            {
                return (float)value;
            }

            if (value is int)
            {
                return (int)value;
            }

            return FallbackRadius;
        }

        private static Pawn SubjectPawnNear(Thing thing, float radius)
        {
            Map map = thing?.Map;
            if (thing == null || map?.mapPawns == null)
            {
                return null;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn != null && pawn.Spawned && pawn.Position.InHorDistOf(thing.Position, radius))
                {
                    return pawn;
                }
            }

            return null;
        }

        private sealed class ProximityLetterState
        {
            public string label;
            public Pawn subjectPawn;
        }
    }

    /// <summary>
    /// Captures completed void monolith activations for XML event-window rules.
    /// </summary>
    public static class VoidMonolithActivationEventWindowPatch
    {
        private const string TargetTypeName = "RimWorld.Building_VoidMonolith";
        private const string TargetMethodName = "Activate";
        private const string AnomalyComponentTypeName = "RimWorld.GameComponent_Anomaly";
        private const string MonolithLevelDefTypeName = "RimWorld.MonolithLevelDef";
        private const string LevelDefPropertyName = "LevelDef";
        private const string LevelInspectTextFieldName = "levelInspectText";
        private const string MonolithLabelFieldName = "monolithLabel";

        private static MethodInfo AnomalyGetter;
        private static MethodInfo LevelDefGetter;
        private static FieldInfo LevelInspectTextField;
        private static FieldInfo MonolithLabelField;

        /// <summary>
        /// Registers the patch only when RimWorld exposes Building_VoidMonolith.Activate(Pawn).
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            Type targetType = AccessTools.TypeByName(TargetTypeName);
            if (targetType == null)
            {
                Log.Warning("[Pawn Diary] Could not find Building_VoidMonolith; void monolith activation event windows will not be captured.");
                return;
            }

            MethodBase target = AccessTools.Method(targetType, TargetMethodName, new[] { typeof(Pawn) });
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Could not find Building_VoidMonolith.Activate(Pawn); void monolith activation event windows will not be captured.");
                return;
            }

            Type anomalyComponentType = AccessTools.TypeByName(AnomalyComponentTypeName);
            Type monolithLevelDefType = AccessTools.TypeByName(MonolithLevelDefTypeName);
            AnomalyGetter = AccessTools.PropertyGetter(typeof(Find), "Anomaly");
            LevelDefGetter = anomalyComponentType == null
                ? null
                : AccessTools.PropertyGetter(anomalyComponentType, LevelDefPropertyName);
            LevelInspectTextField = monolithLevelDefType == null
                ? null
                : AccessTools.Field(monolithLevelDefType, LevelInspectTextFieldName);
            MonolithLabelField = monolithLevelDefType == null
                ? null
                : AccessTools.Field(monolithLevelDefType, MonolithLabelFieldName);

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(VoidMonolithActivationEventWindowPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Forwards the completed activation after vanilla advances the monolith level and sends its letter.
        /// </summary>
        private static void Postfix(object __instance, Pawn pawn)
        {
            DiaryPatchSafety.Run("VoidMonolithActivationEventWindowPatch", () =>
            {
                Thing thing = __instance as Thing;
                if (thing == null)
                {
                    return;
                }

                MonolithLevelFacts facts = CurrentLevelFacts();
                DiaryGameComponent.Current?.RecordEventWindowVoidMonolithActivation(
                    thing,
                    facts.defName,
                    facts.label,
                    pawn);
            });
        }

        private static MonolithLevelFacts CurrentLevelFacts()
        {
            object anomaly = AnomalyGetter?.Invoke(null, null);
            object levelDef = anomaly == null ? null : LevelDefGetter?.Invoke(anomaly, null);
            Def def = levelDef as Def;
            string label = FieldString(LevelInspectTextField, levelDef);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = FieldString(MonolithLabelField, levelDef);
            }

            if (string.IsNullOrWhiteSpace(label) && def != null)
            {
                label = def.LabelCap.Resolve();
            }

            return new MonolithLevelFacts
            {
                defName = def == null ? string.Empty : def.defName,
                label = label ?? string.Empty
            };
        }

        private static string FieldString(FieldInfo field, object instance)
        {
            if (field == null || instance == null)
            {
                return string.Empty;
            }

            object value = field.GetValue(instance);
            return value as string ?? string.Empty;
        }

        private struct MonolithLevelFacts
        {
            public string defName;
            public string label;
        }
    }

    /// <summary>
    /// Captures prisoner breakouts for XML event-window rules.
    /// </summary>
    public static class PrisonBreakEventWindowPatch
    {
        private const string TargetTypeName = "RimWorld.PrisonBreakUtility";
        private const string TargetMethodName = "StartPrisonBreak";

        /// <summary>
        /// Registers the by-ref overload used by both natural and sparked prisoner breakouts.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            Type targetType = AccessTools.TypeByName(TargetTypeName);
            if (targetType == null)
            {
                Log.Warning("[Pawn Diary] Could not find PrisonBreakUtility; prison-break event windows will not be captured.");
                return;
            }

            MethodBase target = AccessTools.Method(targetType, TargetMethodName, new[]
            {
                typeof(Pawn),
                typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(LetterDef).MakeByRefType(),
                typeof(List<Pawn>).MakeByRefType()
            });
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Could not find PrisonBreakUtility.StartPrisonBreak by-ref overload; prison-break event windows will not be captured.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PrisonBreakEventWindowPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Forwards the breakout after vanilla has collected the escaping pawns and letter label.
        /// </summary>
        private static void Postfix(Pawn initiator, ref string letterLabel, ref List<Pawn> escapingPrisoners)
        {
            string capturedLabel = letterLabel;
            List<Pawn> capturedPrisoners = escapingPrisoners;
            DiaryPatchSafety.Run("PrisonBreakEventWindowPatch", () =>
            {
                if ((capturedPrisoners == null || capturedPrisoners.Count == 0) && initiator == null)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordEventWindowPrisonBreak(initiator, capturedLabel, capturedPrisoners);
            });
        }
    }

    // Fires when a vanilla SignalAction_Letter actually sends its letter. Ancient danger uses this
    // path: a hidden RectTrigger sends a signal with the approaching pawn as SUBJECT, then a
    // SignalAction_Letter displays LetterLabelAncientShrineWarning / AncientShrineWarning.
    /// <summary>
    /// Captures generic letter signals for XML event-window rules.
    /// </summary>
    [HarmonyPatch(typeof(SignalAction_Letter), "DoAction")]
    public static class SignalActionLetterEventWindowPatch
    {
        /// <summary>
        /// Harmony Postfix for SignalAction_Letter.DoAction. The hook forwards stable localization
        /// keys instead of translated text so XML can match events without depending on language.
        /// </summary>
        public static void Postfix(SignalAction_Letter __instance, SignalArgs args)
        {
            DiaryPatchSafety.Run("SignalActionLetterEventWindowPatch", () =>
            {
                if (__instance == null)
                {
                    return;
                }

                string letterKey = LetterEventWindowKey(__instance);
                if (string.IsNullOrWhiteSpace(letterKey))
                {
                    return;
                }

                Pawn subjectPawn = LetterSubjectPawn(__instance, args);
                DiaryGameComponent.Current?.RecordEventWindowLetter(
                    letterKey,
                    LetterLabel(__instance, subjectPawn, letterKey),
                    subjectPawn);
            });
        }

        private static string LetterEventWindowKey(SignalAction_Letter action)
        {
            if (!string.IsNullOrWhiteSpace(action.letterMessageKey))
            {
                return action.letterMessageKey;
            }

            return action.letterLabelKey;
        }

        private static Pawn LetterSubjectPawn(SignalAction_Letter action, SignalArgs args)
        {
            Pawn pawn;
            if (args.TryGetArg("SUBJECT", out pawn) && pawn != null)
            {
                return pawn;
            }

            return action.fixedPawnReference;
        }

        private static string LetterLabel(SignalAction_Letter action, Pawn subjectPawn, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(action.letterLabelKey))
            {
                return subjectPawn == null
                    ? action.letterLabelKey.Translate().Resolve()
                    : action.letterLabelKey.Translate(subjectPawn.Named("PAWN")).Resolve();
            }

            return action.letterDef == null ? fallback : action.letterDef.LabelCap.Resolve();
        }
    }

    // Fires after an Ideology ritual applies its outcome. The hook records the finished ritual once,
    // then DiaryGameComponent fans it out to author/target/participant/spectator solo entries.
    /// <summary>
    /// Captures completed Ideology ritual outcomes after vanilla applies their effects.
    /// </summary>
    [HarmonyPatch(typeof(LordJob_Ritual), "ApplyOutcome")]
    public static class RitualOutcomePatch
    {
        /// <summary>
        /// Harmony Postfix for LordJob_Ritual.ApplyOutcome. Runs after vanilla outcome effects and
        /// skips canceled rituals, so only completed ritual events become diary pages.
        /// </summary>
        public static void Postfix(LordJob_Ritual __instance, float progress, bool cancelled)
        {
            DiaryPatchSafety.Run("RitualOutcomePatch", () =>
            {
                if (__instance == null || cancelled)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordRitualFinished(__instance, progress, cancelled);
            });
        }
    }

    // Fires after an Anomaly psychic ritual finishes. Graph.End can run for nested graph transitions;
    // RitualCompleted is the LordToil completion point, after the ritual has actually finished.
    /// <summary>
    /// Captures completed Anomaly psychic rituals at the LordToil completion point.
    /// </summary>
    [HarmonyPatch(typeof(LordToil_PsychicRitual), "RitualCompleted")]
    public static class PsychicRitualCompletedPatch
    {
        /// <summary>
        /// Harmony Postfix for LordToil_PsychicRitual.RitualCompleted. Forwards the completed
        /// psychic ritual instance to the diary after vanilla confirms completion.
        /// </summary>
        public static void Postfix(LordToil_PsychicRitual __instance)
        {
            DiaryPatchSafety.Run("PsychicRitualCompletedPatch", () =>
            {
                PsychicRitual psychicRitual = __instance?.RitualData?.psychicRitual;
                if (psychicRitual == null)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordPsychicRitualFinished(psychicRitual, success: true);
            });
        }
    }

    // Fires after a pawn ability successfully activates on a local map target. Ability covers
    // Royalty psycasts/permits, Biotech/Anomaly powers, and modded abilities that use vanilla defs.
    /// <summary>
    /// Captures successful ability activations that target local map cells/things.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.Activate), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
    public static class AbilityActivateLocalPatch
    {
        /// <summary>
        /// Harmony Postfix for Ability.Activate(LocalTargetInfo, LocalTargetInfo). Only successful
        /// activations are forwarded; the component handles sampling and prompt policy.
        /// </summary>
        public static void Postfix(Ability __instance, LocalTargetInfo target, LocalTargetInfo dest, bool __result)
        {
            DiaryPatchSafety.Run("AbilityActivateLocalPatch", () =>
            {
                if (!__result || __instance == null)
                {
                    return;
                }

                DiaryEvents.Submit(new AbilitySignal(__instance, target, dest));
            });
        }
    }

    // Fires after a pawn ability successfully activates on a world target.
    /// <summary>
    /// Captures successful ability activations that target the world map.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.Activate), new[] { typeof(GlobalTargetInfo) })]
    public static class AbilityActivateGlobalPatch
    {
        /// <summary>
        /// Harmony Postfix for Ability.Activate(GlobalTargetInfo). Only successful activations are
        /// forwarded; the component handles sampling and prompt policy.
        /// </summary>
        public static void Postfix(Ability __instance, GlobalTargetInfo target, bool __result)
        {
            DiaryPatchSafety.Run("AbilityActivateGlobalPatch", () =>
            {
                if (!__result || __instance == null)
                {
                    return;
                }

                DiaryEvents.Submit(new AbilitySignal(__instance, target));
            });
        }
    }
}
