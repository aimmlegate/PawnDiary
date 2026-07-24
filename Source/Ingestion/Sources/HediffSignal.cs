// Hediff ingestion signal — the impure capture+emit half of the "health condition" source. Two
// capture paths feed it: the Pawn_HealthTracker.AddHediff hook (Appeared) and the periodic severity
// scan (Progressed). Replaces the shared DiaryGameComponent.RecordHediffSignal core that both called.
//
// The per-source helpers stay on the component (they read live Hediff state and feed the day-summary
// batcher); this signal is a thin orchestrator: capture builds the payload (and, for Appeared,
// establishes the progression baseline), the dispatcher decides + dedups, and Emit routes to the
// immediate entry or the day-reflection batch. The dedup key is tick/part-stamped, so it is computed
// by the component (HediffDedupKey) rather than the pure payload. Pure decision + game-context format
// live in Source/Capture/Events/HediffEventData.cs. New to C#/RimWorld? See AGENTS.md.
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Captures one hediff appearance or severity progression and emits an immediate entry or a
    /// day-reflection signal per the matched XML Hediff group. Built by <see cref="HealthTrackerAddHediffPatch"/>
    /// (Appeared) and the component's severity scan (Progressed).
    /// </summary>
    internal sealed class HediffSignal : DiarySignal
    {
        private readonly Pawn pawn;
        private readonly Hediff hediff;
        private readonly HediffSignalSource source;
        private readonly DiaryInteractionGroupDef group;
        private readonly HediffSignalPolicy policy;
        private readonly HediffEventData payload;

        public HediffSignal(Pawn pawn, Hediff hediff, HediffSignalSource source)
        {
            this.pawn = pawn;
            this.hediff = hediff;
            this.source = source;

            if (!DiaryGameComponent.GamePlaying || pawn == null || hediff == null || hediff.def == null
                || PawnDiaryMod.Settings == null)
            {
                return;
            }

            if (!DiaryGameComponent.IsDiaryEligible(pawn))
            {
                return;
            }

            // Missing-part hediffs can be created by the same hit that kills the pawn. Death pages own
            // that narrative, so living pawns get body-loss entries and dead pawns do not race them.
            if (pawn.Dead && DiaryGameComponent.IsMissingPartHediff(hediff))
            {
                return;
            }

            if (!DiaryGameComponent.TryGetHediffPolicy(hediff, out group, out policy))
            {
                return;
            }

            // AddHediff is the best moment to establish a progression baseline, even when the hediff is
            // still too mild to record. Later scanner passes can then detect a real worsen. (Appeared
            // only — matches the old RecordHediffSignal order, before the catalog decision.)
            if (source == HediffSignalSource.Appeared)
            {
                DiaryGameComponent.Instance?.RememberHediffProgressionState(pawn, hediff, policy);
            }

            payload = DiaryGameComponent.BuildHediffEventData(pawn, hediff, group, policy, source);
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: true,
                userEnabled: PawnDiaryMod.Settings.IsGroupEnabled(group.defName),
                signalEnabled: policy.enabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey =>
            payload != null ? DiaryGameComponent.HediffDedupKey(pawn, hediff, policy, source) : string.Empty;

        public override int DedupWindowTicks => policy != null ? System.Math.Max(0, policy.dedupTicks) : 0;

        public override void CaptureKnowledgeWithoutPage(DiaryGameComponent sink)
        {
            if (source == HediffSignalSource.Appeared && payload != null)
            {
                sink.CaptureEventKnowledgeWithoutPage(
                    pawn,
                    null,
                    payload.DefName,
                    sink.BuildHediffKnowledgeContext(hediff, payload),
                    payload.Tick);
            }
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision == CaptureDecision.GenerateSolo)
            {
                sink.RecordImmediateHediffEvent(pawn, hediff, group, policy, source, payload);
                return;
            }

            if (decision == CaptureDecision.RouteDayReflection)
            {
                sink.RecordDayReflectionHediffSignal(pawn, hediff, policy, source);
            }
        }
    }
}
