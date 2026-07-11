// PURE mapping layer — no Verse, no UnityEngine, no SP_Module1 references. This is the whole
// "personality -> diary text" decision, isolated so the console test harness
// (tests/Personalities123BridgeLogicTests) can exercise it without loading RimWorld.
//
// Four responsibilities, one per bridge tier's data need:
//   * RuleForRoot: the 9 Enneagram ROOTS (SP_Root1..SP_Root9) -> a diary OUTLOOK rule, in the exact
//     register of Pawn Diary's own DiaryPsychotypeDefs: 1-2 behavioral, mechanics-free sentences in the
//     "This pawn ..." voice, describing what the pawn notices / values / fears and how they judge
//     events. The personality TYPE is never named in the rule. This is the ENGLISH source text AND the
//     universal fallback used when no localization is present. Feeds Tier 2 (override text) and the
//     Tier 3 fallback.
//   * KeyForRoot: the localization key for each root (e.g. "PawnDiaryPersonalities123.Outlook.SP_Root1").
//     EnneagramSync resolves this through RimWorld's Keyed system when it can, so a Russian player gets
//     natively-authored Russian outlook text (see Languages/*/Keyed) instead of the English fallback —
//     the outlook rule reaches the model's prompt, so it must localize like every other prompt string.
//     English is deliberately NOT duplicated in a Keyed file: the source of truth stays here.
//   * InternalPsychotypeForRoot: maps each root to the closest built-in Pawn Diary psychotype defName,
//     so Tier 1 can point the pawn's BASE psychotype at a real, swappable internal type. Pawn Diary
//     resolves an unknown/renamed defName to Neutral, so a stale mapping degrades gracefully.
//   * BuildTransformInput: assembles the compact personality-data block (variant + trait + root + raw
//     serialization) that Tier 3 sends to the LLM as the text to rewrite.
//
// Roots carry no <label> in 1-2-3 Personalities (only variants and traits do), so the tables are keyed
// by the stable root defName. The classic Enneagram type numbers map straight to SP_Root1..9.
using System.Collections.Generic;

namespace PawnDiaryPersonalities123.Pure
{
    /// <summary>Pure personality-to-diary-text mapping. Deterministic, RimWorld-free, unit-testable.</summary>
    public static class EnneagramLensMapping
    {
        // Keyed-translation prefix for the outlook rules. Concatenated with the canonical root defName.
        public const string OutlookKeyPrefix = "PawnDiaryPersonalities123.Outlook.";

