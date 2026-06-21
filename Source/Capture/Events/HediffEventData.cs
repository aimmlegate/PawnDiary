// Payload + pure decision for a "colonist gained or worsened a hediff" event (the
// Pawn_HealthTracker.AddHediff hook + the severity scanner). This is the sixth existing source
// migrated to the Event Catalog.
//
// Like Tale, this migration is intentionally PARTIAL. Hediff has two diary modes (Immediate and
// DayReflection) and a rich per-hediff policy (visibleOnly, badOnly, excludeInjuries, chronicAlways,
// minSeverity, severityStep, etc.). Most of those policy checks read RimWorld hediff state and so
// cannot move into a pure Decider cleanly without exploding the payload with ~10 boolean flags.
// This slice extracts only the basic drop-gate (defName/label/group/mode/source eligibility, plus
// a single "passes-basic-policy" boolean that the caller pre-computes from the richer policy) and
// the gameContext format. The Immediate-vs-DayReflection dispatch stays in RecordHediffSignal with
// a TODO for a future slice that extends CaptureDecision (e.g. add RouteDayReflection) or moves
// more of the policy evaluation into pure helpers.
//
// Even partial, this locks down the load-bearing parts: the source appears in the registry, the
// drop-gate is testable, and the hediff gameContext format (`hediff=<defName>; label=…; source=…;
// group=…; mode=…; severity=…; stage=…` with optional stage_label/body_part) is locked by tests.
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one hediff signal. Filled by DiaryGameComponent.RecordHediffSignal from
    /// the live Pawn + Hediff + the matched DiaryInteractionGroupDef's HediffSignalPolicy.
    /// </summary>
    public class HediffEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.Hediff;

        /// <summary>The hediff's defName (e.g. "Wound", "SurgeryComplication").</summary>
        public string DefName;

        /// <summary>The hediff's cleaned display label.</summary>
        public string Label;

        /// <summary>The signal source as a string token: "add" or "severity_progression". Matches
        /// the source= field in the gameContext string.</summary>
        public string SourceToken;

        /// <summary>The matched DiaryInteractionGroupDef defName (e.g. "hediff_injury").</summary>
        public string GroupKey;

        /// <summary>The diary mode as a string token: "Immediate" or "DayReflection". Matches the
        /// mode= field in the gameContext string.</summary>
        public string ModeToken;

        /// <summary>Hediff.Severity at signal time, pre-formatted as F2.</summary>
        public string SeverityF2;

        /// <summary>Severity-step stage index (Floor(Severity / severityStep)), pre-formatted.</summary>
        public string StageString;

        /// <summary>Optional cleaned stage label (hediff.CurStage.label), empty when none.</summary>
        public string CleanedStageLabel;

        /// <summary>Optional cleaned body-part label, empty when whole-body.</summary>
        public string CleanedBodyPartLabel;

        /// <summary>True when the hediff passes the per-source basic policy (visibleOnly /
        /// badOnly / excludeInjuries) AND the should-record gate (severity ≥ min, OR one of the
        /// *Always overrides for chronic / sick-thought / addiction / missing-part). Pre-computed
        /// by the caller because every check reads RimWorld hediff state.</summary>
        public bool PassesPolicy;

        /// <summary>True when the policy's source gate (recordOnAdd for Appeared, or
        /// recordOnSeverityIncrease for Progressed) is set. Pre-computed by the caller.</summary>
        public bool PolicyRecordsSource;

        /// <summary>True when the policy's mode is recordable in the current game state (Immediate
        /// always, DayReflection only when daySummaryEnabled is on). Pre-computed by the caller.</summary>
        public bool ModeRecordable;

        /// <summary>
        /// Pure drop-gate for a hediff signal. Returns Drop when ANY of: no defName, not eligible,
        /// user disabled the group, policy does not record this source, mode not recordable, hediff
        /// fails the basic policy / should-record gate. Otherwise returns GenerateSolo as a
        /// "continue processing" signal — the actual Immediate-vs-DayReflection dispatch stays in
        /// RecordHediffSignal because the current CaptureDecision contract has no DayReflection
        /// outcome. See file header TODO.
        /// </summary>
        public static CaptureDecision Decide(HediffEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (!data.PolicyRecordsSource || !data.ModeRecordable || !data.PassesPolicy)
            {
                return CaptureDecision.Drop;
            }

            // TODO(catalog): when CaptureDecision grows a RouteDayReflection value (or per-source
            // outcome enums), split Immediate vs DayReflection here. Until then RecordHediffSignal
            // reads data.ModeToken and dispatches to RecordImmediateHediffEvent or
            // RecordDayReflectionHediffSignal itself.
            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Pure assembly of the hediff game-context marker. The leading "hediff=" marker is load-
        /// bearing: the UI parses it to classify the event into the Hediff domain, and the LLM
        /// reads the rest as prompt evidence. Format locked by tests.
        /// </summary>
        public static string BuildGameContext(
            string defName, string cleanedLabel, string sourceToken, string groupKey,
            string modeToken, string severityF2, string stageString,
            string cleanedStageLabel, string cleanedBodyPartLabel)
        {
            string context = "hediff=" + defName
                + "; label=" + cleanedLabel
                + "; source=" + sourceToken
                + "; group=" + groupKey
                + "; mode=" + modeToken
                + "; severity=" + severityF2
                + "; stage=" + stageString;
            if (!string.IsNullOrWhiteSpace(cleanedStageLabel))
            {
                context += "; stage_label=" + cleanedStageLabel;
            }
            if (!string.IsNullOrWhiteSpace(cleanedBodyPartLabel))
            {
                context += "; body_part=" + cleanedBodyPartLabel;
            }
            return context;
        }
    }
}
