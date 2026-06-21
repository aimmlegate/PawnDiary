// Payload + pure decision for a "social interaction logged" event (the PlayLog.Add hook). This is
// the seventh existing source migrated to the Event Catalog.
//
// Like Tale and Hediff, this migration is intentionally PARTIAL. Interaction has three shapes
// (Solo when only one pawn is eligible / Pair when both eligible / Batched when the classified
// group has an XML batch policy and no promotion roll wins). Shape determination reads impure
// state (group classification, promotion RNG) and does not fit the current CaptureDecision
// contract cleanly. This slice extracts only the drop-gate (significance + eligibility +
// user-enabled) and locks the gameContext format. The solo-vs-pair-vs-batch dispatch stays in
// RecordInteraction with a TODO for a future slice.
//
// Even partial, this locks: the source appears in the registry, the drop-gate is testable, and
// the `interaction=<defName>; label=…` gameContext format is locked by tests.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one social interaction. Filled by DiaryGameComponent.RecordInteraction
    /// after the impure group classification + per-pawn eligibility checks run.
    /// </summary>
    public class InteractionEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.Interaction;

        /// <summary>The InteractionDef defName (e.g. "Insult", "Chat").</summary>
        public string DefName;

        /// <summary>The interaction's cleaned label.</summary>
        public string Label;

        /// <summary>The initiator pawn's id.</summary>
        public string InitiatorPawnId;

        /// <summary>The recipient pawn's id.</summary>
        public string RecipientPawnId;

        /// <summary>True when the initiator is diary-eligible.</summary>
        public bool InitiatorEligible;

        /// <summary>True when the recipient is diary-eligible.</summary>
        public bool RecipientEligible;

        /// <summary>True when the interaction's group is enabled AND classified as significant
        /// (i.e. not in the always-skip list). Pre-computed by the caller via
        /// IsInteractionSignificant.</summary>
        public bool IsSignificant;

        /// <summary>
        /// Pure drop-gate for an interaction event. Returns Drop when ANY of: no defName, neither
        /// pawn eligible, group not significant, user disabled. Otherwise returns GenerateSolo as a
        /// "continue processing" signal — the actual Solo/Pair/Batched dispatch stays in
        /// RecordInteraction. See file header TODO.
        /// </summary>
        public static CaptureDecision Decide(InteractionEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!data.IsSignificant || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (!data.InitiatorEligible && !data.RecipientEligible)
            {
                return CaptureDecision.Drop;
            }

            // TODO(catalog): when CaptureDecision grows per-source outcome enums (or a batched
            // value), move the Solo/Pair/Batched dispatch here. Until then RecordInteraction
            // re-runs the shape classification (one-eligible / batch-group / promote-roll /
            // pairwise) on the live pawns.
            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Pure assembly of the interaction game-context marker. The leading "def=" field and the
        /// optional worker/initiatorThought/recipientThought fields together let the UI recover the
        /// interaction's defName and the LLM see which thoughts the interaction granted. Mirrors the
        /// pre-refactor DiaryContextBuilder.BuildGameContextSummary format byte-for-byte. Inputs are
        /// pre-cleaned by the caller.
        /// </summary>
        public static string BuildGameContext(
            string defName, string cleanedLabel,
            string workerClassName, string cleanedInitiatorThoughtLabel, string cleanedRecipientThoughtLabel)
        {
            string context = "def=" + defName + "; label=" + cleanedLabel;
            if (!string.IsNullOrWhiteSpace(workerClassName))
            {
                context += "; worker=" + workerClassName;
            }
            if (!string.IsNullOrWhiteSpace(cleanedInitiatorThoughtLabel))
            {
                context += "; initiatorThought=" + cleanedInitiatorThoughtLabel;
            }
            if (!string.IsNullOrWhiteSpace(cleanedRecipientThoughtLabel))
            {
                context += "; recipientThought=" + cleanedRecipientThoughtLabel;
            }
            return context;
        }
    }
}
