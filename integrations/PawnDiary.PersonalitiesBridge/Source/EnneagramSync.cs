// Enneagram sync — the "who is this pawn" half of the bridge, cloned in shape from the RimTalk bridge's
// PersonaSync. Two tiers, both code-only, both READ-ONLY toward 1-2-3 Personalities (we never write a
// pawn's personality back):
//
//   • Tier A (always on): mutual awareness. Pawn Diary sees a compact "personality=<variant>, <trait>"
//     line via a registered context provider. Nothing is mutated.
//   • Tier B (toggle, default ON): personality-led diary OUTLOOK. The pawn's Enneagram ROOT is turned
//     into a Pawn Diary PSYCHOTYPE (outlook) OVERRIDE — 1-2-3 Personalities supplies WHO the pawn is;
//     Pawn Diary keeps HOW they write. Reapplied when the personality changes and cleared when the
//     toggle turns off or a new colony loads.
//
// SP_Module1-type isolation: every method that names SPM1 types is [NoInlining] and only reached after
// the mod's SimplePersonalitiesActive guard, so a mod list without 1-2-3 Personalities never JITs a
// method that would fail to resolve SP_Module1 (mirrors PersonaSync's RimTalk isolation).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using PawnDiaryPersonalities123.Pure;
using RimWorld;
using Verse;

namespace PawnDiaryPersonalities123
{
    /// <summary>
    /// Reads 1-2-3 Personalities Enneagrams and exposes them to Pawn Diary as context (Tier A) or as an
    /// optional outlook override (Tier B). Never writes back into 1-2-3 Personalities.
    /// </summary>
    internal static class EnneagramSync
    {
        // Tier B bookkeeping: pawnId -> hash of the personality serialization we last turned into an
        // override. Lets us skip pawns whose personality has not changed. In-memory only (not saved):
        // after a reload the first pass simply re-applies, which is harmless because SetPsychotypeOverride
        // is idempotent.
        private static readonly Dictionary<string, int> AppliedPersonalityHash = new Dictionary<string, int>();

        // Whether Tier B was active on the previous pass, so we can detect the moment it turns off and
        // clear every override we placed.
        private static bool tierBWasActive;

        /// <summary>
        /// Registers the Tier A "personality=" context provider. Process-global and idempotent; call once
        /// from the mod constructor, only when 1-2-3 Personalities is active.
        /// </summary>
        public static void RegisterContextProvider()
        {
            PawnDiaryApi.RegisterPawnContextProvider(BridgeIds.PersonalityProviderId, ProvidePersonalityLine);
        }

        /// <summary>
        /// Tier B pass. MAIN THREAD ONLY (calls PawnDiaryApi). Applies/updates the personality-led outlook
        /// override while the toggle is on, and clears all overrides the moment it turns off. No-op work
        /// when nothing changed.
        /// </summary>
        public static void RunTierBPass()
        {
            bool active = PawnDiaryPersonalities123Mod.Settings != null
                && PawnDiaryPersonalities123Mod.Settings.usePersonalityOutlook
                && PawnDiaryApi.IsExternalApiEnabled;

            if (!active)
            {
                // Turned off (or master switch dropped): remove every override we placed. Core refuses to
                // clear another source's override, so resetting untouched pawns is a safe no-op.
                if (tierBWasActive)
                {
                    ResetAllOverrides();
                }

                tierBWasActive = false;
                return;
            }

            tierBWasActive = true;

            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                ApplyTierBFor(pawn);
            }
        }

        /// <summary>
        /// Clears the bridge's outlook overrides from every pawn we may have touched, and forgets our
        /// bookkeeping. Called on toggle-off and on new-game reset. Walks a broad pawn set — not just
        /// spawned free colonists — so an override placed on a pawn that later went downed, joined a
        /// caravan, entered cryptosleep, or left the map is still cleared. Pawn Diary refuses to clear
        /// another source's override, so touching untouched pawns is a safe no-op.
        /// </summary>
        public static void ResetAllOverrides()
        {
            foreach (Pawn pawn in TouchedPawns())
            {
                PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            }

            AppliedPersonalityHash.Clear();
        }

