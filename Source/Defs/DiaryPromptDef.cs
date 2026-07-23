// The prompt instructions and system-prompt defaults — pulled into one Def
// so they can be retuned by editing XML (1.6/Defs/DiaryPromptDef.xml) and restarting — no recompile.
// Every field defaults to the value the code shipped with, so a missing or partial XML changes
// nothing. New to C#/RimWorld? See AGENTS.md ("Defs").
using Verse;

namespace PawnDiary
{
    // One instance of this Def is expected, with defName "Diary_Prompts". Read it via
    // DiaryPrompts.Current (which falls back to safe defaults if the Def is absent).
    public class DiaryPromptDef : Def
    {
        // Final instructions are short LAST-POSITION anchors: they restate only the task and length.
        // The system prompt owns every truth/POV/style/output rule; fallbacks mirror the XML text.
        public string singlePovInstruction = "Write 1-3 first-person diary sentences about the event above. Output only the entry.";

        // Wrapped instruction for the recipient's second request in paired sequential mode.
        public string recipientFollowupInstruction = "Write 1-3 first-person diary sentences from the recipient's point of view. The initiator's entry is hidden context — never mention or answer it. Output only the entry.";

        // Neutral, non-writing-style instruction for colonist death summaries.
        public string deathDescriptionInstruction = "Write 1-3 third-person sentences describing how the colonist died, using only the supplied facts: cause, weapon or illness, lost parts, nearby context. Prefer a shorter complete note. Output only the death description.";

        // Neutral, non-writing-style instruction for the first diary entry describing how a pawn joined.
        public string arrivalDescriptionInstruction = "Write 1-3 third-person sentences explaining how this pawn joined the colony, using only the supplied scenario, join, and backstory facts. For starting colonists, connect scenario and backstory into how they reached this beginning; for later joiners, use the join facts. Output only the arrival description.";

        // Default writing-style Def for new/existing pawns that do not have an explicit saved choice.
        // The field name is kept for save/XML compatibility with older Pawn Diary releases.
        public string defaultPersonaDefName = "DiaryPersona_StoicSurvivor";

        // The three main system prompts, one per narrative mode. Settings can store per-save
        // overrides for these shared prompts; XML remains the default used by the restore action.
        // Which one a request uses is chosen by event type at dispatch (see
        // DiaryGameComponent.Generation.cs). These are the code fallbacks; the values actually
        // loaded live in DiaryPromptDef.xml.

        // Diary voice: first-person entries (interactions, mental states, tales,
        // mood events, thoughts).
        public string systemPrompt = "You are the colonist named in \"pov\". Write 1-3 first-person diary sentences as \"I\", even where the notes describe you by name.\n"
            + "Use only supplied facts: event first, event/group instruction second, tone and writing style last.\n"
            + "Add at most one reaction of your own — an emotion, impression, or interpretation. Invent nothing else: no new events, people, places, actions, dialogue, motives, treatment, outcomes, or time skips.\n"
            + "Open differently: never with weather, \"Today\", or your last opening line. Avoid stock phrases like \"heart skipped a beat\" or \"I couldn't help but\".\n"
            + "Direct speech only for the POV pawn's own quoted words, as [[speech]]words[[/speech]]; paraphrase everyone else.\n"
            + "Output only the diary entry. End with normal punctuation.";

        // Day reflection: first-person, looking back on the whole day, weaving the day's highlights.
        public string systemPromptReflection = "You are the colonist named in \"pov\". Write 2-4 first-person end-of-day diary sentences as \"I\", never your name.\n"
            + "Choose one to three supplied day moments that still matter tonight — do not list everything. Connect them into one private thought, then apply mood, health, setting, and writing style. Invent nothing new.\n"
            + "Begin differently from your last opening line. Avoid stock phrases like \"I couldn't help but\".\n"
            + "Output only the diary entry. End with normal punctuation.";

        // Neutral chronicle: third-person, factual, no writing style (colonist death + arrival descriptions).
        public string systemPromptNeutral = "Write 1-3 short third-person factual RimWorld colony notes.\n"
            + "Use only supplied facts; invent no names, causes, motives, or outcomes.\n"
            + "No writing style, no first person. Output only the note, with normal punctuation.";

        // Title generation: short chat-style subject for an existing diary entry.
        // Used only by the "Generate LLM titles" flow. The system prompt stays minimal so a small
        // local model can follow it; the user instruction is appended to the title request body.
        public string titleSystemPrompt = "Return a 3-8 word title for a RimWorld diary entry. "
            + "Output only the title: no quotes, period, markdown, labels, or commentary.";

        public string titleUserInstruction = "Return one title of three to eight words for the diary entry above \u2014 "
            + "no quotes, period, labels, or commentary. Do not continue or rewrite the entry.";
    }

    // Accessor for the single DiaryPromptDef. Caches the lookup and falls back to a default
    // instance (with the field initializers above) if no Def is loaded, so the code never
    // NullReferences and behaves identically to the pre-Def version when the XML is absent.
    internal static class DiaryPrompts
    {
        private static DiaryPromptDef cached;
        private static readonly DiaryPromptDef Fallback = new DiaryPromptDef();

        public static DiaryPromptDef Current
        {
            get
            {
                if (cached == null)
                {
                    cached = DefDatabase<DiaryPromptDef>.GetNamedSilentFail("Diary_Prompts");
                }

                return cached ?? Fallback;
            }
        }
    }
}
