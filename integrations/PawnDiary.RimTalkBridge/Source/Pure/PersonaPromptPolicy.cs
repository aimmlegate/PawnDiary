// Pure mapping from Pawn Diary catalog identities to the small prompt-policy vocabulary understood
// by the RimTalk bridge. No authored writing-style prose enters this layer or crosses the bridge.
using System;
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge.Pure
{
    /// <summary>Bounded modifiers that may shape a psychotype-to-persona LLM transform.</summary>
    internal enum PersonaPromptModifier
    {
        None,
        MindCrumbled,
        SilentFocus,
        PainNeedle,
        BlankBliss,
        BrightFog,
        ChildPlainOrder,
        ChildBigFeeling,
        ChildLiteralWatch,
        ChildQuestion,
        ChildBrave
    }

    /// <summary>Maps exact Def identities; unknown and ordinary styles intentionally do nothing.</summary>
    internal static class PersonaPromptPolicy
    {
        public static PersonaPromptModifier Resolve(string baseStyleDefName,
            IList<string> activeHediffDefNames)
        {
            // Same precedence as DiaryHediffPersonaOverrideDefs.xml. Only the exact five conditions
            // requested for persona sync participate; all other health conditions are ignored.
            if (Contains(activeHediffDefNames, "TraumaSavant")) return PersonaPromptModifier.SilentFocus;
            if (Contains(activeHediffDefNames, "CrumbledMind")) return PersonaPromptModifier.MindCrumbled;
            if (Contains(activeHediffDefNames, "BlissLobotomy")) return PersonaPromptModifier.BlankBliss;
            if (Contains(activeHediffDefNames, "Mindscrew")) return PersonaPromptModifier.PainNeedle;
            if (Contains(activeHediffDefNames, "Joywire")) return PersonaPromptModifier.BrightFog;

            switch (baseStyleDefName ?? string.Empty)
            {
                case "DiaryPersona_ChildPlainOrder": return PersonaPromptModifier.ChildPlainOrder;
                case "DiaryPersona_ChildBigFeeling": return PersonaPromptModifier.ChildBigFeeling;
                case "DiaryPersona_ChildLiteralWatch": return PersonaPromptModifier.ChildLiteralWatch;
                case "DiaryPersona_ChildQuestion": return PersonaPromptModifier.ChildQuestion;
                case "DiaryPersona_ChildBrave": return PersonaPromptModifier.ChildBrave;
                default: return PersonaPromptModifier.None;
            }
        }

        private static bool Contains(IList<string> values, string expected)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Trauma savant affects RimTalk participation, not persona prose.</summary>
        public static bool ForcesZeroChattiness(PersonaPromptModifier modifier)
        {
            return modifier == PersonaPromptModifier.SilentFocus;
        }

        /// <summary>Silent-focus never changes persona prose, so it shares the unmodified transform key.</summary>
        public static PersonaPromptModifier TransformModifier(PersonaPromptModifier modifier)
        {
            return modifier == PersonaPromptModifier.SilentFocus ? PersonaPromptModifier.None : modifier;
        }
    }
}