        /// <summary>
        /// Clears overrides AND bookkeeping on load. Overrides placed in a previous colony are owned by
        /// Pawn Diary's per-pawn saved state; clearing them here (against the broad pawn set) prevents a
        /// stale bridge outlook from surviving a colony switch when Tier B was on at save time.
        /// </summary>
        public static void ResetForNewGame()
        {
            // Clear overrides BEFORE wiping the bookkeeping: TouchedPawns() does not read it, but keeping
            // the same order as PersonaSync avoids surprises if that ever changes.
            foreach (Pawn pawn in TouchedPawns())
            {
                PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            }

            AppliedPersonalityHash.Clear();
            tierBWasActive = false;
        }

        // Every pawn the bridge may have given a Tier B override: spawned free colonists, caravans and
        // travelling transport pods, plus world pawns (covers downed/caravan/cryptosleep/departed).
        // Deduplicated by load id so a pawn present in two sources is reset once.
        private static IEnumerable<Pawn> TouchedPawns()
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                {
                    yield return pawn;
                }
            }
            if (Find.WorldPawns != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                    {
                        yield return pawn;
                    }
                }
            }
        }

        /// <summary>Applies or refreshes one pawn's Tier B override. Main thread. Names SPM1 types.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyTierBFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string id = pawn.GetUniqueLoadID();
            SPM1.Enneagram enneagram = SafeGetEnneagram(pawn);
            string rootDefName = enneagram != null && enneagram.IsValid && enneagram.Root != null
                ? enneagram.Root.defName
                : null;
            string rule = ResolveOutlookRule(rootDefName);

            if (string.IsNullOrWhiteSpace(rule))
            {
                // No usable personality (cleared, invalid, or an unmapped root): drop any override we had
                // placed for this pawn so a stale outlook does not linger.
                if (AppliedPersonalityHash.ContainsKey(id))
                {
                    PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
                    AppliedPersonalityHash.Remove(id);
                }

                return;
            }

            // Change detection off the full personality serialization: GetHashCode is stable within a
            // process, which is all we need for in-session detection (we never persist it). Re-applying
            // when only the variant/trait changed but the root did not is a harmless idempotent no-op.
            string serialized = SafeExtractPersonality(pawn);
            int hash = string.IsNullOrEmpty(serialized) ? rule.GetHashCode() : serialized.GetHashCode();
            int previous;
            if (AppliedPersonalityHash.TryGetValue(id, out previous) && previous == hash)
            {
                return;
            }

            if (PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, rule))
            {
                AppliedPersonalityHash[id] = hash;
            }
        }

        /// <summary>
        /// Resolves the localized outlook rule for a root: the natively-authored translation when the
        /// active language has one (see Languages/*/Keyed), otherwise the English source rule from the
        /// pure mapper. Returns null for an unmapped root. Main thread — reads the active language DB.
        /// The outlook rule reaches the model's prompt, so it must localize like every other prompt line.
        /// </summary>
        private static string ResolveOutlookRule(string rootDefName)
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
        /// Tier A provider. Called by Pawn Diary on the main thread while building a pawn summary.
        /// Returns "personality=&lt;variant&gt;, &lt;trait&gt;", or null when there is nothing to add.
        /// Names SPM1 types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ProvidePersonalityLine(Pawn pawn)
        {
            if (pawn == null
                || !PawnDiaryPersonalities123Mod.SimplePersonalitiesActive
                || PawnDiaryPersonalities123Mod.Settings == null
                || !PawnDiaryPersonalities123Mod.Settings.provideContextLine)
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
            return EnneagramLensMapping.ContextLine(variant, mainTrait);
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

        /// <summary>Reads the personality serialization defensively for change detection.</summary>
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
