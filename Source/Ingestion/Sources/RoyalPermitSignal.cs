// Impure adapter from a detached successful-use snapshot to one RoyalPermit diary page. All visible
// values were copied and sanitized on the main thread; the saved context never retains a live permit,
// Pawn_RoyaltyTracker, Def, Faction, Map, or target object.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one truthful solo page for an allowlisted successful dramatic permit use.</summary>
    internal sealed class RoyalPermitSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly RoyalPermitUseSnapshot use;
        private readonly RoyaltyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly RoyalPermitEventData payload;

        internal DiaryEvent CreatedEvent { get; private set; }

        public RoyalPermitSignal(
            Pawn pawn,
            RoyalPermitUseSnapshot use,
            RoyaltyPolicySnapshot policy,
            DiaryInteractionGroupDef group)
        {
            this.pawn = pawn;
            this.use = use;
            this.policy = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            this.group = group;
            payload = new RoyalPermitEventData
            {
                PawnId = use?.ownerPawnId ?? string.Empty,
                Tick = Math.Max(0, use?.tick ?? 0),
                DefName = RoyalPermitPolicy.EventDefNameForFamily(use?.permitFamilyToken),
                PermitDefName = use?.permitDefName ?? string.Empty,
                PermitFamily = use?.permitFamilyToken ?? string.Empty,
                FactionId = use?.factionId ?? string.Empty,
                PawnEligible = pawn != null && DiaryGameComponent.IsDiaryEligible(pawn),
                HasExactSuccessfulUse = use != null
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
                signalEnabled: ModsConfig.RoyaltyActive && policy.enabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.permitRepeatSuppressionTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || pawn == null || use == null || group == null
                || decision != CaptureDecision.GenerateSolo) return;
            string familyFallbackKey = FamilyFallbackKey(use.permitFamilyToken);
            string eventFallbackKey = FallbackKey(use.permitFamilyToken);
            // Unknown families should be unreachable through the reviewed allowlist. Fail closed if
            // a future family is added without its localized wording instead of silently calling it
            // an orbital salvo.
            if (familyFallbackKey.Length == 0 || eventFallbackKey.Length == 0) return;
            string permit = string.IsNullOrWhiteSpace(use.permitLabel)
                ? familyFallbackKey.Translate().Resolve()
                : use.permitLabel;
            string faction = string.IsNullOrWhiteSpace(use.factionName)
                ? "PawnDiary.Event.RoyalPermit.FactionFallback".Translate().Resolve()
                : use.factionName;
            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.RoyalPermit.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string text = eventFallbackKey
                .Translate(pawn.LabelShortCap, permit, faction).Resolve();
            if (use.usedDuringCooldown)
                text += " " + "PawnDiary.Event.RoyalPermit.CooldownSuffix".Translate().Resolve();
            CreatedEvent = CreateSoloEvent(
                sink,
                pawn,
                null,
                payload.DefName,
                label,
                text,
                InteractionGroups.InstructionForRoyalPermit(group),
                RoyalPermitContextFormatter.Format(use, policy));
            if (CreatedEvent == null) return;

            ApplyNarrativeEvidence(sink, CreatedEvent);
            sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
        }

        /// <summary>
        /// Attaches exact successful-use evidence after the canonical page exists. The existing generic
        /// Royalty title provider may then add current authority context for this exact caller; failures
        /// preserve the permit page and never turn intent or an unknown permit into evidence.
        /// </summary>
        private void ApplyNarrativeEvidence(DiaryGameComponent sink, DiaryEvent diaryEvent)
        {
            if (sink == null || diaryEvent == null || pawn == null || use == null) return;

            try
            {
                string pawnId = pawn.GetUniqueLoadID();
                NarrativeEvidence evidence = RoyalPermitPolicy.BuildNarrativeEvidence(
                    diaryEvent.eventId,
                    diaryEvent.tick,
                    pawnId,
                    DiaryEvent.InitiatorRole,
                    use,
                    policy);
                if (evidence == null) return;

                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = pawnId,
                        povRole = DiaryEvent.InitiatorRole,
                        evidence = new List<NarrativeEvidence> { evidence },
                        royalty = sink.RoyaltyNarrativeSnapshotFor(pawn, diaryEvent.tick),
                        odyssey = sink.OdysseyNarrativeSnapshotFor(pawn, diaryEvent.tick),
                        recentSelectedCandidateKeys = sink.RecentNarrativeSelectedCandidateKeys(pawnId),
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full)
                    });
                if (result.evidence.Count > 0)
                {
                    diaryEvent.ApplyNarrativeContext(DiaryEvent.InitiatorRole, result);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Royal permit Narrative Continuity evidence failed; the permit page remains: "
                    + exception,
                    "PawnDiary.RoyalPermit.NarrativeEvidence".GetHashCode());
            }
        }

        private static string FallbackKey(string family)
        {
            if (family == RoyalPermitFamilyTokens.MilitaryAid)
                return "PawnDiary.Event.RoyalPermit.MilitaryAid.Fallback";
            if (family == RoyalPermitFamilyTokens.TransportShuttle)
                return "PawnDiary.Event.RoyalPermit.TransportShuttle.Fallback";
            if (family == RoyalPermitFamilyTokens.OrbitalStrike)
                return "PawnDiary.Event.RoyalPermit.OrbitalStrike.Fallback";
            if (family == RoyalPermitFamilyTokens.OrbitalSalvo)
                return "PawnDiary.Event.RoyalPermit.OrbitalSalvo.Fallback";
            return string.Empty;
        }

        private static string FamilyFallbackKey(string family)
        {
            if (family == RoyalPermitFamilyTokens.MilitaryAid)
                return "PawnDiary.Event.RoyalPermit.MilitaryAid.PermitFallback";
            if (family == RoyalPermitFamilyTokens.TransportShuttle)
                return "PawnDiary.Event.RoyalPermit.TransportShuttle.PermitFallback";
            if (family == RoyalPermitFamilyTokens.OrbitalStrike)
                return "PawnDiary.Event.RoyalPermit.OrbitalStrike.PermitFallback";
            if (family == RoyalPermitFamilyTokens.OrbitalSalvo)
                return "PawnDiary.Event.RoyalPermit.OrbitalSalvo.PermitFallback";
            return string.Empty;
        }
    }
}
