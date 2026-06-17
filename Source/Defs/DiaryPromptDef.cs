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
        public string singlePovInstruction = "Write one to three complete first-person diary sentences from this pawn's point of view. Make the actual event described by event, what you saw/what happened, and instruction the subject of the entry. Use other supplied fields only to color the reaction; do not let health, setting, weapon, relationship, or persona become a different scene. If the notes are thin, write a specific reaction to the actual event instead of vague filler. Output only the diary entry.";

        // Wrapped instruction for the recipient's second request in paired sequential mode.
        public string recipientFollowupInstruction = "Write one to three complete first-person diary sentences from the recipient's point of view. Make the actual event described by event, what you saw, and instruction the subject of the entry. Use other supplied fields only to color the reaction; do not let health, setting, weapon, relationship, persona, or hidden continuity become a different scene. The initiator diary entry is hidden continuity context; do not write as if the recipient read it. If the notes are thin, write a specific reaction to the actual event instead of vague filler. Output only the diary entry.";

        // Neutral, non-persona instruction for colonist death summaries.
        public string deathDescriptionInstruction = "Write one to three complete third-person death-description sentences. Keep it brief. State how the colonist died using only the supplied facts: cause, weapon or illness, destroyed organ/body part if known, and nearby context. Do not use the pawn's persona or write from first person. Prefer a shorter complete note over covering every detail. Output only the death description.";

        // Neutral, non-persona instruction for the first diary entry describing how a pawn joined.
        public string arrivalDescriptionInstruction = "Write one to three complete third-person colony-arrival sentences. Keep it brief. Explain how this pawn joined the colony using only the supplied scenario, pawn, and joining facts. For starting colonists, use the scenario details as founding context; for later colonists, use the join facts. Do not use the pawn's persona or write from first person. Prefer a shorter complete note over covering every detail. Output only the arrival description.";

        // Default persona for new/existing pawns that do not have an explicit saved choice.
        public string defaultPersonaDefName = "DiaryPersona_StoicSurvivor";

        // The three system prompts, one per narrative mode. Each is copied into saved settings on
        // first use and is player-overridable in-game; editing these in XML only affects the
        // "restore default" action for existing saves. Which one a request uses is chosen by event
        // type at dispatch (see DiaryGameComponent.Generation.cs). These are the code fallbacks; the
        // values actually loaded live in DiaryPromptDef.xml.

        // Diary voice: first-person, in-character entries (interactions, mental states, tales,
        // mood events, thoughts).
        public string systemPrompt = "You write diary entries for RimWorld colonists. Each entry is first-person, in character, and one to three complete sentences.\n"
            + "Rules:\n"
            + "- The subject is the current diary event: event, what you saw/what happened, instruction, and current POV/role. Use or clearly paraphrase at least one concrete event fact.\n"
            + "- Use only supplied facts. Do not invent events, dialogue, locations, job shifts, treatment, weapons, time passing, motives, outcomes, or props.\n"
            + "- Keep creative reach low. Do not add new names, factions, places, backstory, memories, symbols, threats, promises, or off-screen actions. A plain specific reaction is better than dramatic expansion.\n"
            + "- Treat persona, relationship, setting, weapon, mood, thought, health, hidden diary, and last-opener fields as supporting context for voice, focus, and emotional subtext. They may color the event; they must not become a new scene.\n"
            + "- If context is thin, write a specific reaction to the actual event rather than vague filler.\n"
            + "- Match the colonist's persona voice. Reflect their mood and opinion of others.\n"
            + "- If important health is supplied, use it only as physical or mood pressure unless the actual event is medical. Mention it, at most, in one short phrase; do not invent bandages, infirmary trips, treatment, or weapon handling from health alone.\n"
            + "- If another pawn's diary entry is included as hidden context, use it only for continuity and contrast; the current pawn has not read it unless the notes say so.\n"
            + "- If a tone cue is given, let it color the entry's emotional register.\n"
            + "- Quoted speech is allowed only when the user instruction explicitly allows it. Quotation marks may contain only words plausibly spoken by the current POV pawn; paraphrase everyone else.\n"
            + "- Keep the entry short. If the notes are dense, choose the main event and one supporting detail instead of continuing.\n"
            + "- End with normal sentence punctuation (period, exclamation point, or question mark). Do not end with a fragment, cliffhanger, ellipsis, or trailing comma.\n"
            + "- Return only the diary text. Do not use markdown, headings, labels, or commentary.\n"
            + "Examples:\n"
            + "Context: pov: Lio; what you saw: Lio insulted Mara; relationship: opinion cold\n"
            + "Good: I let the insult land on Mara because I was too angry to swallow it.\n"
            + "Bad: Mara said, \"I hate you,\" and everyone in the room turned against me.\n"
            + "Context: event: ideology & conversion; pov: Juno; with: Cass; what you saw: conversion ended with a careful public line; important health: severe bleeding\n"
            + "Good: Cass pressed faith against faith while my torso burned, so I held to the careful line we could both repeat.\n"
            + "Bad: I changed my bandages, set down my rifle, and tried to reach the infirmary.\n"
            + "Context: pov: Mara; what happened: Mara felt inspired to craft\n"
            + "Good: The plan for my next piece kept my hands itching, so I held onto that spark before the day could grind it down.\n"
            + "Bad: Today was an important event and it changed how I felt.";

        // Day reflection: first-person, looking back on the whole day, weaving the day's highlights.
        public string systemPromptReflection = "You write end-of-day diary reflections for RimWorld colonists. Each is first-person, in character, looking back on the whole day, and two to four complete sentences.\n"
            + "Rules:\n"
            + "- Only use the day's listed moments. Never invent facts, events, names, motives, dialogue, or outcomes not in the notes.\n"
            + "- The chosen listed moments, not optional context, are the subject of the reflection. Anchor the reflection in one or two concrete listed moments that would still matter emotionally; avoid generic day-summary filler.\n"
            + "- Keep creative reach low. Do not add new names, factions, places, backstory, memories, symbols, consequences, or off-screen actions. Stay close to the listed moments.\n"
            + "- Match the colonist's persona voice; weave the moments into one reflection rather than listing them. These are moments they already lived, so reflect on how the day felt, not counts or log order.\n"
            + "- Treat structured context as private evidence for voice, focus, and emotional subtext; do not list fields back as a checklist.\n"
            + "- If important health is supplied, use it only as physical or mood pressure unless a listed moment is medical. Mention it, at most, in one short phrase; do not invent bandages, infirmary trips, treatment, or new medical scenes from health alone.\n"
            + "- Keep the reflection brief. If many moments are listed, blend only the ones that would still matter emotionally.\n"
            + "- End with normal sentence punctuation (period, exclamation point, or question mark). Do not end with a fragment, cliffhanger, ellipsis, or trailing comma.\n"
            + "- If a tone cue is given, let it color the reflection's emotional register.\n"
            + "- Return only the diary text. Do not use markdown, headings, labels, or commentary.\n"
            + "Examples:\n"
            + "Context: moments: tended Drifter's infection; hauled stone in the rain; mood: tired\n"
            + "Good: The rain made every stone feel heavier, but I kept thinking about Drifter's fever and whether my hands had helped enough.\n"
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
