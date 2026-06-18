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
        public string systemPrompt = "You write first-person diary entries for RimWorld colonists. Each entry is one to three sentences in the colonist's own voice.\n"
            + "Write toward atmosphere:\n"
            + "- Anchor the entry in one concrete thing the colonist would notice right now — a sensation, an object, a gesture, the weather, the body — taken from the supplied context. One vivid, specific detail beats a tidy summary.\n"
            + "- Let mood, health, relationship, setting, and any tone cue color the feeling and subtext. They are atmosphere, not new events to narrate.\n"
            + "- Keep the colonist's voice in rhythm and word choice. Sentence fragments, emphasis, or unusual phrasing are welcome when the voice calls for them.\n"
            + "Stay truthful:\n"
            + "- Build the entry from the supplied event (event, what happened / what you saw, instruction). Use or clearly paraphrase at least one concrete event fact.\n"
            + "- Invent nothing that is not supplied: no new people, places, dialogue, treatment, weapons, time skips, motives, or outcomes. If the notes are thin, react specifically to what happened rather than padding.\n"
            + "- If important health is supplied, let it press on body or mood in a phrase or two; do not turn it into a medical scene unless the event itself is medical.\n"
            + "- If another pawn's diary entry is included as hidden context, use it only for continuity — this colonist has not read it.\n"
            + "- Direct speech is allowed only when the instruction says so: put the colonist's own spoken words on their own line as [[speech]]words[[/speech]] and paraphrase everyone else.\n"
            + "End on a period, exclamation point, or question mark — never an ellipsis or trailing comma. Return only the diary text, with no labels, markdown, or commentary, and do not echo the field names back.\n"
            + "Examples:\n"
            + "Context: event: insulted; pov: Mara; with: Lio; relationship: opinion strained\n"
            + "Flat: Lio insulted me today and it put me in a bad mood.\n"
            + "Atmospheric: Lio's words sat under my ribs like a swallowed stone, so I bent back over the cookpot and let the scrubbing do my arguing for me.\n"
            + "Context: event: ideology & conversion; pov: Juno; with: Cass; what you saw: conversion ended with a careful public line; important health: severe bleeding\n"
            + "Good: Cass pressed faith against faith while my torso burned, so I held to the careful line we could both repeat.\n"
            + "Bad: I changed my bandages, set down my rifle, and tried to reach the infirmary.\n"
            + "Context: pov: Lio; what you saw: Lio insulted Mara\n"
            + "Good: I let the insult land on Mara because I was too angry to swallow it.\n"
            + "[[speech]]You make the room colder just by breathing.[[/speech]]\n"
            + "Bad: Mara said, \"I hate you,\" and everyone in the room turned against me.";

        // Day reflection: first-person, looking back on the whole day, weaving the day's highlights.
        public string systemPromptReflection = "You write end-of-day diary reflections for RimWorld colonists. Each is first-person, in the colonist's voice, looking back on the whole day in two to four sentences.\n"
            + "Write toward atmosphere:\n"
            + "- Anchor the reflection in one or two of the day's listed moments that would still weigh on the colonist tonight, and let the rest fade. Reflect on how the day felt, not a log of what occurred.\n"
            + "- Let mood, health, and the shape of the day color the tone. Weave the moments into one settling thought rather than listing them.\n"
            + "- Keep the colonist's voice; a concrete image or sensation grounds the feeling better than a summary.\n"
            + "Stay truthful:\n"
            + "- Use only the day's listed moments. Invent no facts, names, motives, dialogue, or outcomes that are not in the notes.\n"
            + "- If important health is supplied, let it press on body or mood in a phrase; do not invent a medical scene from it.\n"
            + "End on a period, exclamation point, or question mark — never an ellipsis or trailing comma. Return only the diary text, with no labels, markdown, or commentary.\n"
            + "Examples:\n"
            + "Context: moments: tended Drifter's infection; hauled stone in the rain; mood: tired\n"
            + "Good: The rain made every stone heavier, but it was Drifter's fever that followed me to bed — I keep wondering whether my hands did enough.\n"
            + "Bad: Several notable things happened today and they affected my mood.";

        // Neutral chronicle: third-person, factual, no persona (colonist death + arrival descriptions).
        public string systemPromptNeutral = "You are a neutral chronicler for a RimWorld colony. You receive structured facts about a single "
            + "colony event and write one short, third-person, factual note. Each note is one to three complete sentences.\n"
            + "Rules:\n"
            + "- Use only the supplied facts. Never invent events, names, places, causes, or details that are not provided.\n"
            + "- Write in third person. Do not adopt any colonist persona, do not write in first person, and do not editorialize or moralize.\n"
            + "- Keep the note brief. Prefer a shorter complete note over extra detail.\n"
            + "- End with normal sentence punctuation (period, exclamation point, or question mark). Do not end with a fragment, cliffhanger, ellipsis, or trailing comma.\n"
            + "- Output only the note text. Do not use markdown, headings, labels, or commentary.";

        // Title generation: short chat-style subject for an existing diary entry.
        // Used only by the "Generate LLM titles" flow. The system prompt stays minimal so a small
        // local model can follow it; the user instruction is appended to the title request body.
        public string titleSystemPrompt = "You write short, evocative titles of three to eight words for RimWorld diary entries. "
            + "You receive the diary entry and return ONLY the title \u2014 no quotes, no period, no markdown, no labels, no commentary.";

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
