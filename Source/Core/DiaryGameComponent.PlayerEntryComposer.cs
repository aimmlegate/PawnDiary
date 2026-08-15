// Component-owned transient generation facade for the normal-play entry composer. Context mode
// builds a detached current-pawn event and runs the ordinary prompt planner; FullPrompt mode sends
// only the player's sanitized system/user prompts. Both use the existing one-shot completion core,
// and neither creates a diary page, unread badge, knowledge record, voice assignment, or saved draft.
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const string PlayerComposerSourceId = "PawnDiary.PlayerComposer";

        // Hermetic RimTest seams. Production defaults always point at the existing one-shot core;
        // tests may replace them to capture exact prompt envelopes without sockets or paid work.
        internal static Func<ExternalLlmCompletionRequest, PawnDiarySettings, int>
            BeginDraftCompletion = ExternalLlmCompletionService.BeginTrusted;
        internal static Func<PawnDiarySettings, int, string, ExternalLlmEndpointSelection>
            ResolveDraftEndpoint = ExternalLlmCompletionService.ResolveEndpointSelectionSnapshot;
        internal static Func<int, LlmCompletionResult> PollDraftCompletion =
            ExternalLlmCompletionService.Poll;
        internal static Func<int, bool> CancelDraftCompletion =
            ExternalLlmCompletionService.Cancel;

        /// <summary>Restores production completion delegates after one hermetic fixture.</summary>
        internal static void ResetPlayerEntryDraftTestSeams()
        {
            BeginDraftCompletion = ExternalLlmCompletionService.BeginTrusted;
            ResolveDraftEndpoint = ExternalLlmCompletionService.ResolveEndpointSelectionSnapshot;
            PollDraftCompletion = ExternalLlmCompletionService.Poll;
            CancelDraftCompletion = ExternalLlmCompletionService.Cancel;
        }

        /// <summary>
        /// Starts a transient Context or FullPrompt draft. A zero/false result is a local rejection;
        /// accepted handles live only for this game session and are consumed by a terminal Poll.
        /// </summary>
        internal PlayerEntryDraftStartResult StartPlayerEntryDraft(
            Pawn pawn,
            PlayerEntryComposerRequest request)
        {
            PlayerEntryDraftStartResult rejected = new PlayerEntryDraftStartResult();
            if (pawn == null || pawn.Dead || !IsDiaryEligible(pawn))
            {
                rejected.errorCode = "pawn_unavailable";
                return rejected;
            }

            List<PlayerEntryTypeSnapshot> entryTypes = DiaryPlayerEntryTypes.ForUi();
            List<PlayerEntryTemplateSnapshot> templates = DiaryPlayerPromptTemplates.ForUi();
            PlayerEntryComposerPlan input = PlayerEntryComposerPolicy.Plan(
                request, entryTypes, templates);
            if (!input.valid)
            {
                rejected.errorCode = input.errorCode;
                return rejected;
            }

            if (input.mode != PlayerEntryComposerMode.Context
                && input.mode != PlayerEntryComposerMode.FullPrompt)
            {
                rejected.errorCode = "mode_not_generating";
                return rejected;
            }

            string systemPrompt;
            string userPrompt;
            int completionMaxTokens;
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            ExternalLlmEndpointSelection endpoint = ResolveDraftEndpoint(
                settings, input.laneIndex, string.Empty);
            if (endpoint?.endpoint == null)
            {
                rejected.errorCode = "completion_rejected";
                return rejected;
            }
            if (input.mode == PlayerEntryComposerMode.FullPrompt)
            {
                // Pure policy already applied the documented control-character removal and safe cap.
                // Do not trim, wrap, add Pawn Diary context, or otherwise reinterpret either string.
                systemPrompt = input.systemPrompt;
                userPrompt = input.userPrompt;
                completionMaxTokens = PlayerEntryComposerPolicy.ResolveCompletionMaxTokens(
                    input.maxTokens, settings.maxTokens);
            }
            else
            {
                PromptContextDetailLevel detailLevel = settings.EffectiveContextDetailLevel(
                    endpoint.endpoint);
                DiaryPromptPlan prompt = BuildPlayerEntryContextDraftPrompt(
                    pawn,
                    input,
                    detailLevel);
                if (prompt == null || string.IsNullOrWhiteSpace(prompt.userPrompt))
                {
                    rejected.errorCode = "prompt_plan_failed";
                    return rejected;
                }

                // Event prompt policy may pin generation to a configured model. Resolve it after the
                // routing plan exists, then rebuild only when that lane changes context-detail policy.
                endpoint = ResolveDraftEndpoint(
                    settings, input.laneIndex, prompt.forcedModelName);
                if (endpoint?.endpoint == null)
                {
                    rejected.errorCode = "completion_rejected";
                    return rejected;
                }
                PromptContextDetailLevel selectedDetailLevel = settings.EffectiveContextDetailLevel(
                    endpoint.endpoint);
                if (selectedDetailLevel != detailLevel)
                {
                    prompt = BuildPlayerEntryContextDraftPrompt(
                        pawn, input, selectedDetailLevel);
                    if (prompt == null || string.IsNullOrWhiteSpace(prompt.userPrompt))
                    {
                        rejected.errorCode = "prompt_plan_failed";
                        return rejected;
                    }
                }
                systemPrompt = prompt.systemPrompt ?? string.Empty;
                userPrompt = prompt.userPrompt;
                completionMaxTokens = PlayerEntryComposerPolicy.ResolveCompletionMaxTokens(
                    prompt.responseRules?.maxTokens ?? 0,
                    settings.maxTokens);
            }

            int handle = BeginDraftCompletion(
                new ExternalLlmCompletionRequest
                {
                    sourceId = PlayerComposerSourceId,
                    laneIndex = endpoint.laneIndex,
                    systemPrompt = systemPrompt,
                    userText = userPrompt,
                    maxTokens = completionMaxTokens
                },
                settings);
            if (handle <= 0)
            {
                rejected.errorCode = "completion_rejected";
                return rejected;
            }

            return new PlayerEntryDraftStartResult
            {
                accepted = true,
                handle = handle,
                entryTypeKey = input.entryTypeKey,
                templateKey = input.templateKey
            };
        }

        /// <summary>
        /// Polls and consumes a terminal transient draft. Success returns body text only; the review UI
        /// owns any title. Unknown includes canceled, already-consumed, and cross-session handles.
        /// </summary>
        internal PlayerEntryDraftPollResult PollPlayerEntryDraft(int handle)
        {
            LlmCompletionResult result = PollDraftCompletion(handle);
            if (result == null) return new PlayerEntryDraftPollResult();
            return new PlayerEntryDraftPollResult
            {
                status = MapDraftStatus(result.status),
                text = result.status == LlmCompletionStatus.Succeeded
                    ? ExternalDirectEntryText.CleanProse(
                        result.text, ManualEntryBodyMaxCharacters)
                    : string.Empty,
                error = result.status == LlmCompletionStatus.Failed
                    ? result.error ?? string.Empty
                    : string.Empty
            };
        }

        /// <summary>Cancels and forgets an obsolete transient handle, including on dialog close.</summary>
        internal bool CancelPlayerEntryDraft(int handle)
        {
            return CancelDraftCompletion(handle);
        }

        private DiaryPromptPlan BuildPlayerEntryContextDraftPrompt(
            Pawn pawn,
            PlayerEntryComposerPlan input,
            PromptContextDetailLevel detailLevel)
        {
            PlayerEntryTypeSnapshot entryType;
            if (!DiaryPlayerEntryTypes.TryResolve(input.entryTypeKey, out entryType)) return null;

            DiaryEvent diaryEvent = BuildDetachedPlayerEntryContextEvent(pawn, input, entryType);
            if (diaryEvent == null) return null;

            detailLevel = PawnDiarySettings.NormalizeContextDetailLevel(detailLevel);
            string role = DiaryEvent.InitiatorRole;
            string personaRule = PersonaRuleFor(
                diaryEvent, role, null, ensureVoiceStage: false);
            string psychotypeRule = PsychotypeRuleFor(
                diaryEvent,
                role,
                null,
                ensureVoiceStage: false,
                contextDetailLevel: detailLevel);
            string promptEnchantment = PromptEnchantmentRuleFor(
                diaryEvent,
                role,
                null,
                detailLevel,
                readOnlyPreview: true);

            DiaryPromptRequest promptRequest = DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                role,
                personaRule,
                psychotypeRule,
                promptEnchantment,
                // Humor is deliberately absent: even a deterministic implementation is not context
                // promised by this editor, and previewing must never advance or stamp random state.
                string.Empty,
                null,
                null,
                false,
                input.maxTokens,
                detailLevel,
                requestedTemplateKey: input.templateKey,
                readOnlyKnowledge: true);
            return DiaryPromptPlanner.Build(promptRequest);
        }

        private DiaryEvent BuildDetachedPlayerEntryContextEvent(
            Pawn pawn,
            PlayerEntryComposerPlan input,
            PlayerEntryTypeSnapshot entryType)
        {
            if (pawn == null || input == null || entryType == null) return null;
            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            string pawnId = pawn.GetUniqueLoadID();
            int tick = Find.TickManager.TicksGame;
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = "player-composer-" + Guid.NewGuid().ToString("N"),
                tick = tick,
                date = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero),
                interactionDefName = ManualEntryDefName,
                interactionLabel = entryType.label,
                initiatorPawnId = pawnId,
                recipientPawnId = string.Empty,
                initiatorName = pawn.LabelShortCap,
                recipientName = string.Empty,
                initiatorText = input.factualSummary,
                recipientText = string.Empty,
                neutralText = input.factualSummary,
                gameContext = ManualEntryGameContext,
                instruction = input.customInstruction,
                colorCue = entryType.colorCue,
                initiatorPawnSummary = DiaryContextBuilder.BuildPawnSummary(pawn),
                recipientPawnSummary = "n/a",
                initiatorSurroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn),
                recipientSurroundings = "n/a",
                initiatorContinuity = DiaryContextBuilder.BuildContinuitySummary(
                    pawn, null, activeEvents),
                recipientContinuity = "none",
                initiatorLastOpener = DiaryContextBuilder.LatestDiaryOpener(pawnId, activeEvents),
                recipientLastOpener = string.Empty,
                initiatorPreviousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(
                    pawnId, activeEvents),
                recipientPreviousEntryEnding = string.Empty,
                initiatorWeapon = DiaryContextBuilder.EquippedWeapon(pawn),
                recipientWeapon = string.Empty,
                solo = true,
                initiatorStatus = DiaryEvent.NotGeneratedStatus,
                recipientStatus = DiaryEvent.NotGeneratedStatus,
                neutralStatus = DiaryEvent.NotGeneratedStatus
            };
            diaryEvent.TrySetEntryTypeKey(
                DiaryEvent.InitiatorRole, entryType.entryTypeKey, bumpVersion: false);
            diaryEvent.SetIdentitySummary(
                DiaryEvent.InitiatorRole, DiaryContextBuilder.BuildIdentitySummary(pawn, null));

            // Same retrieval layer as a newly registered event, but without creating old-save state or
            // replacing the dev tab's last-selection report. The detached result dies with the dialog.
            ApplyRelevantPastForEvent(
                diaryEvent,
                recordDiagnostics: false,
                createMissingKnowledgeState: false,
                requestedTemplateKey: input.templateKey,
                povRole: DiaryEvent.InitiatorRole);
            return diaryEvent;
        }

        private static PlayerEntryDraftStatus MapDraftStatus(LlmCompletionStatus status)
        {
            switch (status)
            {
                case LlmCompletionStatus.Pending:
                    return PlayerEntryDraftStatus.Pending;
                case LlmCompletionStatus.Succeeded:
                    return PlayerEntryDraftStatus.Succeeded;
                case LlmCompletionStatus.Failed:
                    return PlayerEntryDraftStatus.Failed;
                default:
                    return PlayerEntryDraftStatus.Unknown;
            }
        }
    }
}
