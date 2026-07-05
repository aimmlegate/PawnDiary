// Prompt-preview adapter for the public integration API. It builds a throwaway DiaryEvent from an
// ExternalEventRequest, runs the same prompt planner as live generation, and returns the assembled
// prompts without registering the event, queueing LLM work, spending tokens, or consuming RNG.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using PawnDiary.Capture;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Builds a side-effect-free preview for one external-event prompt POV. Null means the
        /// request would not currently produce a prompt for the requested role.
        /// </summary>
        internal DiaryPromptPreviewSnapshot PreviewExternalEventPrompt(
            ExternalEventRequest request,
            string requestedPovRole)
        {
            if (request == null || request.subject == null || string.IsNullOrWhiteSpace(request.eventKey)
                || PawnDiaryMod.Settings == null)
            {
                return null;
            }

            // Defense-in-depth: the public PawnDiaryApi.PreviewPrompt wrapper already enforces the
            // master integration toggle before calling this helper. Mirror it here so a future
            // internal caller (e.g. a debug action) cannot bypass the player's "Allow external mod
            // integrations" switch. Same shape as PawnDiaryApi.ExternalIntegrationsAllowed.
            if (!PawnDiaryMod.Settings.allowExternalIntegrations)
            {
                return null;
            }

            string eventKey = request.eventKey.Trim();
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyExternal(eventKey);
            bool groupRequired = !(request is ExternalPromptEntryRequest);
            if (group == null && groupRequired)
            {
                return null;
            }

            Pawn subject = request.subject;
            Pawn partner = request.partner;
            bool subjectEligible = IsDiaryEligible(subject);
            bool partnerEligible = partner != null && IsDiaryEligible(partner);

            ExternalEventData payload = new ExternalEventData
            {
                PawnId = subject.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                EventKey = eventKey,
                SourceId = (request.sourceId ?? string.Empty).Trim(),
                SubjectPawnId = subject.GetUniqueLoadID(),
                PartnerPawnId = partner == null ? string.Empty : partner.GetUniqueLoadID(),
                SubjectEligible = subjectEligible,
                PartnerEligible = partnerEligible,
                HasGroup = group != null,
                GroupRequired = groupRequired
            };

            CaptureDecision decision = ExternalEventData.Decide(
                payload,
                BuildCaptureContext(
                    eligible: subjectEligible,
                    // Prompt previews model the external API path, so saved auto-capture filters do
                    // not make adapter-owned prompts unavailable.
                    userEnabled: true,
                    signalEnabled: true,
                    ambientSignalEnabled: true));
            if (decision != CaptureDecision.GenerateSolo && decision != CaptureDecision.GeneratePair)
            {
                return null;
            }

            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            Rand.PushState();
            try
            {
                bool pairwise = decision == CaptureDecision.GeneratePair;
                DiaryEvent previewEvent = BuildExternalPreviewEvent(request, group, payload, pairwise);
                if (previewEvent == null)
                {
                    return null;
                }

                MarkIncapacitatedPovSkipped(previewEvent, DiaryEvent.InitiatorRole, subject);
                if (pairwise)
                {
                    MarkIncapacitatedPovSkipped(previewEvent, DiaryEvent.RecipientRole, partner);
                }

                bool initiatorQueueable = DiaryGenerationEnabledFor(previewEvent, DiaryEvent.InitiatorRole)
                    && previewEvent.CanQueueGeneration(DiaryEvent.InitiatorRole);
                bool recipientQueueable = pairwise
                    && DiaryGenerationEnabledFor(previewEvent, DiaryEvent.RecipientRole)
                    && previewEvent.CanQueueGeneration(DiaryEvent.RecipientRole);

                string povRole = ResolvePreviewPovRole(requestedPovRole, pairwise, initiatorQueueable, recipientQueueable);
                if (string.IsNullOrWhiteSpace(povRole))
                {
                    return null;
                }

                bool requiresPriorPovText = pairwise
                    && DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole)
                    && initiatorQueueable;

                DiaryPromptPlan promptPlan = DiaryPromptBuilder.BuildSequentialInteractionPromptPlan(
                    previewEvent,
                    povRole,
                    PersonaRuleFor(previewEvent, povRole),
                    PromptEnchantmentRuleFor(previewEvent, povRole),
                    0,
                    HumorCueFor(previewEvent));

                if (promptPlan == null)
                {
                    return null;
                }

                DiaryEventPayload previewPayload = DiaryPipelineAdapters.ToPayload(previewEvent);
                DiaryPolicySnapshot previewPolicy = DiaryPipelineAdapters.PolicyFor(previewPayload);

                return new DiaryPromptPreviewSnapshot
                {
                    sourceId = payload.SourceId ?? string.Empty,
                    eventKey = eventKey,
                    povRole = povRole,
                    subjectPawnId = payload.SubjectPawnId ?? string.Empty,
                    partnerPawnId = pairwise ? payload.PartnerPawnId ?? string.Empty : string.Empty,
                    pairwise = pairwise,
                    groupDefName = group == null ? string.Empty : group.defName ?? string.Empty,
                    templateKey = promptPlan.templateKey ?? string.Empty,
                    eventPromptKey = previewPolicy?.group?.eventPromptKey ?? string.Empty,
                    forcedModelName = promptPlan.forcedModelName ?? string.Empty,
                    maxTokens = promptPlan.responseRules?.maxTokens ?? 0,
                    requiresPriorPovText = requiresPriorPovText,
                    systemPrompt = promptPlan.systemPrompt ?? string.Empty,
                    userPrompt = promptPlan.userPrompt ?? string.Empty,
                    combinedPrompt = DiaryPromptCapture.Format(promptPlan.systemPrompt, promptPlan.userPrompt)
                };
            }
            finally
            {
                Rand.PopState();
                UnityEngine.Random.state = randomState;
            }
        }

        private DiaryEvent BuildExternalPreviewEvent(
            ExternalEventRequest request,
            DiaryInteractionGroupDef group,
            ExternalEventData payload,
            bool pairwise)
        {
            Pawn subject = request.subject;
            Pawn partner = request.partner;
            if (subject == null || payload == null)
            {
                return null;
            }

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

            string requestContext = ExternalEventRequestText.JoinRequestContext(
                PromptInstructionFor(request),
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
            string eventId = PreviewEventId(payload.EventKey, payload.SubjectPawnId, pairwise ? payload.PartnerPawnId : string.Empty);
            string date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero);
            System.Collections.Generic.IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();

            if (pairwise && partner != null)
            {
                return new DiaryEvent
                {
                    eventId = eventId,
                    tick = Find.TickManager.TicksGame,
                    date = date,
                    interactionDefName = payload.EventKey,
                    interactionLabel = label,
                    initiatorPawnId = subject.GetUniqueLoadID(),
                    recipientPawnId = partner.GetUniqueLoadID(),
                    initiatorName = subject.LabelShortCap,
                    recipientName = partner.LabelShortCap,
                    initiatorText = text,
                    recipientText = text,
                    neutralText = text,
                    gameContext = gameContext,
                    instruction = instruction,
                    colorCue = DiaryEvent.ResolveColorCue(payload.EventKey, gameContext),
                    initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(subject),
                    recipientPawnSummary = DiaryContextBuilder.BuildPawnSummary(partner),
                    initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(subject),
                    recipientSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(partner),
                    initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(subject, partner, activeEvents),
                    recipientContinuity = DiaryContextBuilder.BuildContinuitySummary(partner, subject, activeEvents),
                    initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(subject.GetUniqueLoadID(), activeEvents),
                    recipientLastOpener = DiaryContextBuilder.LatestDiaryOpener(partner.GetUniqueLoadID(), activeEvents),
                    initiatorPreviousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(subject.GetUniqueLoadID(), activeEvents),
                    recipientPreviousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(partner.GetUniqueLoadID(), activeEvents),
                    initiatorWeapon = DiaryContextBuilder.EquippedWeapon(subject),
                    recipientWeapon = DiaryContextBuilder.EquippedWeapon(partner),
                    initiatorStatus = DiaryEvent.NotGeneratedStatus,
                    recipientStatus = DiaryEvent.NotGeneratedStatus,
                    neutralStatus = DiaryEvent.NotGeneratedStatus
                };
            }

            return new DiaryEvent
            {
                eventId = eventId,
                tick = Find.TickManager.TicksGame,
                date = date,
                interactionDefName = payload.EventKey,
                interactionLabel = label,
                initiatorPawnId = subject.GetUniqueLoadID(),
                recipientPawnId = string.Empty,
                initiatorName = subject.LabelShortCap,
                recipientName = string.Empty,
                initiatorText = text,
                recipientText = string.Empty,
                neutralText = text,
                gameContext = gameContext,
                instruction = instruction,
                colorCue = DiaryEvent.ResolveColorCue(payload.EventKey, gameContext),
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(subject),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(subject),
                recipientSurroundings = "n/a",
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(subject, partner, activeEvents),
                recipientContinuity = "none",
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(subject.GetUniqueLoadID(), activeEvents),
                recipientLastOpener = string.Empty,
                initiatorPreviousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(subject.GetUniqueLoadID(), activeEvents),
                recipientPreviousEntryEnding = string.Empty,
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(subject),
                recipientWeapon = string.Empty,
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };
        }

        private static string PromptInstructionFor(ExternalEventRequest request)
        {
            ExternalPromptEntryRequest promptRequest = request as ExternalPromptEntryRequest;
            return promptRequest == null ? null : promptRequest.promptInstruction;
        }

        private static string ResolvePreviewPovRole(
            string requestedPovRole,
            bool pairwise,
            bool initiatorQueueable,
            bool recipientQueueable)
        {
            if (string.IsNullOrWhiteSpace(requestedPovRole))
            {
                if (initiatorQueueable)
                {
                    return DiaryEvent.InitiatorRole;
                }

                return recipientQueueable ? DiaryEvent.RecipientRole : string.Empty;
            }

            if (DiaryEvent.RoleEquals(requestedPovRole, DiaryEvent.InitiatorRole))
            {
                return initiatorQueueable ? DiaryEvent.InitiatorRole : string.Empty;
            }

            if (DiaryEvent.RoleEquals(requestedPovRole, DiaryEvent.RecipientRole))
            {
                return pairwise && recipientQueueable ? DiaryEvent.RecipientRole : string.Empty;
            }

            return string.Empty;
        }

        private static string PreviewEventId(string eventKey, string subjectPawnId, string partnerPawnId)
        {
            return "preview|" + (eventKey ?? string.Empty) + "|" + (subjectPawnId ?? string.Empty)
                + "|" + (partnerPawnId ?? string.Empty);
        }
    }

}
