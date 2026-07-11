// Enneagram reads — the "who is this pawn" half of the bridge. This is a stateless helper: it turns a
// pawn's live 1-2-3 Personalities Enneagram into the strings the three bridge tiers need, and never
// writes anything back into 1-2-3 Personalities. All the seeding decisions (which tier, change
// detection, applying the result) live in Personalities123GameComponent; this file only READS.
//
//   • RootDefNameFor    -> the canonical Enneagram root defName (drives every tier's change detection).
//   • ResolveOutlookRule -> the localized built-in outlook rule for a root (Tier 2, and the Tier 3
//     fallback). Localizes via RimWorld's Keyed system, English source as fallback.
//   • BuildTransformInputFor -> the compact personality-data block sent to the LLM (Tier 3).
//
// SP_Module1-type isolation: every method that names SPM1 types is [NoInlining] and only reached after
// the mod's SimplePersonalitiesActive guard (the GameComponent checks it before the pass), so a mod list
// without 1-2-3 Personalities never JITs a method that would fail to resolve SP_Module1. The component's
// own body names no SPM1 types — it only calls the isolated readers here (mirrors PersonaSync's RimTalk
// isolation).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Runtime.CompilerServices;
using PawnDiaryPersonalities123.Pure;
using Verse;

namespace PawnDiaryPersonalities123
{
    /// <summary>
    /// Reads 1-2-3 Personalities Enneagrams into bridge-ready strings. Stateless and READ-ONLY toward
    /// 1-2-3 Personalities.
    /// </summary>
    internal static class EnneagramSync
    {
        /// <summary>
        /// The pawn's canonical Enneagram root defName (e.g. "SP_Root3"), or null when the pawn has no
        /// valid personality. Main thread. Names SPM1 types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string RootDefNameFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            SPM1.Enneagram enneagram = SafeGetEnneagram(pawn);
            return enneagram != null && enneagram.IsValid && enneagram.Root != null
                ? enneagram.Root.defName
                : null;
        }

        /// <summary>
        /// Resolves the localized outlook rule for a root: the natively-authored translation when the
        /// active language has one (see Languages/*/Keyed), otherwise the English source rule from the
        /// pure mapper. Returns null for an unmapped root. Main thread — reads the active language DB.
        /// The outlook rule reaches the model's prompt, so it must localize like every other prompt line.
        /// </summary>
        public static string ResolveOutlookRule(string rootDefName)
        {
            string english = EnneagramLensMapping.RuleForRoot(rootDefName);
            if (string.IsNullOrWhiteSpace(english))
            {
                return null;
            }

            string key = EnneagramLensMapping.KeyForRoot(rootDefName);
            return !string.IsNullOrEmpty(key) && key.CanTranslate() ? key.Translate().Resolve() : english;
        }

        /// <summary>
        /// Builds the compact personality-data block the LLM transform (Tier 3) rewrites, from the pawn's
        /// live variant/trait labels, root, and raw serialization. Returns null when there is nothing to
        /// send. Main thread. Names SPM1 types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string BuildTransformInputFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            SPM1.Enneagram enneagram = SafeGetEnneagram(pawn);
            if (enneagram == null || !enneagram.IsValid)
            {
                return null;
            }

            string variant = enneagram.Variant != null ? enneagram.Variant.label : null;
            string mainTrait = enneagram.MainTrait != null ? enneagram.MainTrait.label : null;
            string root = enneagram.Root != null ? enneagram.Root.defName : null;
            string serialization = SafeExtractPersonality(pawn);
            return EnneagramLensMapping.BuildTransformInput(variant, mainTrait, root, serialization);
        }

        /// <summary>Reads the pawn's Enneagram defensively; comp reads can throw on odd/unbuilt pawns.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static SPM1.Enneagram SafeGetEnneagram(Pawn pawn)
        {
            try
            {
                return SPM1.Extensions.TryGetEnneagram(pawn);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Reads the personality serialization defensively for the transform input.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string SafeExtractPersonality(Pawn pawn)
        {
            try
            {
                return SPM1.Extensions.ExtractPersonality(pawn);
            }
            catch
            {
                return null;
            }
        }
    }
}
