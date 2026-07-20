// Defensive registration for exact Anomaly study, containment, visible creepjoiner, and ghoul
// transformation seams. The paid DLC's C# types exist in every RimWorld installation, but these
// hooks register only when Anomaly is active; DlcContext owns live reads and vanilla always continues.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using PawnDiary.Capture;
using PawnDiary.Ingestion;

namespace PawnDiary
{
    /// <summary>Registers and owns the optional <c>CompStudyUnlocks.OnStudied</c> before/after hook.</summary>
    internal static class DiaryAnomalyPatches
    {
        private const string StudyUnlocksTypeName = "RimWorld.CompStudyUnlocks";
        private const string KnowledgeCategoryTypeName = "RimWorld.KnowledgeCategoryDef";
        private const string HoldingPlatformTargetTypeName =
            "RimWorld.CompHoldingPlatformTarget";
        private const string CreepJoinerTrackerTypeName =
            "RimWorld.Pawn_CreepJoinerTracker";
        private const string SurgicalInspectionRecipeTypeName =
            "RimWorld.Recipe_SurgicalInspection";
        private const string GhoulInfusionRecipeTypeName =
            "RimWorld.Recipe_GhoulInfusion";

        /// <summary>True only when the exact A1.2 study method was found and patched.</summary>
        public static bool StudyHookReady { get; private set; }

        /// <summary>True only when the exact A1.3 Escape(bool initiator) method was patched.</summary>
        public static bool ContainmentHookReady { get; private set; }

        /// <summary>True only when exact DoRejection plus all required private markers were patched.</summary>
        public static bool CreepJoinerRejectionHookReady { get; private set; }

        /// <summary>True only when exact DoAggressive plus its private marker were patched.</summary>
        public static bool CreepJoinerAggressionHookReady { get; private set; }

        /// <summary>True only when exact DoLeave plus its private marker were patched.</summary>
        public static bool CreepJoinerDepartureHookReady { get; private set; }

        /// <summary>True only when the exact recipe, Pawn-result, and tracker disclosure seams are patched.</summary>
        public static bool CreepJoinerSurgicalHookReady { get; private set; }

        /// <summary>True only when the exact ghoul infusion ApplyOnPawn signature was patched.</summary>
        public static bool GhoulTransformationHookReady { get; private set; }

        /// <summary>
        /// Registers the exact signature defensively. A changed/missing target disables only study
        /// milestones and leaves vanilla study and the generic StudiedEntity Tale untouched.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            StudyHookReady = false;
            ContainmentHookReady = false;
            CreepJoinerRejectionHookReady = false;
            CreepJoinerAggressionHookReady = false;
            CreepJoinerDepartureHookReady = false;
            CreepJoinerSurgicalHookReady = false;
            GhoulTransformationHookReady = false;
            if (harmony == null || !ModsConfig.AnomalyActive) return;

            TryRegisterStudy(harmony);
            TryRegisterContainment(harmony);
            TryRegisterCreepJoinerOutcomes(harmony);
            TryRegisterCreepJoinerSurgicalInspection(harmony);
            TryRegisterGhoulTransformation(harmony);
        }

