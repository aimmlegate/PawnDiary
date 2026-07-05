// Public result DTO for integration adapters that need to track a submitted entry after creation.
// It contains stable handles only; prompts, raw provider responses, and live RimWorld objects never
// cross the API boundary.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Result returned by <see cref="PawnDiaryApi.SubmitEventWithHandle(ExternalEventRequest)"/>.
    /// </summary>
    public sealed class DiaryEventSubmissionResult
    {
        /// <summary>Submitting mod id copied from the request for adapter-side correlation.</summary>
        public string sourceId = string.Empty;

        /// <summary>External event key copied from the request for adapter-side correlation.</summary>
        public string eventKey = string.Empty;

        /// <summary>True only when the diary pipeline actually created an entry.</summary>
        public bool recorded;

        /// <summary>True when the created entry has both subject and partner POVs.</summary>
        public bool pairwise;

        /// <summary>Handle for the request subject's POV. Null when <see cref="recorded"/> is false.</summary>
        public DiaryEntryHandle primary;

        /// <summary>Handle for the partner's POV on pairwise entries; null for solo entries.</summary>
        public DiaryEntryHandle partner;
    }
}
