// Payload + pure decision for "a sculpture immortalized a colony deed" (Quality Wave H6, the
// CompArt art-generation hook). One quiet solo page per deed, written by the pawn the deed is about
// when they can still write, and otherwise by the artist.
//
// The writer choice and the once-per-deed sampling live in Source/Pipeline/ArtImmortalizationPolicy.cs;
// this file only carries the frozen facts and the catalog gate. Ownership is exact: the dedup key is
// built from the tale's stable identity, so a second sculpture about the same deed is dropped before
// it can create a page — in any language.
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one artwork that immortalized a colony deed. Filled by the art patch after
    /// the pure policy has already chosen the writer.
    /// </summary>
    internal class ArtImmortalizedEventData : DiaryEventData
    {
        /// <summary>Synthetic source defName the XML interaction group matches on.</summary>
        public const string ArtImmortalizedDefName = "PawnDiary_ArtImmortalized";

        public override DiaryEventType EventType => DiaryEventType.ArtImmortalized;

        /// <summary>The sculpture's ThingDef name (e.g. "SculptureLarge").</summary>
        public string ArtDefName;

        /// <summary>The sculpture's stable load ID, so repeat initialization is recognizable.</summary>
        public string ArtThingId;

        /// <summary>The artwork's cleaned title, pre-resolved by the caller.</summary>
        public string ArtTitle;

        /// <summary>The artist's display nickname, or empty when unknown.</summary>
        public string SculptorName;

        /// <summary>The tale's def name, carried for prompt policy.</summary>
        public string TaleDefName;

        /// <summary>
        /// The deed's exact stable identity ("TaleDefName#id"). Verified by the policy before this
        /// payload is built; an unverifiable identity never reaches here.
        /// </summary>
        public string TaleIdentity;

        /// <summary>
        /// Pure decision. Eligibility and the user's group toggle are the gates, plus a defensive
        /// re-check that the deed identity is still exact — ownership must never be claimed on a
        /// key the dedup store cannot reproduce.
        /// </summary>
        public static CaptureDecision Decide(ArtImmortalizedEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (string.IsNullOrWhiteSpace(data.PawnId)
                || !ArtImmortalizationPolicy.IsVerifiedTaleIdentity(data.TaleIdentity))
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// The exact-ownership key. The dispatcher's dedup store holds it for the configured window,
        /// but the durable guarantee is the hot/archive ownership query the patch runs first: this key
        /// only stops two sculptures finished in the same moment.
        /// </summary>
        public string DedupKey()
        {
            return ArtImmortalizationPolicy.OwnershipKey(TaleIdentity);
        }

        /// <summary>
        /// Pure assembly of the game-context marker. The leading "art=" is load-bearing: it is
        /// deliberately NOT a registered domain marker, so the event classifies into the Interaction
        /// domain its XML group declares. The tale def name uses "art_tale=" rather than "tale=" for
        /// the same reason in reverse — "tale=" IS the Tale-domain marker and would misfile the page.
        /// Field order is locked by tests in DiaryCapturePolicyTests.
        /// </summary>
        public static string BuildGameContext(
            string artDefName, string artThingId, string title, string sculptorName,
            string taleDefName, string taleIdentity)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, ArtImmortalizationPolicy.ArtContextKey, artDefName);
            Append(builder, ArtImmortalizationPolicy.ArtThingIdContextKey, artThingId);
            Append(builder, ArtImmortalizationPolicy.LabelContextKey, title);
            Append(builder, ArtImmortalizationPolicy.SculptorContextKey, sculptorName);
            Append(builder, ArtImmortalizationPolicy.TaleDefContextKey, taleDefName);
            Append(builder, ArtImmortalizationPolicy.TaleIdContextKey, taleIdentity);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string key, string value)
        {
            string cleaned = Sanitize(value);
            if (cleaned.Length == 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(key).Append("=").Append(cleaned);
        }

        /// <summary>
        /// Keeps a value safe inside the semicolon/equals-delimited context string. An artwork title
        /// is player- or generator-authored prose, so it really can contain either character.
        /// </summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace(';', ',')
                .Replace('=', '-')
                .Trim();
        }
    }
}
