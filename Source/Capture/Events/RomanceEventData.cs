// Payload + pure decision for a "pawn romance relation changed" event (a new PawnRelationDef of
// type Lover/Spouse/ExLover/ExSpouse added via Pawn_RelationsTracker.AddDirectRelation).
//
// This is the FIRST source designed from scratch onto the Event Catalog (every other migrated
// source started as a pre-existing RecordX method). It proves the registry pattern handles
// net-new additions cleanly: pick a defName list, write the Decide + BuildGameContext helpers,
// register, done. No C#-side RimWorld introspection here — the hook filters to the four romance
// relations and the caller pre-computes both pawns' eligibility.
//
// The event is always a PAIR outcome (two pawns, both POV) — it reuses the GeneratePair value
// added by the MentalState migration. Future net-new pair sources (Raid pairs, etc.) work the
// same way.
using System;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one romance-relation change. Filled by
    /// DiaryGameComponent.RecordRomance from the live Pawn pair + the added PawnRelationDef.
    /// </summary>
    public class RomanceEventData : DiaryEventData
    {
        /// <summary>Kind token derived from the relation defName: "married" (Spouse),
        /// "lover" (Lover), "divorce" (ExSpouse), "breakup" (ExLover). Falls back to the raw
        /// defName for modded relation types that match the hook's filter list.</summary>
        public const string KindMarried = "married";
        public const string KindLover = "lover";
        public const string KindDivorce = "divorce";
        public const string KindBreakup = "breakup";

        public override DiaryEventType EventType => DiaryEventType.Romance;

        /// <summary>The relation defName (e.g. "Lover", "Spouse", "ExLover", "ExSpouse").</summary>
        public string DefName;

        /// <summary>The first pawn's id (the pawn whose tracker was called).</summary>
        public string FirstPawnId;

        /// <summary>The second pawn's id (the otherPawn argument).</summary>
        public string SecondPawnId;

        /// <summary>True when the first pawn is diary-eligible.</summary>
        public bool FirstEligible;

        /// <summary>True when the second pawn is diary-eligible.</summary>
        public bool SecondEligible;

        /// <summary>
        /// Pure decision for a romance event. Returns GeneratePair when both pawns are eligible
        /// (the relation change is mutual — a pairwise event with both POV entries is the right
        /// shape). Returns Drop when either pawn is ineligible, the user disabled the romance
        /// signal, or the data is incomplete.
        /// </summary>
        public static CaptureDecision Decide(RomanceEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.SignalEnabled || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            // Both pawns must be eligible — a romance event between a colonist and a non-colonist
            // (e.g. an animal, a visitor who left) is not diary-worthy as a pair event. The
            // caller still has the option to filter further upstream (the hook only forwards
            // romance relations, but a per-defName user toggle could live here later).
            if (!data.FirstEligible || !data.SecondEligible)
            {
                return CaptureDecision.Drop;
            }

            // Degenerate self-relation — the hook should never emit it, but the catalog guards.
            if (string.IsNullOrEmpty(data.SecondPawnId)
                || string.Equals(data.FirstPawnId, data.SecondPawnId, StringComparison.Ordinal))
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GeneratePair;
        }

        /// <summary>
        /// The transient dedup key for this romance event (raw, source-prefixed). Uses a canonical,
        /// order-independent pair key so the mirrored AddDirectRelation call from the other
        /// participant collapses to the same key. Lifted out of the old RecordRomance unchanged.
        /// </summary>
        public string DedupKey()
        {
            return "romance|" + CanonicalPairKey(FirstPawnId, SecondPawnId) + "|" + DefName;
        }

        /// <summary>
        /// Maps a relation defName to the short kind token embedded in gameContext. Modded
        /// relation defs that aren't one of the four vanilla ones fall back to the raw defName.
        /// </summary>
        public static string KindFor(string relationDefName)
        {
            if (string.Equals(relationDefName, "Spouse", StringComparison.OrdinalIgnoreCase))
            {
                return KindMarried;
            }
            if (string.Equals(relationDefName, "Lover", StringComparison.OrdinalIgnoreCase))
            {
                return KindLover;
            }
            if (string.Equals(relationDefName, "ExSpouse", StringComparison.OrdinalIgnoreCase))
            {
                return KindDivorce;
            }
            if (string.Equals(relationDefName, "ExLover", StringComparison.OrdinalIgnoreCase))
            {
                return KindBreakup;
            }
            return relationDefName ?? string.Empty;
        }

        /// <summary>
        /// Pure assembly of the romance game-context marker. The leading "romance=" marker is
        /// load-bearing: the UI parses it to classify the event into the Romance domain. The
        /// `kind=` field carries the short human-readable category for prompt selection.
        /// </summary>
        public static string BuildGameContext(string relationDefName, string cleanedLabel, string kindToken)
        {
            return "romance=" + relationDefName
                + "; label=" + cleanedLabel
                + "; kind=" + kindToken;
        }
    }
}
