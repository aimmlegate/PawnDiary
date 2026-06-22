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
        // Wrapped instruction appended after the structured context lines in a single-entry prompt.
        // Kept short on purpose: the system prompt already carries the voice/atmosphere/truthfulness
        // rules, so this only restates the task to anchor short-context models.
        public string singlePovInstruction = "Write one to three first-person diary sentences as this colonist, about the event above. If the notes are thin, react specifically to what happened rather than inventing detail. Output only the diary entry.";

        // Wrapped instruction for the recipient's second request in paired sequential mode.
        public string recipientFollowupInstruction = "Write one to three first-person diary sentences from the recipient's point of view, about the event above. The initiator's diary entry is hidden continuity context — do not write as if the recipient read it. If the notes are thin, react specifically to what happened rather than inventing detail. Output only the diary entry.";

        // Neutral, non-persona instruction for colonist death summaries.
        public string deathDescriptionInstruction = "Write one to three complete third-person death-description sentences. Keep it brief. State how the colonist died using only the supplied facts: cause, weapon or illness, destroyed organ/body part if known, and nearby context. Do not use the pawn's persona or write from first person. Prefer a shorter complete note over covering every detail. Output only the death description.";

        // Neutral, non-persona instruction for the first diary entry describing how a pawn joined.
        public string arrivalDescriptionInstruction = "Write one to three complete third-person colony-arrival sentences. Keep it brief. Explain how this pawn joined the colony using only the supplied scenario, pawn, and joining facts. For starting colonists, use the scenario details as founding context; for later colonists, use the join facts. Do not use the pawn's persona or write from first person. Prefer a shorter complete note over covering every detail. Output only the arrival description.";

        // Default persona for new/existing pawns that do not have an explicit saved choice.
        public string defaultPersonaDefName = "DiaryPersona_StoicSurvivor";

        // The three main system prompts, one per narrative mode. Settings can store per-save
        // overrides for these shared prompts; XML remains the default used by the restore action.
        // Which one a request uses is chosen by event type at dispatch (see
        // DiaryGameComponent.Generation.cs). These are the code fallbacks; the values actually
        // loaded live in DiaryPromptDef.xml.

        // Diary voice: first-person, in-character entries (interactions, mental states, tales,
        // mood events, thoughts).
        public string systemPrompt = "Write 1-3 first-person diary sentences in the POV colonist's voice.\n"
            + "Use only supplied fields. Do not invent people, places, dialogue, motives, outcomes, treatment, or time skips.\n"
            + "Let event prompt, event enhancement, instruction, tone, setting, relationship, health, and persona shape mood and wording.\n"
            + "Direct speech only when explicitly allowed: put the POV pawn's own words in [[speech]]words[[/speech]] and paraphrase everyone else.\n"
            + "Output only diary text. End with normal sentence punctuation.";

        // Day reflection: first-person, looking back on the whole day, weaving the day's highlights.
        public string systemPromptReflection = "Write 2-4 first-person end-of-day diary sentences in the colonist's voice.\n"
            + "Use only supplied day moments; choose the ones that still matter tonight instead of listing everything.\n"
            + "Let mood, health, setting, and persona shape the reflection.\n"
            + "Output only diary text. End with normal sentence punctuation.";

        // Neutral chronicle: third-person, factual, no persona (colonist death + arrival descriptions).
        public string systemPromptNeutral = "Write 1-3 short third-person factual RimWorld colony notes.\n"
            + "Use only supplied facts. Do not invent names, causes, places, motives, outcomes, or details.\n"
            + "Do not use persona or first person. Output only note text. End with normal sentence punctuation.";

        // Title generation: short chat-style subject for an existing diary entry.
        // Used only by the "Generate LLM titles" flow. The system prompt stays minimal so a small
        // local model can follow it; the user instruction is appended to the title request body.
        public string titleSystemPrompt = "Return a 3-8 word title for a RimWorld diary entry. "
            + "Output only the title: no quotes, period, markdown, labels, or commentary.";

        public string titleUserInstruction = "Return one short title of three to eight words for this diary entry. "
            + "Output only the title \u2014 no quotes, no period, no labels, no commentary.";
    }

    // Accessor for the single DiaryPromptDef. Caches the lookup and falls back to a default
    // instance (with the field initializers above) if no Def is loaded, so the code never
    // NullReferences and behaves identically to the pre-Def version when the XML is absent.
    public static class DiaryPrompts
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
