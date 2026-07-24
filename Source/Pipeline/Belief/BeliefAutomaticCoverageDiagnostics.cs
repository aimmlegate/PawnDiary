// Pure, dev-safe Ideology automatic-coverage diagnostics. The resolver supplies only fixed mechanical
// tokens and numbers; this file classifies and aggregates them without retaining candidate identities,
// player-authored doctrine text, ideology names, event prose, or prompt content.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>One fixed-token count used by the bounded aggregate.</summary>
    internal sealed class BeliefAutomaticCoverageCount
    {
        public string token = string.Empty;
        public int count;
    }

    /// <summary>
    /// Fixed-memory session aggregate. Counts stop at the XML-owned sample limit; no per-event rows or
    /// arbitrary keys are retained, so long dev sessions cannot grow memory with colony or mod size.
    /// </summary>
    internal sealed class BeliefAutomaticCoverageAggregate
    {
        public int observedCount;
        public int droppedCount;
        public readonly List<BeliefAutomaticCoverageCount> outcomes =
            new List<BeliefAutomaticCoverageCount>();
        public readonly List<BeliefAutomaticCoverageCount> winnerTiers =
            new List<BeliefAutomaticCoverageCount>();
        public readonly List<BeliefAutomaticCoverageCount> confidenceBands =
            new List<BeliefAutomaticCoverageCount>();
        public readonly List<BeliefAutomaticCoverageCount> rejectionReasons =
            new List<BeliefAutomaticCoverageCount>();
        public float runnerUpGapTotal;
        public float minimumRunnerUpGap;
        public float maximumRunnerUpGap;
        public int runnerUpGapCount;
        public BeliefAutomaticCoverageDiagnostic last =
            new BeliefAutomaticCoverageDiagnostic();

        /// <summary>Returns a fixed-token outcome count, or zero when it has not occurred.</summary>
        public int OutcomeCount(string token)
        {
            return CountFor(outcomes, token);
        }

        /// <summary>Returns a fixed-token winner-tier count, or zero when it has not occurred.</summary>
        public int WinnerTierCount(string token)
        {
            return CountFor(winnerTiers, token);
        }

        /// <summary>Returns a fixed-token confidence-band count, or zero when it has not occurred.</summary>
        public int ConfidenceBandCount(string token)
        {
            return CountFor(confidenceBands, token);
        }

        /// <summary>Returns a fixed-token rejection-reason count, or zero when it has not occurred.</summary>
        public int RejectionReasonCount(string token)
        {
            return CountFor(rejectionReasons, token);
        }

        private static int CountFor(List<BeliefAutomaticCoverageCount> rows, string token)
        {
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].token, token, StringComparison.Ordinal)) return rows[i].count;
            return 0;
        }
    }

    /// <summary>Pure classification, bounded aggregation, and token-only formatting.</summary>
    internal static class BeliefAutomaticCoverageDiagnostics
    {
        private static readonly string[] OutcomeOrder =
        {
            BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
            BeliefAutomaticCoverageOutcomeTokens.StructuralCorrelation,
            BeliefAutomaticCoverageOutcomeTokens.SemanticAlias,
            BeliefAutomaticCoverageOutcomeTokens.GuardedLexical,
            BeliefAutomaticCoverageOutcomeTokens.BelowConfidence,
            BeliefAutomaticCoverageOutcomeTokens.Ambiguous,
            BeliefAutomaticCoverageOutcomeTokens.NoMatch
        };

        private static readonly string[] WinnerTierOrder =
        {
            BeliefRelevanceTierTokens.SourcePrecept,
            BeliefRelevanceTierTokens.ExactCorrelation,
            BeliefRelevanceTierTokens.DirectIdentity,
            BeliefRelevanceTierTokens.CorrelationText,
            BeliefRelevanceTierTokens.IssueText,
            BeliefRelevanceTierTokens.GeneralText,
            BeliefRelevanceTierTokens.Association,
            BeliefRelevanceTierTokens.QuietFallback
        };

        private static readonly string[] ConfidenceBandOrder =
        {
            BeliefAutomaticCoverageConfidenceBandTokens.Structural,
            BeliefAutomaticCoverageConfidenceBandTokens.BelowMinimum,
            BeliefAutomaticCoverageConfidenceBandTokens.Qualified,
            BeliefAutomaticCoverageConfidenceBandTokens.Strong,
            BeliefAutomaticCoverageConfidenceBandTokens.None
        };

        private static readonly string[] RejectionReasonOrder =
        {
            BeliefAutomaticCoverageReasonTokens.InvalidInput,
            BeliefAutomaticCoverageReasonTokens.UnavailableSnapshot,
            BeliefAutomaticCoverageReasonTokens.UnverifiedEvidence,
            BeliefAutomaticCoverageReasonTokens.MismatchedPov,
            BeliefAutomaticCoverageReasonTokens.FutureEvidence,
            BeliefAutomaticCoverageReasonTokens.NoEvidence,
            BeliefAutomaticCoverageReasonTokens.BelowConfidence,
            BeliefAutomaticCoverageReasonTokens.RunnerUpAmbiguity,
            BeliefAutomaticCoverageReasonTokens.NoCandidate
        };

        /// <summary>Creates one accepted structural or exact diagnostic.</summary>
        public static BeliefAutomaticCoverageDiagnostic Accepted(
            string outcome,
            string winnerSource,
            string winnerTier,
            int candidateCount)
        {
            return new BeliefAutomaticCoverageDiagnostic
            {
                outcome = BeliefAutomaticCoverageOutcomeTokens.IsKnown(outcome)
                    ? outcome
                    : BeliefAutomaticCoverageOutcomeTokens.NoMatch,
                reason = BeliefAutomaticCoverageReasonTokens.None,
                winnerSource = SafeSource(winnerSource),
                winnerTier = SafeTier(winnerTier),
                confidenceBand = BeliefAutomaticCoverageConfidenceBandTokens.Structural,
                candidateCount = BoundCandidateCount(candidateCount)
            };
        }

        /// <summary>
        /// Creates one lexical/semantic acceptance or rejection from the matcher result without copying
        /// its matched identity. Thresholds and margins come only from the immutable XML policy snapshot.
        /// </summary>
        public static BeliefAutomaticCoverageDiagnostic FromLexical(
            BeliefLexicalMatchResult lexical,
            BeliefPolicySnapshot policy)
        {
            BeliefPolicySnapshot effective = policy ?? BeliefPolicySnapshot.CreateDefault();
            BeliefLexicalMatch top = lexical?.topCandidate;
            if (lexical == null || top == null)
                return RejectedNoMatch(BeliefAutomaticCoverageReasonTokens.NoCandidate);

            string outcome = lexical.rejectedBelowConfidence
                ? BeliefAutomaticCoverageOutcomeTokens.BelowConfidence
                : lexical.rejectedAsAmbiguous
                    ? BeliefAutomaticCoverageOutcomeTokens.Ambiguous
                    : string.Equals(top.matchedEventFieldKind, "semantic_alias", StringComparison.Ordinal)
                        ? BeliefAutomaticCoverageOutcomeTokens.SemanticAlias
                        : BeliefAutomaticCoverageOutcomeTokens.GuardedLexical;
            return new BeliefAutomaticCoverageDiagnostic
            {
                outcome = outcome,
                reason = lexical.rejectedBelowConfidence
                    ? BeliefAutomaticCoverageReasonTokens.BelowConfidence
                    : lexical.rejectedAsAmbiguous
                        ? BeliefAutomaticCoverageReasonTokens.RunnerUpAmbiguity
                        : BeliefAutomaticCoverageReasonTokens.None,
                winnerSource = SafeSource(top.relevanceSource),
                winnerTier = SafeTier(top.relevanceTier),
                confidenceBand = ConfidenceBand(
                    top.confidence,
                    effective.minimumLexicalConfidence,
                    lexical.requiredRunnerUpMargin),
                confidence = SafeScore(top.confidence),
                runnerUpGap = SafeScore(lexical.runnerUpGap),
                hasRunnerUpGap = lexical.candidateCount > 1,
                candidateCount = BoundCandidateCount(lexical.candidateCount)
            };
        }

        /// <summary>Creates the combined no-candidate/no-evidence rejection with a fixed detail token.</summary>
        public static BeliefAutomaticCoverageDiagnostic RejectedNoMatch(string reason)
        {
            return new BeliefAutomaticCoverageDiagnostic
            {
                outcome = BeliefAutomaticCoverageOutcomeTokens.NoMatch,
                reason = BeliefAutomaticCoverageReasonTokens.IsKnown(reason)
                    ? reason
                    : BeliefAutomaticCoverageReasonTokens.InvalidInput
            };
        }

        /// <summary>Adds one diagnostic to a fixed-memory aggregate, respecting the XML sample limit.</summary>
        public static void Add(
            BeliefAutomaticCoverageAggregate aggregate,
            BeliefAutomaticCoverageDiagnostic diagnostic,
            int maximumSamples)
        {
            if (aggregate == null || diagnostic == null) return;
            int cap = Math.Max(1, Math.Min(100000, maximumSamples));
            if (aggregate.observedCount >= cap)
            {
                if (aggregate.droppedCount < cap) aggregate.droppedCount++;
                return;
            }

            BeliefAutomaticCoverageDiagnostic safe = CopySafe(diagnostic);
            aggregate.observedCount++;
            IncrementKnown(aggregate.outcomes, OutcomeOrder, safe.outcome);
            IncrementKnown(aggregate.winnerTiers, WinnerTierOrder, safe.winnerTier);
            IncrementKnown(aggregate.confidenceBands, ConfidenceBandOrder, safe.confidenceBand);
            IncrementKnown(aggregate.rejectionReasons, RejectionReasonOrder, safe.reason);
            if (safe.hasRunnerUpGap)
            {
                aggregate.runnerUpGapTotal += safe.runnerUpGap;
                if (aggregate.runnerUpGapCount == 0 || safe.runnerUpGap < aggregate.minimumRunnerUpGap)
                    aggregate.minimumRunnerUpGap = safe.runnerUpGap;
                if (aggregate.runnerUpGapCount == 0 || safe.runnerUpGap > aggregate.maximumRunnerUpGap)
                    aggregate.maximumRunnerUpGap = safe.runnerUpGap;
                aggregate.runnerUpGapCount++;
            }
            aggregate.last = safe;
        }

        /// <summary>Formats only fixed mechanical tokens and invariant numbers for the developer log.</summary>
        public static string Format(BeliefAutomaticCoverageAggregate aggregate)
        {
            BeliefAutomaticCoverageAggregate value = aggregate ?? new BeliefAutomaticCoverageAggregate();
            StringBuilder text = new StringBuilder();
            text.Append("observed=").Append(value.observedCount);
            text.Append("; dropped=").Append(value.droppedCount);
            AppendCounts(text, "outcomes", value.outcomes, OutcomeOrder);
            AppendCounts(text, "winner_tiers", value.winnerTiers, WinnerTierOrder);
            AppendCounts(text, "confidence_bands", value.confidenceBands, ConfidenceBandOrder);
            AppendCounts(text, "rejection_reasons", value.rejectionReasons, RejectionReasonOrder);
            float average = value.runnerUpGapCount == 0
                ? 0f
                : value.runnerUpGapTotal / value.runnerUpGapCount;
            text.Append("; runner_gap_count=").Append(value.runnerUpGapCount);
            text.Append("; runner_gap_min=").Append(Number(value.minimumRunnerUpGap));
            text.Append("; runner_gap_avg=").Append(Number(average));
            text.Append("; runner_gap_max=").Append(Number(value.maximumRunnerUpGap));
            BeliefAutomaticCoverageDiagnostic last = value.last ?? new BeliefAutomaticCoverageDiagnostic();
            text.Append("; last_outcome=").Append(last.outcome);
            text.Append("; last_reason=").Append(last.reason);
            text.Append("; last_source=").Append(last.winnerSource);
            text.Append("; last_tier=").Append(last.winnerTier);
            text.Append("; last_confidence_band=").Append(last.confidenceBand);
            text.Append("; last_confidence=").Append(Number(last.confidence));
            text.Append("; last_runner_gap=").Append(Number(last.runnerUpGap));
            text.Append("; last_candidates=").Append(last.candidateCount);
            return text.ToString();
        }

        private static BeliefAutomaticCoverageDiagnostic CopySafe(
            BeliefAutomaticCoverageDiagnostic source)
        {
            return new BeliefAutomaticCoverageDiagnostic
            {
                outcome = BeliefAutomaticCoverageOutcomeTokens.IsKnown(source.outcome)
                    ? source.outcome
                    : BeliefAutomaticCoverageOutcomeTokens.NoMatch,
                reason = BeliefAutomaticCoverageReasonTokens.IsKnown(source.reason)
                    ? source.reason
                    : BeliefAutomaticCoverageReasonTokens.InvalidInput,
                winnerSource = SafeSource(source.winnerSource),
                winnerTier = SafeTier(source.winnerTier),
                confidenceBand = BeliefAutomaticCoverageConfidenceBandTokens.IsKnown(source.confidenceBand)
                    ? source.confidenceBand
                    : BeliefAutomaticCoverageConfidenceBandTokens.None,
                confidence = SafeScore(source.confidence),
                runnerUpGap = SafeScore(source.runnerUpGap),
                hasRunnerUpGap = source.hasRunnerUpGap,
                candidateCount = BoundCandidateCount(source.candidateCount)
            };
        }

        private static string ConfidenceBand(float confidence, float minimum, float margin)
        {
            float safeConfidence = SafeScore(confidence);
            float safeMinimum = SafeScore(minimum);
            float safeMargin = SafeScore(margin);
            if (safeConfidence < safeMinimum)
                return BeliefAutomaticCoverageConfidenceBandTokens.BelowMinimum;
            return safeConfidence >= safeMinimum + safeMargin
                ? BeliefAutomaticCoverageConfidenceBandTokens.Strong
                : BeliefAutomaticCoverageConfidenceBandTokens.Qualified;
        }

        private static void IncrementKnown(
            List<BeliefAutomaticCoverageCount> rows,
            string[] known,
            string token)
        {
            if (!Contains(known, token)) return;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.Equals(rows[i].token, token, StringComparison.Ordinal)) continue;
                if (rows[i].count < int.MaxValue) rows[i].count++;
                return;
            }
            rows.Add(new BeliefAutomaticCoverageCount { token = token, count = 1 });
        }

        private static void AppendCounts(
            StringBuilder text,
            string label,
            List<BeliefAutomaticCoverageCount> rows,
            string[] order)
        {
            text.Append("; ").Append(label).Append('=');
            for (int i = 0; i < order.Length; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(order[i]).Append(':').Append(CountFor(rows, order[i]));
            }
        }

        private static int CountFor(List<BeliefAutomaticCoverageCount> rows, string token)
        {
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].token, token, StringComparison.Ordinal)) return rows[i].count;
            return 0;
        }

        private static bool Contains(string[] values, string token)
        {
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], token, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string SafeSource(string value)
        {
            return value == BeliefRelevanceSourceTokens.SourcePrecept
                || value == BeliefRelevanceSourceTokens.ThoughtCorrelation
                || value == BeliefRelevanceSourceTokens.HistoryCorrelation
                || value == BeliefRelevanceSourceTokens.IssueIdentity
                || value == BeliefRelevanceSourceTokens.MemeAssociation
                || value == BeliefRelevanceSourceTokens.LexicalPhrase
                || value == BeliefRelevanceSourceTokens.LexicalTokens
                || value == BeliefRelevanceSourceTokens.LexicalFuzzy
                || value == BeliefRelevanceSourceTokens.Correction
                || value == BeliefRelevanceSourceTokens.QuietFallback
                    ? value
                    : string.Empty;
        }

        private static string SafeTier(string value)
        {
            return Contains(WinnerTierOrder, value) ? value : string.Empty;
        }

        private static int BoundCandidateCount(int value)
        {
            return Math.Max(0, Math.Min(512, value));
        }

        private static float SafeScore(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Math.Max(0f, value);
        }

        private static string Number(float value)
        {
            return SafeScore(value).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
