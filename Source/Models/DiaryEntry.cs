// Small view models for diary text. DiaryEntryView is the read-only display model the UI renders
// (text + status + display hints), and LinkedEntryView previews the other pawn's POV for pairwise
// events.
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Lightweight, read-only preview of the OTHER pawn's diary entry for the same event.
    /// Shown as a clickable "linked entry" card inside the current pawn's diary — recipient
    /// link appears after the initiator's main text, initiator link appears before the
    /// recipient's main text. Clicking navigates to that pawn and scrolls to the same event.
    /// </summary>
    public class LinkedEntryView
    {
        /// <summary>RimWorld unique load ID of the other pawn involved in this event.</summary>
        public readonly string OtherPawnId;
        /// <summary>Display name of the other pawn (e.g. "Alice").</summary>
        public readonly string OtherPawnName;
        /// <summary>POV role of the other pawn in this event ("initiator" or "recipient").</summary>
        public readonly string OtherRole;
        /// <summary>The eventId shared by both entries — used to scroll after navigating.</summary>
        public readonly string EventId;
        /// <summary>Truncated preview of the other pawn's generated text (first ~120 chars + ellipsis).</summary>
        public readonly string TruncatedText;
        /// <summary>Whether the other pawn's entry has finished LLM generation.</summary>
        public readonly bool Generated;
        /// <summary>Short chat-style subject for the other pawn's entry. Empty when no LLM title has been stored.</summary>
        public readonly string Title;

        public LinkedEntryView(
            string otherPawnId,
            string otherPawnName,
            string otherRole,
            string eventId,
            string truncatedText,
            bool generated,
            string title = null)
        {
            OtherPawnId = otherPawnId;
            OtherPawnName = otherPawnName;
            OtherRole = otherRole;
            EventId = eventId;
            TruncatedText = truncatedText;
            Generated = generated;
            Title = title ?? string.Empty;
        }
    }

    /// <summary>
    /// Immutable, read-only display model rendered by the diary UI tab.
    /// Represents a single POV snapshot of a diary event.
    /// </summary>
    public class DiaryEntryView
    {
        public const string AtmosphereFractured = "fractured";
        public const string AtmosphereUnsettled = "unsettled";
        public const string AtmosphereMemorial = "memorial";

        public readonly int Tick;           // Game tick when the event occurred
        public readonly string Date;        // Human-readable date string
        public readonly string Text;        // Raw game-authored event text
        public readonly string GeneratedText; // LLM-generated narrative text
        public readonly string LlmStatus;   // LLM generation status ("pending", "failed", "prompt_only", etc.)
        public readonly string LlmError;    // Error message if LLM generation failed
        public readonly string LlmEndpoint; // API endpoint used for the LLM call
        public readonly string LlmModel;    // LLM model identifier
        public readonly string LlmPrompt;  // Full prompt sent to the LLM
        public readonly string LlmRawResponse; // Final-answer text before local length/sentence cleanup
        public readonly string EventId;     // Identifier of the backing DiaryEvent
        public readonly string PovRole;     // Role/perspective this view represents.
        // Stable UI/cache key for this displayed POV. Built once because the Diary tab asks for it
        // several times per visible entry every draw frame.
        public readonly string EntryKey;
        public readonly string GroupLabel;  // Human-readable event group shown in the entry header
        public readonly string ColorCue;    // Stable semantic cue used for the card's color strip/chip
        // Rare display-only formatting cue used by the Diary tab. Empty means ordinary prose layout.
        public readonly string AtmosphereCue;
        // Display-only severity for variable-size "staggered" words caused by low Consciousness or
        // intoxication at record time. Zero means no staggered words.
        public readonly int StaggeredIntensity;
        // True only for the initiator POV of strange anomaly chat. The Diary tab uses it to add a
        // dramatic distortion to quoted direct speech without changing the saved generated text.
        // Kept for compatibility; current display decorations use TextDecorationContext + XML rules.
        public readonly bool DistortDirectSpeech;
        // Plain saved facts and event metadata used by XML diary text-decoration rules.
        public readonly DiaryTextDecorationContext TextDecorationContext;
        public readonly bool Important;     // Visual importance marker derived from the event group
        public readonly LinkedEntryView LinkedEntry; // Preview of the other pawn's entry for the same event (null for solo).
        // Short chat-style subject: stored LLM-generated title only. Empty when no title has
        // been generated or title generation is disabled.
        public readonly string Title;
        // True while the separate title follow-up is still queued/running and no stored title has
        // arrived yet. The Diary tab uses this to animate the header without pretending the title
        // already exists.
        public readonly bool TitlePending;
        // True when this POV has an attempted but unfinished generation and its event has fallen out of
        // the active scan window. After load, stale pending work is normalized back to not_generated, so
        // a saved prompt also counts as proof that the archived page should render a fallback.
        public readonly bool ArchivedGenerationStale;

        public DiaryEntryView(
            int tick,
            string date,
            string text,
            string generatedText,
            string llmStatus,
            string llmError,
            string llmEndpoint,
            string llmModel,
            string llmPrompt,
            string eventId,
            string povRole,
            string groupLabel,
            string colorCue,
            string atmosphereCue,
            int staggeredIntensity,
            bool distortDirectSpeech,
            bool important,
            LinkedEntryView linkedEntry = null,
            string title = null,
            bool titlePending = false,
            string llmRawResponse = null,
            DiaryTextDecorationContext textDecorationContext = null,
            bool archivedGenerationStale = false)
        {
            Tick = tick;
            Date = date;
            Text = text;
            GeneratedText = generatedText;
            LlmStatus = llmStatus;
            LlmError = llmError;
            LlmEndpoint = llmEndpoint;
            LlmModel = llmModel;
            LlmPrompt = llmPrompt;
            LlmRawResponse = llmRawResponse;
            EventId = eventId;
            PovRole = povRole;
            EntryKey = BuildEntryKey(eventId, povRole, date, tick);
            GroupLabel = groupLabel;
            ColorCue = colorCue ?? string.Empty;
            AtmosphereCue = atmosphereCue ?? string.Empty;
            StaggeredIntensity = staggeredIntensity < 0 ? 0 : (staggeredIntensity > 4 ? 4 : staggeredIntensity);
            DistortDirectSpeech = distortDirectSpeech;
            TextDecorationContext = textDecorationContext;
            Important = important;
            LinkedEntry = linkedEntry;
            Title = title ?? string.Empty;
            TitlePending = titlePending;
            ArchivedGenerationStale = archivedGenerationStale;
        }

        /// <summary>
        /// Builds the per-entry UI key used for expansion state, animation, and fade caches.
        /// Event id plus POV role is stable for saved entries; the fallback keeps damaged entries
        /// clickable without throwing.
        /// </summary>
        private static string BuildEntryKey(string eventId, string povRole, string date, int tick)
        {
            string eventPart = string.IsNullOrWhiteSpace(eventId)
                ? ((date ?? string.Empty) + "|" + tick)
                : eventId;

            return eventPart + "|" + (povRole ?? string.Empty);
        }

        /// <summary>
        /// Returns "writing..." while pending with no output, generatedText if available,
        /// otherwise falls back to raw Text.
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (LlmStatus == DiaryEvent.PendingStatus && string.IsNullOrWhiteSpace(GeneratedText))
                {
                    return "PawnDiary.Status.WritingEllipsis".Translate();
                }

                if (!string.IsNullOrWhiteSpace(GeneratedText))
                {
                    return GeneratedText;
                }

                return Text ?? string.Empty;
            }
        }

        /// <summary>
        /// Human-readable status label: "writing" while pending, "generation failed" on error, empty otherwise.
        /// </summary>
        public string StatusText
        {
            get
            {
                if (ArchivedGenerationStale)
                {
                    return "PawnDiary.Status.GenerationFailed".Translate();
                }

                if (LlmStatus == DiaryEvent.PendingStatus)
                {
                    return "PawnDiary.Status.Writing".Translate();
                }

                if (LlmStatus == DiaryEvent.FailedStatus)
                {
                    return "PawnDiary.Status.GenerationFailed".Translate();
                }

                if (LlmStatus == DiaryEvent.SkippedStatus)
                {
                    return "PawnDiary.Status.Skipped".Translate();
                }

                if (LlmStatus == DiaryEvent.PromptOnlyStatus)
                {
                    return "PawnDiary.Status.PromptOnly".Translate();
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Diagnostic block showing event ID, POV, endpoint, model, status, error, prompt, and raw response.
        /// Used for in-game debugging of LLM generation issues in the view context.
        /// </summary>
        public string DebugText
        {
            get
            {
                string debug = "LLM debug";

                if (!string.IsNullOrWhiteSpace(EventId))
                {
                    debug += "\nEvent: " + EventId;
                }

                if (!string.IsNullOrWhiteSpace(PovRole))
                {
                    debug += "\nPOV: " + PovRole;
                }

                if (!string.IsNullOrWhiteSpace(LlmEndpoint))
                {
                    debug += "\nEndpoint: " + LlmEndpoint;
                }

                if (!string.IsNullOrWhiteSpace(LlmModel))
                {
                    debug += "\nModel: " + LlmModel;
                }

                if (!string.IsNullOrWhiteSpace(LlmStatus))
                {
                    debug += "\nStatus: " + LlmStatus;
                }

                if (!string.IsNullOrWhiteSpace(LlmError))
                {
                    debug += "\nError: " + LlmError;
                }

                debug += "\nPrompt:\n" + (LlmPrompt ?? Text ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(LlmRawResponse))
                {
                    debug += "\nRaw response:\n" + LlmRawResponse;
                }

                return debug;
            }
        }
    }
}
