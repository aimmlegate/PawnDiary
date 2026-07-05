// Pure rolling-window budget policy for public integration API requests that can spend LLM tokens.
// Runtime code turns live requests into plain "source + estimated tokens" reservations, then this
// evaluator decides whether the next reservation fits the configured XML caps.
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// XML/default guardrail knobs copied into the pure external API budget evaluator.
    /// Zero disables that specific cap; <see cref="enabled"/> disables the whole budget gate.
    /// Internal implementation detail; the public integration API never exposes the budget types,
    /// and <c>DiaryPipelineTests</c> reaches in via <c>[InternalsVisibleTo]</c>.
    /// </summary>
    internal class ExternalApiBudgetTuning
    {
        public bool enabled = true;
        public int windowTicks = 2500;
        public int maxRequestsPerSource = 10;
        public int maxRequestsGlobal = 30;
        public int maxTokensPerSource = 20000;
        public int maxTokensGlobal = 60000;
    }

    /// <summary>
    /// One accepted integration request reservation inside the transient rolling window. Internal:
    /// threaded through the budget component and the API submit paths, never returned to adapters.
    /// </summary>
    internal class ExternalApiBudgetReservation
    {
        public int tick;
        public string sourceId;
        public int estimatedTokens;
    }

    /// <summary>
    /// Budget decision plus counters useful for API warnings and tests. Internal: tests are the only
    /// external consumer, via <c>[InternalsVisibleTo]</c>.
    /// </summary>
    internal class ExternalApiBudgetDecision
    {
        public bool allowed;
        public string blockReason = string.Empty;
        public int sourceRequests;
        public int globalRequests;
        public int sourceTokens;
        public int globalTokens;
        public int requestedTokens;
    }

    /// <summary>
    /// Decides whether adding one estimated token-spending integration request would exceed the
    /// configured rolling request or token caps. Internal pure evaluator; the public integration API
    /// surface is only the submit/handle/status members on <c>PawnDiaryApi</c>, and
    /// <c>DiaryPipelineTests</c> reaches in via <c>[InternalsVisibleTo]</c>.
    /// </summary>
    internal static class ExternalApiBudgetPolicy
    {
        public const string SourceRequestCapReason = "source_request_cap";
        public const string GlobalRequestCapReason = "global_request_cap";
        public const string SourceTokenCapReason = "source_token_cap";
        public const string GlobalTokenCapReason = "global_token_cap";

        public static ExternalApiBudgetDecision Evaluate(
            IEnumerable<ExternalApiBudgetReservation> recentReservations,
            ExternalApiBudgetTuning tuning,
            int currentTick,
            string sourceId,
            int estimatedTokens)
        {
            tuning = tuning ?? new ExternalApiBudgetTuning();
            string normalizedSource = NormalizeSourceId(sourceId);
            int requestedTokens = Math.Max(0, estimatedTokens);
            ExternalApiBudgetDecision decision = new ExternalApiBudgetDecision
            {
                allowed = true,
                requestedTokens = requestedTokens
            };

            if (!tuning.enabled || tuning.windowTicks <= 0 || requestedTokens <= 0)
            {
                return decision;
            }

            if (recentReservations != null)
            {
                foreach (ExternalApiBudgetReservation reservation in recentReservations)
                {
                    if (!IsInsideWindow(reservation, currentTick, tuning.windowTicks))
                    {
                        continue;
                    }

                    int tokens = Math.Max(0, reservation.estimatedTokens);
                    decision.globalRequests++;
                    decision.globalTokens += tokens;

                    if (string.Equals(NormalizeSourceId(reservation.sourceId), normalizedSource,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        decision.sourceRequests++;
                        decision.sourceTokens += tokens;
                    }
                }
            }

            if (ExceedsCap(decision.sourceRequests + 1, tuning.maxRequestsPerSource))
            {
                decision.allowed = false;
                decision.blockReason = SourceRequestCapReason;
                return decision;
            }

            if (ExceedsCap(decision.globalRequests + 1, tuning.maxRequestsGlobal))
            {
                decision.allowed = false;
                decision.blockReason = GlobalRequestCapReason;
                return decision;
            }

            if (ExceedsCap(decision.sourceTokens + requestedTokens, tuning.maxTokensPerSource))
            {
                decision.allowed = false;
                decision.blockReason = SourceTokenCapReason;
                return decision;
            }

            if (ExceedsCap(decision.globalTokens + requestedTokens, tuning.maxTokensGlobal))
            {
                decision.allowed = false;
                decision.blockReason = GlobalTokenCapReason;
                return decision;
            }

            return decision;
        }

        public static bool IsInsideWindow(ExternalApiBudgetReservation reservation, int currentTick, int windowTicks)
        {
            if (reservation == null || windowTicks <= 0)
            {
                return false;
            }

            int age = currentTick - reservation.tick;
            return age >= 0 && age < windowTicks;
        }

        public static string NormalizeSourceId(string sourceId)
        {
            return string.IsNullOrWhiteSpace(sourceId) ? "unknown-source" : sourceId.Trim();
        }

        private static bool ExceedsCap(int value, int cap)
        {
            return cap > 0 && value > cap;
        }
    }
}
