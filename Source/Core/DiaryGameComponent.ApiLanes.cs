// API lane selection for LLM generation: given the active endpoints, pick the primary lane for a
// request (spreading new events across lanes, pinning the recipient of a paired event to the
// initiator's lane so a sequential pair shares one model), build the ordered failover list, and
// emit the one-line English debug diagnostics (never API keys). These helpers are shared by the
// queue choke point (DiaryGameComponent.GenerationDispatch.cs) and the title follow-up.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Chooses the primary API lane for a request from the given active lanes (non-empty).
        /// The recipient of a paired event reuses the lane the initiator used — matched
        /// by the endpoint+model recorded on the event — so the sequential pair shares one model.
        /// Everything else uses the saved routing mode to spread or prioritize load across APIs.
        /// </summary>
        private static ApiEndpointConfig SelectApiTarget(DiaryEvent diaryEvent, string povRole, List<ApiEndpointConfig> targets, ApiEndpointConfig primaryOverride, ApiLaneRoutingMode routingMode, out string reason)
        {
            ApiEndpointConfig overrideTarget = FindMatchingActiveLane(targets, primaryOverride);
            if (overrideTarget != null)
            {
                if (CanUsePinnedLane(targets, overrideTarget))
                {
                    reason = "pinned to successful prior lane";
                    return overrideTarget;
                }

                LogApiDebug("Pinned lane is cooling; using routing mode instead lane=" + LaneLabel(overrideTarget));
            }

            // Sequential pinning: keep the recipient on the initiator's lane when paired mode ran
            // the initiator first and recorded which endpoint+model it used (after failover, the
            // recorded meta is the lane that actually produced the initiator's entry).
            if (DiaryEvent.RoleEquals(povRole, DiaryEvent.RecipientRole))
            {
                string initiatorEndpoint = diaryEvent.initiatorLlmEndpoint;
                string initiatorModel = diaryEvent.initiatorLlmModel;
                if (!string.IsNullOrWhiteSpace(initiatorEndpoint) && !string.IsNullOrWhiteSpace(initiatorModel))
                {
                    foreach (ApiEndpointConfig candidate in targets)
                    {
                        if (SameGenerationLane(candidate, initiatorEndpoint, initiatorModel))
                        {
                            if (CanUsePinnedLane(targets, candidate))
                            {
                                reason = "recipient pinned to initiator lane";
                                return candidate;
                            }

                            LogApiDebug("Recipient pin lane is cooling; using routing mode instead event=" + diaryEvent.eventId + " lane=" + LaneLabel(candidate));
                            break;
                        }
                    }

                    LogApiDebug(
                        "Recipient pin missed for event=" + diaryEvent.eventId
                        + " initiatorLane=" + initiatorModel + " @ " + initiatorEndpoint
                        + "; falling back to round-robin");
                }
            }

            int counter = LlmClient.NextRoundRobinIndex();
            int index = ApiLaneSelector.SelectPrimaryIndex(targets.Count, routingMode, counter, LaneReadiness(targets));
            reason = "routing " + ApiLaneSelector.Normalize(routingMode) + " selected index " + index + " of " + targets.Count;
            return targets[index];
        }

        private static bool CanUsePinnedLane(List<ApiEndpointConfig> targets, ApiEndpointConfig lane)
        {
            return lane != null && (!LlmClient.IsLaneCooling(lane) || !HasReadyLane(targets));
        }

        private static bool HasReadyLane(List<ApiEndpointConfig> targets)
        {
            if (targets == null)
            {
                return false;
            }

            foreach (ApiEndpointConfig target in targets)
            {
                if (target != null && !LlmClient.IsLaneCooling(target))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<bool> LaneReadiness(List<ApiEndpointConfig> targets)
        {
            List<bool> ready = new List<bool>();
            if (targets == null)
            {
                return ready;
            }

            foreach (ApiEndpointConfig target in targets)
            {
                ready.Add(target != null && !LlmClient.IsLaneCooling(target));
            }

            return ready;
        }

        private static ApiEndpointConfig FindMatchingActiveLane(List<ApiEndpointConfig> targets, ApiEndpointConfig requested)
        {
            if (targets == null || requested == null)
            {
                return null;
            }

            foreach (ApiEndpointConfig candidate in targets)
            {
                if (SameGenerationLane(candidate, requested, true))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool SameGenerationLane(ApiEndpointConfig left, ApiEndpointConfig right, bool includeApiKey)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (includeApiKey)
            {
                return ApiLaneIdentity.ForGenerationWithAuth(left.url, left.model, left.apiMode, left.authMode, left.customAuthHeaderName, left.apiKey)
                    == ApiLaneIdentity.ForGenerationWithAuth(right.url, right.model, right.apiMode, right.authMode, right.customAuthHeaderName, right.apiKey);
            }

            return ApiLaneIdentity.ForGeneration(left.url, left.model, left.apiMode)
                == ApiLaneIdentity.ForGeneration(right.url, right.model, right.apiMode);
        }

        private static bool SameGenerationLane(ApiEndpointConfig lane, string endpointUrl, string modelName)
        {
            return lane != null
                && ApiLaneIdentity.ForGeneration(lane.url, lane.model, lane.apiMode)
                == ApiLaneIdentity.ForGeneration(endpointUrl, modelName, lane.apiMode);
        }

        /// <summary>
        /// Returns the lanes to fall back to (every active lane except the chosen primary), in order,
        /// so a request that errors on its primary lane can retry on the others.
        /// </summary>
        private static List<ApiEndpointConfig> BuildFailoverTargets(List<ApiEndpointConfig> targets, ApiEndpointConfig primary)
        {
            List<ApiEndpointConfig> failovers = new List<ApiEndpointConfig>();
            foreach (ApiEndpointConfig candidate in targets)
            {
                if (!ReferenceEquals(candidate, primary))
                {
                    failovers.Add(candidate);
                }
            }

            return failovers;
        }

        /// <summary>
        /// Writes one-line API lane diagnostics to the RimWorld log. These are intentionally
        /// English debug logs, not player-facing UI strings; never include API keys here.
        /// </summary>
        private static void LogApiLaneConfiguration(PawnDiarySettings settings, List<ApiEndpointConfig> active)
        {
            int configuredCount = settings?.apiEndpoints?.Count ?? 0;
            int activeCount = active?.Count ?? 0;
            LogApiDebug(
                "Configured API lanes=" + configuredCount
                + ", active lanes=" + activeCount
                + (configuredCount == activeCount ? string.Empty : " (disabled rows or rows with blank url/model are skipped)")
                + "; active=[" + LaneList(active) + "]");
        }

        private static void LogApiDebug(string message)
        {
            if (!LlmClient.DebugLoggingEnabled())
            {
                return;
            }

            Log.Message("[PawnDiary debug] " + message);
        }

        private static string LaneList(List<ApiEndpointConfig> lanes)
        {
            if (lanes == null || lanes.Count == 0)
            {
                return "none";
            }

            List<string> labels = new List<string>();
            foreach (ApiEndpointConfig lane in lanes)
            {
                labels.Add(LaneLabel(lane));
            }

            return string.Join(" | ", labels.ToArray());
        }

        private static string LaneLabel(ApiEndpointConfig lane)
        {
            if (lane == null)
            {
                return "<null>";
            }

            return ApiLaneLabels.Label(lane.url, lane.model, lane.apiMode);
        }
    }
}
