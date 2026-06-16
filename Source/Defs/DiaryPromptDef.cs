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
        public string singlePovInstruction = "Write 1-3 complete first-person diary sentences from this pawn's point of view, 35-75 words total. Prefer a shorter complete entry over covering every detail. Output only the diary entry.";

        // Wrapped instruction for the recipient's second request in paired sequential mode.
        public string recipientFollowupInstruction = "Write 1-3 complete first-person diary sentences from the recipient's point of view, 35-75 words total. The initiator diary entry is hidden continuity context; do not write as if the recipient read it. Prefer a shorter complete entry over covering every detail. Output only the diary entry.";

        // Neutral, non-persona instruction for colonist death summaries.
        public string deathDescriptionInstruction = "Write 1-3 complete third-person death-description sentences, 25-65 words total. State how the colonist died using only the supplied facts: cause, weapon or illness, destroyed organ/body part if known, and nearby context. Do not use the pawn's persona or write from first person. Prefer a shorter complete note over covering every detail. Output only the death description.";

        // Neutral, non-persona instruction for the first diary entry describing how a pawn joined.
        public string arrivalDescriptionInstruction = "Write 1-3 complete third-person colony-arrival sentences, 25-75 words total. Explain how this pawn joined the colony using only the supplied scenario, pawn, and joining facts. For starting colonists, use the scenario details as founding context; for later colonists, use the join facts. Do not use the pawn's persona or write from first person. Prefer a shorter complete note over covering every detail. Output only the arrival description.";

        // Default persona for new/existing pawns that do not have an explicit saved choice.
        public string defaultPersonaDefName = "DiaryPersona_StoicSurvivor";

        // The three system prompts, one per narrative mode. Each is copied into saved settings on
        // first use and is player-overridable in-game; editing these in XML only affects the
        // "restore default" action for existing saves. Which one a request uses is chosen by event
        // type at dispatch (see DiaryGameComponent.Generation.cs). These are the code fallbacks; the
        // values actually loaded live in DiaryPromptDef.xml.

        // Diary voice: first-person, in-character entries (interactions, mental states, tales,
        // mood events, thoughts). The original system prompt.
        public string systemPrompt = "You are the diary-writer for a RimWorld colony. You receive structured notes about a social interaction "
            + "between colonists and write short, first-person diary entries in the voice of the colonist whose point of view is requested. Each entry is exactly 1-3 complete sentences.\n"
            + "Rules:\n"
            + "- Write only what that colonist could plausibly know, see, or feel. Never invent events, names, places, or facts that are not in the notes.\n"
            + "- Stay in first person and in character. Reflect the colonist's persona, mood, and their opinion of the other pawn.\n"
            + "- Use structured context as private evidence for voice, focus, and emotional subtext. Let pawn state, relationship, setting, and tone shape word choice; do not list fields back as a checklist.\n"
            + "- Hard length limit: 35-75 words total. Do not exceed 75 words. If the notes are dense, choose the most emotionally important detail instead of continuing.\n"
            + "- End with normal sentence punctuation (period, exclamation point, or question mark). Do not end with a fragment, cliffhanger, ellipsis, or trailing comma.\n"
            + "- Be concrete and grounded in the provided context; do not moralize or summarize game mechanics.\n"
            + "- If another pawn's diary entry is included as hidden context, use it only for continuity and contrast; the current pawn has not read it unless the notes say so.\n"
            + "- If a tone cue is given, let it color the entry's emotional register.\n"
            + "- Return only the diary text. Do not use markdown, headings, labels, or commentary.";

        // Day reflection: first-person, looking back on the whole day, weaving the day's highlights.
        public string systemPromptReflection = "You are the diary-writer for a RimWorld colony. You receive a short list of one colonist's most "
            + "notable moments from the day and write a brief, first-person end-of-day reflection in that colonist's voice, looking back on the day as a whole. Each reflection is exactly 2-4 complete sentences.\n"
            + "Rules:\n"
            + "- Use only the listed moments. Never invent events, names, places, or facts that are not in the notes.\n"
            + "- Stay in first person and in character; reflect the colonist's persona and mood. Weave the moments together rather than listing them.\n"
            + "- Treat structured context as private evidence for voice, focus, and emotional subtext; do not list fields back as a checklist.\n"
            + "- These are moments the colonist already lived through, so reflect on how the day felt rather than re-reporting each one. Do not mention counts.\n"
            + "- Hard length limit: 50-90 words total. Do not exceed 90 words. If many moments are listed, blend only the ones that would still matter emotionally.\n"
            + "- End with normal sentence punctuation (period, exclamation point, or question mark). Do not end with a fragment, cliffhanger, ellipsis, or trailing comma.\n"
            + "- If a tone cue is given, let it color the reflection's emotional register.\n"
            + "- Return only the diary text. Do not use markdown, headings, labels, or commentary.";

        // Neutral chronicle: third-person, factual, no persona (colonist death + arrival descriptions).
        public string systemPromptNeutral = "You are a neutral chronicler for a RimWorld colony. You receive structured facts about a single "
            + "colony event and write one short, third-person, factual note. Each note is exactly 1-3 complete sentences.\n"
            + "Rules:\n"
            + "- Use only the supplied facts. Never invent events, names, places, causes, or details that are not provided.\n"
            + "- Write in third person. Do not adopt any colonist persona, do not write in first person, and do not editorialize or moralize.\n"
            + "- Hard length limit: 25-65 words total. Do not exceed 65 words. Prefer a shorter complete note over extra detail.\n"
            + "- End with normal sentence punctuation (period, exclamation point, or question mark). Do not end with a fragment, cliffhanger, ellipsis, or trailing comma.\n"
            + "- Output only the note text. Do not use markdown, headings, labels, or commentary.";

        // Title generation: short chat-style subject (3-8 words) for an existing diary entry.
        // Used only by the "Generate LLM titles" flow. The system prompt stays minimal so a small
        // local model can follow it; the user instruction is appended to the title request body.
        public string titleSystemPrompt = "You write short, evocative titles (3 to 8 words) for RimWorld diary entries. "
            + "You receive the diary entry and return ONLY the title \u2014 no quotes, no period, no markdown, no labels, no commentary.";

        public string titleUserInstruction = "Return one short title (3-8 words) for this diary entry. "
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
