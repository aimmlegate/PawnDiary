// XML-backed humor-cue Defs and selection helpers for subtle prompt voice variations.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// XML-backed **humor cue**: a per-entry, structural writing license appended to the
    /// first-person system prompt. These are deliberately *not* "be funny" instructions — each
    /// <see cref="rule"/> is a single sentence-shape constraint (an understatement coda, a flat
    /// inventory, a misplaced priority) that lets a small model produce a subtly droll or deadpan
    /// entry without ever naming comedy, jokes, punchlines, or comic dialogue.
    ///
    /// The feature is **hidden and always-on**: there is no settings field, no UI toggle, and no
    /// player-facing label. The single tunable base rate lives in XML
    /// (<c>DiaryTuningDef.humorChance</c>, see <see cref="DiaryTuning"/>); selection is a weighted
    /// random pick over these defs. Flavor is chosen by event stakes: mundane events draw from the
    /// **Light** tier (dry/absurdist), high-stakes events (combat, raids, death, mental breaks)
    /// draw from the **Gallows** tier (dark/deadpan). Both tiers are always eligible; the tier
    /// only picks the flavor.
    ///
    /// A cue rides into the prompt folded inside the same voice block as the writing style (see
    /// <see cref="PawnDiary"/>.<c>DiaryPipelineAdapters.HumorVoiceBlock</c>), so it is automatically
    /// suppressed on neutral death/arrival/title templates that opt out of persona text.
    /// New to C#/RimWorld? See AGENTS.md ("Defs").
    /// </summary>
    public class DiaryHumorCueDef : Def
    {
        // The cue text injected into the prompt. Localized via DefInjected
        // (Languages/English/DefInjected/PawnDiary.DiaryHumorCueDef/DiaryHumorCueDefs.xml); the def
        // XML holds the English default copy. Each rule is one concrete sentence-shape constraint,
        // never a request to "be funny".
        public string rule;

        // Internal stakes-flavor keyword: "Light" (dry/absurdist, mundane events) or "Gallows"
        // (dark/deadpan, high-stakes events). Compared case-insensitively (see DiaryHumorCues.IsGallows).
        // This is a schema keyword, not player-facing text, so it is NOT localized. Default Light so
        // an untagged def still participates in the mundane pool.
        public string tier = DiaryHumorCues.TierLight;

        // Relative weight for the weighted-random pick within the chosen tier. 1 = even with peers.
        public float weight = 1f;
    }

    /// <summary>
    /// Central lookup/tier helper for the humor-cue catalog. RimWorld loads the cues from
    /// <c>1.6/Defs/DiaryHumorCueDefs.xml</c>; <see cref="All"/> returns an empty list (not null)
    /// when no defs are loaded, which cleanly disables the feature if the XML is absent.
    /// </summary>
    internal static class DiaryHumorCues
    {
        // Stable internal tier keywords. Kept in code so tier matching never depends on spelling in
        // a def. These are schema tokens, not localized text.
        public const string TierLight = "Light";
        public const string TierGallows = "Gallows";

        // Reused when no cues exist so All never returns null.
        private static readonly List<DiaryHumorCueDef> EmptyList = new List<DiaryHumorCueDef>();

        /// <summary>
        /// All loaded humor-cue defs, or an empty list if none exist in XML (which silently turns
        /// the feature off — the selector returns <c>string.Empty</c>).
        /// </summary>
        public static IReadOnlyList<DiaryHumorCueDef> All
        {
            get
            {
                List<DiaryHumorCueDef> defs = DefDatabase<DiaryHumorCueDef>.AllDefsListForReading;
                return defs ?? EmptyList;
            }
        }

        /// <summary>
        /// Returns true when this cue belongs to the high-stakes Gallows flavor. Case-insensitive so
        /// XML authors can write <c>gallows</c> or <c>Gallows</c>. Anything else reads as Light.
        /// </summary>
        public static bool IsGallows(DiaryHumorCueDef def)
        {
            return def != null
                && string.Equals(def.tier, TierGallows, StringComparison.OrdinalIgnoreCase);
        }
    }
}
