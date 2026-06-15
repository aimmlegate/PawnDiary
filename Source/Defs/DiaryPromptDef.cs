// The prompt instructions, legacy dual markers, and system-prompt default — pulled into one Def
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
        public string singlePovInstruction = "Write one short first-person diary entry from this pawn's point of view. Output only the diary entry.";

        // Wrapped instruction for the recipient's second request in paired sequential mode.
        public string recipientFollowupInstruction = "Write one short first-person diary entry from the recipient's point of view. The initiator diary entry is hidden continuity context; do not write as if the recipient read it. Output only the diary entry.";

        // Legacy dual-POV instruction retained for compatibility with older saved/generated data.
        public string dualInstruction = "Write two short first-person diary entries, one from each pawn's point of view, following the instruction.";

        // Neutral, non-persona instruction for colonist death summaries.
        public string deathDescriptionInstruction = "Write one short, third-person death description. State how the colonist died using only the supplied facts: cause, weapon or illness, destroyed organ/body part if known, and nearby context. Do not use the pawn's persona or write from first person. Output only the death description.";

        // Neutral, non-persona instruction for the first diary entry describing how a pawn joined.
        public string arrivalDescriptionInstruction = "Write one short, third-person colony arrival description. Explain how this pawn joined the colony using only the supplied scenario, pawn, and joining facts. For starting colonists, use the scenario details as founding context; for later colonists, use the join facts. Do not use the pawn's persona or write from first person. Output only the arrival description.";

        // Legacy marker that preceded the initiator's diary entry in old dual-POV responses.
        public string initiatorMarker = "[INITIATOR]";

        // Legacy marker that preceded the recipient's diary entry in old dual-POV responses.
        public string recipientMarker = "[RECIPIENT]";

        // Default persona for new/existing pawns that do not have an explicit saved choice.
        public string defaultPersonaDefName = "DiaryPersona_StoicSurvivor";

        // The default system prompt. Copied into saved settings on first use; players can
        // override it in-game. Editing this in XML only affects the "restore default" action
        // for existing saves.
        public string systemPrompt = "You are the diary-writer for a RimWorld colony. You receive structured notes about a social interaction "
            + "between colonists and write short, first-person diary entries in the voice of the colonist whose point of view is requested.\n"
            + "Rules:\n"
            + "- Write only what that colonist could plausibly know, see, or feel. Never invent events, names, places, or facts that are not in the notes.\n"
            + "- Stay in first person and in character. Reflect the colonist's persona, mood, and their opinion of the other pawn.\n"
            + "- Keep each entry to a few sentences. Be concrete and grounded in the provided context; do not moralize or summarize game mechanics.\n"
            + "- If another pawn's diary entry is included as hidden context, use it only for continuity and contrast; the current pawn has not read it unless the notes say so.\n"
            + "- Output only the diary text. Do not use markdown, headings, labels, or commentary.";
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
