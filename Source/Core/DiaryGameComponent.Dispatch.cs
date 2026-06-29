// The single ingestion pipeline. Every DiarySignal funnels through Dispatch here, which runs the
// universal steps that used to be copy-pasted into each RecordXxx method:
//   guard → build context → catalog Decide → dedup → Emit.
// The source-specific work (capturing live state into a payload, building text/context, queuing the
// LLM) stays in the per-source DiarySignal subclass. This file is the "one method, data-controlled"
// half the design asked for: the catalog Spec (XML-backed) decides, and Dispatch performs only the
// shared side effects.
//
// ── Consolidated dedup store ──
// Every source used to own its own transient Dictionary<string,int> (recentThoughtEvents,
// recentRaidEvents, …). Those keys are all source-prefixed ("thought|…", "raid|…"), so they never
// collide across sources and can share ONE dictionary with no behavior change. `recentEvents` below
// is that single store; it is transient (never saved) and pruned by the same window logic as before.
//
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // The single transient dedup store shared by every migrated source. Keyed by the raw
        // source-prefixed key each signal supplies (e.g. "thought|pawnId|defName"). Not saved; cleared
        // on StartedNewGame/LoadedGame alongside the other transient state.
        private readonly Dictionary<string, int> recentEvents = new Dictionary<string, int>();

        /// <summary>
        /// Runs one captured event through the shared pipeline. Called by
        /// <see cref="DiaryEvents.Submit(DiarySignal)"/>. Order matches the pre-refactor RecordXxx
        /// methods exactly: gameplay-time guard, pure Decide, then dedup immediately before the impure
        /// Emit (so a dropped or deduped event never builds text or mutates the save).
        /// </summary>
        internal void Dispatch(DiarySignal signal)
        {
            if (signal == null || !CanRecordGameplayEventNow())
            {
                return;
            }

            CaptureDecision decision;
            if (!TryDecide(signal.Payload, signal.BuildContext(), out decision))
            {
                return;
            }

            // Dedup AFTER the decision, BEFORE the impure build — same ordering as before the refactor.
            string key = signal.DedupKey;
            if (!string.IsNullOrEmpty(key)
                && RecentlyRecorded(recentEvents, key, signal.DedupWindowTicks))
            {
                return;
            }

            signal.Emit(this, decision);
        }

        /// <summary>
        /// Runs a colony-wide fan-out. Peeks the colony dedup window first; iterates the per-pawn
        /// signals (already filtered to eligible colonists) through the same Decide → per-pawn dedup →
        /// Emit path; and marks the colony key only after at least one entry was emitted, so an empty
        /// colonist list cannot consume the whole window (matching the old RecordRaid/RecordMoodEvent).
        /// </summary>
        internal void Dispatch(DiaryFanoutSignal signal)
        {
            if (signal == null || !CanRecordGameplayEventNow())
            {
                return;
            }

            string colonyKey = signal.ColonyDedupKey;
            int colonyTicks = signal.ColonyDedupTicks;
            if (!string.IsNullOrEmpty(colonyKey)
                && IsRecentlyRecorded(recentEvents, colonyKey, colonyTicks))
            {
                return;
            }

            bool emittedAny = false;
            foreach (DiarySignal child in signal.PerPawnSignals())
            {
                if (child == null)
                {
                    continue;
                }

                CaptureDecision decision;
                if (!TryDecide(child.Payload, child.BuildContext(), out decision))
                {
                    continue;
                }

                // Most fan-outs dedup only at the colony level (child.DedupKey empty); a child may add
                // its own per-pawn window if it needs one.
                string childKey = child.DedupKey;
                if (!string.IsNullOrEmpty(childKey)
                    && RecentlyRecorded(recentEvents, childKey, child.DedupWindowTicks))
                {
                    continue;
                }

                child.Emit(this, decision);
                emittedAny = true;
            }

            if (emittedAny && !string.IsNullOrEmpty(colonyKey))
            {
                MarkRecentlyRecorded(recentEvents, colonyKey, colonyTicks);
            }
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
        private static bool TryDecide(DiaryEventData payload, CaptureContext ctx, out CaptureDecision decision)
        {
            decision = CaptureDecision.Drop;
            if (payload == null)
            {
                return false;
            }

            DiaryEventSpec spec = DiaryEventCatalog.Get(payload.EventType);
            if (spec == null)
            {
                return false;
            }

            decision = spec.Decide(payload, ctx);
            return decision != CaptureDecision.Drop;
        }
    }
}
