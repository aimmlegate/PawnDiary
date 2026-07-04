// External ingestion signal — the impure capture+emit half of the integration-API event source.
// PawnDiaryApi.SubmitEvent validates a request from another mod, wraps it in this signal, and
// submits it through the same DiaryEvents front door every native Harmony hook uses, so external
// events get the full standard treatment: pure Decide, dedup window, group toggles, LLM queue.
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
    /// <see cref="PawnDiaryApi.SubmitEvent"/> and submitted via
    /// <see cref="DiaryEvents.Submit(DiarySignal)"/>.
    /// </summary>
    public sealed class ExternalEventSignal : DiarySignal
    {
        // Defensive caps so a misbehaving adapter cannot flood one prompt with context. These are
        // parser-style safety limits, not tunable policy, so they stay hardcoded per AGENTS.md.
        private const int MaxExtraContextLines = 16;
        private const int MaxExtraContextLineChars = 200;
        private const int MaxSummaryChars = 800;

        private readonly Pawn subject;
        private readonly Pawn partner;
        private readonly ExternalEventRequest request;
        private readonly DiaryInteractionGroupDef group;
        private readonly ExternalEventData payload;

        public ExternalEventSignal(ExternalEventRequest request)
        {
            this.request = request;

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

            // XML owns which eventKeys count. No matching External-domain group -> no payload ->
            // the dispatcher drops the event silently (PawnDiaryApi already warned once per key).
            group = InteractionGroups.ClassifyExternal(request.eventKey.Trim());
            if (group == null)
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
                HasGroup = true
            };
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.SubjectEligible,
                userEnabled: PawnDiaryMod.Settings.IsGroupEnabled(group.defName),
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
                label = group.LabelCap.Resolve();
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = payload.EventKey;
            }

            string text = CleanSummary(request.summaryText);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "PawnDiary.Event.External".Translate(subject.LabelShortCap, label).Resolve();
            }

            string gameContext = ExternalEventData.BuildGameContext(
                payload.EventKey, payload.SourceId, JoinExtraContext(request.extraContext));
            string instruction = InteractionGroups.InstructionForGroup(group);

            if (decision == CaptureDecision.GeneratePair)
            {
                DiaryEvent pairEvent = sink.AddPairwiseEvent(subject, partner, payload.EventKey, label,
                    text, text, instruction, gameContext);
                sink.QueuePair(pairEvent);
                return;
            }

            // Solo: the partner (when present but ineligible) still feeds the continuity summary.
            DiaryEvent soloEvent = sink.AddSoloEvent(subject, partner, payload.EventKey, label,
                text, instruction, gameContext);
            sink.QueueSolo(soloEvent, DiaryEvent.InitiatorRole);
        }

        // One-lines and length-caps the adapter's summary so a stray multi-paragraph submission
        // cannot distort the prompt or the diary card's raw-text row.
        private static string CleanSummary(string summary)
        {
            string cleaned = PromptTextSanitizer.OneLine(summary);
            if (cleaned.Length > MaxSummaryChars)
            {
                cleaned = cleaned.Substring(0, MaxSummaryChars);
            }

            return cleaned;
        }

        // Sanitizes and joins the adapter's "key=value" lines into the "; "-separated shape the
        // game-context format uses everywhere. Semicolons inside a value would break that framing,
        // so they become commas (same rule as the arrival-context builder).
        private static string JoinExtraContext(List<string> lines)
        {
            return PromptContextLines.Join(lines, MaxExtraContextLines, MaxExtraContextLineChars);
        }
    }
}