        /// <summary>
        /// The English diary outlook rule for one Enneagram root, or null when the root defName is
        /// unknown (a future/renamed root, or an animal variant with no humanlike root). Case-insensitive.
        /// This is the source text and the fallback when a localization is absent.
        /// </summary>
        public static string RuleForRoot(string rootDefName)
        {
            switch (CanonicalRoot(rootDefName))
            {
                case "SP_Root1":
                    return "This pawn measures each day against how it should have gone. They notice what was done carelessly or wrongly, their own lapses hardest of all, and a small thing set right can settle them more than a large success.";
                case "SP_Root2":
                    return "This pawn reads a day by who needed them and whether they were there for it. Other people's wants come into focus long before their own, and being unneeded unsettles them more than being unthanked.";
                case "SP_Root3":
                    return "This pawn keeps a running tally of what they got done and how it looked to others. Progress and being seen to do well steady them; an idle or unwitnessed day feels like falling behind.";
                case "SP_Root4":
                    return "This pawn feels each day intensely and personally, half-aware of something missing that others seem to have. Ordinary moments carry private weight, and they would rather feel a thing all the way through than smooth it over.";
                case "SP_Root5":
                    return "This pawn stands a step back from the day and takes it apart to understand it. They guard their time and their privacy, trust what they can work out over what they are told, and find company more tiring than most.";
                case "SP_Root6":
                    return "This pawn scans each day for what could go wrong and who can be counted on. Loyalty and preparation reassure them; a calm stretch is often spent bracing for the thing that has not happened yet.";
                case "SP_Root7":
                    return "This pawn meets the day looking for what is next and what is good in it. They keep their options open and turn hardship into a story or a plan, and feeling hemmed in or made to dwell unsettles them more than the trouble itself.";
                case "SP_Root8":
                    return "This pawn takes the day head-on and reads it in terms of strength and control: who has it, who is pushing, what needs protecting. They respect directness, distrust being managed, and would rather face a thing than admit it got to them.";
                case "SP_Root9":
                    return "This pawn weighs a day by how settled and unbothered it felt. They smooth over friction and fold easily into what others want, so their own preferences stay soft and hard to find, and open conflict costs them more than almost anything.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The Keyed-translation key for one root's outlook rule, or null for an unknown root. Pairs with
        /// <see cref="RuleForRoot"/>: callers translate this key and fall back to the English rule.
        /// </summary>
        public static string KeyForRoot(string rootDefName)
        {
            string canonical = CanonicalRoot(rootDefName);
            return canonical == null ? null : OutlookKeyPrefix + canonical;
        }

        /// <summary>
        /// Maps one Enneagram root to the closest built-in Pawn Diary psychotype defName (Tier 1), or null
        /// for an unknown root. These target the ADULT psychotype catalog; Pawn Diary resolves an unknown
        /// defName to Neutral, so a rename on either side degrades to Neutral rather than throwing. The
        /// pairing is a deliberate, editable design choice — not a 1:1 truth — so it lives here beside the
        /// outlook rules rather than in XML, matching how this bridge already owns its personality mapping.
        /// </summary>
        public static string InternalPsychotypeForRoot(string rootDefName)
        {
            switch (CanonicalRoot(rootDefName))
            {
                case "SP_Root1":
                    return "DiaryPsychotype_Perfectionist";
                case "SP_Root2":
                    return "DiaryPsychotype_Dutiful";
                case "SP_Root3":
                    return "DiaryPsychotype_Ambitious";
                case "SP_Root4":
                    return "DiaryPsychotype_Nostalgic";
                case "SP_Root5":
                    return "DiaryPsychotype_Detached";
                case "SP_Root6":
                    return "DiaryPsychotype_Paranoid";
                case "SP_Root7":
                    return "DiaryPsychotype_Wry";
                case "SP_Root8":
                    return "DiaryPsychotype_Ruthless";
                case "SP_Root9":
                    return "DiaryPsychotype_Content";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Builds the compact personality-data block that Tier 3 sends to the LLM as the text to rewrite.
        /// Includes only the fields that are present: the player-facing variant + main-trait labels (from
        /// 1-2-3's own Defs), the LOCALIZED base outlook for the pawn's root (the same text Tier 2 would
        /// seed — deliberately included so a small model rewrites known-good, on-register text instead of
        /// inventing an outlook from a bare type number; its worst failure, copying it verbatim, is still
        /// correct), and the raw personality serialization. Returns null when there is nothing worth
        /// sending. This is INPUT data only — the transform prompt is what tells the model to keep the
        /// type unnamed in its OUTPUT. The schema labels stay English on purpose (machine keys, like the
        /// core prompt schema); the default prompts reference the "base outlook" label by name.
        /// </summary>
        public static string BuildTransformInput(string variantLabel, string mainTraitLabel, string baseOutlookRule, string serialization)
        {
            List<string> lines = new List<string>();

            string variant = Clean(variantLabel);
            if (variant.Length > 0)
            {
                lines.Add("personality style: " + variant);
            }

            string trait = Clean(mainTraitLabel);
            if (trait.Length > 0)
            {
                lines.Add("main trait: " + trait);
            }

            string baseOutlook = Clean(baseOutlookRule);
            if (baseOutlook.Length > 0)
            {
                lines.Add("base outlook: " + baseOutlook);
            }

            string extra = Clean(serialization);
            if (extra.Length > 0)
            {
                lines.Add("details: " + extra);
            }

            return lines.Count == 0 ? null : string.Join("\n", lines.ToArray());
        }

        // Normalizes any casing/padding of a root defName to its canonical "SP_Root1".."SP_Root9" form,
        // or null when it is not one of the nine humanlike roots. Single source of root validity for
        // every table above, so they can never disagree about which roots are mapped.
        private static string CanonicalRoot(string rootDefName)
        {
            if (string.IsNullOrWhiteSpace(rootDefName))
            {
                return null;
            }

            switch (rootDefName.Trim().ToUpperInvariant())
            {
                case "SP_ROOT1": return "SP_Root1";
                case "SP_ROOT2": return "SP_Root2";
                case "SP_ROOT3": return "SP_Root3";
                case "SP_ROOT4": return "SP_Root4";
                case "SP_ROOT5": return "SP_Root5";
                case "SP_ROOT6": return "SP_Root6";
                case "SP_ROOT7": return "SP_Root7";
                case "SP_ROOT8": return "SP_Root8";
                case "SP_ROOT9": return "SP_Root9";
                default: return null;
            }
        }

        private static string Clean(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }
    }
}
