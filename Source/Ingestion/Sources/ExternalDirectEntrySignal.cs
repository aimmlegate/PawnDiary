// External direct-entry signal — the impure capture+emit half of
// PawnDiaryApi.SubmitDirectEntry. Unlike ExternalEventSignal, this path does not queue the main
// first-person LLM rewrite: the adapter supplied final prose, and the signal writes that prose to
// the created event immediately.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Integration;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one caller-authored diary page and emits it as a completed solo or pairwise entry.
    /// Built by <see cref="PawnDiaryApi.SubmitDirectEntry"/>.
    /// </summary>
    internal sealed class ExternalDirectEntrySignal : DiarySignal
    {
        // Defensive summary cap. Parser safety bound, not feature policy; body/title caps live in
        // DiaryTuningDef XML. extraContext caps + reserved-key filtering live in the shared
        // ExternalEventRequestText.JoinAdapterExtraContext used by Emit.
        private const int MaxSummaryChars = 800;

        private readonly Pawn subject;
        private readonly Pawn partner;
        private readonly ExternalDirectEntryRequest request;
        private readonly DiaryInteractionGroupDef group;
        private readonly ExternalEventData payload;
        private readonly string subjectText;
        private readonly string partnerText;

        public ExternalDirectEntrySignal(ExternalDirectEntryRequest request)
        {
            this.request = request;

            // PawnDiaryApi validates first, but the signal re-guards so direct construction can never
            // throw in the shared dispatcher. Blank cleaned prose means there is nothing to save.
            if (!DiaryGameComponent.GamePlaying || request == null || request.subject == null
                || string.IsNullOrWhiteSpace(request.eventKey) || PawnDiaryMod.Settings == null)
            {
                return;
            }

            subjectText = ExternalDirectEntryText.CleanProse(request.text, DiaryTuning.IntegrationDirectTextMaxChars);
            if (string.IsNullOrWhiteSpace(subjectText))
            {
                return;
            }

            subject = request.subject;
            partner = request.partner;
            partnerText = ExternalDirectEntryText.CleanProse(
                request.partnerText, DiaryTuning.IntegrationDirectTextMaxChars);

            string eventKey = request.eventKey.Trim();
            string sourceId = string.IsNullOrWhiteSpace(request.sourceId)
                ? "unknown-source"
                : request.sourceId.Trim();
            bool hasPartnerText = partner != null && !string.IsNullOrWhiteSpace(partnerText);
            bool forceRecord = request.forceRecord;

            // Direct prose can stand alone without XML prompt policy. If an External-domain group does
            // claim the key, its label and styling still apply.
            group = InteractionGroups.ClassifyExternal(eventKey);
            payload = new ExternalEventData
            {
                PawnId = subject.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                EventKey = eventKey,
                SourceId = sourceId,
                SubjectPawnId = subject.GetUniqueLoadID(),
                PartnerPawnId = hasPartnerText ? partner.GetUniqueLoadID() : string.Empty,
                SubjectEligible = forceRecord
                    ? DiaryGameComponent.IsDiaryEligible(subject)
                    : DiaryGameComponent.Instance != null
                        && DiaryGameComponent.Instance.CanWriteExternalDirectEntryFor(subject),
                PartnerEligible = hasPartnerText && (forceRecord
                    ? DiaryGameComponent.IsDiaryEligible(partner)
                    : DiaryGameComponent.Instance != null
                        && DiaryGameComponent.Instance.CanWriteExternalDirectEntryFor(partner)),
                HasGroup = group != null,
                GroupRequired = false
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

        // Keep the same dedup namespace as external LLM-queued events, so an adapter can safely choose
        // whether a factual event or a direct prose entry should win inside a window.
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

            string label = BuildLabel();
            string gameContext = ExternalEventData.BuildGameContext(
                payload.EventKey,
                payload.SourceId,
                ExternalEventRequestText.JoinAdapterExtraContext(request.extraContext));
            string instruction = group == null ? string.Empty : InteractionGroups.InstructionForGroup(group);

            if (decision == CaptureDecision.GeneratePair
                && CanWritePartnerDirectEntry(sink)
                && !string.IsNullOrWhiteSpace(partnerText))
            {
                DiaryEvent pairEvent = sink.AddPairwiseEvent(subject, partner, payload.EventKey, label,
                    RawTextFor(subject, label, subjectText, request.summaryText),
                    RawTextFor(partner, label, partnerText, request.summaryText),
                    instruction,
                    gameContext);

                bool subjectApplied = sink.ApplyExternalDirectEntryText(
                    pairEvent, DiaryEvent.InitiatorRole, subjectText, request.title, request.generateTitleIfMissing);
                bool partnerApplied = sink.ApplyExternalDirectEntryText(
                    pairEvent, DiaryEvent.RecipientRole, partnerText, request.partnerTitle, request.generateTitleIfMissing);

                if (subjectApplied)
                {
                    CreatedEvent = pairEvent;
                    CreatedPairwise = partnerApplied;
                }

                return;
            }

            DiaryEvent soloEvent = sink.AddSoloEvent(subject, partner, payload.EventKey, label,
                RawTextFor(subject, label, subjectText, request.summaryText),
                instruction,
                gameContext);
            bool applied = sink.ApplyExternalDirectEntryText(
                soloEvent, DiaryEvent.InitiatorRole, subjectText, request.title, request.generateTitleIfMissing);
            if (applied)
            {
                CreatedEvent = soloEvent;
                CreatedPairwise = false;
            }
        }

        private string BuildLabel()
        {
            string label = DiaryLineCleaner.CleanLine(request.eventLabel);
            if (string.IsNullOrWhiteSpace(label) && group != null)
            {
                label = group.LabelCap.Resolve();
            }

            return string.IsNullOrWhiteSpace(label) ? payload.EventKey : label;
        }

        private bool CanWritePartnerDirectEntry(DiaryGameComponent sink)
        {
            return ForceRecord
                ? DiaryGameComponent.IsDiaryEligible(partner)
                : sink.CanWriteExternalDirectEntryFor(partner);
        }

        private static string RawTextFor(Pawn pawn, string label, string prose, string summary)
        {
            string text = ExternalDirectEntryText.CleanSummary(summary, MaxSummaryChars);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = DiarySentenceExcerpt.FirstSentence(prose, MaxSummaryChars);
            }

            return string.IsNullOrWhiteSpace(text)
                ? "PawnDiary.Event.External".Translate(pawn.LabelShortCap, label).Resolve()
                : text;
        }
    }
}
