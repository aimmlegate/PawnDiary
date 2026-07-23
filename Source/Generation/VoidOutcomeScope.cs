// Bounded synchronous owner for the A3.0 terminal void answer. One VoidAwakeningUtility method frame
// may temporarily hold one already-built ordinary single-pawn Tale signal (EmbracedTheVoid /
// ClosedTheVoid) so it can be released unchanged unless a dedicated VoidOutcome page is actually
// created. The frame also remembers the exact monolith quest identity so the terminal event can own
// that one quest's success restatement. Lifecycle reset clears all frames; no frame is saved.
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;

namespace PawnDiary
{
    /// <summary>One closed terminal-void frame plus any generic Tale signals that must fail open.</summary>
    internal sealed class VoidOutcomeClose
    {
        internal bool matched;
        internal VoidOutcomeCapture capture;
        internal TaleSignal deferredTale;
        internal readonly List<TaleSignal> releaseTales = new List<TaleSignal>();
    }

    /// <summary>Tracks exact terminal-void ownership with bounded, exception-safe LIFO semantics.</summary>
    internal static class VoidOutcomeScope
    {
        private sealed class Frame
        {
            internal VoidOutcomeCapture capture;
            internal AnomalyVoidTaleClaim claim;
            internal TaleSignal deferredTale;
        }

        private static readonly List<Frame> Frames = new List<Frame>();

        /// <summary>True only while an exact terminal-void method owns the synchronous scope.</summary>
        internal static bool HasActiveFrame => Frames.Count > 0;

        /// <summary>Opens one exact terminal-void frame for the expected single-pawn Tale.</summary>
        internal static bool Begin(VoidOutcomeCapture capture, int maximumDepth)
        {
            if (capture?.facts == null || maximumDepth < 1 || Frames.Count >= maximumDepth)
                return false;
            string expectedTale = AnomalyVoidOutcomePolicy.ExpectedTaleDefName(capture.facts.outcome);
            if (expectedTale.Length == 0) return false;
            Frames.Add(new Frame
            {
                capture = capture,
                claim = new AnomalyVoidTaleClaim
                {
                    actorPawnId = capture.facts.actorPawnId,
                    expectedTaleDefName = expectedTale,
                    openedTick = capture.facts.tick,
                    active = true
                }
            });
            return true;
        }

        /// <summary>
        /// Returns true only when an active frame both captured this exact monolith quest id and
        /// expects to author a page, so a terminal event can own that quest's success restatement.
        /// A missing id, an unmatched id, or an ineligible actor leaves the generic quest page alone.
        /// </summary>
        internal static bool OwnsQuestId(int questId)
        {
            if (questId == 0) return false;
            for (int i = 0; i < Frames.Count; i++)
            {
                VoidOutcomeCapture capture = Frames[i].capture;
                if (capture != null && capture.suppressesQuestSuccess
                    && capture.monolithQuestId == questId) return true;
            }
            return false;
        }

        /// <summary>Defers at most one exact single-pawn terminal-void Tale for this frame.</summary>
        internal static bool TryDeferVoidTale(
            TaleDef def,
            object[] args,
            TaleSignal signal,
            int tick,
            int expiryTicks)
        {
            int last = Frames.Count - 1;
            if (last < 0 || signal == null || Frames[last].deferredTale != null) return false;
            VoidOutcomeCapture capture = Frames[last].capture;
            // Vanilla sets the terminal MonolithLevel before it records the Tale, so the live level
            // must already match this branch. This edge guard prevents an unrelated or mistimed void
            // Tale from being captured before the exact terminal state exists.
            string expectedLevel = AnomalyVoidOutcomePolicy.ExpectedLevelDefName(
                capture?.facts?.outcome);
            if (expectedLevel.Length == 0
                || !string.Equals(
                    DlcContext.CurrentAnomalyMonolithLevelDefName(), expectedLevel,
                    System.StringComparison.Ordinal)) return false;
            AnomalyVoidTaleFacts facts;
            if (!DlcContext.TryCaptureVoidTale(def, args, tick, out facts)
                || !AnomalyVoidTaleOwnershipPolicy.CanDefer(
                    Frames[last].claim, facts, expiryTicks)) return false;
            Frames[last].deferredTale = signal;
            return true;
        }

        /// <summary>Closes a normally returned method or releases every staged signal on mismatch.</summary>
        internal static VoidOutcomeClose Complete(VoidOutcomeCapture capture)
        {
            return Close(capture, normalReturn: true);
        }

        /// <summary>Aborts an exceptional method and releases its staged generic signal unchanged.</summary>
        internal static VoidOutcomeClose Abort(VoidOutcomeCapture capture)
        {
            return Close(capture, normalReturn: false);
        }

        private static VoidOutcomeClose Close(VoidOutcomeCapture capture, bool normalReturn)
        {
            VoidOutcomeClose result = new VoidOutcomeClose();
            int last = Frames.Count - 1;
            if (last >= 0 && object.ReferenceEquals(Frames[last].capture, capture))
            {
                Frame frame = Frames[last];
                Frames.RemoveAt(last);
                frame.claim.active = false;
                frame.capture.scopeClosed = true;
                result.matched = true;
                result.capture = frame.capture;
                if (normalReturn) result.deferredTale = frame.deferredTale;
                else if (frame.deferredTale != null) result.releaseTales.Add(frame.deferredTale);
                return result;
            }

            for (int i = 0; i < Frames.Count; i++)
            {
                Frames[i].claim.active = false;
                Frames[i].capture.scopeClosed = true;
                if (Frames[i].deferredTale != null)
                    result.releaseTales.Add(Frames[i].deferredTale);
            }
            Frames.Clear();
            if (capture != null) capture.scopeClosed = true;
            return result;
        }

        /// <summary>Clears process-static frames at every game lifecycle boundary.</summary>
        internal static void Clear()
        {
            Frames.Clear();
        }

        internal static int CountForTests => Frames.Count;
        internal static bool HasDeferredTaleForTests => Frames.Count > 0
            && Frames[Frames.Count - 1].deferredTale != null;
    }
}
