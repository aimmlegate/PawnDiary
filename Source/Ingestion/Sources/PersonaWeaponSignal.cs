// Impure Royalty persona lifecycle adapter. The component has already committed the pure state
// transition; this signal localizes one truthful solo page, adds bounded structured context and
// N3-R evidence, then queues generation. No live DLC object crosses into pure policy or saved state.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one formation, meaningful separation/recovery, or standalone ending page.</summary>
    internal sealed class PersonaWeaponSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly PersonaWeaponSnapshot weapon;
        private readonly PersonaBondStateSnapshot previous;
        private readonly PersonaLifecycleDecision lifecycle;
        private readonly List<PersonaTraitFact> selectedTraits;
        private readonly string localizedDuration;
        private readonly RoyaltyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly PersonaWeaponEventData payload;

        /// <summary>The durable event created by Emit, or null when runtime creation did not finish.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        public PersonaWeaponSignal(
            Pawn pawn,
            PersonaWeaponSnapshot weapon,
            PersonaBondStateSnapshot previous,
            PersonaLifecycleDecision lifecycle,
            List<PersonaTraitFact> selectedTraits,
            string localizedDuration,
            RoyaltyPolicySnapshot policy,
            DiaryInteractionGroupDef group,
            int tick)
        {
            this.pawn = pawn;
            this.weapon = weapon;
            this.previous = previous;
            this.lifecycle = lifecycle;
            this.selectedTraits = selectedTraits ?? new List<PersonaTraitFact>();
            this.localizedDuration = localizedDuration ?? string.Empty;
            this.policy = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            this.group = group;
            string defName = PersonaWeaponEventData.DefNameForPhase(lifecycle?.narrativePhase);
            payload = new PersonaWeaponEventData
            {
                PawnId = pawn?.GetUniqueLoadID() ?? string.Empty,
                Tick = Math.Max(0, tick),
                DefName = defName,
                WeaponThingId = weapon?.weaponThingId ?? string.Empty,
                BondEpoch = lifecycle?.nextState?.bondEpoch ?? 0,
                NarrativePhase = lifecycle?.narrativePhase ?? string.Empty,
                PawnEligible = pawn != null && DiaryGameComponent.IsDiaryEligible(pawn),
                HasExactLifecycle = lifecycle?.shouldEmit == true
            };
            PreserveHistoricalOrdering(payload.Tick);
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool enabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.PawnEligible,
                userEnabled: enabled,
                signalEnabled: ModsConfig.RoyaltyActive,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.separationThresholdTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || pawn == null || weapon == null || group == null
                || decision != CaptureDecision.GenerateSolo) return;

            string weaponName = string.IsNullOrWhiteSpace(weapon.displayName)
                ? "PawnDiary.Event.Persona.WeaponFallback".Translate().Resolve()
                : weapon.displayName;
            string label = LabelKey(payload.NarrativePhase).Translate().Resolve();
            string text = FallbackText(weaponName);
            string instruction = InteractionGroups.InstructionForPersonaWeapon(group);
            string context = PersonaWeaponContextFormatter.Format(
                weapon, previous, lifecycle, selectedTraits, localizedDuration, policy);
            CreatedEvent = CreateSoloEvent(
                sink, pawn, null, payload.DefName, label, text, instruction, context);
            if (CreatedEvent == null) return;

            ApplyNarrativeEvidence(sink, CreatedEvent);
            try
            {
                sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
            }
            catch (Exception exception)
            {
                // The durable event remains the owner; ordinary orphan recovery can retry generation.
                Log.ErrorOnce(
                    "[Pawn Diary] Persona lifecycle page was created but its initial generation queue "
                    + "failed; normal recovery may retry it: " + exception,
                    "PawnDiary.PersonaWeapon.Queue".GetHashCode());
            }
        }

        private string FallbackText(string weaponName)
        {
            if (payload.NarrativePhase == PersonaNarrativePhaseTokens.BondFormed)
            {
                if (lifecycle.includesExactPreviousBond && previous != null
                    && !string.IsNullOrWhiteSpace(previous.currentPawnName))
                {
                    return "PawnDiary.Event.Persona.Formed.TransferFallback".Translate(
                        pawn.LabelShortCap, weaponName, previous.currentPawnName).Resolve();
                }
                return "PawnDiary.Event.Persona.Formed.Fallback".Translate(
                    pawn.LabelShortCap, weaponName).Resolve();
            }
            if (payload.NarrativePhase == PersonaNarrativePhaseTokens.BondSeparated)
                return "PawnDiary.Event.Persona.Separated.Fallback".Translate(
                    pawn.LabelShortCap, weaponName, localizedDuration).Resolve();
            if (payload.NarrativePhase == PersonaNarrativePhaseTokens.BondRecovered)
                return "PawnDiary.Event.Persona.Recovered.Fallback".Translate(
                    pawn.LabelShortCap, weaponName, localizedDuration).Resolve();
            return "PawnDiary.Event.Persona.Ended.Fallback".Translate(
                pawn.LabelShortCap, weaponName, EndCauseLabel()).Resolve();
        }

        private string EndCauseLabel()
        {
            string cause = lifecycle?.nextState?.endCauseToken ?? PersonaEndCauseTokens.None;
            string key = cause == PersonaEndCauseTokens.WeaponDestroyed
                ? "PawnDiary.Event.Persona.EndCause.Destroyed"
                : cause == PersonaEndCauseTokens.Transfer
                    ? "PawnDiary.Event.Persona.EndCause.Transfer"
                    : "PawnDiary.Event.Persona.EndCause.Other";
            return key.Translate().Resolve();
        }

        private void ApplyNarrativeEvidence(DiaryGameComponent sink, DiaryEvent diaryEvent)
        {
            try
            {
                string pawnId = pawn.GetUniqueLoadID();
                NarrativeEvidence evidence = RoyaltyNarrativeEvidenceFactory.Persona(
                    diaryEvent.eventId,
                    diaryEvent.tick,
                    pawnId,
                    DiaryEvent.InitiatorRole,
                    weapon,
                    lifecycle.nextState.bondEpoch,
                    lifecycle.narrativePhase,
                    string.Empty,
                    payload.DefName,
                    pawnCanKnow: true);
                if (evidence == null) return;
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = pawnId,
                        povRole = DiaryEvent.InitiatorRole,
                        royalty = sink.RoyaltyNarrativeSnapshotFor(pawn, diaryEvent.tick),
                        recentSelectedCandidateKeys = sink.RecentNarrativeSelectedCandidateKeys(pawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full),
                        evidence = new List<NarrativeEvidence> { evidence }
                    });
                if (result.evidence.Count > 0)
                    diaryEvent.ApplyNarrativeContext(DiaryEvent.InitiatorRole, result);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Persona lifecycle Narrative Continuity evidence failed; the page remains: "
                    + exception,
                    "PawnDiary.PersonaWeapon.NarrativeEvidence".GetHashCode());
            }
        }

        private static string LabelKey(string phase)
        {
            if (phase == PersonaNarrativePhaseTokens.BondFormed) return "PawnDiary.Event.Persona.Formed.Label";
            if (phase == PersonaNarrativePhaseTokens.BondSeparated) return "PawnDiary.Event.Persona.Separated.Label";
            if (phase == PersonaNarrativePhaseTokens.BondRecovered) return "PawnDiary.Event.Persona.Recovered.Label";
            return "PawnDiary.Event.Persona.Ended.Label";
        }
    }
}
