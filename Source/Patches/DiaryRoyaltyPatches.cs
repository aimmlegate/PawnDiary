// Royalty persona-weapon lifecycle hooks. They are registered manually by exact signatures so a
// RimWorld update can disable only this optional feature. Every callback starts with the DLC/play/
// Scribe gates and forwards the live observation to the component's guarded collector without
// changing vanilla behavior; no live object crosses into pure policy or saved state.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Defensively registers the exact Phase-2 persona and Phase-4/5 progression seams.</summary>
    internal static class DiaryRoyaltyPatches
    {
        // Patch failures are startup diagnostics, not player text. Keep enough exception detail to
        // identify an incompatible method/patcher while preventing a pathological exception message
        // from producing an unbounded log entry.
        private const int MaximumPatchFailureDetailCharacters = 240;
        private const int MaximumPersonaFailureSummaryCharacters = 1200;

        private sealed class CodingPatchState
        {
            public ThingWithComps weapon;
            public Pawn pawn;
            public PersonaWeaponSnapshot before;
        }

        private sealed class RoyalMutationPatchState
        {
            public RoyalMutationBatchSnapshot batch;
            public Pawn pawn;
            public Pawn bestower;
            public Faction faction;
            public List<Pawn> participants = new List<Pawn>();
            public bool completed;
        }

        private sealed class RoyalSuccessionPatchState
        {
            public Pawn deceased;
            public RoyalSuccessionDeathScope scope;
            public bool completed;
        }

        private sealed class RoyalHeirAppointmentPatchState
        {
            public RoyalHeirAppointmentSnapshot snapshot;
        }

        private sealed class RoyalPermitUsePatchState
        {
            public bool usedDuringCooldown;
            public int tick;
        }

        /// <summary>True when every required persona lifecycle seam was registered.</summary>
        public static bool HooksReady { get; private set; }
        public static bool TitleHookReady { get; private set; }
        public static bool BestowingHookReady { get; private set; }
        public static bool AnimaHookReady { get; private set; }
        public static bool NeuroformerHookReady { get; private set; }
        public static bool SuccessionCandidateHookReady { get; private set; }
        public static bool SuccessionCommitHookReady { get; private set; }
        public static bool HeirAppointmentHookReady { get; private set; }
        public static bool PermitOwnerHookReady { get; private set; }
        public static bool PermitUseHookReady { get; private set; }

        /// <summary>
        /// Typed failure classification kept separate from the developer-facing detail text. Status
        /// reporting must not change merely because somebody improves the wording of a warning.
        /// </summary>
        private enum PatchFailureKind
        {
            None,
            MissingTarget,
            PatchException
        }

        public static void TryRegister(Harmony harmony)
        {
            HooksReady = false;
            TitleHookReady = false;
            BestowingHookReady = false;
            AnimaHookReady = false;
            NeuroformerHookReady = false;
            SuccessionCandidateHookReady = false;
            SuccessionCommitHookReady = false;
            HeirAppointmentHookReady = false;
            PermitOwnerHookReady = false;
            PermitUseHookReady = false;
            if (harmony == null || !ModsConfig.RoyaltyActive)
            {
                if (harmony != null)
                {
                    DiaryPatchManifest.Report(
                        "Royalty",
                        "all Royalty hooks",
                        DiaryPatchManifest.HookStatus.Skipped,
                        "Royalty inactive");
                }
                return;
            }
            Type type = typeof(CompBladelinkWeapon);
            bool ready = true;
            List<string> personaFailures = new List<string>();
            ready &= PatchRequired(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.CodeFor), new[] { typeof(Pawn) }),
                "CompBladelinkWeapon.CodeFor(Pawn)",
                nameof(CodeForPrefix), nameof(CodeForPostfix), personaFailures);
            ready &= PatchRequired(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_Equipped), new[] { typeof(Pawn) }),
                "CompBladelinkWeapon.Notify_Equipped(Pawn)",
                null, nameof(EquippedPostfix), personaFailures);
            ready &= PatchRequired(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_EquipmentLost), new[] { typeof(Pawn) }),
                "CompBladelinkWeapon.Notify_EquipmentLost(Pawn)",
                null, nameof(EquipmentLostPostfix), personaFailures);
            ready &= PatchRequired(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.PostDestroy),
                    new[] { typeof(DestroyMode), typeof(Map) }),
                "CompBladelinkWeapon.PostDestroy(DestroyMode, Map)",
                nameof(DestroyPrefix), null, personaFailures);
            ready &= PatchRequired(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_MapRemoved), Type.EmptyTypes),
                "CompBladelinkWeapon.Notify_MapRemoved()",
                nameof(MapRemovedPrefix), null, personaFailures);
            ready &= PatchRequired(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.UnCode), Type.EmptyTypes),
                "CompBladelinkWeapon.UnCode()",
                nameof(UncodePrefix), null, personaFailures);
            HooksReady = ready;
            if (!ready)
            {
                Log.WarningOnce(
                    "[Pawn Diary] One or more Royalty persona-weapon lifecycle methods changed; "
                        + "the affected diary observation is disabled while vanilla behavior remains untouched. "
                        + "Failures: " + LimitFailureDetail(string.Join("; ", personaFailures.ToArray()),
                            MaximumPersonaFailureSummaryCharacters) + ".",
                    "PawnDiary.Royalty.MissingHook.PersonaLifecycle".GetHashCode());
            }

            MethodBase titleTarget = AccessTools.DeclaredMethod(
                typeof(Pawn_RoyaltyTracker),
                "OnPostTitleChanged",
                new[] { typeof(Faction), typeof(RoyalTitleDef), typeof(RoyalTitleDef) });
            TitleHookReady = PatchAndWarn(harmony, titleTarget,
                "Pawn_RoyaltyTracker.OnPostTitleChanged(Faction, RoyalTitleDef, RoyalTitleDef)",
                "the faction-aware progression scanner remains active as fallback",
                null, nameof(TitleChangedPostfix));

            BestowingHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(RitualOutcomeEffectWorker_Bestowing), "Apply", new[]
                {
                    typeof(float), typeof(Dictionary<Pawn, int>), typeof(LordJob_Ritual)
                }),
                "RitualOutcomeEffectWorker_Bestowing.Apply",
                "title and psylink scanners remain active as fallback",
                nameof(BestowingPrefix), nameof(BestowingPostfix), nameof(BestowingFinalizer));

            AnimaHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(CompPsylinkable), "FinishLinkingRitual",
                    new[] { typeof(Pawn), typeof(int) }),
                "CompPsylinkable.FinishLinkingRitual(Pawn, int)",
                "the psylink scanner remains active as fallback",
                nameof(AnimaPrefix), nameof(AnimaPostfix), nameof(AnimaFinalizer));

            NeuroformerHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(CompUseEffect_InstallImplant), "DoEffect",
                    new[] { typeof(Pawn) }),
                "CompUseEffect_InstallImplant.DoEffect(Pawn)",
                "matching neuroformer changes use the unknown-source scanner fallback",
                nameof(NeuroformerPrefix), nameof(NeuroformerPostfix), nameof(NeuroformerFinalizer));

            SuccessionCandidateHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(RoyalTitleDefExt), "TryInherit", new[]
                {
                    typeof(RoyalTitleDef), typeof(Pawn), typeof(Faction),
                    typeof(RoyalTitleInheritanceOutcome).MakeByRefType()
                }),
                "RoyalTitleDefExt.TryInherit",
                "succession pages are disabled because candidate evidence cannot be proven",
                null, nameof(SuccessionCandidatePostfix));

            SuccessionCommitHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(Pawn_RoyaltyTracker), "Notify_PawnKilled", Type.EmptyTypes),
                "Pawn_RoyaltyTracker.Notify_PawnKilled",
                "succession pages are disabled because the outer commit cannot be proven",
                nameof(SuccessionDeathPrefix), nameof(SuccessionDeathPostfix),
                nameof(SuccessionDeathFinalizer));

            HeirAppointmentHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(QuestPart_ChangeHeir), "Notify_QuestSignalReceived",
                    new[] { typeof(Signal) }),
                "QuestPart_ChangeHeir.Notify_QuestSignalReceived(Signal)",
                "explicit heir-appointment pages are disabled; automatic heir assignment remains silent",
                nameof(HeirAppointmentPrefix), nameof(HeirAppointmentPostfix),
                nameof(HeirAppointmentFinalizer));

            PermitOwnerHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.GetPermit),
                    new[] { typeof(RoyalTitlePermitDef), typeof(Faction) }),
                "Pawn_RoyaltyTracker.GetPermit(RoyalTitlePermitDef, Faction)",
                "dramatic permit pages fail closed because their owner cannot be proven",
                null, nameof(PermitGetPostfix));

            PermitUseHookReady = PatchAndWarn(harmony,
                AccessTools.DeclaredMethod(typeof(FactionPermit), nameof(FactionPermit.Notify_Used),
                    Type.EmptyTypes),
                "FactionPermit.Notify_Used()",
                "dramatic permit pages are disabled because successful use cannot be proven",
                nameof(PermitUsedPrefix), nameof(PermitUsedPostfix));
        }

        private static bool Patch(
            Harmony harmony,
            MethodBase target,
            string prefixName,
            string postfixName,
            string finalizerName,
            out string failureDetail,
            out PatchFailureKind failureKind)
        {
            failureDetail = string.Empty;
            failureKind = PatchFailureKind.None;
            if (target == null)
            {
                failureKind = PatchFailureKind.MissingTarget;
                failureDetail = "target method was not found";
                return false;
            }
            try
            {
                harmony.Patch(
                    target,
                    prefix: prefixName == null ? null : new HarmonyMethod(typeof(DiaryRoyaltyPatches), prefixName),
                    postfix: postfixName == null ? null : new HarmonyMethod(typeof(DiaryRoyaltyPatches), postfixName),
                    finalizer: finalizerName == null
                        ? null
                        : new HarmonyMethod(typeof(DiaryRoyaltyPatches), finalizerName));
                return true;
            }
            catch (Exception ex)
            {
                // The owning registration row emits one bounded, feature-specific warning below.
                // Logging here as well would produce duplicate errors for the same missing seam and
                // could repeat if another compatibility layer retries registration.
                failureKind = PatchFailureKind.PatchException;
                failureDetail = DescribePatchFailure(ex);
                return false;
            }
        }

        private static bool PatchRequired(
            Harmony harmony,
            MethodBase target,
            string targetLabel,
            string prefixName,
            string postfixName,
            List<string> failures,
            string finalizerName = null)
        {
            string failureDetail;
            PatchFailureKind failureKind;
            bool ready = Patch(
                harmony, target, prefixName, postfixName, finalizerName,
                out failureDetail, out failureKind);
            if (!ready && failures != null)
                failures.Add(targetLabel + " [" + failureDetail + "]");
            DiaryPatchManifest.Report(
                "Royalty",
                targetLabel,
                ready
                    ? DiaryPatchManifest.HookStatus.Applied
                    : failureKind == PatchFailureKind.MissingTarget
                        ? DiaryPatchManifest.HookStatus.Degraded
                        : DiaryPatchManifest.HookStatus.Failed,
                ready ? null : failureDetail);
            return ready;
        }

        private static bool PatchAndWarn(
            Harmony harmony,
            MethodBase target,
            string targetLabel,
            string fallback,
            string prefixName,
            string postfixName,
            string finalizerName = null)
        {
            string failureDetail;
            PatchFailureKind failureKind;
            bool ready = Patch(
                harmony, target, prefixName, postfixName, finalizerName,
                out failureDetail, out failureKind);
            WarnMissingOnce(ready, targetLabel, fallback, failureDetail);
            DiaryPatchManifest.Report(
                "Royalty",
                targetLabel,
                ready
                    ? DiaryPatchManifest.HookStatus.Applied
                    : failureKind == PatchFailureKind.MissingTarget
                        ? DiaryPatchManifest.HookStatus.Degraded
                        : DiaryPatchManifest.HookStatus.Failed,
                ready ? null : failureDetail + "; " + fallback);
            return ready;
        }

        private static void WarnMissingOnce(
            bool ready,
            string target,
            string fallback,
            string failureDetail)
        {
            if (ready) return;
            Log.WarningOnce(
                "[Pawn Diary] Could not register Royalty hook " + target + " [" + failureDetail
                    + "]; " + fallback + ".",
                ("PawnDiary.Royalty.MissingHook." + target).GetHashCode());
        }

        private static string DescribePatchFailure(Exception exception)
        {
            if (exception == null) return "unknown patch failure";
            string message = (exception.Message ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            string detail = exception.GetType().Name;
            if (message.Length > 0) detail += ": " + message;
            return LimitFailureDetail(detail, MaximumPatchFailureDetailCharacters);
        }

        private static string LimitFailureDetail(string value, int maximumCharacters)
        {
            string clean = (value ?? string.Empty).Trim();
            if (maximumCharacters <= 0 || clean.Length <= maximumCharacters) return clean;
            return clean.Substring(0, maximumCharacters - 3) + "...";
        }

        /// <summary>Exercises the caught patch-failure path without changing an installed hook.</summary>
        internal static bool ProbePatchFailureForTests(MethodBase target, out string failureDetail)
        {
            // A known target plus a deliberately absent Harmony instance enters the exact exception
            // branch. No patch can be installed, and the caller can assert that diagnostics survive.
            PatchFailureKind ignoredFailureKind;
            return Patch(
                null, target, null, null, null, out failureDetail, out ignoredFailureKind);
        }

        private static void PermitGetPostfix(Pawn_RoyaltyTracker __instance, FactionPermit __result)
        {
            if (!RuntimeReady() || __instance?.pawn == null || __result?.Permit == null) return;
            // GetPermit is UI-adjacent. Pass the live inputs as value-tuple state so the defensive
            // wrapper's non-capturing lambda is cached instead of allocating a closure per lookup.
            DiaryPatchSafety.Run("Royalty.Permit.GetPermit.Postfix",
                (tracker: __instance, permit: __result), s =>
            {
                RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
                if (RoyalPermitPolicy.FamilyFor(s.permit.Permit.defName, policy).Length == 0) return;
                RoyalPermitOwnerCache.Observe(
                    s.permit,
                    s.tracker.pawn,
                    Find.TickManager?.TicksGame ?? 0,
                    policy);
            });
        }

        private static void PermitUsedPrefix(
            FactionPermit __instance,
            ref RoyalPermitUsePatchState __state)
        {
            __state = null;
            if (!RuntimeReady() || __instance?.Permit == null) return;
            RoyalPermitUsePatchState captured = new RoyalPermitUsePatchState { tick = -1 };
            DiaryPatchSafety.Run("Royalty.Permit.NotifyUsed.Prefix",
                (permit: __instance, captured: captured), s =>
            {
                RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
                if (RoyalPermitPolicy.FamilyFor(s.permit.Permit.defName, policy).Length == 0) return;
                s.captured.usedDuringCooldown = s.permit.OnCooldown;
                s.captured.tick = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            });
            if (captured.tick >= 0) __state = captured;
        }

        private static void PermitUsedPostfix(
            FactionPermit __instance,
            RoyalPermitUsePatchState __state)
        {
            if (__state == null || !RuntimeReady() || __instance == null) return;
            DiaryPatchSafety.Run("Royalty.Permit.NotifyUsed.Postfix",
                (permit: __instance, state: __state), s =>
            {
                RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
                RoyalPermitOwnerResolution owner = RoyalPermitOwnerCache.Resolve(
                    s.permit, s.state.tick, policy);
                if (owner == null) return;
                RoyalPermitUseSnapshot use;
                if (DlcContext.TryCaptureRoyalPermitUse(
                    s.permit,
                    owner.candidate,
                    s.state.usedDuringCooldown,
                    s.state.tick,
                    out use))
                    DiaryGameComponent.Instance?.ObserveRoyalPermitUse(owner.pawn, use);
            });
        }

        private static void CodeForPrefix(
            CompBladelinkWeapon __instance,
            Pawn pawn,
            ref CodingPatchState __state)
        {
            __state = null;
            if (!RuntimeReady() || __instance == null || pawn == null) return;
            CodingPatchState captured = null;
            DiaryPatchSafety.Run("Royalty.Persona.CodeFor.Prefix", () =>
            {
                ThingWithComps weapon = __instance.parent as ThingWithComps;
                if (weapon == null) return;
                captured = new CodingPatchState
                {
                    weapon = weapon,
                    pawn = pawn,
                    before = DiaryGameComponent.Instance?.BeginRoyaltyPersonaCoding(weapon, pawn)
                };
            });
            __state = captured;
        }

        private static void CodeForPostfix(CodingPatchState __state)
        {
            if (__state == null) return;
            if (RuntimeReady())
            {
                DiaryPatchSafety.Run("Royalty.Persona.CodeFor.Postfix", () =>
                {
                    DiaryGameComponent.Instance?.CompleteRoyaltyPersonaCoding(
                        __state.weapon, __state.pawn, __state.before);
                });
            }
        }

        private static void EquippedPostfix(CompBladelinkWeapon __instance, Pawn pawn)
        {
            ObserveEquipment(__instance, pawn, "Royalty.Persona.Equipped");
        }

        private static void EquipmentLostPostfix(CompBladelinkWeapon __instance, Pawn pawn)
        {
            ObserveEquipment(__instance, pawn, "Royalty.Persona.EquipmentLost");
        }

        private static void ObserveEquipment(CompBladelinkWeapon comp, Pawn pawn, string patchName)
        {
            if (!RuntimeReady() || comp == null || pawn == null) return;
            DiaryPatchSafety.Run(patchName, () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaEquipment(
                    comp.parent as ThingWithComps, pawn);
            });
        }

        private static void DestroyPrefix(CompBladelinkWeapon __instance)
        {
            if (!RuntimeReady() || __instance == null) return;
            DiaryPatchSafety.Run("Royalty.Persona.Destroy", () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaDestroyed(
                    __instance.parent as ThingWithComps);
            });
        }

        private static void MapRemovedPrefix(CompBladelinkWeapon __instance)
        {
            if (!RuntimeReady() || __instance == null) return;
            DiaryPatchSafety.Run("Royalty.Persona.MapRemoved", () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaMapRemoved(
                    __instance.parent as ThingWithComps);
            });
        }

        private static void UncodePrefix(CompBladelinkWeapon __instance)
        {
            if (!RuntimeReady() || __instance == null) return;
            DiaryPatchSafety.Run("Royalty.Persona.UnCode", () =>
            {
                DiaryGameComponent.Instance?.ObserveRoyaltyPersonaUncode(
                    __instance.parent as ThingWithComps);
            });
        }

        private static void TitleChangedPostfix(
            Pawn_RoyaltyTracker __instance,
            Faction faction,
            RoyalTitleDef prevTitle,
            RoyalTitleDef newTitle)
        {
            if (!RuntimeReady()) return;
            DiaryPatchSafety.Run("Royalty.Title.OnPostTitleChanged", () =>
            {
                Pawn pawn;
                RoyalTitleSnapshot previous;
                RoyalTitleSnapshot current;
                if (DlcContext.TryCaptureRoyalTitleTransition(
                    __instance, faction, prevTitle, newTitle,
                    out pawn, out previous, out current))
                    DiaryGameComponent.Instance?.ObserveRoyalTitleHook(pawn, previous, current);
            });
        }

        private static void BestowingPrefix(
            Dictionary<Pawn, int> totalPresence,
            LordJob_Ritual jobRitual,
            ref RoyalMutationPatchState __state)
        {
            __state = null;
            if (!RuntimeReady()) return;
            RoyalMutationPatchState capturedState = null;
            DiaryPatchSafety.Run("Royalty.Bestowing.Prefix", () =>
            {
                Pawn target;
                Pawn bestower;
                Faction faction;
                if (!DlcContext.TryCaptureBestowingActors(jobRitual, out target, out bestower, out faction)) return;
                RoyalMutationPatchState state = new RoyalMutationPatchState
                {
                    pawn = target,
                    bestower = bestower,
                    faction = faction,
                    batch = DiaryGameComponent.Instance?.BeginRoyalMutationCause(
                        target, faction, RoyalMutationCauseTokens.ImperialBestowing)
                };
                if (totalPresence != null)
                    foreach (Pawn pawn in totalPresence.Keys)
                        if (pawn != null) state.participants.Add(pawn);
                capturedState = state;
            });
            __state = capturedState;
        }

        private static void BestowingPostfix(
            float progress,
            RoyalMutationPatchState __state)
        {
            if (__state == null || !RuntimeReady()) return;
            DiaryPatchSafety.Run("Royalty.Bestowing.Postfix", () =>
            {
                // Mark closed before any downstream dispatch. If dispatch throws after consuming the
                // batch, the Harmony finalizer must not retry the same exact mutation boundary.
                __state.completed = true;
                if (__state.batch != null)
                    DiaryGameComponent.Instance?.CompleteRoyalMutationCause(
                        __state.batch, __state.pawn, __state.faction);
                PawnDiary.Ingestion.RitualFanoutSignal ritual =
                    PawnDiary.Ingestion.RitualFanoutSignal.CreateRoyalBestowing(
                        __state.bestower, __state.pawn, __state.participants, progress);
                if (ritual != null) PawnDiary.Ingestion.DiaryEvents.Submit(ritual);
            });
        }

        private static Exception BestowingFinalizer(
            Exception __exception,
            RoyalMutationPatchState __state)
        {
            CompleteInterruptedMutation(__state, "Royalty.Bestowing.Finalizer");
            return __exception;
        }

        private static void AnimaPrefix(Pawn pawn, ref RoyalMutationPatchState __state)
        {
            BeginSimpleMutation(pawn, RoyalMutationCauseTokens.AnimaLinking, null, ref __state,
                "Royalty.Anima.Prefix");
        }

        private static void AnimaPostfix(RoyalMutationPatchState __state)
        {
            CompleteSimpleMutation(__state, "Royalty.Anima.Postfix");
        }

        private static Exception AnimaFinalizer(Exception __exception, RoyalMutationPatchState __state)
        {
            CompleteInterruptedMutation(__state, "Royalty.Anima.Finalizer");
            return __exception;
        }

        private static void NeuroformerPrefix(
            CompUseEffect_InstallImplant __instance,
            Pawn user,
            ref RoyalMutationPatchState __state)
        {
            __state = null;
            if (!RuntimeReady() || !DlcContext.IsNeuroformerUse(__instance)) return;
            BeginSimpleMutation(user, RoyalMutationCauseTokens.Neuroformer, null, ref __state,
                "Royalty.Neuroformer.Prefix");
        }

        private static void NeuroformerPostfix(RoyalMutationPatchState __state)
        {
            CompleteSimpleMutation(__state, "Royalty.Neuroformer.Postfix");
        }

        private static Exception NeuroformerFinalizer(Exception __exception, RoyalMutationPatchState __state)
        {
            CompleteInterruptedMutation(__state, "Royalty.Neuroformer.Finalizer");
            return __exception;
        }

        private static void SuccessionDeathPrefix(
            Pawn_RoyaltyTracker __instance,
            ref RoyalSuccessionPatchState __state)
        {
            __state = null;
            if (!RuntimeReady() || __instance?.pawn == null) return;
            RoyalSuccessionPatchState captured = null;
            DiaryPatchSafety.Run("Royalty.Succession.NotifyPawnKilled.Prefix", () =>
            {
                captured = new RoyalSuccessionPatchState
                {
                    deceased = __instance.pawn,
                    scope = DiaryGameComponent.Instance?.BeginRoyalSuccessionDeath(__instance.pawn)
                };
            });
            __state = captured;
        }

        private static void SuccessionCandidatePostfix(
            RoyalTitleDef title,
            Pawn from,
            Faction faction,
            RoyalTitleInheritanceOutcome outcome,
            bool __result)
        {
            if (!__result || !RuntimeReady()) return;
            DiaryPatchSafety.Run("Royalty.Succession.TryInherit.Postfix", () =>
            {
                RoyalSuccessionCandidateSnapshot candidate;
                if (DlcContext.TryCaptureSuccessionCandidate(
                    title, from, faction, outcome, Find.TickManager?.TicksGame ?? 0, out candidate))
                    DiaryGameComponent.Instance?.ObserveRoyalSuccessionCandidate(candidate);
            });
        }

        private static void SuccessionDeathPostfix(RoyalSuccessionPatchState __state)
        {
            if (__state == null || __state.completed || !RuntimeReady()) return;
            DiaryPatchSafety.Run("Royalty.Succession.NotifyPawnKilled.Postfix", () =>
            {
                __state.completed = true;
                DiaryGameComponent.Instance?.CompleteRoyalSuccessionDeath(
                    __state.deceased, __state.scope);
            });
        }

        private static Exception SuccessionDeathFinalizer(
            Exception __exception,
            RoyalSuccessionPatchState __state)
        {
            if (__state != null && !__state.completed)
            {
                __state.completed = true;
                DiaryPatchSafety.Run("Royalty.Succession.NotifyPawnKilled.Finalizer", () =>
                    DiaryGameComponent.Instance?.CancelRoyalSuccessionDeath(__state.scope));
            }
            return __exception;
        }

        private static void HeirAppointmentPrefix(
            QuestPart_ChangeHeir __instance,
            Signal signal,
            ref RoyalHeirAppointmentPatchState __state)
        {
            __state = null;
            if (!RuntimeReady() || __instance == null || signal.tag != __instance.inSignal) return;
            RoyalHeirAppointmentPatchState captured = null;
            DiaryPatchSafety.Run("Royalty.HeirAppointment.Prefix", () =>
            {
                RoyalHeirAppointmentSnapshot snapshot;
                if (DlcContext.TryCaptureHeirAppointment(
                    __instance, Find.TickManager?.TicksGame ?? 0, out snapshot))
                    captured = new RoyalHeirAppointmentPatchState { snapshot = snapshot };
            });
            __state = captured;
        }

        private static void HeirAppointmentPostfix(
            QuestPart_ChangeHeir __instance,
            RoyalHeirAppointmentPatchState __state)
        {
            if (__state?.snapshot == null || !RuntimeReady()) return;
            DiaryPatchSafety.Run("Royalty.HeirAppointment.Postfix", () =>
            {
                if (DlcContext.HeirAppointmentCommitted(__instance, __state.snapshot))
                    DiaryGameComponent.Instance?.ObserveRoyalHeirAppointment(
                        __instance.heir, __state.snapshot);
            });
        }

        private static Exception HeirAppointmentFinalizer(
            Exception __exception,
            RoyalHeirAppointmentPatchState __state)
        {
            // The state contains detached facts only and is not queued. Returning the original
            // exception preserves vanilla/mod behavior while guaranteeing no false page is emitted.
            return __exception;
        }

        private static void BeginSimpleMutation(
            Pawn pawn,
            string cause,
            Faction faction,
            ref RoyalMutationPatchState state,
            string patchName)
        {
            state = null;
            if (!RuntimeReady() || pawn == null) return;
            RoyalMutationPatchState captured = null;
            DiaryPatchSafety.Run(patchName, () =>
            {
                captured = new RoyalMutationPatchState
                {
                    pawn = pawn,
                    faction = faction,
                    batch = DiaryGameComponent.Instance?.BeginRoyalMutationCause(pawn, faction, cause)
                };
            });
            state = captured;
        }

        private static void CompleteSimpleMutation(RoyalMutationPatchState state, string patchName)
        {
            if (state == null || state.completed || !RuntimeReady()) return;
            DiaryPatchSafety.Run(patchName, () =>
            {
                // See BestowingPostfix: completion consumes the active scope, so retries are unsafe.
                state.completed = true;
                if (state.batch != null)
                    DiaryGameComponent.Instance?.CompleteRoyalMutationCause(
                        state.batch, state.pawn, state.faction);
            });
        }

        private static void CompleteInterruptedMutation(RoyalMutationPatchState state, string patchName)
        {
            if (state == null || state.completed || !RuntimeReady()) return;
            // If vanilla or another mod throws after partially changing state, close the exact boundary
            // and let its unclaimed mutation expire into one truthful progression fallback.
            CompleteSimpleMutation(state, patchName);
        }

        private static bool RuntimeReady()
        {
            return ModsConfig.RoyaltyActive
                && Verse.Current.ProgramState == ProgramState.Playing
                && Scribe.mode == LoadSaveMode.Inactive;
        }
    }
}
