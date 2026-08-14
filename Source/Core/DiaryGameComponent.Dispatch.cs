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
        /// applies frequency admission, then marks dedup immediately before the impure Emit. Most
        /// frequency skips settle deterministic sources; Work and Ability preserve their historical
        /// retry contract explicitly.
        /// </summary>
        /// <returns>
        /// True if the signal passed guard, semantic decision, dedup, and frequency admission and its
        /// <c>Emit</c> completed. Stateful owners use <see cref="DispatchWithOutcome"/> instead so a
        /// deliberate no-page settlement cannot be mistaken for an ordinary rejection.
        /// </returns>
        internal bool Dispatch(DiarySignal signal)
        {
            return DiaryDispatchOutcomePolicy.EmissionRan(DispatchWithOutcome(signal));
        }

        /// <summary>
        /// Runs one signal while preserving the distinction between an ordinary rejection and a
        /// frequency rejection that deliberately settles a stateful source such as a reflection.
        /// </summary>
        internal DiaryDispatchOutcome DispatchWithOutcome(DiarySignal signal)
        {
            string source = SignalTypeName(signal);
            // Starting-arrival bootstrap can persist its own pages before this signal begins. DispatchCore
            // moves this baseline forward only after that prerequisite succeeds, so a later target fault
            // cannot claim somebody else's arrival commit as its own ExceptionAfterCommit result.
            long targetRegistrationBefore = events.RegistrationVersion;
            bool targetDispatchStarted = false;
            try
            {
                return DispatchCore(
                    signal,
                    source,
                    ref targetRegistrationBefore,
                    ref targetDispatchStarted);
            }
            catch (Exception exception)
            {
                bool committed = targetDispatchStarted
                    && events.RegistrationVersion > targetRegistrationBefore;
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
                return DiaryDispatchOutcomePolicy.ForException(
                    targetDispatchStarted,
                    committed);
            }
        }

        private DiaryDispatchOutcome DispatchCore(
            DiarySignal signal,
            string source,
            ref long targetRegistrationBefore,
            ref bool targetDispatchStarted)
        {
            if (signal == null || !CanRecordGameplayEventNow())
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.DispatchNotReady,
                    "dispatch.guard",
                    signal,
                    null);
                return DiaryDispatchOutcome.Rejected;
            }

            if (!EnsureStartingArrivalsBefore(signal))
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.StartingArrivalBlocked,
                    "dispatch.arrival_bootstrap",
                    signal,
                    null);
                return DiaryDispatchOutcome.Rejected;
            }

            // From this point onward registration changes belong to the requested signal, not to the
            // prerequisite arrival scan above. Set the marker before any payload/context adapter runs.
            targetRegistrationBefore = events.RegistrationVersion;
            targetDispatchStarted = true;

            bool forceRecord = signal.ForceRecord;

            // Dedup CHECK first, before any impure payload work. Two reasons:
            //   1. It preserves the pre-refactor ordering for probability-backed sources: duplicate
            //      Work/Ability candidates are rejected before the shared frequency adapter draws.
            //   2. It skips BuildContext + Decide for a deduped event, which is pure win with no
            //      behavior change (Decide is side-effect-free).
            // The dedup MARK stays after frequency admission below. Work and Ability deliberately do
            // not consume their source window when that probability decision fails.
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
                return DiaryDispatchOutcome.Rejected;
            }

            // Read the payload AFTER the dedup check. A null payload means the signal's capture
            // already decided to drop (missing/ineligible inputs, no matching policy), and its
            // BuildContext may deref state that was never set. This is the common path for sources
            // that submit for every candidate (e.g. a HediffSignal for a hediff with no diary group).
            // Source payload capture no longer owns frequency randomness; the shared admission step
            // below draws only after semantic and both dedup checks have accepted the candidate.
            DiaryEventData payload = signal.Payload;
            if (payload == null)
            {
                RecordSignalOutcome(
                    DiaryTelemetryOutcome.PayloadUnavailable,
                    "dispatch.payload",
                    signal,
                    null);
                return DiaryDispatchOutcome.Rejected;
            }

            CaptureDecision decision;
            CaptureContext context = null;
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
                    return DiaryDispatchOutcome.Rejected;
                }
            }
            else
            {
                context = signal.BuildContext();
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
                    CapturePageRejectedKnowledge(
                        signal,
                        payload,
                        context,
                        "dispatch.knowledge_only");

                    return DiaryDispatchOutcome.Rejected;
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
                return DiaryDispatchOutcome.Rejected;
            }

            // Interaction's independently configured Social Reflection reservation belongs to the
            // semantic source occurrence, not to the ordinary page's frequency result. Keep the
            // historical event-type dedup check above, then offer this hook before page admission.
            try
            {
                signal.OnAccepted(this, decision);
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Pawn Diary] " + source + " accepted-source follow-up failed; the source event "
                    + "will continue normally: " + exception,
                    ("PawnDiary.Dispatch.AcceptedFollowUp." + source
                        + "." + exception.GetType().FullName).GetHashCode());
            }

            DiaryFrequencyDecision frequency = DecideFrequency(context, forceRecord);
            if (!frequency.Accepted)
            {
                if (signal.FrequencyRejectionConsumesDedup)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        MarkRecentlyRecorded(recentEvents, key, windowTicks);
                    }
                    if (!string.IsNullOrEmpty(eventTypeKey))
                    {
                        MarkRecentlyRecorded(recentEvents, eventTypeKey, eventTypeWindowTicks);
                    }
                }

                CaptureFrequencyRejectedKnowledge(signal, payload, context);
                try
                {
                    signal.OnAcceptedEmissionCompleted(this, decision, false);
                }
                catch (Exception exception)
                {
                    Log.WarningOnce(
                        "[Pawn Diary] " + source + " frequency-rejected follow-up failed: "
                        + exception,
                        ("PawnDiary.Dispatch.FrequencyFollowUp." + source
                            + "." + exception.GetType().FullName).GetHashCode());
                }

                RecordSignalOutcome(
                    DiaryTelemetryOutcome.FrequencyRejected,
                    "dispatch.frequency",
                    signal,
                    payload);
                return DiaryDispatchOutcome.FrequencyRejected;
            }

            // Dedup MARK after semantic/frequency admission and both dedup checks, before the impure
            // Emit, so an accepted event is marked exactly once on the path that actually handles it.
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
            try
            {
                signal.OnAcceptedEmissionCompleted(
                    this,
                    decision,
                    registrations > 0);
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Pawn Diary] " + source + " post-emission follow-up failed; the source event "
                    + "will remain recorded: " + exception,
                    ("PawnDiary.Dispatch.PostEmissionFollowUp." + source
                        + "." + exception.GetType().FullName).GetHashCode());
            }
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
            return registrations > 0
                ? DiaryDispatchOutcome.PageRegistered
                : DiaryDispatchOutcome.ConsumedWithoutPage;
        }

        /// <summary>
        /// Runs a colony-wide fan-out. Peeks the colony dedup window first; then iterates the per-pawn
        /// signals (already filtered to eligible colonists) through the fan-out child path:
        /// payload/context snapshot → Decide → optional per-pawn dedup → frequency admission → Emit. The
        /// colony key is marked only after at least one child settles (a page, an intentional no-page
        /// result, or a post-commit fault), so an empty or wholly ineligible list cannot consume the
        /// whole window while a handled occurrence cannot replay.
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

            CaptureContext frequencyContext = signal.BuildFrequencyContext();
            FanoutFrequencyAdmission frequencyAdmission = new FanoutFrequencyAdmission
            {
                context = frequencyContext
            };

            // A colony fan-out is a set of independent pawn stories. One modded getter or one
            // malformed child must cost only that pawn, not every sibling after it.
            int settledCount = FaultIsolatedItemRunner.Run(
                signal.PerPawnSignals(),
                child => TryDispatchFanoutChild(child, frequencyAdmission),
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

            if (settledCount > 0 && !string.IsNullOrEmpty(colonyKey))
            {
                MarkRecentlyRecorded(recentEvents, colonyKey, colonyTicks);
            }
            if (frequencyAdmission.evaluated && !frequencyAdmission.decision.Accepted)
            {
                DiaryTelemetry.Record(
                    DiaryTelemetryOutcome.FrequencyRejected,
                    "fanout.frequency",
                    source,
                    frequencyContext?.FrequencyGroupKey,
                    DiaryTelemetryReporter.CurrentGameTick());
            }
            DiaryTelemetry.Record(
                DiaryTelemetryOutcome.FanoutCompleted,
                "fanout.dispatch",
                source,
                null,
                DiaryTelemetryReporter.CurrentGameTick());
        }

        private bool TryDispatchFanoutChild(
            DiarySignal child,
            FanoutFrequencyAdmission frequencyAdmission)
        {
            string source = SignalTypeName(child);
            long registrationBefore = events.RegistrationVersion;
            try
            {
                return TryDispatchFanoutChildCore(child, frequencyAdmission);
            }
            catch (Exception exception)
            {
                bool committed = events.RegistrationVersion > registrationBefore;
                string fingerprint = DiaryTelemetryReporter.RecordException(
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
                    Log.ErrorOnce(
                        "[Pawn Diary] One fan-out child failed after event persistence began; "
                        + "the shared occurrence was settled and remaining pawns will continue: "
                        + exception,
                        DiaryTelemetryReporter.ErrorOnceKey(
                            "PawnDiary.FanoutChildCommitted." + source,
                            fingerprint));
                    // Persistence already began for this child. Count it as settled so the colony
                    // dedup key closes even when this was the only eligible writer; otherwise a later
                    // retry could duplicate the committed page or its siblings.
                    return true;
                }
                throw;
            }
        }

        private bool TryDispatchFanoutChildCore(
            DiarySignal child,
            FanoutFrequencyAdmission frequencyAdmission)
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
            CaptureContext childContext = child.BuildContext();
            DiaryTelemetryOutcome dropOutcome;
            if (!TryDecide(childPayload, childContext, out decision, out dropOutcome))
            {
                RecordSignalOutcome(
                    dropOutcome,
                    "fanout.decision",
                    child,
                    childPayload);
                CapturePageRejectedKnowledge(
                    child,
                    childPayload,
                    childContext,
                    "fanout.knowledge_only");
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

            try
            {
                child.OnAccepted(this, decision);
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Pawn Diary] One fan-out child's accepted-source follow-up failed; that child "
                    + "will continue normally: " + exception,
                    ("PawnDiary.Fanout.AcceptedFollowUp."
                        + child.GetType().FullName + "." + exception.GetType().FullName).GetHashCode());
            }

            if (!frequencyAdmission.evaluated)
            {
                frequencyAdmission.decision = DecideFrequency(
                    frequencyAdmission.context,
                    bypassFrequency: false);
                frequencyAdmission.evaluated = true;
            }
            if (!frequencyAdmission.decision.Accepted)
            {
                CaptureFrequencyRejectedKnowledge(child, childPayload, childContext);
                try
                {
                    child.OnAcceptedEmissionCompleted(this, decision, false);
                }
                catch (Exception exception)
                {
                    Log.WarningOnce(
                        "[Pawn Diary] One fan-out child's frequency-rejected follow-up failed; "
                        + "the shared occurrence remains settled: " + exception,
                        ("PawnDiary.Fanout.FrequencyFollowUp."
                            + child.GetType().FullName + "."
                            + exception.GetType().FullName).GetHashCode());
                }
                // One semantically valid child proves the shared occurrence existed. Returning true
                // closes the colony key even though the profile deliberately emitted no pages.
                return true;
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
            try
            {
                child.OnAcceptedEmissionCompleted(this, decision, registrations > 0);
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Pawn Diary] One fan-out child's post-emission follow-up failed; its page "
                    + "and the shared occurrence remain recorded: " + exception,
                    ("PawnDiary.Fanout.PostEmissionFollowUp."
                        + child.GetType().FullName + "."
                        + exception.GetType().FullName).GetHashCode());
            }
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

        /// <summary>Mutable holder for the one lazily evaluated decision shared by all fan-out children.</summary>
        private sealed class FanoutFrequencyAdmission
        {
            public CaptureContext context;
            public DiaryFrequencyDecision decision;
            public bool evaluated;
        }

        /// <summary>
        /// Applies the selected frequency profile after semantic policy and dedup have accepted the
        /// source. Probabilistic candidates draw from the component's private evolving admission stream.
        /// </summary>
        private DiaryFrequencyDecision DecideFrequency(
            CaptureContext context,
            bool bypassFrequency)
        {
            if (bypassFrequency
                || context == null
                || context.BypassFrequency
                || string.IsNullOrWhiteSpace(context.FrequencyGroupKey))
            {
                return new DiaryFrequencyDecision
                {
                    reason = DiaryFrequencyDecisionReason.AcceptedBypass,
                    multiplier = DiaryFrequencyPolicy.StandardMultiplier,
                    effectiveChance = 1f
                };
            }

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            float playerOverride = DiaryFrequencyPolicy.StandardMultiplier;
            bool hasOverride = settings != null
                && settings.TryGetRuntimeGroupFrequencyOverride(
                    context.FrequencyGroupKey,
                    out playerOverride);
            DiaryFrequencyPresetSnapshot preset = settings?.RuntimeFrequencyPresetSnapshot();
            float multiplier = DiaryFrequencyPolicy.ResolveEffectiveMultiplier(
                preset,
                context.FrequencyGroupKey,
                context.FrequencyTier,
                hasOverride,
                playerOverride);

            float effectiveChance;
            bool validChance = DiaryFrequencyPolicy.TryCalculateEffectiveChance(
                context.NativeCaptureChance,
                multiplier,
                out effectiveChance);
            float roll = float.NaN;
            if (validChance && effectiveChance > 0f && effectiveChance < 1f)
            {
                roll = admissionRandom.NextUnitFloat();
            }
            else if (validChance)
            {
                // The pure policy still owns the boundary rule; these sentinels merely let a
                // deterministic 0/1 chance reach it without advancing even the isolated stream.
                roll = effectiveChance <= 0f ? 0f : 1f;
            }

            return DiaryFrequencyPolicy.Decide(new DiaryFrequencyRequest
            {
                groupKey = context.FrequencyGroupKey,
                frequencyTier = context.FrequencyTier,
                nativeCaptureChance = context.NativeCaptureChance,
                preset = preset,
                hasPlayerOverride = hasOverride,
                playerOverride = playerOverride,
                enabled = context.UserEnabled,
                bypassFrequency = false,
                roll = roll
            });
        }

        /// <summary>
        /// Applies the shared no-page knowledge policy for both central frequency rejection and an
        /// aggregate owner whose admission is deliberately deferred until after dispatch.
        /// </summary>
        internal void CaptureFrequencyRejectedKnowledge(
            DiarySignal signal,
            DiaryEventData payload,
            CaptureContext context)
        {
            CaptureRejectedKnowledge(
                signal,
                payload,
                context,
                DiaryKnowledgePageRejectionReason.Frequency,
                "dispatch.frequency_knowledge_only");
        }

        private void CapturePageRejectedKnowledge(
            DiarySignal signal,
            DiaryEventData payload,
            CaptureContext context,
            string telemetryStage)
        {
            CaptureRejectedKnowledge(
                signal,
                payload,
                context,
                DiaryKnowledgePageRejectionReason.PagePolicy,
                telemetryStage);
        }

        private void CaptureRejectedKnowledge(
            DiarySignal signal,
            DiaryEventData payload,
            CaptureContext context,
            DiaryKnowledgePageRejectionReason rejectionReason,
            string telemetryStage)
        {
            if (signal == null
                || !DiaryKnowledgeCapturePolicy.ShouldCaptureWithoutPage(
                    payload,
                    context,
                    rejectionReason))
            {
                return;
            }

            try
            {
                if (signal.CaptureKnowledgeWithoutPage(this))
                {
                    RecordSignalOutcome(
                        DiaryTelemetryOutcome.KnowledgeCapturedWithoutPage,
                        telemetryStage,
                        signal,
                        payload);
                }
            }
            catch (Exception exception)
            {
                // Knowledge projection is supplementary for every no-page lane. A broken adapter must
                // neither turn a semantic page rejection into a dispatch fault nor reopen an occurrence
                // whose frequency decision already settled, especially inside colony fan-outs.
                string reasonToken = rejectionReason == DiaryKnowledgePageRejectionReason.Frequency
                    ? "Frequency"
                    : "PagePolicy";
                Log.WarningOnce(
                    "[Pawn Diary] " + SignalTypeName(signal)
                    + " rejected-page knowledge capture failed; the page outcome remains isolated: "
                    + exception,
                    ("PawnDiary.Dispatch." + reasonToken + "Knowledge."
                        + SignalTypeName(signal) + "."
                        + exception.GetType().FullName).GetHashCode());
            }
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
                CompleteInitialArrivalBootstrap();
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
                CompleteInitialArrivalBootstrap();
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
