// The single ingestion pipeline. Every DiarySignal funnels through Dispatch here, which runs the
// universal steps that used to be copy-pasted into each RecordXxx method:
//   guard → dedup-check → build context → catalog Decide → dedup-mark → Emit.
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
// the pre-refactor ability path (which checked dedup before drawing Rand.Value) so a dropped
// duplicate no longer perturbs RimWorld's global RNG, and it skips pure Decide for deduped events.
// The MARK stays after Decide so an event the catalog drops (e.g. an ability that fails its chance
// roll) does not consume the dedup window.
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
        // source-prefixed key each signal supplies (e.g. "thought|pawnId|defName"). Each value also
        // remembers the source's OWN dedup window, so a prune sweep driven by a short-window source
        // cannot evict a still-live long-window key (see RecentEventExpiry). Not saved; cleared on
        // StartedNewGame/LoadedGame alongside the other transient state.
        private readonly Dictionary<string, RecentEventEntry> recentEvents = new Dictionary<string, RecentEventEntry>();

        /// <summary>
        /// Runs one captured event through the shared pipeline. Called by
        /// <see cref="DiaryEvents.Submit(DiarySignal)"/>. The solo path checks dedup before reading
        /// the payload, runs the pure catalog decision, then marks dedup immediately before the impure
        /// Emit (so a dropped event never consumes a window, and a deduped event never builds text or
        /// mutates the save).
        /// </summary>
        /// <returns>
        /// True if the signal passed the guard, decision, and dedup and its <c>Emit</c> ran. Most
        /// callers (the static <see cref="DiaryEvents.Submit(DiarySignal)"/> façade) ignore this, but a
        /// scanner whose own episode/staging state is coupled to whether the event recorded (e.g.
        /// ThoughtProgression's recorded-stage set) calls <c>Dispatch</c> directly and reads the result.
        /// </returns>
        internal bool Dispatch(DiarySignal signal)
        {
            if (signal == null || !CanRecordGameplayEventNow())
            {
                return false;
            }

            // Dedup CHECK first, before any impure payload work. Two reasons:
            //   1. It restores the pre-refactor ordering for sources whose old RecordXxx checked
            //      dedup before drawing impure state — notably Ability, which used to check dedup
            //      before its Rand.Value roll. Drawing the roll at capture time and only then
            //      deduping would consume RimWorld's global RNG even on a dropped duplicate.
            //   2. It skips BuildContext + Decide for a deduped event, which is pure win with no
            //      behavior change (Decide is side-effect-free).
            // The dedup MARK stays after Decide (below): an event that Decide drops (e.g. an ability
            // whose roll fails its cooldown-weighted chance) must not consume the dedup window.
            string key = signal.DedupKey;
            int windowTicks = signal.DedupWindowTicks;
            if (!string.IsNullOrEmpty(key)
                && IsRecentlyRecorded(recentEvents, key, windowTicks))
            {
                return false;
            }

            // Read the payload AFTER the dedup check. A null payload means the signal's capture
            // already decided to drop (missing/ineligible inputs, no matching policy), and its
            // BuildContext may deref state that was never set. This is the common path for sources
            // that submit for every candidate (e.g. a HediffSignal for a hediff with no diary group).
            // For Ability this read is also where the Rand.Value roll is drawn (lazily, post-dedup).
            DiaryEventData payload = signal.Payload;
            if (payload == null)
            {
                return false;
            }

            CaptureDecision decision;
            if (!TryDecide(payload, signal.BuildContext(), out decision))
            {
                return false;
            }

            // Dedup MARK after Decide, before the impure Emit — so a dropped event never consumes the
            // window, and a recorded one is marked exactly once on the path it actually emits on.
            if (!string.IsNullOrEmpty(key))
            {
                MarkRecentlyRecorded(recentEvents, key, windowTicks);
            }

            signal.Emit(this, decision);
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

                DiaryEventData childPayload = child.Payload;
                if (childPayload == null)
                {
                    continue;
                }

                CaptureDecision decision;
                if (!TryDecide(childPayload, child.BuildContext(), out decision))
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
