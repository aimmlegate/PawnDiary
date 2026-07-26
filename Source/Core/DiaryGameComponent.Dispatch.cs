// The single ingestion pipeline. Every DiarySignal funnels through Dispatch here, which runs the
// universal steps that used to be copy-pasted into each RecordXxx method:
//   guard → source dedup-check → build context → catalog Decide → generic/source dedup-mark → Emit.
// The source-specific work (capturing live state into a payload, building text/context, queuing the
// LLM) stays in the per-source DiarySignal subclass. This file is the "one method, data-controlled"
// half the design asked for: the catalog Spec (XML-backed) decides, and Dispatch performs only the
// shared side effects.
//
// ── Consolidated dedup store ──
// Every source used to own its own transient Dictionary<string,int> (recentThoughtEvents,
// recentRaidEvents, …). Those keys are all source-prefixed ("thought|…", "raid|…"), so they never
// collide across sources and can share ONE dictionary. `recentEvents` below is that single store; it
// is transient (never saved). Each entry records the source's OWN dedup window, and prune evicts a
// key only once THAT window has elapsed (see RecentEventExpiry) — so a short-window source can no
// longer evict a still-live long-window key, which the naive "borrow the caller's window" prune did.
//
// ── check-before-decide ──
// The dedup CHECK runs before BuildContext/Decide, and the MARK runs after Decide. The split restores
// the pre-refactor ability path (which checked dedup before drawing Rand.Value), so a dropped
// duplicate performs no unnecessary isolated roll and skips pure Decide entirely.
// The MARK stays after Decide so an event the catalog drops (e.g. an ability that fails its chance
// roll) does not consume the dedup window.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // The single transient dedup store shared by every migrated source. Keyed by the raw
        // source-prefixed key each signal supplies (e.g. "thought|pawnId|defName"). Each value also
        // remembers the source's OWN dedup window, so a prune sweep driven by a short-window source
        // cannot evict a still-live long-window key (see RecentEventExpiry). Not saved; cleared on
        // StartedNewGame/LoadedGame alongside the other transient state.
        private readonly Dictionary<string, RecentEventEntry> recentEvents = new Dictionary<string, RecentEventEntry>();
        // Enum.ToString allocates on every candidate. Cache the stable event-type labels once because
        // telemetry runs on the same high-frequency path as dedup and catalog selection.
        private static readonly string[] TelemetryEventTypeNames =
            BuildTelemetryEventTypeNames();

        /// <summary>
        /// Runs one captured event through the shared pipeline. Called by
        /// <see cref="DiaryEvents.Submit(DiarySignal)"/>. The solo path checks dedup before reading
        /// the payload, runs the pure catalog decision, checks the short generic event-type safety key,
        /// then marks dedup immediately before the impure Emit (so a dropped event never consumes a
        /// window, and a deduped event never builds text or mutates the save).
        /// </summary>
        /// <returns>
        /// True if the signal passed the guard, decision, and dedup and its <c>Emit</c> ran. Most
        /// callers (the static <see cref="DiaryEvents.Submit(DiarySignal)"/> façade) ignore this, but a
        /// scanner whose own episode/staging state is coupled to whether the event recorded (e.g.
        /// ThoughtProgression's recorded-stage set) calls <c>Dispatch</c> directly and reads the result.
        /// </returns>
        internal bool Dispatch(DiarySignal signal)
        {
            string source = SignalTypeName(signal);
            long registrationBefore = events.RegistrationVersion;
            try
            {
                return DispatchCore(signal, source);
            }
            catch (Exception exception)
            {
                bool committed = events.RegistrationVersion > registrationBefore;
                DiaryTelemetryOutcome outcome = committed
                    ? DiaryTelemetryOutcome.DispatchExceptionAfterCommit
                    : DiaryTelemetryOutcome.DispatchException;
                string fingerprint = DiaryTelemetryReporter.RecordException(
                    outcome,
                    "dispatch",
                    source,
                    null,
                    exception,
                    DiaryTelemetryReporter.CurrentGameTick());
                Log.ErrorOnce(
                    "[Pawn Diary] " + source + " dispatch failed and was skipped"
                    + (committed ? " after event persistence began" : string.Empty)
                    + ": " + exception,
                    DiaryTelemetryReporter.ErrorOnceKey(
                        "PawnDiary.Dispatch." + source,
                        fingerprint));
                if (committed)
                {
                    RunDiaryIntegrityAudit("dispatch_exception", true);
                }
                return false;
            }
        }

        private bool DispatchCore(DiarySignal signal, string source)
        {
            if (signal == null || !CanRecordGameplayEventNow())
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.DispatchNotReady,
                    "dispatch.guard",
                    signal,
                    null);
                return false;
            }

            if (!EnsureStartingArrivalsBefore(signal))
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.StartingArrivalBlocked,
                    "dispatch.arrival_bootstrap",
                    signal,
                    null);
                return false;
            }

            bool forceRecord = signal.ForceRecord;

            // Dedup CHECK first, before any impure payload work. Two reasons:
            //   1. It restores the pre-refactor ordering for sources whose old RecordXxx checked
            //      dedup before drawing impure state — notably Ability, which used to check dedup
            //      before its Rand.Value roll. Drawing the roll at capture time and only then
            //      deduping would perform an unnecessary cosmetic roll for a dropped duplicate.
            //   2. It skips BuildContext + Decide for a deduped event, which is pure win with no
            //      behavior change (Decide is side-effect-free).
            // The dedup MARK stays after Decide (below): an event that Decide drops (e.g. an ability
            // whose roll fails its cooldown-weighted chance) must not consume the dedup window.
            string key = signal.DedupKey;
            int windowTicks = signal.DedupWindowTicks;
            if (!forceRecord
                && !string.IsNullOrEmpty(key)
                && IsRecentlyRecorded(recentEvents, key, windowTicks))
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.SourceDuplicate,
                    "dispatch.source_dedup",
                    signal,
                    null);
                return false;
            }

            // Read the payload AFTER the dedup check. A null payload means the signal's capture
            // already decided to drop (missing/ineligible inputs, no matching policy), and its
            // BuildContext may deref state that was never set. This is the common path for sources
            // that submit for every candidate (e.g. a HediffSignal for a hediff with no diary group).
            // For Ability this read is also where its isolated Rand.Value roll is drawn (lazily,
            // post-dedup).
            DiaryEventData payload = signal.Payload;
            if (payload == null)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.PayloadUnavailable,
                    "dispatch.payload",
                    signal,
                    null);
                return false;
            }

            CaptureDecision decision;
            if (forceRecord)
            {
                decision = ForcedDecisionFor(payload);
                if (decision == CaptureDecision.Drop)
                {
                    RecordSignalOutcome(
                        DiaryTelemetryOutcome.ForcedSignalUnsupported,
                        "dispatch.decision",
                        signal,
                        payload);
                    return false;
                }
            }
            else
            {
                CaptureContext context = signal.BuildContext();
                DiaryTelemetryOutcome dropOutcome;
                if (!TryDecide(payload, context, out decision, out dropOutcome))
                {
                    RecordSignalOutcome(
                        dropOutcome,
                        "dispatch.decision",
                        signal,
                        payload);
                    // Page policy and knowledge policy are intentionally independent, but semantic
                    // rejection still applies to both. Relax only page switches and re-run the pure
                    // reducer before invoking an allowlisted signal's no-page adapter. This prevents
                    // duplicate arrivals, invalid mutations, and already-recorded family events from
                    // becoming knowledge merely because their ordinary page was dropped.
                    if (DiaryKnowledgeCapturePolicy.ShouldCaptureWithoutPage(payload, context))
                    {
                        signal.CaptureKnowledgeWithoutPage(this);
                        RecordSignalOutcome(
                            DiaryTelemetryOutcome.KnowledgeCapturedWithoutPage,
                            "dispatch.knowledge_only",
                            signal,
                            payload);
                    }

                    return false;
                }
            }

            string eventTypeKey = EventTypeDedupKeyFor(signal, payload, decision, key);
            int eventTypeWindowTicks = signal.EventTypeDedupWindowTicks;
            if (!forceRecord
                && !string.IsNullOrEmpty(eventTypeKey)
                && IsRecentlyRecorded(recentEvents, eventTypeKey, eventTypeWindowTicks))
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.EventTypeDuplicate,
                    "dispatch.event_type_dedup",
                    signal,
                    payload);
                return false;
            }

            // Dedup MARK after Decide and both dedup checks, before the impure Emit — so a dropped
            // event never consumes the window, and a recorded one is marked exactly once on the path
            // it actually emits on.
            if (!string.IsNullOrEmpty(key))
            {
                MarkRecentlyRecorded(recentEvents, key, windowTicks);
            }
            if (!string.IsNullOrEmpty(eventTypeKey))
            {
                MarkRecentlyRecorded(recentEvents, eventTypeKey, eventTypeWindowTicks);
            }

            long registrationBeforeEmit = events.RegistrationVersion;
            DiaryTelemetryOutcome emitOutcome =
                EmitWithLowSaliencePacing(signal, payload, decision);
            long registrations = events.RegistrationVersion - registrationBeforeEmit;
            if (emitOutcome == DiaryTelemetryOutcome.EventRecorded && registrations == 0)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.EmitCompletedWithoutEvent,
                    "dispatch.emit",
                    signal,
                    payload);
            }
            else if (registrations > 1)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.EmitCreatedMultipleEvents,
                    "dispatch.emit",
                    signal,
                    payload,
                    registrations);
            }
            else
            {
                RecordSignalOutcome(
                    emitOutcome,
                    "dispatch.emit",
                    signal,
                    payload);
            }
            return true;
        }

        /// <summary>
        /// Runs a colony-wide fan-out. Peeks the colony dedup window first; then iterates the per-pawn
        /// signals (already filtered to eligible colonists) through the fan-out child path:
        /// payload/context snapshot → Decide → optional per-pawn dedup → Emit. The colony key is marked
        /// only after at least one entry was emitted, so an empty colonist list cannot consume the whole
        /// window (matching the old fan-out recorders).
        /// </summary>
        internal void Dispatch(DiaryFanoutSignal signal)
        {
            string source = signal?.GetType().Name ?? "null";
            long registrationBefore = events.RegistrationVersion;
            try
            {
                DispatchFanoutCore(signal, source);
            }
            catch (Exception exception)
            {
                bool committed = events.RegistrationVersion > registrationBefore;
                DiaryTelemetryOutcome outcome = committed
                    ? DiaryTelemetryOutcome.DispatchExceptionAfterCommit
                    : DiaryTelemetryOutcome.DispatchException;
                string fingerprint = DiaryTelemetryReporter.RecordException(
                    outcome,
                    "fanout.dispatch",
                    source,
                    null,
                    exception,
                    DiaryTelemetryReporter.CurrentGameTick());
                Log.ErrorOnce(
                    "[Pawn Diary] " + source + " fan-out dispatch failed"
                    + (committed ? " after event persistence began" : string.Empty)
                    + ": " + exception,
                    DiaryTelemetryReporter.ErrorOnceKey(
                        "PawnDiary.Fanout." + source,
                        fingerprint));
                if (committed)
                {
                    RunDiaryIntegrityAudit("fanout_dispatch_exception", true);
                }
            }
        }

        private void DispatchFanoutCore(DiaryFanoutSignal signal, string source)
        {
            if (signal == null || !CanRecordGameplayEventNow())
            {
                DiaryTelemetry.Record(
                    DiaryTelemetryOutcome.DispatchNotReady,
                    "fanout.guard",
                    source,
                    null,
                    DiaryTelemetryReporter.CurrentGameTick());
                return;
            }

            if (!EnsureStartingArrivalsBefore(signal))
            {
                DiaryTelemetry.Record(
                    DiaryTelemetryOutcome.StartingArrivalBlocked,
                    "fanout.arrival_bootstrap",
                    source,
                    null,
                    DiaryTelemetryReporter.CurrentGameTick());
                return;
            }

            string colonyKey = signal.ColonyDedupKey;
            int colonyTicks = signal.ColonyDedupTicks;
            if (!string.IsNullOrEmpty(colonyKey)
                && IsRecentlyRecorded(recentEvents, colonyKey, colonyTicks))
            {
                DiaryTelemetry.Record(
                    DiaryTelemetryOutcome.SourceDuplicate,
                    "fanout.colony_dedup",
                    source,
                    null,
                    DiaryTelemetryReporter.CurrentGameTick());
                return;
            }

            // A colony fan-out is a set of independent pawn stories. One modded getter or one
            // malformed child must cost only that pawn, not every sibling after it.
            int emittedCount = FaultIsolatedItemRunner.Run(
                signal.PerPawnSignals(),
                TryDispatchFanoutChild,
                (child, exception) =>
                {
                    string childType = child?.GetType().FullName ?? "null";
                    string errorKey = "PawnDiary.FanoutChild." + signal.GetType().FullName
                        + "." + childType;
                    string fingerprint = DiaryTelemetryReporter.FingerprintException(
                        "fanout.child",
                        childType,
                        null,
                        exception);
                    Log.ErrorOnce(
                        "[Pawn Diary] Skipped one fan-out child after its payload, context, or emit "
                        + "failed; remaining pawns were still attempted: " + exception,
                        DiaryTelemetryReporter.ErrorOnceKey(errorKey, fingerprint));
                });

            if (emittedCount > 0 && !string.IsNullOrEmpty(colonyKey))
            {
                MarkRecentlyRecorded(recentEvents, colonyKey, colonyTicks);
            }
            DiaryTelemetry.Record(
                DiaryTelemetryOutcome.FanoutCompleted,
                "fanout.dispatch",
                source,
                null,
                DiaryTelemetryReporter.CurrentGameTick());
        }

        private bool TryDispatchFanoutChild(DiarySignal child)
        {
            string source = SignalTypeName(child);
            long registrationBefore = events.RegistrationVersion;
            try
            {
                return TryDispatchFanoutChildCore(child);
            }
            catch (Exception exception)
            {
                bool committed = events.RegistrationVersion > registrationBefore;
                DiaryTelemetryReporter.RecordException(
                    committed
                        ? DiaryTelemetryOutcome.FanoutChildExceptionAfterCommit
                        : DiaryTelemetryOutcome.FanoutChildException,
                    "fanout.child",
                    source,
                    null,
                    exception,
                    DiaryTelemetryReporter.CurrentGameTick());
                if (committed)
                {
                    RunDiaryIntegrityAudit("fanout_child_exception", true);
                }
                throw;
            }
        }

        private bool TryDispatchFanoutChildCore(DiarySignal child)
        {
            if (child == null)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.PayloadUnavailable,
                    "fanout.child",
                    null,
                    null);
                return false;
            }

            DiaryEventData childPayload = child.Payload;
            if (childPayload == null)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.PayloadUnavailable,
                    "fanout.payload",
                    child,
                    null);
                return false;
            }

            CaptureDecision decision;
            DiaryTelemetryOutcome dropOutcome;
            if (!TryDecide(childPayload, child.BuildContext(), out decision, out dropOutcome))
            {
                RecordSignalOutcome(
                    dropOutcome,
                    "fanout.decision",
                    child,
                    childPayload);
                return false;
            }

            // Most fan-outs dedup only at the colony level (child.DedupKey empty); a child may add
            // its own per-pawn window if it needs one. The short generic type key is checked after
            // Decide, matching the solo path, because it needs the payload's event type.
            string childKey = child.DedupKey;
            if (!string.IsNullOrEmpty(childKey)
                && IsRecentlyRecorded(recentEvents, childKey, child.DedupWindowTicks))
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.SourceDuplicate,
                    "fanout.source_dedup",
                    child,
                    childPayload);
                return false;
            }

            string childEventTypeKey =
                EventTypeDedupKeyFor(child, childPayload, decision, childKey);
            int childEventTypeWindowTicks = child.EventTypeDedupWindowTicks;
            if (!string.IsNullOrEmpty(childEventTypeKey)
                && IsRecentlyRecorded(recentEvents, childEventTypeKey, childEventTypeWindowTicks))
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.EventTypeDuplicate,
                    "fanout.event_type_dedup",
                    child,
                    childPayload);
                return false;
            }

            // Preserve the ingestion pipeline's mark-before-emit contract. Emit is not atomic: a
            // failure can occur after an event has already entered persistence, so leaving the key
            // unmarked would let a later retry duplicate that partially completed child.
            if (!string.IsNullOrEmpty(childKey))
            {
                MarkRecentlyRecorded(recentEvents, childKey, child.DedupWindowTicks);
            }
            if (!string.IsNullOrEmpty(childEventTypeKey))
            {
                MarkRecentlyRecorded(recentEvents, childEventTypeKey, childEventTypeWindowTicks);
            }
            long registrationBeforeEmit = events.RegistrationVersion;
            DiaryTelemetryOutcome emitOutcome =
                EmitWithLowSaliencePacing(child, childPayload, decision);
            long registrations = events.RegistrationVersion - registrationBeforeEmit;
            if (emitOutcome == DiaryTelemetryOutcome.EventRecorded && registrations == 0)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.EmitCompletedWithoutEvent,
                    "fanout.emit",
                    child,
                    childPayload);
            }
            else if (registrations > 1)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.EmitCreatedMultipleEvents,
                    "fanout.emit",
                    child,
                    childPayload,
                    registrations);
            }
            else
            {
                RecordSignalOutcome(
                    emitOutcome,
                    "fanout.emit",
                    child,
                    childPayload);
            }

            // The colony window closes on a CONSUMED child, not only on a visible page: a child the
            // B6 soft cap folded into a digest was still handled, and re-running the fan-out for it
            // would duplicate the moment.
            return true;
        }

        /// <summary>
        /// Quality Wave B6. The last step before a page exists: everyday, low-stakes moments are
        /// paced so one colonist cannot fill a day with near-identical entries.
        ///
        /// Nothing here changes WHETHER an event was captured — dedup is already marked and the
        /// catalog has already decided. It only chooses between "write the page" and "remember this
        /// as one line in tonight's reflection". A page is folded away only when it is low-salience,
        /// the cap is on, and EVERY diarist it belongs to is already at their daily limit, so a shared
        /// pair page is never half-visible. The count advances only after a real emit.
        /// </summary>
        private DiaryTelemetryOutcome EmitWithLowSaliencePacing(
            DiarySignal signal, DiaryEventData payload, CaptureDecision decision)
        {
            // Cheapest gates first: the tuning read and the decision test are free, while IsLowSalience
            // costs a (memoized) group classification on a hook that runs for every logged interaction.
            int cap = DiaryTuning.Current.lowSalienceDailySoftCap;
            bool paceable = DigestPacingPolicy.IsSoftCapEnabled(cap)
                && (decision == CaptureDecision.GenerateSolo || decision == CaptureDecision.GeneratePair)
                && signal.IsLowSalience
                // A folded page has value only when the daily-reflection route can consume its digest.
                // If the player disables that route, preserve the ordinary page instead of silently
                // clearing the buffered moment through the disabled-reflection settlement path.
                && DiaryTuning.Current.daySummaryEnabled
                && IsReflectionGroupEnabled(DayReflectionEventData.DefNameToken);
            if (!paceable)
            {
                // Batched/ambient routes never produced a page of their own, and important or combat
                // moments are exempt by design, so both skip pacing entirely.
                signal.Emit(this, decision);
                return TelemetryOutcomeForDecision(decision);
            }

            List<string> writers = new List<string>(2);
            signal.CollectPacedWriters(payload, decision, writers);
            int day = CurrentDayIndex;
            List<int> counts = new List<int>(writers.Count);
            for (int i = 0; i < writers.Count; i++)
            {
                counts.Add(LowSalienceCountForDay(writers[i], day));
            }

            if (!DigestPacingPolicy.ShouldSuppressPage(true, counts, cap))
            {
                signal.Emit(this, decision);
                for (int i = 0; i < writers.Count; i++)
                {
                    RecordLowSalienceEmission(writers[i], day);
                }

                return DiaryTelemetryOutcome.EventRecorded;
            }

            int tick = Find.TickManager.TicksGame;
            string sourceKind = signal.DigestSourceKind;
            for (int i = 0; i < writers.Count; i++)
            {
                AddDayDigestLine(
                    writers[i], day, sourceKind, signal.BuildDigestLineForPawn(writers[i]), tick);
            }
            return DiaryTelemetryOutcome.FoldedIntoDayDigest;
        }

        /// <summary>
        /// New-game Harmony signals can arrive before this component's first tick has recorded the
        /// founding-colonist arrival pages. Before accepting any non-arrival source, flush those
        /// arrivals so the pawn diary starts at "how I joined" rather than at the first incidental event.
        /// </summary>
        private bool EnsureStartingArrivalsBefore(DiarySignal signal)
        {
            if (!initialArrivalScanPending || signal is ArrivalSignal)
            {
                return true;
            }

            if (TryRecordStartingColonistArrivals())
            {
                initialArrivalScanPending = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Fan-out signals are never arrival bootstrap signals, so they wait for the same new-game
        /// arrival flush before recording their per-pawn children.
        /// </summary>
        private bool EnsureStartingArrivalsBefore(DiaryFanoutSignal signal)
        {
            if (!initialArrivalScanPending)
            {
                return true;
            }

            if (TryRecordStartingColonistArrivals())
            {
                initialArrivalScanPending = false;
                return true;
            }

            return false;
        }

        private static string EventTypeDedupKeyFor(
            DiarySignal signal, DiaryEventData payload, CaptureDecision decision, string sourceDedupKey)
        {
            if (signal == null || payload == null)
            {
                return string.Empty;
            }

            string key = signal.EventTypeDedupKey(payload, decision);
            if (!string.IsNullOrEmpty(key))
            {
                return key;
            }

            // Sources with a detailed key already collapse the exact event identity. Sources without
            // one get a short generic type+subject safety key so fluke double hooks do not emit twice.
            return string.IsNullOrEmpty(sourceDedupKey)
                ? GenericEventTypeDedup.KeyFor(payload, decision)
                : string.Empty;
        }

        private static CaptureDecision ForcedDecisionFor(DiaryEventData payload)
        {
            ExternalEventData external = payload as ExternalEventData;
            return external == null
                ? CaptureDecision.Drop
                : ExternalEventData.ForceDecision(external);
        }

        // ── Emit surface for DiarySignal.Emit ──
        // Narrow internal wrappers so signal classes (in PawnDiary.Ingestion) can drive generation
        // without widening the private generation internals (e.g. the bounds-cache parameter types).
        // These forward to the same private methods the old RecordXxx bodies called.

        /// <summary>Queues a single-POV LLM rewrite for a solo (or per-POV) entry.</summary>
        internal void QueueSolo(DiaryEvent diaryEvent, string povRole)
        {
            QueueLlmRewrite(diaryEvent, povRole);
        }

        /// <summary>Queues the sequential two-POV rewrite for a pairwise entry.</summary>
        internal void QueuePair(DiaryEvent diaryEvent)
        {
            QueuePairwiseGeneration(diaryEvent);
        }

        /// <summary>Stamps a transient "delay this POV's generation until tick X" marker.</summary>
        internal void DelaySolo(DiaryEvent diaryEvent, string povRole, int readyTick)
        {
            DelayGenerationUntil(diaryEvent, povRole, readyTick);
        }

        /// <summary>Queues the neutral death-description prompt for a death-shaped entry.</summary>
        internal void QueueDeathDescriptionFor(DiaryEvent diaryEvent)
        {
            QueueDeathDescription(diaryEvent);
        }

        /// <summary>Queues the neutral arrival-description prompt for an arrival-shaped entry.</summary>
        internal void QueueArrivalDescriptionFor(DiaryEvent diaryEvent)
        {
            QueueArrivalDescription(diaryEvent);
        }

        /// <summary>
        /// Shared "ask the catalog" step: looks up the Spec for the payload's event type and runs the
        /// pure Decide. Returns false (and a Drop decision) when the payload is missing, no Spec is
        /// registered, or the decision is Drop — the three cases where the caller should stop.
        /// </summary>
        private static bool TryDecide(
            DiaryEventData payload,
            CaptureContext ctx,
            out CaptureDecision decision,
            out DiaryTelemetryOutcome dropOutcome)
        {
            decision = CaptureDecision.Drop;
            dropOutcome = DiaryTelemetryOutcome.PolicyDropped;
            if (payload == null)
            {
                dropOutcome = DiaryTelemetryOutcome.PayloadUnavailable;
                return false;
            }

            DiaryEventSpec spec = DiaryEventCatalog.Get(payload.EventType);
            if (spec == null)
            {
                dropOutcome = DiaryTelemetryOutcome.CatalogMissing;
                return false;
            }

            decision = spec.Decide(payload, ctx);
            return decision != CaptureDecision.Drop;
        }

        private static DiaryTelemetryOutcome TelemetryOutcomeForDecision(CaptureDecision decision)
        {
            switch (decision)
            {
                case CaptureDecision.RouteBatch:
                    return DiaryTelemetryOutcome.RoutedBatch;
                case CaptureDecision.RouteAmbient:
                    return DiaryTelemetryOutcome.RoutedAmbient;
                case CaptureDecision.RouteDayReflection:
                    return DiaryTelemetryOutcome.RoutedDayReflection;
                default:
                    return DiaryTelemetryOutcome.EventRecorded;
            }
        }

        private static void RecordSignalOutcome(
            DiaryTelemetryOutcome outcome,
            string stage,
            DiarySignal signal,
            DiaryEventData payload,
            long count = 1)
        {
            DiaryTelemetry.Record(
                outcome,
                stage,
                SignalTypeName(signal),
                TelemetryEventTypeName(payload),
                payload?.Tick ?? -1,
                count);
        }

        private static string SignalTypeName(DiarySignal signal)
        {
            return signal?.GetType().Name ?? "null";
        }

        private static string TelemetryEventTypeName(DiaryEventData payload)
        {
            if (payload == null)
            {
                return null;
            }

            int index = (int)payload.EventType;
            return index >= 0 && index < TelemetryEventTypeNames.Length
                && TelemetryEventTypeNames[index] != null
                ? TelemetryEventTypeNames[index]
                : "unknown";
        }

        private static string[] BuildTelemetryEventTypeNames()
        {
            DiaryEventType[] values =
                (DiaryEventType[])Enum.GetValues(typeof(DiaryEventType));
            int maximum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                maximum = Math.Max(maximum, (int)values[i]);
            }

            string[] names = new string[maximum + 1];
            for (int i = 0; i < values.Length; i++)
            {
                names[(int)values[i]] = values[i].ToString();
            }
            return names;
        }
    }
}
