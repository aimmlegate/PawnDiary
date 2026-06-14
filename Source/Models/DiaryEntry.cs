// Two small models for diary text:
//   DiaryEntry     — a LEGACY entry persisted in old saves. New events use DiaryEvent instead;
//                    this stays only so older saves still load.
//   DiaryEntryView — the read-only display model the UI renders (text + status + debug block).
// IExposable/ExposeData is RimWorld's save/load hook; see CSHARP-NOTES.md ("IExposable").
using System;
using Verse;

namespace PawnDiary
{
    public class DiaryEntry : IExposable
    {
        public int tick;
        public string date;
        public string text;
        public string id;
        public string generatedText;
        public string llmStatus;
        public string llmError;
        public string llmEndpoint;
        public string llmModel;
        public string llmPrompt;

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

        public string StatusText
        {
            get
            {
                if (llmStatus == "pending")
                {
                    return "generating";
                }

                if (llmStatus == "failed")
                {
                    return "generation failed";
                }

                return string.Empty;
            }
        }

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

    public class DiaryEntryView
    {
        public readonly int Tick;
        public readonly string Date;
        public readonly string Text;
        public readonly string GeneratedText;
        public readonly string LlmStatus;
        public readonly string LlmError;
        public readonly string LlmEndpoint;
        public readonly string LlmModel;
        public readonly string LlmPrompt;
        public readonly string EventId;
        public readonly string PovRole;

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
            string povRole)
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
        }

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
                "legacy");
        }

        public string DisplayText
        {
            get
            {
                if (LlmStatus == "pending" && string.IsNullOrWhiteSpace(GeneratedText))
                {
                    return "writing...";
                }

                if (!string.IsNullOrWhiteSpace(GeneratedText))
                {
                    return GeneratedText;
                }

                return Text ?? string.Empty;
            }
        }

        public string StatusText
        {
            get
            {
                if (LlmStatus == "pending")
                {
                    return "writing";
                }

                if (LlmStatus == "failed")
                {
                    return "generation failed";
                }

                return string.Empty;
            }
        }

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