        private static void TryRegisterStudy(Harmony harmony)
        {
            try
            {
                Type studyType = AccessTools.TypeByName(StudyUnlocksTypeName);
                Type categoryType = AccessTools.TypeByName(KnowledgeCategoryTypeName);
                MethodBase target = studyType == null || categoryType == null
                    ? null
                    : AccessTools.DeclaredMethod(
                        studyType,
                        "OnStudied",
                        new[] { typeof(Pawn), typeof(float), categoryType });
                if (target == null)
                {
                    WarnMissing("the exact CompStudyUnlocks.OnStudied(Pawn, float, KnowledgeCategoryDef) target was not found");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(DiaryAnomalyPatches), nameof(StudyPrefix)),
                    postfix: new HarmonyMethod(typeof(DiaryAnomalyPatches), nameof(StudyPostfix)));
                StudyHookReady = true;
            }
            catch (Exception exception)
            {
                WarnMissing("registration failed: " + exception.GetType().Name + ": "
                    + Limit(exception.Message));
            }
        }

        private static void TryRegisterContainment(Harmony harmony)
        {
            try
            {
                Type targetType = AccessTools.TypeByName(HoldingPlatformTargetTypeName);
                MethodInfo target = targetType == null
                    ? null
                    : AccessTools.DeclaredMethod(
                        targetType, "Escape", new[] { typeof(bool) }) as MethodInfo;
                ParameterInfo[] parameters = target?.GetParameters();
                if (target == null || target.ReturnType != typeof(void)
                    || parameters == null || parameters.Length != 1
                    || !string.Equals(parameters[0].Name, "initiator", StringComparison.Ordinal))
                {
                    WarnMissingContainment(
                        "the exact CompHoldingPlatformTarget.Escape(bool initiator) target was not found");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(ContainmentPrefix)),
                    postfix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(ContainmentPostfix)),
                    finalizer: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(ContainmentFinalizer)));
                ContainmentHookReady = true;
            }
            catch (Exception exception)
            {
                WarnMissingContainment("registration failed: " + exception.GetType().Name + ": "
                    + Limit(exception.Message));
            }
        }

        private static void TryRegisterCreepJoinerOutcomes(Harmony harmony)
        {
            Type trackerType = AccessTools.TypeByName(CreepJoinerTrackerTypeName);
            CreepJoinerReflectionHealth fields = DlcContext.ResolveCreepJoinerReflection(trackerType);
            CreepJoinerRejectionHookReady = TryRegisterCreepJoinerOutcome(
                harmony,
                trackerType,
                "DoRejection",
                fields.rejectionReady,
                nameof(CreepJoinerRejectionPrefix),
                nameof(CreepJoinerRejectionPostfix),
                nameof(CreepJoinerRejectionFinalizer),
                "Rejection");
            CreepJoinerAggressionHookReady = TryRegisterCreepJoinerOutcome(
                harmony,
                trackerType,
                "DoAggressive",
                fields.aggressionReady,
                nameof(CreepJoinerAggressionPrefix),
                nameof(CreepJoinerOutcomePostfix),
                null,
                "Aggression");
            CreepJoinerDepartureHookReady = TryRegisterCreepJoinerOutcome(
                harmony,
                trackerType,
                "DoLeave",
                fields.departureReady,
                nameof(CreepJoinerDeparturePrefix),
                nameof(CreepJoinerOutcomePostfix),
                null,
                "Departure");
        }

        private static bool TryRegisterCreepJoinerOutcome(
            Harmony harmony,
            Type trackerType,
            string methodName,
            bool fieldsReady,
            string prefixName,
            string postfixName,
            string finalizerName,
            string outcomeLabel)
        {
            try
            {
                MethodInfo target = trackerType == null
                    ? null
                    : AccessTools.DeclaredMethod(trackerType, methodName, Type.EmptyTypes) as MethodInfo;
                if (!fieldsReady || target == null || !target.IsPublic
                    || target.ReturnType != typeof(void) || target.GetParameters().Length != 0)
                {
                    WarnMissingCreepJoiner(outcomeLabel,
                        "the exact public Pawn_CreepJoinerTracker." + methodName
                            + "() target or required committed-transition field was not found");
                    return false;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(DiaryAnomalyPatches), prefixName),
                    postfix: new HarmonyMethod(typeof(DiaryAnomalyPatches), postfixName),
                    finalizer: finalizerName == null
                        ? null
                        : new HarmonyMethod(typeof(DiaryAnomalyPatches), finalizerName));
                return true;
            }
            catch (Exception exception)
            {
                WarnMissingCreepJoiner(outcomeLabel,
                    "registration failed: " + exception.GetType().Name + ": "
                        + Limit(exception.Message));
                return false;
            }
        }

        private static void TryRegisterCreepJoinerSurgicalInspection(Harmony harmony)
        {
            try
            {
                Type recipeType = AccessTools.TypeByName(SurgicalInspectionRecipeTypeName);
                Type trackerType = AccessTools.TypeByName(CreepJoinerTrackerTypeName);
                MethodInfo recipe = recipeType == null ? null : AccessTools.DeclaredMethod(
                    recipeType,
                    "ApplyOnPawn",
                    new[]
                    {
                        typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn),
                        typeof(List<Thing>), typeof(Bill)
                    }) as MethodInfo;
                MethodInfo tracker = trackerType == null ? null : AccessTools.DeclaredMethod(
                    trackerType,
                    "DoSurgicalInspection",
                    new[] { typeof(Pawn), typeof(StringBuilder) }) as MethodInfo;
                MethodInfo pawnInspection = AccessTools.DeclaredMethod(
                    typeof(Pawn),
                    "DoSurgicalInspection",
                    new[] { typeof(Pawn), typeof(string).MakeByRefType() }) as MethodInfo;
                if (recipe == null || !recipe.IsPublic || recipe.ReturnType != typeof(void)
                    || !ParametersNamed(
                        recipe, "pawn", "part", "billDoer", "ingredients", "bill")
                    || tracker == null || !tracker.IsPublic || tracker.ReturnType != typeof(bool)
                    || !ParametersNamed(tracker, "surgeon", "sb")
                    || pawnInspection == null || !pawnInspection.IsPublic
                    || pawnInspection.ReturnType != typeof(SurgicalInspectionOutcome)
                    || !ParametersNamed(pawnInspection, "surgeon", "desc")
                    || !pawnInspection.GetParameters()[1].IsOut)
                {
                    WarnMissingSurgical(
                        "the exact recipe, Pawn result, or tracker disclosure signature was not found");
                    return;
                }

                harmony.Patch(
                    recipe,
                    prefix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(SurgicalRecipePrefix)),
                    postfix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(SurgicalRecipePostfix)),
                    finalizer: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(SurgicalRecipeFinalizer)));
                harmony.Patch(
                    tracker,
                    prefix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(SurgicalTrackerPrefix)),
                    postfix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(SurgicalTrackerPostfix)));
                harmony.Patch(
                    pawnInspection,
                    postfix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(SurgicalPawnInspectionPostfix)));
                CreepJoinerSurgicalHookReady = true;
            }
            catch (Exception exception)
            {
                // A partial registration remains inert because every callback checks the composite
                // health flag, leaving the ordinary DidSurgery route untouched.
                CreepJoinerSurgicalHookReady = false;
                WarnMissingSurgical("registration failed: " + exception.GetType().Name + ": "
                    + Limit(exception.Message));
            }
        }

        private static void TryRegisterGhoulTransformation(Harmony harmony)
        {
            try
            {
                Type recipeType = AccessTools.TypeByName(GhoulInfusionRecipeTypeName);
                MethodInfo recipe = recipeType == null ? null : AccessTools.DeclaredMethod(
                    recipeType,
                    "ApplyOnPawn",
                    new[]
                    {
                        typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn),
                        typeof(List<Thing>), typeof(Bill)
                    }) as MethodInfo;
                if (recipe == null || !recipe.IsPublic || recipe.ReturnType != typeof(void)
                    || !ParametersNamed(
                        recipe, "pawn", "part", "billDoer", "ingredients", "bill"))
                {
                    WarnMissingGhoul(
                        "the exact public Recipe_GhoulInfusion.ApplyOnPawn(Pawn, "
                            + "BodyPartRecord, Pawn, List<Thing>, Bill) signature was not found");
                    return;
                }

                harmony.Patch(
                    recipe,
                    prefix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(GhoulInfusionPrefix)),
                    postfix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(GhoulInfusionPostfix)),
                    finalizer: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(GhoulInfusionFinalizer)));
                GhoulTransformationHookReady = true;
            }
            catch (Exception exception)
            {
                GhoulTransformationHookReady = false;
                WarnMissingGhoul("registration failed: " + exception.GetType().Name + ": "
                    + Limit(exception.Message));
            }
        }

        /// <summary>Captures exact progress, completion, identity, and activation state before vanilla.</summary>
        private static void StudyPrefix(
            object __instance,
            Pawn studier,
            ref AnomalyStudyFacts __state)
        {
            __state = null;
            if (!RuntimeReady()) return;

            AnomalyStudyFacts captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.StudyPrefix", () =>
            {
                DlcContext.TryCaptureAnomalyStudyBefore(
                    __instance, studier, out captured);
            });
            __state = captured;
        }

        /// <summary>Observes only committed progress after vanilla and never changes vanilla's result.</summary>
        private static void StudyPostfix(
            object __instance,
            Pawn studier,
            object category,
            AnomalyStudyFacts __state)
        {
            if (!RuntimeReady() || __state == null) return;
            DiaryPatchSafety.Run(
                "DiaryAnomalyPatches.StudyPostfix",
                (instance: __instance, pawn: studier, categoryObject: category, before: __state),
                state => DiaryGameComponent.Instance?.CompleteAnomalyStudy(
                    state.instance, state.pawn, state.categoryObject, state.before));
        }

        /// <summary>Opens one exact synchronous frame before vanilla ejects the held entity.</summary>
        private static void ContainmentPrefix(
            object __instance,
            bool initiator,
            ref ContainmentEscapeCallState __state)
        {
            __state = null;
            if (!ContainmentHookReady || !RuntimeReady()) return;
            ContainmentEscapeCallState captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.ContainmentPrefix", () =>
            {
                captured = ContainmentEscapeScopeStack.Begin(__instance, initiator);
            });
            __state = captured;
        }

        /// <summary>Verifies the post-ejection state and lets only the outer frame submit one signal.</summary>
        private static void ContainmentPostfix(ContainmentEscapeCallState __state)
        {
            if (__state == null) return;
            ContainmentEscapeCompletion completion = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.ContainmentPostfix.Close", () =>
            {
                completion = ContainmentEscapeScopeStack.Complete(__state);
            });
            if (completion == null) return;

            DiaryPatchSafety.Run("DiaryAnomalyPatches.ContainmentPostfix.Emit", () =>
            {
                ContainmentBreachPlan plan = ContainmentBreachPolicy.Plan(
                    completion.facts, completion.policy);
                if (!plan.writePage) return;
                List<Pawn> writers = completion.ResolveWriters(plan);
                if (writers.Count != plan.selectedWriters.Count)
                {
                    Log.WarningOnce(
                        "[Pawn Diary] A verified containment breach was skipped because its captured "
                            + "writer identities could not be resolved consistently.",
                        "PawnDiary.Anomaly.Containment.WriterResolution".GetHashCode());
                    return;
                }
                DiaryInteractionGroupDef group = InteractionGroups.ClassifyAnomalyEvent(
                    AnomalyEventDefNames.ContainmentBreach);
                DiaryEvents.Submit(new AnomalyContainmentBreachSignal(
                    completion.facts,
                    plan,
                    completion.policy,
                    group,
                    writers,
                    completion.FirstEscapedPawn(plan)));
            });
        }

        /// <summary>Always clears an unfinished call/scope while preserving vanilla's exception.</summary>
        private static Exception ContainmentFinalizer(
            Exception __exception,
            ContainmentEscapeCallState __state)
        {
            if (__state == null || __state.completed) return __exception;
            try
            {
                ContainmentEscapeScopeStack.Abort(__state);
            }
            catch (Exception cleanupException)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Anomaly containment scope cleanup failed and was skipped: "
                        + cleanupException,
                    "PawnDiary.Anomaly.Containment.Finalizer".GetHashCode());
            }
            return __exception;
        }

        /// <summary>Opens one exact patient/surgeon transaction before the recipe can fail early.</summary>
        private static void SurgicalRecipePrefix(
            Pawn pawn,
            Pawn billDoer,
            ref CreepJoinerSurgicalInspectionCapture __state)
        {
            __state = null;
            if (!CreepJoinerSurgicalHookReady || !RuntimeReady()) return;
            CreepJoinerSurgicalInspectionCapture captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalRecipePrefix", () =>
            {
                AnomalyPolicySnapshot policy = DiaryAnomalyPolicy.Snapshot();
                if (!DlcContext.TryCaptureCreepJoinerSurgicalInspection(
                        pawn, billDoer, out captured)
                    || !CreepJoinerSurgicalInspectionScope.Begin(
                        captured, policy.taleOwnershipMaxDepth))
                {
                    captured = null;
                }
            });
            __state = captured;
        }

        /// <summary>Captures builder length only inside the exact active outer recipe.</summary>
        private static void SurgicalTrackerPrefix(
            object __instance,
            Pawn surgeon,
            StringBuilder sb,
            ref CreepJoinerSurgicalTrackerCallState __state)
        {
            __state = null;
            if (!CreepJoinerSurgicalHookReady || !RuntimeReady()) return;
            CreepJoinerSurgicalTrackerCallState captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalTrackerPrefix", () =>
            {
                DlcContext.TryCaptureCreepJoinerSurgicalTrackerCall(
                    __instance,
                    surgeon,
                    sb,
                    CreepJoinerSurgicalInspectionScope.Current,
                    out captured);
            });
            __state = captured;
        }

        /// <summary>Accepts tracker evidence only when true was returned and the builder grew.</summary>
        private static void SurgicalTrackerPostfix(
            object __instance,
            Pawn surgeon,
            StringBuilder sb,
            bool __result,
            CreepJoinerSurgicalTrackerCallState __state)
        {
            if (__state == null || !CreepJoinerSurgicalHookReady) return;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalTrackerPostfix", () =>
            {
                DlcContext.CompleteCreepJoinerSurgicalTrackerCall(
                    __instance, surgeon, sb, __result, __state);
            });
        }

        /// <summary>
        /// Verifies that the whole Pawn inspection remained letter-visible; a DetectedNoLetter result
        /// from another inspectable condition must release the generic surgery Tale.
        /// </summary>
        private static void SurgicalPawnInspectionPostfix(
            Pawn __instance,
            Pawn surgeon,
            SurgicalInspectionOutcome __result)
        {
            if (!CreepJoinerSurgicalHookReady || !RuntimeReady()) return;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalPawnInspectionPostfix", () =>
            {
                DlcContext.ObserveCreepJoinerSurgicalInspectionResult(
                    __instance,
                    surgeon,
                    __result,
                    CreepJoinerSurgicalInspectionScope.Current);
            });
        }

        /// <summary>Commits non-terminal history first, then owns the generic Tale only on page success.</summary>
        private static void SurgicalRecipePostfix(
            Pawn pawn,
            Pawn billDoer,
            CreepJoinerSurgicalInspectionCapture __state)
        {
            if (__state == null) return;
            CreepJoinerSurgicalInspectionClose close = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalRecipePostfix.Close", () =>
            {
                close = CreepJoinerSurgicalInspectionScope.Complete(__state);
            });
            if (close == null) return;
            ReleaseSurgicalTales(close.releaseTales);
            if (!close.matched) return;

            CreepJoinerSurgicalDisclosurePlan plan = null;
            bool dedicatedEventCreated = false;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalRecipePostfix.Complete", () =>
            {
                DiaryGameComponent component = DiaryGameComponent.Instance;
                if (component != null)
                {
                    plan = component.CompleteCreepJoinerSurgicalInspection(
                        pawn, billDoer, close.capture, out dedicatedEventCreated);
                }
            });
            bool suppress = AnomalySurgeryTaleOwnershipPolicy.ShouldSuppress(
                CreepJoinerSurgicalDisclosurePolicy.OwnsDidSurgery(plan),
                dedicatedEventCreated,
                close.deferredTale != null);
            if (!suppress) ReleaseSurgicalTale(close.deferredTale);
        }

        /// <summary>Always aborts an unfinished surgery scope and preserves vanilla's exception.</summary>
        private static Exception SurgicalRecipeFinalizer(
            Exception __exception,
            CreepJoinerSurgicalInspectionCapture __state)
        {
            if (__state == null || __state.scopeClosed) return __exception;
            CreepJoinerSurgicalInspectionClose close = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalRecipeFinalizer", () =>
            {
                close = CreepJoinerSurgicalInspectionScope.Abort(__state);
            });
            if (close != null) ReleaseSurgicalTales(close.releaseTales);
            return __exception;
        }

        /// <summary>Freezes exact pre-state and opens Tale ownership before vanilla's failure check.</summary>
        private static void GhoulInfusionPrefix(
            Pawn pawn,
            Pawn billDoer,
            ref GhoulTransformationCapture __state)
        {
            __state = null;
            if (!GhoulTransformationHookReady || !RuntimeReady()) return;
            GhoulTransformationCapture captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.GhoulInfusionPrefix", () =>
            {
                AnomalyPolicySnapshot policy = DiaryAnomalyPolicy.Snapshot();
                if (!DlcContext.TryCaptureGhoulTransformation(pawn, billDoer, out captured)
                    || !GhoulInfusionScope.Begin(captured, policy.taleOwnershipMaxDepth))
                {
                    captured = null;
                }
            });
            __state = captured;
        }

        /// <summary>Verifies post-state and consumes DidSurgery only after the dedicated page exists.</summary>
        private static void GhoulInfusionPostfix(
            Pawn pawn,
            Pawn billDoer,
            GhoulTransformationCapture __state)
        {
            if (__state == null) return;
            GhoulInfusionClose close = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.GhoulInfusionPostfix.Close", () =>
            {
                close = GhoulInfusionScope.Complete(__state);
            });
            if (close == null) return;
            ReleaseSurgicalTales(close.releaseTales);
            if (!close.matched) return;

            GhoulTransformationPlan plan = null;
            bool dedicatedEventCreated = false;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.GhoulInfusionPostfix.Complete", () =>
            {
                DiaryGameComponent component = DiaryGameComponent.Instance;
                if (component != null)
                {
                    plan = component.CompleteGhoulTransformation(
                        pawn, billDoer, close.capture, out dedicatedEventCreated);
                }
            });
            bool suppress = AnomalySurgeryTaleOwnershipPolicy.ShouldSuppress(
                GhoulTransformationPolicy.OwnsDidSurgery(plan),
                dedicatedEventCreated,
                close.deferredTale != null);
            if (!suppress) ReleaseSurgicalTale(close.deferredTale);
        }

        /// <summary>Always aborts an unfinished ghoul scope and preserves vanilla's exception.</summary>
        private static Exception GhoulInfusionFinalizer(
            Exception __exception,
            GhoulTransformationCapture __state)
        {
            if (__state == null || __state.scopeClosed) return __exception;
            GhoulInfusionClose close = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.GhoulInfusionFinalizer", () =>
            {
                close = GhoulInfusionScope.Abort(__state);
            });
            if (close != null) ReleaseSurgicalTales(close.releaseTales);
            return __exception;
        }

        private static void ReleaseSurgicalTales(List<TaleSignal> signals)
        {
            if (signals == null) return;
            for (int i = 0; i < signals.Count; i++) ReleaseSurgicalTale(signals[i]);
        }

        private static void ReleaseSurgicalTale(TaleSignal signal)
        {
            if (signal == null) return;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.SurgicalTaleRelease", () =>
            {
                DiaryEvents.Submit(signal);
            });
        }

        /// <summary>Captures rejection before-state and owns nested calls only for a visible outer response.</summary>
        private static void CreepJoinerRejectionPrefix(
            object __instance,
            ref CreepJoinerOutcomeCapture __state)
        {
            __state = null;
            if (!CreepJoinerRejectionHookReady || !RuntimeReady()) return;
            CreepJoinerOutcomeCapture captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.CreepJoinerRejectionPrefix", () =>
            {
                AnomalyPolicySnapshot policy = DiaryAnomalyPolicy.Snapshot();
                if (!DlcContext.TryCaptureCreepJoinerOutcomeBefore(
                    __instance,
                    AnomalyOutcomeTokens.Rejected,
                    policy.creepJoinerWitnessRadius,
                    out captured)) return;
                // A letterless modded rejection must not suppress a nested visible DoAggressive or
                // DoLeave page. The outer callback remains captured so its committed marker can still
                // become a silent replay barrier when no nested visible response owns the transition.
                if (!captured.visibleResponseExpected) return;
                if (!CreepJoinerOutcomeScope.BeginRejection(captured.SubjectPawnId))
                {
                    captured = null;
                    return;
                }
                captured.ownsRejectionScope = true;
            });
            __state = captured;
        }

        /// <summary>Captures aggressive before-state; a surrounding rejection remains semantic owner.</summary>
        private static void CreepJoinerAggressionPrefix(
            object __instance,
            ref CreepJoinerOutcomeCapture __state)
        {
            CaptureCreepJoinerOutcomePrefix(
                __instance,
                AnomalyOutcomeTokens.Aggressive,
                CreepJoinerAggressionHookReady,
                ref __state);
        }

        /// <summary>Captures departure before-state; a surrounding rejection remains semantic owner.</summary>
        private static void CreepJoinerDeparturePrefix(
            object __instance,
            ref CreepJoinerOutcomeCapture __state)
        {
            CaptureCreepJoinerOutcomePrefix(
                __instance,
                AnomalyOutcomeTokens.Departed,
                CreepJoinerDepartureHookReady,
                ref __state);
        }

        private static void CaptureCreepJoinerOutcomePrefix(
            object instance,
            string phase,
            bool hookReady,
            ref CreepJoinerOutcomeCapture state)
        {
            state = null;
            if (!hookReady || !RuntimeReady()) return;
            CreepJoinerOutcomeCapture captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.CreepJoinerOutcomePrefix." + phase, () =>
            {
                DlcContext.TryCaptureCreepJoinerOutcomeBefore(
                    instance,
                    phase,
                    DiaryAnomalyPolicy.Snapshot().creepJoinerWitnessRadius,
                    out captured);
            });
            state = captured;
        }

        /// <summary>Closes rejection ownership, verifies normal return, and submits at most one page.</summary>
        private static void CreepJoinerRejectionPostfix(
            object __instance,
            CreepJoinerOutcomeCapture __state)
        {
            if (__state == null) return;
            EndRejectionScope(__state);
            CompleteCreepJoinerOutcome(__instance, __state);
        }

        /// <summary>Verifies one normal aggressive/departure return and submits only committed truth.</summary>
        private static void CreepJoinerOutcomePostfix(
            object __instance,
            CreepJoinerOutcomeCapture __state)
        {
            if (__state == null) return;
            CompleteCreepJoinerOutcome(__instance, __state);
        }

        private static void CompleteCreepJoinerOutcome(
            object instance,
            CreepJoinerOutcomeCapture capture)
        {
            DiaryPatchSafety.Run(
                "DiaryAnomalyPatches.CreepJoinerOutcomePostfix",
                (tracker: instance, state: capture),
                row => DiaryGameComponent.Instance?.CompleteCreepJoinerOutcome(
                    row.tracker, row.state));
        }

        /// <summary>Always unwinds a rejection owner and preserves vanilla's exception unchanged.</summary>
        private static Exception CreepJoinerRejectionFinalizer(
            Exception __exception,
            CreepJoinerOutcomeCapture __state)
        {
            if (__state != null) EndRejectionScope(__state);
            return __exception;
        }

        private static void EndRejectionScope(CreepJoinerOutcomeCapture capture)
        {
            if (capture == null || !capture.ownsRejectionScope) return;
            CreepJoinerOutcomeScope.EndRejection(capture.SubjectPawnId);
            capture.ownsRejectionScope = false;
        }

        private static bool RuntimeReady()
        {
            return ModsConfig.AnomalyActive
                && DiaryGameComponent.GamePlaying
                && Scribe.mode == LoadSaveMode.Inactive;
        }

        private static bool ParametersNamed(MethodInfo method, params string[] names)
        {
            ParameterInfo[] parameters = method?.GetParameters();
            if (parameters == null || parameters.Length != names.Length) return false;
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.Equals(parameters[i].Name, names[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static string Limit(string value)
        {
            string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= 240 ? clean : clean.Substring(0, 240);
        }

        private static void WarnMissing(string detail)
        {
            Log.WarningOnce(
                "[Pawn Diary] Anomaly study milestones are disabled because " + detail
                    + "; vanilla study and generic Tales remain unchanged.",
                "PawnDiary.Anomaly.MissingHook.Study".GetHashCode());
        }

        private static void WarnMissingContainment(string detail)
        {
            Log.WarningOnce(
                "[Pawn Diary] Anomaly containment breach capture is disabled because " + detail
                    + "; vanilla escape letters, entity behavior, and other diary routes remain unchanged.",
                "PawnDiary.Anomaly.MissingHook.Containment".GetHashCode());
        }

        private static void WarnMissingCreepJoiner(string outcome, string detail)
        {
            Log.WarningOnce(
                "[Pawn Diary] Anomaly creepjoiner " + outcome.ToLowerInvariant()
                    + " capture is disabled because " + detail
                    + "; vanilla creepjoiner behavior and other diary routes remain unchanged.",
                ("PawnDiary.Anomaly.MissingHook.CreepJoiner." + outcome).GetHashCode());
        }

        private static void WarnMissingSurgical(string detail)
        {
            Log.WarningOnce(
                "[Pawn Diary] Anomaly creepjoiner surgical disclosure capture is disabled because "
                    + detail
                    + "; vanilla surgery, letters, Tales, and the generic diary Tale route remain unchanged.",
                "PawnDiary.Anomaly.MissingHook.CreepJoiner.Surgical".GetHashCode());
        }

        private static void WarnMissingGhoul(string detail)
        {
            Log.WarningOnce(
                "[Pawn Diary] Anomaly ghoul transformation capture is disabled because " + detail
                    + "; vanilla infusion, Tales, and the generic diary Tale route remain unchanged.",
                "PawnDiary.Anomaly.MissingHook.GhoulTransformation".GetHashCode());
        }
    }
}
