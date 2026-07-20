// Bounded synchronous owner for A2.2 ghoul infusion. One recipe frame may temporarily hold one
// already-built ordinary TaleSignal so it can be released unchanged unless a dedicated ghoul event
// is actually created. Lifecycle reset clears all frames; no frame is saved.
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;

namespace PawnDiary
{
    /// <summary>One closed ghoul recipe plus any generic signals that must fail open.</summary>
    internal sealed class GhoulInfusionClose
    {
        internal bool matched;
        internal GhoulTransformationCapture capture;
        internal TaleSignal deferredTale;
        internal readonly List<TaleSignal> releaseTales = new List<TaleSignal>();
    }

    /// <summary>Tracks exact nested ghoul-recipe ownership with bounded, exception-safe LIFO semantics.</summary>
    internal static class GhoulInfusionScope
    {
        private sealed class Frame
        {
            internal GhoulTransformationCapture capture;
            internal AnomalySurgeryTaleClaim claim;
            internal TaleSignal deferredTale;
        }

        private static readonly List<Frame> Frames = new List<Frame>();

        /// <summary>True only while an exact ghoul infusion owns the synchronous scope.</summary>
        internal static bool HasActiveFrame => Frames.Count > 0;

        /// <summary>Opens one exact recipe frame without overlapping A2.1 Tale ownership.</summary>
        internal static bool Begin(GhoulTransformationCapture capture, int maximumDepth)
        {
            if (capture?.facts == null || maximumDepth < 1 || Frames.Count >= maximumDepth
                || CreepJoinerSurgicalInspectionScope.HasActiveFrame) return false;
            Frames.Add(new Frame
            {
                capture = capture,
                claim = new AnomalySurgeryTaleClaim
                {
                    subjectPawnId = capture.facts.subjectPawnId,
                    surgeonPawnId = capture.facts.surgeonPawnId,
                    openedTick = capture.facts.tick,
                    active = true
                }
            });
            return true;
        }

        /// <summary>Defers at most one exact surgeon-first/subject-second DidSurgery signal.</summary>
        internal static bool TryDeferDidSurgery(
            TaleDef def,
            object[] args,
            TaleSignal signal,
            int tick,
            int expiryTicks)
        {
            int last = Frames.Count - 1;
            if (last < 0 || signal == null || Frames[last].deferredTale != null) return false;
            // Installed vanilla records DidSurgery only after setting the subject to a ghoul. This
            // live-edge guard prevents an unrelated nested surgery with the same role IDs from being
            // mistaken for the infusion's Tale before the exact post-state exists.
            if (Frames[last].capture.facts.wasGhoul
                || !DlcContext.IsGhoul(Frames[last].capture.subject)) return false;
            AnomalySurgeryTaleFacts facts;
            if (!DlcContext.TryCaptureAnomalySurgeryTale(def, args, tick, out facts)
                || !AnomalySurgeryTaleOwnershipPolicy.CanDefer(
                    Frames[last].claim, facts, expiryTicks)) return false;
            Frames[last].deferredTale = signal;
            return true;
        }

        /// <summary>Closes a normally returned recipe or releases every staged signal on mismatch.</summary>
        internal static GhoulInfusionClose Complete(GhoulTransformationCapture capture)
        {
            return Close(capture, normalReturn: true);
        }

        /// <summary>Aborts an exceptional recipe and releases its staged generic signal unchanged.</summary>
        internal static GhoulInfusionClose Abort(GhoulTransformationCapture capture)
        {
            return Close(capture, normalReturn: false);
        }

        private static GhoulInfusionClose Close(
            GhoulTransformationCapture capture,
            bool normalReturn)
        {
            GhoulInfusionClose result = new GhoulInfusionClose();
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
