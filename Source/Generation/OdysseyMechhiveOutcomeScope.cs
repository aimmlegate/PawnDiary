// Bounded synchronous owner for Odyssey O3's exact Cerebrex-core resolution. Vanilla ends the
// Mechhive quest near the start of DeactivateCore, before Pawn Diary's postfix can verify the final
// destroy/scavenge state. This scope parks that one already-built generic Quest signal and releases it
// unchanged unless the dedicated operator-authored ending page verifiably exists.
using System.Collections.Generic;
using PawnDiary.Ingestion;

namespace PawnDiary
{
    /// <summary>One closed Mechhive frame plus generic Quest signals that must fail open.</summary>
    internal sealed class OdysseyMechhiveOutcomeClose
    {
        internal bool matched;
        internal OdysseyMechhiveOutcomeCapture capture;
        internal QuestFanoutSignal deferredQuest;
        internal readonly List<QuestFanoutSignal> releaseQuests =
            new List<QuestFanoutSignal>();
    }

    /// <summary>Tracks exact Mechhive Quest-page ownership with bounded exception-safe LIFO scope.</summary>
    internal static class OdysseyMechhiveOutcomeScope
    {
        private sealed class Frame
        {
            internal OdysseyMechhiveOutcomeCapture capture;
            internal QuestFanoutSignal deferredQuest;
        }

        private static readonly List<Frame> Frames = new List<Frame>();

        internal static bool HasActiveFrame => Frames.Count > 0;

        /// <summary>Opens one exact core/quest frame; malformed identities never own a Quest page.</summary>
        internal static bool Begin(
            OdysseyMechhiveOutcomeCapture capture,
            int maximumDepth)
        {
            if (capture?.facts == null || capture.facts.questId <= 0
                || maximumDepth < 1 || Frames.Count >= maximumDepth)
            {
                return false;
            }
            Frames.Add(new Frame { capture = capture });
            return true;
        }

        /// <summary>
        /// Parks the exact predicted Mechhive success once. Other quests, failures, disabled output,
        /// and duplicate fan-outs remain on the normal route.
        /// </summary>
        internal static bool TryDeferQuestSuccess(int questId, QuestFanoutSignal signal)
        {
            int last = Frames.Count - 1;
            if (last < 0 || questId <= 0 || signal == null
                || Frames[last].deferredQuest != null)
            {
                return false;
            }
            OdysseyMechhiveOutcomeCapture capture = Frames[last].capture;
            if (capture == null || !capture.suppressesQuestSuccess
                || capture.facts?.questId != questId)
            {
                return false;
            }
            Frames[last].deferredQuest = signal;
            return true;
        }

        internal static OdysseyMechhiveOutcomeClose Complete(
            OdysseyMechhiveOutcomeCapture capture)
        {
            return Close(capture, normalReturn: true);
        }

        internal static OdysseyMechhiveOutcomeClose Abort(
            OdysseyMechhiveOutcomeCapture capture)
        {
            return Close(capture, normalReturn: false);
        }

        private static OdysseyMechhiveOutcomeClose Close(
            OdysseyMechhiveOutcomeCapture capture,
            bool normalReturn)
        {
            OdysseyMechhiveOutcomeClose result = new OdysseyMechhiveOutcomeClose();
            int last = Frames.Count - 1;
            if (last >= 0 && object.ReferenceEquals(Frames[last].capture, capture))
            {
                Frame frame = Frames[last];
                Frames.RemoveAt(last);
                frame.capture.scopeClosed = true;
                result.matched = true;
                result.capture = frame.capture;
                if (normalReturn) result.deferredQuest = frame.deferredQuest;
                else if (frame.deferredQuest != null)
                    result.releaseQuests.Add(frame.deferredQuest);
                return result;
            }

            for (int i = 0; i < Frames.Count; i++)
            {
                Frames[i].capture.scopeClosed = true;
                if (Frames[i].deferredQuest != null)
                    result.releaseQuests.Add(Frames[i].deferredQuest);
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
        internal static bool HasDeferredQuestForTests => Frames.Count > 0
            && Frames[Frames.Count - 1].deferredQuest != null;
    }
}
