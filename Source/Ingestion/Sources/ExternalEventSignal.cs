// External ingestion signal — the impure capture+emit half of the integration-API event source.
// PawnDiaryApi.SubmitEvent validates a request from another mod, wraps it in this signal, and
// submits it through the same DiaryEvents front door every native Harmony hook uses, so external
// events get the full standard treatment: pure Decide, dedup window, pawn gates, LLM queue.
// The forceRecord flag keeps the same capture/emit shape but asks Dispatch to skip the soft
// budget/dedup drops for adapter-owned moments that must create a diary event.
//
// The pure decision + game-context format live in Source/Capture/Events/ExternalEventData.cs.
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Integration;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one integration-API event and emits it as a solo or pairwise diary event. Built by
    /// <see cref="PawnDiaryApi.SubmitEvent"/> / prompt-entry submission and submitted via
    /// <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    internal sealed class ExternalEventSignal : DiarySignal
    {
        private readonly Pawn subject;
        private readonly Pawn partner;
        private readonly ExternalEventRequest request;
        private readonly DiaryInteractionGroupDef group;
        private readonly ExternalEventData payload;

        private readonly bool groupRequired;

        public ExternalEventSignal(ExternalEventRequest request)
            : this(request, true)
        {
        }

        public ExternalEventSignal(ExternalEventRequest request, bool groupRequired)
        {
            this.request = request;
            this.groupRequired = groupRequired;

            // Same guard shape as RomanceSignal: drop (payload stays null) unless a game is playing
            // and the request carries the required pieces. PawnDiaryApi validates first, but the
            // signal re-guards so a direct construction can never throw in the dispatcher.
            if (!DiaryGameComponent.GamePlaying || request == null || request.subject == null
                || string.IsNullOrWhiteSpace(request.eventKey) || PawnDiaryMod.Settings == null)
            {
                return;
            }

            subject = request.subject;
            partner = request.partner;

            // Ordinary external events require XML policy to claim the key. Wrapped prompt entries
            // may run without a group because the caller supplies the event instruction, but an
            // optional group still contributes label, styling, and prompt metadata when present.
            group = InteractionGroups.ClassifyExternal(request.eventKey.Trim());
            if (group == null && groupRequired)
            {
                return;
            }

            payload = new ExternalEventData
            {
                PawnId = subject.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                EventKey = request.eventKey.Trim(),
                SourceId = (request.sourceId ?? string.Empty).Trim(),
                SubjectPawnId = subject.GetUniqueLoadID(),
                PartnerPawnId = partner == null ? string.Empty : partner.GetUniqueLoadID(),
                SubjectEligible = DiaryGameComponent.IsDiaryEligible(subject),
                PartnerEligible = partner != null && DiaryGameComponent.IsDiaryEligible(partner),
                HasGroup = group != null,
                GroupRequired = groupRequired
            };
        }

        /// <summary>
        /// Event created by <see cref="Emit"/>. Null when validation, policy, or dedup dropped the
        /// request before an entry was written.
        /// </summary>
        public DiaryEvent CreatedEvent { get; private set; }

        /// <summary>True when <see cref="CreatedEvent"/> has both subject and partner POVs.</summary>
        public bool CreatedPairwise { get; private set; }

        public override DiaryEventData Payload => payload;

        public override bool ForceRecord => request != null && request.forceRecord;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.SubjectEligible,
                // Player event filters are for automatic game listeners only. External submissions
                // are governed by the integration master switch, validation, budget, and pawn gates.
                userEnabled: true,
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        // A custom request key still gets the "external|eventKey|" prefix so adapters can never
        // collide with another source's (or another adapter's) dedup namespace.
        public override string DedupKey
        {
            get
            {
                if (payload == null)
                {
                    return string.Empty;
                }

                string custom = request.dedupKey;
                return string.IsNullOrWhiteSpace(custom)
                    ? payload.DedupKey()
                    : "external|" + payload.EventKey + "|" + custom.Trim();
            }
        }

        public override int DedupWindowTicks =>
            request != null && request.dedupTicks > 0
                ? request.dedupTicks
                : DiaryTuning.Current.externalEventDedupTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo && decision != CaptureDecision.GeneratePair)
            {
                return;
            }

            // Impure build: label, XML prompt instruction, event text, gameContext. The label chain
            // is adapter label -> group label -> raw eventKey, so the UI always has something.
            string label = DiaryLineCleaner.CleanLine(request.eventLabel);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = group == null ? string.Empty : group.LabelCap.Resolve();
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = payload.EventKey;
            }

            string text = ExternalEventRequestText.CleanSummary(request.summaryText);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "PawnDiary.Event.External".Translate(subject.LabelShortCap, label).Resolve();
            }

            string promptInstruction = PromptInstructionFor(request);
            string requestContext = ExternalEventRequestText.JoinRequestContext(
                promptInstruction,
                request.promptFragment,
                request.promptEnchantmentCandidates,
                request.replacePromptEnchantments,
                request.extraContext,
                DiaryTuning.IntegrationPromptFragmentMaxChars,
                DiaryTuning.IntegrationPromptEnchantmentMaxCandidates,
                DiaryTuning.IntegrationPromptEnchantmentCandidateMaxChars);
            string gameContext = ExternalEventData.BuildGameContext(
                payload.EventKey,
                payload.SourceId,
                requestContext);
            string instruction = group == null ? string.Empty : InteractionGroups.InstructionForGroup(group);

            if (decision == CaptureDecision.GeneratePair)
            {
                DiaryEvent pairEvent = sink.AddPairwiseEvent(subject, partner, payload.EventKey, label,
                    text, text, instruction, gameContext);
                CreatedEvent = pairEvent;
                CreatedPairwise = pairEvent != null;
                sink.QueuePair(pairEvent);
                return;
            }

            // Solo: the partner (when present but ineligible) still feeds the continuity summary.
            DiaryEvent soloEvent = sink.AddSoloEvent(subject, partner, payload.EventKey, label,
                text, instruction, gameContext);
            CreatedEvent = soloEvent;
            CreatedPairwise = false;
            sink.QueueSolo(soloEvent, DiaryEvent.InitiatorRole);
        }

        private static string PromptInstructionFor(ExternalEventRequest request)
        {
            ExternalPromptEntryRequest promptRequest = request as ExternalPromptEntryRequest;
            return promptRequest == null ? null : promptRequest.promptInstruction;
        }

    }
}
