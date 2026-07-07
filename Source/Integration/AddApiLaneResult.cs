// Public result DTO returned by PawnDiaryApi.AddApiLane. Like every other integration entry point,
// AddApiLane never throws; it reports what happened here instead. Keep this class plain: fields only.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Outcome of a <see cref="PawnDiaryApi.AddApiLane"/> call.
    /// </summary>
    public sealed class AddApiLaneResult
    {
        /// <summary>True when a new lane row was appended and persisted.</summary>
        public bool added;

        /// <summary>True when the request matched an existing lane and no new row was added (only
        /// possible when the request left avoidDuplicate on). <see cref="index"/> points at the match.</summary>
        public bool alreadyExisted;

        /// <summary>Zero-based index of the new lane (or the matched existing lane), or -1 on failure.</summary>
        public int index = -1;

        /// <summary>Whether the resulting lane participates in generation now (enabled + url + model).</summary>
        public bool active;

        /// <summary>
        /// Stable reason token explaining the outcome:
        /// "added", "duplicate", "missingUrl", "missingModel", "invalidRequest", "offThread",
        /// or "ineligible" (no settings / master integration toggle off).
        /// </summary>
        public string reason = string.Empty;
    }
}
