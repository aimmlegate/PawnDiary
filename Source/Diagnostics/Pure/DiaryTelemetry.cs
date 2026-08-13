// Session-local, privacy-safe telemetry for the diary pipeline.
//
// The hot capture path records only stable code labels (outcome, stage, signal type, event type),
// never pawn names, pawn ids, event ids, prompt text, or generated text. Counters explain where
// candidates went; a separate bounded ring retains only unexpected outcomes so diagnostics cannot
// grow with colony history. The state is thread-safe because LLM admission can run off-thread.
//
// This file deliberately has no RimWorld/Verse/Unity dependencies and is covered by a standalone
// console test. Impure logging and optional remote error reporting live in DiaryTelemetryReporter.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace PawnDiary
{
    /// <summary>
    /// Stable lifecycle outcomes counted by the runtime diagnostics ledger. Names are intentionally
    /// explicit because they are also the developer-export schema.
    /// </summary>
    internal enum DiaryTelemetryOutcome
    {
        SubmitNull,
        SubmitWithoutGame,
        DispatchNotReady,
        StartingArrivalBlocked,
        SourceDuplicate,
        PayloadUnavailable,
        ForcedSignalUnsupported,
        CatalogMissing,
        PolicyDropped,
        FrequencyRejected,
        KnowledgeCapturedWithoutPage,
        EventTypeDuplicate,
        EventRecorded,
        RoutedBatch,
        RoutedAmbient,
        RoutedDayReflection,
        FoldedIntoDayDigest,
        EmitCompletedWithoutEvent,
        EmitCreatedMultipleEvents,
        DispatchException,
        DispatchExceptionAfterCommit,
        FanoutCompleted,
        FanoutChildException,
        FanoutChildExceptionAfterCommit,
        LlmQueueAccepted,
        LlmQueueInvalid,
        LlmQueueInactive,
        LlmQueueDuplicate,
        LlmQueueFull,
        LlmResultApplied,
        LlmTitleResultApplied,
        LlmResultInvalid,
        LlmResultMissingEvent,
        LlmResultApplyException,
        LlmPendingRecovered,
        RepositoryRegistrationInvalid,
        RepositoryDuplicateIdRejected,
        LoadedEventIdRepaired,
        LoadedEventInvalidDiscarded,
        LoadedEventDuplicateDiscarded,
        IntegrityAuditHealthy,
        IntegrityIssue,
        IntegrityAuditException,
        PatchException,
        PreSaveActionException,
        HookApplied,
        HookSkipped,
        HookDegraded,
        HookFailed,
        CorrelationScopeStarted,
        CorrelationSignalStaged,
        CorrelationCommitted,
        CorrelationReleased,
        CorrelationExpired,
        CorrelationOverflowReleased,
        CorrelationReverseOrderMatched,
        CorrelationResetDropped,
        TelemetryCounterCapacityReached
    }

    /// <summary>One aggregate row in a point-in-time telemetry snapshot.</summary>
    internal sealed class DiaryTelemetryCounter
    {
        public DiaryTelemetryOutcome outcome;
        public string stage = string.Empty;
        public string source = string.Empty;
        public string eventType = string.Empty;
        public long count;

        public DiaryTelemetryCounter Copy()
        {
            return new DiaryTelemetryCounter
            {
                outcome = outcome,
                stage = stage,
                source = source,
                eventType = eventType,
                count = count
            };
        }
    }

    /// <summary>
    /// One recent anomalous transition. It contains code labels and a stable fingerprint only; detail
    /// must be a sanitized summary such as an exception type or integrity-count string.
    /// </summary>
    internal sealed class DiaryTelemetryRecord
    {
        public long sequence;
        public int tick;
        public DiaryTelemetryOutcome outcome;
        public string stage = string.Empty;
        public string source = string.Empty;
        public string eventType = string.Empty;
        public long count;
        public string detail = string.Empty;
        public string fingerprint = string.Empty;

        public DiaryTelemetryRecord Copy()
        {
            return new DiaryTelemetryRecord
            {
                sequence = sequence,
                tick = tick,
                outcome = outcome,
                stage = stage,
                source = source,
                eventType = eventType,
                count = count,
                detail = detail,
                fingerprint = fingerprint
            };
        }
    }

    /// <summary>Detached, immutable-by-convention view of one telemetry session.</summary>
    internal sealed class DiaryTelemetrySnapshot
    {
        public readonly List<DiaryTelemetryCounter> counters;
        public readonly List<DiaryTelemetryRecord> recentAnomalies;

        public DiaryTelemetrySnapshot(
            List<DiaryTelemetryCounter> counters,
            List<DiaryTelemetryRecord> recentAnomalies)
        {
            this.counters = counters ?? new List<DiaryTelemetryCounter>();
            this.recentAnomalies = recentAnomalies ?? new List<DiaryTelemetryRecord>();
        }
    }

    /// <summary>
    /// Thread-safe bounded state for one loaded game. All inputs are plain values supplied by adapters.
    /// </summary>
    internal sealed class DiaryTelemetrySessionState
    {
        // These caps are defensive memory limits, not gameplay policy, so hardcoding is intentional.
        internal const int DefaultMaximumCounterBuckets = 2048;
        internal const int DefaultMaximumRecentAnomalies = 96;
        private const int MaximumDimensionCharacters = 96;
        private const int MaximumDetailCharacters = 240;
        private static readonly CounterKey OverflowCounterKey = new CounterKey(
            DiaryTelemetryOutcome.TelemetryCounterCapacityReached,
            "telemetry",
            "overflow",
            string.Empty);

        private readonly object sync = new object();
        private readonly int maximumCounterBuckets;
        private readonly int maximumRecentAnomalies;
        private readonly Dictionary<CounterKey, DiaryTelemetryCounter> counters =
            new Dictionary<CounterKey, DiaryTelemetryCounter>();
        private readonly Queue<DiaryTelemetryRecord> recentAnomalies =
            new Queue<DiaryTelemetryRecord>();
        private long sequence;

        /// <summary>Creates a bounded independent session; non-positive caps are clamped to one.</summary>
        public DiaryTelemetrySessionState(int maximumCounterBuckets, int maximumRecentAnomalies)
        {
            this.maximumCounterBuckets = Math.Max(1, maximumCounterBuckets);
            this.maximumRecentAnomalies = Math.Max(1, maximumRecentAnomalies);
        }

        /// <summary>
        /// Adds <paramref name="count"/> to one aggregate bucket and, for anomalous outcomes, appends
        /// one bounded recent record. Values are normalized before they become keys or export text.
        /// </summary>
        public void Record(
            DiaryTelemetryOutcome outcome,
            string stage,
            string source,
            string eventType,
            int tick,
            long count = 1,
            string detail = null,
            string fingerprint = null)
        {
            long safeCount = count <= 0 ? 1 : count;
            string safeStage = Normalize(stage, MaximumDimensionCharacters);
            string safeSource = Normalize(source, MaximumDimensionCharacters);
            string safeEventType = Normalize(eventType, MaximumDimensionCharacters);
            CounterKey key = new CounterKey(outcome, safeStage, safeSource, safeEventType);

            lock (sync)
            {
                DiaryTelemetryCounter counter;
                if (!counters.TryGetValue(key, out counter))
                {
                    // Reserve one bucket for overflow so the dictionary itself never exceeds the cap.
                    int maximumSpecificBuckets = Math.Max(0, maximumCounterBuckets - 1);
                    if (counters.Count >= maximumSpecificBuckets)
                    {
                        key = OverflowCounterKey;
                        if (!counters.TryGetValue(key, out counter))
                        {
                            counter = new DiaryTelemetryCounter
                            {
                                outcome = DiaryTelemetryOutcome.TelemetryCounterCapacityReached,
                                stage = "telemetry",
                                source = "overflow",
                                eventType = string.Empty
                            };
                            counters[key] = counter;
                        }
                    }
                    else
                    {
                        counter = new DiaryTelemetryCounter
                        {
                            outcome = outcome,
                            stage = safeStage,
                            source = safeSource,
                            eventType = safeEventType
                        };
                        counters[key] = counter;
                    }
                }

                counter.count = SaturatingAdd(counter.count, safeCount);
                sequence++;

                if (!IsAnomaly(outcome))
                {
                    return;
                }

                while (recentAnomalies.Count >= maximumRecentAnomalies)
                {
                    recentAnomalies.Dequeue();
                }

                recentAnomalies.Enqueue(new DiaryTelemetryRecord
                {
                    sequence = sequence,
                    tick = tick,
                    outcome = outcome,
                    stage = safeStage,
                    source = safeSource,
                    eventType = safeEventType,
                    count = safeCount,
                    detail = Normalize(detail, MaximumDetailCharacters),
                    fingerprint = Normalize(fingerprint, MaximumDimensionCharacters)
                });
            }
        }

        /// <summary>Returns deep copies sorted deterministically for tests and developer export.</summary>
        public DiaryTelemetrySnapshot Snapshot()
        {
            lock (sync)
            {
                List<DiaryTelemetryCounter> counterCopy =
                    new List<DiaryTelemetryCounter>(counters.Count);
                foreach (DiaryTelemetryCounter counter in counters.Values)
                {
                    counterCopy.Add(counter.Copy());
                }
                counterCopy.Sort(CompareCounters);

                List<DiaryTelemetryRecord> anomalyCopy =
                    new List<DiaryTelemetryRecord>(recentAnomalies.Count);
                foreach (DiaryTelemetryRecord record in recentAnomalies)
                {
                    anomalyCopy.Add(record.Copy());
                }

                return new DiaryTelemetrySnapshot(counterCopy, anomalyCopy);
            }
        }

        /// <summary>True for outcomes that deserve a recent breadcrumb in addition to a counter.</summary>
        internal static bool IsAnomaly(DiaryTelemetryOutcome outcome)
        {
            switch (outcome)
            {
                case DiaryTelemetryOutcome.CatalogMissing:
                case DiaryTelemetryOutcome.EmitCompletedWithoutEvent:
                case DiaryTelemetryOutcome.EmitCreatedMultipleEvents:
                case DiaryTelemetryOutcome.DispatchException:
                case DiaryTelemetryOutcome.DispatchExceptionAfterCommit:
                case DiaryTelemetryOutcome.FanoutChildException:
                case DiaryTelemetryOutcome.FanoutChildExceptionAfterCommit:
                case DiaryTelemetryOutcome.LlmQueueInvalid:
                case DiaryTelemetryOutcome.LlmQueueInactive:
                case DiaryTelemetryOutcome.LlmQueueFull:
                case DiaryTelemetryOutcome.LlmResultInvalid:
                case DiaryTelemetryOutcome.LlmResultMissingEvent:
                case DiaryTelemetryOutcome.LlmResultApplyException:
                case DiaryTelemetryOutcome.LlmPendingRecovered:
                case DiaryTelemetryOutcome.RepositoryRegistrationInvalid:
                case DiaryTelemetryOutcome.RepositoryDuplicateIdRejected:
                case DiaryTelemetryOutcome.LoadedEventInvalidDiscarded:
                case DiaryTelemetryOutcome.LoadedEventDuplicateDiscarded:
                case DiaryTelemetryOutcome.IntegrityIssue:
                case DiaryTelemetryOutcome.IntegrityAuditException:
                case DiaryTelemetryOutcome.PatchException:
                case DiaryTelemetryOutcome.PreSaveActionException:
                case DiaryTelemetryOutcome.HookDegraded:
                case DiaryTelemetryOutcome.HookFailed:
                case DiaryTelemetryOutcome.CorrelationResetDropped:
                case DiaryTelemetryOutcome.TelemetryCounterCapacityReached:
                    return true;
                default:
                    return false;
            }
        }

        private struct CounterKey : IEquatable<CounterKey>
        {
            private readonly DiaryTelemetryOutcome outcome;
            private readonly string stage;
            private readonly string source;
            private readonly string eventType;

            public CounterKey(
                DiaryTelemetryOutcome outcome,
                string stage,
                string source,
                string eventType)
            {
                this.outcome = outcome;
                this.stage = stage ?? string.Empty;
                this.source = source ?? string.Empty;
                this.eventType = eventType ?? string.Empty;
            }

            public bool Equals(CounterKey other)
            {
                return outcome == other.outcome
                    && string.Equals(stage, other.stage, StringComparison.Ordinal)
                    && string.Equals(source, other.source, StringComparison.Ordinal)
                    && string.Equals(eventType, other.eventType, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is CounterKey && Equals((CounterKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)outcome;
                    hash = (hash * 397) ^ stage.GetHashCode();
                    hash = (hash * 397) ^ source.GetHashCode();
                    return (hash * 397) ^ eventType.GetHashCode();
                }
            }
        }

        private static long SaturatingAdd(long current, long value)
        {
            return current > long.MaxValue - value ? long.MaxValue : current + value;
        }

        private static string Normalize(string value, int maximumCharacters)
        {
            string clean = (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
            return clean.Length <= maximumCharacters
                ? clean
                : clean.Substring(0, maximumCharacters);
        }

        private static int CompareCounters(
            DiaryTelemetryCounter left,
            DiaryTelemetryCounter right)
        {
            int outcome = left.outcome.CompareTo(right.outcome);
            if (outcome != 0) return outcome;
            int stage = string.CompareOrdinal(left.stage, right.stage);
            if (stage != 0) return stage;
            int source = string.CompareOrdinal(left.source, right.source);
            return source != 0
                ? source
                : string.CompareOrdinal(left.eventType, right.eventType);
        }
    }

    /// <summary>
    /// Static session façade used by capture and transport adapters. Reset once per loaded Game.
    /// </summary>
    internal static class DiaryTelemetry
    {
        private static DiaryTelemetrySessionState currentSession =
            NewSession();

        /// <summary>Starts an empty bounded session without touching game state.</summary>
        public static void ResetSession()
        {
            Interlocked.Exchange(ref currentSession, NewSession());
        }

        /// <summary>Records one plain transition in the current session. Safe from any thread.</summary>
        public static void Record(
            DiaryTelemetryOutcome outcome,
            string stage,
            string source = null,
            string eventType = null,
            int tick = -1,
            long count = 1,
            string detail = null,
            string fingerprint = null)
        {
            DiaryTelemetrySessionState session = Volatile.Read(ref currentSession);
            session.Record(
                outcome,
                stage,
                source,
                eventType,
                tick,
                count,
                detail,
                fingerprint);
        }

        /// <summary>Returns a detached snapshot of counters and recent anomalies.</summary>
        public static DiaryTelemetrySnapshot Snapshot()
        {
            return Volatile.Read(ref currentSession).Snapshot();
        }

        private static DiaryTelemetrySessionState NewSession()
        {
            return new DiaryTelemetrySessionState(
                DiaryTelemetrySessionState.DefaultMaximumCounterBuckets,
                DiaryTelemetrySessionState.DefaultMaximumRecentAnomalies);
        }
    }

    /// <summary>Plain-text formatter for the developer export; contains no player data.</summary>
    internal static class DiaryTelemetryFormatter
    {
        public static string Format(DiaryTelemetrySnapshot snapshot)
        {
            snapshot = snapshot ?? new DiaryTelemetrySnapshot(null, null);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("== Runtime telemetry (current game; not saved) ==");
            if (snapshot.counters.Count == 0)
            {
                builder.AppendLine("(no runtime transitions recorded)");
            }
            else
            {
                builder.AppendLine("Counters:");
                for (int i = 0; i < snapshot.counters.Count; i++)
                {
                    DiaryTelemetryCounter counter = snapshot.counters[i];
                    builder.Append("  ")
                        .Append(counter.outcome)
                        .Append(" | stage=").Append(Display(counter.stage))
                        .Append(" | source=").Append(Display(counter.source))
                        .Append(" | event_type=").Append(Display(counter.eventType))
                        .Append(" | count=").Append(counter.count.ToString(CultureInfo.InvariantCulture))
                        .AppendLine();
                }
            }

            builder.AppendLine("Recent anomalies (oldest first, bounded):");
            if (snapshot.recentAnomalies.Count == 0)
            {
                builder.AppendLine("  (none)");
                return builder.ToString();
            }

            for (int i = 0; i < snapshot.recentAnomalies.Count; i++)
            {
                DiaryTelemetryRecord record = snapshot.recentAnomalies[i];
                builder.Append("  #").Append(record.sequence.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(record.outcome)
                    .Append(" | tick=").Append(record.tick >= 0
                        ? record.tick.ToString(CultureInfo.InvariantCulture)
                        : "n/a")
                    .Append(" | stage=").Append(Display(record.stage))
                    .Append(" | source=").Append(Display(record.source))
                    .Append(" | event_type=").Append(Display(record.eventType))
                    .Append(" | count=").Append(record.count.ToString(CultureInfo.InvariantCulture));
                if (record.detail.Length > 0)
                {
                    builder.Append(" | detail=").Append(record.detail);
                }
                if (record.fingerprint.Length > 0)
                {
                    builder.Append(" | fingerprint=").Append(record.fingerprint);
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static string Display(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }
    }
}
