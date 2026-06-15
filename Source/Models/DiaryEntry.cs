// Two small models for diary text:
//   DiaryEntry     — a LEGACY entry persisted in old saves. New events use DiaryEvent instead;
//                    this stays only so older saves still load.
//   DiaryEntryView — the read-only display model the UI renders (text + status + display hints).
// IExposable/ExposeData is RimWorld's save/load hook; see AGENTS.md ("IExposable").
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Legacy diary entry persisted in older save files. New events use DiaryEvent instead;
    /// this class exists solely so older saves still load correctly.
    /// </summary>
    public class DiaryEntry : IExposable
    {
        public int tick;            // Game tick when the entry was recorded
        public string date;         // Human-readable date string (e.g. "2nd of Apr, 5500")
        public string text;         // Raw game-authored text for this event
        public string id;           // Unique identifier — backfilled on load if missing
        public string generatedText; // Text produced by the LLM (may be empty while pending)
        public string llmStatus;    // "pending", "failed", or empty after successful generation
        public string llmError;     // Error message from the LLM call, if any
        public string llmEndpoint;  // API endpoint that was called
        public string llmModel;     // LLM model identifier used for generation
        public string llmPrompt;    // Full prompt sent to the LLM

        public DiaryEntry()
        {
        }

        public DiaryEntry(int tick, string date, string text)
        {
            id = Guid.NewGuid().ToString("N");
            this.tick = tick;
            this.date = date;
            this.text = text;
            llmStatus = "pending";
        }

        /// <summary>
        /// RimWorld save/load hook. Deserialises all fields from the save file and
        /// backfills a missing id for saves created before the id field existed.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref date, "date");
            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref generatedText, "generatedText");
            Scribe_Values.Look(ref llmStatus, "llmStatus");
            Scribe_Values.Look(ref llmError, "llmError");
            Scribe_Values.Look(ref llmEndpoint, "llmEndpoint");
            Scribe_Values.Look(ref llmModel, "llmModel");

            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }
        }

        /// <summary>
        /// Returns generatedText if available, otherwise falls back to the raw game text.
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(generatedText))
                {
                    return generatedText;
                }

                return text ?? string.Empty;
            }
        }

        /// <summary>
        /// Human-readable status label: "generating" while pending, "generation failed" on error, empty otherwise.
        /// </summary>
        public string StatusText
        {
            get
            {
                if (llmStatus == "pending")
                {
                    return "PawnDiary.Status.Generating".Translate();
                }

                if (llmStatus == "failed")
                {
                    return "PawnDiary.Status.GenerationFailed".Translate();
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Diagnostic block showing endpoint, model, status, error, and the prompt — used for
        /// in-game debugging of LLM generation issues.
        /// </summary>
        public string DebugText
        {
            get
            {
                string debug = "LLM debug";

                if (!string.IsNullOrWhiteSpace(llmEndpoint))
                {
                    debug += "\nEndpoint: " + llmEndpoint;
                }

                if (!string.IsNullOrWhiteSpace(llmModel))
                {
                    debug += "\nModel: " + llmModel;
                }

                if (!string.IsNullOrWhiteSpace(llmStatus))
                {
                    debug += "\nStatus: " + llmStatus;
                }

                if (!string.IsNullOrWhiteSpace(llmError))
                {
                    debug += "\nError: " + llmError;
                }

                debug += "\nPrompt:\n" + (llmPrompt ?? text ?? string.Empty);
                return debug;
            }
        }
    }

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

        public LinkedEntryView(
            string otherPawnId,
            string otherPawnName,
            string otherRole,
            string eventId,
            string truncatedText,
            bool generated)
        {
            OtherPawnId = otherPawnId;
            OtherPawnName = otherPawnName;
            OtherRole = otherRole;
            EventId = eventId;
            TruncatedText = truncatedText;
            Generated = generated;
        }
    }

    /// <summary>
    /// Immutable, read-only display model rendered by the diary UI tab.
    /// Represents a single POV snapshot of a diary event (or a legacy DiaryEntry).
    /// </summary>
    public class DiaryEntryView
    {
        public readonly int Tick;           // Game tick when the event occurred
        public readonly string Date;        // Human-readable date string
        public readonly string Text;        // Raw game-authored event text
        public readonly string GeneratedText; // LLM-generated narrative text
        public readonly string LlmStatus;   // LLM generation status ("pending", "failed", or empty)
        public readonly string LlmError;    // Error message if LLM generation failed
        public readonly string LlmEndpoint; // API endpoint used for the LLM call
        public readonly string LlmModel;    // LLM model identifier
        public readonly string LlmPrompt;  // Full prompt sent to the LLM
        public readonly string EventId;     // Identifier of the backing DiaryEvent
        public readonly string PovRole;     // Role/perspective this view represents (e.g. "legacy")
        public readonly string GroupLabel;  // Human-readable event group shown in the entry header
        public readonly bool Important;     // Visual importance marker derived from the event group
        public readonly LinkedEntryView LinkedEntry; // Preview of the other pawn's entry for the same event (null for solo/legacy)

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
            bool important,
            LinkedEntryView linkedEntry = null)
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
            EventId = eventId;
            PovRole = povRole;
            GroupLabel = groupLabel;
            Important = important;
            LinkedEntry = linkedEntry;
        }

        /// <summary>
        /// Creates a DiaryEntryView from a legacy DiaryEntry, tagging it with POV role "legacy".
        /// Returns null if the entry is null.
        /// </summary>
        public static DiaryEntryView FromLegacy(DiaryEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new DiaryEntryView(
                entry.tick,
                entry.date,
                entry.text,
                entry.generatedText,
                entry.llmStatus,
                entry.llmError,
                entry.llmEndpoint,
                entry.llmModel,
                entry.llmPrompt,
                entry.id,
                "legacy",
                string.Empty,
                true);
        }

        /// <summary>
        /// Returns "writing..." while pending with no output, generatedText if available,
        /// otherwise falls back to raw Text.
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (LlmStatus == "pending" && string.IsNullOrWhiteSpace(GeneratedText))
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
                if (LlmStatus == "pending")
                {
                    return "PawnDiary.Status.Writing".Translate();
                }

                if (LlmStatus == "failed")
                {
                    return "PawnDiary.Status.GenerationFailed".Translate();
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Diagnostic block showing event ID, POV, endpoint, model, status, error, and the prompt.
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
                return debug;
            }
        }
    }
}
