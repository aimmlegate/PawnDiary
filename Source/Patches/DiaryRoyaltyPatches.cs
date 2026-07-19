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
            if (harmony == null || !ModsConfig.RoyaltyActive) return;
            Type type = typeof(CompBladelinkWeapon);
            bool ready = true;
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.CodeFor), new[] { typeof(Pawn) }),
                nameof(CodeForPrefix), nameof(CodeForPostfix));
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_Equipped), new[] { typeof(Pawn) }),
                null, nameof(EquippedPostfix));
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_EquipmentLost), new[] { typeof(Pawn) }),
                null, nameof(EquipmentLostPostfix));
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.PostDestroy),
                    new[] { typeof(DestroyMode), typeof(Map) }),
                nameof(DestroyPrefix), null);
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_MapRemoved), Type.EmptyTypes),
                nameof(MapRemovedPrefix), null);
            ready &= Patch(harmony,
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.UnCode), Type.EmptyTypes),
                nameof(UncodePrefix), null);
            HooksReady = ready;
            if (!ready)
            {
                Log.WarningOnce(
                    "[Pawn Diary] One or more Royalty persona-weapon lifecycle methods changed; "
                        + "the affected diary observation is disabled while vanilla behavior remains untouched.",
                    "PawnDiary.Royalty.MissingHook.PersonaLifecycle".GetHashCode());
            }

            MethodBase titleTarget = AccessTools.DeclaredMethod(
                typeof(Pawn_RoyaltyTracker),
                "OnPostTitleChanged",
                new[] { typeof(Faction), typeof(RoyalTitleDef), typeof(RoyalTitleDef) });
            TitleHookReady = Patch(harmony, titleTarget, null, nameof(TitleChangedPostfix));
            WarnMissingOnce(TitleHookReady, "Pawn_RoyaltyTracker.OnPostTitleChanged(Faction, RoyalTitleDef, RoyalTitleDef)",
                "the faction-aware progression scanner remains active as fallback");

            BestowingHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(RitualOutcomeEffectWorker_Bestowing), "Apply", new[]
                {
                    typeof(float), typeof(Dictionary<Pawn, int>), typeof(LordJob_Ritual)
                }),
                nameof(BestowingPrefix), nameof(BestowingPostfix), nameof(BestowingFinalizer));
            WarnMissingOnce(BestowingHookReady, "RitualOutcomeEffectWorker_Bestowing.Apply",
                "title and psylink scanners remain active as fallback");

            AnimaHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(CompPsylinkable), "FinishLinkingRitual",
                    new[] { typeof(Pawn), typeof(int) }),
                nameof(AnimaPrefix), nameof(AnimaPostfix), nameof(AnimaFinalizer));
            WarnMissingOnce(AnimaHookReady, "CompPsylinkable.FinishLinkingRitual(Pawn, int)",
                "the psylink scanner remains active as fallback");

            NeuroformerHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(CompUseEffect_InstallImplant), "DoEffect",
                    new[] { typeof(Pawn) }),
                nameof(NeuroformerPrefix), nameof(NeuroformerPostfix), nameof(NeuroformerFinalizer));
            WarnMissingOnce(NeuroformerHookReady, "CompUseEffect_InstallImplant.DoEffect(Pawn)",
                "matching neuroformer changes use the unknown-source scanner fallback");

            SuccessionCandidateHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(RoyalTitleDefExt), "TryInherit", new[]
                {
                    typeof(RoyalTitleDef), typeof(Pawn), typeof(Faction),
                    typeof(RoyalTitleInheritanceOutcome).MakeByRefType()
                }),
                null, nameof(SuccessionCandidatePostfix));
            WarnMissingOnce(SuccessionCandidateHookReady, "RoyalTitleDefExt.TryInherit",
                "succession pages are disabled because candidate evidence cannot be proven");

            SuccessionCommitHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(Pawn_RoyaltyTracker), "Notify_PawnKilled", Type.EmptyTypes),
                nameof(SuccessionDeathPrefix), nameof(SuccessionDeathPostfix),
                nameof(SuccessionDeathFinalizer));
            WarnMissingOnce(SuccessionCommitHookReady, "Pawn_RoyaltyTracker.Notify_PawnKilled",
                "succession pages are disabled because the outer commit cannot be proven");

            HeirAppointmentHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(QuestPart_ChangeHeir), "Notify_QuestSignalReceived",
                    new[] { typeof(Signal) }),
                nameof(HeirAppointmentPrefix), nameof(HeirAppointmentPostfix),
                nameof(HeirAppointmentFinalizer));
            WarnMissingOnce(HeirAppointmentHookReady,
                "QuestPart_ChangeHeir.Notify_QuestSignalReceived(Signal)",
                "explicit heir-appointment pages are disabled; automatic heir assignment remains silent");

            PermitOwnerHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.GetPermit),
                    new[] { typeof(RoyalTitlePermitDef), typeof(Faction) }),
                null, nameof(PermitGetPostfix));
            WarnMissingOnce(PermitOwnerHookReady,
                "Pawn_RoyaltyTracker.GetPermit(RoyalTitlePermitDef, Faction)",
                "dramatic permit pages fail closed because their owner cannot be proven");

            PermitUseHookReady = Patch(harmony,
                AccessTools.DeclaredMethod(typeof(FactionPermit), nameof(FactionPermit.Notify_Used),
                    Type.EmptyTypes),
                nameof(PermitUsedPrefix), nameof(PermitUsedPostfix));
            WarnMissingOnce(PermitUseHookReady, "FactionPermit.Notify_Used()",
                "dramatic permit pages are disabled because successful use cannot be proven");
        }

        private static bool Patch(
            Harmony harmony,
            MethodBase target,
            string prefixName,
            string postfixName,
            string finalizerName = null)
        {
            if (target == null) return false;
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
            catch (Exception)
            {
                // The owning registration row emits one bounded, feature-specific warning below.
                // Logging here as well would produce duplicate errors for the same missing seam and
                // could repeat if another compatibility layer retries registration.
                return false;
            }
        }

        private static void WarnMissingOnce(bool ready, string target, string fallback)
        {
            if (ready) return;
            Log.WarningOnce(
                "[Pawn Diary] Could not register Royalty hook " + target + "; " + fallback + ".",
                ("PawnDiary.Royalty.MissingHook." + target).GetHashCode());
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
