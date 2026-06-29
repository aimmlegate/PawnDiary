// Payload + pure decision for a "social interaction logged" event (the PlayLog.Add hook). This is
// the seventh existing source migrated to the Event Catalog.
//
// Interaction has four shapes (Solo when only one pawn is eligible / Pair when both eligible /
// Batched when the classified group has a normal XML batch policy / Ambient when the group batches
// into per-pawn day notes). DiaryGameComponent pre-computes the impure group classification and
// promotion RNG result, then this pure Decide chooses the final outcome.
//
// This locks the source registry entry, the pure drop-gate, final routing, and the
// `interaction=<defName>; label=…` gameContext format with tests.
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

        /// <summary>True when this interaction should be routed to a normal delayed pair batch.
        /// Pre-computed by the caller after XML group classification and promotion RNG.</summary>
        public bool RouteToBatch;

        /// <summary>True when this interaction should be routed to the ambient day-note batcher.
        /// Pre-computed by the caller after XML group classification and promotion RNG.</summary>
        public bool RouteToAmbient;

        /// <summary>
        /// Pure decision for an interaction event. Returns Drop when ANY of: no defName, neither
        /// pawn eligible, group not significant, user disabled. Otherwise returns the final shape.
        /// </summary>
        public static CaptureDecision Decide(InteractionEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!data.IsSignificant || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (!data.InitiatorEligible && !data.RecipientEligible)
            {
                return CaptureDecision.Drop;
            }

            if (!data.InitiatorEligible || !data.RecipientEligible)
            {
                return CaptureDecision.GenerateSolo;
            }

            if (data.RouteToAmbient)
            {
                return CaptureDecision.RouteAmbient;
            }

            if (data.RouteToBatch)
            {
                return CaptureDecision.RouteBatch;
            }

            return CaptureDecision.GeneratePair;
        }

        /// <summary>The shape the impure interaction emit should build for a decision.</summary>
        public enum InteractionEmitShape { Drop, Solo, Pair, Batch }

        /// <summary>
        /// Pure routing for the impure Emit step: maps the catalog decision to the emit shape.
        /// RouteBatch and RouteAmbient both feed the same batch accumulator, so they collapse to
        /// Batch. Extracted so the routing — otherwise exercised only inside the RimWorld-coupled
        /// Emit — is unit-testable. Mirrors InteractionSignal.Emit; the solo POV pawn (initiator vs
        /// recipient) is a captured eligibility flag the signal applies, not part of this routing.
        /// </summary>
        public static InteractionEmitShape PlanEmit(CaptureDecision decision)
        {
            switch (decision)
            {
                case CaptureDecision.GenerateSolo:
                    return InteractionEmitShape.Solo;
                case CaptureDecision.GeneratePair:
                    return InteractionEmitShape.Pair;
                case CaptureDecision.RouteBatch:
                case CaptureDecision.RouteAmbient:
                    return InteractionEmitShape.Batch;
                default:
                    return InteractionEmitShape.Drop;
            }
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
