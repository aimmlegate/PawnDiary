// External integration API budget guard. This partial turns live RimWorld requests into plain
// rolling-window reservations before the shared dispatcher can queue LLM work.
// This state is intentionally transient: it stops bursts and adapter loops in the loaded game, but
// does not write rate-limit bookkeeping into the player's save.
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private readonly List<ExternalApiBudgetReservation> externalApiBudgetReservations =
            new List<ExternalApiBudgetReservation>();

        /// <summary>
        /// Resets transient integration budget state when a game starts or loads.
        /// </summary>
        private void ResetExternalApiBudgetState()
        {
            externalApiBudgetReservations.Clear();
        }

        /// <summary>
        /// Reserves budget for a normal or wrapped external event, which may enqueue main diary
        /// generation and optional title follow-ups.
        /// </summary>
        internal bool TryReserveExternalApiBudgetForEvent(ExternalEventRequest request, string operation)
        {
            return TryReserveExternalApiBudget(
                request == null ? null : request.sourceId,
                operation,
                EstimateExternalEventPromptTokens(request));
        }

        /// <summary>
        /// Reserves budget for direct prose only when it can enqueue title generation.
        /// </summary>
        internal bool TryReserveExternalApiBudgetForDirectEntry(ExternalDirectEntryRequest request)
        {
            return TryReserveExternalApiBudget(
                request == null ? null : request.sourceId,
                "SubmitDirectEntry",
                EstimateExternalDirectEntryTitleTokens(request));
        }

        private bool TryReserveExternalApiBudget(string sourceId, string operation, int estimatedTokens)
        {
            if (estimatedTokens <= 0)
            {
                return true;
            }

            ExternalApiBudgetTuning tuning = DiaryTuning.IntegrationPromptBudgetTuning;
            if (!ExternalApiBudgetHasActiveCaps(tuning))
            {
                return true;
            }

            int currentTick = CurrentGameTick();
            PruneExternalApiBudgetReservations(currentTick, tuning.windowTicks);

            ExternalApiBudgetDecision decision = ExternalApiBudgetPolicy.Evaluate(
                externalApiBudgetReservations,
                tuning,
                currentTick,
                sourceId,
                estimatedTokens);

            if (!decision.allowed)
            {
                LogExternalApiBudgetRejection(sourceId, operation, decision, tuning);
                return false;
            }

            externalApiBudgetReservations.Add(new ExternalApiBudgetReservation
            {
                tick = currentTick,
                sourceId = ExternalApiBudgetPolicy.NormalizeSourceId(sourceId),
                estimatedTokens = estimatedTokens
            });
            return true;
        }

        private static bool ExternalApiBudgetHasActiveCaps(ExternalApiBudgetTuning tuning)
        {
            return tuning != null
                && tuning.enabled
                && tuning.windowTicks > 0
                && (tuning.maxRequestsPerSource > 0
                    || tuning.maxRequestsGlobal > 0
                    || tuning.maxTokensPerSource > 0
                    || tuning.maxTokensGlobal > 0);
        }

        private void PruneExternalApiBudgetReservations(int currentTick, int windowTicks)
        {
            if (externalApiBudgetReservations.Count == 0)
            {
                return;
            }

            for (int i = externalApiBudgetReservations.Count - 1; i >= 0; i--)
            {
                if (!ExternalApiBudgetPolicy.IsInsideWindow(
                    externalApiBudgetReservations[i], currentTick, windowTicks))
                {
                    externalApiBudgetReservations.RemoveAt(i);
                }
            }
        }

        private int EstimateExternalEventPromptTokens(ExternalEventRequest request)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (!ExternalApiCanSpendTokens(settings)
                || request == null
                || request.subject == null
                || string.IsNullOrWhiteSpace(request.eventKey))
            {
                return 0;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyExternal(request.eventKey.Trim());
            if (group == null && !(request is ExternalPromptEntryRequest))
            {
                return 0;
            }

            if (group != null && !settings.IsGroupEnabled(group.defName))
            {
                return 0;
            }

            if (!ExternalApiPawnMaySpendTokens(request.subject))
            {
                return 0;
            }

            int povCount = 1;
            if (HasDistinctPartner(request.subject, request.partner)
                && ExternalApiPawnMaySpendTokens(request.partner))
            {
                povCount++;
            }

            int tokensPerPov = Math.Max(1, settings.maxTokens);
            if (settings.generateTitles)
            {
                tokensPerPov += TitleMaxTokens;
            }

            return povCount * tokensPerPov;
        }

        private int EstimateExternalDirectEntryTitleTokens(ExternalDirectEntryRequest request)
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (!ExternalApiCanSpendTokens(settings)
                || request == null
                || !request.generateTitleIfMissing
                || !settings.generateTitles
                || request.subject == null
                || string.IsNullOrWhiteSpace(request.eventKey))
            {
                return 0;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyExternal(request.eventKey.Trim());
            if (group != null && !settings.IsGroupEnabled(group.defName))
            {
                return 0;
            }

            int titleRequests = 0;
            if (DirectEntryWouldQueueTitle(
                request.subject,
                request.text,
                request.title))
            {
                titleRequests++;
            }

            if (HasDistinctPartner(request.subject, request.partner)
                && DirectEntryWouldQueueTitle(request.partner, request.partnerText, request.partnerTitle))
            {
                titleRequests++;
            }

            return titleRequests * TitleMaxTokens;
        }

        private bool DirectEntryWouldQueueTitle(Pawn pawn, string text, string title)
        {
            return pawn != null
                && CanWriteExternalDirectEntryFor(pawn)
                && string.IsNullOrWhiteSpace(
                    ExternalDirectEntryText.CleanTitle(title, DiaryTuning.IntegrationDirectTitleMaxChars))
                && !string.IsNullOrWhiteSpace(
                    ExternalDirectEntryText.CleanProse(text, DiaryTuning.IntegrationDirectTextMaxChars));
        }

        private bool ExternalApiCanSpendTokens(PawnDiarySettings settings)
        {
            if (settings == null || PromptTestModeEnabled())
            {
                return false;
            }

            List<ApiEndpointConfig> targets = settings.ActiveEndpoints();
            return targets != null && targets.Count > 0;
        }

        private bool ExternalApiPawnMaySpendTokens(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn) || initialArrivalScanPending)
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary != null && !diary.diaryGenerationEnabled)
            {
                return false;
            }

            return !ShouldSkipFirstPersonGenerationForIncapacitation(pawn);
        }

        private static bool HasDistinctPartner(Pawn subject, Pawn partner)
        {
            if (subject == null || partner == null)
            {
                return false;
            }

            return !string.Equals(subject.GetUniqueLoadID(), partner.GetUniqueLoadID(), StringComparison.Ordinal);
        }

        private static int CurrentGameTick()
        {
            return Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
        }

        private static void LogExternalApiBudgetRejection(
            string sourceId,
            string operation,
            ExternalApiBudgetDecision decision,
            ExternalApiBudgetTuning tuning)
        {
            string normalizedSource = ExternalApiBudgetPolicy.NormalizeSourceId(sourceId);
            string reason = string.IsNullOrWhiteSpace(decision.blockReason)
                ? "unknown"
                : decision.blockReason;
            Log.WarningOnce(
                "[Pawn Diary] Integration API: " + operation + " from '" + normalizedSource
                + "' was ignored because the external API prompt budget rejected it (reason="
                + reason + ", requestedTokens=" + decision.requestedTokens
                + ", sourceRequests=" + decision.sourceRequests
                + ", globalRequests=" + decision.globalRequests
                + ", sourceTokens=" + decision.sourceTokens
                + ", globalTokens=" + decision.globalTokens
                + ", windowTicks=" + (tuning == null ? 0 : tuning.windowTicks) + ").",
                ("PawnDiary.Api.Budget." + operation + "." + normalizedSource + "." + reason).GetHashCode());
        }
    }
}
