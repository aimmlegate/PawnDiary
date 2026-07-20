// Bounded synchronous owner for A2.1 surgical inspection. One recipe frame may temporarily hold one
// already-built ordinary TaleSignal so it can be released unchanged unless a dedicated disclosure
// event is actually created. Lifecycle reset clears all frames; no frame is saved.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;

namespace PawnDiary
{
    /// <summary>One closed recipe plus any generic signals that must fail open.</summary>
    internal sealed class CreepJoinerSurgicalInspectionClose
    {
        internal bool matched;
        internal CreepJoinerSurgicalInspectionCapture capture;
        internal TaleSignal deferredTale;
        internal readonly List<TaleSignal> releaseTales = new List<TaleSignal>();
    }

    /// <summary>Tracks exact nested recipe ownership with bounded, exception-safe LIFO semantics.</summary>
    internal static class CreepJoinerSurgicalInspectionScope
    {
        private sealed class Frame
        {
            internal CreepJoinerSurgicalInspectionCapture capture;
            internal CreepJoinerSurgeryTaleClaim claim;
            internal TaleSignal deferredTale;
        }

        private static readonly List<Frame> Frames = new List<Frame>();

        /// <summary>Opens one exact outer recipe frame, respecting the normalized XML depth cap.</summary>
        internal static bool Begin(CreepJoinerSurgicalInspectionCapture capture, int maximumDepth)
        {
            if (capture?.facts == null || maximumDepth < 1 || Frames.Count >= maximumDepth)
                return false;
            Frames.Add(new Frame
            {
                capture = capture,
                claim = new CreepJoinerSurgeryTaleClaim
                {
                    subjectPawnId = capture.facts.subjectPawnId,
                    surgeonPawnId = capture.facts.surgeonPawnId,
                    openedTick = capture.facts.tick,
                    active = true
                }
            });
            return true;
        }

        /// <summary>Returns the innermost active recipe for tracker/Pawn-result correlation.</summary>
        internal static CreepJoinerSurgicalInspectionCapture Current
        {
            get
            {
                int last = Frames.Count - 1;
                return last < 0 ? null : Frames[last].capture;
            }
        }

        /// <summary>
        /// Defers at most one exact matching DidSurgery signal. Mismatches/expiry return false so the
        /// caller immediately dispatches the ordinary signal through its mature route.
        /// </summary>
        internal static bool TryDeferDidSurgery(
            TaleDef def,
            object[] args,
            TaleSignal signal,
            int tick,
            int expiryTicks)
        {
            int last = Frames.Count - 1;
            if (last < 0 || signal == null || Frames[last].deferredTale != null) return false;
            CreepJoinerSurgeryTaleFacts facts;
            if (!DlcContext.TryCaptureCreepJoinerSurgeryTale(def, args, tick, out facts)
                || !CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(
                    Frames[last].claim, facts, expiryTicks)) return false;
            Frames[last].deferredTale = signal;
            return true;
        }

        /// <summary>Closes a normally returned recipe or releases every staged signal on mismatch.</summary>
        internal static CreepJoinerSurgicalInspectionClose Complete(
            CreepJoinerSurgicalInspectionCapture capture)
        {
            return Close(capture, normalReturn: true);
        }

        /// <summary>Aborts an exceptional recipe and releases its staged generic signal unchanged.</summary>
        internal static CreepJoinerSurgicalInspectionClose Abort(
            CreepJoinerSurgicalInspectionCapture capture)
        {
            return Close(capture, normalReturn: false);
        }

        private static CreepJoinerSurgicalInspectionClose Close(
            CreepJoinerSurgicalInspectionCapture capture,
            bool normalReturn)
        {
            CreepJoinerSurgicalInspectionClose result = new CreepJoinerSurgicalInspectionClose();
            int last = Frames.Count - 1;
            if (last >= 0 && ReferenceEquals(Frames[last].capture, capture))
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

            // Unusual modded reentrancy or a mismatched unwind invalidates ownership. Release every
            // pending ordinary signal and clear all frames so nothing can leak into a later surgery.
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
