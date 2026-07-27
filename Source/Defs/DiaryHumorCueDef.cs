// XML-backed optional voice-cue Defs and selection helpers. Historical "humor" type/field names are
// retained for save, XML, translation, settings, and mod-patch compatibility.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// XML-backed optional **voice cue**: a per-entry structural device appended to the first-person
    /// system prompt. These are deliberately *not* "be funny" instructions — each <see cref="rule"/>
    /// authorizes one concrete, factual sentence habit or familiar phrase.
    ///
    /// The feature has no ordinary player-facing toggle. XML/Advanced tuning owns its base chance and
    /// stable per-writer repertoire size (see <see cref="DiaryTuning"/>). Mundane events draw from the
    /// **Light** tier; high-stakes events draw from **Gallows**. A stable hash assigns a small repertoire
    /// in each tier, then the existing event seed makes one weighted pick inside that repertoire.
    ///
    /// A cue rides into the prompt folded inside the same voice block as the writing style (see
    /// <see cref="PawnDiary"/>.<c>DiaryPipelineAdapters.HumorVoiceBlock</c>), so it is automatically
    /// suppressed on neutral death/arrival/title templates that opt out of persona text.
    /// New to C#/RimWorld? See AGENTS.md ("Defs").
    /// </summary>
    public class DiaryHumorCueDef : Def
    {
        // The optional device injected into the prompt. Localized via DefInjected
        // (Languages/English/DefInjected/PawnDiary.DiaryHumorCueDef/DiaryHumorCueDefs.xml); the def
        // XML holds the English default copy. Each rule authorizes one concrete factual device,
        // never a request to "be funny" or invent missing context.
        public string rule;

        // Internal stakes-flavor keyword: "Light" (dry/absurdist, mundane events) or "Gallows"
        // (dark/deadpan, high-stakes events). Compared case-insensitively (see DiaryHumorCues.IsGallows).
        // This is a schema keyword, not player-facing text, so it is NOT localized. Default Light so
        // an untagged def still participates in the mundane pool.
        public string tier = DiaryHumorCues.TierLight;

        // Relative weight for the event-time pick within the writer's owned tier repertoire.
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

        /// <summary>
        /// Returns true only for the two supported internal tier tokens. Malformed modded rows are
        /// rejected instead of silently entering the Light pool.
        /// </summary>
        public static bool HasRecognizedTier(DiaryHumorCueDef def)
        {
            return def != null
                && (string.Equals(def.tier, TierLight, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(def.tier, TierGallows, StringComparison.OrdinalIgnoreCase));
        }
    }
}
