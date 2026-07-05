// The request object for direct diary-page injection through PawnDiaryApi.SubmitDirectEntry.
// Other mods use this when they already own the final prose and want Pawn Diary to save/display it
// without spending tokens on the main entry. This is PUBLIC integration surface: fields are only
// ever added, never renamed or removed.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using Verse;

namespace PawnDiary.Integration
{
    /// <summary>
    /// One caller-authored diary page. Required: <see cref="eventKey"/>, <see cref="subject"/>,
    /// and <see cref="text"/>. Optional partner text creates a partner POV when the partner is
    /// supplied and eligible.
    /// </summary>
    public class ExternalDirectEntryRequest
    {
        /// <summary>The submitting mod's package id. Used for logs and saved prompt/display context.</summary>
        public string sourceId;

        /// <summary>
        /// Stable classifier/tag for this kind of injected page. Lowercase, prefixed, and never
        /// renamed after release. If an External-domain group claims it, that group's label, toggle,
        /// and styling apply; otherwise the tag still records under the External domain.
        /// </summary>
        public string eventKey;

        /// <summary>The pawn whose diary receives <see cref="text"/>.</summary>
        public Pawn subject;

        /// <summary>Optional second pawn. Requires nonblank <see cref="partnerText"/> to create a second POV.</summary>
        public Pawn partner;

        /// <summary>Final diary prose for the subject POV. Required. Paragraph breaks are preserved.</summary>
        public string text;

        /// <summary>Optional final diary prose for the partner POV. Blank means the request records a solo entry.</summary>
        public string partnerText;

        /// <summary>Optional title for the subject POV. Blank leaves the date-only header unless title generation is requested.</summary>
        public string title;

        /// <summary>Optional title for the partner POV.</summary>
        public string partnerTitle;

        /// <summary>
        /// Optional factual fallback line for raw/debug context. Blank falls back to the first sentence
        /// of the injected prose, then to the standard external-event line.
        /// </summary>
        public string summaryText;

        /// <summary>Optional UI label. Blank falls back to the matching group label or eventKey.</summary>
        public string eventLabel;

        /// <summary>Optional extra saved context lines, each "key=value"; cleaned and capped like SubmitEvent.</summary>
        public List<string> extraContext;

        /// <summary>
        /// When true, this valid direct entry bypasses soft recording gates: the external API budget,
        /// group/user toggles, dedup windows, and per-pawn generation-enabled/incapacitated checks.
        /// It does not bypass required fields, main-thread/game readiness, the master integration
        /// toggle, or base diary-owner eligibility for the subject pawn.
        /// </summary>
        public bool forceRecord;

        /// <summary>Optional custom dedup key. Blank uses eventKey + pawn/pair.</summary>
        public string dedupKey;

        /// <summary>Optional dedup window override in game ticks. Values &lt;= 0 use XML default.</summary>
        public int dedupTicks;

        /// <summary>
        /// When true and a title is blank, Pawn Diary may queue its existing title-only LLM call if
        /// the player has title generation enabled and an API lane is configured. The main prose is
        /// still never sent for a full rewrite.
        /// </summary>
        public bool generateTitleIfMissing;
    }
}
